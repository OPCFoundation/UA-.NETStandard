/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A node manager for a global discovery server
    /// </summary>
    public class ApplicationsNodeManager : CustomNodeManager2, ICallAsyncNodeManager
    {
        private readonly NodeId m_defaultApplicationGroupId;
        private readonly NodeId m_defaultHttpsGroupId;
        private readonly NodeId m_defaultUserTokenGroupId;

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ApplicationsNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IApplicationsDatabase database,
            ICertificateRequest request,
            ICertificateGroup certificateGroup,
            bool autoApprove = false)
            : base(server, configuration)
        {
            m_logger = server.Telemetry.CreateLogger<ApplicationsNodeManager>();
            NamespaceUris = ["http://opcfoundation.org/UA/GDS/applications/", Namespaces.OpcUaGds];

            SystemContext.NodeIdFactory = this;

            m_configuration = configuration;
            // get the configuration for the node manager.
            m_globalDiscoveryServerConfiguration =
                configuration.ParseExtension<GlobalDiscoveryServerConfiguration>()
                ?? new GlobalDiscoveryServerConfiguration();

            // use suitable defaults if no configuration exists.

            if (!string.IsNullOrEmpty(
                    m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext) &&
                m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext[0] != ',')
            {
                m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext =
                    "," + m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext;
            }

            m_defaultApplicationGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                Server.NamespaceUris);
            m_defaultHttpsGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup,
                Server.NamespaceUris);
            m_defaultUserTokenGroupId = ExpandedNodeId.ToNodeId(
                ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup,
                Server.NamespaceUris);

            m_autoApprove = autoApprove;
            m_database = database;
            m_request = request;
            m_certificateGroupFactory = certificateGroup;
            m_certificateGroups = [];

            try
            {
                ServerOnNetwork[] results = m_database.QueryServers(
                    0,
                    5,
                    null,
                    null,
                    null,
                    null,
                    out DateTime lastResetTime);
                m_logger.LogInformation("QueryServers Returned: {0} records", results.Length);

                foreach (ServerOnNetwork result in results)
                {
                    m_logger.LogInformation("Server Found at {0}", result.DiscoveryUrl);
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not connect to the Database!");

                Exception ie = e.InnerException;

                while (ie != null)
                {
                    m_logger.LogInformation(ie, string.Empty);
                    ie = ie.InnerException;
                }

                m_logger.LogInformation("Initialize Database tables!");
                m_database.Initialize();

                m_logger.LogInformation("Database Initialized!");
            }

            Server.MessageContext.Factory
                .AddEncodeableTypes(typeof(ObjectIds).GetTypeInfo().Assembly);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD
            }
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            // generate a numeric node id if the node has a parent and no node id assigned.
            if (node is BaseInstanceState instance && instance.Parent != null)
            {
                return GenerateNodeId();
            }

            return node.NodeId;
        }

        private NodeId GetTrustListId(NodeId certificateGroupId)
        {
            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup certificateGroup))
            {
                return certificateGroup.DefaultTrustList?.NodeId;
            }

            return null;
        }

        private bool? GetCertificateStatus(NodeId certificateGroupId, NodeId certificateTypeId)
        {
            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup certificateGroup))
            {
                if (!NodeId.IsNull(certificateTypeId) &&
                    !certificateGroup.CertificateTypes.Contains(certificateTypeId))
                {
                    return null;
                }
                return certificateGroup.UpdateRequired;
            }

            return null;
        }

        private ICertificateGroup GetGroupForCertificate(byte[] certificate)
        {
            if (certificate != null && certificate.Length > 0)
            {
                using X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(certificate);
                NodeId certificateType = CertificateIdentifier.GetCertificateType(x509);
                foreach (ICertificateGroup certificateGroup in m_certificateGroups.Values)
                {
                    KeyValuePair<NodeId, X509Certificate2>? matchingCert = certificateGroup
                        .Certificates
                        .FirstOrDefault(
                            kvp =>
                                X509Utils.CompareDistinguishedName(
                                    kvp.Value.Subject,
                                    x509.Issuer) &&
                                kvp.Key == certificateType);

                    if (matchingCert != null)
                    {
                        return certificateGroup;
                    }
                }
            }

            return null;
        }

        private async Task<bool> RevokeCertificateAsync(byte[] certificate)
        {
            bool revoked = false;
            if (certificate != null && certificate.Length > 0)
            {
                ICertificateGroup certificateGroup = GetGroupForCertificate(certificate);

                if (certificateGroup != null)
                {
                    using X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(
                        certificate);
                    try
                    {
                        Security.Certificates.X509CRL crl = await certificateGroup
                            .RevokeCertificateAsync(x509)
                            .ConfigureAwait(false);
                        if (crl != null)
                        {
                            revoked = true;
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(
                            e,
                            "Unexpected error revoking certificate. {0} for Authority={1}",
                            x509.Subject,
                            certificateGroup.Id);
                    }
                }
            }
            return revoked;
        }

        protected async Task<ICertificateGroup> InitializeCertificateGroupAsync(
            CertificateGroupConfiguration certificateGroupConfiguration)
        {
            if (string.IsNullOrEmpty(certificateGroupConfiguration.SubjectName))
            {
                throw new ArgumentNullException(
                    nameof(certificateGroupConfiguration),
                    "SubjectName not specified");
            }

            if (string.IsNullOrEmpty(certificateGroupConfiguration.BaseStorePath))
            {
                throw new ArgumentNullException(
                    nameof(certificateGroupConfiguration),
                    "BaseStorePath not specified");
            }

            ICertificateGroup certificateGroup = m_certificateGroupFactory.Create(
                m_globalDiscoveryServerConfiguration.AuthoritiesStorePath,
                certificateGroupConfiguration,
                m_configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath);
            await certificateGroup.InitAsync().ConfigureAwait(false);

            SetCertificateGroupNodes(certificateGroup);

            return certificateGroup;
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                base.CreateAddressSpace(externalReferences);

                m_certTypeMap = new Dictionary<NodeId, string>
                {
                    // list of supported cert type mappings (V1.04)
                    {
                        Ua.ObjectTypeIds.HttpsCertificateType,
                        nameof(Ua.ObjectTypeIds.HttpsCertificateType) },
                    {
                        Ua.ObjectTypeIds.UserCredentialCertificateType,
                        nameof(Ua.ObjectTypeIds.UserCredentialCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.ApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.RsaMinApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.RsaMinApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)
                    // ECC / V1.05
#if ECC_SUPPORT
                    },
                    {
                        Ua.ObjectTypeIds.EccApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
#if CURVE25519
                    },
                    {
                        Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType)
                    },
                    {
                        Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType,
                        nameof(Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType)
#endif
#endif
                    }
                };

                m_database.NamespaceIndex = NamespaceIndexes[0];
                m_request.NamespaceIndex = NamespaceIndexes[0];

                foreach (
                    CertificateGroupConfiguration certificateGroupConfiguration in m_globalDiscoveryServerConfiguration
                        .CertificateGroups)
                {
                    try
                    {
                        ICertificateGroup certificateGroup = InitializeCertificateGroupAsync(
                                certificateGroupConfiguration)
                            .GetAwaiter()
                            .GetResult();
                        m_certificateGroups[certificateGroup.Id] = certificateGroup;
                    }
                    catch (Exception e)
                    {
                        var message = new StringBuilder();
                        message.AppendLine("Unexpected error initializing certificateGroup: {0}")
                            .AppendLine("{1}");
                        m_logger.LogError(
                            e,
                            message.ToString(),
                            certificateGroupConfiguration.Id,
                            ServiceResult.BuildExceptionTrace(e));
                        // make sure gds server doesn't start without cert groups!
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            var predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(
                context,
                "Opc.Ua.Gds.Server.Model.Opc.Ua.Gds.PredefinedNodes.uanodes",
                typeof(ApplicationsNodeManager).GetTypeInfo().Assembly,
                true);
            return predefinedNodes;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(
            ISystemContext context,
            NodeState predefinedNode)
        {
            if (predefinedNode is not BaseObjectState passiveNode)
            {
                return predefinedNode;
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || typeId.IdType != IdType.Numeric)
            {
                return predefinedNode;
            }

            switch ((uint)typeId.Identifier)
            {
                case ObjectTypes.CertificateDirectoryType:
                    if (passiveNode is CertificateDirectoryState)
                    {
                        break;
                    }

                    var activeNode = new CertificateDirectoryState(passiveNode.Parent)
                    {
                        RevokeCertificate = new RevokeCertificateMethodState(passiveNode),
                        CheckRevocationStatus = new CheckRevocationStatusMethodState(passiveNode),
                        GetCertificates = new GetCertificatesMethodState(passiveNode)
                    };

                    activeNode.Create(context, passiveNode);
                    activeNode.QueryServers.OnCall
                        = new QueryServersMethodStateMethodCallHandler(OnQueryServers);
                    activeNode.QueryApplications.OnCall
                        = new QueryApplicationsMethodStateMethodCallHandler(
                        OnQueryApplications);
                    activeNode.RegisterApplication.OnCall
                        = new RegisterApplicationMethodStateMethodCallHandler(
                        OnRegisterApplication);
                    activeNode.UpdateApplication.OnCall
                        = new UpdateApplicationMethodStateMethodCallHandler(
                        OnUpdateApplication);
                    activeNode.UnregisterApplication.OnCallAsync
                        = new UnregisterApplicationMethodStateMethodAsyncCallHandler(
                        OnUnregisterApplicationAsync);
                    activeNode.FindApplications.OnCall
                        = new FindApplicationsMethodStateMethodCallHandler(
                        OnFindApplications);
                    activeNode.GetApplication.OnCall
                        = new GetApplicationMethodStateMethodCallHandler(OnGetApplication);
                    activeNode.StartNewKeyPairRequest.OnCall
                        = new StartNewKeyPairRequestMethodStateMethodCallHandler(
                        OnStartNewKeyPairRequest);
                    activeNode.FinishRequest.OnCallAsync
                        = new FinishRequestMethodStateMethodAsyncCallHandler(OnFinishRequestAsync);
                    activeNode.GetCertificateGroups.OnCall
                        = new GetCertificateGroupsMethodStateMethodCallHandler(
                        OnGetCertificateGroups);
                    activeNode.GetTrustList.OnCall
                        = new GetTrustListMethodStateMethodCallHandler(OnGetTrustList);
                    activeNode.GetCertificateStatus.OnCall
                        = new GetCertificateStatusMethodStateMethodCallHandler(
                        OnGetCertificateStatus);
                    activeNode.StartSigningRequest.OnCallAsync
                        = new StartSigningRequestMethodStateMethodAsyncCallHandler(
                        OnStartSigningRequestAsync);
                    activeNode.RevokeCertificate.OnCall
                        = new RevokeCertificateMethodStateMethodCallHandler(
                        OnRevokeCertificate);
                    activeNode.CheckRevocationStatus.OnCallAsync
                        = new CheckRevocationStatusMethodStateMethodAsyncCallHandler(
                        OnCheckRevocationStatusAsync);
                    activeNode.GetCertificates.OnCall
                        = new GetCertificatesMethodStateMethodCallHandler(
                        OnGetCertificates);
                    if (m_certificateGroups.TryGetValue(
                            m_defaultApplicationGroupId,
                            out ICertificateGroup applicationCertificateGroup))
                    {
                        activeNode.CertificateGroups.DefaultApplicationGroup.CertificateTypes.Value
                            =
                        [
                            .. applicationCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        activeNode.CertificateGroups.DefaultApplicationGroup.CertificateTypes.Value
                            =
                        [
                            Ua.ObjectTypeIds.ApplicationCertificateType
                        ];
                    }
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.LastUpdateTime
                        .Value =
                        DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.Writable.Value
                        = false;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.UserWritable
                        .Value = false;

                    if (m_certificateGroups.TryGetValue(
                            m_defaultHttpsGroupId,
                            out ICertificateGroup httpsCertificateGroup))
                    {
                        activeNode.CertificateGroups.DefaultHttpsGroup.CertificateTypes.Value =
                        [
                            .. httpsCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        activeNode.CertificateGroups.DefaultHttpsGroup.CertificateTypes.Value =
                        [
                            Ua.ObjectTypeIds.HttpsCertificateType
                        ];
                    }
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.LastUpdateTime.Value
                        = DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.Writable.Value = false;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.UserWritable.Value
                        = false;
                    if (m_certificateGroups.TryGetValue(
                            m_defaultUserTokenGroupId,
                            out ICertificateGroup userTokenCertificateGroup))
                    {
                        activeNode.CertificateGroups.DefaultUserTokenGroup.CertificateTypes.Value =
                        [
                            .. userTokenCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        activeNode.CertificateGroups.DefaultUserTokenGroup.CertificateTypes.Value =
                        [
                            Ua.ObjectTypeIds.UserCredentialCertificateType
                        ];
                    }
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.LastUpdateTime
                        .Value = DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.Writable.Value
                        = false;
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.UserWritable.Value
                        = false;

                    // replace the node in the parent.
                    passiveNode.Parent?.ReplaceChild(context, activeNode);

                    return activeNode;
            }

            return predefinedNode;
        }

        private ServiceResult OnQueryServers(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            string[] serverCapabilities,
            ref DateTime lastCounterResetTime,
            ref ServerOnNetwork[] servers)
        {
            m_logger.LogInformation("QueryServers: {0} {1}", applicationUri, applicationName);

            servers = m_database.QueryServers(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities,
                out lastCounterResetTime);

            return ServiceResult.Good;
        }

        private ServiceResult OnQueryApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            string[] serverCapabilities,
            ref DateTime lastCounterResetTime,
            ref uint nextRecordId,
            ref ApplicationDescription[] applications)
        {
            m_logger.LogInformation("QueryApplications: {0} {1}", applicationUri, applicationName);

            applications = m_database.QueryApplications(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities,
                out lastCounterResetTime,
                out nextRecordId);
            return ServiceResult.Good;
        }

        private ServiceResult OnRegisterApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application,
            ref NodeId applicationId)
        {
            AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.DiscoveryAdmin);

            m_logger.LogInformation("OnRegisterApplication: {0}", application.ApplicationUri);

            applicationId = m_database.RegisterApplication(application);

            if (applicationId != null)
            {
                object[] inputArguments = [application, applicationId];
                Server.ReportApplicationRegistrationChangedAuditEvent(
                    context,
                    objectId,
                    method,
                    inputArguments,
                    m_logger);
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnUpdateApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application)
        {
            AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.DiscoveryAdmin);

            m_logger.LogInformation("OnUpdateApplication: {0}", application.ApplicationUri);

            ApplicationRecordDataType record = m_database.GetApplication(application.ApplicationId);

            if (record == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The application id does not exist.");
            }

            m_database.RegisterApplication(application);

            object[] inputArguments = [application];
            Server.ReportApplicationRegistrationChangedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);

            return ServiceResult.Good;
        }

        private async ValueTask<UnregisterApplicationMethodStateResult>
            OnUnregisterApplicationAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrSelfAdmin);

            m_logger.LogInformation("OnUnregisterApplication: {0}", applicationId.ToString());

            foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
            {
                try
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out byte[] certificate) &&
                        certificate != null)
                    {
                        await RevokeCertificateAsync(certificate).ConfigureAwait(false);
                    }
                }
                catch
                {
                    m_logger.LogError("Failed to revoke: {0}", certType.Value);
                }
            }

            m_database.UnregisterApplication(applicationId);

            object[] inputArguments = [applicationId];
            Server.ReportApplicationRegistrationChangedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);

            return new UnregisterApplicationMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
        }

        private ServiceResult OnRevokeCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            byte[] certificate)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdmin);

            if (m_database.GetApplication(applicationId) == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a registered application.");
            }
            if (certificate == null || certificate.Length == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The certificate is not a Certificate for the specified Application that was issued by the CertificateManager.");
            }

            bool revoked = false;
            foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
            {
                if (!m_database.GetApplicationCertificate(
                        applicationId,
                        certType.Value,
                        out byte[] applicationCertificate) ||
                    applicationCertificate == null ||
                    !Utils.IsEqual(applicationCertificate, certificate))
                {
                    continue;
                }

                revoked = RevokeCertificateAsync(certificate).Result;
                if (revoked)
                {
                    break;
                }
            }
            if (!revoked)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The certificate is not a Certificate for the specified Application that was issued by the CertificateManager.");
            }
            return ServiceResult.Good;
        }

        private ServiceResult OnFindApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ref ApplicationRecordDataType[] applications)
        {
            AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.AuthenticatedUser);
            m_logger.LogInformation("OnFindApplications: {0}", applicationUri);
            applications = m_database.FindApplications(applicationUri);
            return ServiceResult.Good;
        }

        private ServiceResult OnGetApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref ApplicationRecordDataType application)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.AuthenticatedUserOrSelfAdmin,
                applicationId);
            m_logger.LogInformation("OnGetApplication: {0}", applicationId);
            application = m_database.GetApplication(applicationId);
            return ServiceResult.Good;
        }

        private async ValueTask<CheckRevocationStatusMethodStateResult> OnCheckRevocationStatusAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte[] certificate,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context);

            var result = new CheckRevocationStatusMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                //TODO return When the result expires and should be rechecked.
                ValidityTime = DateTime.MinValue
            };

            try
            {
                //create chain to validate Certificate against it
                var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;

                //add GDS Issuer Cert Store Certificates to the Chain validation for consistent behaviour on all Platforms
                ICertificateStore store = m_configuration.SecurityConfiguration
                    .TrustedIssuerCertificates
                    .OpenStore(Server.Telemetry);
                if (store != null)
                {
                    try
                    {
                        chain.ChainPolicy.ExtraStore
                            .AddRange(await store.EnumerateAsync(cancellationToken)
                                .ConfigureAwait(false));
                    }
                    finally
                    {
                        store.Close();
                    }
                }

                using X509Certificate2 x509 = X509CertificateLoader.LoadCertificate(certificate);
                if (chain.Build(x509))
                {
                    result.CertificateStatus = StatusCodes.Good;
                    return result;
                }

                //Assing certificateStatus for invalid chain if no matching found use StatusCodes.BadCertificateRevoked
                result.CertificateStatus = chain.ChainStatus.FirstOrDefault().Status switch
                {
                    X509ChainStatusFlags.NotTimeValid => StatusCodes.BadCertificateTimeInvalid,
                    X509ChainStatusFlags.Revoked => StatusCodes.BadCertificateRevoked,
                    X509ChainStatusFlags.NotSignatureValid => StatusCodes.BadCertificateInvalid,
                    X509ChainStatusFlags.NotValidForUsage => StatusCodes.BadCertificateUseNotAllowed,
                    X509ChainStatusFlags.RevocationStatusUnknown => StatusCodes.BadCertificateRevocationUnknown,
                    X509ChainStatusFlags.PartialChain => StatusCodes.BadCertificateChainIncomplete,
                    X509ChainStatusFlags.ExplicitDistrust => StatusCodes.BadCertificateUntrusted,
                    _ => StatusCodes.BadCertificateRevoked
                };
            }
            catch (CryptographicException)
            {
                result.CertificateStatus = StatusCodes.BadCertificateRevoked;
            }

            return result;
        }

        private ServiceResult OnGetCertificates(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            ref NodeId[] certificateTypeIds,
            ref byte[][] certificates)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin);

            var certificateTypeIdsList = new List<NodeId>();
            var certificatesList = new List<byte[]>();

            if (m_database.GetApplication(applicationId) == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a registered application.");
            }

            //If CertificateGroupId is null, the CertificateManager shall return the Certificates for all CertificateGroups assigned to the Application.
            if (certificateGroupId == null)
            {
                foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out byte[] certificate) &&
                        certificate != null)
                    {
                        certificateTypeIdsList.Add(certType.Key);
                        certificatesList.Add(certificate);
                    }
                }
            }
            //get only Certificate of the provided CertificateGroup
            else
            {
                if (!m_certificateGroups.TryGetValue(
                    certificateGroupId,
                    out ICertificateGroup certificateGroup))
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        "The CertificateGroupId is not recognized or not valid for the Application.");
                }
                foreach (NodeId certificateType in certificateGroup.CertificateTypes)
                {
                    if (m_certTypeMap.TryGetValue(certificateType, out string certificateTypeId) &&
                        m_database.GetApplicationCertificate(
                            applicationId,
                            certificateTypeId,
                            out byte[] certificate
                        ) &&
                        certificate != null)
                    {
                        certificateTypeIdsList.Add(certificateType);
                        certificatesList.Add(certificate);
                    }
                }
            }

            certificates = [.. certificatesList];
            certificateTypeIds = [.. certificateTypeIdsList];

            return ServiceResult.Good;
        }

        private static ServiceResult CheckHttpsDomain(
            ApplicationRecordDataType application,
            string commonName)
        {
            if (application.ApplicationType == ApplicationType.Client)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "Cannot issue HTTPS certificates to client applications.");
            }

            bool found = false;

            if (application.DiscoveryUrls != null)
            {
                foreach (string discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        var url = new Uri(discoveryUrl);

                        if (url.Scheme == Utils.UriSchemeHttps &&
                            Utils.AreDomainsEqual(commonName, url.DnsSafeHost))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }

            if (!found)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "Cannot issue HTTPS certificates to server applications without a matching HTTPS discovery URL.");
            }

            return ServiceResult.Good;
        }

        private static string GetDefaultHttpsDomain(ApplicationRecordDataType application)
        {
            if (application.DiscoveryUrls != null)
            {
                foreach (string discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        var url = new Uri(discoveryUrl);

                        if (url.Scheme == Utils.UriSchemeHttps)
                        {
                            return url.DnsSafeHost;
                        }
                    }
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                "Cannot issue HTTPS certificates to server applications without a HTTPS discovery URL.");
        }

        private static string GetDefaultUserToken()
        {
            return "USER";
        }

        private string GetSubjectName(
            ApplicationRecordDataType application,
            ICertificateGroup certificateGroup,
            string subjectName)
        {
            bool contextFound = false;

            List<string> fields = X509Utils.ParseDistinguishedName(subjectName);

            var builder = new StringBuilder();

            foreach (string field in fields)
            {
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }

                if (field.StartsWith("CN=", StringComparison.Ordinal) &&
                    certificateGroup.Id == m_defaultHttpsGroupId)
                {
                    ServiceResult error = CheckHttpsDomain(application, field[3..]);

                    if (StatusCode.IsBad(error.StatusCode))
                    {
                        builder.Append("CN=")
                            .Append(GetDefaultHttpsDomain(application));
                        continue;
                    }
                }

                contextFound |=
                    field.StartsWith("DC=", StringComparison.Ordinal) ||
                    field.StartsWith("O=", StringComparison.Ordinal);

                builder.Append(field);
            }

            if (!contextFound &&
                !string.IsNullOrEmpty(
                    m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext))
            {
                builder.Append(m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext);
            }

            return builder.ToString();
        }

        private static string[] GetDefaultDomainNames(ApplicationRecordDataType application)
        {
            var names = new List<string>();

            if (application.DiscoveryUrls != null && application.DiscoveryUrls.Count > 0)
            {
                foreach (string discoveryUrl in application.DiscoveryUrls)
                {
                    if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                    {
                        var url = new Uri(discoveryUrl);

                        foreach (string name in names)
                        {
                            if (Utils.AreDomainsEqual(name, url.DnsSafeHost))
                            {
                                url = null;
                                break;
                            }
                        }

                        if (url != null)
                        {
                            names.Add(url.DnsSafeHost);
                        }
                    }
                }
            }

            return [.. names];
        }

        private ServiceResult OnStartNewKeyPairRequest(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            ref NodeId requestId)
        {
            object[] inputArguments =
            [
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                privateKeyPassword
            ];
            Server.ReportCertificateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                certificateGroupId,
                certificateTypeId,
                m_logger);

            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                applicationId);

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Server.NamespaceUris);
            }

            if (!m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup certificateGroup))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The certificateGroup is not supported.");
            }

            if (!NodeId.IsNull(certificateTypeId))
            {
                if (!certificateGroup.CertificateTypes.Any(certificateType =>
                        Server.TypeTree.IsTypeOf(certificateType, certificateTypeId)))
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        "The CertificateType is not supported by the certificateGroup.");
                }
            }
            else
            {
                certificateTypeId = certificateGroup.CertificateTypes[0];
            }

            if (!m_certTypeMap.TryGetValue(certificateTypeId, out string certificateTypeNameId))
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateType is invalid.");
            }

            if (!string.IsNullOrEmpty(subjectName))
            {
                subjectName = GetSubjectName(application, certificateGroup, subjectName);
            }
            else
            {
                var buffer = new StringBuilder();

                buffer.Append("CN=");

                if ((NodeId.IsNull(certificateGroup.Id) ||
                    (certificateGroup.Id == m_defaultApplicationGroupId)) &&
                    (application.ApplicationNames.Count > 0))
                {
                    buffer.Append(application.ApplicationNames[0]);
                }
                else if (certificateGroup.Id == m_defaultHttpsGroupId)
                {
                    buffer.Append(GetDefaultHttpsDomain(application));
                }
                else if (certificateGroup.Id == m_defaultUserTokenGroupId)
                {
                    buffer.Append(GetDefaultUserToken());
                }

                if (!string.IsNullOrEmpty(
                    m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext))
                {
                    buffer.Append(m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext);
                }

                subjectName = buffer.ToString();
            }

            if (domainNames != null && domainNames.Length > 0)
            {
                foreach (string domainName in domainNames)
                {
                    if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown)
                    {
                        return new ServiceResult(
                            StatusCodes.BadInvalidArgument,
                            "The domainName ({0}) is not a valid DNS Name or IPAddress.",
                            domainName);
                    }
                }
            }
            else
            {
                domainNames = GetDefaultDomainNames(application);
            }

            requestId = m_request.StartNewKeyPairRequest(
                applicationId,
                certificateGroup.Configuration.Id,
                certificateTypeNameId,
                subjectName,
                domainNames,
                privateKeyFormat,
                privateKeyPassword,
                context.UserIdentity?.DisplayName);

            if (m_autoApprove)
            {
                try
                {
                    m_request.ApproveRequest(requestId, false);
                }
                catch
                {
                    // ignore error as user may not have authorization to approve requests
                }
            }

            return ServiceResult.Good;
        }

        private async ValueTask<StartSigningRequestMethodStateResult> OnStartSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                applicationId);

            var result = new StartSigningRequestMethodStateResult();

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
                return result;
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ExpandedNodeId.ToNodeId(
                    ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                    Server.NamespaceUris);
            }

            if (!m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup certificateGroup))
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateGroupId does not refer to a supported certificateGroup.");
                return result;
            }

            if (!NodeId.IsNull(certificateTypeId))
            {
                if (!certificateGroup.CertificateTypes.Any(certificateType =>
                        Server.TypeTree.IsTypeOf(certificateType, certificateTypeId)))
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        "The CertificateTypeId is not supported by the certificateGroup.");
                    return result;
                }
            }
            else
            {
                certificateTypeId = certificateGroup.CertificateTypes[0];
            }

            if (!m_certTypeMap.TryGetValue(certificateTypeId, out string certificateTypeNameId))
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateType is invalid.");
                return result;
            }

            // verify the CSR integrity for the application
            await certificateGroup.VerifySigningRequestAsync(application, certificateRequest).ConfigureAwait(false);

            // store request in the queue for approval
            result.RequestId = m_request.StartSigningRequest(
                applicationId,
                certificateGroup.Configuration.Id,
                certificateTypeNameId,
                certificateRequest,
                context.UserIdentity?.DisplayName);

            if (m_autoApprove)
            {
                try
                {
                    m_request.ApproveRequest(result.RequestId, false);
                }
                catch
                {
                    // ignore error as user may not have authorization to approve requests
                }
            }

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<FinishRequestMethodStateResult> OnFinishRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId requestId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                applicationId);

            var result = new FinishRequestMethodStateResult();

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);
            if (application == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
                return result;
            }

            CertificateRequestState state = m_request.FinishRequest(
                applicationId,
                requestId,
                out string certificateGroupId,
                out string certificateTypeId,
                out byte[] generatedCertificate,
                out byte[] privateKey);

            result.Certificate = generatedCertificate;
            result.PrivateKey = privateKey;

            result.ServiceResult = VerifyApprovedState(state);
            if (result.ServiceResult != null)
            {
                return result;
            }

            ICertificateGroup certificateGroup = null;
            if (!string.IsNullOrWhiteSpace(certificateGroupId))
            {
                foreach (KeyValuePair<NodeId, ICertificateGroup> group in m_certificateGroups)
                {
                    if (string.Equals(
                            group.Value.Configuration.Id,
                            certificateGroupId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        certificateGroup = group.Value;
                        break;
                    }
                }
            }

            if (certificateGroup == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateGroupId does not refer to a supported certificate group.");
                return result;
            }

            NodeId certificateTypeNodeId = m_certTypeMap
                .Where(
                    pair => pair.Value
                        .Equals(certificateTypeId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .SingleOrDefault();

            if (!NodeId.IsNull(certificateTypeNodeId) &&
                !certificateGroup.CertificateTypes.Any(certificateType =>
                    Server.TypeTree.IsTypeOf(certificateType, certificateTypeNodeId)))
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    "The CertificateTypeId is not supported by the certificateGroup.");
                return result;
            }

            // distinguish cert creation at approval/complete time
            X509Certificate2 certificate = null;
            if (result.Certificate == null)
            {
                state = m_request.ReadRequest(
                    applicationId,
                    requestId,
                    out certificateGroupId,
                    out certificateTypeId,
                    out byte[] certificateRequest,
                    out string subjectName,
                    out string[] domainNames,
                    out string privateKeyFormat,
                    out string privateKeyPassword);

                result.ServiceResult = VerifyApprovedState(state);
                if (result.ServiceResult != null)
                {
                    return result;
                }

                if (certificateRequest != null)
                {
                    try
                    {
                        string[] defaultDomainNames = GetDefaultDomainNames(application);
                        certificate = certificateGroup
                            .SigningRequestAsync(
                                application,
                                certificateTypeNodeId,
                                defaultDomainNames,
                                certificateRequest)
                            .Result;
                    }
                    catch (Exception e)
                    {
                        var error = new StringBuilder();
                        error.AppendLine("Error Generating Certificate={0}")
                            .AppendLine("ApplicationId={1}")
                            .AppendLine("ApplicationUri={2}")
                            .AppendLine("ApplicationName={3}");
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            error.ToString(),
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri,
                            application.ApplicationNames[0].Text);
                        return result;
                    }
                }
                else
                {
                    X509Certificate2KeyPair newKeyPair = null;
                    try
                    {
                        newKeyPair = certificateGroup
                            .NewKeyPairRequestAsync(
                                application,
                                certificateTypeNodeId,
                                subjectName,
                                domainNames,
                                privateKeyFormat,
                                privateKeyPassword)
                            .Result;
                    }
                    catch (Exception e)
                    {
                        var error = new StringBuilder();
                        error.AppendLine("Error Generating New Key Pair Certificate={0}")
                            .AppendLine("ApplicationId={1}")
                            .AppendLine("ApplicationUri={2}");
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            error.ToString(),
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri);
                        return result;
                    }

                    certificate = newKeyPair.Certificate;
                    result.PrivateKey = newKeyPair.PrivateKey;
                }

                result.Certificate = certificate.RawData;
            }
            else
            {
                certificate = X509CertificateLoader.LoadCertificate(result.Certificate);
            }

            // TODO: return chain, verify issuer chain cert is up to date, otherwise update local chain
            result.IssuerCertificates = new byte[1][];
            result.IssuerCertificates[0] = certificateGroup.Certificates[certificateTypeNodeId].RawData;

            // store new app certificate
            var certificateStoreIdentifier = new CertificateStoreIdentifier(
                m_globalDiscoveryServerConfiguration.ApplicationCertificatesStorePath);
            using (ICertificateStore store = certificateStoreIdentifier.OpenStore(Server.Telemetry))
            {
                if (store != null)
                {
                    await store.AddAsync(certificate, null, cancellationToken).ConfigureAwait(false);
                }
            }

            m_database.SetApplicationCertificate(
                applicationId,
                m_certTypeMap[certificateTypeNodeId],
                result.Certificate);

            m_database.SetApplicationTrustLists(
                applicationId,
                m_certTypeMap[certificateTypeNodeId],
                certificateGroup.Configuration.TrustedListPath);

            m_request.AcceptRequest(requestId, result.Certificate);

            object[] inputArguments
                = [applicationId, requestId, result.Certificate, result.PrivateKey, result.IssuerCertificates];
            Server.ReportCertificateDeliveredAuditEvent(context, objectId, method, inputArguments, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        public ServiceResult OnGetCertificateGroups(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref NodeId[] certificateGroupIds)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                applicationId);

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
            }

            var certificateGroupIdList = new List<NodeId>();
            foreach (KeyValuePair<NodeId, ICertificateGroup> certificateGroup in m_certificateGroups)
            {
                NodeId key = certificateGroup.Key;
                certificateGroupIdList.Add(key);
            }
            certificateGroupIds = [.. certificateGroupIdList];

            return ServiceResult.Good;
        }

        public ServiceResult OnGetTrustList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            ref NodeId trustListId)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdmin,
                applicationId);

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            trustListId = GetTrustListId(certificateGroupId);

            if (trustListId == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The CertificateGroupId does not refer to a group that is valid for the application.");
            }

            return ServiceResult.Good;
        }

        public ServiceResult OnGetCertificateStatus(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ref bool updateRequired)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.AuthenticatedUserOrSelfAdmin,
                applicationId);

            ApplicationRecordDataType application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The ApplicationId does not refer to a valid application.");
            }

            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            bool? updateRequiredResult = GetCertificateStatus(
                certificateGroupId,
                certificateTypeId);
            if (updateRequiredResult == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    "The CertificateGroupId and CertificateTypeId do not refer to a group and type that is valid for the application.");
            }

            updateRequired = (bool)updateRequiredResult;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                // check cache (the cache is used because the same node id can appear many times in a single request).
                if (cache != null && cache.TryGetValue(nodeId, out NodeState node))
                {
                    return new NodeHandle(nodeId, node);
                }

                // look up predefined node.
                if (PredefinedNodes.TryGetValue(nodeId, out node))
                {
                    var handle = new NodeHandle(nodeId, node);

                    cache?.Add(nodeId, node);

                    return handle;
                }

                // node not found.
                return null;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // lookup in operation cache.
            NodeState target = FindNodeInCache(context, handle, cache);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            // put root into operation cache.
            if (cache != null)
            {
                cache[handle.NodeId] = target;
            }

            handle.Node = target;
            handle.Validated = true;
            return handle.Node;
        }

        /// <summary>
        /// Generates a new node id.
        /// </summary>
        private NodeId GenerateNodeId()
        {
            return new NodeId(++m_nextNodeId, NamespaceIndex);
        }

        protected void SetCertificateGroupNodes(ICertificateGroup certificateGroup)
        {
            certificateGroup.DefaultTrustList = null;
            if (certificateGroup.CertificateTypes.Contains(Ua.ObjectTypeIds.HttpsCertificateType))
            {
                certificateGroup.Id = m_defaultHttpsGroupId;
                certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup_TrustList,
                        Server.NamespaceUris
                    ),
                    typeof(TrustListState));
            }
            else if (certificateGroup.CertificateTypes
                .Contains(Ua.ObjectTypeIds.UserCredentialCertificateType))
            {
                certificateGroup.Id = m_defaultUserTokenGroupId;
                certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList,
                        Server.NamespaceUris
                    ),
                    typeof(TrustListState));
            }
            else if (certificateGroup.CertificateTypes.Any(certificateType =>
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.ApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.RsaMinApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType) ||
#if ECC_SUPPORT
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccNistP384ApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType) ||
#if CURVE25519
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccCurve25519ApplicationCertificateType) ||
                Utils.IsEqual(
                    certificateType,
                    Ua.ObjectTypeIds.EccCurve448ApplicationCertificateType)
#endif
#endif
                    false))
            {
                certificateGroup.Id = m_defaultApplicationGroupId;
                certificateGroup.DefaultTrustList = (TrustListState)FindPredefinedNode(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Server.NamespaceUris
                    ),
                    typeof(TrustListState));
            }
            else
            {
                throw new NotImplementedException(
                    $"Unknown certificate type {certificateGroup.CertificateTypes}. " +
                    "Use ApplicationCertificateType, HttpsCertificateType or UserCredentialCertificateType");
            }

            if (certificateGroup.DefaultTrustList != null)
            {
                certificateGroup.DefaultTrustList.Handle = new TrustList(
                    certificateGroup.DefaultTrustList,
                    new CertificateStoreIdentifier(certificateGroup.Configuration.TrustedListPath),
                    new CertificateStoreIdentifier(certificateGroup.Configuration.IssuerListPath),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    Server.Telemetry);
            }
        }

        private void HasTrustListAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore)
        {
            AuthorizationHelper.HasTrustListAccess(
                context,
                trustedStore,
                m_certTypeMap,
                m_database);
        }

        private static ServiceResult VerifyApprovedState(CertificateRequestState state)
        {
            switch (state)
            {
                case CertificateRequestState.New:
                    return new ServiceResult(
                        StatusCodes.BadNothingToDo,
                        "The request has not been approved by the administrator.");
                case CertificateRequestState.Rejected:
                    return new ServiceResult(
                        StatusCodes.BadRequestNotAllowed,
                        "The request has been rejected by the administrator.");
                case CertificateRequestState.Accepted:
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        "The request has already been accepted by the application.");
                case CertificateRequestState.Approved:
                    break;
            }
            return null;
        }

        private readonly ILogger m_logger;
        private readonly bool m_autoApprove;
        private uint m_nextNodeId;
        private readonly ApplicationConfiguration m_configuration;
        private readonly GlobalDiscoveryServerConfiguration m_globalDiscoveryServerConfiguration;
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroupFactory;
        private readonly Dictionary<NodeId, ICertificateGroup> m_certificateGroups;
        private Dictionary<NodeId, string> m_certTypeMap;
    }
}
