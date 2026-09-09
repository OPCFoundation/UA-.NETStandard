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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Extended <see cref="HistoryClient"/> surface covering at-time
    /// reads, processed (aggregate) reads, annotations, and discovery
    /// (server capabilities + per-variable historical-data configuration).
    /// </summary>
    public sealed partial class HistoryClient
    {
        /// <summary>
        /// Reads the value of <paramref name="nodeId"/> at the supplied
        /// timestamps (Part 11 §5.2.6.6 ReadAtTimeDetails).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="times"/> is <c>null</c>.</exception>
        public IAsyncEnumerable<DataValue> ReadAtTimeAsync(
            NodeId nodeId,
            ArrayOf<DateTime> times,
            bool useSimpleBounds = false,
            TimestampsToReturn? timestampsToReturn = null,
            HistoryReadNodeOptions? nodeOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (times.IsNull)
            {
                throw new ArgumentNullException(nameof(times));
            }
            return ReadAtTimeIteratorAsync(
                nodeId, BuildAtTimeDetails(times, useSimpleBounds),
                ResolveTimestamps(timestampsToReturn, nameof(timestampsToReturn)),
                nodeOptions,
                times.Count,
                cancellationToken);
        }

        private async IAsyncEnumerable<DataValue> ReadAtTimeIteratorAsync(
            NodeId nodeId,
            ExtensionObject details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadNodeOptions? nodeOptions,
            int expectedValueCount,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int returnedValueCount = 0;
            await foreach (DataValue v in ReadDetailsAsync(
                nodeId,
                details,
                timestampsToReturn,
                DecodeHistoryData,
                nodeOptions,
                cancellationToken)
                .ConfigureAwait(false))
            {
                if (returnedValueCount >= expectedValueCount)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "ReadAtTime returned more values than requested.");
                }
                returnedValueCount++;
                yield return v;
            }
            if (returnedValueCount != expectedValueCount)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "ReadAtTime returned fewer values than requested.");
            }
        }

        private static ExtensionObject BuildAtTimeDetails(
            ArrayOf<DateTime> times,
            bool useSimpleBounds)
        {
            var typed = new DateTimeUtc[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                typed[i] = times[i];
            }
            return new ExtensionObject(new ReadAtTimeDetails
            {
                ReqTimes = typed,
                UseSimpleBounds = useSimpleBounds
            });
        }

        /// <summary>
        /// Reads processed (aggregate) values of <paramref name="nodeId"/>
        /// for the time range using <paramref name="aggregateFunctionId"/>
        /// (Part 11 §5.2.6 ReadProcessedDetails).
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public IAsyncEnumerable<DataValue> ReadProcessedAsync(
            NodeId nodeId,
            NodeId aggregateFunctionId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            AggregateConfiguration? configuration = null,
            TimestampsToReturn? timestampsToReturn = null,
            HistoryReadNodeOptions? nodeOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (nodeId.IsNull)
            {
                throw new ArgumentNullException(nameof(nodeId));
            }
            if (aggregateFunctionId.IsNull)
            {
                throw new ArgumentNullException(nameof(aggregateFunctionId));
            }
            if (double.IsNaN(processingInterval) ||
                double.IsInfinity(processingInterval) ||
                processingInterval < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processingInterval));
            }
            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                AggregateType = new NodeId[] { aggregateFunctionId },
                AggregateConfiguration = configuration ??
                    new AggregateConfiguration
                    {
                        UseServerCapabilitiesDefaults = true
                    }
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
        /// Reads annotations on a historizing variable (Part 11 §5.2.7).
        /// Translates <paramref name="variableId"/> to its
        /// <c>Annotations</c> property NodeId via TranslateBrowsePaths
        /// before issuing the read.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public IAsyncEnumerable<Annotation> ReadAnnotationsAsync(
            NodeId variableId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            CancellationToken cancellationToken = default)
        {
            if (variableId.IsNull)
            {
                throw new ArgumentNullException(nameof(variableId));
            }
            return ReadAnnotationsIteratorAsync(
                variableId,
                startTime,
                endTime,
                maxValuesPerNode,
                cancellationToken);
        }

        private async IAsyncEnumerable<Annotation> ReadAnnotationsIteratorAsync(
            NodeId variableId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NodeId annotationsNode = await ResolveAnnotationsPropertyAsync(
                variableId, cancellationToken).ConfigureAwait(false);
            if (annotationsNode.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadHistoryOperationUnsupported,
                    "The variable does not expose an Annotations property.");
            }

            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode == 0
                    ? Options.DefaultMaxValuesPerNode
                    : maxValuesPerNode,
                ReturnBounds = false
            };

            await foreach (DataValue v in ReadDetailsAsync(
                annotationsNode,
                new ExtensionObject(details),
                TimestampsToReturn.Source,
                DecodeHistoryData,
                nodeOptions: null,
                cancellationToken).ConfigureAwait(false))
            {
                if (v.WrappedValue.TryGetValue(out ExtensionObject ext) &&
                    !ext.IsNull &&
                    ext.TryGetValue(out Annotation? annotation))
                {
                    yield return annotation;
                    continue;
                }
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Historical annotation data did not contain an Annotation.");
            }
        }

        /// <summary>
        /// Inserts, replaces or updates a single annotation on
        /// <paramref name="variableId"/>.
        /// </summary>
        public async ValueTask<StatusCode> WriteAnnotationAsync(
            NodeId variableId,
            DateTime annotationTime,
            string message,
            string? userName = null,
            PerformUpdateType performUpdate = PerformUpdateType.Insert,
            CancellationToken cancellationToken = default)
        {
            var annotation = new Annotation
            {
                Message = message,
                UserName = userName,
                AnnotationTime = annotationTime
            };

            ArrayOf<StatusCode> statuses = await WriteAnnotationsAsync(
                variableId,
                [annotation],
                performUpdate,
                cancellationToken).ConfigureAwait(false);

            return statuses.Count > 0 ? statuses[0] : StatusCodes.BadInternalError;
        }

        /// <summary>
        /// Inserts, replaces, updates, or removes a batch of annotations in one
        /// <c>UpdateStructureDataDetails</c> request.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public async ValueTask<ArrayOf<StatusCode>> WriteAnnotationsAsync(
            NodeId variableId,
            ArrayOf<Annotation> annotations,
            PerformUpdateType performUpdate = PerformUpdateType.Insert,
            CancellationToken cancellationToken = default)
        {
            if (variableId.IsNull)
            {
                throw new ArgumentNullException(nameof(variableId));
            }
            if (annotations.IsNull)
            {
                throw new ArgumentNullException(nameof(annotations));
            }

            NodeId annotationsNode = await ResolveAnnotationsPropertyAsync(
                variableId, cancellationToken).ConfigureAwait(false);
            if (annotationsNode.IsNull)
            {
                var statuses = new StatusCode[annotations.Count];
                for (int i = 0; i < statuses.Length; i++)
                {
                    statuses[i] = StatusCodes.BadNodeIdUnknown;
                }
                return statuses.ToArrayOf();
            }

            var values = new DataValue[annotations.Count];
            for (int i = 0; i < values.Length; i++)
            {
                Annotation annotation = annotations[i] ??
                    throw new ArgumentException(
                        "The annotations collection contains a null value.",
                        nameof(annotations));
                values[i] = new DataValue(
                    new Variant(new ExtensionObject(annotation)),
                    StatusCodes.Good,
                    sourceTimestamp: annotation.AnnotationTime,
                    serverTimestamp: DateTimeUtc.MinValue);
            }

            return await UpdateStructureDataAsync(
                annotationsNode,
                performUpdate,
                values.ToArrayOf(),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the annotation on <paramref name="variableId"/> with
        /// the supplied <paramref name="annotationTime"/>.
        /// </summary>
        public ValueTask<StatusCode> DeleteAnnotationAsync(
            NodeId variableId,
            DateTime annotationTime,
            CancellationToken cancellationToken = default)
        {
            return WriteAnnotationAsync(
                variableId,
                annotationTime,
                string.Empty,
                userName: null,
                performUpdate: PerformUpdateType.Remove,
                cancellationToken);
        }

        /// <summary>
        /// Reads the server-wide
        /// <c>HistoryServerCapabilities</c> snapshot.
        /// </summary>
        public async ValueTask<HistoryServerCapabilitiesInfo> GetServerCapabilitiesAsync(
            CancellationToken cancellationToken = default)
        {
            var nodes = new NodeId[]
            {
                VariableIds.HistoryServerCapabilities_AccessHistoryDataCapability,
                VariableIds.HistoryServerCapabilities_AccessHistoryEventsCapability,
                VariableIds.HistoryServerCapabilities_MaxReturnDataValues,
                VariableIds.HistoryServerCapabilities_MaxReturnEventValues,
                VariableIds.HistoryServerCapabilities_InsertDataCapability,
                VariableIds.HistoryServerCapabilities_ReplaceDataCapability,
                VariableIds.HistoryServerCapabilities_UpdateDataCapability,
                VariableIds.HistoryServerCapabilities_DeleteRawCapability,
                VariableIds.HistoryServerCapabilities_DeleteAtTimeCapability,
                VariableIds.HistoryServerCapabilities_InsertAnnotationCapability,
                VariableIds.HistoryServerCapabilities_ServerTimestampSupported,
                VariableIds.HistoryServerCapabilities_InsertEventCapability,
                VariableIds.HistoryServerCapabilities_ReplaceEventCapability,
                VariableIds.HistoryServerCapabilities_UpdateEventCapability,
                VariableIds.HistoryServerCapabilities_DeleteEventCapability
            };

            DataValue[] values = await BatchReadValueAsync(nodes, cancellationToken)
                .ConfigureAwait(false);

            return new HistoryServerCapabilitiesInfo
            {
                AccessHistoryData = ReadBool(values[0]),
                AccessHistoryEvents = ReadBool(values[1]),
                MaxReturnDataValues = ReadUInt(values[2]),
                MaxReturnEventValues = ReadUInt(values[3]),
                InsertData = ReadBool(values[4]),
                ReplaceData = ReadBool(values[5]),
                UpdateData = ReadBool(values[6]),
                DeleteRaw = ReadBool(values[7]),
                DeleteAtTime = ReadBool(values[8]),
                InsertAnnotation = ReadBool(values[9]),
                ServerTimestampSupported = ReadBool(values[10]),
                InsertEvent = ReadBool(values[11]),
                ReplaceEvent = ReadBool(values[12]),
                UpdateEvent = ReadBool(values[13]),
                DeleteEvent = ReadBool(values[14])
            };
        }

        /// <summary>
        /// Reads the historical entries published in ServerProfileArray and
        /// ConformanceUnits.
        /// </summary>
        public async ValueTask<HistoricalConformanceInfo> GetConformanceInfoAsync(
            CancellationToken cancellationToken = default)
        {
            DataValue[] values = await BatchReadValueAsync(
                [
                    VariableIds.Server_ServerCapabilities_ServerProfileArray,
                    VariableIds.Server_ServerCapabilities_ConformanceUnits
                ],
                cancellationToken).ConfigureAwait(false);
            ArrayOf<string> profiles =
                values[0].WrappedValue.TryGetValue(
                    out ArrayOf<string> serverProfiles)
                ? FilterHistoricalProfiles(serverProfiles)
                : [];
            ArrayOf<QualifiedName> units =
                values[1].WrappedValue.TryGetValue(
                    out ArrayOf<QualifiedName> conformanceUnits)
                ? FilterHistoricalConformanceUnits(conformanceUnits)
                : [];
            return new HistoricalConformanceInfo
            {
                ServerProfiles = profiles,
                ConformanceUnits = units
            };
        }

        /// <summary>
        /// Reads the per-variable <c>HistoricalDataConfigurationType</c>
        /// companion object. Returns a snapshot with
        /// <see cref="HistoricalDataConfigurationInfo.HasConfiguration"/>=<c>false</c>
        /// when the variable does not expose a configuration object.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<HistoricalDataConfigurationInfo> GetConfigurationAsync(
            NodeId variableId,
            CancellationToken cancellationToken = default)
        {
            if (variableId.IsNull)
            {
                throw new ArgumentNullException(nameof(variableId));
            }
            // The companion object lives under <variable>/HA Configuration,
            // linked via HasHistoricalConfiguration (i=56) per Part 11 §5.2.3.
            NodeId configNode = await TranslateBrowseChildAsync(
                variableId,
                BrowseNames.HAConfiguration,
                ReferenceTypeIds.HasHistoricalConfiguration,
                cancellationToken)
                .ConfigureAwait(false);
            if (configNode.IsNull)
            {
                return new HistoricalDataConfigurationInfo();
            }

            // Resolve all known child properties via browse paths.
            string[] childNames =
            [
                BrowseNames.Stepped,
                BrowseNames.Definition,
                BrowseNames.MaxTimeInterval,
                BrowseNames.MinTimeInterval,
                BrowseNames.ExceptionDeviation,
                BrowseNames.ExceptionDeviationFormat,
                BrowseNames.StartOfArchive,
                BrowseNames.StartOfOnlineArchive,
                BrowseNames.ServerTimestampSupported,
                BrowseNames.MaxTimeStoredValues,
                BrowseNames.MaxCountStoredValues
            ];
            var childNodes = new NodeId[childNames.Length];
            for (int i = 0; i < childNames.Length; i++)
            {
                childNodes[i] = await TranslateBrowseChildAsync(
                    configNode, childNames[i], ReferenceTypeIds.HasProperty, cancellationToken)
                    .ConfigureAwait(false);
            }

            DataValue[] values = await BatchReadValueAsync(childNodes, cancellationToken)
                .ConfigureAwait(false);

            // The AggregateConfiguration is a nested object (HasComponent) whose
            // PercentDataGood / PercentDataBad / TreatUncertainAsBad /
            // UseSlopedExtrapolation properties a client reads to reproduce the
            // server's aggregate results under UseServerCapabilitiesDefaults.
            AggregateConfiguration? aggregateConfiguration = await ReadAggregateConfigurationAsync(
                configNode, cancellationToken).ConfigureAwait(false);

            return new HistoricalDataConfigurationInfo
            {
                HasConfiguration = true,
                Stepped = !childNodes[0].IsNull ? ReadBool(values[0]) : null,
                Definition = !childNodes[1].IsNull ? ReadString(values[1]) : null,
                MaxTimeInterval = !childNodes[2].IsNull ? ReadDouble(values[2]) : null,
                MinTimeInterval = !childNodes[3].IsNull ? ReadDouble(values[3]) : null,
                ExceptionDeviation = !childNodes[4].IsNull ? ReadDouble(values[4]) : null,
                ExceptionDeviationFormat = !childNodes[5].IsNull
                    ? ReadExceptionDeviationFormat(values[5])
                    : null,
                StartOfArchive = !childNodes[6].IsNull
                    ? ReadDateTimeUtc(values[6]).ToDateTime()
                    : null,
                StartOfOnlineArchive = !childNodes[7].IsNull
                    ? ReadDateTimeUtc(values[7]).ToDateTime()
                    : null,
                ServerTimestampSupported = !childNodes[8].IsNull
                    ? ReadBool(values[8])
                    : null,
                MaxTimeStoredValues = !childNodes[9].IsNull
                    ? ReadDouble(values[9])
                    : null,
                MaxCountStoredValues = !childNodes[10].IsNull
                    ? ReadUInt(values[10])
                    : null,
                AggregateConfiguration = aggregateConfiguration
            };
        }

        /// <summary>
        /// Reads the <c>HistoricalEventConfigurationType</c> companion of an
        /// event notifier.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<HistoricalEventConfigurationInfo>
            GetEventConfigurationAsync(
                NodeId notifierId,
                CancellationToken cancellationToken = default)
        {
            if (notifierId.IsNull)
            {
                throw new ArgumentNullException(nameof(notifierId));
            }
            NodeId configurationNode = await TranslateBrowseChildAsync(
                notifierId,
                BrowseNames.HAConfiguration,
                ReferenceTypeIds.HasHistoricalConfiguration,
                cancellationToken).ConfigureAwait(false);
            if (configurationNode.IsNull)
            {
                return new HistoricalEventConfigurationInfo();
            }

            NodeId eventTypesNode = await TranslateBrowseChildAsync(
                configurationNode,
                BrowseNames.EventTypes,
                ReferenceTypeIds.HasComponent,
                cancellationToken).ConfigureAwait(false);
            NodeId startOfArchiveNode = await TranslateBrowseChildAsync(
                configurationNode,
                BrowseNames.StartOfArchive,
                ReferenceTypeIds.HasProperty,
                cancellationToken).ConfigureAwait(false);
            NodeId startOfOnlineArchiveNode = await TranslateBrowseChildAsync(
                configurationNode,
                BrowseNames.StartOfOnlineArchive,
                ReferenceTypeIds.HasProperty,
                cancellationToken).ConfigureAwait(false);
            NodeId sortByEventFieldsNode = await TranslateBrowseChildAsync(
                configurationNode,
                BrowseNames.SortByEventFields,
                ReferenceTypeIds.HasProperty,
                cancellationToken).ConfigureAwait(false);

            DataValue[] values = await BatchReadValueAsync(
                [
                    startOfArchiveNode,
                    startOfOnlineArchiveNode,
                    sortByEventFieldsNode
                ],
                cancellationToken).ConfigureAwait(false);
            var eventTypes = new List<NodeId>();
            if (!eventTypesNode.IsNull)
            {
                ArrayOf<ReferenceDescription> references = await Session
                    .FetchReferencesAsync(eventTypesNode, cancellationToken)
                    .ConfigureAwait(false);
                for (int i = 0; i < references.Count; i++)
                {
                    ReferenceDescription reference = references[i];
                    if (reference.IsForward &&
                        reference.ReferenceTypeId == ReferenceTypeIds.Organizes)
                    {
                        var typeId = ExpandedNodeId.ToNodeId(
                            reference.NodeId,
                            Session.NamespaceUris);
                        if (!typeId.IsNull)
                        {
                            eventTypes.Add(typeId);
                        }
                    }
                }
            }

            return new HistoricalEventConfigurationInfo
            {
                HasConfiguration = true,
                EventTypes = eventTypes.ToArrayOf(),
                StartOfArchive = !startOfArchiveNode.IsNull
                    ? ReadDateTimeUtc(values[0]).ToDateTime()
                    : null,
                StartOfOnlineArchive = !startOfOnlineArchiveNode.IsNull
                    ? ReadDateTimeUtc(values[1]).ToDateTime()
                    : null,
                SortByEventFields = !sortByEventFieldsNode.IsNull &&
                    values[2].WrappedValue.TryGetValue(
                        out ArrayOf<SimpleAttributeOperand> sortFields,
                        Session.MessageContext)
                    ? sortFields
                    : []
            };
        }

        /// <summary>
        /// Reads the <c>AggregateConfiguration</c> object (PercentDataGood,
        /// PercentDataBad, TreatUncertainAsBad, UseSlopedExtrapolation) beneath a
        /// <c>HistoricalDataConfiguration</c> companion object, or <c>null</c>
        /// when the object is not exposed.
        /// </summary>
        private async ValueTask<AggregateConfiguration?> ReadAggregateConfigurationAsync(
            NodeId configNode,
            CancellationToken cancellationToken)
        {
            NodeId aggregateNode = await TranslateBrowseChildAsync(
                configNode,
                BrowseNames.AggregateConfiguration,
                ReferenceTypeIds.HasComponent,
                cancellationToken)
                .ConfigureAwait(false);
            if (aggregateNode.IsNull)
            {
                return null;
            }

            string[] propertyNames =
            [
                BrowseNames.PercentDataGood,
                BrowseNames.PercentDataBad,
                BrowseNames.TreatUncertainAsBad,
                BrowseNames.UseSlopedExtrapolation
            ];
            var propertyNodes = new NodeId[propertyNames.Length];
            for (int i = 0; i < propertyNames.Length; i++)
            {
                propertyNodes[i] = await TranslateBrowseChildAsync(
                    aggregateNode, propertyNames[i], ReferenceTypeIds.HasProperty, cancellationToken)
                    .ConfigureAwait(false);
            }

            DataValue[] values = await BatchReadValueAsync(propertyNodes, cancellationToken)
                .ConfigureAwait(false);

            return new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = false,
                PercentDataGood = ReadByte(values[0]),
                PercentDataBad = ReadByte(values[1]),
                TreatUncertainAsBad = ReadBool(values[2]),
                UseSlopedExtrapolation = ReadBool(values[3])
            };
        }

        private ValueTask<NodeId> ResolveAnnotationsPropertyAsync(
            NodeId variableId, CancellationToken cancellationToken)
        {
            return TranslateBrowseChildAsync(
                variableId, BrowseNames.Annotations, ReferenceTypeIds.HasProperty, cancellationToken);
        }

        private async ValueTask<NodeId> TranslateBrowseChildAsync(
            NodeId startNode,
            string browseName,
            NodeId referenceType,
            CancellationToken cancellationToken)
        {
            var path = new BrowsePath
            {
                StartingNode = startNode,
                RelativePath = new RelativePath
                {
                    Elements = new RelativePathElement[]
                    {
                        new()
                        {
                            ReferenceTypeId = referenceType,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName)
                        }
                    }
                }
            };

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, [path], cancellationToken)
                .ConfigureAwait(false);

            if (response.Results.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "TranslateBrowsePaths returned a result count that does not match the request.");
            }
            BrowsePathResult result = response.Results[0];
            if (StatusCode.IsBad(result.StatusCode))
            {
                return NodeId.Null;
            }
            if (result.Targets.Count == 0)
            {
                return NodeId.Null;
            }
            if (result.Targets.Count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Historical configuration browse path resolved to multiple targets.");
            }
            var resolved = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
            return resolved.IsNull ? NodeId.Null : resolved;
        }

        private async ValueTask<DataValue[]> BatchReadValueAsync(
            IReadOnlyList<NodeId> nodes,
            CancellationToken cancellationToken)
        {
            ReadValueId[] requests = [.. nodes.Select(n => new ReadValueId
            {
                NodeId = n,
                AttributeId = Attributes.Value
            })];

            ReadResponse response = await Session.ReadAsync(
                null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: requests,
                cancellationToken).ConfigureAwait(false);

            if (response.Results.Count != requests.Length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Read returned a result count that does not match the request.");
            }
            return response.Results.ToArray() ??
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Read returned a null result array.");
        }

        private static bool ReadBool(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return false;
            }
            return value.WrappedValue.TryGetValue(out bool v) && v;
        }

        private static uint ReadUInt(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return 0u;
            }
            return value.WrappedValue.TryGetValue(out uint v) ? v : 0u;
        }

        private static byte ReadByte(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return 0;
            }
            return value.WrappedValue.TryGetValue(out byte v) ? v : (byte)0;
        }

        private static double ReadDouble(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return 0d;
            }
            return value.WrappedValue.TryGetValue(out double v) ? v : 0d;
        }

        private static DateTimeUtc ReadDateTimeUtc(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return DateTimeUtc.MinValue;
            }
            return value.WrappedValue.TryGetValue(out DateTimeUtc v) ? v : DateTimeUtc.MinValue;
        }

        private static string? ReadString(DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return null;
            }
            return value.WrappedValue.TryGetValue(out string s) ? s : null;
        }

        private static ExceptionDeviationFormat ReadExceptionDeviationFormat(
            DataValue value)
        {
            if (value.IsNull || StatusCode.IsBad(value.StatusCode))
            {
                return default;
            }
            return value.WrappedValue.TryGetValue(out ExceptionDeviationFormat format)
                ? format
                : default;
        }

        private static ArrayOf<string> FilterHistoricalProfiles(
            ArrayOf<string> profiles)
        {
            var historical = new List<string>();
            for (int i = 0; i < profiles.Count; i++)
            {
                string profile = profiles[i];
                if (profile.Contains(
                    "Historical",
                    StringComparison.OrdinalIgnoreCase))
                {
                    historical.Add(profile);
                }
            }
            return historical.ToArrayOf();
        }

        private static ArrayOf<QualifiedName> FilterHistoricalConformanceUnits(
            ArrayOf<QualifiedName> units)
        {
            var historical = new List<QualifiedName>();
            for (int i = 0; i < units.Count; i++)
            {
                QualifiedName unit = units[i];
                if (unit.Name?.Contains(
                    "Histor",
                    StringComparison.OrdinalIgnoreCase) == true ||
                    unit.Name?.Contains(
                        "Aggregate",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    historical.Add(unit);
                }
            }
            return historical.ToArrayOf();
        }
    }
}
