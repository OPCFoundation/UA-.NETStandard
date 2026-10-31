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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Bridge.Model;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        private void BindPreparedTransport(RegistryBridgeState bridge)
        {
            bridge.PrepareRequest!.OnCallMethod2Async = async (context, method, owner, input, output, ct) =>
            {
                if (input.Count != 3 ||
                    !input[0].TryGetValue(out NodeId id) ||
                    !input[1].TryGetValue(out string operationId) ||
                    !input[2].TryGetValue(out string digest))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                XRegistryRequest request = await ClaimRequestAsync(context, id, operationId, digest, ct)
                    .ConfigureAwait(false);
                (NodeId file, uint handle) = await PrepareClaimedRequestAsync(context, id, request, ct)
                    .ConfigureAwait(false);
                output[0] = Variant.From(file);
                output[1] = Variant.From(handle);
                return ServiceResult.Good;
            };
            bridge.CommitPreparedRequest!.OnCallMethod2Async = async (context, method, owner, input, output, ct) =>
            {
                if (input.Count != 1 || !input[0].TryGetValue(out NodeId id))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                XRegistryResponse response = await CommitPreparedTransferAsync(context, id, ct).ConfigureAwait(false);
                output[0] = Variant.From(NodeId.Null);
                output[1] = Variant.From(0u);
                if (!response.IsSuccess)
                {
                    (NodeId file, uint handle) = await AddTransferAsync(
                        context, m_codec.EncodeResponse(response), false, ct).ConfigureAwait(false);
                    output[0] = Variant.From(file);
                    output[1] = Variant.From(handle);
                }
                return ServiceResult.Good;
            };
        }

        private async ValueTask<XRegistryRequest> ClaimRequestAsync(
            ISystemContext context, NodeId id, string operationId, string digest, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
            await ExpireTransfersAsync(ct).ConfigureAwait(false);
            lock (m_transportGate)
            {
                ct.ThrowIfCancellationRequested();
                Transfer upload = OwnedTransfer(context, id);
                if (!upload.Upload || !upload.Sealed || upload.Claimed)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The upload is unsealed or consumed.");
                }
                XRegistryRequest request;
                try
                {
                    request = m_codec.DecodeRequest(upload.Bytes, XRegistryBridgeNativeOptions.CreateContext(context));
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, exception.Message);
                }
                if (operationId != (request.OperationId ?? string.Empty) ||
                    digest !=
                        m_codec.ComputeRequestDigest(request))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "Operation identity or digest differs from the sealed request.");
                }
                upload.Claimed = true;
                return request;
            }
        }

        private async ValueTask<(NodeId File, uint Handle)> PrepareClaimedRequestAsync(
            ISystemContext context, NodeId id, XRegistryRequest request, CancellationToken ct)
        {
            XRegistryCallContext caller = await AuthorizeAsync(context, request.IsMutation, ct).ConfigureAwait(false);
            XRegistryEndpointDescription description = await InspectAsync(context, ct).ConfigureAwait(false);
            if (m_endpoint is not IXRegistryPreparedEndpoint endpoint ||
                !description.SupportsPreparedMutations ||
                (request.ExpectedVersionIncarnation is not null && !description.SupportsVersionIncarnationGuards) ||
                (request.ExpectedGeneration is not null && !description.SupportsGenerationGuards) ||
                (!string.IsNullOrEmpty(request.OperationId) && !description.SupportsOperationReplay))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The endpoint does not provide the requested preparation or replay guarantee.");
            }
            IXRegistryPreparedOperation operation = await PrepareEndpointAsync(
                endpoint, request with { Context = caller }, ct)
                .ConfigureAwait(false);
            operation = await PrepareProjectionAsync(operation, ct).ConfigureAwait(false);
            bool attached = false;
            NodeId preview = NodeId.Null;
            try
            {
                ByteString encoded = m_codec.EncodeResponse(operation.Response);
                (NodeId file, uint handle) = await AddTransferAsync(context, encoded, false, ct).ConfigureAwait(false);
                preview = file;
                lock (m_transportGate)
                {
                    ct.ThrowIfCancellationRequested();
                    Transfer upload = OwnedTransfer(context, id);
                    upload.Prepared = operation;
                    upload.PreparedMutation = request.IsMutation;
                    upload.PreparedAction = request.Action;
                    upload.PreparedPath = request.Path;
                    upload.PreviewId = file;
                    attached = true;
                }
                return (file, handle);
            }
            finally
            {
                if (!attached)
                {
                    try
                    {
                        if (!preview.IsNull)
                        {
                            await RemoveTransferAsync(preview, CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await ReleasePreparedAfterOperationAsync(operation).ConfigureAwait(false);
                    }
                }
            }
        }

        private async ValueTask<XRegistryResponse> CommitPreparedTransferAsync(
            ISystemContext context, NodeId id, CancellationToken ct)
        {
            _ = await AuthorizeAsync(context, false, ct).ConfigureAwait(false);
            await ExpireTransfersAsync(ct).ConfigureAwait(false);
            bool mutation;
            lock (m_transportGate)
            {
                ct.ThrowIfCancellationRequested();
                mutation = OwnedTransfer(context, id).PreparedMutation;
            }
            _ = await AuthorizeAsync(context, mutation, ct).ConfigureAwait(false);
            var deadline =
                new XRegistryOperationDeadline(m_options.TimeProvider, m_options.PreparedOperationTimeout, ct);
            CancellationToken token = deadline.Token;
            Task<XRegistryResponse> pending = CompletePreparedCommitAsync(context, id, mutation, deadline);
            try
            {
                return await pending.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _ = ObserveLateCommitAsync(pending);
                ct.ThrowIfCancellationRequested();
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "The prepared commit exceeded its deadline; its outcome must be reconciled, not retried.");
            }
        }

        private async Task<XRegistryResponse> CompletePreparedCommitAsync(
            ISystemContext context, NodeId id, bool mutation, XRegistryOperationDeadline deadline)
        {
            await using ConfiguredAsyncDisposable lifetime = deadline.ConfigureAwait(false);
            IXRegistryPreparedOperation operation;
            XRegistryAction action;
            string path;
            lock (m_transportGate)
            {
                deadline.Token.ThrowIfCancellationRequested();
                Transfer ticket = OwnedTransfer(context, id);
                operation = ticket.Prepared
                    ?? throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The prepared lease was consumed.");
                ticket.Prepared = null;
                action = ticket.PreparedAction;
                path = ticket.PreparedPath;
            }
            try
            {
                XRegistryResponse response = await CommitAndRefreshAsync(operation, mutation, deadline.Token)
                    .ConfigureAwait(false);
                AuditResult(context, action, path, response.StatusCode,
                    m_bridge?.CommitPreparedRequest?.NodeId ?? NodeId.Null);
                return response;
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception))
            {
                AuditResult(context, action, path, 0, m_bridge?.CommitPreparedRequest?.NodeId ?? NodeId.Null);
                throw;
            }
            finally
            {
                await ReleasePreparedAfterOperationAsync(operation).ConfigureAwait(false);
            }
        }

        private async ValueTask<XRegistryResponse> CommitAndRefreshAsync(
            IXRegistryPreparedOperation operation, bool mutation, CancellationToken ct)
        {
            if (operation is ProjectedPreparation projected)
            {
                return await CommitProjectedAsync(projected, ct).ConfigureAwait(false);
            }
            XRegistryResponse response = await operation.CommitAsync(ct).ConfigureAwait(false);
            if (mutation && response.IsSuccess && !m_transportClosed)
            {
                try
                {
                    await RefreshAsync(ct).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is ServiceResultException or IOException
                    or InvalidDataException or JsonException or OperationCanceledException or InvalidOperationException)
                {
                    Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().CommittedProjectionRefreshFailed(
                        exception);
                }
            }
            return response;
        }

        private async ValueTask<(NodeId File, uint Handle)> ExecuteTransferAsync(
            ISystemContext context, NodeId id, XRegistryRequest request, CancellationToken ct)
        {
            XRegistryEndpointDescription? description = request.IsMutation && m_endpoint is IXRegistryPreparedEndpoint
                ? await InspectAsync(context, ct).ConfigureAwait(false) : null;
            if (description is { SupportsPreparedMutations: true } &&
                (request.OperationId is null || description.SupportsOperationReplay))
            {
                (NodeId file, uint handle) = await PrepareClaimedRequestAsync(context, id, request,
                    ct).ConfigureAwait(false);
                XRegistryResponse result = await CommitPreparedTransferAsync(context, id, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    return (file, handle);
                }
                await RemoveTransferAsync(file, ct).ConfigureAwait(false);
                return await AddTransferAsync(context, m_codec.EncodeResponse(result), false, ct).ConfigureAwait(false);
            }
            XRegistryResponse response = await ExecuteAsync(context, request, ct).ConfigureAwait(false);
            return await AddTransferAsync(context, m_codec.EncodeResponse(response), false, ct).ConfigureAwait(false);
        }

        private async Task ReleasePreparedOnDisposalAsync(IXRegistryPreparedOperation operation)
        {
            try
            {
                await operation.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ServiceResultException
                or InvalidOperationException or OperationCanceledException)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().PreparedCleanupFailed(exception);
            }
        }
    }

    internal static partial class XRegistryBridgeNodeManagerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 1, Level = LogLevel.Warning,
            Message = "A prepared registry lease could not be released during bounded cleanup.")]
        public static partial void PreparedCleanupFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 2, Level = LogLevel.Error,
            Message = "Registry commit succeeded but native projection refresh failed; reads must refresh.")]
        public static partial void CommittedProjectionRefreshFailed(this ILogger logger, Exception exception);
    }
}
