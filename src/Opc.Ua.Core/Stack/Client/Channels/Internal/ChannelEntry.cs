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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;
using ChannelCloseReason = Opc.Ua.ClientChannelManager.ChannelCloseReason;

namespace Opc.Ua
{
    internal sealed class ChannelEntry : IAsyncDisposable
    {
        public ChannelEntry(
            IChannelEntryHost host,
            ManagedChannelKey key,
            ConfiguredEndpoint endpoint,
            ITransportWaitingConnection? reverseConnection)
        {
            OwnerManager = host;
            Key = key;
            Endpoint = endpoint;
            MessageContext = host.Configuration.CreateMessageContext();
            m_reverseConnection = reverseConnection;
            m_lastStateChange = host.TimeProvider.GetUtcNow();
            m_readyGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ManagedChannelKey Key { get; }
        public ConfiguredEndpoint Endpoint { get; }

        public ITransportWaitingConnection? ReverseConnection
        {
            get
            {
                lock (m_lock)
                {
                    return m_reverseConnection;
                }
            }
        }

        public IChannelEntryHost OwnerManager { get; }
        public IServiceMessageContext MessageContext { get; }

        public string EndpointUrl => Key.EndpointUrl;

        public bool IsReverse => ReverseConnection != null;

        public int RefCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_refcount;
                }
            }
        }

        public int ParticipantCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_leases.Count(l => l.IsActive);
                }
            }
        }

        public ChannelState State
        {
            get
            {
                lock (m_lock)
                {
                    return m_state;
                }
            }
        }

        /// <summary>
        /// Whether the entry is closed, faulted, or being torn down. A
        /// teardown clears the underlying channel before the state reaches
        /// <see cref="ChannelState.Closed"/>, so the state alone does not
        /// tell whether the entry can still hand out leases.
        /// </summary>
        public bool IsClosing
        {
            get
            {
                lock (m_lock)
                {
                    return IsClosingLocked;
                }
            }
        }

        private bool IsClosingLocked
            => m_closing || m_state is ChannelState.Closed or ChannelState.Faulted;

        /// <summary>
        /// Monotonic counter incremented each time the entry enters
        /// <see cref="ChannelState.TransportReconnecting"/>. Callers capture
        /// it before sending a request and compare afterwards to tell a
        /// genuinely undetected transport drop (generation unchanged) apart
        /// from a stale in-flight failure that arrives after the shared
        /// channel has already begun (or finished) reconnecting.
        /// </summary>
        internal long ReconnectGeneration => Interlocked.Read(ref m_reconnectGeneration);

        /// <summary>
        /// Gets the currently installed transport without transferring its ownership from this entry.
        /// </summary>
        public ITransportChannel? Underlying
        {
            get
            {
                lock (m_lock)
                {
                    return m_underlying?.Channel;
                }
            }
        }

        public event Action<IManagedTransportChannel, ChannelStateChange>? StateChanged;

        /// <summary>
        /// Acquires an independent snapshot of the certificate material actually installed on this entry's transport.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal ClientChannelCertificateSnapshot SnapshotClientCertificate()
        {
            lock (m_lock)
            {
                OwnedTransport transport = m_underlying ??
                    throw new ServiceResultException(
                        StatusCodes.BadSecureChannelClosed,
                        "The managed transport is closed.");
                return new ClientChannelCertificateSnapshot(
                    transport.Certificates.Certificate,
                    transport.Certificates.Chain,
                    m_clientCertificateVersion);
            }
        }

        /// <summary>
        /// Open the initial transport channel. Called once per
        /// entry before any lease is handed out.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task OpenInitialAsync(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            long clientCertificateVersion,
            CancellationToken ct)
        {
            TransitionTo(ChannelState.TransportConnecting, error: null, attempt: 0);

            try
            {
                OwnedTransport channel = await CreateTransportChannelAsync(
                    clientCertificate, clientCertificateChain, clientCertificateVersion, ct)
                    .ConfigureAwait(false);
                bool entryClosed;
                bool channelInstalled;
                bool recordActiveMetric;
                lock (m_lock)
                {
                    entryClosed = IsClosingLocked;
                    channelInstalled = !entryClosed && m_underlying == null;
                    if (channelInstalled)
                    {
                        m_underlying = channel;
                        m_clientCertificateVersion = clientCertificateVersion;
                    }

                    recordActiveMetric = !entryClosed && !m_activeMetricRecorded;
                    if (recordActiveMetric)
                    {
                        m_activeMetricRecorded = true;
                    }
                }
                if (entryClosed)
                {
                    await CloseTransportBestEffortAsync(channel).ConfigureAwait(false);
                    OwnerManager.OnEntryClosed(this, ChannelCloseReason.Faulted);
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel is {0}.",
                        State);
                }

                if (recordActiveMetric)
                {
                    OwnerManager.RecordChannelActiveChanged(this, 1);
                }

                if (!channelInstalled)
                {
                    // A reconnect cycle can start while this initial open awaits
                    // network I/O. If that cycle installs a newer transport first,
                    // keep it and dispose this losing candidate instead of
                    // overwriting the only reference to the reconnect transport.
                    await CloseTransportBestEffortAsync(channel).ConfigureAwait(false);
                    OwnerManager.OnEntryClosed(this, ChannelCloseReason.Faulted);
                    return;
                }

                TransitionTo(ChannelState.Ready, error: null, attempt: 0);
                SignalReady();
            }
            catch (Exception ex)
            {
                TransitionTo(
                    ChannelState.Faulted,
                    new ServiceResult(ex),
                    attempt: 0);
                FailReady(ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    ex,
                    "The shared channel failed during initial opening."));
                throw;
            }
        }

        /// <summary>
        /// Acquire a new lease for the supplied participant. The
        /// caller is responsible for disposing the returned
        /// lease.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="participant"/> is <c>null</c>.</exception>
        public ManagedTransportChannelLease AcquireLease(IReconnectParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            return AcquireLease(_ => participant);
        }

        /// <summary>
        /// Acquire a new lease and atomically bind the participant
        /// returned by <paramref name="participantFactory"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="participantFactory"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public ManagedTransportChannelLease AcquireLease(
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory)
        {
            if (participantFactory == null)
            {
                throw new ArgumentNullException(nameof(participantFactory));
            }

            ManagedTransportChannelLease lease;
            int refCount;
            int participantCount;
            string participantId;
            lock (m_lock)
            {
                if (IsClosingLocked)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Cannot attach to a {0} channel.",
                        m_state);
                }

                lease = new ManagedTransportChannelLease(this, participantFactory);
                m_leases.Add(lease);
                m_refcount++;
                refCount = m_refcount;
                participantCount = m_leases.Count(l => l.IsActive);
                participantId = lease.Participant.Id;
            }

            OwnerManager.OnEntryParticipantAttached(this, participantId, refCount, participantCount);
            return lease;
        }

        internal void ReattachParticipant(
            ManagedTransportChannelLease lease,
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }
            if (participantFactory == null)
            {
                throw new ArgumentNullException(nameof(participantFactory));
            }

            IReconnectParticipant participant = participantFactory(lease)
                ?? throw new InvalidOperationException("Participant factory returned null.");
            int refCount = 0;
            int participantCount = 0;
            ChannelState currentState;
            ServiceResult? currentError;
            int currentAttempt;
            bool attached = false;
            lock (m_lock)
            {
                if (IsClosingLocked)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Cannot attach to a {0} channel.",
                        m_state);
                }
                if (!lease.IsActive)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Cannot attach a released channel lease.");
                }

                if (!m_leases.Contains(lease))
                {
                    m_leases.Add(lease);
                    m_refcount++;
                    attached = true;
                }
                lease.SwapParticipantForEntry(participant);
                lease.SwapEntry(this);
                refCount = m_refcount;
                participantCount = m_leases.Count(l => l.IsActive);
                currentState = m_state;
                currentError = m_lastError;
                currentAttempt = m_lastReconnectAttempt;
            }

            if (attached)
            {
                OwnerManager.OnEntryParticipantAttached(this, participant.Id, refCount, participantCount);
                if (currentState != ChannelState.Disconnected)
                {
                    lease.RaiseStateChanged(new ChannelStateChange(
                        ChannelState.Disconnected,
                        currentState,
                        currentError,
                        currentAttempt));
                }
            }
        }

        internal async ValueTask DetachLeaseForSwapAsync(ManagedTransportChannelLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            bool detached = false;
            bool teardown = false;
            ChannelCloseReason reason = ChannelCloseReason.LeaseReleased;
            int refCount = 0;
            int participantCount = 0;
            string participantId = lease.Participant.Id;
            lock (m_lock)
            {
                if (m_leases.Remove(lease))
                {
                    m_refcount--;
                    refCount = m_refcount;
                    participantCount = m_leases.Count(l => l.IsActive);
                    detached = true;
                    teardown = m_refcount == 0 && m_operationRef == 0 && m_state != ChannelState.Closed;
                    if (teardown && m_state == ChannelState.Faulted)
                    {
                        reason = ChannelCloseReason.Faulted;
                    }
                }
            }

            if (!detached)
            {
                return;
            }

            OwnerManager.OnEntryParticipantDetached(this, participantId, refCount, participantCount);
            if (teardown)
            {
                await TearDownAsync(reason).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Release a lease. When refcount drops to zero and no
        /// reconnect operation is in progress, the underlying
        /// channel is torn down and the entry is removed from the
        /// manager registry.
        /// </summary>
        public async ValueTask ReleaseLeaseAsync(ManagedTransportChannelLease lease)
        {
            if (DetachLease(lease, out ChannelCloseReason reason))
            {
                await TearDownAsync(reason, onlyIfUnused: true).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Release a lease without blocking the caller: the lease stops
        /// counting against the channel before this returns, and only the
        /// teardown of an unused channel, which does network I/O and raises
        /// state change events, runs in the background.
        /// </summary>
        public void ReleaseLease(ManagedTransportChannelLease lease)
        {
            if (DetachLease(lease, out ChannelCloseReason reason))
            {
                OwnerManager.BackgroundWork.Run(
                    nameof(TearDownAsync),
                    async _ => await TearDownAsync(reason, onlyIfUnused: true).ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Removes the lease from the entry and reports whether the channel
        /// became unused and has to be torn down.
        /// </summary>
        private bool DetachLease(
            ManagedTransportChannelLease lease,
            out ChannelCloseReason reason)
        {
            bool teardown;
            int refCount;
            int participantCount;
            string participantId;
            reason = ChannelCloseReason.LeaseReleased;
            lock (m_lock)
            {
                if (!m_leases.Remove(lease))
                {
                    return false;
                }
                m_refcount--;
                refCount = m_refcount;
                participantCount = m_leases.Count(l => l.IsActive);
                participantId = lease.Participant.Id;
                teardown = m_refcount == 0 && m_operationRef == 0;
                if (m_refcount == 0 && m_reconnectDeadline != null)
                {
                    m_closing = true;
                    m_reconnectDeadline.Cancel();
                }
                if (teardown && m_state == ChannelState.Faulted)
                {
                    reason = ChannelCloseReason.Faulted;
                }
            }

            OwnerManager.OnEntryParticipantDetached(this, participantId, refCount, participantCount);
            return teardown;
        }

        /// <summary>
        /// Request a reconnect cycle. Multiple concurrent callers
        /// coalesce into one cycle.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When multiple callers request a reconnect concurrently, the
        /// first call starts a single underlying cycle and subsequent
        /// callers join the same cycle (they all share the resulting
        /// <see cref="Task{Boolean}"/>). The
        /// <see cref="IRetryBudget"/> supplied by EACH caller is
        /// coalesced into a single "effective" budget — the tightest of
        /// all participating budgets wins, so a joiner with a stricter
        /// deadline can shorten an in-flight cycle that the starter
        /// began without a budget (or with a looser one). A
        /// <see langword="null"/> budget is treated as "no constraint"
        /// and never tightens. The effective budget is consulted by
        /// the running cycle on every iteration and is cleared when
        /// the cycle completes.
        /// </para>
        /// </remarks>
        public Task<bool> RequestReconnectAsync(CancellationToken ct)
        {
            return RequestReconnectAsync(null, budget: null, ct);
        }

        /// <inheritdoc cref="RequestReconnectAsync(CancellationToken)"/>
        public Task<bool> RequestReconnectAsync(IRetryBudget? budget, CancellationToken ct)
        {
            return RequestReconnectAsync(null, budget, ct);
        }

        public Task<bool> RequestReconnectAsync(
            ITransportWaitingConnection? reverseConnection,
            IRetryBudget? budget,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(ct);
            }
            TaskCompletionSource<bool> tcs;
            ReconnectDeadline deadline;
            bool starter;
            lock (m_lock)
            {
                if (IsClosingLocked)
                {
                    return Task.FromException<bool>(
                        ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel is {0}.", m_state));
                }

                if (reverseConnection != null)
                {
                    m_reverseConnection = reverseConnection;
                }

                // Coalesce budgets: every caller's budget tightens the
                // running cycle. The cycle reads m_effectiveBudget on
                // each iteration so joiners can shorten an in-flight
                // cycle that the starter began with a looser (or null)
                // budget.
                m_effectiveBudget = TighterOf(m_effectiveBudget, budget);

                if (m_reconnectCoalescer != null)
                {
                    // already running — join it
                    tcs = m_reconnectCoalescer;
                    deadline = m_reconnectDeadline!;
                    starter = false;
                }
                else
                {
                    tcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    m_reconnectCoalescer = tcs;
                    deadline = new ReconnectDeadline(OwnerManager.TimeProvider, OwnerManager.ShutdownToken);
                    m_reconnectDeadline = deadline;
                    starter = true;
                    // hold an internal op ref so disposal during
                    // reconnect doesn't tear down the entry
                    m_operationRef++;
                }
            }

            try
            {
                deadline.Tighten(budget);
            }
            catch (Exception ex)
            {
                if (starter)
                {
                    _ = CompleteRejectedReconnectAsync(tcs, ex);
                    return tcs.Task.WaitAsync(ct);
                }
                return Task.FromException<bool>(ex);
            }
            if (starter)
            {
                // The cycle's own outcome is observed through tcs, but the manager
                // still has to know the work exists so disposal waits for it.
                if (!OwnerManager.BackgroundWork.Run(
                    nameof(RunReconnectCycleAsync),
                    async _ => await RunReconnectCycleAsync(tcs, deadline).ConfigureAwait(false)))
                {
                    // The manager is going away; nothing will run the cycle.
                    _ = CompleteRejectedReconnectAsync(tcs, ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel manager is shutting down."));
                }
            }

            return tcs.Task.WaitAsync(ct);
        }

        private async Task CompleteRejectedReconnectAsync(TaskCompletionSource<bool> completion, Exception error)
        {
            await DisposeReconnectDeadlineAsync().ConfigureAwait(false);
            lock (m_lock)
            {
                ReleaseReconnectOwnershipLocked();
            }
            completion.TrySetException(error);
        }

        private async ValueTask DisposeReconnectDeadlineAsync()
        {
            try
            {
                if (m_reconnectDeadline != null)
                {
                    await m_reconnectDeadline.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                OwnerManager.Logger?.ChannelReconnectCancellationFailed(ex);
            }
        }

        /// <summary>
        /// Whether the most recent cycle stopped on a policy, deadline or fatal-participant verdict,
        /// rather than losing a race against a concurrent close.
        /// </summary>
        /// <remarks>
        /// These stops leave the entry <see cref="ChannelState.Faulted"/> and surface
        /// the same <see cref="StatusCodes.BadSecureChannelClosed"/> to the caller, so
        /// the state and status code alone cannot tell them apart. Only a genuine race
        /// is worth retrying on a freshly swapped entry; retrying a deliberate stop
        /// would run a second, unbudgeted reconnect cycle behind the swap back-off and
        /// defeat the very limit that ended the first one.
        /// </remarks>
        internal bool ReconnectStoppedIntentionally
            => Volatile.Read(ref m_reconnectStoppedIntentionally) != 0;

        /// <summary>
        /// Consumes a pending retry-delay hint when the current transport supports server-provided backoff.
        /// </summary>
        private TimeSpan? ConsumeServerRetryAfterHint()
        {
            ITransportChannel? underlying;
            lock (m_lock)
            {
                underlying = m_underlying?.Channel;
            }

            return underlying is IServerRetryAfterHintProvider provider
                ? provider.ConsumeServerRetryAfterHint()
                : null;
        }

        private IRetryBudget? GetEffectiveBudget()
        {
            lock (m_lock)
            {
                return m_effectiveBudget;
            }
        }

        private static IRetryBudget? TighterOf(IRetryBudget? a, IRetryBudget? b)
        {
            // null = "no constraint" — the non-null side wins; if both
            // null the result stays null.
            if (a == null)
            {
                return b;
            }
            if (b == null)
            {
                return a;
            }
            if (ReferenceEquals(a, b))
            {
                return a;
            }

            // Compare current remaining time as a best-effort tightness
            // ordering. TryConsume reports current remaining without
            // consuming a fixed slice; on RetryBudget it starts the
            // monotonic clock on first call, which is the intended
            // semantic when a budget attaches to an in-flight cycle.
            bool aOk = a.TryConsume(out TimeSpan remA);
            bool bOk = b.TryConsume(out TimeSpan remB);
            if (!aOk)
            {
                return a;
            }
            if (!bOk)
            {
                return b;
            }
            return remA <= remB ? a : b;
        }

        /// <summary>
        /// Wait until the channel is ready, or until cancellation.
        /// </summary>
        public Task WaitForReadyAsync(CancellationToken ct)
        {
            TaskCompletionSource<bool> gate;
            lock (m_lock)
            {
                // A teardown in progress still reports Ready until it reaches
                // Closed, so the closing check has to come first.
                if (IsClosingLocked)
                {
                    return Task.FromException(
                        ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel is {0}.", m_state));
                }
                if (m_state == ChannelState.Ready)
                {
                    return Task.CompletedTask;
                }
                gate = m_readyGate;
            }
            return WaitForReadyCoreAsync(gate.Task, ct);
        }

        public ValueTask DisposeAsync()
        {
            return DisposeAsync(ChannelCloseReason.Faulted);
        }

        internal ManagedChannelDiagnostic GetDiagnosticSnapshot()
        {
            lock (m_lock)
            {
                return new ManagedChannelDiagnostic(
                    Key,
                    m_state,
                    m_refcount,
                    m_leases.Count(l => l.IsActive),
                    m_openedAt,
                    m_lastStateChange,
                    m_lastReconnectAttempt,
                    m_lastError);
            }
        }

        internal async ValueTask DisposeAsync(ChannelCloseReason reason)
        {
            List<ManagedTransportChannelLease> leases;
            Task<bool>? reconnect;
            lock (m_lock)
            {
                m_closing = true;
                leases = [.. m_leases];
                m_leases.Clear();
                m_refcount = 0;
                reconnect = m_reconnectCoalescer?.Task;
                m_reconnectDeadline?.Cancel();
            }
            foreach (ManagedTransportChannelLease lease in leases)
            {
                lease.MarkReleased();
                OwnerManager.OnEntryParticipantDetached(this, lease.Participant.Id, 0, 0);
            }
            if (reconnect != null)
            {
                try
                {
                    await reconnect.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Closing the entry cancels its in-flight cycle.
                }
                catch (Exception ex)
                {
                    OwnerManager.Logger?.ChannelReconnectCancellationFailed(ex);
                }
            }
            await DisposeReconnectDeadlineAsync().ConfigureAwait(false);
            await TearDownAsync(reason).ConfigureAwait(false);
        }

        private async Task WaitForReadyCoreAsync(Task readyTask, CancellationToken ct)
        {
            long startingTimestamp = OwnerManager.TimeProvider.GetTimestamp();
            try
            {
                await readyTask.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                OwnerManager.RecordGateWait(
                    this,
                    OwnerManager.TimeProvider.GetElapsedTime(startingTimestamp));
            }
        }

        private Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

#if NET8_0_OR_GREATER
            return Task.Delay(delay, OwnerManager.TimeProvider, ct);
#else
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled(ct);
            }

            var state = new DelayState();
            state.Initialize(OwnerManager.TimeProvider, delay, ct);

            return state.Task;
#endif
        }

        /// <summary>
        /// Closes the entry, fails readiness waiters, and releases its transport, certificate handles, and metrics.
        /// </summary>
        private async Task TearDownAsync(
            ChannelCloseReason reason,
            bool onlyIfUnused = false)
        {
            OwnedTransport? underlying;
            bool activeMetricRecorded;
            lock (m_lock)
            {
                if (m_state == ChannelState.Closed)
                {
                    return;
                }

                // A lease release decides to tear down in an earlier lock
                // region: a lease acquired or an operation started since then
                // keeps the channel.
                if (onlyIfUnused && (m_refcount != 0 || m_operationRef != 0))
                {
                    return;
                }

                // Reserve the teardown before the underlying channel is
                // cleared: leases and reconnects are refused from here on,
                // not only once the state reaches Closed below.
                m_closing = true;
                underlying = m_underlying;
                m_underlying = null;
                activeMetricRecorded = m_activeMetricRecorded;
                m_activeMetricRecorded = false;
            }

            TransitionTo(ChannelState.Closed, error: null, attempt: 0);
            FailReady(new ServiceResultException(
                StatusCodes.BadSecureChannelClosed,
                "Channel closed."));

            if (underlying != null)
            {
                await CloseTransportBestEffortAsync(underlying).ConfigureAwait(false);
                OwnerManager.OnEntryClosed(this, reason);
            }

            if (activeMetricRecorded)
            {
                OwnerManager.RecordChannelActiveChanged(this, -1);
            }

            OwnerManager.RemoveEntryIfPresent(Key, this);
        }

        /// <summary>
        /// Publishes the reconnect result only after the completed cycle releases
        /// its coalescer and operation reference.
        /// </summary>
        private async Task RunReconnectCycleAsync(
            TaskCompletionSource<bool> tcs,
            ReconnectDeadline deadline)
        {
            try
            {
                bool reconnected = await ReconnectCycleAsync(deadline).ConfigureAwait(false);
                tcs.TrySetResult(reconnected);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        /// <summary>
        /// Reconnects the transport and its participants within the retry policy while rejecting superseded
        /// certificates.
        /// </summary>
        private async Task<bool> ReconnectCycleAsync(ReconnectDeadline deadline)
        {
            using Activity? activity = OwnerManager.StartReconnectActivity(this);
            long startingTimestamp = OwnerManager.TimeProvider.GetTimestamp();
            string finalOutcome = kReconnectOutcomeTransientFailure;
            ServiceResult? finalError = null;
            int attemptsStarted = 0;
            int terminalAttempt = 0;
            bool terminal = false;

            // A fresh cycle has not yet decided to stop, so any stale verdict from
            // an earlier cycle on this entry must not leak into it.
            Volatile.Write(ref m_reconnectStoppedIntentionally, 0);

            CancellationToken cycleToken = deadline.Token;

            async Task StopWithFaultAsync(
                ServiceResult error,
                int failedAttempt)
            {
                finalError = error;
                terminal = true;
                terminalAttempt = failedAttempt;

                // Record before completing the waiters: this is a deliberate stop
                // (the retry policy or the caller's budget said so), not a lost race
                // against a concurrent close, and callers inspect the flag as soon
                // as the reconnect result completes.
                Volatile.Write(ref m_reconnectStoppedIntentionally, 1);

                await NotifyParticipantsFinalAsync(cycleToken).ConfigureAwait(false);
                finalOutcome = kReconnectOutcomePolicyExhausted;
                OwnerManager.RecordReconnectAttempt(this, finalOutcome);
            }

            try
            {
                IReconnectBudgetParticipant[] budgetParticipants;
                lock (m_lock)
                {
                    budgetParticipants = [.. m_leases.Where(lease => lease.IsActive)
                        .Select(lease => lease.Participant).OfType<IReconnectBudgetParticipant>()];
                }
                foreach (IReconnectBudgetParticipant participant in budgetParticipants)
                {
                    IRetryBudget? participantBudget = participant.CreateReconnectBudget(OwnerManager.TimeProvider);
                    deadline.Tighten(participantBudget);
                    lock (m_lock)
                    {
                        m_effectiveBudget = TighterOf(m_effectiveBudget, participantBudget);
                    }
                }

                int attempt = 0;
                while (true)
                {
                    deadline.ThrowIfCancellationRequested();
                    // Re-read the effective budget every iteration so a
                    // late joiner can tighten an in-flight cycle.
                    IRetryBudget? budget = GetEffectiveBudget();
                    if (budget != null && budget.IsExhausted)
                    {
                        var error = ServiceResult.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel reconnect budget exhausted after {0} attempts.",
                            attempt);
                        await StopWithFaultAsync(
                                error,
                                attempt)
                            .ConfigureAwait(false);
                        return false;
                    }

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
                    TimeSpan delay = OwnerManager.ReconnectPolicy.GetDelay(attempt, budget);
#else
                    TimeSpan delay = ChannelReconnectPolicyBudget.GetDelay(
                        OwnerManager.ReconnectPolicy,
                        attempt,
                        budget);
#endif
                    TimeSpan? serverRetryAfter = ConsumeServerRetryAfterHint();
                    delay = RetryAfterHint.ApplyReconnectDelayLowerBound(
                        OwnerManager.ReconnectPolicy,
                        delay,
                        serverRetryAfter);
                    delay = ChannelReconnectPolicyBudget.ClampDelayToBudget(delay, budget);

                    if (delay < TimeSpan.Zero)
                    {
                        var error = ServiceResult.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel reconnect policy exhausted after {0} attempts.",
                            attempt);
                        await StopWithFaultAsync(
                                error,
                                attempt)
                            .ConfigureAwait(false);
                        return false;
                    }

                    attemptsStarted++;
                    TransitionTo(
                        ChannelState.TransportReconnecting,
                        error: null,
                        attempt);
                    ResetReadyGate();

                    if (delay > TimeSpan.Zero)
                    {
                        try
                        {
                            await DelayAsync(delay, cycleToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cycleToken.IsCancellationRequested)
                        {
                            // delay-cancelled by unrelated source — ignore and try
                        }
                    }

                    // Re-read after the delay: a joiner that arrived
                    // while we were sleeping may have tightened the
                    // budget, or the existing budget may have expired.
                    deadline.ThrowIfCancellationRequested();
                    budget = GetEffectiveBudget();
                    if (budget != null && budget.IsExhausted)
                    {
                        var error = ServiceResult.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Channel reconnect budget exhausted after {0} attempts.",
                            attempt);
                        await StopWithFaultAsync(
                                error,
                                attempt)
                            .ConfigureAwait(false);
                        return false;
                    }

                    try
                    {
                        Task transport = EnsureTransportConnectedAsync(cycleToken);
                        ObserveRecoveryTask(transport);
                        await transport.WaitAsync(cycleToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ServiceResult error = new(ex);
                        OwnerManager.Logger?.ChannelEntryLog2(ex, attempt);
                        OwnerManager.OnEntryReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                        attempt++;
                        continue;
                    }

                    deadline.ThrowIfCancellationRequested();
                    TransitionTo(
                        ChannelState.TransportConnectedSessionReactivating,
                        error: null,
                        attempt);

                    AggregatedReactivationOutcome outcome;
                    try
                    {
                        outcome = await NotifyParticipantsAsync(attempt, cycleToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ServiceResult error = new(ex);
                        OwnerManager.Logger?.ChannelEntryLog3(ex, attempt);
                        OwnerManager.OnEntryReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                        attempt++;
                        continue;
                    }

                    if (outcome.FatalForChannel)
                    {
                        Volatile.Write(ref m_reconnectStoppedIntentionally, 1);
                        finalError = ServiceResult.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "Participant signaled fatal channel error.");
                        terminal = true;
                        terminalAttempt = attempt;
                        await NotifyParticipantsFinalAsync(cycleToken).ConfigureAwait(false);
                        finalOutcome = kReconnectOutcomeFatalChannel;
                        OwnerManager.RecordReconnectAttempt(this, finalOutcome);
                        return false;
                    }

                    if (outcome.AnyTransient)
                    {
                        var error = ServiceResult.Create(
                            StatusCodes.BadSecureChannelClosed,
                            outcome.AnyTransient
                                ? "Participant signaled transient channel reconnect failure."
                                : "Participant signaled transient channel reconnect failure.");
                        OwnerManager.OnEntryReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                        if (outcome.AnyTransient)
                        {
                            attempt++;
                        }
                        continue;
                    }

                    using (ClientChannelCertificateSnapshot installed = SnapshotClientCertificate())
                    using (ClientChannelCertificateSnapshot current =
                        OwnerManager.SnapshotClientCertificate(installed))
                    {
                        if (!ClientChannelCertificateSnapshot.HaveSameMaterial(
                            installed.Certificate, installed.Chain, current.Certificate, current.Chain))
                        {
                            continue;
                        }
                    }

                    deadline.ThrowIfCancellationRequested();
                    TransitionTo(ChannelState.Ready, error: null, attempt);
                    SignalReady();
                    try
                    {
                        await CompleteParticipantRecoveryAsync(cycleToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cycleToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ResetReadyGate();
                        OwnerManager.Logger?.ChannelEntryLog3(ex, attempt);
                        OwnerManager.OnEntryReconnectFailed(
                            this, attempt, kReconnectOutcomeTransientFailure, new ServiceResult(ex));
                        attempt++;
                        continue;
                    }
                    if (!deadline.TryComplete())
                    {
                        throw new OperationCanceledException(cycleToken);
                    }
                    finalOutcome = kReconnectOutcomeSuccess;
                    OwnerManager.RecordReconnectAttempt(this, finalOutcome);
                    return true;
                }
            }
            catch (OperationCanceledException) when (
                deadline.Expired && !OwnerManager.ShutdownToken.IsCancellationRequested)
            {
                terminal = true;
                terminalAttempt = attemptsStarted;
                Volatile.Write(ref m_reconnectStoppedIntentionally, 1);
                finalError = ServiceResult.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel recovery deadline expired after {0}.",
                    deadline.Elapsed);
                finalOutcome = kReconnectOutcomeDeadlineExpired;
                OwnerManager.Logger?.ChannelReconnectDeadlineExpired(deadline.Duration, deadline.Elapsed, State);
                await NotifyParticipantsFinalAsync(cycleToken).ConfigureAwait(false);
                OwnerManager.RecordReconnectAttempt(this, finalOutcome);
                return false;
            }
            catch (Exception ex)
            {
                finalError = new ServiceResult(ex);
                throw;
            }
            finally
            {
                await DisposeReconnectDeadlineAsync().ConfigureAwait(false);

                if (terminal)
                {
                    TransitionTo(
                        ChannelState.Faulted, finalError, terminalAttempt, releaseReconnectOwnership: true);
                }
                else
                {
                    lock (m_lock)
                    {
                        ReleaseReconnectOwnershipLocked();
                    }
                }
                OwnerManager.CompleteReconnectActivity(activity, this, attemptsStarted, finalOutcome, finalError);
                OwnerManager.RecordReconnectDuration(
                    this,
                    OwnerManager.TimeProvider.GetElapsedTime(startingTimestamp),
                    finalOutcome);

                bool teardown = false;
                ChannelCloseReason teardownReason = ChannelCloseReason.LeaseReleased;
                lock (m_lock)
                {
                    teardown = m_refcount == 0 &&
                        m_operationRef == 0 &&
                        m_state != ChannelState.Closed;
                    if (teardown && m_state == ChannelState.Faulted)
                    {
                        teardownReason = ChannelCloseReason.Faulted;
                    }
                }
                if (teardown)
                {
                    await TearDownAsync(teardownReason, onlyIfUnused: true).ConfigureAwait(false);
                }
            }
        }

        private void ReleaseReconnectOwnershipLocked()
        {
            m_reconnectCoalescer = null;
            m_reconnectDeadline = null;
            m_effectiveBudget = null;
            m_operationRef--;
        }

        /// <summary>
        /// Reconnects a reusable transport or replaces it when the current certificate configuration has changed.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async Task EnsureTransportConnectedAsync(CancellationToken ct)
        {
            OwnedTransport? underlying;
            ClientChannelCertificateSnapshot certificates;
            lock (m_lock)
            {
                underlying = m_underlying;
                certificates = underlying == null
                    ? OwnerManager.SnapshotClientCertificate()
                    : OwnerManager.SnapshotClientCertificate(underlying.Certificates);
            }

            using (certificates)
            {
                if (underlying != null &&
                    ClientChannelCertificateSnapshot.HaveSameMaterial(
                        underlying.Certificates.Certificate,
                        underlying.Certificates.Chain,
                        certificates.Certificate,
                        certificates.Chain) &&
                    (underlying.Channel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                {
                    try
                    {
                        await underlying.Channel.ReconnectAsync(ReverseConnection, ct).ConfigureAwait(false);
                        if (ct.IsCancellationRequested)
                        {
                            await CloseTransportBestEffortAsync(underlying).ConfigureAwait(false);
                        }
                        ct.ThrowIfCancellationRequested();
                        MarkOpened();
                        return;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        OwnerManager.Logger?.ChannelEntryLog4(ex);
                    }
                }

                OwnedTransport fresh = await CreateTransportChannelAsync(
                    certificates.Certificate, certificates.Chain, certificates.Version, ct).ConfigureAwait(false);

                OwnedTransport? old;
                bool entryClosed;
                lock (m_lock)
                {
                    entryClosed = IsClosingLocked || ct.IsCancellationRequested;
                    if (entryClosed)
                    {
                        old = null;
                    }
                    else
                    {
                        old = m_underlying;
                        m_underlying = fresh;
                        m_clientCertificateVersion = certificates.Version;
                    }
                }
                if (entryClosed)
                {
                    await CloseTransportBestEffortAsync(fresh).ConfigureAwait(false);
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel is {0}.",
                        State);
                }
                if (old != null)
                {
                    await CloseTransportBestEffortAsync(old).ConfigureAwait(false);
                    OwnerManager.OnEntryClosed(this, ChannelCloseReason.Faulted);
                }
            }
        }

        /// <summary>
        /// Attempts both transport close paths and releases retained certificate material even if either close fails.
        /// </summary>
        private async ValueTask CloseTransportBestEffortAsync(OwnedTransport transport)
        {
            using ClientChannelCertificateSnapshot certificates = transport.Certificates;
            try
            {
                await transport.Channel.CloseAsync(default).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OwnerManager.Logger?.ChannelEntryLog0(ex);
            }
            try
            {
                OwnerManager.CloseChannel(transport.Channel);
            }
            catch (Exception ex)
            {
                OwnerManager.Logger?.ChannelEntryLog1(ex);
            }
        }

#if !NET8_0_OR_GREATER
        private sealed class DelayState
        {
            public Task Task => m_taskSource.Task;

            public void Initialize(TimeProvider timeProvider, TimeSpan delay, CancellationToken ct)
            {
                m_timer = timeProvider.CreateTimer(
                    static state => ((DelayState)state!).Complete(),
                    this,
                    delay,
                    Timeout.InfiniteTimeSpan);

                if (ct.CanBeCanceled)
                {
                    m_registration = ct.Register(
                        static state => ((DelayState)state!).Cancel(),
                        this,
                        useSynchronizationContext: false);
                }
            }

            private void Complete()
            {
                if (m_taskSource.TrySetResult(true))
                {
                    DisposeResources();
                }
            }

            private void Cancel()
            {
                if (m_taskSource.TrySetCanceled())
                {
                    DisposeResources();
                }
            }

            private void DisposeResources()
            {
                m_registration.Dispose();
                m_timer?.Dispose();
            }

            private readonly TaskCompletionSource<bool> m_taskSource = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

            private CancellationTokenRegistration m_registration;
            private ITimer? m_timer;
        }
#endif

        /// <summary>
        /// Opens a transport with independently retained certificate handles and cleans up ownership on failure.
        /// </summary>
        private async Task<OwnedTransport> CreateTransportChannelAsync(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain,
            long certificateVersion,
            CancellationToken ct)
        {
            ClientChannelCertificateSnapshot? certificates = new(
                clientCertificate, clientCertificateChain, certificateVersion);
            OwnedTransport? transport = null;
            try
            {
                ITransportChannel channel = await OwnerManager.CreateChannelAsync(
                    Endpoint,
                    MessageContext,
                    certificates.Certificate,
                    certificates.Chain,
                    ReverseConnection,
                    ct).ConfigureAwait(false);
                transport = new OwnedTransport(channel, certificates);
                certificates = null;
                if (IsClosing)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed, "Channel is {0}.", State);
                }
                ct.ThrowIfCancellationRequested();
                MarkOpened();
                OwnerManager.OnEntryOpened(this);
                return transport;
            }
            catch
            {
                if (transport != null)
                {
                    await CloseTransportBestEffortAsync(transport).ConfigureAwait(false);
                }
                throw;
            }
            finally
            {
                certificates?.Dispose();
            }
        }

        private void MarkOpened()
        {
            lock (m_lock)
            {
                m_openedAt = OwnerManager.TimeProvider.GetUtcNow();
            }
        }

        /// <summary>
        /// Reactivates a snapshot of active lease participants and aggregates their timeout and failure outcomes.
        /// </summary>
        private async Task<AggregatedReactivationOutcome> NotifyParticipantsAsync(
            int attempt, CancellationToken ct)
        {
            ManagedTransportChannelLease[] snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_leases.Where(l => l.IsActive)];
            }

            if (snapshot.Length == 0)
            {
                return new AggregatedReactivationOutcome();
            }

            TimeSpan participantTimeout = ResolveParticipantTimeout(OwnerManager.ReconnectPolicy);
            Task<ParticipantReconnectResult>[] tasks = [.. snapshot.Select(lease => Task.Run(
                async () =>
                {
                    using var callback = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    using IManagedTransportChannel view = lease.CreateReactivationView(callback.Token);
                    Task<ParticipantReconnectResult>? reconnectTask = null;
                    try
                    {
                        reconnectTask =
                            lease.Participant is IChannelRecoveryParticipant recovery
                                ? recovery.OnReconnectAsync(lease, view, attempt, callback.Token).AsTask()
                                : lease.Participant.OnReconnectAsync(view, attempt, callback.Token).AsTask();
                        ObserveRecoveryTask(reconnectTask);
                        ParticipantReconnectResult result = participantTimeout == Timeout.InfiniteTimeSpan
                            ? await reconnectTask.WaitAsync(ct).ConfigureAwait(false)
                            : await reconnectTask
                                .WaitAsync(participantTimeout, OwnerManager.TimeProvider, ct)
                                .ConfigureAwait(false);
                        if (result != ParticipantReconnectResult.RequiresSessionRecreate)
                        {
                            return result;
                        }

                        if (lease.Participant is IChannelRecoveryParticipant)
                        {
                            view.Dispose();
                            await callback.CancelAsync().ConfigureAwait(false);
                        }
                        // Legacy RecreateAsync has no channel parameter and uses the view supplied above.
                        bool recreated = await RecreateParticipantAsync(lease, participantTimeout, ct)
                            .ConfigureAwait(false);
                        return recreated
                            ? ParticipantReconnectResult.Reactivated
                            : ParticipantReconnectResult.TransientFailure;
                    }
                    catch (TimeoutException)
                    {
                        OwnerManager.Logger?
                            .ChannelEntryLog5(
                                lease.Participant.Id,
                                participantTimeout);
                        OwnerManager.RecordParticipantTimeout(this, lease.Participant.Id);
                        return ParticipantReconnectResult.TransientFailure;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        OwnerManager.Logger?
                            .ChannelEntryLog6(
                                ex,
                                lease.Participant.Id);
                        return ParticipantReconnectResult.TransientFailure;
                    }
                    finally
                    {
                        await CancelParticipantWorkAsync(lease.Participant, callback, reconnectTask)
                            .ConfigureAwait(false);
                    }
                }, ct))];

            ParticipantReconnectResult[] results = await Task.WhenAll(tasks)
                .ConfigureAwait(false);

            var outcome = new AggregatedReactivationOutcome();
            for (int i = 0; i < results.Length; i++)
            {
                switch (results[i])
                {
                    case ParticipantReconnectResult.FatalForChannel:
                        outcome.FatalForChannel = true;
                        break;
                    case ParticipantReconnectResult.TransientFailure:
                        outcome.AnyTransient = true;
                        break;
                    case ParticipantReconnectResult.FatalForParticipant:
                        snapshot[i].MarkReleased();
                        int refCount = 0;
                        int participantCount = 0;
                        bool detached = false;
                        lock (m_lock)
                        {
                            if (m_leases.Remove(snapshot[i]))
                            {
                                m_refcount--;
                                refCount = m_refcount;
                                participantCount = m_leases.Count(l => l.IsActive);
                                detached = true;
                            }
                        }
                        if (detached)
                        {
                            OwnerManager.OnEntryParticipantDetached(
                                this,
                                snapshot[i].Participant.Id,
                                refCount,
                                participantCount);
                        }
                        break;
                }
            }
            return outcome;
        }

        private async Task<bool> RecreateParticipantAsync(
            ManagedTransportChannelLease lease,
            TimeSpan timeout,
            CancellationToken ct)
        {
            IReconnectParticipant participant = lease.Participant;
            using var callback = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using ITransportChannel? view = participant is IChannelRecoveryParticipant
                ? lease.CreateReactivationView(callback.Token)
                : null;
            Task? work = null;
            try
            {
                work = participant is IChannelRecoveryParticipant recovery
                    ? recovery.RecreateAsync(lease, view!, callback.Token).AsTask()
                    : ResolveRecreateInvocation(participant, callback.Token).AsTask();
                await AwaitParticipantWorkAsync(work, timeout, ct).ConfigureAwait(false);
                OwnerManager.RecordParticipantRecreate(this, participant.Id, success: true);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                OwnerManager.RecordParticipantTimeout(this, participant.Id);
                OwnerManager.Logger?.ChannelEntryLog7(ex, participant.Id);
                OwnerManager.RecordParticipantRecreate(this, participant.Id, success: false);
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                OwnerManager.Logger?.ChannelEntryLog7(ex, participant.Id);
                OwnerManager.RecordParticipantRecreate(this, participant.Id, success: false);
                return false;
            }
            finally
            {
                await CancelParticipantWorkAsync(participant, callback, work).ConfigureAwait(false);
            }
        }

        private async Task CompleteParticipantRecoveryAsync(CancellationToken ct)
        {
            IChannelRecoveryParticipant[] participants;
            lock (m_lock)
            {
                participants = [.. m_leases.Where(lease => lease.IsActive)
                    .Select(lease => lease.Participant).OfType<IChannelRecoveryParticipant>()];
            }
            TimeSpan timeout = ResolveParticipantTimeout(OwnerManager.ReconnectPolicy);
            await Task.WhenAll(participants.Select(async participant =>
            {
                using var callback = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task? work = null;
                try
                {
                    work = participant.CompleteRecoveryAsync(callback.Token).AsTask();
                    await AwaitParticipantWorkAsync(work, timeout, ct).ConfigureAwait(false);
                }
                finally
                {
                    await CancelParticipantWorkAsync(participant, callback, work).ConfigureAwait(false);
                }
            })).ConfigureAwait(false);
        }

        private async Task AwaitParticipantWorkAsync(Task work, TimeSpan timeout, CancellationToken ct)
        {
            ObserveRecoveryTask(work);
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                await work.WaitAsync(ct).ConfigureAwait(false);
            }
            else
            {
                await work.WaitAsync(timeout, OwnerManager.TimeProvider, ct).ConfigureAwait(false);
            }
        }

        private async Task CancelParticipantWorkAsync(
            IReconnectParticipant participant,
            CancellationTokenSource callback,
            Task? work)
        {
            try
            {
                await callback.CancelAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OwnerManager.Logger?.ChannelReconnectCancellationFailed(ex);
            }

            // Budget-aware participants promise to unwind local state before another owner can recover it.
            // Legacy callbacks may ignore cancellation; their expired send views remain quarantined instead.
            if (participant is IReconnectBudgetParticipant && work != null)
            {
                try
                {
                    await work.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (callback.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    OwnerManager.Logger?.ChannelEntryLog6(ex, participant.Id);
                }
            }
        }

        private static TimeSpan ResolveParticipantTimeout(IChannelReconnectPolicy policy)
        {
            TimeSpan timeout;
            if (policy is IParticipantTimeoutPolicy timeoutPolicy)
            {
                timeout = timeoutPolicy.ParticipantTimeout;
            }
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            else
            {
                timeout = policy.ParticipantTimeout;
            }
#else
            else
            {
                timeout = Timeout.InfiniteTimeSpan;
            }
#endif

            return timeout < TimeSpan.Zero ? Timeout.InfiniteTimeSpan : timeout;
        }

        private static ValueTask ResolveRecreateInvocation(
            IReconnectParticipant participant,
            CancellationToken ct)
        {
            if (participant is IRecreateAwareReconnectParticipant aware)
            {
                return aware.RecreateAsync(ct);
            }
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
            return participant.RecreateAsync(ct);
#else
            return new ValueTask();
#endif
        }

        internal static void ObserveRecoveryTask(Task task)
        {
            _ = task.ContinueWith(
                static faultedTask => _ = faultedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task NotifyParticipantsFinalAsync(CancellationToken ct)
        {
            ManagedTransportChannelLease[] snapshot;
            lock (m_lock)
            {
                snapshot = [.. m_leases.Where(l => l.IsActive)];
            }
            if (snapshot.Length == 0)
            {
                return;
            }
            TimeSpan timeout = ResolveParticipantTimeout(OwnerManager.ReconnectPolicy);
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                timeout = TimeSpan.FromSeconds(5);
            }
            Task[] tasks = [.. snapshot.Select(async lease =>
            {
                using var callback = CancellationTokenSource.CreateLinkedTokenSource(ct);
                CancellationToken callbackToken = callback.Token;
                try
                {
                    Task work = lease.Participant is IReconnectBudgetParticipant
                        ? lease.Participant.OnReconnectAsync(lease, -1, callbackToken).AsTask()
                        : Task.Run(() => lease.Participant.OnReconnectAsync(lease, -1, callbackToken).AsTask());
                    await AwaitParticipantWorkAsync(work, timeout, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    OwnerManager.Logger?.ChannelEntryLog6(ex, lease.Participant.Id);
                }
                finally
                {
                    await CancelParticipantWorkAsync(lease.Participant, callback, work: null).ConfigureAwait(false);
                }
            })];
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private void TransitionTo(
            ChannelState next,
            ServiceResult? error,
            int attempt,
            bool releaseReconnectOwnership = false)
        {
            ChannelState previous;
            IManagedTransportChannel[] subjects;
            Action<IManagedTransportChannel, ChannelStateChange>? handler;
            lock (m_lock)
            {
                if (releaseReconnectOwnership)
                {
                    ReleaseReconnectOwnershipLocked();
                }
                previous = m_state;
                if (previous == next ||
                    ((m_closing || previous is ChannelState.Closed or ChannelState.Faulted) &&
                        next != ChannelState.Closed))
                {
                    return;
                }
                m_state = next;
                if (next == ChannelState.Faulted)
                {
                    Interlocked.Increment(ref m_reconnectGeneration);
                    m_readyGate.TrySetException(new ServiceResultException(
                        StatusCodes.BadSecureChannelClosed, "Channel recovery failed."));
                    ObserveRecoveryTask(m_readyGate.Task);
                }
                if (next == ChannelState.TransportReconnecting)
                {
                    Interlocked.Increment(ref m_reconnectGeneration);
                }
                m_lastStateChange = OwnerManager.TimeProvider.GetUtcNow();
                m_lastReconnectAttempt = attempt;
                m_lastError = error;
                subjects = [.. m_leases];
                handler = StateChanged;
            }

            var change = new ChannelStateChange(previous, next, error, attempt);
            foreach (IManagedTransportChannel subject in subjects)
            {
                try
                {
                    handler?.Invoke(subject, change);
                    if (subject is ManagedTransportChannelLease lease)
                    {
                        lease.RaiseStateChanged(change);
                    }
                }
                catch
                {
                    // observer errors are isolated
                }
            }

            OwnerManager.OnEntryStateChanged(this, change);
        }

        private void SignalReady()
        {
            lock (m_lock)
            {
                m_readyGate.TrySetResult(true);
            }
        }

        private void FailReady(Exception ex)
        {
            lock (m_lock)
            {
                m_readyGate.TrySetException(ex);
                ObserveRecoveryTask(m_readyGate.Task);
            }
        }

        private void ResetReadyGate()
        {
            lock (m_lock)
            {
                if (m_readyGate.Task.IsCompleted)
                {
                    m_readyGate = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        private struct AggregatedReactivationOutcome
        {
            public bool AnyTransient;
            public bool FatalForChannel;
        }

        private const string kReconnectOutcomeSuccess = "success";
        private const string kReconnectOutcomeTransientFailure = "transient-failure";
        private const string kReconnectOutcomeFatalChannel = "fatal-channel";
        private const string kReconnectOutcomePolicyExhausted = "policy-exhausted";
        private const string kReconnectOutcomeDeadlineExpired = "deadline-expired";
        private readonly Lock m_lock = new();
        private readonly List<ManagedTransportChannelLease> m_leases = [];
        private int m_refcount;
        private int m_operationRef;
        private bool m_activeMetricRecorded;
        private DateTimeOffset m_openedAt;
        private DateTimeOffset m_lastStateChange;
        private int m_lastReconnectAttempt;
        private ServiceResult? m_lastError;
        private ChannelState m_state = ChannelState.Disconnected;
        private bool m_closing;
        private ITransportWaitingConnection? m_reverseConnection;

        /// <summary>
        /// Couples a transport with the certificate references that must remain alive until it closes.
        /// </summary>
        /// <param name="channel">The transport whose lifetime is managed by the entry.</param>
        /// <param name="certificates">The certificate snapshot retained for that transport.</param>
        private sealed class OwnedTransport(
            ITransportChannel channel,
            ClientChannelCertificateSnapshot certificates)
        {
            /// <summary>
            /// Gets the transport opened with the retained certificate snapshot.
            /// </summary>
            public ITransportChannel Channel { get; } = channel;

            /// <summary>
            /// Gets the certificate references released after the transport is closed.
            /// </summary>
            public ClientChannelCertificateSnapshot Certificates { get; } = certificates;
        }

        /// <summary>
        /// Holds the installed transport and its certificate ownership until replacement or entry teardown.
        /// </summary>
        private OwnedTransport? m_underlying;
        private long m_reconnectGeneration;
        private long m_clientCertificateVersion;
        private TaskCompletionSource<bool> m_readyGate;
        private TaskCompletionSource<bool>? m_reconnectCoalescer;
        private int m_reconnectStoppedIntentionally;
        private IRetryBudget? m_effectiveBudget;
        private ReconnectDeadline? m_reconnectDeadline;
    }

    /// <summary>
    /// Source-generated log messages for ChannelEntry.
    /// </summary>
    internal static partial class ChannelEntryLog
    {
        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 0, Level = LogLevel.Debug,
            Message = "ClientChannelManager: underlying CloseAsync failed.")]
        public static partial void ChannelEntryLog0(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 1, Level = LogLevel.Debug,
            Message = "ClientChannelManager: CloseChannel failed.")]
        public static partial void ChannelEntryLog1(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 2, Level = LogLevel.Warning,
            Message = "ClientChannelManager: transport reconnect attempt {Attempt} failed.")]
        public static partial void ChannelEntryLog2(
            this ILogger logger,
            Exception? exception,
            int attempt);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 3, Level = LogLevel.Warning,
            Message = "ClientChannelManager: participant notification attempt {Attempt} failed.")]
        public static partial void ChannelEntryLog3(
            this ILogger logger,
            Exception? exception,
            int attempt);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 4, Level = LogLevel.Debug,
            Message = "ClientChannelManager: channel.ReconnectAsync failed; recreating.")]
        public static partial void ChannelEntryLog4(this ILogger logger, Exception? exception);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 5, Level = LogLevel.Warning,
            Message = "ClientChannelManager: participant {Participant} OnReconnect timed out after " +
                "{Timeout}; treating as TransientFailure.")]
        public static partial void ChannelEntryLog5(
            this ILogger logger,
            string participant,
            TimeSpan timeout);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 6, Level = LogLevel.Warning,
            Message = "ClientChannelManager: participant {Participant} OnReconnect failed.")]
        public static partial void ChannelEntryLog6(
            this ILogger logger,
            Exception? exception,
            string participant);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 7, Level = LogLevel.Warning,
            Message = "ClientChannelManager: participant {Participant} RecreateAsync failed.")]
        public static partial void ChannelEntryLog7(
            this ILogger logger,
            Exception? exception,
            string participant);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 8, Level = LogLevel.Warning,
            Message = "ClientChannelManager: recovery deadline {Duration} expired after {Elapsed} in {State}; " +
                "handing recovery to the session owner.")]
        public static partial void ChannelReconnectDeadlineExpired(
            this ILogger logger,
            TimeSpan duration,
            TimeSpan elapsed,
            ChannelState state);

        [LoggerMessage(EventId = CoreEventIds.ChannelEntry + 9, Level = LogLevel.Warning,
            Message = "ClientChannelManager: a recovery cancellation callback failed.")]
        public static partial void ChannelReconnectCancellationFailed(this ILogger logger, Exception exception);
    }
}
