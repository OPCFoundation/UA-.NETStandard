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
using System.Xml;
using Opc.Ua.Client;
using Opc.Ua.Schema;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Resolves one finite type-definition closure without modifying another session's shared factory or cache.
    /// </summary>
    internal sealed class SchemaDefinitionSnapshot
    {
        /// <summary>
        /// Captures the namespace context and per-operation definition registry.
        /// </summary>
        internal SchemaDefinitionSnapshot(
            IServiceMessageContext context, McpSchemaOptions options, IDataTypeDefinitionResolver? registered = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            m_options = options?.Snapshot() ?? throw new ArgumentNullException(nameof(options));
            m_registered = registered;
            Registry = new DataTypeDefinitionRegistry();
            m_factory = new EncodeableFactoryDefinitionSource(context.Factory, context.NamespaceUris);
            Resolver = Registry;
        }

        /// <summary>
        /// Gets the exact namespace and encodeable context used by these definitions.
        /// </summary>
        internal IServiceMessageContext Context { get; }

        /// <summary>
        /// Gets only the bounded definitions collected for this operation.
        /// </summary>
        internal DataTypeDefinitionRegistry Registry { get; }

        /// <summary>
        /// Gets the resolver for generated and remotely collected definitions.
        /// </summary>
        internal IDataTypeDefinitionResolver Resolver { get; }

        /// <summary>
        /// Gets the count of explicitly collected structure and enum definitions.
        /// </summary>
        internal int Count => m_types.Count;

        /// <summary>
        /// Gets the collected definitions in discovery order, including their actual identifier namespaces.
        /// </summary>
        internal ArrayOf<UaTypeDescription> Types => [.. m_types.Values];

        /// <summary>
        /// Gets the validated remote node without repeating a mutable server read.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal DataTypeNode GetNode(NodeId id)
        {
            return m_nodes.TryGetValue(id, out DataTypeNode? node)
                ? node
                : throw Unsupported("The type was not part of the validated remote snapshot.");
        }

        /// <summary>
        /// Inspects one registered definition without requiring its dependencies to be registered too.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal UaTypeDescription InspectRegistered(NodeId id)
        {
            if (!(m_registered?.TryResolve(id, out UaTypeDescription? description) ?? false))
            {
                if ((Context.Factory.TryGetEncodeableType(id, out IEncodeableType? type) ||
                    Context.Factory.TryGetEncodeableType(
                        NodeId.ToExpandedNodeId(id, Context.NamespaceUris), out type)) &&
                    SchemaTypeDiscovery.LocalId(type.CreateInstance().TypeId, Context.NamespaceUris) != id)
                {
                    throw Unsupported("The identifier is an encoding alias, not a DataType identifier.");
                }
                if (!m_factory.TryResolve(id, out description))
                {
                    throw new ServiceResultException(StatusCodes.BadDataTypeIdUnknown,
                        $"DataType '{id}' has no usable structure or enumeration definition.");
                }
            }
            UaTypeDescription result = Normalize(id, description!);
            ValidateDefinition(result.Definition, m_options);
            return result;
        }

        /// <summary>
        /// Loads selected roots and their dependencies, rejecting missing custom definitions instead of inventing them.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask LoadAsync(
            ISession? session, ArrayOf<NodeId> roots, CancellationToken ct)
        {
            if (roots.Count > m_options.MaxTypes)
            {
                throw Limit("The selected root count exceeds the host type limit.");
            }
            var visiting = new HashSet<NodeId>();
            for (int index = 0; index < roots.Count; index++)
            {
                await LoadTypeAsync(session, roots[index], 0, visiting, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Rejects unsupported wire shapes instead of letting a generator silently approximate them.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        internal void ValidateFormat(UaSchemaFormat format)
        {
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentOutOfRangeException(nameof(format));
            }
            foreach (UaTypeDescription type in m_types.Values)
            {
                if (type.Name.Contains('/', StringComparison.Ordinal) ||
                    type.Name.Contains('~', StringComparison.Ordinal))
                {
                    throw Unsupported("The SDK cannot preserve slash or tilde characters in schema reference names.");
                }
                if (format is UaSchemaFormat.Xsd or UaSchemaFormat.Bsd)
                {
                    ValidateXmlName(type.Name);
                }
                if (format == UaSchemaFormat.Bsd && type.Definition is EnumDefinition { IsOptionSet: true })
                {
                    throw Unsupported("The existing BSD generator cannot preserve OptionSet wire semantics.");
                }
                if (type.Definition is StructureDefinition structure)
                {
                    if (structure.StructureType is StructureType.StructureWithSubtypedValues or
                        StructureType.UnionWithSubtypedValues)
                    {
                        throw Unsupported("The SDK schema generators do not preserve subtyped field envelopes.");
                    }
                    foreach (StructureField field in structure.Fields)
                    {
                        if (format is UaSchemaFormat.Xsd or UaSchemaFormat.Bsd)
                        {
                            ValidateXmlName(field.Name!);
                        }
                        if (field.ValueRank is ValueRanks.Any or ValueRanks.OneOrMoreDimensions)
                        {
                            throw Unsupported("The SDK cannot fully describe an unconstrained-dimensional field.");
                        }
                        if (format == UaSchemaFormat.Bsd &&
                            field.ValueRank is not (ValueRanks.Scalar or ValueRanks.OneDimension))
                        {
                            throw Unsupported("BSD generation supports scalar and one-dimensional fields only.");
                        }
                        if (format == UaSchemaFormat.Xsd && field.ValueRank < ValueRanks.Scalar)
                        {
                            throw Unsupported(
                                "The existing XSD generator cannot preserve variable scalar/array ranks.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Rejects oversized definitions before a schema generator can expand their fields.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateDefinition(DataTypeDefinition definition, McpSchemaOptions options)
        {
            int count = definition switch
            {
                StructureDefinition structure => structure.Fields.Count,
                EnumDefinition enumeration => enumeration.Fields.Count,
                _ => throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Only structure and enumeration definitions are supported.")
            };
            if (count > options.MaxFieldsPerType)
            {
                throw Limit("The selected data type exceeds the host field-count limit.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (definition is StructureDefinition fields)
            {
                if (!Enum.IsDefined(fields.StructureType))
                {
                    throw Unsupported("The StructureType discriminator is not supported.");
                }
                int optional = 0;
                foreach (StructureField field in fields.Fields)
                {
                    if (field is null)
                    {
                        throw Unsupported("A structure field cannot be null.");
                    }
                    ValidateName(field.Name, names);
                    ValidateRank(field.ValueRank, options);
                    if (field.DataType.IsNull)
                    {
                        throw Unsupported("A structure field has no declared DataType.");
                    }
                    if (field.IsOptional && ++optional > 32)
                    {
                        throw Unsupported("The binary optional-field encoding mask cannot exceed 32 bits.");
                    }
                    if (field.ArrayDimensions.Count > options.MaxDepth)
                    {
                        throw Limit("Array dimensions exceed the host depth limit.");
                    }
                }
            }
            else if (definition is EnumDefinition enumeration)
            {
                var values = new HashSet<long>();
                foreach (EnumField field in enumeration.Fields)
                {
                    if (field is null)
                    {
                        throw Unsupported("An enumeration field cannot be null.");
                    }
                    ValidateName(field.Name, names);
                    if (!values.Add(field.Value) ||
                        (!enumeration.IsOptionSet &&
                            (field.Value < int.MinValue || field.Value > int.MaxValue)) ||
                        (enumeration.IsOptionSet && field.Value < 0))
                    {
                        throw Unsupported("The enumeration contains duplicate or unrepresentable values.");
                    }
                }
            }
        }

        /// <summary>
        /// Rejects ranks which would cause an unbounded nested-array expansion.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateRank(int rank, McpSchemaOptions options)
        {
            if (rank < ValueRanks.ScalarOrOneDimension)
            {
                throw Unsupported("The ValueRank discriminator is not supported.");
            }
            if (rank > options.MaxDepth)
            {
                throw Limit("The field's array rank exceeds the host depth limit.");
            }
        }

        /// <summary>
        /// Creates a consistent host-budget error.
        /// </summary>
        internal static ServiceResultException Limit(string message)
        {
            return new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, message);
        }

        /// <summary>
        /// Creates a precise unsupported-definition error.
        /// </summary>
        internal static ServiceResultException Unsupported(string message)
        {
            return new ServiceResultException(StatusCodes.BadNotSupported, message);
        }

        /// <summary>
        /// Walks dependencies before any generator can obtain them; built-in scalar edges consume no type slots.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask LoadTypeAsync(
            ISession? session, NodeId id, int depth, HashSet<NodeId> visiting, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (id.IsNull)
            {
                throw Unsupported("A non-null DataType NodeId is required.");
            }
            if (visiting.Contains(id))
            {
                return;
            }
            if (depth > 0 && TypeInfo.GetBuiltInType(id) != BuiltInType.Null)
            {
                return;
            }
            if (depth > m_options.MaxDepth)
            {
                throw Limit("The requested type dependency depth exceeds the host limit.");
            }
            if (m_types.ContainsKey(id))
            {
                if (m_heights.TryGetValue(id, out int height) && depth + height > m_options.MaxDepth)
                {
                    throw Limit("A previously collected dependency exceeds the remaining host depth budget.");
                }
                return;
            }
            if (m_types.Count >= m_options.MaxTypes)
            {
                throw Limit("The requested schema type closure exceeds the host type limit.");
            }
            UaTypeDescription? description = null;
            if (session is not null && id.NamespaceIndex != 0)
            {
                (UaTypeDescription resolved, DataTypeNode node) =
                    await SchemaTypeDiscovery.ReadTypeAsync(session, id, m_options, ct).ConfigureAwait(false);
                description = resolved;
                m_nodes.Add(id, node);
            }
            else
            {
                description = InspectRegistered(id);
            }
            UaTypeDescription normalized = Normalize(id, description!);
            ValidateDefinition(normalized.Definition, m_options);
            if (normalized.Definition is EnumDefinition { IsOptionSet: true } enumeration &&
                !normalized.IsStructureOptionSet &&
                enumeration.Fields.FindIndex(field => field.Value > 63) >= 0)
            {
                throw Unsupported("An integer-backed OptionSet cannot address bits above 63.");
            }
            if (m_types.Values.Any(existing =>
                existing.NamespaceUri == normalized.NamespaceUri && existing.Name == normalized.Name))
            {
                throw Unsupported("Distinct DataTypes share a schema name in the same identifier namespace.");
            }
            m_types.Add(id, normalized);
            Registry.Add(normalized);
            visiting.Add(id);
            int dependencyHeight = 0;
            if (normalized.Definition is StructureDefinition structure)
            {
                if (!structure.BaseDataType.IsNull && structure.BaseDataType != id)
                {
                    await LoadTypeAsync(session, structure.BaseDataType, depth + 1, visiting, ct)
                        .ConfigureAwait(false);
                    if (m_heights.TryGetValue(structure.BaseDataType, out int height))
                    {
                        dependencyHeight = Math.Max(dependencyHeight, height + 1);
                    }
                }
                for (int index = 0; index < structure.Fields.Count; index++)
                {
                    StructureField field = structure.Fields[index];
                    await LoadTypeAsync(session, field.DataType, depth + 1, visiting, ct).ConfigureAwait(false);
                    if (m_heights.TryGetValue(field.DataType, out int height))
                    {
                        dependencyHeight = Math.Max(dependencyHeight, height + 1);
                    }
                }
            }
            m_heights[id] = dependencyHeight;
            visiting.Remove(id);
        }

        /// <summary>
        /// Binds registry aliases to the identifier namespace, never the BrowseName or XML name namespace.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private UaTypeDescription Normalize(NodeId id, UaTypeDescription description)
        {
            if (description.BrowseName.IsNull || string.IsNullOrWhiteSpace(description.BrowseName.Name))
            {
                throw Unsupported("A DataType requires an explicit BrowseName for schema generation.");
            }
            string? uri = Context.NamespaceUris.GetString(id.NamespaceIndex);
            if (string.IsNullOrEmpty(uri))
            {
                throw Unsupported("The DataType identifier uses an unknown namespace index.");
            }
            return new UaTypeDescription(id, description.BrowseName, description.Definition, uri,
                description.IsStructureOptionSet);
        }

        /// <summary>
        /// Requires explicit unique field names rather than the generators' synthetic fallback names.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateName(string? name, HashSet<string> names)
        {
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                throw Unsupported("Fields must have non-empty, unique names.");
            }
        }

        /// <summary>
        /// Rejects invalid XML identifiers before the SDK writes a syntactically well-formed but unusable schema.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateXmlName(string name)
        {
            try
            {
                _ = XmlConvert.VerifyNCName(name);
            }
            catch (XmlException)
            {
                throw Unsupported("A type or field name cannot be represented as an XML schema name.");
            }
        }

        /// <summary>Immutable per-operation host limits.</summary>
        private readonly McpSchemaOptions m_options;

        /// <summary>Generated definitions used only during bounded collection.</summary>
        private readonly EncodeableFactoryDefinitionSource m_factory;

        /// <summary>The optional host's explicitly registered definition source.</summary>
        private readonly IDataTypeDefinitionResolver? m_registered;

        /// <summary>Definitions collected in their actual identifier namespace.</summary>
        private readonly Dictionary<NodeId, UaTypeDescription> m_types = [];

        /// <summary>Validated remote attributes retained only for this operation's client loader.</summary>
        private readonly Dictionary<NodeId, DataTypeNode> m_nodes = [];

        /// <summary>Known dependency heights prevent shared graph edges bypassing the depth budget.</summary>
        private readonly Dictionary<NodeId, int> m_heights = [];
    }
}
