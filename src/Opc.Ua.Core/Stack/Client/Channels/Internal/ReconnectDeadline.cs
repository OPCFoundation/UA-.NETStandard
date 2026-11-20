/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Owns one monotonic reconnect deadline, which coalesced callers can only shorten.
    /// </summary>
    internal sealed class ReconnectDeadline : IAsyncDisposable
    {
        /// <summary>
        /// Starts an unbounded recovery window linked to manager shutdown.
        /// </summary>
        public ReconnectDeadline(TimeProvider timeProvider, CancellationToken shutdown)
        {
            m_timeProvider = timeProvider;
            m_startedAt = timeProvider.GetTimestamp();
            m_source = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            Token = m_source.Token;
        }

        /// <summary>
        /// Cancellation shared by the cycle's recovery operations.
        /// </summary>
        public CancellationToken Token { get; }

        /// <summary>
        /// Monotonic time elapsed since this recovery cycle started.
        /// </summary>
        public TimeSpan Elapsed => m_timeProvider.GetElapsedTime(m_startedAt);

        /// <summary>
        /// Remaining time for callback-dependent work deferred to an outer recovery owner.
        /// The absolute bound remains meaningful after the channel handshake retires its timer.
        /// </summary>
        internal TimeSpan Remaining
        {
            get
            {
                lock (m_lock)
                {
                    if (m_duration == TimeSpan.MaxValue)
                    {
                        return TimeSpan.MaxValue;
                    }
                    TimeSpan remaining = m_duration - Elapsed;
                    return m_expired || remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
                }
            }
        }

        /// <summary>
        /// Whether the recovery deadline expired, rather than being cancelled by closing or shutdown.
        /// </summary>
        public bool Expired
        {
            get
            {
                lock (m_lock)
                {
                    return m_expired;
                }
            }
        }

        /// <summary>
        /// The tightest attached recovery duration, or <see cref="TimeSpan.MaxValue"/> when unbounded.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                lock (m_lock)
                {
                    return m_duration;
                }
            }
        }

        /// <summary>
        /// Rejects work after expiry or cancellation, including before asynchronous token cancellation finishes.
        /// </summary>
        public void ThrowIfCancellationRequested()
        {
            lock (m_lock)
            {
                if (m_expired || m_cancelled)
                {
                    throw new OperationCanceledException(Token);
                }
            }
            Token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Attaches a caller's remaining budget only when it shortens the existing deadline.
        /// </summary>
        public void Tighten(IRetryBudget? budget)
        {
            if (budget == null)
            {
                return;
            }
            if (!budget.TryConsume(out TimeSpan remaining))
            {
                remaining = TimeSpan.Zero;
            }
            if (remaining == TimeSpan.MaxValue)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_completed || m_expired)
                {
                    return;
                }
                TimeSpan elapsed = Elapsed;
                TimeSpan duration = remaining >= TimeSpan.MaxValue - elapsed
                    ? TimeSpan.MaxValue
                    : elapsed + remaining;
                if (duration >= m_duration)
                {
                    return;
                }
                m_duration = duration;
                ArmTimerUnderLock();
            }
        }

        /// <summary>
        /// Arbitrates completion against the timer so a successful cycle cannot subsequently expire.
        /// </summary>
        public bool TryComplete()
        {
            lock (m_lock)
            {
                if (!m_expired && !m_completed && Elapsed >= m_duration)
                {
                    ExpireUnderLock();
                }
                if (m_expired || m_cancelled || Token.IsCancellationRequested)
                {
                    return false;
                }
                m_completed = true;
                m_timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return true;
            }
        }

        /// <summary>
        /// Stops active recovery without classifying the cancellation as deadline expiry.
        /// </summary>
        public void Cancel()
        {
            lock (m_lock)
            {
                // check the own expiry flag too: on targets where CancelAsync cannot set the
                // token synchronously the expiry's cancellation may still be pending, and
                // overwriting m_cancellation would stop DisposeAsync awaiting it.
                if (!m_completed && !m_expired && !Token.IsCancellationRequested)
                {
                    m_cancelled = true;
                    m_completed = true;
                    m_timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    m_cancellation = m_source.CancelAsync();
                }
            }
        }

        /// <summary>
        /// Retires the deadline timer and awaits any scheduled cancellation callbacks.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Task cancellation;
            lock (m_lock)
            {
                m_completed = true;
                m_timer?.Dispose();
                cancellation = m_cancellation;
            }
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            finally
            {
                m_source.Dispose();
            }
        }

        private void OnTimer()
        {
            lock (m_lock)
            {
                if (m_completed || m_expired)
                {
                    return;
                }
                if (Elapsed < m_duration)
                {
                    ArmTimerUnderLock();
                    return;
                }
                ExpireUnderLock();
            }
        }

        private void ArmTimerUnderLock()
        {
            TimeSpan remaining = m_duration - Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                ExpireUnderLock();
                return;
            }
            TimeSpan dueTime = remaining < s_maxTimerDuration ? remaining : s_maxTimerDuration;
            if (m_timer == null)
            {
                m_timer = m_timeProvider.CreateTimer(
                    static state => ((ReconnectDeadline)state!).OnTimer(),
                    this,
                    dueTime,
                    Timeout.InfiniteTimeSpan);
            }
            else
            {
                m_timer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
        }

        private void ExpireUnderLock()
        {
            m_expired = true;
            // Never run user cancellation callbacks under the deadline lock or on a fake clock's Advance stack.
            m_cancellation = m_source.CancelAsync();
        }

        private static readonly TimeSpan s_maxTimerDuration = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        private readonly Lock m_lock = new();
        private readonly TimeProvider m_timeProvider;
        private readonly long m_startedAt;
        private readonly CancellationTokenSource m_source;
        private ITimer? m_timer;
        private TimeSpan m_duration = TimeSpan.MaxValue;
        private Task m_cancellation = Task.CompletedTask;
        private bool m_expired;
        private bool m_completed;
        private bool m_cancelled;
    }
}
