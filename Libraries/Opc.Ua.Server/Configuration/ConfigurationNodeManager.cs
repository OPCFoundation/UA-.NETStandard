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
        /// <param name="keyGenerator">
        /// The generator that creates the regenerated signing-request key,
        /// genuinely incorporating the caller-supplied nonce entropy
        /// (§7.10.10). When <see langword="null"/>, a private
        /// <see cref="AdditionalEntropyCertificateKeyGenerator"/> is created.
        /// </param>
        /// <param name="trustListEffectHandler">
        /// Applies the post-<c>ApplyChanges</c> TrustList effects of
        /// §7.10.9 (force affected SecureChannels to renegotiate; close
        /// Sessions/Subscriptions whose certificate user identity is no
        /// longer valid). When <see langword="null"/>, a private
        /// <see cref="PushConfigurationTrustListEffectHandler"/> is created.
        /// </param>
        /// <param name="serverConfigurationOptions">
        /// Configures the Optional <c>ServerConfigurationType</c> surface of
        /// OPC 10000-12 §7.10.3: the <c>HasSecureElement</c> and
        /// <c>InApplicationSetup</c> Properties, the
        /// <c>ResetToServerDefaults</c> Method (§7.10.13), and the
        /// <c>ConfigurationFile</c> Object (§7.10.20). Each member is only
        /// exposed when configured; when <see langword="null"/> none of those
        /// Optional members are exposed. The identity Properties
        /// (<c>ApplicationUri</c>, <c>ProductUri</c>, <c>ApplicationType</c>,
        /// <c>ApplicationNames</c>) are always exposed from the
        /// <see cref="ApplicationConfiguration"/>.
        /// </param>
        public ConfigurationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            TimeProvider? timeProvider,
            IPushConfigurationTransactionCoordinator? coordinator,
            IPendingCertificateKeyStore? pendingKeyStore,
            IPushCertificateKeyGenerator? keyGenerator = null,
            IPushConfigurationTrustListEffectHandler? trustListEffectHandler = null,
            ServerConfigurationOptions? serverConfigurationOptions = null)
            : base(server, configuration, logger, timeProvider)
        {
            m_timeProvider = timeProvider
                ?? (server as ITimeProviderProvider)?.TimeProvider
                ?? TimeProvider.System;
            m_coordinator = coordinator
                ?? new PushConfigurationTransactionCoordinator(server.Telemetry, m_timeProvider);
            m_pendingKeyStore = pendingKeyStore ?? new DirectoryPendingCertificateKeyStore();
            m_keyGenerator = keyGenerator ?? new AdditionalEntropyCertificateKeyGenerator();
            m_trustListEffectHandler = trustListEffectHandler
                ?? new PushConfigurationTrustListEffectHandler(server.Telemetry);
            m_serverConfigurationOptions = serverConfigurationOptions ?? new ServerConfigurationOptions();
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

                            // OPC 10000-12 §7.10.3 identity Properties are always
                            // known from the ApplicationConfiguration and are
                            // therefore always exposed.
                            activeNode
                                .AddApplicationUri(context)
                                .AddProductUri(context)
                                .AddApplicationType(context)
                                .AddApplicationNames(context);

                            // The remaining Optional members are only exposed
                            // when configured (provider/value supplied); otherwise
                            // the optional child is suppressed.
                            if (m_serverConfigurationOptions.HasSecureElement.HasValue)
                            {
                                activeNode.AddHasSecureElement(context);
                            }
                            if (m_serverConfigurationOptions.InApplicationSetup.HasValue)
                            {
                                activeNode.AddInApplicationSetup(context);
                            }
                            if (m_serverConfigurationOptions.ResetProvider != null)
                            {
                                activeNode.AddResetToServerDefaults(context);
                            }
                            if (m_serverConfigurationOptions.ConfigurationFileProvider != null)
                            {
                                activeNode.AddConfigurationFile(context);
                            }

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

                // Releases the ConfigurationFile handler (§7.10.20): cancels any
                // pending confirm/revert timer, disposes the open write stream
                // and the activity timer.
                m_configurationFile?.Dispose();
                m_configurationFile = null;

                // Cancels and disposes any transaction still active
                // (staged certificate/TrustList operations) so their
                // captured certificates and streams do not leak. Any
                // rotations produced by a commit are always drained and
                // handled (disposed or scheduled) by that same call to
                // ApplyChangesAsync below before it returns, so there is
                // no separate global rotation list to clean up here.
                m_coordinator.Reset();

                // Signal any in-flight deferred ApplyChanges effects to stop
                // so they never run against listeners/managers being disposed.
                // DeleteAddressSpaceAsync drains the task deterministically as
                // part of the async server shutdown; Dispose only signals
                // (it must never block on async work), which also covers the
                // direct-construction path where DeleteAddressSpaceAsync is
                // not invoked.
                CancelPendingApplyChanges();
                m_shutdownCts.Dispose();

                StopAlarmMonitoring();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Loads the predefined configuration nodes and then creates the
        /// optional per-certificate-group alarm instances
        /// (<c>CertificateExpired</c> and <c>TrustListOutOfDate</c>,
        /// OPC 10000-12 §7.8.3). The alarm nodes are created here - once the
        /// certificate-group nodes exist - and initialized in an inactive,
        /// event-free state. Periodic monitoring is started later, after the
        /// server is fully running (see <see cref="StartAlarmMonitoring"/>).
        /// </summary>
        /// <param name="externalReferences">The external references collection.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);

            await CreateCertificateAlarmsAsync(
                SystemContext,
                externalReferences,
                cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Drains any deferred post-<c>ApplyChanges</c> effects (OPC UA Part 12
        /// §7.10.9) before the address space is torn down, integrating with the
        /// async server shutdown lifecycle
        /// (<see cref="MasterNodeManager.ShutdownAsync"/>). The pending effects
        /// are first signalled to stop - so a long grace period does not delay
        /// shutdown and no effect runs against a listener/manager that is about
        /// to be disposed - and then awaited to completion.
        /// </summary>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            StopAlarmMonitoring();
            CancelPendingApplyChanges();

            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingApplyChangesTask;
            }

            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A faulted deferred apply is already logged where it runs;
                // never let it abort the shutdown drain.
                m_logger.DeferredApplyChangesFaultedDuringShutdown(ex);
            }

            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Signals any in-flight deferred <c>ApplyChanges</c> effects to stop,
        /// tolerating an already-disposed cancellation source.
        /// </summary>
        private void CancelPendingApplyChanges()
        {
            try
            {
                m_shutdownCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed via Dispose(bool); nothing to cancel.
            }
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

            // §7.10.20: an abandoned Session must not leave the ConfigurationFile
            // permanently open for writing (which would block ApplyChanges).
            m_configurationFile?.NotifySessionClosing(sessionId);

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
            // OPC 10000-12 §8.4.5: MaxTrustListSize is the maximum TrustList
            // size, in bytes, a Client may write (0 = unlimited). The server
            // bounds actual enforcement by a resource-protection safety ceiling,
            // so advertise the honest effective limit — the value the TrustList
            // handlers actually enforce — instead of a raw 0 while a hidden cap
            // is in force.
            int effectiveMaxTrustListSize = TrustList.ComputeEffectiveMaxTrustListSize(
                configuration.ServerConfiguration!.MaxTrustListSize,
                m_serverConfigurationOptions.MaxTrustListSizeSafetyCeiling);
            configNode.MaxTrustListSize!.Value = (uint)effectiveMaxTrustListSize;
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

            ConfigureOptionalServerConfigurationSurface(systemContext, configNode, configuration);

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
                    m_configuration.ServerConfiguration!.MaxTrustListSize,
                    m_serverConfigurationOptions.MaxTrustListSizeSafetyCeiling);
                certGroup.Node.ClearChangeMasks(systemContext, true);
            }

            // OPC 10000-12 §7.8.3: publish the current certificate and CRL
            // state onto the optional alarm inputs (ExpirationDate,
            // TrustListId, LastUpdateTime) and establish the baseline
            // inactive state. Events are suppressed here (emitEvents: false)
            // because the subscription infrastructure is not yet ready during
            // CreateAddressSpace; StartAlarmMonitoring re-evaluates with events
            // once the server is running.
            UpdateAndEvaluateAlarms(systemContext, emitEvents: false);

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

        /// <summary>
        /// Configures the Optional OPC 10000-12 §7.10.3
        /// <c>ServerConfigurationType</c> surface on the configuration node:
        /// the identity Properties (always known from the
        /// <see cref="ApplicationConfiguration"/>), the <c>HasSecureElement</c>
        /// and <c>InApplicationSetup</c> Properties (when a value is
        /// configured), the <c>ResetToServerDefaults</c> Method (§7.10.13) and
        /// the <c>ConfigurationFile</c> Object (§7.10.20) (each when a provider
        /// is configured). Only members whose address-space nodes were added in
        /// <see cref="AddBehaviourToPredefinedNodeAsync"/> are seeded/wired, so
        /// the optional-child suppression is preserved.
        /// </summary>
        private void ConfigureOptionalServerConfigurationSurface(
            ServerSystemContext systemContext,
            ServerConfigurationState configNode,
            ApplicationConfiguration configuration)
        {
            if (configNode.ApplicationUri != null)
            {
                configNode.ApplicationUri.Value = configuration.ApplicationUri ?? string.Empty;
            }
            if (configNode.ProductUri != null)
            {
                configNode.ProductUri.Value = configuration.ProductUri ?? string.Empty;
            }
            if (configNode.ApplicationType != null)
            {
                configNode.ApplicationType.Value = configuration.ApplicationType;
            }
            if (configNode.ApplicationNames != null)
            {
                configNode.ApplicationNames.Value = string.IsNullOrEmpty(configuration.ApplicationName)
                    ? ArrayOf<LocalizedText>.Empty
                    : ArrayOf.Wrapped(new LocalizedText(configuration.ApplicationName));
                configNode.ApplicationNames.ValueRank = ValueRanks.OneDimension;
            }

            if (configNode.HasSecureElement != null &&
                m_serverConfigurationOptions.HasSecureElement is bool hasSecureElement)
            {
                configNode.HasSecureElement.Value = hasSecureElement;
            }
            if (configNode.InApplicationSetup != null &&
                m_serverConfigurationOptions.InApplicationSetup is bool inApplicationSetup)
            {
                configNode.InApplicationSetup.Value = inApplicationSetup;
            }

            if (configNode.ResetToServerDefaults != null &&
                m_serverConfigurationOptions.ResetProvider != null)
            {
                configNode.ResetToServerDefaults.OnCallMethod2Async
                    = new GenericMethodCalledEventHandler2Async(ResetToServerDefaultsAsync);
            }

            ConfigureConfigurationFile(systemContext, configNode);
        }

        /// <summary>
        /// Instantiates and wires the <see cref="ApplicationConfigurationFile"/>
        /// handler onto the <c>ConfigurationFile</c> node (§7.10.20) when a
        /// provider is configured and the optional node was materialised.
        /// </summary>
        private void ConfigureConfigurationFile(
            ServerSystemContext systemContext,
            ServerConfigurationState configNode)
        {
            if (m_serverConfigurationOptions.ConfigurationFileProvider is not { } fileProvider ||
                configNode.ConfigurationFile is not { } fileNode)
            {
                return;
            }

            m_configurationFile = new ApplicationConfigurationFile(
                fileNode,
                fileProvider,
                new ApplicationConfigurationFile.SecureAccess(
                    ctx => HasApplicationSecureAdminAccess(ctx, requireEncryptedChannel: true)),
                new ApplicationConfigurationFile.SecureAccess(
                    ctx => HasApplicationSecureAdminAccess(ctx, requireEncryptedChannel: false)),
                Server.Telemetry,
                m_coordinator,
                m_timeProvider,
                m_serverConfigurationOptions.ConfigurationFileActivityTimeout);

            if (fileNode.ActivityTimeout != null)
            {
                fileNode.ActivityTimeout.Value = m_serverConfigurationOptions.ConfigurationFileActivityTimeout;
            }
            if (fileNode.CurrentVersion != null)
            {
                fileNode.CurrentVersion.Value = fileProvider.CurrentVersion;
            }
            if (fileNode.LastUpdateTime != null)
            {
                fileNode.LastUpdateTime.Value = new DateTimeUtc(fileProvider.LastUpdateTime);
            }
            if (fileNode.SupportedDataType != null)
            {
                fileNode.SupportedDataType.Value = DataTypeIds.ApplicationConfigurationDataType;
            }
            if (fileNode.Writable != null)
            {
                fileNode.Writable.Value = true;
            }
            if (fileNode.UserWritable != null)
            {
                fileNode.UserWritable.Value = true;
            }
            if (fileNode.OpenCount != null)
            {
                fileNode.OpenCount.Value = 0;
            }

            fileNode.ClearChangeMasks(systemContext, true);
        }

        /// <summary>
        /// Implements the Optional OPC 10000-12 §7.10.13
        /// <c>ResetToServerDefaults</c> Method: it resets the application
        /// security configuration to its default state. The Method requires an
        /// authenticated SecureChannel and the SecurityAdmin Role, is rejected
        /// while another Session owns an active PushManagement transaction, and
        /// returns its response before the actual reset runs. After the
        /// response, the server advertises the pending shutdown
        /// (<c>ServerState</c> = Shutdown, <c>ShutdownReason</c>,
        /// <c>SecondsTillShutdown</c>), waits the configured grace period so the
        /// Client can receive this response, and then invokes the injected
        /// <see cref="IServerConfigurationResetProvider"/>.
        /// </summary>
        private ValueTask<ServiceResult> ResetToServerDefaultsAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments,
            CancellationToken cancellationToken)
        {
            // §7.10.13: authenticated SecureChannel + SecurityAdmin Role.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            if (m_serverConfigurationOptions.ResetProvider == null)
            {
                return new ValueTask<ServiceResult>(
                    (ServiceResult)StatusCodes.BadNotSupported);
            }

            // A reset invalidates the whole configuration, so it must not race
            // an in-flight PushManagement transaction owned by another Session.
            NodeId sessionId = GetSessionId(context);
            try
            {
                m_coordinator.ValidateSessionCanParticipate(sessionId);
            }
            catch (ServiceResultException ex)
            {
                return new ValueTask<ServiceResult>((ServiceResult)ex.StatusCode);
            }

            m_logger.ResetToServerDefaultsRequested(sessionId);

            ScheduleDeferredReset();

            // §7.10.13: the response is returned before the reset/shutdown runs.
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <summary>
        /// Advertises the pending shutdown and, after the configured grace
        /// period has elapsed so the <c>ResetToServerDefaults</c> response can
        /// be received, invokes the reset provider. Honors the shutdown
        /// cancellation token so a server shutdown that races the reset
        /// abandons it cleanly. The completion is exposed via
        /// <see cref="DrainPendingResetAsync"/> for deterministic testing.
        /// </summary>
        private void ScheduleDeferredReset()
        {
            IServerConfigurationResetProvider? resetProvider = m_serverConfigurationOptions.ResetProvider;
            if (resetProvider == null)
            {
                return;
            }

            CancellationToken shutdownToken;
            try
            {
                shutdownToken = m_shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (shutdownToken.IsCancellationRequested)
            {
                return;
            }

            TimeSpan delay = m_serverConfigurationOptions.ResetShutdownDelay;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (m_pendingApplyChangesLock)
            {
                m_pendingResetTask = completion.Task;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    AdvertisePendingShutdown(delay);

                    try
                    {
                        await m_timeProvider.Delay(delay, shutdownToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    if (shutdownToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    await resetProvider.ResetToServerDefaultsAsync(shutdownToken).ConfigureAwait(false);
                    completion.TrySetResult(null);
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.ResetToServerDefaultsFailed(ex);
                    completion.TrySetException(ex);
                }
            });
        }

        /// <summary>
        /// Sets <c>ServerState</c> to <see cref="ServerState.Shutdown"/> and
        /// advertises the <c>ShutdownReason</c> and <c>SecondsTillShutdown</c>
        /// per OPC 10000-12 §7.10.13, tolerating a server whose status object is
        /// not available.
        /// </summary>
        private void AdvertisePendingShutdown(TimeSpan delay)
        {
            try
            {
                uint secondsTillShutdown = (uint)Math.Ceiling(Math.Max(0, delay.TotalSeconds));
                var reason = new LocalizedText(
                    "en-US",
                    "The server is resetting to its default configuration. " +
                    "Existing credentials may no longer be valid after the restart.");

                Server.UpdateServerStatus(status =>
                {
                    status.Value.State = ServerState.Shutdown;
                    status.Value.ShutdownReason = reason;
                    status.Value.SecondsTillShutdown = secondsTillShutdown;

                    ServerStatusState? variable = status.Variable;
                    if (variable != null)
                    {
                        if (variable.State != null)
                        {
                            variable.State.Value = ServerState.Shutdown;
                        }
                        if (variable.ShutdownReason != null)
                        {
                            variable.ShutdownReason.Value = reason;
                        }
                        if (variable.SecondsTillShutdown != null)
                        {
                            variable.SecondsTillShutdown.Value = secondsTillShutdown;
                        }
                        variable.ClearChangeMasks(Server.DefaultSystemContext, true);
                    }
                });
            }
            catch (Exception ex)
            {
                m_logger.FailedToAdvertisePendingShutdown(ex);
            }
        }

        /// <summary>
        /// Awaits completion of any pending deferred <c>ResetToServerDefaults</c>
        /// work scheduled by a recent Method call. Returns immediately when no
        /// reset is in flight. Used by tests and tightly-coupled hosts to
        /// deterministically wait for the reset to run.
        /// </summary>
        internal Task DrainPendingResetAsync(CancellationToken cancellationToken = default)
        {
            Task pending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingResetTask;
            }

            if (pending.IsCompleted)
            {
                return Task.CompletedTask;
            }

            return cancellationToken.CanBeCanceled ? pending.WaitAsync(cancellationToken) : pending;
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
                    m_logger.CannotCreateNamespaceMetadataState(namespaceUri);
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
                namespaceMetadataState.AddDefaultRolePermissions(SystemContext)
                    .AddDefaultUserRolePermissions(SystemContext);

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
            // The generic ServerConfiguration / TrustList (§7.8) access path
            // requires an encrypted SecureChannel. Individual Push methods
            // that do not transfer private-key material relax this to an
            // authenticated channel by calling the
            // requireEncryptedChannel overload directly.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: true);
        }

        /// <summary>
        /// Enforces the SecureChannel security and SecurityAdmin Role
        /// requirements shared by the standard <c>ServerConfiguration</c>
        /// Push methods (OPC 10000-12 §7.10). The channel requirement is
        /// evaluated first and, when unmet, reported as
        /// <see cref="StatusCodes.BadSecurityModeInsufficient"/> as required
        /// by the §7.10 Method result tables; the Role requirement is
        /// reported separately as <see cref="StatusCodes.BadUserAccessDenied"/>.
        /// </summary>
        /// <param name="context">
        /// The calling context. Non Session-bound (internal/programmatic)
        /// calls are always permitted.
        /// </param>
        /// <param name="requireEncryptedChannel">
        /// When <see langword="true"/> the SecureChannel must be encrypted
        /// (<see cref="MessageSecurityMode.SignAndEncrypt"/>), as required by
        /// <c>UpdateCertificate</c> (§7.10.5) and <c>CreateSigningRequest</c>
        /// (§7.10.10). When <see langword="false"/> an authenticated channel
        /// (<see cref="MessageSecurityMode.Sign"/> or
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/>) is sufficient,
        /// as required by <c>CreateSelfSignedCertificate</c> (§7.10.6),
        /// <c>DeleteCertificate</c> (§7.10.7), <c>GetCertificates</c>,
        /// <c>GetRejectedList</c>, <c>ApplyChanges</c> (§7.10.9) and
        /// <c>CancelChanges</c>.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadSecurityModeInsufficient"/>
        /// when the channel security is insufficient, or with
        /// <see cref="StatusCodes.BadUserAccessDenied"/> when the caller does
        /// not hold the SecurityAdmin Role.
        /// </exception>
        private void HasApplicationSecureAdminAccess(
            ISystemContext context,
            bool requireEncryptedChannel)
        {
            if (context is SessionSystemContext { OperationContext: OperationContext operationContext })
            {
                MessageSecurityMode securityMode = operationContext
                    .ChannelContext?
                    .EndpointDescription?
                    .SecurityMode
                    ?? MessageSecurityMode.Invalid;

                bool channelSecure = requireEncryptedChannel
                    ? securityMode == MessageSecurityMode.SignAndEncrypt
                    : securityMode is MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt;

                if (!channelSecure)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityModeInsufficient,
                        requireEncryptedChannel
                            ? "This Method must be called from an encrypted SecureChannel " +
                                "(MessageSecurityMode SignAndEncrypt)."
                            : "This Method must be called from an authenticated SecureChannel " +
                                "(MessageSecurityMode Sign or SignAndEncrypt).");
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
        /// from the coordinator's current snapshot, applying the OPC 10000-12
        /// §7.10.17 DataValue status semantics. Called after every
        /// <c>ApplyChanges</c>, <c>CancelChanges</c>, Session-close
        /// cancellation, and after a staged operation starts/continues a
        /// transaction so a Client reading the node while a transaction is
        /// active observes <see cref="StatusCodes.BadInvalidState"/> on
        /// <c>Result</c>.
        /// </summary>
        /// <remarks>
        /// §7.10.17: when no transaction has ever started, every Variable
        /// reads with a status of <see cref="StatusCodes.BadOutOfService"/>.
        /// While a transaction is active, <c>StartTime</c> is Good,
        /// <c>EndTime</c> is <see cref="DateTime.MinValue"/>, and
        /// <c>Result</c> reads <see cref="StatusCodes.BadInvalidState"/>. Once
        /// the transaction completes, <c>Result</c> is Good and carries the
        /// outcome <see cref="StatusCode"/> (the <c>ApplyChanges</c> result,
        /// or <see cref="StatusCodes.BadRequestCancelledByClient"/> for
        /// <c>CancelChanges</c>).
        /// </remarks>
        private void UpdateTransactionDiagnostics(ISystemContext context)
        {
            if (m_serverConfigurationNode?.TransactionDiagnostics is not { } diagnosticsNode)
            {
                return;
            }

            PushConfigurationTransactionSnapshot snapshot = m_coordinator.GetSnapshot();
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;

            if (snapshot.State == PushConfigurationTransactionState.None)
            {
                // §7.10.17: before any transaction has started every Variable
                // reads with a status of Bad_OutOfService.
                SetDiagnosticVariableStatus(diagnosticsNode.StartTime, StatusCodes.BadOutOfService, now);
                SetDiagnosticVariableStatus(diagnosticsNode.EndTime, StatusCodes.BadOutOfService, now);
                SetDiagnosticVariableStatus(diagnosticsNode.Result, StatusCodes.BadOutOfService, now);
                SetDiagnosticVariableStatus(diagnosticsNode.AffectedTrustLists, StatusCodes.BadOutOfService, now);
                SetDiagnosticVariableStatus(diagnosticsNode.AffectedCertificateGroups, StatusCodes.BadOutOfService, now);
                SetDiagnosticVariableStatus(diagnosticsNode.Errors, StatusCodes.BadOutOfService, now);
                diagnosticsNode.ClearChangeMasks(context, true);
                return;
            }

            bool active = snapshot.State == PushConfigurationTransactionState.Active;

            if (diagnosticsNode.StartTime != null)
            {
                // StartTime is Good once a transaction has started.
                diagnosticsNode.StartTime.Value = snapshot.StartTime;
                diagnosticsNode.StartTime.StatusCode = StatusCodes.Good;
                diagnosticsNode.StartTime.Timestamp = now;
            }

            if (diagnosticsNode.EndTime != null)
            {
                // EndTime keeps the value DateTime.MinValue until the
                // transaction completes.
                diagnosticsNode.EndTime.Value = active ? DateTime.MinValue : snapshot.EndTime;
                diagnosticsNode.EndTime.StatusCode = StatusCodes.Good;
                diagnosticsNode.EndTime.Timestamp = now;
            }

            if (diagnosticsNode.Result != null)
            {
                // Result status is Bad_InvalidState while a transaction is in
                // flight; once completed the status is Good and the value is
                // the ApplyChanges/CancelChanges outcome StatusCode.
                diagnosticsNode.Result.Value = active ? (StatusCode)StatusCodes.Good : snapshot.Result;
                diagnosticsNode.Result.StatusCode = active ? StatusCodes.BadInvalidState : StatusCodes.Good;
                diagnosticsNode.Result.Timestamp = now;
            }

            if (diagnosticsNode.AffectedTrustLists != null)
            {
                diagnosticsNode.AffectedTrustLists.Value = snapshot.AffectedTrustLists;
                diagnosticsNode.AffectedTrustLists.StatusCode = StatusCodes.Good;
                diagnosticsNode.AffectedTrustLists.Timestamp = now;
            }

            if (diagnosticsNode.AffectedCertificateGroups != null)
            {
                diagnosticsNode.AffectedCertificateGroups.Value = snapshot.AffectedCertificateGroups;
                diagnosticsNode.AffectedCertificateGroups.StatusCode = StatusCodes.Good;
                diagnosticsNode.AffectedCertificateGroups.Timestamp = now;
            }

            if (diagnosticsNode.Errors != null)
            {
                diagnosticsNode.Errors.Value = snapshot.Errors;
                diagnosticsNode.Errors.StatusCode = StatusCodes.Good;
                diagnosticsNode.Errors.Timestamp = now;
            }

            diagnosticsNode.ClearChangeMasks(context, true);
        }

        /// <summary>
        /// Sets the DataValue status (and source timestamp) of a single
        /// <c>TransactionDiagnostics</c> Variable, tolerating a
        /// <see langword="null"/> Variable (optional children).
        /// </summary>
        private static void SetDiagnosticVariableStatus(
            BaseVariableState? variable,
            StatusCode statusCode,
            DateTime timestamp)
        {
            if (variable != null)
            {
                variable.StatusCode = statusCode;
                variable.Timestamp = timestamp;
            }
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
        /// Reserves cross-replica ownership of the server-wide PushManagement
        /// transaction at an <see langword="await"/> boundary before the
        /// synchronous <see cref="IPushConfigurationTransactionCoordinator.Stage"/>
        /// call that follows. The default per-server coordinator does not
        /// implement <see cref="IPushConfigurationTransactionOwnershipGate"/>,
        /// so this is a no-op for the non-distributed server; a distributed
        /// coordinator acquires or renews a shared lease so only one replica
        /// owns the transaction at a time.
        /// </summary>
        private ValueTask AcquireTransactionOwnershipAsync(
            NodeId sessionId,
            CancellationToken cancellationToken)
        {
            return m_coordinator is IPushConfigurationTransactionOwnershipGate gate
                ? gate.AcquireTransactionOwnershipAsync(sessionId, cancellationToken)
                : default;
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
        /// full (see <see cref="PushConfigurationTransactionCoordinator.ApplyChangesAsync(NodeId, CancellationToken)"/>);
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
                    m_logger.DeleteApplicationCertificate(removeThumbprint);
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
                        m_logger.AddApplicationCertificate(addCertificateWithKey);
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
                            m_logger.RestoredPreviousCertificateAfterReplacementFailed(
                                existingCertIdentifier.CertificateType);
                        }
                        catch (Exception restoreException)
                        {
                            m_logger.FailedToRestorePreviousCertificateAfterReplacementFailed(
                                restoreException,
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
                    m_logger.RestoredPreviousCertificateAfterIssuerImportFailed(
                        existingCertIdentifier.CertificateType);
                }
            }
            catch (Exception restoreException)
            {
                m_logger.FailedToRestorePreviousCertificateAfterIssuerImportFailed(
                    restoreException,
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
                    m_logger.FailedToRemoveStagedIssuerCertificate(
                        ex,
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

        /// <summary>
        /// OPC 10000-12 §7.10.7 endpoint-reference determination, evaluated
        /// during <c>ApplyChanges</c> preparation: rejects deleting a
        /// certificate that is still referenced by an active
        /// <see cref="EndpointDescription"/>. Because a delete that is
        /// superseded within the same transaction by a
        /// <c>CreateSelfSignedCertificate</c>/<c>UpdateCertificate</c> for the
        /// same slot never reaches commit (the operations coalesce), only a
        /// delete that genuinely empties the slot is checked here.
        /// </summary>
        /// <param name="deletedThumbprint">
        /// The thumbprint of the certificate the staged delete removes.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadInvalidState"/> when the
        /// certificate is still referenced by an endpoint.
        /// </exception>
        private void EnsureCertificateNotEndpointReferenced(string? deletedThumbprint)
        {
            if (string.IsNullOrEmpty(deletedThumbprint))
            {
                return;
            }

            ArrayOf<EndpointDescription> endpoints =
                (Server as IServerEndpointRegistryProvider)?.ServerEndpoints ?? default;

            // Resolve the certificate each endpoint currently presents from the
            // active certificate registry rather than the EndpointDescription's
            // ServerCertificate blob captured at startup: after a successful
            // certificate rotation that blob may be stale, so the live registry
            // (keyed by the endpoint's SecurityPolicyUri, exactly as the channel
            // handshake resolves the presented certificate) is authoritative for
            // which certificate/type is presented at this moment. When no
            // registry is available (an external/mocked IServerInternal) the
            // endpoint's own blob is the only source and is used as a fallback.
            var registry = m_configuration.CertificateManager as ICertificateRegistry;

            if (IsCertificateReferencedByEndpoint(deletedThumbprint!, endpoints, registry, Server.Telemetry))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState,
                    "The certificate is referenced by an EndpointDescription and cannot be deleted " +
                    "(OPC 10000-12 §7.10.7).");
            }
        }

        /// <summary>
        /// Resolves the exact certificate each <see cref="EndpointDescription"/>
        /// currently presents and reports whether any matches
        /// <paramref name="thumbprint"/>.
        /// </summary>
        /// <remarks>
        /// When <paramref name="registry"/> is supplied the presented
        /// certificate is resolved live from the certificate registry using the
        /// endpoint's (immutable) <see cref="EndpointDescription.SecurityPolicyUri"/>,
        /// so a certificate that was rotated after the endpoints were created is
        /// still matched even though the endpoint's cached
        /// <see cref="EndpointDescription.ServerCertificate"/> blob is stale;
        /// endpoints that do not require encryption present no channel
        /// certificate and are skipped. When no <paramref name="registry"/> is
        /// available the endpoint's <see cref="EndpointDescription.ServerCertificate"/>
        /// blob is used as a fallback (external/mocked servers).
        /// </remarks>
        internal static bool IsCertificateReferencedByEndpoint(
            string thumbprint,
            ArrayOf<EndpointDescription> endpoints,
            ICertificateRegistry? registry,
            ITelemetryContext? telemetry)
        {
            if (endpoints.IsNull)
            {
                return false;
            }

            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint == null)
                {
                    continue;
                }

                if (registry != null)
                {
                    // Authoritative path: an endpoint that requires encryption
                    // presents the certificate the registry currently maps its
                    // SecurityPolicyUri to. Endpoints without encryption present
                    // no channel certificate to protect.
                    if (!ServerBase.RequireEncryption(endpoint))
                    {
                        continue;
                    }

                    using CertificateEntry? entry = registry
                        .AcquireApplicationCertificateBySecurityPolicy(endpoint.SecurityPolicyUri!);
                    if (entry?.Certificate is { } current &&
                        string.Equals(current.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // The registry resolved the exact certificate this endpoint
                    // presents; do not also consult the potentially stale blob.
                    continue;
                }

                ByteString serverCertificate = endpoint.ServerCertificate;
                if (serverCertificate.IsNull || serverCertificate.Length == 0)
                {
                    continue;
                }

                try
                {
                    using Certificate leaf = Utils.ParseCertificateBlob(serverCertificate, telemetry);
                    if (string.Equals(leaf.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (ServiceResultException)
                {
                    // A malformed endpoint certificate cannot be matched;
                    // skip it rather than blocking every DeleteCertificate.
                }
            }

            return false;
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
            // §7.10.5: UpdateCertificate may transfer private-key material,
            // so it requires an encrypted SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: true);

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
                await AcquireTransactionOwnershipAsync(sessionId, ct).ConfigureAwait(false);
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

                newIssuerCollection = [];

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
                        m_logger.FailedToVerifyIntegrityAgainstTrustList(ex, newCert);
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
                            m_logger.FailedToVerifyIntegrityAndIssuerList(ex, newCert);
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

            // §7.10.17: the staged operation started/continued the active
            // transaction, so refresh TransactionDiagnostics now (Result reads
            // Bad_InvalidState while active) rather than only at ApplyChanges.
            UpdateTransactionDiagnostics(context);

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

            using CertificateManager validator = CertificateManagerFactory.Create(securityConfiguration, telemetry);
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
            // §7.10.6: CreateSelfSignedCertificate does not transfer a
            // private key, so an authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

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
            await AcquireTransactionOwnershipAsync(sessionId, cancellationToken).ConfigureAwait(false);
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

                m_logger.StagedSelfSignedCertificate(
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

            // §7.10.17: refresh TransactionDiagnostics now the operation is
            // staged so Result reads Bad_InvalidState while the transaction
            // is active.
            UpdateTransactionDiagnostics(context);

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
            // §7.10.7: DeleteCertificate requires an authenticated (but not
            // necessarily encrypted) SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId)!;

            NodeId sessionId = GetSessionId(context);
            await AcquireTransactionOwnershipAsync(sessionId, cancellationToken).ConfigureAwait(false);
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

            // Conservative net-state safety check applied at staging time so
            // the administrator gets immediate feedback: deleting every
            // application-certificate slot (netting the live registry against
            // everything already staged in this transaction) is rejected up
            // front. The authoritative OPC 10000-12 §7.10.7 endpoint-reference
            // determination happens later, during ApplyChanges preparation
            // (see the operation's PrepareAsync below).
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
                // OPC 10000-12 §7.10.7: "Certificates that are referenced by
                // EndpointDescriptions shall not be deleted. This
                // determination happens when ApplyChanges is called." Because
                // a delete-then-create/update for the same slot coalesces to
                // the later operation, only a delete that survives to commit
                // (i.e. genuinely leaves the slot empty) reaches this check.
                PrepareAsync = _ =>
                {
                    EnsureCertificateNotEndpointReferenced(previousThumbprint);
                    return Task.CompletedTask;
                },
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

            // §7.10.17: refresh TransactionDiagnostics now the operation is
            // staged so Result reads Bad_InvalidState while the transaction
            // is active.
            UpdateTransactionDiagnostics(context);

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
            // §7.10.10: CreateSigningRequest may return a regenerated key's
            // signing request, so it requires an encrypted SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: true);

            ServerCertificateGroup certificateGroup = VerifyGroupAndTypeId(
                certificateGroupId,
                certificateTypeId)!;

            // OPC 10000-12 §7.10.10: when a new private key is regenerated the
            // caller must supply at least 32 bytes of additional entropy in
            // the Nonce. An invalid Nonce is reported as Bad_InvalidArgument
            // and leaves all state unchanged.
            if (regeneratePrivateKey && (nonce.IsNull || nonce.Length < kMinimumRegenerateNonceLength))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The Nonce must be at least 32 bytes long when regeneratePrivateKey is true.");
            }

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
                (m_configuration.CertificateManager as ICertificateRegistry)?
                    .AcquireApplicationCertificateByType(certificateTypeId);
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
                    domainNames,
                    nonce,
                    cancellationToken);

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
                m_logger.CreateSigningRequest(certWithPrivateKey);
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

        /// <summary>
        /// Generates the temporary application certificate and private key
        /// for a <c>CreateSigningRequest</c> regenerate-key request
        /// (OPC 10000-12 §7.10.10), delegating to the injected
        /// <see cref="IPushCertificateKeyGenerator"/> so the caller-supplied
        /// <paramref name="additionalEntropy"/> is genuinely mixed into the
        /// new private key.
        /// </summary>
        private Certificate GenerateTemporaryApplicationCertificate(
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            ByteString additionalEntropy,
            CancellationToken cancellationToken)
        {
            DateTime utcToday = m_timeProvider.GetUtcNow().UtcDateTime.Date;
            return m_keyGenerator.CreateApplicationCertificate(
                new PushCertificateKeyGenerationRequest
                {
                    CertificateTypeId = certificateTypeId,
                    ApplicationUri = m_configuration.ApplicationUri!,
                    ApplicationName = m_configuration.ApplicationName!,
                    SubjectName = subjectName,
                    DomainNames = domainNames,
                    KeySizeInBits = 0,
                    NotBefore = utcToday.AddDays(-1),
                    NotAfter = utcToday.AddDays(14),
                    AdditionalEntropy = additionalEntropy
                },
                cancellationToken);
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
            // §7.10.9: ApplyChanges requires an authenticated SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

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

            // An apply-local collector for the exact certificate groups and
            // TrustLists this call commits, filled by the coordinator while
            // this Session still owns the transaction. This is the §7.10.9
            // counterpart of the rotation collector above: it must never be
            // re-derived from a fresh coordinator snapshot after ApplyChanges
            // returns, because ownership is released before the coordinator
            // returns and another Session may already have staged a new
            // transaction whose (uncommitted) targets a snapshot would report.
            var committedEffects = new PushConfigurationApplyEffects();
            ServiceResult result;
            try
            {
                m_activeRotationCollector.Value = rotations;
                result = await m_coordinator.ApplyChangesAsync(sessionId, committedEffects, cancellationToken)
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

            // §7.10.9: identify the TrustLists whose committed changes must
            // force affected SecureChannels to renegotiate (application/HTTPS
            // groups) or invalidate certificate-based user identities (user
            // token group). Only TrustLists actually committed by THIS
            // transaction are considered - taken from the apply-local
            // collector, not a fresh coordinator snapshot that may already
            // represent another Session's active transaction - so unaffected
            // channels/Sessions are never disturbed.
            List<TrustListChangeEffect> trustListEffects =
                BuildTrustListEffects(committedEffects.TrustLists);

            if (rotations.Count > 0 || trustListEffects.Count > 0)
            {
                // Schedule the deferred apply: wait a short grace period for
                // the method response to be flushed, then re-sync the
                // certificate manager from disk (for rotations), force-close
                // every SecureChannel that was negotiated against the rotated
                // certificate(s), force channels with a now-untrusted peer
                // certificate to renegotiate, and close Sessions whose
                // certificate user identity is no longer valid. The
                // completion handle is exposed via DrainPendingApplyChangesAsync
                // so tests and hosts can deterministically await the effects
                // rather than racing the delay.
                ScheduleDeferredApplyChanges(rotations, trustListEffects);
            }

            // OPC 10000-12 §7.8.3: a committed TrustList change updates the
            // TrustList's LastUpdateTime synchronously here (even when no
            // deferred §7.10.9 effects are scheduled), so refresh the alarm
            // values now. A certificate rotation additionally re-evaluates
            // once the deferred reload has completed.
            try
            {
                UpdateAndEvaluateAlarms(context, emitEvents: m_alarmMonitoringActive);
            }
            catch (Exception alarmEx)
            {
                m_logger.CertificateAlarmReevaluationAfterCommitFailed(alarmEx);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Maps the TrustList NodeIds committed by a transaction to the
        /// §7.10.9 post-<c>ApplyChanges</c> effect that must be applied to
        /// running SecureChannels or Sessions, using the certificate group
        /// each TrustList belongs to. TrustLists that do not map to a known
        /// server certificate group are ignored.
        /// </summary>
        internal List<TrustListChangeEffect> BuildTrustListEffects(ArrayOf<NodeId> affectedTrustLists)
        {
            var effects = new List<TrustListChangeEffect>();
            if (affectedTrustLists.Count == 0)
            {
                return effects;
            }

            foreach (NodeId trustListId in affectedTrustLists)
            {
                if (trustListId.IsNull)
                {
                    continue;
                }

                foreach (ServerCertificateGroup certGroup in m_certificateGroups)
                {
                    if (certGroup.Node?.TrustList == null ||
                        !Utils.IsEqual(certGroup.Node.TrustList.NodeId, trustListId))
                    {
                        continue;
                    }

                    if (certGroup.BrowseName == BrowseNames.DefaultUserTokenGroup)
                    {
                        effects.Add(new TrustListChangeEffect
                        {
                            TrustListId = trustListId,
                            CertificateGroupId = certGroup.NodeId,
                            Kind = TrustListEffectKind.UserIdentityTrust,
                            ValidationScope = TrustListIdentifier.Users
                        });
                    }
                    else
                    {
                        effects.Add(new TrustListChangeEffect
                        {
                            TrustListId = trustListId,
                            CertificateGroupId = certGroup.NodeId,
                            Kind = TrustListEffectKind.SecureChannelTrust,
                            ValidationScope = certGroup.BrowseName == BrowseNames.DefaultHttpsGroup
                                ? TrustListIdentifier.Https
                                : TrustListIdentifier.Peers
                        });
                    }

                    break;
                }
            }

            return effects;
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
            // §7.10.2/§7.10.11: CancelChanges requires an authenticated
            // SecureChannel.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

            NodeId sessionId = GetSessionId(context);
            ServiceResult result = m_coordinator.CancelChanges(sessionId);
            UpdateTransactionDiagnostics(context);
            return new ValueTask<ServiceResult>(result);
        }

        /// <summary>
        /// Schedules the post-response fan-out for both the server
        /// certificate rotation channel-cuts and the §7.10.9 TrustList
        /// effects. Chains onto any already-running deferred apply so
        /// concurrent calls to <see cref="ApplyChangesAsync"/> run
        /// sequentially.
        /// </summary>
        private void ScheduleDeferredApplyChanges(
            List<PendingCertificateRotation> rotations,
            List<TrustListChangeEffect> trustListEffects)
        {
            CancellationToken shutdownToken;
            try
            {
                shutdownToken = m_shutdownCts.Token;
            }
            catch (ObjectDisposedException)
            {
                // The manager is being disposed; do not schedule new deferred
                // work. Release the captured rotations so nothing leaks.
                DisposeRotations(rotations);
                return;
            }

            if (shutdownToken.IsCancellationRequested)
            {
                // Shutdown already signalled: skip the post-response effects
                // entirely (they would only run against listeners/managers
                // being torn down) and release the captured rotations.
                DisposeRotations(rotations);
                return;
            }

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

                    m_logger.ApplyChangesScheduled(gracePeriod.TotalMilliseconds);

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
                    try
                    {
                        await m_timeProvider.Delay(gracePeriod, shutdownToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Server shutting down during the grace period: skip
                        // the post-response effects entirely so they never run
                        // against listeners/managers that are about to be
                        // disposed. The finally below releases the rotations.
                        completion.TrySetResult(null);
                        return;
                    }

                    if (shutdownToken.IsCancellationRequested)
                    {
                        completion.TrySetResult(null);
                        return;
                    }

                    m_logger.ApplyChangesRunning();

                    // Reload the certificate manager only when a server
                    // application certificate actually rotated. A TrustList-
                    // only change does not touch the server's own
                    // certificates, and the validator's directory-backed
                    // trust stores refresh themselves, so an app-cert reload
                    // here would be needless work.
                    if (rotations.Count > 0 && m_configuration.CertificateManager != null)
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
                                m_logger.ListenerFailedToCloseChannels(
                                    ex,
                                    listener.ListenerId,
                                    rotation.CertificateType);
                            }
                        }
                    }

                    m_logger.ApplyChangesCompleted(totalCut);

                    // §7.10.9 TrustList effects: force channels whose peer
                    // certificate is no longer trusted to renegotiate and
                    // close Sessions (plus Subscriptions) whose certificate
                    // user identity is no longer valid. Unaffected channels
                    // and Sessions are left untouched.
                    if (trustListEffects.Count > 0)
                    {
                        await ApplyTrustListEffectsAsync(trustListEffects, listeners)
                            .ConfigureAwait(false);
                    }

                    // OPC 10000-12 §7.8.3: the committed certificate/TrustList
                    // change may clear (or raise) the CertificateExpired /
                    // TrustListOutOfDate alarms, so re-evaluate now that the new
                    // certificate has been reloaded from disk.
                    try
                    {
                        UpdateAndEvaluateAlarms(SystemContext, emitEvents: m_alarmMonitoringActive);
                    }
                    catch (Exception alarmEx)
                    {
                        m_logger.CertificateAlarmReevaluationFailed(alarmEx);
                    }

                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    m_logger.ApplyChangesUpdateFailed(ex);
                    completion.TrySetException(ex);
                }
                finally
                {
                    DisposeRotations(rotations);
                }
            });
        }

        /// <summary>
        /// Disposes the captured old-certificate references of every pending
        /// rotation, tolerating a <see langword="null"/> reference.
        /// </summary>
        private static void DisposeRotations(List<PendingCertificateRotation> rotations)
        {
            foreach (PendingCertificateRotation rotation in rotations)
            {
                rotation.OldCertificate?.Dispose();
            }
        }

        /// <summary>
        /// Builds the effect context from the running server and applies the
        /// committed §7.10.9 TrustList effects through the injected
        /// <see cref="IPushConfigurationTrustListEffectHandler"/>.
        /// </summary>
        private ValueTask ApplyTrustListEffectsAsync(
            List<TrustListChangeEffect> trustListEffects,
            IReadOnlyList<ITransportListener> listeners)
        {
            var context = new PushConfigurationTrustListEffectContext
            {
                Effects = trustListEffects,
                TransportListeners = listeners,
                SessionManager = Server.SessionManager,
                CertificateValidator = m_configuration.CertificateManager,
                // A server-initiated close carries no client OperationContext,
                // matching the SessionManager's own timeout-driven close path.
                CloseSessionAsync = (sessionId, deleteSubscriptions, ct) =>
                    Server.CloseSessionAsync(null!, sessionId, deleteSubscriptions, ct)
            };

            return m_trustListEffectHandler.ApplyAsync(context, CancellationToken.None);
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
            // GetRejectedList returns only public certificates, so an
            // authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

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
            // GetCertificates returns only public certificates, so an
            // authenticated SecureChannel is sufficient.
            HasApplicationSecureAdminAccess(context, requireEncryptedChannel: false);

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
                    m_logger.CannotFindServerNamespacesNode();
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
                m_logger.ErrorSearchingNamespaceMetadata(ex, namespaceUri);
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

        /// <inheritdoc/>
        public void StartAlarmMonitoring(TimeSpan interval)
        {
            lock (m_alarmEvaluationLock)
            {
                if (m_alarmTimer != null)
                {
                    return;
                }

                // The subscription/event infrastructure is ready once this is
                // called (see StandardServer.OnServerStarted), so transition
                // events may now be reported. Clear any prior stopped state so a
                // restart after StopAlarmMonitoring resumes evaluation.
                m_alarmMonitoringStopped = false;
                m_alarmMonitoringActive = true;
            }

            // Perform an immediate evaluation so an already-expired certificate
            // or a stale TrustList is signalled without waiting a full interval.
            // This is done outside the lock (UpdateAndEvaluateAlarms takes it
            // itself) because System.Threading.Lock is not reentrant.
            try
            {
                UpdateAndEvaluateAlarms(SystemContext, emitEvents: true);
            }
            catch (Exception ex)
            {
                m_logger.InitialCertificateAlarmEvaluationFailed(ex);
            }

            lock (m_alarmEvaluationLock)
            {
                // A concurrent StopAlarmMonitoring may have run during the
                // initial evaluation; do not arm the periodic timer in that case.
                if (m_alarmMonitoringStopped || m_alarmTimer != null)
                {
                    return;
                }

                m_alarmTimer = m_timeProvider.CreateTimer(
                    _ =>
                    {
                        try
                        {
                            UpdateAndEvaluateAlarms(SystemContext, emitEvents: true);
                        }
                        catch (Exception ex)
                        {
                            m_logger.AlarmEvaluationTickFailed(ex);
                        }
                    },
                    null,
                    interval,
                    interval);
            }
        }

        /// <inheritdoc/>
        public void StopAlarmMonitoring()
        {
            // Prevent further evaluations, then serialize with any in-flight one:
            // setting the stopped flag under the evaluation lock waits for a
            // running evaluation to finish and guarantees any evaluation still
            // queued behind the lock returns without mutating nodes that may be
            // getting torn down. The timer is disposed outside the lock so its
            // disposal can never deadlock against a callback that is blocked
            // waiting for the same lock.
            ITimer? timer;
            lock (m_alarmEvaluationLock)
            {
                m_alarmMonitoringActive = false;
                m_alarmMonitoringStopped = true;
                timer = m_alarmTimer;
                m_alarmTimer = null;
            }

            timer?.Dispose();
        }

        /// <summary>
        /// Gets the certificate-group alarm monitors created for the server's
        /// certificate groups. Exposed for testing.
        /// </summary>
        internal IReadOnlyList<CertificateGroupAlarmMonitor> AlarmMonitors
            => m_alarmMonitors.ConvertAll(entry => entry.Monitor);

        /// <summary>
        /// Gets a value indicating whether alarm monitoring is currently
        /// active (i.e. transition events may be reported). Exposed for testing.
        /// </summary>
        internal bool AlarmMonitoringActive => m_alarmMonitoringActive;

        /// <summary>
        /// Refreshes the alarm inputs from the current certificate/TrustList
        /// state and then evaluates every certificate-group alarm, driving the
        /// standard active/inactive state transitions per OPC 10000-12 §7.8.3.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="emitEvents">Whether transition events may be reported.</param>
        internal void UpdateAndEvaluateAlarms(ISystemContext context, bool emitEvents)
        {
            // Serialize the entire refresh + evaluation path so the NodeState
            // mutations and event reporting driven from the periodic timer,
            // ApplyChanges commits, startup and shutdown never overlap. The lock
            // only ever guards fully synchronous work (RefreshAlarmInputs +
            // EvaluateCertificateAlarms perform no awaits), so it never spans an
            // await and never introduces sync-over-async.
            lock (m_alarmEvaluationLock)
            {
                // Once monitoring has been stopped (shutdown/dispose) the
                // address-space nodes may be getting torn down: a timer tick
                // that was already in flight when StopAlarmMonitoring ran blocks
                // here until Stop releases the lock, then observes the stopped
                // flag and returns without mutating any disposed nodes.
                if (m_alarmMonitoringStopped)
                {
                    return;
                }

                RefreshAlarmInputs(context);
                EvaluateCertificateAlarms(context, emitEvents);
            }
        }

        /// <summary>
        /// Evaluates every certificate-group alarm against the current time.
        /// Transition events are only reported when <paramref name="emitEvents"/>
        /// is <see langword="true"/> and the active state actually changed.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="emitEvents">Whether transition events may be reported.</param>
        internal void EvaluateCertificateAlarms(ISystemContext context, bool emitEvents)
        {
            foreach (AlarmMonitorEntry entry in m_alarmMonitors)
            {
                entry.Monitor.Evaluate(context, emitEvents);
            }
        }

        /// <summary>
        /// Publishes the current certificate and TrustList state onto the
        /// alarm inputs (expiration date/certificate/type, trust-list id and
        /// last-update time) without emitting any event.
        /// </summary>
        /// <param name="context">The system context.</param>
        private void RefreshAlarmInputs(ISystemContext context)
        {
            foreach (AlarmMonitorEntry entry in m_alarmMonitors)
            {
                CertificateGroupAlarmMonitor monitor = entry.Monitor;
                ServerCertificateGroup certGroup = entry.Group;
                CertificateGroupState? node = certGroup.Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    DateTime earliest = DateTime.MaxValue;
                    ByteString certificate = default;
                    NodeId certificateType = NodeId.Null;

                    foreach (CertificateIdentifier certIdent in certGroup.ApplicationCertificates)
                    {
                        if (certIdent.RawData == null || certIdent.RawData.Length == 0)
                        {
                            continue;
                        }

                        try
                        {
                            using Certificate cert = Certificate.FromRawData(certIdent.RawData);
                            if (cert.NotAfter < earliest)
                            {
                                earliest = cert.NotAfter;
                                certificate = ByteString.From(certIdent.RawData);
                                certificateType = certIdent.CertificateType;
                            }
                        }
                        catch (Exception ex)
                        {
                            m_logger.SkippingUnreadableCertificate(ex, certGroup.BrowseName);
                        }
                    }

                    monitor.SetCertificateExpiration(
                        context,
                        earliest == DateTime.MaxValue ? null : earliest,
                        certificate,
                        certificateType);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRefreshCertificateExpiredAlarm(ex, certGroup.BrowseName);
                }

                try
                {
                    NodeId trustListId = node.TrustList?.NodeId ?? NodeId.Null;
                    var lastUpdate = (DateTime)(node.TrustList?.LastUpdateTime?.Value
                        ?? (DateTimeUtc)DateTime.MinValue);
                    double updateFrequency = monitor.TrustListOutOfDate?.UpdateFrequency?.Value ?? 0;

                    monitor.SetTrustListStatus(context, trustListId, lastUpdate, updateFrequency);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRefreshTrustListOutOfDateAlarm(ex, certGroup.BrowseName);
                }
            }
        }

        /// <summary>
        /// Creates the optional per-group <c>CertificateExpired</c> and
        /// <c>TrustListOutOfDate</c> alarm instances (OPC 10000-12 §7.8.3),
        /// registers them with the node manager and as event sources, and
        /// initializes their condition state without emitting any event.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="externalReferences">
        /// References from standard certificate-group nodes owned by another node manager.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async ValueTask CreateCertificateAlarmsAsync(
            ISystemContext context,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken)
        {
            BaseObjectTypeState certificateExpirationAlarmType =
                FindPredefinedNode<BaseObjectTypeState>(
                    ObjectTypeIds.CertificateExpirationAlarmType);
            BaseObjectTypeState trustListOutOfDateAlarmType =
                FindPredefinedNode<BaseObjectTypeState>(
                    ObjectTypeIds.TrustListOutOfDateAlarmType);

            foreach (ServerCertificateGroup certGroup in m_certificateGroups)
            {
                CertificateGroupState? node = certGroup.Node;
                if (node == null)
                {
                    continue;
                }

                try
                {
                    // Instantiate the optional alarm instances when the loaded
                    // nodeset did not already provide them.
                    if (node.CertificateExpired == null)
                    {
                        node.AddCertificateExpired(context);
                        WireConditionMethodHandlers(context, node.CertificateExpired!);
                        node.CertificateExpired!.AddExpirationLimit(context);
                    }
                    RebasePredefinedInstanceSubtree(
                        context,
                        node,
                        node.CertificateExpired!,
                        ObjectTypeIds.CertificateExpirationAlarmType);

                    if (node.TrustListOutOfDate == null)
                    {
                        node.AddTrustListOutOfDate(context);
                        WireConditionMethodHandlers(context, node.TrustListOutOfDate!);
                    }
                    RebasePredefinedInstanceSubtree(
                        context,
                        node,
                        node.TrustListOutOfDate!,
                        ObjectTypeIds.TrustListOutOfDateAlarmType);

                    var monitor = new CertificateGroupAlarmMonitor(
                        node,
                        certGroup.BrowseName,
                        m_timeProvider,
                        m_logger);
                    monitor.InitializeQuiet(context);
                    m_alarmMonitors.Add(new AlarmMonitorEntry(monitor, certGroup));

                    // Register the new alarm subtrees and wire them as event
                    // sources so their transition events reach subscriptions
                    // and ConditionRefresh.
                    if (node.CertificateExpired != null)
                    {
                        await AddPredefinedNodeAsync(context, node.CertificateExpired, cancellationToken)
                            .ConfigureAwait(false);
                        AddExternalReferenceIfMissing(
                            context,
                            node,
                            node.CertificateExpired,
                            externalReferences);
                        await AddRootNotifierAsync(node.CertificateExpired, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (node.TrustListOutOfDate != null)
                    {
                        await AddPredefinedNodeAsync(context, node.TrustListOutOfDate, cancellationToken)
                            .ConfigureAwait(false);
                        AddExternalReferenceIfMissing(
                            context,
                            node,
                            node.TrustListOutOfDate,
                            externalReferences);
                        await AddRootNotifierAsync(node.TrustListOutOfDate, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    m_logger.FailedToCreateCertificateAlarms(ex, certGroup.BrowseName);
                }
            }

            if (certificateExpirationAlarmType != null)
            {
                AddPredefinedNodeSynchronously(certificateExpirationAlarmType);
            }
            if (trustListOutOfDateAlarmType != null)
            {
                AddPredefinedNodeSynchronously(trustListOutOfDateAlarmType);
            }
        }

        private void RebasePredefinedInstanceSubtree(
            ISystemContext context,
            NodeState referenceRoot,
            BaseInstanceState instance,
            NodeId typeDefinitionId)
        {
            var subtree = new List<NodeState> { instance };
            for (int ii = 0; ii < subtree.Count; ii++)
            {
                var children = new List<BaseInstanceState>();
                subtree[ii].GetChildren(context, children);
                subtree.AddRange(children);
            }

            if (subtree.All(node => node.NodeId.NamespaceIndex != 0))
            {
                return;
            }

            foreach (NodeState node in subtree)
            {
                if (PredefinedNodes.TryGetValue(node.NodeId, out NodeState? indexedNode) &&
                    ReferenceEquals(indexedNode, node))
                {
                    PredefinedNodes.TryRemove(node.NodeId, out _);
                }
            }

            NodeId previousNodeId = context.AssignInstanceNodeId(instance);
            context.AssignInstanceChildNodeIds(
                instance,
                previousNodeId,
                referenceRoot);
            instance.TypeDefinitionId = typeDefinitionId;
        }

        private void AddExternalReferenceIfMissing(
            ISystemContext context,
            NodeState source,
            BaseInstanceState target,
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            NodeId referenceTypeId = target.ReferenceTypeId.IsNull
                ? ReferenceTypeIds.HasComponent
                : target.ReferenceTypeId;
            target.ReferenceTypeId = referenceTypeId;

            if (externalReferences.TryGetValue(source.NodeId, out IList<IReference>? references) &&
                references.Any(reference =>
                    reference.ReferenceTypeId == referenceTypeId &&
                    !reference.IsInverse &&
                    !reference.TargetId.IsAbsolute &&
                    ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris) ==
                        target.NodeId))
            {
                return;
            }

            AddExternalReference(
                source.NodeId,
                referenceTypeId,
                false,
                target.NodeId,
                externalReferences);
        }

        /// <summary>
        /// Wires the standard condition method handlers (Acknowledge, Confirm,
        /// Enable, Disable, AddComment) for an alarm that was built by the
        /// generated <c>Add&lt;Alarm&gt;</c> factory. The factory constructs the
        /// full alarm structure but does not run <c>OnAfterCreate</c>, which is
        /// what binds those handlers; re-running <c>Create</c>
        /// without reassigning NodeIds triggers <c>OnAfterCreate</c> while
        /// preserving the existing structure so client method calls are honoured.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="alarm">The alarm whose method handlers must be wired.</param>
        private static void WireConditionMethodHandlers(ISystemContext context, NodeState alarm)
        {
            alarm.Create(
                context,
                alarm.NodeId,
                alarm.BrowseName,
                alarm.DisplayName,
                assignNodeIds: false);
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

        /// <summary>
        /// Explicitly binds a <see cref="CertificateGroupAlarmMonitor"/> to the
        /// <see cref="ServerCertificateGroup"/> whose certificate/TrustList state
        /// it evaluates. Pairing them removes the fragile positional/index
        /// coupling between the monitor list and the certificate-group list
        /// (groups without a node are skipped when monitors are created, which
        /// would otherwise misalign the two lists).
        /// </summary>
        /// <param name="Monitor">The alarm monitor.</param>
        /// <param name="Group">The certificate group it evaluates.</param>
        private sealed record AlarmMonitorEntry(
            CertificateGroupAlarmMonitor Monitor,
            ServerCertificateGroup Group);

#pragma warning disable CA2213 // m_serverConfigurationNode is owned by the address space, not by this manager.
        private ServerConfigurationState? m_serverConfigurationNode;
        private UserManagement.UserManagementBinding? m_userManagementBinding;
#pragma warning restore CA2213
        private readonly ApplicationConfiguration m_configuration;
        private readonly TimeProvider m_timeProvider;
        private readonly IPushConfigurationTransactionCoordinator m_coordinator;
        private readonly IPendingCertificateKeyStore m_pendingKeyStore;
        private readonly IPushCertificateKeyGenerator m_keyGenerator;
        private readonly IPushConfigurationTrustListEffectHandler m_trustListEffectHandler;
        private readonly ServerConfigurationOptions m_serverConfigurationOptions;
        private ApplicationConfigurationFile? m_configurationFile;
        private readonly List<ServerCertificateGroup> m_certificateGroups;
        private readonly CertificateStoreIdentifier? m_rejectedStore;
        private ITimer? m_alarmTimer;
        private readonly List<AlarmMonitorEntry> m_alarmMonitors = [];
        private readonly Lock m_alarmEvaluationLock = new();
        private bool m_alarmMonitoringActive;
        private bool m_alarmMonitoringStopped;
        private readonly Dictionary<string, NamespaceMetadataState> m_namespaceMetadataStates = [];
        private readonly Dictionary<ushort, NamespaceMetadataState> m_namespaceMetadataStatesByIndex = [];
        private readonly Lock m_namespaceMetadataStatesLock = new();
        private readonly Lock m_pendingApplyChangesLock = new();
        private Task m_pendingApplyChangesTask = Task.CompletedTask;
        private Task m_pendingResetTask = Task.CompletedTask;
        private readonly CancellationTokenSource m_shutdownCts = new();
        private readonly AsyncLocal<List<PendingCertificateRotation>?> m_activeRotationCollector = new();

        /// <inheritdoc/>
        public TimeSpan ApplyChangesGracePeriod { get; set; }
            = TimeSpan.FromMilliseconds(250);

        private static readonly ICertificateFactory s_certificateFactory = DefaultCertificateFactory.Instance;

        private const int kMinimumRegenerateNonceLength = 32;
    }

    internal static partial class ConfigurationNodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 0, Level = LogLevel.Warning,
            Message = "A deferred ApplyChanges task faulted while draining during shutdown.")]
        public static partial void DeferredApplyChangesFaultedDuringShutdown(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 1, Level = LogLevel.Warning,
            Message = "ResetToServerDefaults requested by session {SessionId}; scheduling reset to server defaults.")]
        public static partial void ResetToServerDefaultsRequested(this ILogger logger, NodeId sessionId);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 2, Level = LogLevel.Error,
            Message = "ResetToServerDefaults failed. Server could be in a faulted state.")]
        public static partial void ResetToServerDefaultsFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 3, Level = LogLevel.Warning,
            Message = "Failed to advertise pending shutdown for ResetToServerDefaults.")]
        public static partial void FailedToAdvertisePendingShutdown(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 4, Level = LogLevel.Error,
            Message = "Cannot create NamespaceMetadataState for namespace '{NamespaceUri}'.")]
        public static partial void CannotCreateNamespaceMetadataState(this ILogger logger, string namespaceUri);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 5, Level = LogLevel.Information,
            Message = "Delete application certificate {Thumbprint}")]
        public static partial void DeleteApplicationCertificate(this ILogger logger, string? thumbprint);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 6, Level = LogLevel.Information,
            Message = "Add application certificate {Certificate}")]
        public static partial void AddApplicationCertificate(this ILogger logger, Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 7, Level = LogLevel.Warning,
            Message = "Restored the previous application certificate for {Type} after " +
                "the replacement failed to commit.")]
        public static partial void RestoredPreviousCertificateAfterReplacementFailed(
            this ILogger logger,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 8, Level = LogLevel.Critical,
            Message = "Failed to restore the previous application certificate for {Type} after " +
                "the replacement failed to commit. Server configuration may be inconsistent.")]
        public static partial void FailedToRestorePreviousCertificateAfterReplacementFailed(
            this ILogger logger,
            Exception ex,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 9, Level = LogLevel.Warning,
            Message = "Restored the previous application certificate for {Type} after " +
                "importing its issuer chain failed to commit.")]
        public static partial void RestoredPreviousCertificateAfterIssuerImportFailed(
            this ILogger logger,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 10, Level = LogLevel.Critical,
            Message = "Failed to restore the previous application certificate for {Type} after " +
                "importing its issuer chain failed to commit. Server configuration may be inconsistent.")]
        public static partial void FailedToRestorePreviousCertificateAfterIssuerImportFailed(
            this ILogger logger,
            Exception ex,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 11, Level = LogLevel.Critical,
            Message = "Failed to remove a newly staged issuer certificate {Thumbprint} from {Group} " +
                "while rolling back a PushManagement operation. Server configuration may be inconsistent.")]
        public static partial void FailedToRemoveStagedIssuerCertificate(
            this ILogger logger,
            Exception ex,
            string thumbprint,
            NodeId group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 12, Level = LogLevel.Error,
            Message = "Failed to verify integrity of the new certificate {Certificate} against " +
                "the certificate group's TrustList.")]
        public static partial void FailedToVerifyIntegrityAgainstTrustList(
            this ILogger logger,
            Exception ex,
            Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 13, Level = LogLevel.Error,
            Message = "Failed to verify integrity of the new certificate {Certificate} and the issuer list.")]
        public static partial void FailedToVerifyIntegrityAndIssuerList(
            this ILogger logger,
            Exception ex,
            Certificate? certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 14, Level = LogLevel.Information,
            Message = "Staged self-signed certificate {Subject} for {Group}/{Type}.")]
        public static partial void StagedSelfSignedCertificate(
            this ILogger logger,
            string subject,
            NodeId group,
            NodeId type);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 15, Level = LogLevel.Information,
            Message = "Create signing request {Certificate}")]
        public static partial void CreateSigningRequest(this ILogger logger, Certificate certificate);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 16, Level = LogLevel.Warning,
            Message = "Certificate-alarm re-evaluation after ApplyChanges commit failed.")]
        public static partial void CertificateAlarmReevaluationAfterCommitFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 17, Level = LogLevel.Information,
            Message = "Apply Changes for application certificate scheduled in {Grace} ms...")]
        public static partial void ApplyChangesScheduled(this ILogger logger, double grace);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 18, Level = LogLevel.Information,
            Message = "Apply Changes running...")]
        public static partial void ApplyChangesRunning(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 19, Level = LogLevel.Warning,
            Message = "Listener {Listener} failed to close channels for {CertType}.")]
        public static partial void ListenerFailedToCloseChannels(
            this ILogger logger,
            Exception ex,
            string listener,
            NodeId certType);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 20, Level = LogLevel.Information,
            Message = "Apply Changes for application certificate completed: {Count} SecureChannel(s) cut.")]
        public static partial void ApplyChangesCompleted(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 21, Level = LogLevel.Warning,
            Message = "Certificate-alarm re-evaluation after ApplyChanges failed.")]
        public static partial void CertificateAlarmReevaluationFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 22, Level = LogLevel.Critical,
            Message = "Apply Changes for application certificate update failed. " +
                "Server could be in a faulted state.")]
        public static partial void ApplyChangesUpdateFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 23, Level = LogLevel.Error,
            Message = "Cannot find ObjectIds.Server_Namespaces node.")]
        public static partial void CannotFindServerNamespacesNode(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 24, Level = LogLevel.Error,
            Message = "Error searching NamespaceMetadata for namespaceUri {NamespaceUri}.")]
        public static partial void ErrorSearchingNamespaceMetadata(
            this ILogger logger,
            Exception ex,
            string namespaceUri);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 25, Level = LogLevel.Warning,
            Message = "Initial certificate-alarm evaluation failed.")]
        public static partial void InitialCertificateAlarmEvaluationFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 26, Level = LogLevel.Warning,
            Message = "Alarm evaluation tick failed.")]
        public static partial void AlarmEvaluationTickFailed(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 27, Level = LogLevel.Debug,
            Message = "Skipping unreadable certificate in group {Group}.")]
        public static partial void SkippingUnreadableCertificate(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 28, Level = LogLevel.Warning,
            Message = "Failed to refresh CertificateExpired alarm inputs for group {Group}.")]
        public static partial void FailedToRefreshCertificateExpiredAlarm(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 29, Level = LogLevel.Warning,
            Message = "Failed to refresh TrustListOutOfDate alarm inputs for group {Group}.")]
        public static partial void FailedToRefreshTrustListOutOfDateAlarm(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 30, Level = LogLevel.Warning,
            Message = "Failed to create certificate alarms for group {Group}.")]
        public static partial void FailedToCreateCertificateAlarms(
            this ILogger logger,
            Exception ex,
            string group);
    }
}
