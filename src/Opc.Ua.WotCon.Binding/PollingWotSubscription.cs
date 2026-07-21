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

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// A reusable observe / event subscription that periodically polls a source
    /// on a bounded interval and stops cleanly when disposed. It is used by
    /// executors whose transports have no native push channel (for example HTTP
    /// polling or Modbus polling).
    /// </summary>
    public sealed class PollingWotSubscription : IWotSubscription
    {
        /// <summary>Initializes and starts a new polling subscription.</summary>
        /// <param name="form">The compiled form being observed.</param>
        /// <param name="pollAsync">The poll callback, invoked once per interval.</param>
        /// <param name="interval">The bounded poll interval.</param>
        /// <param name="onError">
        /// An optional handler invoked when a single poll iteration faults with a
        /// non-cancellation exception. A transient poll or callback fault is
        /// reported here and the loop keeps polling; it never permanently faults
        /// the subscription. A <c>null</c> handler silently continues.
        /// </param>
        public PollingWotSubscription(
            WotCompiledForm form,
            Func<CancellationToken, ValueTask> pollAsync,
            TimeSpan interval,
            Action<Exception>? onError = null)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            m_pollAsync = pollAsync ?? throw new ArgumentNullException(nameof(pollAsync));
            m_interval = interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;
            m_onError = onError;
            m_loop = RunAsync(m_cts.Token);
        }

        /// <inheritdoc/>
        public WotCompiledForm Form { get; }

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
                try
                {
                    await m_pollAsync(cancellationToken).ConfigureAwait(false);
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
                    // fault the loop: report it and keep polling on the next
                    // interval. This includes a spurious OperationCanceledException
                    // that is not our own cancellation (for example a transport
                    // timeout surfaced as a cancellation).
                    ReportError(ex);
                }

                try
                {
                    await Task.Delay(m_interval, cancellationToken).ConfigureAwait(false);
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

        private readonly Func<CancellationToken, ValueTask> m_pollAsync;
        private readonly TimeSpan m_interval;
        private readonly Action<Exception>? m_onError;
        private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
        private readonly Task m_loop;
    }
}
