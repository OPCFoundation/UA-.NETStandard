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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// The counters of the subscription capacity workload of the o6-automation opcua-benchmarks
    /// (UInt64 counter per monitored item, advanced by its read callback, so only sampling drives
    /// them), shared by the node manager variants.
    /// </summary>
    internal sealed class CounterValues
    {
        public CounterValues(int count, bool lockedCallback)
        {
            m_lockedCallback = lockedCallback;
            m_values = new ulong[count];
            m_marked = new ulong[count];
        }

        /// <summary>
        /// Creates the counter variables ns=namespaceIndex;i=1..count below the Objects folder.
        /// </summary>
        public IEnumerable<BaseDataVariableState> CreateNodes(ushort namespaceIndex)
        {
            for (int i = 0; i < m_values.Length; i++)
            {
                int index = i;
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId((uint)i + 1, namespaceIndex),
                    BrowseName = new QualifiedName("Counter" + i.ToString(CultureInfo.InvariantCulture), namespaceIndex),
                    DisplayName = new LocalizedText("Counter" + i.ToString(CultureInfo.InvariantCulture)),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.Scalar,
                    MinimumSamplingInterval = 1,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new Variant((ulong)0),
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTimeUtc.Now
                };
                node.OnReadValue = (ISystemContext context, NodeState state, NumericRange range,
                    QualifiedName encoding, ref Variant value, ref StatusCode status, ref DateTimeUtc timestamp) =>
                {
                    value = new Variant(Increment(index));
                    status = StatusCodes.Good;
                    timestamp = DateTimeUtc.Now;
                    return ServiceResult.Good;
                };
                node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                yield return node;
            }
        }

        /// <summary>
        /// Starts a measurement window.
        /// </summary>
        public void Mark()
        {
            for (int i = 0; i < m_values.Length; i++)
            {
                m_marked[i] = Interlocked.Read(ref m_values[i]);
            }
            m_markTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Reports the sampling progress of the window: completed read callbacks per item
        /// against the number the sampling interval calls for.
        /// </summary>
        public string Report(double samplingIntervalMs)
        {
            double elapsedMs = Stopwatch.GetElapsedTime(m_markTimestamp).TotalMilliseconds;
            double expected = elapsedMs / samplingIntervalMs;
            double min = double.MaxValue;
            double sum = 0;
            int belowTarget = 0;
            for (int i = 0; i < m_values.Length; i++)
            {
                double progress = (Interlocked.Read(ref m_values[i]) - m_marked[i]) / expected;
                min = Math.Min(min, progress);
                sum += progress;
                if (progress < 0.99)
                {
                    belowTarget++;
                }
            }
            return FormattableString.Invariant(
                $"counters={m_values.Length} samplesExpected={expected:F0} progressMin={min:P2} ") +
                FormattableString.Invariant(
                $"progressMean={sum / m_values.Length:P2} itemsBelow99={belowTarget}");
        }

        private ulong Increment(int index)
        {
            if (m_lockedCallback)
            {
                // the o6-automation .NET worker increments under a lock it owns
                lock (m_lock)
                {
                    return ++m_values[index];
                }
            }
            return Interlocked.Increment(ref m_values[index]);
        }

        private readonly bool m_lockedCallback;
        private readonly Lock m_lock = new();
        private readonly ulong[] m_values;
        private readonly ulong[] m_marked;
        private long m_markTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// The counters on <see cref="CustomNodeManager2"/> with sampling groups.
    /// </summary>
    internal sealed class CounterNodeManager : CustomNodeManager2
    {
        public const string Namespace = "urn:opcfoundation.org:perf:counters";

        public CounterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CounterValues counters,
            string namespaceUri)
            : base(server, configuration, useSamplingGroups: true, namespaceUri)
        {
            m_counters = counters;
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            foreach (BaseDataVariableState node in m_counters.CreateNodes(NamespaceIndex))
            {
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                AddPredefinedNode(SystemContext, node);
            }
        }

        private readonly CounterValues m_counters;
    }

    /// <summary>
    /// The counters on <see cref="AsyncCustomNodeManager"/> with sampling groups, as the
    /// o6-automation .NET worker for UA-.NETStandard 2.0 builds them.
    /// </summary>
    internal sealed class AsyncCounterNodeManager : AsyncCustomNodeManager
    {
        public AsyncCounterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CounterValues counters,
            string namespaceUri)
            : base(server, configuration, useSamplingGroups: true, namespaceUri)
        {
            m_counters = counters;
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            foreach (BaseDataVariableState node in m_counters.CreateNodes(NamespaceIndex))
            {
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                await AddPredefinedNodeAsync(SystemContext, node, cancellationToken).ConfigureAwait(false);
            }
        }

        private readonly CounterValues m_counters;
    }

    /// <summary>
    /// A server exposing only the counter node manager in addition to the standard nodes.
    /// </summary>
    internal sealed class CounterServer : StandardServer
    {
        public CounterServer(
            ITelemetryContext telemetry,
            int count,
            string namespaceUri = CounterNodeManager.Namespace,
            bool lockedCallback = false,
            bool asyncNodeManager = false)
            : base(telemetry)
        {
            Counters = new CounterValues(count, lockedCallback);
            m_namespaceUri = namespaceUri;
            m_asyncNodeManager = asyncNodeManager;
        }

        public CounterValues Counters { get; }

        protected override IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_asyncNodeManager
                ? new MasterNodeManager(
                    server, configuration, null, new AsyncCounterNodeManager(server, configuration, Counters, m_namespaceUri))
                : new MasterNodeManager(
                    server, configuration, null, new CounterNodeManager(server, configuration, Counters, m_namespaceUri));
        }

        private readonly string m_namespaceUri;
        private readonly bool m_asyncNodeManager;
    }
}
