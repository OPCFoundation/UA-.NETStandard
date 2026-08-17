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

