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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: lease-based <see cref="ILeaderElection"/> over an
    /// <see cref="ISharedKeyValueStore"/>. A single lease key holds the
    /// current leader's id and an expiry; leadership is acquired and renewed
    /// atomically with <see cref="ISharedKeyValueStore.CompareAndSwapAsync"/>
    /// ("shared read, master write"). A leader that stops renewing loses the
    /// lease once it expires, allowing a standby to take over.
    /// </summary>
    public sealed class SharedStoreLeaseElection : ILeaderElection
    {
        /// <summary>
        /// Creates a lease election.
        /// </summary>
        /// <param name="store">The shared key/value backend.</param>
        /// <param name="leaseKey">The key holding the lease.</param>
        /// <param name="nodeId">This replica's unique identity.</param>
        /// <param name="leaseDuration">
        /// How long an acquired lease remains valid without renewal.
        /// </param>
        /// <param name="renewInterval">
        /// How often the background loop renews the lease.
        /// </param>
        /// <param name="timeProvider">Time source (defaults to system).</param>
        /// <param name="logger">Optional logger.</param>
        public SharedStoreLeaseElection(
            ISharedKeyValueStore store,
            string leaseKey,
            string nodeId,
            TimeSpan leaseDuration,
            TimeSpan renewInterval,
            TimeProvider? timeProvider = null,
            ILogger? logger = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrEmpty(leaseKey))
            {
                throw new ArgumentException("Lease key must not be null or empty.", nameof(leaseKey));
            }
            if (string.IsNullOrEmpty(nodeId))
            {
                throw new ArgumentException("Node id must not be null or empty.", nameof(nodeId));
            }
            m_leaseKey = leaseKey;
            m_nodeId = nodeId;
            m_leaseDuration = leaseDuration;
            m_renewInterval = renewInterval;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = logger;
            m_expiryTimer = m_timeProvider.CreateTimer(
                _ => OnLeaseExpiry(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <inheritdoc/>
        public bool IsLeader
        {
            get
            {
                bool isLeader;
                lock (m_lock)
                {
                    ExpireLeaseIfNeeded();
                    isLeader = m_isLeader;
                }
                DispatchNotifications();
                return isLeader;
            }
        }

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public async ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
        {
            long attempt;
            lock (m_lock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedStoreLeaseElection));
                }
                ExpireLeaseIfNeeded();
                attempt = ++m_attempt;
            }
            DispatchNotifications();

            (bool found, ByteString current) = await m_store.TryGetAsync(m_leaseKey, ct).ConfigureAwait(false);
            if (!IsCurrentAttempt(attempt))
            {
                return false;
            }
            long timestamp = m_timeProvider.GetTimestamp();
            long nowTicks = m_timeProvider.GetUtcNow().UtcTicks;

            bool canTake = !found;
            if (found)
            {
                canTake = !TryParseLease(current, out string owner, out long expiryTicks) ||
                    nowTicks >= expiryTicks ||
                    string.Equals(owner, m_nodeId, StringComparison.Ordinal);
            }

            if (!canTake)
            {
                return CompleteAttempt(attempt, false, 0, 0);
            }

            long newExpiryTicks = nowTicks + m_leaseDuration.Ticks;
            ByteString newLease = EncodeLease(m_nodeId, newExpiryTicks);
            ByteString expected = found ? current : default;
            bool acquired = await m_store
                .CompareAndSwapAsync(m_leaseKey, expected, newLease, ct)
                .ConfigureAwait(false);
            return CompleteAttempt(attempt, acquired, timestamp, newExpiryTicks);
        }

        /// <inheritdoc/>
        public void Start()
        {
            lock (m_lock)
            {
                if (m_started)
                {
                    return;
                }
                m_started = true;
                m_loop = Task.Run(() => RenewLoopAsync(m_cts.Token));
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_lock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                ++m_attempt;
                if (m_isLeader)
                {
                    m_pendingNotifications.Enqueue(false);
                }
                m_isLeader = false;
                m_expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            DispatchNotifications();

            m_cts.Cancel();
            await m_expiryTimer.DisposeAsync().ConfigureAwait(false);
            if (m_loop != null)
            {
                try
                {
                    await m_loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            await ReleaseIfOwnedAsync().ConfigureAwait(false);
            m_cts.Dispose();
        }

        private async Task RenewLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await TryAcquireOrRenewAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger?.SharedStoreLeaseElectionLogMessage0(ex, m_nodeId);

                        OnLeaseExpiry();
                    }

                    await m_timeProvider.Delay(m_renewInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async Task ReleaseIfOwnedAsync()
        {
            try
            {
                (bool found, ByteString current) = await m_store
                    .TryGetAsync(m_leaseKey, CancellationToken.None)
                    .ConfigureAwait(false);
                if (found &&
                    TryParseLease(current, out string owner, out _) &&
                    string.Equals(owner, m_nodeId, StringComparison.Ordinal))
                {
                    await m_store.DeleteAsync(m_leaseKey, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger?.SharedStoreLeaseElectionLogMessage1(ex, m_nodeId);
            }
        }

        private bool IsCurrentAttempt(long attempt)
        {
            bool current;
            lock (m_lock)
            {
                ExpireLeaseIfNeeded();
                current = !m_disposed && attempt == m_attempt;
            }
            DispatchNotifications();
            return current;
        }

        private bool CompleteAttempt(long attempt, bool acquired, long timestamp, long expiryTicks)
        {
            bool confirmed = false;
            lock (m_lock)
            {
                ExpireLeaseIfNeeded();
                if (!m_disposed && attempt == m_attempt)
                {
                    TimeSpan remaining = acquired
                        ? GetRemainingLeaseTime(timestamp, expiryTicks)
                        : TimeSpan.Zero;
                    confirmed = acquired && remaining > TimeSpan.Zero;
                    if (m_isLeader != confirmed)
                    {
                        m_pendingNotifications.Enqueue(confirmed);
                    }
                    m_isLeader = confirmed;
                    if (confirmed)
                    {
                        m_confirmedTimestamp = timestamp;
                        m_confirmedExpiryTicks = expiryTicks;
                        m_expiryTimer.Change(remaining, Timeout.InfiniteTimeSpan);
                    }
                    else
                    {
                        ++m_attempt;
                        m_expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    }
                }
            }
            DispatchNotifications();
            return confirmed;
        }

        private void OnLeaseExpiry()
        {
            try
            {
                lock (m_lock)
                {
                    ExpireLeaseIfNeeded();
                    if (m_isLeader)
                    {
                        m_expiryTimer.Change(
                            GetRemainingLeaseTime(m_confirmedTimestamp, m_confirmedExpiryTicks),
                            Timeout.InfiniteTimeSpan);
                    }
                }
                DispatchNotifications();
            }
            catch (Exception ex)
            {
                m_logger?.SharedStoreLeaseElectionLogMessage2(ex, m_nodeId);
            }
        }

        private void ExpireLeaseIfNeeded()
        {
            if (!m_isLeader ||
                GetRemainingLeaseTime(m_confirmedTimestamp, m_confirmedExpiryTicks) > TimeSpan.Zero)
            {
                return;
            }

            m_isLeader = false;
            m_pendingNotifications.Enqueue(false);
            // Replies belonging to the expired authority cannot establish a new lease.
            ++m_attempt;
            m_expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        private TimeSpan GetRemainingLeaseTime(long timestamp, long expiryTicks)
        {
            TimeSpan utcRemaining = TimeSpan.FromTicks(expiryTicks - m_timeProvider.GetUtcNow().UtcTicks);
            TimeSpan elapsedRemaining = m_leaseDuration - m_timeProvider.GetElapsedTime(timestamp);
            return utcRemaining < elapsedRemaining ? utcRemaining : elapsedRemaining;
        }

        /// <summary>
        /// Delivers queued <see cref="LeadershipChanged"/> notifications in the
        /// order the transitions were made.
        /// </summary>
        /// <remarks>
        /// The renew loop and the expiry timer both make transitions, so two can
        /// race. Each is queued under the state lock as it is made, and one
        /// caller at a time drains the queue outside that lock: handlers run
        /// without holding it, and a handler that itself causes a transition
        /// queues it for after its own notification rather than being called
        /// back re-entrantly with the newer value first.
        /// </remarks>
        private void DispatchNotifications()
        {
            lock (m_lock)
            {
                if (m_notifying || m_pendingNotifications.Count == 0)
                {
                    return;
                }

                m_notifying = true;
            }

            ExceptionDispatchInfo? firstFailure = null;

            while (true)
            {
                bool value;

                lock (m_lock)
                {
                    if (m_pendingNotifications.Count == 0)
                    {
                        m_notifying = false;
                        break;
                    }

                    value = m_pendingNotifications.Dequeue();
                }

                try
                {
                    LeadershipChanged?.Invoke(value);
                }
                catch (Exception ex)
                {
                    // Keep draining: a handler that throws must not strand the
                    // notifications queued behind it. The first failure still
                    // reaches the caller that made the transition.
                    firstFailure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            firstFailure?.Throw();
        }

        private static ByteString EncodeLease(string owner, long expiryUtcTicks)
        {
            byte[] ownerBytes = Encoding.UTF8.GetBytes(owner);
            byte[] buffer = new byte[4 + ownerBytes.Length + 8];
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), ownerBytes.Length);
            ownerBytes.CopyTo(buffer, 4);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(4 + ownerBytes.Length, 8), expiryUtcTicks);
            return new ByteString(buffer);
        }

        private static bool TryParseLease(ByteString raw, out string owner, out long expiryUtcTicks)
        {
            owner = string.Empty;
            expiryUtcTicks = 0;
            byte[] bytes = raw.ToArray();
            if (bytes.Length < 4)
            {
                return false;
            }
            int ownerLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            if (ownerLength < 0 || bytes.Length < 4 + ownerLength + 8)
            {
                return false;
            }
            owner = Encoding.UTF8.GetString(bytes, 4, ownerLength);
            expiryUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(4 + ownerLength, 8));
            return true;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly string m_leaseKey;
        private readonly string m_nodeId;
        private readonly TimeSpan m_leaseDuration;
        private readonly TimeSpan m_renewInterval;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger? m_logger;
        private readonly ITimer m_expiryTimer;
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_loop;
        private long m_attempt;
        private long m_confirmedTimestamp;
        private long m_confirmedExpiryTicks;
        private bool m_isLeader;
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Leadership notifications not yet delivered, in transition order.
        /// Guarded by <see cref="m_lock"/>.
        /// </summary>
        private readonly Queue<bool> m_pendingNotifications = new();

        /// <summary>
        /// Whether a caller is draining <see cref="m_pendingNotifications"/>.
        /// Guarded by <see cref="m_lock"/>.
        /// </summary>
        private bool m_notifying;
    }

    /// <summary>
    /// Source-generated log messages for SharedStoreLeaseElection.
    /// </summary>
    internal static partial class SharedStoreLeaseElectionLog
    {

        [LoggerMessage(EventId = CoreEventIds.SharedStoreLeaseElection + 0, Level = LogLevel.Error,
            Message = "Lease election renew failed for {NodeId}.")]
        public static partial void SharedStoreLeaseElectionLogMessage0(
            this ILogger logger,
            global::System.Exception? exception,
            string nodeId);

        [LoggerMessage(EventId = CoreEventIds.SharedStoreLeaseElection + 1, Level = LogLevel.Error,
            Message = "Lease election release failed for {NodeId}.")]
        public static partial void SharedStoreLeaseElectionLogMessage1(
            this ILogger logger,
            global::System.Exception? exception,
            string nodeId);

        [LoggerMessage(EventId = CoreEventIds.SharedStoreLeaseElection + 2, Level = LogLevel.Error,
            Message = "Lease election step-down failed for {NodeId}; a LeadershipChanged handler threw.")]
        public static partial void SharedStoreLeaseElectionLogMessage2(
            this ILogger logger,
            global::System.Exception? exception,
            string nodeId);
    }

}
