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

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Async <c>System.IO</c>-style client over OPC UA Part 11
    /// HistoryRead / HistoryUpdate services. Automatically paginates
    /// continuation points so callers can <c>await foreach</c> over an
    /// entire time range without seeing the wire-level batching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constructed via <see cref="SessionHistorianExtensions.Historian"/>
    /// over an active <see cref="ISession"/>.
    /// </para>
    /// </remarks>
    public sealed partial class HistoryClient
    {
        /// <summary>
        /// Creates a new <see cref="HistoryClient"/> wrapping the supplied session.
        /// </summary>
        public HistoryClient(ISession session, HistoryClientOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Options = options ?? new HistoryClientOptions();
        }

        /// <summary>
        /// The underlying session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Client options.
        /// </summary>
        public HistoryClientOptions Options { get; }

        /// <summary>
        /// Reads raw history for a single variable. The returned async
        /// stream transparently re-issues HistoryRead with the server's
        /// continuation point until the time window is fully drained or
        /// the client cancels iteration.
        /// </summary>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="startTime">Start of the time range (inclusive).</param>
        /// <param name="endTime">End of the time range (exclusive).</param>
        /// <param name="maxValuesPerNode">
        /// Maximum number of values per <c>HistoryRead</c> request.
        /// Zero (the default) lets the server decide.
        /// </param>
        /// <param name="returnBounds">Whether to return bounding values.</param>
        /// <param name="timestampsToReturn">Timestamps to include with the values.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async IAsyncEnumerable<DataValue> ReadRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            bool returnBounds = false,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode,
                ReturnBounds = returnBounds
            };

            await foreach (DataValue value in ReadRawOrModifiedAsync(
                nodeId, details, timestampsToReturn, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }

        /// <summary>
        /// Reads the modified-history audit trail for a single variable
        /// (Part 11 §5.2.5).
        /// </summary>
        public async IAsyncEnumerable<DataValue> ReadModifiedAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = true,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode
            };

            await foreach (DataValue value in ReadRawOrModifiedAsync(
                nodeId, details, timestampsToReturn, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return value;
            }
        }

        /// <summary>
        /// Inserts raw values into the history archive.
        /// </summary>
        public ValueTask<IList<StatusCode>> InsertAsync(
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Insert, cancellationToken);
        }

        /// <summary>
        /// Replaces existing values in the history archive.
        /// </summary>
        public ValueTask<IList<StatusCode>> ReplaceAsync(
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Replace, cancellationToken);
        }

        /// <summary>
        /// Upserts values (insert if absent, replace if present).
        /// </summary>
        public ValueTask<IList<StatusCode>> UpdateAsync(
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Update, cancellationToken);
        }

        /// <summary>
        /// Deletes raw values in the half-open interval
        /// <c>[startTime, endTime)</c>.
        /// </summary>
        public async ValueTask<StatusCode> DeleteRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            bool isDeleteModified = false,
            CancellationToken cancellationToken = default)
        {
            var details = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                IsDeleteModified = isDeleteModified,
                StartTime = startTime,
                EndTime = endTime
            };

            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null,
                [new ExtensionObject(details)],
                cancellationToken).ConfigureAwait(false);

            return response.Results.Count > 0 ? response.Results[0].StatusCode : StatusCodes.BadInternalError;
        }

        /// <summary>
        /// Deletes values at the specified timestamps.
        /// </summary>
        public async ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
            NodeId nodeId,
            IList<DateTime> timestamps,
            CancellationToken cancellationToken = default)
        {
            var typed = new DateTimeUtc[timestamps.Count];
            for (int i = 0; i < timestamps.Count; i++)
            {
                typed[i] = timestamps[i];
            }

            var details = new DeleteAtTimeDetails
            {
                NodeId = nodeId,
                ReqTimes = typed
            };

            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null,
                [new ExtensionObject(details)],
                cancellationToken).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return [];
            }

            ArrayOf<StatusCode> opResults = response.Results[0].OperationResults;
            var result = new StatusCode[opResults.Count];
            for (int i = 0; i < opResults.Count; i++)
            {
                result[i] = opResults[i];
            }
            return result;
        }

        private async IAsyncEnumerable<DataValue> ReadRawOrModifiedAsync(
            NodeId nodeId,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // See HistoryClient.Extras.cs ReadDetailsAsync for the rationale:
            // tracks the in-flight continuation point so the finally block
            // can release it when the consumer abandons iteration; counts
            // consecutive empty pages to detect a buggy server.
            ByteString continuationPoint = ByteString.Empty;
            ByteString liveContinuationPoint = ByteString.Empty;
            ExtensionObject detailsExt = new(details);
            int emptyPagesInARow = 0;
            try
            {
                while (true)
                {
                    var nodesToRead = new HistoryReadValueId[]
                    {
                        new()
                        {
                            NodeId = nodeId,
                            ContinuationPoint = continuationPoint
                        }
                    };

                    HistoryReadResponse response = await Session.HistoryReadAsync(
                        null,
                        detailsExt,
                        timestampsToReturn,
                        releaseContinuationPoints: false,
                        nodesToRead,
                        cancellationToken).ConfigureAwait(false);

                    if (response.Results.Count == 0)
                    {
                        yield break;
                    }

                    HistoryReadResult result = response.Results[0];
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        throw new ServiceResultException(result.StatusCode, "HistoryRead returned a bad status.");
                    }

                    liveContinuationPoint = result.ContinuationPoint;

                    bool yieldedSomething = false;
                    if (!result.HistoryData.IsNull &&
                        result.HistoryData.TryGetValue(out HistoryData? hd))
                    {
                        DataValue[]? values = hd.DataValues.ToArray();
                        if (values != null && values.Length > 0)
                        {
                            foreach (DataValue v in values)
                            {
                                yield return v;
                            }
                            yieldedSomething = true;
                        }
                    }

                    if (result.ContinuationPoint.IsEmpty)
                    {
                        liveContinuationPoint = ByteString.Empty;
                        yield break;
                    }

                    if (!yieldedSomething)
                    {
                        emptyPagesInARow++;
                        if (emptyPagesInARow >= 3)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInternalError,
                                "Server returned three consecutive empty paginated history pages with a non-empty continuation point.");
                        }
                    }
                    else
                    {
                        emptyPagesInARow = 0;
                    }

                    continuationPoint = result.ContinuationPoint;
                }
            }
            finally
            {
                if (!liveContinuationPoint.IsEmpty)
                {
                    try
                    {
                        var releaseNodes = new HistoryReadValueId[]
                        {
                            new() { NodeId = nodeId, ContinuationPoint = liveContinuationPoint }
                        };
                        _ = await Session.HistoryReadAsync(
                            null,
                            detailsExt,
                            timestampsToReturn,
                            releaseContinuationPoints: true,
                            releaseNodes,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // best-effort cleanup
                    }
                    catch (TaskCanceledException)
                    {
                        // best-effort cleanup
                    }
                    catch (OperationCanceledException)
                    {
                        // best-effort cleanup
                    }
                }
            }
        }

        private async ValueTask<IList<StatusCode>> PerformUpdateAsync(
            NodeId nodeId,
            IList<DataValue> values,
            PerformUpdateType performUpdate,
            CancellationToken cancellationToken)
        {
            var updateValues = new DataValue[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                updateValues[i] = values[i];
            }

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                UpdateValues = updateValues
            };

            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null,
                [new ExtensionObject(details)],
                cancellationToken).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return [];
            }

            ArrayOf<StatusCode> opResults = response.Results[0].OperationResults;
            var result = new StatusCode[opResults.Count];
            for (int i = 0; i < opResults.Count; i++)
            {
                result[i] = opResults[i];
            }
            return result;
        }
    }
}
