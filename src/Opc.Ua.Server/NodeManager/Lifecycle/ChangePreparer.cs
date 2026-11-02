/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

            IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
            if (isRegistered(manager) || s_candidates.TryGetValue(identity, out _))
            {
                await server.DisposeAsync().ConfigureAwait(false);
                throw new NodeManagerAlreadyRegisteredException();
            }
            if (server.HasPendingHistorianState && !server.CanPublishHistorianRegistrations)
            {
                var failure = new NotSupportedException(
                    "The serving historian registry does not support candidate-owned registrations.");
                try
                {
                    await DisposeCreatedManagerAsync(manager).ConfigureAwait(false);
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Candidate historian publication is unsupported and candidate cleanup failed.",
                        failure,
                        cleanupFailure);
                }
                throw failure;
            }
            if (server.HasPendingHistorianState && !NodeManagerReadiness.IsParticipant(manager))
            {
                var failure = ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Historian registration during preparation requires {0} to implement " +
                    "INodeManagerReadinessNotification.OnServerReadyAsync.",
                    manager.GetType().FullName ?? "(unknown)");
                try
                {
                    await DisposeCreatedManagerAsync(manager).ConfigureAwait(false);
                    await server.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "Historian registration requires readiness and candidate cleanup failed.",
                        failure,
                        cleanupFailure);
                }
                throw failure;
            }
            s_candidates.Add(identity, server);
            return manager;
        }

        private static async ValueTask DisposeCreatedManagerAsync(IAsyncNodeManager manager)
        {
            if (manager is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (manager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <summary>
        /// Gets the candidate's explicit staging server, if it has one.
        /// </summary>
        internal static StagingServer? GetServer(IAsyncNodeManager manager)
        {
            IAsyncNodeManager identity = NodeManagerGenerationRegistry.GetIdentity(manager);
            if (s_candidates.TryGetValue(identity, out StagingServer? server))
            {
                return server;
            }
            return identity is AsyncCustomNodeManager asynchronous
                ? asynchronous.Server as StagingServer
                : (identity.SyncNodeManager as CustomNodeManager2)?.Server as StagingServer;
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
        internal static void PublishHistorianRegistrations(IAsyncNodeManager manager)
        {
            GetServer(manager)?.PublishHistorianRegistrations();
        }

        /// <summary>
        /// Withdraws candidate historian registrations when its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retired NodeManager.</param>
        internal static void RetireHistorianRegistrations(IAsyncNodeManager manager)
        {
            GetServer(manager)?.RetireHistorianRegistrations();
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
        /// Revokes a manager's service-level writer as soon as its generation leaves routing.
        /// </summary>
        /// <param name="manager">The retiring NodeManager.</param>
        internal static void RevokeServiceLevelControl(IAsyncNodeManager manager)
        {
            GetServer(manager)?.RevokeServiceLevelControl();
        }

        /// <summary>Retains candidate-owned handles without extending manager lifetimes.</summary>
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
            internal bool IsStaging { get; private set; } = true;

            /// <summary>
            /// Gets whether this facade has been detached from its published generation.
            /// </summary>
            internal bool IsDetached { get; private set; }

            /// <summary>
            /// Gets whether preparation recorded historian state that requires a readiness participant.
            /// </summary>
            internal bool HasPendingHistorianState
            {
                get
                {
                    if (m_historianRegistry?.HasRegistrations == true)
                    {
                        return true;
                    }
                    lock (m_historianBuildersLock)
                    {
                        return m_historianBuilders.Count != 0;
                    }
                }
            }

            /// <summary>
            /// Gets whether the serving historian registry supports owner-scoped publication.
            /// </summary>
            internal bool CanPublishHistorianRegistrations =>
                m_historianRegistry?.SupportsOwnedRegistrations == true;

            /// <summary>
            /// Publishes candidate historian bindings and transfers builder tracking to the server.
            /// </summary>
            internal void PublishHistorianRegistrations()
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
            }

            /// <summary>
            /// Withdraws this candidate's bindings from the serving registry.
            /// </summary>
            internal void RetireHistorianRegistrations()
            {
                m_historianRegistry?.Retire();
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
                HistorianBuilder[] builders;
                lock (m_historianBuildersLock)
                {
                    if (m_historianBuildersDisposed)
                    {
                        return;
                    }
                    builders = [.. m_historianBuilders];
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
                    try
                    {
                        await builder.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
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
                            failures ??= [];
                            failures.Add(exception);
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
            /// Captures the contributions whose removal belongs to publication, not address-space deletion.
            /// </summary>
            /// <param name="references">The candidate's external contributions.</param>
            internal void CaptureExternalReferences(Dictionary<NodeId, IList<IReference>> references)
            {
                foreach (KeyValuePair<NodeId, IList<IReference>> source in references)
                {
                    var contributed = new ReferenceDictionary<bool>();
                    foreach (IReference reference in source.Value)
                    {
                        contributed[new NodeStateReference(
                            reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)] = true;
                    }
                    m_externalReferences.Add(source.Key, contributed);
                }
            }

            /// <summary>
            /// Excludes transaction-owned contributions from legacy inverse-reference cleanup.
            /// </summary>
            /// <param name="references">The inverse references discovered during deletion.</param>
            internal void ExcludePreparedReferences(List<LocalReference> references)
            {
                references.RemoveAll(reference =>
                    m_externalReferences.TryGetValue(reference.SourceId, out ReferenceDictionary<bool>? contributed) &&
                    contributed.ContainsKey(new NodeStateReference(
                        reference.ReferenceTypeId, reference.IsInverse, reference.TargetId)));
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
                IsStaging = false;
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
            internal void StageRootNotifierReference(NodeId notifierId)
            {
                m_pendingRootNotifierReferences.Add(notifierId);
            }

            /// <summary>
            /// Removes a root-notifier reference contribution that was withdrawn during preparation.
            /// </summary>
            internal void UnstageRootNotifierReference(NodeId notifierId)
            {
                m_pendingRootNotifierReferences.Remove(notifierId);
            }

            /// <summary>
            /// Merges retained root-notifier contributions into the prepared external-reference set.
            /// </summary>
            internal void AppendRootNotifierReferences(
                IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                if (m_pendingRootNotifierReferences.Count == 0)
                {
                    return;
                }

                if (!externalReferences.TryGetValue(
                    ObjectIds.Server,
                    out IList<IReference>? serverReferences))
                {
                    externalReferences.Add(ObjectIds.Server, serverReferences = []);
                }

                foreach (NodeId notifierId in m_pendingRootNotifierReferences)
                {
                    bool exists = false;
                    foreach (IReference reference in serverReferences)
                    {
                        if (reference.ReferenceTypeId == ReferenceTypeIds.HasNotifier &&
                            !reference.IsInverse &&
                            reference.TargetId == notifierId)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        serverReferences.Add(new ReferenceNode
                        {
                            ReferenceTypeId = ReferenceTypeIds.HasNotifier,
                            IsInverse = false,
                            TargetId = notifierId
                        });
                    }
                }
            }

            /// <summary>
            /// Claims the candidate without publishing its namespace registrations.
            /// </summary>
            internal void Attach(IAsyncNodeManager manager)
            {
                if (!ReferenceEquals(s_candidates.GetValue(manager, _ => this), this))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
            }

            /// <summary>
            /// Preserves this manager's view privately before cleanup.
            /// </summary>
            internal void Detach()
            {
                if (IsDetached)
                {
                    return;
                }
                IsDetached = true;
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
            private readonly HashSet<NodeId> m_pendingRootNotifierReferences = [];

            /// <summary>Frozen reference contributions reconciled by publication before physical deletion.</summary>
            private readonly Dictionary<NodeId, ReferenceDictionary<bool>> m_externalReferences = [];

            private readonly Lock m_serviceLevelControlLock = new();
            private Action<byte>? m_serviceLevelControlWriter;
            private bool m_serviceLevelControlRevoked;
            private bool m_historianBuildersDisposed;
        }
    }
}
