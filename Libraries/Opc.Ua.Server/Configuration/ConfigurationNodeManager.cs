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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
#if !NET9_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// The Server Configuration Node Manager.
    /// </summary>
    public class ConfigurationNodeManager : DiagnosticsNodeManager, IConfigurationNodeManager
    {
        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : this(server, configuration, server.Telemetry.CreateLogger<ConfigurationNodeManager>(), timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager.
        /// </summary>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger)
            : this(server, configuration, logger, timeProvider: null)
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager with an
        /// explicit <see cref="TimeProvider"/>.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> used by the certificate-alarm
        /// timer and by the "apply changes" delay. When <c>null</c>, the time
        /// provider exposed by the server (via <see cref="ITimeProviderProvider"/>)
        /// is used, falling back to <see cref="TimeProvider.System"/>.
        /// </param>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            TimeProvider? timeProvider)
            : base(server, configuration, logger, timeProvider)
        {
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            string? rejectedStorePath = configuration.SecurityConfiguration.RejectedCertificateStore?
                .StorePath;
            if (!string.IsNullOrEmpty(rejectedStorePath))
            {
                m_rejectedStore = new CertificateStoreIdentifier(rejectedStorePath!);
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
                    configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath!
                ),
                TrustedStore = new CertificateStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath!)
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
                        configuration.SecurityConfiguration.UserIssuerCertificates.StorePath!
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedUserCertificates.StorePath!)
                };

                m_certificateGroups.Add(defaultUserGroup);
            }
            ServerCertificateGroup? defaultHttpsGroup = null;
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
                        configuration.SecurityConfiguration.HttpsIssuerCertificates.StorePath!
                    ),
                    TrustedStore = new CertificateStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedHttpsCertificates.StorePath!)
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
                defaultApplicationGroup.ApplicationCertificates =
                    defaultApplicationGroup.ApplicationCertificates.AddItem(cert);

                if (cert.CertificateType == ObjectTypeIds.HttpsCertificateType &&
                    defaultHttpsGroup != null)
                {
                    defaultHttpsGroup.CertificateTypes =
                    [
                        .. defaultHttpsGroup.CertificateTypes,
                        .. new NodeId[] { cert.CertificateType }
                    ];
                    defaultHttpsGroup.ApplicationCertificates =
                        defaultHttpsGroup.ApplicationCertificates.AddItem(cert);
                }
            }
        }

        /// <summary>
        /// Replaces the generic node with a node specific to the model.
        /// </summary>
        protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
            ISystemContext context,
            NodeState predefinedNode,
            CancellationToken cancellationToken = default)
        {
            if (predefinedNode is BaseObjectState passiveNode)
            {
                NodeId typeId = passiveNode.TypeDefinitionId;
                if (IsNodeIdInNamespace(typeId) && typeId.TryGetValue(out uint numericId))
                {
                    switch (numericId)
                    {
                        case ObjectTypes.ServerConfigurationType:
                        {
                            var activeNode = new ServerConfigurationState(passiveNode.Parent);

                            // Optional ServerConfigurationType methods this
                            // SDK wires in CreateServerConfiguration but that
                            // are no longer emitted by the singleton factory
                            // (Optional per Part 12). Use the idempotent
                            // generated Add{Method} helpers so the typed
                            // slot is initialised with the type-level
                            // factory (BrowseName, InputArguments, etc.)
                            // before Create() copies the loaded passive
                            // node into the active subtree. The new
                            // Add{Method}(context, nodeId?) chains via the
                            // owner state for fluent usage.
                            activeNode
                                .AddGetCertificates(context)
                                .AddCreateSelfSignedCertificate(context);

                            activeNode.Create(context, passiveNode);

                            m_serverConfigurationNode = activeNode;

                            // replace the node in the parent.
                            if (passiveNode.Parent != null)
                            {
                                passiveNode.Parent.ReplaceChild(context, activeNode);
                            }
                            else
                            {
                                NodeState? serverNode = await Server.NodeManager.FindNodeInAddressSpaceAsync(ObjectIds.Server, cancellationToken)
                                    .ConfigureAwait(false);
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
                                    activeNode.DefaultHttpsGroup == null ||
                                    activeNode.DefaultHttpsGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultHttpsGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    activeNode.DefaultUserTokenGroup == null ||
                                    activeNode.DefaultUserTokenGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultUserTokenGroup = null;
                            }
                            if (m_certificateGroups.All(group =>
                                    activeNode.DefaultApplicationGroup == null ||
                                    activeNode.DefaultApplicationGroup.BrowseName != group.BrowseName))
                            {
                                activeNode.DefaultApplicationGroup = null;
                            }

                            // replace the node in the parent.
                            passiveNode.Parent?.ReplaceChild(context, activeNode);
                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupType:
                        {
                            ServerCertificateGroup? result = m_certificateGroups
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
                        case ObjectTypes.UserManagementType:
                        {
                            if (passiveNode is UserManagementState)
                            {
                                break;
                            }
                            var activeNode = new UserManagementState(passiveNode.Parent);
                            activeNode.Create(context, passiveNode);
                            passiveNode.Parent?.ReplaceChild(context, activeNode);
                            return activeNode;
                        }
                    }
                }
            }
            return await base.AddBehaviourToPredefinedNodeAsync(context, predefinedNode, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is NamespacesState serverNamespacesNode)
                {
                    serverNamespacesNode.StateChanged -= ServerNamespacesChanged;
                }

                foreach (NodeState node in PredefinedNodes.Values)
                {
                    if (node is NamespaceMetadataState metadataState)
                    {
                        metadataState.StateChanged -= OnNamespaceChildrenChanged;
                        metadataState.DefaultRolePermissions?.StateChanged -= OnNamespaceDefaultPermissionsChanged;

                        metadataState.DefaultUserRolePermissions?.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                    }
                }

                // m_serverConfigurationNode is owned by the address space, not by this manager
                m_serverConfigurationNode = null;
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = null;

                foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
                {
                    DisposePendingRotationState(certificateGroup);
                }

                StopAlarmMonitoring();
            }

            base.Dispose(disposing);
        }

        ///<inheritdoc/>
        public void CreateServerConfiguration(
            ServerSystemContext systemContext,
            ApplicationConfiguration configuration)
        {
            // setup server configuration node
            ServerConfigurationState configNode = m_serverConfigurationNode!;
            configNode.ServerCapabilities!.Value =
            [
                .. configuration.ServerConfiguration!.ServerCapabilities
            ];
            configNode.ServerCapabilities.ValueRank = ValueRanks.OneDimension;
            configNode.SupportedPrivateKeyFormats!.Value =
            [
                .. configuration.ServerConfiguration.SupportedPrivateKeyFormats
            ];
            configNode.SupportedPrivateKeyFormats.ValueRank = ValueRanks
                .OneDimension;
            configNode.MaxTrustListSize!.Value = (uint)configuration
                .ServerConfiguration
                .MaxTrustListSize;
            configNode.MulticastDnsEnabled!.Value = configuration.ServerConfiguration
                .MultiCastDnsEnabled;

            configNode.UpdateCertificate!.OnCallAsync
                = new UpdateCertificateMethodStateMethodAsyncCallHandler(
                UpdateCertificateAsync);
            configNode.CreateSigningRequest!.OnCallAsync =
                new CreateSigningRequestMethodStateMethodAsyncCallHandler(CreateSigningRequestAsync);
            configNode.CreateSelfSignedCertificate?.OnCallAsync =
                    new CreateSelfSignedCertificateMethodStateMethodAsyncCallHandler(
                        CreateSelfSignedCertificateAsync);
            configNode.ApplyChanges!.OnCallMethod2
                = new GenericMethodCalledEventHandler2(ApplyChanges);
            configNode.GetRejectedList!.OnCall
                = new GetRejectedListMethodStateMethodCallHandler(
                GetRejectedList);
            configNode.GetCertificates!.OnCall
                = new GetCertificatesMethodStateMethodCallHandler(
                GetCertificates);
            configNode.ClearChangeMasks(systemContext, true);

            // setup certificate group trust list handlers
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                certGroup.Node!.CertificateTypes!.Value = certGroup.CertificateTypes;
                certGroup.Node!.TrustList!.Handle = new TrustList(
                    certGroup.Node.TrustList,
                    certGroup.TrustedStore,
                    certGroup.IssuerStore,
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    new TrustList.SecureAccess(HasApplicationSecureAdminAccess),
                    Server.Telemetry,
                    m_configuration.ServerConfiguration!.MaxTrustListSize);
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // OPC 10000-12 §7.8.3: populate the optional alarm property
            // values (ExpirationDate, TrustListId, LastUpdateTime) from
            // the current certificate and CRL state. Active-state
            // transitions (SetActiveState) are not performed during
            // CreateAddressSpace to avoid event-notification issues before
            // the subscription infrastructure is ready.
            EvaluateCertificateAlarms(systemContext);

            // find ServerNamespaces node and subscribe to StateChanged

            if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                is NamespacesState serverNamespacesNode)
            {
                serverNamespacesNode.StateChanged += ServerNamespacesChanged;

                IList<BaseInstanceState> children = [];
                serverNamespacesNode.GetChildren(systemContext, children);

                foreach (BaseInstanceState child in children)
                {
                    if (child is NamespaceMetadataState metadataState)
                    {
                        SubscribeToNamespaceDefaultPermissions(metadataState);
                    }
                }
            }

            // Bind ServerConfiguration.UserManagement (i=24290) per Part 18 §5
            // if an IUserManagement was injected via IServerInternal.SetUserManagement.
            if (Server is IServerInternal serverInternal && serverInternal.UserManagement != null)
            {
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = UserManagement.UserManagementBinding.Bind(
                    this,
                    serverInternal.UserManagement,
                    serverInternal.SessionManager);
            }
            else
            {
                m_userManagementBinding?.Dispose();
                m_userManagementBinding = null;
                DeleteNodeAsync(systemContext, new NodeId(Objects.UserManagement))
                    .AsTask().GetAwaiter().GetResult();
            }
        }

        ///<inheritdoc/>
        public async ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            if (namespaceUri == null)
            {
                return null;
            }

            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStates.TryGetValue(
                    namespaceUri,
                    out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            NamespaceMetadataState? namespaceMetadataState = await FindNamespaceMetadataStateAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            lock (m_namespaceMetadataStatesLock)
            {
                // remember the result for faster access.
                m_namespaceMetadataStates[namespaceUri] = namespaceMetadataState!;
            }

            return namespaceMetadataState;
        }

        ///<inheritdoc/>
        public async ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(ushort namespaceIndex, CancellationToken cancellationToken = default)
        {
            lock (m_namespaceMetadataStatesLock)
            {
                if (m_namespaceMetadataStatesByIndex.TryGetValue(
                    namespaceIndex,
                    out NamespaceMetadataState? value))
                {
                    return value;
                }
            }

            string? namespaceUri = Server.NamespaceUris.GetString(namespaceIndex);
            NamespaceMetadataState? namespaceMetadataState = await GetNamespaceMetadataStateAsync(namespaceUri!, cancellationToken).ConfigureAwait(false);

            lock (m_namespaceMetadataStatesLock)
            {
                m_namespaceMetadataStatesByIndex[namespaceIndex] = namespaceMetadataState!;
            }

            return namespaceMetadataState!;
        }

        /// <inheritdoc/>
        public async ValueTask<NamespaceMetadataState> CreateNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            NamespaceMetadataState? namespaceMetadataState = await FindNamespaceMetadataStateAsync(
                namespaceUri, cancellationToken).ConfigureAwait(false);

            if (namespaceMetadataState == null)
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
                    is not NamespacesState serverNamespacesNode)
                {
                    m_logger.LogError(
                        "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.",
                        namespaceUri);
                    return null!;
                }

                // create the NamespaceMetadata node
                namespaceMetadataState = new NamespaceMetadataState(serverNamespacesNode)
                {
                    BrowseName = new QualifiedName(namespaceUri, NamespaceIndex)
                };
                namespaceMetadataState.Create(
                    SystemContext,
                    default,
                    namespaceMetadataState.BrowseName,
                    default,
                    true);
                namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState!.NamespaceUri!.Value = namespaceUri;
                namespaceMetadataState.AddDefaultRolePermissions(SystemContext);
                namespaceMetadataState.AddDefaultUserRolePermissions(SystemContext);

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                serverNamespacesNode.ClearChangeMasks(SystemContext, true);
                await AddPredefinedNodeAsync(SystemContext, namespaceMetadataState, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Subscribe to the default permission properties so that any future changes
            // trigger a DefaultPermissionsChanged notification to allow caches to be invalidated.
            SubscribeToNamespaceDefaultPermissions(namespaceMetadataState);

            return namespaceMetadataState;
        }

        /// <inheritdoc/>
        public async ValueTask BindKeyCredentialPushAsync(
            KeyCredentialPushSubject subject,
            CancellationToken cancellationToken = default)
        {
            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            NodeState? node = FindPredefinedNode<NodeState>(
                KeyCredentialPushSubject.StandardConfigurationFolderNodeId) ?? await Server.NodeManager
                    .FindNodeInAddressSpaceAsync(KeyCredentialPushSubject.StandardConfigurationFolderNodeId, cancellationToken)
                    .ConfigureAwait(false);

            if (node is not KeyCredentialConfigurationFolderState folder)
            {
                if (node is not BaseObjectState passiveNode)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown,
                        "The standard KeyCredentialConfiguration folder is not present.");
                }

                folder = new KeyCredentialConfigurationFolderState(passiveNode.Parent);
                folder.Create(SystemContext, passiveNode);
                passiveNode.Parent?.ReplaceChild(SystemContext, folder);
                await AddPredefinedNodeAsync(SystemContext, folder, cancellationToken)
                    .ConfigureAwait(false);
            }

            await subject.BindAsync(
                    folder,
                    SystemContext,
                    (state, ct) => AddPredefinedNodeAsync(SystemContext, state, ct),
                    async (state, ct) => await DeleteNodeAsync(SystemContext, state.NodeId, ct).ConfigureAwait(false),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void HasApplicationSecureAdminAccess(ISystemContext context)
        {
            HasApplicationSecureAdminAccess(context, null!);
        }

        /// <inheritdoc/>
        public void HasApplicationSecureAdminAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext operationContext })
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
            ByteString certificate,
            ArrayOf<ByteString> issuerCertificates,
            string? privateKeyFormat,
            ByteString privateKey,
            CancellationToken ct)
        {
            bool applyChangesRequired = false;
            HasApplicationSecureAdminAccess(context);

            // OPC 10000-12 §7.10.3: the private key is sensitive material;
            // it must not be persisted into the
            // CertificateUpdateRequested / CertificateUpdated audit events.
            // The audit payload still reflects the public-key certificate,
            // issuer chain and key format so administrators can correlate
            // the request without exposing the secret.
            ArrayOf<Variant> inputArguments =
            [
                certificateGroupId,
                certificateTypeId,
                certificate,
                issuerCertificates,
                privateKeyFormat!,
                AuditEvents.RedactedPrivateKey
            ];

            Server.ReportCertificateUpdateRequestedAuditEvent(
                context,
                objectId,
                method,
                inputArguments,
                m_logger);
            Certificate? newCert = null;
            try
            {
                if (certificate.IsEmpty)
                {
                    throw new ArgumentNullException(nameof(certificate));
                }

                privateKeyFormat = privateKeyFormat?.ToUpperInvariant();
                if (privateKeyFormat is not null and not "PEM" and not "PFX" and not "")
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        $"The private key format {privateKeyFormat} is not supported.");
                }

                ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                    certificateGroupId,
                    certificateTypeId)!;
                ResetPendingUpdateCertificate(certificateGroup);

                try
                {
                    newCert = Certificate.FromRawData(certificate);
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
                CertificateIdentifier existingCertIdentifier;
                CertificateIdentifier? subjectMatch = certificateGroup.ApplicationCertificates
                    .ToList()
                    .FirstOrDefault(cert =>
                        X509Utils.CompareDistinguishedName(cert.SubjectName!, newCert.Subject) &&
                        cert.CertificateType == certificateTypeId);

                if (subjectMatch != null)
                {
                    existingCertIdentifier = subjectMatch;
                }
                else if (m_configuration.CertificateManager is ICertificateRegistry registryFallback)
                {
                    // Subject changed mid-rotation: use the manager registry's
                    // currently-registered cert for this type to identify the
                    // configured identifier (matches by certificate type).
                    CertificateEntry currentEntry = registryFallback
                        .GetApplicationCertificate(certificateTypeId) ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument,
                            "No existing certificate found for the specified certificate type and subject name.");

                    existingCertIdentifier = certificateGroup.ApplicationCertificates
                        .ToList()
                        .FirstOrDefault(cert => cert.CertificateType == certificateTypeId) ??
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument,
                            "No existing certificate found for the specified certificate type and subject name.");
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "No existing certificate found for the specified certificate type and subject name.");
                }

                var newIssuerCollection = new CertificateCollection();

                try
                {
                    // build issuer chain
                    foreach (ByteString issuerRawCert in issuerCertificates)
                    {
                        newIssuerCollection.Add(Certificate.FromRawData(issuerRawCert));
                    }
                }
                catch
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Issuer certificate data is invalid.");
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
                        // Verify chain integrity: build a chain rooted at any of the provided
                        // issuer certificates and ensure all signatures are valid. We do not
                        // consult the application's trust list here — the caller is supplying
                        // the issuer chain as part of the UpdateCertificate input.
                        var chainPolicy = new X509ChainPolicy
                        {
                            RevocationFlag = X509RevocationFlag.EntireChain,
                            RevocationMode = X509RevocationMode.NoCheck,
                            VerificationFlags =
                                X509VerificationFlags.AllowUnknownCertificateAuthority |
                                X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                                X509VerificationFlags.IgnoreEndRevocationUnknown |
                                X509VerificationFlags.IgnoreRootRevocationUnknown,
#if NET5_0_OR_GREATER
                            DisableCertificateDownloads = true,
#endif
                            UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1)
                        };

                        var extraIssuers = new List<X509Certificate2>(newIssuerCollection.Count);
                        foreach (Certificate issuerCert in newIssuerCollection)
                        {
                            X509Certificate2 issuerX509 = issuerCert.AsX509Certificate2();
                            extraIssuers.Add(issuerX509);
                            chainPolicy.ExtraStore.Add(issuerX509);
                        }

                        try
                        {
                            using var chain = new X509Chain { ChainPolicy = chainPolicy };
                            using X509Certificate2 newCertX509 = newCert.AsX509Certificate2();
                            chain.Build(newCertX509);

                            foreach (X509ChainStatus chainStatus in chain.ChainStatus ?? [])
                            {
                                if (chainStatus.Status is X509ChainStatusFlags.NoError or
                                    X509ChainStatusFlags.UntrustedRoot)
                                {
                                    continue;
                                }
                                if (chainStatus.Status is X509ChainStatusFlags.NotSignatureValid or
                                    X509ChainStatusFlags.PartialChain or
                                    X509ChainStatusFlags.NotValidForUsage or
                                    X509ChainStatusFlags.InvalidBasicConstraints)
                                {
                                    throw new ServiceResultException(
                                        StatusCodes.BadSecurityChecksFailed,
                                        Utils.Format(
                                            "Certificate chain validation failed. {0}: {1}",
                                            chainStatus.Status,
                                            chainStatus.StatusInformation));
                                }
                            }

                            if (newIssuerCollection.Count + 1 != chain.ChainElements.Count)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadSecurityChecksFailed,
                                    "The supplied issuer chain is incomplete.");
                            }
                        }
                        finally
                        {
                            foreach (X509Certificate2 extra in extraIssuers)
                            {
                                extra.Dispose();
                            }
                        }
                    }
                    catch (ServiceResultException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            Utils.TraceMasks.Security,
                            ex,
                            "Failed to verify integrity of the new certificate {Certificate} and the issuer list.",
                            newCert);
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to verify integrity of the new certificate and the issuer list.",
                            ex);
                    }
                }

                var updateCertificate = new UpdateCertificateData
                {
                    IssuerCollection = newIssuerCollection,
                    SessionId = (context as ISessionSystemContext)?.SessionId ?? default
                };
                try
                {
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                            for (int attempt = 0; ; attempt++)
                            {
                                Certificate? exportableKey = null;
                                try
                                {
                                    // use the new generated private key if one exists and matches the provided public key
                                    if (certificateGroup.TemporaryApplicationCertificate != null &&
                                        X509Utils.VerifyKeyPair(
                                            newCert,
                                            certificateGroup.TemporaryApplicationCertificate))
                                    {
                                        // CA2000: disposed in the finally block of the surrounding try.
#pragma warning disable CA2000
                                        exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                            certificateGroup.TemporaryApplicationCertificate,
                                            false);
#pragma warning restore CA2000
                                    }
                                    else
                                    {
                                        using Certificate certWithPrivateKey = await CertificateIdentifierResolver
                                            .LoadPrivateKeyAsync(
                                                existingCertIdentifier,
                                                passwordProvider,
                                                m_configuration.ApplicationUri,
                                                Server.Telemetry,
                                                ct)
                                            .ConfigureAwait(false) ??
                                            throw new ServiceResultException(
                                                StatusCodes.BadSecurityChecksFailed,
                                                "A private key was not found");
                                        // CA2000: disposed in the finally block of the surrounding try.
#pragma warning disable CA2000
                                        exportableKey = X509Utils.CreateCopyWithPrivateKey(
                                            certWithPrivateKey,
                                            false);
#pragma warning restore CA2000
                                    }

                                    updateCertificate.CertificateWithPrivateKey =
                                        DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                                            newCert,
                                            exportableKey);
                                    try
                                    {
                                        await UpdateCertificateInternalAsync(
                                            certificateGroup,
                                            existingCertIdentifier,
                                            updateCertificate, ct).ConfigureAwait(false);
                                        break;
                                    }
                                    catch (Exception ex) when (ShouldRetry(attempt, ex))
                                    {
                                        m_logger.LogDebug(
                                            Utils.TraceMasks.Security,
                                            ex,
                                            "Failed to update certificate {Certificate}. Retrying...",
                                            newCert);
                                    }
                                }
                                finally
                                {
                                    exportableKey?.Dispose();
                                }
                            }
                            break;
                        case "PFX":
                            for (int attempt = 0; ; attempt++)
                            {
#if !NET9_0_OR_GREATER
                                // https://github.com/OPCFoundation/UA-.NETStandard/commit/0b24d62b7c2bab2e5ed08e694103d49278e457af
                                // CopyWithPrivateKey apparently does not support ephimeralkeysets on windows
                                bool noEphemeralKeySet = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                                // But it seems to work on .net 9 - and we prefer that over files
                                const bool noEphemeralKeySet = false;
#endif
#pragma warning disable CA2000 // Dispose objects before losing scope
                                using Certificate certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(
                                    privateKey.ToArray(),
                                    passwordProvider?.GetPassword(existingCertIdentifier),
                                    noEphemeralKeySet);
#pragma warning restore CA2000 // Dispose objects before losing scope
                                try
                                {
                                    updateCertificate.CertificateWithPrivateKey =
                                        DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                                            newCert,
                                            certWithPrivateKey);
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.LogDebug(
                                        Utils.TraceMasks.Security,
                                        ex,
                                        "Failed to update certificate {Certificate} with PFX private key. Retrying...",
                                        newCert);
                                }
                            }
                            break;
                        case "PEM":
                            for (int attempt = 0; ; attempt++)
                            {
                                updateCertificate.CertificateWithPrivateKey =
                                    DefaultCertificateFactory.Instance.CreateWithPEMPrivateKey(
                                        newCert,
                                        privateKey.ToArray(),
                                        passwordProvider?.GetPassword(existingCertIdentifier));
                                try
                                {
                                    await UpdateCertificateInternalAsync(
                                        certificateGroup,
                                        existingCertIdentifier,
                                        updateCertificate, ct).ConfigureAwait(false);
                                    break;
                                }
                                catch (Exception ex) when (ShouldRetry(attempt, ex))
                                {
                                    m_logger.LogDebug(
                                        Utils.TraceMasks.Security,
                                        ex,
                                        "Failed to update certificate {Certificate} with PEM private key. Retrying...",
                                        newCert);
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex) when (ex is not ServiceResultException)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to verify integrity of the new certificate and the private key.", ex);
                }
                finally
                {
                    // dispose temporary new private key as it is no longer needed
                    certificateGroup.TemporaryApplicationCertificate?.Dispose();
                    certificateGroup.TemporaryApplicationCertificate = null!;
                }

                certificateGroup.UpdateCertificate = updateCertificate;
                applyChangesRequired = true;
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
                Server.ReportAuditCertificateEvent(newCert!, e, m_logger);
                throw;
            }
            finally
            {
                // certWithPrivateKey?.Dispose();
            }

            return new UpdateCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ApplyChangesRequired = applyChangesRequired
            };

            static bool ShouldRetry(int attempt, Exception ex)
            {
                if (ex is ServiceResultException sre && sre.StatusCode == StatusCodes.BadConfigurationError)
                {
                    return false;
                }
                const int maxAttempts = 3;
                return attempt < maxAttempts;
            }

            // Handle the store update
            async Task UpdateCertificateInternalAsync(
                ServerCertificateGroup certificateGroup,
                CertificateIdentifier existingCertIdentifier,
                UpdateCertificateData updateCertificate,
                CancellationToken ct)
            {
                try
                {
                    // Resolve the currently-loaded certificate so we can
                    // delete the right blob from the store. The configured
                    // CertificateIdentifier may not carry an explicit
                    // thumbprint (typical config: only StorePath +
                    // SubjectName), and the identifier no longer caches the
                    // loaded certificate, so we ask the registry for the
                    // currently-active cert of this type.
                    string? thumbprintToDelete = null;
                    if (m_configuration.CertificateManager is ICertificateRegistry registry)
                    {
                        CertificateEntry? currentEntry = registry
                            .GetApplicationCertificate(existingCertIdentifier.CertificateType);
                        thumbprintToDelete = currentEntry?.Certificate.Thumbprint
                            ?? existingCertIdentifier.Thumbprint;

                        // Capture the pre-transaction certificate exactly
                        // once, even if UpdateCertificate is called multiple
                        // times before ApplyChanges. Per OPC UA Part 12
                        // §7.10.2 a transaction groups multiple changes; the
                        // channel-cut in ApplyChanges must match every
                        // SecureChannel still negotiated against the cert
                        // that was active when the transaction started —
                        // including connections that arrived between the
                        // first and last staged UpdateCertificate. The
                        // captured cert is owned by the group and disposed
                        // by ApplyChanges after consumption (or by
                        // DisposePendingRotationState on teardown).
                        if (certificateGroup.OriginalCertificate == null && currentEntry != null)
                        {
                            certificateGroup.OriginalCertificate = currentEntry.Certificate.AddRef();
                            certificateGroup.OriginalCertificateType =
                                existingCertIdentifier.CertificateType;
                        }
                    }
                    else
                    {
                        thumbprintToDelete = existingCertIdentifier.Thumbprint;
                    }

                    using (ICertificateStore? appStore = CertificateIdentifierResolver
                        .OpenStore(existingCertIdentifier, Server.Telemetry))
                    {
                        if (appStore == null)
                        {
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open application certificate store.");
                        }

                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Delete application certificate {Thumbprint}",
                            thumbprintToDelete);
                        if (!string.IsNullOrEmpty(thumbprintToDelete))
                        {
                            await appStore.DeleteAsync(
                                thumbprintToDelete!,
                                ct)
                                .ConfigureAwait(false);
                        }
                        ICertificatePasswordProvider? passwordProvider = m_configuration
                            .SecurityConfiguration
                            .CertificatePasswordProvider;
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Add new application certificate {Certificate}",
                            updateCertificate.CertificateWithPrivateKey);
                        Debug.Assert(updateCertificate.CertificateWithPrivateKey.HasPrivateKey);
                        await appStore.AddAsync(
                            updateCertificate.CertificateWithPrivateKey,
                            passwordProvider?.GetPassword(existingCertIdentifier),
                            ct)
                            .ConfigureAwait(false);

                        // Replace the registered application certificate in
                        // the CertificateManager's registry so endpoint
                        // descriptions, transport listeners, and validation
                        // cores pick up the new cert without waiting for the
                        // ApplyChanges-driven UpdateAsync reload.
                        if (m_configuration.CertificateManager is ICertificateLifecycle lifecycle)
                        {
                            await lifecycle.UpdateApplicationCertificateAsync(
                                existingCertIdentifier.CertificateType,
                                updateCertificate.CertificateWithPrivateKey,
                                issuerChain: null,
                                ct).ConfigureAwait(false);
                        }

                        // keep only track of cert without private key
                        var certOnly = Certificate.FromRawData(
                            updateCertificate.CertificateWithPrivateKey.RawData);
                        updateCertificate.CertificateWithPrivateKey.Dispose();
                        updateCertificate.CertificateWithPrivateKey = certOnly;
                    }

                    ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
                    try
                    {
                        if (issuerStore == null)
                        {
                            throw ServiceResultException.ConfigurationError(
                                "Failed to open issuer certificate store.");
                        }

                        foreach (Certificate issuer in updateCertificate.IssuerCollection)
                        {
                            try
                            {
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "Add new issuer certificate {Certificate}",
                                    issuer);
                                await issuerStore.AddAsync(issuer, ct: ct).ConfigureAwait(false);
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

                    updateCertificate.IssuerCollection?.Dispose();
                    updateCertificate.IssuerCollection = null!;

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
                        ex,
                        "Failed to update certificate {Certificate}.",
                        newCert);
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to update certificate.",
                        ex);
                }
            }
        }

        /// <summary>
        /// Creates a new self-signed certificate per OPC 10000-12 §7.10.6.
        /// The server generates a key pair internally, builds a self-signed
        /// certificate with the requested subject / DNS / IP and lifetime,
        /// stores it, and returns the DER-encoded public certificate.
        /// </summary>
        private ValueTask<CreateSelfSignedCertificateMethodStateResult>
            CreateSelfSignedCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> dnsNames,
            ArrayOf<string> ipAddresses,
            ushort lifetimeInDays,
            ushort keySizeInBits,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup? certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            if (string.IsNullOrEmpty(subjectName))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "SubjectName must be provided.");
            }

            if (lifetimeInDays == 0)
            {
                lifetimeInDays = CertificateFactory.DefaultLifeTime;
            }

            // merge DNS names and IP addresses into one domain list
            var domainNames = new List<string>();
            if (!dnsNames.IsNull)
            {
                foreach (string dns in dnsNames)
                {
                    if (!string.IsNullOrEmpty(dns))
                    {
                        domainNames.Add(dns);
                    }
                }
            }
            if (!ipAddresses.IsNull)
            {
                foreach (string ip in ipAddresses)
                {
                    if (!string.IsNullOrEmpty(ip))
                    {
                        domainNames.Add(ip);
                    }
                }
            }

            ICertificateBuilder builder = s_certificateFactory
                .CreateApplicationCertificate(
                    m_configuration.ApplicationUri!,
                    m_configuration.ApplicationName!,
                    subjectName,
                    [.. domainNames])
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddDays(lifetimeInDays));

            Certificate certificate;
            if (certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                ushort keySize = keySizeInBits > 0
                    ? keySizeInBits
                    : CertificateFactory.DefaultKeySize;
                certificate = builder.SetRSAKeySize(keySize).CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The ECC certificate type is not supported.");
                certificate = builder.SetECCurve(curve.Value).CreateForECDsa();
            }

            // persist the new self-signed certificate into the group's
            // configured store so it survives restarts and becomes the
            // active application certificate.
            CertificateIdentifier? existingIdent = certificateGroup!.ApplicationCertificates
                .ToList()
                .FirstOrDefault(c => c.CertificateType == certificateTypeId);

            existingIdent?.RawData = certificate.RawData;

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Created self-signed certificate {Subject} for {Group}/{Type}.",
                certificate.Subject,
                certificateGroupId,
                certificateTypeId);

            ByteString certBytes = certificate.RawData.ToByteString();
            return new ValueTask<CreateSelfSignedCertificateMethodStateResult>(
                new CreateSelfSignedCertificateMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Certificate = certBytes
                });
        }

        private async ValueTask<CreateSigningRequestMethodStateResult> CreateSigningRequestAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            bool regeneratePrivateKey,
            ByteString nonce,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup? certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId);

            // identify the existing certificate for which to CreateSigningRequest
            // it should be of the same type
            CertificateIdentifier? existingCertIdentifier = certificateGroup!.ApplicationCertificates
                .ToList().FirstOrDefault(
                    cert => cert.CertificateType == certificateTypeId);

            // Look up the currently-active certificate via the manager
            // registry — the configured identifier is metadata only.
            Certificate? currentCert = null;
            if (m_configuration.CertificateManager is ICertificateRegistry currentRegistry)
            {
                CertificateEntry? currentEntry = currentRegistry
                    .GetApplicationCertificate(certificateTypeId);
                currentCert = currentEntry?.Certificate;
            }

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = (currentCert?.Subject ?? existingCertIdentifier?.SubjectName)!;
            }

            certificateGroup.TemporaryApplicationCertificate?.Dispose();
            certificateGroup.TemporaryApplicationCertificate = null!;

            Certificate certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                ArrayOf<string> domainNames = currentCert != null
                    ? X509Utils.GetDomainsFromCertificate(currentCert)
                    : default;

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    certificateGroup,
                    subjectName,
                    domainNames);
            }
            else
            {
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier!,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false) ??
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");
            }

            m_logger.LogInformation(
                Utils.TraceMasks.Security,
                "Create signing request {Certificate}",
                certWithPrivateKey);
            var certificateRequest = ByteString.From(s_certificateFactory.CreateSigningRequest(
                certWithPrivateKey,
                X509Utils.GetDomainsFromCertificate(certWithPrivateKey).ToArray()));

            return new CreateSigningRequestMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                CertificateRequest = certificateRequest
            };
        }

        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            ServerCertificateGroup certificateGroup,
            string subjectName,
            ArrayOf<string> domainNames)
        {
            Certificate certificate;

            ICertificateBuilder certificateBuilder = s_certificateFactory
                .CreateApplicationCertificate(m_configuration.ApplicationUri!, m_configuration.ApplicationName!, subjectName, domainNames.ToArray())
                .SetNotBefore(DateTime.Today.AddDays(-1))
                .SetNotAfter(DateTime.Today.AddDays(14));

            if (certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType)
            {
                certificate = certificateBuilder.SetRSAKeySize(CertificateFactory.DefaultKeySize)
                    .CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The Ecc certificate type is not supported.");
                certificate = certificateBuilder.SetECCurve(curve.Value).CreateForECDsa();
            }

            certificateGroup.TemporaryApplicationCertificate = certificate;

            return certificate;
        }

        private ServiceResult ApplyChanges(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            HasApplicationSecureAdminAccess(context);

            // Capture the per-group rotation payload (original cert +
            // type, captured at the first staged UpdateCertificate) and
            // clear the staging slot so the post-response channel-cut
            // can target the correct SecureChannels by thumbprint per
            // OPC UA Part 12 §7.10.9.
            var pendingRotations = new List<PendingCertificateRotation>();

            foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
            {
                UpdateCertificateData? updateCertificate = certificateGroup.UpdateCertificate;
                if (updateCertificate == null)
                {
                    // No staged update for this group — but a previous
                    // failed staging may have left an OriginalCertificate
                    // behind. Discard it so it does not leak.
                    DisposePendingRotationState(certificateGroup);
                    continue;
                }

                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "Apply Changes for certificate {Certificate}",
                    updateCertificate.CertificateWithPrivateKey);

                // Hand off ownership of OriginalCertificate to the
                // deferred task. The reference on the group is then
                // cleared so DisposePendingRotationState cannot
                // double-dispose it.
                pendingRotations.Add(new PendingCertificateRotation
                {
                    OldCertificate = certificateGroup.OriginalCertificate,
                    CertificateType = certificateGroup.OriginalCertificateType
                });
                certificateGroup.OriginalCertificate = null;
                certificateGroup.OriginalCertificateType = NodeId.Null;
                certificateGroup.UpdateCertificate = null!;
            }

            if (pendingRotations.Count == 0)
            {
                return StatusCodes.Good;
            }

            // Schedule the deferred apply: wait a short grace period for
            // the method response to be flushed, then re-sync the
            // certificate manager from disk and force-close every
            // SecureChannel that was negotiated against the rotated
            // certificate(s). The completion handle is exposed via
            // DrainPendingApplyChangesAsync so tests and hosts can
            // deterministically await rotation rather than racing the
            // delay.
            ScheduleDeferredApplyChanges(pendingRotations);

            return StatusCodes.Good;
        }

        /// <summary>
        /// Schedules the post-response cert-rotation fan-out. Chains
        /// onto any already-running deferred apply so concurrent calls
        /// to <see cref="ApplyChanges"/> run sequentially.
        /// </summary>
        private void ScheduleDeferredApplyChanges(List<PendingCertificateRotation> rotations)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            Task previous;
            lock (m_pendingApplyChangesLock)
            {
                previous = m_pendingApplyChangesTask;
                m_pendingApplyChangesTask = completion.Task;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for any earlier deferred apply to finish to
                    // preserve ordering.
                    if (previous != null)
                    {
                        try
                        {
                            await previous.ConfigureAwait(false);
                        }
                        catch
                        {
                            // Errors on the previous task are already
                            // logged; do not propagate to the new one.
                        }
                    }

                    TimeSpan gracePeriod = ApplyChangesGracePeriod;
                    if (gracePeriod < TimeSpan.Zero)
                    {
                        gracePeriod = TimeSpan.Zero;
                    }

                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Apply Changes for application certificate scheduled in {Grace} ms...",
                        gracePeriod.TotalMilliseconds);

                    // Give the client a chance to receive the
                    // ApplyChanges response before cutting its channel.
                    // OPC UA Part 12 §7.10.9 requires the response is
                    // returned first; without a transport-level
                    // "response flushed" hook this grace period is the
                    // pragmatic compromise. The grace period itself is
                    // configurable via ApplyChangesGracePeriod so hosts
                    // running over high-latency links can tune it.
                    // TODO: implement a transport-level
                    // "response-flushed" callback so this can be
                    // deterministic without relying on a fixed delay.
                    await m_timeProvider.Delay(gracePeriod)
                        .ConfigureAwait(false);

                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Apply Changes for application certificate running...");

                    if (m_configuration.CertificateManager != null)
                    {
                        await m_configuration.CertificateManager.UpdateAsync(
                                m_configuration.SecurityConfiguration,
                                m_configuration.ApplicationUri)
                            .ConfigureAwait(false);
                    }

                    // Force-close affected SecureChannels on every
                    // transport listener that opted into
                    // ITransportListenerCertificateRotation.
                    IReadOnlyList<ITransportListener> listeners
                        = (Server as ITransportListenerRegistryProvider)?.TransportListeners
                          ?? [];

                    int totalCut = 0;
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        if (rotation.OldCertificate == null)
                        {
                            continue;
                        }

                        foreach (ITransportListener listener in listeners)
                        {
                            if (listener is not ITransportListenerCertificateRotation rotator)
                            {
                                continue;
                            }

                            try
                            {
                                IReadOnlyList<string> closed
                                    = rotator.CloseChannelsForCertificate(rotation.OldCertificate);
                                totalCut += closed.Count;
                            }
                            catch (Exception ex)
                            {
                                m_logger.LogWarning(
                                    ex,
                                    "Listener {Listener} failed to close channels for {CertType}.",
                                    listener.ListenerId,
                                    rotation.CertificateType);
                            }
                        }
                    }

                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Apply Changes for application certificate completed: {Count} SecureChannel(s) cut.",
                        totalCut);

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.LogCritical(
                        ex,
                        "Apply Changes for application certificate update failed. " +
                        "Server could be in a faulted state.");
                    completion.TrySetException(ex);
                }
                finally
                {
                    foreach (PendingCertificateRotation rotation in rotations)
                    {
                        rotation.OldCertificate?.Dispose();
                    }
                }
            });
        }

        /// <inheritdoc/>
        public Task DrainPendingApplyChangesAsync(CancellationToken cancellationToken = default)
        {
            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingApplyChangesTask;
            }

            if (pending == null || pending.IsCompleted)
            {
                return Task.CompletedTask;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                return pending;
            }

            return pending.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Clears the staging slot for a new <c>UpdateCertificate</c>
        /// call. The pre-transaction
        /// <see cref="ServerCertificateGroup.OriginalCertificate"/> is
        /// <i>preserved</i> across consecutive stagings so a multi-step
        /// transaction (Part 12 §7.10.2) still cuts every SecureChannel
        /// established before the first staged update. Use
        /// <see cref="DisposePendingRotationState"/> to release the
        /// captured certificate on full teardown.
        /// </summary>
        private static void ResetPendingUpdateCertificate(ServerCertificateGroup certificateGroup)
        {
            certificateGroup.UpdateCertificate = null!;
        }

        /// <summary>
        /// Releases the pre-transaction certificate captured during
        /// <c>UpdateCertificate</c> staging and clears the staging slot.
        /// Called by <see cref="ApplyChanges"/> when a group has no
        /// pending update (stale capture from a failed transaction) and
        /// by the manager's <see cref="Dispose"/>.
        /// </summary>
        private static void DisposePendingRotationState(ServerCertificateGroup certificateGroup)
        {
            certificateGroup.OriginalCertificate?.Dispose();
            certificateGroup.OriginalCertificate = null;
            certificateGroup.OriginalCertificateType = NodeId.Null;
            certificateGroup.UpdateCertificate = null!;
        }

        /// <summary>
        /// Captured payload for a single certificate-group rotation
        /// scheduled by <see cref="ApplyChanges"/>. The deferred apply
        /// task owns the contained <see cref="Certificate"/> reference
        /// and disposes it once the channel-cut completes.
        /// </summary>
        private sealed class PendingCertificateRotation
        {
            public Certificate? OldCertificate { get; set; }
            public NodeId CertificateType { get; set; }
        }

        private ServiceResult GetRejectedList(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref ArrayOf<ByteString> certificates)
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
                    using CertificateCollection collection = store.EnumerateAsync().Result;
                    var rawList = new List<ByteString>();
                    foreach (Certificate cert in collection)
                    {
                        rawList.Add(cert.RawData.ToByteString());
                    }
                    certificates = rawList.ToArrayOf();
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
            ref ArrayOf<NodeId> certificateTypeIds,
            ref ArrayOf<ByteString> certificates)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup =
                m_certificateGroups.FirstOrDefault(
                    group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");

            certificateTypeIds = certificateGroup.CertificateTypes;

            // Look up each certificate via the manager registry so the
            // returned blobs reflect the currently-active cert (the
            // configured identifier carries no Certificate cache).
            var rawCerts = new List<ByteString>();
            var registry = m_configuration.CertificateManager as ICertificateRegistry;
            foreach (CertificateIdentifier appId in certificateGroup.ApplicationCertificates)
            {
                CertificateEntry? entry = registry?.GetApplicationCertificate(appId.CertificateType);
                rawCerts.Add(entry?.Certificate?.RawData.ToByteString() ?? default);
            }
            certificates = rawCerts.ToArrayOf();

            return ServiceResult.Good;
        }

        private ServerCertificateGroup? VerifyGroupAndTypeId(
            NodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            // verify typeid must be set
            if (certificateTypeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not specified.");
            }

            // verify requested certificate group
            if (certificateGroupId.IsNull)
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
        private async ValueTask<NamespaceMetadataState?> FindNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default)
        {
            try
            {
                // find ServerNamespaces node
                if (FindPredefinedNode<NamespacesState>(ObjectIds.Server_Namespaces)
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

                    if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
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
                        // Find NamespaceMetadata node of NamespaceUri in Namespaces references.
                        var nameSpaceNodeId = ExpandedNodeId.ToNodeId(
                            serverNamespacesReference.TargetId,
                            Server.NamespaceUris);
                        if (await Server.NodeManager.FindNodeInAddressSpaceAsync(
                            nameSpaceNodeId, cancellationToken).ConfigureAwait(false) is not NamespaceMetadataState namespaceMetadata)
                        {
                            continue;
                        }

                        if (namespaceMetadata!.NamespaceUri!.Value == namespaceUri)
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
                    lock (m_namespaceMetadataStatesLock)
                    {
                        m_namespaceMetadataStates.Clear();
                        m_namespaceMetadataStatesByIndex.Clear();
                    }

                    if (node is NamespacesState serverNamespacesNode)
                    {
                        IList<BaseInstanceState> children = [];
                        serverNamespacesNode.GetChildren(context, children);

                        foreach (BaseInstanceState child in children)
                        {
                            if (child is NamespaceMetadataState metadataState)
                            {
                                SubscribeToNamespaceDefaultPermissions(metadataState);
                            }
                        }
                    }
                }
                catch
                {
                    // ignore errors
                }
            }
        }

        /// <summary>
        /// Subscribes to the <c>StateChanged</c> events of the <c>DefaultRolePermissions</c>
        /// and <c>DefaultUserRolePermissions</c> child nodes of a <see cref="NamespaceMetadataState"/>
        /// to detect changes that require permission cache invalidation.
        /// </summary>
        private void SubscribeToNamespaceDefaultPermissions(NamespaceMetadataState namespaceMetadataState)
        {
            if (namespaceMetadataState.DefaultRolePermissions != null)
            {
                // unsubscribe first to avoid duplicate subscriptions if called multiple times
                namespaceMetadataState.DefaultRolePermissions.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                namespaceMetadataState.DefaultRolePermissions.StateChanged += OnNamespaceDefaultPermissionsChanged;
            }

            if (namespaceMetadataState.DefaultUserRolePermissions != null)
            {
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged -= OnNamespaceDefaultPermissionsChanged;
                namespaceMetadataState.DefaultUserRolePermissions.StateChanged += OnNamespaceDefaultPermissionsChanged;
            }

            namespaceMetadataState.StateChanged -= OnNamespaceChildrenChanged;
            namespaceMetadataState.StateChanged += OnNamespaceChildrenChanged;
        }

        /// <summary>
        /// Handles children change on NamespaceMetadataState and resubscribes to the default permissions nodes
        /// to ensure we are notified of changes on those nodes even if they are recreated.
        /// </summary>
        private void OnNamespaceChildrenChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Children) != 0 &&
                node is NamespaceMetadataState namespaceMetadataState)
            {
                SubscribeToNamespaceDefaultPermissions(namespaceMetadataState);
            }
        }

        /// <summary>
        /// Handles value changes on <c>DefaultRolePermissions</c> or <c>DefaultUserRolePermissions</c>
        /// and raises the <see cref="DefaultPermissionsChanged"/> event.
        /// </summary>
        private void OnNamespaceDefaultPermissionsChanged(
            ISystemContext context,
            NodeState node,
            NodeStateChangeMasks changes)
        {
            if ((changes & NodeStateChangeMasks.Value) != 0)
            {
                DefaultPermissionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        public event EventHandler? DefaultPermissionsChanged;

        private class UpdateCertificateData
        {
            public NodeId SessionId { get; set; }
            public Certificate CertificateWithPrivateKey { get; set; } = null!;
            public CertificateCollection IssuerCollection { get; set; } = null!;
        }

        /// <summary>
        /// Evaluates certificate expiration and trust-list staleness for
        /// all certificate groups and activates/deactivates the optional
        /// <c>CertificateExpired</c> and <c>TrustListOutOfDate</c> alarm
        /// instances per OPC 10000-12 §7.8.3.
        /// </summary>
        /// <inheritdoc/>
        public void StartAlarmMonitoring(TimeSpan interval)
        {
            if (m_alarmTimer != null)
            {
                return;
            }

            m_alarmTimer = m_timeProvider.CreateTimer(
                _ =>
                {
                    try
                    {
                        EvaluateCertificateAlarms(SystemContext);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogWarning(ex, "Alarm evaluation tick failed.");
                    }
                },
                null,
                interval,
                interval);
        }

        /// <inheritdoc/>
        public void StopAlarmMonitoring()
        {
            m_alarmTimer?.Dispose();
            m_alarmTimer = null;
        }

        private void EvaluateCertificateAlarms(ISystemContext context)
        {
            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                CertificateGroupState node = certGroup.Node!;

                try
                {
                    // --- CertificateExpired alarm ---
                    // Only populate properties if the optional alarm instance
                    // was loaded from the predefined nodeset. We set property
                    // values directly rather than calling SetActiveState to
                    // avoid triggering event notifications during server
                    // startup (which can fail before subscriptions exist).
                    if (node.CertificateExpired?.ExpirationDate != null)
                    {
                        DateTime expirationDate = DateTime.MaxValue;

                        foreach (CertificateIdentifier certIdent in certGroup.ApplicationCertificates)
                        {
                            if (certIdent.RawData != null && certIdent.RawData.Length > 0)
                            {
                                try
                                {
                                    using var cert = Certificate.FromRawData(certIdent.RawData);
                                    if (cert.NotAfter < expirationDate)
                                    {
                                        expirationDate = cert.NotAfter;
                                    }
                                }
                                catch
                                {
                                    // ignore parsing errors
                                }
                            }
                        }

                        if (expirationDate != DateTime.MaxValue)
                        {
                            node.CertificateExpired.ExpirationDate.Value = expirationDate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to evaluate CertificateExpired alarm for group {Group}.",
                        certGroup.BrowseName);
                }

                try
                {
                    // --- TrustListOutOfDate alarm ---
                    if (node.TrustListOutOfDate?.TrustListId != null)
                    {
                        node.TrustListOutOfDate.TrustListId.Value =
                            node.TrustList?.NodeId ?? default;

                        node.TrustListOutOfDate.LastUpdateTime?.Value =
                                (DateTime)(node.TrustList?.LastUpdateTime?.Value
                                    ?? (DateTimeUtc)DateTime.MinValue);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to evaluate TrustListOutOfDate alarm for group {Group}.",
                        certGroup.BrowseName);
                }
            }
        }

        private class ServerCertificateGroup
        {
            public string BrowseName { get; set; } = null!;
            public NodeId NodeId { get; set; }
            public CertificateGroupState Node { get; set; } = null!;
            public NodeId[] CertificateTypes { get; set; } = null!;
            public ArrayOf<CertificateIdentifier> ApplicationCertificates { get; set; }
            public CertificateStoreIdentifier IssuerStore { get; set; } = null!;
            public CertificateStoreIdentifier TrustedStore { get; set; } = null!;
            public UpdateCertificateData UpdateCertificate { get; set; } = null!;
            public Certificate TemporaryApplicationCertificate { get; set; } = null!;

            /// <summary>
            /// The application certificate that was active in the
            /// registry BEFORE the first <c>UpdateCertificate</c> call
            /// of the current transaction. Captured on the first staging
            /// in <c>UpdateCertificateInternalAsync</c> and preserved
            /// across subsequent staging calls (per OPC UA Part 12
            /// §7.10.2 transaction lifecycle) so that the channel-cut
            /// in <c>ApplyChanges</c> matches every SecureChannel still
            /// negotiated against the pre-transaction certificate —
            /// including connections established between the first and
            /// last staged <c>UpdateCertificate</c>. Owned by the group;
            /// disposed only by <c>ApplyChanges</c> after consumption
            /// or by <c>DisposePendingRotationState</c> on
            /// teardown.
            /// </summary>
            public Certificate? OriginalCertificate { get; set; }

            /// <summary>
            /// The certificate type that <see cref="OriginalCertificate"/>
            /// belongs to. <see cref="NodeId.Null"/> when no original
            /// has been captured.
            /// </summary>
            public NodeId OriginalCertificateType { get; set; }
        }

#pragma warning disable CA2213 // m_serverConfigurationNode is owned by the address space, not by this manager.
        private ServerConfigurationState? m_serverConfigurationNode;
        private UserManagement.UserManagementBinding? m_userManagementBinding;
#pragma warning restore CA2213
        private readonly ApplicationConfiguration m_configuration;
        private readonly TimeProvider m_timeProvider;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier? m_rejectedStore;
        private ITimer? m_alarmTimer;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_namespaceMetadataStatesByIndex = [];
        private readonly Lock m_namespaceMetadataStatesLock = new();
        private readonly Lock m_pendingApplyChangesLock = new();
        private Task m_pendingApplyChangesTask = Task.CompletedTask;

        /// <inheritdoc/>
        public TimeSpan ApplyChangesGracePeriod { get; set; }
            = TimeSpan.FromMilliseconds(250);

        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;
    }
}
