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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    public sealed partial class ClientChannelManager
    {
        /// <summary>
        /// One entry per <see cref="ManagedChannelKey"/>. Owns the
        /// underlying <see cref="ITransportChannel"/>, the refcount,
        /// the list of active leases, the state, the ready-gate TCS and
        /// the reconnect coalescer.
        /// </summary>
        internal sealed class ChannelEntry : IAsyncDisposable
        {
            public ChannelEntry(
                ClientChannelManager owner,
                ManagedChannelKey key,
                ConfiguredEndpoint endpoint,
                ITransportWaitingConnection? reverseConnection)
            {
                m_owner = owner;
                Key = key;
                Endpoint = endpoint;
                ReverseConnection = reverseConnection;
                m_lastStateChange = owner.TimeProvider.GetUtcNow();
                m_readyGate = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public ManagedChannelKey Key { get; }
            public ConfiguredEndpoint Endpoint { get; }
            public ITransportWaitingConnection? ReverseConnection { get; }

            public ClientChannelManager OwnerManager => m_owner;

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

            public ITransportChannel? Underlying
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_underlying;
                    }
                }
            }

            public event Action<IManagedTransportChannel, ChannelStateChange>? StateChanged;

            /// <summary>
            /// Open the initial transport channel. Called once per
            /// entry before any lease is handed out.
            /// </summary>
            public async Task OpenInitialAsync(
                Certificate? clientCertificate,
                CertificateCollection? clientCertificateChain,
                long clientCertificateVersion,
                CancellationToken ct)
            {
                TransitionTo(ChannelState.TransportConnecting, error: null, attempt: 0);

                try
                {
                    ITransportChannel channel = await CreateTransportChannelAsync(
                        clientCertificate, clientCertificateChain, ct)
                        .ConfigureAwait(false);
                    lock (m_lock)
                    {
                        m_underlying = channel;
                        m_clientCertificateVersion = clientCertificateVersion;
                        m_activeMetricRecorded = true;
                    }
                    m_owner.RecordChannelActiveChanged(this, 1);
                    TransitionTo(ChannelState.Ready, error: null, attempt: 0);
                    SignalReady();
                }
                catch (Exception ex)
                {
                    TransitionTo(
                        ChannelState.Faulted,
                        new ServiceResult(ex),
                        attempt: 0);
                    FailReady(ex);
                    throw;
                }
            }

            /// <summary>
            /// Acquire a new lease for the supplied participant. The
            /// caller is responsible for disposing the returned
            /// lease.
            /// </summary>
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
                    if (m_state is ChannelState.Closed or ChannelState.Faulted)
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

                EmitParticipantAttached(this, participantId, refCount, participantCount);
                return lease;
            }

            /// <summary>
            /// Release a lease. When refcount drops to zero and no
            /// reconnect operation is in progress, the underlying
            /// channel is torn down and the entry is removed from the
            /// manager registry.
            /// </summary>
            public async ValueTask ReleaseLeaseAsync(ManagedTransportChannelLease lease)
            {
                bool teardown = false;
                ChannelCloseReason reason = ChannelCloseReason.LeaseReleased;
                int refCount;
                int participantCount;
                string participantId;
                lock (m_lock)
                {
                    if (!m_leases.Remove(lease))
                    {
                        return;
                    }
                    m_refcount--;
                    refCount = m_refcount;
                    participantCount = m_leases.Count(l => l.IsActive);
                    participantId = lease.Participant.Id;
                    teardown = m_refcount == 0 && m_operationRef == 0;
                    if (teardown && m_state == ChannelState.Faulted)
                    {
                        reason = ChannelCloseReason.Faulted;
                    }
                }

                EmitParticipantDetached(this, participantId, refCount, participantCount);
                if (teardown)
                {
                    await TearDownAsync(reason).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Request a reconnect cycle. Multiple concurrent callers
            /// coalesce into one cycle.
            /// </summary>
            public Task<bool> RequestReconnectAsync(CancellationToken ct)
            {
                return RequestReconnectAsync(budget: null, ct);
            }

            public Task<bool> RequestReconnectAsync(IRetryBudget? budget, CancellationToken ct)
            {
                TaskCompletionSource<bool> tcs;
                bool starter;
                lock (m_lock)
                {
                    if (m_state is ChannelState.Closed or ChannelState.Faulted)
                    {
                        return Task.FromException<bool>(
                            ServiceResultException.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Channel is {0}.", m_state));
                    }

                    if (m_reconnectCoalescer != null)
                    {
                        // already running — join it
                        tcs = m_reconnectCoalescer;
                        starter = false;
                    }
                    else
                    {
                        tcs = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        m_reconnectCoalescer = tcs;
                        starter = true;
                        // hold an internal op ref so disposal during
                        // reconnect doesn't tear down the entry
                        m_operationRef++;
                    }
                }

                if (starter)
                {
                    _ = Task.Run(() => RunReconnectCycleAsync(tcs, budget), CancellationToken.None);
                }

                return tcs.Task.WaitAsync(ct);
            }

            /// <summary>
            /// Wait until the channel is ready, or until cancellation.
            /// </summary>
            public Task WaitForReadyAsync(CancellationToken ct)
            {
                TaskCompletionSource<bool> gate;
                lock (m_lock)
                {
                    if (m_state == ChannelState.Ready)
                    {
                        return Task.CompletedTask;
                    }
                    if (m_state is ChannelState.Closed or ChannelState.Faulted)
                    {
                        return Task.FromException(
                            ServiceResultException.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Channel is {0}.", m_state));
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
                lock (m_lock)
                {
                    leases = [.. m_leases];
                    m_leases.Clear();
                    m_refcount = 0;
                }
                foreach (ManagedTransportChannelLease lease in leases)
                {
                    lease.MarkReleased();
                    EmitParticipantDetached(this, lease.Participant.Id, 0, 0);
                }
                await TearDownAsync(reason).ConfigureAwait(false);
            }

            private async Task WaitForReadyCoreAsync(Task readyTask, CancellationToken ct)
            {
                long startingTimestamp = m_owner.TimeProvider.GetTimestamp();
                try
                {
                    await readyTask.WaitAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    m_owner.RecordGateWait(
                        this,
                        m_owner.TimeProvider.GetElapsedTime(startingTimestamp));
                }
            }

            private Task DelayAsync(TimeSpan delay, CancellationToken ct)
            {
                if (delay <= TimeSpan.Zero)
                {
                    return Task.CompletedTask;
                }

#if NET8_0_OR_GREATER
                return Task.Delay(delay, m_owner.TimeProvider, ct);
#else
                if (ct.IsCancellationRequested)
                {
                    return Task.FromCanceled(ct);
                }

                var state = new DelayState();
                state.Initialize(m_owner.TimeProvider, delay, ct);

                return state.Task;
#endif
            }

            private async Task TearDownAsync(ChannelCloseReason reason)
            {
                ITransportChannel? underlying;
                bool activeMetricRecorded;
                lock (m_lock)
                {
                    if (m_state == ChannelState.Closed)
                    {
                        return;
                    }
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
                    try
                    {
                        await underlying.CloseAsync(default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_owner.Logger?.LogDebug(
                            ex, "ClientChannelManager: underlying CloseAsync failed.");
                    }
                    try
                    {
                        m_owner.CloseChannel(underlying);
                    }
                    catch (Exception ex)
                    {
                        m_owner.Logger?.LogDebug(
                            ex, "ClientChannelManager: CloseChannel failed.");
                    }
                    m_owner.RecordChannelClosed(this, reason);
                    EmitChannelClosed(this, reason);
                }

                if (activeMetricRecorded)
                {
                    m_owner.RecordChannelActiveChanged(this, -1);
                }

                m_owner.RemoveEntryIfPresent(Key, this);
            }

            private async Task RunReconnectCycleAsync(
                TaskCompletionSource<bool> tcs,
                IRetryBudget? budget)
            {
                using var activity = StartReconnectActivity(this);
                long startingTimestamp = m_owner.TimeProvider.GetTimestamp();
                string finalOutcome = kReconnectOutcomeTransientFailure;
                ServiceResult? finalError = null;
                int attemptsStarted = 0;

                async Task StopWithFaultAsync(
                    ServiceResult error,
                    string message,
                    int failedAttempt)
                {
                    finalError = error;
                    TransitionTo(
                        ChannelState.Faulted,
                        error,
                        failedAttempt);
                    FailReady(new ServiceResultException(
                        StatusCodes.BadSecureChannelClosed,
                        message));
                    await NotifyParticipantsFinalAsync().ConfigureAwait(false);
                    finalOutcome = kReconnectOutcomePolicyExhausted;
                    m_owner.RecordReconnectAttempt(this, finalOutcome);
                    tcs.TrySetResult(false);
                }

                try
                {
                    int attempt = 0;
                    while (true)
                    {
                        if (budget != null && budget.IsExhausted)
                        {
                            ServiceResult error = ServiceResult.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Channel reconnect budget exhausted after {0} attempts.",
                                attempt);
                            await StopWithFaultAsync(
                                    error,
                                    "Channel reconnect budget exhausted.",
                                    attempt)
                                .ConfigureAwait(false);
                            return;
                        }

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
                        TimeSpan delay = m_owner.ReconnectPolicy.GetDelay(attempt, budget);
#else
                        TimeSpan delay = ChannelReconnectPolicyBudget.GetDelay(
                            m_owner.ReconnectPolicy,
                            attempt,
                            budget);
#endif
                        if (delay < TimeSpan.Zero)
                        {
                            ServiceResult error = ServiceResult.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Channel reconnect policy exhausted after {0} attempts.",
                                attempt);
                            await StopWithFaultAsync(
                                    error,
                                    "Channel reconnect policy exhausted.",
                                    attempt)
                                .ConfigureAwait(false);
                            return;
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
                                await DelayAsync(delay, default).ConfigureAwait(false);
                            }
                            catch
                            {
                                // delay-cancelled — ignore and try
                            }
                        }

                        if (budget != null && budget.IsExhausted)
                        {
                            ServiceResult error = ServiceResult.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Channel reconnect budget exhausted after {0} attempts.",
                                attempt);
                            await StopWithFaultAsync(
                                    error,
                                    "Channel reconnect budget exhausted.",
                                    attempt)
                                .ConfigureAwait(false);
                            return;
                        }

                        try
                        {
                            await EnsureTransportConnectedAsync(default).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ServiceResult error = new(ex);
                            m_owner.Logger?.LogWarning(
                                ex,
                                "ClientChannelManager: transport reconnect attempt {Attempt} failed.",
                                attempt);
                            EmitReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                            m_owner.RecordReconnectAttempt(this, kReconnectOutcomeTransientFailure);
                            attempt++;
                            continue;
                        }

                        TransitionTo(
                            ChannelState.TransportConnectedSessionReactivating,
                            error: null,
                            attempt);

                        AggregatedReactivationOutcome outcome;
                        try
                        {
                            outcome = await NotifyParticipantsAsync(attempt, default)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ServiceResult error = new(ex);
                            m_owner.Logger?.LogWarning(
                                ex,
                                "ClientChannelManager: participant notification attempt {Attempt} failed.",
                                attempt);
                            EmitReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                            m_owner.RecordReconnectAttempt(this, kReconnectOutcomeTransientFailure);
                            attempt++;
                            continue;
                        }

                        if (outcome.FatalForChannel)
                        {
                            finalError = ServiceResult.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Participant signaled fatal channel error.");
                            TransitionTo(
                                ChannelState.Faulted,
                                finalError,
                                attempt);
                            FailReady(new ServiceResultException(
                                StatusCodes.BadSecureChannelClosed,
                                "Participant signaled fatal channel error."));
                            await NotifyParticipantsFinalAsync().ConfigureAwait(false);
                            finalOutcome = kReconnectOutcomeFatalChannel;
                            m_owner.RecordReconnectAttempt(this, finalOutcome);
                            tcs.TrySetResult(false);
                            return;
                        }

                        if (outcome.AnyTransient)
                        {
                            ServiceResult error = ServiceResult.Create(
                                StatusCodes.BadSecureChannelClosed,
                                "Participant signaled transient channel reconnect failure.");
                            EmitReconnectFailed(this, attempt, kReconnectOutcomeTransientFailure, error);
                            m_owner.RecordReconnectAttempt(this, kReconnectOutcomeTransientFailure);
                            attempt++;
                            continue;
                        }

                        TransitionTo(ChannelState.Ready, error: null, attempt);
                        SignalReady();
                        finalOutcome = kReconnectOutcomeSuccess;
                        m_owner.RecordReconnectAttempt(this, finalOutcome);
                        tcs.TrySetResult(true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    finalError = new ServiceResult(ex);
                    tcs.TrySetException(ex);
                }
                finally
                {
                    CompleteReconnectActivity(activity, this, attemptsStarted, finalOutcome, finalError);
                    m_owner.RecordReconnectDuration(
                        this,
                        m_owner.TimeProvider.GetElapsedTime(startingTimestamp),
                        finalOutcome);

                    bool teardown = false;
                    ChannelCloseReason teardownReason = ChannelCloseReason.LeaseReleased;
                    lock (m_lock)
                    {
                        m_reconnectCoalescer = null;
                        m_operationRef--;
                        teardown = m_refcount == 0
                            && m_operationRef == 0
                            && m_state != ChannelState.Closed;
                        if (teardown && m_state == ChannelState.Faulted)
                        {
                            teardownReason = ChannelCloseReason.Faulted;
                        }
                    }
                    if (teardown)
                    {
                        await TearDownAsync(teardownReason).ConfigureAwait(false);
                    }
                }
            }

            private async Task EnsureTransportConnectedAsync(CancellationToken ct)
            {
                (Certificate? clientCert, CertificateCollection? clientChain, long certVersion) =
                    m_owner.CurrentClientCertificateSnapshot;
                ITransportChannel? underlying;
                bool certificateChanged;
                lock (m_lock)
                {
                    underlying = m_underlying;
                    certificateChanged = m_clientCertificateVersion != certVersion;
                }

                if (!certificateChanged &&
                    underlying != null &&
                    (underlying.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                {
                    try
                    {
                        await underlying.ReconnectAsync(ReverseConnection, ct).ConfigureAwait(false);
                        MarkOpened();
                        return;
                    }
                    catch (Exception ex)
                    {
                        m_owner.Logger?.LogDebug(
                            ex,
                            "ClientChannelManager: channel.ReconnectAsync failed; recreating.");
                    }
                }

                ITransportChannel fresh = await CreateTransportChannelAsync(
                    clientCert, clientChain, ct).ConfigureAwait(false);

                ITransportChannel? old;
                lock (m_lock)
                {
                    old = m_underlying;
                    m_underlying = fresh;
                    m_clientCertificateVersion = certVersion;
                }
                if (old != null)
                {
                    try
                    {
                        await old.CloseAsync(default).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best-effort
                    }
                    try
                    {
                        m_owner.CloseChannel(old);
                    }
                    catch
                    {
                        // best-effort
                    }
                    m_owner.RecordChannelClosed(this, ChannelCloseReason.Faulted);
                    EmitChannelClosed(this, ChannelCloseReason.Faulted);
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

            private async Task<ITransportChannel> CreateTransportChannelAsync(
                Certificate? clientCertificate,
                CertificateCollection? clientCertificateChain,
                CancellationToken ct)
            {
                IServiceMessageContext context = m_owner.Configuration.CreateMessageContext();
                ITransportChannel channel = await m_owner.CreateChannelAsync(
                    Endpoint,
                    context,
                    clientCertificate,
                    clientCertificateChain,
                    ReverseConnection,
                    ct).ConfigureAwait(false);
                MarkOpened();
                m_owner.RecordChannelOpen(this);
                EmitChannelOpened(this);
                return channel;
            }

            private void MarkOpened()
            {
                lock (m_lock)
                {
                    m_openedAt = m_owner.TimeProvider.GetUtcNow();
                }
            }

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

                Task<ParticipantReconnectResult>[] tasks = [.. snapshot.Select(lease => Task.Run(
                    async () =>
                    {
                        using IDisposable scope = EnterReactivationScope();
                        try
                        {
                            return await lease.Participant.OnReconnectAsync(
                                lease, attempt, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_owner.Logger?.LogWarning(
                                ex,
                                "ClientChannelManager: participant {Participant} OnReconnect failed.",
                                lease.Participant.Id);
                            return ParticipantReconnectResult.TransientFailure;
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
                                EmitParticipantDetached(
                                    this,
                                    snapshot[i].Participant.Id,
                                    refCount,
                                    participantCount);
                            }
                            break;
                        case ParticipantReconnectResult.RequiresSessionRecreate:
                        case ParticipantReconnectResult.Reactivated:
                        default:
                            break;
                    }
                }
                return outcome;
            }

            private async Task NotifyParticipantsFinalAsync()
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
                Task[] tasks = [.. snapshot.Select(lease => Task.Run(async () =>
                {
                    try
                    {
                        await lease.Participant.OnReconnectAsync(lease, -1, default)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // best-effort final notification
                    }
                }))];
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort final notification
                }
            }

            private void TransitionTo(ChannelState next, ServiceResult? error, int attempt)
            {
                ChannelState previous;
                IManagedTransportChannel[] subjects;
                Action<IManagedTransportChannel, ChannelStateChange>? handler;
                lock (m_lock)
                {
                    previous = m_state;
                    if (previous == next)
                    {
                        return;
                    }
                    m_state = next;
                    m_lastStateChange = m_owner.TimeProvider.GetUtcNow();
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

                EmitStateChanged(this, change);
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

            private readonly ClientChannelManager m_owner;
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
            private ITransportChannel? m_underlying;
            private long m_clientCertificateVersion;
            private TaskCompletionSource<bool> m_readyGate;
            private TaskCompletionSource<bool>? m_reconnectCoalescer;
        }

        private struct AggregatedReactivationOutcome
        {
            public bool AnyTransient;
            public bool FatalForChannel;
        }
    }
}
