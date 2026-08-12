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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AiLearningJobClient
    {
        public AiLearningJobClient(AiClient client, NodeId jobNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), jobNodeId)
        {
        }

        internal AiLearningJobClient(AiClientOperations operations, NodeId jobNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (jobNodeId.IsNull)
            {
                throw new ArgumentException("Learning job NodeId must not be null.", nameof(jobNodeId));
            }
            JobNodeId = jobNodeId;
            m_proxy = new LearningJobTypeClient(m_operations.Session, jobNodeId, m_operations.Telemetry);
        }

        public NodeId JobNodeId { get; }

        public async ValueTask<AiLearningJobSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.JobId,
                BrowseNames.State,
                BrowseNames.Progress,
                BrowseNames.CandidateModel,
                BrowseNames.TargetDeployment
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                JobNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AiLearningJobSnapshot
            {
                NodeId = JobNodeId,
                JobId = ReadString(nodes, values, ref cursor, 0),
                State = ReadEnum<LearningJobStateEnum>(nodes, values, ref cursor, 1),
                Progress = ReadDouble(nodes, values, ref cursor, 2),
                CandidateModelId = ReadNodeId(nodes, values, ref cursor, 3),
                TargetDeploymentId = ReadNodeId(nodes, values, ref cursor, 4)
            };
        }

        public ValueTask StartCollectionAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.StartCollectionAsync(cancellationToken);
        }

        public ValueTask StopCollectionAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.StopCollectionAsync(cancellationToken);
        }

        public ValueTask<bool> TriggerTrainingAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.TriggerTrainingAsync(cancellationToken);
        }

        public ValueTask<NodeId> PromoteModelAsync(
            NodeId deployment,
            CancellationToken cancellationToken = default)
        {
            return m_proxy.PromoteModelAsync(deployment, cancellationToken);
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AiClientOperations.ReadString(values[cursor++]);
        }

        private static double ReadDouble(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AiClientOperations.ReadDouble(values[cursor++]);
        }

        private static NodeId ReadNodeId(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            if (nodes[index].IsNull)
            {
                return NodeId.Null;
            }
            return AiClientOperations.TryReadNodeId(values[cursor++], out NodeId nodeId)
                ? nodeId
                : NodeId.Null;
        }

        private static TEnum ReadEnum<TEnum>(
            ArrayOf<NodeId> nodes,
            ArrayOf<DataValue> values,
            ref int cursor,
            int index)
            where TEnum : struct, Enum
        {
            if (nodes[index].IsNull)
            {
                return default;
            }
            return AiClientOperations.TryReadEnum(values[cursor++], out TEnum result)
                ? result
                : default;
        }

        private readonly AiClientOperations m_operations;
        private readonly LearningJobTypeClient m_proxy;
    }
}

