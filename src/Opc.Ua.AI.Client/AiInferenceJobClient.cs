/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
