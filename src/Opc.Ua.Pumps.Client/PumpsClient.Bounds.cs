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

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        private async Task<ArrayOf<ReferenceDescription>> BrowseBoundedAsync(
            NodeId parent,
            NodeClass nodeClass,
            CancellationToken cancellationToken)
        {
            var references = new List<ReferenceDescription>();
            var outstanding = new HashSet<ByteString>();
            Exception? failure = null;
            bool complete = false;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null,
                    null,
                    (uint)Math.Min(64, m_maximumBrowseReferences),
                    [
                        new BrowseDescription
                        {
                            NodeId = parent,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            NodeClassMask = (uint)nodeClass,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                RememberContinuations(response.Results, outstanding);
                ValidateBoundedResponse(response.ResponseHeader, response.Results.Count, 1);
                BrowseResult result = response.Results[0];
                for (int page = 0; page < 128; page++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireGoodResponse(result.StatusCode);
                    if (result.References.Count > m_maximumBrowseReferences - references.Count)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "The pump browse reference limit was exceeded.");
                    }
                    references.AddRange(result.References);
                    if (result.ContinuationPoint.IsNull || result.ContinuationPoint.Length == 0)
                    {
                        complete = true;
                        break;
                    }
                    if (page == 127)
                    {
                        break;
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null,
                        false,
                        [result.ContinuationPoint],
                        cancellationToken).ConfigureAwait(false);
                    RememberContinuations(next.Results, outstanding);
                    ValidateBoundedResponse(next.ResponseHeader, next.Results.Count, 1);
                    RequireGoodResponse(next.Results[0].StatusCode);
                    outstanding.Remove(result.ContinuationPoint);
                    result = next.Results[0];
                    RememberContinuations(next.Results, outstanding);
                }
                if (!complete)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The pump browse exceeded 128 pages.");
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (outstanding.Count != 0)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    BrowseNextResponse released = await Session.BrowseNextAsync(
                        null,
                        true,
                        outstanding.ToArrayOf(),
                        cleanup.Token).ConfigureAwait(false);
                    ValidateBoundedResponse(
                        released.ResponseHeader,
                        released.Results.Count,
                        released.Results.Count == 0 ? 0 : outstanding.Count);
                    foreach (BrowseResult result in released.Results)
                    {
                        if (result.StatusCode != StatusCodes.BadContinuationPointInvalid)
                        {
                            RequireGoodResponse(result.StatusCode);
                        }
                    }
                }
                catch (Exception cleanupFailure) when (failure is not null)
                {
                    throw new AggregateException(
                        "The pump browse failed and its continuation points could not be released.",
                        failure,
                        cleanupFailure);
                }
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return references.ToArrayOf();
        }

        private void ValidateBoundedResponse(ResponseHeader? header, int actual, int expected)
        {
            if (m_maximumBrowseReferences == 0)
            {
                return;
            }
            if (header is null || actual != expected)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "An incomplete pump service response was returned.");
            }
            RequireGoodResponse(header.ServiceResult);
        }

        private static void RememberContinuations(
            ArrayOf<BrowseResult> results,
            HashSet<ByteString> outstanding)
        {
            foreach (BrowseResult result in results)
            {
                if (!result.ContinuationPoint.IsNull && result.ContinuationPoint.Length != 0)
                {
                    outstanding.Add(result.ContinuationPoint);
                }
            }
        }

        private static void RequireGoodResponse(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status, "The pump service response is not Good.");
            }
        }

        /// <summary>
        /// Accounts for nested groups before their reads allocate result buffers.
        /// </summary>
        private sealed class ValueReadBudget(int maximum)
        {
            /// <summary>
            /// Reserves discovered members without allowing a partial value set.
            /// </summary>
            /// <exception cref="ServiceResultException">The value-set limit was exceeded.</exception>
            public void Add(int count)
            {
                if (maximum != 0 && count > maximum - m_count)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "The pump value-set limit was exceeded.");
                }
                m_count = checked(m_count + count);
            }

            private int m_count;
        }
    }
}
