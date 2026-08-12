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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AI.Client
{
    public sealed class AIModelClient
    {
        public AIModelClient(AIClient client, NodeId modelNodeId)
            : this(client?.Operations ?? throw new ArgumentNullException(nameof(client)), modelNodeId)
        {
        }

        internal AIModelClient(AIClientOperations operations, NodeId modelNodeId)
        {
            m_operations = operations ?? throw new ArgumentNullException(nameof(operations));
            if (modelNodeId.IsNull)
            {
                throw new ArgumentException("Model NodeId must not be null.", nameof(modelNodeId));
            }
            ModelNodeId = modelNodeId;
            m_proxy = new ModelTypeClient(m_operations.Session, modelNodeId, m_operations.Telemetry);
        }

        public NodeId ModelNodeId { get; }

        public async ValueTask<AIModelSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            string[] members =
            [
                BrowseNames.ModelId,
                BrowseNames.Name,
                BrowseNames.Version,
                BrowseNames.Framework,
                BrowseNames.Format,
                BrowseNames.License,
                BrowseNames.Digest,
                BrowseNames.DigestAlgorithm,
                BrowseNames.CreatedAt,
                BrowseNames.LastModifiedAt,
                BrowseNames.Publisher
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                ModelNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadPresentValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            string? modelId = ReadString(nodes, values, ref cursor, 0);
            string? name = ReadString(nodes, values, ref cursor, 1);
            string? version = ReadString(nodes, values, ref cursor, 2);
            string? framework = ReadString(nodes, values, ref cursor, 3);
            string? format = ReadString(nodes, values, ref cursor, 4);
            string? license = ReadString(nodes, values, ref cursor, 5);
            ByteString digest = ReadByteString(nodes, values, ref cursor, 6);
            string? digestAlgorithm = ReadString(nodes, values, ref cursor, 7);
            DateTimeUtc createdAt = ReadDateTime(nodes, values, ref cursor, 8);
            DateTimeUtc lastModifiedAt = ReadDateTime(nodes, values, ref cursor, 9);
            NodeId publisherId = ReadNodeId(nodes, values, ref cursor, 10);
            ModelCardTypeClient? card = await m_proxy.GetCardAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            NodeId source = await m_operations.FollowReferenceAsync(
                ModelNodeId, ReferenceTypes.ImportedFrom, cancellationToken).ConfigureAwait(false);
            return new AIModelSnapshot
            {
                NodeId = ModelNodeId,
                ModelId = modelId,
                Name = name,
                Version = version,
                Framework = framework,
                Format = format,
                License = license,
                Digest = digest,
                DigestAlgorithm = digestAlgorithm,
                CreatedAt = createdAt,
                LastModifiedAt = lastModifiedAt,
                CardId = card?.ObjectId ?? NodeId.Null,
                PublisherId = publisherId,
                SourceId = source
            };
        }

        public async ValueTask<AIModelCardSnapshot> ReadCardAsync(
            CancellationToken cancellationToken = default)
        {
            ModelCardTypeClient? card = await m_proxy.GetCardAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (card is null || card.ObjectId.IsNull)
            {
                return new AIModelCardSnapshot();
            }
            return await ReadCardAsync(card.ObjectId, cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<AINodeEntry> EnumerateResourcesAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<ReferenceDescription> references = await m_operations
                .BrowseHierarchicalObjectsAsync(ModelNodeId, cancellationToken).ConfigureAwait(false);
            NodeId resourceType = m_operations.AINamespaceType(ObjectTypes.ModelResourceType);
            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    reference.TypeDefinition, m_operations.Session.NamespaceUris);
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, m_operations.Session.NamespaceUris);
                if (typeDef.IsNull || nodeId.IsNull)
                {
                    continue;
                }
                if (typeDef == resourceType ||
                    await m_operations.Session.NodeCache.IsTypeOfAsync(
                        typeDef, resourceType, cancellationToken).ConfigureAwait(false))
                {
                    yield return new AINodeEntry(
                        nodeId, reference.BrowseName, reference.DisplayName, typeDef);
                }
            }
        }

        public async ValueTask<AIModelResourceSnapshot> ReadResourceAsync(
            NodeId resourceNodeId,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(resourceNodeId, nameof(resourceNodeId));
            string[] members =
            [
                BrowseNames.ArtifactUri,
                BrowseNames.ContentType,
                BrowseNames.SizeBytes,
                BrowseNames.Digest,
                BrowseNames.DigestAlgorithm
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                resourceNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadPresentValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AIModelResourceSnapshot
            {
                NodeId = resourceNodeId,
                ArtifactUri = ReadString(nodes, values, ref cursor, 0),
                ContentType = ReadString(nodes, values, ref cursor, 1),
                SizeBytes = ReadUInt64(nodes, values, ref cursor, 2),
                Digest = ReadByteString(nodes, values, ref cursor, 3),
                DigestAlgorithm = ReadString(nodes, values, ref cursor, 4)
            };
        }

        public async ValueTask<AIModelSourceClient?> OpenSourceAsync(
            CancellationToken cancellationToken = default)
        {
            NodeId source = await m_operations.FollowReferenceAsync(
                ModelNodeId, ReferenceTypes.ImportedFrom, cancellationToken).ConfigureAwait(false);
            return source.IsNull ? null : new AIModelSourceClient(m_operations, source);
        }

        private async ValueTask<AIModelCardSnapshot> ReadCardAsync(
            NodeId cardNodeId,
            CancellationToken cancellationToken)
        {
            string[] members =
            [
                BrowseNames.IntendedUse,
                BrowseNames.OutOfScopeUse,
                BrowseNames.Limitations,
                BrowseNames.EthicalConsiderations,
                BrowseNames.TrainingDataCutoff,
                BrowseNames.DataJurisdiction,
                BrowseNames.SafetyAssessment
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                cardNodeId, members, cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadPresentValuesAsync(nodes, cancellationToken)
                .ConfigureAwait(false);
            int cursor = 0;
            return new AIModelCardSnapshot
            {
                NodeId = cardNodeId,
                IntendedUse = ReadString(nodes, values, ref cursor, 0),
                OutOfScopeUse = ReadString(nodes, values, ref cursor, 1),
                Limitations = ReadString(nodes, values, ref cursor, 2),
                EthicalConsiderations = ReadString(nodes, values, ref cursor, 3),
                TrainingDataCutoff = ReadString(nodes, values, ref cursor, 4),
                DataJurisdiction = ReadString(nodes, values, ref cursor, 5),
                SafetyAssessment = ReadStructureArray<SafetyAssessmentDataType>(
                    nodes, values, m_operations.Session.MessageContext, ref cursor, 6)
            };
        }

        private ValueTask<ArrayOf<DataValue>> ReadPresentValuesAsync(
            ArrayOf<NodeId> nodes,
            CancellationToken cancellationToken)
        {
            return m_operations.ReadValuesAsync(nodes, cancellationToken);
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

        private static DateTimeUtc ReadDateTime(
            ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? default : AIClientOperations.ReadDateTime(values[cursor++]);
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

        private static ulong ReadUInt64(ArrayOf<NodeId> nodes, ArrayOf<DataValue> values, ref int cursor, int index)
        {
            return nodes[index].IsNull ? 0 : AIClientOperations.ReadUInt64(values[cursor++]);
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
            return values[cursor++].WrappedValue.TryGetValue(
                    out ArrayOf<T> array, messageContext)
                ? array
                : ArrayOf<T>.Empty;
        }

        private static void ValidateNodeId(NodeId nodeId, string paramName)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentException("NodeId must not be null.", paramName);
            }
        }

        private readonly AIClientOperations m_operations;
        private readonly ModelTypeClient m_proxy;
    }
}
