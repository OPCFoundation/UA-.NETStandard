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
        /// <exception cref="ArgumentNullException"></exception>
        public IAsyncEnumerable<HistoryEventFieldList> ReadEventsAsync(
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            EventFilter filter,
            uint maxValuesPerNode = 0,
            TimestampsToReturn? timestampsToReturn = null,
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
                NumValuesPerNode = maxValuesPerNode == 0
                    ? Options.DefaultMaxValuesPerNode
                    : maxValuesPerNode,
                Filter = filter
            };
            return ReadEventsIteratorAsync(
                nodeId,
                new ExtensionObject(details),
                ResolveEventTimestamps(
                    timestampsToReturn,
                    nameof(timestampsToReturn)),
                filter.SelectClauses.Count,
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
        /// <exception cref="ArgumentNullException"></exception>
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

            HistoryUpdateResult result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, eventIds.Count);
        }

        private async IAsyncEnumerable<HistoryEventFieldList> ReadEventsIteratorAsync(
            NodeId nodeId,
            ExtensionObject details,
            TimestampsToReturn timestampsToReturn,
            int expectedFieldCount,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (HistoryEventFieldList fields in ReadDetailsAsync(
                nodeId,
                details,
                timestampsToReturn,
                DecodeHistoryEvents,
                nodeOptions: null,
                cancellationToken).ConfigureAwait(false))
            {
                if (fields == null ||
                    fields.EventFields.Count != expectedFieldCount)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "HistoryEvent returned a field count that does not match the EventFilter.");
                }
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
            await ValidateEventUpdateAsync(
                filter,
                events,
                performUpdate,
                cancellationToken).ConfigureAwait(false);
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

            HistoryUpdateResult result = await SendHistoryUpdateAsync(
                new ExtensionObject(details),
                cancellationToken).ConfigureAwait(false);

            return GetOperationResults(result, events.Count);
        }

        private async ValueTask ValidateEventUpdateAsync(
            EventFilter filter,
            ArrayOf<HistoryEventFieldList> events,
            PerformUpdateType performUpdate,
            CancellationToken cancellationToken)
        {
            if (filter.SelectClauses.Count == 0 ||
                filter.WhereClause.Elements.Count != 0)
            {
                throw new ArgumentException(
                    "Historical event updates require select clauses and an empty WhereClause.",
                    nameof(filter));
            }
            if (performUpdate is not PerformUpdateType.Insert and
                not PerformUpdateType.Replace and
                not PerformUpdateType.Update)
            {
                throw new ArgumentOutOfRangeException(nameof(performUpdate));
            }

            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[i] ??
                    throw new ArgumentException(
                        "Historical event filters cannot contain null select clauses.",
                        nameof(filter));
                if ((performUpdate is PerformUpdateType.Insert or PerformUpdateType.Update) &&
                    !string.IsNullOrEmpty(clause.IndexRange))
                {
                    throw new ArgumentException(
                        "Historical event insert/update filters cannot use IndexRange.",
                        nameof(filter));
                }
            }
            var eventTypeCache = new Dictionary<NodeId, bool>();
            int eventIdIndex = await FindStandardEventFieldAsync(
                filter,
                BrowseNames.EventId,
                eventTypeCache,
                cancellationToken).ConfigureAwait(false);
            int eventTypeIndex = await FindStandardEventFieldAsync(
                filter,
                BrowseNames.EventType,
                eventTypeCache,
                cancellationToken).ConfigureAwait(false);
            int timeIndex = await FindStandardEventFieldAsync(
                filter,
                BrowseNames.Time,
                eventTypeCache,
                cancellationToken).ConfigureAwait(false);
            int sourceNodeIndex = await FindStandardEventFieldAsync(
                filter,
                BrowseNames.SourceNode,
                eventTypeCache,
                cancellationToken).ConfigureAwait(false);
            if (eventIdIndex == kAmbiguousEventField ||
                eventTypeIndex == kAmbiguousEventField ||
                timeIndex == kAmbiguousEventField ||
                sourceNodeIndex == kAmbiguousEventField)
            {
                throw new ArgumentException(
                        "Historical event filters cannot contain duplicate standard fields.",
                        nameof(filter));
            }
            if (performUpdate == PerformUpdateType.Replace && eventIdIndex < 0)
            {
                throw new ArgumentException(
                    "Historical event replacement requires an EventId select clause.",
                    nameof(filter));
            }
            if (performUpdate == PerformUpdateType.Replace &&
                !string.IsNullOrEmpty(
                    filter.SelectClauses[eventIdIndex].IndexRange))
            {
                throw new ArgumentException(
                    "Historical event replacement cannot apply an IndexRange to EventId.",
                    nameof(filter));
            }
            if ((performUpdate is PerformUpdateType.Insert or PerformUpdateType.Update) &&
                (eventTypeIndex < 0 || timeIndex < 0))
            {
                throw new ArgumentException(
                    "Historical event insert/update requires EventType and Time select clauses.",
                    nameof(filter));
            }
            for (int i = 0; i < events.Count; i++)
            {
                HistoryEventFieldList fields = events[i];
                if (fields == null ||
                    fields.EventFields.Count != filter.SelectClauses.Count)
                {
                    throw new ArgumentException(
                        "Each historical event must match the filter select-clause count.",
                        nameof(events));
                }
                if (performUpdate == PerformUpdateType.Replace &&
                    (!fields.EventFields[eventIdIndex].TryGetValue(
                        out ByteString eventId) ||
                        eventId.IsEmpty))
                {
                    throw new ArgumentException(
                        "Historical event replacement requires a non-empty EventId.",
                        nameof(events));
                }
                if (eventIdIndex >= 0 &&
                    !fields.EventFields[eventIdIndex].IsNull &&
                    !fields.EventFields[eventIdIndex].TryGetValue(
                        out ByteString _))
                {
                    throw new ArgumentException(
                        "Historical event EventId must be a ByteString.",
                        nameof(events));
                }
                if (performUpdate is PerformUpdateType.Insert or PerformUpdateType.Update)
                {
                    Variant eventTypeValue =
                        fields.EventFields[eventTypeIndex];
                    if (!eventTypeValue.IsNull &&
                        (!eventTypeValue.TryGetValue(
                            out NodeId eventType) ||
                            eventType.IsNull))
                    {
                        throw new ArgumentException(
                            "Historical event EventType must be a valid NodeId or null.",
                            nameof(events));
                    }
                    Variant eventTimeValue = fields.EventFields[timeIndex];
                    if (!eventTimeValue.IsNull &&
                        (!eventTimeValue.TryGetValue(
                            out DateTimeUtc eventTime) ||
                            eventTime == DateTimeUtc.MinValue))
                    {
                        throw new ArgumentException(
                            "Historical event Time must be a valid DateTime or null.",
                            nameof(events));
                    }
                }
                if (sourceNodeIndex >= 0 &&
                    !fields.EventFields[sourceNodeIndex].IsNull &&
                    (!fields.EventFields[sourceNodeIndex].TryGetValue(
                        out NodeId sourceNode) ||
                        sourceNode.IsNull))
                {
                    throw new ArgumentException(
                        "Historical event SourceNode must be a valid NodeId.",
                        nameof(events));
                }
            }
        }

        private async ValueTask<int> FindStandardEventFieldAsync(
            EventFilter filter,
            string browseName,
            Dictionary<NodeId, bool> eventTypeCache,
            CancellationToken cancellationToken)
        {
            int found = -1;
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand clause = filter.SelectClauses[i];
                if (clause.AttributeId == Attributes.Value &&
                    clause.BrowsePath.Count == 1 &&
                    clause.BrowsePath[0].NamespaceIndex == 0 &&
                    string.Equals(
                        clause.BrowsePath[0].Name,
                        browseName,
                        StringComparison.Ordinal))
                {
                    NodeId typeDefinitionId = clause.TypeDefinitionId;
                    if (typeDefinitionId.IsNull)
                    {
                        continue;
                    }
                    bool isEventType =
                        typeDefinitionId == ObjectTypeIds.BaseEventType;
                    if (!isEventType &&
                        !eventTypeCache.TryGetValue(
                            typeDefinitionId,
                            out isEventType))
                    {
                        isEventType = await Session.NodeCache.IsTypeOfAsync(
                            typeDefinitionId,
                            ObjectTypeIds.BaseEventType,
                            cancellationToken).ConfigureAwait(false);
                        eventTypeCache.Add(typeDefinitionId, isEventType);
                    }
                    if (!isEventType)
                    {
                        continue;
                    }
                    if (found >= 0)
                    {
                        return kAmbiguousEventField;
                    }
                    found = i;
                }
            }
            return found;
        }

        private TimestampsToReturn ResolveEventTimestamps(
            TimestampsToReturn? requested,
            string parameterName)
        {
            TimestampsToReturn value = requested ??
                Options.DefaultTimestampsToReturn;
            if (value is not TimestampsToReturn.Source and
                not TimestampsToReturn.Server and
                not TimestampsToReturn.Both and
                not TimestampsToReturn.Neither)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
            return value;
        }

        private const int kAmbiguousEventField = -2;
    }
}
