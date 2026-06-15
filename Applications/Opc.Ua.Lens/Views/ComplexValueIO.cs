/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Views;

/// <summary>
/// Shared helpers used by <see cref="ComplexValueEditor"/> and the dialogs
/// that host it.  Fetches the <c>DataTypeDefinition</c> attribute for a
/// DataType node and caches the result per <see cref="ManagedSession"/>
/// so that a single editor build doesn't replay the same Read repeatedly
/// when a structure has many fields of the same nested type.
/// </summary>
internal static class ComplexValueIO
{
    private static readonly ConditionalWeakTable<ManagedSession,
        ConcurrentDictionary<NodeId, CacheEntry>> s_cache = new();

    /// <summary>
    /// Resolves the <c>DataTypeDefinition</c> attribute of a DataType
    /// node.  Returns <c>null</c> when the server does not expose the
    /// attribute or returns a non-definition payload; callers fall back
    /// to the primitive editor in that case.  Successful lookups
    /// (including authoritative "definition is null") are cached for
    /// the lifetime of the session.
    /// </summary>
    public static async Task<DataTypeDefinition?> GetDataTypeDefinitionAsync(
        NodeId dataTypeId,
        ManagedSession session,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (dataTypeId.IsNull)
        {
            return null;
        }

        ConcurrentDictionary<NodeId, CacheEntry> cache = s_cache.GetValue(
            session, _ => new ConcurrentDictionary<NodeId, CacheEntry>());
        if (cache.TryGetValue(dataTypeId, out CacheEntry? hit))
        {
            return hit.Definition;
        }

        DataTypeDefinition? def = null;
        try
        {
            ArrayOf<ReadValueId> ids =
            [
                new ReadValueId { NodeId = dataTypeId, AttributeId = Attributes.DataTypeDefinition }
            ];
            ReadResponse resp = await session.ReadAsync(null, 0,
                TimestampsToReturn.Neither, ids, ct).ConfigureAwait(true);
            if (resp.Results.Count > 0 && !StatusCode.IsBad(resp.Results[0].StatusCode))
            {
                object? boxed = resp.Results[0].WrappedValue.AsBoxedObject();
                if (boxed is ExtensionObject eo
                    && eo.Body is DataTypeDefinition raw)
                {
                    def = raw;
                }
                else if (boxed is DataTypeDefinition direct)
                {
                    def = direct;
                }
            }
        }
        catch
        {
            // Servers that don't implement the attribute return Bad —
            // already handled by the IsBad check above; any transport
            // error simply leaves def null which the editor surfaces
            // as the "(complex type — opaque)" hint.
        }

        cache[dataTypeId] = new CacheEntry(def);
        return def;
    }

    /// <summary>
    /// Quick predicate: does this DataType resolve to something the
    /// complex-value editor can render?  Used by the host dialogs to
    /// decide whether to swap the primitive TextBox for the editor.
    /// </summary>
    public static async Task<bool> IsComplexAsync(
        NodeId dataTypeId,
        ManagedSession session,
        CancellationToken ct)
    {
        DataTypeDefinition? def = await GetDataTypeDefinitionAsync(
            dataTypeId, session, ct).ConfigureAwait(true);
        return def is StructureDefinition or EnumDefinition;
    }

    /// <summary>
    /// Materialises a default <see cref="Variant"/> for a primitive
    /// built-in scalar field — used by <see cref="ComplexValueEditor"/>
    /// to seed empty rows so that "(blank) → write" produces a typed
    /// zero instead of <c>Variant.Null</c>.
    /// </summary>
    public static Variant DefaultScalar(BuiltInType bi)
    {
        return bi switch
        {
            BuiltInType.Boolean => Variant.From(false),
            BuiltInType.SByte => Variant.From((sbyte)0),
            BuiltInType.Byte => Variant.From((byte)0),
            BuiltInType.Int16 => Variant.From((short)0),
            BuiltInType.UInt16 => Variant.From((ushort)0),
            BuiltInType.Int32 => Variant.From(0),
            BuiltInType.UInt32 => Variant.From(0u),
            BuiltInType.Int64 => Variant.From(0L),
            BuiltInType.UInt64 => Variant.From(0ul),
            BuiltInType.Float => Variant.From(0f),
            BuiltInType.Double => Variant.From(0.0),
            BuiltInType.String => Variant.From(string.Empty),
            BuiltInType.DateTime => Variant.From(new DateTimeUtc(DateTime.MinValue)),
            BuiltInType.Guid => Variant.From(new Uuid(Guid.Empty)),
            BuiltInType.LocalizedText => Variant.From(new LocalizedText(string.Empty)),
            BuiltInType.QualifiedName => Variant.From(new QualifiedName(string.Empty)),
            BuiltInType.NodeId => Variant.From(NodeId.Null),
            _ => Variant.Null
        };
    }

    /// <summary>
    /// Cached fetch result.  Stored as a wrapper so that an authoritative
    /// "definition is null" (server returned Bad / non-definition payload)
    /// is distinguishable from a cache miss.
    /// </summary>
    private sealed record CacheEntry(DataTypeDefinition? Definition);
}
