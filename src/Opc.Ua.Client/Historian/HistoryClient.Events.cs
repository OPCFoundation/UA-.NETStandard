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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Event-history operations for <see cref="HistoryClient"/>.
    /// </summary>
    public sealed partial class HistoryClient
    {
        /// <summary>
        /// Reads historical events for a notifier. Event fields are returned
        /// in the order of <paramref name="filter"/>'s select clauses.
        /// </summary>
        public IAsyncEnumerable<HistoryEventFieldList> ReadEventsAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            EventFilter filter,
            uint maxValuesPerNode = 0,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            var details = new ReadEventDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode,
                Filter = filter
            };
            return ReadEventsIteratorAsync(
                nodeId,
                new ExtensionObject(details),
                timestampsToReturn,
                cancellationToken);
        }

        /// <summary>
        /// Inserts historical events.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> InsertEventsAsync(
            NodeId nodeId,
            EventFilter filter,
            ArrayOf<HistoryEventFieldList> events,
            CancellationToken cancellationToken = default)
        {
            return PerformEventUpdateAsync(
                nodeId,
                filter,
                events,
                PerformUpdateType.Insert,
                cancellationToken);
        }

        /// <summary>
        /// Replaces historical events identified by their EventIds.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> ReplaceEventsAsync(
            NodeId nodeId,
            EventFilter filter,
            ArrayOf<HistoryEventFieldList> events,
            CancellationToken cancellationToken = default)
        {
            return PerformEventUpdateAsync(
                nodeId,
                filter,
                events,
                PerformUpdateType.Replace,
                cancellationToken);
        }

        /// <summary>
        /// Inserts or replaces historical events.
        /// </summary>
        public ValueTask<ArrayOf<StatusCode>> UpdateEventsAsync(
            NodeId nodeId,
            EventFilter filter,
            ArrayOf<HistoryEventFieldList> events,
            CancellationToken cancellationToken = default)
        {
            return PerformEventUpdateAsync(
                nodeId,
                filter,
                events,
                PerformUpdateType.Update,
                cancellationToken);
        }

        /// <summary>
        /// Deletes historical events by EventId.
        /// </summary>
        public async ValueTask<ArrayOf<StatusCode>> DeleteEventsAsync(
            NodeId nodeId,
            ArrayOf<ByteString> eventIds,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (eventIds.IsNull)
            {
                throw new ArgumentNullException(nameof(eventIds));
            }

            var details = new DeleteEventDetails
            {
                NodeId = nodeId,
                EventIds = eventIds
            };

            HistoryUpdateResult? result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, eventIds.Count, useStatusFallback: true);
        }

        private async IAsyncEnumerable<HistoryEventFieldList> ReadEventsIteratorAsync(
            NodeId nodeId,
            ExtensionObject details,
            TimestampsToReturn timestampsToReturn,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (HistoryEventFieldList fields in ReadDetailsAsync(
                nodeId,
                details,
                timestampsToReturn,
                DecodeHistoryEvents,
                cancellationToken).ConfigureAwait(false))
            {
                yield return fields;
            }
        }

        private async ValueTask<ArrayOf<StatusCode>> PerformEventUpdateAsync(
            NodeId nodeId,
            EventFilter filter,
            ArrayOf<HistoryEventFieldList> events,
            PerformUpdateType performUpdate,
            CancellationToken cancellationToken)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }
            if (events.IsNull)
            {
                throw new ArgumentNullException(nameof(events));
            }
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] == null)
                {
                    throw new ArgumentException(
                        "The events collection contains a null value.",
                        nameof(events));
                }
            }

            var details = new UpdateEventDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = performUpdate,
                Filter = filter,
                EventData = events
            };

            HistoryUpdateResult? result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, events.Count, useStatusFallback: true);
        }
    }
}
