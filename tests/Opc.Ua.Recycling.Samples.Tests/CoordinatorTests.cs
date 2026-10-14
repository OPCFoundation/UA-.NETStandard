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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Exercises the durable coordinator boundary with deterministic time and real workspace-local storage.
    /// </summary>
    [TestFixture]
    public sealed class CoordinatorTests
    {
        /// <summary>
        /// Creates an isolated owned test root; never uses a temporary directory.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_root = Path.Combine(TestContext.CurrentContext.TestDirectory, "r", Guid.NewGuid().ToString("N")[..8]);
            m_files = new PhysicalFileSystemProvider(m_root);
            m_clock = new TestClock();
            m_telemetry = DefaultTelemetry.Create(static logging => logging.ClearProviders());
        }

        /// <summary>
        /// Removes only this test's isolated files after all journal owners have been disposed.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
            if (Directory.Exists(m_root))
            {
                Directory.Delete(m_root, recursive: true);
            }
        }

        /// <summary>
        /// No early start is possible before the tenth distinct initialized identity.
        /// </summary>
        [Test]
        public async Task BarrierRequiresTenDistinctAuthenticatedInitializedRoles()
        {
            await using RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false);
            foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
            {
                RecyclingPrincipal principal = ProcessPrincipal(process);
                await coordinator.ExecuteAsync(principal, Command(coordinator, RecyclingCommandKind.Register, process))
                    .ConfigureAwait(false);
                Assert.That(coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.WaitingForParticipants));
                await coordinator.TickAsync().ConfigureAwait(false);
                Assert.That(coordinator.Current.Tick, Is.Zero);
                await coordinator.ExecuteAsync(
                    principal, Command(coordinator, RecyclingCommandKind.Initialize, process))
                    .ConfigureAwait(false);
            }
            Assert.That(coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Running));
            Assert.That(coordinator.Current.SourceBatches, Is.Zero);
            await coordinator.TickAsync().ConfigureAwait(false);
            Assert.That(coordinator.Current.SourceBatches, Is.EqualTo(1));
        }

        /// <summary>
        /// Process credentials cannot impersonate another role or perform operator/admin operations.
        /// </summary>
        [Test]
        public async Task UnauthorizedCommandsCannotMutateStateOrAcquireAnotherRole()
        {
            await using RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false);
            CommittedSnapshot before = coordinator.Current;
            RecyclingPrincipal principal = ProcessPrincipal(RecyclingProcess.Collection);
            AssertDenied(() => coordinator.ExecuteAsync(
                principal, Command(coordinator, RecyclingCommandKind.Register, RecyclingProcess.Sorting)).AsTask());
            AssertDenied(() => coordinator.ExecuteAsync(
                principal, Command(coordinator, RecyclingCommandKind.Checkpoint)).AsTask());
            AssertDenied(() => coordinator.ExecuteAsync(
                principal, Command(coordinator, RecyclingCommandKind.EnableProcess)).AsTask());
            Assert.That(coordinator.Current, Is.SameAs(before));
        }

        /// <summary>
        /// Duplicate ID/payload returns its original outcome, but conflicts and stale new requests fail.
        /// </summary>
        [Test]
        public async Task DurableRetryReturnsOriginalOutcomeAndRejectsConflictingOrStaleCommands()
        {
            await using RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false);
            RecyclingCommand original = Command(coordinator, RecyclingCommandKind.Checkpoint);
            RecyclingCommandOutcome outcome = await coordinator.ExecuteAsync(s_administrator, original)
                .ConfigureAwait(false);
            Assert.That(
                await coordinator.ExecuteAsync(s_administrator, original).ConfigureAwait(false), Is.EqualTo(outcome));
            Assert.That(coordinator.Current.Revision, Is.EqualTo(outcome.Revision));
            ServiceResultException conflict = Assert.ThrowsAsync<ServiceResultException>(() =>
                coordinator.ExecuteAsync(
                    s_administrator, original with { Kind = RecyclingCommandKind.Pause }).AsTask())!;
            Assert.That(conflict.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            ServiceResultException stale = Assert.ThrowsAsync<ServiceResultException>(() => coordinator.ExecuteAsync(
                s_administrator, original with { RequestId = Guid.NewGuid().ToString("N") }).AsTask())!;
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Routine lease renewals cannot consume the durable budget reserved for production and operator commands.
        /// </summary>
        [Test]
        public async Task PausedMembershipRenewalsDoNotExhaustDurableCommandHistory()
        {
            var configuration = new RecyclingConfiguration { MaxCommands = 24 };
            await using RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false);
            await InitializeAllAsync(coordinator).ConfigureAwait(false);
            RecyclingCommand pause = Command(coordinator, RecyclingCommandKind.Pause);
            RecyclingCommandOutcome pauseOutcome = await coordinator.ExecuteAsync(s_administrator, pause)
                .ConfigureAwait(false);
            CommittedSnapshot paused = coordinator.Current;
            RecyclingProcess[] processes = Enum.GetValues<RecyclingProcess>();
            for (int round = 0; round < 4; round++)
            {
                foreach (RecyclingProcess process in processes)
                {
                    await coordinator.ExecuteAsync(
                        ProcessPrincipal(process), Command(coordinator, RecyclingCommandKind.Heartbeat, process))
                        .ConfigureAwait(false);
                }
                m_clock.Advance(TimeSpan.FromSeconds(2));
            }
            await coordinator.ExecuteAsync(s_administrator, Command(coordinator, RecyclingCommandKind.Checkpoint))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
                Assert.That(coordinator.Current.Tick, Is.EqualTo(paused.Tick));
                Assert.That(coordinator.Current.SourceTimestampUtc, Is.EqualTo(paused.SourceTimestampUtc));
                Assert.That(coordinator.Current.SourceMass, Is.EqualTo(paused.SourceMass));
                Assert.That(coordinator.Current.Packages, Is.EqualTo(paused.Packages));
                Assert.That(coordinator.Current.Participants.Span.ToArray().All(
                    participant => participant.Initialized &&
                        participant.LeaseExpiresUtc > m_clock.GetUtcNow().UtcDateTime), Is.True);
            });
            Assert.That(await coordinator.ExecuteAsync(s_administrator, pause).ConfigureAwait(false),
                Is.EqualTo(pauseOutcome));
        }

        /// <summary>
        /// A full durable control ledger still permits renewal; expired heartbeat retries cannot extend leases again.
        /// </summary>
        [Test]
        public async Task LatestHeartbeatRetryIsDurableWithoutEvictingControlOutcomes()
        {
            var configuration = new RecyclingConfiguration { MaxCommands = 21 };
            RecyclingCommand pause;
            RecyclingCommand newest = null!;
            RecyclingCommandOutcome pauseOutcome;
            RecyclingCommandOutcome newestOutcome = null!;
            string configurationId;
            await using (RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false))
            {
                await InitializeAllAsync(coordinator).ConfigureAwait(false);
                pause = Command(coordinator, RecyclingCommandKind.Pause);
                pauseOutcome = await coordinator.ExecuteAsync(s_administrator, pause).ConfigureAwait(false);
                configurationId = coordinator.Current.ConfigurationId;
                RecyclingPrincipal principal = ProcessPrincipal(RecyclingProcess.Collection);
                RecyclingCommand first = Command(coordinator, RecyclingCommandKind.Heartbeat);
                await coordinator.ExecuteAsync(principal, first).ConfigureAwait(false);
                m_clock.Advance(TimeSpan.FromSeconds(2));
                newest = Command(coordinator, RecyclingCommandKind.Heartbeat);
                newestOutcome = await coordinator.ExecuteAsync(principal, newest).ConfigureAwait(false);
                DateTime expiry = coordinator.Current.Participants[0].LeaseExpiresUtc;
                m_clock.Advance(TimeSpan.FromSeconds(1));
                Assert.That(await coordinator.ExecuteAsync(principal, newest).ConfigureAwait(false),
                    Is.EqualTo(newestOutcome));
                Assert.That(coordinator.Current.Participants[0].LeaseExpiresUtc, Is.EqualTo(expiry));
                ServiceResultException stale = Assert.ThrowsAsync<ServiceResultException>(() =>
                    coordinator.ExecuteAsync(principal, first).AsTask())!;
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(coordinator.Current.Participants[0].LeaseExpiresUtc, Is.EqualTo(expiry));
                ServiceResultException full = Assert.ThrowsAsync<ServiceResultException>(() =>
                    coordinator.ExecuteAsync(s_administrator, Command(coordinator, RecyclingCommandKind.Resume))
                        .AsTask())!;
                Assert.That(full.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(await coordinator.ExecuteAsync(s_administrator, pause).ConfigureAwait(false),
                    Is.EqualTo(pauseOutcome));
            }
            await using (RecyclingCoordinator recovered = await OpenAsync(configuration).ConfigureAwait(false))
            {
                Assert.That(await recovered.ExecuteAsync(s_administrator, pause).ConfigureAwait(false),
                    Is.EqualTo(pauseOutcome));
                Assert.That(await recovered.ExecuteAsync(
                    ProcessPrincipal(RecyclingProcess.Collection), newest).ConfigureAwait(false),
                    Is.EqualTo(newestOutcome));
                Assert.That(recovered.Current.Participants.Span.ToArray().All(
                    static participant => !participant.Initialized), Is.True);
            }
            await using var journal = new RecyclingJournal(m_files);
            RecyclingCheckpoint? image = await journal.OpenAsync(configurationId, "test-line").ConfigureAwait(false);
            Assert.That(image!.Receipts.Count, Is.EqualTo(22));
            Assert.That(image.Receipts.Span.ToArray().Count(
                static receipt => receipt.Command.Kind == RecyclingCommandKind.Heartbeat), Is.EqualTo(1));
        }

        /// <summary>
        /// Recovery reclaims an older history filled by lease renewals while retaining material and all control receipts.
        /// </summary>
        [Test]
        public async Task LegacyHeartbeatHistoryCompactsWithoutChangingPausedProduction()
        {
            var configuration = new RecyclingConfiguration { MaxCommands = 24 };
            var legacyHeartbeats = new List<RecyclingCommandReceipt>();
            CommittedSnapshot before;
            RecyclingCommand pause;
            RecyclingCommandOutcome pauseOutcome;
            await using (RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false))
            {
                await InitializeAllAsync(coordinator).ConfigureAwait(false);
                pause = Command(coordinator, RecyclingCommandKind.Pause);
                pauseOutcome = await coordinator.ExecuteAsync(s_administrator, pause).ConfigureAwait(false);
                RecyclingPrincipal principal = ProcessPrincipal(RecyclingProcess.Collection);
                for (int index = 0; index < 3; index++)
                {
                    RecyclingCommand command = Command(coordinator, RecyclingCommandKind.Heartbeat);
                    RecyclingCommandOutcome outcome = await coordinator.ExecuteAsync(principal, command)
                        .ConfigureAwait(false);
                    legacyHeartbeats.Add(new RecyclingCommandReceipt(
                        principal.Identity, principal.Access, command, outcome, m_clock.GetUtcNow().UtcDateTime));
                }
                before = coordinator.Current;
            }
            await using (var journal = new RecyclingJournal(m_files))
            {
                RecyclingCheckpoint? image = await journal.OpenAsync(before.ConfigurationId, "test-line")
                    .ConfigureAwait(false);
                ArrayOf<RecyclingCommandReceipt> receipts = image!.Receipts.Span.ToArray()
                    .Where(static receipt => receipt.Command.Kind != RecyclingCommandKind.Heartbeat)
                    .Concat(legacyHeartbeats).OrderBy(static receipt => receipt.Outcome.Revision).ToArrayOf();
                Assert.That(receipts.Count, Is.EqualTo(configuration.MaxCommands));
                await journal.CommitAsync(image with { Receipts = receipts }).ConfigureAwait(false);
            }
            await using RecyclingCoordinator recovered = await OpenAsync(configuration).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Current.RunId, Is.EqualTo(before.RunId));
                Assert.That(recovered.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
                Assert.That(recovered.Current.Tick, Is.EqualTo(before.Tick));
                Assert.That(recovered.Current.SourceTimestampUtc, Is.EqualTo(before.SourceTimestampUtc));
                Assert.That(recovered.Current.SourceMass, Is.EqualTo(before.SourceMass));
                Assert.That(recovered.Current.Packages, Is.EqualTo(before.Packages));
            });
            Assert.That(await recovered.ExecuteAsync(s_administrator, pause).ConfigureAwait(false),
                Is.EqualTo(pauseOutcome));
            await recovered.ExecuteAsync(s_administrator, Command(recovered, RecyclingCommandKind.Checkpoint))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Pausing freezes material time while commands, hold dispositions and lease expiry remain available.
        /// </summary>
        [Test]
        public async Task PauseFreezesMaterialAndTimestampButAllowsAuthorizedControlAndDisposition()
        {
            var configuration = RecyclingConfiguration.ForScenario("sorter-drift");
            await using RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false);
            await InitializeAllAsync(coordinator).ConfigureAwait(false);
            for (int i = 0; i < 20; i++)
            {
                await coordinator.TickAsync().ConfigureAwait(false);
            }
            Assert.That(coordinator.Current.Holds.Count, Is.GreaterThan(0));
            await coordinator.ExecuteAsync(s_administrator, Command(coordinator, RecyclingCommandKind.Pause))
                .ConfigureAwait(false);
            CommittedSnapshot paused = coordinator.Current;
            var release = Command(coordinator, RecyclingCommandKind.DisposeHold) with
            {
                HoldId = paused.Holds[0].Id,
                Disposition = HoldDisposition.Released
            };
            await coordinator.ExecuteAsync(s_operator, release).ConfigureAwait(false);
            m_clock.Advance(TimeSpan.FromHours(1));
            await coordinator.TickAsync().ConfigureAwait(false);
            Assert.That(coordinator.Current.Tick, Is.EqualTo(paused.Tick));
            Assert.That(coordinator.Current.SourceTimestampUtc, Is.EqualTo(paused.SourceTimestampUtc));
            Assert.That(coordinator.Current.Revision, Is.GreaterThan(paused.Revision));
            Assert.That(coordinator.Current.Holds[0].Disposition, Is.EqualTo(HoldDisposition.Released));
            Assert.That(coordinator.Current.Holds[0].DisposedBy, Is.EqualTo(s_operator.Identity));
            Assert.That(coordinator.Current.Participants.Span.ToArray().Any(
                static participant => participant.Initialized), Is.False);
        }

        /// <summary>
        /// A crash after durable application but before acknowledgement cannot duplicate command effects.
        /// </summary>
        [Test]
        public async Task RestartAndLostResponseRetryPreservePausedMaterialCountersAndHolds()
        {
            RecyclingConfiguration configuration = RecyclingConfiguration.ForScenario("sorter-drift");
            RecyclingCommand pause;
            CommittedSnapshot before;
            RecyclingCommandOutcome committed;
            await using (RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false))
            {
                await InitializeAllAsync(coordinator).ConfigureAwait(false);
                for (int i = 0; i < 45; i++)
                {
                    await coordinator.TickAsync().ConfigureAwait(false);
                }
                pause = Command(coordinator, RecyclingCommandKind.Pause);
                committed = await coordinator.ExecuteAsync(s_administrator, pause).ConfigureAwait(false);
                before = coordinator.Current;
            }
            await using RecyclingCoordinator recovered = await OpenAsync(configuration).ConfigureAwait(false);
            Assert.That(recovered.Current.RunId, Is.EqualTo(before.RunId));
            Assert.That(recovered.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
            Assert.That(recovered.Current.Tick, Is.EqualTo(before.Tick));
            Assert.That(recovered.Current.Parcels, Is.EqualTo(before.Parcels));
            Assert.That(recovered.Current.Packages, Is.EqualTo(before.Packages));
            Assert.That(recovered.Current.Holds, Is.EqualTo(before.Holds));
            Assert.That(recovered.Current.History, Is.EqualTo(before.History));
            Assert.That(recovered.Current.SourceTimestampUtc, Is.EqualTo(before.SourceTimestampUtc));
            Assert.That(
                await recovered.ExecuteAsync(s_administrator, pause).ConfigureAwait(false), Is.EqualTo(committed));
            Assert.That(recovered.Current.Participants.Span.ToArray().All(
                static participant => !participant.Initialized), Is.True);
        }

        /// <summary>
        /// Synthetic fault counters, provenance and observation timestamps survive pause/restart without replay.
        /// </summary>
        [Test]
        public async Task SyntheticTelemetryAndAbsoluteFaultCountersSurvivePausedRecovery()
        {
            RecyclingConfiguration configuration = RecyclingConfiguration.ForScenario("equipment-faults") with
            {
                PlannedSourceBatches = 2,
                SourceZones = ["zone-1", "zone-2"],
                HotWashPh = 9.25,
                SourceColorVariationMilligrams = 0
            };
            CommittedSnapshot before;
            await using (RecyclingCoordinator coordinator = await OpenAsync(configuration).ConfigureAwait(false))
            {
                await InitializeAllAsync(coordinator).ConfigureAwait(false);
                for (int tick = 0; tick < 45; tick++)
                {
                    await coordinator.TickAsync().ConfigureAwait(false);
                }
                await coordinator.ExecuteAsync(
                    s_administrator, Command(coordinator, RecyclingCommandKind.Pause)).ConfigureAwait(false);
                before = coordinator.Current;
                Assert.That(before.Stages[3].JamEvents, Is.EqualTo(1));
                Assert.That(before.Stages[6].JamEvents, Is.EqualTo(1));
                Assert.That(before.Stages[18].StrandBreakEvents, Is.EqualTo(1));
                Assert.That(before.Stages[10].Ph, Is.EqualTo(9.25));
            }
            m_clock.Advance(TimeSpan.FromHours(1));
            await using RecyclingCoordinator recovered = await OpenAsync(configuration).ConfigureAwait(false);
            Assert.That(recovered.Current.Lifecycle, Is.EqualTo(RunLifecycle.Paused));
            Assert.That(recovered.Current.Sources, Is.EqualTo(before.Sources));
            Assert.That(recovered.Current.Packages, Is.EqualTo(before.Packages));
            Assert.That(recovered.Current.Stages[10].PhSourceTimestampUtc,
                Is.EqualTo(before.Stages[10].PhSourceTimestampUtc));
            Assert.That(recovered.Current.Stages[0].Color, Is.EqualTo(before.Stages[0].Color));
            Assert.That(recovered.Current.Stages[0].ColorSourceTimestampUtc,
                Is.EqualTo(before.Stages[0].ColorSourceTimestampUtc));
            await InitializeAllAsync(recovered).ConfigureAwait(false);
            await recovered.ExecuteAsync(
                s_administrator, Command(recovered, RecyclingCommandKind.Resume)).ConfigureAwait(false);
            await recovered.TickAsync().ConfigureAwait(false);
            Assert.That(recovered.Current.Stages[3].JamEvents, Is.EqualTo(1));
            Assert.That(recovered.Current.Stages[6].JamEvents, Is.EqualTo(1));
            Assert.That(recovered.Current.Stages[18].StrandBreakEvents, Is.EqualTo(1));
        }

        /// <summary>
        /// Legacy frame identifiers fail recovery explicitly rather than silently starting a new run.
        /// </summary>
        [Test]
        public async Task LegacyPersistenceFrameIsRejectedAfterSchemaUpgrade()
        {
            await using (RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false))
            {
                Assert.That(coordinator.Current.SchemaVersion, Is.EqualTo(2));
            }
            string path = Directory.GetFiles(m_root, "*.record").Single();
            byte[] frame = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            frame[7] = (byte)'1';
            await File.WriteAllBytesAsync(path, frame).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(() => OpenAsync().AsTask());
        }

        /// <summary>
        /// Partial staging is diagnosed; corruption in an acknowledged generation is a hard recovery failure.
        /// </summary>
        [Test]
        public async Task PartialStagingIsExplicitAndCommittedCorruptionNeverCreatesFreshState()
        {
            string run;
            await using (RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false))
            {
                run = coordinator.Current.RunId;
            }
            await File.WriteAllBytesAsync(Path.Combine(m_root, "journal.pending"), new byte[] { 1, 2, 3 })
                .ConfigureAwait(false);
            await using (RecyclingCoordinator recovered = await OpenAsync().ConfigureAwait(false))
            {
                Assert.That(recovered.RecoveryNotices.Count, Is.EqualTo(1));
                Assert.That(recovered.Current.RunId, Is.EqualTo(run));
            }
            string committed = Directory.GetFiles(m_root, "*.record")[0];
            await File.WriteAllBytesAsync(committed, new byte[] { 1, 2, 3 }).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidDataException>(() => OpenAsync().AsTask());
        }

        /// <summary>
        /// The configuration hash and exclusive writer lease are enforced on every open.
        /// </summary>
        [Test]
        public async Task StateRootRejectsConcurrentWritersAndConfigurationMismatch()
        {
            await using (RecyclingCoordinator coordinator = await OpenAsync().ConfigureAwait(false))
            {
                Assert.ThrowsAsync<IOException>(async () =>
                {
                    await using RecyclingCoordinator unexpected = await OpenAsync().ConfigureAwait(false);
                });
                Assert.That(coordinator.Current.Tick, Is.Zero);
            }
            Assert.ThrowsAsync<InvalidDataException>(() =>
                OpenAsync(new RecyclingConfiguration { Seed = 7 }).AsTask());
        }

        /// <summary>
        /// Releasing a journal's lease permanently removes its ability to checkpoint, commit or reopen.
        /// </summary>
        [Test]
        public async Task DisposedJournalCannotCheckpointOrDeleteAReplacementWritersCommit()
        {
            var original = new RecyclingJournal(m_files);
            await using (original.ConfigureAwait(false))
            {
                await original.OpenAsync("test-configuration", "test-line").ConfigureAwait(false);
                await original.CommitAsync(new RecyclingCheckpoint
                {
                    ConfigurationHash = "test-configuration",
                    LineId = "test-line",
                    Snapshot = new RecyclingEngine(new RecyclingConfiguration()).CreateRun("journal-run")
                }).ConfigureAwait(false);
            }
            await using (var replacement = new RecyclingJournal(m_files))
            {
                RecyclingCheckpoint? recovered = await replacement.OpenAsync("test-configuration", "test-line")
                    .ConfigureAwait(false);
                await replacement.CommitAsync(recovered! with
                {
                    Snapshot = recovered.Snapshot with { Revision = 1 }
                }).ConfigureAwait(false);
                Assert.ThrowsAsync<ObjectDisposedException>(() => original.CheckpointAsync().AsTask());
                Assert.ThrowsAsync<ObjectDisposedException>(() => original.CommitAsync(recovered).AsTask());
                Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    original.OpenAsync("test-configuration", "test-line").AsTask());
            }
            await using var verifier = new RecyclingJournal(m_files);
            RecyclingCheckpoint? latest = await verifier.OpenAsync("test-configuration", "test-line")
                .ConfigureAwait(false);
            Assert.That(latest!.Sequence, Is.EqualTo(1));
            Assert.That(latest.Snapshot.Revision, Is.EqualTo(1));
        }

        /// <summary>
        /// A blocked durable publication cannot leak into state or acknowledgement, and queue overflow is explicit.
        /// </summary>
        [Test]
        public async Task DurableBarrierPrecedesPublicationAndBoundsConcurrentCommands()
        {
            Mock<IFileSystemProvider> provider = ForwardingFileSystem(m_files);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool block = false;
            provider.Setup(fileSystem => fileSystem.MoveAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(async (string source, string target, CancellationToken ct) =>
                {
                    if (block && source == "journal.pending")
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    await m_files.MoveAsync(source, target, ct).ConfigureAwait(false);
                });
            await using RecyclingCoordinator coordinator = await RecyclingCoordinator.OpenAsync(
                new RecyclingConfiguration { MaxQueuedCommands = 1 },
                provider.Object, "test-line", m_clock, m_telemetry).ConfigureAwait(false);
            CommittedSnapshot before = coordinator.Current;
            block = true;
            Task<RecyclingCommandOutcome> pending = coordinator.ExecuteAsync(
                s_administrator, Command(coordinator, RecyclingCommandKind.Checkpoint)).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(coordinator.Current, Is.SameAs(before));
                Assert.That(pending.IsCompleted, Is.False);
                ServiceResultException overflow = Assert.ThrowsAsync<ServiceResultException>(() =>
                    coordinator.TickAsync().AsTask())!;
                Assert.That(overflow.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            }
            finally
            {
                release.TrySetResult();
            }
            RecyclingCommandOutcome outcome = await pending.ConfigureAwait(false);
            Assert.That(coordinator.Current.Revision, Is.EqualTo(outcome.Revision));
        }

        /// <summary>
        /// A definite I/O failure leaves the last committed image and prevents continued uncertain writes.
        /// </summary>
        [Test]
        public async Task WriteFailureDoesNotPublishOrAcknowledgeUncommittedEffects()
        {
            Mock<IFileSystemProvider> provider = ForwardingFileSystem(m_files);
            await using RecyclingCoordinator coordinator = await RecyclingCoordinator.OpenAsync(
                new RecyclingConfiguration(), provider.Object, "test-line", m_clock, m_telemetry)
                .ConfigureAwait(false);
            CommittedSnapshot before = coordinator.Current;
            provider.Setup(fileSystem => fileSystem.OpenWriteAsync(
                    "journal.pending", FileWriteMode.Truncate, It.IsAny<CancellationToken>()))
                .Throws(new IOException("Injected persistence failure."));
            Assert.ThrowsAsync<IOException>(() => coordinator.ExecuteAsync(
                s_administrator, Command(coordinator, RecyclingCommandKind.Checkpoint)).AsTask());
            Assert.That(coordinator.IsHealthy, Is.False);
            Assert.That(coordinator.Current, Is.SameAs(before));
            ServiceResultException stopped =
                Assert.ThrowsAsync<ServiceResultException>(() => coordinator.TickAsync().AsTask())!;
            Assert.That(stopped.StatusCode, Is.EqualTo(StatusCodes.BadOutOfService));
        }

        private ValueTask<RecyclingCoordinator> OpenAsync(RecyclingConfiguration? configuration = null)
        {
            return RecyclingCoordinator.OpenAsync(
                configuration ?? new RecyclingConfiguration(), m_files, "test-line", m_clock, m_telemetry);
        }

        /// <summary>
        /// Creates a request against the latest directly observed committed image.
        /// </summary>
        internal static RecyclingCommand Command(
            RecyclingCoordinator coordinator,
            RecyclingCommandKind kind,
            RecyclingProcess process = RecyclingProcess.Collection)
        {
            return new RecyclingCommand
            {
                RequestId = Guid.NewGuid().ToString("N"),
                LineId = coordinator.LineId,
                RunId = coordinator.Current.RunId,
                ExpectedRevision = coordinator.Current.Revision,
                Kind = kind,
                Process = process
            };
        }

        private static RecyclingPrincipal ProcessPrincipal(RecyclingProcess process)
        {
            return new RecyclingPrincipal(
                "view-" + process, RecyclingAccess.ProcessView, process, "session-" + process);
        }

        /// <summary>
        /// Registers and initializes all ten fixture-owned process identities.
        /// </summary>
        internal static async Task InitializeAllAsync(RecyclingCoordinator coordinator)
        {
            foreach (RecyclingProcess process in Enum.GetValues<RecyclingProcess>())
            {
                RecyclingPrincipal principal = ProcessPrincipal(process);
                await coordinator.ExecuteAsync(principal, Command(coordinator, RecyclingCommandKind.Register, process))
                    .ConfigureAwait(false);
                await coordinator.ExecuteAsync(
                    principal, Command(coordinator, RecyclingCommandKind.Initialize, process))
                    .ConfigureAwait(false);
            }
        }

        private static void AssertDenied(Func<Task<RecyclingCommandOutcome>> action)
        {
            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await action().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Forwards persistence operations while permitting deterministic interruption of one call.
        /// </summary>
        internal static Mock<IFileSystemProvider> ForwardingFileSystem(PhysicalFileSystemProvider inner)
        {
            var mock = new Mock<IFileSystemProvider>(MockBehavior.Strict);
            mock.SetupGet(static provider => provider.MountName).Returns(inner.MountName);
            mock.SetupGet(static provider => provider.IsWritable).Returns(true);
            mock.Setup(provider => provider.CreateDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.CreateDirectoryAsync(path, ct));
            mock.Setup(provider => provider.CreateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.CreateFileAsync(path, ct));
            mock.Setup(provider => provider.GetEntryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.GetEntryAsync(path, ct));
            mock.Setup(provider => provider.EnumerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.EnumerateAsync(path, ct));
            mock.Setup(provider => provider.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.OpenReadAsync(path, ct));
            mock.Setup(provider => provider.OpenWriteAsync(
                    It.IsAny<string>(), It.IsAny<FileWriteMode>(), It.IsAny<CancellationToken>()))
                .Returns((string path, FileWriteMode mode, CancellationToken ct) =>
                    inner.OpenWriteAsync(path, mode, ct));
            mock.Setup(provider => provider.MoveAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string source, string target, CancellationToken ct) => inner.MoveAsync(source, target, ct));
            mock.Setup(provider => provider.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string path, CancellationToken ct) => inner.DeleteAsync(path, ct));
            return mock;
        }

        private static readonly RecyclingPrincipal s_administrator =
            new("administrator", RecyclingAccess.SimulatorAdministrator,
                RecyclingProcess.Collection, "administrator-session");
        private static readonly RecyclingPrincipal s_operator =
            new("operator", RecyclingAccess.Operator, RecyclingProcess.Collection, "operator-session");
        private string m_root = string.Empty;
        private PhysicalFileSystemProvider m_files = null!;
        private TestClock m_clock = null!;
        private ITelemetryContext m_telemetry = null!;
    }

    /// <summary>
    /// Explicit wall-time input for deterministic lease and source-timestamp tests.
    /// </summary>
    internal sealed class TestClock : TimeProvider
    {
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(Interlocked.Read(ref m_ticks), TimeSpan.Zero);
        }

        /// <summary>
        /// Advances only the supplied wall clock, not the simulation.
        /// </summary>
        internal void Advance(TimeSpan duration)
        {
            Interlocked.Add(ref m_ticks, duration.Ticks);
        }

        private long m_ticks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;
    }
}
