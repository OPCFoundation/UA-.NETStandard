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
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        private async ValueTask<List<ReferenceDescription>> BrowseBoundedAsync(
            NodeId node,
            NodeId referenceTypeId,
            BrowseDirection direction,
            NodeClass nodeClassMask,
            BrowseBudget budget,
            CancellationToken cancellationToken)
        {
            var references = new List<ReferenceDescription>();
            ByteString owned = default;
            ArrayOf<BrowseResult> unreconciled = [];
            Exception? failure = null;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null,
                    null,
                    (uint)Math.Max(1, Math.Min(64, budget.Remaining)),
                    [
                        new BrowseDescription
                        {
                            NodeId = node,
                            ReferenceTypeId = referenceTypeId,
                            BrowseDirection = direction,
                            IncludeSubtypes = true,
                            NodeClassMask = (uint)nodeClassMask,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                unreconciled = response.Results;
                ValidateResponse(response.ResponseHeader, response.Results.Count, 1, response.DiagnosticInfos.Count);
                BrowseResult result = RequireBrowseResult(response.Results[0]);
                owned = result.ContinuationPoint;
                unreconciled = [];
                for (int page = 1; ; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    budget.Add(result.References.Count);
                    for (int ii = 0; ii < result.References.Count; ii++)
                    {
                        if (result.References[ii] is null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError, "An AMB browse reference is missing.");
                        }
                    }
                    references.AddRange(result.References);
                    if (!HasContinuation(owned))
                    {
                        break;
                    }
                    if (page >= m_options!.MaximumBrowsePages)
                    {
                        throw LimitExceeded("The AMB browse page limit was exceeded.");
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [owned], cancellationToken).ConfigureAwait(false);
                    unreconciled = next.Results;
                    ValidateResponse(next.ResponseHeader, next.Results.Count, 1, next.DiagnosticInfos.Count);
                    result = RequireBrowseResult(next.Results[0]);
                    // A successful next page consumes the old token, even when
                    // the server reuses its bytes for the new continuation.
                    owned = result.ContinuationPoint;
                    unreconciled = [];
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Exception? cleanupFailure = await ReleaseContinuationsAsync(owned, unreconciled).ConfigureAwait(false);
            if (failure is not null && cleanupFailure is not null)
            {
                throw new AggregateException(
                    "The AMB browse failed and its continuation points could not be released.",
                    failure,
                    cleanupFailure);
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return references;
        }

        private async ValueTask<Exception?> ReleaseContinuationsAsync(
            ByteString owned,
            ArrayOf<BrowseResult> unreconciled)
        {
            if (!HasContinuation(owned) && unreconciled.Count == 0)
            {
                return null;
            }
            Exception? failure = null;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Keep the malformed response borrowed instead of copying an
            // arbitrary number of server-supplied continuation points.
            for (int ii = -1; ii < unreconciled.Count; ii++)
            {
                try
                {
                    cleanup.Token.ThrowIfCancellationRequested();
                    ByteString token = ii < 0 ? owned : ContinuationOf(unreconciled[ii]);
                    if (!HasContinuation(token) ||
                        AlreadyReleased(token, owned, unreconciled, ii, cleanup.Token))
                    {
                        continue;
                    }
                    BrowseNextResponse response = await Session.BrowseNextAsync(
                        null, true, [token], cleanup.Token).ConfigureAwait(false);
                    // Part 4 release responses are empty; also accept the stack's single-result form.
                    ValidateResponse(
                        response.ResponseHeader,
                        response.Results.Count,
                        response.Results.Count == 0 ? 0 : 1,
                        response.DiagnosticInfos.Count);
                    if (response.Results.Count == 0)
                    {
                        continue;
                    }
                    BrowseResult result = response.Results[0] ??
                        throw new ServiceResultException(StatusCodes.BadDecodingError);
                    if (HasContinuation(result.ContinuationPoint) || result.References.Count != 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "Releasing an AMB continuation returned unexpected data.");
                    }
                    if (result.StatusCode != StatusCodes.BadContinuationPointInvalid)
                    {
                        RequireGoodResponse(result.StatusCode);
                    }
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                    if (cleanup.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
            return failure;
        }

        private static ByteString ContinuationOf(BrowseResult? result)
        {
            return result is null ? default : result.ContinuationPoint;
        }

        private static bool AlreadyReleased(
            ByteString token,
            ByteString owned,
            ArrayOf<BrowseResult> results,
            int index,
            CancellationToken cancellationToken)
        {
            if (index >= 0 && token == owned)
            {
                return true;
            }
            // Only malformed, multi-result responses need this scan. It
            // avoids an unbounded auxiliary set and shares the cleanup deadline.
            for (int ii = 0; ii < index; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (token == ContinuationOf(results[ii]))
                {
                    return true;
                }
            }
            return false;
        }

        private void ValidateResponse(ResponseHeader? header, int actual, int expected, int diagnostics = 0)
        {
            if (m_options is null)
            {
                return;
            }
            if (header is null || actual != expected || (diagnostics != 0 && diagnostics != expected))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "An invalid AMB response was returned.");
            }
            RequireGoodResponse(header.ServiceResult);
        }

        private static BrowseResult RequireBrowseResult(BrowseResult? result)
        {
            if (result is null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "A browse result is missing.");
            }
            RequireGoodResponse(result.StatusCode);
            return result;
        }

        private static void RequireGoodResponse(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status, "The AMB response is not Good.");
            }
        }

        private static void ValidateCallResult(CallMethodResult? result, int inputs, int outputs)
        {
            if (result is null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "A method result is missing.");
            }
            RequireGoodResponse(result.StatusCode);
            if (result.OutputArguments.Count != outputs ||
                (result.InputArgumentResults.Count != 0 && result.InputArgumentResults.Count != inputs) ||
                (result.InputArgumentDiagnosticInfos.Count != 0 && result.InputArgumentDiagnosticInfos.Count != inputs))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "An invalid method result was returned.");
            }
            for (int ii = 0; ii < result.InputArgumentResults.Count; ii++)
            {
                RequireGoodResponse(result.InputArgumentResults[ii]);
            }
        }

        private static bool HasContinuation(ByteString token)
        {
            return !token.IsNull && token.Length != 0;
        }

        private static ServiceResultException LimitExceeded(string message)
        {
            return new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, message);
        }

        private BrowseBudget CreateBrowseBudget()
        {
            return new BrowseBudget(m_options?.MaximumBrowseReferences ?? 0);
        }

        /// <summary>
        /// Counts raw references across a complete traversal, before copying
        /// them or adding their targets to queues and sets.
        /// </summary>
        private sealed class BrowseBudget(int maximum)
        {
            /// <summary>
            /// Gets the remaining reference allowance.
            /// </summary>
            public int Remaining => maximum == 0 ? int.MaxValue : maximum - m_count;

            /// <summary>
            /// Reserves references before accumulating a page or alias result.
            /// </summary>
            public void Add(int count)
            {
                if (maximum == 0)
                {
                    return;
                }
                if (count > Remaining)
                {
                    throw LimitExceeded("The AMB traversal reference limit was exceeded.");
                }
                m_count += count;
            }

            private int m_count;
        }
    }
}
