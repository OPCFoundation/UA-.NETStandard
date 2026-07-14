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
using System.Buffers.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a distributed
    /// <see cref="IPushConfigurationTransactionCoordinator"/> that enforces a
    /// single, cluster-wide PushManagement configuration transaction
    /// (OPC 10000-12 §§7.10.2-7.10.11) across a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distributed coordinator wraps a per-server local coordinator (the
    /// default <see cref="PushConfigurationTransactionCoordinator"/>) and adds
    /// cross-replica exclusion on top of it, reusing the lease pattern of
    /// <see cref="SharedStoreLeaseElection"/>: a single shared-store key holds
    /// the owning replica's id and an expiry, updated atomically with
    /// <see cref="ISharedKeyValueStore.CompareAndSwapAsync"/> ("shared read,
    /// master write"). A replica may only start a transaction while it holds
    /// that lease, so at most one replica has an active PushManagement
    /// transaction at a time. A replica that stops renewing (for example,
    /// because it crashed) loses the lease once it expires, allowing a standby
    /// to take over.
    /// </para>
    /// <para>
    /// The lease is acquired or renewed at the asynchronous
    /// <see cref="AcquireTransactionOwnershipAsync"/> boundary that
    /// <see cref="ConfigurationNodeManager"/> and <see cref="TrustList"/> invoke
    /// immediately before every synchronous
    /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/> call, so the
    /// coordinator's synchronous contract is preserved without any
    /// sync-over-async work. A background loop renews the lease while the local
    /// transaction is active and releases it when the transaction is applied,
    /// cancelled, abandoned (session close) or reset.
    /// </para>
    /// <para>
    /// This type is intended to be used as a singleton per server process; it
    /// owns a background loop and shared-store lease, and must be disposed
    /// (<see cref="DisposeAsync"/>) with the host. Distributed behaviour is
    /// opt-in: the default server registers no coordinator and each
    /// <see cref="ConfigurationNodeManager"/> owns a private, process-local
    /// coordinator.
    /// </para>
    /// </remarks>
    public sealed class DistributedPushConfigurationTransactionCoordinator :
        IPushConfigurationTransactionCoordinator,
        IPushConfigurationTransactionOwnershipGate,
        IAsyncDisposable,
        IDisposable
    {
        private const int MaxAcquireAttempts = 5;

        /// <summary>
        /// Creates a distributed coordinator over a shared key/value store.
        /// </summary>
        /// <param name="inner">
        /// The per-server local coordinator that owns process-local
        /// transaction state (defaults to a new
        /// <see cref="PushConfigurationTransactionCoordinator"/> when
        /// <c>null</c> and <paramref name="telemetry"/> is supplied).
        /// </param>
        /// <param name="store">The shared key/value backend holding the lease.</param>
        /// <param name="options">The distributed PushManagement options.</param>
        /// <param name="telemetry">
        /// Telemetry context used to create a logger and, when
        /// <paramref name="inner"/> is <c>null</c>, the default local
        /// coordinator.
        /// </param>
        /// <param name="timeProvider">
        /// Optional time provider for deterministic tests. Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="leaderElection">
        /// Optional leader election used only when
        /// <see cref="DistributedPushConfigurationOptions.RequireLeadership"/>
        /// is set: transaction ownership is then granted only to the elected
        /// leader. When the Kubernetes extension is configured this is the
        /// Kubernetes-native <c>Lease</c> election, reused without a second
        /// Kubernetes client.
        /// </param>
        public DistributedPushConfigurationTransactionCoordinator(
            IPushConfigurationTransactionCoordinator? inner,
            ISharedKeyValueStore store,
            DistributedPushConfigurationOptions options,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            ILeaderElection? leaderElection = null)
            : this(inner, store, options, telemetry, timeProvider, leaderElection, startRenewLoop: true)
        {
        }

        /// <summary>
        /// Test seam that allows the background renew/reconcile loop to be
        /// suppressed so a test can drive reconciliation deterministically via
        /// <see cref="ReconcileNowAsync"/> and a
        /// <see cref="System.TimeProvider"/> it controls.
        /// </summary>
        internal DistributedPushConfigurationTransactionCoordinator(
            IPushConfigurationTransactionCoordinator? inner,
            ISharedKeyValueStore store,
            DistributedPushConfigurationOptions options,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider,
            ILeaderElection? leaderElection,
            bool startRenewLoop)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<DistributedPushConfigurationTransactionCoordinator>();
            m_inner = inner ?? new PushConfigurationTransactionCoordinator(telemetry, m_timeProvider);
            m_leaderElection = leaderElection;
            m_requireLeadership = options.RequireLeadership;

            m_replicaId = options.ReplicaId;
            m_leaseKey = options.TransactionLeaseKey;
            m_leaseDuration = options.LeaseDuration;
            m_renewInterval = options.RenewInterval;
            m_reservationTimeout = options.EffectiveReservationTimeout;

            // Start the reconcile loop so this replica renews the lease while a
            // transaction is active and releases it promptly on
            // cancel/reset/session-close (signalled) or after it expires.
            if (startRenewLoop)
            {
                m_loop = Task.Run(() => ReconcileLoopAsync(m_cts.Token));
            }
        }

        /// <summary>
        /// Test-only hook that runs a single reconcile pass synchronously with
        /// respect to the caller, so a test can deterministically observe lease
        /// renewal and release without depending on the background loop timing.
        /// </summary>
        internal ValueTask ReconcileNowAsync(CancellationToken cancellationToken = default)
        {
            return new ValueTask(ReconcileAsync(cancellationToken));
        }

        /// <summary>
        /// Test-only accessor reporting whether this replica currently believes
        /// it holds the shared transaction lease.
        /// </summary>
        internal bool HoldsLease
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_holdsLease;
                }
            }
        }

        /// <inheritdoc/>
        public NodeId OwnerSessionId => m_inner.OwnerSessionId;

        /// <inheritdoc/>
        public bool IsTransactionActive => m_inner.IsTransactionActive;

        /// <inheritdoc/>
        public bool HasOpenTrustListWriter => m_inner.HasOpenTrustListWriter;

        /// <inheritdoc/>
        public void ValidateSessionCanParticipate(NodeId sessionId)
        {
            ThrowIfForeignOwner();
            m_inner.ValidateSessionCanParticipate(sessionId);
        }

        /// <inheritdoc/>
        public void Stage(NodeId sessionId, PushConfigurationOperation operation)
        {
            // AcquireTransactionOwnershipAsync ran at the async boundary before
            // this synchronous call, so this replica holds (or has reserved)
            // the shared lease. The foreign-owner guard defends against a split
            // brain detected by the background loop between acquisition and
            // staging.
            ThrowIfForeignOwner();
            m_inner.Stage(sessionId, operation);
        }

        /// <inheritdoc/>
        public void SetTrustListWriteOpen(NodeId trustListId, bool isOpen)
        {
            m_inner.SetTrustListWriteOpen(trustListId, isOpen);
            SignalReconcile();
        }

        /// <inheritdoc/>
        public ArrayOf<PushConfigurationOperation> GetStagedOperations()
        {
            return m_inner.GetStagedOperations();
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            ServiceResult result = await m_inner
                .ApplyChangesAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            await ReleaseAfterCompletionAsync(sessionId).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects committedEffects,
            CancellationToken cancellationToken = default)
        {
            ServiceResult result = await m_inner
                .ApplyChangesAsync(sessionId, committedEffects, cancellationToken)
                .ConfigureAwait(false);
            await ReleaseAfterCompletionAsync(sessionId).ConfigureAwait(false);
            return result;
        }

        /// <inheritdoc/>
        public ServiceResult CancelChanges(NodeId sessionId)
        {
            ServiceResult result = m_inner.CancelChanges(sessionId);
            ClearReservationIfOwner(sessionId);
            SignalReconcile();
            return result;
        }

        /// <inheritdoc/>
        public void CancelForSessionClose(NodeId sessionId)
        {
            m_inner.CancelForSessionClose(sessionId);
            ClearReservationIfOwner(sessionId);
            SignalReconcile();
        }

        /// <inheritdoc/>
        public void Reset()
        {
            m_inner.Reset();
            lock (m_stateLock)
            {
                m_reservationDeadlineTicks = 0;
                m_reservationOwner = NodeId.Null;
            }
            SignalReconcile();
        }

        /// <inheritdoc/>
        public PushConfigurationTransactionSnapshot GetSnapshot()
        {
            return m_inner.GetSnapshot();
        }

        /// <inheritdoc/>
        public async ValueTask AcquireTransactionOwnershipAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(DistributedPushConfigurationTransactionCoordinator));
                }
            }

            // Optional leadership gate: funnel PushManagement transactions
            // through the elected leader (Kubernetes-native Lease, Raft or
            // shared-store, whichever is configured) when RequireLeadership is
            // set. The shared-store transaction lease below still enforces
            // single ownership on its own when leadership is not required.
            if (m_requireLeadership && m_leaderElection != null)
            {
                bool leader = await m_leaderElection
                    .TryAcquireOrRenewAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (!leader)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTransactionPending,
                        "This replica is not the active leader of the redundant server set, so it " +
                        "cannot start a PushManagement configuration transaction.");
                }
            }

            await m_storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long now = NowTicks();

                    (bool found, ByteString current) = await m_store
                        .TryGetAsync(m_leaseKey, cancellationToken)
                        .ConfigureAwait(false);
                    bool parsed = TryParseLease(current, out string owner, out long expiry);
                    bool live = found && parsed && now < expiry;
                    if (live && !IsSelf(owner))
                    {
                        SetLeaseState(holdsLease: false, foreignOwner: true);
                        ThrowTransactionPending();
                    }

                    ByteString newLease = EncodeLease(m_replicaId, now + m_leaseDuration.Ticks);
                    ByteString expected = found ? current : default;
                    bool acquired = await m_store
                        .CompareAndSwapAsync(m_leaseKey, expected, newLease, cancellationToken)
                        .ConfigureAwait(false);
                    if (acquired)
                    {
                        // Reserve ownership only now that the lease is actually
                        // granted, so a rejected or otherwise failed acquisition
                        // (leadership, foreign owner, cancellation, CAS
                        // contention, or any exception) never leaves a
                        // reservation behind that the reconcile loop could later
                        // use to acquire the lease on this replica's behalf. The
                        // reservation is set while the store gate is still held so
                        // a concurrent reconcile pass cannot delete the lease
                        // before the first Stage marks the transaction active.
                        SetLeaseState(holdsLease: true, foreignOwner: false);
                        ReserveOwnership(sessionId);
                        return;
                    }

                    if (attempt >= MaxAcquireAttempts)
                    {
                        // The compare-and-swap kept losing. Re-read once more to
                        // decide whether another replica now owns the lease
                        // (reject) or the contention was with this replica's own
                        // renew loop (we effectively already hold it).
                        (bool reFound, ByteString reValue) = await m_store
                            .TryGetAsync(m_leaseKey, cancellationToken)
                            .ConfigureAwait(false);
                        bool reParsed = TryParseLease(reValue, out string reOwner, out long reExpiry);
                        bool foreignLive = reFound && reParsed && NowTicks() < reExpiry && !IsSelf(reOwner);
                        if (foreignLive)
                        {
                            SetLeaseState(holdsLease: false, foreignOwner: true);
                            ThrowTransactionPending();
                        }
                        SetLeaseState(holdsLease: true, foreignOwner: false);
                        ReserveOwnership(sessionId);
                        return;
                    }
                }
            }
            finally
            {
                m_storeGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_cts.Cancel();
            SignalReconcile();
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

            await ReleaseIfOwnedAsync(CancellationToken.None).ConfigureAwait(false);

            m_cts.Dispose();
            m_storeGate.Dispose();
            m_wake.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Non-blocking teardown: signalling the background loop to stop and
            // releasing the lease require awaiting, which must never be done
            // sync-over-async. Cancel the loop and let it release the lease as
            // it unwinds; a lease that outlives this call still expires on its
            // own. Prefer DisposeAsync where the caller can await full cleanup.
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
            }

            m_cts.Cancel();
            SignalReconcile();
        }

        private async ValueTask ReleaseAfterCompletionAsync(NodeId sessionId)
        {
            ClearReservationIfOwner(sessionId);
            try
            {
                await ReconcileAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Failed to reconcile the distributed PushManagement lease after ApplyChanges for {Replica}.",
                    m_replicaId);
                SignalReconcile();
            }
        }

        private async Task ReconcileLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await ReconcileAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(
                            ex,
                            "Distributed PushManagement lease reconcile failed for {Replica}.",
                            m_replicaId);
                    }

                    try
                    {
                        await m_wake.WaitAsync(m_renewInterval, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        private async Task ReconcileAsync(CancellationToken ct)
        {
            await m_storeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Read the hold/reservation state under the same gate that a
                // successful AcquireTransactionOwnershipAsync sets it under, so a
                // concurrent grant cannot slip a reservation in between this read
                // and the store update below (which would otherwise let this pass
                // delete a lease that was just acquired).
                bool innerBusy = m_inner.IsTransactionActive || m_inner.HasOpenTrustListWriter;
                bool holds;
                long reservationDeadline;
                lock (m_stateLock)
                {
                    holds = m_holdsLease;
                    reservationDeadline = m_reservationDeadlineTicks;
                }

                (bool found, ByteString current) = await m_store
                    .TryGetAsync(m_leaseKey, ct)
                    .ConfigureAwait(false);
                long now = NowTicks();
                bool shouldHold = innerBusy || now < reservationDeadline;
                bool parsed = TryParseLease(current, out string owner, out long expiry);
                bool live = found && parsed && now < expiry;
                bool ownedBySelf = live && IsSelf(owner);
                bool foreignOwner = live && !IsSelf(owner);

                if (shouldHold)
                {
                    if (foreignOwner)
                    {
                        // Another replica owns the transaction; back off and let
                        // the synchronous guards reject local staging.
                        SetLeaseState(holdsLease: false, foreignOwner: true);
                        return;
                    }

                    ByteString newLease = EncodeLease(m_replicaId, now + m_leaseDuration.Ticks);
                    ByteString expected = found ? current : default;
                    bool renewed = await m_store
                        .CompareAndSwapAsync(m_leaseKey, expected, newLease, ct)
                        .ConfigureAwait(false);
                    SetLeaseState(holdsLease: renewed, foreignOwner: false);
                    return;
                }

                if (holds && ownedBySelf)
                {
                    await m_store.DeleteAsync(m_leaseKey, ct).ConfigureAwait(false);
                }
                SetLeaseState(holdsLease: false, foreignOwner: foreignOwner);
            }
            finally
            {
                m_storeGate.Release();
            }
        }

        private async ValueTask ReleaseIfOwnedAsync(CancellationToken ct)
        {
            await m_storeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                (bool found, ByteString current) = await m_store
                    .TryGetAsync(m_leaseKey, ct)
                    .ConfigureAwait(false);
                if (found &&
                    TryParseLease(current, out string owner, out _) &&
                    IsSelf(owner))
                {
                    await m_store.DeleteAsync(m_leaseKey, ct).ConfigureAwait(false);
                }
                SetLeaseState(holdsLease: false, foreignOwner: false);
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Failed to release the distributed PushManagement lease for {Replica}.",
                    m_replicaId);
            }
            finally
            {
                m_storeGate.Release();
            }
        }

        private void ReserveOwnership(NodeId sessionId)
        {
            long now = NowTicks();
            lock (m_stateLock)
            {
                m_reservationDeadlineTicks = now + m_reservationTimeout.Ticks;
                m_reservationOwner = sessionId;
            }
        }

        private void ClearReservationIfOwner(NodeId sessionId)
        {
            lock (m_stateLock)
            {
                if (Utils.IsEqual(sessionId, m_reservationOwner))
                {
                    m_reservationDeadlineTicks = 0;
                    m_reservationOwner = NodeId.Null;
                }
            }
        }

        private void SetLeaseState(bool holdsLease, bool foreignOwner)
        {
            lock (m_stateLock)
            {
                m_holdsLease = holdsLease;
                m_foreignOwner = foreignOwner;
            }
        }

        private void ThrowIfForeignOwner()
        {
            bool foreign;
            lock (m_stateLock)
            {
                foreign = m_foreignOwner;
            }
            if (foreign)
            {
                ThrowTransactionPending();
            }
        }

        private static void ThrowTransactionPending()
        {
            throw new ServiceResultException(
                StatusCodes.BadTransactionPending,
                "Another replica already owns the active PushManagement configuration transaction.");
        }

        private void SignalReconcile()
        {
            try
            {
                m_wake.Release();
            }
            catch (SemaphoreFullException)
            {
                // A reconcile pass is already pending; coalesce the signal.
            }
        }

        private bool IsSelf(string owner)
        {
            return string.Equals(owner, m_replicaId, StringComparison.Ordinal);
        }

        private long NowTicks()
        {
            return m_timeProvider.GetUtcNow().UtcTicks;
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
            if (raw.IsNull)
            {
                return false;
            }
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

        private readonly IPushConfigurationTransactionCoordinator m_inner;
        private readonly ISharedKeyValueStore m_store;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly ILeaderElection? m_leaderElection;
        private readonly bool m_requireLeadership;
        private readonly string m_replicaId;
        private readonly string m_leaseKey;
        private readonly TimeSpan m_leaseDuration;
        private readonly TimeSpan m_renewInterval;
        private readonly TimeSpan m_reservationTimeout;
        private readonly Lock m_stateLock = new();
        private readonly SemaphoreSlim m_storeGate = new(1, 1);
        private readonly SemaphoreSlim m_wake = new(0, 1);
        private readonly CancellationTokenSource m_cts = new();
        private readonly Task? m_loop;
        private bool m_holdsLease;
        private bool m_foreignOwner;
        private long m_reservationDeadlineTicks;
        private NodeId m_reservationOwner = NodeId.Null;
        private bool m_disposed;
    }
}
