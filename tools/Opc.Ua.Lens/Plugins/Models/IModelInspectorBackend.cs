/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Schema;
using UaLens.StructuredValues;

namespace UaLens.Plugins.Models;

internal enum ModelReadState
{
    NotRead,
    Available,
    Unavailable,
    Canceled,
    Denied,
    Failed
}

/// <summary>
/// Transient read evidence. This is never used as a workspace-state DTO.
/// </summary>
internal sealed record ModelInspection(
    long Generation,
    NodeId SessionId,
    ArrayOf<string> NamespaceUris,
    string PortableTarget,
    NodeId NodeId,
    string Name,
    NodeClass NodeClass,
    NodeId DataType,
    QualifiedName DataTypeName,
    int ValueRank,
    DataValue Value,
    bool CanWrite,
    bool CanCall,
    DataTypeDefinition? Definition)
{
    public ArrayOf<uint> ArrayDimensions { get; init; }
}

internal sealed record ModelSchemaPreview(bool Available, string Text, string MediaType, string Extension);

/// <summary>
/// Replaceable session backend. Binding is local-only and never reads or mutates
/// the server; the document drains its canceled operations before rebinding.
/// </summary>
internal interface IModelInspectorBackend : IAsyncDisposable
{
    ISession? Session { get; }
    IStructuredValueService? Values { get; }
    bool IsBound { get; }

    Task BindAsync(ISession? session, CancellationToken cancellationToken);

    Task<ModelInspection> ReadAsync(string portableTarget, bool refreshMetadata, CancellationToken cancellationToken);

    Task<ModelSchemaPreview> CreateSchemaAsync(
        ModelInspection inspection, UaSchemaFormat format, CancellationToken cancellationToken);

    Task<StatusCode> WriteAsync(ModelInspection inspection, Variant value, CancellationToken cancellationToken);

    void EnsureCurrent(ModelInspection inspection);
}
