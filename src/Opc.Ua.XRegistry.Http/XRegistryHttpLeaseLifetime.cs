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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    internal sealed class XRegistryHttpLeaseLifetime(IAsyncDisposable lease, TimeSpan timeout, ILogger logger)
        : IAsyncDisposable
    {
        public void Retain(IAsyncDisposable preparation)
        {
            preparation.ThrowIfNull(nameof(preparation));
            if (m_preparation is not null || Volatile.Read(ref m_disposed) != 0)
            {
                throw new InvalidOperationException("The caller lifetime already owns a preparation or is closing.");
            }
            m_preparation = preparation;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            Task cleanup = ReleaseAsync();
            using var deadline = new CancellationTokenSource(timeout);
            try
            {
                await cleanup.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                logger.CallerLeaseCleanupDeferred();
                // ReleaseAsync retains the lease; the timeout source is not used by the pending cleanup.
                // TODO: Remove when CA2025 distinguishes observer tasks from the expired wait token.
#pragma warning disable CA2025
                _ = ObserveLateAsync(cleanup);
#pragma warning restore CA2025
            }
        }

        private async Task ReleaseAsync()
        {
            try
            {
                try
                {
                    if (m_preparation is { } preparation)
                    {
                        await preparation.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (IsCleanupFailure(exception))
            {
                logger.CallerLeaseCleanupFailed(exception);
            }
        }

        private async Task ObserveLateAsync(Task cleanup)
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            catch (Exception exception) when (IsCleanupFailure(exception))
            {
                logger.CallerLeaseCleanupFailed(exception);
            }
        }

        private static bool IsCleanupFailure(Exception exception) =>
            exception is IOException or InvalidOperationException or OperationCanceledException
                or ServiceResultException or HttpRequestException or UnauthorizedAccessException;

        private int m_disposed;
        private IAsyncDisposable? m_preparation;
    }

    internal static partial class XRegistryHttpLeaseLifetimeLog
    {
        [LoggerMessage(EventId = XRegistryHttpEventIds.LeaseLifetime, Level = LogLevel.Error,
            Message = "xRegistry caller lease cleanup failed; the operation outcome is not rewritten.")]
        public static partial void CallerLeaseCleanupFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryHttpEventIds.LeaseLifetime + 1, Level = LogLevel.Warning,
            Message = "xRegistry caller lease cleanup exceeded its deadline; late cleanup remains observed.")]
        public static partial void CallerLeaseCleanupDeferred(this ILogger logger);
    }
}
#endif
