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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// Pins source or target schema to the actual adapter namespace generation and checks it before each operation.
    /// </summary>
    internal sealed class PubSubSchemaMapping : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Captures reviewed and adapter contexts without owning the connected server session.
        /// </summary>
        public PubSubSchemaMapping(
            IServerSession session, PubSubConfiguration configuration, ArrayOf<NodeId> nodes,
            IServiceMessageContext reviewed, IServiceMessageContext? remote, bool write)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_configuration = configuration;
            m_nodes = nodes;
            m_reviewed = reviewed;
            m_write = write;
            if (configuration.Fields.Count != nodes.Count)
            {
                throw new ArgumentException("Every reviewed field requires exactly one mapped node.", nameof(nodes));
            }
            if (remote is null &&
                (!configuration.Structures.IsEmpty ||
                    configuration.Fields.Contains(field => PubSubFieldSchema.UsesNamespaces(field.Type))))
            {
                throw new InvalidOperationException(
                    "Namespace-bearing mappings require the adapter's actual namespace context.");
            }
            m_remoteContext = remote;
            m_remoteNamespaces = remote?.NamespaceUris.ToArray();
            m_reviewedNamespaces = reviewed.NamespaceUris.ToArray();
            var remoteValues = new PubSubConfiguredValueService(
                configuration, reviewed.Telemetry, namespaces: remote?.NamespaceUris);
            m_remoteValues = remoteValues.MessageContext;
            m_session.ModelChanged += OnModelChanged;
        }

        /// <summary>
        /// Rechecks complete node metadata, type definitions, namespaces and write permissions.
        /// </summary>
        public async ValueTask ValidateAsync(CancellationToken cancellationToken)
        {
            RequireCurrent();
            var reads = new List<ReadValueId>();
            foreach (NodeId node in m_nodes)
            {
                reads.Add(new ReadValueId { NodeId = node, AttributeId = Attributes.DataType });
                reads.Add(new ReadValueId { NodeId = node, AttributeId = Attributes.ValueRank });
                reads.Add(new ReadValueId { NodeId = node, AttributeId = Attributes.ArrayDimensions });
            }
            if (m_remoteContext is not null)
            {
                reads.Add(new ReadValueId
                {
                    NodeId = VariableIds.Server_NamespaceArray, AttributeId = Attributes.Value
                });
            }
            ArrayOf<DataValue> result = await m_session.ReadAsync([.. reads], cancellationToken).ConfigureAwait(false);
            RequireGood(result, reads.Count);
            if (m_remoteContext is not null &&
                (!result[^1].WrappedValue.TryGetValue(out ArrayOf<string> namespaces) ||
                    !namespaces.Span.SequenceEqual(m_remoteNamespaces)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The adapter's server namespace table changed. Review the mapping again.");
            }
            for (int index = 0; index < m_configuration.Fields.Count; index++)
            {
                PubSubFieldConfiguration field = m_configuration.Fields[index];
                int offset = index * 3;
                NodeId expected = PubSubStructureSchema.FieldType(field, m_remoteValues.NamespaceUris);
                if (!result[offset].WrappedValue.TryGetValue(out NodeId type) ||
                    type != expected ||
                    !result[offset + 1].WrappedValue.TryGetValue(out int rank) ||
                    rank != field.ValueRank ||
                    !result[offset + 2].WrappedValue.TryGetValue(out ArrayOf<uint> dimensions) ||
                    !dimensions.Span.SequenceEqual(field.ArrayDimensions.Span))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A mapped node's type, rank or dimensions differ from the reviewed field schema.");
                }
            }
            if (!m_configuration.Structures.IsEmpty)
            {
                var definitions = new List<ReadValueId>();
                foreach (PubSubStructureConfiguration type in m_configuration.Structures)
                {
                    definitions.Add(new ReadValueId
                    {
                        NodeId = PubSubStructureSchema.Resolve(type.DataTypeId, m_remoteValues.NamespaceUris),
                        AttributeId = Attributes.DataTypeDefinition
                    });
                }
                ArrayOf<DataValue> actual = await m_session.ReadAsync([.. definitions], cancellationToken)
                    .ConfigureAwait(false);
                RequireGood(actual, definitions.Count);
                for (int index = 0; index < actual.Count; index++)
                {
                    StructureDefinition expected = PubSubStructureSchema.Definition(
                        m_configuration.Structures[index], m_remoteValues.NamespaceUris);
                    if (!actual[index].WrappedValue.TryGetValue<StructureDefinition>(
                        out StructureDefinition? definition, m_remoteValues) ||
                        !expected.IsEqual(definition))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState,
                            "The adapter's structure definition changed. Review and configure it again.");
                    }
                }
            }
            if (m_write)
            {
                ArrayOf<DataValue> access = await m_session.ReadAsync(m_nodes.ConvertAll(node =>
                    new ReadValueId { NodeId = node, AttributeId = Attributes.UserAccessLevel }), cancellationToken)
                    .ConfigureAwait(false);
                RequireGood(access, m_nodes.Count);
                foreach (DataValue value in access)
                {
                    if (!value.WrappedValue.TryGetValue(out byte allowed) ||
                        (allowed & AccessLevels.CurrentWrite) == 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                            "A selected mapped node does not permit value writes.");
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            RequireCurrent();
        }

        /// <summary>
        /// Captures a remote field in the reviewed namespace order and validates its declared shape.
        /// </summary>
        public Variant FromRemote(int index, Variant value)
        {
            RequireCurrent();
            Variant source = PubSubFieldSchema.Snapshot(value, m_remoteValues);
            ByteString encoded = PubSubFieldSchema.Encode(source, m_remoteValues, m_reviewed.NamespaceUris);
            return PubSubFieldSchema.ValidateAndSnapshot(m_configuration.Fields[index],
                PubSubFieldSchema.Decode(encoded, m_reviewed), m_reviewed);
        }

        /// <summary>
        /// Remaps a reviewed value without guessing missing namespace or server indexes.
        /// </summary>
        public Variant ToRemote(Variant value)
        {
            RequireCurrent();
            ByteString encoded = PubSubFieldSchema.Encode(value, m_reviewed, m_remoteValues.NamespaceUris);
            return PubSubFieldSchema.Decode(encoded, m_remoteValues);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_session.ModelChanged -= OnModelChanged;
            Interlocked.Exchange(ref m_invalidated, 1);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void OnModelChanged(object? sender, EventArgs args)
        {
            Interlocked.Exchange(ref m_invalidated, 1);
        }

        private void RequireCurrent()
        {
            if (Volatile.Read(ref m_invalidated) != 0 ||
                !m_session.IsConnected ||
                !m_reviewed.NamespaceUris.ToArray().AsSpan().SequenceEqual(m_reviewedNamespaces) ||
                (m_remoteContext is not null &&
                    !m_remoteContext.NamespaceUris.ToArray().AsSpan().SequenceEqual(m_remoteNamespaces)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "The mapped session or schema generation changed; no automatic remapping is allowed.");
            }
        }

        private static void RequireGood(ArrayOf<DataValue> result, int count)
        {
            if (result.Count != count)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "A mapped metadata read is incomplete.");
            }
            foreach (DataValue value in result)
            {
                if (!StatusCode.IsGood(value.StatusCode))
                {
                    throw new ServiceResultException(value.StatusCode, "Mapped metadata could not be read reliably.");
                }
            }
        }

        private readonly IServerSession m_session;
        private readonly PubSubConfiguration m_configuration;
        private readonly ArrayOf<NodeId> m_nodes;
        private readonly IServiceMessageContext m_reviewed;
        private readonly string[] m_reviewedNamespaces;
        private readonly IServiceMessageContext m_remoteValues;
        private readonly IServiceMessageContext? m_remoteContext;
        private readonly string[]? m_remoteNamespaces;
        private readonly bool m_write;
        private int m_invalidated;
    }

    /// <summary>
    /// Uses the existing UA sampling module while refusing schema drift or partial fields before publishing.
    /// </summary>
    internal sealed class PubSubSchemaSource(
        IPublishedDataSetSource source, PubSubSchemaMapping mapping, DataSetMetaDataType metadata)
        : IPublishedDataSetSource
    {
        /// <inheritdoc/>
        public DataSetMetaDataType BuildMetaData()
        {
            return CoreUtils.Clone(metadata)!;
        }

        /// <inheritdoc/>
        public async ValueTask<PublishedDataSetSnapshot> SampleAsync(
            DataSetMetaDataType metaData, CancellationToken cancellationToken = default)
        {
            if (!PubSubStructureSchema.Matches(metadata, metaData))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The publication schema changed.");
            }
            await mapping.ValidateAsync(cancellationToken).ConfigureAwait(false);
            PublishedDataSetSnapshot snapshot = await source.SampleAsync(metadata, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot.Fields.Count != metadata.Fields.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The source returned a partial dataset.");
            }
            var fields = new DataSetField[snapshot.Fields.Count];
            for (int index = 0; index < fields.Length; index++)
            {
                DataSetField field = snapshot.Fields[index];
                if (field.Name != metadata.Fields[index].Name || !StatusCode.IsGood(field.StatusCode))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A source field has incompatible identity or quality.");
                }
                fields[index] = field with { Value = mapping.FromRemote(index, field.Value) };
            }
            await mapping.ValidateAsync(cancellationToken).ConfigureAwait(false);
            return new PublishedDataSetSnapshot(
                metadata.ConfigurationVersion, fields, snapshot.SampledAt, snapshot.MessageType);
        }
    }
}
