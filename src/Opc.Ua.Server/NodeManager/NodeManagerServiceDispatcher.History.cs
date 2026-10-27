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
            SessionContinuationPoints? sessionContinuationPoints = !releaseContinuationPoints
                ? context.Session?.ContinuationPoints as SessionContinuationPoints
                : null;
            // Protect both captured-generation resumes and new pages for the full request.
            using IDisposable? historyRequest = sessionContinuationPoints?.BeginHistoryRequest(nodesToRead);
            try
            {
                return await HistoryReadCoreAsync(
                    context, historyReadDetails, timestampsToReturn, releaseContinuationPoints, nodesToRead,
                    results, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                ReleaseUnreturnedHistoryContinuationPoints(
                    context, sessionContinuationPoints, historyRequest, nodesToRead, results);
                ReleaseFailedHistoryResults(context, nodesToRead, results, failure);
                throw;
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
            NodeManagerGeneration generation = GetGeneration(context, source);
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
        /// <param name="details">The details of the read.</param>
        /// <param name="timestampsToReturn">The timestamps to return.</param>
        /// <param name="release">Whether to release the continuation point.</param>
        /// <param name="index">The index of the node in the request.</param>
        /// <param name="nodeCount">The number of nodes in the request.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The result and the error of the node.</returns>
        private async ValueTask<(HistoryReadResult Result, ServiceResult Error)> ReadHistoryContinuationAsync(
            OperationContext context,
            SessionContinuationPoints holder,
            HistoryReadValueId node,
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
            IHistoryContinuationPoint? point = await holder.RestoreHistoryAsync(
                node.ContinuationPoint, cancellationToken).ConfigureAwait(false);
            using SessionContinuationPoints.HistoryReadScope use = holder.BeginHistoryRead(node.ContinuationPoint, point);
            var result = new HistoryReadResult();
            if (use.Point is null ||
                (use.Point is HistorianContinuationState saved &&
                    (saved.OriginNodeId.IsNull ? saved.NodeId : saved.OriginNodeId) != node.NodeId))
            {
                return (result, StatusCodes.BadContinuationPointInvalid);
            }
            var state = use.Point as HistorianContinuationState;
            NodeManagerGenerationReference? generationReference =
                state?.Ownership.Generation ?? holder.GetHistoryGeneration(use.Point);
            if (generationReference is null ||
                !generationReference.TryGetGeneration(out NodeManagerGeneration? generation) ||
                generation is null)
            {
                return (result, StatusCodes.BadContinuationPointInvalid);
            }
            using GenerationGate.Lease? generationLease = TryEnterHistoryContinuation(generation);
            if (generationLease is null)
            {
                return (result, StatusCodes.BadContinuationPointInvalid);
            }
            using (generationLease)
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
                    await generation.NodeManager.HistoryReadAsync(
                        context,
                        details,
                        timestampsToReturn,
                        release,
                        [node],
                        results,
                        errors,
                        cancellationToken).ConfigureAwait(false);
                    return (results[0] ?? result, node.Processed ? errors[0] : StatusCodes.BadNodeIdUnknown);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    ReleaseFailedHistoryResults(context, [node], results, failure);
                    throw;
                }
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
        /// Disposes the continuation points in already produced results after a failed request.
        /// </summary>
        /// <param name="context">The operation context of the request.</param>
        /// <param name="requested">
        /// The operations of the request. A result that carries a continuation point the client supplied is kept,
        /// since the client still holds it.
        /// </param>
        /// <param name="results">The results produced so far.</param>
        /// <param name="failure">The failure that aborted the request.</param>
        private static void ReleaseFailedHistoryResults(
            OperationContext context,
            ArrayOf<HistoryReadValueId> requested,
            List<HistoryReadResult> results,
            Exception failure)
        {
            List<Exception>? failures = null;
            foreach (HistoryReadResult? result in results)
            {
                if (result is null || result.ContinuationPoint.IsEmpty || IsClientSupplied(requested, result))
                {
                    continue;
                }
                try
                {
                    context.Session?.ContinuationPoints.RestoreHistory(result.ContinuationPoint)?.Dispose();
                }
                catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                {
                    (failures ??= [failure]).Add(cleanupFailure);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException("HistoryRead and continuation cleanup failed.", failures);
            }
        }

        private static bool IsClientSupplied(ArrayOf<HistoryReadValueId> requested, HistoryReadResult result)
        {
            for (int ii = 0; ii < requested.Count; ii++)
            {
                if (requested[ii] != null && requested[ii].ContinuationPoint.Equals(result.ContinuationPoint))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
