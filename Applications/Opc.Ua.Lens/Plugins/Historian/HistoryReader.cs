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
/// On cancellation we issue one final HistoryRead with
/// <c>releaseContinuationPoints: true</c> so any outstanding server-side
/// cursor is freed.  Failures of that release call are intentionally
/// swallowed — the original cancellation is what matters and the server
/// will GC the cursor on its own timeout.
/// </remarks>
internal sealed class HistoryReader
{
    private readonly ISession m_session;

    public HistoryReader(ISession session)
    {
        m_session = session;
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
    /// silently skipped — annotations are an optional feature and missing
    /// annotations must never break the primary value read.
    /// </summary>
    public async Task AttachAnnotationsAsync(
        NodeId variableNodeId,
        IReadOnlyList<HistoryRow> rows,
        CancellationToken ct)
    {
        if (variableNodeId.IsNull || rows.Count == 0)
        {
            return;
        }
        NodeId? annotationsNodeId = await ResolveAnnotationsNodeIdAsync(variableNodeId, ct).ConfigureAwait(false);
        if (!annotationsNodeId.HasValue || annotationsNodeId.Value.IsNull)
        {
            return;
        }
        NodeId annNode = annotationsNodeId.Value;
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
        catch (Exception)
        {
            // Best-effort — server doesn't support annotations or read
            // failed for an unrelated reason.  Annotation column simply
            // stays empty for this batch.
            return;
        }
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
        ByteString continuationPoint = default;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var valueId = new HistoryReadValueId
            {
                NodeId = annotationsNodeId,
                ContinuationPoint = continuationPoint
            };
            var nodesToRead = new HistoryReadValueId[] { valueId };
            HistoryReadResponse response = await m_session.HistoryReadAsync(
                requestHeader: null,
                historyReadDetails: detailsObject,
                timestampsToReturn: TimestampsToReturn.Source,
                releaseContinuationPoints: false,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                return results;
            }
            HistoryReadResult r = response.Results[0];
            if (StatusCode.IsBad(r.StatusCode))
            {
                return results;
            }
            if (r.HistoryData.TryGetValue(out HistoryData? hd) && hd is not null)
            {
                foreach (DataValue dv in hd.DataValues)
                {
                    results.Add(dv);
                }
            }
            ByteString cp = r.ContinuationPoint;
            if (cp.IsNull || cp.Length == 0)
            {
                return results;
            }
            continuationPoint = cp;
        }
    }

    private static Annotation? ExtractAnnotation(Variant v)
    {
        object? boxed = v.Value;
        switch (boxed)
        {
            case Annotation ann:
                return ann;
            case ExtensionObject eo when eo.Body is Annotation ann2:
                return ann2;
            case ExtensionObject[] arr when arr.Length > 0:
                foreach (ExtensionObject e in arr)
                {
                    if (e.Body is Annotation match)
                    {
                        return match;
                    }
                }
                return null;
            case Annotation[] arr when arr.Length > 0:
                return arr[0];
            default:
                return null;
        }
    }

    private async Task<List<HistoryRow>> ReadLoopAsync(
        NodeId nodeId,
        ExtensionObject historyReadDetails,
        CancellationToken ct)
    {
        var rows = new List<HistoryRow>();
        ByteString continuationPoint = default;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var valueId = new HistoryReadValueId
                {
                    NodeId = nodeId,
                    ContinuationPoint = continuationPoint
                };
                var nodesToRead = new HistoryReadValueId[] { valueId };

                HistoryReadResponse response = await m_session.HistoryReadAsync(
                    requestHeader: null,
                    historyReadDetails: historyReadDetails,
                    timestampsToReturn: TimestampsToReturn.Both,
                    releaseContinuationPoints: false,
                    nodesToRead: nodesToRead,
                    ct: ct).ConfigureAwait(false);

                if (response.Results.Count == 0)
                {
                    return rows;
                }
                HistoryReadResult result = response.Results[0];
                if (StatusCode.IsBad(result.StatusCode))
                {
                    throw new ServiceResultException(
                        result.StatusCode,
                        $"HistoryRead returned {result.StatusCode}.");
                }

                if (result.HistoryData.TryGetValue(out HistoryData? hd) && hd is not null)
                {
                    foreach (DataValue dv in hd.DataValues)
                    {
                        rows.Add(new HistoryRow(
                            (DateTime)dv.SourceTimestamp,
                            (DateTime)dv.ServerTimestamp,
                            dv.WrappedValue,
                            dv.StatusCode));
                    }
                }
                else if (result.HistoryData.TryGetValue(out HistoryModifiedData? hmd) && hmd is not null)
                {
                    foreach (DataValue dv in hmd.DataValues)
                    {
                        rows.Add(new HistoryRow(
                            (DateTime)dv.SourceTimestamp,
                            (DateTime)dv.ServerTimestamp,
                            dv.WrappedValue,
                            dv.StatusCode));
                    }
                }

                ByteString cp = result.ContinuationPoint;
                if (cp.IsNull || cp.Length == 0)
                {
                    return rows;
                }
                continuationPoint = cp;
            }
        }
        catch (OperationCanceledException)
        {
            await TryReleaseAsync(nodeId, historyReadDetails, continuationPoint).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryReleaseAsync(
        NodeId nodeId,
        ExtensionObject historyReadDetails,
        ByteString continuationPoint)
    {
        if (continuationPoint.IsNull || continuationPoint.Length == 0)
        {
            return;
        }
        try
        {
            var valueId = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = continuationPoint
            };
            var nodesToRead = new HistoryReadValueId[] { valueId };
            await m_session.HistoryReadAsync(
                requestHeader: null,
                historyReadDetails: historyReadDetails,
                timestampsToReturn: TimestampsToReturn.Both,
                releaseContinuationPoints: true,
                nodesToRead: nodesToRead,
                ct: CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort release — the server will GC the cursor on its
            // own timeout if we fail to free it explicitly.
        }
    }
}
