// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// renewing. A rejected, failed, or timed-out renewal demotes the
    /// component before it retries acquisition. A distributed lease store
    /// provides genuine cross-instance failover; the default in-memory store
    /// is single-process.
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
        private static readonly TimeSpan s_minTimerDelay = TimeSpan.FromMilliseconds(1);
        private static readonly TimeSpan s_maxTimerDelay = TimeSpan.FromMilliseconds(int.MaxValue);

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
        /// Target interval at which the active instance renews its leases.
        /// Defaults to a third of <paramref name="leaseDuration"/>; renewal
        /// may start earlier if the remaining lease lifetime is short.
        /// </param>
        /// <param name="retryInterval">
        /// Delay between acquisition attempts while in standby, including
        /// after store errors. Defaults to a third of <paramref name="leaseDuration"/>.
        /// </param>
        /// <param name="timeProvider">Clock used for scheduling and checking lease expiry.</param>
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
            m_renewInterval = GetTimerInterval(renewInterval);
            m_retryInterval = GetTimerInterval(retryInterval);
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
            if (RoleChanged is not { } handlers)
            {
                return;
            }
            var args = new PubSubRoleChangedEventArgs(componentId, role);
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<PubSubRoleChangedEventArgs>)handler)(this, args);
                }
                catch (Exception ex)
                {
                    m_logger.PubSubLeaseRoleChangedHandlerFailed(componentId, ex.GetType().Name);
                }
            }
        }

        private TimeSpan GetTimerInterval(TimeSpan? interval)
        {
            long ticks = interval is { Ticks: > 0 } value ? value.Ticks : m_leaseDuration.Ticks / 3;
            return TimeSpan.FromTicks(Math.Min(Math.Max(ticks, s_minTimerDelay.Ticks), s_maxTimerDelay.Ticks));
        }

        private sealed class ComponentLoop
        {
            public ComponentLoop(
                LeaseActivationCoordinator owner,
                string componentId,
                CancellationToken token)
            {
                m_owner = owner;
                m_componentId = componentId;
                m_token = token;
            }

            public PubSubComponentRole Role =>
                !m_token.IsCancellationRequested &&
                Volatile.Read(ref m_activeLease) is { } held &&
                GetRemainingTime(held) > TimeSpan.Zero
                    ? PubSubComponentRole.Active
                    : PubSubComponentRole.Standby;

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
                PubSubLease? leaseOnShutdown = null;
                try
                {
                    LeaseConfirmation? held = null;
                    while (!m_token.IsCancellationRequested)
                    {
                        long requestedAt = m_owner.m_timeProvider.GetTimestamp();
                        (PubSubLease? lease, Exception? failure) = held is null
                            ? await TryUpdateLeaseAsync(null, m_token).ConfigureAwait(false)
                            : await RenewAsync(held).ConfigureAwait(false);
                        if (m_token.IsCancellationRequested)
                        {
                            leaseOnShutdown = lease;
                            break;
                        }

                        bool wasRenewing = held is not null;
                        held = lease is { } value ? new LeaseConfirmation(value, requestedAt) : null;
                        TimeSpan delay = held is null ? TimeSpan.Zero : GetRenewalInterval(held);
                        if (delay < s_minTimerDelay)
                        {
                            held = null;
                            delay = m_owner.m_retryInterval;
                        }
                        SetLease(held);
                        if (failure is not null)
                        {
                            m_owner.m_logger.PubSubLeaseStoreOperationFailed(
                                m_componentId, wasRenewing, failure.GetType().Name);
                        }
                        await m_owner.m_timeProvider.Delay(delay, m_token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (m_token.IsCancellationRequested)
                {
                    // Expected on shutdown.
                }
                catch (Exception ex)
                {
                    leaseOnShutdown = m_activeLease?.Lease;
                    SetLease(null);
                    m_owner.m_logger.PubSubLeaseCoordinationLoopFaulted(ex, m_componentId);
                }
                finally
                {
                    leaseOnShutdown ??= m_activeLease?.Lease;
                    SetLease(null);
                    if (leaseOnShutdown is { } lease)
                    {
                        try
                        {
                            await m_owner.m_leaseStore.ReleaseAsync(lease, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_owner.m_logger.ReleasingLeaseOnShutdownFailed(ex, m_componentId);
                        }
                    }
                }
            }

            private async Task<(PubSubLease? Lease, Exception? Failure)> RenewAsync(LeaseConfirmation held)
            {
                TimeSpan budget = GetRenewalBudget(held);
                if (budget < s_minTimerDelay)
                {
                    return default;
                }

                using var deadline = m_owner.m_timeProvider.CreateCancellationTokenSource(budget);
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_token, deadline.Token);
                Task<(PubSubLease? Lease, Exception? Failure)> renewal = TryUpdateLeaseAsync(
                    held.Lease, cancellation.Token);
                try
                {
                    var result = await renewal.WaitAsync(cancellation.Token).ConfigureAwait(false);
                    if (m_token.IsCancellationRequested ||
                        (!deadline.IsCancellationRequested && GetRemainingTime(held) > TimeSpan.Zero))
                    {
                        return result;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    if (m_token.IsCancellationRequested && renewal.IsCompleted)
                    {
                        return await renewal.ConfigureAwait(false);
                    }
                }

                // Do not release or start another store operation while this renewal may still be committing.
                // The old lease can expire naturally; a late response cannot restore local publishing.
                SetLease(null);
                m_token.ThrowIfCancellationRequested();
                m_owner.m_logger.PubSubLeaseRenewalDeadlineExceeded(m_componentId);
                _ = await renewal.WaitAsync(m_token).ConfigureAwait(false);
                return default;
            }

            private async Task<(PubSubLease? Lease, Exception? Failure)> TryUpdateLeaseAsync(
                PubSubLease? lease,
                CancellationToken cancellationToken)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PubSubLease? updated = lease is { } held
                        ? await m_owner.m_leaseStore.TryRenewAsync(held, m_owner.m_leaseDuration, cancellationToken)
                            .ConfigureAwait(false)
                        : await m_owner.m_leaseStore.TryAcquireAsync(
                            m_componentId, m_owner.m_ownerId, m_owner.m_leaseDuration, cancellationToken)
                            .ConfigureAwait(false);
                    return (updated, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return default;
                }
                catch (Exception ex)
                {
                    return (null, ex);
                }
            }

            private TimeSpan GetRenewalInterval(LeaseConfirmation held)
            {
                return TimeSpan.FromTicks(Math.Min(m_owner.m_renewInterval.Ticks, GetRenewalBudget(held).Ticks));
            }

            private TimeSpan GetRenewalBudget(LeaseConfirmation held)
            {
                // Leave time after the renewal deadline to demote before the advertised lease expires.
                return TimeSpan.FromTicks(Math.Min(s_maxTimerDelay.Ticks, GetRemainingTime(held).Ticks / 2));
            }

            private TimeSpan GetRemainingTime(LeaseConfirmation held)
            {
                TimeProvider clock = m_owner.m_timeProvider;
                TimeSpan utcRemaining = held.Lease.ExpiresAt - clock.GetUtcNow();
                TimeSpan monotonicRemaining = m_owner.m_leaseDuration - clock.GetElapsedTime(held.RequestedAt);
                return utcRemaining < monotonicRemaining ? utcRemaining : monotonicRemaining;
            }

            private void SetLease(LeaseConfirmation? lease)
            {
                LeaseConfirmation? previous = Interlocked.Exchange(ref m_activeLease, lease);
                if ((previous is null) != (lease is null))
                {
                    m_owner.RaiseRoleChanged(
                        m_componentId,
                        lease is null ? PubSubComponentRole.Standby : PubSubComponentRole.Active);
                }
            }

            private sealed record LeaseConfirmation(PubSubLease Lease, long RequestedAt);

            private readonly LeaseActivationCoordinator m_owner;
            private readonly string m_componentId;
            private readonly CancellationToken m_token;
            private Task? m_task;
            private LeaseConfirmation? m_activeLease;
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="LeaseActivationCoordinator"/>.
    /// </summary>
    internal static partial class LeaseActivationCoordinatorLog
    {
        /// <summary>
        /// Logs an unexpected coordination loop failure.
        /// </summary>
        [LoggerMessage(EventId = PubSubEventIds.LeaseActivationCoordinator + 0, Level = LogLevel.Error,
            Message = "PubSub lease coordination loop for '{ComponentId}' faulted.")]
        public static partial void PubSubLeaseCoordinationLoopFaulted(
            this ILogger logger,
            Exception exception,
            string componentId);

        /// <summary>
        /// Logs a failed best-effort release during shutdown.
        /// </summary>
        [LoggerMessage(EventId = PubSubEventIds.LeaseActivationCoordinator + 1, Level = LogLevel.Debug,
            Message = "Releasing lease for '{ComponentId}' on shutdown failed.")]
        public static partial void ReleasingLeaseOnShutdownFailed(
            this ILogger logger,
            Exception exception,
            string componentId);

        /// <summary>
        /// Logs a failed acquisition or renewal that will be retried from standby.
        /// </summary>
        [LoggerMessage(EventId = PubSubEventIds.LeaseActivationCoordinator + 2, Level = LogLevel.Warning,
            Message = "PubSub lease store operation for '{ComponentId}' failed (renewing: {Renewing}, {ErrorType}).")]
        public static partial void PubSubLeaseStoreOperationFailed(
            this ILogger logger,
            string componentId,
            bool renewing,
            string errorType);

        /// <summary>
        /// Logs the loss of local authority when renewal does not finish in time.
        /// </summary>
        [LoggerMessage(EventId = PubSubEventIds.LeaseActivationCoordinator + 3, Level = LogLevel.Warning,
            Message = "PubSub lease renewal for '{ComponentId}' exceeded the local deadline.")]
        public static partial void PubSubLeaseRenewalDeadlineExceeded(this ILogger logger, string componentId);

        /// <summary>
        /// Logs a role-change subscriber failure without skipping other subscribers.
        /// </summary>
        [LoggerMessage(EventId = PubSubEventIds.LeaseActivationCoordinator + 4, Level = LogLevel.Error,
            Message = "A PubSub lease role-change handler for '{ComponentId}' failed ({ErrorType}).")]
        public static partial void PubSubLeaseRoleChangedHandlerFailed(
            this ILogger logger,
            string componentId,
            string errorType);
    }
}
