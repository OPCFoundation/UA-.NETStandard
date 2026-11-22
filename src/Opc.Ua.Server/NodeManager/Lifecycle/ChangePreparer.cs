// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Identity;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns the private metadata captured under the lifecycle writer and used by candidate preparation callbacks.
    /// </summary>
    internal sealed class ChangePreparer
    {
        /// <summary>
        /// Captures the serving metadata before invoking any candidate factory.
        /// </summary>
        internal ChangePreparer(IServerInternal server)
        {
            Server = server;
            NamespaceUris = new NamespaceTable(server.NamespaceUris.GetSnapshot(out long namespaceRevision).ToArray()!);
            NamespaceRevision = namespaceRevision;
            ServerUris = new StringTable(server.ServerUris.GetSnapshot(out long serverRevision).ToArray()!);
            ServerRevision = serverRevision;
            TypeTree = server.TypeTree.CaptureSnapshot(
                out TypeTable originalTypes, out long typeRevision, NamespaceUris);
            OriginalTypes = originalTypes;
            TypeRevision = typeRevision;
            if (server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server factory does not support staged registrations.");
            }
            Factory = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            OriginalFactory = originalFactory;
            FactoryRevision = factoryRevision;
        }

        /// <summary>
        /// Gets the serving server.
        /// </summary>
        internal IServerInternal Server { get; }

        /// <summary>
        /// Gets the privately staged namespaces.
        /// </summary>
        internal NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Gets the captured namespace revision.
        /// </summary>
        internal long NamespaceRevision { get; }

        /// <summary>
        /// Gets the privately staged server URIs.
        /// </summary>
        internal StringTable ServerUris { get; }

        /// <summary>
        /// Gets the captured server URI revision.
        /// </summary>
        internal long ServerRevision { get; }

        /// <summary>
        /// Gets the private type overlay.
        /// </summary>
        internal TypeTable TypeTree { get; }

        /// <summary>
        /// Gets the captured canonical table.
        /// </summary>
        internal TypeTable OriginalTypes { get; }

        /// <summary>
        /// Gets the captured type revision.
        /// </summary>
        internal long TypeRevision { get; }

        /// <summary>
        /// Gets the private encodeable registrations.
        /// </summary>
        internal EncodeableFactory Factory { get; }

        /// <summary>
        /// Gets a builder that only mutates the private registrations.
        /// </summary>
        internal IEncodeableFactoryBuilder Builder => Factory.Builder;

        /// <summary>
        /// Gets the canonical registration table.
        /// </summary>
        internal EncodeableFactory OriginalFactory { get; }

        /// <summary>
        /// Gets the captured registration revision.
        /// </summary>
        internal long FactoryRevision { get; }

        /// <summary>
        /// Creates independently rebindable handles for a candidate, including contexts copied by its constructor.
        /// </summary>
        internal StagingServer CreateServer()
        {
            return new StagingServer(this);
        }

        /// <summary>
        /// Invokes a candidate factory with its own stable metadata handles.
        /// </summary>
        internal async ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IAsyncNodeManagerFactory factory,
            ApplicationConfiguration configuration,
            bool replacement,
            Func<IAsyncNodeManager, bool> isRegistered,
            CancellationToken cancellationToken)
        {
            using var server = new StagingServer(this);
            IAsyncNodeManager manager;
            try
            {
                manager = await factory.CreateAsync(server, configuration, cancellationToken)
                    .ConfigureAwait(false) ??
                    throw new InvalidOperationException(replacement
                        ? "The replacement NodeManager factory returned null."
                        : "The NodeManager factory returned null.");
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                try
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Candidate creation and historian cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
                throw;
            }

            IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetGenerationIdentity(manager);
            if (isRegistered(manager) ||
                !ReferenceEquals(s_candidates.GetValue(identity, _ => server), server))
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw new NodeManagerAlreadyRegisteredException();
            }
            try
            {
                NodeManagerGenerationRegistry.ReserveCandidate(manager);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                s_candidates.Remove(identity);
                try
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Candidate identity admission and staging cleanup both failed.",
                        failure,
                        cleanupFailure);
                }
                throw;
            }
            server.RegisteredFacade = manager;
            return manager;
        }

        /// <summary>
        /// Rejects staged historian state that cannot be published through a readiness callback.
        /// </summary>
        internal static void ValidateHistorianReadiness(IAsyncNodeManager manager)
        {
            StagingServer? server = GetServer(manager);
            if (server?.HasPendingHistorianState != true)
            {
                return;
            }
            if (!server.CanPublishHistorianRegistrations)
            {
                throw new NotSupportedException(
                    "The serving historian registry does not support candidate-owned registrations.");
            }
            if (!NodeManagerReadiness.IsParticipant(manager))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Historian registration during preparation requires {0} to implement " +
                    "INodeManagerReadinessNotification.OnServerReadyAsync.",
                    manager.GetType().FullName ?? "(unknown)");
            }
        }

        /// <summary>
        /// Gets the candidate's explicit staging server, if it has one.
        /// </summary>
        internal static StagingServer? GetServer(IAsyncNodeManager manager)
        {
            IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetGenerationIdentity(manager);
            if (s_candidates.TryGetValue(identity, out StagingServer? server))
            {
                return server;
            }
            return identity is AsyncCustomNodeManager asynchronous
                ? asynchronous.Server as StagingServer
#pragma warning disable CS0618, UA0031 // Legacy CustomNodeManager2 owners stage through their server.
                : identity is AsyncNodeManagerAdapter { SyncNodeManager: CustomNodeManager2 synchronous }
                    ? synchronous.Server as StagingServer
                    : null;
#pragma warning restore CS0618, UA0031
        }

        /// <summary>
        /// Publishes canonical storage into all cached handles of this candidate.
        /// </summary>
        internal static void Bind(IAsyncNodeManager manager)
        {
            GetServer(manager)?.Bind();
        }

        /// <summary>
        /// Detaches candidate metadata before deletion or disposal can remove serving registrations.
        /// </summary>
        internal static void Detach(IAsyncNodeManager manager)
        {
            GetServer(manager)?.Detach();
        }

        /// <summary>
        /// Publishes candidate historian registrations after its readiness callback succeeds.
        /// </summary>
        /// <param name="manager">The ready NodeManager.</param>
        internal static bool PublishHistorianRegistrations(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.PublishHistorianRegistrations() == true;
        }

        /// <summary>
        /// Withdraws candidate historian registrations when its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retired NodeManager.</param>
        internal static bool RetireHistorianRegistrations(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.RetireHistorianRegistrations() == true;
        }

        /// <summary>
        /// Disposes candidate-owned historian builders and staged provider state.
        /// </summary>
        /// <param name="manager">The candidate NodeManager.</param>
        /// <returns>The asynchronous cleanup.</returns>
        internal static ValueTask DisposeAsync(IAsyncNodeManager manager)
        {
            return GetServer(manager)?.DisposeAsync() ?? default;
        }

        /// <summary>
        /// Releases the exact candidate association after verified cleanup, never a shared resource's association.
        /// </summary>
        internal static void Release(IAsyncNodeManager manager)
        {
            s_candidates.Remove(NodeManagerGenerationRegistry.GetGenerationIdentity(manager));
        }

        /// <summary>
        /// Revokes a manager's service-level writer as soon as its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retiring NodeManager.</param>
        internal static void RevokeServiceLevelControl(IAsyncNodeManager manager)
        {
            GetServer(manager)?.RevokeServiceLevelControl();
        }

        /// <summary>Associates managers with staging servers through weak keys.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, StagingServer> s_candidates = new();

        /// <summary>
        /// Candidate-specific server facade. Metadata handles are stable even when copied into a system context.
        /// </summary>
        internal sealed class StagingServer :
            IServerInternal, INodeIdFactoryProvider, ITimeProviderProvider,
            IHistorianRegistryProvider, IHistorianBuilderRegistry,
            AliasNames.IAliasNameStoreRegistryProvider, AliasNames.IAliasNameStoreRegistrationValidator,
            IConformanceUnitsManagerProvider, IServerEndpointRegistryProvider, ITransportListenerRegistryProvider,
            ISecurityPolicyRegistryProvider, ICertificateValidatorProvider, IHistoryContinuationPointStoreProvider,
            IAsyncDisposable,
            IServerServiceLevelControl
        {
            /// <summary>
            /// Creates a candidate's metadata handles over the preparation's shared private storage.
            /// </summary>
            internal StagingServer(ChangePreparer preparation)
            {
                m_server = preparation.Server;
                m_servingNamespaceUris = m_server.NamespaceUris;
                m_servingServerUris = m_server.ServerUris;
                m_servingTypes = preparation.OriginalTypes;
                m_servingFactory = preparation.OriginalFactory;
                NamespaceUris = new NamespaceTable();
                NamespaceUris.BindTo(preparation.NamespaceUris);
                ServerUris = new StringTable();
                ServerUris.BindTo(preparation.ServerUris);
                TypeTree = new TypeTable(NamespaceUris);
                TypeTree.BindTo(preparation.TypeTree);
                TypeTree.RejectCandidateConflicts();
                Factory = preparation.Factory.Fork();
                ((EncodeableFactory)Factory).BindTo(preparation.Factory);
                DefaultSystemContext = new ServerSystemContext(this).Copy(m_server.DefaultSystemContext);
                DefaultSystemContext.NamespaceUris = NamespaceUris;
                DefaultSystemContext.ServerUris = ServerUris;
                DefaultSystemContext.TypeTable = TypeTree;
                DefaultSystemContext.EncodeableFactory = Factory;
                MessageContext = new ServiceMessageContext(m_server.Telemetry, Factory)
                {
                    NamespaceUris = NamespaceUris,
                    ServerUris = ServerUris,
                    MaxStringLength = m_server.MessageContext.MaxStringLength,
                    MaxByteStringLength = m_server.MessageContext.MaxByteStringLength,
                    MaxArrayLength = m_server.MessageContext.MaxArrayLength,
                    MaxMessageSize = m_server.MessageContext.MaxMessageSize,
                    MaxEncodingNestingLevels = m_server.MessageContext.MaxEncodingNestingLevels,
                    MaxDecoderRecoveries = m_server.MessageContext.MaxDecoderRecoveries
                };
                m_historianRegistry = m_server is IHistorianRegistryProvider historianRegistryProvider
                    ? new StagedHistorianProviderRegistry(
                        NamespaceUris,
                        historianRegistryProvider.HistorianRegistry,
                        this)
                    : null;
            }

            /// <inheritdoc/>
            public NamespaceTable NamespaceUris { get; }

            /// <inheritdoc/>
            public StringTable ServerUris { get; }

            /// <inheritdoc/>
            public TypeTable TypeTree { get; }

            /// <inheritdoc/>
            public IEncodeableFactory Factory { get; }

            /// <inheritdoc/>
            public ServerSystemContext DefaultSystemContext { get; }

            /// <inheritdoc/>
            public IServiceMessageContext MessageContext { get; }

            /// <inheritdoc/>
            public TimeProvider TimeProvider => (m_server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;

            /// <inheritdoc/>
            public IRebasableNodeIdFactory? NodeIdFactory => (m_server as INodeIdFactoryProvider)?.NodeIdFactory;

            /// <inheritdoc/>
            public bool? DetectNodeIdCollisions => (m_server as INodeIdFactoryProvider)?.DetectNodeIdCollisions;

            /// <inheritdoc/>
            public ArrayOf<EndpointDescription> ServerEndpoints =>
                m_server is IServerEndpointRegistryProvider provider ? provider.ServerEndpoints : [];

            /// <inheritdoc/>
            public IReadOnlyList<ITransportListener> TransportListeners =>
                (m_server as ITransportListenerRegistryProvider)?.TransportListeners ?? [];

            /// <inheritdoc/>
            public ISecurityPolicyRegistry SecurityPolicyRegistry =>
                (m_server as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry ?? SecurityPolicies.Default;

            /// <inheritdoc/>
            public ICertificateValidatorEx? CertificateValidator =>
                (m_server as ICertificateValidatorProvider)?.CertificateValidator;

            /// <inheritdoc/>
            public IHistoryContinuationPointStore? HistoryContinuationPointStore =>
                (m_server as IHistoryContinuationPointStoreProvider)?.HistoryContinuationPointStore;

            /// <inheritdoc/>
            public IEnumerable<Uri> EndpointAddresses => m_server.EndpointAddresses;

            /// <inheritdoc/>
            public IMainNodeManagerFactory MainNodeManagerFactory => m_server.MainNodeManagerFactory;

            /// <inheritdoc/>
            public IMasterNodeManager NodeManager => m_server.NodeManager;

            /// <inheritdoc/>
            public ICoreNodeManager CoreNodeManager => m_server.CoreNodeManager;

            /// <inheritdoc/>
            public IDiagnosticsNodeManager DiagnosticsNodeManager => m_server.DiagnosticsNodeManager;

            /// <inheritdoc/>
            public IConfigurationNodeManager ConfigurationNodeManager => m_server.ConfigurationNodeManager;

            /// <inheritdoc/>
            public EventManager EventManager => m_server.EventManager;

            /// <inheritdoc/>
            public ResourceManager ResourceManager => m_server.ResourceManager;

            /// <inheritdoc/>
            public RequestManager RequestManager => m_server.RequestManager;

            /// <inheritdoc/>
            public AggregateManager AggregateManager => m_server.AggregateManager;

            /// <inheritdoc/>
            public ConformanceUnitsManager? ConformanceUnitsManager => IsStaging || IsDetached
                ? null
                : (m_server as IConformanceUnitsManagerProvider)?.ConformanceUnitsManager;

            /// <inheritdoc/>
            public ISessionManager SessionManager => m_server.SessionManager;

            /// <inheritdoc/>
            public IRoleManager RoleManager => m_server.RoleManager;

            /// <inheritdoc/>
            public IServerIdentityRegistry IdentityRegistry => m_server.IdentityRegistry;

            /// <inheritdoc/>
            public UserManagement.IUserManagement? UserManagement => m_server.UserManagement;

            /// <inheritdoc/>
            public IHistorianProviderRegistry HistorianRegistry
            {
                get
                {
                    return m_historianRegistry ??
                        throw new NotSupportedException("The server does not provide a historian registry.");
                }
            }

            /// <inheritdoc/>
            void IHistorianBuilderRegistry.RegisterHistorianBuilder(HistorianBuilder builder)
            {
                if (builder == null)
                {
                    throw new ArgumentNullException(nameof(builder));
                }
                lock (m_historianBuildersLock)
                {
                    if (IsDetached || m_historianBuildersDisposed)
                    {
                        throw new ObjectDisposedException(nameof(StagingServer));
                    }
                    if (m_server is not IHistorianBuilderRegistry ||
                        m_historianRegistry?.SupportsOwnedRegistrations != true)
                    {
                        throw new NotSupportedException(
                            "The serving server cannot manage candidate historian builder lifetimes.");
                    }
                    if (!m_historianBuilders.Contains(builder))
                    {
                        m_historianBuilders.Add(builder);
                    }
                }
            }

            /// <inheritdoc/>
            void IHistorianBuilderRegistry.UnregisterHistorianBuilder(HistorianBuilder builder)
            {
                if (builder == null)
                {
                    throw new ArgumentNullException(nameof(builder));
                }
                lock (m_historianBuildersLock)
                {
                    m_historianBuilders.Remove(builder);
                }
            }

            /// <inheritdoc/>
            public AliasNames.IAliasNameStoreRegistry AliasNameStoreRegistry
            {
                get
                {
                    EnsurePublishedRegistryAccess();
                    return m_server is AliasNames.IAliasNameStoreRegistryProvider provider
                        ? provider.AliasNameStoreRegistry
                        : throw new NotSupportedException("The server does not provide an alias-name registry.");
                }
            }

            /// <inheritdoc />
            public void ValidateAliasNameStoreRegistration(
                AliasNames.IAliasNameStore store, bool replacesRegisteredStore)
            {
                if (m_server is AliasNames.IAliasNameStoreRegistryProvider provider)
                {
                    AliasNames.AliasNameNodeManager.ValidateRegistryRegistration(
                        provider.AliasNameStoreRegistry, store, replacesRegisteredStore);
                }
            }

            /// <inheritdoc/>
            public ISubscriptionManager SubscriptionManager => m_server.SubscriptionManager;

            /// <inheritdoc/>
            public IMonitoredItemQueueFactory MonitoredItemQueueFactory => m_server.MonitoredItemQueueFactory;

            /// <inheritdoc/>
            public ISubscriptionStore SubscriptionStore => m_server.SubscriptionStore;

            /// <inheritdoc/>
            public ITelemetryContext Telemetry => m_server.Telemetry;

            /// <inheritdoc/>
            public bool IsRunning => m_server.IsRunning;

            /// <inheritdoc/>
            public ServerState CurrentState => m_server.CurrentState;

            /// <inheritdoc/>
            public ServerObjectState ServerObject => m_server.ServerObject;

            /// <inheritdoc/>
            public bool Auditing => m_server.Auditing;

            /// <inheritdoc/>
            public ISystemContext DefaultAuditContext => DefaultSystemContext;

            /// <summary>
            /// Gets whether this candidate's metadata is still private.
            /// </summary>
            internal bool IsStaging => Volatile.Read(ref m_isStaging);

            /// <summary>
            /// Gets or sets the exact candidate facade returned by the factory.
            /// </summary>
            internal IAsyncNodeManager? RegisteredFacade
            {
                get => Volatile.Read(ref m_registeredFacade);
                set => Volatile.Write(ref m_registeredFacade, value);
            }

            /// <summary>
            /// Gets whether this facade has been detached from its published generation.
            /// </summary>
            internal bool IsDetached => Volatile.Read(ref m_isDetached);

            /// <summary>
            /// Gets whether preparation recorded candidate-owned historian bindings that require readiness.
            /// </summary>
            internal bool HasPendingHistorianState => m_historianRegistry?.RequiresReadiness == true;

            /// <summary>
            /// Gets whether the serving historian registry supports owner-scoped publication.
            /// </summary>
            internal bool CanPublishHistorianRegistrations =>
                m_historianRegistry?.SupportsOwnedRegistrations == true;

            /// <summary>
            /// Publishes candidate historian bindings and transfers builder tracking to the server.
            /// </summary>
            internal bool PublishHistorianRegistrations()
            {
                EnsurePublishedRegistryAccess();
                HistorianBuilder[] builders;
                lock (m_historianBuildersLock)
                {
                    builders = [.. m_historianBuilders];
                }
                if (builders.Length != 0 && m_server is not IHistorianBuilderRegistry)
                {
                    throw new NotSupportedException("The server does not manage historian builder lifetimes.");
                }

                m_historianRegistry?.Publish();
                if (m_server is IHistorianBuilderRegistry registry)
                {
                    foreach (HistorianBuilder builder in builders)
                    {
                        registry.RegisterHistorianBuilder(builder);
                    }
                }
                return m_historianRegistry?.HasRegistrations == true;
            }

            /// <summary>
            /// Withdraws this candidate's bindings from the serving registry.
            /// </summary>
            internal bool RetireHistorianRegistrations()
            {
                return m_historianRegistry?.Retire() == true;
            }

            /// <summary>
            /// Gets explicit namespace registrations retained until routing publication.
            /// </summary>
            internal IEnumerable<string> NamespaceRegistrations => m_pendingNamespaces;

            /// <inheritdoc/>
            public void Dispose()
            {
                // The facade does not own the running server.
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Volatile.Write(ref m_registeredFacade, null);
                HistorianBuilder[] builders;
                bool alreadyDisposed;
                lock (m_historianBuildersLock)
                {
                    alreadyDisposed = m_historianBuildersDisposed;
                    builders = alreadyDisposed ? [] : [.. m_historianBuilders];
                }
                if (alreadyDisposed)
                {
                    ReleaseAbandonedReferences();
                    return;
                }

                List<Exception>? failures = null;
                try
                {
                    RetireHistorianRegistrations();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures = [exception];
                }

                foreach (HistorianBuilder builder in builders)
                {
                    bool cleanupCompleted = true;
                    try
                    {
                        await builder.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        cleanupCompleted = false;
                        failures ??= [];
                        failures.Add(exception);
                    }
                    if (m_server is IHistorianBuilderRegistry registry)
                    {
                        try
                        {
                            registry.UnregisterHistorianBuilder(builder);
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            cleanupCompleted = false;
                            failures ??= [];
                            failures.Add(exception);
                        }
                    }
                    if (!cleanupCompleted)
                    {
                        lock (m_historianBuildersLock)
                        {
                            if (!m_historianBuilders.Contains(builder))
                            {
                                m_historianBuilders.Add(builder);
                            }
                        }
                    }
                }
                try
                {
                    m_historianRegistry?.Dispose();
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
                if (failures != null)
                {
                    throw new AggregateException(
                        "One or more candidate historian resources failed during cleanup.",
                        failures);
                }
                lock (m_historianBuildersLock)
                {
                    m_historianBuilders.Clear();
                    m_historianBuildersDisposed = true;
                }
                ReleaseAbandonedReferences();
            }

            /// <inheritdoc/>
            public Action<byte> ClaimServiceLevelControl()
            {
                EnsurePublishedRegistryAccess();
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    if (m_serviceLevelControlWriter is not null)
                    {
                        throw new InvalidOperationException(
                            "This NodeManager generation already claimed service-level publication.");
                    }
                }
                if (m_server is not IServerServiceLevelControl control)
                {
                    throw new NotSupportedException("The server does not provide service-level control.");
                }
                Action<byte> publishLevel = control.ClaimServiceLevelControl();
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        if (m_server is IServerServiceLevelControlRevocation revocation)
                        {
                            revocation.ReleaseServiceLevelControl(publishLevel);
                        }
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    m_serviceLevelControlWriter = publishLevel;
                }
                return level =>
                {
                    EnsurePublishedRegistryAccess();
                    if (Volatile.Read(ref m_serviceLevelControlRevoked))
                    {
                        throw new InvalidOperationException(
                            "The NodeManager generation no longer owns service-level publication.");
                    }
                    publishLevel(level);
                };
            }

            /// <summary>
            /// Revokes this generation's service-level writer and releases the serving server's claim.
            /// </summary>
            internal void RevokeServiceLevelControl()
            {
                Action<byte>? writer;
                lock (m_serviceLevelControlLock)
                {
                    if (m_serviceLevelControlRevoked)
                    {
                        return;
                    }
                    Volatile.Write(ref m_serviceLevelControlRevoked, true);
                    writer = m_serviceLevelControlWriter;
                    m_serviceLevelControlWriter = null;
                }
                if (writer is not null && m_server is IServerServiceLevelControlRevocation revocation)
                {
                    revocation.ReleaseServiceLevelControl(writer);
                }
            }

            /// <inheritdoc/>
            public ServerSystemContext CreateSystemContext(ISession session)
            {
                return DefaultSystemContext.Copy(session);
            }

            /// <inheritdoc/>
            public T? FindPredefinedNode<T>(NodeId nodeId) where T : NodeState
                => m_server.FindPredefinedNode<T>(nodeId);

            /// <inheritdoc/>
            public IEnumerable<T> FindNodeManagers<T>() where T : class => m_server.FindNodeManagers<T>();

            /// <inheritdoc/>
            public void ReportEvent(IFilterTarget e) => m_server.ReportEvent(e);

            /// <inheritdoc/>
            public void ReportEvent(ISystemContext context, IFilterTarget e) => m_server.ReportEvent(context, e);

            /// <inheritdoc/>
            public ValueTask ReportEventAsync(IFilterTarget e, CancellationToken cancellationToken = default)
                => m_server.ReportEventAsync(e, cancellationToken);

            /// <inheritdoc/>
            public ValueTask ReportEventAsync(
                ISystemContext context, IFilterTarget e, CancellationToken cancellationToken = default)
                => m_server.ReportEventAsync(context, e, cancellationToken);

            /// <inheritdoc/>
            public void ReportAuditEvent(ISystemContext context, AuditEventState e)
                => m_server.ReportAuditEvent(context, e);

            /// <inheritdoc/>
            public void ConditionRefresh(OperationContext context, uint subscriptionId)
                => m_server.ConditionRefresh(context, subscriptionId);

            /// <inheritdoc/>
            public void ConditionRefresh2(OperationContext context, uint subscriptionId, uint monitoredItemId)
                => m_server.ConditionRefresh2(context, subscriptionId, monitoredItemId);

            /// <inheritdoc/>
            public void UpdateServerStatus(Action<ServerStatusValue> action) => m_server.UpdateServerStatus(action);

            /// <inheritdoc/>
            public void UpdateServerDiagnostics(Action<ServerDiagnosticsSummaryDataType> update)
                => m_server.UpdateServerDiagnostics(update);

            /// <inheritdoc/>
            public ValueTask CloseSessionAsync(
                OperationContext context, NodeId sessionId, bool deleteSubscriptions,
                CancellationToken cancellationToken = default)
                => m_server.CloseSessionAsync(context, sessionId, deleteSubscriptions, cancellationToken);

            /// <inheritdoc/>
            public ValueTask DeleteSubscriptionAsync(uint subscriptionId, CancellationToken cancellationToken = default)
                => m_server.DeleteSubscriptionAsync(subscriptionId, cancellationToken);

            /// <summary>
            /// Retains an owned node's external reference until the candidate is published.
            /// </summary>
            internal bool TryStageExternalReference(
                AsyncCustomNodeManager owner,
                NodeState target,
                LocalReference reference)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        throw new InvalidOperationException(
                            "External references cannot be added to a detached node manager.");
                    }
                    if (!IsStaging)
                    {
                        return false;
                    }
                    bool exists = m_stagedNodeReferences.Exists(entry =>
                        ReferenceEquals(entry.Target, target) && SameReference(entry.Reference, reference));
                    if (!exists)
                    {
                        m_stagedNodeReferences.Add((owner, target, reference));
                    }
                    if (m_preparedExternalReferences is { } references)
                    {
                        AddPreparedReference(references, reference, target);
                    }
                    return true;
                }
            }

            /// <summary>
            /// Checks whether a staged owned-node reference requires the source at publication.
            /// </summary>
            internal bool RequiresExternalSource(NodeId sourceId)
            {
                lock (m_externalReferencesLock)
                {
                    return m_stagedNodeReferences.Exists(entry => entry.Reference.SourceId == sourceId);
                }
            }

            /// <summary>
            /// Connects staged references to the dictionary used by address-space preparation.
            /// </summary>
            internal void TrackExternalReferences(Dictionary<NodeId, IList<IReference>> references)
            {
                lock (m_externalReferencesLock)
                {
                    if (m_preparedExternalReferences is not null)
                    {
                        throw new InvalidOperationException("External reference preparation has already started.");
                    }
                    m_preparedExternalReferences = references;
                    foreach (var entry in m_stagedNodeReferences)
                    {
                        if (ReferenceEquals(entry.Owner.Find(entry.Target.NodeId), entry.Target))
                        {
                            AddPreparedReference(references, entry.Reference, entry.Target);
                        }
                    }
                }
            }

            /// <summary>
            /// Discards private contributions targeting a node removed before publication.
            /// </summary>
            internal void DiscardNodeReferences(NodeState target)
            {
                lock (m_externalReferencesLock)
                {
                    m_stagedNodeReferences.RemoveAll(entry => ReferenceEquals(entry.Target, target));
                    if (m_preparedExternalReferences is not { } references)
                    {
                        return;
                    }
                    foreach (KeyValuePair<NodeId, IList<IReference>> source in references)
                    {
                        for (int index = source.Value.Count - 1; index >= 0; index--)
                        {
                            IReference reference = source.Value[index];
                            if (!reference.TargetId.IsAbsolute && (NodeId)reference.TargetId == target.NodeId &&
                                (reference is not OwnedExternalReference owned ||
                                    ReferenceEquals(owned.TargetNode, target)))
                            {
                                source.Value.RemoveAt(index);
                                RemoveCapturedReference(source.Key, reference);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Forgets published contributions removed before their manager retires.
            /// </summary>
            internal void DiscardCapturedReferences(List<LocalReference> references)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (LocalReference reference in references)
                    {
                        bool belongsToRemovedTarget = true;
                        if (m_externalReferences.TryGetValue(
                            reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                        {
                            var key = new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                            if (captured.TryGetValue(key, out NodeState? target))
                            {
                                belongsToRemovedTarget = reference.DeletedTarget is null ||
                                    target is null ||
                                    ReferenceEquals(reference.DeletedTarget, target);
                                if (belongsToRemovedTarget)
                                {
                                    captured.Remove(key);
                                }
                            }
                        }
                        if (belongsToRemovedTarget &&
                            reference.SourceId == ObjectIds.Server &&
                            reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                            !reference.IsInverse &&
                            (reference.DeletedTarget is null ||
                                !m_pendingRootNotifierReferences.TryGetValue(
                                    reference.TargetId, out NodeState? notifier) ||
                                ReferenceEquals(reference.DeletedTarget, notifier)))
                        {
                            m_pendingRootNotifierReferences.Remove(reference.TargetId);
                        }
                    }
                }
            }

            /// <summary>
            /// Discards staged references whose exact target was replaced before publication.
            /// </summary>
            internal void PruneStagedExternalReferences()
            {
                lock (m_externalReferencesLock)
                {
                    for (int index = m_stagedNodeReferences.Count - 1; index >= 0; index--)
                    {
                        var entry = m_stagedNodeReferences[index];
                        if (ReferenceEquals(entry.Owner.Find(entry.Target.NodeId), entry.Target))
                        {
                            continue;
                        }
                        m_stagedNodeReferences.RemoveAt(index);
                        int currentIndex = m_stagedNodeReferences.FindIndex(candidate =>
                            SameReference(candidate.Reference, entry.Reference) &&
                            ReferenceEquals(candidate.Owner.Find(candidate.Target.NodeId), candidate.Target));
                        if (m_preparedExternalReferences is not { } references)
                        {
                            continue;
                        }
                        if (currentIndex < 0)
                        {
                            RemovePreparedReference(references, entry.Reference);
                        }
                        else
                        {
                            var current = m_stagedNodeReferences[currentIndex];
                            AddPreparedReference(references, current.Reference, current.Target);
                        }
                    }
                }
            }

            /// <summary>
            /// Captures the contributions whose removal belongs to publication, not address-space deletion.
            /// </summary>
            /// <param name="references">The candidate's external contributions.</param>
            internal void CaptureExternalReferences(Dictionary<NodeId, IList<IReference>> references)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (KeyValuePair<NodeId, IList<IReference>> source in references)
                    {
                        var contributed = new ReferenceDictionary<NodeState?>();
                        foreach (IReference reference in source.Value)
                        {
                            contributed[new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] =
                                (reference as OwnedExternalReference)?.TargetNode;
                        }
                        m_externalReferences.Add(source.Key, contributed);
                    }
                    m_externalReferencesCaptured = true;
                }
            }

            /// <summary>
            /// Records a published contribution and its exact target.
            /// </summary>
            internal void CapturePublishedReference(LocalReference reference, NodeState target)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsStaging || IsDetached || !m_externalReferencesCaptured)
                    {
                        throw new InvalidOperationException(
                            "The node manager cannot record a published reference.");
                    }
                    if (!m_externalReferences.TryGetValue(
                        reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                    {
                        m_externalReferences.Add(reference.SourceId, captured = new ReferenceDictionary<NodeState?>());
                    }
                    captured[new NodeStateReference(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] = target;
                }
            }

            /// <summary>
            /// Excludes transaction-owned contributions from legacy inverse-reference cleanup.
            /// </summary>
            /// <param name="references">The inverse references discovered during deletion.</param>
            internal void ExcludePreparedReferences(List<LocalReference> references)
            {
                lock (m_externalReferencesLock)
                {
                    references.RemoveAll(reference =>
                        m_externalReferences.TryGetValue(
                            reference.SourceId, out ReferenceDictionary<NodeState?>? contributed) &&
                        contributed.TryGetValue(
                            new NodeStateReference(
                                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId),
                            out NodeState? target) &&
                        (reference.DeletedTarget is null ||
                            target is null ||
                            ReferenceEquals(reference.DeletedTarget, target)));
                }
            }

            /// <summary>
            /// Releases detached contribution metadata after reference cleanup completes.
            /// </summary>
            internal void ReleaseCapturedReferences()
            {
                lock (m_externalReferencesLock)
                {
                    if (!IsDetached)
                    {
                        throw new InvalidOperationException("The node manager must be detached before releasing references.");
                    }
                    m_externalReferences.Clear();
                    m_pendingRootNotifierReferences.Clear();
                    m_withdrawnRootNotifierReferences.Clear();
                    m_stagedNodeReferences.Clear();
                    m_preparedExternalReferences = null;
                }
            }

            /// <summary>
            /// Binds cached metadata handles to canonical storage after compatible publication.
            /// </summary>
            internal void Bind()
            {
                NamespaceUris.BindTo(m_servingNamespaceUris);
                ServerUris.BindTo(m_servingServerUris);
                TypeTree.BindTo(m_servingTypes);
                ((EncodeableFactory)Factory).BindTo(m_servingFactory);
                lock (m_externalReferencesLock)
                {
                    Volatile.Write(ref m_isStaging, false);
                    m_preparedExternalReferences = null;
                    m_stagedNodeReferences.Clear();
                    m_pendingRootNotifierReferences.Clear();
                }
            }

            /// <summary>
            /// Retains candidate namespace registrations without changing serving routing.
            /// </summary>
            internal void StageNamespaceRegistration(string namespaceUri)
            {
                m_pendingNamespaces.Add(namespaceUri);
            }

            /// <summary>
            /// Removes a candidate registration without changing serving routing.
            /// </summary>
            internal bool UnstageNamespaceRegistration(string namespaceUri)
            {
                return m_pendingNamespaces.Remove(namespaceUri);
            }

            /// <summary>
            /// Retains a root-notifier reference contribution until external-reference publication.
            /// </summary>
            internal bool TryStageRootNotifierReference(NodeState notifier)
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        throw new ObjectDisposedException(nameof(StagingServer));
                    }
                    if (!IsStaging)
                    {
                        return false;
                    }
                    m_pendingRootNotifierReferences[notifier.NodeId] = notifier;
                    m_withdrawnRootNotifierReferences.Remove(notifier.NodeId);
                    if (m_preparedExternalReferences is { } references)
                    {
                        AddPreparedReference(
                            references,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId),
                            notifier);
                    }
                    return true;
                }
            }

            /// <summary>
            /// Removes a root-notifier reference contribution that was withdrawn during preparation.
            /// </summary>
            internal void UnstageRootNotifierReference(NodeState notifier)
            {
                lock (m_externalReferencesLock)
                {
                    if (!IsStaging)
                    {
                        return;
                    }
                    if (m_pendingRootNotifierReferences.TryGetValue(notifier.NodeId, out NodeState? current) &&
                        !ReferenceEquals(current, notifier))
                    {
                        return;
                    }
                    m_pendingRootNotifierReferences.Remove(notifier.NodeId);
                    m_withdrawnRootNotifierReferences.Add(notifier.NodeId);
                    if (m_preparedExternalReferences is { } references)
                    {
                        RemovePreparedReference(
                            references,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId));
                    }
                }
            }

            /// <summary>
            /// Gets roots withdrawn while prepared reference updates may already have captured them.
            /// </summary>
            internal NodeId[] GetWithdrawnRootNotifierReferences()
            {
                lock (m_externalReferencesLock)
                {
                    return [.. m_withdrawnRootNotifierReferences];
                }
            }

            /// <summary>
            /// Releases a withdrawn root after its serving reference has been reconciled.
            /// </summary>
            internal void ForgetWithdrawnRootNotifierReference(NodeId notifierId)
            {
                lock (m_externalReferencesLock)
                {
                    m_withdrawnRootNotifierReferences.Remove(notifierId);
                }
            }

            /// <summary>
            /// Merges retained root-notifier contributions into the prepared external-reference set.
            /// </summary>
            internal void AppendRootNotifierReferences(
                Dictionary<NodeId, IList<IReference>> externalReferences)
            {
                lock (m_externalReferencesLock)
                {
                    foreach (NodeState notifier in m_pendingRootNotifierReferences.Values)
                    {
                        AddPreparedReference(
                            externalReferences,
                            new LocalReference(ObjectIds.Server, ReferenceTypeIds.HasNotifier, false, notifier.NodeId),
                            notifier);
                    }
                }
            }

            /// <summary>
            /// Adds a contribution once and records additions made after the initial capture.
            /// </summary>
            private void AddPreparedReference(
                Dictionary<NodeId, IList<IReference>> references,
                LocalReference reference,
                NodeState? target = null)
            {
                if (!references.TryGetValue(reference.SourceId, out IList<IReference>? sourceReferences))
                {
                    references.Add(reference.SourceId, sourceReferences = []);
                }
                int existingIndex = -1;
                for (int index = 0; index < sourceReferences.Count; index++)
                {
                    if (SameReference(sourceReferences[index], reference))
                    {
                        existingIndex = index;
                        break;
                    }
                }
                NodeStateReference addition = target is null
                    ? new NodeStateReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)
                    : new OwnedExternalReference(reference.ReferenceTypeId, reference.IsInverse, target);
                if (existingIndex < 0)
                {
                    sourceReferences.Add(addition);
                }
                else if (target is not null)
                {
                    sourceReferences[existingIndex] = addition;
                }
                if (m_externalReferencesCaptured)
                {
                    if (!m_externalReferences.TryGetValue(
                        reference.SourceId, out ReferenceDictionary<NodeState?>? captured))
                    {
                        m_externalReferences.Add(reference.SourceId, captured = new ReferenceDictionary<NodeState?>());
                    }
                    if (target is not null || !captured.ContainsKey(addition))
                    {
                        captured[addition] = target ??
                            (existingIndex >= 0
                                ? (sourceReferences[existingIndex] as OwnedExternalReference)?.TargetNode
                                : null);
                    }
                }
            }

            /// <summary>
            /// Removes one staged contribution from the prepared and captured sets.
            /// </summary>
            private void RemovePreparedReference(
                Dictionary<NodeId, IList<IReference>> references,
                LocalReference reference)
            {
                if (!references.TryGetValue(reference.SourceId, out IList<IReference>? sourceReferences))
                {
                    return;
                }
                for (int index = sourceReferences.Count - 1; index >= 0; index--)
                {
                    IReference existing = sourceReferences[index];
                    if (SameReference(existing, reference))
                    {
                        sourceReferences.RemoveAt(index);
                        RemoveCapturedReference(reference.SourceId, existing);
                    }
                }
            }

            /// <summary>
            /// Removes a contribution from the captured set when it has already been captured.
            /// </summary>
            private void RemoveCapturedReference(NodeId sourceId, IReference reference)
            {
                if (m_externalReferences.TryGetValue(sourceId, out ReferenceDictionary<NodeState?>? captured))
                {
                    captured.Remove(new NodeStateReference(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId));
                }
            }

            /// <summary>
            /// Releases an unpublished candidate's private reference metadata after disposal.
            /// </summary>
            private void ReleaseAbandonedReferences()
            {
                if (IsStaging && IsDetached)
                {
                    ReleaseCapturedReferences();
                }
            }

            /// <summary>
            /// Compares the external source and reference coordinates.
            /// </summary>
            private static bool SameReference(LocalReference left, LocalReference right)
            {
                return left.SourceId == right.SourceId &&
                    left.ReferenceTypeId == right.ReferenceTypeId &&
                    left.IsInverse == right.IsInverse &&
                    left.TargetId == right.TargetId;
            }

            /// <summary>
            /// Compares a prepared reference with a local contribution.
            /// </summary>
            private static bool SameReference(IReference left, LocalReference right)
            {
                return left.ReferenceTypeId == right.ReferenceTypeId &&
                    left.IsInverse == right.IsInverse &&
                    !left.TargetId.IsAbsolute &&
                    (NodeId)left.TargetId == right.TargetId;
            }

            /// <summary>
            /// Claims the candidate without publishing its namespace registrations.
            /// </summary>
            internal void Attach(IAsyncNodeManager manager)
            {
                IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetGenerationIdentity(manager);
                if (!ReferenceEquals(s_candidates.GetValue(identity, _ => this), this))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
            }

            /// <summary>
            /// Preserves this manager's view privately before cleanup.
            /// </summary>
            internal void Detach()
            {
                lock (m_externalReferencesLock)
                {
                    if (IsDetached)
                    {
                        return;
                    }
                    Volatile.Write(ref m_isDetached, true);
                    Volatile.Write(ref m_registeredFacade, null);
                }
                RetireHistorianRegistrations();
                RevokeServiceLevelControl();
                TypeTree.Detach();
                ((EncodeableFactory)Factory).Detach();
                NamespaceUris.Detach();
                ServerUris.Detach();
            }

            /// <summary>
            /// Keeps non-historian shared registries out of private preparation; runtime services bind during readiness.
            /// </summary>
            private void EnsurePublishedRegistryAccess()
            {
                if (IsStaging || IsDetached)
                {
                    throw new InvalidOperationException("Registry access requires a published generation.");
                }
            }

            /// <summary>The running server whose non-metadata services are forwarded.</summary>
            private readonly IServerInternal m_server;

            /// <summary>The exact facade associated with this candidate.</summary>
            private IAsyncNodeManager? m_registeredFacade;

            /// <summary>Whether the candidate's metadata is still private.</summary>
            private bool m_isStaging = true;

            /// <summary>Whether the candidate's metadata handles have been detached.</summary>
            private bool m_isDetached;

            /// <summary>Roots withdrawn before publication and reconciled against the serving view.</summary>
            private readonly HashSet<NodeId> m_withdrawnRootNotifierReferences = [];

            /// <summary>Canonical handles captured before any durable publication decision.</summary>
            private readonly NamespaceTable m_servingNamespaceUris;
            private readonly StringTable m_servingServerUris;
            private readonly TypeTable m_servingTypes;
            private readonly EncodeableFactory m_servingFactory;
            private readonly StagedHistorianProviderRegistry? m_historianRegistry;
            private readonly Lock m_historianBuildersLock = new();
            private readonly List<HistorianBuilder> m_historianBuilders = [];

            /// <summary>Retains namespace registration intent until routing publication.</summary>
            private readonly HashSet<string> m_pendingNamespaces = [];

            /// <summary>Retains root-notifier reference intent until external-reference publication.</summary>
            private readonly Dictionary<NodeId, NodeState> m_pendingRootNotifierReferences = [];

            /// <summary>Serializes private external-reference bookkeeping.</summary>
            private readonly Lock m_externalReferencesLock = new();

            /// <summary>Retains the exact owners of staged reference targets.</summary>
            private readonly List<(AsyncCustomNodeManager Owner, NodeState Target, LocalReference Reference)>
                m_stagedNodeReferences = [];

            /// <summary>The candidate's mutable external-reference set before publication.</summary>
            private Dictionary<NodeId, IList<IReference>>? m_preparedExternalReferences;

            /// <summary>Frozen reference contributions reconciled by publication before physical deletion.</summary>
            private readonly Dictionary<NodeId, ReferenceDictionary<NodeState?>> m_externalReferences = [];

            /// <summary>Whether the candidate's contributions have been captured for retirement.</summary>
            private bool m_externalReferencesCaptured;

            private readonly Lock m_serviceLevelControlLock = new();
            private Action<byte>? m_serviceLevelControlWriter;
            private bool m_serviceLevelControlRevoked;
            private bool m_historianBuildersDisposed;
        }
    }
}
