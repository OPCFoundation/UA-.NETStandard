// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.StructuredValues;

/// <summary>
/// Resolves schema metadata and opens isolated structured-value edits. Shared by
/// value dialogs and model inspection; it has no dependency on a desktop control.
/// </summary>
internal interface IStructuredValueService
{
    /// <summary>
    /// The current session's encoding context.
    /// </summary>
    IServiceMessageContext MessageContext { get; }

    /// <summary>
    /// Returns a definition, or null only when its absence is authoritative.
    /// Cancellation, denied access, malformed replies and service failures propagate.
    /// </summary>
    Task<DataTypeDefinition?> ResolveAsync(NodeId dataTypeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a draft using native generated codecs or the default complex-type adapters.
    /// Neither opening nor committing a draft changes the supplied value.
    /// </summary>
    Task<StructuredValueDraft> OpenAsync(
        NodeId dataTypeId,
        DataTypeDefinition definition,
        Variant value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a typed array or matrix draft with the declared rank and dimension
    /// bounds. An untyped null starts as a typed null, not an empty array.
    /// </summary>
    Task<StructuredArrayDraft> OpenArrayAsync(
        NodeId elementDataTypeId,
        int valueRank,
        ArrayOf<uint> declaredDimensions,
        Variant value,
        bool isStructureField = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards metadata and invalidates outstanding lookups and drafts.
    /// </summary>
    void Refresh();
}
