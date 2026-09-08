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
using System.IO;
using System.Text;
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
    /// <see cref="ISessionContinuationPoints.SaveHistory"/> /
    /// <see cref="ISessionContinuationPoints.RestoreHistory"/>.
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
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        public static async ValueTask<ServiceResult> DispatchRawReadAsync(
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
            HistorianContinuationClaim? claim = await TryClaimContinuationAsync(
                systemContext,
                provider,
                nodeToRead,
                details.IsReadModified
                    ? HistorianReadKind.Modified
                    : HistorianReadKind.Raw,
                cancellationToken).ConfigureAwait(false);
            try
            {
                // A non-empty ContinuationPoint that does not resolve to a saved history
                // continuation (unknown, released, foreign, or a Browse CP) is invalid
                // (OPC UA Part 11; CTT HA Read Raw Err-014/Err-024).
                if (claim == null && !nodeToRead.ContinuationPoint.IsEmpty)
                {
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }

                HistorianNodeCapabilities capabilities = await provider
                    .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (details.IsReadModified
                    ? !capabilities.ReadModifiedData
                    : !capabilities.ReadRawData)
                {
                    claim?.Retire();
                    return StatusCodes.BadHistoryOperationUnsupported;
                }

                // Part 11 6.5.3.3: Bounding Values are not defined for modified reads.
                if (details.IsReadModified && details.ReturnBounds)
                {
                    claim?.Retire();
                    result.StatusCode = StatusCodes.BadInvalidArgument;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }
                if (!await SupportsRequestedTimestampsAsync(
                    provider,
                    node.NodeId,
                    timestampsToReturn,
                    cancellationToken).ConfigureAwait(false))
                {
                    claim?.Retire();
                    result.StatusCode = StatusCodes.BadTimestampNotSupported;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }

                HistorianOperationContext opContext = new(
                    systemContext,
                    systemContext.OperationContext!,
                    node,
                    HistoryUpdateType.Insert);

                if (details.IsReadModified)
                {
                    return await ReadModifiedPageAsync(
                        systemContext, provider, node, nodeToRead, details,
                        timestampsToReturn, result, claim, opContext, cancellationToken)
                        .ConfigureAwait(false);
                }
                return await ReadRawPageAsync(
                    systemContext, provider, node, nodeToRead, details,
                    timestampsToReturn, result, claim, opContext, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (claim != null)
                {
                    await claim.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Dispatches a single update-data history operation
        /// (Insert / Replace / Update).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditUpdateData(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            bool supported = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => capabilities.InsertData,
                PerformUpdateType.Replace => capabilities.ReplaceData,
                PerformUpdateType.Update => capabilities.UpdateData,
                _ => true
            };
            if (!supported)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditUpdateData(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistoryUpdateType updateType = MapPerformUpdate(details.PerformInsertReplace);
            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                updateType);

            ArrayOf<DataValue> values = details.UpdateValues;
            HistorianUpdateOutcome<DataValue> outcome =
                provider is IHistorianTransactionalProvider transactional
                ? details.PerformInsertReplace switch
                {
                    PerformUpdateType.Insert => await transactional.InsertAtomicAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Replace => await transactional.ReplaceAtomicAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Update => await transactional.UpdateAtomicAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    _ => new HistorianUpdateOutcome<DataValue>(
                        RepeatStatus(StatusCodes.BadInvalidArgument, values.Count).ToArrayOf())
                }
                : details.PerformInsertReplace switch
                {
                    PerformUpdateType.Insert => await data.InsertAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Replace => await data.ReplaceAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Update => await data.UpdateAsync(
                        opContext,
                        node.NodeId,
                        values,
                        cancellationToken).ConfigureAwait(false),
                    _ => new HistorianUpdateOutcome<DataValue>(
                        RepeatStatus(StatusCodes.BadInvalidArgument, values.Count).ToArrayOf())
                };
            if (outcome.OperationResults.Count != values.Count)
            {
                outcome = CreateFailureOutcome<DataValue>(
                    StatusCodes.BadUnexpectedError,
                    values.Count);
            }

            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;

            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditUpdateData(systemContext, details, outcome, operationResult.StatusCode);
            return operationResult;
        }

        /// <summary>
        /// Dispatches a single delete-raw history operation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        1);
                result.StatusCode = StatusCodes.BadHistoryOperationUnsupported;
                ReportAuditDeleteRaw(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.DeleteRaw)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        1);
                result.StatusCode = StatusCodes.BadHistoryOperationUnsupported;
                ReportAuditDeleteRaw(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            HistorianUpdateOutcome<DataValue> outcome = await data.DeleteRawAsync(
                opContext,
                node.NodeId,
                details.StartTime,
                details.EndTime,
                details.IsDeleteModified,
                cancellationToken).ConfigureAwait(false);
            if (outcome.OperationResults.Count != 1)
            {
                outcome = CreateFailureOutcome<DataValue>(
                    StatusCodes.BadUnexpectedError,
                    1);
            }

            StatusCode status = outcome.OperationResults.Count > 0
                ? outcome.OperationResults[0]
                : StatusCodes.BadUnexpectedError;
            result.StatusCode = status;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ReportAuditDeleteRaw(systemContext, details, outcome, status);
            return StatusCode.IsBad(status) ? status : ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a single delete-at-time history operation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.ReqTimes.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditDeleteAtTime(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.DeleteAtTime)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.ReqTimes.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditDeleteAtTime(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            ArrayOf<DateTimeUtc> times = details.ReqTimes;

            HistorianUpdateOutcome<DataValue> outcome = await data.DeleteAtTimeAsync(
                opContext, node.NodeId, times, cancellationToken).ConfigureAwait(false);
            if (outcome.OperationResults.Count != times.Count)
            {
                outcome = CreateFailureOutcome<DataValue>(
                    StatusCodes.BadUnexpectedError,
                    times.Count);
            }

            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditDeleteAtTime(systemContext, details, outcome, operationResult.StatusCode);
            return operationResult;
        }

        /// <summary>
        /// Dispatches a single processed (aggregate) history read with the
        /// standard streaming fallback when the provider does not
        /// implement <see cref="IHistorianProcessedProvider"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via ContinuationPoints.SaveHistory or disposed inline by EmitProcessedPage.")]
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
            bool hasContinuationPoint =
                !nodeToRead.ContinuationPoint.IsEmpty;
            // Part 11 v1.05.07 §6.5.4.2: the request domain is defined by StartTime, EndTime and
            // ProcessingInterval, all of which shall be specified. A zero ProcessingInterval is
            // valid and requests one aggregate over the entire range; negative or non-finite
            // durations are invalid. If StartTime equals EndTime there is no meaningful way to
            // interpret the zero-width time domain. A continuation resumes its persisted request,
            // so validate these wire details only for the initial request.
            if (!hasContinuationPoint &&
                (details.StartTime == details.EndTime ||
                    details.ProcessingInterval < 0 ||
                    double.IsNaN(details.ProcessingInterval) ||
                    double.IsInfinity(details.ProcessingInterval)))
            {
                result.StatusCode = StatusCodes.BadInvalidArgument;
                return StatusCodes.BadInvalidArgument;
            }

            HistorianContinuationClaim? claim = await TryClaimContinuationAsync(
                systemContext,
                provider,
                nodeToRead,
                HistorianReadKind.Processed,
                cancellationToken).ConfigureAwait(false);
            if (claim == null && hasContinuationPoint)
            {
                result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }

            if (claim == null)
            {
                return await DispatchProcessedReadCoreAsync(
                    systemContext,
                    provider,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    timestampsToReturn,
                    result,
                    claim,
                    cancellationToken).ConfigureAwait(false);
            }
            await using (claim.ConfigureAwait(false))
            {
                return await DispatchProcessedReadCoreAsync(
                    systemContext,
                    provider,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    timestampsToReturn,
                    result,
                    claim,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private static async ValueTask<ServiceResult>
            DispatchProcessedReadCoreAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadProcessedDetails details,
            NodeId aggregateId,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            HistorianContinuationClaim? claim,
            CancellationToken cancellationToken)
        {
            HistorianContinuationState? cont = claim?.State;
            aggregateId = cont?.ProcessedRequest?.AggregateId ?? aggregateId;
            if (systemContext.Server?.AggregateManager is { } aggregateManager &&
                !aggregateManager.IsSupported(aggregateId))
            {
                claim?.Retire();
                result.ContinuationPoint = ByteString.Empty;
                return StatusCodes.BadAggregateNotSupported;
            }

            if (aggregateId == ObjectIds.AggregateFunction_AnnotationCount)
            {
                if (provider is not IHistorianProcessedProvider and
                    not IHistorianAnnotationProvider)
                {
                    claim?.Retire();
                    return StatusCodes.BadAggregateNotSupported;
                }
            }
            else if (provider is not IHistorianProcessedProvider and
                not IHistorianDataProvider)
            {
                claim?.Retire();
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            bool timestampsSupported = await SupportsRequestedTimestampsAsync(
                provider,
                node.NodeId,
                timestampsToReturn,
                cancellationToken).ConfigureAwait(false);
            if (!capabilities.ReadProcessedData)
            {
                claim?.Retire();
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            if (!timestampsSupported)
            {
                claim?.Retire();
                result.StatusCode = StatusCodes.BadTimestampNotSupported;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }

            // Resume from buffered output if a continuation already exists.
            if (cont?.BufferedProcessedOutputs is { })
            {
                await EmitProcessedPageAsync(
                    cont,
                    claim,
                    result,
                    nodeToRead,
                    timestampsToReturn,
                    systemContext,
                    cancellationToken).ConfigureAwait(false);
                return ServiceResult.Good;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            AggregateConfiguration config = cont?.ProcessedRequest?.Configuration
                ?? details.AggregateConfiguration;
            // A default-initialized AggregateConfiguration (all-zero, UseServerCapabilitiesDefaults
            // unset) is the implicit "no override" case from a request that didn't set the field
            // explicitly. Treat it as use-server-defaults rather than as an explicit configuration.
            bool isImplicitDefault = config != null &&
                !config.UseServerCapabilitiesDefaults &&
                config.PercentDataBad == 0 &&
                config.PercentDataGood == 0 &&
                !config.TreatUncertainAsBad &&
                !config.UseSlopedExtrapolation;

            if (config == null || config.UseServerCapabilitiesDefaults || isImplicitDefault)
            {
                config = systemContext.Server != null
                    ? systemContext.Server.AggregateManager.GetDefaultConfiguration(node.NodeId)
                    : new AggregateConfiguration
                    {
                        PercentDataBad = 100,
                        PercentDataGood = 100,
                        // Part 13 v1.05.07 §4.2.1.2: the TreatUncertainAsBad default is True.
                        TreatUncertainAsBad = true,
                        UseSlopedExtrapolation = false,
                        UseServerCapabilitiesDefaults = false
                    };
            }
            else
            {
                // Part 13 v1.05.07 §4.2.1.2: validate explicit AggregateConfiguration inputs.
                // PercentDataGood and PercentDataBad must each be ≤ 100, and the relationship
                // PercentDataGood ≥ (100 - PercentDataBad) must hold.
                if (config.PercentDataGood > 100 ||
                    config.PercentDataBad > 100 ||
                    config.PercentDataGood < 100 - config.PercentDataBad)
                {
                    claim?.Retire();
                    result.StatusCode = StatusCodes.BadAggregateInvalidInputs;
                    return StatusCodes.BadAggregateInvalidInputs;
                }
            }

            HistorianProcessedReadRequest processedRequest =
                cont?.ProcessedRequest ??
                new HistorianProcessedReadRequest
                {
                    NodeId = node.NodeId,
                    AggregateId = aggregateId,
                    StartTime = details.StartTime,
                    EndTime = details.EndTime,
                    ProcessingInterval = details.ProcessingInterval,
                    MaxValues = capabilities.MaxReturnDataValues,
                    Configuration = config
                };

            // Native push-down path
            if (provider is IHistorianProcessedProvider native)
            {
                HistorianResumeToken token = cont?.ResumeToken ?? default;
                HistorianPage<DataValue> page = await native.ReadProcessedAsync(
                    opContext,
                    processedRequest,
                    token,
                    cancellationToken).ConfigureAwait(false);

                FillHistoryData(
                    systemContext,
                    result,
                    page.Values,
                    nodeToRead,
                    timestampsToReturn);
                HistorianContinuationState? initialState = null;
                try
                {
                    if (claim == null && !page.NextToken.IsEmpty)
                    {
                        initialState = new HistorianContinuationState
                        {
                            Id = Guid.NewGuid(),
                            Provider = provider,
                            NodeId = node.NodeId,
                            Kind = HistorianReadKind.Processed,
                            ResumeToken = page.NextToken,
                            ProcessedRequest = processedRequest,
                            TimestampsToReturn = timestampsToReturn,
                            IndexRange = nodeToRead.ParsedIndexRange,
                            DataEncoding = nodeToRead.DataEncoding
                        };
                    }
                    await SaveSuccessorOrCompleteAsync(
                        systemContext,
                        result,
                        claim,
                        hasMore: !page.NextToken.IsEmpty,
                        page.NextToken,
                        initialState,
                        bufferedProcessedOffset: null,
                        cancellationToken).ConfigureAwait(false);
                    initialState = null;
                }
                finally
                {
                    initialState?.Dispose();
                }
                return ServiceResult.Good;
            }

            if (cont != null)
            {
                claim?.Retire();
                result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }

            // Framework streaming fallback through AggregateManager
            IServerInternal? serverInternal = systemContext.Server;
            if (serverInternal == null)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            // Part 13 v1.05.07 §5.4.3.20: AnnotationCount counts the Annotations in each interval,
            // not the raw data values. Compute it from the node's annotation history; the raw-value
            // calculator path cannot produce a correct result.
            if (aggregateId == ObjectIds.AggregateFunction_AnnotationCount)
            {
                return await ComputeAnnotationCountAsync(
                    systemContext,
                    provider,
                    node,
                    nodeToRead,
                    details,
                    processedRequest,
                    opContext,
                    timestampsToReturn,
                    result,
                    cancellationToken).ConfigureAwait(false);
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
                IsForward = details.StartTime < details.EndTime,
                ReturnBounds = true
            };

            HistorianResumeToken token2 = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    opContext, rawRequest, token2, cancellationToken).ConfigureAwait(false);

                foreach (HistoricalDataValue sample in page.Values)
                {
                    if (!calculator.QueueRawValue(sample.Value) &&
                        !FlushCalculator(
                            calculator,
                            values,
                            partial: false,
                            kMaxProcessedBufferedOutputs))
                    {
                        return StatusCodes.BadTooManyOperations;
                    }
                }

                if (page.IsFinal)
                {
                    break;
                }
                token2 = page.NextToken;
            }

            if (!FlushCalculator(
                calculator,
                values,
                partial: true,
                kMaxProcessedBufferedOutputs))
            {
                return StatusCodes.BadTooManyOperations;
            }

            // Buffer the entire output set and emit the first page from it.
            HistorianContinuationState? state = null;
            try
            {
                state = new HistorianContinuationState
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
                    BufferedProcessedOutputs =
                        new HistorianBufferedProcessedPayload(
                            values.ToArrayOf()),
                    BufferedProcessedOffset = 0
                };
                await EmitProcessedPageAsync(
                    state,
                    claim: null,
                    result,
                    nodeToRead,
                    timestampsToReturn,
                    systemContext,
                    cancellationToken).ConfigureAwait(false);
                state = null;
            }
            finally
            {
                state?.Dispose();
            }
            return ServiceResult.Good;
        }

        private static async ValueTask EmitProcessedPageAsync(
            HistorianContinuationState state,
            HistorianContinuationClaim? claim,
            HistoryReadResult result,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn,
            ServerSystemContext systemContext,
            CancellationToken cancellationToken)
        {
            HistorianBufferedProcessedPayload buffered =
                state.BufferedProcessedOutputs!;
            int remaining = buffered.Count - state.BufferedProcessedOffset;
            int configuredPageSize = state.ProcessedRequest?.MaxValues > 0
                ? (int)Math.Min(
                    state.ProcessedRequest.MaxValues,
                    kProcessedPageSize)
                : kProcessedPageSize;
            int pageSize = Math.Min(remaining, configuredPageSize);

            var page = new List<DataValue>(pageSize);
            for (int i = 0; i < pageSize; i++)
            {
                page.Add(buffered[state.BufferedProcessedOffset + i]);
            }
            int nextOffset = state.BufferedProcessedOffset + pageSize;
            FillHistoryData(
                systemContext,
                result,
                page,
                nodeToRead,
                timestampsToReturn);

            if (nextOffset >= buffered.Count)
            {
                await SaveSuccessorOrCompleteAsync(
                    systemContext,
                    result,
                    claim,
                    hasMore: false,
                    default,
                    claim == null ? state : null,
                    nextOffset,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (claim == null)
            {
                state.BufferedProcessedOffset = nextOffset;
            }
            await SaveSuccessorOrCompleteAsync(
                systemContext,
                result,
                claim,
                hasMore: true,
                state.ResumeToken,
                claim == null ? state : null,
                nextOffset,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the Part 13 AnnotationCount aggregate (§5.4.3.20) from the node's annotation
        /// history. Emits one Int32 value per processing interval (count of Annotations in the
        /// interval, with endTime excluded). Requires an
        /// <see cref="IHistorianAnnotationProvider"/>; otherwise the aggregate is reported as
        /// unsupported for the node.
        /// </summary>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "HistorianContinuationState ownership is transferred to the session via ContinuationPoints.SaveHistory or disposed inline by EmitProcessedPage.")]
        private static async ValueTask<ServiceResult> ComputeAnnotationCountAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadProcessedDetails details,
            HistorianProcessedReadRequest processedRequest,
            HistorianOperationContext opContext,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            if (provider is not IHistorianAnnotationProvider annotationProvider)
            {
                return StatusCodes.BadAggregateNotSupported;
            }

            DateTimeUtc startTime = details.StartTime;
            DateTimeUtc endTime = details.EndTime;
            bool isForward = startTime <= endTime;
            DateTimeUtc windowStart = isForward ? startTime : endTime;
            DateTimeUtc windowEnd = isForward ? endTime : startTime;

            // Read every annotation timestamp in the window.
            var annotationTimes = new List<DateTimeUtc>();
            var request = new HistorianAnnotationReadRequest
            {
                NodeId = node.NodeId,
                StartTime = windowStart,
                EndTime = windowEnd,
                MaxValues = 0,
                IsForward = isForward
            };

            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<Annotation> page = await annotationProvider.ReadAnnotationsAsync(
                    opContext, request, token, cancellationToken).ConfigureAwait(false);

                foreach (Annotation annotation in page.Values)
                {
                    annotationTimes.Add(annotation.AnnotationTime);
                }

                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }
            ArrayOf<DataValue> outputs;
            try
            {
                outputs = CountAggregateCalculator.CalculateAnnotationCounts(
                    annotationTimes.ToArrayOf(),
                    startTime,
                    endTime,
                    details.ProcessingInterval,
                    kMaxProcessedBufferedOutputs,
                    cancellationToken);
            }
            catch (ServiceResultException exception) when (
                exception.StatusCode == StatusCodes.BadTooManyOperations)
            {
                return exception.StatusCode;
            }

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
                BufferedProcessedOutputs =
                    new HistorianBufferedProcessedPayload(outputs),
                BufferedProcessedOffset = 0
            };
            await EmitProcessedPageAsync(
                state,
                claim: null,
                result,
                nodeToRead,
                timestampsToReturn,
                systemContext,
                cancellationToken).ConfigureAwait(false);
            return ServiceResult.Good;
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
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
            if (provider is not IHistorianAtTimeProvider and
                not IHistorianDataProvider)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.ReadAtTime)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            if (!nodeToRead.ContinuationPoint.IsEmpty)
            {
                result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }
            if (!await SupportsRequestedTimestampsAsync(
                provider,
                node.NodeId,
                timestampsToReturn,
                cancellationToken).ConfigureAwait(false))
            {
                result.StatusCode = StatusCodes.BadTimestampNotSupported;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Insert);

            ArrayOf<DateTimeUtc> reqTimes = details.ReqTimes;

            // Provider push-down
            if (provider is IHistorianAtTimeProvider atTime)
            {
                var atTimeRequest = new HistorianAtTimeReadRequest
                {
                    NodeId = node.NodeId,
                    RequestedTimes = reqTimes,
                    UseSimpleBounds = details.UseSimpleBounds
                };
                ArrayOf<DataValue> values = await atTime.ReadAtTimeAsync(
                    opContext, atTimeRequest, cancellationToken).ConfigureAwait(false);
                if (values.IsNull || values.Count != reqTimes.Count)
                {
                    result.StatusCode = StatusCodes.BadUnexpectedError;
                    result.ContinuationPoint = ByteString.Empty;
                    return StatusCodes.BadUnexpectedError;
                }

                FillHistoryData(
                    systemContext,
                    result,
                    values,
                    nodeToRead,
                    timestampsToReturn);
                result.StatusCode = StatusCodes.Good;
                return ServiceResult.Good;
            }

            // Framework fallback: interpolate from raw values
            if (provider is not IHistorianDataProvider raw)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            List<DataValue> samples = await CollectAllRawAsync(
                opContext,
                raw,
                node.NodeId,
                reqTimes,
                details.UseSimpleBounds,
                capabilities.Stepped,
                cancellationToken)
                .ConfigureAwait(false);

            ArrayOf<DataValue> orderedSamples = samples.ToArrayOf();
            var produced = new List<DataValue>(reqTimes.Count);
            foreach (DateTimeUtc requestedTime in reqTimes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                produced.Add(AggregateCalculator.CalculateAtTime(
                    orderedSamples,
                    requestedTime,
                    details.UseSimpleBounds,
                    capabilities.Stepped));
            }

            FillHistoryData(
                systemContext,
                result,
                produced,
                nodeToRead,
                timestampsToReturn);
            result.StatusCode = StatusCodes.Good;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Dispatches a HistoryRead on an Annotations property
        /// (Part 11 §5.2.7) by translating to the parent variable's
        /// <see cref="IHistorianAnnotationProvider"/> and wrapping each
        /// returned annotation as a <see cref="DataValue"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveSuccessorOrCompleteAsync takes ownership of the initial continuation state on invocation and either saves it to the session or disposes it.")]
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

            HistorianContinuationClaim? claim = await TryClaimContinuationAsync(
                systemContext,
                provider,
                nodeToRead,
                HistorianReadKind.Annotations,
                cancellationToken,
                parentVariable.NodeId).ConfigureAwait(false);
            try
            {
                if (claim == null && !nodeToRead.ContinuationPoint.IsEmpty)
                {
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }
                if (provider is not IHistorianAnnotationProvider annotations)
                {
                    claim?.Retire();
                    return StatusCodes.BadHistoryOperationUnsupported;
                }
                if (!await SupportsRequestedTimestampsAsync(
                    provider,
                    parentVariable.NodeId,
                    timestampsToReturn,
                    cancellationToken).ConfigureAwait(false))
                {
                    claim?.Retire();
                    result.StatusCode = StatusCodes.BadTimestampNotSupported;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }

                HistorianContinuationState? state = claim?.State;
                HistorianAnnotationReadRequest request;
                HistorianResumeToken resumeToken;
                if (state is
                    {
                        Kind: HistorianReadKind.Annotations,
                        AnnotationRequest: { } existing
                    })
                {
                    request = existing;
                    resumeToken = state.ResumeToken;
                }
                else
                {
                    HistorianNodeCapabilities capabilities = await provider
                        .GetCapabilitiesAsync(parentVariable.NodeId, cancellationToken)
                        .ConfigureAwait(false);
                    (DateTimeUtc start, DateTimeUtc end, bool isForward) =
                        NormalizeTimeRange(
                            details.StartTime,
                            details.EndTime);
                    request = new HistorianAnnotationReadRequest
                    {
                        NodeId = parentVariable.NodeId,
                        StartTime = start,
                        EndTime = end,
                        MaxValues = ApplyHistorianLimit(
                            details.NumValuesPerNode,
                            capabilities.MaxReturnDataValues),
                        IsForward = isForward
                    };
                    resumeToken = default;
                }

                HistorianOperationContext opContext = new(
                    systemContext,
                    systemContext.OperationContext!,
                    parentVariable,
                    HistoryUpdateType.Insert);

                HistorianPage<Annotation> page = await annotations.ReadAnnotationsAsync(
                    opContext,
                    request,
                    resumeToken,
                    cancellationToken).ConfigureAwait(false);

                var dataValues = new List<DataValue>(page.Values.Count);
                foreach (Annotation a in page.Values)
                {
                    dataValues.Add(new DataValue(
                        new Variant(new ExtensionObject(a)),
                        StatusCodes.Good,
                        sourceTimestamp: a.AnnotationTime,
                        serverTimestamp: DateTimeUtc.MinValue));
                }
                FillHistoryData(
                    systemContext,
                    result,
                    dataValues,
                    nodeToRead,
                    timestampsToReturn);

                HistorianContinuationState? initialState = null;
                if (claim == null && !page.NextToken.IsEmpty)
                {
                    initialState = new HistorianContinuationState
                    {
                        Id = Guid.NewGuid(),
                        Provider = provider,
                        NodeId = nodeToRead.NodeId,
                        Kind = HistorianReadKind.Annotations,
                        ResumeToken = page.NextToken,
                        AnnotationRequest = request,
                        TimestampsToReturn = timestampsToReturn,
                        IndexRange = nodeToRead.ParsedIndexRange,
                        DataEncoding = nodeToRead.DataEncoding
                    };
                }
                await SaveSuccessorOrCompleteAsync(
                    systemContext,
                    result,
                    claim,
                    hasMore: !page.NextToken.IsEmpty,
                    page.NextToken,
                    initialState,
                    bufferedProcessedOffset: null,
                    cancellationToken).ConfigureAwait(false);

                return ServiceResult.Good;
            }
            finally
            {
                if (claim != null)
                {
                    await claim.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Dispatches a HistoryUpdate on an Annotations property by
        /// translating to the parent variable's
        /// <see cref="IHistorianAnnotationProvider"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<Annotation> failure =
                    CreateFailureOutcome<Annotation>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditAnnotationUpdate(
                    systemContext,
                    details,
                    parentVariable,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(parentVariable.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.InsertAnnotation)
            {
                HistorianUpdateOutcome<Annotation> failure =
                    CreateFailureOutcome<Annotation>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditAnnotationUpdate(
                    systemContext,
                    details,
                    parentVariable,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
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

            HistorianUpdateOutcome<Annotation> outcome = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => await annotations.InsertAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Replace => await annotations.ReplaceAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Update => await annotations.UpdateAnnotationsAsync(
                    opContext, parentVariable.NodeId, annotationList, cancellationToken).ConfigureAwait(false),
                PerformUpdateType.Remove => await annotations.DeleteAnnotationsAsync(
                    opContext, parentVariable.NodeId, times, cancellationToken).ConfigureAwait(false),
                _ => new HistorianUpdateOutcome<Annotation>(
                    RepeatStatus(
                        StatusCodes.BadInvalidArgument,
                        annotationList.Count).ToArrayOf())
            };
            if (outcome.OperationResults.Count != annotationList.Count)
            {
                outcome = CreateFailureOutcome<Annotation>(
                    StatusCodes.BadUnexpectedError,
                    annotationList.Count);
            }

            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditAnnotationUpdate(
                systemContext,
                details,
                parentVariable,
                outcome,
                operationResult.StatusCode);
            return operationResult;
        }

        /// <summary>
        /// Dispatches a generic StructuredHistoryData update.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        public static async ValueTask<ServiceResult>
            DispatchStructuredDataUpdateAsync(
                ServerSystemContext systemContext,
                IHistorianProvider provider,
                NodeState node,
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

            if (provider is not IHistorianStructuredDataProvider structured)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditStructuredUpdate(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            bool supported = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert =>
                    capabilities.InsertStructuredData,
                PerformUpdateType.Replace =>
                    capabilities.ReplaceStructuredData,
                PerformUpdateType.Update =>
                    capabilities.UpdateStructuredData,
                PerformUpdateType.Remove =>
                    capabilities.DeleteStructuredData,
                _ => true
            };
            if (!supported)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.UpdateValues.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditStructuredUpdate(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            var operationContext = new HistorianOperationContext(
                systemContext,
                systemContext.OperationContext!,
                node,
                MapPerformUpdate(details.PerformInsertReplace));
            ArrayOf<DataValue> values = details.UpdateValues;
            HistorianUpdateOutcome<DataValue> outcome =
                details.PerformInsertReplace switch
                {
                    PerformUpdateType.Insert =>
                        await structured.InsertStructuredDataAsync(
                            operationContext,
                            node.NodeId,
                            values,
                            cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Replace =>
                        await structured.ReplaceStructuredDataAsync(
                            operationContext,
                            node.NodeId,
                            values,
                            cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Update =>
                        await structured.UpdateStructuredDataAsync(
                            operationContext,
                            node.NodeId,
                            values,
                            cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Remove =>
                        await structured.RemoveStructuredDataAsync(
                            operationContext,
                            node.NodeId,
                            values,
                            cancellationToken).ConfigureAwait(false),
                    _ => new HistorianUpdateOutcome<DataValue>(
                        RepeatStatus(
                            StatusCodes.BadInvalidArgument,
                            values.Count).ToArrayOf())
                };
            if (outcome.OperationResults.Count != values.Count)
            {
                outcome = CreateFailureOutcome<DataValue>(
                    StatusCodes.BadUnexpectedError,
                    values.Count);
            }

            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditStructuredUpdate(
                systemContext,
                details,
                outcome,
                operationResult.StatusCode);
            return operationResult;
        }

        private static Annotation? DecodeAnnotation(DataValue dv)
        {
            if (dv.WrappedValue.TryGetValue(out ExtensionObject extension) &&
                !extension.IsNull &&
                extension.TryGetValue(out Annotation? annotation))
            {
                return annotation;
            }
            return null;
        }

        /// <summary>
        /// Dispatches a HistoryRead with <c>ReadEventDetails</c> against
        /// an event-history notifier. The provider returns raw event
        /// records; the framework projects each record's fields through
        /// the supplied <c>EventFilter.SelectClauses</c> to build the
        /// returned <c>HistoryEventFieldList</c> entries.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveSuccessorOrCompleteAsync takes ownership of the initial continuation state on invocation and either saves it to the session or disposes it.")]
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

            HistorianContinuationClaim? claim = await TryClaimContinuationAsync(
                systemContext,
                provider,
                nodeToRead,
                HistorianReadKind.Events,
                cancellationToken).ConfigureAwait(false);
            try
            {
                if (claim == null && !nodeToRead.ContinuationPoint.IsEmpty)
                {
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }
                if (provider is not IHistorianEventProvider events)
                {
                    claim?.Retire();
                    return StatusCodes.BadHistoryOperationUnsupported;
                }
                HistorianNodeCapabilities eventCapabilities = await provider
                    .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (!eventCapabilities.ReadEventHistory)
                {
                    claim?.Retire();
                    return StatusCodes.BadHistoryOperationUnsupported;
                }

                HistorianContinuationState? state = claim?.State;
                HistorianEventReadRequest request;
                HistorianResumeToken token;
                if (state is
                    {
                        Kind: HistorianReadKind.Events,
                        EventRequest: { } existing
                    })
                {
                    request = existing;
                    token = state.ResumeToken;
                }
                else
                {
                    HistorianNodeCapabilities capabilities = await provider
                        .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                        .ConfigureAwait(false);
                    (DateTimeUtc start, DateTimeUtc end, bool isForward) =
                        NormalizeTimeRange(
                            details.StartTime,
                            details.EndTime);
                    request = new HistorianEventReadRequest
                    {
                        NodeId = node.NodeId,
                        StartTime = start,
                        EndTime = end,
                        MaxValues = ApplyHistorianLimit(
                            details.NumValuesPerNode,
                            capabilities.MaxReturnEventValues),
                        IsForward = isForward,
                        Filter = details.Filter
                    };
                    token = default;
                }

                HistorianOperationContext opContext = new(
                    systemContext,
                    systemContext.OperationContext!,
                    node,
                    HistoryUpdateType.Insert);

                HistorianPage<HistorianEventRecord> page = await events.ReadEventsAsync(
                    opContext,
                    request,
                    token,
                    cancellationToken).ConfigureAwait(false);

                IServerInternal serverInternal = systemContext.Server;
                var filterContext = new FilterContext(
                    serverInternal.NamespaceUris,
                    serverInternal.TypeTree,
                    systemContext.OperationContext,
                    serverInternal.Telemetry);

                EventFilter filter = request.Filter;
                ArrayOf<HistorianEventRecord> filtered = page.Values;
                if (filter.WhereClause.Elements.Count > 0)
                {
                    var keep = new List<HistorianEventRecord>(page.Values.Count);
                    foreach (HistorianEventRecord record in page.Values)
                    {
                        var target = new HistorianEventFilterTarget(record);
                        var evaluator = new FilterEvaluator(
                            filter.WhereClause,
                            filterContext,
                            target);
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
                    fields[i] = ProjectEventFields(
                        filtered[i],
                        filter,
                        filterContext);
                }

                result.HistoryData = new ExtensionObject(new HistoryEvent
                {
                    Events = fields
                });

                HistorianContinuationState? initialState = null;
                if (claim == null && !page.NextToken.IsEmpty)
                {
                    initialState = new HistorianContinuationState
                    {
                        Id = Guid.NewGuid(),
                        Provider = provider,
                        NodeId = nodeToRead.NodeId,
                        Kind = HistorianReadKind.Events,
                        ResumeToken = page.NextToken,
                        EventRequest = request,
                        TimestampsToReturn = TimestampsToReturn.Source
                    };
                }
                await SaveSuccessorOrCompleteAsync(
                    systemContext,
                    result,
                    claim,
                    hasMore: !page.NextToken.IsEmpty,
                    page.NextToken,
                    initialState,
                    bufferedProcessedOffset: null,
                    cancellationToken).ConfigureAwait(false);
                return ServiceResult.Good;
            }
            finally
            {
                if (claim != null)
                {
                    await claim.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Dispatches an UpdateEventDetails HistoryUpdate.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.EventData.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventUpdate(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            bool supported = details.PerformInsertReplace switch
            {
                PerformUpdateType.Insert => capabilities.InsertEvent,
                PerformUpdateType.Replace => capabilities.ReplaceEvent,
                PerformUpdateType.Update => capabilities.UpdateEvent,
                _ => true
            };
            if (!supported)
            {
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.EventData.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventUpdate(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            ServiceResult validation = HistorianEventUpdateValidator.Validate(
                systemContext,
                node,
                details,
                capabilities,
                out HistorianEventUpdatePlan plan);
            if (ServiceResult.IsBad(validation))
            {
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        validation.StatusCode,
                        details.EventData.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventUpdate(
                    systemContext,
                    details,
                    failure,
                    validation.StatusCode);
                return validation;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                MapPerformUpdate(details.PerformInsertReplace));

            ArrayOf<HistoryEventFieldList> incoming = details.EventData;
            var decoded = new List<HistorianEventRecord>(incoming.Count);
            var decodedIndexes = new List<int>(incoming.Count);
            var decodedStatuses = new List<StatusCode>(incoming.Count);
            var statuses = new StatusCode[incoming.Count];
            var diagnostics = new DiagnosticInfo[incoming.Count];
            bool hasDiagnostics = false;
            for (int i = 0; i < incoming.Count; i++)
            {
                HistorianEventDecodeResult decodedEvent =
                    await HistorianEventUpdateValidator.DecodeAsync(
                    systemContext,
                    capabilities,
                    incoming[i],
                    details.Filter,
                    plan,
                    cancellationToken).ConfigureAwait(false);
                DiagnosticInfo? diagnostic = CreateEventFieldDiagnostic(
                    systemContext,
                    decodedEvent.StatusCode,
                    decodedEvent.FieldIndexes,
                    decodedEvent.FieldNames);
                if (diagnostic != null)
                {
                    diagnostics[i] = diagnostic;
                    hasDiagnostics = true;
                }
                if (StatusCode.IsBad(decodedEvent.StatusCode))
                {
                    statuses[i] = decodedEvent.StatusCode;
                    continue;
                }
                decoded.Add(decodedEvent.Record!);
                decodedIndexes.Add(i);
                decodedStatuses.Add(decodedEvent.StatusCode);
            }

            HistorianUpdateOutcome<HistorianEventRecord> providerOutcome =
                decoded.Count == 0
                ? new HistorianUpdateOutcome<HistorianEventRecord>(
                    [])
                : details.PerformInsertReplace switch
                {
                    PerformUpdateType.Insert => await events.InsertEventsAsync(
                        opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Replace => await events.ReplaceEventsAsync(
                        opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                    PerformUpdateType.Update => await events.UpdateEventsAsync(
                        opContext, node.NodeId, decoded, cancellationToken).ConfigureAwait(false),
                    _ => new HistorianUpdateOutcome<HistorianEventRecord>(
                        RepeatStatus(StatusCodes.BadInvalidArgument, decoded.Count).ToArrayOf())
                };

            if (providerOutcome.OperationResults.Count != decoded.Count)
            {
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        StatusCodes.BadUnexpectedError,
                        incoming.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventUpdate(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadUnexpectedError);
                return StatusCodes.BadUnexpectedError;
            }
            for (int i = 0; i < decodedIndexes.Count; i++)
            {
                int decodedIndex = decodedIndexes[i];
                StatusCode providerStatus = providerOutcome.OperationResults[i];
                statuses[decodedIndex] =
                    StatusCode.IsGood(providerStatus) &&
                    decodedStatuses[i].Code == StatusCodes.GoodDataIgnored.Code
                        ? StatusCodes.GoodDataIgnored
                        : providerStatus;
                if (diagnostics[decodedIndex] == null &&
                    !providerOutcome.DiagnosticInfos.IsEmpty &&
                    providerOutcome.DiagnosticInfos[i] != null)
                {
                    diagnostics[decodedIndex] =
                        providerOutcome.DiagnosticInfos[i];
                    hasDiagnostics = true;
                }
            }
            var outcome = new HistorianUpdateOutcome<HistorianEventRecord>(
                statuses.ToArrayOf(),
                providerOutcome.OldValues,
                hasDiagnostics
                    ? diagnostics.ToArrayOf()
                    : [],
                providerOutcome.TransactionRolledBack);
            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditEventUpdate(
                systemContext,
                details,
                outcome,
                operationResult.StatusCode);
            return operationResult;
        }

        /// <summary>
        /// Dispatches a DeleteEventDetails HistoryUpdate.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.EventIds.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventDelete(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                .ConfigureAwait(false);
            if (!capabilities.DeleteEvent)
            {
                HistorianUpdateOutcome<HistorianEventRecord> failure =
                    CreateFailureOutcome<HistorianEventRecord>(
                        StatusCodes.BadHistoryOperationUnsupported,
                        details.EventIds.Count);
                result.OperationResults = failure.OperationResults;
                ReportAuditEventDelete(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadHistoryOperationUnsupported);
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianOperationContext opContext = new(
                systemContext,
                systemContext.OperationContext!,
                node,
                HistoryUpdateType.Delete);

            ArrayOf<ByteString> ids = details.EventIds;
            HistorianUpdateOutcome<HistorianEventRecord> outcome =
                await events.DeleteEventsAsync(
                opContext, node.NodeId, ids, cancellationToken).ConfigureAwait(false);
            if (outcome.OperationResults.Count != ids.Count)
            {
                outcome = CreateFailureOutcome<HistorianEventRecord>(
                    StatusCodes.BadUnexpectedError,
                    ids.Count);
            }

            result.OperationResults = outcome.OperationResults;
            result.DiagnosticInfos = outcome.DiagnosticInfos;
            ServiceResult operationResult = GetOperationResult(outcome);
            ReportAuditEventDelete(
                systemContext,
                details,
                outcome,
                operationResult.StatusCode);
            return operationResult;
        }

        /// <summary>
        /// Projects an event record's fields through the supplied filter's
        /// <c>SelectClauses</c>. Operands whose browse path does not
        /// resolve to a field receive an empty <see cref="Variant"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="record"/> is <c>null</c>.</exception>
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

        private static Variant ResolveOperand(
            HistorianEventRecord record,
            SimpleAttributeOperand op)
        {
            if (op.BrowsePath.Count == 0)
            {
                if (op.AttributeId == Attributes.NodeId)
                {
                    return new Variant(record.EventType);
                }
                return default;
            }
            if (!record.TryGetQualifiedField(
                    HistorianEventFieldKey.FromOperand(op),
                    out Variant value) &&
                !record.TryGetQualifiedField(
                    new HistorianEventFieldKey(
                        op.TypeDefinitionId,
                        op.BrowsePath,
                        op.AttributeId,
                        null),
                    out value))
            {
                if (record.QualifiedFields.Count != 0)
                {
                    return default;
                }
                string key = HistorianEventFieldKey.BuildPath(op.BrowsePath);
                if (!record.TryGetField(key, out value))
                {
                    return default;
                }
            }
            if (!string.IsNullOrEmpty(op.IndexRange))
            {
                ServiceResult validation = NumericRange.Validate(
                    op.IndexRange,
                    out NumericRange range);
                if (ServiceResult.IsBad(validation) ||
                    StatusCode.IsBad(range.ApplyRange(ref value)))
                {
                    return default;
                }
            }
            return value;
        }

        private static HistoryEventFieldList ProjectEventFields(
            HistorianEventRecord record,
            EventFilter filter,
            IFilterContext context)
        {
            var target = new HistorianEventFilterTarget(record);
            var fields = new Variant[filter.SelectClauses.Count];
            for (int i = 0; i < filter.SelectClauses.Count; i++)
            {
                SimpleAttributeOperand operand = filter.SelectClauses[i];
                ServiceResult validation = NumericRange.Validate(
                    operand.IndexRange ?? string.Empty,
                    out NumericRange indexRange);
                if (ServiceResult.IsBad(validation))
                {
                    fields[i] = Variant.Null;
                    continue;
                }
                fields[i] = target.GetAttributeValue(
                    context,
                    operand.TypeDefinitionId,
                    operand.BrowsePath,
                    operand.AttributeId,
                    indexRange);
            }
            return new HistoryEventFieldList
            {
                EventFields = fields
            };
        }

        /// <summary>
        /// Releases a continuation point that was previously saved by the
        /// dispatcher.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
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

            return systemContext.OperationContext?.Session?.ContinuationPoints
                .ReleaseHistory(nodeToRead.ContinuationPoint) == true
                ? ServiceResult.Good
                : StatusCodes.BadContinuationPointInvalid;
        }

        /// <summary>
        /// Asynchronously releases a portable continuation point.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="systemContext"/> is <c>null</c>.</exception>
        public static async ValueTask<ServiceResult> ReleaseContinuationPointAsync(
            ServerSystemContext systemContext,
            HistoryReadValueId nodeToRead,
            CancellationToken cancellationToken = default)
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

            ISessionContinuationPoints? continuationPoints =
                systemContext.OperationContext?.Session?.ContinuationPoints;
            if (continuationPoints == null)
            {
                return StatusCodes.BadContinuationPointInvalid;
            }
            IHistoryContinuationPoint? state = await continuationPoints
                .RestoreHistoryAsync(
                    nodeToRead.ContinuationPoint,
                    cancellationToken)
                .ConfigureAwait(false);
            if (state != null)
            {
                state.Dispose();
                return ServiceResult.Good;
            }
            return StatusCodes.BadContinuationPointInvalid;
        }

        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveSuccessorOrCompleteAsync takes ownership of the initial continuation state on invocation and either saves it to the session or disposes it.")]
        private static async ValueTask<ServiceResult> ReadRawPageAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            HistorianContinuationClaim? claim,
            HistorianOperationContext opContext,
            CancellationToken cancellationToken)
        {
            if (provider is not IHistorianDataProvider data)
            {
                claim?.Retire();
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            // An IndexRange selects elements from an array Value; it cannot select
            // anything from scalar history values, so report Bad_IndexRangeNoData at
            // the operation level (OPC UA Part 11; CTT HA Read Raw Err-009).
            if (!nodeToRead.ParsedIndexRange.IsNull &&
                node is BaseVariableState scalarCheck &&
                scalarCheck.ValueRank == ValueRanks.Scalar)
            {
                claim?.Retire();
                result.StatusCode = StatusCodes.BadIndexRangeNoData;
                result.ContinuationPoint = ByteString.Empty;
                return ServiceResult.Good;
            }

            HistorianContinuationState? state = claim?.State;
            HistorianRawReadRequest request;
            HistorianResumeToken token;
            if (state is { Kind: HistorianReadKind.Raw, RawRequest: { } existingRaw })
            {
                request = existingRaw;
                token = state.ResumeToken;
            }
            else
            {
                HistorianNodeCapabilities capabilities = await provider
                    .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                (DateTimeUtc start, DateTimeUtc end, bool isForward) =
                    NormalizeTimeRange(
                        details.StartTime,
                        details.EndTime);

                request = new HistorianRawReadRequest
                {
                    NodeId = node.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = ApplyHistorianLimit(
                        details.NumValuesPerNode,
                        capabilities.MaxReturnDataValues),
                    IsForward = isForward,
                    ReturnBounds = details.ReturnBounds
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
            FillHistoryData(
                systemContext,
                result,
                values,
                nodeToRead,
                timestampsToReturn);

            HistorianContinuationState? initialState = null;
            if (claim == null && !page.NextToken.IsEmpty)
            {
                initialState = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = node.NodeId,
                    Kind = HistorianReadKind.Raw,
                    ResumeToken = page.NextToken,
                    RawRequest = request,
                    TimestampsToReturn = timestampsToReturn,
                    IndexRange = nodeToRead.ParsedIndexRange,
                    DataEncoding = nodeToRead.DataEncoding
                };
            }
            await SaveSuccessorOrCompleteAsync(
                systemContext,
                result,
                claim,
                hasMore: !page.NextToken.IsEmpty,
                page.NextToken,
                initialState,
                bufferedProcessedOffset: null,
                cancellationToken).ConfigureAwait(false);

            // Per OPC UA Part 11 6.5.3.2, an interval in which no data exists (and no
            // Bounding Values were requested/returned) is reported with Good_NoData.
            if (values.Count == 0 && page.NextToken.IsEmpty)
            {
                result.StatusCode = StatusCodes.GoodNoData;
            }

            return ServiceResult.Good;
        }

        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveSuccessorOrCompleteAsync takes ownership of the initial continuation state on invocation and either saves it to the session or disposes it.")]
        private static async ValueTask<ServiceResult> ReadModifiedPageAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            NodeState node,
            HistoryReadValueId nodeToRead,
            ReadRawModifiedDetails details,
            TimestampsToReturn timestampsToReturn,
            HistoryReadResult result,
            HistorianContinuationClaim? claim,
            HistorianOperationContext opContext,
            CancellationToken cancellationToken)
        {
            if (provider is not IHistorianModifiedProvider modified)
            {
                claim?.Retire();
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            HistorianContinuationState? state = claim?.State;
            HistorianModifiedReadRequest request;
            HistorianResumeToken token;
            if (state is { Kind: HistorianReadKind.Modified, ModifiedRequest: { } existing })
            {
                request = existing;
                token = state.ResumeToken;
            }
            else
            {
                HistorianNodeCapabilities capabilities = await provider
                    .GetCapabilitiesAsync(node.NodeId, cancellationToken)
                    .ConfigureAwait(false);
                (DateTimeUtc start, DateTimeUtc end, bool isForward) =
                    NormalizeTimeRange(
                        details.StartTime,
                        details.EndTime);

                request = new HistorianModifiedReadRequest
                {
                    NodeId = node.NodeId,
                    StartTime = start,
                    EndTime = end,
                    MaxValues = ApplyHistorianLimit(
                        details.NumValuesPerNode,
                        capabilities.MaxReturnDataValues),
                    IsForward = isForward
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
            FillHistoryModifiedData(
                systemContext,
                result,
                values,
                infos,
                nodeToRead,
                timestampsToReturn);

            HistorianContinuationState? initialState = null;
            if (claim == null && !page.NextToken.IsEmpty)
            {
                initialState = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = node.NodeId,
                    Kind = HistorianReadKind.Modified,
                    ResumeToken = page.NextToken,
                    ModifiedRequest = request,
                    TimestampsToReturn = timestampsToReturn,
                    IndexRange = nodeToRead.ParsedIndexRange,
                    DataEncoding = nodeToRead.DataEncoding
                };
            }
            await SaveSuccessorOrCompleteAsync(
                systemContext,
                result,
                claim,
                hasMore: !page.NextToken.IsEmpty,
                page.NextToken,
                initialState,
                bufferedProcessedOffset: null,
                cancellationToken).ConfigureAwait(false);

            // Per OPC UA Part 11 6.5.3.2, an interval in which no data exists is reported
            // with Good_NoData.
            if (values.Count == 0 && page.NextToken.IsEmpty)
            {
                result.StatusCode = StatusCodes.GoodNoData;
            }

            return ServiceResult.Good;
        }

        private static async ValueTask<HistorianContinuationClaim?>
            TryClaimContinuationAsync(
            ServerSystemContext systemContext,
            IHistorianProvider provider,
            HistoryReadValueId nodeToRead,
            HistorianReadKind expectedKind,
            CancellationToken cancellationToken,
            NodeId legacyAnnotationProviderNodeId = default)
        {
            if (nodeToRead.ContinuationPoint.IsEmpty)
            {
                return null;
            }
            ISessionContinuationPoints? continuationPoints =
                systemContext.OperationContext?.Session?.ContinuationPoints;
            if (continuationPoints == null)
            {
                return null;
            }
            IHistoryContinuationPoint? raw = await continuationPoints
                .RestoreHistoryAsync(nodeToRead.ContinuationPoint, cancellationToken)
                .ConfigureAwait(false);
            if (raw is not HistorianContinuationState state)
            {
                raw?.Dispose();
                return null;
            }
            HistorianContinuationClaim? claim = null;
            try
            {
                claim = new HistorianContinuationClaim(
                    continuationPoints,
                    state);
                bool annotationProviderNodeMatches =
                    expectedKind != HistorianReadKind.Annotations ||
                    (!legacyAnnotationProviderNodeId.IsNull &&
                        state.AnnotationRequest is { } annotationRequest &&
                        annotationRequest.NodeId ==
                            legacyAnnotationProviderNodeId);
                bool legacyAnnotationNodeMatches =
                    expectedKind == HistorianReadKind.Annotations &&
                    state.UsesLegacyAnnotationNodeId &&
                    !nodeToRead.NodeId.IsNull &&
                    !legacyAnnotationProviderNodeId.IsNull &&
                    state.NodeId == legacyAnnotationProviderNodeId &&
                    annotationProviderNodeMatches;
                if (state.Kind != expectedKind ||
                    !annotationProviderNodeMatches ||
                    (state.NodeId != nodeToRead.NodeId &&
                        !legacyAnnotationNodeMatches))
                {
                    claim.Retire();
                    return null;
                }
                if (legacyAnnotationNodeMatches)
                {
                    claim.NormalizeLegacyAnnotationNodeId(
                        nodeToRead.NodeId);
                }
                HistorianContinuationState claimedState = claim.State;
                if (!ReferenceEquals(claimedState.Provider, provider) &&
                    (claimedState.Provider is not
                            IHistorianProviderIdentity savedIdentity ||
                            provider is not IHistorianProviderIdentity currentIdentity ||
                            !string.Equals(
                                savedIdentity.ProviderId,
                                currentIdentity.ProviderId,
                                StringComparison.Ordinal)))
                {
                    claim.Retire();
                    return null;
                }
                HistorianContinuationClaim result = claim;
                claim = null;
                return result;
            }
            finally
            {
                if (claim != null)
                {
                    await claim.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask SaveSuccessorOrCompleteAsync(
            ServerSystemContext systemContext,
            HistoryReadResult result,
            HistorianContinuationClaim? claim,
            bool hasMore,
            HistorianResumeToken nextToken,
            HistorianContinuationState? initialState,
            int? bufferedProcessedOffset,
            CancellationToken cancellationToken)
        {
            if (!hasMore)
            {
                initialState?.Dispose();
                claim?.Retire();
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = ByteString.Empty;
                return;
            }

            HistorianContinuationState successor;
            if (claim != null)
            {
                try
                {
                    successor = await claim.CommitSuccessorAsync(
                        nextToken,
                        bufferedProcessedOffset,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is ServiceResultException or
                    InvalidOperationException or
                    IOException or
                    TimeoutException)
                {
                    result.StatusCode =
                        exception is ServiceResultException serviceException &&
                        serviceException.StatusCode == StatusCodes.BadSessionClosed
                            ? StatusCodes.BadSessionClosed
                            : StatusCodes.BadNoContinuationPoints;
                    result.ContinuationPoint = ByteString.Empty;
                    return;
                }
            }
            else
            {
                ISessionContinuationPoints? continuationPoints =
                    systemContext.OperationContext?.Session?.ContinuationPoints;
                if (continuationPoints == null || initialState == null)
                {
                    initialState?.Dispose();
                    result.StatusCode = StatusCodes.BadNoContinuationPoints;
                    result.ContinuationPoint = ByteString.Empty;
                    return;
                }
                if (!await TrySaveHistoryContinuationAsync(
                        continuationPoints,
                        initialState,
                        result,
                        cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                successor = initialState;
            }

            result.StatusCode = StatusCodes.Good;
            result.ContinuationPoint = new ByteString(
                successor.Id.ToByteArray());
        }

        private static async ValueTask<bool> TrySaveHistoryContinuationAsync(
            ISessionContinuationPoints continuationPoints,
            HistorianContinuationState state,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                await continuationPoints.SaveHistoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ServiceResultException or
                InvalidOperationException or
                IOException or
                TimeoutException)
            {
                result.StatusCode = exception is ServiceResultException serviceException &&
                    serviceException.StatusCode == StatusCodes.BadSessionClosed
                        ? StatusCodes.BadSessionClosed
                        : StatusCodes.BadNoContinuationPoints;
                result.ContinuationPoint = ByteString.Empty;
                return false;
            }
        }

        private static void FillHistoryData(
            ISystemContext systemContext,
            HistoryReadResult result,
            ArrayOf<DataValue> values,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn)
        {
            var filtered = new List<DataValue>(values.Count);
            foreach (DataValue v in values)
            {
                DataValue clone = ApplyTimestampFilter(v, timestampsToReturn);
                clone = ApplyIndexRange(clone, nodeToRead.ParsedIndexRange);
                clone = ApplyEncoding(
                    systemContext,
                    clone,
                    nodeToRead.DataEncoding);
                filtered.Add(clone);
            }
            var data = new HistoryData { DataValues = filtered };
            result.HistoryData = new ExtensionObject(data);
        }

        private static void FillHistoryModifiedData(
            ISystemContext systemContext,
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
                clone = ApplyEncoding(
                    systemContext,
                    clone,
                    nodeToRead.DataEncoding);
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
                ModificationInfos = modInfos
            };
            result.HistoryData = new ExtensionObject(data);
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

        private static DataValue ApplyEncoding(
            ISystemContext systemContext,
            DataValue value,
            QualifiedName dataEncoding)
        {
            if (!dataEncoding.IsNull && StatusCode.IsGood(value.StatusCode))
            {
                Variant variant = value.WrappedValue;
                ServiceResult result = EncodeableObject.ApplyDataEncoding(
                    systemContext.AsMessageContext(),
                    dataEncoding,
                    ref variant);
                if (ServiceResult.IsBad(result))
                {
                    return value.WithWrappedValue(default).WithStatus(result.StatusCode);
                }
                return value.WithWrappedValue(variant);
            }
            return value;
        }

        private static bool FlushCalculator(
            IAggregateCalculator calculator,
            List<DataValue> output,
            bool partial,
            int maxValues)
        {
            while (calculator.TryGetProcessedValue(partial, out DataValue computed))
            {
                if (output.Count >= maxValues)
                {
                    return false;
                }
                output.Add(computed);
            }
            return true;
        }

        private static async ValueTask<List<DataValue>> CollectAllRawAsync(
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            ArrayOf<DateTimeUtc> times,
            bool useSimpleBounds,
            bool stepped,
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
                ReturnBounds = true
            };

            var collected = new List<DataValue>();
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    context, request, token, cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue v in page.Values)
                {
                    if (v.Value.StatusCode != StatusCodes.BadBoundNotFound)
                    {
                        collected.Add(v.Value);
                    }
                }
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }
            if (max == DateTimeUtc.MaxValue &&
                min != max &&
                !collected.Exists(value =>
                    value.SourceTimestamp == DateTimeUtc.MaxValue))
            {
                await AddExactRawValuesAsync(
                    collected,
                    context,
                    raw,
                    nodeId,
                    DateTimeUtc.MaxValue,
                    cancellationToken).ConfigureAwait(false);
            }
            collected.Sort((a, b) => a.SourceTimestamp.CompareTo(b.SourceTimestamp));
            if (!useSimpleBounds)
            {
                bool hasNonBadBefore = false;
                bool hasNonBadAfter = false;
                for (int i = 0; i < collected.Count; i++)
                {
                    DataValue value = collected[i];
                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        continue;
                    }
                    hasNonBadBefore |= value.SourceTimestamp < min;
                    hasNonBadAfter |= value.SourceTimestamp > max;
                }
                if (!hasNonBadBefore)
                {
                    _ = await AddOuterNonBadValuesAsync(
                        collected,
                        context,
                        raw,
                        nodeId,
                        min,
                        before: true,
                        requiredCount: 1,
                        cancellationToken).ConfigureAwait(false);
                }
                if (!hasNonBadAfter)
                {
                    hasNonBadAfter = await AddOuterNonBadValuesAsync(
                        collected,
                        context,
                        raw,
                        nodeId,
                        max,
                        before: false,
                        requiredCount: 1,
                        cancellationToken).ConfigureAwait(false) > 0;
                }
                if (!stepped && !hasNonBadAfter)
                {
                    int precedingCount = CountDistinctNonBadValuesBefore(
                        collected,
                        max);
                    if (precedingCount < 2)
                    {
                        _ = await AddOuterNonBadValuesAsync(
                            collected,
                            context,
                            raw,
                            nodeId,
                            max,
                            before: true,
                            requiredCount: 2 - precedingCount,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                collected.Sort((a, b) =>
                    a.SourceTimestamp.CompareTo(b.SourceTimestamp));
            }
            return collected;
        }

        private static async ValueTask AddExactRawValuesAsync(
            List<DataValue> collected,
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            DateTimeUtc timestamp,
            CancellationToken cancellationToken)
        {
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = timestamp,
                EndTime = timestamp,
                MaxValues = 0,
                IsForward = true,
                ReturnBounds = false
            };
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page =
                    await raw.ReadRawAsync(
                        context,
                        request,
                        token,
                        cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue historicalValue in page.Values)
                {
                    if (historicalValue.Value.SourceTimestamp == timestamp)
                    {
                        collected.Add(historicalValue.Value);
                    }
                }
                if (page.IsFinal)
                {
                    return;
                }
                token = page.NextToken;
            }
        }

        private static async ValueTask<int> AddOuterNonBadValuesAsync(
            List<DataValue> collected,
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            DateTimeUtc boundary,
            bool before,
            int requiredCount,
            CancellationToken cancellationToken)
        {
            var existingTimestamps = new HashSet<DateTimeUtc>();
            for (int i = 0; i < collected.Count; i++)
            {
                if (StatusCode.IsNotBad(collected[i].StatusCode))
                {
                    existingTimestamps.Add(collected[i].SourceTimestamp);
                }
            }
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = before ? DateTimeUtc.MinValue : boundary,
                EndTime = before ? boundary : DateTimeUtc.MaxValue,
                MaxValues = 0,
                IsForward = !before,
                ReturnBounds = false
            };
            int added = 0;
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page =
                    await raw.ReadRawAsync(
                        context,
                        request,
                        token,
                        cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue historicalValue in page.Values)
                {
                    DataValue value = historicalValue.Value;
                    bool outside = before
                        ? value.SourceTimestamp < boundary
                        : value.SourceTimestamp > boundary;
                    if (outside &&
                        StatusCode.IsNotBad(value.StatusCode) &&
                        existingTimestamps.Add(value.SourceTimestamp))
                    {
                        collected.Add(value);
                        added++;
                        if (added >= requiredCount)
                        {
                            return added;
                        }
                    }
                }
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }

            DateTimeUtc extreme = before
                ? DateTimeUtc.MinValue
                : DateTimeUtc.MaxValue;
            bool extremeIsOutside = before
                ? extreme < boundary
                : extreme > boundary;
            if (!extremeIsOutside || added >= requiredCount)
            {
                return added;
            }

            var exactRequest = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = extreme,
                EndTime = extreme,
                MaxValues = 0,
                IsForward = true,
                ReturnBounds = false
            };
            token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page =
                    await raw.ReadRawAsync(
                        context,
                        exactRequest,
                        token,
                        cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue historicalValue in page.Values)
                {
                    DataValue value = historicalValue.Value;
                    if (StatusCode.IsNotBad(value.StatusCode) &&
                        existingTimestamps.Add(value.SourceTimestamp))
                    {
                        collected.Add(value);
                        added++;
                        if (added >= requiredCount)
                        {
                            return added;
                        }
                    }
                }
                if (page.IsFinal)
                {
                    return added;
                }
                token = page.NextToken;
            }
        }

        private static int CountDistinctNonBadValuesBefore(
            List<DataValue> values,
            DateTimeUtc boundary)
        {
            var timestamps = new HashSet<DateTimeUtc>();
            for (int i = 0; i < values.Count; i++)
            {
                DataValue value = values[i];
                if (value.SourceTimestamp < boundary &&
                    StatusCode.IsNotBad(value.StatusCode))
                {
                    timestamps.Add(value.SourceTimestamp);
                }
            }
            return timestamps.Count;
        }

        private static HistoryUpdateType MapPerformUpdate(PerformUpdateType performUpdate)
        {
            return performUpdate switch
            {
                PerformUpdateType.Insert => HistoryUpdateType.Insert,
                PerformUpdateType.Replace => HistoryUpdateType.Replace,
                PerformUpdateType.Update => HistoryUpdateType.Update,
                _ => HistoryUpdateType.Insert
            };
        }

        private static async ValueTask<bool> SupportsRequestedTimestampsAsync(
            IHistorianProvider provider,
            NodeId nodeId,
            TimestampsToReturn timestampsToReturn,
            CancellationToken cancellationToken)
        {
            if (timestampsToReturn == TimestampsToReturn.Source)
            {
                return true;
            }
            HistorianNodeCapabilities capabilities = await provider
                .GetCapabilitiesAsync(nodeId, cancellationToken)
                .ConfigureAwait(false);
            return capabilities.ServerTimestampSupported;
        }

        private static uint ApplyHistorianLimit(uint requested, uint capabilityLimit)
        {
            if (requested == 0)
            {
                return capabilityLimit;
            }
            return capabilityLimit == 0
                ? requested
                : Math.Min(requested, capabilityLimit);
        }

        private static (
            DateTimeUtc Start,
            DateTimeUtc End,
            bool IsForward) NormalizeTimeRange(
                DateTimeUtc requestedStart,
                DateTimeUtc requestedEnd)
        {
            bool startSpecified = requestedStart != DateTimeUtc.MinValue;
            bool endSpecified = requestedEnd != DateTimeUtc.MinValue;
            bool isForward = startSpecified &&
                (!endSpecified || requestedStart <= requestedEnd);
            if (!startSpecified)
            {
                return (
                    DateTimeUtc.MinValue,
                    requestedEnd,
                    isForward);
            }
            if (!endSpecified)
            {
                return (
                    requestedStart,
                    DateTimeUtc.MaxValue,
                    isForward);
            }
            return isForward
                ? (requestedStart, requestedEnd, true)
                : (requestedEnd, requestedStart, false);
        }

        private static HistorianUpdateOutcome<T> CreateFailureOutcome<T>(
            StatusCode status,
            int count)
        {
            return new HistorianUpdateOutcome<T>(
                RepeatStatus(status, count).ToArrayOf());
        }

        private static DiagnosticInfo? CreateEventFieldDiagnostic(
            ServerSystemContext systemContext,
            StatusCode statusCode,
            ArrayOf<int> fieldIndexes,
            ArrayOf<string> fieldNames)
        {
            OperationContext? operationContext =
                systemContext.OperationContext;
            if (operationContext == null ||
                fieldIndexes.IsEmpty ||
                fieldIndexes.Count != fieldNames.Count ||
                (operationContext.DiagnosticsMask &
                    DiagnosticsMasks.OperationAll) == 0)
            {
                return null;
            }

            var indexes = new StringBuilder();
            var names = new StringBuilder();
            for (int i = 0; i < fieldIndexes.Count; i++)
            {
                if (i > 0)
                {
                    indexes.Append(' ');
                    names.Append(' ');
                }
                indexes.Append(
                    fieldIndexes[i].ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                names.Append(fieldNames[i]);
            }
            var serviceResult = new ServiceResult(
                indexes.ToString(),
                statusCode,
                new LocalizedText(names.ToString()));
            return new DiagnosticInfo(
                serviceResult,
                operationContext.DiagnosticsMask,
                false,
                operationContext.StringTable,
                systemContext.Server.Telemetry.CreateLogger(
                    nameof(HistorianDispatcher)));
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

        private static StatusCode AggregateStatus(ArrayOf<StatusCode> statuses)
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
            return systemContext.Server;
        }

        /// <summary>
        /// Returns <c>true</c> when at least one status in <paramref name="statuses"/>
        /// is <see cref="StatusCode.IsGood(StatusCode)"/>.
        /// </summary>
        private static bool HasAnyGood(ArrayOf<StatusCode> statuses)
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

        private static ServiceResult GetOperationResult<T>(
            HistorianUpdateOutcome<T> outcome)
        {
            if (outcome.TransactionRolledBack)
            {
                return StatusCodes.BadTransactionFailed;
            }
            if (outcome.OperationResults.Count > 0 &&
                !HasAnyGood(outcome.OperationResults))
            {
                return AggregateStatus(outcome.OperationResults);
            }
            return ServiceResult.Good;
        }

        private static void ReportAuditUpdateData(
            ServerSystemContext systemContext,
            UpdateDataDetails details,
            HistorianUpdateOutcome<DataValue> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryValueUpdateEvent(
                systemContext,
                details,
                outcome.OldValues.ToArray() ?? [],
                status,
                logger);
        }

        private static void ReportAuditAnnotationUpdate(
            ServerSystemContext systemContext,
            UpdateStructureDataDetails details,
            BaseVariableState parentVariable,
            HistorianUpdateOutcome<Annotation> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            _ = parentVariable;
            server.ReportAuditHistoryAnnotationUpdateEvent(
                systemContext,
                details,
                outcome.OldValues,
                status,
                logger);
        }

        private static void ReportAuditStructuredUpdate(
            ServerSystemContext systemContext,
            UpdateStructureDataDetails details,
            HistorianUpdateOutcome<DataValue> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryValueUpdateEvent(
                systemContext,
                details,
                outcome.OldValues,
                status,
                logger);
        }

        private static void ReportAuditDeleteRaw(
            ServerSystemContext systemContext,
            DeleteRawModifiedDetails details,
            HistorianUpdateOutcome<DataValue> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryRawModifyDeleteEvent(
                systemContext,
                details,
                outcome.OldValues,
                status,
                logger);
        }

        private static void ReportAuditDeleteAtTime(
            ServerSystemContext systemContext,
            DeleteAtTimeDetails details,
            HistorianUpdateOutcome<DataValue> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            server.ReportAuditHistoryAtTimeDeleteEvent(
                systemContext,
                details,
                outcome.OldValues.ToArray() ?? [],
                status,
                logger);
        }

        private static void ReportAuditEventUpdate(
            ServerSystemContext systemContext,
            UpdateEventDetails details,
            HistorianUpdateOutcome<HistorianEventRecord> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            var oldValues = new HistoryEventFieldList[outcome.OldValues.Count];
            IServerInternal serverInternal = systemContext.Server;
            var filterContext = new FilterContext(
                serverInternal.NamespaceUris,
                serverInternal.TypeTree,
                systemContext.OperationContext,
                serverInternal.Telemetry);
            for (int i = 0; i < oldValues.Length; i++)
            {
                oldValues[i] = ProjectEventFields(
                    outcome.OldValues[i],
                    details.Filter,
                    filterContext);
            }
            server.ReportAuditHistoryEventUpdateEvent(
                systemContext,
                details,
                oldValues.ToArrayOf(),
                status,
                logger);
        }

        private static void ReportAuditEventDelete(
            ServerSystemContext systemContext,
            DeleteEventDetails details,
            HistorianUpdateOutcome<HistorianEventRecord> outcome,
            StatusCode status)
        {
            IAuditEventServer? server = GetAuditServer(systemContext);
            ILogger? logger = GetAuditLogger(systemContext);
            if (server == null || logger == null)
            {
                return;
            }
            if (outcome.OldValues.IsEmpty)
            {
                server.ReportAuditHistoryEventDeleteEvent(
                    systemContext,
                    details,
                    oldValue: null,
                    status,
                    logger);
                return;
            }
            for (int i = 0; i < outcome.OldValues.Count; i++)
            {
                server.ReportAuditHistoryEventDeleteEvent(
                    systemContext,
                    details,
                    ProjectDeletedEvent(outcome.OldValues[i]),
                    status,
                    logger);
            }
        }

        private static HistoryEventFieldList ProjectDeletedEvent(
            HistorianEventRecord record)
        {
            var fields = new List<KeyValuePair<string, Variant>>();
            foreach (KeyValuePair<HistorianEventFieldKey, Variant> field in
                record.QualifiedFields)
            {
                HistorianEventFieldKey key = field.Key;
                StringBuilder identity = new StringBuilder()
                    .Append(key.TypeDefinitionId)
                    .Append('|')
                    .Append(key.AttributeId)
                    .Append('|')
                    .Append(key.IndexRange);
                for (int i = 0; i < key.BrowsePath.Count; i++)
                {
                    identity
                        .Append('|')
                        .Append(key.BrowsePath[i].NamespaceIndex)
                        .Append(':')
                        .Append(key.BrowsePath[i].Name);
                }
                fields.Add(new KeyValuePair<string, Variant>(
                    identity.ToString(),
                    field.Value));
            }
            if (fields.Count == 0)
            {
                foreach (KeyValuePair<string, Variant> field in record.Fields)
                {
                    fields.Add(field);
                }
            }
            fields.Sort(static (left, right) =>
                string.CompareOrdinal(
                    left.Key,
                    right.Key));
            var values = new Variant[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                values[i] = fields[i].Value;
            }
            return new HistoryEventFieldList
            {
                EventFields = values
            };
        }
    }
}
