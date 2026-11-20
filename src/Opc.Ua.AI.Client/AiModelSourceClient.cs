/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AIModelSourceClient
    {
        public AIModelSourceClient(AIClient client, NodeId sourceNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), sourceNodeId)
        {
        }

        internal AIModelSourceClient(AIClientOperations operations, NodeId sourceNodeId)
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

        public async ValueTask<AIModelSourceSnapshot> ReadAsync(
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
            return new AIModelSourceSnapshot
            {
                NodeId = SourceNodeId,
                SourceId = ReadString(nodes, values, ref cursor, 0),
                EndpointUri = ReadString(nodes, values, ref cursor, 1),
                ApiDialect = ReadEnum<ApiDialectEnum>(nodes, values, ref cursor, 2),
                AuthenticationKind = ReadEnum<AuthenticationKindEnum>(nodes, values, ref cursor, 3),
                CredentialReference = ReadString(nodes, values, ref cursor, 4)
            };
        }

        public async ValueTask<AISourceConnectionResult> TestConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            (bool reachable, LocalizedText detail) = await m_proxy.TestConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            return new AISourceConnectionResult
            {
                Reachable = reachable,
                Detail = detail
            };
        }

        public async ValueTask<AISourceModelListResult> ListModelsAsync(
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
            return new AISourceModelListResult
            {
                Models = models,
                ContinuationPoint = continuationPointOut
            };
        }

        private static string? ReadString(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? null : AIClientOperations.ReadString(values[cursor++]);
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
        private readonly ModelSourceTypeClient m_proxy;
    }
}
