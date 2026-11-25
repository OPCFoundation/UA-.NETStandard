// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
    /// Each commit or compensation delegate must first renew the exact confirmed lease.
    /// This does not make the lease update atomic with a mutation in a separate store.
    /// Trust-store writes through the ordinary certificate and CRL APIs are therefore unsupported.
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
        IPushConfigurationTrustStoreMutationGate,
        IPushConfigurationTransactionCompletion,
        IAsyncDisposable,
        IDisposable
    {
        private const int MaxAcquireRetries = 5;

        /// <summary>
        /// Creates a distributed coordinator over a shared key/value store.
        /// </summary>
        /// <param name="inner">
        /// The per-server local coordinator that owns process-local
        /// transaction state (defaults to a new
        /// <see cref="PushConfigurationTransactionCoordinator"/> when
        /// <c>null</c> and <paramref name="telemetry"/> is supplied).
        /// </param>
        /// <param name="store">The freshness-protected view shared by all lease members.</param>
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
            : this(inner, RequireProtectedStore(store), options, telemetry, timeProvider, leaderElection,
                startRenewLoop: true)
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
            if (string.IsNullOrWhiteSpace(options.ReplicaId))
            {
                throw new ArgumentException("An explicit lease replica id is required.", nameof(options));
            }
            if (store is not InMemorySharedKeyValueStore)
            {
                _ = RequireProtectedStore(store);
                var members = new HashSet<string>(StringComparer.Ordinal);
                foreach (string member in options.LeaseMemberIds)
                {
                    if (string.IsNullOrWhiteSpace(member) || !members.Add(member))
                    {
                        throw new ArgumentException("Lease members must have unique, nonempty ids.", nameof(options));
                    }
                    _ = s_leaseEncoding.GetByteCount(member);
                }
                if (!members.Contains(options.ReplicaId))
                {
                    throw new ArgumentException(
                        "The local replica id must be a configured lease member.", nameof(options));
                }
                m_memberIds = members;
            }
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<DistributedPushConfigurationTransactionCoordinator>();
            m_inner = inner ?? new PushConfigurationTransactionCoordinator(telemetry, m_timeProvider);
            m_leaderElection = options.RequireLeadership
                ? leaderElection ?? throw new ArgumentNullException(nameof(leaderElection))
                : null;

            m_replicaId = options.ReplicaId;
            m_ownerBytes = s_leaseEncoding.GetBytes(m_replicaId);
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
                if (m_leaderElection?.IsLeader == false)
                {
                    return false;
                }
                lock (m_stateLock)
                {
                    return !m_disposed && IsLive(m_confirmation);
                }
            }
        }

        /// <inheritdoc/>
        public NodeId OwnerSessionId => m_inner.OwnerSessionId;

        /// <inheritdoc/>
        public bool IsTransactionActive => m_inner.IsTransactionActive && HoldsLease;

        /// <inheritdoc/>
        public bool HasOpenTrustListWriter => m_inner.HasOpenTrustListWriter;

        /// <inheritdoc/>
        public void ValidateSessionCanParticipate(NodeId sessionId)
        {
            m_inner.ValidateSessionCanParticipate(sessionId);
            bool active = m_inner.IsTransactionActive;
            lock (m_stateLock)
            {
                if (m_disposed || m_unavailable || (active && !IsLive(m_confirmation)))
                {
                    ThrowTransactionPending();
                }
            }
        }

        /// <inheritdoc/>
        public void ValidateUnfencedTrustStoreMutation()
        {
            throw new ServiceResultException(
                StatusCodes.BadNotSupported,
                "Distributed PushManagement trust changes require a sink that checks the current lease " +
                "and publishes the trust revision atomically. The configured path cannot do that.");
        }

        /// <inheritdoc/>
        public void Stage(NodeId sessionId, PushConfigurationOperation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            NodeId owner = m_inner.OwnerSessionId;
            lock (m_stateLock)
            {
                if (m_disposed || !IsLive(m_confirmation) ||
                    (!Utils.IsEqual(owner, sessionId) &&
                        (!Utils.IsEqual(m_reservationOwner, sessionId) || !HasReservation())))
                {
                    ThrowTransactionPending();
                }
            }
            m_inner.Stage(sessionId, FenceOperation(operation));
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
        public ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            return ApplyChangesCoreAsync(sessionId, null, null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects committedEffects,
            CancellationToken cancellationToken = default)
        {
            return ApplyChangesCoreAsync(
                sessionId, committedEffects ?? throw new ArgumentNullException(nameof(committedEffects)),
                null, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ApplyChangesAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects committedEffects,
            PushConfigurationOperation completion,
            CancellationToken cancellationToken = default)
        {
            return ApplyChangesCoreAsync(
                sessionId, committedEffects ?? throw new ArgumentNullException(nameof(committedEffects)),
                completion ?? throw new ArgumentNullException(nameof(completion)), cancellationToken);
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
            CancellationToken lifetime;
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(
                        nameof(DistributedPushConfigurationTransactionCoordinator));
                }
                lifetime = m_cts.Token;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
            CancellationToken ct = linked.Token;
            await m_storeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                m_inner.ValidateSessionCanParticipate(sessionId);
                lock (m_stateLock)
                {
                    if (IsLive(m_confirmation) && HasReservation() &&
                        !Utils.IsEqual(m_reservationOwner, sessionId))
                    {
                        ThrowTransactionPending();
                    }
                }
                if (!await ConfirmLeadershipAsync(ct).ConfigureAwait(false))
                {
                    InvalidateLease();
                    ThrowTransactionPending();
                }
                LeaseConfirmation previous = GetConfirmation();
                if (m_inner.IsTransactionActive || IsLive(previous))
                {
                    // A staged transaction may renew its exact confirmed lease, never join a new one.
                    if (await TryWriteLeaseAsync(previous.Value, previous, ct).ConfigureAwait(false))
                    {
                        ReserveOwnership(sessionId);
                        return;
                    }
                }
                else
                {
                    for (int attempt = 0; attempt <= MaxAcquireRetries; attempt++)
                    {
                        (bool found, ByteString current) = await ReadLeaseAsync(ct).ConfigureAwait(false);
                        if (found && !IsVacant(current, NowTicks()))
                        {
                            break;
                        }
                        if (await TryWriteLeaseAsync(found ? current : default, null, ct).ConfigureAwait(false))
                        {
                            ReserveOwnership(sessionId);
                            return;
                        }
                    }
                }
                InvalidateLease();
                ThrowTransactionPending();
            }
            catch (Exception ex) when (ex is not ServiceResultException result ||
                result.StatusCode != StatusCodes.BadTransactionPending)
            {
                InvalidateLease();
                throw;
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

            using var timeout = new CancellationTokenSource(m_renewInterval);
            await ReleaseIfOwnedAsync(timeout.Token).ConfigureAwait(false);

            m_cts.Dispose();
            m_storeGate.Dispose();
            m_wake.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Stop renewing without waiting for store I/O. Prefer DisposeAsync for early release.
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

        private async ValueTask<ServiceResult> ApplyChangesCoreAsync(
            NodeId sessionId,
            PushConfigurationApplyEffects? committedEffects,
            PushConfigurationOperation? completion,
            CancellationToken ct)
        {
            if (!m_inner.HasOpenTrustListWriter && m_inner.IsTransactionActive &&
                Utils.IsEqual(m_inner.OwnerSessionId, sessionId) && !HoldsLease)
            {
                return StatusCodes.BadTransactionPending;
            }

            ServiceResult result;
            if (completion != null)
            {
                if (m_inner is not IPushConfigurationTransactionCompletion transactionalCompletion)
                {
                    return StatusCodes.BadNotSupported;
                }
                result = await transactionalCompletion.ApplyChangesAsync(
                    sessionId, committedEffects!, FenceOperation(completion), ct).ConfigureAwait(false);
            }
            else
            {
                result = committedEffects == null
                    ? await m_inner.ApplyChangesAsync(sessionId, ct).ConfigureAwait(false)
                    : await m_inner.ApplyChangesAsync(sessionId, committedEffects, ct).ConfigureAwait(false);
            }
            await ReleaseAfterCompletionAsync(sessionId).ConfigureAwait(false);
            return result;
        }

        private PushConfigurationOperation FenceOperation(PushConfigurationOperation operation)
        {
            if (operation.WritesUnfencedTrustStore || !operation.AffectedTrustList.IsNull)
            {
                ValidateUnfencedTrustStoreMutation();
            }
            return new PushConfigurationOperation
            {
                AffectedCertificateGroup = operation.AffectedCertificateGroup,
                AffectedCertificateType = operation.AffectedCertificateType,
                AffectedTrustList = operation.AffectedTrustList,
                LeavesCertificateSlotEmpty = operation.LeavesCertificateSlotEmpty,
                PrepareAsync = operation.PrepareAsync,
                CommitAsync = ct => RunWithLeaseAsync(operation.CommitAsync, ct),
                RollbackAsync = operation.RollbackAsync is { } rollback ? ct => RunWithLeaseAsync(rollback, ct) : null,
                DisposeStaged = operation.DisposeStaged
            };
        }

        private async Task RunWithLeaseAsync(Func<CancellationToken, Task> mutation, CancellationToken ct)
        {
            CancellationToken lifetime;
            lock (m_stateLock)
            {
                if (m_disposed)
                {
                    ThrowTransactionPending();
                }
                lifetime = m_cts.Token;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime);
            await m_storeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                LeaseConfirmation previous = GetConfirmation();
                if (!IsLive(previous) || !await ConfirmLeadershipAsync(linked.Token).ConfigureAwait(false) ||
                    !await TryWriteLeaseAsync(previous.Value, previous, linked.Token).ConfigureAwait(false))
                {
                    ThrowTransactionPending();
                }
            }
            catch
            {
                InvalidateLease();
                throw;
            }
            finally
            {
                m_storeGate.Release();
            }

            linked.Token.ThrowIfCancellationRequested();
            if (!HoldsLease)
            {
                ThrowTransactionPending();
            }
            // The mutation's store is not part of the lease CAS. Recheck at every delegate boundary.
            await mutation(linked.Token).ConfigureAwait(false);
        }

        private async ValueTask ReleaseAfterCompletionAsync(NodeId sessionId)
        {
            ClearReservationIfOwner(sessionId);
            try
            {
                using var timeout = new CancellationTokenSource(m_renewInterval);
                await ReconcileAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.FailedToReconcileLeaseAfterApplyChanges(ex, m_replicaId);
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
                        m_logger.DistributedLeaseReconcileFailed(ex, m_replicaId);
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
                LeaseConfirmation previous = GetConfirmation();
                bool reserved;
                lock (m_stateLock)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    reserved = HasReservation();
                }

                if (IsLive(previous))
                {
                    if ((m_inner.IsTransactionActive || m_inner.HasOpenTrustListWriter || reserved) &&
                        m_leaderElection?.IsLeader != false)
                    {
                        if (!await TryWriteLeaseAsync(previous.Value, previous, ct).ConfigureAwait(false))
                        {
                            InvalidateLease();
                        }
                    }
                    else
                    {
                        bool released = await SwapLeaseAsync(previous.Value, default, ct).ConfigureAwait(false);
                        InvalidateLease(unavailable: !released);
                    }
                }
                else
                {
                    (bool found, ByteString current) = await ReadLeaseAsync(ct).ConfigureAwait(false);
                    InvalidateLease(unavailable: found && !IsVacant(current, NowTicks()));
                }
            }
            catch
            {
                InvalidateLease();
                throw;
            }
            finally
            {
                m_storeGate.Release();
            }
        }

        private async ValueTask ReleaseIfOwnedAsync(CancellationToken ct)
        {
            try
            {
                await m_storeGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    LeaseConfirmation previous = GetConfirmation();
                    if (!previous.Value.IsNull)
                    {
                        _ = await SwapLeaseAsync(previous.Value, default, ct).ConfigureAwait(false);
                    }
                    InvalidateLease();
                }
                finally
                {
                    m_storeGate.Release();
                }
            }
            catch (Exception ex)
            {
                m_logger.FailedToReleaseDistributedLease(ex, m_replicaId);
            }
        }

        private void ReserveOwnership(NodeId sessionId)
        {
            lock (m_stateLock)
            {
                m_reservedAt = m_timeProvider.GetTimestamp();
                m_reservationOwner = sessionId;
            }
        }

        private void ClearReservationIfOwner(NodeId sessionId)
        {
            lock (m_stateLock)
            {
                if (Utils.IsEqual(sessionId, m_reservationOwner))
                {
                    m_reservationOwner = NodeId.Null;
                }
            }
        }

        private bool HasReservation()
        {
            return !m_reservationOwner.IsNull &&
                m_timeProvider.GetElapsedTime(m_reservedAt) < m_reservationTimeout;
        }

        private LeaseConfirmation GetConfirmation()
        {
            lock (m_stateLock)
            {
                return m_confirmation;
            }
        }

        private void InvalidateLease(bool unavailable = true)
        {
            lock (m_stateLock)
            {
                m_confirmation = default;
                m_unavailable = unavailable;
                m_reservationOwner = NodeId.Null;
            }
        }

        private bool IsLive(LeaseConfirmation proof)
        {
            return !proof.Value.IsNull && NowTicks() < proof.Expiry &&
                m_timeProvider.GetElapsedTime(proof.Timestamp) < m_leaseDuration;
        }

        private async ValueTask<bool> TryWriteLeaseAsync(
            ByteString expected,
            LeaseConfirmation? previous,
            CancellationToken ct)
        {
            long timestamp = m_timeProvider.GetTimestamp();
            long now = NowTicks();
            if ((previous.HasValue && !IsLive(previous.Value)) ||
                now > DateTimeOffset.MaxValue.UtcTicks - m_leaseDuration.Ticks)
            {
                return false;
            }
            long expiry = now + m_leaseDuration.Ticks;
            ByteString next = EncodeLease(now, expiry);
            if (!await SwapLeaseAsync(expected, next, ct).ConfigureAwait(false))
            {
                return false;
            }
            ct.ThrowIfCancellationRequested();
            if (m_leaderElection?.IsLeader == false)
            {
                return false;
            }
            var proof = new LeaseConfirmation(next, timestamp, expiry);
            lock (m_stateLock)
            {
                if (m_disposed || !IsLive(proof) || (previous.HasValue && !IsLive(previous.Value)))
                {
                    return false;
                }
                m_confirmation = proof;
                m_unavailable = false;
                return true;
            }
        }

        private Task<bool> ConfirmLeadershipAsync(CancellationToken ct)
        {
            return m_leaderElection == null
                ? Task.FromResult(true)
                : m_leaderElection.TryAcquireOrRenewAsync(ct).AsTask().WaitAsync(ct);
        }

        private Task<(bool Found, ByteString Value)> ReadLeaseAsync(CancellationToken ct)
        {
            return m_store.TryGetAsync(m_leaseKey, ct).AsTask().WaitAsync(ct);
        }

        private Task<bool> SwapLeaseAsync(ByteString expected, ByteString value, CancellationToken ct)
        {
            return m_store.CompareAndSwapAsync(m_leaseKey, expected, value, ct).AsTask().WaitAsync(ct);
        }

        private static void ThrowTransactionPending()
        {
            throw new ServiceResultException(
                StatusCodes.BadTransactionPending,
                "This replica could not confirm ownership of the PushManagement transaction lease.");
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

        private long NowTicks()
        {
            return m_timeProvider.GetUtcNow().UtcTicks;
        }

        private ByteString EncodeLease(long issuedTicks, long expiryTicks)
        {
            byte[] buffer = new byte[sizeof(int) + m_ownerBytes.Length + kLeaseFieldsLength];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, m_ownerBytes.Length);
            m_ownerBytes.CopyTo(buffer, sizeof(int));
            int offset = sizeof(int) + m_ownerBytes.Length;
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset), issuedTicks);
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(offset + sizeof(long)), expiryTicks);
            Guid.NewGuid().ToByteArray().CopyTo(buffer, offset + 2 * sizeof(long));
            return new ByteString(buffer);
        }

        private bool IsVacant(ByteString raw, long nowTicks)
        {
            ReadOnlySpan<byte> bytes = raw.Span;
            int ownerSize = bytes.Length - sizeof(int) - kLeaseFieldsLength;
            if (ownerSize <= 0 || BinaryPrimitives.ReadInt32LittleEndian(bytes) != ownerSize)
            {
                return false;
            }
            string owner;
            try
            {
                owner = s_leaseEncoding.GetString(bytes.Slice(sizeof(int), ownerSize).ToArray());
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(owner) || (m_memberIds != null && !m_memberIds.Contains(owner)))
            {
                return false;
            }
            ReadOnlySpan<byte> fields = bytes[(sizeof(int) + ownerSize)..];
            long issuedTicks = BinaryPrimitives.ReadInt64LittleEndian(fields);
            long expiryTicks = BinaryPrimitives.ReadInt64LittleEndian(fields[sizeof(long)..]);
            return !fields[(2 * sizeof(long))..].SequenceEqual(s_emptyGuid) &&
                issuedTicks >= 0 && expiryTicks > issuedTicks && expiryTicks <= nowTicks &&
                expiryTicks - issuedTicks <= m_leaseDuration.Ticks;
        }

        private static FreshnessProtectedKeyValueStore RequireProtectedStore(ISharedKeyValueStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }
            return store as FreshnessProtectedKeyValueStore ??
                throw new ArgumentException(
                    "Distributed PushManagement requires a freshness-protected store.", nameof(store));
        }

        private const int kLeaseFieldsLength = 2 * sizeof(long) + 16;
        private static readonly byte[] s_emptyGuid = Guid.Empty.ToByteArray();
        private static readonly UTF8Encoding s_leaseEncoding = new(false, true);
        private readonly IPushConfigurationTransactionCoordinator m_inner;
        private readonly ISharedKeyValueStore m_store;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly ILeaderElection? m_leaderElection;
        private readonly string m_replicaId;
        private readonly byte[] m_ownerBytes;
        private readonly HashSet<string>? m_memberIds;
        private readonly string m_leaseKey;
        private readonly TimeSpan m_leaseDuration;
        private readonly TimeSpan m_renewInterval;
        private readonly TimeSpan m_reservationTimeout;
        private readonly Lock m_stateLock = new();
        private readonly SemaphoreSlim m_storeGate = new(1, 1);
        private readonly SemaphoreSlim m_wake = new(0, 1);
        private readonly CancellationTokenSource m_cts = new();
        private readonly Task? m_loop;
        private LeaseConfirmation m_confirmation;
        private bool m_unavailable;
        private long m_reservedAt;
        private NodeId m_reservationOwner = NodeId.Null;
        private bool m_disposed;

        private readonly record struct LeaseConfirmation(ByteString Value, long Timestamp, long Expiry);
    }

    internal static partial class DistributedPushConfigurationTransactionCoordinatorLog
    {
        [LoggerMessage(
            EventId = RedundancyServerEventIds.DistributedPushConfigurationTransactionCoordinator + 0,
            Level = LogLevel.Error,
            Message = "Failed to reconcile the distributed PushManagement lease after ApplyChanges for {Replica}.")]
        public static partial void FailedToReconcileLeaseAfterApplyChanges(
            this ILogger logger,
            Exception ex,
            string replica);

        [LoggerMessage(
            EventId = RedundancyServerEventIds.DistributedPushConfigurationTransactionCoordinator + 1,
            Level = LogLevel.Error,
            Message = "Distributed PushManagement lease reconcile failed for {Replica}.")]
        public static partial void DistributedLeaseReconcileFailed(
            this ILogger logger,
            Exception ex,
            string replica);

        [LoggerMessage(
            EventId = RedundancyServerEventIds.DistributedPushConfigurationTransactionCoordinator + 2,
            Level = LogLevel.Error,
            Message = "Failed to release the distributed PushManagement lease for {Replica}.")]
        public static partial void FailedToReleaseDistributedLease(
            this ILogger logger,
            Exception ex,
            string replica);
    }
}
