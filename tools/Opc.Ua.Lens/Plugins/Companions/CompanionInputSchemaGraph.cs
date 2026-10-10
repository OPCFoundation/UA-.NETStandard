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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Companions.Providers;
using UaLens.StructuredValues;

namespace UaLens.Plugins.Companions
{
    /// <summary>
    /// Pins the transitive definitions used by a typed input, including concrete
    /// structures carried inside Variant fields. Resolution never writes values.
    /// </summary>
    internal sealed class CompanionInputSchemaGraph
    {
        private CompanionInputSchemaGraph(IStructuredValueService values)
        {
            m_values = values;
        }

        public static async Task<CompanionInputSchemaGraph> ResolveAsync(
            NodeId root, DataTypeDefinition definition, IStructuredValueService values,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(values);
            cancellationToken.ThrowIfCancellationRequested();
            var graph = new CompanionInputSchemaGraph(values);
            graph.Add(root, definition, BuiltInType.Null);
            await graph.ResolvePendingAsync(cancellationToken).ConfigureAwait(false);
            return graph;
        }

        public Task ValidateAsync(
            NodeId dataType, Variant value, int rank, ArrayOf<uint> dimensions, CancellationToken cancellationToken)
        {
            return ValidateCoreAsync(dataType, value, rank, dimensions, 0, cancellationToken);
        }

        public ByteString GetDigest()
        {
            if (m_entries.Count == 1)
            {
                return ByteString.From(SHA256.HashData(m_entries[0].Encoded.Span));
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (Entry entry in m_entries)
            {
                using var buffer = new IndustrialDocumentBuffer(CompanionInputContract.MaximumEncodedBytes);
                using var encoder = new BinaryEncoder(buffer, m_values.MessageContext, leaveOpen: true);
                encoder.WriteExpandedNodeId(null,
                    NodeId.ToExpandedNodeId(entry.Id, m_values.MessageContext.NamespaceUris));
                encoder.WriteInt32(null, (int)entry.ScalarType);
                encoder.WriteInt32(null, entry.Encoded.Length);
                hash.AppendData(encoder.CloseAndReturnBuffer()!);
                hash.AppendData(entry.Encoded.Span);
            }
            return ByteString.From(hash.GetHashAndReset());
        }

        internal static ByteString EncodeDefinition(DataTypeDefinition definition, IServiceMessageContext context)
        {
            using var buffer = new IndustrialDocumentBuffer(CompanionInputContract.MaximumEncodedBytes);
            using var encoder = new BinaryEncoder(buffer, context, leaveOpen: true);
            encoder.WriteVariant(null, Variant.FromStructure(definition));
            return ByteString.From(encoder.CloseAndReturnBuffer());
        }

        private async Task ValidateCoreAsync(
            NodeId dataType, Variant value, int rank, ArrayOf<uint> dimensions, int depth,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaximumDepth)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The typed input exceeds its nesting limit.");
            }
            if (!value.TypeInfo.IsScalar)
            {
                var array = new StructuredArrayDraft(value.TypeInfo.BuiltInType, rank, dimensions,
                    value, m_values.MessageContext, cancellationToken.ThrowIfCancellationRequested);
                ArrayOf<Variant> elements = array.InitialValue.Elements;
                for (int index = 0; index < elements.Count; index++)
                {
                    await ValidateCoreAsync(dataType, elements[index], ValueRanks.Scalar, [], depth + 1,
                        cancellationToken).ConfigureAwait(false);
                }
                return;
            }
            if (depth != 0 &&
                (value.IsNull || (value.TryGetValue(out ExtensionObject empty) && empty.IsNull)))
            {
                return;
            }
            if ((dataType == DataTypeIds.BaseDataType || dataType == DataTypeIds.Structure) &&
                value.TryGetValue(out ExtensionObject extension) &&
                !extension.IsNull)
            {
                if (!extension.TryGetValue(out IEncodeable? body, m_values.MessageContext) || body is null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDataTypeIdUnknown, "An embedded structure has no resolved native type.");
                }
                dataType = ExpandedNodeId.ToNodeId(body.TypeId, m_values.MessageContext.NamespaceUris);
                if (dataType.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDataTypeIdUnknown, "An embedded structure namespace is unresolved.");
                }
                await ResolveTypeAsync(dataType, cancellationToken).ConfigureAwait(false);
                await ResolvePendingAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!m_byId.TryGetValue(dataType, out Entry? entry) || entry.Definition is null)
            {
                return;
            }
            StructuredValueDraft draft = await m_values.OpenAsync(
                dataType, entry.Definition, value, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<StructuredValueField> fields = draft.Fields;
            for (int index = 0; index < fields.Count; index++)
            {
                StructuredValueField field = fields[index];
                if (field.IsIncluded)
                {
                    await ValidateCoreAsync(field.Definition.DataType, field.Value,
                        field.Definition.ValueRank, field.Definition.ArrayDimensions, depth + 1, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task ResolvePendingAsync(CancellationToken cancellationToken)
        {
            while (m_pending.TryDequeue(out DataTypeDefinition? definition))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (definition is not StructureDefinition structure)
                {
                    continue;
                }
                await ResolveTypeAsync(structure.BaseDataType, cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < structure.Fields.Count; index++)
                {
                    if (structure.Fields[index].DataType.IsNull)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDataTypeIdUnknown, "A structure field has no declared DataType.");
                    }
                    await ResolveTypeAsync(structure.Fields[index].DataType, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ResolveTypeAsync(NodeId id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (id.IsNull || TypeInfo.GetBuiltInType(id) != BuiltInType.Null || m_byId.ContainsKey(id))
            {
                return;
            }
            if (m_entries.Count >= MaximumDefinitions)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The typed input references too many definitions.");
            }
            DataTypeDefinition? definition = await m_values.ResolveAsync(id, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            BuiltInType scalarType = BuiltInType.Null;
            if (definition is null)
            {
                IType? type = TypeInfo.GetSystemType(
                    NodeId.ToExpandedNodeId(id, m_values.MessageContext.NamespaceUris),
                    m_values.MessageContext.Factory);
                if (type is not IBuiltInType scalar ||
                    type is IEnumeratedType ||
                    !CompanionInputContract.IsScalarType(scalar.BuiltInType))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDataTypeIdUnknown, "A referenced input definition is unavailable.");
                }
                scalarType = scalar.BuiltInType;
            }
            Add(id, definition, scalarType);
        }

        private void Add(NodeId id, DataTypeDefinition? definition, BuiltInType scalarType)
        {
            definition = CoreUtils.Clone(definition);
            ByteString encoded = definition is null
                ? ByteString.Empty : EncodeDefinition(definition, m_values.MessageContext);
            m_encodedBytes = checked(m_encodedBytes + encoded.Length);
            if (m_encodedBytes > CompanionInputContract.MaximumEncodedBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The input definitions exceed their aggregate byte limit.");
            }
            var entry = new Entry(id, definition, encoded, scalarType);
            m_entries.Add(entry);
            m_byId.Add(id, entry);
            if (definition is not null)
            {
                m_pending.Enqueue(definition);
            }
        }

        private sealed record Entry(
            NodeId Id, DataTypeDefinition? Definition, ByteString Encoded, BuiltInType ScalarType);

        private const int MaximumDefinitions = 128;
        private const int MaximumDepth = 64;
        private readonly IStructuredValueService m_values;
        private readonly Dictionary<NodeId, Entry> m_byId = [];
        private readonly List<Entry> m_entries = [];
        private readonly Queue<DataTypeDefinition> m_pending = [];
        private int m_encodedBytes;
    }
}
