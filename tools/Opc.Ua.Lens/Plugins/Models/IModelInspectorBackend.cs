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
