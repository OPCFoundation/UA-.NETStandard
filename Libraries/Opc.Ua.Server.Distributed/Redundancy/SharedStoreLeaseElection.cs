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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// Lease-based <see cref="ILeaderElection"/> over an
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
                lock (m_lock)
                {
                    return m_isLeader;
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
                canTake = !TryParseLease(current, out string owner, out long expiryTicks)
                    || nowTicks >= expiryTicks
                    || string.Equals(owner, m_nodeId, StringComparison.Ordinal);
            }

            if (!canTake)
            {
                SetLeader(false);
                return false;
            }

            ByteString newLease = EncodeLease(m_nodeId, nowTicks + m_leaseDuration.Ticks);
            ByteString expected = found ? current : default;
            bool acquired = await m_store
                .CompareAndSwapAsync(m_leaseKey, expected, newLease, ct)
                .ConfigureAwait(false);
            SetLeader(acquired);
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
                        m_logger?.LogError(ex, "Lease election renew failed for {NodeId}.", m_nodeId);
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
                m_logger?.LogError(ex, "Lease election release failed for {NodeId}.", m_nodeId);
            }
        }

        private void SetLeader(bool value)
        {
            bool changed;
            lock (m_lock)
            {
                changed = m_isLeader != value;
                m_isLeader = value;
            }
            if (changed)
            {
                LeadershipChanged?.Invoke(value);
            }
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
    }
}
