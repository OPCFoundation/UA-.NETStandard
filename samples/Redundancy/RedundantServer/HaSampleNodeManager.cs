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
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;

namespace RedundantServer
{
    /// <summary>
    /// Factory that produces <see cref="HaSampleNodeManager"/> instances for DI-hosted servers.
    /// </summary>
    public sealed class HaSampleNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Shared namespace reserved at the same index before every replica's node managers are created.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/Samples/HighAvailability";
        private readonly ILeaderElection m_leaderElection;
        private readonly HaSampleReplicaInfo m_replicaInfo;
        private readonly IDistributedValueCache? m_valueCache;
        private readonly SharedKeyValueHistorianProvider? m_historian;

        /// <summary>
        /// Creates a factory using the distributed leader-election service registered by the host.
        /// </summary>
        /// <param name="leaderElection">The leader-election service that identifies the active writer replica.</param>
        /// <param name="replicaInfo">The local replica identity published by sample variables.</param>
        /// <param name="valueCaches">
        /// The distributed value cache registered by <c>UseDistributedAddressSpace</c>, when present. Injected as
        /// a sequence so active/active and single-instance topologies (which register no cache) resolve to empty.
        /// </param>
        public HaSampleNodeManagerFactory(
            ILeaderElection leaderElection,
            HaSampleReplicaInfo replicaInfo,
            IEnumerable<IDistributedValueCache> valueCaches,
            IEnumerable<SharedKeyValueHistorianProvider> historians)
        {
            m_leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
            m_replicaInfo = replicaInfo ?? throw new ArgumentNullException(nameof(replicaInfo));
            m_valueCache = valueCaches?.FirstOrDefault();
            m_historian = historians?.FirstOrDefault();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [NamespaceUri];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            _ = configuration;
            _ = cancellationToken;

#pragma warning disable CA2000 // ownership transfers to the server
            var manager = new HaSampleNodeManager(
                server,
                m_leaderElection,
                m_replicaInfo,
                m_valueCache,
                m_historian,
                [.. NamespacesUris]);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }

    /// <summary>
    /// Carries the local replica identity into sample node managers created by dependency injection.
    /// </summary>
    public sealed class HaSampleReplicaInfo
    {
        /// <summary>
        /// Creates a replica identity descriptor.
        /// </summary>
        /// <param name="nodeId">The unique local high-availability node id.</param>
        public HaSampleReplicaInfo(string nodeId)
        {
            NodeId = nodeId;
        }

        /// <summary>
        /// Gets the unique local high-availability node id.
        /// </summary>
        public string NodeId { get; }
    }

    /// <summary>
    /// Starts the sample producer after distributed services and redundancy metadata are initialized.
    /// </summary>
    internal sealed class HaSampleSimulationStartupTask : IServerStartupTask
    {
        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (HaSampleNodeManager manager in server.FindNodeManagers<HaSampleNodeManager>())
            {
                manager.StartSimulation();
            }
            return default;
        }
    }

    /// <summary>
    /// Minimal <see cref="FluentNodeManagerBase"/> address space that participates in distributed replication.
    /// </summary>
    public sealed class HaSampleNodeManager : FluentNodeManagerBase
    {
        private readonly ILeaderElection m_leaderElection;
        private readonly HaSampleReplicaInfo m_replicaInfo;
        private readonly IDistributedValueCache? m_valueCache;
        private readonly SharedKeyValueHistorianProvider? m_historian;
        private readonly CancellationTokenSource m_simulationCts = new();
        private readonly Lock m_updateLock = new();
        private static readonly TimeSpan s_valueFreshness = TimeSpan.FromSeconds(10);
        private const string kSampleFolderName = "HighAvailability";
        private FolderState? m_folder;
        private BaseVariableState? m_counter;
        private BaseVariableState? m_activeReplica;
        private BaseObjectState? m_historyEvents;
        private Task? m_simulationTask;
        private int m_counterValue;

        /// <summary>
        /// Creates the high-availability sample node manager.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="leaderElection">The leader-election service that gates sample writes.</param>
        /// <param name="replicaInfo">The local replica identity published by sample variables.</param>
        /// <param name="valueCache">
        /// The distributed value cache used to share the Counter value across the replica set, or <c>null</c> when
        /// the topology registers none (active/active and single-instance).
        /// </param>
        /// <param name="historian">
        /// The shared historian used by strong active/passive deployments, or <c>null</c> for other topologies.
        /// </param>
        /// <param name="namespaceUris">The namespace URIs exposed by this node manager.</param>
        public HaSampleNodeManager(
            IServerInternal server,
            ILeaderElection leaderElection,
            HaSampleReplicaInfo replicaInfo,
            IDistributedValueCache? valueCache,
            SharedKeyValueHistorianProvider? historian,
            params string[] namespaceUris)
            : base(server, namespaceUris)
        {
            m_leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
            m_replicaInfo = replicaInfo ?? throw new ArgumentNullException(nameof(replicaInfo));
            m_valueCache = valueCache;
            m_historian = historian;
        }

        /// <summary>
        /// Mints the browse name as the identifier, for the handful of nodes
        /// this sample publishes by name.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Those identifiers are part of the sample's contract - the redundant
        /// client addresses <c>ns=N;s=Counter</c> directly - so they cannot be
        /// left to the derived form the default factory mints.
        /// </para>
        /// <para>
        /// Every other node keeps that derived form, and the narrowness
        /// matters: a browse name is only unique among its siblings, and the
        /// historian hangs an <c>HA Configuration</c> object off each node it
        /// historizes, so a blanket rule would hand the Counter's and the
        /// HistoryEvents' one the same identifier. An identifier a caller
        /// already chose here is kept, on the default factory's own terms.
        /// </para>
        /// </remarks>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node == null)
            {
                return base.New(context, node!);
            }

            ushort namespaceIndex = NamespaceIndexes[0];
            if (!node.NodeId.IsNull &&
                (node.NodeId.NamespaceIndex == namespaceIndex ||
                    node is not BaseInstanceState { Parent: not null }))
            {
                return node.NodeId;
            }

            return IsPublishedByName(node)
                ? new NodeId(node.BrowseName.Name!, namespaceIndex)
                : base.New(context, node);
        }

        /// <summary>
        /// Whether <paramref name="node"/> is the sample folder or one of the
        /// nodes directly beneath it - the ones a client spells out.
        /// </summary>
        /// <remarks>
        /// The folder reaches this before it has been staged, so it has no
        /// parent yet and is recognised by its browse name instead.
        /// </remarks>
        private bool IsPublishedByName(NodeState node)
        {
            if (string.IsNullOrEmpty(node.BrowseName.Name))
            {
                return false;
            }

            NodeState? parent = (node as BaseInstanceState)?.Parent;
            return parent == null
                ? node.BrowseName.Name == kSampleFolderName
                : ReferenceEquals(parent, m_folder);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndexes[0]);

            // Unparented, so the staged folder organises itself under the
            // Objects folder; the manager used to add that reference and its
            // externalReferences counterpart by hand.
            INodeBuilder<FolderState> folder = builder.AddFolder(kSampleFolderName);
            folder.Node.DisplayName = new LocalizedText("en", "High Availability");
            NodeId folderId = folder.Node.NodeId;

            // New() names the folder's direct children after their browse
            // name, so it has to know which node the folder is.
            m_folder = folder.Node;

            m_counter = AddSampleVariable<int>(builder, folderId, "Counter", Variant.From(0));
            if (m_valueCache != null)
            {
                // Opt the Counter's read/write callbacks into the distributed
                // value cache (extension beyond OPC 10000-4 §6.6): the active
                // replica caches each new value write-through, and every replica
                // serves the last value shared through the store while it is
                // fresh (falling back to the local value). This is how the
                // Counter is shared across the set and continues after failover;
                // monitored items keep reading through the normal pipeline.
                m_counter.EnableDistributedValueParticipation(
                    m_valueCache,
                    s_valueFreshness,
                    _ => new ValueTask<DataValue>(ReadLocalCounter()));
            }

            m_activeReplica = AddSampleVariable<string>(
                builder,
                folderId,
                "ActiveReplica",
                Variant.From("unknown"));

            m_historyEvents = builder.AddObject("HistoryEvents", folderId).Node;
            m_historyEvents.ReferenceTypeId = ReferenceTypeIds.Organizes;
            m_historyEvents.DisplayName = new LocalizedText("en", "History Events");
            m_historyEvents.EventNotifier = EventNotifiers.SubscribeToEvents;

            // Deliberately not staged: this subtree exists to show what a
            // factory-assigned identity looks like next to the named ones
            // above, so it keeps minting through the NodeIdFactory itself
            // rather than through New. Attaching it once the folder has been
            // staged leaves those identifiers untouched, and the subtree is
            // still registered as part of the folder's.
            AddFactoryAssignedNodes(folder.Node, NamespaceIndexes[0]);

            if (m_historian != null)
            {
#pragma warning disable CA2000 // ownership transfers to the server's historian-builder registry
                HistorianBuilder historian = new HistorianBuilder(Server)
                    .UseProvider(m_historian);
#pragma warning restore CA2000
                await historian.HistorizeAsync(
                    m_counter,
                    SystemContext,
                    capabilities: HistorianNodeCapabilities.DataReadWrite,
                    autoCapture: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await historian.HistorizeEventsAsync(
                    m_historyEvents,
                    SystemContext,
                    capabilities: HistorianNodeCapabilities.EventReadWrite with
                    {
                        EventTypes = [ObjectTypeIds.BaseEventType]
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            // Registering the staged subtree and re-running the reverse
            // reference pass is what publishes the folder's inverse Organizes
            // reference into externalReferences[ObjectsFolder].
            await RegisterAuthoredNodesAsync(builder, cancellationToken).ConfigureAwait(false);
            await CompleteConfigureAsync(externalReferences, cancellationToken)
                .ConfigureAwait(false);
            await SealConfigurationAsync(builder, cancellationToken).ConfigureAwait(false);
        }

        private void AddFactoryAssignedNodes(FolderState parent, ushort namespaceIndex)
        {
            var generated = new BaseObjectState(parent)
            {
                BrowseName = new QualifiedName("FactoryAssigned", namespaceIndex),
                DisplayName = new LocalizedText("Factory-assigned identities"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            generated.NodeId = NodeIdFactory.New(SystemContext, generated);
            parent.AddChild(generated);
            var value = new BaseDataVariableState(generated)
            {
                BrowseName = new QualifiedName("Value", namespaceIndex),
                DisplayName = new LocalizedText("Value"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From(12345),
                StatusCode = StatusCodes.Good
            };
            value.NodeId = NodeIdFactory.New(SystemContext, value);
            generated.AddChild(value);
            var target = new BaseDataVariableState(generated)
            {
                BrowseName = new QualifiedName("Target", namespaceIndex),
                DisplayName = new LocalizedText("Target"),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From(value.NodeId),
                StatusCode = StatusCodes.Good
            };
            target.NodeId = NodeIdFactory.New(SystemContext, target);
            generated.AddChild(target);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_simulationCts.Cancel();
                m_simulationCts.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override IHistorianProvider? GetHistorianProvider(NodeState node)
        {
            return m_historian ?? base.GetHistorianProvider(node);
        }

        /// <summary>
        /// Stages one writable sample variable under the sample folder.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type the variable carries; the staged node takes its DataType
        /// and ValueRank from it.
        /// </typeparam>
        /// <param name="builder">The fluent builder staging the subtree.</param>
        /// <param name="parentId">The folder the variable hangs off.</param>
        /// <param name="name">Browse name of the variable.</param>
        /// <param name="initialValue">The value a client reads before the
        /// simulation has produced one.</param>
        private static BaseVariableState AddSampleVariable<TValue>(
            NodeManagerBuilder builder,
            NodeId parentId,
            string name,
            Variant initialValue)
        {
            BaseVariableState variable = builder
                .AddVariable<TValue>(name, parentId)
                .Writable()
                .Node;

            // The sample organises its variables rather than componentising
            // them, which is the shape the redundant client browses.
            variable.ReferenceTypeId = ReferenceTypeIds.Organizes;
            variable.DisplayName = new LocalizedText("en", name);
            variable.Value = initialValue;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            return variable;
        }

        /// <summary>
        /// Starts the replica's simulation loop if it has not already been started.
        /// </summary>
        internal void StartSimulation()
        {
            m_simulationTask ??= Task.Run(() => RunSimulationAsync(m_simulationCts.Token));
        }

        private async Task RunSimulationAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            bool wasLeader = false;
            DateTime lastHeartbeat = DateTime.MinValue;
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!m_leaderElection.IsLeader)
                {
                    if (wasLeader)
                    {
                        // A failover moved the write role away from this replica.
                        m_logger.ReplicaBecameStandby(m_replicaInfo.NodeId);
                    }
                    wasLeader = false;
                    UpdateActiveReplica("standby");
                    continue;
                }

                if (!wasLeader)
                {
                    // This replica has just become the active writer (initial
                    // election or a failover). Continue the Counter from the
                    // last value shared through the distributed store instead of
                    // restarting from the local count.
                    await SeedCounterFromCacheAsync(cancellationToken).ConfigureAwait(false);
                    if (m_logger.IsEnabled(LogLevel.Information))
                    {
                        m_logger.ReplicaBecameActiveWriter(
                            m_replicaInfo.NodeId,
                            Volatile.Read(ref m_counterValue));
                    }
                    wasLeader = true;
                }

                await UpdateCounterAsync(cancellationToken).ConfigureAwait(false);
                UpdateActiveReplica(m_replicaInfo.NodeId);

                // Periodic heartbeat so the log shows liveness, which replica is
                // producing which Counter values, and (across replicas) the
                // per-replica divergence a client observes on failover.
                DateTime now = DateTime.UtcNow;
                if (now - lastHeartbeat >= TimeSpan.FromSeconds(5))
                {
                    lastHeartbeat = now;
                    if (m_logger.IsEnabled(LogLevel.Information))
                    {
                        m_logger.ReplicaActive(
                            m_replicaInfo.NodeId,
                            Volatile.Read(ref m_counterValue));
                    }
                }
            }
        }

        private async Task UpdateCounterAsync(CancellationToken cancellationToken)
        {
            BaseVariableState? counter = m_counter;
            if (counter == null)
            {
                return;
            }

            int value = Interlocked.Increment(ref m_counterValue);
            DateTimeUtc timestamp = DateTimeUtc.Now;
            var sample = new DataValue(
                Variant.From(value),
                StatusCodes.Good,
                timestamp,
                timestamp);
            lock (m_updateLock)
            {
                counter.Value = value;
                counter.Timestamp = timestamp.ToDateTime();
                counter.ClearChangeMasks(SystemContext, false);
            }

            // Share the new value through the distributed store so standby
            // replicas can serve it and a promoted replica continues from it.
            if (m_valueCache != null)
            {
                try
                {
                    await m_valueCache
                        .CacheAsync(
                            counter.NodeId,
                            sample,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // The distributed store is not ready yet (the server is
                    // still starting); the value is shared on the next tick.
                }
            }

            bool historyReady = m_historian == null ||
                await ArchiveCounterAsync(
                    counter,
                    sample,
                    cancellationToken).ConfigureAwait(false);

            BaseObjectState? historyEvents = m_historyEvents;
            if (historyEvents != null && historyReady)
            {
                var e = new BaseEventState(historyEvents);
                e.Initialize(
                    SystemContext,
                    historyEvents,
                    EventSeverity.Low,
                    new LocalizedText(
                        $"Replica '{m_replicaInfo.NodeId}' archived counter {value}."));
                try
                {
                    await historyEvents.ReportEventAsync(
                        SystemContext,
                        e,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    m_logger.EventHistoryWriteFailed(
                        exception,
                        m_replicaInfo.NodeId);
                }
            }
        }

        private async Task<bool> ArchiveCounterAsync(
            BaseVariableState counter,
            DataValue sample,
            CancellationToken cancellationToken)
        {
            SharedKeyValueHistorianProvider? historian = m_historian;
            if (historian == null || !m_leaderElection.IsLeader)
            {
                return false;
            }

            try
            {
                using var operationContext = new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryUpdate,
                    RequestLifetime.None);
                var historianContext = new HistorianOperationContext(
                    SystemContext,
                    operationContext,
                    counter,
                    HistoryUpdateType.Insert);
                HistorianUpdateOutcome<DataValue> outcome = await historian.InsertAsync(
                    historianContext,
                    counter.NodeId,
                    [sample],
                    cancellationToken).ConfigureAwait(false);
                StatusCode status = outcome.OperationResults.Count == 1
                    ? outcome.OperationResults[0]
                    : StatusCodes.BadUnexpectedError;
                if (StatusCode.IsBad(status))
                {
                    m_logger.CounterHistoryWriteRejected(
                        m_replicaInfo.NodeId,
                        status);
                    return false;
                }
                return true;
            }
            catch (Exception exception) when (
                exception is ServiceResultException or
                TimeoutException or
                InvalidOperationException)
            {
                m_logger.CounterHistoryWriteFailed(
                    exception,
                    m_replicaInfo.NodeId);
                return false;
            }
        }

        private async Task SeedCounterFromCacheAsync(CancellationToken cancellationToken)
        {
            BaseVariableState? counter = m_counter;
            if (m_valueCache == null || counter == null)
            {
                return;
            }

            try
            {
                (bool _, DataValue cached) = await m_valueCache
                    .TryGetAsync(counter.NodeId, s_valueFreshness, cancellationToken)
                    .ConfigureAwait(false);
                if (cached.WrappedValue.TryGetValue(out int last) && last > Volatile.Read(ref m_counterValue))
                {
                    Interlocked.Exchange(ref m_counterValue, last);
                    lock (m_updateLock)
                    {
                        counter.Value = last;
                        counter.Timestamp = DateTime.UtcNow;
                        counter.ClearChangeMasks(SystemContext, false);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // The distributed store is not ready yet; start from the local
                // value and share on the next tick.
            }
        }

        private DataValue ReadLocalCounter()
        {
            return new DataValue(
                Variant.From(Volatile.Read(ref m_counterValue)),
                StatusCodes.Good,
                DateTimeUtc.Now);
        }

        private void UpdateActiveReplica(string value)
        {
            BaseVariableState? activeReplica = m_activeReplica;
            if (activeReplica == null)
            {
                return;
            }

            lock (m_updateLock)
            {
                activeReplica.Value = value;
                activeReplica.Timestamp = DateTime.UtcNow;
                activeReplica.ClearChangeMasks(SystemContext, false);
            }
        }
    }

    /// <summary>
    /// Defines log messages for replica write roles, counter activity, and history capture.
    /// </summary>
    internal static partial class HaSampleNodeManagerLog
    {
        /// <summary>
        /// Logs that a replica has relinquished the active writer role.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 0, Level = LogLevel.Information,
            Message = "HA: replica {ReplicaId} became STANDBY (no longer the active writer).")]
        public static partial void ReplicaBecameStandby(this ILogger logger, string replicaId);

        /// <summary>
        /// Logs that a replica has become the active writer and reports its counter value.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 1, Level = LogLevel.Information,
            Message = "HA: replica {ReplicaId} became ACTIVE writer (Counter={Counter}).")]
        public static partial void ReplicaBecameActiveWriter(this ILogger logger, string replicaId, int counter);

        /// <summary>
        /// Logs the active replica's heartbeat and current counter value.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 2, Level = LogLevel.Information,
            Message = "HA: replica {ReplicaId} ACTIVE, Counter={Counter}.")]
        public static partial void ReplicaActive(this ILogger logger, string replicaId, int counter);

        /// <summary>
        /// Logs a rejected counter history write and its status code.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 3, Level = LogLevel.Warning,
            Message = "HA: replica {ReplicaId} could not archive the Counter sample ({StatusCode}).")]
        public static partial void CounterHistoryWriteRejected(
            this ILogger logger,
            string replicaId,
            StatusCode statusCode);

        /// <summary>
        /// Logs an exception while archiving a counter sample.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 4, Level = LogLevel.Warning,
            Message = "HA: replica {ReplicaId} failed to archive the Counter sample.")]
        public static partial void CounterHistoryWriteFailed(
            this ILogger logger,
            Exception exception,
            string replicaId);

        /// <summary>
        /// Logs an exception while archiving a history event.
        /// </summary>
        [LoggerMessage(EventId = RedundantServerEventIds.HaSampleNodeManager + 5, Level = LogLevel.Warning,
            Message = "HA: replica {ReplicaId} failed to archive the history event.")]
        public static partial void EventHistoryWriteFailed(
            this ILogger logger,
            Exception exception,
            string replicaId);
    }
}
