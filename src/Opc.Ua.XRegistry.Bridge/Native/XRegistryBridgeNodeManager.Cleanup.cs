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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Native
{
    public sealed partial class XRegistryBridgeNodeManager
    {
        private async ValueTask<IXRegistryPreparedOperation> PrepareEndpointAsync(
            IXRegistryPreparedEndpoint endpoint, XRegistryRequest request, CancellationToken ct)
        {
            var deadline =
                new XRegistryOperationDeadline(m_options.TimeProvider, m_options.PreparedOperationTimeout, ct);
            CancellationToken token = deadline.Token;
            Task<IXRegistryPreparedOperation> pending = PrepareAndReleaseDeadlineAsync(endpoint, request, deadline);
            try
            {
                return await pending.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _ = ReleaseLatePreparationAsync(pending);
                ct.ThrowIfCancellationRequested();
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "The provider did not prepare the operation before its deadline; any late lease will be aborted.");
            }
        }

        private static async Task<IXRegistryPreparedOperation> PrepareAndReleaseDeadlineAsync(
            IXRegistryPreparedEndpoint endpoint, XRegistryRequest request, XRegistryOperationDeadline deadline)
        {
            await using ConfiguredAsyncDisposable lifetime = deadline.ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return await endpoint.PrepareAsync(request, deadline.Token).ConfigureAwait(false);
        }

        private async ValueTask AwaitTransferCleanupAsync(Func<Task> cleanup, CancellationToken ct)
        {
            XRegistryOperationDeadline deadline;
            bool initialized = false;
            try
            {
                deadline = new XRegistryOperationDeadline(m_options.TimeProvider, m_options.CleanupTimeout, ct);
                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    _ = ObserveLateCleanupAsync(InvokeCleanupAsync(cleanup));
                }
            }
            CancellationToken token = deadline.Token;
            Task pending = CompleteCleanupAsync(cleanup, deadline);
            try
            {
                await pending.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                _ = ObserveLateCleanupAsync(pending);
                ct.ThrowIfCancellationRequested();
                throw new ServiceResultException(StatusCodes.BadTimeout,
                    "Transfer ownership was revoked; cleanup continues after its deadline.");
            }
        }

        private static async Task InvokeCleanupAsync(Func<Task> cleanup)
        {
            await cleanup().ConfigureAwait(false);
        }

        private static async Task CompleteCleanupAsync(Func<Task> cleanup, XRegistryOperationDeadline deadline)
        {
            await using ConfiguredAsyncDisposable lifetime = deadline.ConfigureAwait(false);
            await cleanup().ConfigureAwait(false);
        }

        private async ValueTask ReleasePreparedAfterOperationAsync(IXRegistryPreparedOperation operation)
        {
            try
            {
                await AwaitTransferCleanupAsync(() => operation.DisposeAsync().AsTask(), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().PreparedCleanupFailed(exception);
            }
        }

        private async Task ReleaseLatePreparationAsync(Task<IXRegistryPreparedOperation> pending)
        {
            try
            {
                IXRegistryPreparedOperation operation = await pending.ConfigureAwait(false);
                await ReleasePreparedAfterOperationAsync(operation).ConfigureAwait(false);
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().PreparedCleanupFailed(exception);
            }
        }

        private async Task ObserveLateCommitAsync(Task<XRegistryResponse> pending)
        {
            try
            {
                XRegistryResponse response = await pending.ConfigureAwait(false);
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().LatePreparedCommit(response.StatusCode);
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().LatePreparedCommitFailed(exception);
            }
        }

        private async Task ObserveLateCleanupAsync(Task pending)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception exception) when (XRegistryOperationDeadline.IsEndpointFailure(exception) ||
                exception is InvalidOperationException or ArgumentException)
            {
                Server.Telemetry.CreateLogger<XRegistryBridgeNodeManager>().PreparedCleanupFailed(exception);
            }
        }
    }

    internal static partial class XRegistryBridgeNodeManagerLog
    {
        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 3, Level = LogLevel.Warning,
            Message = "A timed-out prepared registry operation completed later with status {StatusCode}.")]
        public static partial void LatePreparedCommit(this ILogger logger, int statusCode);

        [LoggerMessage(EventId = XRegistryBridgeNativeEventIds.TransferCleanupFailed + 4, Level = LogLevel.Error,
            Message = "A timed-out prepared registry operation failed after the caller stopped waiting.")]
        public static partial void LatePreparedCommitFailed(this ILogger logger, Exception exception);
    }
}
