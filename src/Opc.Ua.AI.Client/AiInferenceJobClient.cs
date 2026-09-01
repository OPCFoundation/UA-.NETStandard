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
    public sealed class AIInferenceJobClient
    {
        public AIInferenceJobClient(AIClient client, NodeId jobNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), jobNodeId)
        {
        }

        internal AIInferenceJobClient(AIClientOperations operations, NodeId jobNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (jobNodeId.IsNull)
            {
                throw new ArgumentException("Job NodeId must not be null.", nameof(jobNodeId));
            }
            JobNodeId = jobNodeId;
        }

        public NodeId JobNodeId { get; }

        public async ValueTask<AIInferenceJobSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.JobId,
                BrowseNames.Deployment,
                BrowseNames.ResponsePayload,
                BrowseNames.ResponseContentType,
                BrowseNames.ModelUsed,
                BrowseNames.FinishReason
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                JobNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AIInferenceJobSnapshot
            {
                NodeId = JobNodeId,
                JobId = ReadString(nodes, values, ref cursor, 0),
                DeploymentId = ReadNodeId(nodes, values, ref cursor, 1),
                ResponsePayload = ReadByteString(nodes, values, ref cursor, 2),
                ResponseContentType = ReadString(nodes, values, ref cursor, 3),
                ModelUsed = ReadNodeId(nodes, values, ref cursor, 4),
                FinishReason = ReadEnum<FinishReasonEnum>(nodes, values, ref cursor, 5)
            };
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
        }

        private static ByteString ReadByteString(
            ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? ByteString.Empty : AIClientOperations.ReadByteString(values[cursor++]);
        }

        private static NodeId ReadNodeId(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            if (nodes[index].IsNull)
            {
                return NodeId.Null;
            }
            return AIClientOperations.TryReadNodeId(values[cursor++], out NodeId nodeId)
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
            return AIClientOperations.TryReadEnum(values[cursor++], out TEnum result)
                ? result
                : default;
        }

        private readonly AIClientOperations m_operations;
    }
}
