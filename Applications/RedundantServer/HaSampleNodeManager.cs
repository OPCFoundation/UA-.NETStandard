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
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;

namespace RedundantServer
{
    /// <summary>
    /// Factory that produces <see cref="HaSampleNodeManager"/> instances for DI-hosted servers.
    /// </summary>
    public sealed class HaSampleNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private const string NamespaceUri = "http://opcfoundation.org/UA/Samples/HighAvailability";
        private readonly ILeaderElection m_leaderElection;
        private readonly HaSampleReplicaInfo m_replicaInfo;
        private readonly IDistributedValueCache? m_valueCache;

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
            IEnumerable<IDistributedValueCache> valueCaches)
        {
            m_leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
            m_replicaInfo = replicaInfo ?? throw new ArgumentNullException(nameof(replicaInfo));
            m_valueCache = valueCaches?.FirstOrDefault();
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
                server, m_leaderElection, m_replicaInfo, m_valueCache, [.. NamespacesUris]);
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
    /// Minimal <see cref="AsyncCustomNodeManager"/> address space that participates in distributed replication.
    /// </summary>
    public sealed class HaSampleNodeManager : AsyncCustomNodeManager
    {
        private readonly ILeaderElection m_leaderElection;
        private readonly HaSampleReplicaInfo m_replicaInfo;
        private readonly IDistributedValueCache? m_valueCache;
        private readonly CancellationTokenSource m_simulationCts = new();
        private readonly Lock m_updateLock = new();
        private static readonly TimeSpan s_valueFreshness = TimeSpan.FromSeconds(10);
        private BaseDataVariableState? m_counter;
        private BaseDataVariableState? m_activeReplica;
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
        /// <param name="namespaceUris">The namespace URIs exposed by this node manager.</param>
        public HaSampleNodeManager(
            IServerInternal server,
            ILeaderElection leaderElection,
            HaSampleReplicaInfo replicaInfo,
            IDistributedValueCache? valueCache,
            params string[] namespaceUris)
            : base(server, namespaceUris)
        {
            m_leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
            m_replicaInfo = replicaInfo ?? throw new ArgumentNullException(nameof(replicaInfo));
            m_valueCache = valueCache;
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = [];
            }

            ushort namespaceIndex = NamespaceIndexes[0];
            FolderState folder = CreateFolder(null, namespaceIndex, "HighAvailability", "High Availability");
            folder.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, folder.NodeId));

            m_counter = CreateVariable(folder, namespaceIndex, "Counter", DataTypeIds.Int32, Variant.From(0));
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

            m_activeReplica = CreateVariable(
                folder,
                namespaceIndex,
                "ActiveReplica",
                DataTypeIds.String,
                Variant.From("unknown"));

            await AddPredefinedNodeAsync(SystemContext, folder, cancellationToken).ConfigureAwait(false);
            StartSimulation();
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

        private static FolderState CreateFolder(NodeState? parent, ushort namespaceIndex, string path, string name)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(path, namespaceIndex),
                BrowseName = new QualifiedName(path, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            parent?.AddChild(folder);
            return folder;
        }

        private static BaseDataVariableState CreateVariable(
            NodeState parent,
            ushort namespaceIndex,
            string name,
            NodeId dataType,
            Variant initialValue)
        {
            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(name, namespaceIndex),
                BrowseName = new QualifiedName(name, namespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Historizing = false,
                Value = initialValue,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };

            parent.AddChild(variable);
            return variable;
        }

        private void StartSimulation()
        {
            m_simulationTask ??= Task.Run(() => RunSimulationAsync(m_simulationCts.Token));
        }

        private async Task RunSimulationAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            bool wasLeader = false;
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!m_leaderElection.IsLeader)
                {
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
                    wasLeader = true;
                }

                await UpdateCounterAsync(cancellationToken).ConfigureAwait(false);
                UpdateActiveReplica(m_replicaInfo.NodeId);
            }
        }

        private async Task UpdateCounterAsync(CancellationToken cancellationToken)
        {
            BaseDataVariableState? counter = m_counter;
            if (counter == null)
            {
                return;
            }

            int value = Interlocked.Increment(ref m_counterValue);
            lock (m_updateLock)
            {
                counter.Value = value;
                counter.Timestamp = DateTime.UtcNow;
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
                            new DataValue(Variant.From(value), StatusCodes.Good, DateTimeUtc.Now),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // The distributed store is not ready yet (the server is
                    // still starting); the value is shared on the next tick.
                }
            }
        }

        private async Task SeedCounterFromCacheAsync(CancellationToken cancellationToken)
        {
            BaseDataVariableState? counter = m_counter;
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
            BaseDataVariableState? activeReplica = m_activeReplica;
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
}
