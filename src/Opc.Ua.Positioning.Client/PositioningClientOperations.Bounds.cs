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

namespace Opc.Ua.Positioning.Client
{
    internal sealed partial class PositioningClientOperations
    {
        private async ValueTask<ArrayOf<ReferenceDescription>> BrowseBoundedAsync(
            NodeId nodeId,
            NodeId referenceTypeId,
            uint nodeClassMask,
            CancellationToken cancellationToken)
        {
            ValidateNodeId(nodeId);
            cancellationToken.ThrowIfCancellationRequested();
            var references = new List<ReferenceDescription>();
            ByteString owned = default;
            ArrayOf<BrowseResult> pending = [];
            Exception? failure = null;
            try
            {
                BrowseResponse response = await Session.BrowseAsync(
                    null, null, (uint)Math.Min(64, m_options!.MaximumBrowseReferences),
                    [
                        new BrowseDescription
                        {
                            NodeId = nodeId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = referenceTypeId,
                            IncludeSubtypes = true,
                            NodeClassMask = nodeClassMask,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                pending = response.Results;
                ValidateResponse(response.ResponseHeader, pending.Count, 1, response.DiagnosticInfos.Count);
                for (int page = 1; ; page++)
                {
                    BrowseResult result = pending[0] ??
                        throw new ServiceResultException(StatusCodes.BadDecodingError, "A browse result is missing.");
                    RequireGood(result.StatusCode);
                    // A validated page consumes the submitted token, including
                    // when its successor has identical bytes or is empty.
                    owned = result.ContinuationPoint;
                    pending = [];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (result.References.Count > m_options.MaximumBrowseReferences - references.Count)
                    {
                        throw LimitExceeded("The Positioning browse reference limit was exceeded.");
                    }
                    foreach (ReferenceDescription reference in result.References)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reference is null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError, "A Positioning browse reference is missing.");
                        }
                        LocalNodeId(reference.NodeId);
                        if (!reference.TypeDefinition.IsNull)
                        {
                            LocalNodeId(reference.TypeDefinition);
                        }
                    }
                    references.AddRange(result.References);
                    if (owned.IsEmpty)
                    {
                        break;
                    }
                    if (page >= m_options.MaximumBrowsePages)
                    {
                        throw LimitExceeded("The Positioning browse page limit was exceeded.");
                    }
                    BrowseNextResponse next = await Session.BrowseNextAsync(
                        null, false, [owned], cancellationToken).ConfigureAwait(false);
                    pending = next.Results;
                    ValidateResponse(next.ResponseHeader, pending.Count, 1, next.DiagnosticInfos.Count);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Exception? cleanupFailure = await ReleaseContinuationsAsync(owned, pending).ConfigureAwait(false);
            if (failure is not null && cleanupFailure is not null)
            {
                throw new AggregateException(
                    "The Positioning browse and continuation cleanup both failed.", failure, cleanupFailure);
            }
            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return references.ToArrayOf();
        }

        private async ValueTask<Exception?> ReleaseContinuationsAsync(
            ByteString owned,
            ArrayOf<BrowseResult> pending)
        {
            if (owned.IsEmpty && pending.Count == 0)
            {
                return null;
            }
            Exception? failure = null;
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Malformed multi-result responses remain borrowed, not copied into
            // an unbounded token collection. The deadline also bounds deduplication.
            for (int ii = -1; ii < pending.Count; ii++)
            {
                try
                {
                    cleanup.Token.ThrowIfCancellationRequested();
                    ByteString token = ii < 0 ? owned : ContinuationOf(pending[ii]);
                    if (token.IsEmpty || WasReleased(token, owned, pending, ii, cleanup.Token))
                    {
                        continue;
                    }
                    BrowseNextResponse response = await Session.BrowseNextAsync(
                        null, true, [token], cleanup.Token).ConfigureAwait(false);
                    ValidateResponse(
                        response.ResponseHeader, response.Results.Count, 1, response.DiagnosticInfos.Count);
                    BrowseResult result = response.Results[0] ??
                        throw new ServiceResultException(StatusCodes.BadDecodingError, "A release result is missing.");
                    if (result.StatusCode != StatusCodes.BadContinuationPointInvalid)
                    {
                        RequireGood(result.StatusCode);
                    }
                    if (!result.ContinuationPoint.IsEmpty || result.References.Count != 0)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "Continuation cleanup returned unexpected browse data.");
                    }
                }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
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

        private static bool WasReleased(
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

        private void ValidateResponse(ResponseHeader? header, int actual, int expected, int diagnostics)
        {
            if (!Strict)
            {
                return;
            }
            if (header is null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The response header is missing.");
            }
            RequireGood(header.ServiceResult);
            if (actual != expected || (diagnostics != 0 && diagnostics != expected))
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "The response counts are invalid.");
            }
        }

        private NodeId LocalNodeId(ExpandedNodeId target)
        {
            if (Strict && target.ServerIndex != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "A remote Positioning target cannot be read through this session.");
            }
            NodeId nodeId = ExpandedNodeId.ToNodeId(target, Session.NamespaceUris);
            ValidateNodeId(nodeId);
            return nodeId;
        }

        private void ValidateNodeId(NodeId nodeId)
        {
            if (Strict && (nodeId.IsNull || Session.NamespaceUris.GetString(nodeId.NamespaceIndex) is null))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown, "A non-null local NodeId in an existing namespace is required.");
            }
        }

        private static void RequireGood(StatusCode status)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status, "The Positioning service or value quality is not Good.");
            }
        }

        private static ServiceResultException LimitExceeded(string message)
        {
            return new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, message);
        }
    }
}
