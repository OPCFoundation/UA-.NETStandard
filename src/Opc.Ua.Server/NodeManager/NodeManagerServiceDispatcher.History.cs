// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server
{
    internal sealed partial class NodeManagerServiceDispatcher
    {
        /// <summary>
        /// Reads history, resumes saved continuation points or releases them, and cleans up continuation points that
        /// were not returned if the request fails.
        /// </summary>
        /// <param name="context">The operation context of the request.</param>
        /// <param name="historyReadDetails">The details of the read.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="releaseContinuationPoints">Whether the request only releases continuation points.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The results and diagnostics for each node.</returns>
        /// <exception cref="AggregateException">
        /// The history read fails and releasing continuation points not returned to the caller also fails.
        /// </exception>
        internal async ValueTask<(ArrayOf<HistoryReadResult> Values, ArrayOf<DiagnosticInfo> Diagnostics)>
            HistoryReadAsync(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            ArrayOf<HistoryReadValueId> nodesToRead,
            CancellationToken cancellationToken = default)
        {
            var results = new List<HistoryReadResult>(nodesToRead.Count);
            HashSet<ByteString>? clientTokens = CaptureClientHistoryTokens(nodesToRead);
            SessionContinuationPoints? sessionContinuationPoints = !releaseContinuationPoints
                ? context.Session?.ContinuationPoints as SessionContinuationPoints
                : null;
            // Protect both captured-generation resumes and new pages for the full request.
            using IDisposable? historyRequest = sessionContinuationPoints?.BeginHistoryRequest(nodesToRead);
            try
            {
                return await HistoryReadCoreAsync(
                    context, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead,
                    clientTokens, historyRequest, results, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                List<Exception>? cleanupFailures = ReleaseUnreturnedHistoryContinuationPoints(
                    context, sessionContinuationPoints, historyRequest, clientTokens, results);
                if (cleanupFailures is not null)
                {
                    cleanupFailures.Insert(0, failure);
                    throw new AggregateException("HistoryRead and continuation cleanup failed.", cleanupFailures);
                }
                throw;
            }
            finally
            {
                clientTokens = null;
            }
        }

        /// <summary>
        /// Captures the node managers that a history continuation depends on, if its source requires captured
        /// ownership.
        /// </summary>
        /// <param name="context">The operation whose routing view owns the read.</param>
        /// <param name="state">The continuation state to capture.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <param name="validateSource">Whether restoration must validate the current live source node.</param>
        /// <returns>Good if captured or not required; otherwise the reason it cannot be captured.</returns>
        internal async ValueTask<ServiceResult> CaptureHistoryContinuationAsync(
            OperationContext context,
            HistorianContinuationState state,
            CancellationToken cancellationToken,
            bool validateSource = false)
        {
            if (state.Ownership.Generation is not null)
            {
                return ServiceResult.Good;
            }
            using OperationContext.RoutingScope requestRouting = CaptureRouting(context);
            (object? handle, IAsyncNodeManager? source) = await GetManagerHandleAsync(
                context, state.OriginNodeId.IsNull ? state.NodeId : state.OriginNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (handle is null || source is null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }
            NodeManagerGeneration generation = GetGenerationReference(context, source);
            if (validateSource)
            {
                using GenerationGate.Lease? admission = TryEnterHistoryContinuation(generation);
                if (admission is null)
                {
                    return StatusCodes.BadContinuationPointInvalid;
                }
                NodeMetadata? metadata = await source.GetNodeMetadataAsync(
                    context, handle, BrowseResultMask.None, cancellationToken).ConfigureAwait(false);
                if (metadata is null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }
                state.Ownership.Generation = generation.Reference;
                return ServiceResult.Good;
            }
            state.Ownership.Generation = generation.Reference;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Resumes or releases one history continuation point against its captured node managers.
        /// </summary>
        /// <param name="context">The operation context of the request.</param>
        /// <param name="holder">The session continuation points.</param>
        /// <param name="node">The node to read.</param>
        /// <param name="clientTokens">The continuation points the client supplied for this request.</param>
        /// <param name="historyRequest">The exact request owning saved points, or null for a release.</param>
        /// <param name="details">The details of the read.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="release">Whether to release the continuation point.</param>
        /// <param name="index">The index of the node in the request.</param>
        /// <param name="nodeCount">The number of nodes in the request.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The result and the error of the node.</returns>
        /// <exception cref="InvalidOperationException">
        /// A resumed history read needs a manager dispatch, but its owning history request is unavailable.
        /// </exception>
        private async ValueTask<(HistoryReadResult Result, ServiceResult Error)> ReadHistoryContinuationAsync(
            OperationContext context,
            SessionContinuationPoints holder,
            HistoryReadValueId node,
            HashSet<ByteString>? clientTokens,
            IDisposable? historyRequest,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool release,
            int index,
            int nodeCount,
            CancellationToken cancellationToken)
        {
            if (!release && details is ReadProcessedDetails processed)
            {
                if (processed.AggregateType.Count != nodeCount)
                {
                    throw new ServiceResultException(StatusCodes.BadAggregateListMismatch);
                }
                var single = (ReadProcessedDetails)processed.Clone();
                single.AggregateType = [processed.AggregateType[index]];
                details = single;
            }
            if (release)
            {
                return (
                    new HistoryReadResult(),
                    holder.ReleaseHistory(node.ContinuationPoint)
                        ? ServiceResult.Good
                        : StatusCodes.BadContinuationPointInvalid);
            }
            using SessionContinuationPoints.HistoryReadScope use = await holder.RestoreHistoryReadAsync(
                node.ContinuationPoint, cancellationToken).ConfigureAwait(false);
            holder.ActivateHistoryRead(use);
            var result = new HistoryReadResult();
            if (use.Point is null ||
                (use.Point is HistorianContinuationState saved &&
                    (saved.OriginNodeId.IsNull ? saved.NodeId : saved.OriginNodeId) != node.NodeId))
            {
                return (result, StatusCodes.BadContinuationPointInvalid);
            }
            var state = use.Point as HistorianContinuationState;
            NodeManagerGeneration? generation = use.Generation;
            if (generation is null)
            {
                NodeManagerGenerationReference? generationReference =
                    state?.Ownership.Generation ?? holder.GetHistoryGeneration(use.Point);
                if (generationReference is null ||
                    !generationReference.TryGetGeneration(out generation) ||
                    generation is null ||
                    !use.TryAdmitGeneration(generation))
                {
                    return (result, StatusCodes.BadContinuationPointInvalid);
                }
            }
            Exception? admittedFailure = null;
            try
            {
                ServiceResult? error = await ValidateHistoryReadRequestAsync(context, node, cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(error))
                {
                    return (result, error!);
                }

                node.Processed = false;
                var results = new List<HistoryReadResult> { result };
                var errors = new List<ServiceResult> { ServiceResult.Good };
                try
                {
                    IDisposable currentRequest = historyRequest ??
                        throw new InvalidOperationException("The history request is unavailable.");
                    using IDisposable historyDispatch = holder.BeginHistoryDispatch(currentRequest, generation);
                    await generation.NodeManager.HistoryReadAsync(
                        context,
                        details,
                        timestampsToReturn,
                        release,
                        [node],
                        results,
                        errors,
                        cancellationToken).ConfigureAwait(false);
                    if (node.Processed &&
                        results[0] is { ContinuationPoint.IsEmpty: false } page &&
                        !holder.TryBindHistoryResult(
                            currentRequest, historyDispatch, page.ContinuationPoint, generation))
                    {
                        return (new HistoryReadResult(), StatusCodes.BadContinuationPointInvalid);
                    }
                    return (results[0] ?? result, node.Processed ? errors[0] : StatusCodes.BadNodeIdUnknown);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    ReportUnattributedHistoryResults(holder, clientTokens, results, failure);
                    throw;
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                admittedFailure = failure;
                throw;
            }
            finally
            {
                DisposeHistoryReadScope(use, admittedFailure);
            }
        }

        /// <summary>
        /// Releases a resumed read without losing a failure from the admitted operation.
        /// </summary>
        /// <exception cref="AggregateException">
        /// An admitted history operation has failed and disposing its read scope also fails.
        /// </exception>
        private static void DisposeHistoryReadScope(
            SessionContinuationPoints.HistoryReadScope use,
            Exception? admittedFailure)
        {
            try
            {
                use.Dispose();
            }
            catch (Exception cleanupFailure) when (
                admittedFailure is not null && cleanupFailure is not OutOfMemoryException)
            {
                throw new AggregateException(
                    "HistoryRead and continuation cleanup failed.", admittedFailure, cleanupFailure);
            }
        }

        /// <summary>
        /// Enters a history cursor's issuing generation for the duration of a resumed read.
        /// </summary>
        /// <param name="generation">The cursor's issuing generation.</param>
        /// <returns>A generation lease while it remains live, or null after retirement.</returns>
        private static GenerationGate.Lease? TryEnterHistoryContinuation(NodeManagerGeneration generation)
        {
            return generation.Gate.TryEnter(out GenerationGate.Lease? lease) ? lease : null;
        }

        /// <summary>
        /// Reports result tokens whose exact ownership cannot be established after a failed request.
        /// </summary>
        /// <param name="holder">The session continuation points.</param>
        /// <param name="clientTokens">The continuation points the client still holds.</param>
        /// <param name="results">The results produced so far.</param>
        /// <param name="failure">The failure that aborted the request.</param>
        /// <exception cref="AggregateException">
        /// A result token is neither client-supplied nor recorded by the current request.
        /// </exception>
        private static void ReportUnattributedHistoryResults(
            SessionContinuationPoints holder,
            HashSet<ByteString>? clientTokens,
            List<HistoryReadResult> results,
            Exception failure)
        {
            List<Exception>? failures = null;
            foreach (HistoryReadResult? result in results)
            {
                if (result is null ||
                    result.ContinuationPoint.IsEmpty ||
                    IsClientSupplied(clientTokens, result) ||
                    holder.IsSavedByCurrentHistoryRequest(result.ContinuationPoint))
                {
                    continue;
                }
                (failures ??= [failure]).Add(CreateUnattributedHistoryContinuationFailure());
            }
            if (failures is not null)
            {
                throw new AggregateException("HistoryRead and continuation cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Creates a failure for a result that cannot safely be released by token alone.
        /// </summary>
        private static InvalidOperationException CreateUnattributedHistoryContinuationFailure()
        {
            return new InvalidOperationException(
                "An unreturned history continuation could not be attributed to this request; no release was attempted.");
        }

        private static HashSet<ByteString>? CaptureClientHistoryTokens(ArrayOf<HistoryReadValueId> requested)
        {
            HashSet<ByteString>? tokens = null;
            for (int ii = 0; ii < requested.Count; ii++)
            {
                if (requested[ii] is { ContinuationPoint.IsEmpty: false } node)
                {
                    (tokens ??= []).Add(node.ContinuationPoint.Copy());
                }
            }
            return tokens;
        }

        private static bool IsClientSupplied(HashSet<ByteString>? clientTokens, HistoryReadResult result)
        {
            return clientTokens?.Contains(result.ContinuationPoint) == true;
        }
    }
}
