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
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
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
            : this(server, configuration, logger, timeProvider, coordinator: null, pendingKeyStore: null)
        {
        }

        /// <summary>
        /// Initializes the configuration and diagnostics manager with an
        /// explicit PushManagement transaction coordinator and pending-key
        /// store, replacing the defaults this manager would otherwise
        /// create for itself.
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
        /// <param name="coordinator">
        /// The shared PushManagement transaction coordinator (OPC 10000-12
        /// §§7.10.2-7.10.11). When <see langword="null"/>, a private
        /// <see cref="PushConfigurationTransactionCoordinator"/> is created.
        /// </param>
        /// <param name="pendingKeyStore">
        /// The store used to persist regenerated signing-request private
        /// keys (§7.10.10). When <see langword="null"/>, a private
        /// <see cref="DirectoryPendingCertificateKeyStore"/> is created.
        /// </param>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            TimeProvider? timeProvider,
            IPushConfigurationTransactionCoordinator? coordinator,
            IPendingCertificateKeyStore? pendingKeyStore)
            : base(server, configuration, logger, timeProvider)
        {
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_coordinator = coordinator
                ?? new PushConfigurationTransactionCoordinator(server.Telemetry, m_timeProvider);
            m_pendingKeyStore = pendingKeyStore ?? new DirectoryPendingCertificateKeyStore();
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
                            var activeNode = (ServerConfigurationState)passiveNode;

                            activeNode
                                .AddGetCertificates(context)
                                .AddCreateSelfSignedCertificate(context)
                                .AddDeleteCertificate(context)
                                .AddCancelChanges(context)
                                .AddSupportsTransactions(context)
                                .AddTransactionDiagnostics(context);

                            m_serverConfigurationNode = activeNode;

                            return activeNode;
                        }
                        case ObjectTypes.CertificateGroupFolderType:
                        {
                            // The standard nodeset contains CertificateGroupFolderType
                            // instances under several types (e.g. ServerConfigurationType,
                            // ApplicationConfigurationType, ProvisionableDeviceType). Only
                            // the Server's own ServerConfiguration certificate groups folder
                            // is managed here; the others must keep their loaded structure.
                            if (passiveNode.NodeId != ObjectIds.ServerConfiguration_CertificateGroups)
                            {
                                break;
                            }

                            var activeNode = (CertificateGroupFolderState)passiveNode;

                            ServerCertificateGroup? applicationGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultApplicationGroup);

                            applicationGroup!.Node = activeNode.DefaultApplicationGroup!;

                            ServerCertificateGroup? httpsGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultHttpsGroup);
                            if (httpsGroup != null)
                            {
                                activeNode.AddDefaultHttpsGroup(context);
                                httpsGroup.Node = activeNode.DefaultHttpsGroup!;
                            }

                            ServerCertificateGroup? userTokenGroup =
                                m_certificateGroups.FirstOrDefault(m => m.BrowseName == BrowseNames.DefaultUserTokenGroup);
                            if (userTokenGroup != null)
                            {
                                activeNode.AddDefaultUserTokenGroup(context);
                                userTokenGroup.Node = activeNode.DefaultUserTokenGroup!;
                            }

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

                // Cancels and disposes any transaction still active
                // (staged certificate/TrustList operations) so their
                // captured certificates and streams do not leak. Any
                // rotations produced by a commit are always drained and
                // handled (disposed or scheduled) by that same call to
                // ApplyChangesAsync below before it returns, so there is
                // no separate global rotation list to clean up here.
                m_coordinator.Reset();

                StopAlarmMonitoring();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Per OPC UA Part 12 §7.10.2, an abandoned PushManagement
        /// transaction must not block every other Session indefinitely.
        /// When the closing Session owns the active transaction, it is
        /// cancelled (staged operations discarded, never applied) and
        /// every TrustList's open write handle owned by this Session is
        /// closed.
        /// </remarks>
        public override async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            m_coordinator.CancelForSessionClose(sessionId);
            UpdateTransactionDiagnostics(SystemContext);

            foreach (ServerCertificateGroup certificateGroup in m_certificateGroups)
            {
                if (certificateGroup.Node?.TrustList?.Handle is TrustList trustList)
                {
                    trustList.NotifySessionClosing(sessionId);
                }
            }

            await base.SessionClosingAsync(context, sessionId, deleteSubscriptions, cancellationToken)
                .ConfigureAwait(false);
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
            configNode.DeleteCertificate?.OnCallAsync =
                new DeleteCertificateMethodStateMethodAsyncCallHandler(DeleteCertificateAsync);
            configNode.ApplyChanges!.OnCallMethod2Async
                = new GenericMethodCalledEventHandler2Async(ApplyChangesAsync);
            configNode.CancelChanges?.OnCallMethod2Async
                = new GenericMethodCalledEventHandler2Async(CancelChangesAsync);
            configNode.GetRejectedList!.OnCall
                = new GetRejectedListMethodStateMethodCallHandler(
                GetRejectedList);
            configNode.GetCertificates!.OnCall
                = new GetCertificatesMethodStateMethodCallHandler(
                GetCertificates);
            if (configNode.SupportsTransactions != null)
            {
                configNode.SupportsTransactions.Value = true;
            }
            configNode.ClearChangeMasks(systemContext, true);
            UpdateTransactionDiagnostics(systemContext);

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
                    m_coordinator,
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
                namespaceMetadataState = SystemContext.CreateInstanceOfNamespaceMetadataType(
                    serverNamespacesNode,
                    new QualifiedName(namespaceUri, NamespaceIndex));
                namespaceMetadataState.NodeId = SystemContext.NodeIdFactory.New(SystemContext, namespaceMetadataState);
                namespaceMetadataState.DisplayName = LocalizedText.From(namespaceUri);
                namespaceMetadataState.SymbolicName = namespaceUri;
                namespaceMetadataState!.NamespaceUri!.Value = namespaceUri;
                namespaceMetadataState.AddDefaultRolePermissions(SystemContext);
                namespaceMetadataState.AddDefaultUserRolePermissions(SystemContext);

                // add node as child of ServerNamespaces and in predefined nodes
                serverNamespacesNode.AddChild(namespaceMetadataState);
                await serverNamespacesNode.ClearChangeMasksAsync(SystemContext, true, cancellationToken)
                    .ConfigureAwait(false);
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
                KeyCredentialPushSubject.StandardConfigurationFolderNodeId) ??
                await Server.NodeManager
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

        /// <summary>
        /// Extracts the owning Session's NodeId from <paramref name="context"/>,
        /// or <see cref="NodeId.Null"/> when the context is not
        /// Session-bound (for example, an internal call).
        /// </summary>
        private static NodeId GetSessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        /// <summary>
        /// Refreshes the <c>TransactionDiagnostics</c> address-space node
        /// from the coordinator's current snapshot. Called after every
        /// <c>ApplyChanges</c>, <c>CancelChanges</c>, and Session-close
        /// cancellation.
        /// </summary>
        private void UpdateTransactionDiagnostics(ISystemContext context)
        {
            if (m_serverConfigurationNode?.TransactionDiagnostics is not { } diagnosticsNode)
            {
                return;
            }

            PushConfigurationTransactionSnapshot snapshot = m_coordinator.GetSnapshot();

            if (diagnosticsNode.StartTime != null)
            {
                diagnosticsNode.StartTime.Value = snapshot.StartTime;
            }

            if (diagnosticsNode.EndTime != null)
            {
                diagnosticsNode.EndTime.Value = snapshot.EndTime;
            }

            if (diagnosticsNode.Result != null)
            {
                diagnosticsNode.Result.Value = snapshot.Result;
            }

            if (diagnosticsNode.AffectedTrustLists != null)
            {
                diagnosticsNode.AffectedTrustLists.Value = snapshot.AffectedTrustLists;
            }

            if (diagnosticsNode.AffectedCertificateGroups != null)
            {
                diagnosticsNode.AffectedCertificateGroups.Value = snapshot.AffectedCertificateGroups;
            }

            if (diagnosticsNode.Errors != null)
            {
                diagnosticsNode.Errors.Value = snapshot.Errors;
            }

            diagnosticsNode.ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Finds the configured <see cref="CertificateIdentifier"/> for
        /// <paramref name="certificateTypeId"/> within <paramref name="certificateGroup"/>.
        /// The identifier is metadata-only (store path/type and
        /// certificate type); it may or may not currently resolve to a
        /// certificate on disk.
        /// </summary>
        private static CertificateIdentifier FindCertificateIdentifier(
            ServerCertificateGroup certificateGroup,
            NodeId certificateTypeId)
        {
            return certificateGroup.ApplicationCertificates
                .ToList()
                .FirstOrDefault(cert => cert.CertificateType == certificateTypeId)
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate type not valid for certificate group.");
        }

        /// <summary>
        /// Asynchronously determines whether <paramref name="existingCertIdentifier"/>'s
        /// slot currently resolves to a certificate. The certificate
        /// manager's registry (keyed purely by configured certificate
        /// type, consistent with <c>GetCertificates</c>/<c>UpdateCertificate</c>
        /// elsewhere in this class) is authoritative when available;
        /// resolving directly against the store is used only as a
        /// fallback, since store resolution re-validates the certificate's
        /// cryptographic properties against the certificate type and would
        /// otherwise disagree with the registry for perfectly valid
        /// configurations. Used by <c>CreateSelfSignedCertificate</c> (OPC
        /// 10000-12 §7.10.6: never replace an occupied slot) and by
        /// <c>DeleteCertificate</c> (the slot must be occupied).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The live occupancy above is netted against every operation
        /// already staged (but not yet committed) in the active
        /// transaction for this exact (<paramref name="certificateGroupId"/>,
        /// <c>existingCertIdentifier.CertificateType</c>) slot, via
        /// <see cref="IPushConfigurationTransactionCoordinator.GetStagedOperations"/>,
        /// so a later request in the same transaction is validated
        /// against the cumulative effect of every earlier request against
        /// this slot - not just the live state as it appeared before any
        /// staging began. This permits, for example, staging
        /// <c>DeleteCertificate</c> followed by <c>CreateSelfSignedCertificate</c>
        /// for the same slot in one transaction (the slot nets as
        /// unoccupied even though the live delete has not committed yet),
        /// and makes <c>CreateSelfSignedCertificate</c> followed by
        /// <c>DeleteCertificate</c> see the slot as occupied (even though
        /// the live create has not committed yet either).
        /// </para>
        /// <para>
        /// Only the returned <c>Occupied</c> flag is netted this way;
        /// <c>Thumbprint</c> always reflects the real live state.
        /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/>
        /// supersedes (and discards, without ever committing) whichever
        /// operation a new request for the same slot replaces, so the
        /// single operation left staged for this slot must still act
        /// against whatever is genuinely live on disk/registry once it
        /// commits; ordered operation semantics are preserved because at
        /// most one staged operation can ever match this exact slot.
        /// </para>
        /// </remarks>
        private async ValueTask<(bool Occupied, string? Thumbprint)> IsSlotOccupiedAsync(
            NodeId certificateGroupId,
            CertificateIdentifier existingCertIdentifier,
            CancellationToken cancellationToken)
        {
            bool occupied;
            string? thumbprint;
            if (m_configuration.CertificateManager is ICertificateRegistry registry)
            {
                using CertificateEntry? entry = registry
                    .AcquireApplicationCertificateByType(existingCertIdentifier.CertificateType);
                occupied = entry != null;
                thumbprint = entry?.Certificate.Thumbprint;
            }
            else
            {
                Certificate? resolved = await CertificateIdentifierResolver.ResolveAsync(
                    existingCertIdentifier,
                    registry: null,
                    needPrivateKey: false,
                    m_configuration.ApplicationUri,
                    Server.Telemetry,
                    cancellationToken).ConfigureAwait(false);

                if (resolved == null)
                {
                    occupied = false;
                    thumbprint = null;
                }
                else
                {
                    using (resolved)
                    {
                        occupied = true;
                        thumbprint = resolved.Thumbprint;
                    }
                }
            }

            foreach (PushConfigurationOperation staged in m_coordinator.GetStagedOperations())
            {
                if (staged.AffectedCertificateType.IsNull ||
                    !Utils.IsEqual(staged.AffectedCertificateType, existingCertIdentifier.CertificateType) ||
                    !Utils.IsEqual(staged.AffectedCertificateGroup, certificateGroupId))
                {
                    continue;
                }

                // Stage() supersedes (and disposes) any earlier operation
                // staged for this same (group, type) pair, so at most one
                // entry here can ever match; that single match always
                // reflects the net effect of every request already made
                // against this slot in this transaction.
                occupied = !staged.LeavesCertificateSlotEmpty;
            }

            return (occupied, thumbprint);
        }

        /// <summary>
        /// Builds the scope used to persist or retrieve the pending
        /// regenerated private key (§7.10.10) for a certificate group/type
        /// slot.
        /// </summary>
        private PendingCertificateKeyContext CreatePendingKeyContext(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier)
        {
            var baseStore = new CertificateStoreIdentifier(
                existingCertIdentifier.StorePath ?? string.Empty,
                existingCertIdentifier.StoreType ?? string.Empty,
                noPrivateKeys: false);
            return new PendingCertificateKeyContext(
                baseStore,
                certificateGroup.NodeId,
                existingCertIdentifier.CertificateType,
                m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                Server.Telemetry);
        }

        /// <summary>
        /// Applies a single certificate-group slot mutation: removes
        /// <paramref name="removeThumbprint"/> (if any) from the
        /// application store, adds <paramref name="addCertificateWithKey"/>
        /// (if any), imports <paramref name="addIssuerChain"/> into the
        /// group's issuer store (skipping any issuer certificate whose
        /// thumbprint is already present), and synchronizes the
        /// certificate manager's registry. This single primitive
        /// implements every staged certificate operation's commit AND
        /// rollback: a rollback simply invokes it with the before/after
        /// roles swapped (remove what commit added, restore what commit
        /// removed).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The coordinator only compensates operations that commit in
        /// full (see <see cref="PushConfigurationTransactionCoordinator.ApplyChangesAsync"/>);
        /// an operation whose own <c>CommitAsync</c> throws is excluded
        /// from that reverse-order compensation. When <paramref name="removedCertificateBackup"/>
        /// is supplied and the certificate removal above already
        /// succeeded, this method is therefore self-compensating: it
        /// restores <paramref name="removedCertificateBackup"/> before
        /// propagating a failure to add <paramref name="addCertificateWithKey"/>,
        /// so the slot is never left empty just because the replacement
        /// certificate could not be written.
        /// </para>
        /// <para>
        /// The returned thumbprints identify exactly the issuer
        /// certificates this call newly added (excluding any that were
        /// already present in the issuer store before it ran). A caller
        /// that later needs to compensate this import - either via a
        /// reverse-order rollback once a later staged operation in the
        /// same transaction fails to commit, or via its own self-
        /// compensation - should remove exactly those thumbprints (for
        /// example with <see cref="RemoveIssuerCertificatesAsync"/>), so a
        /// pre-existing issuer certificate is never removed just because
        /// it was also part of this call's issuer chain.
        /// </para>
        /// <para>
        /// This method is also self-compensating for the reverse ordering:
        /// when the application certificate slot above has already been
        /// fully swapped (both <paramref name="removeThumbprint"/> removed
        /// and <paramref name="addCertificateWithKey"/> added) and
        /// <paramref name="removedCertificateBackup"/> is supplied, a
        /// subsequent failure importing <paramref name="addIssuerChain"/>
        /// is compensated via <see cref="RestoreCertificateSlotAfterIssuerImportFailureAsync"/>
        /// before it propagates: the previous application certificate is
        /// restored and exactly the issuer certificates this call's import
        /// loop newly added so far are removed again, so a partial issuer
        /// import can never leave a newer application certificate live
        /// alongside orphaned or half-imported issuers.
        /// </para>
        /// </remarks>
        private async Task<ArrayOf<string>> ApplyCertificateSlotChangeAsync(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            string? removeThumbprint,
            Certificate? addCertificateWithKey,
            CertificateCollection? addIssuerChain,
            CancellationToken ct,
            Certificate? removedCertificateBackup = null)
        {
            bool removedCertificate = false;
            using (ICertificateStore? appStore = CertificateIdentifierResolver
                .OpenStore(existingCertIdentifier, Server.Telemetry))
            {
                if (appStore == null)
                {
                    throw ServiceResultException.ConfigurationError(
                        "Failed to open application certificate store.");
                }

                if (!string.IsNullOrEmpty(removeThumbprint))
                {
                    m_logger.LogInformation(
                        Utils.TraceMasks.Security,
                        "Delete application certificate {Thumbprint}",
                        removeThumbprint);
                    await appStore.DeleteAsync(removeThumbprint!, ct).ConfigureAwait(false);
                    removedCertificate = true;
                }

                if (addCertificateWithKey != null)
                {
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    try
                    {
                        m_logger.LogInformation(
                            Utils.TraceMasks.Security,
                            "Add application certificate {Certificate}",
                            addCertificateWithKey);
                        Debug.Assert(addCertificateWithKey.HasPrivateKey);
                        await appStore.AddAsync(
                            addCertificateWithKey,
                            passwordProvider?.GetPassword(existingCertIdentifier),
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception) when (removedCertificate && removedCertificateBackup != null)
                    {
                        // This operation already removed the previous
                        // certificate above before this add failed; self-
                        // compensate by restoring it (see remarks) before
                        // the original exception propagates below.
                        try
                        {
                            await appStore.AddAsync(
                                removedCertificateBackup,
                                passwordProvider?.GetPassword(existingCertIdentifier),
                                ct).ConfigureAwait(false);
                            m_logger.LogWarning(
                                Utils.TraceMasks.Security,
                                "Restored the previous application certificate for {Type} after " +
                                "the replacement failed to commit.",
                                existingCertIdentifier.CertificateType);
                        }
                        catch (Exception restoreException)
                        {
                            m_logger.LogCritical(
                                restoreException,
                                "Failed to restore the previous application certificate for {Type} " +
                                "after the replacement failed to commit. Server configuration may " +
                                "be inconsistent.",
                                existingCertIdentifier.CertificateType);
                        }

                        throw;
                    }
                }
            }

            List<string>? newlyAddedIssuerThumbprints = null;
            if (addIssuerChain is { Count: > 0 })
            {
                using ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
                try
                {
                    foreach (Certificate issuer in addIssuerChain)
                    {
                        bool alreadyPresent;
                        using (CertificateCollection existingMatches = await issuerStore
                            .FindByThumbprintAsync(issuer.Thumbprint, ct).ConfigureAwait(false))
                        {
                            alreadyPresent = existingMatches.Count > 0;
                        }

                        try
                        {
                            await issuerStore.AddAsync(issuer, ct: ct).ConfigureAwait(false);
                        }
                        catch (ArgumentException)
                        {
                            // ignore error if issuer cert already exists
                            alreadyPresent = true;
                        }

                        if (!alreadyPresent)
                        {
                            (newlyAddedIssuerThumbprints ??= []).Add(issuer.Thumbprint);
                        }
                    }
                }
                catch (Exception)
                    when (removedCertificate && addCertificateWithKey != null && removedCertificateBackup != null)
                {
                    // The application certificate slot above was already
                    // fully swapped (the previous certificate removed and
                    // the new one added) before this issuer import failed;
                    // self-compensate by restoring the previous certificate
                    // and removing exactly the issuer certificates this
                    // loop newly added so far (preserving every issuer that
                    // was already present before it ran), before the
                    // original exception propagates below.
                    await RestoreCertificateSlotAfterIssuerImportFailureAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        addCertificateWithKey,
                        removedCertificateBackup,
                        newlyAddedIssuerThumbprints?.ToArrayOf() ?? ArrayOf<string>.Empty,
                        ct).ConfigureAwait(false);

                    throw;
                }
            }

            if (addCertificateWithKey != null)
            {
                if (m_configuration.CertificateManager is ICertificateLifecycle lifecycle)
                {
                    using Certificate certOnly = Certificate.FromRawData(addCertificateWithKey.RawData);
                    await lifecycle.UpdateApplicationCertificateAsync(
                        existingCertIdentifier.CertificateType,
                        certOnly,
                        issuerChain: null,
                        ct).ConfigureAwait(false);
                }
            }
            else if (m_configuration.CertificateManager != null)
            {
                // DeleteCertificate / rollback-of-create leaves nothing to
                // register. ICertificateLifecycle exposes no direct
                // "unregister" primitive, so a reload re-derives the
                // registry from the security configuration's stores; the
                // now-missing certificate file naturally drops this
                // type's entry from the reloaded snapshot.
                await m_configuration.CertificateManager.UpdateAsync(
                    m_configuration.SecurityConfiguration,
                    m_configuration.ApplicationUri,
                    ct).ConfigureAwait(false);
            }

            return newlyAddedIssuerThumbprints?.ToArrayOf() ?? ArrayOf<string>.Empty;
        }

        /// <summary>
        /// Self-compensates a completed application-certificate slot swap
        /// (the previous certificate removed and <paramref name="committedCertificateWithKey"/>
        /// added in its place) once importing that certificate's issuer
        /// chain fails after the swap has already committed: deletes
        /// <paramref name="committedCertificateWithKey"/> from the
        /// application store, restores <paramref name="removedCertificateBackup"/>
        /// in its place, and removes exactly <paramref name="newlyAddedIssuerThumbprints"/>
        /// from the group's issuer store, preserving every issuer that was
        /// already present before the failed import ran.
        /// </summary>
        /// <remarks>
        /// Every step here is best-effort cleanup running after the
        /// triggering issuer-import failure; each stage is isolated so a
        /// failure restoring the application certificate does not prevent
        /// the issuer cleanup from being attempted, and any compensation
        /// failure is only logged (never thrown), so the caller's
        /// <see langword="throw"/> of the original import failure is never
        /// masked or replaced.
        /// </remarks>
        private async Task RestoreCertificateSlotAfterIssuerImportFailureAsync(
            ServerCertificateGroup certificateGroup,
            CertificateIdentifier existingCertIdentifier,
            Certificate committedCertificateWithKey,
            Certificate removedCertificateBackup,
            ArrayOf<string> newlyAddedIssuerThumbprints,
            CancellationToken ct)
        {
            try
            {
                using ICertificateStore? appStore = CertificateIdentifierResolver
                    .OpenStore(existingCertIdentifier, Server.Telemetry);
                if (appStore != null)
                {
                    await appStore.DeleteAsync(committedCertificateWithKey.Thumbprint, ct)
                        .ConfigureAwait(false);
                    ICertificatePasswordProvider? passwordProvider = m_configuration
                        .SecurityConfiguration
                        .CertificatePasswordProvider;
                    await appStore.AddAsync(
                        removedCertificateBackup,
                        passwordProvider?.GetPassword(existingCertIdentifier),
                        ct).ConfigureAwait(false);
                    m_logger.LogWarning(
                        Utils.TraceMasks.Security,
                        "Restored the previous application certificate for {Type} after " +
                        "importing its issuer chain failed to commit.",
                        existingCertIdentifier.CertificateType);
                }
            }
            catch (Exception restoreException)
            {
                m_logger.LogCritical(
                    restoreException,
                    "Failed to restore the previous application certificate for {Type} after " +
                    "importing its issuer chain failed to commit. Server configuration may be " +
                    "inconsistent.",
                    existingCertIdentifier.CertificateType);
            }

            // RemoveIssuerCertificatesAsync never throws (it logs and
            // continues per thumbprint); it is still awaited within its
            // own scope here so a hypothetical future change to that
            // contract can never mask the original issuer-import failure
            // this method was called to compensate.
            await RemoveIssuerCertificatesAsync(certificateGroup, newlyAddedIssuerThumbprints, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes each issuer certificate identified by <paramref name="thumbprints"/>
        /// from <paramref name="certificateGroup"/>'s issuer store.
        /// </summary>
        /// <remarks>
        /// Used to compensate the issuers a completed <c>UpdateCertificate</c>
        /// commit imported via <see cref="ApplyCertificateSlotChangeAsync"/>,
        /// so a reverse-order rollback (or self-compensation) removes
        /// exactly the issuer certificates that commit newly added and
        /// never a pre-existing issuer certificate. A failure to remove
        /// one thumbprint is logged and does not prevent the remaining
        /// thumbprints from being attempted, since these failures are
        /// best-effort cleanup after the more critical application
        /// certificate has already been restored by the caller.
        /// </remarks>
        private async Task RemoveIssuerCertificatesAsync(
            ServerCertificateGroup certificateGroup,
            ArrayOf<string> thumbprints,
            CancellationToken ct)
        {
            if (thumbprints.Count == 0)
            {
                return;
            }

            using ICertificateStore issuerStore = certificateGroup.IssuerStore.OpenStore(Server.Telemetry);
            // Indexed rather than foreach: ArrayOf<T>'s enumerator is a
            // ReadOnlySpan<T>.Enumerator (a ref struct), which cannot be
            // held across the await below.
            for (int i = 0; i < thumbprints.Count; i++)
            {
                string thumbprint = thumbprints[i];
                try
                {
                    await issuerStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogCritical(
                        ex,
                        "Failed to remove a newly staged issuer certificate {Thumbprint} from {Group} " +
                        "while rolling back a PushManagement operation. Server configuration may be " +
                        "inconsistent.",
                        thumbprint,
                        certificateGroup.NodeId);
                }
            }
        }

        /// <summary>
        /// Records that <paramref name="oldCertificateWithKey"/> was
        /// replaced or removed by a just-committed operation so
        /// <c>ApplyChanges</c> can force-close the SecureChannels that were
        /// negotiated against it (OPC UA Part 12 §7.10.9), once the whole
        /// transaction commits successfully. Takes its own reference; the
        /// caller's own copy is unaffected.
        /// </summary>
        /// <remarks>
        /// Adds to the collector that is flowed, through the ambient
        /// async call chain, by whichever call to <c>ApplyChanges</c> is
        /// currently running the coordinator's commit loop. This is
        /// deliberately NOT a single shared/global collection: a
        /// concurrent or duplicate <c>ApplyChanges</c> call that finds no
        /// active transaction (and so short-circuits with
        /// <see cref="StatusCodes.BadNothingToDo"/> without running any
        /// commit) never sees, and can therefore never drain or dispose,
        /// the rotations produced by another call's still-running
        /// successful commit.
        /// </remarks>
        private void RegisterPendingRotation(NodeId certificateType, Certificate oldCertificateWithKey)
        {
            List<PendingCertificateRotation>? collector = m_activeRotationCollector.Value;
            if (collector == null)
            {
                // Not reachable through the standard ApplyChanges method
                // handler, which always sets the collector before running
                // the coordinator's commit loop; nothing to correlate this
                // rotation with.
                return;
            }

            using Certificate rotationCopy = Certificate.FromRawData(oldCertificateWithKey.RawData);
            collector.Add(new PendingCertificateRotation
            {
                OldCertificate = rotationCopy.AddRef(),
                CertificateType = certificateType
            });
        }

        /// <summary>
        /// Conservative OPC 10000-12 §7.10.7 safety check for <c>DeleteCertificate</c>:
        /// rejects deleting the last remaining active application
        /// certificate across every certificate group, since every secure
        /// endpoint would then have no certificate to present.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a deliberately conservative subset of the full "is this
        /// certificate the sole reference of an active endpoint" check,
        /// which would additionally require correlating each endpoint's
        /// configured SecurityPolicyUri to a specific certificate
        /// group/type. It never rejects a delete that the full check would
        /// allow, but a deployment that assigns different certificate
        /// groups to different endpoints could be more permissive than a
        /// full per-endpoint check.
        /// </para>
        /// <para>
        /// The live registry alone is not enough: staging one
        /// <c>DeleteCertificate</c> request per certificate type within
        /// the same transaction would otherwise pass this check
        /// individually for every request (none of the earlier staged
        /// deletes have actually been applied to the live registry yet)
        /// and still leave every certificate-group/type slot empty once
        /// <c>ApplyChanges</c> commits them all together. This check
        /// therefore nets the live registry against every certificate
        /// type already staged (but not yet committed) in the active
        /// transaction, via <see cref="IPushConfigurationTransactionCoordinator.GetStagedOperations"/>,
        /// before deciding whether this additional delete is safe.
        /// </para>
        /// </remarks>
        private void EnsureCertificateNotSoleEndpointReference(NodeId certificateTypeId)
        {
            if (m_configuration.CertificateManager is not ICertificateRegistry registry)
            {
                return;
            }

            var occupiedTypes = new HashSet<NodeId>();
            using (CertificateEntryCollection snapshot = registry.SnapshotApplicationCertificates())
            {
                foreach (CertificateEntry entry in snapshot)
                {
                    occupiedTypes.Add(entry.CertificateType);
                }
            }

            foreach (PushConfigurationOperation staged in m_coordinator.GetStagedOperations())
            {
                if (staged.AffectedCertificateType.IsNull)
                {
                    continue;
                }

                if (staged.LeavesCertificateSlotEmpty)
                {
                    occupiedTypes.Remove(staged.AffectedCertificateType);
                }
                else
                {
                    occupiedTypes.Add(staged.AffectedCertificateType);
                }
            }

            // The delete about to be staged removes this type too.
            occupiedTypes.Remove(certificateTypeId);

            if (occupiedTypes.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "Deleting this certificate would leave the server with no application certificate " +
                    "for any active endpoint.");
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
            CertificateCollection? newIssuerCollection = null;
            Certificate? newCertificateWithKey = null;
            Certificate? previousCertificateWithKey = null;
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

                // OPC 10000-12 §7.10.5: "The Purpose of the associated
                // CertificateGroup determines the validation rules for
                // Certificate being updated."
                bool isApplicationCertificateGroup = IsApplicationCertificateGroup(certificateGroup);

                NodeId sessionId = GetSessionId(context);
                m_coordinator.ValidateSessionCanParticipate(sessionId);

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
                    using (CertificateEntry? currentEntry = registryFallback
                        .AcquireApplicationCertificateByType(certificateTypeId))
                    {
                        if (currentEntry == null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidArgument,
                                "No existing certificate found for the specified certificate type and subject name.");
                        }
                    }

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

                newIssuerCollection = new CertificateCollection();

                if (isApplicationCertificateGroup)
                {
                    // OPC 10000-12 §7.10.5: "If the CertificateGroup Purpose
                    // is ApplicationCertificateType, this list is redundant
                    // because the IssuerCertificates are already required
                    // to be in the associated TrustList, therefore the
                    // Server shall ignore this list." The caller-supplied
                    // issuerCertificates are therefore never parsed, staged,
                    // or imported for this group; newIssuerCollection stays
                    // empty. The Server instead validates newCert using the
                    // validation process defined in OPC 10000-4 against the
                    // group's own configured TrustList, which is
                    // authoritative, ignoring every suppressible validation
                    // error while still enforcing every other error.
                    try
                    {
                        await ValidateCertificateAgainstGroupTrustListAsync(
                            certificateGroup.TrustedStore,
                            certificateGroup.IssuerStore,
                            certificateGroup.BrowseName,
                            newCert,
                            m_configuration.SecurityConfiguration,
                            Server.Telemetry,
                            ct).ConfigureAwait(false);
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
                            "Failed to verify integrity of the new certificate {Certificate} against " +
                            "the certificate group's TrustList.",
                            newCert);
                        throw new ServiceResultException(
                            StatusCodes.BadSecurityChecksFailed,
                            "Failed to verify integrity of the new certificate against the " +
                            "certificate group's TrustList.",
                            ex);
                    }
                }
                else
                {
                    try
                    {
                        // build issuer chain
                        foreach (ByteString issuerRawCert in issuerCertificates)
                        {
                            using Certificate issuerCertificate = Certificate.FromRawData(issuerRawCert);
                            newIssuerCollection.Add(issuerCertificate);
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
                            await ValidatePushCertificateAndIssuerChainAsync(
                                newCert,
                                newIssuerCollection,
                                m_configuration.SecurityConfiguration,
                                Server.Telemetry,
                                ct).ConfigureAwait(false);
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
                }

                // Capture the pre-transaction certificate/private key
                // before any mutation (OPC UA Part 12 §7.10.2) so a
                // reverse-compensation rollback can restore it if a later
                // staged operation in this transaction fails to commit.
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                string? previousThumbprint;
                if (m_configuration.CertificateManager is ICertificateRegistry registry)
                {
                    using CertificateEntry? currentEntry = registry
                        .AcquireApplicationCertificateByType(existingCertIdentifier.CertificateType);
                    previousThumbprint = currentEntry?.Certificate.Thumbprint
                        ?? existingCertIdentifier.Thumbprint;
                }
                else
                {
                    previousThumbprint = existingCertIdentifier.Thumbprint;
                }

                previousCertificateWithKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        ct)
                    .ConfigureAwait(false);

                try
                {
                    switch (privateKeyFormat)
                    {
                        case null:
                        case "":
                            PendingCertificateKeyContext pendingKeyContext =
                                CreatePendingKeyContext(certificateGroup, existingCertIdentifier);
                            Certificate? pendingKey = await m_pendingKeyStore
                                .TryTakeAsync(pendingKeyContext, ct).ConfigureAwait(false);

                            Certificate exportableKey;
                            if (pendingKey != null && X509Utils.VerifyKeyPair(newCert, pendingKey))
                            {
                                // The regenerated key from a matching
                                // CreateSigningRequest(regeneratePrivateKey:
                                // true) is consumed here.
                                exportableKey = pendingKey;
                            }
                            else
                            {
                                pendingKey?.Dispose();
                                // CA2000: exportableKey is disposed by the
                                // `using` immediately below; the analyzer
                                // cannot track disposal through the
                                // conditional (?:) assignment.
#pragma warning disable CA2000
                                exportableKey = previousCertificateWithKey != null
                                    ? X509Utils.CreateCopyWithPrivateKey(previousCertificateWithKey, false)
                                    : throw new ServiceResultException(
                                        StatusCodes.BadSecurityChecksFailed,
                                        "A private key was not found");
#pragma warning restore CA2000
                            }

                            using (exportableKey)
                            {
                                newCertificateWithKey = DefaultCertificateFactory.Instance
                                    .CreateWithPrivateKey(newCert, exportableKey);
                            }
                            break;
                        case "PFX":
                        {
#if !NET9_0_OR_GREATER
                            // https://github.com/OPCFoundation/UA-.NETStandard/commit/0b24d62b7c2bab2e5ed08e694103d49278e457af
                            // CopyWithPrivateKey apparently does not support ephimeralkeysets on windows
                            bool noEphemeralKeySet = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#else
                            // But it seems to work on .net 9 - and we prefer that over files
                            const bool noEphemeralKeySet = false;
#endif
                            using Certificate certWithPrivateKey = X509Utils.CreateCertificateFromPKCS12(
                                privateKey.ToArray(),
                                passwordProvider?.GetPassword(existingCertIdentifier),
                                noEphemeralKeySet);
                            newCertificateWithKey = DefaultCertificateFactory.Instance
                                .CreateWithPrivateKey(newCert, certWithPrivateKey);
                            break;
                        }
                        case "PEM":
                            newCertificateWithKey = DefaultCertificateFactory.Instance
                                .CreateWithPEMPrivateKey(
                                    newCert,
                                    privateKey.ToArray(),
                                    passwordProvider?.GetPassword(existingCertIdentifier));
                            break;
                    }
                }
                catch (Exception ex) when (ex is not ServiceResultException)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "Failed to verify integrity of the new certificate and the private key.", ex);
                }

                NodeId groupNodeId = certificateGroup.NodeId;
                Certificate stagedNewCert = newCertificateWithKey!;
                CertificateCollection stagedIssuers = newIssuerCollection;
                Certificate? stagedPreviousCert = previousCertificateWithKey;
                // Populated by CommitAsync with the thumbprints of exactly
                // the issuer certificates it newly adds (excluding any
                // already present in the issuer store); RollbackAsync only
                // ever runs after CommitAsync has fully completed (the
                // coordinator only reverse-compensates operations that
                // committed in full), so it always reads the value
                // CommitAsync wrote.
                ArrayOf<string> stagedNewlyAddedIssuerThumbprints = ArrayOf<string>.Empty;

                // CA2025: the coordinator guarantees CommitAsync/RollbackAsync
                // always complete (awaited to conclusion) before it invokes
                // DisposeStaged, so stagedNewCert/stagedIssuers/
                // stagedPreviousCert are never disposed while an operation
                // delegate is still using them; the analyzer cannot see
                // across that ordering contract.
#pragma warning disable CA2025
                m_coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup = groupNodeId,
                    AffectedCertificateType = certificateTypeId,
                    CommitAsync = async ct2 =>
                    {
                        stagedNewlyAddedIssuerThumbprints = await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            previousThumbprint,
                            stagedNewCert,
                            stagedIssuers,
                            ct2,
                            stagedPreviousCert).ConfigureAwait(false);
                        if (stagedPreviousCert != null)
                        {
                            RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                        }

                        Server.ReportCertificateUpdatedAuditEvent(
                            context,
                            objectId,
                            method,
                            inputArguments,
                            certificateGroupId,
                            certificateTypeId,
                            m_logger);
                    },
                    RollbackAsync = async ct2 =>
                    {
                        await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            stagedNewCert.Thumbprint,
                            stagedPreviousCert,
                            null,
                            ct2).ConfigureAwait(false);
                        // Remove exactly the issuers the commit above newly
                        // added, preserving every issuer that was already
                        // present in the store before this operation ran.
                        await RemoveIssuerCertificatesAsync(
                            certificateGroup,
                            stagedNewlyAddedIssuerThumbprints,
                            ct2).ConfigureAwait(false);
                    },
                    DisposeStaged = () =>
                    {
                        stagedNewCert.Dispose();
                        stagedIssuers.Dispose();
                        stagedPreviousCert?.Dispose();
                    }
                });
#pragma warning restore CA2025

                // Ownership of these transferred to the staged operation
                // above; clear the local handles so the outer finally does
                // not double-dispose them.
                newCertificateWithKey = null;
                newIssuerCollection = null;
                previousCertificateWithKey = null;
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
                newCert?.Dispose();
                // Disposed only when ownership was not transferred to the
                // staged operation (i.e. an exception occurred before staging).
                newIssuerCollection?.Dispose();
                newCertificateWithKey?.Dispose();
                previousCertificateWithKey?.Dispose();
            }

            return new UpdateCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                ApplyChangesRequired = true
            };
        }

        internal static async Task ValidatePushCertificateAndIssuerChainAsync(
            Certificate newCertificate,
            CertificateCollection issuerCertificates,
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            if (issuerCertificates == null)
            {
                throw new ArgumentNullException(nameof(issuerCertificates));
            }

            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            using CertificateCollection validationChain = issuerCertificates.AddRef();
            validationChain.Insert(0, newCertificate);

            using var validator = CertificateManagerFactory.Create(securityConfiguration, telemetry);
            var options = new Security.Certificates.CertificateValidationOptions
            {
                AllowCertificateDownload = false,
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1),
                AcceptError = static (_, serviceResult) =>
                    serviceResult.StatusCode == StatusCodes.BadCertificateUntrusted
            };

            CertificateValidationResult validationResult = await validator.ValidateAsync(
                validationChain,
                trustList: null,
                options: options,
                ct).ConfigureAwait(false);

            validationResult.ThrowIfInvalid();
        }

        /// <summary>
        /// Determines whether <paramref name="certificateGroup"/>'s Purpose
        /// is <c>ApplicationCertificateType</c> per OPC 10000-12 §7.10.5,
        /// i.e. whether it is the standard <c>DefaultApplicationGroup</c>
        /// used for the Server's own ApplicationInstance Certificates, as
        /// opposed to a group used for another purpose (HTTPS, user
        /// credentials).
        /// </summary>
        private static bool IsApplicationCertificateGroup(ServerCertificateGroup certificateGroup)
        {
            return Utils.IsEqual(
                certificateGroup.NodeId,
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup);
        }

        /// <summary>
        /// Validates <paramref name="newCertificate"/> against the TrustList
        /// (<paramref name="trustedStore"/>/<paramref name="issuerStore"/>)
        /// associated with a certificate group whose Purpose is
        /// <c>ApplicationCertificateType</c>, per OPC 10000-12 §7.10.5: "the
        /// Server shall verify the Certificate using the validation process
        /// defined in OPC 10000-4. All suppressible errors shall be
        /// ignored; however, they may be logged as warnings. If the
        /// validation fails, the appropriate StatusCode defined in
        /// OPC 10000-4 shall be reported. The validation process requires
        /// that the TrustList associated with the CertificateGroup already
        /// contains the IssuerCertificates."
        /// </summary>
        /// <remarks>
        /// Delegates entirely to the shared certificate validator's own
        /// suppressible-status-code classification (accepting every error
        /// it reports as suppressible) rather than maintaining a second
        /// hard-coded status list here: anything the validator does not
        /// classify as suppressible (key size, certificate type, signature
        /// integrity, URI/hostname requirements, and so on) still fails
        /// before this method's <c>AcceptError</c> callback is ever
        /// consulted.
        /// </remarks>
        /// <exception cref="ServiceResultException">
        /// Thrown when validation fails with a non-suppressible error.
        /// </exception>
        internal static async Task ValidateCertificateAgainstGroupTrustListAsync(
            CertificateStoreIdentifier trustedStore,
            CertificateStoreIdentifier? issuerStore,
            string trustListName,
            Certificate newCertificate,
            SecurityConfiguration securityConfiguration,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            if (trustedStore == null)
            {
                throw new ArgumentNullException(nameof(trustedStore));
            }

            if (string.IsNullOrEmpty(trustListName))
            {
                throw new ArgumentException(
                    "Trust list name must not be null or empty.",
                    nameof(trustListName));
            }

            if (newCertificate == null)
            {
                throw new ArgumentNullException(nameof(newCertificate));
            }

            if (securityConfiguration == null)
            {
                throw new ArgumentNullException(nameof(securityConfiguration));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            var trustList = new TrustListIdentifier(trustListName);
            using CertificateManager validator = CertificateManagerFactory.Create(
                securityConfiguration,
                telemetry,
                managerOptions => managerOptions.AddTrustList(
                    trustList.Name,
                    trustedStore.StorePath!,
                    issuerStore?.StorePath));

            using var validationChain = new CertificateCollection { newCertificate };

            var options = new Security.Certificates.CertificateValidationOptions
            {
                AllowCertificateDownload = false,
                UrlRetrievalTimeout = TimeSpan.FromMilliseconds(1),
                // OPC 10000-12 §7.10.5: "All suppressible errors shall be
                // ignored."
                AcceptError = static (_, _) => true
            };

            CertificateValidationResult validationResult = await validator.ValidateAsync(
                validationChain,
                trustList: trustList,
                options: options,
                ct).ConfigureAwait(false);

            validationResult.ThrowIfInvalid();
        }

        /// <summary>
        /// Builds a suitable default SubjectName for an ApplicationCertificateType
        /// slot when the caller omits one, per OPC 10000-12 §7.10.6/§7.10.21:
        /// a subject derived from the Server's ApplicationIdentity (here,
        /// its configured application name).
        /// </summary>
        internal static string CreateDefaultApplicationCertificateSubjectName(string? applicationName)
        {
            if (string.IsNullOrEmpty(applicationName))
            {
                applicationName = "UA Server";
            }

            // Distinguished-name field separators/control characters are not
            // valid inside a single RDN value.
            var sanitized = new StringBuilder(applicationName!.Length);
            foreach (char ch in applicationName)
            {
                sanitized.Append(char.IsControl(ch) || ch is '/' or ',' or ';' ? '+' : ch);
            }

            return Utils.Format("CN={0}, O=OPC Foundation", sanitized);
        }

        /// <summary>
        /// Determines whether <paramref name="subjectName"/>'s common name
        /// (the <c>CN=</c> field) equals one of <paramref name="domainNames"/>,
        /// per OPC 10000-12 §7.10.6: "For HttpsCertificateTypes the
        /// SubjectName shall be specified and have the dnsName or IP
        /// Address as the common name."
        /// </summary>
        internal static bool SubjectCommonNameMatchesDomain(
            string subjectName,
            IEnumerable<string> domainNames)
        {
            string? commonName = null;
            foreach (string field in X509Utils.ParseDistinguishedName(subjectName))
            {
                if (field.StartsWith("CN=", StringComparison.Ordinal))
                {
                    commonName = field[3..].Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(commonName))
            {
                return false;
            }

            foreach (string domainName in domainNames)
            {
                if (string.Equals(domainName, commonName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Validates <paramref name="keySizeInBits"/> against the set of
        /// key sizes permitted for <paramref name="certificateTypeId"/> per
        /// OPC 10000-12 §7.10.6: "The CertificateTypeId limits the values
        /// that may be set." A value of 0 (use a suitable default) is
        /// always permitted.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadOutOfRange"/> when
        /// <paramref name="keySizeInBits"/> is not supported for the
        /// specified certificate type.
        /// </exception>
        internal static void ValidateKeySizeForCertificateType(
            NodeId certificateTypeId,
            bool isRsaCertificateType,
            ushort keySizeInBits)
        {
            if (keySizeInBits == 0)
            {
                return;
            }

            bool supported;
            if (isRsaCertificateType)
            {
                supported = certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType
                    ? keySizeInBits is 1024 or 2048
                    : certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType
                        ? keySizeInBits is 2048 or 3072 or 4096
                        : keySizeInBits is 1024 or 2048 or 3072 or 4096;
            }
            else if (certificateTypeId == ObjectTypeIds.EccNistP256ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
            {
                supported = keySizeInBits == 256;
            }
            else if (certificateTypeId == ObjectTypeIds.EccNistP384ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType)
            {
                supported = keySizeInBits == 384;
            }
            else
            {
                // An unrecognized ECC certificate type; CryptoUtils.GetCurveFromCertificateTypeId
                // reports Bad_NotSupported once certificate construction is attempted.
                return;
            }

            if (!supported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange,
                    Utils.Format(
                        "The keySizeInBits value {0} is not supported for the specified certificate type.",
                        keySizeInBits));
            }
        }

        /// <summary>
        /// Creates a new self-signed certificate per OPC 10000-12 §7.10.6.
        /// The server generates a key pair internally, builds a self-signed
        /// certificate with the requested subject / DNS / IP and lifetime,
        /// asynchronously verifies the target slot is not occupied (a
        /// self-signed certificate never replaces an occupied slot; use
        /// <c>DeleteCertificate</c> first) - netted against every operation
        /// already staged in the active transaction (see
        /// <see cref="IsSlotOccupiedAsync"/>), so a <c>DeleteCertificate</c>
        /// staged earlier in the same transaction for this slot permits
        /// this call even though nothing has actually been removed from
        /// the store/registry yet - stages the new private-key
        /// certificate (also removing, and restoring on a later rollback,
        /// whatever the slot still genuinely holds live in that case),
        /// and returns the DER-encoded public certificate.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<CreateSelfSignedCertificateMethodStateResult>
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

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId)!;

            // merge DNS names and IP addresses into one domain list. OPC
            // 10000-12 §7.10.6 requires at least one non-empty entry
            // across both lists, regardless of certificate type.
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

            if (domainNames.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "At least one DNS name or IP address must be provided.");
            }

            bool isHttpsCertificateType = certificateTypeId == ObjectTypeIds.HttpsCertificateType;
            if (string.IsNullOrEmpty(subjectName))
            {
                if (isHttpsCertificateType)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidArgument,
                        "SubjectName must be provided for HTTPS certificate types.");
                }

                // OPC 10000-12 §7.10.6/§7.10.21: for ApplicationCertificateTypes
                // the SubjectName may be omitted; the Server creates a
                // suitable default based on the Server's ApplicationIdentity.
                subjectName = CreateDefaultApplicationCertificateSubjectName(m_configuration.ApplicationName);
            }
            else if (isHttpsCertificateType && !SubjectCommonNameMatchesDomain(subjectName, domainNames))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "For HTTPS certificate types the SubjectName common name must match a " +
                    "supplied DNS name or IP address.");
            }

            // OPC 10000-12 §7.10.6: "keySizeInBits ... The CertificateTypeId
            // limits the values that may be set." Validated before invoking
            // the builder so an unsupported value is reported as
            // Bad_OutOfRange rather than a raw ArgumentException from the
            // certificate builder (or silently accepted for ECC types).
            bool isRsaCertificateType = certificateTypeId.IsNull ||
                certificateTypeId == ObjectTypeIds.ApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType ||
                certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType;
            ValidateKeySizeForCertificateType(certificateTypeId, isRsaCertificateType, keySizeInBits);

            NodeId sessionId = GetSessionId(context);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            // OPC 10000-12 §7.10.6: never replace an occupied slot;
            // DeleteCertificate is the standard mechanism to empty one.
            // Netted against every operation already staged in this
            // transaction, so a DeleteCertificate staged earlier for this
            // same slot permits this call to proceed.
            (bool occupied, string? previousThumbprint) = await IsSlotOccupiedAsync(
                certificateGroup.NodeId, existingCertIdentifier, cancellationToken).ConfigureAwait(false);
            if (occupied)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate slot is already occupied. Use DeleteCertificate to empty it first.");
            }

            // The slot may still genuinely hold a certificate on disk/the
            // registry even though it is not occupied above: an earlier
            // DeleteCertificate staged for this same slot in this
            // transaction nets it as unoccupied without having actually
            // removed anything from the store yet. Capture that
            // certificate now so this operation can restore it - instead
            // of just discarding the newly created one and leaving the
            // slot empty - if a later staged operation in the same
            // transaction fails and this one must be rolled back.
            Certificate? previousCertificateWithKey = string.IsNullOrEmpty(previousThumbprint)
                ? null
                : await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        m_configuration.SecurityConfiguration.CertificatePasswordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false);

            if (lifetimeInDays == 0)
            {
                lifetimeInDays = CertificateFactory.DefaultLifeTime;
            }

            DateTime utcToday = m_timeProvider.GetUtcNow().UtcDateTime.Date;
            ICertificateBuilder builder = s_certificateFactory
                .CreateApplicationCertificate(
                    m_configuration.ApplicationUri!,
                    m_configuration.ApplicationName!,
                    subjectName,
                    [.. domainNames])
                .SetNotBefore(utcToday.AddDays(-1))
                .SetNotAfter(utcToday.AddDays(lifetimeInDays));

            Certificate certificateWithKey;
            if (isRsaCertificateType)
            {
                ushort keySize = keySizeInBits > 0
                    ? keySizeInBits
                    : CertificateFactory.DefaultKeySize;
                certificateWithKey = builder.SetRSAKeySize(keySize).CreateForRSA();
            }
            else
            {
                ECCurve? curve =
                    CryptoUtils.GetCurveFromCertificateTypeId(certificateTypeId)
                    ?? throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The ECC certificate type is not supported.");
                certificateWithKey = builder.SetECCurve(curve.Value).CreateForECDsa();
            }

            ByteString certBytes;
            try
            {
                certBytes = certificateWithKey.RawData.ToByteString();

                m_logger.LogInformation(
                    Utils.TraceMasks.Security,
                    "Staged self-signed certificate {Subject} for {Group}/{Type}.",
                    certificateWithKey.Subject,
                    certificateGroupId,
                    certificateTypeId);

                NodeId groupNodeId = certificateGroup.NodeId;
                Certificate stagedNewCert = certificateWithKey;
                Certificate? stagedPreviousCert = previousCertificateWithKey;
                // CA2025: the coordinator guarantees CommitAsync/RollbackAsync
                // complete before DisposeStaged runs; see the identical
                // suppression in UpdateCertificateAsync for the full
                // rationale.
#pragma warning disable CA2025
                m_coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedCertificateGroup = groupNodeId,
                    AffectedCertificateType = certificateTypeId,
                    CommitAsync = async ct =>
                    {
                        await ApplyCertificateSlotChangeAsync(
                            certificateGroup,
                            existingCertIdentifier,
                            previousThumbprint,
                            stagedNewCert,
                            null,
                            ct,
                            stagedPreviousCert).ConfigureAwait(false);
                        if (stagedPreviousCert != null)
                        {
                            RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                        }
                    },
                    // Mirrors the commit's before/after roles: when this
                    // slot genuinely held stagedPreviousCert live (a
                    // DeleteCertificate staged earlier in this same
                    // transaction had netted the slot as unoccupied without
                    // having actually removed it yet), restore it instead
                    // of leaving the slot empty; otherwise there was
                    // nothing live to restore.
                    RollbackAsync = ct => ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        stagedNewCert.Thumbprint,
                        stagedPreviousCert,
                        null,
                        ct),
                    DisposeStaged = () =>
                    {
                        stagedNewCert.Dispose();
                        stagedPreviousCert?.Dispose();
                    }
                });
#pragma warning restore CA2025

                // Ownership transferred to the staged operation above;
                // clear the local handle so the finally below does not
                // double-dispose it.
                previousCertificateWithKey = null;
            }
            catch
            {
                certificateWithKey.Dispose();
                throw;
            }
            finally
            {
                // Disposed only when ownership was not transferred to the
                // staged operation (i.e. an exception occurred before staging).
                previousCertificateWithKey?.Dispose();
            }

            // The slot's future content no longer comes from a pending
            // signing request; discard any pending regenerated key for it.
            await m_pendingKeyStore
                .RemoveAsync(CreatePendingKeyContext(certificateGroup, existingCertIdentifier), cancellationToken)
                .ConfigureAwait(false);

            return new CreateSelfSignedCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Certificate = certBytes
            };
        }

        /// <summary>
        /// Deletes the certificate occupying a certificate group/type slot
        /// per OPC 10000-12 §7.10.7. Unlike <c>CreateSelfSignedCertificate</c>,
        /// this is the standard mechanism for emptying an occupied slot; it
        /// always requires <c>ApplyChanges</c> to take effect. The
        /// occupied-slot check is netted against every operation already
        /// staged in the active transaction (see <see cref="IsSlotOccupiedAsync"/>),
        /// so a <c>CreateSelfSignedCertificate</c> staged earlier in the
        /// same transaction for this slot permits this call even though
        /// nothing has actually been added to the store/registry yet.
        /// </summary>
        private async ValueTask<DeleteCertificateMethodStateResult> DeleteCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId)!;

            NodeId sessionId = GetSessionId(context);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            (bool occupied, string? previousThumbprint) = await IsSlotOccupiedAsync(
                certificateGroup.NodeId, existingCertIdentifier, cancellationToken).ConfigureAwait(false);
            if (!occupied)
            {
                // OPC 10000-12 §7.10.7: "If no Certificate is assigned to
                // the CertificateType slot then a Bad_InvalidState error is
                // returned."
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate slot is already empty.");
            }

            // Deferred to staging time (i.e. now) rather than commit time,
            // using the server's current endpoint/registry state netted
            // against every certificate type already staged in this
            // transaction, so the administrator gets immediate feedback.
            EnsureCertificateNotSoleEndpointReference(certificateTypeId);

            ICertificatePasswordProvider? passwordProvider = m_configuration
                .SecurityConfiguration
                .CertificatePasswordProvider;
            Certificate? previousCertificateWithKey = await CertificateIdentifierResolver
                .LoadPrivateKeyAsync(
                    existingCertIdentifier,
                    passwordProvider,
                    m_configuration.ApplicationUri,
                    Server.Telemetry,
                    cancellationToken)
                .ConfigureAwait(false);

            NodeId groupNodeId = certificateGroup.NodeId;
            Certificate? stagedPreviousCert = previousCertificateWithKey;
            m_coordinator.Stage(sessionId, new PushConfigurationOperation
            {
                AffectedCertificateGroup = groupNodeId,
                AffectedCertificateType = certificateTypeId,
                LeavesCertificateSlotEmpty = true,
                CommitAsync = async ct =>
                {
                    await ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        previousThumbprint,
                        null,
                        null,
                        ct).ConfigureAwait(false);
                    if (stagedPreviousCert != null)
                    {
                        RegisterPendingRotation(certificateTypeId, stagedPreviousCert);
                    }
                },
                RollbackAsync = stagedPreviousCert == null
                    ? null
                    : ct => ApplyCertificateSlotChangeAsync(
                        certificateGroup,
                        existingCertIdentifier,
                        null,
                        stagedPreviousCert,
                        null,
                        ct),
                DisposeStaged = () => stagedPreviousCert?.Dispose()
            });

            await m_pendingKeyStore
                .RemoveAsync(CreatePendingKeyContext(certificateGroup, existingCertIdentifier), cancellationToken)
                .ConfigureAwait(false);

            return new DeleteCertificateMethodStateResult
            {
                ServiceResult = ServiceResult.Good
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
            ByteString nonce,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId)!;

            // OPC 10000-12 §7.10.10: while a transaction is active, only
            // its owning Session may regenerate the pending key, since a
            // second Session's ApplyChanges/CancelChanges could otherwise
            // race the pending-key lifecycle.
            NodeId sessionId = GetSessionId(context);
            m_coordinator.ValidateSessionCanParticipate(sessionId);

            CertificateIdentifier existingCertIdentifier =
                FindCertificateIdentifier(certificateGroup, certificateTypeId);

            // Look up the currently-active certificate via the manager
            // registry — the configured identifier is metadata only. The
            // acquired entry is disposed at method scope; the borrowed
            // certificate is only read.
            using CertificateEntry? currentEntry =
                (m_configuration.CertificateManager as ICertificateRegistry)
                    ?.AcquireApplicationCertificateByType(certificateTypeId);
            Certificate? currentCert = currentEntry?.Certificate;

            if (string.IsNullOrEmpty(subjectName))
            {
                subjectName = (currentCert?.Subject ?? existingCertIdentifier.SubjectName)!;
            }

            PendingCertificateKeyContext pendingKeyContext =
                CreatePendingKeyContext(certificateGroup, existingCertIdentifier);

            Certificate certWithPrivateKey;
            if (regeneratePrivateKey)
            {
                ArrayOf<string> domainNames = currentCert != null
                    ? X509Utils.GetDomainsFromCertificate(currentCert)
                    : default;

                certWithPrivateKey = GenerateTemporaryApplicationCertificate(
                    certificateTypeId,
                    subjectName,
                    domainNames);

                // A repeated signing request replaces (and disposes) any
                // previously pending key for this slot (§7.10.10).
                if (!await m_pendingKeyStore
                    .SaveAsync(pendingKeyContext, certWithPrivateKey, cancellationToken)
                    .ConfigureAwait(false))
                {
                    certWithPrivateKey.Dispose();
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Secure persistence of the regenerated private key is not supported " +
                        "for this certificate store.");
                }
            }
            else
            {
                ICertificatePasswordProvider? passwordProvider = m_configuration
                    .SecurityConfiguration
                    .CertificatePasswordProvider;
                certWithPrivateKey = await CertificateIdentifierResolver
                    .LoadPrivateKeyAsync(
                        existingCertIdentifier,
                        passwordProvider,
                        m_configuration.ApplicationUri,
                        Server.Telemetry,
                        cancellationToken)
                    .ConfigureAwait(false) ??
                    throw ServiceResultException.Create(StatusCodes.BadInternalError, "Failed to load private key");

                // No regenerated key accompanies this request; discard any
                // previously pending one so a later UpdateCertificate does
                // not pick up a stale key.
                await m_pendingKeyStore.RemoveAsync(pendingKeyContext, cancellationToken).ConfigureAwait(false);
            }

            try
            {
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
            finally
            {
                certWithPrivateKey.Dispose();
            }
        }

        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames)
        {
            Certificate certificate;

            DateTime utcToday = m_timeProvider.GetUtcNow().UtcDateTime.Date;
            ICertificateBuilder certificateBuilder = s_certificateFactory
                .CreateApplicationCertificate(
                    m_configuration.ApplicationUri!,
                    m_configuration.ApplicationName!,
                    subjectName,
                    domainNames.ToArray())
                .SetNotBefore(utcToday.AddDays(-1))
                .SetNotAfter(utcToday.AddDays(14));

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

            return certificate;
        }

        /// <summary>
        /// Commits the active PushManagement transaction (OPC UA Part 12
        /// §7.10.2). Runs every staged certificate/TrustList operation's
        /// commit in request order (reverse-compensating on failure),
        /// updates <c>TransactionDiagnostics</c>, and — once the commit
        /// succeeds — schedules the post-response SecureChannel
        /// renegotiation for every rotated certificate (§7.10.9).
        /// </summary>
        private async ValueTask<ServiceResult> ApplyChangesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            NodeId sessionId = GetSessionId(context);

            // A fresh collector for this call alone, flowed to
            // RegisterPendingRotation via the ambient async call chain
            // (see m_activeRotationCollector) rather than a shared field:
            // a concurrent/duplicate ApplyChanges call that finds no
            // active transaction owned by its Session short-circuits
            // through the coordinator without ever running a commit, so
            // it always observes its OWN empty collector and can never
            // drain or dispose the rotations produced by this call.
            var rotations = new List<PendingCertificateRotation>();
            ServiceResult result;
            try
            {
                m_activeRotationCollector.Value = rotations;
                result = await m_coordinator.ApplyChangesAsync(sessionId, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_activeRotationCollector.Value = null;
            }

            UpdateTransactionDiagnostics(context);

            if (!ServiceResult.IsGood(result))
            {
                // The transaction failed and was reverse-compensated;
                // nothing actually rotated, so any provisionally-recorded
                // rotations must be discarded rather than scheduled.
                foreach (PendingCertificateRotation rotation in rotations)
                {
                    rotation.OldCertificate?.Dispose();
                }
                return result;
            }

            if (rotations.Count > 0)
            {
                // Schedule the deferred apply: wait a short grace period for
                // the method response to be flushed, then re-sync the
                // certificate manager from disk and force-close every
                // SecureChannel that was negotiated against the rotated
                // certificate(s). The completion handle is exposed via
                // DrainPendingApplyChangesAsync so tests and hosts can
                // deterministically await rotation rather than racing the
                // delay.
                ScheduleDeferredApplyChanges(rotations);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Cancels (discards, without applying) the active PushManagement
        /// transaction owned by the calling Session (OPC UA Part 12
        /// §7.10.2/§7.10.11).
        /// </summary>
        private ValueTask<ServiceResult> CancelChangesAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            HasApplicationSecureAdminAccess(context);

            NodeId sessionId = GetSessionId(context);
            ServiceResult result = m_coordinator.CancelChanges(sessionId);
            UpdateTransactionDiagnostics(context);
            return new ValueTask<ServiceResult>(result);
        }

        /// <summary>
        /// Schedules the post-response cert-rotation fan-out. Chains
        /// onto any already-running deferred apply so concurrent calls
        /// to <see cref="ApplyChangesAsync"/> run sequentially.
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
                                    = await rotator.CloseChannelsForCertificateAsync(rotation.OldCertificate)
                                        .ConfigureAwait(false);
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
        /// Captured payload for a single certificate-group rotation
        /// scheduled by <see cref="ApplyChangesAsync"/>. The deferred apply
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

            // Look up each certificate via the manager registry so the
            // returned blobs reflect the currently-active cert (the
            // configured identifier carries no Certificate cache).
            var registry = m_configuration.CertificateManager as ICertificateRegistry;
            (certificateTypeIds, certificates) = SelectOccupiedCertificateSlots(
                certificateGroup.ApplicationCertificates,
                certificateType => registry?.AcquireApplicationCertificateByType(certificateType));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Builds the aligned (CertificateTypeIds, Certificates) pair
        /// returned by <c>GetCertificates</c> from only the currently
        /// occupied slots in <paramref name="applicationCertificates"/>,
        /// preserving configured order. A configured placeholder slot
        /// whose <paramref name="resolveActiveCertificate"/> resolves to
        /// <see langword="null"/> (no active certificate) is omitted
        /// rather than reported with an empty <see cref="ByteString"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="resolveActiveCertificate"/> is
        /// <see langword="null"/>.
        /// </exception>
        internal static (ArrayOf<NodeId> CertificateTypeIds, ArrayOf<ByteString> Certificates)
            SelectOccupiedCertificateSlots(
                ArrayOf<CertificateIdentifier> applicationCertificates,
                Func<NodeId, CertificateEntry?> resolveActiveCertificate)
        {
            if (resolveActiveCertificate == null)
            {
                throw new ArgumentNullException(nameof(resolveActiveCertificate));
            }

            var occupiedTypes = new List<NodeId>();
            var occupiedCerts = new List<ByteString>();

            foreach (CertificateIdentifier appId in applicationCertificates)
            {
                using CertificateEntry? entry = resolveActiveCertificate(appId.CertificateType);
                if (entry?.Certificate == null)
                {
                    continue;
                }

                occupiedTypes.Add(appId.CertificateType);
                occupiedCerts.Add(entry.Certificate.RawData.ToByteString());
            }

            return (occupiedTypes.ToArrayOf(), occupiedCerts.ToArrayOf());
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
        }

#pragma warning disable CA2213 // m_serverConfigurationNode is owned by the address space, not by this manager.
        private ServerConfigurationState? m_serverConfigurationNode;
        private UserManagement.UserManagementBinding? m_userManagementBinding;
#pragma warning restore CA2213
        private readonly ApplicationConfiguration m_configuration;
        private readonly TimeProvider m_timeProvider;
        private readonly IPushConfigurationTransactionCoordinator m_coordinator;
        private readonly IPendingCertificateKeyStore m_pendingKeyStore;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier? m_rejectedStore;
        private ITimer? m_alarmTimer;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_namespaceMetadataStatesByIndex = [];
        private readonly Lock m_namespaceMetadataStatesLock = new();
        private readonly Lock m_pendingApplyChangesLock = new();
        private Task m_pendingApplyChangesTask = Task.CompletedTask;
        private readonly AsyncLocal<List<PendingCertificateRotation>?> m_activeRotationCollector = new();

        /// <inheritdoc/>
        public TimeSpan ApplyChangesGracePeriod { get; set; }
            = TimeSpan.FromMilliseconds(250);

        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;
    }
}
