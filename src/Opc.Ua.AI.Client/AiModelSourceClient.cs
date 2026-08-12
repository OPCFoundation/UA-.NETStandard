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
    public sealed class AiModelSourceClient
    {
        public AiModelSourceClient(AiClient client, NodeId sourceNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), sourceNodeId)
        {
        }

        internal AiModelSourceClient(AiClientOperations operations, NodeId sourceNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (sourceNodeId.IsNull)
            {
                throw new ArgumentException("Source NodeId must not be null.", nameof(sourceNodeId));
            }
            SourceNodeId = sourceNodeId;
            m_proxy = new ModelSourceTypeClient(
                m_operations.Session, sourceNodeId, m_operations.Telemetry);
        }

        public NodeId SourceNodeId { get; }

        public async ValueTask<AiModelSourceSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.SourceId,
                BrowseNames.EndpointUri,
                BrowseNames.ApiDialect,
                BrowseNames.AuthenticationKind,
                BrowseNames.CredentialReference
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                SourceNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AiModelSourceSnapshot
            {
                NodeId = SourceNodeId,
                SourceId = ReadString(nodes, values, ref cursor, 0),
                EndpointUri = ReadString(nodes, values, ref cursor, 1),
                ApiDialect = ReadEnum<ApiDialectEnum>(nodes, values, ref cursor, 2),
                AuthenticationKind = ReadEnum<AuthenticationKindEnum>(nodes, values, ref cursor, 3),
                CredentialReference = ReadString(nodes, values, ref cursor, 4)
            };
        }

        public async ValueTask<AiSourceConnectionResult> TestConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            (bool reachable, LocalizedText detail) = await m_proxy.TestConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            return new AiSourceConnectionResult
            {
                Reachable = reachable,
                Detail = detail
            };
        }

        public async ValueTask<AiSourceModelListResult> ListModelsAsync(
            string filter = "",
            uint maxResults = 100,
            ByteString continuationPoint = default,
            CancellationToken cancellationToken = default)
        {
            (ArrayOf<ModelReferenceDataType> models, ByteString continuationPointOut) = await m_proxy.ListModelsAsync(
                filter ?? string.Empty,
                maxResults,
                continuationPoint,
                cancellationToken).ConfigureAwait(false);
            return new AiSourceModelListResult
            {
                Models = models,
                ContinuationPoint = continuationPointOut
            };
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AiClientOperations.ReadString(values[cursor++]);
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
        private readonly ModelSourceTypeClient m_proxy;
    }
}

