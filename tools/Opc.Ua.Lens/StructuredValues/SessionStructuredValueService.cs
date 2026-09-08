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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Schema;

namespace UaLens.StructuredValues;

/// <summary>
/// Session-scoped schema reads and NativeAOT-safe structured-value construction.
/// A managed session can replace its inner session without changing its own identity.
/// </summary>
internal sealed class SessionStructuredValueService : IStructuredValueService, IDisposable
{
    public SessionStructuredValueService(ISession session, IComplexTypeSystemFactory? typeSystems = null)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_typeSystems = typeSystems;
    }

    public IServiceMessageContext MessageContext => m_session.MessageContext ??
        throw new ServiceResultException(StatusCodes.BadNotConnected);

    public async Task<DataTypeDefinition?> ResolveAsync(
        NodeId dataTypeId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dataTypeId.IsNull)
        {
            return null;
        }

        Generation generation = GetGeneration();
        lock (m_gate)
        {
            EnsureCurrent(generation);
            if (generation.Definitions.TryGetValue(dataTypeId, out DataTypeDefinition? cached))
            {
                return CoreUtils.Clone(cached);
            }
        }

        ArrayOf<ReadValueId> ids =
        [
            new ReadValueId { NodeId = dataTypeId, AttributeId = Attributes.DataTypeDefinition }
        ];
        ReadResponse response = await m_session.ReadAsync(
            null, 0, TimestampsToReturn.Neither, ids, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(generation);
        if (!StatusCode.IsGood(response.ResponseHeader.ServiceResult))
        {
            throw new ServiceResultException(response.ResponseHeader.ServiceResult);
        }
        if (response.Results.Count != 1)
        {
            throw new ServiceResultException(
                StatusCodes.BadDecodingError, "The DataTypeDefinition read returned an invalid result count.");
        }

        DataTypeDefinition? definition = ReadDefinition(response.Results[0], generation.MessageContext);
        if (definition is null)
        {
            // A generated definition remains usable on pre-1.04 servers. Do not
            // take this path for denied reads or transport/decoding failures, or
            // reuse a server-derived adapter's old schema after a refresh.
            var generated = new EncodeableFactoryDefinitionSource(m_session.Factory, generation.NamespaceUris);
            IType? registered = FindType(dataTypeId, generation);
            bool serverDerived = registered is Opc.Ua.Encoders.Structure
                or Opc.Ua.Encoders.Enumeration or Opc.Ua.Encoders.OptionSet;
            if (!serverDerived && generated.TryResolve(dataTypeId, out UaTypeDescription? description))
            {
                definition = description.Definition;
            }
        }

        lock (m_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrent(generation);
            generation.Definitions[dataTypeId] = CoreUtils.Clone(definition);
        }
        return CoreUtils.Clone(definition);
    }

    public async Task<StructuredValueDraft> OpenAsync(
        NodeId dataTypeId,
        DataTypeDefinition definition,
        Variant value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        Generation generation = GetGeneration();

        await m_typeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureCurrent(generation);
            if (definition is EnumDefinition enumeration)
            {
                IEnumeratedType? enumType = FindType(dataTypeId, generation) as IEnumeratedType;
                enumType ??= new DefaultComplexTypeFactory()
                    .Create(generation.NamespaceUris.GetString(dataTypeId.NamespaceIndex) ?? string.Empty,
                        dataTypeId.NamespaceIndex)
                    .AddEnumType(new QualifiedName("Value", dataTypeId.NamespaceIndex), enumeration);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCurrent(generation);
                return StructuredValueDraft.ForEnumeration(
                    dataTypeId, enumeration, value, enumType, () => EnsureCurrent(generation));
            }
            if (definition is not StructureDefinition structureDefinition)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "The definition is not editable.");
            }

            IEncodeable source;
            if (value.IsNull ||
                (value.TryGetValue(out ExtensionObject empty) && empty.IsNull))
            {
                IType? type = await LoadTypeAsync(dataTypeId, generation, cancellationToken).ConfigureAwait(false);
                if (type is not IEncodeableType activator)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDataTypeIdUnknown,
                        $"No native structure adapter is available for {dataTypeId}.");
                }
                source = activator.CreateInstance();
            }
            else if (!value.TryGetValue(out ExtensionObject extension))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The value cannot be decoded as a structured value.");
            }
            else
            {
                if (!extension.TryGetValue(out IEncodeable? decoded, generation.MessageContext))
                {
                    // The first Read can precede complex-type loading and return
                    // a binary/XML body. Load the default native adapter before
                    // retrying its decode; malformed bodies still fail explicitly.
                    _ = await LoadTypeAsync(dataTypeId, generation, cancellationToken).ConfigureAwait(false);
                    if (!extension.TryGetValue(out decoded, generation.MessageContext))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "The value cannot be decoded with its native type adapter.");
                    }
                }
                source = decoded;
            }

            if (!StructuredValueDraft.SameType(source.TypeId, dataTypeId, generation.NamespaceUris))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "The value does not match its DataType.");
            }

            IEncodeable adapter = source;
            bool needsPresenceAdapter = structureDefinition.StructureType switch
            {
                StructureType.StructureWithOptionalFields => source is not Opc.Ua.Encoders.StructureWithOptionalFields,
                StructureType.Union or StructureType.UnionWithSubtypedValues => source is not Opc.Ua.Encoders.Union,
                _ => false
            };
            if (source is not IStructure || needsPresenceAdapter)
            {
                // Generated types expose native Encode/Decode and activators, not
                // necessarily IStructure. Core.Schema's default adapter supplies
                // field access without reflecting over the generated CLR type.
                var factory = new DefaultComplexTypeFactory();
                IComplexTypeFieldBuilder fields = factory
                    .Create(generation.NamespaceUris.GetString(dataTypeId.NamespaceIndex) ?? string.Empty,
                        dataTypeId.NamespaceIndex)
                    .AddStructuredType(new QualifiedName("Value", dataTypeId.NamespaceIndex), structureDefinition);
                fields.AddTypeIdAttribute(source.TypeId, source.BinaryEncodingId, source.XmlEncodingId);
                bool allowSubtypes = structureDefinition.StructureType is
                    StructureType.StructureWithSubtypedValues or StructureType.UnionWithSubtypedValues;
                for (int i = 0; i < structureDefinition.Fields.Count; i++)
                {
                    StructureField field = structureDefinition.Fields[i];
                    IType? fieldType = await LoadTypeAsync(
                        field.DataType, generation, cancellationToken).ConfigureAwait(false);
                    if (fieldType is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDataTypeIdUnknown, $"The type of field '{field.Name}' cannot be resolved.");
                    }
                    fields.AddField(field, fieldType, i + 1, allowSubtypes);
                }
                adapter = fields.CreateType().CreateInstance();
                StructuredValueDraft.CopyBody(source, adapter, generation.MessageContext);
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrent(generation);
            return StructuredValueDraft.ForStructure(
                dataTypeId, structureDefinition, source, adapter, generation.MessageContext,
                () => EnsureCurrent(generation));
        }
        finally
        {
            m_typeGate.Release();
        }
    }

    public void Refresh()
    {
        lock (m_gate)
        {
            m_generation = null;
        }
    }

    public void Dispose()
    {
        Refresh();
        m_typeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DataTypeDefinition? ReadDefinition(in DataValue value, IServiceMessageContext context)
    {
        if (value.StatusCode.CodeBits == StatusCodes.BadAttributeIdInvalid.CodeBits ||
            value.StatusCode.CodeBits == StatusCodes.BadNotSupported.CodeBits)
        {
            return null;
        }
        if (!StatusCode.IsGood(value.StatusCode))
        {
            throw new ServiceResultException(value.StatusCode);
        }
        if (value.WrappedValue.IsNull)
        {
            return null;
        }
        if (value.WrappedValue.TryGetValue(out ExtensionObject extension))
        {
            if (extension.IsNull)
            {
                return null;
            }
            if (extension.TryGetValue(out DataTypeDefinition? definition, context))
            {
                return definition;
            }
        }
        throw new ServiceResultException(
            StatusCodes.BadDecodingError, "DataTypeDefinition was not a decoded data-type definition.");
    }

    private async Task<IType?> LoadTypeAsync(
        NodeId dataTypeId,
        Generation generation,
        CancellationToken cancellationToken)
    {
        IType? known = FindType(dataTypeId, generation);
        if (known is not null)
        {
            return known;
        }
        generation.TypeSystem ??= (m_typeSystems ??
            new DefaultComplexTypeSystemFactory(generation.MessageContext.Telemetry)).Create(m_session);
        IType? loaded = await generation.TypeSystem.LoadTypeAsync(
            NodeId.ToExpandedNodeId(dataTypeId, generation.NamespaceUris),
            throwOnError: true, ct: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(generation);
        if (loaded is not null)
        {
            return loaded;
        }
        BuiltInType builtIn = await m_session.NodeCache.GetBuiltInTypeAsync(
            dataTypeId, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(generation);
        return builtIn is BuiltInType.Null or BuiltInType.ExtensionObject ? null : TypeInfo.GetSystemType(builtIn);
    }

    private IType? FindType(NodeId dataTypeId, Generation generation)
    {
        return TypeInfo.GetSystemType(new ExpandedNodeId(dataTypeId), m_session.Factory) ??
            TypeInfo.GetSystemType(NodeId.ToExpandedNodeId(dataTypeId, generation.NamespaceUris), m_session.Factory);
    }

    private Generation GetGeneration()
    {
        lock (m_gate)
        {
            if (m_generation is null || !m_generation.Matches(m_session))
            {
                m_generation = new Generation(m_session);
            }
            return m_generation;
        }
    }

    private void EnsureCurrent(Generation generation)
    {
        lock (m_gate)
        {
            if (!ReferenceEquals(m_generation, generation) || !generation.Matches(m_session))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The session or namespace metadata changed. Reload the value.");
            }
        }
    }

    private sealed class Generation
    {
        public Generation(ISession session)
        {
            SessionId = session.SessionId;
            MessageContext = session.MessageContext ?? throw new ServiceResultException(StatusCodes.BadNotConnected);
            NamespaceUris = session.NamespaceUris;
            m_namespaces = NamespaceUris.ToArray();
            m_serverUri = session.Endpoint.Server.ApplicationUri;
            m_endpointUrl = session.Endpoint.EndpointUrl;
        }

        public NodeId SessionId { get; }
        public IServiceMessageContext MessageContext { get; }
        public NamespaceTable NamespaceUris { get; }
        public Dictionary<NodeId, DataTypeDefinition?> Definitions { get; } = [];
        public ComplexTypeSystem? TypeSystem { get; set; }

        public bool Matches(ISession session)
        {
            return session.Connected && SessionId == session.SessionId &&
                ReferenceEquals(MessageContext, session.MessageContext) &&
                ReferenceEquals(NamespaceUris, session.NamespaceUris) &&
                string.Equals(m_serverUri, session.Endpoint.Server.ApplicationUri, StringComparison.Ordinal) &&
                string.Equals(m_endpointUrl, session.Endpoint.EndpointUrl, StringComparison.Ordinal) &&
                m_namespaces.AsSpan().SequenceEqual(session.NamespaceUris.ToArray());
        }

        private readonly string[] m_namespaces;
        private readonly string? m_serverUri;
        private readonly string? m_endpointUrl;
    }

    private readonly ISession m_session;
    private readonly IComplexTypeSystemFactory? m_typeSystems;
    private readonly System.Threading.Lock m_gate = new();
    private readonly SemaphoreSlim m_typeGate = new(1, 1);
    private Generation? m_generation;
}
