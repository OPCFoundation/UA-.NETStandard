/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Client.AliasNames.Refresh
{
    /// <summary>
    /// Refresh strategy that periodically reads the category's
    /// <c>LastChange</c> property (Part 17 §6.3.1) via the standard
    /// <c>Read</c> service and invalidates the resolver cache when the
    /// value changes (wrap-safe — compares for inequality, not strict
    /// greater-than).
    /// </summary>
    /// <remarks>
    /// Suitable for any server that exposes <c>LastChange</c> — including
    /// servers that disable subscriptions. Pollintroduces one
    /// <c>Read</c> service round-trip per <see cref="PollInterval"/>; use
    /// <see cref="MonitoredItemAliasNameRefreshStrategy"/> instead when
    /// the server supports subscriptions and a finer push notification
    /// is preferable.
    /// </remarks>
    public sealed class PollingAliasNameRefreshStrategy : IAliasNameRefreshStrategy
    {
        /// <summary>
        /// Initializes a new poller with the supplied interval.
        /// </summary>
        /// <param name="pollInterval">Time between successive
        /// <c>LastChange</c> reads. Must be at least 100 ms.</param>
        public PollingAliasNameRefreshStrategy(TimeSpan pollInterval)
        {
            if (pollInterval < TimeSpan.FromMilliseconds(100))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollInterval),
                    "Poll interval must be at least 100 ms.");
            }
            PollInterval = pollInterval;
        }

        /// <summary>
        /// The polling interval.
        /// </summary>
        public TimeSpan PollInterval { get; }

        /// <inheritdoc/>
        public ValueTask StartAsync(
            AliasNameClient client,
            Action onInvalidate,
            CancellationToken ct)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            m_onInvalidate = onInvalidate ?? throw new ArgumentNullException(nameof(onInvalidate));

            int periodMs = (int)PollInterval.TotalMilliseconds;
            m_timer = new Timer(
                Tick,
                state: null,
                dueTime: periodMs,
                period: periodMs);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Timer? timer = m_timer;
            m_timer = null;
            timer?.Dispose();
            return default;
        }

        private void Tick(object? _)
        {
            // Fire-and-forget; a server hiccup must not crash the
            // timer thread.
            _ = TickAsync();
        }

        private async Task TickAsync()
        {
            AliasNameClient? client = m_client;
            Action? invalidate = m_onInvalidate;
            if (client == null || invalidate == null)
            {
                return;
            }
            try
            {
                uint? current = await client.ReadLastChangeAsync().ConfigureAwait(false);
                if (current == null)
                {
                    return;
                }
                uint? last = m_lastSeen;
                // Wrap-safe — VersionTime is uint and may wrap; any
                // difference (including wraparound) is a change.
                if (last != current)
                {
                    m_lastSeen = current;
                    invalidate();
                }
            }
            catch
            {
                // Transient errors are swallowed; the next tick retries.
            }
        }

        private AliasNameClient? m_client;
        private Action? m_onInvalidate;
        private Timer? m_timer;
        private uint? m_lastSeen;
    }
}
