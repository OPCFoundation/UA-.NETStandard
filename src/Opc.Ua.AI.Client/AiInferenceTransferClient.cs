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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AIInferenceTransferClient
    {
        public AIInferenceTransferClient(AIClient client, NodeId transferNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), transferNodeId)
        {
        }

        internal AIInferenceTransferClient(AIClientOperations operations, NodeId transferNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (transferNodeId.IsNull)
            {
                throw new ArgumentException("Transfer NodeId must not be null.", nameof(transferNodeId));
            }
            TransferNodeId = transferNodeId;
            m_proxy = new InferenceTransferTypeClient(
                m_operations.Session, transferNodeId, m_operations.Telemetry);
        }

        public NodeId TransferNodeId { get; }

        public async ValueTask<AITransferSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.TransferId,
                BrowseNames.State,
                BrowseNames.BytesTransferred,
                BrowseNames.ModelUsed,
                BrowseNames.ResponseContentType
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                TransferNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AITransferSnapshot
            {
                NodeId = TransferNodeId,
                TransferId = ReadString(nodes, values, ref cursor, 0),
                State = ReadEnum<TransferStateEnum>(nodes, values, ref cursor, 1),
                BytesTransferred = ReadUInt64(nodes, values, ref cursor, 2),
                ModelUsed = ReadNodeId(nodes, values, ref cursor, 3),
                ResponseContentType = ReadString(nodes, values, ref cursor, 4)
            };
        }

        public async ValueTask WriteRequestAsync(
            ByteString content,
            int chunkSize = AIClientOperations.DefaultChunkSize,
            CancellationToken cancellationToken = default)
        {
            FileTypeClient file = await OpenRequestFileAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await m_operations.WriteFileAsync(file, content, chunkSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown ||
                ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                await m_operations.WriteFileAsync(file.ObjectId, content, chunkSize, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public async ValueTask WriteRequestAsync(
            Stream content,
            int chunkSize = AIClientOperations.DefaultChunkSize,
            CancellationToken cancellationToken = default)
        {
            FileTypeClient file = await OpenRequestFileAsync(cancellationToken).ConfigureAwait(false);
            await m_operations.WriteFileAsync(file, content, chunkSize, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<ByteString> ReadResponseAsync(
            int chunkSize = AIClientOperations.DefaultChunkSize,
            CancellationToken cancellationToken = default)
        {
            FileTypeClient file = await OpenResponseFileAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await m_operations.ReadFileAsync(file, chunkSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdUnknown ||
                ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                return await m_operations.ReadFileAsync(file.ObjectId, chunkSize, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        public async ValueTask ReadResponseAsync(
            Stream destination,
            int chunkSize = AIClientOperations.DefaultChunkSize,
            CancellationToken cancellationToken = default)
        {
            FileTypeClient file = await OpenResponseFileAsync(cancellationToken).ConfigureAwait(false);
            await m_operations.ReadFileAsync(file, destination, chunkSize, cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await m_proxy.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMethodInvalid)
            {
                ArrayOf<Variant> outputs = await m_operations.CallAsync(
                    TransferNodeId,
                    BrowseNames.Execute,
                    ArrayOf<Variant>.Empty,
                    cancellationToken).ConfigureAwait(false);
                return outputs.Count > 0 && outputs[0].TryGetValue(out bool accepted) && accepted;
            }
        }

        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            return m_proxy.AbortAsync(cancellationToken);
        }

        private async ValueTask<FileTypeClient> OpenRequestFileAsync(CancellationToken cancellationToken)
        {
            NodeId request = await m_operations.ResolveChildAsync(
                TransferNodeId, BrowseNames.Request, cancellationToken).ConfigureAwait(false);
            if (!request.IsNull)
            {
                return new FileTypeClient(m_operations.Session, request, m_operations.Telemetry);
            }
            FileTypeClient? file = await m_proxy.GetRequestAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (file is null || file.ObjectId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return file;
        }

        private async ValueTask<FileTypeClient> OpenResponseFileAsync(CancellationToken cancellationToken)
        {
            NodeId response = await m_operations.ResolveChildAsync(
                TransferNodeId, BrowseNames.Response, cancellationToken).ConfigureAwait(false);
            if (!response.IsNull)
            {
                return new FileTypeClient(m_operations.Session, response, m_operations.Telemetry);
            }
            FileTypeClient? file = await m_proxy.GetResponseAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (file is null || file.ObjectId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            return file;
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
        }

        private static ulong ReadUInt64(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AIClientOperations.ReadUInt64(values[cursor++]);
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
        private readonly InferenceTransferTypeClient m_proxy;
    }
}
