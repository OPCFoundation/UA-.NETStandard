/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Diagnostics;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// A node manager for a global discovery server
    /// </summary>
    public class ApplicationsNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Gets or sets the trust-list manager for named store access.
        /// </summary>
        public ICertificateTrustListManager? TrustListManager { get; set; }

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
            ICertificateGroup certificateGroupFactory,
            bool autoApprove = false)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<ApplicationsNodeManager>())
        {
            NamespaceUris = ["http://opcfoundation.org/UA/GDS/applications/", Namespaces.OpcUaGds];

            SystemContext.NodeIdFactory = this;

            m_configuration = configuration;
            // get the configuration for the node manager.
            m_globalDiscoveryServerConfiguration =
                configuration.ParseExtension<GlobalDiscoveryServerConfiguration>()
                ?? new GlobalDiscoveryServerConfiguration();

            // use suitable defaults if no configuration exists.

            string? defaultSubjectNameContext = m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext;
            if (!string.IsNullOrEmpty(defaultSubjectNameContext) &&
                defaultSubjectNameContext![0] != ',')
            {
                m_globalDiscoveryServerConfiguration.DefaultSubjectNameContext =
                    "," + defaultSubjectNameContext;
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
            m_certificateGroupFactory = certificateGroupFactory;
            m_certificateGroups = [];

            try
            {
                ServerOnNetwork[]? results = m_database.QueryServers(
                    0,
                    5,
                    null!,
                    null!,
                    null!,
                    default,
                    out DateTimeUtc lastResetTime);
                m_logger.LogInformation("QueryServers Returned: {Count} records", results?.Length ?? 0);

                if (results != null)
                {
                    foreach (ServerOnNetwork result in results)
                    {
                        m_logger.LogInformation("Server Found at {DiscoveryUrl}", result.DiscoveryUrl);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Could not connect to the Database!");

                Exception? ie = e.InnerException;

                while (ie != null)
                {
                    m_logger.LogInformation(ie, "Exception");
                    ie = ie.InnerException;
                }

                m_logger.LogInformation("Initialize Database tables!");
                m_database.Initialize();

                m_logger.LogInformation("Database Initialized!");
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
            if (certificateGroupId.IsNull)
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup? certificateGroup))
            {
                return certificateGroup.DefaultTrustList?.NodeId ?? default;
            }

            return default;
        }

        private bool? GetCertificateStatus(NodeId certificateGroupId, NodeId certificateTypeId)
        {
            if (m_certificateGroups.TryGetValue(
                certificateGroupId,
                out ICertificateGroup? certificateGroup))
            {
                if (!certificateTypeId.IsNull &&
                    !certificateGroup.CertificateTypes.Contains(certificateTypeId))
                {
                    return null;
                }
                return certificateGroup.UpdateRequired;
            }

            return null;
        }

        private ICertificateGroup? GetGroupForCertificate(ByteString certificate)
        {
            if (certificate.Length > 0)
            {
                using var x509 = Certificate.FromRawData(certificate);
                NodeId certificateType = CertificateIdentifier.GetCertificateType(x509);
                foreach (ICertificateGroup certificateGroup in m_certificateGroups.Values)
                {
                    KeyValuePair<NodeId, Certificate?> matchingCert = certificateGroup
                        .Certificates
                        .FirstOrDefault(
                            kvp =>
                                X509Utils.CompareDistinguishedName(
                                    kvp.Value!.Subject,
                                    x509.Issuer) &&
                                kvp.Key == certificateType);

                    if (matchingCert.Value != null)
                    {
                        return certificateGroup;
                    }
                }
            }

            return null;
        }

        private async Task<bool> RevokeCertificateAsync(
            ByteString certificate,
            CancellationToken cancellationToken = default)
        {
            bool revoked = false;
            if (certificate.Length > 0)
            {
                ICertificateGroup? certificateGroup = GetGroupForCertificate(certificate);

                if (certificateGroup != null)
                {
                    using var x509 = Certificate.FromRawData(certificate);
                    try
                    {
                        X509CRL crl = await certificateGroup
                            .RevokeCertificateAsync(x509, cancellationToken)
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
                            "Unexpected error revoking certificate. {Subject} for Authority={CertificateGroupId}",
                            x509.Subject,
                            certificateGroup.Id);
                    }
                }
            }
            return revoked;
        }

        /// <summary>
        /// Builds the issuer-certificate chain for a newly issued
        /// <paramref name="certificate"/> per OPC 10000-12 §7.6.6.
        /// </summary>
        /// <remarks>
        /// The chain is returned in leaf-to-root order, excluding the leaf
        /// itself. The immediate issuing CA is always included. The
        /// in-memory <see cref="ICertificateGroup.Certificates"/> map is
        /// used as the additional chain-building candidate set so the
        /// helper does not contend with concurrent
        /// <c>SigningRequestAsync</c> / <c>NewKeyPairRequestAsync</c>
        /// writes against the CertificateGroup's AuthoritiesStore.
        /// </remarks>
#pragma warning disable CA1822 // method uses instance fields via the captured certificateGroup parameter
        private ArrayOf<ByteString> BuildIssuerCertificateChain(
            Certificate certificate,
            ICertificateGroup certificateGroup,
            NodeId certificateTypeNodeId)
#pragma warning restore CA1822
        {
            var issuerChain = new List<ByteString>();
            var clones = new List<X509Certificate2>();

            try
            {
                var candidates = new X509Certificate2Collection();

                // Use the in-memory CA cert(s) the CertificateGroup
                // already exposes; clone the byte arrays so the chain
                // build holds independent X509Certificate2 handles.
                foreach (KeyValuePair<NodeId, Certificate?> kvp in certificateGroup.Certificates)
                {
                    if (kvp.Value == null)
                    {
                        continue;
                    }
                    X509Certificate2 clone = X509CertificateLoader.LoadCertificate(kvp.Value.RawData);
                    clones.Add(clone);
                    candidates.Add(clone);
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags =
                    X509VerificationFlags.AllowUnknownCertificateAuthority |
                    X509VerificationFlags.IgnoreEndRevocationUnknown |
                    X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                    X509VerificationFlags.IgnoreCtlNotTimeValid |
                    X509VerificationFlags.IgnoreRootRevocationUnknown;
                chain.ChainPolicy.ExtraStore.AddRange(candidates);

                using X509Certificate2 leaf = certificate.AsX509Certificate2();
                if (chain.Build(leaf))
                {
                    // ChainElements is leaf-first; skip the leaf and emit
                    // the remaining issuers in order (immediate issuer
                    // first).
                    for (int i = 1; i < chain.ChainElements.Count; i++)
                    {
                        byte[] raw = chain.ChainElements[i].Certificate.RawData;
                        issuerChain.Add(ByteString.From(raw));
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "Failed to build issuer chain for {Subject}; falling back to the immediate issuing CA.",
                    certificate.Subject);
            }
            finally
            {
                foreach (X509Certificate2 clone in clones)
                {
                    clone.Dispose();
                }
            }

            // Always ensure the immediate issuing CA is included even if
            // chain building did not produce one (e.g. AKI mismatch).
            if (issuerChain.Count == 0 &&
                certificateGroup.Certificates.TryGetValue(
                    certificateTypeNodeId,
                    out Certificate? issuingCa) &&
                issuingCa != null)
            {
                issuerChain.Add(ByteString.From(issuingCa.RawData));
            }

            return [.. issuerChain];
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
                m_globalDiscoveryServerConfiguration.AuthoritiesStorePath!,
                certificateGroupConfiguration,
                m_configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath);
            await certificateGroup.InitAsync().ConfigureAwait(false);

            await SetCertificateGroupNodesAsync(certificateGroup).ConfigureAwait(false);

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
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            m_certTypeMap = new Dictionary<NodeId, string>
            {
                // list of supported cert type mappings (V1.04)
                {
                    Ua.ObjectTypeIds.HttpsCertificateType,
                    nameof(Ua.ObjectTypeIds.HttpsCertificateType)
                },
                {
                    Ua.ObjectTypeIds.UserCertificateType,
                    nameof(Ua.ObjectTypeIds.UserCertificateType)
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
                }
            };

            m_database.NamespaceIndex = NamespaceIndexes[0];
            m_request.NamespaceIndex = NamespaceIndexes[0];

            await EnsureDefaultAuthorizationServiceAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (
                CertificateGroupConfiguration certificateGroupConfiguration in m_globalDiscoveryServerConfiguration
                    .CertificateGroups.ToList())
            {
                try
                {
                    ICertificateGroup certificateGroup = await InitializeCertificateGroupAsync(
                            certificateGroupConfiguration)
                        .ConfigureAwait(false);
                    m_certificateGroups[certificateGroup.Id] = certificateGroup;
                }
                catch (Exception e)
                {
                    m_logger.LogError(
                        e,
                        "Unexpected error initializing certificateGroup: {CertificateGroupId}",
                        certificateGroupConfiguration.Id);
                    // make sure gds server doesn't start without cert groups!
                    throw;
                }
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddOpcUaGds(context));
        }

        private async ValueTask EnsureDefaultAuthorizationServiceAsync(
            BaseObjectState? folder = null,
            CancellationToken cancellationToken = default)
        {
            ushort namespaceIndex = NamespaceIndexes[1];
            if (folder == null)
            {
                var folderId = new NodeId(Objects.AuthorizationServices, namespaceIndex);
                folder = FindPredefinedNode<BaseObjectState>(folderId);
            }

            var browseName = new QualifiedName("Default", namespaceIndex);
            if (folder?.FindChild(SystemContext, browseName) != null)
            {
                return;
            }

            AuthorizationServiceState service = CreateDefaultAuthorizationService(
                folder,
                SystemContext,
                namespaceIndex,
                browseName);
            folder?.AddChild(service);
            await AddPredefinedNodeAsync(SystemContext, service, cancellationToken).ConfigureAwait(false);
        }

        private AuthorizationServiceState CreateDefaultAuthorizationService(
            NodeState? folder,
            ISystemContext context,
            ushort namespaceIndex,
            QualifiedName browseName)
        {
            var service = new AuthorizationServiceState(folder);

            service.Create(
                context,
                new NodeId("AuthorizationServices/Default", namespaceIndex),
                browseName,
                new LocalizedText("Default"),
                false);

            // ServiceUri, ServiceCertificate and the mandatory GetServiceDescription method
            // are created automatically by the source-generated AuthorizationServiceState.
            // The Optional method children must be added explicitly using the generated
            // Add* helpers before ConfigureAuthorizationServiceNode wires the OnCall handlers.
            service.AddRequestAccessToken(context);
            service.AddStartRequestToken(context);
            service.AddFinishRequestToken(context);
            service.AddRefreshToken(context);

            service.ServiceUri!.Value = m_configuration.ApplicationUri ?? string.Empty;
            service.ServiceCertificate!.Value = ByteString.Empty;
            service.UserTokenPolicies?.Value = m_configuration.ServerConfiguration?.UserTokenPolicies ?? default;
            ConfigureAuthorizationServiceNode(service);

            return service;
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is not BaseObjectState passiveNode)
            {
                return predefinedNode;
            }

            if (IsNodeIdInNamespace(passiveNode.NodeId) &&
                passiveNode.NodeId.TryGetValue(out uint nodeNumericId) &&
                nodeNumericId == Objects.AuthorizationServices)
            {
                await EnsureDefaultAuthorizationServiceAsync(passiveNode, cancellationToken).ConfigureAwait(false);
            }

            NodeId typeId = passiveNode.TypeDefinitionId;

            if (!IsNodeIdInNamespace(typeId) || !typeId.TryGetValue(out uint numericId))
            {
                return predefinedNode;
            }

            switch (numericId)
            {
                case ObjectTypes.CertificateDirectoryType:
                    if (passiveNode is not CertificateDirectoryState activeNode)
                    {
                        activeNode = new CertificateDirectoryState(passiveNode.Parent)
                        {
                            RevokeCertificate = new RevokeCertificateMethodState(passiveNode),
                            CheckRevocationStatus = new CheckRevocationStatusMethodState(passiveNode),
                            GetCertificates = new GetCertificatesMethodState(passiveNode)
                        };

                        activeNode.Create(context, passiveNode);
                        // replace the node in the parent.
                        passiveNode.Parent?.ReplaceChild(context, activeNode);
                    }

                    activeNode.QueryServers!.OnCall = OnQueryServers;
                    activeNode.QueryApplications!.OnCall = OnQueryApplications;
                    activeNode.RegisterApplication!.OnCall = OnRegisterApplication;
                    activeNode.UpdateApplication!.OnCall = OnUpdateApplication;
                    activeNode.UpdateApplication.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.UpdateApplication.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.GetApplication!.OnCall = OnGetApplication;

                    // These also add self admin role permissions (call)
                    activeNode.UnregisterApplication!.OnCallAsync = OnUnregisterApplicationAsync;
                    activeNode.UnregisterApplication.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.UnregisterApplication.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.FindApplications!.OnCall = OnFindApplications;
                    activeNode.FindApplications.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.FindApplications.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.StartNewKeyPairRequest!.OnCall = OnStartNewKeyPairRequest;
                    activeNode.StartNewKeyPairRequest.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.StartNewKeyPairRequest.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.FinishRequest!.OnCallAsync = OnFinishRequestAsync;
                    activeNode.FinishRequest.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.FinishRequest.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.GetCertificateGroups!.OnCall = OnGetCertificateGroups;
                    activeNode.GetCertificateGroups.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.GetCertificateGroups.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.GetTrustList!.OnCall = OnGetTrustList;
                    activeNode.GetTrustList.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.GetTrustList.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.GetCertificateStatus!.OnCall = OnGetCertificateStatus;
                    activeNode.GetCertificateStatus.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.GetCertificateStatus.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.StartSigningRequest!.OnCallAsync = OnStartSigningRequestAsync;
                    activeNode.StartSigningRequest.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.StartSigningRequest.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;
                    activeNode.GetCertificates!.OnCall = OnGetCertificates;
                    activeNode.GetCertificates.OnReadRolePermissions = OnAddSelfAdminRolePermissions;
                    activeNode.GetCertificates.OnReadUserRolePermissions = OnAddSelfAdminUserRolePermissions;

                    activeNode.RevokeCertificate!.OnCallAsync = OnRevokeCertificateAsync;
                    activeNode.CheckRevocationStatus!.OnCallAsync = OnCheckRevocationStatusAsync;

                    PropertyState<ArrayOf<NodeId>> defaultApplicationCertificateTypes = activeNode.CertificateGroups!
                        .DefaultApplicationGroup!.CertificateTypes!;
                    if (m_certificateGroups.TryGetValue(
                            m_defaultApplicationGroupId,
                            out ICertificateGroup? applicationCertificateGroup))
                    {
                        defaultApplicationCertificateTypes.Value =
                        [
                            .. applicationCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        defaultApplicationCertificateTypes.Value =
                        [
                            Ua.ObjectTypeIds.ApplicationCertificateType
                        ];
                    }
                    // OPC 10000-12 §7.8.2.1: a TrustList that supports
                    // CloseAndUpdate / AddCertificate / RemoveCertificate
                    // is writeable; Writable / UserWritable advertise the
                    // capability while the role-based access on the
                    // individual methods enforces who may actually mutate
                    // the trust list.
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList!.LastUpdateTime!.Value =
                        DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.Writable!.Value =
                        true;
                    activeNode.CertificateGroups.DefaultApplicationGroup.TrustList.UserWritable!.Value =
                        true;

                    PropertyState<ArrayOf<NodeId>> defaultHttpsCertificateTypes = activeNode.CertificateGroups
                        .DefaultHttpsGroup!.CertificateTypes!;
                    if (m_certificateGroups.TryGetValue(
                            m_defaultHttpsGroupId,
                            out ICertificateGroup? httpsCertificateGroup))
                    {
                        defaultHttpsCertificateTypes.Value =
                        [
                            .. httpsCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        defaultHttpsCertificateTypes.Value =
                        [
                            Ua.ObjectTypeIds.HttpsCertificateType
                        ];
                    }
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList!.LastUpdateTime!.Value =
                        DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.Writable!.Value =
                        true;
                    activeNode.CertificateGroups.DefaultHttpsGroup.TrustList.UserWritable!.Value =
                        true;

                    PropertyState<ArrayOf<NodeId>> defaultUserTokenCertificateTypes = activeNode.CertificateGroups
                        .DefaultUserTokenGroup!.CertificateTypes!;
                    if (m_certificateGroups.TryGetValue(
                            m_defaultUserTokenGroupId,
                            out ICertificateGroup? userTokenCertificateGroup))
                    {
                        defaultUserTokenCertificateTypes.Value =
                        [
                            .. userTokenCertificateGroup.CertificateTypes
                        ];
                    }
                    else
                    {
                        defaultUserTokenCertificateTypes.Value =
                        [
                            Ua.ObjectTypeIds.UserCertificateType
                        ];
                    }
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList!.LastUpdateTime!.Value =
                        DateTime.UtcNow;
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.Writable!.Value =
                        true;
                    activeNode.CertificateGroups.DefaultUserTokenGroup.TrustList.UserWritable!.Value =
                        true;

                    return activeNode;
                case ObjectTypes.KeyCredentialServiceType:
                    if (passiveNode is not KeyCredentialServiceState keyCredNode)
                    {
                        keyCredNode = new KeyCredentialServiceState(passiveNode.Parent);
                        keyCredNode.Create(context, passiveNode);
                        passiveNode.Parent?.ReplaceChild(context, keyCredNode);
                    }

                    keyCredNode.StartRequest!.OnCallAsync = OnKeyCredentialStartRequestAsync;
                    keyCredNode.FinishRequest!.OnCallAsync = OnKeyCredentialFinishRequestAsync;
                    keyCredNode.Revoke?.OnCallAsync = OnKeyCredentialRevokeAsync;

                    return keyCredNode;
                case ObjectTypes.AuthorizationServiceType:
                    if (passiveNode is not AuthorizationServiceState authServiceNode)
                    {
                        authServiceNode = new AuthorizationServiceState(passiveNode.Parent);
                        authServiceNode.Create(context, passiveNode);
                        passiveNode.Parent?.ReplaceChild(context, authServiceNode);
                    }

                    ConfigureAuthorizationServiceNode(authServiceNode);

                    return authServiceNode;
            }

            return predefinedNode;
        }

        private void ConfigureAuthorizationServiceNode(AuthorizationServiceState authServiceNode)
        {
            authServiceNode.GetServiceDescription!.OnCall = OnGetServiceDescription;
            authServiceNode.RequestAccessToken?.OnCallAsync = OnRequestAccessTokenAsync;
            authServiceNode.StartRequestToken?.OnCallAsync = OnStartRequestTokenAsync;
            authServiceNode.FinishRequestToken?.OnCallAsync = OnFinishRequestTokenAsync;
            authServiceNode.RefreshToken?.OnCallAsync = OnRefreshTokenAsync;
        }

        private ServiceResult OnAddSelfAdminRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            if (value.IsEmpty)
            {
                return ServiceResult.Good;
            }
            return AddSelfAdminRolePermission(context, ref value);
        }

        private ServiceResult OnAddSelfAdminUserRolePermissions(
            ISystemContext context,
            NodeState node,
            ref ArrayOf<RolePermissionType> value)
        {
            var selfAdminRole = ExpandedNodeId.ToNodeId(
                GdsRole.ApplicationSelfAdmin.RoleId,
                context.NamespaceUris);
            IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;

            if (userIdentity == null ||
                !userIdentity.GrantedRoleIds.Contains(selfAdminRole))
            {
                return ServiceResult.Good;
            }

            // This contains the self admin role and other permissions
            return AddSelfAdminRolePermission(context, ref value);
        }

        private static ServiceResult AddSelfAdminRolePermission(
            ISystemContext context,
            ref ArrayOf<RolePermissionType> value)
        {
            var selfAdminRole = ExpandedNodeId.ToNodeId(
                GdsRole.ApplicationSelfAdmin.RoleId,
                context.NamespaceUris);
            var selfAdminPermission = new RolePermissionType
            {
                RoleId = selfAdminRole,
                Permissions = (uint)PermissionType.Call
            };
            value = ArrayOf.Combine(value, [selfAdminPermission]);
            return ServiceResult.Good;
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
            ArrayOf<string> serverCapabilities,
            ref DateTimeUtc lastCounterResetTime,
            ref ArrayOf<ServerOnNetwork> servers)
        {
            m_logger.LogInformation("QueryServers: {ApplicationUri} {ApplicationName}", applicationUri, applicationName);

            servers = m_database.QueryServers(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities,
                out lastCounterResetTime)!;

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
            ArrayOf<string> serverCapabilities,
            ref DateTimeUtc lastCounterResetTime,
            ref uint nextRecordId,
            ref ArrayOf<ApplicationDescription> applications)
        {
            m_logger.LogInformation("QueryApplications: {ApplicationUri} {ApplicationName}", applicationUri, applicationName);

            applications = m_database.QueryApplications(
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                applicationType,
                productUri,
                serverCapabilities,
                out lastCounterResetTime,
                out nextRecordId)!;
            return ServiceResult.Good;
        }

        private ServiceResult OnRegisterApplication(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ApplicationRecordDataType application,
            ref NodeId applicationId)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrAppAdmin);

            m_logger.LogInformation("OnRegisterApplication: {ApplicationUri}", application.ApplicationUri);

            try
            {
                applicationId = m_database.RegisterApplication(application);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }

            if (!applicationId.IsNull)
            {
                ArrayOf<Variant> inputArguments = [Variant.FromStructure(application), applicationId];
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
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                application.ApplicationId);

            m_logger.LogInformation("OnUpdateApplication: {ApplicationUri}", application.ApplicationUri);

            ApplicationRecordDataType? record = m_database.GetApplication(application.ApplicationId);

            if (record == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The application id does not exist."));
            }

            try
            {
                m_database.UpdateApplication(application);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }

            ArrayOf<Variant> inputArguments = [Variant.FromStructure(application)];
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
                AuthorizationHelper.DiscoveryAdminOrSelfAdminOrAppAdmin,
                applicationId);

            m_logger.LogInformation("OnUnregisterApplication: {ApplicationId}", applicationId.ToString());

            if (m_database.GetApplication(applicationId) == null)
            {
                return new UnregisterApplicationMethodStateResult
                {
                    ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The application id does not exist."))
                };
            }

            foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
            {
                try
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out ByteString certificate) &&
                        !certificate.IsEmpty)
                    {
                        await RevokeCertificateAsync(certificate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Failed to revoke: {CertificateType}", certType.Value);
                }
            }

            m_database.UnregisterApplication(applicationId);

            ArrayOf<Variant> inputArguments = [applicationId];
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

        private async ValueTask<RevokeCertificateMethodStateResult> OnRevokeCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ByteString certificate,
            CancellationToken cancellationToken)
        {
            // Per OPC 10000-12 §7.6.9 the CertificateRevokedAuditEvent shall
            // be generated on success or failure. The InputArguments are
            // (applicationId, certificate) per the method signature; the
            // certificate is a public ByteString so no redaction is needed.
            var result = new RevokeCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
            Exception? auditException = null;

            try
            {
                AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.CertificateAuthorityAdmin);

                if (m_database.GetApplication(applicationId) == null)
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a registered application."));
                    return result;
                }
                if (certificate.IsEmpty)
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
                            out ByteString applicationCertificate) ||
                        applicationCertificate.IsEmpty ||
                        !Utils.IsEqual(applicationCertificate, certificate))
                    {
                        continue;
                    }

                    revoked = await RevokeCertificateAsync(
                        certificate,
                        cancellationToken).ConfigureAwait(false);
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
                result.ServiceResult = ServiceResult.Good;
                return result;
            }
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null &&
                    result.ServiceResult != null &&
                    StatusCode.IsBad(result.ServiceResult.StatusCode))
                {
                    auditException = new ServiceResultException(result.ServiceResult);
                }

                ArrayOf<Variant> auditInputs = [applicationId, certificate];
                Server.ReportCertificateRevokedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    m_logger,
                    auditException);
            }
        }

        private ServiceResult OnFindApplications(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ref ArrayOf<ApplicationRecordDataType> applications)
        {
            AuthorizationHelper.HasAuthorization(context, AuthorizationHelper.AuthenticatedUser);
            m_logger.LogInformation("OnFindApplications: {ApplicationUri}", applicationUri);
            applications = m_database.FindApplications(applicationUri) ?? [];
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
            m_logger.LogInformation("OnGetApplication: {ApplicationId}", applicationId);
            try
            {
                application = m_database.GetApplication(applicationId)
                    ?? throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            catch (ArgumentException ex)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, ex);
            }
            return ServiceResult.Good;
        }

        private async ValueTask<CheckRevocationStatusMethodStateResult> OnCheckRevocationStatusAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ByteString certificate,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context);

            var result = new CheckRevocationStatusMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ValidityTime = DateTime.MinValue
            };

            // Per OPC 10000-12 §7.6.11, ValidityTime indicates when the
            // CertificateStatus result expires and should be rechecked.
            // We compute it as the earliest NextUpdate across the CRLs in
            // the trusted-issuer store after the chain has validated; for
            // Bad results the field remains DateTime.MinValue so callers
            // re-check immediately.
            DateTime computedValidityTime = DateTime.MinValue;

            try
            {
                //create chain to validate Certificate against it
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;

                //add GDS Issuer Cert Store Certificates to the Chain validation for consistent behaviour on all Platforms
                using ICertificateStore store = m_configuration.SecurityConfiguration
                    .TrustedIssuerCertificates
                    .OpenStore(Server.Telemetry);
                if (store != null)
                {
                    try
                    {
                        using CertificateCollection issuerCerts = await store
                            .EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        chain.ChainPolicy.ExtraStore
                            .AddRange(issuerCerts.AsX509Certificate2Collection());

                        X509CRLCollection crls = await store
                            .EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        DateTime nextUpdate = DateTime.MaxValue;
                        foreach (X509CRL crl in crls)
                        {
                            if (crl.NextUpdate != DateTime.MinValue &&
                                crl.NextUpdate < nextUpdate)
                            {
                                nextUpdate = crl.NextUpdate;
                            }
                        }
                        if (nextUpdate != DateTime.MaxValue)
                        {
                            computedValidityTime = nextUpdate;
                        }
                    }
                    finally
                    {
                        store.Close();
                    }
                }

                using var x509 = Certificate.FromRawData(certificate);
                using X509Certificate2 x509Cert = x509.AsX509Certificate2();
                if (chain.Build(x509Cert))
                {
                    result.CertificateStatus = StatusCodes.Good;
                    result.ValidityTime = computedValidityTime;
                    return result;
                }

                // Assessing certificateStatus for invalid chain
                X509ChainStatusFlags status = chain.ChainStatus.FirstOrDefault().Status;
                if ((status & X509ChainStatusFlags.NotTimeValid) ==
                    X509ChainStatusFlags.NotTimeValid)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateTimeInvalid;
                }
                else if ((status & X509ChainStatusFlags.Revoked) ==
                    X509ChainStatusFlags.Revoked)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateRevoked;
                }
                else if ((status & X509ChainStatusFlags.NotSignatureValid) ==
                    X509ChainStatusFlags.NotSignatureValid)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateInvalid;
                }
                else if ((status & X509ChainStatusFlags.NotValidForUsage) ==
                    X509ChainStatusFlags.NotValidForUsage)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateUseNotAllowed;
                }
                else if ((status & X509ChainStatusFlags.RevocationStatusUnknown) ==
                    X509ChainStatusFlags.RevocationStatusUnknown)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateRevocationUnknown;
                }
                else if ((status & X509ChainStatusFlags.PartialChain) ==
                    X509ChainStatusFlags.PartialChain)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateChainIncomplete;
                }
                else if ((status & X509ChainStatusFlags.ExplicitDistrust) ==
                    X509ChainStatusFlags.ExplicitDistrust)
                {
                    result.CertificateStatus = StatusCodes.BadCertificateUntrusted;
                }
                else
                {
                    // If no matching found use StatusCodes.BadCertificateRevoked
                    // Even though this is a no error = 0 case, the chain is invalid
                    result.CertificateStatus = StatusCodes.BadCertificateRevoked;
                }
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
            ref ArrayOf<NodeId> certificateTypeIds,
            ref ArrayOf<ByteString> certificates)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            var certificateTypeIdsList = new List<NodeId>();
            var certificatesList = new List<ByteString>();

            if (m_database.GetApplication(applicationId) == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a registered application."));
            }

            // If CertificateGroupId is null, the CertificateManager shall return the Certificates
            // for all CertificateGroups assigned to the Application.
            if (certificateGroupId.IsNull)
            {
                foreach (KeyValuePair<NodeId, string> certType in m_certTypeMap)
                {
                    if (m_database.GetApplicationCertificate(
                            applicationId,
                            certType.Value,
                            out ByteString certificate) &&
                        !certificate.IsEmpty)
                    {
                        certificateTypeIdsList.Add(certType.Key);
                        certificatesList.Add(certificate);
                    }
                }
            }
            // get only Certificate of the provided CertificateGroup
            else
            {
                if (!m_certificateGroups.TryGetValue(
                    certificateGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From(
                            "The CertificateGroupId is not recognized or not valid for the Application."));
                }
                foreach (NodeId certificateType in certificateGroup.CertificateTypes)
                {
                    if (m_certTypeMap.TryGetValue(certificateType, out string? certificateTypeId) &&
                        m_database.GetApplicationCertificate(
                            applicationId,
                            certificateTypeId,
                            out ByteString certificate
                        ) &&
                        !certificate.IsEmpty)
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
                    LocalizedText.From("Cannot issue HTTPS certificates to client applications."));
            }

            bool found = false;

            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    if (url.Scheme == Utils.UriSchemeHttps &&
                        Utils.AreDomainsEqual(commonName, url.IdnHost))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                return new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("Cannot issue HTTPS certificates to server applications without a matching HTTPS discovery URL."));
            }

            return ServiceResult.Good;
        }

        private static string GetDefaultHttpsDomain(ApplicationRecordDataType application)
        {
            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    if (url.Scheme == Utils.UriSchemeHttps)
                    {
                        return url.IdnHost;
                    }
                }
            }

            throw new ServiceResultException(
                StatusCodes.BadInvalidArgument,
                LocalizedText.From("Cannot issue HTTPS certificates to server applications without a HTTPS discovery URL."));
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
            if (application.DiscoveryUrls.IsEmpty)
            {
                return [];
            }
            var names = new List<string>();
            foreach (string discoveryUrl in application.DiscoveryUrls)
            {
                if (Uri.IsWellFormedUriString(discoveryUrl, UriKind.Absolute))
                {
                    var url = new Uri(discoveryUrl);

                    foreach (string name in names)
                    {
                        if (Utils.AreDomainsEqual(name, url.IdnHost))
                        {
                            url = null;
                            break;
                        }
                    }

                    if (url != null)
                    {
                        names.Add(url.IdnHost);
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
            ArrayOf<string> domainNames,
            string privateKeyFormat,
            string privateKeyPassword,
            ref NodeId requestId)
        {
            // Per OPC 10000-12 §7.6.4 / §7.9.3, CertificateRequestedAuditEvent
            // is emitted after the method outcome is known so Status reflects
            // success or failure. The privateKeyPassword input is redacted to
            // avoid leaking secrets into audit payloads.
            ServiceResult result = ServiceResult.Good;
            Exception? auditException = null;
            NodeId resolvedGroupId = certificateGroupId;
            NodeId resolvedTypeId = certificateTypeId;

            try
            {
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

                if (application == null)
                {
                    return result = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a valid application."));
                }

                if (resolvedGroupId.IsNull)
                {
                    resolvedGroupId = ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                        Server.NamespaceUris);
                }

                if (!m_certificateGroups.TryGetValue(
                    resolvedGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    return result = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The certificateGroup is not supported."));
                }

                if (!resolvedTypeId.IsNull)
                {
                    if (!certificateGroup.CertificateTypes.Contains(certificateType =>
                            Server.TypeTree.IsTypeOf(certificateType, resolvedTypeId)))
                    {
                        return result = new ServiceResult(
                            StatusCodes.BadInvalidArgument,
                            LocalizedText.From("The CertificateType is not supported by the certificateGroup."));
                    }
                }
                else
                {
                    resolvedTypeId = certificateGroup.CertificateTypes[0];
                }

                if (!m_certTypeMap.TryGetValue(resolvedTypeId, out string? certificateTypeNameId))
                {
                    return result = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateType is invalid."));
                }

                if (!string.IsNullOrEmpty(subjectName))
                {
                    subjectName = GetSubjectName(application, certificateGroup, subjectName);
                }
                else
                {
                    var buffer = new StringBuilder();

                    buffer.Append("CN=");

                    if ((certificateGroup.Id.IsNull ||
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

                if (domainNames.Count > 0)
                {
                    foreach (string domainName in domainNames)
                    {
                        if (Uri.CheckHostName(domainName) == UriHostNameType.Unknown)
                        {
                            return result = ServiceResult.Create(
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

                IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
                requestId = m_request.StartNewKeyPairRequest(
                    applicationId,
                    certificateGroup.Configuration.Id!,
                    certificateTypeNameId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    privateKeyPassword?.ToCharArray()!,
                    userIdentity?.DisplayName!);

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

                return result = ServiceResult.Good;
            }
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null && result != null && StatusCode.IsBad(result.StatusCode))
                {
                    auditException = new ServiceResultException(result);
                }

                ArrayOf<Variant> auditInputs =
                [
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    subjectName,
                    domainNames,
                    privateKeyFormat,
                    Diagnostics.AuditEvents.RedactedPrivateKeyPassword
                ];
                Server.ReportCertificateRequestedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    resolvedGroupId,
                    resolvedTypeId,
                    m_logger,
                    auditException);
            }
        }

        private async ValueTask<StartSigningRequestMethodStateResult> OnStartSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            CancellationToken cancellationToken)
        {
            // Per OPC 10000-12 §7.6.4 / §7.9.3, CertificateRequestedAuditEvent
            // is emitted after the method outcome is known so Status reflects
            // success or failure.
            var result = new StartSigningRequestMethodStateResult();
            Exception? auditException = null;
            NodeId resolvedGroupId = certificateGroupId;
            NodeId resolvedTypeId = certificateTypeId;

            try
            {
                AuthorizationHelper.HasAuthorization(
                    context,
                    AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                    applicationId);

                ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

                if (application == null)
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadNotFound,
                        LocalizedText.From("The ApplicationId does not refer to a valid application."));
                    return result;
                }

                if (resolvedGroupId.IsNull)
                {
                    resolvedGroupId = ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
                        Server.NamespaceUris);
                }

                if (!m_certificateGroups.TryGetValue(
                    resolvedGroupId,
                    out ICertificateGroup? certificateGroup))
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateGroupId does not refer to a supported certificateGroup."));
                    return result;
                }

                if (!resolvedTypeId.IsNull)
                {
                    if (!certificateGroup.CertificateTypes.Contains(certificateType =>
                            Server.TypeTree.IsTypeOf(certificateType, resolvedTypeId)))
                    {
                        result.ServiceResult = new ServiceResult(
                            StatusCodes.BadInvalidArgument,
                            LocalizedText.From("The CertificateTypeId is not supported by the certificateGroup."));
                        return result;
                    }
                }
                else
                {
                    resolvedTypeId = certificateGroup.CertificateTypes[0];
                }

                if (!m_certTypeMap.TryGetValue(resolvedTypeId, out string? certificateTypeNameId))
                {
                    result.ServiceResult = new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The CertificateType is invalid."));
                    return result;
                }

                // verify the CSR integrity for the application
                await certificateGroup.VerifySigningRequestAsync(application, certificateRequest, cancellationToken).ConfigureAwait(false);

                // store request in the queue for approval
                IUserIdentity? userIdentity = (context as ISessionSystemContext)?.UserIdentity;
                result.RequestId = m_request.StartSigningRequest(
                    applicationId,
                    certificateGroup.Configuration.Id!,
                    certificateTypeNameId,
                    certificateRequest,
                    userIdentity?.DisplayName!);

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
            catch (Exception ex)
            {
                auditException = ex;
                throw;
            }
            finally
            {
                if (auditException == null &&
                    result.ServiceResult != null &&
                    StatusCode.IsBad(result.ServiceResult.StatusCode))
                {
                    auditException = new ServiceResultException(result.ServiceResult);
                }

                ArrayOf<Variant> auditInputs =
                [
                    applicationId,
                    certificateGroupId,
                    certificateTypeId,
                    certificateRequest
                ];
                Server.ReportCertificateRequestedAuditEvent(
                    context,
                    objectId,
                    method,
                    auditInputs,
                    resolvedGroupId,
                    resolvedTypeId,
                    m_logger,
                    auditException);
            }
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
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            var result = new FinishRequestMethodStateResult();

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);
            if (application == null)
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
                return result;
            }

            CertificateRequestState state = m_request.FinishRequest(
                applicationId,
                requestId,
                out string? certificateGroupId,
                out string? certificateTypeId,
                out ByteString generatedCertificate,
                out ByteString privateKey);

            result.Certificate = generatedCertificate;
            result.PrivateKey = privateKey;

            result.ServiceResult = VerifyApprovedState(state)!;
            if (result.ServiceResult != null)
            {
                return result;
            }

            ICertificateGroup? certificateGroup = null;
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
                    LocalizedText.From("The CertificateGroupId does not refer to a supported certificate group."));
                return result;
            }

            NodeId certificateTypeNodeId = m_certTypeMap
                .Where(
                    pair => pair.Value
                        .Equals(certificateTypeId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .SingleOrDefault();

            if (!certificateTypeNodeId.IsNull &&
                !certificateGroup.CertificateTypes.Contains(certificateType =>
                    Server.TypeTree.IsTypeOf(certificateType, certificateTypeNodeId)))
            {
                result.ServiceResult = new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From("The CertificateTypeId is not supported by the certificateGroup."));
                return result;
            }

            // distinguish cert creation at approval/complete time
            Certificate? certificate = null;
            if (result.Certificate.IsEmpty)
            {
                state = m_request.ReadRequest(
                    applicationId,
                    requestId,
                    out certificateGroupId,
                    out certificateTypeId,
                    out ByteString certificateRequest,
                    out string? subjectName,
                    out string[]? domainNames,
                    out string? privateKeyFormat,
                    out ReadOnlySpan<char> privateKeyPassword);

                result.ServiceResult = VerifyApprovedState(state)!;
                if (result.ServiceResult != null)
                {
                    return result;
                }

                if (!certificateRequest.IsEmpty)
                {
                    try
                    {
                        certificate = await certificateGroup.SigningRequestAsync(
                            application,
                            certificateTypeNodeId,
                            GetDefaultDomainNames(application),
                            certificateRequest,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            "Error Generating Certificate={0}\nApplicationId={1}\nApplicationUri={2}\nApplicationName={3}",
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri!,
                            application.ApplicationNames[0].Text!);
                        return result;
                    }
                }
                else
                {
                    X509Certificate2KeyPair? newKeyPair = null;
                    try
                    {
                        newKeyPair = await certificateGroup.NewKeyPairRequestAsync(
                            application,
                            certificateTypeNodeId,
                            subjectName!,
                            domainNames!,
                            privateKeyFormat!,
                            privateKeyPassword.ToArray(),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        result.ServiceResult = ServiceResult.Create(
                            StatusCodes.BadConfigurationError,
                            "Error Generating New Key Pair Certificate={0}\nApplicationId={1}\nApplicationUri={2}",
                            e.Message,
                            applicationId.ToString(),
                            application.ApplicationUri!);
                        return result;
                    }

                    certificate = newKeyPair.Certificate;
                    result.PrivateKey = newKeyPair.PrivateKey;
                }

                result.Certificate = certificate.RawData.ToByteString();
            }
            else
            {
                certificate = Certificate.FromRawData(result.Certificate);
            }

            // Per OPC 10000-12 §7.6.6 FinishRequest returns the chain of
            // issuer certificates so the application can validate the new
            // certificate without depending on its local issuer store. The
            // chain is built from the in-memory CertificateGroup state to
            // avoid contending with concurrent SigningRequestAsync /
            // NewKeyPairRequestAsync writes against the AuthoritiesStore.
            try
            {
                result.IssuerCertificates = BuildIssuerCertificateChain(
                    certificate,
                    certificateGroup,
                    certificateTypeNodeId);

                // store new app certificate
                var certificateStoreIdentifier = new CertificateStoreIdentifier(
                    m_globalDiscoveryServerConfiguration.ApplicationCertificatesStorePath!);
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

                // Per OPC 10000-12 §7.6.6 the FinishRequest method takes
                // only (applicationId, requestId) as input. The previous
                // implementation included the returned PrivateKey in the audit
                // payload, which would leak the secret. Only true input
                // arguments are recorded; if a private key needs to be reflected
                // in audit it must be passed via the redacted placeholder.
                ArrayOf<Variant> inputArguments = [applicationId, requestId];
                Server.ReportCertificateDeliveredAuditEvent(context, objectId, method, inputArguments, m_logger);

                result.ServiceResult = ServiceResult.Good;
                return result;
            }
            finally
            {
                // Dispose the local owning handle even if any of the store /
                // database / audit operations above throw (the certificate's
                // raw data was already copied into result.Certificate).
                certificate?.Dispose();
            }
        }

        public ServiceResult OnGetCertificateGroups(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId applicationId,
            ref ArrayOf<NodeId> certificateGroupIds)
        {
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            var certificateGroupIdList = new List<NodeId>();
            foreach (KeyValuePair<NodeId, ICertificateGroup> certificateGroup in m_certificateGroups)
            {
                certificateGroupIdList.Add(certificateGroup.Key);
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
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            if (certificateGroupId.IsNull)
            {
                certificateGroupId = m_defaultApplicationGroupId;
            }

            trustListId = GetTrustListId(certificateGroupId);

            if (trustListId.IsNull)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The CertificateGroupId does not refer to a group that is valid for the application."));
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
            // Per OPC 10000-12 §7.6.12 GetCertificateStatus shall be called from
            // a Client with the CertificateAuthorityAdmin Role, the
            // ApplicationSelfAdmin Privilege, or the ApplicationAdmin Privilege.
            AuthorizationHelper.HasAuthorization(
                context,
                AuthorizationHelper.CertificateAuthorityAdminOrSelfAdminOrAppAdmin,
                applicationId);

            ApplicationRecordDataType? application = m_database.GetApplication(applicationId);

            if (application == null)
            {
                return new ServiceResult(
                    StatusCodes.BadNotFound,
                    LocalizedText.From("The ApplicationId does not refer to a valid application."));
            }

            if (certificateGroupId.IsNull)
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
                    LocalizedText.From(
                        "The CertificateGroupId and CertificateTypeId do not refer to a group and type that is valid for the application."));
            }

            updateRequired = (bool)updateRequiredResult;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            // TBD
            return base.DeleteAddressSpaceAsync(cancellationToken);
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // quickly exclude nodes that are not in the namespace.
            if (!IsNodeIdInNamespace(nodeId))
            {
                return new ValueTask<NodeHandle>();
            }

            // check cache (the cache is used because the same node id can appear many times in a single request).
            if (cache != null && cache.TryGetValue(nodeId, out NodeState? node))
            {
                return new ValueTask<NodeHandle>(new NodeHandle(nodeId, node));
            }

            // look up predefined node.
            if (PredefinedNodes.TryGetValue(nodeId, out node))
            {
                var handle = new NodeHandle(nodeId, node);

                cache?.Add(nodeId, node);

                return new ValueTask<NodeHandle>(handle);
            }

            // node not found.
            return new ValueTask<NodeHandle>();
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override async ValueTask<NodeState> ValidateNodeAsync(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null!;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // lookup in operation cache.
            NodeState? target = await FindNodeInCacheAsync(context, handle, cache, cancellationToken).ConfigureAwait(false);

            if (target != null)
            {
                handle.Node = target;
                handle.Validated = true;
                return handle.Node;
            }

            // put root into operation cache.
            cache?[handle.NodeId] = target!;

            handle.Node = target!;
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

        protected async ValueTask SetCertificateGroupNodesAsync(ICertificateGroup certificateGroup)
        {
            certificateGroup.DefaultTrustList = null!;
            string groupId = certificateGroup.Configuration.Id!;

            if (string.Equals(groupId, "DefaultHttpsGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultHttpsGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultHttpsGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else if (string.Equals(groupId, "DefaultUserTokenGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultUserTokenGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultUserTokenGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else if (string.Equals(groupId, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(groupId, "DefaultApplicationGroup", StringComparison.OrdinalIgnoreCase))
            {
                certificateGroup.Id = m_defaultApplicationGroupId;
                certificateGroup.DefaultTrustList = FindPredefinedNode<TrustListState>(
                    ExpandedNodeId.ToNodeId(
                        ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup_TrustList,
                        Server.NamespaceUris
                    ))!;
            }
            else
            {
                // Create a new custom certificate group node in the address space
                // for any group whose Id does not match one of the three predefined groups.
                CertificateGroupFolderState certGroupsFolder = FindPredefinedNode<CertificateGroupFolderState>(
                    ExpandedNodeId.ToNodeId(ObjectIds.Directory_CertificateGroups, Server.NamespaceUris)) ??
                    throw new ServiceResultException(
                        StatusCodes.BadInternalError,
                        "CertificateGroups folder node was not found in the address space.");

                var customGroupNode = new CertificateGroupState(certGroupsFolder);
                customGroupNode.Create(
                    SystemContext,
                    NodeId.Null,
                    new QualifiedName(groupId, NamespaceIndex),
                    new LocalizedText(groupId),
                    true);

                // Read back the NodeId assigned by Create (assignNodeIds: true reassigns the root id).
                certificateGroup.Id = customGroupNode.NodeId;

                customGroupNode.CertificateTypes?.Value = [.. certificateGroup.CertificateTypes];

                certGroupsFolder.AddChild(customGroupNode);
                await AddPredefinedNodeAsync(SystemContext, customGroupNode).ConfigureAwait(false);

                certificateGroup.DefaultTrustList = customGroupNode.TrustList!;

                m_logger.LogInformation(
                    "Created custom certificate group node: {Id} with NodeId {NodeId}",
                    groupId,
                    certificateGroup.Id);
            }

            certificateGroup.DefaultTrustList?.Handle = new TrustList(
                    certificateGroup.DefaultTrustList,
                    new CertificateStoreIdentifier(certificateGroup.Configuration.TrustedListPath!),
                    new CertificateStoreIdentifier(certificateGroup.Configuration.IssuerListPath!),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    new TrustList.SecureAccess(HasTrustListAccess),
                    Server.Telemetry);
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

        private static ServiceResult? VerifyApprovedState(CertificateRequestState state)
        {
            switch (state)
            {
                case CertificateRequestState.New:
                    return new ServiceResult(
                        StatusCodes.BadNothingToDo,
                        LocalizedText.From("The request has not been approved by the administrator."));
                case CertificateRequestState.Rejected:
                    return new ServiceResult(
                        StatusCodes.BadRequestNotAllowed,
                        LocalizedText.From("The request has been rejected by the administrator."));
                case CertificateRequestState.Accepted:
                    return new ServiceResult(
                        StatusCodes.BadInvalidArgument,
                        LocalizedText.From("The request has already been accepted by the application."));
                case CertificateRequestState.Approved:
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected CertificateRequestState {state}");
            }
        }

        private ServiceResult OnGetServiceDescription(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref string serviceUri,
            ref ByteString serviceCertificate,
            ref ArrayOf<UserTokenPolicy> userTokenPolicies)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context);

            // Read the ServiceUri and ServiceCertificate from the parent
            // AuthorizationServiceState instance; these are populated from
            // the predefined nodeset.
            if (method.Parent is AuthorizationServiceState parentService)
            {
                serviceUri = parentService.ServiceUri?.Value ?? string.Empty;
                serviceCertificate = parentService.ServiceCertificate?.Value ?? default;
                userTokenPolicies = parentService.UserTokenPolicies?.Value ?? default;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the legacy Part 12 <c>RequestAccessToken</c> method.
        /// Prefer <see cref="OnStartRequestTokenAsync"/> and
        /// <see cref="OnFinishRequestTokenAsync"/> for v1.05 clients.
        /// </summary>
        private async ValueTask<RequestAccessTokenMethodStateResult> OnRequestAccessTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            UserIdentityToken identityToken,
            string resourceId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new RequestAccessTokenMethodStateResult();

            ArrayOf<Variant> auditInputs = [Variant.FromStructure(identityToken), resourceId];
            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "RequestAccessToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                IUserIdentity? callerIdentity = (context as ISessionSystemContext)?.UserIdentity;
#pragma warning disable CS0618 // Legacy wire method is intentionally kept functional.
                result.AccessToken = provider is AuthorizationServiceManager manager
                    ? await manager.RequestAccessTokenAsync(
                        identityToken, resourceId, callerIdentity, cancellationToken).ConfigureAwait(false)
                    : await provider.RequestAccessTokenAsync(
                        identityToken, resourceId, cancellationToken).ConfigureAwait(false);
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<StartRequestTokenMethodStateResult> OnStartRequestTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string policyId,
            ByteString requestorData,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new StartRequestTokenMethodStateResult();
            ArrayOf<Variant> auditInputs = [resourceId, policyId, requestorData];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "StartRequestToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                IUserIdentity? callerIdentity = (context as ISessionSystemContext)?.UserIdentity;

                (ByteString serviceData, Guid requestId) = provider is AuthorizationServiceManager manager
                    ? await manager.StartRequestTokenAsync(
                        resourceId,
                        policyId,
                        requestorData,
                        callerIdentity,
                        cancellationToken).ConfigureAwait(false)
                    : await provider.StartRequestTokenAsync(
                        resourceId,
                        policyId,
                        requestorData,
                        cancellationToken).ConfigureAwait(false);

                result.ServiceData = serviceData;
                result.RequestId = requestId;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<FinishRequestTokenMethodStateResult> OnFinishRequestTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Uuid requestId,
            ArrayOf<string> requestedRoles,
            UserIdentityToken userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new FinishRequestTokenMethodStateResult();
            ArrayOf<Variant> auditInputs =
            [
                requestId,
                requestedRoles,
                Variant.FromStructure(userIdentityToken),
                Variant.FromStructure(userTokenSignature)
            ];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "FinishRequestToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                AccessTokenResult atr = await provider.FinishRequestTokenAsync(
                    requestId, requestedRoles, userIdentityToken, userTokenSignature, cancellationToken)
                    .ConfigureAwait(false);

                result.AccessToken = atr.AccessToken;
                result.AccessTokenExpiryTime = atr.AccessTokenExpiryTime;
                result.RefreshToken = atr.RefreshToken ?? string.Empty;
                result.RefreshTokenExpiryTime = atr.RefreshTokenExpiryTime;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<RefreshTokenMethodStateResult> OnRefreshTokenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string resourceId,
            string currentRefreshToken,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            var result = new RefreshTokenMethodStateResult();
            ArrayOf<Variant> auditInputs = [resourceId];

            IAccessTokenProvider provider = GetAccessTokenProvider(
                context,
                objectId,
                method,
                auditInputs,
                "RefreshToken is not implemented by this GDS. Set AccessTokenProvider to enable.");

            try
            {
                AccessTokenResult atr = await provider
                    .RefreshTokenAsync(resourceId, currentRefreshToken, cancellationToken)
                    .ConfigureAwait(false);

                result.AccessToken = atr.AccessToken;
                result.AccessTokenExpiryTime = atr.AccessTokenExpiryTime;
                result.NewRefreshToken = atr.RefreshToken ?? string.Empty;
                result.NewRefreshTokenExpiryTime = atr.RefreshTokenExpiryTime;
            }
            catch (Exception ex)
            {
                Server.ReportAccessTokenIssuedAuditEvent(
                    context, objectId, method, auditInputs, m_logger, ex);
                throw;
            }

            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private IAccessTokenProvider GetAccessTokenProvider(
            ISystemContext context,
            NodeId objectId,
            MethodState method,
            ArrayOf<Variant> auditInputs,
            string message)
        {
            if (AccessTokenProvider != null)
            {
                return AccessTokenProvider;
            }

            var ex = new ServiceResultException(StatusCodes.BadNotSupported, message);
            Server.ReportAccessTokenIssuedAuditEvent(
                context, objectId, method, auditInputs, m_logger, ex);
            throw ex;
        }

        private async ValueTask<KeyCredentialStartRequestMethodStateResult> OnKeyCredentialStartRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string applicationUri,
            ByteString publicKey,
            string securityPolicyUri,
            ArrayOf<NodeId> requestedRoles,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            m_logger.LogInformation("OnKeyCredentialStartRequest: {ApplicationUri}", applicationUri);

            NodeId requestId = await KeyCredentialRequestStore.StartRequestAsync(
                applicationUri,
                publicKey,
                securityPolicyUri,
                requestedRoles,
                cancellationToken).ConfigureAwait(false);

            ArrayOf<Variant> auditInputs = [applicationUri, publicKey, securityPolicyUri, requestedRoles];
            Server.ReportKeyCredentialRequestedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            return new KeyCredentialStartRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                RequestId = requestId
            };
        }

        private async ValueTask<KeyCredentialFinishRequestMethodStateResult> OnKeyCredentialFinishRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId requestId,
            bool cancelRequest,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            m_logger.LogInformation("OnKeyCredentialFinishRequest: {RequestId}", requestId);

            FinishKeyCredentialRequestResult finished = await KeyCredentialRequestStore.FinishRequestAsync(
                requestId,
                cancelRequest,
                cancellationToken).ConfigureAwait(false);

            var result = new KeyCredentialFinishRequestMethodStateResult
            {
                CredentialId = finished.CredentialId ?? string.Empty,
                CredentialSecret = finished.CredentialSecret,
                CertificateThumbprint = finished.CertificateThumbprint ?? string.Empty,
                SecurityPolicyUri = finished.SecurityPolicyUri ?? string.Empty,
                GrantedRoles = finished.GrantedRoles
            };

            if (finished.State == KeyCredentialRequestState.New)
            {
                result.ServiceResult = new ServiceResult(StatusCodes.BadNothingToDo);
                return result;
            }

            ArrayOf<Variant> auditInputs = [requestId, cancelRequest];
            Server.ReportKeyCredentialDeliveredAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            result.ServiceResult = ServiceResult.Good;
            return result;
        }

        private async ValueTask<KeyCredentialRevokeMethodStateResult> OnKeyCredentialRevokeAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string credentialId,
            CancellationToken cancellationToken)
        {
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);

            m_logger.LogInformation("OnKeyCredentialRevoke: {CredentialId}", credentialId);

            await KeyCredentialRequestStore.RevokeAsync(credentialId, cancellationToken).ConfigureAwait(false);

            ArrayOf<Variant> auditInputs = [credentialId];
            Server.ReportKeyCredentialRevokedAuditEvent(
                context, objectId, method, auditInputs, m_logger);

            return new KeyCredentialRevokeMethodStateResult { ServiceResult = ServiceResult.Good };
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (ICertificateGroup certificateGroup in m_certificateGroups.Values)
                {
                    (certificateGroup as IDisposable)?.Dispose();
                }

                m_certificateGroups.Clear();
            }

            base.Dispose(disposing);
        }

        private readonly bool m_autoApprove;
        private uint m_nextNodeId;
        private readonly ApplicationConfiguration m_configuration;
        private readonly GlobalDiscoveryServerConfiguration m_globalDiscoveryServerConfiguration;
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_request;
        private readonly ICertificateGroup m_certificateGroupFactory;
        private readonly Dictionary<NodeId, ICertificateGroup> m_certificateGroups;
        private Dictionary<NodeId, string> m_certTypeMap = [];
        private IKeyCredentialRequestStore? m_keyCredentialStore;

        /// <summary>
        /// Gets or sets the key-credential request store used by
        /// KeyCredentialService handler methods. When <c>null</c> an
        /// in-memory store is created lazily on first use.
        /// </summary>
        public IKeyCredentialRequestStore KeyCredentialRequestStore
        {
            get => m_keyCredentialStore ??= new InMemoryKeyCredentialRequestStore();
            set => m_keyCredentialStore = value;
        }

        /// <summary>
        /// Gets or sets the access-token provider used by
        /// AuthorizationService handler methods. When <c>null</c> the
        /// built-in handlers return <c>Bad_NotSupported</c>.
        /// </summary>
        public IAccessTokenProvider? AccessTokenProvider { get; set; }
    }
}
