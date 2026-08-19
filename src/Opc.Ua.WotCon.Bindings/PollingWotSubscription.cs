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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// A reusable observe / event subscription that periodically polls a source
    /// on a bounded interval and stops cleanly when disposed. It is used by
    /// executors whose transports have no native push channel (for example HTTP
    /// polling or Modbus polling).
    /// <para>
    /// A poll reports whether it was healthy. Consecutive unhealthy polls back the loop off
    /// through an <see cref="IChannelReconnectPolicy"/> — the same policy abstraction the stack
    /// already uses for channel reconnects — so an asset that has gone offline is not hammered
    /// once per poll interval. The backoff never polls faster than the configured interval, and
    /// the first healthy poll resets it.
    /// </para>
    /// </summary>
    public sealed class PollingWotSubscription : IWotSubscription
    {
        /// <summary>
        /// Initializes and starts a new polling subscription.
        /// </summary>
        /// <param name="form">The compiled form being observed.</param>
        /// <param name="pollAsync">
        /// The poll callback, invoked once per interval. It returns <c>true</c> when the poll was
        /// healthy and <c>false</c> when the source reported a failure without throwing — a
        /// protocol binding that maps a failure onto a bad <see cref="StatusCode"/> rather than an
        /// exception must return <c>false</c> so the retry policy engages.
        /// </param>
        /// <param name="interval">The bounded poll interval.</param>
        /// <param name="onError">
        /// An optional handler invoked when a single poll iteration faults with a
        /// non-cancellation exception. A transient poll or callback fault is
        /// reported here and the loop keeps polling; it never permanently faults
        /// the subscription. A <c>null</c> handler silently continues.
        /// </param>
        /// <param name="retryPolicy">
        /// The backoff applied after consecutive unhealthy polls. Defaults to
        /// <see cref="ExponentialBackoffChannelReconnectPolicy"/>. A policy that reports "stop
        /// retrying" (a negative delay) ends the poll loop.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used for best-effort disposal diagnostics.
        /// </param>
        public PollingWotSubscription(
            WotCompiledForm form,
            Func<CancellationToken, ValueTask<bool>> pollAsync,
            TimeSpan interval,
            Action<Exception>? onError = null,
            IChannelReconnectPolicy? retryPolicy = null,
            ITelemetryContext? telemetry = null)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            m_pollAsync = pollAsync ?? throw new ArgumentNullException(nameof(pollAsync));
            m_interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
            m_onError = onError;
            m_retryPolicy = retryPolicy ?? new ExponentialBackoffChannelReconnectPolicy();
            m_logger = telemetry.CreateLogger<PollingWotSubscription>();
            m_loop = RunAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public WotCompiledForm Form { get; }

        /// <summary>
        /// Gets the number of consecutive unhealthy polls. Zero while the source is healthy.
        /// </summary>
        public int ConsecutiveFailures => Volatile.Read(ref m_consecutiveFailures);

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            // Dispose the cancellation source in a finally so a faulted loop can
            // never leak it. The loop is designed not to fault (transient errors
            // are handled per iteration), but awaiting it is still guarded so a
            // residual exception is never rethrown from DisposeAsync.
            try
            {
                m_cts.Cancel();
                try
                {
                    await m_loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on cancellation.
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    m_logger.IgnoringResidualLoopExceptionDuringDispose(ex);
                }
            }
            finally
            {
                m_cts.Dispose();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool healthy;
                try
                {
                    healthy = await m_pollAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cooperative cancellation is never swallowed as an error;
                    // stop the loop cleanly.
                    return;
                }
                catch (Exception ex)
                {
                    // A transient poll or callback fault must not permanently
                    // fault the loop: report it and back off before the next
                    // attempt. This includes a spurious OperationCanceledException
                    // that is not our own cancellation (for example a transport
                    // timeout surfaced as a cancellation).
                    ReportError(ex);
                    healthy = false;
                }

                TimeSpan delay;
                if (healthy)
                {
                    Volatile.Write(ref m_consecutiveFailures, 0);
                    delay = m_interval;
                }
                else
                {
                    int attempt = Volatile.Read(ref m_consecutiveFailures) + 1;
                    Volatile.Write(ref m_consecutiveFailures, attempt);
                    delay = m_retryPolicy.GetDelay(attempt);
                    if (delay < TimeSpan.Zero)
                    {
                        // The policy has given up. Stop polling rather than spin;
                        // the last reported bad status stays on the variable.
                        return;
                    }
                    if (delay < m_interval)
                    {
                        // Backing off must never poll faster than the configured interval.
                        delay = m_interval;
                    }
                }

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        private void ReportError(Exception ex)
        {
            try
            {
                m_onError?.Invoke(ex);
            }
            catch
            {
                // An error handler must never take down the poll loop.
            }
        }

        private readonly Func<CancellationToken, ValueTask<bool>> m_pollAsync;
        private readonly TimeSpan m_interval;
        private readonly Action<Exception>? m_onError;
        private readonly IChannelReconnectPolicy m_retryPolicy;
        private readonly ILogger m_logger;
        private readonly CancellationTokenSource m_cts = new();
        private readonly Task m_loop;
        private int m_consecutiveFailures;
    }

    internal static partial class PollingWotSubscriptionLog
    {
        [LoggerMessage(
            EventId = WotConBindingsEventIds.PollingWotSubscription + 0,
            Level = LogLevel.Warning,
            Message = "Ignoring residual polling loop exception during disposal.")]
        public static partial void IgnoringResidualLoopExceptionDuringDispose(
            this ILogger logger, Exception exception);
    }
}
