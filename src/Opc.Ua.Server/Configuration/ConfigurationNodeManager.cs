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

namespace Opc.Ua.Server
{
    /// <summary>
    /// The Server Configuration Node Manager: the diagnostics manager extended with the OPC UA
    /// Part 12 ServerConfiguration surface. This file holds construction, address-space setup,
    /// access control, transaction diagnostics and the shared state; the Push method handlers,
    /// certificate-slot bookkeeping, ApplyChanges/reset, trust-material propagation,
    /// certificate alarms and namespace metadata live in the sibling
    /// <c>ConfigurationNodeManager.*.cs</c> files.
    /// </summary>
    public partial class ConfigurationNodeManager : DiagnosticsNodeManager, IConfigurationNodeManager
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
            CertificateStoreIdentifier? rejectedStore =
                configuration.SecurityConfiguration.RejectedCertificateStore;
            if (!string.IsNullOrEmpty(rejectedStore?.StorePath))
            {
                // Preserves a configured custom StoreType, like the group
                // stores below - the validator writes rejected certificates
                // through the configured store, so GetRejectedList must read
                // through the same store implementation.
                m_rejectedStore = CreateGroupStoreIdentifier(rejectedStore!);
            }
            m_certificateGroups = [];
            m_configuration = configuration;
            m_namespaceMetadata = new NamespaceMetadataRegistry(this, m_logger);
            m_alarmScheduler = new CertificateAlarmScheduler(m_timeProvider, m_logger);
            // TODO: configure cert groups in configuration
            var defaultApplicationGroup = new ServerCertificateGroup
            {
                NodeId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                BrowseName = BrowseNames.DefaultApplicationGroup,
                CertificateTypes = [],
                ApplicationCertificates = [],
                IssuerStore = CreateGroupStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedIssuerCertificates),
                TrustedStore = CreateGroupStoreIdentifier(
                    configuration.SecurityConfiguration.TrustedPeerCertificates)
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
                    IssuerStore = CreateGroupStoreIdentifier(
                        configuration.SecurityConfiguration.UserIssuerCertificates),
                    TrustedStore = CreateGroupStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedUserCertificates)
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
                    IssuerStore = CreateGroupStoreIdentifier(
                        configuration.SecurityConfiguration.HttpsIssuerCertificates),
                    TrustedStore = CreateGroupStoreIdentifier(
                        configuration.SecurityConfiguration.TrustedHttpsCertificates)
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

        /// <inheritdoc/>
        public TimeSpan ApplyChangesGracePeriod { get; set; }
            = TimeSpan.FromMilliseconds(250);

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
                // Signal any in-flight deferred ApplyChanges and trust-material
                // enforcement pass to stop FIRST, before any member they use is
                // torn down below. DeleteAddressSpaceAsync drains those tasks
                // deterministically during the async server shutdown; Dispose
                // only signals (it must never block on async work), which also
                // covers the direct-construction path where
                // DeleteAddressSpaceAsync is not invoked.
                CancelPendingApplyChanges();

                // Stop reacting to trust-material changes before the
                // listeners/session manager are torn down. An enforcement
                // pass already in flight bails out on the shutdown token
                // cancelled above.
                m_trustMaterialPump?.Dispose();
                m_trustMaterialPump = null;

                // Signal only: Dispose is synchronous. A deferred apply already
                // running stops at its next await once the token trips.
                m_backgroundWork.Dispose();

                // Dispose the TrustList handlers and the rejected store
                // instance held open for reuse across operations.
                foreach (ServerCertificateGroup certGroup in m_certificateGroups)
                {
                    (certGroup.Node?.TrustList?.Handle as IDisposable)?.Dispose();
                }
                Interlocked.Exchange(ref m_rejectedStoreInstance, null)?.Dispose();

                m_namespaceMetadata.Detach();

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

                // The shutdown source was cancelled at the top of this
                // method; only the disposal remains.
                m_shutdownCts.Dispose();

                m_alarmScheduler.Dispose();

                // Disposed LAST: a deferred apply that raced past the
                // cancellation above may still write the self-notification
                // flag from its own thread; disposing the ThreadLocal any
                // earlier would fault that task with ObjectDisposedException
                // and skip its remaining effect application.
                m_selfTrustNotification.Dispose();
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
            Task pumpPending;
            lock (m_pendingApplyChangesLock)
            {
                pending = m_pendingApplyChangesTask;
                pumpPending = m_trustMaterialPumpTask;
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

            try
            {
                // Drain any in-flight trust-material enforcement pass so it
                // never runs against listeners/sessions being torn down. The
                // shutdown token cancelled above makes it bail out promptly.
                await pumpPending.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
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

            // Enforce trust-material changes on live channels/Sessions
            // regardless of origin: any TrustListUpdated/CrlUpdated raised on
            // the certificate manager (push ApplyChanges, WriteTrustListAsync,
            // ITrustListTransaction commits, HA store sync, ...) triggers the
            // same §7.10.9 effect fan-out that ApplyChanges runs, so a client
            // whose certificate was revoked out-of-band is cut immediately
            // instead of at its next security-token renewal.
            SubscribeTrustMaterialEnforcement();

            // OPC 10000-12 §7.8.3: publish the current certificate and CRL
            // state onto the optional alarm inputs (ExpirationDate,
            // TrustListId, LastUpdateTime) and establish the baseline
            // inactive state. Events are suppressed here (emitEvents: false)
            // because the subscription infrastructure is not yet ready during
            // CreateAddressSpace; StartAlarmMonitoring re-evaluates with events
            // once the server is running.
            m_alarmScheduler.UpdateAndEvaluate(systemContext, emitEvents: false);

            // Track Server/Namespaces so metadata lookups stay consistent and
            // default-permission changes invalidate permission caches.
            m_namespaceMetadata.Attach(systemContext);

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
        /// Builds the certificate group's private store identifier from the
        /// configured trust list, preserving the configured
        /// <see cref="CertificateStoreIdentifier.StoreType"/>. Re-inferring
        /// the type from the path alone (the single-argument constructor)
        /// would silently downgrade a configured custom store type to a
        /// directory store, making the push path write through a different
        /// store implementation than the validator reads. The preserved type
        /// resolves through <see cref="CertificateStoreIdentifier.OpenStore()"/>,
        /// i.e. the built-in types plus any type registered via
        /// <see cref="CertificateStoreType.RegisterCertificateStoreType"/>;
        /// DI-registered <see cref="ICertificateStoreProvider"/>s are not
        /// reachable through identifier-based store access (a pre-existing
        /// limitation of the TrustList store plumbing).
        /// </summary>
        private static CertificateStoreIdentifier CreateGroupStoreIdentifier(
            CertificateStoreIdentifier source)
        {
            return string.IsNullOrEmpty(source.StoreType)
                ? new CertificateStoreIdentifier(source.StorePath!)
                : new CertificateStoreIdentifier(source.StorePath!, source.StoreType!);
        }

        private ServerCertificateGroup VerifyGroupId(NodeId certificateGroupId)
        {
            if (certificateGroupId.IsNull)
            {
                certificateGroupId = ObjectIds
                    .ServerConfiguration_CertificateGroups_DefaultApplicationGroup;
            }

            return m_certificateGroups.FirstOrDefault(
                group => Utils.IsEqual(group.NodeId, certificateGroupId))
                ?? throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "Certificate group invalid.");
        }

        private ServerCertificateGroup VerifyGroupAndTypeId(
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

            ServerCertificateGroup certificateGroup = VerifyGroupId(certificateGroupId);

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
        private ICertificateStore? m_rejectedStoreInstance;
        private readonly CertificateAlarmScheduler m_alarmScheduler;
        private readonly NamespaceMetadataRegistry m_namespaceMetadata;
        private readonly Lock m_pendingApplyChangesLock = new();
        private Task m_pendingApplyChangesTask = Task.CompletedTask;
        private Task m_trustMaterialPumpTask = Task.CompletedTask;
        private Task m_pendingResetTask = Task.CompletedTask;
        private readonly CancellationTokenSource m_shutdownCts = new();
        private CertificateChangePump<HashSet<TrustListIdentifier>>? m_trustMaterialPump;
        private readonly ThreadLocal<bool> m_selfTrustNotification = new();
        private readonly BackgroundTaskScope m_backgroundWork =
            new(nameof(ConfigurationNodeManager), AmbientMessageContext.Telemetry);
        private readonly AsyncLocal<List<PendingCertificateRotation>?> m_activeRotationCollector = new();

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

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 30, Level = LogLevel.Warning,
            Message = "Failed to create certificate alarms for group {Group}.")]
        public static partial void FailedToCreateCertificateAlarms(
            this ILogger logger,
            Exception ex,
            string group);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 31, Level = LogLevel.Warning,
            Message = "Failed to notify the certificate manager of the TrustList change for scope {Scope}.")]
        public static partial void TrustListChangeNotificationFailed(
            this ILogger logger,
            Exception ex,
            string scope);

        [LoggerMessage(EventId = ServerEventIds.ConfigurationNodeManager + 32, Level = LogLevel.Warning,
            Message = "Trust-material change enforcement pass failed.")]
        public static partial void TrustMaterialEnforcementFailed(
            this ILogger logger,
            Exception ex);
    }
}
