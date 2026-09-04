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
            if (Options.MaxReadDuration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "MaxReadDuration must not be negative.");
            }
            _ = ResolveTimestamps(
                requested: null,
                parameterName: nameof(HistoryClientOptions.DefaultTimestampsToReturn));
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
        /// <param name="nodeOptions">Optional index range and data encoding.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public IAsyncEnumerable<DataValue> ReadRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            bool returnBounds = false,
            TimestampsToReturn? timestampsToReturn = null,
            HistoryReadNodeOptions? nodeOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode == 0
                    ? Options.DefaultMaxValuesPerNode
                    : maxValuesPerNode,
                ReturnBounds = returnBounds
            };

            return ReadDetailsAsync(
                nodeId,
                new ExtensionObject(details),
                ResolveTimestamps(timestampsToReturn, nameof(timestampsToReturn)),
                DecodeHistoryData,
                nodeOptions,
                cancellationToken);
        }

        /// <summary>
        /// Reads the modified-history audit trail for a single variable
        /// (Part 11 §5.2.5).
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public IAsyncEnumerable<ModifiedHistoryValue> ReadModifiedAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            TimestampsToReturn? timestampsToReturn = null,
            HistoryReadNodeOptions? nodeOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            var details = new ReadRawModifiedDetails
            {
                IsReadModified = true,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode == 0
                    ? Options.DefaultMaxValuesPerNode
                    : maxValuesPerNode
            };

            return ReadDetailsAsync(
                nodeId,
                new ExtensionObject(details),
                ResolveTimestamps(timestampsToReturn, nameof(timestampsToReturn)),
                DecodeHistoryModifiedData,
                nodeOptions,
                cancellationToken);
        }

        /// <summary>
        /// Inserts raw values into the history archive.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> InsertAsync(
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Insert, cancellationToken);
        }

        /// <summary>
        /// Replaces existing values in the history archive.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> ReplaceAsync(
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Replace, cancellationToken);
        }

        /// <summary>
        /// Upserts values (insert if absent, replace if present).
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> UpdateAsync(
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken cancellationToken = default)
        {
            return PerformUpdateAsync(nodeId, values, PerformUpdateType.Update, cancellationToken);
        }

        /// <summary>
        /// Deletes raw values in the half-open interval
        /// <c>[startTime, endTime)</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<StatusCode> DeleteRawAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            bool isDeleteModified = false,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            var details = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                IsDeleteModified = isDeleteModified,
                StartTime = startTime,
                EndTime = endTime
            };

            HistoryUpdateResult result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return result.StatusCode;
        }

        /// <summary>
        /// Deletes values at the specified timestamps.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<ArrayOf<StatusCode>> DeleteAtTimeAsync(
            NodeId nodeId,
            ArrayOf<DateTime> timestamps,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (timestamps.IsNull)
            {
                throw new ArgumentNullException(nameof(timestamps));
            }
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

            HistoryUpdateResult result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, timestamps.Count);
        }

        private async ValueTask<ArrayOf<StatusCode>> PerformUpdateAsync(
            NodeId nodeId,
            ArrayOf<DataValue> values,
            PerformUpdateType performUpdate,
            CancellationToken cancellationToken)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (values.IsNull)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                UpdateValues = values
            };

            HistoryUpdateResult result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, values.Count);
        }

        private TimestampsToReturn ResolveTimestamps(
            TimestampsToReturn? requested,
            string parameterName)
        {
            TimestampsToReturn value = requested ??
                Options.DefaultTimestampsToReturn;
            if (value is not TimestampsToReturn.Source and
                not TimestampsToReturn.Server and
                not TimestampsToReturn.Both)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
            return value;
        }
    }
}
