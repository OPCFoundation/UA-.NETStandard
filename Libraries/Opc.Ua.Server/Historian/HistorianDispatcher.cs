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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Central dispatch helper that bridges <see cref="AsyncCustomNodeManager"/>
    /// history hooks to the <see cref="IHistorianProvider"/> registry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatcher is stateless apart from continuation-point storage,
    /// which lives in the session via
    /// <see cref="Opc.Ua.Server.Session.SaveHistoryContinuationPoint"/> /
    /// <see cref="Opc.Ua.Server.Session.RestoreHistoryContinuationPoint"/>.
    /// </para>
    /// </remarks>
    public static class HistorianDispatcher
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="node"/> is the
        /// <c>Annotations</c> property of a historizing variable
        /// (Part 11 §5.2.7). The framework routes HistoryRead and
        /// HistoryUpdate operations against this property to the
        /// <see cref="IHistorianAnnotationProvider"/> on the parent
        /// variable.
        /// </summary>
        public static bool IsAnnotationsProperty(NodeState? node)
        {
            return node is PropertyState property &&
                string.Equals(property.BrowseName.Name, BrowseNames.Annotations, StringComparison.Ordinal) &&
                property.BrowseName.NamespaceIndex == 0 &&
                property.Parent is BaseVariableState;
        }

        /// <summary>
        /// Returns the parent variable node of a node identified by
        /// <see cref="IsAnnotationsProperty(NodeState?)"/>.
        /// </summary>
        public static BaseVariableState? GetAnnotationsParent(NodeState? node)
        {
            return (node as BaseInstanceState)?.Parent as BaseVariableState;
        }

        /// <summary>
        /// Resolves the provider for a given node using the node-manager
        /// override first, then the server-wide registry.
        /// </summary>
        public static IHistorianProvider? ResolveProvider(
            IServerInternal server,
            NodeState node,
            IHistorianProvider? nodeManagerOverride)
        {
            if (nodeManagerOverride != null)
            {
                return nodeManagerOverride;
            }
            if (server is IHistorianRegistryProvider registry)
            {
                return registry.HistorianRegistry.Resolve(node.NodeId);
            }
            return null;
        }

        /// <summary>
        /// Dispatches a single raw / modified history read against a
        /// historizing variable. Updates <paramref name="result"/> and
        /// returns the status code that should be assigned to the caller's
        /// errors slot.
        /// </summary>
        public static ValueTask<ServiceResult> DispatchRawReadAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            HistorianContinuationState? state = TryRestoreContinuation(
                systemContext, nodeToRead, expectedKind: details.IsReadModified
                    ? HistorianReadKind.Modified
                    : HistorianReadKind.Raw);

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            return details.IsReadModified
                ? ReadModifiedPageAsync(
                    systemContext, provider, node, nodeToRead, details,
                    timestampsToReturn, result, state, opContext, cancellationToken)
                : ReadRawPageAsync(
                    systemContext, provider, node, nodeToRead, details,
                    timestampsToReturn, result, state, opContext, cancellationToken);
        }

        /// <summary>
        /// Dispatches a single update-data history operation
        /// (Insert / Replace / Update).
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchUpdateDataAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            UpdateDataDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianDataProvider data)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistoryUpdateType updateType = MapPerformUpdate(details.PerformInsertReplace);
            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                updateType);

            ArrayOf<DataValue> values = details.UpdateValues;
            IList<StatusCode> statuses = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => await data.InsertAsync(opContext, node.NodeId, ToList(values), cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Replace => await data.ReplaceAsync(opContext, node.NodeId, ToList(values), cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Update => await data.UpdateAsync(opContext, node.NodeId, ToList(values), cancellationToken).ConfigureAwait(false),
                _ => RepeatStatus(StatusCodes.BadInvalidArgument, values.Count),
            };

            result.OperationResults = ToStatusArray(statuses);

            ReportAuditUpdateData(systemContext, details, statuses);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a single delete-raw history operation.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchDeleteRawAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            DeleteRawModifiedDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianDataProvider data)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            StatusCode status = await data.DeleteRawAsync(
                opContext,
                node.NodeId,
                details.StartTime,
                details.EndTime,
                details.IsDeleteModified,
                cancellationToken).ConfigureAwait(false);

            result.StatusCode = status;
            ReportAuditDeleteRaw(systemContext, details, status);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a single delete-at-time history operation.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchDeleteAtTimeAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            DeleteAtTimeDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianDataProvider data)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            ArrayOf<DateTimeUtc> times = details.ReqTimes;
            var typed = new List<DateTimeUtc>(times.Count);
            for (int i = 0; i < times.Count; i++)
            {
                typed.Add(times[i]);
            }

            IList<StatusCode> statuses = await data.DeleteAtTimeAsync(
                opContext, node.NodeId, typed, cancellationToken).ConfigureAwait(false);

            result.OperationResults = ToStatusArray(statuses);
            ReportAuditDeleteAtTime(systemContext, details, statuses);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a single processed (aggregate) history read with the
        /// standard streaming fallback when the provider does not
        /// implement <see cref="IHistorianProcessedProvider"/>.
        /// </summary>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via SaveHistoryContinuationPoint or disposed inline by EmitProcessedPage.")]
        public static async ValueTask<ServiceResult> DispatchProcessedReadAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadProcessedDetails details,
            NodeId aggregateId,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            HistorianContinuationState? cont = TryRestoreContinuation(
                systemContext, nodeToRead, HistorianReadKind.Processed);

            // Resume from buffered output if a continuation already exists.
            if (cont?.BufferedProcessedOutputs is { } buffered)
            {
                EmitProcessedPage(cont, result, nodeToRead, timestampsToReturn, systemContext);
                return ServiceResult.Good;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            AggregateConfiguration config = details.AggregateConfiguration;
            if (config == null || config.UseServerCapabilitiesDefaults)
            {
                config = systemContext.Server != null
                    ? systemContext.Server.AggregateManager.GetDefaultConfiguration(node.NodeId)
                    : new AggregateConfiguration
                    {
                        PercentDataBad = 100,
                        PercentDataGood = 100,
                        TreatUncertainAsBad = false,
                        UseSlopedExtrapolation = false,
                        UseServerCapabilitiesDefaults = false,
                    };
            }

            var processedRequest = new HistorianProcessedReadRequest
            {
                NodeId = node.NodeId,
                AggregateId = aggregateId,
                StartTime = details.StartTime,
                EndTime = details.EndTime,
                ProcessingInterval = details.ProcessingInterval,
                Configuration = config,
            };

            // Native push-down path
            if (provider is IHistorianProcessedProvider native)
            {
                HistorianResumeToken token = TryDecodeContinuation(nodeToRead);
                HistorianPage<DataValue> page = await native.ReadProcessedAsync(
                    opContext, processedRequest, token, cancellationToken).ConfigureAwait(false);

                FillHistoryData(result, page.Values, nodeToRead, timestampsToReturn);
                ApplyContinuation(systemContext, node.NodeId, result, page);
                return ServiceResult.Good;
            }

            // Framework streaming fallback through AggregateManager
            IServerInternal? serverInternal = systemContext.Server;
            if (serverInternal == null)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            IAggregateCalculator? calculator = serverInternal.AggregateManager.CreateCalculator(
                aggregateId,
                details.StartTime,
                details.EndTime,
                details.ProcessingInterval,
                false,
                config);

            if (calculator == null)
            {
                return StatusCodes.BadAggregateNotSupported;
            }

            if (provider is not IHistorianDataProvider raw)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            var values = new List<DataValue>();
            var rawRequest = new HistorianRawReadRequest
            {
                NodeId = node.NodeId,
                StartTime = details.StartTime <= details.EndTime ? details.StartTime : details.EndTime,
                EndTime = details.StartTime <= details.EndTime ? details.EndTime : details.StartTime,
                MaxValues = 0,
                IsForward = true,
                ReturnBounds = true,
            };

            HistorianResumeToken token2 = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    opContext, rawRequest, token2, cancellationToken).ConfigureAwait(false);

                foreach (HistoricalDataValue sample in page.Values)
                {
                    if (!calculator.QueueRawValue(sample.Value))
                    {
                        FlushCalculator(calculator, values, partial: false);
                        if (values.Count > kMaxProcessedBufferedOutputs)
                        {
                            return StatusCodes.BadTooManyOperations;
                        }
                    }
                }

                if (page.IsFinal)
                {
                    break;
                }
                token2 = page.NextToken;
            }

            FlushCalculator(calculator, values, partial: true);
            if (values.Count > kMaxProcessedBufferedOutputs)
            {
                return StatusCodes.BadTooManyOperations;
            }

            // Buffer the entire output set and emit the first page from it.
            HistorianContinuationState state = new()
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                NodeId = node.NodeId,
                Kind = HistorianReadKind.Processed,
                ResumeToken = default,
                ProcessedRequest = processedRequest,
                TimestampsToReturn = timestampsToReturn,
                IndexRange = nodeToRead.ParsedIndexRange,
                DataEncoding = nodeToRead.DataEncoding,
                BufferedProcessedOutputs = values,
                BufferedProcessedOffset = 0,
            };
            EmitProcessedPage(state, result, nodeToRead, timestampsToReturn, systemContext);
            return ServiceResult.Good;
        }

        private static void EmitProcessedPage(
            HistorianContinuationState state,
            HistoryReadResult result,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn,
            ServerSystemContext systemContext)
        {
            List<DataValue> buffered = state.BufferedProcessedOutputs!;
            int remaining = buffered.Count - state.BufferedProcessedOffset;
            int pageSize = Math.Min(remaining, kProcessedPageSize);

            var page = new List<DataValue>(pageSize);
            for (int i = 0; i < pageSize; i++)
            {
                page.Add(buffered[state.BufferedProcessedOffset + i]);
            }
            state.BufferedProcessedOffset += pageSize;
            FillHistoryData(result, page, nodeToRead, timestampsToReturn);

            if (state.BufferedProcessedOffset >= buffered.Count)
            {
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
                state.Dispose();
                return;
            }

            systemContext.OperationContext?.Session?.SaveHistoryContinuationPoint(state.Id, state);
            result.StatusCode = StatusCodes.GoodMoreData;
            result.ContinuationPoint = new ByteString(state.Id.ToByteArray());
        }

        private const int kProcessedPageSize = 1000;

        /// <summary>
        /// Safety cap on the buffered output of the framework's streaming
        /// processed-read fallback. A 1-year window with a 1-second
        /// processing interval produces ~31M outputs; bounding the buffer
        /// at 100k aggregate samples avoids OOM-ing the server when a
        /// client requests an enormous aggregation window without a
        /// native push-down provider. Exceeding the cap returns
        /// <see cref="StatusCodes.BadTooManyOperations"/> — providers
        /// that need higher throughput should implement
        /// <see cref="IHistorianProcessedProvider"/> directly.
        /// </summary>
        internal const int kMaxProcessedBufferedOutputs = 100_000;

        /// <summary>
        /// Dispatches a single at-time history read with a streaming
        /// framework fallback that interpolates from raw values.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchAtTimeReadAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadAtTimeDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            ArrayOf<DateTimeUtc> reqTimes = details.ReqTimes;
            var typedTimes = new List<DateTimeUtc>(reqTimes.Count);
            for (int i = 0; i < reqTimes.Count; i++)
            {
                typedTimes.Add(reqTimes[i]);
            }

            // Provider push-down
            if (provider is IHistorianAtTimeProvider atTime)
            {
                var atTimeRequest = new HistorianAtTimeReadRequest
                {
                    NodeId = node.NodeId,
                    RequestedTimes = typedTimes,
                    UseSimpleBounds = details.UseSimpleBounds,
                };
                IList<DataValue> values = await atTime.ReadAtTimeAsync(
                    opContext, atTimeRequest, cancellationToken).ConfigureAwait(false);

                FillHistoryData(result, ToReadOnlyList(values), nodeToRead, timestampsToReturn);
                result.StatusCode = StatusCodes.Good;
                return ServiceResult.Good;
            }

            // Framework fallback: interpolate from raw values
            if (provider is not IHistorianDataProvider raw)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            var samples = await CollectAllRawAsync(opContext, raw, node.NodeId, typedTimes, cancellationToken)
                .ConfigureAwait(false);

            var produced = new List<DataValue>(typedTimes.Count);
            foreach (DateTimeUtc requestedTime in typedTimes)
            {
                produced.Add(InterpolateAtTime(samples, requestedTime, details.UseSimpleBounds));
            }

            FillHistoryData(result, produced, nodeToRead, timestampsToReturn);
            result.StatusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a HistoryRead on an Annotations property
        /// (Part 11 §5.2.7) by translating to the parent variable's
        /// <see cref="IHistorianAnnotationProvider"/> and wrapping each
        /// returned annotation as a <see cref="DataValue"/>.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchAnnotationReadAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            BaseVariableState parentVariable,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (parentVariable == null)
            {
                throw new ArgumentNullException(nameof(parentVariable));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianAnnotationProvider annotations)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianContinuationState? state = TryRestoreContinuation(
                systemContext, nodeToRead, HistorianReadKind.Annotations);

            HistorianAnnotationReadRequest request;
            HistorianResumeToken resumeToken;
            if (state is { Kind: HistorianReadKind.Annotations, AnnotationRequest: { } existing })
            {
                request = existing;
                resumeToken = state.ResumeToken;
            }
            else
            {
                bool isForward = details.StartTime <= details.EndTime;
                DateTimeUtc start = isForward ? details.StartTime : details.EndTime;
                DateTimeUtc end = isForward ? details.EndTime : details.StartTime;
                if (end == DateTimeUtc.MinValue)
                {
                    end = DateTimeUtc.MaxValue;
                }
                request = new HistorianAnnotationReadRequest
                {
                    NodeId = parentVariable.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = details.NumValuesPerNode,
                    IsForward = isForward,
                };
                resumeToken = default;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                parentVariable,
                HistoryUpdateType.Insert);

            HistorianPage<Annotation> page = await annotations.ReadAnnotationsAsync(
                opContext, request, resumeToken, cancellationToken).ConfigureAwait(false);

            var dataValues = new List<DataValue>(page.Values.Count);
            foreach (Annotation a in page.Values)
            {
                dataValues.Add(new DataValue(
                    new Variant(new ExtensionObject(a)),
                    StatusCodes.Good,
                    sourceTimestamp: a.AnnotationTime,
                    serverTimestamp: DateTimeUtc.MinValue));
            }
            FillHistoryData(result, dataValues, nodeToRead, timestampsToReturn);

            SaveOrReleaseAnnotationContinuation(
                systemContext, nodeToRead, result, state, page.NextToken,
                provider, parentVariable, request, timestampsToReturn,
                nodeToRead.ParsedIndexRange, nodeToRead.DataEncoding);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a HistoryUpdate on an Annotations property by
        /// translating to the parent variable's
        /// <see cref="IHistorianAnnotationProvider"/>.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchAnnotationUpdateAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            BaseVariableState parentVariable,
            UpdateStructureDataDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (parentVariable == null)
            {
                throw new ArgumentNullException(nameof(parentVariable));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianAnnotationProvider annotations)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistoryUpdateType updateType = MapPerformUpdate(details.PerformInsertReplace);
            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                parentVariable,
                updateType);

            ArrayOf<DataValue> updateValues = details.UpdateValues;
            var annotationList = new List<Annotation>(updateValues.Count);
            var times = new List<DateTimeUtc>(updateValues.Count);
            for (int i = 0; i < updateValues.Count; i++)
            {
                DataValue dv = updateValues[i];
                if (dv.IsNull)
                {
                    annotationList.Add(null!);
                    times.Add(DateTimeUtc.MinValue);
                    continue;
                }

                Annotation? annotation = DecodeAnnotation(dv);
                annotationList.Add(annotation!);
                times.Add(annotation != null ? annotation.AnnotationTime : dv.SourceTimestamp);
            }

            IList<StatusCode> statuses = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => await annotations.InsertAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Replace => await annotations.ReplaceAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Update => await annotations.UpdateAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Remove => await annotations.DeleteAnnotationsAsync(
                    opContext, parentVariable.NodeId, times, cancellationToken).ConfigureAwait(false),
                _ => RepeatStatus(StatusCodes.BadInvalidArgument, annotationList.Count),
            };

            result.OperationResults = ToStatusArray(statuses);
            ReportAuditAnnotationUpdate(systemContext, details, parentVariable, statuses);
            return ServiceResult.Good;
        }

        private static Annotation? DecodeAnnotation(DataValue dv)
        {
            if (dv.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                !extension.IsNull &&
                extension.TryGetValue<Annotation>(out Annotation? annotation))
            {
                return annotation;
            }
            return null;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via SaveHistoryContinuationPoint.")]
        private static void SaveOrReleaseAnnotationContinuation(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            HistorianContinuationState? existingState,
            HistorianResumeToken nextToken,
            IHistorianProvider provider,
            BaseVariableState parentVariable,
            HistorianAnnotationReadRequest request,
            TimestampsToReturn timestampsToReturn,
            NumericRange indexRange,
            QualifiedName dataEncoding)
        {
            if (nextToken.IsEmpty)
            {
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
                existingState?.Dispose();
                return;
            }

            HistorianContinuationState state;
            if (existingState != null)
            {
                state = existingState;
                state.ResumeToken = nextToken;
            }
            else
            {
                state = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = parentVariable.NodeId,
                    Kind = HistorianReadKind.Annotations,
                    ResumeToken = nextToken,
                    AnnotationRequest = request,
                    TimestampsToReturn = timestampsToReturn,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding,
                };
            }

            systemContext.OperationContext?.Session?.SaveHistoryContinuationPoint(state.Id, state);
            result.StatusCode = StatusCodes.GoodMoreData;
            result.ContinuationPoint = new ByteString(state.Id.ToByteArray());
        }

        /// <summary>
        /// Dispatches a HistoryRead with <c>ReadEventDetails</c> against
        /// an event-history notifier. The provider returns raw event
        /// records; the framework projects each record's fields through
        /// the supplied <c>EventFilter.SelectClauses</c> to build the
        /// returned <c>HistoryEventFieldList</c> entries.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchEventReadAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadEventDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            _ = timestampsToReturn;

            if (provider is not IHistorianEventProvider events)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianContinuationState? state = TryRestoreContinuation(
                systemContext, nodeToRead, HistorianReadKind.Events);

            HistorianEventReadRequest request;
            HistorianResumeToken token;
            if (state is { Kind: HistorianReadKind.Events, EventRequest: { } existing })
            {
                request = existing;
                token = state.ResumeToken;
            }
            else
            {
                bool isForward = details.StartTime <= details.EndTime;
                DateTimeUtc start = isForward ? details.StartTime : details.EndTime;
                DateTimeUtc end = isForward ? details.EndTime : details.StartTime;
                if (end == DateTimeUtc.MinValue)
                {
                    end = DateTimeUtc.MaxValue;
                }
                request = new HistorianEventReadRequest
                {
                    NodeId = node.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = details.NumValuesPerNode,
                    IsForward = isForward,
                    Filter = details.Filter,
                };
                token = default;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            HistorianPage<HistorianEventRecord> page = await events.ReadEventsAsync(
                opContext, request, token, cancellationToken).ConfigureAwait(false);

            // Evaluate the WhereClause if any elements are present.
            IReadOnlyList<HistorianEventRecord> filtered = page.Values;
            if (details.Filter.WhereClause.Elements.Count > 0 &&
                systemContext.Server is IServerInternal serverInternal)
            {
                var context = new FilterContext(
                    serverInternal.NamespaceUris,
                    serverInternal.TypeTree,
                    systemContext.OperationContext,
                    serverInternal.Telemetry);
                var keep = new List<HistorianEventRecord>(page.Values.Count);
                foreach (HistorianEventRecord record in page.Values)
                {
                    var target = new HistorianEventFilterTarget(record);
                    var evaluator = new FilterEvaluator(details.Filter.WhereClause, context, target);
                    if (evaluator.Result)
                    {
                        keep.Add(record);
                    }
                }
                filtered = keep;
            }

            var fields = new HistoryEventFieldList[filtered.Count];
            for (int i = 0; i < filtered.Count; i++)
            {
                fields[i] = ProjectEventFields(filtered[i], details.Filter);
            }

            result.HistoryData = new ExtensionObject(new HistoryEvent
            {
                Events = fields,
            });

            SaveOrReleaseEventContinuation(
                systemContext, nodeToRead, result, state, page.NextToken,
                provider, node, request);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches an UpdateEventDetails HistoryUpdate.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchUpdateEventAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            UpdateEventDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianEventProvider events)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                MapPerformUpdate(details.PerformInsertReplace));

            ArrayOf<HistoryEventFieldList> incoming = details.EventData;
            var decoded = new List<HistorianEventRecord>(incoming.Count);
            for (int i = 0; i < incoming.Count; i++)
            {
                decoded.Add(DecodeEventRecord(incoming[i], details.Filter, node.NodeId));
            }

            IList<StatusCode> statuses = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => await events.InsertEventsAsync(
                    opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Replace => await events.ReplaceEventsAsync(
                    opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Update => await events.UpdateEventsAsync(
                    opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                _ => RepeatStatus(StatusCodes.BadInvalidArgument, decoded.Count),
            };

            result.OperationResults = ToStatusArray(statuses);
            ReportAuditEventUpdate(systemContext, details, statuses);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a DeleteEventDetails HistoryUpdate.
        /// </summary>
        public static async ValueTask<ServiceResult> DispatchDeleteEventsAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            DeleteEventDetails details,
            HistoryUpdateResult result,
            CancellationToken cancellationToken)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (provider is not IHistorianEventProvider events)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            ArrayOf<ByteString> ids = details.EventIds;
            var typed = new List<ByteString>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                typed.Add(ids[i]);
            }

            IList<StatusCode> statuses = await events.DeleteEventsAsync(
                opContext, node.NodeId, typed, cancellationToken).ConfigureAwait(false);

            result.OperationResults = ToStatusArray(statuses);
            ReportAuditEventDelete(systemContext, details, statuses);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Projects an event record's fields through the supplied filter's
        /// <c>SelectClauses</c>. Operands whose browse path does not
        /// resolve to a field receive an empty <see cref="Variant"/>.
        /// </summary>
        public static HistoryEventFieldList ProjectEventFields(
            HistorianEventRecord record,
            EventFilter filter)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            var fields = new Variant[filter.SelectClauses.Count];
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand op = filter.SelectClauses[i];
                fields[i] = ResolveOperand(record, op);
            }
            return new HistoryEventFieldList { EventFields = fields };
        }

        private static Variant ResolveOperand(HistorianEventRecord record, SimpleAttributeOperand op)
        {
            if (op.BrowsePath.Count == 0)
            {
                if (op.AttributeId == Attributes.NodeId)
                {
                    return new Variant(record.EventType);
                }
                return default;
            }
            string key = BuildOperandKey(op.BrowsePath);
            return record.Fields.TryGetValue(key, out Variant value) ? value : default;
        }

        private static string BuildOperandKey(ArrayOf<QualifiedName> path)
        {
            if (path.Count == 1)
            {
                return path[0].Name ?? string.Empty;
            }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }
                sb.Append(path[i].Name);
            }
            return sb.ToString();
        }

        private static HistorianEventRecord DecodeEventRecord(
            HistoryEventFieldList incoming,
            EventFilter filter,
            NodeId notifierNodeId)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            ByteString eventId = ByteString.Empty;
            NodeId eventType = notifierNodeId;
            DateTimeUtc sourceTs = DateTimeUtc.MinValue;
            var fields = new Dictionary<string, Variant>(System.StringComparer.Ordinal);

            int count = System.Math.Min(filter.SelectClauses.Count, incoming.EventFields.Count);
            for (int i = 0; i < count; i++)
            {
                SimpleAttributeOperand op = filter.SelectClauses[i];
                Variant value = incoming.EventFields[i];
                string key = BuildOperandKey(op.BrowsePath);

                fields[key] = value;

                if (string.Equals(key, BrowseNames.EventId, System.StringComparison.Ordinal) &&
                    value.TryGetValue(out ByteString idValue))
                {
                    eventId = idValue;
                }
                else if (string.Equals(key, BrowseNames.EventType, System.StringComparison.Ordinal) &&
                    value.TryGetValue(out NodeId typeValue))
                {
                    eventType = typeValue;
                }
                else if (string.Equals(key, BrowseNames.Time, System.StringComparison.Ordinal) &&
                    value.TryGetValue(out DateTimeUtc tsValue))
                {
                    sourceTs = tsValue;
                }
            }

            return new HistorianEventRecord(eventId, eventType, sourceTs, fields);
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via SaveHistoryContinuationPoint.")]
        private static void SaveOrReleaseEventContinuation(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            HistorianContinuationState? existingState,
            HistorianResumeToken nextToken,
            IHistorianProvider provider,
            NodeState node,
            HistorianEventReadRequest request)
        {
            if (nextToken.IsEmpty)
            {
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
                existingState?.Dispose();
                return;
            }

            HistorianContinuationState state;
            if (existingState != null)
            {
                state = existingState;
                state.ResumeToken = nextToken;
            }
            else
            {
                state = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = node.NodeId,
                    Kind = HistorianReadKind.Events,
                    ResumeToken = nextToken,
                    EventRequest = request,
                    TimestampsToReturn = TimestampsToReturn.Source,
                };
            }

            systemContext.OperationContext?.Session?.SaveHistoryContinuationPoint(state.Id, state);
            result.StatusCode = StatusCodes.GoodMoreData;
            result.ContinuationPoint = new ByteString(state.Id.ToByteArray());
            _ = nodeToRead;
        }

        /// <summary>
        /// Releases a continuation point that was previously saved by the
        /// dispatcher.
        /// </summary>
        public static ServiceResult ReleaseContinuationPoint(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead)
        {
            if (systemContext == null)
            {
                throw new ArgumentNullException(nameof(systemContext));
            }
            if (nodeToRead == null)
            {
                throw new ArgumentNullException(nameof(nodeToRead));
            }

            if (nodeToRead.ContinuationPoint.IsEmpty)
            {
                return StatusCodes.BadContinuationPointInvalid;
            }

            object? state = systemContext.OperationContext?.Session?.RestoreHistoryContinuationPoint(
                nodeToRead.ContinuationPoint);
            if (state is HistorianContinuationState cont)
            {
                cont.Dispose();
                return ServiceResult.Good;
            }
            return StatusCodes.BadContinuationPointInvalid;
        }

        private static async ValueTask<ServiceResult> ReadRawPageAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            HistorianContinuationState? state,
            HistorianOperationContext opContext,
            CancellationToken cancellationToken)
        {
            if (provider is not IHistorianDataProvider data)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianRawReadRequest request;
            HistorianResumeToken token;
            if (state is { Kind: HistorianReadKind.Raw, RawRequest: { } existingRaw })
            {
                request = existingRaw;
                token = state.ResumeToken;
            }
            else
            {
                bool isForward = details.StartTime <= details.EndTime;
                DateTimeUtc start = isForward ? details.StartTime : details.EndTime;
                DateTimeUtc end = isForward ? details.EndTime : details.StartTime;
                if (end == DateTimeUtc.MinValue)
                {
                    end = DateTimeUtc.MaxValue;
                }

                request = new HistorianRawReadRequest
                {
                    NodeId = node.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = details.NumValuesPerNode,
                    IsForward = isForward,
                    ReturnBounds = details.ReturnBounds,
                };
                token = default;
            }

            HistorianPage<HistoricalDataValue> page = await data.ReadRawAsync(
                opContext, request, token, cancellationToken).ConfigureAwait(false);

            var values = new List<DataValue>(page.Values.Count);
            foreach (HistoricalDataValue v in page.Values)
            {
                values.Add(v.Value);
            }
            FillHistoryData(result, values, nodeToRead, timestampsToReturn);

            SaveOrReleaseContinuation(
                systemContext, nodeToRead, result, state, page.NextToken,
                provider, node, request, kind: HistorianReadKind.Raw,
                timestampsToReturn: timestampsToReturn,
                indexRange: nodeToRead.ParsedIndexRange,
                dataEncoding: nodeToRead.DataEncoding);

            return ServiceResult.Good;
        }

        private static async ValueTask<ServiceResult> ReadModifiedPageAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            HistorianContinuationState? state,
            HistorianOperationContext opContext,
            CancellationToken cancellationToken)
        {
            if (provider is not IHistorianModifiedProvider modified)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianModifiedReadRequest request;
            HistorianResumeToken token;
            if (state is { Kind: HistorianReadKind.Modified, ModifiedRequest: { } existing })
            {
                request = existing;
                token = state.ResumeToken;
            }
            else
            {
                bool isForward = details.StartTime <= details.EndTime;
                DateTimeUtc start = isForward ? details.StartTime : details.EndTime;
                DateTimeUtc end = isForward ? details.EndTime : details.StartTime;
                if (end == DateTimeUtc.MinValue)
                {
                    end = DateTimeUtc.MaxValue;
                }

                request = new HistorianModifiedReadRequest
                {
                    NodeId = node.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = details.NumValuesPerNode,
                    IsForward = isForward,
                };
                token = default;
            }

            HistorianPage<ModifiedDataValue> page = await modified.ReadModifiedAsync(
                opContext, request, token, cancellationToken).ConfigureAwait(false);

            var values = new List<DataValue>(page.Values.Count);
            var infos = new List<ModificationInfo>(page.Values.Count);
            foreach (ModifiedDataValue v in page.Values)
            {
                values.Add(v.Value);
                infos.Add(v.Info);
            }
            FillHistoryModifiedData(result, values, infos, nodeToRead, timestampsToReturn);

            SaveOrReleaseContinuation(
                systemContext, nodeToRead, result, state, page.NextToken,
                provider, node, modifiedRequest: request, kind: HistorianReadKind.Modified,
                timestampsToReturn: timestampsToReturn,
                indexRange: nodeToRead.ParsedIndexRange,
                dataEncoding: nodeToRead.DataEncoding);

            return ServiceResult.Good;
        }

        private static HistorianContinuationState? TryRestoreContinuation(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead,
            HistorianReadKind expectedKind)
        {
            if (nodeToRead.ContinuationPoint.IsEmpty)
            {
                return null;
            }
            object? raw = systemContext.OperationContext?.Session?.RestoreHistoryContinuationPoint(
                nodeToRead.ContinuationPoint);
            if (raw is not HistorianContinuationState state)
            {
                return null;
            }
            if (state.Kind != expectedKind)
            {
                return null;
            }
            // Reject cross-wired continuation points — a client that
            // submits a CP from one node against a different node would
            // otherwise get the wrong page from the wrong provider.
            if (state.NodeId != nodeToRead.NodeId)
            {
                state.Dispose();
                return null;
            }
            return state;
        }

        private static HistorianResumeToken TryDecodeContinuation(HistoryReadValueId nodeToRead)
        {
            // Currently we only use the resume-token field for processed reads when
            // we don't bother with a full HistorianContinuationState; future iterations
            // may use the full continuation state for these too.
            return default;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via SaveHistoryContinuationPoint.")]
        private static void SaveOrReleaseContinuation(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead,
            HistoryReadResult result,
            HistorianContinuationState? existingState,
            HistorianResumeToken nextToken,
            IHistorianProvider? provider = null,
            NodeState? node = null,
            HistorianRawReadRequest? rawRequest = null,
            HistorianModifiedReadRequest? modifiedRequest = null,
            HistorianReadKind kind = HistorianReadKind.Raw,
            TimestampsToReturn timestampsToReturn = TimestampsToReturn.Source,
            NumericRange indexRange = default,
            QualifiedName? dataEncoding = null)
        {
            if (nextToken.IsEmpty)
            {
                // final page
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
                existingState?.Dispose();
                return;
            }

            HistorianContinuationState state;
            if (existingState != null)
            {
                state = existingState;
                state.ResumeToken = nextToken;
            }
            else
            {
                if (provider == null || node == null)
                {
                    throw new InvalidOperationException("Provider/node required for new continuation state.");
                }
                state = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = node.NodeId,
                    Kind = kind,
                    ResumeToken = nextToken,
                    RawRequest = rawRequest,
                    ModifiedRequest = modifiedRequest,
                    TimestampsToReturn = timestampsToReturn,
                    IndexRange = indexRange,
                    DataEncoding = dataEncoding ?? QualifiedName.Null,
                };
            }

            systemContext.OperationContext?.Session?.SaveHistoryContinuationPoint(state.Id, state);
            result.StatusCode = StatusCodes.GoodMoreData;
            result.ContinuationPoint = new ByteString(state.Id.ToByteArray());
        }

        private static void FillHistoryData(
            HistoryReadResult result,
            IReadOnlyList<DataValue> values,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn)
        {
            var filtered = new List<DataValue>(values.Count);
            foreach (DataValue v in values)
            {
                DataValue clone = ApplyTimestampFilter(v, timestampsToReturn);
                clone = ApplyIndexRange(clone, nodeToRead.ParsedIndexRange);
                clone = ApplyEncoding(clone, nodeToRead.DataEncoding);
                filtered.Add(clone);
            }
            var data = new HistoryData { DataValues = filtered };
            result.HistoryData = new ExtensionObject(data);
        }

        private static void FillHistoryModifiedData(
            HistoryReadResult result,
            List<DataValue> values,
            List<ModificationInfo> infos,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn)
        {
            var filtered = new List<DataValue>(values.Count);
            foreach (DataValue v in values)
            {
                DataValue clone = ApplyTimestampFilter(v, timestampsToReturn);
                clone = ApplyIndexRange(clone, nodeToRead.ParsedIndexRange);
                clone = ApplyEncoding(clone, nodeToRead.DataEncoding);
                filtered.Add(clone);
            }
            var modInfos = new ModificationInfo[infos.Count];
            for (int i = 0; i < infos.Count; i++)
            {
                modInfos[i] = infos[i];
            }
            var data = new HistoryModifiedData
            {
                DataValues = filtered,
                ModificationInfos = modInfos,
            };
            result.HistoryData = new ExtensionObject(data);
        }

        private static ArrayOf<StatusCode> ToStatusArray(IList<StatusCode> statuses)
        {
            var array = new StatusCode[statuses.Count];
            for (int i = 0; i < statuses.Count; i++)
            {
                array[i] = statuses[i];
            }
            return array;
        }

        private static DataValue ApplyTimestampFilter(DataValue source, TimestampsToReturn timestampsToReturn)
        {
            DateTimeUtc sourceTs = source.SourceTimestamp;
            DateTimeUtc serverTs = source.ServerTimestamp;
            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Server)
            {
                sourceTs = DateTimeUtc.MinValue;
            }
            if (timestampsToReturn is TimestampsToReturn.Neither or TimestampsToReturn.Source)
            {
                serverTs = DateTimeUtc.MinValue;
            }
            return new DataValue(
                source.WrappedValue,
                source.StatusCode,
                sourceTs,
                serverTs,
                source.SourcePicoseconds,
                source.ServerPicoseconds);
        }

        private static DataValue ApplyIndexRange(DataValue value, NumericRange indexRange)
        {
            if (indexRange.IsNull || !StatusCode.IsGood(value.StatusCode))
            {
                return value;
            }
            Variant variant = value.WrappedValue;
            StatusCode err = indexRange.ApplyRange(ref variant);
            if (StatusCode.IsBad(err))
            {
                return value.WithWrappedValue(default).WithStatus(err);
            }
            return value.WithWrappedValue(variant);
        }

        private static DataValue ApplyEncoding(DataValue value, QualifiedName dataEncoding)
        {
            if (!dataEncoding.IsNull && StatusCode.IsGood(value.StatusCode))
            {
                return value
                    .WithWrappedValue(default)
                    .WithStatus(StatusCodes.BadDataEncodingUnsupported);
            }
            return value;
        }

        private static void ApplyContinuation(
            ServerSystemContext systemContext,
            NodeId nodeId,
            HistoryReadResult result,
            HistorianPage<DataValue> page)
        {
            if (page.IsFinal)
            {
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
            }
            else
            {
                // Native provider continuation — not yet wired through framework state.
                // TODO(historian): persist provider-side resume token for processed reads.
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
            }
            _ = systemContext;
            _ = nodeId;
        }

        private static void FlushCalculator(IAggregateCalculator calculator, List<DataValue> output, bool partial)
        {
            while (calculator.TryGetProcessedValue(partial, out DataValue computed))
            {
                output.Add(computed);
            }
        }

        private static async ValueTask<List<DataValue>> CollectAllRawAsync(
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            List<DateTimeUtc> times,
            CancellationToken cancellationToken)
        {
            if (times.Count == 0)
            {
                return [];
            }

            DateTimeUtc min = times[0];
            DateTimeUtc max = times[0];
            for (int i = 1; i < times.Count; i++)
            {
                if (times[i] < min)
                {
                    min = times[i];
                }
                if (times[i] > max)
                {
                    max = times[i];
                }
            }

            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = min,
                EndTime = max,
                MaxValues = 0,
                IsForward = true,
                ReturnBounds = true,
            };

            var collected = new List<DataValue>();
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    context, request, token, cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue v in page.Values)
                {
                    collected.Add(v.Value);
                }
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }
            collected.Sort((a, b) => a.SourceTimestamp.CompareTo(b.SourceTimestamp));
            return collected;
        }

        private static DataValue InterpolateAtTime(List<DataValue> samples, DateTimeUtc requestedTime, bool useSimpleBounds)
        {
            DataValue before = DataValue.Null;
            DataValue after = DataValue.Null;
            for (int i = 0; i < samples.Count; i++)
            {
                DataValue v = samples[i];
                int cmp = v.SourceTimestamp.CompareTo(requestedTime);
                if (cmp == 0)
                {
                    return new DataValue(
                        v.WrappedValue,
                        v.StatusCode,
                        sourceTimestamp: requestedTime,
                        serverTimestamp: v.ServerTimestamp);
                }
                if (cmp < 0)
                {
                    before = v;
                }
                else
                {
                    after = v;
                    break;
                }
            }

            if (useSimpleBounds || before.IsNull || after.IsNull)
            {
                DataValue closest = !before.IsNull ? before : after;
                if (closest.IsNull)
                {
                    return new DataValue(
                        Variant.Null,
                        StatusCodes.BadNoData,
                        sourceTimestamp: requestedTime,
                        serverTimestamp: DateTimeUtc.MinValue);
                }
                return new DataValue(
                    closest.WrappedValue,
                    StatusCodes.UncertainNoCommunicationLastUsableValue,
                    sourceTimestamp: requestedTime,
                    serverTimestamp: DateTimeUtc.MinValue);
            }

            try
            {
                double y0 = Convert.ToDouble(before.WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
                double y1 = Convert.ToDouble(after.WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
                double t0 = before.SourceTimestamp.ToDateTime().Ticks;
                double t1 = after.SourceTimestamp.ToDateTime().Ticks;
                double t = requestedTime.ToDateTime().Ticks;
                double ratio = (t - t0) / (t1 - t0);
                double y = y0 + (y1 - y0) * ratio;
                return new DataValue(
                    new Variant(y),
                    StatusCodes.UncertainDataSubNormal,
                    sourceTimestamp: requestedTime,
                    serverTimestamp: DateTimeUtc.MinValue);
            }
            catch (InvalidCastException)
            {
                return new DataValue(
                    before.WrappedValue,
                    StatusCodes.UncertainNoCommunicationLastUsableValue,
                    sourceTimestamp: requestedTime,
                    serverTimestamp: DateTimeUtc.MinValue);
            }
            catch (FormatException)
            {
                return new DataValue(
                    before.WrappedValue,
                    StatusCodes.UncertainNoCommunicationLastUsableValue,
                    sourceTimestamp: requestedTime,
                    serverTimestamp: DateTimeUtc.MinValue);
            }
        }

        private static HistoryUpdateType MapPerformUpdate(PerformUpdateType performUpdate)
        {
            return performUpdate switch
            {
                PerformUpdateType.Insert => HistoryUpdateType.Insert,
                PerformUpdateType.Replace => HistoryUpdateType.Replace,
                PerformUpdateType.Update => HistoryUpdateType.Update,
                _ => HistoryUpdateType.Insert,
            };
        }

        private static List<DataValue> ToList(ArrayOf<DataValue> values)
        {
            var list = new List<DataValue>(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                list.Add(values[i]);
            }
            return list;
        }

        private static IReadOnlyList<DataValue> ToReadOnlyList(IList<DataValue> values)
        {
            return values is IReadOnlyList<DataValue> rol ? rol : new List<DataValue>(values);
        }

        private static StatusCode[] RepeatStatus(StatusCode code, int count)
        {
            var statuses = new StatusCode[count];
            for (int i = 0; i < count; i++)
            {
                statuses[i] = code;
            }
            return statuses;
        }

        private static StatusCode AggregateStatus(IList<StatusCode> statuses)
        {
            StatusCode worst = StatusCodes.Good;
            for (int i = 0; i < statuses.Count; i++)
            {
                if (StatusCode.IsBad(statuses[i]))
                {
                    return statuses[i];
                }
                if (StatusCode.IsUncertain(statuses[i]) && !StatusCode.IsBad(worst))
                {
                    worst = statuses[i];
                }
            }
            return worst;
        }

        private static ILogger? GetAuditLogger(ServerSystemContext systemContext)
        {
            ITelemetryContext? telemetry = systemContext.Server?.Telemetry;
            return telemetry?.CreateLogger(nameof(HistorianDispatcher));
        }

        private static IAuditEventServer? GetAuditServer(ServerSystemContext systemContext)
        {
            return systemContext.Server as IAuditEventServer;
        }

        /// <summary>
        /// Returns <c>true</c> when at least one status in <paramref name="statuses"/>
        /// is <see cref="StatusCode.IsGood(StatusCode)"/>. Audit events are
        /// only reported when at least one per-value operation succeeded —
        /// emitting an "Update" event when every value failed would
        /// misrepresent the outcome.
        /// </summary>
        private static bool HasAnyGood(IList<StatusCode> statuses)
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (StatusCode.IsGood(statuses[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ReportAuditUpdateData(
            ServerSystemContext systemContext,
            UpdateDataDetails details,
            IList<StatusCode> statuses)
        {
            if (!HasAnyGood(statuses))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            // The dispatcher does not currently fetch the previous values
            // (would require an extra read-before-write per item); pass an
            // empty array so the audit event still fires but the OldValues
            // field is empty. Providers that need full audit fidelity may
            // attach the old values to the input details before calling.
            server.ReportAuditHistoryValueUpdateEvent(
                systemContext, details, [], AggregateStatus(statuses), logger);
        }

        private static void ReportAuditAnnotationUpdate(
            ServerSystemContext systemContext,
            UpdateStructureDataDetails details,
            BaseVariableState parentVariable,
            IList<StatusCode> statuses)
        {
            if (!HasAnyGood(statuses))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            _ = parentVariable;
            server.ReportAuditHistoryAnnotationUpdateEvent(
                systemContext, details, [], AggregateStatus(statuses), logger);
        }

        private static void ReportAuditDeleteRaw(
            ServerSystemContext systemContext,
            DeleteRawModifiedDetails details,
            StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryRawModifyDeleteEvent(
                systemContext, details, default, status, logger);
        }

        private static void ReportAuditDeleteAtTime(
            ServerSystemContext systemContext,
            DeleteAtTimeDetails details,
            IList<StatusCode> statuses)
        {
            if (!HasAnyGood(statuses))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryAtTimeDeleteEvent(
                systemContext, details, [], AggregateStatus(statuses), logger);
        }

        private static void ReportAuditEventUpdate(
            ServerSystemContext systemContext,
            UpdateEventDetails details,
            IList<StatusCode> statuses)
        {
            if (!HasAnyGood(statuses))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryEventUpdateEvent(
                systemContext, details, [], AggregateStatus(statuses), logger);
        }

        private static void ReportAuditEventDelete(
            ServerSystemContext systemContext,
            DeleteEventDetails details,
            IList<StatusCode> statuses)
        {
            if (!HasAnyGood(statuses))
            {
                return;
            }
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryEventDeleteEvent(
                systemContext, details, [], AggregateStatus(statuses), logger);
        }
    }
}
