/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
#if ECC_SUPPORT
using System.Security.Cryptography;
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// Privileged identity which can access the system configuration.
    /// </summary>
    public class SystemConfigurationIdentity : RoleBasedIdentity
    {
        /// <summary>
        /// Create a user identity with the privilege
        /// to modify the system configuration.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        public SystemConfigurationIdentity(IUserIdentity identity)
            : base(identity, [Role.SecurityAdmin, Role.ConfigureAdmin])
        {
        }
    }

    /// <summary>
    /// The Server Configuration Node Manager.
    /// </summary>
    public class ConfigurationNodeManager : DiagnosticsNodeManager, ICallAsyncNodeManager
    {
        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, server.Telemetry.CreateLogger<ConfigurationNodeManager>())
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : base(server, configuration, logger)
        {
            string rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore?
                .StorePath;
            if (!string.IsNullOrEmpty(rejectedStorePath))
            {
                m_rejectedStore = new CertificateStoreIdentifier(rejectedStorePath);
            }
            m_certificateGroups = [];
            m_configuration = configuration;
            // TODO: configure cert groups in configuration
            var defaultApplicationGroup = new ServerCertificateGroup
            {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                BrowseName = BrowseNames.DefaultApplicationGroup,
                CertificateTypes = [],
                ApplicationCertificates = [],
                IssuerStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath
                ),
                TrustedStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath)
            };
            m_certificateGroups.Add(defaultApplicationGroup);

            if (configuration.SecurityConfiguration.UserIssuerCertificates != null &&
                configuration.SecurityConfiguration.TrustedUserCertificates != null)
            {
                var defaultUserGroup = new ServerCertificateGroup
                {
                    NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultUserTokenGroup,
                    BrowseName = BrowseNames.DefaultUserTokenGroup,
                    CertificateTypes = [],
                    ApplicationCertificates = [],
                    IssuerStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.UserIssuerCertificates.StorePath
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedUserCertificates.StorePath)
                };

                m_certificateGroups.Add(defaultUserGroup);
            }
            ServerCertificateGroup defaultHttpsGroup = null;
            if (configuration.SecurityConfiguration.HttpsIssuerCertificates != null &&
                configuration.SecurityConfiguration.TrustedHttpsCertificates != null)
            {
                defaultHttpsGroup = new ServerCertificateGroup
                {
                    NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultHttpsGroup,
                    BrowseName = BrowseNames.DefaultHttpsGroup,
                    CertificateTypes = [],
                    ApplicationCertificates = [],
                    IssuerStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.HttpsIssuerCertificates.StorePath
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedHttpsCertificates.StorePath)
                };

                m_certificateGroups.Add(defaultHttpsGroup);
            }

            // For each certificate in ApplicationCertificates, add the certificate type to ServerConfiguration_CertificateGroups_DefaultApplicationGroup
            // under the CertificateTypes field.
            foreach (CertificateIdentifier cert in configuration.SecurityConfiguration
                .ApplicationCertificates)
            {
                defaultApplicationGroup.CertificateTypes =
                [
                    .. defaultApplicationGroup.CertificateTypes,
                    .. new NodeId[] { cert.CertificateType }
                ];
                defaultApplicationGroup.ApplicationCertificates.Add(cert);

                if (cert.CertificateType == ObjectTypeIds.HttpsCertificateType &&
                    defaultHttpsGroup != null)
                {
                    defaultHttpsGroup.CertificateTypes =
                    [
                        .. defaultHttpsGroup.CertificateTypes,
                        .. new NodeId[] { cert.CertificateType }
                    ];
                    defaultHttpsGroup.ApplicationCertificates.Add(cert);
                }
            }
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override NodeState AddBehaviourToPredefinedNode(
            ISystemContext context,
            NodeState predefinedNode)
        {
            if (predefinedNode is BaseObjectState passiveNode)
            {
                NodeId typeId = passiveNode.TypeDefinitionId;
                if (IsNodeIdInNamespace(typeId) && typeId.IdType == IdType.Numeric)
                {
                    switch ((uint)typeId.Identifier)
                    {
                        case ObjectTypes.ServerConfigurationType:
                        {
                            var activeNode = new ServerConfigurationState(passiveNode.Parent);

                            activeNode.GetCertificates = new GetCertificatesMethodState(activeNode);

                            activeNode.Create(context, passiveNode);

                            m_serverConfigurationNode = activeNode;

                            // replace the node in the parent.
                            if (passiveNode.Parent != null)
                            {
                                passiveNode.Parent.ReplaceChild(context, activeNode);
                            }
                            else
                            {
                                NodeState serverNode = FindNodeInAddressSpace(ObjectIds.Server);
                                serverNode?.ReplaceChild(context, activeNode);
                            }
                            // remove the reference to server node because it is set as parent
                            activeNode.RemoveReference(
                                ReferenceTypeIds.HasComponent,
                                true,
                                ObjectIds.Server);

                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupFolderType:
                        {
                            var activeNode = new CertificateGroupFolderState(passiveNode.Parent);
                            activeNode.Create(context, passiveNode);

                            // delete unsupported groups
                            if (m_certificateGroups.All(group =>
                                    group.BrowseName != activeNode.DefaultHttpsGroup?.BrowseName))
                            {
                                activeNode.DefaultHttpsGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    group.BrowseName != activeNode.DefaultUserTokenGroup?
                                        .BrowseName))
                            {
                                activeNode.DefaultUserTokenGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    group.BrowseName != activeNode.DefaultApplicationGroup?
                                        .BrowseName))
                            {
                                activeNode.DefaultApplicationGroup = null;
                            }

                            // replace the node in the parent.
                            passiveNode.Parent?.ReplaceChild(context, activeNode);
                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupType:
                        {
                            ServerCertificateGroup result = m_certificateGroups
                                .FirstOrDefault(group =>
                                    group.NodeId == passiveNode.NodeId);

                            if (result != null)
                            {
                                var activeNode = new CertificateGroupState(passiveNode.Parent);
                                activeNode.Create(context, passiveNode);

                                result.NodeId = activeNode.NodeId;
                                result.Node = activeNode;

                                // replace the node in the parent.
                                passiveNode.Parent?.ReplaceChild(context, activeNode);
                                return activeNode;
                            }
                        }
                        break;
                    }
                }
            }
            return base.AddBehaviourToPredefinedNode(context, predefinedNode);
        }

        /// <summary>
        /// Creates the configuration node for the server.
        /// </summary>
        public void CreateServerConfiguration(
            ServerSystemContext systemContext,
            ApplicationConfiguration configuration)
        {
            // setup server configuration node
            m_serverConfigurationNode.ServerCapabilities.Value =
            [
                .. configuration.ServerConfiguration.ServerCapabilities
            ];
            m_serverConfigurationNode.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            m_serverConfigurationNode.ServerCapabilities.ArrayDimensions
                = new ReadOnlyList<uint>([0]);
            m_serverConfigurationNode.SupportedPrivateKeyFormats.Value =
            [
                .. configuration.ServerConfiguration.SupportedPrivateKeyFormats
            ];
            m_serverConfigurationNode.SupportedPrivateKeyFormats.ValueRank = ValueRanks
                .OneDimension;
            m_serverConfigurationNode.SupportedPrivateKeyFormats.ArrayDimensions
                = new ReadOnlyList<uint>([0]);
            m_serverConfigurationNode.MaxTrustListSize.Value = (uint)configuration
                .ServerConfiguration
                .MaxTrustListSize;
            m_serverConfigurationNode.MulticastDnsEnabled.Value = configuration.ServerConfiguration
                .MultiCastDnsEnabled;

            m_serverConfigurationNode.UpdateCertificate.OnCallAsync
                = new UpdateCertificateMethodStateMethodAsyncCallHandler(
                UpdateCertificateAsync);
            m_serverConfigurationNode.CreateSigningRequest.OnCallAsync =
                new CreateSigningRequestMethodStateMethodAsyncCallHandler(CreateSigningRequestAsync);
            m_serverConfigurationNode.ApplyChanges.OnCallMethod2
                = new GenericMethodCalledEventHandler2(ApplyChanges);
            m_serverConfigurationNode.GetRejectedList.OnCall
                = new GetRejectedListMethodStateMethodCallHandler(
                GetRejectedList);
            m_serverConfigurationNode.GetCertificates.OnCall
                = new GetCertificatesMethodStateMethodCallHandler(
                GetCertificates);
            m_serverConfigurationNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                certGroup.Node.CertificateTypes.Value = certGroup.CertificateTypes;
                certGroup.Node.TrustList.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStore,
                    certGroup.IssuerStore,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    Server.Telemetry);
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // find ServerNamespaces node and subscribe to StateChanged

            if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState))
                is NamespacesState serverNamespacesNode)
            {
                serverNamespacesNode.StateChanged += ServerNamespacesChanged;
            }
        }

        /// <summary>
        /// Gets and returns the <see cref="NamespaceMetadataState"/> node associated with the specified NamespaceUri
        /// </summary>
        public NamespaceMetadataState GetNamespaceMetadataState(string namespaceUri)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            if (m_namespaceMetadataStates.TryGetValue(
                namespaceUri,
                out NamespaceMetadataState value))
            {
                return value;
            }

            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(
                namespaceUri);

            lock (Lock)
            {
                // remember the result for faster access.
                m_namespaceMetadataStates[namespaceUri] = namespaceMetadataState;
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Gets or creates the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        public NamespaceMetadataState CreateNamespaceMetadataState(string namespaceUri)
        {
            NamespaceMetadataState namespaceMetadataState = FindNamespaceMetadataState(
                namespaceUri);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState))
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.LogError(
                        "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.",
                        namespaceUri);
                    return null;
                }

                // create the NamespaceMetadata node
                namespaceMetadataState = new NamespaceMetadataState(serverNamespacesNode)
                {
                    BrowseName = new QualifiedName(namespaceUri, NamespaceIndex)
                };
                namespaceMetadataState.Create(
                    SystemContext,
                    null,
                    namespaceMetadataState.BrowseName,
                    null,
                    true);
                namespaceMetadataState.DisplayName = namespaceUri;
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState.NamespaceUri.Value = namespaceUri;

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                serverNamespacesNode.ClearChangeMasks(Server.DefaultSystemContext, true);
                AddPredefinedNode(SystemContext, namespaceMetadataState);
            }

            return namespaceMetadataState;
        }

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        public void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            HasApplicationSecureAdminAccess(context, null);
        }

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        public void HasApplicationSecureAdminAccess(
            ISystemContext context,
            CertificateStoreIdentifier _)
        {
            if (context is SystemContext { OperationContext: OperationContext operationContext })
            {
                if (operationContext.ChannelContext?.EndpointDescription?.SecurityMode !=
                    MessageSecurityMode.SignAndEncrypt)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "Access to this item is only allowed with MessageSecurityMode SignAndEncrypt.");
                }
                IUserIdentity identity = operationContext.UserIdentity;
                // allow access to system configuration only with Role SecurityAdmin
                if (identity == null ||
                    identity.TokenType == UserTokenType.Anonymous ||
                    !identity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "Security Admin Role required to access this item.");
                }
            }
        }

        private async ValueTask<UpdateCertificateMethodStateResult> UpdateCertificateAsync(
           ISystemContext context,
           MethodState method,
           NodeId objectId,
           NodeId certificateGroupId,
           NodeId certificateTypeId,
           byte[] certificate,
           byte[][] issuerCertificates,
           string privateKeyFormat,
           byte[] privateKey,
           CancellationToken cancellation)
        {
            bool applyChangesRequired = false;
            HasApplicationSecureAdminAccess(context);

            object[] inputArguments =
            [
                certificateGroupId,
                certificateTypeId,
                certificate,
                issuerCertificates,
                privateKeyFormat,
                privateKey
            ];
            X509Certificate2 newCert = null;

            Server.ReportCertificateUpdateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);
            try
            {
                if (certificate == null)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                privateKeyFormat = privateKeyFormat?.ToUpper();
                if (!(string.IsNullOrEmpty(privateKeyFormat) ||
                    privateKeyFormat == "PEM" ||
                    privateKeyFormat == "PFX"))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The private key format is not supported.");
                }

                ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                    certificateGroupId,
                    certificateTypeId);
                certificateGroup.UpdateCertificate = null;

                try
                {
                    newCert = X509CertificateLoader.LoadCertificate(certificate);
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate data is invalid.");
                }

                // validate certificate type of new certificate
                if (!CertificateIdentifier.ValidateCertificateType(newCert, certificateTypeId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate type of new certificate doesn't match the provided certificate type.");
                }

                // identify the existing certificate to be updated
                // it should be of the same type and same subject name as the new certificate
                CertificateIdentifier existingCertIdentifier =
                    (
                        certificateGroup.ApplicationCertificates.FirstOrDefault(cert =>
                            X509Utils.CompareDistinguishedName(cert.SubjectName, newCert.Subject) &&
                            cert.CertificateType == certificateTypeId)
                        ?? certificateGroup.ApplicationCertificates.FirstOrDefault(cert =>
                            cert.Certificate != null &&
                            m_configuration.ApplicationUri ==
                                X509Utils.GetApplicationUriFromCertificate(cert.Certificate) &&
                            cert.CertificateType == certificateTypeId))
                    ?? throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "No existing certificate found for the specified certificate type and subject name.");

                var newIssuerCollection = new X509Certificate2Collection();

                try
                {
                    // build issuer chain
                    if (issuerCertificates != null)
                    {
                        foreach (byte[] issuerRawCert in issuerCertificates)
                        {
                            X509Certificate2 newIssuerCert = X509CertificateLoader.LoadCertificate(
                                issuerRawCert);
                            newIssuerCollection.Add(newIssuerCert);
                        }
                    }
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate data is invalid.");
                }

                // self signed
                bool selfSigned = X509Utils.IsSelfSigned(newCert);
                if (selfSigned && newIssuerCollection.Count != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Issuer list not empty for self signed certificate.");
                }

                if (!selfSigned)
                {
                    try
                    {
                        // verify cert with issuer chain
                        var certValidator = new CertificateValidator(Server.Telemetry);
                        var issuerStore = new CertificateTrustList();
                        var issuerCollection = new CertificateIdentifierCollection();
                        foreach (X509Certificate2 issuerCert in newIssuerCollection)
                        {
                            issuerCollection.Add(new CertificateIdentifier(issuerCert));
                        }
                        issuerStore.TrustedCertificates = issuerCollection;
                        certValidator.Update(issuerStore, issuerStore, null);
                        certValidator.Validate(newCert);
                    }
                    catch (Exception ex)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to verify integrity of the new certificate and the issuer list.",
                            ex);
                    }
                }

                var updateCertificate = new UpdateCertificateData();
                try
                {
                    ICertificatePasswordProvider passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                        {
                            X509Certificate2 exportableKey;
                            //use the new generated private key if one exists and matches the provided public key
                            if (certificateGroup.TemporaryApplicationCertificate != null &&
                                X509Utils.VerifyKeyPair(
                                    newCert,
                                    certificateGroup.TemporaryApplicationCertificate))
                            {
                                exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                    certificateGroup.TemporaryApplicationCertificate,
                                    false);
                            }
                            else
                            {
                                X509Certificate2 certWithPrivateKey = await existingCertIdentifier
                                    .LoadPrivateKeyExAsync(
                                        passwordProvider,
                                        m_configuration.ApplicationUri,
                                        Server.Telemetry,
                                        cancellation)
                                    .ConfigureAwait(false);
                                exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                    certWithPrivateKey,
                                    false);
                            }

                            updateCertificate.CertificateWithPrivateKey =
                                CertificateFactory.CreateCertificateWithPrivateKey(
                                    newCert,
                                    exportableKey);
                            break;
                        }
                        case "PFX":
                        {
                            X509Certificate2 certWithPrivateKey = X509Utils
                                .CreateCertificateFromPKCS12(
                                    privateKey,
                                    passwordProvider?.GetPassword(existingCertIdentifier),
                                    true);
                            updateCertificate.CertificateWithPrivateKey =
                                CertificateFactory.CreateCertificateWithPrivateKey(
                                    newCert,
                                    certWithPrivateKey);
                            break;
                        }
                        case "PEM":
                            updateCertificate.CertificateWithPrivateKey =
                                CertificateFactory.CreateCertificateWithPEMPrivateKey(
                                    newCert,
                                    privateKey,
                                    passwordProvider?.GetPassword(existingCertIdentifier));
                            break;
                    }
                    //dispose temporary new private key as it is no longer needed
                    certificateGroup.TemporaryApplicationCertificate?.Dispose();
                    certificateGroup.TemporaryApplicationCertificate = null;

                    updateCertificate.IssuerCollection = newIssuerCollection;
                    updateCertificate.SessionId = context.SessionId;
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to verify integrity of the new certificate and the private key.");
                }

                certificateGroup.UpdateCertificate = updateCertificate;
                applyChangesRequired = true;

                if (updateCertificate != null)
                {
                    try
                    {
                        using (ICertificateStore appStore = existingCertIdentifier.OpenStore(Server.Telemetry))
                        {
                            if (appStore == null)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadConfigurationError,
                                    "Failed to open application certificate store.");
                            }

                            m_logger.LogInformation(
                                Utils.TraceMasks.Security,
                                "Delete application certificate {Certificate}",
                                existingCertIdentifier.Certificate.AsLogSafeString());
                            appStore.DeleteAsync(existingCertIdentifier.Thumbprint, cancellation)
                                .Wait(cancellation);
                            m_logger.LogInformation(
                                Utils.TraceMasks.Security,
                                "Add new application certificate {Certificate}",
                                updateCertificate.CertificateWithPrivateKey.AsLogSafeString());
                            ICertificatePasswordProvider passwordProvider = m_configuration
                                .SecurityConfiguration
                                .CertificatePasswordProvider;
                            appStore
                                .AddAsync(
                                    updateCertificate.CertificateWithPrivateKey,
                                    passwordProvider?.GetPassword(existingCertIdentifier),
                                    cancellation)
                                .Wait(cancellation);
                            // keep only track of cert without private key
                            X509Certificate2 certOnly = X509CertificateLoader.LoadCertificate(
                                updateCertificate.CertificateWithPrivateKey.RawData);
                            updateCertificate.CertificateWithPrivateKey.Dispose();
                            updateCertificate.CertificateWithPrivateKey = certOnly;
                            //update certificate identifier with new certificate
                            await existingCertIdentifier.FindAsync(
                                m_configuration.ApplicationUri,
                                Server.Telemetry,
                                cancellation)
                                .ConfigureAwait(false);
                        }

                        ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
                        try
                        {
                            if (issuerStore == null)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadConfigurationError,
                                    "Failed to open issuer certificate store.");
                            }

                            foreach (X509Certificate2 issuer in updateCertificate.IssuerCollection)
                            {
                                try
                                {
                                    m_logger.LogInformation(
                                        Utils.TraceMasks.Security,
                                        "Add new issuer certificate {Certificate}",
                                        issuer.AsLogSafeString());
                                    issuerStore.AddAsync(issuer, ct: cancellation)
                                        .Wait(cancellation);
                                }
                                catch (ArgumentException)
                                {
                                    // ignore error if issuer cert already exists
                                }
                            }
                        }
                        finally
                        {
                            issuerStore?.Close();
                        }

                        Server.ReportCertificateUpdatedAuditEvent(
                            context,
                            objectId,
                            method,
                            inputArguments,
                            certificateGroupId,
                            certificateTypeId,
                            m_logger);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            Utils.TraceMasks.Security,
                            "{StackTrace}",
                            ServiceResult.BuildExceptionTrace(ex));
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to update certificate.",
                            ex);
                    }
                }
            }
            catch (Exception e)
            {
                // report the failure of UpdateCertificate via an audit event
                Server.ReportCertificateUpdatedAuditEvent(
                    context,
                    objectId,
                    method,
                    inputArguments,
                    certificateGroupId,
                    certificateTypeId,
                    m_logger,
                    e);
                // Raise audit certificate event
                Server.ReportAuditCertificateEvent(newCert, e, m_logger);
                throw;
            }

            return new UpdateCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ApplyChangesRequired = applyChangesRequired
            };
        }

        private async ValueTask<CreateSigningRequestMethodStateResult> CreateSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            byte[] nonce,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // identify the existing certificate for which to CreateSigningRequest
            // it should be of the same type
            CertificateIdentifier existingCertIdentifier = certificateGroup.ApplicationCertificates
                .FirstOrDefault(
                    cert => cert.CertificateType == certificateTypeId);

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = existingCertIdentifier.Certificate.Subject;
            }

            certificateGroup.TemporaryApplicationCertificate?.Dispose();
            certificateGroup.TemporaryApplicationCertificate = null;

            X509Certificate2 certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                IList<string> domainNames = X509Utils.GetDomainsFromCertificate(existingCertIdentifier.Certificate);

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    certificateGroup,
                    subjectName,
                    domainNames);
            }
            else
            {
                ICertificatePasswordProvider passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await existingCertIdentifier
                    .LoadPrivateKeyExAsync(passwordProvider,
                                           m_configuration.ApplicationUri,
                                           Server.Telemetry,
                                           cancellationToken)
                    .ConfigureAwait(false);

                if (certWithPrivateKey == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");
                }
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Create signing request {Certificate}",
                certWithPrivateKey.AsLogSafeString());
            byte[] certificateRequest = CertificateFactory.CreateSigningRequest(
                certWithPrivateKey,
                X509Utils.GetDomainsFromCertificate(certWithPrivateKey));

            return new CreateSigningRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                CertificateRequest = certificateRequest
            };
        }

        private X509Certificate2 GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            ServerCertificateGroup certificateGroup,
            string subjectName,
            IList<string> domainNames)
        {
            X509Certificate2 certificate;

            ICertificateBuilder certificateBuilder = CertificateFactory
                .CreateCertificate(m_configuration.ApplicationUri, m_configuration.ApplicationName, subjectName, null)
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddDays(14));

            if (certificateTypeId == null ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                certificate = certificateBuilder.SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();
            }
            else
            {
#if !ECC_SUPPORT
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The Ecc certificate type is not supported.");
#else
                ECCurve? curve =
                    EccUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The Ecc certificate type is not supported.");
                certificate = certificateBuilder.SetECCurve(curve.Value).CreateForECDsa();
#endif
            }

            certificateGroup.TemporaryApplicationCertificate = certificate;

            return certificate;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            HasApplicationSecureAdminAccess(context);

            bool disconnectSessions = false;

            foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
            {
                try
                {
                    UpdateCertificateData updateCertificate = certificateGroup.UpdateCertificate;
                    if (updateCertificate != null)
                    {
                        disconnectSessions = true;
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Apply Changes for certificate {Certificate}",
                            updateCertificate.CertificateWithPrivateKey.AsLogSafeString());
                    }
                }
                finally
                {
                    certificateGroup.UpdateCertificate = null;
                }
            }

            if (disconnectSessions)
            {
                Task.Run(async () =>
                {
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Apply Changes for application certificate update.");
                    // give the client some time to receive the response
                    // before the certificate update may disconnect all sessions
                    await Task.Delay(1000).ConfigureAwait(false);
                    try
                    {
                        await m_configuration
                            .CertificateValidator.UpdateCertificateAsync(
                                m_configuration.SecurityConfiguration,
                                m_configuration.ApplicationUri)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogCritical(
                            ex,
                            "Failed to sucessfully Apply Changes: Error updating application instance certificates. Server could be in faulted state.");
                        throw;
                    }
                });
            }

            return StatusCodes.Good;
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref byte[][] certificates)
        {
            HasApplicationSecureAdminAccess(context);

            // No rejected store configured
            if (m_rejectedStore == null)
            {
                certificates = [];
                return StatusCodes.Good;
            }

            ICertificateStore store = m_rejectedStore.OpenStore(Server.Telemetry);
            try
            {
                if (store != null)
                {
                    X509Certificate2Collection collection = store.EnumerateAsync().Result;
                    var rawList = new List<byte[]>();
                    foreach (X509Certificate2 cert in collection)
                    {
                        rawList.Add(cert.RawData);
                    }
                    certificates = [.. rawList];
                }
            }
            finally
            {
                store?.Close();
            }

            return StatusCodes.Good;
        }

        private ServiceResult GetCertificates(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            ref NodeId[] certificateTypeIds,
            ref byte[][] certificates)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup =
                m_certificateGroups.FirstOrDefault(
                    group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");

            certificateTypeIds = certificateGroup.CertificateTypes;
            certificates = [.. certificateGroup.ApplicationCertificates
                .Select(s => s.Certificate?.RawData)];

            return ServiceResult.Good;
        }

        private ServerCertificateGroup VerifyGroupAndTypeId(
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            // verify typeid must be set
            if (NodeId.IsNull(certificateTypeId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not specified.");
            }

            // verify requested certificate group
            if (NodeId.IsNull(certificateGroupId))
            {
                certificateGroupId = ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            }

            ServerCertificateGroup certificateGroup =
                m_certificateGroups.FirstOrDefault(
                    group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");

            // verify certificate type
            bool foundCertType = certificateGroup.CertificateTypes
                .Any(t => Utils.IsEqual(t, certificateTypeId));
            if (!foundCertType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not valid for certificate group.");
            }

            return certificateGroup;
        }

        /// <summary>
        /// Finds the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        private NamespaceMetadataState FindNamespaceMetadataState(string namespaceUri)
        {
            try
            {
                // find ServerNamespaces node
                if (FindPredefinedNode(ObjectIds.Server_Namespaces, typeof(NamespacesState))
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.LogError("Cannot find ObjectIds.Server_Namespaces node.");
                    return null;
                }

                IList<BaseInstanceState> serverNamespacesChildren = [];
                serverNamespacesNode.GetChildren(SystemContext, serverNamespacesChildren);

                foreach (BaseInstanceState namespacesReference in serverNamespacesChildren)
                {
                    // Find NamespaceMetadata node of NamespaceUri in Namespaces children
                    if (namespacesReference is not NamespaceMetadataState namespaceMetadata)
                    {
                        continue;
                    }

                    if (namespaceMetadata.NamespaceUri.Value == namespaceUri)
                    {
                        return namespaceMetadata;
                    }
                }

                IList<IReference> serverNamespacesReferencs = [];
                serverNamespacesNode.GetReferences(SystemContext, serverNamespacesReferencs);

                foreach (IReference serverNamespacesReference in serverNamespacesReferencs)
                {
                    if (!serverNamespacesReference.IsInverse)
                    {
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(
                            serverNamespacesReference.TargetId,
                            Server.NamespaceUris);
                        if (FindNodeInAddressSpace(
                            nameSpaceNodeId) is not NamespaceMetadataState namespaceMetadata)
                        {
                            continue;
                        }

                        if (namespaceMetadata.NamespaceUri.Value == namespaceUri)
                        {
                            return namespaceMetadata;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Error searching NamespaceMetadata for namespaceUri {NamespaceUri}.",
                    namespaceUri);
                return null;
            }
        }

        /// <summary>
        /// Clear NamespaceMetadata nodes cache in case nodes are added or deleted
        /// </summary>
        private void ServerNamespacesChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 ||
                (changes & NodeStateChangeMasks.References) != 0)
            {
                try
                {
                    lock (Lock)
                    {
                        m_namespaceMetadataStates.Clear();
                    }
                }
                catch
                {
                    // ignore errors
                }
            }
        }

        private class UpdateCertificateData
        {
            public NodeId SessionId;
            public X509Certificate2 CertificateWithPrivateKey;
            public X509Certificate2Collection IssuerCollection;
        }

        private class ServerCertificateGroup
        {
            public string BrowseName;
            public NodeId NodeId;
            public CertificateGroupState Node;
            public NodeId[] CertificateTypes;
            public CertificateIdentifierCollection ApplicationCertificates;
            public CertificateStoreIdentifier IssuerStore;
            public CertificateStoreIdentifier TrustedStore;
            public UpdateCertificateData UpdateCertificate;
            public X509Certificate2 TemporaryApplicationCertificate;
        }

        private ServerConfigurationState m_serverConfigurationNode;
        private readonly ApplicationConfiguration m_configuration;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier m_rejectedStore;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
    }
}
