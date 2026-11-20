/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// used to create the poll timer. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public PollingAliasNameRefreshStrategy(
            TimeSpan pollInterval,
            TimeProvider? timeProvider = null)
        {
            if (pollInterval < TimeSpan.FromMilliseconds(100))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pollInterval),
                    "Poll interval must be at least 100 ms.");
            }
            m_timeProvider = timeProvider ?? TimeProvider.System;
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

            m_timer = m_timeProvider.CreateTimer(
                Tick,
                state: null,
                dueTime: PollInterval,
                period: PollInterval);
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            ITimer? timer = m_timer;
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
        private ITimer? m_timer;
        private uint? m_lastSeen;
        private readonly TimeProvider m_timeProvider;
    }
}
