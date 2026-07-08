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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// <see cref="IPubSubActivationCoordinator"/> that elects the active
    /// instance of each component through leadership leases held in a shared
    /// <see cref="IPubSubLeaseStore"/>, per OPC UA Part 14 §9.1.6.
    /// </summary>
    /// <remarks>
    /// Each component is backed by one lease. The instance that holds the
    /// lease is <see cref="PubSubComponentRole.Active"/>; the others are
    /// <see cref="PubSubComponentRole.Standby"/> and continuously attempt to
    /// acquire it, taking over automatically when the active instance stops
    /// renewing. A distributed lease store provides genuine cross-instance
    /// failover; the default in-memory store is single-process.
    /// </remarks>
    public sealed class LeaseActivationCoordinator : IPubSubActivationCoordinator, IAsyncDisposable
    {
        private readonly IPubSubLeaseStore m_leaseStore;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly string m_ownerId;
        private readonly TimeSpan m_leaseDuration;
        private readonly TimeSpan m_renewInterval;
        private readonly TimeSpan m_retryInterval;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, ComponentLoop> m_loops = new(StringComparer.Ordinal);
        private CancellationTokenSource? m_cts;
        private bool m_started;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new <see cref="LeaseActivationCoordinator"/>.
        /// </summary>
        /// <param name="leaseStore">Shared lease store used for election.</param>
        /// <param name="telemetry">Telemetry context for logging.</param>
        /// <param name="ownerId">
        /// Stable identifier of this instance. Defaults to a random value.
        /// </param>
        /// <param name="leaseDuration">
        /// Lease time-to-live. Defaults to 15 seconds.
        /// </param>
        /// <param name="renewInterval">
        /// Interval at which the active instance renews its leases. Defaults
        /// to a third of <paramref name="leaseDuration"/>.
        /// </param>
        /// <param name="retryInterval">
        /// Interval at which a standby instance retries acquisition. Defaults
        /// to a third of <paramref name="leaseDuration"/>.
        /// </param>
        /// <param name="timeProvider">Clock used for scheduling.</param>
        public LeaseActivationCoordinator(
            IPubSubLeaseStore leaseStore,
            ITelemetryContext? telemetry = null,
            string? ownerId = null,
            TimeSpan? leaseDuration = null,
            TimeSpan? renewInterval = null,
            TimeSpan? retryInterval = null,
            TimeProvider? timeProvider = null)
        {
            m_leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<LeaseActivationCoordinator>();
            m_ownerId = string.IsNullOrEmpty(ownerId)
                ? Guid.NewGuid().ToString("N")
                : ownerId!;
            m_leaseDuration = leaseDuration is { } d && d > TimeSpan.Zero
                ? d
                : TimeSpan.FromSeconds(15);
            m_renewInterval = renewInterval is { } r && r > TimeSpan.Zero
                ? r
                : TimeSpan.FromTicks(m_leaseDuration.Ticks / 3);
            m_retryInterval = retryInterval is { } t && t > TimeSpan.Zero
                ? t
                : TimeSpan.FromTicks(m_leaseDuration.Ticks / 3);
        }

        /// <inheritdoc/>
        public event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged;

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(LeaseActivationCoordinator));
                }
                if (!m_started)
                {
                    m_cts = new CancellationTokenSource();
                    m_started = true;
                }
            }
            return default;
        }

        /// <inheritdoc/>
        public async ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            List<ComponentLoop> loops;
            CancellationTokenSource? cts;
            lock (m_gate)
            {
                m_started = false;
                cts = m_cts;
                m_cts = null;
                loops = [.. m_loops.Values];
                m_loops.Clear();
            }
            cts?.Cancel();
            foreach (ComponentLoop loop in loops)
            {
                await loop.StopAsync().ConfigureAwait(false);
            }
            cts?.Dispose();
        }

        /// <inheritdoc/>
        public ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(componentId))
            {
                throw new ArgumentException("componentId is required.", nameof(componentId));
            }
            ComponentLoop loop;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(LeaseActivationCoordinator));
                }
                if (!m_started || m_cts is null)
                {
                    return new ValueTask<PubSubComponentRole>(PubSubComponentRole.Standby);
                }
                if (!m_loops.TryGetValue(componentId, out ComponentLoop? existing))
                {
                    existing = new ComponentLoop(this, componentId, m_cts.Token);
                    m_loops[componentId] = existing;
                    existing.Start();
                }
                loop = existing;
            }
            return new ValueTask<PubSubComponentRole>(loop.Role);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }
            await StopAsync().ConfigureAwait(false);
        }

        private void RaiseRoleChanged(string componentId, PubSubComponentRole role)
        {
            RoleChanged?.Invoke(this, new PubSubRoleChangedEventArgs(componentId, role));
        }

        private sealed class ComponentLoop
        {
            private readonly LeaseActivationCoordinator m_owner;
            private readonly string m_componentId;
            private readonly CancellationToken m_token;
            private Task? m_task;
            private volatile int m_role = (int)PubSubComponentRole.Standby;

            public ComponentLoop(
                LeaseActivationCoordinator owner,
                string componentId,
                CancellationToken token)
            {
                m_owner = owner;
                m_componentId = componentId;
                m_token = token;
            }

            public PubSubComponentRole Role => (PubSubComponentRole)m_role;

            public void Start()
            {
                m_task = Task.Run(() => RunAsync(), CancellationToken.None);
            }

            public async ValueTask StopAsync()
            {
                Task? task = m_task;
                if (task is not null)
                {
                    try
                    {
                        await task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on shutdown.
                    }
                }
            }

            private async Task RunAsync()
            {
                PubSubLease? held = null;
                try
                {
                    while (!m_token.IsCancellationRequested)
                    {
                        if (held is null)
                        {
                            held = await m_owner.m_leaseStore.TryAcquireAsync(
                                m_componentId,
                                m_owner.m_ownerId,
                                m_owner.m_leaseDuration,
                                m_token).ConfigureAwait(false);
                            if (held is not null)
                            {
                                SetRole(PubSubComponentRole.Active);
                                await DelayAsync(m_owner.m_renewInterval).ConfigureAwait(false);
                            }
                            else
                            {
                                SetRole(PubSubComponentRole.Standby);
                                await DelayAsync(m_owner.m_retryInterval).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            PubSubLease? renewed = await m_owner.m_leaseStore.TryRenewAsync(
                                held.Value,
                                m_owner.m_leaseDuration,
                                m_token).ConfigureAwait(false);
                            if (renewed is null)
                            {
                                held = null;
                                SetRole(PubSubComponentRole.Standby);
                                await DelayAsync(m_owner.m_retryInterval).ConfigureAwait(false);
                                continue;
                            }
                            held = renewed;
                            await DelayAsync(m_owner.m_renewInterval).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
                catch (Exception ex)
                {
                    m_owner.m_logger.LogError(
                        ex,
                        "PubSub lease coordination loop for '{ComponentId}' faulted.",
                        m_componentId);
                }
                finally
                {
                    if (held is not null)
                    {
                        try
                        {
                            await m_owner.m_leaseStore.ReleaseAsync(held.Value, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_owner.m_logger.LogDebug(
                                ex,
                                "Releasing lease for '{ComponentId}' on shutdown failed.",
                                m_componentId);
                        }
                    }
                }
            }

            private async Task DelayAsync(TimeSpan delay)
            {
                await m_owner.m_timeProvider.Delay(delay, m_token).ConfigureAwait(false);
            }

            private void SetRole(PubSubComponentRole role)
            {
                int previous = Interlocked.Exchange(ref m_role, (int)role);
                if (previous != (int)role)
                {
                    m_owner.RaiseRoleChanged(m_componentId, role);
                }
            }
        }
    }
}
