/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
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
using Opc.Ua.Client.Historian;

namespace Quickstarts.ConsoleReferenceClient
{
    /// <summary>
    /// End-to-end Historical Access workflow for the ReferenceServer.
    /// </summary>
    public static class HistorianClientSample
    {
        /// <summary>
        /// Discovers historian capabilities and exercises data, annotations,
        /// aggregates, and event history through <see cref="HistoryClient"/>.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task RunAsync(
            ISession session,
            CancellationToken cancellationToken = default)
        {
            int namespaceIndex = session.NamespaceUris.GetIndex(ReferenceServerNamespaceUri);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    $"The server does not expose {ReferenceServerNamespaceUri}.");
            }

            var dataNodeId = new NodeId(DataNodeName, (ushort)namespaceIndex);
            var structuredNodeId = new NodeId(
                StructuredNodeName,
                (ushort)namespaceIndex);
            var eventNotifierId = new NodeId(EventNotifierName, (ushort)namespaceIndex);
            HistoryClient client = session.Historian(new HistoryClientOptions
            {
                MaxPagesPerRead = 10_000,
                MaxReadDuration = TimeSpan.FromMinutes(1)
            });

            HistoryServerCapabilitiesInfo capabilities =
                await client.GetServerCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            HistoricalConformanceInfo conformance =
                await client.GetConformanceInfoAsync(
                    cancellationToken).ConfigureAwait(false);
            HistoricalDataConfigurationInfo configuration =
                await client.GetConfigurationAsync(
                    dataNodeId,
                    cancellationToken).ConfigureAwait(false);
            HistoricalDataConfigurationInfo structuredConfiguration =
                await client.GetConfigurationAsync(
                    structuredNodeId,
                    cancellationToken).ConfigureAwait(false);
            HistoricalEventConfigurationInfo eventConfiguration =
                await client.GetEventConfigurationAsync(
                    eventNotifierId,
                    cancellationToken).ConfigureAwait(false);
            ValidateDiscovery(
                capabilities,
                conformance,
                configuration,
                structuredConfiguration,
                eventConfiguration);

            Console.WriteLine(
                "Historian capabilities: data={0}, events={1}, data writes={2}, event writes={3}.",
                capabilities.AccessHistoryData,
                capabilities.AccessHistoryEvents,
                capabilities.InsertData && capabilities.ReplaceData && capabilities.UpdateData,
                capabilities.InsertEvent &&
                capabilities.ReplaceEvent &&
                capabilities.UpdateEvent &&
                capabilities.DeleteEvent);
            Console.WriteLine(
                "Historical claims: server profiles={0}, conformance units={1}.",
                conformance.ServerProfiles.Count,
                conformance.ConformanceUnits.Count);
            Console.WriteLine(
                "Data configuration: installed={0}, server timestamps={1}, archive start={2:O}.",
                configuration.HasConfiguration,
                configuration.ServerTimestampSupported,
                configuration.StartOfArchive);
            Console.WriteLine(
                "Event configuration: installed={0}, event types={1}, archive start={2:O}.",
                eventConfiguration.HasConfiguration,
                eventConfiguration.EventTypes.Count,
                eventConfiguration.StartOfArchive);

            DateTime endTime = DateTime.UtcNow;
            DateTime startTime = endTime.AddHours(-4);
            int rawCount = await CountAsync(
                client.ReadRawAsync(
                    dataNodeId,
                    startTime,
                    endTime,
                    maxValuesPerNode: 50,
                    returnBounds: true,
                    timestampsToReturn: TimestampsToReturn.Both,
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            int atTimeCount = await CountAsync(
                client.ReadAtTimeAsync(
                    dataNodeId,
                    [startTime.AddMinutes(30), startTime.AddHours(2)],
                    useSimpleBounds: false,
                    TimestampsToReturn.Both,
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Raw history values={0}; at-time values={1}.", rawCount, atTimeCount);

            await ReadAggregateFamiliesAsync(
                client,
                dataNodeId,
                startTime,
                endTime,
                cancellationToken).ConfigureAwait(false);
            await DemonstrateDataUpdatesAsync(
                client,
                dataNodeId,
                cancellationToken).ConfigureAwait(false);
            await DemonstrateStructuredDataAsync(
                client,
                structuredNodeId,
                cancellationToken).ConfigureAwait(false);
            await DemonstrateEventsAsync(
                client,
                eventNotifierId,
                cancellationToken).ConfigureAwait(false);
            await DemonstrateAnnotationsAsync(
                client,
                dataNodeId,
                cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask ReadAggregateFamiliesAsync(
            HistoryClient client,
            NodeId nodeId,
            DateTime startTime,
            DateTime endTime,
            CancellationToken cancellationToken)
        {
            (string Name, NodeId Id)[] aggregateFamilies =
            [
                ("interpolative", ObjectIds.AggregateFunction_Interpolative),
                ("average/total", ObjectIds.AggregateFunction_Average),
                ("minimum/maximum", ObjectIds.AggregateFunction_Minimum),
                ("count/state", ObjectIds.AggregateFunction_Count),
                ("start/end/delta", ObjectIds.AggregateFunction_Start),
                ("quality", ObjectIds.AggregateFunction_DurationGood),
                ("statistical", ObjectIds.AggregateFunction_StandardDeviationPopulation)
            ];

            foreach ((string name, NodeId aggregateId) in aggregateFamilies)
            {
                int count = await CountAsync(
                    client.ReadProcessedAsync(
                        nodeId,
                        aggregateId,
                        startTime,
                        endTime,
                        processingInterval: TimeSpan.FromMinutes(30).TotalMilliseconds,
                        cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                Console.WriteLine("Aggregate family {0}: {1} processed values.", name, count);
            }
        }

        private static async ValueTask DemonstrateDataUpdatesAsync(
            HistoryClient client,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DateTime timestamp = DateTime.UtcNow.AddYears(-20);
            DataValue insert = CreateHistoricalValue(4387, timestamp);
            AssertGood(
                await client.InsertAsync(
                    nodeId,
                    [insert],
                    cancellationToken).ConfigureAwait(false),
                "insert raw value");

            DataValue replacement = CreateHistoricalValue(4388, timestamp);
            AssertGood(
                await client.ReplaceAsync(
                    nodeId,
                    [replacement],
                    cancellationToken).ConfigureAwait(false),
                "replace raw value");

            DataValue update = CreateHistoricalValue(4389, timestamp);
            AssertGood(
                await client.UpdateAsync(
                    nodeId,
                    [update],
                    cancellationToken).ConfigureAwait(false),
                "update raw value");

            int modifiedCount = await CountAsync(
                client.ReadModifiedAsync(
                    nodeId,
                    timestamp.AddMilliseconds(-1),
                    timestamp.AddMilliseconds(1),
                    maxValuesPerNode: 1,
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Modified-history entries={0}.", modifiedCount);

            AssertGood(
                await client.DeleteAtTimeAsync(
                    nodeId,
                    [timestamp],
                    cancellationToken).ConfigureAwait(false),
                "delete raw value");

            DateTime rangeTimestamp = timestamp.AddSeconds(10);
            AssertGood(
                await client.InsertAsync(
                    nodeId,
                    [CreateHistoricalValue(4390, rangeTimestamp)],
                    cancellationToken).ConfigureAwait(false),
                "insert value for range delete");
            AssertGood(
                await client.DeleteRawAsync(
                    nodeId,
                    rangeTimestamp.AddMilliseconds(-1),
                    rangeTimestamp.AddMilliseconds(1),
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false),
                "delete raw range");
        }

        private static async ValueTask DemonstrateAnnotationsAsync(
            HistoryClient client,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DateTime annotationTime = DateTime.UtcNow.AddYears(-20).AddMinutes(1);
            ArrayOf<Annotation> annotations =
            [
                new Annotation
                {
                    AnnotationTime = annotationTime,
                    Message = "ReferenceClient historian sample",
                    UserName = "ReferenceClient"
                },
                new Annotation
                {
                    AnnotationTime = annotationTime.AddSeconds(1),
                    Message = "ReferenceClient historian sample batch",
                    UserName = "ReferenceClient"
                }
            ];

            AssertGood(
                await client.WriteAnnotationsAsync(
                    nodeId,
                    annotations,
                    cancellationToken: cancellationToken).ConfigureAwait(false),
                "insert annotation");
            ArrayOf<Annotation> replacements =
            [
                new Annotation
                {
                    AnnotationTime = annotations[0].AnnotationTime,
                    Message = "ReferenceClient replaced annotation",
                    UserName = "ReferenceClient"
                },
                new Annotation
                {
                    AnnotationTime = annotations[1].AnnotationTime,
                    Message = "ReferenceClient replaced annotation batch",
                    UserName = "ReferenceClient"
                }
            ];
            AssertGood(
                await client.WriteAnnotationsAsync(
                    nodeId,
                    replacements,
                    PerformUpdateType.Replace,
                    cancellationToken).ConfigureAwait(false),
                "replace annotations");
            var updated = new Annotation
            {
                AnnotationTime = replacements[1].AnnotationTime,
                Message = "ReferenceClient updated annotation",
                UserName = "ReferenceClient"
            };
            AssertGood(
                await client.WriteAnnotationsAsync(
                    nodeId,
                    [updated],
                    PerformUpdateType.Update,
                    cancellationToken).ConfigureAwait(false),
                "update annotation");
            int annotationCount = await CountAsync(
                client.ReadAnnotationsAsync(
                    nodeId,
                    annotationTime.AddMilliseconds(-1),
                    updated.AnnotationTime.ToDateTime().AddMilliseconds(1),
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Annotations read={0}.", annotationCount);
            AssertGood(
                await client.WriteAnnotationsAsync(
                    nodeId,
                    [replacements[0], updated],
                    PerformUpdateType.Remove,
                    cancellationToken).ConfigureAwait(false),
                "remove annotation");
        }

        private static async ValueTask DemonstrateStructuredDataAsync(
            HistoryClient client,
            NodeId nodeId,
            CancellationToken cancellationToken)
        {
            DateTime timestamp =
                DateTime.UtcNow.AddYears(-20).AddMinutes(3);
            DataValue pressure = CreateStructuredValue(
                nodeId.NamespaceIndex,
                "Pressure",
                timestamp,
                42.5);
            DataValue temperature = CreateStructuredValue(
                nodeId.NamespaceIndex,
                "Temperature",
                timestamp,
                21.25);
            AssertGood(
                await client.UpdateStructureDataAsync(
                    nodeId,
                    PerformUpdateType.Insert,
                    [pressure, temperature],
                    cancellationToken).ConfigureAwait(false),
                "insert structured values");

            // IDE0001 is suppressed: "KeyValuePair" is ambiguous between Opc.Ua.KeyValuePair
            // and System.Collections.Generic.KeyValuePair in this file (CS0104), so the
            // qualification cannot be simplified away.
#pragma warning disable IDE0001
            var readBack = new List<Opc.Ua.KeyValuePair>();
#pragma warning restore IDE0001
            await foreach (DataValue value in client.ReadRawAsync(
                nodeId,
                timestamp.AddMilliseconds(-1),
                timestamp.AddMilliseconds(1),
                maxValuesPerNode: 1,
                nodeOptions: new HistoryReadNodeOptions
                {
                    DataEncoding = new QualifiedName(
                        BrowseNames.DefaultBinary)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                readBack.Add(ReadStructuredValue(value));
            }
            if (readBack.Count != 2)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    "The structured history read did not return both same-time keys.");
            }

            DataValue replacedPressure = CreateStructuredValue(
                nodeId.NamespaceIndex,
                "Pressure",
                timestamp,
                43.0);
            AssertGood(
                await client.UpdateStructureDataAsync(
                    nodeId,
                    PerformUpdateType.Replace,
                    [replacedPressure],
                    cancellationToken).ConfigureAwait(false),
                "replace structured value");
            DataValue updatedTemperature = CreateStructuredValue(
                nodeId.NamespaceIndex,
                "Temperature",
                timestamp,
                22.0);
            AssertGood(
                await client.UpdateStructureDataAsync(
                    nodeId,
                    PerformUpdateType.Update,
                    [updatedTemperature],
                    cancellationToken).ConfigureAwait(false),
                "update structured value");

            int modifiedCount = await CountAsync(
                client.ReadModifiedAsync(
                    nodeId,
                    timestamp.AddMilliseconds(-1),
                    timestamp.AddMilliseconds(1),
                    maxValuesPerNode: 1,
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            int atTimeCount = await CountAsync(
                client.ReadAtTimeAsync(
                    nodeId,
                    [timestamp],
                    nodeOptions: new HistoryReadNodeOptions
                    {
                        DataEncoding = new QualifiedName(
                            BrowseNames.DefaultBinary)
                    },
                    cancellationToken: cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (modifiedCount < 2 || atTimeCount != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    "Structured modified/at-time history was incomplete.");
            }

            AssertGood(
                await client.UpdateStructureDataAsync(
                    nodeId,
                    PerformUpdateType.Remove,
                    [replacedPressure, updatedTemperature],
                    cancellationToken).ConfigureAwait(false),
                "remove structured values");
            Console.WriteLine(
                "Structured history CRUD completed; same-time keys={0}, modified={1}.",
                readBack.Count,
                modifiedCount);
        }

        private static async ValueTask DemonstrateEventsAsync(
            HistoryClient client,
            NodeId notifierId,
            CancellationToken cancellationToken)
        {
            EventFilter filter = CreateEventFilter();
            await DemonstrateCapturedServerEventAsync(
                client,
                notifierId,
                filter,
                cancellationToken).ConfigureAwait(false);
            DateTime eventTime = DateTime.UtcNow.AddYears(-20).AddMinutes(2);
            HistoryEventFieldList inserted = CreateEvent(
                ByteString.Empty,
                notifierId,
                eventTime,
                "ReferenceClient inserted historical event");
            AssertGood(
                await client.InsertEventsAsync(
                    notifierId,
                    filter,
                    [inserted],
                    cancellationToken).ConfigureAwait(false),
                "insert event");

            ByteString generatedEventId = ByteString.Empty;
            await foreach (HistoryEventFieldList historicalEvent in client.ReadEventsAsync(
                notifierId,
                eventTime.AddMilliseconds(-1),
                eventTime.AddMilliseconds(1),
                filter,
                maxValuesPerNode: 1,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (historicalEvent.EventFields.Count > 0 &&
                    historicalEvent.EventFields[0].TryGetValue(out ByteString eventId))
                {
                    generatedEventId = eventId;
                    break;
                }
            }
            if (generatedEventId.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    "The server did not return the generated EventId.");
            }

            AssertGood(
                await client.ReplaceEventsAsync(
                    notifierId,
                    filter,
                    [
                        CreateEvent(
                            generatedEventId,
                            notifierId,
                            eventTime,
                            "ReferenceClient replaced historical event")
                    ],
                    cancellationToken).ConfigureAwait(false),
                "replace event");
            AssertGood(
                await client.UpdateEventsAsync(
                    notifierId,
                    filter,
                    [
                        CreateEvent(
                            generatedEventId,
                            notifierId,
                            eventTime,
                            "ReferenceClient updated historical event")
                    ],
                    cancellationToken).ConfigureAwait(false),
                "update event");
            AssertGood(
                await client.DeleteEventsAsync(
                    notifierId,
                    [generatedEventId],
                    cancellationToken).ConfigureAwait(false),
                "delete event");
            Console.WriteLine("Historical event CRUD completed; generated EventId={0}.", generatedEventId);
        }

        private static async ValueTask DemonstrateCapturedServerEventAsync(
            HistoryClient client,
            NodeId notifierId,
            EventFilter filter,
            CancellationToken cancellationToken)
        {
            DateTime startTime = DateTime.UtcNow.AddSeconds(-1);
            var triggerNodeId = new NodeId(
                EventTriggerNodeName,
                notifierId.NamespaceIndex);
            WriteResponse response = await client.Session.WriteAsync(
                null,
                [
                    new WriteValue
                    {
                        NodeId = triggerNodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(Environment.TickCount))
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != 1 ||
                StatusCode.IsBad(response.Results[0]))
            {
                StatusCode status = response.Results.Count == 1
                    ? response.Results[0]
                    : StatusCodes.BadUnexpectedError;
                throw new ServiceResultException(
                    status,
                    "Writing the ReferenceServer event trigger failed.");
            }

            bool found = false;
            for (int attempt = 0; attempt < 20 && !found; attempt++)
            {
                await foreach (HistoryEventFieldList historicalEvent in
                    client.ReadEventsAsync(
                        notifierId,
                        startTime,
                        DateTime.UtcNow.AddSeconds(1),
                        filter,
                        cancellationToken: cancellationToken)
                        .ConfigureAwait(false))
                {
                    if (historicalEvent.EventFields.Count > 4 &&
                        historicalEvent.EventFields[4].TryGetValue(
                            out LocalizedText message) &&
                        message.Text?.Contains(
                            "Trigger event",
                            StringComparison.Ordinal) == true)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(50),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            if (!found)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    "The server-reported event was not captured in event history.");
            }
            Console.WriteLine("Server-reported event was captured automatically.");
        }

        private static EventFilter CreateEventFilter()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventType,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.SourceNode,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Time,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            return filter;
        }

        private static void ValidateDiscovery(
            HistoryServerCapabilitiesInfo capabilities,
            HistoricalConformanceInfo conformance,
            HistoricalDataConfigurationInfo dataConfiguration,
            HistoricalDataConfigurationInfo structuredConfiguration,
            HistoricalEventConfigurationInfo eventConfiguration)
        {
            if (!capabilities.AccessHistoryData ||
                !capabilities.AccessHistoryEvents ||
                !capabilities.InsertData ||
                !capabilities.ReplaceData ||
                !capabilities.UpdateData ||
                !capabilities.DeleteRaw ||
                !capabilities.DeleteAtTime ||
                !capabilities.InsertAnnotation ||
                !capabilities.InsertEvent ||
                !capabilities.ReplaceEvent ||
                !capabilities.UpdateEvent ||
                !capabilities.DeleteEvent ||
                !capabilities.ServerTimestampSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The ReferenceServer does not expose the complete Historical Access capability set.");
            }
            if (!dataConfiguration.HasConfiguration ||
                !structuredConfiguration.HasConfiguration ||
                !eventConfiguration.HasConfiguration)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The ReferenceServer is missing a historical configuration object.");
            }
            ArrayOf<HistoricalAccessProfileDescriptor> serverProfiles =
                HistoricalAccessProfileCatalog.GetProfiles(
                    HistoricalAccessProfileSide.Server);
            if (conformance.ServerProfiles.Count !=
                serverProfiles.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The ReferenceServer does not advertise all 15 Historical Access Server facets.");
            }
            for (int i = 0; i < serverProfiles.Count; i++)
            {
                HistoricalAccessProfileDescriptor profile =
                    serverProfiles[i];
                if (!conformance.ServerProfiles.Contains(
                        profile.ProfileUri))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        $"The ReferenceServer is missing profile {profile.ProfileUri}.");
                }
                foreach (string unitName in
                    profile.MandatoryConformanceUnits)
                {
                    if (!conformance.ConformanceUnits.Contains(
                            new QualifiedName(unitName)))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            $"The ReferenceServer is missing conformance unit {unitName}.");
                    }
                }
            }
            foreach (HistoricalAggregateFunctionDescriptor aggregate in
                HistoricalAggregateFunctionCatalog.AllFunctions)
            {
                if (!conformance.ConformanceUnits.Contains(
                        new QualifiedName(
                            aggregate.ServerConformanceUnit)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        $"The ReferenceServer is missing aggregate unit {aggregate.ServerConformanceUnit}.");
                }
            }
        }

        private static HistoryEventFieldList CreateEvent(
            ByteString eventId,
            NodeId sourceNode,
            DateTime eventTime,
            string message)
        {
            return new HistoryEventFieldList
            {
                EventFields =
                [
                    new Variant(eventId),
                    new Variant(ObjectTypeIds.BaseEventType),
                    new Variant(sourceNode),
                    new Variant((DateTimeUtc)eventTime),
                    new Variant(new LocalizedText(message))
                ]
            };
        }

        private static DataValue CreateHistoricalValue(int value, DateTime timestamp)
        {
            return new DataValue(
                Variant.From(value),
                StatusCodes.Good,
                sourceTimestamp: timestamp,
                serverTimestamp: timestamp);
        }

        private static DataValue CreateStructuredValue(
            ushort namespaceIndex,
            string key,
            DateTime timestamp,
            double value)
        {
            // IDE0001 is suppressed: "KeyValuePair" is ambiguous between Opc.Ua.KeyValuePair
            // and System.Collections.Generic.KeyValuePair in this file (CS0104), so the
            // qualification cannot be simplified away.
#pragma warning disable IDE0001
            var pair = new Opc.Ua.KeyValuePair
            {
                Key = new QualifiedName(key, namespaceIndex),
                Value = Variant.From(value)
            };
#pragma warning restore IDE0001
            return new DataValue(
                new Variant(new ExtensionObject(pair)),
                StatusCodes.Good,
                timestamp,
                timestamp);
        }

        // IDE0001 is suppressed: "KeyValuePair" is ambiguous between Opc.Ua.KeyValuePair
        // and System.Collections.Generic.KeyValuePair in this file (CS0104), so the
        // qualification cannot be simplified away.
#pragma warning disable IDE0001
        private static Opc.Ua.KeyValuePair ReadStructuredValue(
            DataValue value)
        {
            if (value.WrappedValue.TryGetValue(
                    out ExtensionObject extension) &&
                extension.TryGetValue(
                    out Opc.Ua.KeyValuePair? pair) &&
                pair != null)
            {
                return pair;
            }
#pragma warning restore IDE0001
            throw new ServiceResultException(
                StatusCodes.BadDecodingError,
                "Structured history did not contain a KeyValuePair.");
        }

        private static async ValueTask<int> CountAsync<T>(
            IAsyncEnumerable<T> values,
            CancellationToken cancellationToken)
        {
            int count = 0;
            await foreach (T _ in values.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
            return count;
        }

        private static void AssertGood(
            ArrayOf<StatusCode> statuses,
            string operation)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (StatusCode.IsBad(statuses[i]))
                {
                    throw new ServiceResultException(
                        statuses[i],
                        $"Historical Access sample failed to {operation}.");
                }
            }
        }

        private static void AssertGood(
            StatusCode status,
            string operation)
        {
            if (StatusCode.IsBad(status))
            {
                throw new ServiceResultException(
                    status,
                    $"Historical Access sample failed to {operation}.");
            }
        }

        private const string ReferenceServerNamespaceUri =
            "http://opcfoundation.org/Quickstarts/ReferenceServer";

        private const string DataNodeName = "Scalar_Static_Int32";

        private const string StructuredNodeName =
            "Historical_KeyValuePairs";

        private const string EventNotifierName = "CTT";
        private const string EventTriggerNodeName = "NodeIds_Events_TriggerNode01";
    }
}
