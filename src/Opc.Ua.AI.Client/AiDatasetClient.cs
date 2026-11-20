/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AIDatasetClient
    {
        public AIDatasetClient(AIClient client, NodeId datasetNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), datasetNodeId)
        {
        }

        internal AIDatasetClient(AIClientOperations operations, NodeId datasetNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (datasetNodeId.IsNull)
            {
                throw new ArgumentException("Dataset NodeId must not be null.", nameof(datasetNodeId));
            }
            DatasetNodeId = datasetNodeId;
        }

        public NodeId DatasetNodeId { get; }

        public async ValueTask<AIDatasetSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.DatasetId,
                BrowseNames.Name,
                BrowseNames.SourceKind,
                BrowseNames.ArtifactUri,
                BrowseNames.ContentType,
                BrowseNames.SizeBytes,
                BrowseNames.SampleCount
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                DatasetNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AIDatasetSnapshot
            {
                NodeId = DatasetNodeId,
                DatasetId = ReadString(nodes, values, ref cursor, 0),
                Name = ReadString(nodes, values, ref cursor, 1),
                SourceKind = ReadEnum<DatasetSourceEnum>(nodes, values, ref cursor, 2),
                ArtifactUri = ReadString(nodes, values, ref cursor, 3),
                ContentType = ReadString(nodes, values, ref cursor, 4),
                SizeBytes = ReadUInt64(nodes, values, ref cursor, 5),
                SampleCount = ReadUInt32(nodes, values, ref cursor, 6)
            };
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
        }

        private static ulong ReadUInt64(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AIClientOperations.ReadUInt64(values[cursor++]);
        }

        private static uint ReadUInt32(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AIClientOperations.ReadUInt32(values[cursor++]);
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
