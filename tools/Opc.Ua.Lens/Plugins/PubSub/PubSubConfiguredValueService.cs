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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.StructuredValues;

namespace UaLens.Plugins.PubSub
{
    /// <summary>
    /// Compiles a reviewed offline schema through the existing NativeAOT-safe structure adapters and editor.
    /// </summary>
    internal sealed class PubSubConfiguredValueService : IStructuredValueService
    {
        /// <summary>
        /// Creates an isolated type factory with no server or reflection dependency.
        /// </summary>
        public PubSubConfiguredValueService(
            PubSubConfiguration configuration, ITelemetryContext telemetry, Action? requireCurrent = null,
            NamespaceTable? namespaces = null)
        {
            PubSubStructureSchema.Validate(configuration);
            m_context = PubSubFieldSchema.CreateContext(telemetry);
            m_context.NamespaceUris = namespaces is null
                ? PubSubStructureSchema.NamespaceTable(configuration) : new NamespaceTable(namespaces);
            m_requireCurrent = requireCurrent;
            m_fingerprint = Fingerprint(configuration);
            var declarations = new Dictionary<NodeId, PubSubStructureConfiguration>();
            foreach (PubSubStructureConfiguration structure in configuration.Structures)
            {
                declarations.Add(
                    PubSubStructureSchema.Resolve(structure.DataTypeId, m_context.NamespaceUris), structure);
            }
            var factory = new DefaultComplexTypeFactory();
            foreach (NodeId id in declarations.Keys)
            {
                _ = Build(id);
            }

            IType Build(NodeId id)
            {
                if (m_types.TryGetValue(id, out IType? existing))
                {
                    return existing;
                }
                if (id.NamespaceIndex == 0)
                {
                    return TypeInfo.GetSystemType(TypeInfo.GetBuiltInType(id));
                }
                PubSubStructureConfiguration structure = declarations[id];
                StructureDefinition definition = PubSubStructureSchema.Definition(structure, m_context.NamespaceUris);
                IComplexTypeFieldBuilder builder = factory.Create(
                        m_context.NamespaceUris.GetString(id.NamespaceIndex)!, id.NamespaceIndex)
                    .AddStructuredType(new QualifiedName(structure.Name, id.NamespaceIndex), definition);
                builder.AddTypeIdAttribute(
                    NodeId.ToExpandedNodeId(id, m_context.NamespaceUris),
                    NodeId.ToExpandedNodeId(definition.DefaultEncodingId, m_context.NamespaceUris),
                    ExpandedNodeId.Null);
                for (int index = 0; index < definition.Fields.Count; index++)
                {
                    StructureField field = definition.Fields[index];
                    builder.AddField(field, Build(field.DataType), index + 1, allowSubTypes: false);
                }
                IEncodeableType type = builder.CreateType();
                m_context.Factory.Builder.AddEncodeableType(type)
                    .AddEncodeableType(new ExpandedNodeId(id), type)
                    .AddEncodeableType(new ExpandedNodeId(definition.DefaultEncodingId), type)
                    .Commit();
                m_types.Add(id, type);
                m_definitions.Add(id, definition);
                return type;
            }
        }

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_context;

        /// <summary>
        /// Captures a local sample only after its exact field type and reviewed schema are validated.
        /// </summary>
        public PubSubFieldConfiguration CaptureSample(PubSubFieldConfiguration field, Variant value)
        {
            m_requireCurrent?.Invoke();
            Variant captured = PubSubFieldSchema.ValidateAndSnapshot(field, value, m_context);
            ByteString encoded = PubSubFieldSchema.Encode(captured, m_context);
            return field with { SampleValue = Convert.ToBase64String(encoded.Span), SampleSchema = m_fingerprint };
        }

        /// <summary>
        /// Returns a detached sample only when its schema fingerprint is still current.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ReadSample(PubSubFieldConfiguration field)
        {
            m_requireCurrent?.Invoke();
            if (field.SampleSchema != m_fingerprint ||
                field.SampleValue.Length > (PubSubFieldSchema.MaximumValueBytes * 4 / 3) + 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The local sample's schema changed. Edit it again.");
            }
            ByteString bytes;
            try
            {
                bytes = ByteString.From(Convert.FromBase64String(field.SampleValue));
            }
            catch (FormatException error)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The local sample is not valid base64.", error);
            }
            return PubSubFieldSchema.ValidateAndSnapshot(field, PubSubFieldSchema.Decode(bytes, m_context), m_context);
        }

        /// <inheritdoc/>
        public Task<DataTypeDefinition?> ResolveAsync(NodeId dataTypeId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_requireCurrent?.Invoke();
            return Task.FromResult<DataTypeDefinition?>(
                m_definitions.TryGetValue(dataTypeId, out StructureDefinition? definition)
                    ? CoreUtils.Clone(definition) : null);
        }

        /// <inheritdoc/>
        public Task<StructuredValueDraft> OpenAsync(
            NodeId dataTypeId, DataTypeDefinition definition, Variant value,
            CancellationToken cancellationToken = default)
        {
            int revision = Volatile.Read(ref m_revision);
            void RequireCurrent()
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_requireCurrent?.Invoke();
                if (Volatile.Read(ref m_revision) != revision)
                {
                    throw new InvalidOperationException("The reviewed PubSub schema changed.");
                }
            }
            RequireCurrent();
            if (!m_definitions.TryGetValue(dataTypeId, out StructureDefinition? expected) ||
                !expected.IsEqual(definition) ||
                m_types[dataTypeId] is not IEncodeableType type)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDataTypeIdUnknown, "The type is not in the reviewed schema.");
            }
            IEncodeable source;
            if (value.IsNull || value.ValueIsDefaultOrNull)
            {
                source = type.CreateInstance();
            }
            else if (!value.TryGetValue<IEncodeable>(out IEncodeable? decoded, m_context) ||
                decoded is null ||
                ExpandedNodeId.ToNodeId(decoded.TypeId, m_context.NamespaceUris) != dataTypeId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The value has a different structure type.");
            }
            else
            {
                source = decoded;
            }
            IEncodeable adapter = type.CreateInstance();
            StructuredValueDraft.CopyBody(source, adapter, m_context);
            return Task.FromResult(StructuredValueDraft.ForStructure(
                dataTypeId, expected, source, adapter, m_context, RequireCurrent));
        }

        /// <inheritdoc/>
        public Task<StructuredArrayDraft> OpenArrayAsync(
            NodeId elementDataTypeId, int valueRank, ArrayOf<uint> declaredDimensions,
            Variant value, bool isStructureField = false, CancellationToken cancellationToken = default)
        {
            int revision = Volatile.Read(ref m_revision);
            BuiltInType type = elementDataTypeId.NamespaceIndex == 0
                ? TypeInfo.GetBuiltInType(elementDataTypeId)
                : m_types.ContainsKey(elementDataTypeId) ? BuiltInType.ExtensionObject : BuiltInType.Null;
            if (type == BuiltInType.Null)
            {
                throw new ServiceResultException(StatusCodes.BadDataTypeIdUnknown);
            }
            return Task.FromResult(new StructuredArrayDraft(type, valueRank, declaredDimensions, value, m_context, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_requireCurrent?.Invoke();
                if (Volatile.Read(ref m_revision) != revision)
                {
                    throw new InvalidOperationException("The reviewed PubSub schema changed.");
                }
            }, isStructureField));
        }

        /// <inheritdoc/>
        public void Refresh()
        {
            Interlocked.Increment(ref m_revision);
        }

        private static string Fingerprint(PubSubConfiguration configuration)
        {
            var schema = new PubSubConfiguration
            {
                Namespaces = configuration.Namespaces,
                Structures = configuration.Structures,
                DataSetClassId = configuration.DataSetClassId,
                MetadataMajorVersion = configuration.MetadataMajorVersion,
                MetadataMinorVersion = configuration.MetadataMinorVersion,
                Fields = configuration.Fields.ConvertAll(field => field with
                {
                    SourceNodeId = string.Empty,
                    TargetNodeId = string.Empty,
                    SampleValue = string.Empty,
                    SampleSchema = string.Empty
                })
            };
            string json = PubSubStateCodec.Format(schema);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        private readonly ServiceMessageContext m_context;
        private readonly Action? m_requireCurrent;
        private readonly string m_fingerprint;
        private readonly Dictionary<NodeId, IType> m_types = [];
        private readonly Dictionary<NodeId, StructureDefinition> m_definitions = [];
        private int m_revision;
    }
}
