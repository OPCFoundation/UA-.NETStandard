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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Recycling.Model;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Host-local protocol state and absolute snapshot projection. Only the coordinator role owns an engine.
    /// </summary>
    public sealed class RecyclingRuntime : IAsyncDisposable
    {
        /// <summary>
        /// Creates injectable runtime wiring without synchronously opening files or connecting sessions.
        /// </summary>
        public RecyclingRuntime(
            RecyclingHostOptions options,
            RecyclingAuthorization authorization,
            IRecyclingProcessViewAdapter adapter,
            IRecyclingUserCommandForwarder forwarder,
            IServiceProvider services,
            TimeProvider timeProvider,
            ITelemetryContext telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            m_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            m_forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = telemetry.CreateLogger<RecyclingRuntime>();
        }

        /// <summary>
        /// Direct callable coordinator API, available only in the coordinator role after host startup.
        /// </summary>
        public RecyclingCoordinator Coordinator => m_coordinator ??
            throw new InvalidOperationException("This process does not own the coordinator.");

        /// <summary>
        /// Actual OPC UA address-space startup signal for tests and callers.
        /// </summary>
        public Task AddressSpaceReady => m_ready.Task;

        /// <summary>
        /// Last applied committed image, or null before a process view obtains one.
        /// </summary>
        public CommittedSnapshot? Latest => Volatile.Read(ref m_projection)?.Snapshot;

        /// <summary>
        /// Current view-local membership state. Contention does not mean the managed transport failed.
        /// </summary>
        public RecyclingMembershipState MembershipState =>
            (RecyclingMembershipState)Volatile.Read(ref m_membershipState);

        /// <summary>
        /// Explicit membership diagnostic; a refused authorization status is never rewritten as contention.
        /// </summary>
        public StatusCode MembershipStatusCode => new(Volatile.Read(ref m_membershipStatusCode));

        /// <summary>
        /// Whether the process currently has usable committed data, independent of machine/product state.
        /// </summary>
        public bool IsAvailable => Volatile.Read(ref m_projection)?.Available == true &&
            (m_options.Role == RecyclingHostRole.Coordinator
                ? Coordinator.IsHealthy
                : MembershipState == RecyclingMembershipState.Ready &&
                    Volatile.Read(ref m_activeSession)?.Connected == true &&
                    m_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref m_lastContactTicks) <
                        m_options.ViewPollPeriod.Ticks * 3);

        /// <summary>
        /// Opens the journal only for the sole coordinator.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            if (m_options.Role == RecyclingHostRole.Coordinator)
            {
                m_coordinator = await RecyclingCoordinator.OpenAsync(
                    m_options.Simulation,
                    m_services.GetRequiredService<IFileSystemProvider>(),
                    m_options.LineId,
                    m_timeProvider,
                    m_telemetry,
                    cancellationToken).ConfigureAwait(false);
                await SetProjectionAsync(m_coordinator.Current, available: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases the writer only after background loops and the endpoint have stopped.
        /// </summary>
        public async ValueTask StopAsync()
        {
            if (m_coordinator != null)
            {
                await m_coordinator.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Binds generated typed state/methods and lets the process adapter compose its real models.
        /// </summary>
        public async ValueTask ConfigureAsync(IMachineryBuildContext context, CancellationToken cancellationToken)
        {
            m_modelContext = context.Context;
            ushort index = context.Context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri);
            if (m_options.Role == RecyclingHostRole.Coordinator)
            {
                var state = context.Manager.FindPredefinedNode(new NodeId(Model.Objects.Coordinator, index))
                    as RecyclingCoordinatorState ??
                        throw new InvalidOperationException("Coordinator model was not loaded.");
                state.LineId!.Value = m_options.LineId;
                BindReads(state.State!, state.Available!);
                state.Execute!.OnCallAsync = async (callContext, _, _, command, ct) =>
                {
                    RecyclingCommandOutcome outcome = await Coordinator.ExecuteAsync(
                        m_authorization.Resolve(callContext),
                        RecyclingModelMapping.FromWire(command),
                        ct).ConfigureAwait(false);
                    return new ExecuteCommandMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Outcome = RecyclingModelMapping.ToWire(outcome)
                    };
                };
                state.ReadSnapshot!.OnCallAsync = (callContext, _, _, ct) =>
                    ReadSnapshotAsync(callContext, ct);
                state.ReadGenealogy!.OnCallAsync = (callContext, _, _, afterId, maximum, ct) =>
                {
                    m_authorization.Resolve(callContext);
                    ct.ThrowIfCancellationRequested();
                    return new ValueTask<ReadGenealogyMethodStateResult>(new ReadGenealogyMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Executions = RecyclingModelMapping.ToWire(Coordinator.GetHistory(afterId, maximum))
                    });
                };
            }
            else
            {
                var state = context.Manager.FindPredefinedNode(new NodeId(Model.Objects.ProcessView, index))
                    as RecyclingProcessViewState ??
                        throw new InvalidOperationException("Process-view model was not loaded.");
                state.LineId!.Value = m_options.LineId;
                state.Process!.Value = (ProcessEnum)m_options.Process;
                state.CompanionBindingsComplete!.Value = m_adapter.CompanionBindingsComplete;
                BindReads(state.State!, state.Available!);
                state.ReadSnapshot!.OnCallAsync = (callContext, _, _, ct) =>
                    ReadSnapshotAsync(callContext, ct);
                state.ForwardCommand!.OnCallAsync = async (callContext, _, _, command, ct) =>
                {
                    RecyclingPrincipal user = m_authorization.Resolve(callContext);
                    RecyclingCommand request = RecyclingModelMapping.FromWire(command);
                    if (user.Access is not (RecyclingAccess.Operator or RecyclingAccess.SimulatorAdministrator) ||
                        request.Process != m_options.Process ||
                        request.Kind is not (
                            RecyclingCommandKind.EnableProcess or RecyclingCommandKind.DisableProcess))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUserAccessDenied,
                            "A view forwards only authorized controls for its own process.");
                    }
                    RecyclingCommandOutcome outcome = await m_forwarder.ForwardAsync(user, request, ct)
                        .ConfigureAwait(false);
                    return new ExecuteCommandMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        Outcome = RecyclingModelMapping.ToWire(outcome)
                    };
                };
                await m_adapter.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies an already committed image to a process view without advancing material time.
        /// Restoring availability reapplies the same revision so adapters can restore native-node quality.
        /// This in-process projection seam does not create commits or expose a remote write operation.
        /// </summary>
        public async ValueTask ApplyProjectionAsync(
            CommittedSnapshot snapshot,
            bool available,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (m_options.Role != RecyclingHostRole.ProcessView)
            {
                throw new InvalidOperationException("Only a process view accepts externally committed projections.");
            }
            Interlocked.Exchange(ref m_lastContactTicks, m_timeProvider.GetUtcNow().UtcTicks);
            await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ProjectionFrame? previous = Volatile.Read(ref m_projection);
                if (previous != null && snapshot.Revision < previous.Snapshot.Revision)
                {
                    return;
                }
                if (previous == null || snapshot.RunId != previous.Snapshot.RunId ||
                    snapshot.Revision != previous.Snapshot.Revision || (available && !previous.Available))
                {
                    await m_adapter.ApplyAsync(snapshot, cancellationToken).ConfigureAwait(false);
                }
                if (!available)
                {
                    await m_adapter.MarkUnavailableAsync(snapshot.SourceTimestampUtc, cancellationToken)
                        .ConfigureAwait(false);
                }
                await SetProjectionAsync(snapshot, available, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_projectionGate.Release();
            }
        }

        /// <summary>
        /// Attempts at most sixteen membership commands on the supplied authenticated connection.
        /// A contended round returns explicitly so the owner can pace another round without replacing the session.
        /// Real transport, authorization and non-revision protocol errors propagate unchanged.
        /// </summary>
        public async ValueTask<RecyclingMembershipState> RefreshMembershipAsync(
            IRecyclingMembershipClient client,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            if (m_options.Role != RecyclingHostRole.ProcessView)
            {
                throw new InvalidOperationException("Only a process view maintains coordinator membership.");
            }
            if (!await m_membershipGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyOperations, "Membership refresh is already active.");
            }
            try
            {
                ConfirmConnected(client);
                RememberConfiguredIdentity(client);
                CommittedSnapshot snapshot = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConfirmConnected(client);
                    string connectionId = client.ConnectionId;
                    ProcessParticipant? participant = snapshot.Participants.Span.ToArray()
                        .FirstOrDefault(item => item.Process == m_options.Process);
                    DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                    if (participant != null && participant.ConnectionId == connectionId)
                    {
                        m_membershipIdentity = participant.Identity;
                    }
                    if (participant != null && participant.ConnectionId != connectionId &&
                        participant.ConnectionId.Length != 0 && participant.LeaseExpiresUtc > now &&
                        participant.Identity == m_membershipIdentity)
                    {
                        return await MembershipPendingAsync(
                            RecyclingMembershipState.WaitingForPreviousLease,
                            StatusCodes.BadWaitingForInitialData,
                            cancellationToken).ConfigureAwait(false);
                    }
                    RecyclingCommandKind kind = participant == null ||
                        participant.ConnectionId != connectionId || participant.LeaseExpiresUtc <= now
                        ? RecyclingCommandKind.Register
                        : participant.Initialized ? RecyclingCommandKind.Heartbeat : RecyclingCommandKind.Initialize;
                    if (kind == RecyclingCommandKind.Initialize)
                    {
                        await ApplyProjectionAsync(snapshot, available: false, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    var command = new RecyclingCommand
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        LineId = m_options.LineId,
                        RunId = snapshot.RunId,
                        ExpectedRevision = snapshot.Revision,
                        Kind = kind,
                        Process = m_options.Process
                    };
                    try
                    {
                        RecyclingCommandOutcome outcome = await client.ExecuteAsync(command, cancellationToken)
                            .ConfigureAwait(false);
                        if (StatusCode.IsBad(new StatusCode(outcome.Status)))
                        {
                            throw new ServiceResultException(new StatusCode(outcome.Status), outcome.Message);
                        }
                    }
                    catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadInvalidState)
                    {
                        ConfirmConnected(client);
                        CommittedSnapshot latest = await client.ReadSnapshotAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (latest.RunId == snapshot.RunId && latest.Revision == snapshot.Revision)
                        {
                            ProcessParticipant? current = latest.Participants.Span.ToArray()
                                .FirstOrDefault(item => item.Process == m_options.Process);
                            if (kind == RecyclingCommandKind.Register || current == null ||
                                current.ConnectionId != connectionId ||
                                current.LeaseExpiresUtc > m_timeProvider.GetUtcNow().UtcDateTime)
                            {
                                throw;
                            }
                        }
                        // Only a definitive revision/run conflict or an expired own lease is retried.
                        snapshot = latest;
                        continue;
                    }
                    if (kind != RecyclingCommandKind.Register)
                    {
                        SetMembershipState(RecyclingMembershipState.Ready, StatusCodes.Good);
                        return RecyclingMembershipState.Ready;
                    }
                    snapshot = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                }
                return await MembershipPendingAsync(
                    RecyclingMembershipState.RevisionContention,
                    StatusCodes.BadTooManyOperations,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                StatusCode status = exception is ServiceResultException serviceError
                    ? serviceError.StatusCode
                    : StatusCodes.BadCommunicationError;
                SetMembershipState(
                    status == StatusCodes.BadUserAccessDenied
                        ? RecyclingMembershipState.Rejected
                        : IsTransportFailure(exception)
                            ? RecyclingMembershipState.TransportUnavailable
                            : RecyclingMembershipState.Faulted,
                    status);
                await MarkUnavailableAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
            finally
            {
                m_membershipGate.Release();
            }
        }

        /// <summary>
        /// Signals successful endpoint startup from the hosting infrastructure.
        /// </summary>
        public void SignalAddressSpaceReady()
        {
            m_ready.TrySetResult();
        }

        /// <summary>
        /// Starts either the authoritative coordinator loops or the non-authoritative view connection.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            await AddressSpaceReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task first = m_options.Role == RecyclingHostRole.Coordinator
                    ? ObserveCoordinatorAsync(lifetime.Token)
                    : ConnectViewAsync(lifetime.Token);
                Task second = m_options.Role == RecyclingHostRole.Coordinator
                    ? PaceCoordinatorAsync(lifetime.Token)
                    : MonitorViewAvailabilityAsync(lifetime.Token);
                Task completed = await Task.WhenAny(first, second).ConfigureAwait(false);
                await lifetime.CancelAsync().ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(first, second).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    completed.IsCompletedSuccessfully && !first.IsFaulted && !second.IsFaulted)
                {
                    // A normally completed worker stops its companion, but faults still reach the host.
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected host shutdown, not a simulated recovery or reset.
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await StopAsync().ConfigureAwait(false);
            m_projectionGate.Dispose();
            m_membershipGate.Dispose();
        }

        private void BindReads(BaseVariableState snapshot, BaseVariableState available)
        {
            m_snapshotNode = snapshot;
            m_availableNode = available;
            snapshot.AccessLevel = AccessLevels.CurrentRead;
            snapshot.UserAccessLevel = AccessLevels.CurrentRead;
            snapshot.OnReadUserAccessLevel = ReadUserAccessLevel;
            snapshot.MinimumSamplingInterval = 100;
            snapshot.OnReadValueAsync = (_, _, indexRange, encoding, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (!indexRange.IsNull || !encoding.IsNull)
                {
                    return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                        new ServiceResult(StatusCodes.BadDataEncodingUnsupported),
                        Variant.Null, StatusCodes.BadDataEncodingUnsupported, DateTimeUtc.MinValue));
                }
                ProjectionFrame? frame = Volatile.Read(ref m_projection);
                StatusCode status = frame == null
                    ? StatusCodes.BadWaitingForInitialData
                    : IsAvailable ? StatusCodes.Good
                    : m_options.Role == RecyclingHostRole.ProcessView && StatusCode.IsBad(MembershipStatusCode)
                        ? MembershipStatusCode
                        : StatusCodes.BadNoCommunication;
                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    ServiceResult.Good,
                    frame == null ? Variant.Null : Variant.FromStructure(frame.Wire),
                    status,
                    frame?.Snapshot.SourceTimestampUtc ?? DateTime.UnixEpoch));
            };
            available.AccessLevel = AccessLevels.CurrentRead;
            available.UserAccessLevel = AccessLevels.CurrentRead;
            available.OnReadUserAccessLevel = ReadUserAccessLevel;
            available.MinimumSamplingInterval = 100;
            available.OnReadValueAsync = (_, _, _, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                ProjectionFrame? frame = Volatile.Read(ref m_projection);
                return new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    ServiceResult.Good, Variant.From(IsAvailable), StatusCodes.Good,
                    frame?.Snapshot.SourceTimestampUtc ?? DateTime.UnixEpoch));
            };
        }

        private ServiceResult ReadUserAccessLevel(ISystemContext context, NodeState _, ref byte value)
        {
            try
            {
                m_authorization.Resolve(context);
                value = AccessLevels.CurrentRead;
            }
            catch (ServiceResultException)
            {
                value = AccessLevels.None;
            }
            return ServiceResult.Good;
        }

        private ValueTask<ReadSnapshotMethodStateResult> ReadSnapshotAsync(
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            m_authorization.Resolve(context);
            cancellationToken.ThrowIfCancellationRequested();
            if (m_options.Role == RecyclingHostRole.Coordinator)
            {
                if (!Coordinator.IsHealthy)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadOutOfService, "Coordinator persistence is unavailable.");
                }
                return new ValueTask<ReadSnapshotMethodStateResult>(new ReadSnapshotMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    Snapshot = RecyclingModelMapping.ToWire(Coordinator.Current)
                });
            }
            ProjectionFrame? frame = Volatile.Read(ref m_projection);
            if (frame == null || !IsAvailable)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoCommunication, "No current coordinator snapshot is available.");
            }
            return new ValueTask<ReadSnapshotMethodStateResult>(new ReadSnapshotMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Snapshot = RecyclingModelMapping.ToWire(frame.Snapshot)
            });
        }

        private async Task ObserveCoordinatorAsync(CancellationToken cancellationToken)
        {
            await foreach (CoordinatorNotification notification in Coordinator.WatchAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                await SetProjectionAsync(notification.Snapshot, notification.IsAvailable, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task PaceCoordinatorAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(m_options.TickPeriod, m_timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (m_options.AutomaticTicks && Coordinator.IsHealthy)
                {
                    try
                    {
                        await Coordinator.TickAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (
                        exception.StatusCode == StatusCodes.BadTooManyOperations && Coordinator.IsHealthy)
                    {
                        m_logger.CoordinatorTickDeferred();
                    }
                    catch (Exception) when (!Coordinator.IsHealthy)
                    {
                        await SetProjectionAsync(Coordinator.Current, available: false, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Invalidates equipment independently of potentially blocked coordinator calls and subscriptions.
        /// </summary>
        private async Task MonitorViewAvailabilityAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(m_options.ViewPollPeriod, m_timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (Volatile.Read(ref m_projection)?.Available == true &&
                        (Volatile.Read(ref m_activeSession)?.Connected == false ||
                            m_timeProvider.GetUtcNow().UtcTicks - Interlocked.Read(ref m_lastContactTicks) >=
                                m_options.ViewPollPeriod.Ticks * 3))
                    {
                        m_logger.ViewUnavailable(new ServiceResultException(
                            StatusCodes.BadNoCommunication, "Coordinator contact is unavailable."),
                            m_options.Process);
                        await MarkUnavailableCoreAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_projectionGate.Release();
                }
            }
        }

        private async Task ConnectViewAsync(CancellationToken cancellationToken)
        {
            Func<CancellationToken, Task<ManagedSession>> connect =
                m_services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ManagedSession session = await connect(cancellationToken).ConfigureAwait(false);
                    Volatile.Write(ref m_activeSession, session);
                    var client = new RecyclingCoordinatorClient(session, m_telemetry);
                    await using var clientLifetime = client.ConfigureAwait(false);
                    await MaintainMembershipAsync(client, cancellationToken).ConfigureAwait(false);
                    CommittedSnapshot initial = await client.ReadSnapshotAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await ApplyProjectionAsync(initial, available: true, cancellationToken).ConfigureAwait(false);

                    using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Task observe = ObserveRemoteAsync(client, connection.Token);
                    Task renew = RenewLeaseAsync(client, connection.Token);
                    await Task.WhenAny(observe, renew).ConfigureAwait(false);
                    await connection.CancelAsync().ConfigureAwait(false);
                    await Task.WhenAll(observe, renew).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (IsTransportFailure(exception))
                {
                    SetMembershipState(RecyclingMembershipState.TransportUnavailable,
                        exception is ServiceResultException serviceError
                            ? serviceError.StatusCode
                            : StatusCodes.BadNoCommunication);
                    m_logger.ViewUnavailable(exception, m_options.Process);
                    await MarkUnavailableAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    StatusCode status = exception is ServiceResultException serviceError
                        ? serviceError.StatusCode
                        : StatusCodes.BadUnexpectedError;
                    SetMembershipState(
                        status == StatusCodes.BadUserAccessDenied
                            ? RecyclingMembershipState.Rejected
                            : RecyclingMembershipState.Faulted,
                        status);
                    m_logger.MembershipRejected(exception, m_options.Process, status);
                    await MarkUnavailableAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                finally
                {
                    Volatile.Write(ref m_activeSession, null);
                }
                await Task.Delay(m_options.ViewPollPeriod, m_timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ObserveRemoteAsync(RecyclingCoordinatorClient client, CancellationToken cancellationToken)
        {
            await foreach (CoordinatorNotification notification in client.WatchAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!notification.IsAvailable)
                {
                    await MarkUnavailableAsync(cancellationToken).ConfigureAwait(false);
                    throw new ServiceResultException(StatusCodes.BadNoCommunication, notification.Detail);
                }
                bool initialized = notification.Snapshot.Participants.Span.ToArray()
                    .Any(participant => participant.Process == m_options.Process && participant.Initialized &&
                        participant.ConnectionId == client.Session.SessionId.ToString());
                await ApplyProjectionAsync(
                    notification.Snapshot, initialized && MembershipState == RecyclingMembershipState.Ready,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            throw new ServiceResultException(StatusCodes.BadNoCommunication, "Coordinator observation ended.");
        }

        private async Task RenewLeaseAsync(RecyclingCoordinatorClient client, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(m_options.ViewPollPeriod, m_timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await MaintainMembershipAsync(client, cancellationToken).ConfigureAwait(false);
                CommittedSnapshot latest = await client.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
                await ApplyProjectionAsync(latest, available: true, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask MaintainMembershipAsync(
            IRecyclingMembershipClient client,
            CancellationToken cancellationToken)
        {
            while (await RefreshMembershipAsync(client, cancellationToken).ConfigureAwait(false) !=
                RecyclingMembershipState.Ready)
            {
                // This wait retains the same authenticated client and connection identity.
                await Task.Delay(m_options.ViewPollPeriod, m_timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask<RecyclingMembershipState> MembershipPendingAsync(
            RecyclingMembershipState state,
            StatusCode status,
            CancellationToken cancellationToken)
        {
            if (MembershipState != state)
            {
                m_logger.MembershipPending(m_options.Process, state, status);
            }
            SetMembershipState(state, status);
            await MarkUnavailableAsync(cancellationToken).ConfigureAwait(false);
            return state;
        }

        private void RememberConfiguredIdentity(IRecyclingMembershipClient client)
        {
            if (m_membershipIdentity != null)
            {
                return;
            }
            foreach (RecyclingIdentityBinding binding in m_options.Identities)
            {
                if (binding.Access == RecyclingAccess.ProcessView && binding.Process == m_options.Process &&
                    string.Equals(
                        binding.CertificateSha256, client.CertificateSha256, StringComparison.OrdinalIgnoreCase))
                {
                    m_membershipIdentity = binding.Identity;
                    return;
                }
            }
        }

        private void SetMembershipState(RecyclingMembershipState state, StatusCode status)
        {
            Volatile.Write(ref m_membershipStatusCode, status.Code);
            Volatile.Write(ref m_membershipState, (int)state);
        }

        private static void ConfirmConnected(IRecyclingMembershipClient client)
        {
            if (!client.IsConnected)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoCommunication, "The managed session is disconnected.");
            }
        }

        /// <summary>
        /// Identifies temporary coordinator or channel unavailability without retrying protocol rejections.
        /// </summary>
        internal static bool IsTransportFailure(Exception exception)
        {
            if (exception is IOException)
            {
                return true;
            }
            if (exception is not ServiceResultException serviceError)
            {
                return false;
            }
            StatusCode status = serviceError.StatusCode;
            return status == StatusCodes.BadNoCommunication || status == StatusCodes.BadCommunicationError ||
                status == StatusCodes.BadConnectionClosed || status == StatusCodes.BadSecureChannelClosed ||
                status == StatusCodes.BadSessionClosed || status == StatusCodes.BadSessionIdInvalid ||
                status == StatusCodes.BadTimeout || status == StatusCodes.BadServerNotConnected ||
                status == StatusCodes.BadNotConnected || status == StatusCodes.BadOutOfService ||
                status == StatusCodes.BadServerHalted || status == StatusCodes.BadShutdown;
        }

        private async ValueTask MarkUnavailableAsync(CancellationToken cancellationToken)
        {
            await m_projectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await MarkUnavailableCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_projectionGate.Release();
            }
        }

        /// <summary>
        /// Publishes unavailability while projection updates are serialized by the caller.
        /// </summary>
        private async ValueTask MarkUnavailableCoreAsync(CancellationToken cancellationToken)
        {
            ProjectionFrame? frame = Volatile.Read(ref m_projection);
            if (frame != null)
            {
                Volatile.Write(ref m_projection, frame with { Available = false });
                await NotifyProjectionAsync(cancellationToken).ConfigureAwait(false);
            }
            await m_adapter.MarkUnavailableAsync(
                frame?.Snapshot.SourceTimestampUtc ?? DateTime.UnixEpoch, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask SetProjectionAsync(
            CommittedSnapshot snapshot,
            bool available,
            CancellationToken cancellationToken)
        {
            Volatile.Write(ref m_projection, new ProjectionFrame(
                snapshot, RecyclingModelMapping.ToWire(snapshot), available));
            await NotifyProjectionAsync(cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask NotifyProjectionAsync(CancellationToken cancellationToken)
        {
            if (m_modelContext != null && m_snapshotNode != null && m_availableNode != null)
            {
                m_snapshotNode.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await m_snapshotNode.ClearChangeMasksAsync(m_modelContext, false, cancellationToken)
                    .ConfigureAwait(false);
                m_availableNode.UpdateChangeMasks(NodeStateChangeMasks.Value);
                await m_availableNode.ClearChangeMasksAsync(m_modelContext, false, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An atomically replaced, immutable holder around an unmodified generated wire copy.
        /// </summary>
        private sealed record ProjectionFrame(CommittedSnapshot Snapshot, SnapshotDataType Wire, bool Available);

        private readonly RecyclingHostOptions m_options;
        private readonly RecyclingAuthorization m_authorization;
        private readonly IRecyclingProcessViewAdapter m_adapter;
        private readonly IRecyclingUserCommandForwarder m_forwarder;
        private readonly IServiceProvider m_services;
        private readonly TimeProvider m_timeProvider;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TaskCompletionSource m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SemaphoreSlim m_projectionGate = new(1, 1);
        private readonly SemaphoreSlim m_membershipGate = new(1, 1);
        private RecyclingCoordinator? m_coordinator;
        private ProjectionFrame? m_projection;
        private ISystemContext? m_modelContext;
        private BaseVariableState? m_snapshotNode;
        private BaseVariableState? m_availableNode;
        private ManagedSession? m_activeSession;
        private long m_lastContactTicks;
        private int m_disposed;
        private int m_membershipState;
        private uint m_membershipStatusCode = StatusCodes.BadWaitingForInitialData.Code;
        private string? m_membershipIdentity;
    }

    /// <summary>
    /// Source-generated process-view connection diagnostics.
    /// </summary>
    internal static partial class RecyclingRuntimeLog
    {
        [LoggerMessage(
            EventId = RecyclingServerEventIds.CoordinatorTickDeferred,
            Level = LogLevel.Warning,
            Message = "Recycling coordinator tick deferred because the writer queue is full; retrying next interval.")]
        internal static partial void CoordinatorTickDeferred(this ILogger logger);

        [LoggerMessage(
            EventId = RecyclingServerEventIds.ViewUnavailable,
            Level = LogLevel.Warning,
            Message = "Recycling view {Process} is unavailable; retaining the last source timestamp.")]
        internal static partial void ViewUnavailable(
            this ILogger logger, Exception exception, RecyclingProcess process);

        [LoggerMessage(
            EventId = RecyclingServerEventIds.MembershipPending,
            Level = LogLevel.Warning,
            Message = "Recycling view {Process} membership is {State} ({Status}); " +
                "retaining the authenticated session.")]
        internal static partial void MembershipPending(
            this ILogger logger, RecyclingProcess process, RecyclingMembershipState state, StatusCode status);

        [LoggerMessage(
            EventId = RecyclingServerEventIds.MembershipRejected,
            Level = LogLevel.Error,
            Message = "Recycling view {Process} membership failed ({Status}); automatic reconnect is stopped.")]
        internal static partial void MembershipRejected(
            this ILogger logger, Exception exception, RecyclingProcess process, StatusCode status);
    }
}
