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
using Opc.Ua.Schema;
using Opc.Ua.Schema.Bsd;
using Opc.Ua.Schema.Json;
using Opc.Ua.Schema.Xsd;
using UaLens.StructuredValues;

namespace UaLens.Plugins.Models;

/// <summary>
/// Explicit Read/Write and schema operations using the existing stack services.
/// It never owns or disconnects the primary session.
/// </summary>
internal sealed class SessionModelInspectorBackend : IModelInspectorBackend
{
    public SessionModelInspectorBackend(
        IStructuredValueService? values = null,
        ISchemaProvider? schemas = null)
    {
        m_injectedValues = values;
        m_schemas = schemas;
    }

    public ISession? Session { get; private set; }
    public IStructuredValueService? Values { get; private set; }
    public bool IsBound => Session?.Connected == true && Values is not null;

    public Task BindAsync(ISession? session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        m_ownedValues?.Dispose();
        m_ownedValues = null;
        Values = null;
        Session = null;
        m_generation++;
        if (session is not null)
        {
            if (m_injectedValues is not null &&
                !ReferenceEquals(m_injectedValues.MessageContext, session.MessageContext))
            {
                throw new InvalidOperationException(
                    "The injected structured-value service belongs to another session.");
            }
            Session = session;
            Values = m_injectedValues ?? (m_ownedValues = new SessionStructuredValueService(session));
        }
        return Task.CompletedTask;
    }

    public async Task<ModelInspection> ReadAsync(
        string portableTarget,
        bool refreshMetadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ISession session = RequireSession();
        IStructuredValueService values = Values!;
        long generation = m_generation;
        NodeId sessionId = session.SessionId;
        ArrayOf<string> namespaces = session.NamespaceUris.ToArray();
        NodeId nodeId = ModelInspectorStateCodec.ResolveTarget(portableTarget, session.NamespaceUris);
        if (refreshMetadata)
        {
            values.Refresh();
        }
        ArrayOf<DataValue> basic = await ReadAttributesAsync(
            session, nodeId, [Attributes.NodeClass, Attributes.DisplayName, Attributes.BrowseName], cancellationToken)
            .ConfigureAwait(false);
        if (!basic[0].WrappedValue.TryGetValue(out int rawClass) ||
            !basic[1].WrappedValue.TryGetValue(out LocalizedText displayName) ||
            !basic[2].WrappedValue.TryGetValue(out QualifiedName browseName))
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid node identity attributes.");
        }
        var nodeClass = (NodeClass)rawClass;
        if (nodeClass is not (NodeClass.Variable or NodeClass.DataType or NodeClass.Method))
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported, "Models inspects Variables, DataTypes and Methods.");
        }
        NodeId dataType = NodeId.Null;
        QualifiedName dataTypeName = QualifiedName.Null;
        int rank = ValueRanks.Scalar;
        ArrayOf<uint> dimensions = default;
        DataValue value = default;
        bool canWrite = false;
        bool canCall = false;
        if (nodeClass == NodeClass.Variable)
        {
            ArrayOf<DataValue> attributes = await ReadAttributesAsync(
                session, nodeId,
                [Attributes.Value, Attributes.DataType, Attributes.ValueRank, Attributes.UserAccessLevel],
                cancellationToken).ConfigureAwait(false);
            if (!attributes[1].WrappedValue.TryGetValue(out dataType) ||
                !attributes[2].WrappedValue.TryGetValue(out rank) ||
                !attributes[3].WrappedValue.TryGetValue(out byte access))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid variable metadata.");
            }
            value = new DataValue(attributes[0].WrappedValue.Copy()).WithStatus(attributes[0].StatusCode);
            canWrite = (access & AccessLevels.CurrentWrite) != 0;
            if (rank != ValueRanks.Scalar)
            {
                dimensions = await ReadArrayDimensionsAsync(session, nodeId, cancellationToken).ConfigureAwait(false);
            }
        }
        else if (nodeClass == NodeClass.DataType)
        {
            dataType = nodeId;
            dataTypeName = browseName;
        }
        else
        {
            ArrayOf<DataValue> executable = await ReadAttributesAsync(
                session, nodeId, [Attributes.Executable, Attributes.UserExecutable], cancellationToken)
                .ConfigureAwait(false);
            if (!executable[0].WrappedValue.TryGetValue(out bool enabled) ||
                !executable[1].WrappedValue.TryGetValue(out bool permitted))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid method permission metadata.");
            }
            canCall = enabled && permitted;
        }
        DataTypeDefinition? definition = dataType.IsNull
            ? null
            : await values.ResolveAsync(dataType, cancellationToken).ConfigureAwait(false);
        if (!dataType.IsNull && dataTypeName.IsNull)
        {
            dataTypeName = await ReadBrowseNameAsync(session, dataType, cancellationToken).ConfigureAwait(false);
        }
        var inspection = new ModelInspection(
            generation, sessionId, namespaces, portableTarget, nodeId,
            displayName.Text ?? browseName.Name ?? nodeId.ToString(),
            nodeClass, dataType, dataTypeName, rank, value, canWrite, canCall, CoreUtils.Clone(definition))
        {
            ArrayDimensions = dimensions
        };
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(inspection);
        return inspection;
    }

    public async Task<ModelSchemaPreview> CreateSchemaAsync(
        ModelInspection inspection,
        UaSchemaFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(inspection);
        if (inspection.Definition is null || inspection.DataType.IsNull)
        {
            return new ModelSchemaPreview(
                false, "No authoritative DataTypeDefinition is available.", string.Empty, string.Empty);
        }
        ISession session = RequireSession();
        var registry = new DataTypeDefinitionRegistry();
        var generated = new EncodeableFactoryDefinitionSource(session.Factory, session.NamespaceUris);
        var resolver = new CompositeDataTypeDefinitionResolver([registry, generated]);
        var pending = new Queue<(NodeId Id, QualifiedName Name, DataTypeDefinition Definition)>();
        var visited = new HashSet<NodeId>();
        var scheduled = new HashSet<NodeId> { inspection.DataType };
        pending.Enqueue((inspection.DataType, inspection.DataTypeName, inspection.Definition));
        while (pending.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(item.Id))
            {
                continue;
            }
            if (visited.Count > 256)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "The schema dependency graph exceeds 256 types.");
            }
            registry.Add(Describe(item.Id, item.Name, item.Definition, session.NamespaceUris));
            if (item.Definition is not StructureDefinition structure)
            {
                continue;
            }
            var dependencies = new List<(NodeId Id, string Name)>();
            foreach (StructureField field in structure.Fields)
            {
                dependencies.Add((field.DataType, field.Name ?? "(unnamed)"));
            }
            if (!structure.BaseDataType.IsNull)
            {
                dependencies.Add((structure.BaseDataType, "base type"));
            }
            foreach ((NodeId dependency, string fieldName) in dependencies)
            {
                if (SchemaTypeInfo.GetFieldEncodingType(dependency) != BuiltInType.Null || !scheduled.Add(dependency))
                {
                    continue;
                }
                DataTypeDefinition? nested = await Values!.ResolveAsync(dependency, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (nested is null)
                {
                    // The standard generators permit unresolved types as "any".
                    // An inspector must not export that as a complete typed schema.
                    return new ModelSchemaPreview(false,
                        $"Schema unavailable: '{fieldName}' has no definition for {dependency}.",
                        string.Empty, string.Empty);
                }
                QualifiedName name = await ReadBrowseNameAsync(session, dependency, cancellationToken)
                    .ConfigureAwait(false);
                pending.Enqueue((dependency, name, nested));
            }
        }
        EnsureCurrent(inspection);
        ISchemaProvider provider = m_schemas ?? new DefaultSchemaProvider(
            resolver, [new JsonSchemaGenerator(), new BsdSchemaGenerator(), new XsdSchemaGenerator()]);
        IUaSchema schema = provider.CreateSchema(
            Describe(inspection.DataType, inspection.DataTypeName, inspection.Definition, session.NamespaceUris),
            format);
        string text = schema.ToSchemaString();
        if (text.Length > 4 * 1024 * 1024)
        {
            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, "Schema preview exceeds 4 MiB.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(inspection);
        return new ModelSchemaPreview(true, text, schema.MediaType, format switch
        {
            UaSchemaFormat.Xsd => "xsd",
            UaSchemaFormat.Bsd => "bsd",
            _ => "json"
        });
    }

    public async Task<StatusCode> WriteAsync(
        ModelInspection inspection,
        Variant value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        EnsureCurrent(inspection);
        if (inspection.NodeClass != NodeClass.Variable || !inspection.CanWrite)
        {
            throw new ServiceResultException(StatusCodes.BadNotWritable, "The read target is not writable.");
        }
        ISession session = RequireSession();
        ArrayOf<DataValue> current = await ReadAttributesAsync(
            session, inspection.NodeId, [Attributes.DataType, Attributes.ValueRank, Attributes.UserAccessLevel],
            cancellationToken).ConfigureAwait(false);
        if (!current[0].WrappedValue.TryGetValue(out NodeId dataType) ||
            !current[1].WrappedValue.TryGetValue(out int rank) ||
            !current[2].WrappedValue.TryGetValue(out byte access) ||
            dataType != inspection.DataType || rank != inspection.ValueRank)
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Variable metadata changed; read it again.");
        }
        if ((access & AccessLevels.CurrentWrite) == 0)
        {
            throw new ServiceResultException(StatusCodes.BadNotWritable);
        }
        if (rank != ValueRanks.Scalar)
        {
            ArrayOf<uint> dimensions = await ReadArrayDimensionsAsync(session, inspection.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!dimensions.Span.SequenceEqual(inspection.ArrayDimensions.Span))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "Array dimensions changed; read the variable again.");
            }
            if (StructuredArrayValue.RequiresEditor(rank, value) && !value.IsNull)
            {
                StructuredArrayValue.Read(value, session.MessageContext)
                    .Validate(rank, dimensions, session.MessageContext);
            }
        }
        TypeInfo actual = TypeInfo.IsInstanceOfDataType(
            value, dataType, rank, session.NamespaceUris, session.TypeTree);
        if (actual.IsUnknown)
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "The prepared value does not match the target.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrent(inspection);
        WriteResponse response = await session.WriteAsync(null,
            [
                new WriteValue
                {
                    NodeId = inspection.NodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(value.Copy())
                }
            ],
            cancellationToken).ConfigureAwait(false);
        CheckResponse(response.ResponseHeader.ServiceResult, response.Results.Count, 1);
        if (!StatusCode.IsGood(response.Results[0]))
        {
            throw new ServiceResultException(response.Results[0]);
        }
        return response.Results[0];
    }

    public void EnsureCurrent(ModelInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        ISession session = RequireSession();
        if (inspection.Generation != m_generation || inspection.SessionId != session.SessionId ||
            !inspection.NamespaceUris.Span.SequenceEqual(session.NamespaceUris.ToArray()))
        {
            throw new ServiceResultException(
                StatusCodes.BadInvalidState, "The session/namespace generation changed. Read the target again.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await BindAsync(null, CancellationToken.None).ConfigureAwait(false);
    }

    private ISession RequireSession()
    {
        return IsBound ? Session! : throw new ServiceResultException(StatusCodes.BadNotConnected);
    }

    private static UaTypeDescription Describe(
        NodeId id, QualifiedName name, DataTypeDefinition definition, NamespaceTable namespaceUris)
    {
        return new UaTypeDescription(new ExpandedNodeId(id), name, definition,
            namespaceUris.GetString(id.NamespaceIndex));
    }

    private static async Task<ArrayOf<uint>> ReadArrayDimensionsAsync(
        ISession session, NodeId nodeId, CancellationToken cancellationToken)
    {
        ArrayOf<DataValue> attributes = await ReadAttributesAsync(
            session, nodeId, [Attributes.ArrayDimensions], cancellationToken).ConfigureAwait(false);
        if (!attributes[0].WrappedValue.TryGetValue(out ArrayOf<uint> dimensions))
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Invalid ArrayDimensions metadata.");
        }
        return CoreUtils.Clone(dimensions);
    }

    private static async Task<QualifiedName> ReadBrowseNameAsync(
        ISession session, NodeId id, CancellationToken cancellationToken)
    {
        ArrayOf<DataValue> attributes = await ReadAttributesAsync(
            session, id, [Attributes.BrowseName], cancellationToken).ConfigureAwait(false);
        return attributes[0].WrappedValue.TryGetValue(out QualifiedName name)
            ? name
            : throw new ServiceResultException(StatusCodes.BadDecodingError, "The DataType BrowseName is invalid.");
    }

    private static async Task<ArrayOf<DataValue>> ReadAttributesAsync(
        ISession session, NodeId nodeId, ArrayOf<uint> attributes, CancellationToken cancellationToken)
    {
        ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
            attributes.ConvertAll(attribute => new ReadValueId { NodeId = nodeId, AttributeId = attribute }),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        CheckResponse(response.ResponseHeader.ServiceResult, response.Results.Count, attributes.Count);
        foreach (DataValue value in response.Results)
        {
            if (!StatusCode.IsGood(value.StatusCode))
            {
                throw new ServiceResultException(value.StatusCode);
            }
        }
        return response.Results;
    }

    private static void CheckResponse(StatusCode status, int actual, int expected)
    {
        if (!StatusCode.IsGood(status))
        {
            throw new ServiceResultException(status);
        }
        if (actual != expected)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "Unexpected service result count.");
        }
    }

    private readonly IStructuredValueService? m_injectedValues;
    private readonly ISchemaProvider? m_schemas;
    private SessionStructuredValueService? m_ownedValues;
    private long m_generation;
}
