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
using Opc.Ua.Server;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Counter variables advanced by their read callback, so only sampling drives them.
    /// Mirrors the subscription capacity workload of the o6-automation opcua-benchmarks
    /// (UInt64 counter per monitored item, sampling groups, read-callback increments).
    /// </summary>
    internal sealed class CounterNodeManager : CustomNodeManager2
    {
        public const string Namespace = "urn:opcfoundation.org:perf:counters";

        public CounterNodeManager(IServerInternal server, ApplicationConfiguration configuration, int count)
            : base(server, configuration, useSamplingGroups: true, Namespace)
        {
            m_values = new ulong[count];
            m_marked = new ulong[count];
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }
            for (int i = 0; i < m_values.Length; i++)
            {
                int index = i;
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId((uint)i + 1, NamespaceIndex),
                    BrowseName = new QualifiedName("Counter" + i.ToString(CultureInfo.InvariantCulture), NamespaceIndex),
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
                    value = new Variant(Interlocked.Increment(ref m_values[index]));
                    status = StatusCodes.Good;
                    timestamp = DateTimeUtc.Now;
                    return ServiceResult.Good;
                };
                node.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, node.NodeId));
                AddPredefinedNode(SystemContext, node);
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

        private readonly ulong[] m_values;
        private readonly ulong[] m_marked;
        private long m_markTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// A server exposing only the counter node manager in addition to the standard nodes.
    /// </summary>
    internal sealed class CounterServer : StandardServer
    {
        public CounterServer(ITelemetryContext telemetry, int count)
            : base(telemetry)
        {
            m_count = count;
        }

        public CounterNodeManager? Counters { get; private set; }

        protected override IMasterNodeManager CreateMasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            Counters = new CounterNodeManager(server, configuration, m_count);
            return new MasterNodeManager(server, configuration, null, Counters);
        }

        private readonly int m_count;
    }
}
