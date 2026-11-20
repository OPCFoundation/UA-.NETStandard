/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AIEvaluationRunClient
    {
        public AIEvaluationRunClient(AIClient client, NodeId runNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), runNodeId)
        {
        }

        internal AIEvaluationRunClient(AIClientOperations operations, NodeId runNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (runNodeId.IsNull)
            {
                throw new ArgumentException("Evaluation run NodeId must not be null.", nameof(runNodeId));
            }
            RunNodeId = runNodeId;
        }

        public NodeId RunNodeId { get; }

        public async ValueTask<AIEvaluationRunSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.RunId,
                BrowseNames.EvaluatedModel,
                BrowseNames.Passed,
                BrowseNames.Metrics,
                BrowseNames.ReportUri
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                RunNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AIEvaluationRunSnapshot
            {
                NodeId = RunNodeId,
                RunId = ReadString(nodes, values, ref cursor, 0),
                EvaluatedModelId = ReadNodeId(nodes, values, ref cursor, 1),
                Passed = ReadBoolean(nodes, values, ref cursor, 2),
                Metrics = ReadStructureArray<EvaluationMetricDataType>(
                    nodes, values, m_operations.Session.MessageContext, ref cursor, 3),
                ReportUri = ReadString(nodes, values, ref cursor, 4)
            };
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
        }

        private static bool ReadBoolean(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return !nodes[index].IsNull && AIClientOperations.ReadBoolean(values[cursor++]);
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

        private static ArrayOf<T> ReadStructureArray<T>(
            ArrayOf<NodeId> nodes,
            ArrayOf<DataValue> values,
            IServiceMessageContext messageContext,
            ref int cursor,
            int index)
            where T : class, IEncodeable
        {
            if (nodes[index].IsNull)
            {
                return ArrayOf<T>.Empty;
            }
            return values[cursor++].WrappedValue.TryGetValue(out ArrayOf<T> array, messageContext)
                ? array
                : ArrayOf<T>.Empty;
        }

        private readonly AIClientOperations m_operations;
    }
}
