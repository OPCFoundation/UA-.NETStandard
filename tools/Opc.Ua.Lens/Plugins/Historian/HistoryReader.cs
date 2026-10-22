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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Plugins.Historian;

/// <summary>
/// Thin façade over <see cref="ISession.HistoryReadAsync"/> for the three
/// flavours of history read used by the Historian tab.  Each method
/// loops on the returned continuation point until the server signals
/// the end of the dataset (CP is null/empty).
/// </summary>
/// <remarks>
/// On failure or cancellation we issue one final HistoryRead with
/// <c>releaseContinuationPoints: true</c> so any outstanding server-side
/// cursor is freed.  Failures of that release call are intentionally
/// swallowed to preserve the original outcome. Cleanup has an independent
/// five-second cancellation deadline.
/// </remarks>
internal sealed class HistoryReader
{
    private readonly ISession m_session;
    private readonly TimeSpan m_cleanupTimeout;

    public HistoryReader(ISession session, TimeSpan? cleanupTimeout = null)
    {
        m_session = session ?? throw new ArgumentNullException(nameof(session));
        m_cleanupTimeout = cleanupTimeout ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Reads raw or modified history.  See Part 11 §6.4.3.</summary>
    public Task<List<HistoryRow>> ReadRawAsync(
        NodeId nodeId,
        DateTime startUtc,
        DateTime endUtc,
        bool returnBounds,
        bool isReadModified,
        uint numValuesPerNode,
        CancellationToken ct)
    {
        var details = new ReadRawModifiedDetails
        {
            StartTime = startUtc,
            EndTime = endUtc,
            NumValuesPerNode = numValuesPerNode,
            IsReadModified = isReadModified,
            ReturnBounds = returnBounds
        };
        return ReadLoopAsync(nodeId, new ExtensionObject(details), ct);
    }

    /// <summary>Reads aggregated (processed) history.  See Part 11 §6.4.4.</summary>
    public Task<List<HistoryRow>> ReadProcessedAsync(
        NodeId nodeId,
        NodeId aggregateType,
        DateTime startUtc,
        DateTime endUtc,
        double processingIntervalMs,
        CancellationToken ct)
    {
        var aggregateArray = new NodeId[] { aggregateType };
        var details = new ReadProcessedDetails
        {
            StartTime = startUtc,
            EndTime = endUtc,
            ProcessingInterval = processingIntervalMs,
            AggregateType = aggregateArray,
            AggregateConfiguration = new AggregateConfiguration()
        };
        return ReadLoopAsync(nodeId, new ExtensionObject(details), ct);
    }

    /// <summary>Reads values at specified timestamps.  See Part 11 §6.4.5.</summary>
    public Task<List<HistoryRow>> ReadAtTimeAsync(
        NodeId nodeId,
        IReadOnlyList<DateTime> reqTimes,
        CancellationToken ct)
    {
        var timesArray = new DateTimeUtc[reqTimes.Count];
        for (int i = 0; i < reqTimes.Count; i++)
        {
            timesArray[i] = reqTimes[i];
        }
        var details = new ReadAtTimeDetails
        {
            ReqTimes = timesArray,
            UseSimpleBounds = true
        };
        return ReadLoopAsync(nodeId, new ExtensionObject(details), ct);
    }

    /// <summary>
    /// Best-effort: resolves the standard <c>Annotations</c> property of
    /// the historizing variable (Part 11 §5.4.5) and, when present, reads
    /// the annotation history covering the source timestamps of the given
    /// rows.  Each <see cref="HistoryRow.Annotation"/> is populated with
    /// the first annotation whose <see cref="Annotation.AnnotationTime"/>
    /// matches the row's <see cref="HistoryRow.SourceTimestamp"/> (UTC,
    /// millisecond precision).  Servers that do not expose the
    /// <c>Annotations</c> property or that fail the secondary read are
    /// skipped — annotations are optional. Cancellation is not an optional-feature
    /// failure and always propagates to the caller.
    /// </summary>
    public async Task AttachAnnotationsAsync(
        NodeId variableNodeId,
        IReadOnlyList<HistoryRow> rows,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (variableNodeId.IsNull || rows.Count == 0)
        {
            return;
        }
        NodeId annotationsNodeId = await ResolveAnnotationsNodeIdAsync(variableNodeId, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (annotationsNodeId.IsNull)
        {
            return;
        }
        NodeId annNode = annotationsNodeId;
        DateTime minTs = DateTime.MaxValue;
        DateTime maxTs = DateTime.MinValue;
        foreach (HistoryRow r in rows)
        {
            DateTime ts = r.SourceTimestamp.Kind == DateTimeKind.Utc
                ? r.SourceTimestamp
                : r.SourceTimestamp.ToUniversalTime();
            if (ts < minTs)
            {
                minTs = ts;
            }
            if (ts > maxTs)
            {
                maxTs = ts;
            }
        }
        if (minTs == DateTime.MaxValue)
        {
            return;
        }
        // Pad the range by one millisecond on each side so servers that
        // treat StartTime/EndTime as exclusive still return the bounding
        // annotations.
        DateTime start = minTs.AddMilliseconds(-1);
        DateTime end = maxTs.AddMilliseconds(1);
        List<DataValue> annotationValues;
        try
        {
            annotationValues = await ReadAnnotationDataValuesAsync(annNode, start, end, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Best-effort — server doesn't support annotations or read
            // failed for an unrelated reason.  Annotation column simply
            // stays empty for this batch.
            ct.ThrowIfCancellationRequested();
            return;
        }
        ct.ThrowIfCancellationRequested();
        if (annotationValues.Count == 0)
        {
            return;
        }
        // Build a lookup keyed by 100ns-truncated source ticks so an
        // ordinary equality match suffices.  Annotations whose
        // AnnotationTime aligns with a known row are projected onto the
        // row; orphan annotations are ignored.
        var byTick = new Dictionary<long, HistoryRow>(rows.Count);
        foreach (HistoryRow r in rows)
        {
            DateTime ts = r.SourceTimestamp.Kind == DateTimeKind.Utc
                ? r.SourceTimestamp
                : r.SourceTimestamp.ToUniversalTime();
            byTick[ts.Ticks] = r;
        }
        foreach (DataValue dv in annotationValues)
        {
            if (dv.WrappedValue.IsNull)
            {
                continue;
            }
            Annotation? ann = ExtractAnnotation(dv.WrappedValue);
            if (ann is null)
            {
                continue;
            }
            // The DataValue's SourceTimestamp identifies the *data point*
            // being annotated (per Part 11 §5.4.5); the inner
            // Annotation.AnnotationTime is when the annotation itself
            // was created, which is not what we want to match on.
            DateTime key = (DateTime)dv.SourceTimestamp;
            if (key.Kind != DateTimeKind.Utc)
            {
                key = key.ToUniversalTime();
            }
            if (byTick.TryGetValue(key.Ticks, out HistoryRow? matched))
            {
                matched.Annotation = ann;
            }
        }
    }

    private async Task<NodeId> ResolveAnnotationsNodeIdAsync(
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
                return NodeId.Null;
            }
            BrowsePathResult r = resp.Results[0];
            if (StatusCode.IsBad(r.StatusCode) || r.Targets is not { Count: > 0 } targets)
            {
                return NodeId.Null;
            }
            NodeId mapped = ExpandedNodeId.ToNodeId(targets[0].TargetId, m_session.NamespaceUris);
            return mapped;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ct.ThrowIfCancellationRequested();
            return NodeId.Null;
        }
    }

    private async Task<List<DataValue>> ReadAnnotationDataValuesAsync(
        NodeId annotationsNodeId,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken ct)
    {
        var details = new ReadRawModifiedDetails
        {
            StartTime = startUtc,
            EndTime = endUtc,
            NumValuesPerNode = 0,
            IsReadModified = false,
            ReturnBounds = false
        };
        var detailsObject = new ExtensionObject(details);
        var results = new List<DataValue>();
        await foreach (HistoryReadResult result in ReadPagesAsync(
            annotationsNodeId, detailsObject, TimestampsToReturn.Source, ct).ConfigureAwait(false))
        {
            if (result.HistoryData.TryGetValue(out HistoryData? hd) && hd is not null)
            {
                foreach (DataValue dv in hd.DataValues)
                {
                    results.Add(dv);
                }
            }
        }
        return results;
    }

    private static Annotation? ExtractAnnotation(Variant v)
    {
        if (v.TryGetValue(out ExtensionObject extension) &&
            extension.TryGetValue(out Annotation? annotation))
        {
            return annotation;
        }
        if (v.TryGetValue(out ArrayOf<ExtensionObject> extensions))
        {
            foreach (ExtensionObject item in extensions)
            {
                if (item.TryGetValue(out Annotation? match) && match is not null)
                {
                    return match;
                }
            }
        }
        return null;
    }

    private async Task<List<HistoryRow>> ReadLoopAsync(
        NodeId nodeId,
        ExtensionObject historyReadDetails,
        CancellationToken ct)
    {
        var rows = new List<HistoryRow>();
        await foreach (HistoryReadResult result in ReadPagesAsync(
            nodeId, historyReadDetails, TimestampsToReturn.Both, ct).ConfigureAwait(false))
        {
            ArrayOf<DataValue> values = default;
            if (result.HistoryData.TryGetValue(out HistoryModifiedData? modified) && modified is not null)
            {
                values = modified.DataValues;
            }
            else if (result.HistoryData.TryGetValue(out HistoryData? data) && data is not null)
            {
                values = data.DataValues;
            }
            foreach (DataValue value in values)
            {
                rows.Add(new HistoryRow(
                    (DateTime)value.SourceTimestamp, (DateTime)value.ServerTimestamp,
                    value.WrappedValue, value.StatusCode));
            }
        }
        return rows;
    }

    private async IAsyncEnumerable<HistoryReadResult> ReadPagesAsync(
        NodeId nodeId,
        ExtensionObject historyReadDetails,
        TimestampsToReturn timestamps,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outstanding = new List<ByteString>();
        ByteString continuationPoint = default;
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                HistoryReadResponse response = await m_session.HistoryReadAsync(
                    null, historyReadDetails, timestamps, false,
                    [new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint }],
                    cancellationToken).ConfigureAwait(false);
                bool good = response.ResponseHeader is not null &&
                    StatusCode.IsGood(response.ResponseHeader.ServiceResult) && response.Results.Count == 1 &&
                    StatusCode.IsGood(response.Results[0].StatusCode);
                if (good)
                {
                    outstanding.Clear();
                }
                foreach (HistoryReadResult result in response.Results)
                {
                    if (!result.ContinuationPoint.IsNull && result.ContinuationPoint.Length != 0 &&
                        !outstanding.Contains(result.ContinuationPoint))
                    {
                        outstanding.Add(result.ContinuationPoint);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!good)
                {
                    StatusCode status = response.ResponseHeader is not null &&
                        StatusCode.IsBad(response.ResponseHeader.ServiceResult)
                        ? response.ResponseHeader.ServiceResult
                        : response.Results.Count == 1
                            ? response.Results[0].StatusCode
                            : StatusCodes.BadUnexpectedError;
                    throw new ServiceResultException(
                        StatusCode.IsBad(status) ? status : StatusCodes.BadUnexpectedError);
                }
                HistoryReadResult page = response.Results[0];
                continuationPoint = page.ContinuationPoint;
                yield return page;
            }
            while (!continuationPoint.IsNull && continuationPoint.Length != 0);
        }
        finally
        {
            await TryReleaseAsync(nodeId, historyReadDetails, timestamps, outstanding).ConfigureAwait(false);
        }
    }

    private async Task TryReleaseAsync(
        NodeId nodeId,
        ExtensionObject historyReadDetails,
        TimestampsToReturn timestamps,
        List<ByteString> continuationPoints)
    {
        if (continuationPoints.Count == 0)
        {
            return;
        }
        try
        {
            using var cleanup = new CancellationTokenSource(m_cleanupTimeout);
            var nodesToRead = new List<HistoryReadValueId>(continuationPoints.Count);
            foreach (ByteString continuationPoint in continuationPoints)
            {
                nodesToRead.Add(new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = continuationPoint });
            }
            await m_session.HistoryReadAsync(
                requestHeader: null,
                historyReadDetails: historyReadDetails,
                timestampsToReturn: timestamps,
                releaseContinuationPoints: true,
                nodesToRead: [.. nodesToRead],
                ct: cleanup.Token).AsTask().WaitAsync(cleanup.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort release — the server will GC the cursor on its
            // own timeout if we fail to free it explicitly.
        }
    }
}
