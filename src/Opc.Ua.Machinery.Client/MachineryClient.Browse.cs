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

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        private async ValueTask<ArrayOf<MachineEntry>> BrowseBoundedAsync(
            NodeId parent,
            NodeClass nodeClass,
            NodeId referenceTypeId,
            BrowseBudget budget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parent.IsNull)
            {
                return [];
            }
            MachineryReadValue evidence = UnresolvedField(parent, [], StatusCodes.Good);
            ValidateLocalNode(parent, evidence);
            var entries = new List<MachineEntry>();
            var seen = new HashSet<NodeId>();
            var continuations = new HashSet<ByteString>();
            ByteString owned = default;
            ArrayOf<BrowseResult> pending = [];
            Exception? failure = null;
            try
            {
                budget.BeginPage(evidence);
                BrowseResponse response = await Session.BrowseAsync(
                    null, null, (uint)Math.Max(1, Math.Min(64, budget.RemainingReferences)),
                    [
                        new BrowseDescription
                        {
                            NodeId = parent,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = referenceTypeId,
                            IncludeSubtypes = true,
                            NodeClassMask = (uint)nodeClass,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    cancellationToken).ConfigureAwait(false) ??
                    throw ReadFailure(StatusCodes.BadDecodingError, evidence, "The browse response is missing.");
                pending = response.Results;
                ValidateStrictResponse(
                    response.ResponseHeader, pending.Count, 1, response.DiagnosticInfos.Count, evidence);
                while (true)
                {
                    BrowseResult result = pending[0] ??
                        throw ReadFailure(StatusCodes.BadDecodingError, evidence, "The browse result is missing.");
                    RequireGood(result.StatusCode, evidence);
                    // Only a validated page consumes the submitted token.
                    // Retain malformed responses separately for bounded cleanup.
                    owned = result.ContinuationPoint;
                    pending = [];
                    cancellationToken.ThrowIfCancellationRequested();
                    budget.AddReferences(result.References.Count, evidence);
                    foreach (ReferenceDescription reference in result.References)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reference is null)
                        {
                            throw ReadFailure(StatusCodes.BadDecodingError, evidence, "A browse reference is missing.");
                        }
                        MachineryReadValue field = UnresolvedField(parent, [reference.BrowseName], StatusCodes.Good);
                        if (reference.NodeClass != nodeClass ||
                            string.IsNullOrEmpty(reference.BrowseName.Name) ||
                            Session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex) is null)
                        {
                            throw ReadFailure(
                                StatusCodes.BadDecodingError, field, "The browse reference is malformed.");
                        }
                        NodeId nodeId = StrictLocalNodeId(reference.NodeId, field);
                        NodeId typeId = reference.TypeDefinition.IsNull
                            ? NodeId.Null
                            : StrictLocalNodeId(reference.TypeDefinition, field);
                        if (seen.Add(nodeId))
                        {
                            entries.Add(new MachineEntry(
                                nodeId, reference.BrowseName, reference.DisplayName, typeId));
                        }
                    }
                    if (owned.IsEmpty)
                    {
                        break;
                    }
                    if (!continuations.Add(owned))
                    {
                        throw ReadFailure(
                            StatusCodes.BadDecodingError, evidence, "A browse continuation point was repeated.");
                    }
                    budget.BeginPage(evidence);
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [owned], cancellationToken).ConfigureAwait(false) ??
                        throw ReadFailure(
                            StatusCodes.BadDecodingError, evidence, "The continuation response is missing.");
                    pending = next.Results;
                    ValidateStrictResponse(next.ResponseHeader, pending.Count, 1, next.DiagnosticInfos.Count, evidence);
                }
            }
            catch (ServiceResultException exception) when (exception is not MachineryReadException)
            {
                failure = ReadFailure(
                    exception.StatusCode, UnresolvedField(parent, [], exception.StatusCode),
                    exception.Message, exception);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Exception? cleanupFailure = await ReleaseBrowseContinuationsAsync(owned, pending, evidence)
                .ConfigureAwait(false);
            if (failure is not null && cleanupFailure is not null)
            {
                throw new AggregateException(
                    "The Machinery browse and continuation cleanup both failed.", failure, cleanupFailure);
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return entries.ToArrayOf();
        }

        private async ValueTask<Exception?> ReleaseBrowseContinuationsAsync(
            ByteString owned,
            ArrayOf<BrowseResult> pending,
            MachineryReadValue evidence)
        {
            if (owned.IsEmpty && pending.Count == 0)
            {
                return null;
            }
            Exception? failure = null;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Borrow malformed result arrays; never copy arbitrary server
            // tokens into an unbounded cleanup collection.
            for (int ii = -1; ii < pending.Count; ii++)
            {
                try
                {
                    cleanup.Token.ThrowIfCancellationRequested();
                    ByteString token = ii < 0 ? owned : ContinuationOf(pending[ii]);
                    if (token.IsEmpty || AlreadyReleased(token, owned, pending, ii, cleanup.Token))
                    {
                        continue;
                    }
                    BrowseNextResponse response = await Session.BrowseNextAsync(
                        null, true, [token], cleanup.Token).ConfigureAwait(false) ??
                        throw ReadFailure(StatusCodes.BadDecodingError, evidence, "The release response is missing.");
                    ValidateStrictResponse(
                        response.ResponseHeader, response.Results.Count, 1, response.DiagnosticInfos.Count, evidence);
                    BrowseResult result = response.Results[0] ??
                        throw ReadFailure(StatusCodes.BadDecodingError, evidence, "The release result is missing.");
                    if (result.StatusCode != StatusCodes.BadContinuationPointInvalid)
                    {
                        RequireGood(result.StatusCode, evidence);
                    }
                    if (!result.ContinuationPoint.IsEmpty || result.References.Count != 0)
                    {
                        throw ReadFailure(
                            StatusCodes.BadDecodingError, evidence, "Continuation release returned unexpected data.");
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
            return result is { ContinuationPoint: var point } ? point : default;
        }

        private static bool AlreadyReleased(
            ByteString token,
            ByteString owned,
            ArrayOf<BrowseResult> pending,
            int index,
            CancellationToken cancellationToken)
        {
            if (index >= 0 && token == owned)
            {
                return true;
            }
            for (int ii = 0; ii < index; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (token == ContinuationOf(pending[ii]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Counts raw references and pages before accumulating a bounded traversal.
        /// </summary>
        private sealed class BrowseBudget(MachineryClientOptions options)
        {
            /// <summary>
            /// Gets the remaining reference allowance, including duplicate targets.
            /// </summary>
            public int RemainingReferences => options.MaximumBrowseReferences - m_references;

            /// <summary>
            /// Reserves a page before making its service call.
            /// </summary>
            /// <exception cref="MachineryReadException"></exception>
            public void BeginPage(MachineryReadValue evidence)
            {
                if (m_pages == options.MaximumBrowsePages)
                {
                    throw ReadFailure(
                        StatusCodes.BadEncodingLimitsExceeded, evidence,
                        "The Machinery browse page limit was exceeded.");
                }
                m_pages++;
            }

            /// <summary>
            /// Reserves raw references before copying them or deduplicating targets.
            /// </summary>
            /// <exception cref="MachineryReadException"></exception>
            public void AddReferences(int count, MachineryReadValue evidence)
            {
                if (count > RemainingReferences)
                {
                    throw ReadFailure(
                        StatusCodes.BadEncodingLimitsExceeded,
                        evidence,
                        "The Machinery browse reference limit was exceeded.");
                }
                m_references += count;
            }

            private int m_pages;
            private int m_references;
        }
    }
}
