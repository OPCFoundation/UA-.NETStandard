// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
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
        /// Checks that the effective user access level of <paramref name="variable"/>
        /// includes <paramref name="requiredAccessLevel"/>. A failure reported by the
        /// <see cref="BaseVariableState.OnReadUserAccessLevel"/> callback is returned
        /// as is, so a callback that cannot determine the access level denies access.
        /// </summary>
        internal static ServiceResult ValidateUserAccessLevel(
            ISystemContext context,
            BaseVariableState variable,
            byte requiredAccessLevel)
        {
            byte userAccessLevel = variable.UserAccessLevel;
            if (variable.OnReadUserAccessLevel is { } onReadUserAccessLevel)
            {
                ServiceResult result = onReadUserAccessLevel(context, variable, ref userAccessLevel);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            return (userAccessLevel & requiredAccessLevel) == 0
                ? StatusCodes.BadUserAccessDenied
                : ServiceResult.Good;
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
        /// Returns whether the provider historizes the node. A variable that the provider
        /// does not historize has no history to read, so the read operations report
        /// Bad_HistoryOperationUnsupported (Part 4 §5.11.3.4) instead of an empty result.
        /// </summary>
        private static ValueTask<bool> IsHistorizedAsync(
            IHistorianProvider provider,
            NodeState node,
            CancellationToken cancellationToken)
        {
            return provider.IsHistorizingAsync(node.NodeId, cancellationToken);
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
            Exception? operationFailure = null;
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

                if (claim is not null)
                {
                    provider = claim.State.Provider;
                    node = claim.State.SourceNode ?? node;
                    timestampsToReturn = claim.State.TimestampsToReturn;
                    nodeToRead = RestoreReadParameters(claim.State, nodeToRead);
                }
                if (claim == null &&
                    !await IsHistorizedAsync(provider, node, cancellationToken).ConfigureAwait(false))
                {
                    return StatusCodes.BadHistoryOperationUnsupported;
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
            catch (Exception failure)
            {
                operationFailure = failure;
                throw;
            }
            finally
            {
                await DisposeContinuationClaimAsync(claim, operationFailure).ConfigureAwait(false);
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
                details.PerformInsertReplace switch
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

            // Part 11 6.9.5.1: both times shall be defined and startTime shall
            // not be greater than endTime (startTime == endTime deletes the
            // value at startTime).
            if (details.StartTime == DateTimeUtc.MinValue ||
                details.EndTime == DateTimeUtc.MinValue ||
                details.StartTime > details.EndTime)
            {
                HistorianUpdateOutcome<DataValue> failure =
                    CreateFailureOutcome<DataValue>(
                        StatusCodes.BadInvalidTimestampArgument,
                        1);
                result.StatusCode = StatusCodes.BadInvalidTimestampArgument;
                ReportAuditDeleteRaw(
                    systemContext,
                    details,
                    failure,
                    StatusCodes.BadInvalidTimestampArgument);
                return StatusCodes.BadInvalidTimestampArgument;
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

            // startTime == endTime deletes the value at startTime: widen the range to
            // one tick so every provider keeps the plain half-open [start, end) contract.
            DateTimeUtc deleteEndTime = details.StartTime == details.EndTime
                ? details.StartTime + TimeSpan.FromTicks(1)
                : details.EndTime;
            HistorianUpdateOutcome<DataValue> outcome = await data.DeleteRawAsync(
                opContext,
                node.NodeId,
                details.StartTime,
                deleteEndTime,
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
#pragma warning disable CA2000 // State transfers to the session continuation or is disposed inline.
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
            // A positive interval shorter than one DateTime tick cannot advance
            // the slices; it would only spin the calculator up to the output
            // cap. Reject it as the AnnotationCount path does.
            if (!hasContinuationPoint &&
                details.ProcessingInterval > 0 &&
                details.ProcessingInterval * TimeSpan.TicksPerMillisecond < 1)
            {
                result.StatusCode = StatusCodes.BadAggregateInvalidInputs;
                return StatusCodes.BadAggregateInvalidInputs;
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

            Exception? operationFailure = null;
            try
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
            catch (Exception failure)
            {
                operationFailure = failure;
                throw;
            }
            finally
            {
                await DisposeContinuationClaimAsync(claim, operationFailure).ConfigureAwait(false);
            }
        }
#pragma warning restore CA2000

        /// <summary>
        /// Validates aggregate inputs and pages processed history through the provider or a local aggregate calculator.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
            if (cont is not null)
            {
                provider = cont.Provider;
                node = cont.SourceNode ?? node;
                timestampsToReturn = cont.TimestampsToReturn;
                nodeToRead = RestoreReadParameters(cont, nodeToRead);
            }
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
            if (claim == null &&
                !await IsHistorizedAsync(provider, node, cancellationToken).ConfigureAwait(false))
            {
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
                config = CoreUtils.Clone(capabilities.DefaultAggregateConfiguration) ??
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "The historical node has no default aggregate configuration.");
                config.UseServerCapabilitiesDefaults = false;
            }

            // Validate both explicit inputs and the node's advertised defaults.
            if (config.PercentDataGood > 100 ||
                config.PercentDataBad > 100 ||
                config.PercentDataGood < 100 - config.PercentDataBad)
            {
                claim?.Retire();
                result.StatusCode = StatusCodes.BadAggregateInvalidInputs;
                return StatusCodes.BadAggregateInvalidInputs;
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
                            OriginNodeId = nodeToRead.NodeId,
                            SourceNode = node,
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
                capabilities.Stepped,
                config);

            if (calculator == null)
            {
                return StatusCodes.BadAggregateNotSupported;
            }

            if (provider is not IHistorianDataProvider raw)
            {
                return StatusCodes.BadHistoryOperationUnsupported;
            }

            // the raw values of one processing interval are held until the interval is
            // complete, so bound how many one operation may hold.
            int maxHeldRawValues = serverInternal.AggregateManager.MaxProcessedHistoryRawValues;
            var standardCalculator = calculator as AggregateCalculator;
            bool HoldsTooManyRawValues()
            {
                return maxHeldRawValues > 0 &&
                    standardCalculator != null &&
                    standardCalculator.RawValueCount > maxHeldRawValues;
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

            // The raw read returns only the raw value at or beyond each end of the range,
            // which can be Bad (or Uncertain with TreatUncertainAsBad). Interpolated bounds
            // use the nearest Good values outside the range (Part 13 §3.1.8), so when such
            // an end value is not Good the calculator also gets the raw values up to the
            // two nearest Good values before the range or the nearest one after it.
            bool treatUncertainAsBad = config.TreatUncertainAsBad;
            bool IsGoodBound(DataValue value)
            {
                return treatUncertainAsBad
                    ? StatusCode.IsGood(value.StatusCode)
                    : StatusCode.IsNotBad(value.StatusCode);
            }
            bool NeedsMoreBounds(DataValue endValue, bool before)
            {
                // the provider reports a missing bound with Bad_BoundNotFound.
                if (endValue.StatusCode == StatusCodes.BadBoundNotFound || IsGoodBound(endValue))
                {
                    return false;
                }
                return before
                    ? endValue.SourceTimestamp <= rawRequest.StartTime
                    : endValue.SourceTimestamp >= rawRequest.EndTime;
            }

            // Returns the raw values beyond the end value that was read (null when the
            // search limit was reached), in the order the request queues them.
            async ValueTask<List<DataValue>?> ReadMoreBoundsAsync(bool before, DataValue endValue)
            {
                var extra = new List<DataValue>();
                int found = await AddBoundingValuesAsync(
                    extra,
                    opContext,
                    raw,
                    node.NodeId,
                    before ? rawRequest.StartTime : rawRequest.EndTime,
                    before,
                    requiredNonBad: before ? 2 : 1,
                    cancellationToken,
                    IsGoodBound).ConfigureAwait(false);
                if (found < 0)
                {
                    return null;
                }

                // the end value and the entries at its timestamp came with the range read.
                DateTimeUtc endTime = endValue.SourceTimestamp;
                extra.RemoveAll(value => before
                    ? value.SourceTimestamp >= endTime
                    : value.SourceTimestamp <= endTime);
                extra.Sort((a, b) => rawRequest.IsForward
                    ? a.SourceTimestamp.CompareTo(b.SourceTimestamp)
                    : b.SourceTimestamp.CompareTo(a.SourceTimestamp));
                return extra;
            }

            // queues a batch of bounding values and produces the outputs once.
            bool QueueBounds(List<DataValue> batch)
            {
                foreach (DataValue value in batch)
                {
                    calculator.QueueRawValue(value);
                }
                if (HoldsTooManyRawValues())
                {
                    return false;
                }
                return FlushCalculator(
                    calculator,
                    values,
                    partial: false,
                    kMaxProcessedBufferedOutputs);
            }

            // the first value read is the end value on the leading side of the request,
            // the last value read the one on the trailing side.
            bool leadingBefore = rawRequest.IsForward;
            bool first = true;
            DataValue lastValue = default;
            HistorianResumeToken token2 = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    opContext, rawRequest, token2, cancellationToken).ConfigureAwait(false);

                if (first && page.Values.Count > 0)
                {
                    first = false;
                    DataValue leadingValue = page.Values[0].Value;
                    if (NeedsMoreBounds(leadingValue, leadingBefore))
                    {
                        List<DataValue>? leading = await ReadMoreBoundsAsync(
                            leadingBefore,
                            leadingValue).ConfigureAwait(false);
                        if (leading == null || !QueueBounds(leading))
                        {
                            return StatusCodes.BadTooManyOperations;
                        }
                    }
                }

                foreach (HistoricalDataValue sample in page.Values)
                {
                    calculator.QueueRawValue(sample.Value);
                    if (!FlushCalculator(
                            calculator,
                            values,
                            partial: false,
                            kMaxProcessedBufferedOutputs) ||
                        HoldsTooManyRawValues())
                    {
                        return StatusCodes.BadTooManyOperations;
                    }
                    lastValue = sample.Value;
                }

                if (page.IsFinal)
                {
                    break;
                }
                token2 = page.NextToken;
            }

            if (!first && NeedsMoreBounds(lastValue, !leadingBefore))
            {
                List<DataValue>? trailing = await ReadMoreBoundsAsync(
                    !leadingBefore,
                    lastValue).ConfigureAwait(false);
                if (trailing == null || !QueueBounds(trailing))
                {
                    return StatusCodes.BadTooManyOperations;
                }
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
                    OriginNodeId = nodeToRead.NodeId,
                    SourceNode = node,
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
#pragma warning disable CA2000 // State transfers to the session continuation or is disposed inline.
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
                HistorianPage<HistorianAnnotation> timestampedPage;
                if (annotationProvider is IHistorianTimestampedAnnotationProvider timestamped)
                {
                    timestampedPage = await timestamped.ReadAnnotationsWithTimestampsAsync(
                        opContext, request, token, cancellationToken).ConfigureAwait(false);
                    foreach (HistorianAnnotation annotation in timestampedPage.Values)
                    {
                        annotationTimes.Add(annotation.SourceTimestamp);
                    }
                    if (timestampedPage.IsFinal)
                    {
                        break;
                    }
                    token = timestampedPage.NextToken;
                    continue;
                }
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

            // Part 13 §5.4.3.20: intervals before the start or after the end of data are
            // Bad_NoData and intervals overlapping either edge are Partial. The data is the
            // raw history (read with bounds, as for the other aggregates) and the annotations.
            DateTimeUtc startOfData = DateTimeUtc.MaxValue;
            DateTimeUtc endOfData = DateTimeUtc.MinValue;
            foreach (DateTimeUtc annotationTime in annotationTimes)
            {
                startOfData = annotationTime < startOfData ? annotationTime : startOfData;
                endOfData = annotationTime > endOfData ? annotationTime : endOfData;
            }

            // Annotations outside the window are data too: the nearest one before and after it.
            DateTimeUtc? annotationBefore = await ReadEdgeAnnotationTimestampAsync(
                annotationProvider, opContext, node.NodeId, DateTimeUtc.MinValue, windowStart, false,
                cancellationToken).ConfigureAwait(false);
            DateTimeUtc? annotationAfter = await ReadEdgeAnnotationTimestampAsync(
                annotationProvider, opContext, node.NodeId, windowEnd, DateTimeUtc.MaxValue, true,
                cancellationToken).ConfigureAwait(false);
            if (annotationBefore is DateTimeUtc before && before < startOfData)
            {
                startOfData = before;
            }
            if (annotationAfter is DateTimeUtc after && after > endOfData)
            {
                endOfData = after;
            }
            if (provider is IHistorianDataProvider raw)
            {
                // Only the edges matter: the first raw value of a forward read with bounds is
                // the earliest one, the first of a reverse read the latest one.
                DateTimeUtc? firstRaw = await ReadEdgeRawTimestampAsync(
                    raw, opContext, node.NodeId, windowStart, windowEnd, true, cancellationToken)
                    .ConfigureAwait(false);
                DateTimeUtc? lastRaw = await ReadEdgeRawTimestampAsync(
                    raw, opContext, node.NodeId, windowStart, windowEnd, false, cancellationToken)
                    .ConfigureAwait(false);
                if (firstRaw is DateTimeUtc first && first < startOfData)
                {
                    startOfData = first;
                }
                if (lastRaw is DateTimeUtc last && last > endOfData)
                {
                    endOfData = last;
                }
            }

            ArrayOf<DataValue> outputs;
            try
            {
                outputs = CountAggregateCalculator.CalculateAnnotationCounts(
                    annotationTimes.ToArrayOf(),
                    startTime,
                    endTime,
                    details.ProcessingInterval,
                    startOfData,
                    endOfData,
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
                OriginNodeId = nodeToRead.NodeId,
                SourceNode = node,
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
#pragma warning restore CA2000

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
        /// Maximum number of values one at-time HistoryRead may ask for: requested times per
        /// node, and requested times times nodes per request (see
        /// <see cref="StandardServer.HistoryReadAsync"/>). Every requested time is answered
        /// with a value whatever the archive holds, so without a cap a request of a few hundred
        /// kilobytes makes the server build millions of values. More is rejected with
        /// <see cref="StatusCodes.BadTooManyOperations"/>.
        /// </summary>
        internal const int kMaxAtTimeRequestedValues = kMaxProcessedBufferedOutputs;

        /// <summary>
        /// Raw values the at-time fallback may read per requested time, on top of
        /// <see cref="kMaxProcessedBufferedOutputs"/> for the whole operation: enough to find
        /// the bounds of every requested time in an archive of mostly Good values. A provider
        /// that returns far more than asked for (or an archive of Bad values that hides the
        /// bounds) no longer scans its archive once per requested time: once the budget is
        /// spent the remaining requested times get <see cref="StatusCodes.BadBoundNotSupported"/>,
        /// as when the search limit of one requested time is reached (Part 11 Table 25).
        /// </summary>
        internal const int kAtTimeScanValuesPerRequestedTime = 64;

        /// <summary>
        /// The raw values one framework at-time read may still scan.
        /// </summary>
        private sealed class AtTimeScanBudget
        {
            public AtTimeScanBudget(long values)
            {
                m_remaining = values;
            }

            /// <summary>
            /// Whether more raw values were scanned than the budget allows.
            /// </summary>
            public bool Exhausted => m_remaining < 0;

            /// <summary>
            /// Charges a page of scanned values.
            /// </summary>
            /// <exception cref="ServiceResultException">The budget is exhausted.</exception>
            public void Charge(int count)
            {
                m_remaining -= count;
                if (m_remaining < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBoundNotSupported,
                        "The at-time read scanned more raw values than its requested times allow.");
                }
            }

            private long m_remaining;
        }

        /// <summary>
        /// Number of raw values the at-time fallback first reads on each side of a
        /// requested time; the read widens only while Bad values hide the bound.
        /// </summary>
        private const int kInitialBoundingReadSize = 16;

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
            if (!await IsHistorizedAsync(provider, node, cancellationToken).ConfigureAwait(false))
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
            if (reqTimes.Count > kMaxAtTimeRequestedValues)
            {
                result.StatusCode = StatusCodes.BadTooManyOperations;
                result.ContinuationPoint = ByteString.Empty;
                return StatusCodes.BadTooManyOperations;
            }

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

            var produced = new List<DataValue>(reqTimes.Count);
            var budget = new AtTimeScanBudget(
                kMaxProcessedBufferedOutputs + ((long)kAtTimeScanValuesPerRequestedTime * reqTimes.Count));
            for (int i = 0; i < reqTimes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTimeUtc requestedTime = reqTimes[i];
                List<DataValue>? samples = null;
                if (!budget.Exhausted)
                {
                    try
                    {
                        samples = await CollectAtTimeSamplesAsync(
                            opContext,
                            raw,
                            node.NodeId,
                            requestedTime,
                            details.UseSimpleBounds,
                            capabilities.Stepped,
                            budget,
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException) when (budget.Exhausted)
                    {
                        samples = null;
                    }
                }
                if (samples == null)
                {
                    // Part 11 4.6 / 6.2.2 Table 25: the search limit was reached before a
                    // bound was found, so this requested time gets Bad_BoundNotSupported
                    // while the other requested times are still answered. Once the scan
                    // budget of the operation is spent, every remaining requested time is
                    // answered so without reading the provider again.
                    produced.Add(new DataValue(
                        Variant.Null,
                        StatusCodes.BadBoundNotSupported,
                        requestedTime,
                        DateTimeUtc.MinValue));
                    continue;
                }
                produced.Add(AggregateCalculator.CalculateAtTime(
                    samples.ToArrayOf(),
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
#pragma warning disable CA2000 // Continuation state transfers to the session or is disposed inline.
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
            Exception? operationFailure = null;
            try
            {
                if (claim == null && !nodeToRead.ContinuationPoint.IsEmpty)
                {
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }
                if (claim is not null)
                {
                    provider = claim.State.Provider;
                    parentVariable = claim.State.SourceNode as BaseVariableState ?? parentVariable;
                    timestampsToReturn = claim.State.TimestampsToReturn;
                    nodeToRead = RestoreReadParameters(claim.State, nodeToRead);
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
                        MaxValues = annotations is IHistorianTimestampedAnnotationProvider
                            ? details.NumValuesPerNode
                            : ApplyHistorianLimit(details.NumValuesPerNode, capabilities.MaxReturnDataValues),
                        PageLimit = capabilities.MaxReturnDataValues,
                        IsForward = isForward
                    };
                    resumeToken = default;
                }

                HistorianOperationContext opContext = new(
                    systemContext,
                    systemContext.OperationContext!,
                    parentVariable,
                    HistoryUpdateType.Insert);

                if (annotations is IHistorianTimestampedAnnotationProvider timestamped)
                {
                    HistorianPage<HistorianAnnotation> timestampedPage =
                        await timestamped.ReadAnnotationsWithTimestampsAsync(
                            opContext,
                            request,
                            resumeToken,
                            cancellationToken).ConfigureAwait(false);
                    var timestampedDataValues = new List<DataValue>(timestampedPage.Values.Count);
                    foreach (HistorianAnnotation a in timestampedPage.Values)
                    {
                        timestampedDataValues.Add(new DataValue(
                            new Variant(new ExtensionObject(a.Annotation)),
                            StatusCodes.Good,
                            sourceTimestamp: a.SourceTimestamp,
                            serverTimestamp: DateTimeUtc.MinValue));
                    }
                    FillHistoryData(systemContext, result, timestampedDataValues, nodeToRead, timestampsToReturn);
                    HistorianContinuationState? timestampedInitialState = null;
                    if (claim == null && !timestampedPage.NextToken.IsEmpty)
                    {
                        timestampedInitialState = new HistorianContinuationState
                        {
                            Id = Guid.NewGuid(),
                            Provider = provider,
                            NodeId = nodeToRead.NodeId,
                            OriginNodeId = nodeToRead.NodeId,
                            SourceNode = parentVariable,
                            Kind = HistorianReadKind.Annotations,
                            ResumeToken = timestampedPage.NextToken,
                            AnnotationRequest = request,
                            TimestampsToReturn = timestampsToReturn,
                            IndexRange = nodeToRead.ParsedIndexRange,
                            DataEncoding = nodeToRead.DataEncoding
                        };
                    }
                    await SaveSuccessorOrCompleteAsync(
                        systemContext, result, claim, !timestampedPage.NextToken.IsEmpty, timestampedPage.NextToken,
                        timestampedInitialState, null, cancellationToken).ConfigureAwait(false);
                    return ServiceResult.Good;
                }
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
                        OriginNodeId = nodeToRead.NodeId,
                        SourceNode = parentVariable,
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
            catch (Exception failure)
            {
                operationFailure = failure;
                throw;
            }
            finally
            {
                await DisposeContinuationClaimAsync(claim, operationFailure).ConfigureAwait(false);
            }
        }
#pragma warning restore CA2000

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
            var timestampedList = new List<HistorianAnnotation>(updateValues.Count);
            for (int i = 0; i < updateValues.Count; i++)
            {
                DataValue dv = updateValues[i];
                if (dv.IsNull)
                {
                    annotationList.Add(null!);
                    times.Add(DateTimeUtc.MinValue);
                    timestampedList.Add(default);
                    continue;
                }

                Annotation? annotation = DecodeAnnotation(dv);
                annotationList.Add(annotation!);
                times.Add(annotation != null ? annotation.AnnotationTime : dv.SourceTimestamp);
                timestampedList.Add(new HistorianAnnotation(dv.SourceTimestamp, annotation!));
            }

            if (annotations is IHistorianTimestampedAnnotationProvider timestampedProvider)
            {
                HistorianUpdateOutcome<HistorianAnnotation> timestampedOutcome =
                    details.PerformInsertReplace switch
                    {
                        PerformUpdateType.Insert => await timestampedProvider.InsertAnnotationsWithTimestampsAsync(
                            opContext,
                            parentVariable.NodeId,
                            timestampedList.ToArrayOf(),
                            cancellationToken).ConfigureAwait(false),
                        PerformUpdateType.Replace => await timestampedProvider.ReplaceAnnotationsWithTimestampsAsync(
                            opContext,
                            parentVariable.NodeId,
                            timestampedList.ToArrayOf(),
                            cancellationToken).ConfigureAwait(false),
                        PerformUpdateType.Update => await timestampedProvider.UpdateAnnotationsWithTimestampsAsync(
                            opContext,
                            parentVariable.NodeId,
                            timestampedList.ToArrayOf(),
                            cancellationToken).ConfigureAwait(false),
                        PerformUpdateType.Remove => await timestampedProvider.DeleteAnnotationsWithTimestampsAsync(
                            opContext,
                            parentVariable.NodeId,
                            timestampedList.ToArrayOf(),
                            cancellationToken).ConfigureAwait(false),
                        _ => new HistorianUpdateOutcome<HistorianAnnotation>(
                            RepeatStatus(StatusCodes.BadInvalidArgument, annotationList.Count).ToArrayOf())
                    };
                if (timestampedOutcome.OperationResults.Count != updateValues.Count)
                {
                    timestampedOutcome = CreateFailureOutcome<HistorianAnnotation>(
                        StatusCodes.BadUnexpectedError, updateValues.Count);
                }
                var timestampedOldValues = new Annotation[timestampedOutcome.OldValues.Count];
                for (int i = 0; i < timestampedOldValues.Length; i++)
                {
                    timestampedOldValues[i] = timestampedOutcome.OldValues[i].Annotation;
                }
                var adaptedOutcome = new HistorianUpdateOutcome<Annotation>(
                    timestampedOutcome.OperationResults,
                    timestampedOldValues.ToArrayOf(),
                    timestampedOutcome.DiagnosticInfos,
                    timestampedOutcome.TransactionRolledBack);
                result.OperationResults = adaptedOutcome.OperationResults;
                result.DiagnosticInfos = adaptedOutcome.DiagnosticInfos;
                ServiceResult adaptedResult = GetOperationResult(adaptedOutcome);
                ReportAuditAnnotationUpdate(
                    systemContext,
                    details,
                    parentVariable,
                    adaptedOutcome,
                    adaptedResult.StatusCode);
                return adaptedResult;
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
#pragma warning disable CA2000 // Continuation state transfers to the session or is disposed inline.
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
            Exception? operationFailure = null;
            try
            {
                if (claim == null && !nodeToRead.ContinuationPoint.IsEmpty)
                {
                    result.StatusCode = StatusCodes.BadContinuationPointInvalid;
                    result.ContinuationPoint = ByteString.Empty;
                    return ServiceResult.Good;
                }
                if (claim is not null)
                {
                    provider = claim.State.Provider;
                    node = claim.State.SourceNode ?? node;
                    nodeToRead = RestoreReadParameters(claim.State, nodeToRead);
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
                        // shared read-only by every operation of the request and their
                        // continuation points rather than copied for each of them.
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

                // Part 3 8.55: a client only receives an event, live or historical, when
                // ReceiveEvents is granted on its EventType and SourceNode. Every page,
                // including continuation pages, is authorized for the current request
                // before any client filter sees a record.
                ArrayOf<HistorianEventRecord> authorized = await AuthorizeHistoricalEventsAsync(
                    systemContext,
                    node.NodeId,
                    page.Values,
                    cancellationToken).ConfigureAwait(false);

                EventFilter filter = request.Filter;
                ArrayOf<HistorianEventRecord> filtered = authorized;
                if (filter.WhereClause.Elements.Count > 0)
                {
                    var keep = new List<HistorianEventRecord>(authorized.Count);
                    foreach (HistorianEventRecord record in authorized)
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
                        OriginNodeId = nodeToRead.NodeId,
                        SourceNode = node,
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
                if (filtered.Count == 0 && page.NextToken.IsEmpty)
                {
                    result.StatusCode = StatusCodes.GoodNoData;
                }
                return ServiceResult.Good;
            }
            catch (Exception failure)
            {
                operationFailure = failure;
                throw;
            }
            finally
            {
                await DisposeContinuationClaimAsync(claim, operationFailure).ConfigureAwait(false);
            }
        }
#pragma warning restore CA2000

        /// <summary>
        /// Keeps the stored events whose EventType and SourceNode grant
        /// <see cref="PermissionType.ReceiveEvents"/> to the caller of the current request,
        /// in their original order. A missing or malformed stored identity, an unknown node
        /// or a failed permission lookup excludes the event. Only a trusted in-process read
        /// without an operation context sees every stored event.
        /// </summary>
        internal static async ValueTask<ArrayOf<HistorianEventRecord>> AuthorizeHistoricalEventsAsync(
            ServerSystemContext systemContext,
            NodeId notifierId,
            ArrayOf<HistorianEventRecord> records,
            CancellationToken cancellationToken)
        {
            OperationContext? operationContext = systemContext.OperationContext;
            if (operationContext == null || records.Count == 0)
            {
                return records;
            }

            var verdicts = new Dictionary<NodeId, bool>();
            var allowed = new List<HistorianEventRecord>(records.Count);
            for (int index = 0; index < records.Count; index++)
            {
                HistorianEventRecord record = records[index];
                if (record == null ||
                    !TryGetStoredEventIdentity(record, notifierId, out NodeId eventType, out NodeId sourceNode))
                {
                    continue;
                }

                if (await IsReceiveEventsGrantedAsync(
                        systemContext,
                        operationContext,
                        eventType,
                        verdicts,
                        cancellationToken).ConfigureAwait(false) &&
                    await IsReceiveEventsGrantedAsync(
                        systemContext,
                        operationContext,
                        sourceNode,
                        verdicts,
                        cancellationToken).ConfigureAwait(false))
                {
                    allowed.Add(record);
                }
            }

            return allowed.Count == records.Count ? records : allowed.ToArrayOf();
        }

        /// <summary>
        /// Reads the server-stored identity of a historical event. A record without a stored
        /// SourceNode was captured or inserted for the notifier itself; a stored SourceNode that
        /// is not a non-null NodeId, or a null EventType, is malformed.
        /// </summary>
        private static bool TryGetStoredEventIdentity(
            HistorianEventRecord record,
            NodeId notifierId,
            out NodeId eventType,
            out NodeId sourceNode)
        {
            eventType = record.EventType;
            sourceNode = default;
            if (eventType.IsNull)
            {
                return false;
            }

            var sourceKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(BrowseNames.SourceNode)],
                Attributes.Value,
                null);
            if (!record.TryGetQualifiedField(sourceKey, out Variant stored) &&
                !record.TryGetField(BrowseNames.SourceNode, out stored))
            {
                sourceNode = notifierId;
                return !sourceNode.IsNull;
            }

            return stored.TryGetValue(out sourceNode) && !sourceNode.IsNull;
        }

        /// <summary>
        /// Validates ReceiveEvents (role permissions and access restrictions, with the
        /// namespace defaults of the owning node manager) on a node for the current request.
        /// </summary>
        private static async ValueTask<bool> IsReceiveEventsGrantedAsync(
            ServerSystemContext systemContext,
            OperationContext operationContext,
            NodeId nodeId,
            Dictionary<NodeId, bool> verdicts,
            CancellationToken cancellationToken)
        {
            if (verdicts.TryGetValue(nodeId, out bool granted))
            {
                return granted;
            }

            granted = false;
            try
            {
                bool? verdict = await TryGetReceiveEventsVerdictAsync(
                    systemContext,
                    operationContext,
                    nodeId,
                    cancellationToken).ConfigureAwait(false);
                if (verdict == null)
                {
                    // A resumed page routes only to the owners its continuation retained. The EventType
                    // and SourceNode of a stored event can belong to other owners, so a node the
                    // retained routing cannot resolve is checked against the live routing instead.
                    verdict = await TryGetReceiveEventsVerdictAsync(
                        systemContext,
                        operationContext,
                        nodeId,
                        cancellationToken,
                        useLiveRouting: true).ConfigureAwait(false);
                }
                granted = verdict == true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                granted = false;
            }

            verdicts[nodeId] = granted;
            return granted;
        }

        /// <summary>
        /// Validates ReceiveEvents on a node through request routing or, when requested, current routing.
        /// </summary>
        /// <returns>The verdict, or null if the selected routing has no owner for the node.</returns>
        private static async ValueTask<bool?> TryGetReceiveEventsVerdictAsync(
            ServerSystemContext systemContext,
            OperationContext operationContext,
            NodeId nodeId,
            CancellationToken cancellationToken,
            bool useLiveRouting = false)
        {
            IMasterNodeManager master = systemContext.Server.NodeManager;
            (object? handle, IAsyncNodeManager? nodeManager) = useLiveRouting
                ? await master.GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false)
                : await master.ResolveManagerHandleAsync(operationContext, nodeId, cancellationToken)
                    .ConfigureAwait(false);
            if (handle == null || nodeManager == null)
            {
                return null;
            }

            NodeMetadata? metadata = await nodeManager.GetNodeMetadataAsync(
                operationContext,
                handle,
                BrowseResultMask.All,
                cancellationToken).ConfigureAwait(false);
            return metadata != null &&
                !ServiceResult.IsBad(MasterNodeManager.ValidateRolePermissions(
                    systemContext.Server,
                    operationContext,
                    metadata,
                    PermissionType.ReceiveEvents)) &&
                !ServiceResult.IsBad(MasterNodeManager.ValidateAccessRestrictions(
                    operationContext,
                    metadata,
                    PermissionType.ReceiveEvents));
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

            ISessionContinuationPoints? points = systemContext.OperationContext?.Session?.ContinuationPoints;
            IHistoryContinuationPoint? restored = points?.RestoreHistory(nodeToRead.ContinuationPoint);
            if (restored != null)
            {
                bool matches = restored is HistorianContinuationState state &&
                    (state.OriginNodeId.IsNull ? state.NodeId : state.OriginNodeId) == nodeToRead.NodeId;
                restored.Dispose();
                return matches ? ServiceResult.Good : StatusCodes.BadContinuationPointInvalid;
            }
            return points?.ReleaseHistory(nodeToRead.ContinuationPoint) == true
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
            if (state is HistorianContinuationState history &&
                (history.OriginNodeId.IsNull ? history.NodeId : history.OriginNodeId) == nodeToRead.NodeId)
            {
                state.Dispose();
                return ServiceResult.Good;
            }
            state?.Dispose();
            return StatusCodes.BadContinuationPointInvalid;
        }

        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
#pragma warning disable CA2000 // Continuation state transfers to the session or is disposed inline.
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
                    MaxValues = details.NumValuesPerNode,
                    PageLimit = capabilities.MaxReturnDataValues,
                    IsForward = isForward,
                    ReturnBounds = details.ReturnBounds
                };
                token = default;
            }

            // the values kept for the response are charged against the response byte
            // budget of the service: a page holds at most the values that fit it.
            OperationContext? operationContext = systemContext.OperationContext;
            long responseBytes = operationContext?.ResponseBytesRemaining ?? -1;
            HistorianRawReadRequest pageRequest = responseBytes < 0
                ? request
                : LimitPageByResponseBudget(request, request.PageLimit, responseBytes);

            HistorianPage<HistoricalDataValue> page = await data.ReadRawAsync(
                opContext, pageRequest, token, cancellationToken).ConfigureAwait(false);

            List<DataValue> filled = FillRawPage(systemContext, result, page, nodeToRead, timestampsToReturn);

            bool resumeAtStart = false;
            if (responseBytes >= 0)
            {
                // the values are measured with the binary encoder: a page that is sent must
                // fit, a lower bound of its size does not show that.
                using var meter = new EncodedSizeMeter(systemContext);
                bool fits = false;
                bool someFit = false;
                for (int reads = 1; ; reads++)
                {
                    // every page read is checked, the last re-read as well. The last page
                    // of the read is sent without a continuation point, so it may use the
                    // continuation point bytes the budget reserves for its result.
                    // Not when the reserved framing alone exceeds the limit: then the
                    // reserve covers part of the deficit.
                    long reserve = page.NextToken.IsEmpty && !operationContext!.ResponseBudgetExhausted
                        ? ResponseSizeEstimator.HistoryContinuationPointSize
                        : 0;
                    (int count, long bytes) = meter.CountValuesThatFit(
                        filled,
                        operationContext!.ResponseBytesRemaining + reserve);
                    someFit |= count > 0;
                    if (count == filled.Count)
                    {
                        // the continuation point bytes the values do not use stay in the
                        // budget for the other results of the request.
                        if (bytes >= reserve)
                        {
                            fits = operationContext.TryChargeResponse(bytes - reserve);
                        }
                        else
                        {
                            operationContext.CreditResponse(reserve - bytes);
                            fits = true;
                        }
                        break;
                    }
                    if (count == 0 || reads > kMaxResponseBudgetPageRereads)
                    {
                        break;
                    }

                    // read the page again with the values that fit, so the provider hands
                    // back the position after the last of them.
                    page = await data.ReadRawAsync(
                        opContext,
                        LimitPageByResponseBudget(request, (uint)count, long.MaxValue),
                        token,
                        cancellationToken).ConfigureAwait(false);
                    filled = FillRawPage(systemContext, result, page, nodeToRead, timestampsToReturn);
                }

                if (!fits)
                {
                    // no page that fits was read. When nothing else was charged, no later
                    // request can hold this page either.
                    if (operationContext!.ResponseBudgetUnused)
                    {
                        operationContext.TryChargeResponse(long.MaxValue);
                        throw new ServiceResultException(
                            StatusCodes.BadResponseTooLarge,
                            someFit
                                ? "The history provider returned more values than the response size limit holds."
                                : "A value of the HistoryRead exceeds the response size limit.");
                    }
                    // Part 11 6.3: fewer values than requested, here none, with a
                    // continuation point that resumes where this page started.
                    page = new HistorianPage<HistoricalDataValue>([], token);
                    filled = FillRawPage(systemContext, result, page, nodeToRead, timestampsToReturn);
                    resumeAtStart = true;
                }
            }

            List<DataValue> values = filled;
            bool hasMore = resumeAtStart || !page.NextToken.IsEmpty;

            HistorianContinuationState? initialState = null;
            if (claim == null && hasMore)
            {
                initialState = new HistorianContinuationState
                {
                    Id = Guid.NewGuid(),
                    Provider = provider,
                    NodeId = node.NodeId,
                    OriginNodeId = nodeToRead.NodeId,
                    SourceNode = node,
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
                hasMore,
                page.NextToken,
                initialState,
                bufferedProcessedOffset: null,
                cancellationToken).ConfigureAwait(false);

            // Per OPC UA Part 11 6.5.3.2, an interval in which no data exists (and no
            // Bounding Values were requested/returned) is reported with Good_NoData.
            if (values.Count == 0 && !hasMore)
            {
                result.StatusCode = StatusCodes.GoodNoData;
            }

            return ServiceResult.Good;
        }
#pragma warning restore CA2000

        /// <summary>
        /// The smallest encoded size of a raw history value that a page is limited by: its
        /// encoding mask and one timestamp, which a raw value is returned with. The limit
        /// only shortens a page, the values of the page are charged with their own size.
        /// </summary>
        private const int kMinHistoryDataValueSize = 1 + 8;

        /// <summary>
        /// How often a raw page is read again with fewer values before it is returned
        /// without values: a provider that does not honor the page limit, or whose values
        /// grow between the reads, does not hold the read in a loop.
        /// </summary>
        private const int kMaxResponseBudgetPageRereads = 3;

        /// <summary>
        /// Limits the values of a raw page to <paramref name="pageLimit"/> and to the
        /// values the rest of the response byte budget can hold at the smallest size.
        /// </summary>
        private static HistorianRawReadRequest LimitPageByResponseBudget(
            HistorianRawReadRequest request,
            uint pageLimit,
            long responseBytes)
        {
            uint limit = pageLimit == 0 ? uint.MaxValue : pageLimit;
            long byBudget = Math.Max(1, responseBytes / kMinHistoryDataValueSize);
            if (byBudget < limit)
            {
                limit = (uint)byBudget;
            }
            return limit == request.PageLimit ? request : request with { PageLimit = limit };
        }

        /// <summary>
        /// Measures the binary encoded size of history values, without storing the bytes.
        /// </summary>
        /// <remarks>
        /// The length limits of the server's message context do not apply: the measurement
        /// only sizes the values, the encoder of the response still enforces them. The
        /// lengths and the written bytes are limited by the rest of the budget instead. The
        /// nesting limit is the server's, the one the response is encoded with, which also
        /// bounds the recursion of the measurement. A value that fails to encode does not
        /// fit.
        /// </remarks>
        private sealed class EncodedSizeMeter : IDisposable
        {
            public EncodedSizeMeter(ServerSystemContext context)
            {
                m_context = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris,
                    MaxStringLength = 0,
                    MaxByteStringLength = 0,
                    MaxArrayLength = 0,
                    MaxMessageSize = 0,
                    MaxEncodingNestingLevels = context.Server.MessageContext?.MaxEncodingNestingLevels ??
                        DefaultEncodingLimits.MaxEncodingNestingLevels
                };
            }

            /// <summary>
            /// Counts the leading values whose encoded size fits
            /// <paramref name="responseBytes"/> and returns their size.
            /// </summary>
            public (int Count, long Bytes) CountValuesThatFit(List<DataValue> values, long responseBytes)
            {
                long bytes = 0;
                for (int ii = 0; ii < values.Count; ii++)
                {
                    // a value whose lower bound does not fit is not encoded: encoding a
                    // large string buffers all of its UTF-8 bytes.
                    if (ResponseSizeEstimator.Estimate(values[ii]) > responseBytes - bytes)
                    {
                        return (ii, bytes);
                    }
                    long size = Measure(values[ii], responseBytes - bytes);
                    if (size > responseBytes - bytes)
                    {
                        return (ii, bytes);
                    }
                    bytes += size;
                }
                return (values.Count, bytes);
            }

            public void Dispose()
            {
                DisposeEncoder();
                m_stream.Dispose();
            }

            private void DisposeEncoder()
            {
                m_encoder?.Dispose();
                m_encoder = null;
            }

            /// <summary>
            /// Measures a value that is encoded only as far as <paramref name="limit"/>
            /// bytes: a string, byte string or array that alone is longer (also nested in
            /// variants), or any write past the limit, stops the encoding, so a value that
            /// does not fit is not buffered or encoded in full.
            /// </summary>
            private long Measure(in DataValue value, long limit)
            {
                // each element of an array and each byte of a string is at least one byte.
                int lengthLimit = (int)Math.Min(Math.Max(limit, 1), int.MaxValue);
                m_context.MaxStringLength = lengthLimit;
                m_context.MaxByteStringLength = lengthLimit;
                m_context.MaxArrayLength = lengthLimit;
                m_encoder ??= new BinaryEncoder(m_stream, m_context, true);
                m_stream.Position = 0;
                m_stream.Limit = limit;
                try
                {
                    m_encoder.WriteDataValue(null, value);
                    return m_stream.Position;
                }
                catch (ServiceResultException)
                {
                    // the encoder is not used after a failure.
                    DisposeEncoder();
                    return long.MaxValue;
                }
            }

            private readonly ServiceMessageContext m_context;
            private readonly ByteCountingStream m_stream = new();
            private BinaryEncoder? m_encoder;
        }

        /// <summary>
        /// A stream that counts the bytes written to it and discards them.
        /// </summary>
        private sealed class ByteCountingStream : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => m_length;

            public override long Position
            {
                get => m_position;
                set => m_position = value;
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                m_position = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => m_position + offset,
                    _ => m_length + offset
                };
                return m_position;
            }

            public override void SetLength(long value)
            {
                m_length = value;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Advance(count);
            }

#if NET
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                Advance(buffer.Length);
            }
#endif

            public override void WriteByte(byte value)
            {
                Advance(1);
            }

            /// <summary>
            /// The position writes may not pass.
            /// </summary>
            public long Limit { get; set; } = long.MaxValue;

            private void Advance(int count)
            {
                if (count > Limit - m_position)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The value exceeds the response size limit.");
                }
                m_position += count;
                if (m_position > m_length)
                {
                    m_length = m_position;
                }
            }

            private long m_length;
            private long m_position;
        }

        private static List<DataValue> FillRawPage(
            ServerSystemContext systemContext,
            HistoryReadResult result,
            HistorianPage<HistoricalDataValue> page,
            HistoryReadValueId nodeToRead,
            TimestampsToReturn timestampsToReturn)
        {
            var values = new List<DataValue>(page.Values.Count);
            foreach (HistoricalDataValue v in page.Values)
            {
                values.Add(v.Value);
            }
            return FillHistoryData(
                systemContext,
                result,
                values,
                nodeToRead,
                timestampsToReturn);
        }

        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
#pragma warning disable CA2000 // Continuation state transfers to the session or is disposed inline.
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
                    OriginNodeId = nodeToRead.NodeId,
                    SourceNode = node,
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
#pragma warning restore CA2000

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
            ILogger logger = systemContext.Server.Telemetry.CreateLogger(nameof(HistorianContinuationClaim));
            IHistoryContinuationPoint? raw = await continuationPoints
                .RestoreHistoryAsync(nodeToRead.ContinuationPoint, cancellationToken)
                .ConfigureAwait(false);
            if (raw is not HistorianContinuationState state)
            {
                raw?.Dispose();
                return null;
            }
            var claim = new HistorianContinuationClaim(
                continuationPoints,
                state,
                logger);
            try
            {
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
                    ((state.OriginNodeId.IsNull ? state.NodeId : state.OriginNodeId) != nodeToRead.NodeId &&
                        !legacyAnnotationNodeMatches))
                {
                    claim.Retire();
                    await claim.DisposeAsync().ConfigureAwait(false);
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
                    await claim.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
                return claim;
            }
            catch (Exception operationFailure)
            {
                try
                {
                    await claim.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "The history operation and continuation restoration both failed.",
                        operationFailure,
                        cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>
        /// Restores an unfinished claim without losing a failure from the operation being unwound.
        /// </summary>
        /// <exception cref="AggregateException">
        /// An operation failure was supplied and disposing its continuation claim also fails.
        /// </exception>
        private static async ValueTask DisposeContinuationClaimAsync(
            HistorianContinuationClaim? claim,
            Exception? operationFailure)
        {
            if (claim is null)
            {
                return;
            }
            try
            {
                await claim.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure) when (
                operationFailure is not null && cleanupFailure is not OutOfMemoryException)
            {
                throw new AggregateException(
                    "The history operation and continuation restoration both failed.",
                    operationFailure,
                    cleanupFailure);
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
                catch (Exception exception) when (claim.IsActive &&
                    exception is ServiceResultException or
                        InvalidOperationException or
                        IOException or
                        TimeoutException)
                {
                    await DisposeContinuationClaimAsync(claim, exception).ConfigureAwait(false);
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
                        systemContext,
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

        /// <summary>
        /// Saves an initial continuation or disposes it when admission does not transfer ownership.
        /// </summary>
        internal static async ValueTask<bool> TrySaveHistoryContinuationAsync(
            ServerSystemContext systemContext,
            ISessionContinuationPoints continuationPoints,
            HistorianContinuationState state,
            HistoryReadResult result,
            CancellationToken cancellationToken)
        {
            bool transferred = false;
            Exception? operationFailure = null;
            ServiceResult? deniedAdmission = null;
            try
            {
                ServiceResult admission = ServiceResult.Good;
                if (systemContext.Server?.NodeManager is MasterNodeManager master)
                {
                    admission = await master.CaptureHistoryContinuationAsync(
                        systemContext.OperationContext!, state, validateSource: false, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (ServiceResult.IsBad(admission))
                {
                    deniedAdmission = admission;
                    result.StatusCode = admission.StatusCode;
                    result.HistoryData = default;
                    result.ContinuationPoint = ByteString.Empty;
                    return false;
                }
                cancellationToken.ThrowIfCancellationRequested();
                transferred = true;
                await continuationPoints.SaveHistoryAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException failure)
                when (cancellationToken.IsCancellationRequested)
            {
                operationFailure = failure;
                throw;
            }
            catch (Exception exception) when (
                exception is ServiceResultException or
                InvalidOperationException or
                IOException or
                TimeoutException)
            {
                operationFailure = exception;
                result.StatusCode = exception is ServiceResultException serviceException &&
                    serviceException.StatusCode == StatusCodes.BadSessionClosed
                        ? StatusCodes.BadSessionClosed
                        : StatusCodes.BadNoContinuationPoints;
                result.ContinuationPoint = ByteString.Empty;
                return false;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                operationFailure = failure;
                throw;
            }
            finally
            {
                if (!transferred)
                {
                    DisposeUntransferredHistoryState(
                        continuationPoints, state, operationFailure, deniedAdmission);
                }
            }
        }

        /// <summary>
        /// Releases a state whose admission failed without discarding the original failure or denial.
        /// </summary>
        /// <exception cref="AggregateException">
        /// Disposing the untransferred state fails after admission failed or was denied.
        /// </exception>
        private static void DisposeUntransferredHistoryState(
            ISessionContinuationPoints continuationPoints,
            HistorianContinuationState state,
            Exception? operationFailure,
            ServiceResult? deniedAdmission)
        {
            try
            {
                if (continuationPoints is SessionContinuationPoints sessionPoints)
                {
                    sessionPoints.DisposeHistoryReadPoint(state, checkSessionOwnership: true);
                }
                else
                {
                    state.Dispose();
                }
            }
            catch (Exception cleanupFailure) when (
                cleanupFailure is not OutOfMemoryException &&
                (operationFailure is not null || deniedAdmission is not null))
            {
                throw new AggregateException(
                    "History continuation admission and cleanup failed.",
                    operationFailure ?? new ServiceResultException(deniedAdmission!),
                    cleanupFailure);
            }
        }

        private static HistoryReadValueId RestoreReadParameters(
            HistorianContinuationState state,
            HistoryReadValueId current)
        {
            return new HistoryReadValueId
            {
                NodeId = current.NodeId,
                ContinuationPoint = current.ContinuationPoint,
                IndexRange = state.IndexRange.ToString(),
                ParsedIndexRange = state.IndexRange,
                DataEncoding = state.DataEncoding
            };
        }

        private static List<DataValue> FillHistoryData(
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
            return filtered;
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
            if (indexRange.IsNull || value.WrappedValue.IsNull)
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

        /// <summary>
        /// Reads the raw values the at-time fallback needs to calculate the value at
        /// <paramref name="requestedTime"/>: the values at that time or, failing that,
        /// the bounding values on either side (Part 11 6.4.4 / Part 13 3.1.8). Only the
        /// neighbourhood of each requested time is read, so the cost does not grow with
        /// the history between widely separated requested times. Returns <c>null</c>
        /// when more than <see cref="kMaxProcessedBufferedOutputs"/> values had to be
        /// scanned to find a bound.
        /// </summary>
        private static async ValueTask<List<DataValue>?> CollectAtTimeSamplesAsync(
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            DateTimeUtc requestedTime,
            bool useSimpleBounds,
            bool stepped,
            AtTimeScanBudget budget,
            CancellationToken cancellationToken)
        {
            var samples = new List<DataValue>();
            await AddExactRawValuesAsync(
                samples,
                context,
                raw,
                nodeId,
                requestedTime,
                cancellationToken,
                budget).ConfigureAwait(false);
            if (samples.Count > 0)
            {
                return samples;
            }

            // Simple bounds use the nearest raw value on each side whatever its status;
            // a stepped signal only needs to know whether any later value exists; a
            // sloped signal needs the first non-Bad value after the time.
            int requiredAfter = useSimpleBounds || stepped ? 0 : 1;
            int foundAfter = await AddBoundingValuesAsync(
                samples,
                context,
                raw,
                nodeId,
                requestedTime,
                before: false,
                requiredAfter,
                cancellationToken,
                budget: budget).ConfigureAwait(false);
            if (foundAfter < 0)
            {
                return null;
            }

            // Without a non-Bad value after the time a sloped signal extrapolates from
            // the two non-Bad values before it.
            int requiredBefore = useSimpleBounds ? 0 : !stepped && foundAfter == 0 ? 2 : 1;
            int foundBefore = await AddBoundingValuesAsync(
                samples,
                context,
                raw,
                nodeId,
                requestedTime,
                before: true,
                requiredBefore,
                cancellationToken,
                budget: budget).ConfigureAwait(false);
            if (foundBefore < 0)
            {
                return null;
            }

            samples.Sort((a, b) => a.SourceTimestamp.CompareTo(b.SourceTimestamp));
            return samples;
        }

        /// <summary>
        /// Adds the raw values on one side of <paramref name="boundary"/>, nearest first,
        /// until <paramref name="requiredNonBad"/> non-Bad values with distinct timestamps
        /// were added (or, when it is zero, until one value of any status was added).
        /// Bad values passed on the way are added too, since they make the calculated
        /// value Uncertain. Returns the number of non-Bad values added, or -1 when more
        /// than <see cref="kMaxProcessedBufferedOutputs"/> values were scanned first.
        /// <paramref name="isBound"/> replaces the non-Bad test, for example to require
        /// Good values when TreatUncertainAsBad is set.
        /// </summary>
        private static async ValueTask<int> AddBoundingValuesAsync(
            List<DataValue> collected,
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            DateTimeUtc boundary,
            bool before,
            int requiredNonBad,
            CancellationToken cancellationToken,
            Func<DataValue, bool>? isBound = null,
            AtTimeScanBudget? budget = null)
        {
            var nonBadTimestamps = new HashSet<DateTimeUtc>();
            int added = 0;
            bool IsSatisfied()
            {
                return requiredNonBad == 0
                    ? added > 0
                    : nonBadTimestamps.Count >= requiredNonBad;
            }
            bool Add(DataValue value)
            {
                collected.Add(value);
                added++;
                if (isBound?.Invoke(value) ?? StatusCode.IsNotBad(value.StatusCode))
                {
                    nonBadTimestamps.Add(value.SourceTimestamp);
                }
                return IsSatisfied();
            }

            // Open-ended reads, so MaxValues bounds the total the provider returns
            // (Part 11 6.4.3.2). Start with a few values and only widen the read,
            // up to the scan budget, while Bad values hide the bound.
            int start = collected.Count;
            int limit = kInitialBoundingReadSize;
            while (true)
            {
                var request = new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = before ? DateTimeUtc.MinValue : boundary,
                    EndTime = before ? boundary : DateTimeUtc.MaxValue,
                    MaxValues = (uint)limit,
                    IsForward = !before,
                    ReturnBounds = false
                };
                int scanned = 0;
                HistorianResumeToken token = default;
                while (true)
                {
                    HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                        context,
                        request,
                        token,
                        cancellationToken).ConfigureAwait(false);
                    budget?.Charge(page.Values.Count);
                    // Walk the page nearest-first whatever order the provider returned it in.
                    var outside = new List<(DataValue Value, int Index)>();
                    foreach (HistoricalDataValue historicalValue in page.Values)
                    {
                        DataValue value = historicalValue.Value;
                        if ((before
                                ? value.SourceTimestamp < boundary
                                : value.SourceTimestamp > boundary) &&
                            value.StatusCode != StatusCodes.BadBoundNotFound)
                        {
                            outside.Add((value, outside.Count));
                        }
                    }
                    outside.Sort((a, b) =>
                    {
                        int order = a.Value.SourceTimestamp.CompareTo(b.Value.SourceTimestamp);
                        return order != 0 ? before ? -order : order : a.Index.CompareTo(b.Index);
                    });
                    foreach ((DataValue value, _) in outside)
                    {
                        if (Add(value))
                        {
                            return nonBadTimestamps.Count;
                        }
                    }
                    scanned += page.Values.Count;
                    if (page.IsFinal || scanned > kMaxProcessedBufferedOutputs)
                    {
                        break;
                    }
                    token = page.NextToken;
                }
                if (scanned < limit)
                {
                    // The provider has no further values on this side.
                    break;
                }
                if (limit > kMaxProcessedBufferedOutputs)
                {
                    return -1;
                }
                collected.RemoveRange(start, collected.Count - start);
                nonBadTimestamps.Clear();
                added = 0;
                limit = (int)Math.Min((long)limit * 64, kMaxProcessedBufferedOutputs + 1L);
            }

            // The range read excludes the extreme timestamp itself.
            DateTimeUtc extreme = before ? DateTimeUtc.MinValue : DateTimeUtc.MaxValue;
            if (extreme == boundary)
            {
                return nonBadTimestamps.Count;
            }
            var extremeValues = new List<DataValue>();
            await AddExactRawValuesAsync(
                extremeValues,
                context,
                raw,
                nodeId,
                extreme,
                cancellationToken,
                budget).ConfigureAwait(false);
            foreach (DataValue value in extremeValues)
            {
                if (Add(value))
                {
                    break;
                }
            }
            return nonBadTimestamps.Count;
        }
        private static async ValueTask AddExactRawValuesAsync(
            List<DataValue> collected,
            HistorianOperationContext context,
            IHistorianDataProvider raw,
            NodeId nodeId,
            DateTimeUtc timestamp,
            CancellationToken cancellationToken,
            AtTimeScanBudget? budget = null)
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
                budget?.Charge(page.Values.Count);
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

        /// <summary>
        /// Returns the timestamp of the first stored raw value that a read with bounds over
        /// the window returns in the given direction: the earliest value at or before the
        /// window for a forward read, the latest at or after it for a reverse read, or
        /// <c>null</c> when the node has no raw data. Reads at most one value and a bound.
        /// </summary>
        private static async ValueTask<DateTimeUtc?> ReadEdgeRawTimestampAsync(
            IHistorianDataProvider raw,
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc windowStart,
            DateTimeUtc windowEnd,
            bool isForward,
            CancellationToken cancellationToken)
        {
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = windowStart,
                EndTime = windowEnd,
                MaxValues = 2,
                IsForward = isForward,
                ReturnBounds = true
            };
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await raw.ReadRawAsync(
                    context, request, token, cancellationToken).ConfigureAwait(false);
                foreach (HistoricalDataValue sample in page.Values)
                {
                    // skip bound placeholders as AggregateCalculator.QueueRawValue does.
                    StatusCode status = sample.Value.StatusCode;
                    if (!sample.Value.IsNull &&
                        status != StatusCodes.BadNoData &&
                        status != StatusCodes.BadBoundNotFound)
                    {
                        return sample.Value.SourceTimestamp;
                    }
                }
                if (page.IsFinal)
                {
                    return null;
                }
                token = page.NextToken;
            }
        }

        /// <summary>
        /// Returns the timestamp of the first annotation in the range in the given direction,
        /// or <c>null</c> when the range has none. Reads at most one annotation.
        /// </summary>
        private static async ValueTask<DateTimeUtc?> ReadEdgeAnnotationTimestampAsync(
            IHistorianAnnotationProvider provider,
            HistorianOperationContext context,
            NodeId nodeId,
            DateTimeUtc startTime,
            DateTimeUtc endTime,
            bool isForward,
            CancellationToken cancellationToken)
        {
            var request = new HistorianAnnotationReadRequest
            {
                NodeId = nodeId,
                StartTime = startTime,
                EndTime = endTime,
                MaxValues = 1,
                IsForward = isForward
            };
            HistorianResumeToken token = default;
            while (true)
            {
                if (provider is IHistorianTimestampedAnnotationProvider timestamped)
                {
                    HistorianPage<HistorianAnnotation> timestampedPage =
                        await timestamped.ReadAnnotationsWithTimestampsAsync(
                            context, request, token, cancellationToken).ConfigureAwait(false);
                    foreach (HistorianAnnotation annotation in timestampedPage.Values)
                    {
                        return annotation.SourceTimestamp;
                    }
                    if (timestampedPage.IsFinal)
                    {
                        return null;
                    }
                    token = timestampedPage.NextToken;
                    continue;
                }
                HistorianPage<Annotation> page = await provider.ReadAnnotationsAsync(
                    context, request, token, cancellationToken).ConfigureAwait(false);
                foreach (Annotation annotation in page.Values)
                {
                    return annotation.AnnotationTime;
                }
                if (page.IsFinal)
                {
                    return null;
                }
                token = page.NextToken;
            }
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
