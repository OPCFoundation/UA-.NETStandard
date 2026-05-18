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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Outcome of a HistoryUpdate call.  Carries the overall operation
/// <see cref="StatusCode"/> plus the per-row operation results (if any).
/// Used by the UI to render a result label without throwing on
/// partial-success responses.
/// </summary>
internal sealed class HistoryUpdateOutcome
{
    /// <summary>Operation-level status (one per HistoryUpdateDetails entry).</summary>
    public StatusCode StatusCode { get; init; }

    /// <summary>Per-value operation results, when the server returned any.</summary>
    public IReadOnlyList<StatusCode> OperationResults { get; init; } = Array.Empty<StatusCode>();

    /// <summary>Convenience: true when no bad status was observed at any level.</summary>
    public bool IsGood
    {
        get
        {
            if (StatusCode.IsBad(StatusCode))
            {
                return false;
            }
            for (int i = 0; i < OperationResults.Count; i++)
            {
                if (StatusCode.IsBad(OperationResults[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Renders a short human-readable summary suitable for a status
    /// label, e.g. <c>"Good (1/1 ok)"</c> or
    /// <c>"BadHistoryOperationUnsupported"</c>.
    /// </summary>
    public string Summarise()
    {
        if (OperationResults.Count == 0)
        {
            return FormatStatus(StatusCode);
        }
        int ok = 0;
        int bad = 0;
        foreach (StatusCode s in OperationResults)
        {
            if (StatusCode.IsBad(s))
            {
                bad++;
            }
            else
            {
                ok++;
            }
        }
        if (bad == 0)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} ({1}/{2} ok)",
                FormatStatus(StatusCode), ok, OperationResults.Count);
        }
        var firstBad = OperationResults.First(StatusCode.IsBad);
        return string.Format(CultureInfo.InvariantCulture,
            "{0} ({1}/{2} ok, {3} bad — first: {4})",
            FormatStatus(StatusCode), ok, OperationResults.Count, bad, FormatStatus(firstBad));
    }

    private static string FormatStatus(StatusCode s) =>
        StatusCode.LookupSymbolicId(s.Code) is { Length: > 0 } sym
            ? sym
            : $"0x{s.Code:X8}";
}

/// <summary>
/// Thin façade over <see cref="ISession.HistoryUpdateAsync"/> for the
/// single-row and range operations exposed by the Historian tab
/// (Part 11 §6.8).  The legacy
/// <see cref="UpdateAsync(NodeId, PerformUpdateType, DateTime, Variant, StatusCode, CancellationToken)"/>
/// and <see cref="DeleteAsync"/> overloads throw on a bad status — the
/// newer op-specific methods return a <see cref="HistoryUpdateOutcome"/>
/// instead so the UI can surface per-row failures in a result label
/// without abandoning the rest of a batch.
/// </summary>
internal sealed class HistoryUpdater
{
    private readonly ISession m_session;

    public HistoryUpdater(ISession session)
    {
        m_session = session;
    }

    /// <summary>
    /// Performs an Insert / Replace / Update on a single historical
    /// timestamp using <see cref="UpdateDataDetails"/>.  Legacy overload
    /// — throws on a bad status.  Prefer <see cref="InsertAsync"/> /
    /// <see cref="ReplaceAsync"/> / <see cref="InsertReplaceAsync"/>
    /// which return an outcome instead.
    /// </summary>
    public async Task UpdateAsync(
        NodeId nodeId,
        PerformUpdateType action,
        DateTime timestamp,
        Variant value,
        StatusCode status,
        CancellationToken ct)
    {
        var dv = new DataValue
        {
            WrappedValue = value,
            StatusCode = status,
            SourceTimestamp = timestamp,
            ServerTimestamp = timestamp
        };
        HistoryUpdateOutcome outcome = await UpdateDataAsync(nodeId, action, dv, ct).ConfigureAwait(false);
        ThrowIfFailed(outcome);
    }

    /// <summary>
    /// Deletes a single historical timestamp using
    /// <see cref="DeleteAtTimeDetails"/>.  Legacy overload — throws on
    /// a bad status.  Prefer <see cref="DeleteAtTimesAsync"/> or
    /// <see cref="RemoveAsync"/> which return an outcome instead.
    /// </summary>
    public async Task DeleteAsync(
        NodeId nodeId,
        DateTime timestamp,
        CancellationToken ct)
    {
        HistoryUpdateOutcome outcome = await DeleteAtTimesAsync(nodeId, new[] { timestamp }, ct).ConfigureAwait(false);
        ThrowIfFailed(outcome);
    }

    /// <summary>
    /// <c>PerformUpdateType.Insert</c> — inserts a new
    /// <see cref="DataValue"/> at a previously-empty timestamp.
    /// </summary>
    public Task<HistoryUpdateOutcome> InsertAsync(NodeId nodeId, DataValue dv, CancellationToken ct)
        => UpdateDataAsync(nodeId, PerformUpdateType.Insert, dv, ct);

    /// <summary>
    /// <c>PerformUpdateType.Update</c> — inserts when the timestamp is
    /// new and replaces an existing entry otherwise.
    /// </summary>
    public Task<HistoryUpdateOutcome> InsertReplaceAsync(NodeId nodeId, DataValue dv, CancellationToken ct)
        => UpdateDataAsync(nodeId, PerformUpdateType.Update, dv, ct);

    /// <summary>
    /// <c>PerformUpdateType.Replace</c> — replaces an existing
    /// historical entry; fails if no entry exists at that timestamp.
    /// </summary>
    public Task<HistoryUpdateOutcome> ReplaceAsync(NodeId nodeId, DataValue dv, CancellationToken ct)
        => UpdateDataAsync(nodeId, PerformUpdateType.Replace, dv, ct);

    /// <summary>
    /// Removes the single raw history entry at the given timestamp by
    /// issuing a <see cref="DeleteRawModifiedDetails"/> with
    /// <c>StartTime == EndTime == timestamp</c>.
    /// </summary>
    public Task<HistoryUpdateOutcome> RemoveAsync(NodeId nodeId, DateTime timestamp, CancellationToken ct)
        => DeleteRawRangeAsync(nodeId, timestamp, timestamp, isDeleteModified: false, ct);

    /// <summary>
    /// Deletes every raw historical entry in <c>[start, end]</c> using
    /// <see cref="DeleteRawModifiedDetails"/> with
    /// <c>IsDeleteModified = false</c>.
    /// </summary>
    public Task<HistoryUpdateOutcome> DeleteRawAsync(
        NodeId nodeId, DateTime start, DateTime end, CancellationToken ct)
        => DeleteRawRangeAsync(nodeId, start, end, isDeleteModified: false, ct);

    /// <summary>
    /// Deletes every <i>modified</i> (audit-history) entry in
    /// <c>[start, end]</c> using <see cref="DeleteRawModifiedDetails"/>
    /// with <c>IsDeleteModified = true</c>.
    /// </summary>
    public Task<HistoryUpdateOutcome> DeleteModifiedAsync(
        NodeId nodeId, DateTime start, DateTime end, CancellationToken ct)
        => DeleteRawRangeAsync(nodeId, start, end, isDeleteModified: true, ct);

    /// <summary>
    /// Deletes the entries at the given timestamps using
    /// <see cref="DeleteAtTimeDetails"/>.  Returns per-row results.
    /// </summary>
    public Task<HistoryUpdateOutcome> DeleteAtTimesAsync(
        NodeId nodeId, IReadOnlyList<DateTime> timestamps, CancellationToken ct)
    {
        var times = new DateTimeUtc[timestamps.Count];
        for (int i = 0; i < timestamps.Count; i++)
        {
            DateTime t = timestamps[i].Kind == DateTimeKind.Utc
                ? timestamps[i]
                : timestamps[i].ToUniversalTime();
            times[i] = t;
        }
        var details = new DeleteAtTimeDetails
        {
            NodeId = nodeId,
            ReqTimes = times
        };
        return HistoryUpdateAsync(new ExtensionObject(details), ct);
    }

    /// <summary>
    /// Edits the <see cref="Opc.Ua.Annotation"/> associated with the
    /// raw history entry at <paramref name="sourceTimestamp"/>.  The
    /// historizing variable's <c>Annotations</c> property is resolved
    /// via <c>TranslateBrowsePathsToNodeIds</c> and the annotation is
    /// written using <see cref="UpdateDataDetails"/> with
    /// <see cref="PerformUpdateType.Update"/> (insert-or-replace).
    /// Returns <see cref="HistoryUpdateOutcome"/> with
    /// <see cref="StatusCodes.BadNodeIdUnknown"/> if the server does
    /// not expose the <c>Annotations</c> property on this node.
    /// </summary>
    public async Task<HistoryUpdateOutcome> UpdateAnnotationAsync(
        NodeId variableNodeId,
        DateTime sourceTimestamp,
        Annotation annotation,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        NodeId? annotationsNodeId = await ResolveAnnotationsNodeIdAsync(variableNodeId, ct).ConfigureAwait(false);
        if (!annotationsNodeId.HasValue || annotationsNodeId.Value.IsNull)
        {
            return new HistoryUpdateOutcome
            {
                StatusCode = StatusCodes.BadNodeIdUnknown
            };
        }
        NodeId annNode = annotationsNodeId.Value;
        DateTime ts = sourceTimestamp.Kind == DateTimeKind.Utc
            ? sourceTimestamp
            : sourceTimestamp.ToUniversalTime();
        var dv = new DataValue
        {
            WrappedValue = new Variant(new ExtensionObject(annotation)),
            StatusCode = StatusCodes.Good,
            SourceTimestamp = ts,
            ServerTimestamp = ts
        };
        return await UpdateDataAsync(
            annNode, PerformUpdateType.Update, dv, ct).ConfigureAwait(false);
    }

    private Task<HistoryUpdateOutcome> UpdateDataAsync(
        NodeId nodeId, PerformUpdateType action, DataValue dv, CancellationToken ct)
    {
        var details = new UpdateDataDetails
        {
            NodeId = nodeId,
            PerformInsertReplace = action,
            UpdateValues = new DataValue[] { dv }
        };
        return HistoryUpdateAsync(new ExtensionObject(details), ct);
    }

    private Task<HistoryUpdateOutcome> DeleteRawRangeAsync(
        NodeId nodeId,
        DateTime start,
        DateTime end,
        bool isDeleteModified,
        CancellationToken ct)
    {
        DateTime startUtc = start.Kind == DateTimeKind.Utc ? start : start.ToUniversalTime();
        DateTime endUtc = end.Kind == DateTimeKind.Utc ? end : end.ToUniversalTime();
        var details = new DeleteRawModifiedDetails
        {
            NodeId = nodeId,
            IsDeleteModified = isDeleteModified,
            StartTime = startUtc,
            EndTime = endUtc
        };
        return HistoryUpdateAsync(new ExtensionObject(details), ct);
    }

    private async Task<HistoryUpdateOutcome> HistoryUpdateAsync(
        ExtensionObject details, CancellationToken ct)
    {
        var detailsArray = new ExtensionObject[] { details };
        HistoryUpdateResponse response = await m_session.HistoryUpdateAsync(
            requestHeader: null,
            historyUpdateDetails: detailsArray,
            ct: ct).ConfigureAwait(false);
        if (response.Results.Count == 0)
        {
            return new HistoryUpdateOutcome
            {
                StatusCode = StatusCodes.Good
            };
        }
        HistoryUpdateResult r = response.Results[0];
        ArrayOf<StatusCode> opList = r.OperationResults;
        var ops = new List<StatusCode>(opList.Count);
        if (opList.Count > 0)
        {
            foreach (StatusCode s in opList)
            {
                ops.Add(s);
            }
        }
        return new HistoryUpdateOutcome
        {
            StatusCode = r.StatusCode,
            OperationResults = ops
        };
    }

    private async Task<NodeId?> ResolveAnnotationsNodeIdAsync(
        NodeId variableNodeId, CancellationToken ct)
    {
        try
        {
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IsInverse = false,
                IncludeSubtypes = false,
                TargetName = new QualifiedName(BrowseNames.Annotations)
            };
            var browsePath = new BrowsePath
            {
                StartingNode = variableNodeId,
                RelativePath = new RelativePath { Elements = [element] }
            };
            var browsePaths = new List<BrowsePath> { browsePath }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse resp = await m_session
                .TranslateBrowsePathsToNodeIdsAsync(null, browsePaths, ct)
                .ConfigureAwait(false);
            if (resp.Results.Count == 0)
            {
                return null;
            }
            BrowsePathResult r = resp.Results[0];
            if (StatusCode.IsBad(r.StatusCode) || r.Targets is not { Count: > 0 } targets)
            {
                return null;
            }
            NodeId mapped = ExpandedNodeId.ToNodeId(targets[0].TargetId, m_session.NamespaceUris);
            return mapped.IsNull ? null : mapped;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ThrowIfFailed(HistoryUpdateOutcome outcome)
    {
        if (StatusCode.IsBad(outcome.StatusCode))
        {
            throw new ServiceResultException(
                outcome.StatusCode,
                string.Format(CultureInfo.InvariantCulture,
                    "HistoryUpdate returned {0}.", outcome.StatusCode));
        }
        foreach (StatusCode op in outcome.OperationResults)
        {
            if (StatusCode.IsBad(op))
            {
                throw new ServiceResultException(
                    op,
                    string.Format(CultureInfo.InvariantCulture,
                        "HistoryUpdate per-operation result: {0}.", op));
            }
        }
    }
}
