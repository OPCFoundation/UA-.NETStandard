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

                if (result.HistoryData.TryGetValue(out HistoryData hd) && hd is not null)
                {
                    foreach (DataValue dv in hd.DataValues)
                    {
                        if (dv is null)
                        {
                            continue;
                        }

                        rows.Add(new HistoryRow(
                            (DateTime)dv.SourceTimestamp,
                            (DateTime)dv.ServerTimestamp,
                            dv.WrappedValue,
                            dv.StatusCode));
                    }
                }
                else if (result.HistoryData.TryGetValue(out HistoryModifiedData hmd) && hmd is not null)
                {
                    foreach (DataValue dv in hmd.DataValues)
                    {
                        if (dv is null)
                        {
                            continue;
                        }

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
