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
        public IAsyncEnumerable<DataValue> ReadAtTimeAsync(
            NodeId nodeId,
            IReadOnlyList<DateTime> times,
            bool useSimpleBounds = false,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            CancellationToken cancellationToken = default)
        {
            if (times == null)
            {
                throw new ArgumentNullException(nameof(times));
            }
            return ReadAtTimeIteratorAsync(
                nodeId, BuildAtTimeDetails(times, useSimpleBounds),
                timestampsToReturn, cancellationToken);
        }

        private async IAsyncEnumerable<DataValue> ReadAtTimeIteratorAsync(
            NodeId nodeId,
            ExtensionObject details,
            TimestampsToReturn timestampsToReturn,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (DataValue v in ReadDetailsAsync(
                nodeId, details, timestampsToReturn, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return v;
            }
        }

        private static ExtensionObject BuildAtTimeDetails(IReadOnlyList<DateTime> times, bool useSimpleBounds)
        {
            var typed = new DateTimeUtc[times.Count];
            for (int i = 0; i < times.Count; i++)
            {
                typed[i] = times[i];
            }
            return new ExtensionObject(new ReadAtTimeDetails
            {
                ReqTimes = typed,
                UseSimpleBounds = useSimpleBounds,
            });
        }

        /// <summary>
        /// Reads processed (aggregate) values of <paramref name="nodeId"/>
        /// for the time range using <paramref name="aggregateFunctionId"/>
        /// (Part 11 §5.2.6 ReadProcessedDetails).
        /// </summary>
        public async IAsyncEnumerable<DataValue> ReadProcessedAsync(
            NodeId nodeId,
            NodeId aggregateFunctionId,
            DateTime startTime,
            DateTime endTime,
            double processingInterval,
            AggregateConfiguration? configuration = null,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var details = new ReadProcessedDetails
            {
                StartTime = startTime,
                EndTime = endTime,
                ProcessingInterval = processingInterval,
                AggregateType = new NodeId[] { aggregateFunctionId },
                AggregateConfiguration = configuration ??
                    new AggregateConfiguration
                        {
                            UseServerCapabilitiesDefaults = true,
                        },
            };

            await foreach (DataValue v in ReadDetailsAsync(
                nodeId, new ExtensionObject(details), timestampsToReturn, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return v;
            }
        }

        /// <summary>
        /// Reads annotations on a historizing variable (Part 11 §5.2.7).
        /// Translates <paramref name="variableId"/> to its
        /// <c>Annotations</c> property NodeId via TranslateBrowsePaths
        /// before issuing the read.
        /// </summary>
        public async IAsyncEnumerable<Annotation> ReadAnnotationsAsync(
            NodeId variableId,
            DateTime startTime,
            DateTime endTime,
            uint maxValuesPerNode = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId annotationsNode = await ResolveAnnotationsPropertyAsync(
                variableId, cancellationToken).ConfigureAwait(false);
            if (annotationsNode.IsNull)
            {
                yield break;
            }

            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = startTime,
                EndTime = endTime,
                NumValuesPerNode = maxValuesPerNode,
                ReturnBounds = false,
            };

            await foreach (DataValue v in ReadDetailsAsync(
                annotationsNode, new ExtensionObject(details),
                TimestampsToReturn.Source, cancellationToken).ConfigureAwait(false))
            {
                if (v.WrappedValue.TryGetValue(out ExtensionObject ext) &&
                    !ext.IsNull &&
                    ext.TryGetValue(out Annotation? annotation))
                {
                    yield return annotation;
                }
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
            NodeId annotationsNode = await ResolveAnnotationsPropertyAsync(
                variableId, cancellationToken).ConfigureAwait(false);
            if (annotationsNode.IsNull)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            var annotation = new Annotation
            {
                Message = message,
                UserName = userName,
                AnnotationTime = annotationTime,
            };

            var dataValue = new DataValue(
                new Variant(new ExtensionObject(annotation)),
                StatusCodes.Good,
                sourceTimestamp: annotationTime,
                serverTimestamp: DateTimeUtc.MinValue);

            var details = new UpdateStructureDataDetails
            {
                NodeId = annotationsNode,
                PerformInsertReplace = performUpdate,
                UpdateValues = new DataValue[] { dataValue },
            };

            HistoryUpdateResponse response = await Session.HistoryUpdateAsync(
                null, new ExtensionObject[] { new(details) }, cancellationToken)
                .ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return StatusCodes.BadInternalError;
            }
            HistoryUpdateResult result = response.Results[0];
            if (result.OperationResults.Count > 0)
            {
                return result.OperationResults[0];
            }
            return result.StatusCode;
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
            };
        }

        /// <summary>
        /// Reads the per-variable <c>HistoricalDataConfigurationType</c>
        /// companion object. Returns a snapshot with
        /// <see cref="HistoricalDataConfigurationInfo.HasConfiguration"/>=<c>false</c>
        /// when the variable does not expose a configuration object.
        /// </summary>
        public async ValueTask<HistoricalDataConfigurationInfo> GetConfigurationAsync(
            NodeId variableId,
            CancellationToken cancellationToken = default)
        {
            // The companion object lives under <variable>/HA Configuration
            // (BrowseName i=11215 HAConfiguration is the standard name).
            NodeId configNode = await TranslateBrowseChildAsync(
                variableId, BrowseNames.HAConfiguration, ReferenceTypeIds.HasAddIn, cancellationToken)
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
                BrowseNames.StartOfArchive,
                BrowseNames.StartOfOnlineArchive,
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

            return new HistoricalDataConfigurationInfo
            {
                HasConfiguration = true,
                Stepped = !childNodes[0].IsNull ? ReadBool(values[0]) : null,
                Definition = !childNodes[1].IsNull ? ReadString(values[1]) : null,
                MaxTimeInterval = !childNodes[2].IsNull ? ReadDouble(values[2]) : null,
                MinTimeInterval = !childNodes[3].IsNull ? ReadDouble(values[3]) : null,
                ExceptionDeviation = !childNodes[4].IsNull ? ReadDouble(values[4]) : null,
                StartOfArchive = !childNodes[5].IsNull
                    ? ReadDateTimeUtc(values[5]).ToDateTime()
                    : null,
                StartOfOnlineArchive = !childNodes[6].IsNull
                    ? ReadDateTimeUtc(values[6]).ToDateTime()
                    : null,
            };
        }

        private async IAsyncEnumerable<DataValue> ReadDetailsAsync(
            NodeId nodeId,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Tracks the in-flight continuation point. When the enumerator
            // is abandoned mid-iteration (via break/exception/cancellation)
            // we issue a best-effort release in the finally block to avoid
            // server-side continuation-point leaks. R3.1: also detects a
            // buggy server emitting an unbounded sequence of empty pages
            // with a non-empty continuation point.
            ByteString continuationPoint = ByteString.Empty;
            ByteString liveContinuationPoint = ByteString.Empty;
            int emptyPagesInARow = 0;
            try
            {
                while (true)
                {
                    var nodesToRead = new HistoryReadValueId[]
                    {
                        new() { NodeId = nodeId, ContinuationPoint = continuationPoint },
                    };

                    HistoryReadResponse response = await Session.HistoryReadAsync(
                        null,
                        historyReadDetails,
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
                        throw new ServiceResultException(
                            result.StatusCode, "HistoryRead returned a bad status.");
                    }

                    // Capture the server-held CP so the finally block can release it.
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
                        // Server has already released the CP — clear our cleanup marker.
                        liveContinuationPoint = ByteString.Empty;
                        yield break;
                    }

                    // R3.1: malformed-server guard.
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
                    // Best-effort release. Swallow exceptions because the
                    // caller may already be tearing down the session, or
                    // the CP may have expired before this runs.
                    try
                    {
                        var releaseNodes = new HistoryReadValueId[]
                        {
                            new() { NodeId = nodeId, ContinuationPoint = liveContinuationPoint },
                        };
                        _ = await Session.HistoryReadAsync(
                            null,
                            historyReadDetails,
                            timestampsToReturn,
                            releaseContinuationPoints: true,
                            releaseNodes,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // ignore — best-effort cleanup
                    }
                    catch (TaskCanceledException)
                    {
                        // ignore — best-effort cleanup
                    }
                    catch (OperationCanceledException)
                    {
                        // ignore — best-effort cleanup
                    }
                }
            }
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
                            TargetName = new QualifiedName(browseName),
                        },
                    },
                },
            };

            TranslateBrowsePathsToNodeIdsResponse response = await Session
                .TranslateBrowsePathsToNodeIdsAsync(null, [path], cancellationToken)
                .ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                return NodeId.Null;
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
            NodeId resolved = ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, Session.NamespaceUris);
            return resolved.IsNull ? NodeId.Null : resolved;
        }

        private async ValueTask<DataValue[]> BatchReadValueAsync(
            IReadOnlyList<NodeId> nodes,
            CancellationToken cancellationToken)
        {
            ReadValueId[] requests = [.. nodes.Select(n => new ReadValueId
            {
                NodeId = n,
                AttributeId = Attributes.Value,
            })];

            ReadResponse response = await Session.ReadAsync(
                null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: requests,
                cancellationToken).ConfigureAwait(false);

            DataValue[] values = response.Results.ToArray() ?? new DataValue[requests.Length];
            // Defensive: if response.Results was short, fill the rest with BadNoData.
            if (values.Length < requests.Length)
            {
                int original = values.Length;
                Array.Resize(ref values, requests.Length);
                for (int i = original; i < values.Length; i++)
                {
                    values[i] = DataValue.FromStatusCode(StatusCodes.BadNoData);
                }
            }
            return values;
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
    }
}
