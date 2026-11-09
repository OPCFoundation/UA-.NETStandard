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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Sole engine/journal owner. Changes become visible only after the state and command receipt are durable.
    /// </summary>
    public sealed class RecyclingCoordinator : IAsyncDisposable
    {
        private RecyclingCoordinator(
            RecyclingConfiguration configuration,
            RecyclingEngine engine,
            RecyclingJournal journal,
            TimeProvider timeProvider,
            ITelemetryContext telemetry,
            RecyclingCheckpoint checkpoint)
        {
            m_configuration = configuration;
            m_engine = engine;
            m_journal = journal;
            m_timeProvider = timeProvider;
            m_logger = telemetry.CreateLogger<RecyclingCoordinator>();
            m_checkpoint = checkpoint;
            m_current = checkpoint.Snapshot;
            m_lastOperationalRevision = checkpoint.Snapshot.Revision;
            m_lastControlRevision = checkpoint.Snapshot.Revision;
            m_membershipRevisions = Enum.GetValues<RecyclingProcess>()
                .ToDictionary(static process => process, _ => checkpoint.Snapshot.Revision);
        }

        /// <summary>
        /// Latest immutable committed production image. Reading it never advances time.
        /// </summary>
        public CommittedSnapshot Current => Volatile.Read(ref m_current);

        /// <summary>
        /// Generic configured line identity.
        /// </summary>
        public string LineId => m_checkpoint.LineId;

        /// <summary>
        /// Whether the writer remains usable; I/O failures require explicit recovery by reopening it.
        /// </summary>
        public bool IsHealthy => Volatile.Read(ref m_failure) == null && !m_disposed;

        /// <summary>
        /// Explicit interrupted-write recovery notices.
        /// </summary>
        public ArrayOf<string> RecoveryNotices => m_journal.RecoveryNotices;

        /// <summary>
        /// Opens the exclusive journal, restores validated state, and invalidates old session leases.
        /// Recovery preserves the persisted paused/running lifecycle and all hold dispositions.
        /// </summary>
        public static async ValueTask<RecyclingCoordinator> OpenAsync(
            RecyclingConfiguration configuration,
            IFileSystemProvider fileSystem,
            string lineId,
            TimeProvider timeProvider,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(telemetry);
            ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
            if (lineId.Length > 64)
            {
                throw new ArgumentException("Line identity exceeds its bound.", nameof(lineId));
            }
            configuration.Validate();
            var engine = new RecyclingEngine(configuration);
            RecyclingJournal? journal = new(fileSystem);
            try
            {
                string identity = await ConfigurationIdentityAsync(configuration, cancellationToken)
                    .ConfigureAwait(false);
                RecyclingCheckpoint? recovered = await journal.OpenAsync(identity, lineId, cancellationToken)
                    .ConfigureAwait(false);
                RecyclingCheckpoint checkpoint = recovered ?? new RecyclingCheckpoint
                {
                    ConfigurationHash = identity,
                    LineId = lineId,
                    Snapshot = engine.CreateRun(Guid.NewGuid().ToString("N")) with { ConfigurationId = identity }
                };
                if (checkpoint.Snapshot.ConfigurationId != identity)
                {
                    throw new InvalidDataException("Snapshot configuration/model identity mismatch.");
                }
                engine.ValidateSnapshot(checkpoint.Snapshot);
                ValidateReceipts(checkpoint.Receipts, configuration.MaxCommands);
                checkpoint = checkpoint with { Receipts = CompactHeartbeatReceipts(checkpoint.Receipts) };
                var coordinator = new RecyclingCoordinator(
                    configuration, engine, journal, timeProvider, telemetry, checkpoint);
                if (recovered == null)
                {
                    await journal.CommitAsync(checkpoint, cancellationToken).ConfigureAwait(false);
                    await journal.CheckpointAsync(cancellationToken).ConfigureAwait(false);
                    coordinator.m_checkpoint = journal.Current!;
                }
                else
                {
                    CommittedSnapshot stoppedViews = checkpoint.Snapshot with
                    {
                        Participants = checkpoint.Snapshot.Participants.Span.ToArray()
                            .Select(static participant => participant with
                            {
                                Initialized = false,
                                ConnectionId = string.Empty,
                                LeaseExpiresUtc = DateTime.UnixEpoch
                            }).ToArrayOf(),
                        Stages = checkpoint.Snapshot.Stages.Span.ToArray()
                            .Select(static stage => stage with { State = EquipmentState.Unavailable }).ToArrayOf(),
                        Revision = checked(checkpoint.Snapshot.Revision + 1)
                    };
                    await coordinator.PersistAsync(stoppedViews, checkpoint.Receipts, forceCheckpoint: false)
                        .ConfigureAwait(false);
                }
                coordinator.m_lastOperationalRevision = coordinator.Current.Revision;
                coordinator.m_lastControlRevision = coordinator.Current.Revision;
                foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
                {
                    coordinator.m_membershipRevisions[process] = coordinator.Current.Revision;
                }
                journal = null;
                return coordinator;
            }
            finally
            {
                if (journal != null)
                {
                    await journal.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Applies an authenticated optimistic command or returns its original durable outcome on a retry.
        /// No caller-supplied actor/audit string is part of the wire command.
        /// </summary>
        public async ValueTask<RecyclingCommandOutcome> ExecuteAsync(
            RecyclingPrincipal principal,
            RecyclingCommand command,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(command);
            ValidateCommand(principal, command);
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                foreach (RecyclingCommandReceipt receipt in m_checkpoint.Receipts)
                {
                    if (receipt.Command.RequestId == command.RequestId)
                    {
                        if (receipt.Identity != principal.Identity || receipt.Access != principal.Access ||
                            receipt.Command != command)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidArgument,
                                "Request ID was already used with a different actor/payload.");
                        }
                        return receipt.Outcome;
                    }
                }
                CommittedSnapshot before = Current;
                bool membership = command.Kind is RecyclingCommandKind.Register or RecyclingCommandKind.Initialize or
                    RecyclingCommandKind.Heartbeat or RecyclingCommandKind.Disconnect;
                bool operatorTarget = command.Kind is RecyclingCommandKind.EnableProcess or
                    RecyclingCommandKind.DisableProcess or RecyclingCommandKind.DisposeHold;
                long earliestRevision = membership
                    ? m_membershipRevisions[command.Process]
                    : operatorTarget ? m_lastControlRevision : m_lastOperationalRevision;
                if (command.LineId != LineId || command.RunId != before.RunId ||
                    command.ExpectedRevision < earliestRevision || command.ExpectedRevision > before.Revision)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Stale line, run or state revision.");
                }
                if (command.Kind != RecyclingCommandKind.Heartbeat &&
                    m_checkpoint.Receipts.Span.ToArray().Count(
                        static receipt => receipt.Command.Kind != RecyclingCommandKind.Heartbeat) >=
                            m_configuration.MaxCommands)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyOperations, "Durable command-history capacity has been reached.");
                }
                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                CommittedSnapshot proposed = Apply(before, principal, command, now) with
                {
                    Revision = checked(before.Revision + 1)
                };
                m_engine.ValidateSnapshot(proposed);
                var outcome = new RecyclingCommandOutcome(
                    command.RequestId, proposed.RunId, proposed.Revision, proposed.Tick,
                    StatusCodes.Good.Code, "Applied");
                ArrayOf<RecyclingCommandReceipt> receipts = CompactHeartbeatReceipts(
                    m_checkpoint.Receipts.Span.ToArray()
                        .Append(new RecyclingCommandReceipt(principal.Identity, principal.Access, command, outcome, now))
                        .ToArrayOf());
                cancellationToken.ThrowIfCancellationRequested();
                await PersistAsync(
                    proposed, receipts, command.Kind == RecyclingCommandKind.Checkpoint).ConfigureAwait(false);
                if (!membership || proposed.Lifecycle != before.Lifecycle)
                {
                    m_lastOperationalRevision = proposed.Revision;
                    m_lastControlRevision = proposed.Revision;
                }
                if (membership)
                {
                    m_membershipRevisions[command.Process] = proposed.Revision;
                }
                m_logger.CommandCommitted(command.Kind, proposed.Revision);
                return outcome;
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Journals lease expiry and, if running, advances one material tick.
        /// This is the single clock input; process views cannot call it remotely.
        /// </summary>
        public async ValueTask<CommittedSnapshot> TickAsync(CancellationToken cancellationToken = default)
        {
            await EnterAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfUnavailable();
                CommittedSnapshot before = Current;
                DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
                CommittedSnapshot next = ExpireLeases(before, now);
                if (next.Lifecycle == RunLifecycle.Running)
                {
                    try
                    {
                        next = m_engine.Advance(next, now, Available(next, now));
                    }
                    catch (ServiceResultException exception)
                        when (exception.StatusCode == StatusCodes.BadResourceUnavailable)
                    {
                        next = next with
                        {
                            Lifecycle = RunLifecycle.ResourceLimited,
                            BlockingReason = exception.Message
                        };
                    }
                }
                if (ReferenceEquals(before, next))
                {
                    return before;
                }
                next = next with { Revision = checked(before.Revision + 1) };
                cancellationToken.ThrowIfCancellationRequested();
                await PersistAsync(next, m_checkpoint.Receipts, forceCheckpoint: false).ConfigureAwait(false);
                if (next.Tick != before.Tick || next.Lifecycle != before.Lifecycle)
                {
                    m_lastOperationalRevision = next.Revision;
                }
                foreach (ProcessParticipant participant in before.Participants)
                {
                    if (participant.Initialized && participant.LeaseExpiresUtc <= now)
                    {
                        m_membershipRevisions[participant.Process] = next.Revision;
                    }
                }
                return next;
            }
            finally
            {
                Exit();
            }
        }

        /// <summary>
        /// Enumerates bounded, coalesced current images. Historical lineage remains in the durable snapshot.
        /// </summary>
        public async IAsyncEnumerable<CoordinatorNotification> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var channel = Channel.CreateBounded<CoordinatorNotification>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
            lock (m_observerLock)
            {
                ThrowIfUnavailable();
                if (m_observers.Count >= 32)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations, "Too many snapshot observers.");
                }
                m_observers.Add(channel);
                channel.Writer.TryWrite(new CoordinatorNotification(Current, true, string.Empty));
            }
            try
            {
                await foreach (CoordinatorNotification notification in channel.Reader.ReadAllAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return notification;
                }
            }
            finally
            {
                lock (m_observerLock)
                {
                    m_observers.Remove(channel);
                    channel.Writer.TryComplete();
                }
            }
        }

        /// <summary>
        /// Returns a bounded page of immutable genealogy; invalid windows fail explicitly.
        /// </summary>
        public ArrayOf<MaterialExecution> GetHistory(long afterExecutionId, int maximumResults)
        {
            if (afterExecutionId < 0 || maximumResults is < 1 or > 128)
            {
                throw new ServiceResultException(
                    StatusCodes.BadOutOfRange, "Genealogy page limit must be 1 through 128.");
            }
            return Current.History.Span.ToArray()
                .Where(execution => execution.Id > afterExecutionId)
                .Take(maximumResults).ToArrayOf();
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            lock (m_observerLock)
            {
                return new ValueTask(m_disposeTask ??= DisposeCoreAsync());
            }
        }

        /// <summary>
        /// Hashes the effective simulation configuration and embedded sample model for both host roles.
        /// </summary>
        internal static async ValueTask<string> ConfigurationIdentityAsync(
            RecyclingConfiguration configuration,
            CancellationToken cancellationToken)
        {
            byte[] config = JsonSerializer.SerializeToUtf8Bytes(
                configuration, RecyclingJsonContext.Default.RecyclingConfiguration);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(config);
            Stream model = typeof(RecyclingCoordinator).Assembly
                .GetManifestResourceStream("Recycling.Model.xml")
                ?? throw new InvalidOperationException("The compiled sample model identity resource is missing.");
            await using var modelLifetime = model.ConfigureAwait(false);
            byte[] modelHash = await SHA256.HashDataAsync(model, cancellationToken).ConfigureAwait(false);
            hash.AppendData(modelHash);
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private async Task DisposeCoreAsync()
        {
            await m_writer.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                await m_journal.DisposeAsync().ConfigureAwait(false);
                lock (m_observerLock)
                {
                    foreach (Channel<CoordinatorNotification> channel in m_observers)
                    {
                        channel.Writer.TryComplete();
                    }
                }
            }
            finally
            {
                m_writer.Release();
                m_writer.Dispose();
            }
        }

        private CommittedSnapshot Apply(
            CommittedSnapshot before,
            RecyclingPrincipal principal,
            RecyclingCommand command,
            DateTime now)
        {
            switch (command.Kind)
            {
                case RecyclingCommandKind.Register:
                case RecyclingCommandKind.Initialize:
                case RecyclingCommandKind.Heartbeat:
                case RecyclingCommandKind.Disconnect:
                    return ApplyMembership(before, principal, command, now);
                case RecyclingCommandKind.Pause:
                    if (before.Lifecycle is not (RunLifecycle.Running or RunLifecycle.Paused))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Only a started run may be paused.");
                    }
                    return before with { Lifecycle = RunLifecycle.Paused };
                case RecyclingCommandKind.Resume:
                    if (before.Lifecycle is not (RunLifecycle.Paused or RunLifecycle.Stopped))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Run cannot be resumed in this state.");
                    }
                    return before with { Lifecycle = RunLifecycle.Running };
                case RecyclingCommandKind.Stop:
                    if (before.Lifecycle == RunLifecycle.WaitingForParticipants)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The new-run barrier is not complete.");
                    }
                    return before with { Lifecycle = RunLifecycle.Stopped };
                case RecyclingCommandKind.EnableProcess:
                case RecyclingCommandKind.DisableProcess:
                    bool enabled = command.Kind == RecyclingCommandKind.EnableProcess;
                    return before with
                    {
                        Stages = before.Stages.Span.ToArray().Select(stage => stage.Process == command.Process
                            ? stage with { Enabled = enabled, State = EquipmentState.Stopped }
                            : stage).ToArrayOf()
                    };
                case RecyclingCommandKind.Step:
                    if (before.Lifecycle != RunLifecycle.Paused)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Explicit stepping requires pause.");
                    }
                    CommittedSnapshot stepped = before with { Lifecycle = RunLifecycle.Running };
                    for (int i = 0; i < command.Steps; i++)
                    {
                        stepped = m_engine.Advance(stepped, now, Available(stepped, now));
                    }
                    return stepped with { Lifecycle = RunLifecycle.Paused };
                case RecyclingCommandKind.Reset:
                    if (before.Lifecycle != RunLifecycle.Stopped)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Stop explicitly before resetting.");
                    }
                    return m_engine.CreateRun(Guid.NewGuid().ToString("N")) with
                    {
                        ConfigurationId = m_checkpoint.ConfigurationHash
                    };
                case RecyclingCommandKind.DisposeHold:
                    if (!before.Holds.Span.ToArray().Any(hold => hold.Id == command.HoldId))
                    {
                        throw new ServiceResultException(StatusCodes.BadNotFound, "Unknown quality hold.");
                    }
                    return before with
                    {
                        Holds = before.Holds.Span.ToArray().Select(hold => hold.Id == command.HoldId
                            ? hold with
                            {
                                Disposition = command.Disposition,
                                DisposedBy = principal.Identity,
                                DispositionRevision = checked(before.Revision + 1)
                            }
                            : hold).ToArrayOf()
                    };
                case RecyclingCommandKind.Checkpoint:
                    return before;
                default:
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
        }

        private CommittedSnapshot ApplyMembership(
            CommittedSnapshot before,
            RecyclingPrincipal principal,
            RecyclingCommand command,
            DateTime now)
        {
            ProcessParticipant? existing = before.Participants.Span.ToArray()
                .FirstOrDefault(participant => participant.Process == command.Process);
            var participants = before.Participants.Span.ToArray().ToList();
            if (command.Kind == RecyclingCommandKind.Register)
            {
                if ((existing != null && existing.Identity != principal.Identity) ||
                    (existing != null && existing.ConnectionId != principal.ConnectionId &&
                        existing.ConnectionId.Length != 0 && existing.LeaseExpiresUtc > now) ||
                    participants.Any(participant =>
                        participant.Process != command.Process && participant.Identity == principal.Identity))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied,
                        "The process role already belongs to a distinct live identity/session.");
                }
                participants.RemoveAll(participant => participant.Process == command.Process);
                participants.Add(new ProcessParticipant(
                    command.Process, principal.Identity, principal.ConnectionId, false,
                    now.AddSeconds(m_configuration.LeaseSeconds), command.ExpectedRevision));
            }
            else
            {
                if (existing == null || existing.Identity != principal.Identity ||
                    existing.ConnectionId != principal.ConnectionId ||
                    existing.LeaseExpiresUtc <= now)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Register this authenticated session first.");
                }
                if (command.Kind == RecyclingCommandKind.Heartbeat && !existing.Initialized)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Initial state acknowledgement is required.");
                }
                participants.Remove(existing);
                participants.Add(existing with
                {
                    Initialized = command.Kind != RecyclingCommandKind.Disconnect,
                    LeaseExpiresUtc = command.Kind == RecyclingCommandKind.Disconnect
                        ? now
                        : now.AddSeconds(m_configuration.LeaseSeconds),
                    ObservedRevision = command.ExpectedRevision
                });
            }
            bool ready = participants.Count == 10 && participants.All(participant =>
                participant.Initialized && participant.LeaseExpiresUtc > now);
            return before with
            {
                Participants = participants.OrderBy(static participant => participant.Process).ToArrayOf(),
                Lifecycle = before.Lifecycle == RunLifecycle.WaitingForParticipants && ready
                    ? RunLifecycle.Running
                    : before.Lifecycle
            };
        }

        private static CommittedSnapshot ExpireLeases(CommittedSnapshot snapshot, DateTime now)
        {
            if (!snapshot.Participants.Span.ToArray().Any(participant =>
                participant.Initialized && participant.LeaseExpiresUtc <= now))
            {
                return snapshot;
            }
            return snapshot with
            {
                Participants = snapshot.Participants.Span.ToArray().Select(participant =>
                    participant.LeaseExpiresUtc <= now
                        ? participant with { Initialized = false }
                        : participant).ToArrayOf(),
                Stages = snapshot.Stages.Span.ToArray().Select(stage =>
                    snapshot.Participants.Span.ToArray().Any(participant =>
                        participant.Process == stage.Process && participant.Initialized &&
                        participant.LeaseExpiresUtc > now)
                        ? stage
                        : stage with { State = EquipmentState.Unavailable }).ToArrayOf()
            };
        }

        private static ArrayOf<RecyclingProcess> Available(CommittedSnapshot state, DateTime now)
        {
            return state.Participants.Span.ToArray()
                .Where(participant => participant.Initialized && participant.LeaseExpiresUtc > now)
                .Select(static participant => participant.Process).ToArrayOf();
        }

        private void ValidateCommand(RecyclingPrincipal principal, RecyclingCommand command)
        {
            if (string.IsNullOrWhiteSpace(principal.Identity) || principal.Identity.Length > 128 ||
                string.IsNullOrWhiteSpace(principal.ConnectionId) || principal.ConnectionId.Length > 256 ||
                !Enum.IsDefined(principal.Access) || !Enum.IsDefined(command.Kind) ||
                !Enum.IsDefined(command.Process) || string.IsNullOrWhiteSpace(command.RequestId) ||
                string.IsNullOrWhiteSpace(command.RunId) || string.IsNullOrWhiteSpace(command.LineId) ||
                command.RequestId.Length > 128 || command.RunId.Length > 128 || command.LineId.Length > 64 ||
                command.ExpectedRevision < 0 ||
                (command.Kind == RecyclingCommandKind.Step
                    ? command.Steps < 1 || command.Steps > m_configuration.MaxStepCount
                    : command.Steps != 0) ||
                (command.Kind == RecyclingCommandKind.DisposeHold
                    ? command.HoldId <= 0 ||
                        command.Disposition is not (HoldDisposition.Released or HoldDisposition.Rejected)
                    : command.HoldId != 0 || command.Disposition != HoldDisposition.Held))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "Invalid typed command bounds or fields.");
            }
            bool allowed = command.Kind switch
            {
                RecyclingCommandKind.Register or RecyclingCommandKind.Initialize or
                    RecyclingCommandKind.Heartbeat or RecyclingCommandKind.Disconnect =>
                    principal.Access == RecyclingAccess.ProcessView && principal.Process == command.Process,
                RecyclingCommandKind.EnableProcess or RecyclingCommandKind.DisableProcess or
                    RecyclingCommandKind.DisposeHold =>
                    principal.Access is RecyclingAccess.Operator or RecyclingAccess.SimulatorAdministrator,
                _ => principal.Access == RecyclingAccess.SimulatorAdministrator
            };
            if (!allowed)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUserAccessDenied, "Command is not permitted for this identity.");
            }
        }

        private async ValueTask PersistAsync(
            CommittedSnapshot snapshot,
            ArrayOf<RecyclingCommandReceipt> receipts,
            bool forceCheckpoint)
        {
            try
            {
                RecyclingCheckpoint proposed = m_checkpoint with { Snapshot = snapshot, Receipts = receipts };
                await m_journal.CommitAsync(proposed, CancellationToken.None).ConfigureAwait(false);
                if (forceCheckpoint || m_journal.Current!.Sequence % 8 == 0)
                {
                    await m_journal.CheckpointAsync(CancellationToken.None).ConfigureAwait(false);
                }
                m_checkpoint = m_journal.Current!;
                Volatile.Write(ref m_current, snapshot);
                Publish(new CoordinatorNotification(snapshot, true, string.Empty));
            }
            catch (Exception exception)
            {
                Volatile.Write(ref m_failure, exception);
                Publish(new CoordinatorNotification(
                    Current, false, "Persistence failed; restart and recover before continuing."));
                m_logger.PersistenceFailed(exception);
                throw;
            }
        }

        private void Publish(CoordinatorNotification notification)
        {
            lock (m_observerLock)
            {
                foreach (Channel<CoordinatorNotification> channel in m_observers)
                {
                    channel.Writer.TryWrite(notification);
                }
            }
        }

        private async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref m_waiters) > m_configuration.MaxQueuedCommands)
            {
                Interlocked.Decrement(ref m_waiters);
                throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The single-writer queue is full.");
            }
            try
            {
                await m_writer.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                Interlocked.Decrement(ref m_waiters);
                throw;
            }
        }

        private void Exit()
        {
            m_writer.Release();
            Interlocked.Decrement(ref m_waiters);
        }

        private void ThrowIfUnavailable()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_failure != null)
            {
                throw new ServiceResultException(StatusCodes.BadOutOfService, "Persistence fault requires recovery.");
            }
        }

        private static void ValidateReceipts(ArrayOf<RecyclingCommandReceipt> receipts, int maximum)
        {
            int processCount = Enum.GetValues<RecyclingProcess>().Length;
            if (receipts.Count > maximum + processCount ||
                receipts.Span.ToArray().Count(
                    static receipt => receipt.Command.Kind != RecyclingCommandKind.Heartbeat) > maximum ||
                receipts.Span.ToArray().Any(static receipt =>
                    receipt.Command.Kind == RecyclingCommandKind.Heartbeat &&
                    (receipt.Access != RecyclingAccess.ProcessView || !Enum.IsDefined(receipt.Command.Process))) ||
                receipts.Span.ToArray()
                .Select(static receipt => receipt.Command.RequestId)
                .Distinct(StringComparer.Ordinal).Count() != receipts.Count)
            {
                throw new InvalidDataException("Invalid retained command history.");
            }
        }

        /// <summary>
        /// Retains every durable control outcome and only the latest lease-renewal outcome for each process.
        /// Older verbatim heartbeat retries retain their stale expected revision and cannot renew a lease again.
        /// </summary>
        private static ArrayOf<RecyclingCommandReceipt> CompactHeartbeatReceipts(
            ArrayOf<RecyclingCommandReceipt> receipts)
        {
            var retained = new List<RecyclingCommandReceipt>();
            var heartbeats = new Dictionary<RecyclingProcess, RecyclingCommandReceipt>();
            foreach (RecyclingCommandReceipt receipt in receipts)
            {
                if (receipt.Command.Kind != RecyclingCommandKind.Heartbeat)
                {
                    retained.Add(receipt);
                }
                else if (!heartbeats.TryGetValue(receipt.Command.Process, out RecyclingCommandReceipt? current) ||
                    receipt.Outcome.Revision > current.Outcome.Revision)
                {
                    heartbeats[receipt.Command.Process] = receipt;
                }
            }
            retained.AddRange(heartbeats.Values);
            return retained.OrderBy(static receipt => receipt.Outcome.Revision).ToArrayOf();
        }

        private readonly RecyclingConfiguration m_configuration;
        private readonly RecyclingEngine m_engine;
        private readonly RecyclingJournal m_journal;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_writer = new(1, 1);
        private readonly Lock m_observerLock = new();
        private readonly List<Channel<CoordinatorNotification>> m_observers = [];
        private RecyclingCheckpoint m_checkpoint;
        /// <summary>
        /// Oldest control revision still current after membership-only metadata commits; recovery resets this fence.
        /// </summary>
        private long m_lastOperationalRevision;
        /// <summary>Latest explicit control change; material movement cannot starve equipment or hold actions.</summary>
        private long m_lastControlRevision;
        /// <summary>Latest membership or lease-expiry revision for each independently owned process role.</summary>
        private readonly Dictionary<RecyclingProcess, long> m_membershipRevisions;
        private CommittedSnapshot m_current;
        private Exception? m_failure;
        private Task? m_disposeTask;
        private int m_waiters;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated logging for the single-writer coordinator.
    /// </summary>
    internal static partial class RecyclingCoordinatorLog
    {
        [LoggerMessage(
            EventId = RecyclingServerEventIds.CommandCommitted,
            Level = LogLevel.Debug,
            Message = "Recycling command {Command} committed at revision {Revision}.")]
        internal static partial void CommandCommitted(
            this ILogger logger, RecyclingCommandKind command, long revision);

        [LoggerMessage(
            EventId = RecyclingServerEventIds.PersistenceFailed,
            Level = LogLevel.Error,
            Message = "Recycling persistence failed. The writer is stopped until explicit recovery.")]
        internal static partial void PersistenceFailed(this ILogger logger, Exception exception);
    }
}
