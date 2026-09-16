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
        }

        /// <inheritdoc/>
        public bool IsLeader
        {
            get
            {
                // The lease this replica last wrote has to still be running for
                // it to count as the leader. Checked on read, not only where a
                // renew fails: a store that hangs rather than throws never
                // reaches the failure path at all, and every other replica
                // watches the lease expire while this one keeps claiming it.
                lock (m_lock)
                {
                    return m_isLeader &&
                        m_timeProvider.GetUtcNow().UtcTicks < m_localLeaseExpiryTicks;
                }
            }
        }

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public async ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
        {
            (bool found, ByteString current) = await m_store.TryGetAsync(m_leaseKey, ct).ConfigureAwait(false);
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
                SetLeader(false);
                return false;
            }

            long expiry = nowTicks + m_leaseDuration.Ticks;
            ByteString newLease = EncodeLease(m_nodeId, expiry);
            ByteString expected = found ? current : default;
            bool acquired = await m_store
                .CompareAndSwapAsync(m_leaseKey, expected, newLease, ct)
                .ConfigureAwait(false);

            // The expiry was computed before the store call. A call that took
            // longer than the lease duration wrote a lease that has already run
            // out, and another replica may have taken over in the meantime - so
            // this is not leadership, however the swap went.
            if (acquired && m_timeProvider.GetUtcNow().UtcTicks >= expiry)
            {
                SetLeader(false);
                return false;
            }

            SetLeader(acquired, expiry);
            return acquired;
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

                // Runs independently of the renew loop. A store that hangs
                // rather than throws never lets that loop come round to its
                // failure path, so the step-down has to be driven from
                // somewhere the store cannot block - otherwise a replica keeps
                // announcing itself as leader on a lease every other replica
                // has already watched expire.
                m_watchdog = m_timeProvider.CreateTimer(
                    _ => StepDownIfLeaseExpiredSafely(),
                    null,
                    m_renewInterval,
                    m_renewInterval);
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
            }

            ITimer? watchdog;
            lock (m_lock)
            {
                watchdog = m_watchdog;
                m_watchdog = null;
            }

            if (watchdog != null)
            {
                await watchdog.DisposeAsync().ConfigureAwait(false);
            }

            m_cts.Cancel();
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

                        // A renew that never reached the store leaves the shared
                        // lease to expire on its own, and a standby takes over
                        // once it does. Step down at the same moment rather than
                        // keep leading on a lease that is no longer held, which
                        // would put two leaders on the line.
                        StepDownIfLeaseExpiredSafely();
                    }

                    await Task.Delay(m_renewInterval, ct).ConfigureAwait(false);
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

        /// <summary>
        /// Gives up leadership once the lease last written to the store has
        /// run out locally.
        /// </summary>
        /// <remarks>
        /// The expiry check and the transition are one critical section. Taken
        /// apart, a renewal could commit a fresh expiry between the two and this
        /// would then revoke the lease the renewal had just extended.
        /// </remarks>
        private void StepDownIfLeaseExpired()
        {
            lock (m_lock)
            {
                // IsLeader already reports false once the lease has run out;
                // this makes the step-down explicit so LeadershipChanged fires
                // and the replica releases whatever it holds as leader.
                if (m_isLeader &&
                    m_timeProvider.GetUtcNow().UtcTicks >= m_localLeaseExpiryTicks)
                {
                    m_isLeader = false;
                    m_localLeaseExpiryTicks = 0;
                    m_pendingNotifications.Enqueue(false);
                }
            }

            DispatchNotifications();
        }

        /// <summary>
        /// <see cref="StepDownIfLeaseExpired"/> for callers with nowhere to
        /// report a failure: the watchdog timer, where an escaping exception
        /// would end the process, and the renew loop's own failure path.
        /// </summary>
        private void StepDownIfLeaseExpiredSafely()
        {
            try
            {
                StepDownIfLeaseExpired();
            }
            catch (Exception ex)
            {
                m_logger?.SharedStoreLeaseElectionLogMessage2(ex, m_nodeId);
            }
        }

        /// <summary>
        /// Records the outcome of an acquire or renew.
        /// </summary>
        /// <param name="value">Whether this replica now holds the lease.</param>
        /// <param name="expiryTicks">
        /// When the lease it wrote runs out; ignored when <paramref name="value"/>
        /// is <see langword="false"/>.
        /// </param>
        private void SetLeader(bool value, long expiryTicks = 0)
        {
            lock (m_lock)
            {
                // Leadership and the lease behind it change together, so no
                // reader - IsLeader or the watchdog - sees one without the other.
                m_localLeaseExpiryTicks = value ? expiryTicks : 0;

                if (m_isLeader != value)
                {
                    m_isLeader = value;
                    m_pendingNotifications.Enqueue(value);
                }
            }

            DispatchNotifications();
        }

        /// <summary>
        /// Delivers queued <see cref="LeadershipChanged"/> notifications in the
        /// order the transitions were made.
        /// </summary>
        /// <remarks>
        /// The renew loop and the watchdog both make transitions, so two can
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
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_cts = new();
        private Task? m_loop;
        private bool m_isLeader;
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Drives <see cref="StepDownIfLeaseExpired"/> on its own cadence, so a
        /// store call that never returns cannot keep this replica claiming
        /// leadership.
        /// </summary>
        private ITimer? m_watchdog;

        /// <summary>
        /// When the lease this replica last wrote to the store runs out. Used to
        /// step down while the store is unreachable, because a lease that cannot
        /// be renewed expires for everyone else too. Guarded by
        /// <see cref="m_lock"/>, together with <see cref="m_isLeader"/>.
        /// </summary>
        private long m_localLeaseExpiryTicks;

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
