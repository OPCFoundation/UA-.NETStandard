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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Exercises absolute process-view projection without timers, sockets or a second material authority.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingRuntimeTests
    {
        /// <summary>
        /// A rejected timer admission is retried after the occupied writer is released.
        /// </summary>
        [Test]
        public async Task AutomaticTicksResumeAfterWriterAdmissionIsRejected()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "r",
                Guid.NewGuid().ToString("N")[..8]);
            var provider = CoordinatorTests.ForwardingFileSystem(new PhysicalFileSystemProvider(root));
            TimeProvider clock = ControlledClock(out Action<TimeSpan> advance, out Task timerCreated);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var logger = new Mock<ILogger>();
            logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            logger.Setup(value => value.Log(
                    LogLevel.Warning, It.Is<EventId>(id => id.Name == "CoordinatorTickDeferred"),
                    It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(_ => deferred.TrySetResult()));
            var logs = new Mock<ILoggerProvider>();
            logs.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                logging => logging.ClearProviders().AddProvider(logs.Object));
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(provider.Object).BuildServiceProvider();
            using var stopping = new CancellationTokenSource();
            try
            {
                await using var runtime = new RecyclingRuntime(
                    new RecyclingHostOptions
                    {
                        Simulation = new RecyclingConfiguration { MaxQueuedCommands = 1 },
                        TickPeriod = TimeSpan.FromSeconds(1)
                    },
                    new RecyclingAuthorization([]), Mock.Of<IRecyclingProcessViewAdapter>(),
                    Mock.Of<IRecyclingUserCommandForwarder>(), services, clock, telemetry);
                await runtime.StartAsync(stopping.Token).ConfigureAwait(false);
                await CoordinatorTests.InitializeAllAsync(runtime.Coordinator).ConfigureAwait(false);
                provider.Setup(value => value.OpenWriteAsync(
                        "journal.pending", FileWriteMode.Truncate, It.IsAny<CancellationToken>()))
                    .Returns(async (string path, FileWriteMode mode, CancellationToken ct) =>
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                        return await new PhysicalFileSystemProvider(root).OpenWriteAsync(path, mode, ct)
                            .ConfigureAwait(false);
                    });
                var administrator = new RecyclingPrincipal("admin", RecyclingAccess.SimulatorAdministrator,
                    RecyclingProcess.Collection, "admin-session");
                Task<RecyclingCommandOutcome> command = runtime.Coordinator.ExecuteAsync(administrator,
                    CoordinatorTests.Command(runtime.Coordinator, RecyclingCommandKind.Checkpoint)).AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                runtime.SignalAddressSpaceReady();
                Task run = runtime.RunAsync(stopping.Token);
                try
                {
                    await timerCreated.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    advance(TimeSpan.FromSeconds(1));
                    await deferred.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    release.TrySetResult();
                    await command.ConfigureAwait(false);
                    advance(TimeSpan.FromSeconds(1));
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await foreach (CoordinatorNotification notification in runtime.Coordinator.WatchAsync(timeout.Token)
                        .ConfigureAwait(false))
                    {
                        if (notification.Snapshot.Tick > 0)
                        {
                            Assert.That(notification.IsAvailable, Is.True);
                            break;
                        }
                    }
                    Assert.That(runtime.Coordinator.Current.Tick, Is.GreaterThan(0));
                }
                finally
                {
                    release.TrySetResult();
                    await command.ConfigureAwait(false);
                    await stopping.CancelAsync().ConfigureAwait(false);
                    await run.ConfigureAwait(false);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        /// <summary>
        /// A blocked connection attempt cannot leave a previous equipment image good beyond its contact deadline.
        /// </summary>
        [Test]
        public async Task MissedContactMarksEquipmentUnavailableWithoutWaitingForTransportFailure()
        {
            TimeProvider clock = ControlledClock(out Action<TimeSpan> advance, out Task timerCreated);
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            CommittedSnapshot snapshot = await CoordinatorTests.CreateInitialSnapshotAsync(
                new RecyclingConfiguration(), clock, telemetry).ConfigureAwait(false);
            snapshot = snapshot with
            {
                Revision = 7,
                SourceTimestampUtc = clock.GetUtcNow().UtcDateTime
            };
            var unavailable = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
            adapter.Setup(value => value.ApplyAsync(snapshot, It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            adapter.Setup(value => value.MarkUnavailableAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, CancellationToken>((timestamp, _) => unavailable.TrySetResult(timestamp))
                .Returns(ValueTask.CompletedTask);
            var connection = new TaskCompletionSource<ManagedSession>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(ct => connection.Task.WaitAsync(ct))
                .BuildServiceProvider();
            await using var runtime = new RecyclingRuntime(
                new RecyclingHostOptions
                {
                    Role = RecyclingHostRole.ProcessView,
                    ViewPollPeriod = TimeSpan.FromSeconds(1)
                },
                new RecyclingAuthorization([]), adapter.Object, Mock.Of<IRecyclingUserCommandForwarder>(),
                services, clock, telemetry);
            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            using var stopping = new CancellationTokenSource();
            runtime.SignalAddressSpaceReady();
            Task run = runtime.RunAsync(stopping.Token);
            try
            {
                await timerCreated.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                advance(TimeSpan.FromSeconds(3));
                DateTime retained = await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(retained, Is.EqualTo(snapshot.SourceTimestampUtc));
                Assert.That(runtime.Latest, Is.SameAs(snapshot));
                Assert.That(connection.Task.IsCompleted, Is.False);
                await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
                adapter.Verify(value => value.ApplyAsync(snapshot, It.IsAny<CancellationToken>()), Times.Exactly(2));
            }
            finally
            {
                await stopping.CancelAsync().ConfigureAwait(false);
                await run.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Recovering data quality reprojects an unchanged image, without advancing or duplicating production.
        /// </summary>
        [Test]
        public async Task RestoringAvailabilityReappliesSameRevisionWithoutChangingCommittedProduction()
        {
            var clock = new TestClock();
            DateTime sourceTimestamp = clock.GetUtcNow().UtcDateTime;
            var configuration = new RecyclingConfiguration { PlannedSourceBatches = 1 };
            var engine = new RecyclingEngine(configuration);
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            CommittedSnapshot snapshot = await CoordinatorTests.CreateInitialSnapshotAsync(
                configuration, clock, telemetry).ConfigureAwait(false);
            snapshot = snapshot with
            {
                Lifecycle = RunLifecycle.Running,
                Revision = 7
            };
            ArrayOf<RecyclingProcess> processes = Enum.GetValues<RecyclingProcess>().ToArrayOf();
            for (int tick = 0; tick < 80; tick++)
            {
                snapshot = engine.Advance(snapshot, sourceTimestamp, processes);
            }
            snapshot = snapshot with { Lifecycle = RunLifecycle.Paused };
            Assert.That(snapshot.Packages.Count, Is.EqualTo(1));
            Assert.That(snapshot.History.Count, Is.GreaterThan(0));

            bool nativeValuesAvailable = false;
            int nativePackageCount = 0;
            DateTime nativeSourceTimestamp = DateTime.UnixEpoch;
            var applied = new List<CommittedSnapshot>();
            var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
            adapter.Setup(view => view.ApplyAsync(
                    It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback<CommittedSnapshot, CancellationToken>((committed, _) =>
                {
                    applied.Add(committed);
                    nativeValuesAvailable = true;
                    nativePackageCount = committed.Packages.Count;
                    nativeSourceTimestamp = committed.SourceTimestampUtc;
                })
                .Returns(ValueTask.CompletedTask);
            adapter.Setup(view => view.MarkUnavailableAsync(
                    It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, CancellationToken>((timestamp, _) =>
                {
                    nativeValuesAvailable = false;
                    nativeSourceTimestamp = timestamp;
                })
                .Returns(ValueTask.CompletedTask);
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            await using var runtime = new RecyclingRuntime(
                new RecyclingHostOptions { Role = RecyclingHostRole.ProcessView, Simulation = configuration },
                new RecyclingAuthorization([]),
                adapter.Object,
                Mock.Of<IRecyclingUserCommandForwarder>(),
                services,
                clock,
                telemetry);

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            await runtime.ApplyProjectionAsync(snapshot, available: false).ConfigureAwait(false);
            Assert.That(nativeValuesAvailable, Is.False);
            Assert.That(applied, Has.Count.EqualTo(1));
            clock.Advance(TimeSpan.FromMinutes(5));

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            Assert.That(nativeValuesAvailable, Is.True);
            Assert.That(applied, Has.Count.EqualTo(2));
            Assert.That(applied[1], Is.SameAs(snapshot));
            Assert.That(nativePackageCount, Is.EqualTo(1));
            Assert.That(nativeSourceTimestamp, Is.EqualTo(sourceTimestamp));
            CommittedSnapshot projected = runtime.Latest ??
                throw new InvalidOperationException("No committed image was projected.");
            Assert.That(projected, Is.SameAs(snapshot));
            Assert.That(projected.Revision, Is.EqualTo(7));
            Assert.That(projected.Tick, Is.EqualTo(80));
            Assert.That(projected.History, Is.EqualTo(applied[0].History));
            Assert.That(projected.SourceMass, Is.EqualTo(applied[0].SourceMass));

            await runtime.ApplyProjectionAsync(snapshot, available: true).ConfigureAwait(false);
            Assert.That(applied, Has.Count.EqualTo(2), "Ordinary duplicate delivery is still coalesced.");
            adapter.Verify(
                view => view.MarkUnavailableAsync(sourceTimestamp, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A view refuses incompatible or missing configuration identity before registering or projecting state.
        /// </summary>
        [TestCase(8, 30, false)]
        [TestCase(4, 60, false)]
        [TestCase(4, 30, true)]
        public async Task MembershipRejectsConfigurationMismatchBeforeCommandsOrProjection(
            int residenceTicks,
            int tickSeconds,
            bool omitConfigurationId)
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "r",
                Guid.NewGuid().ToString("N")[..8]);
            var clock = new TestClock();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            try
            {
                await using RecyclingCoordinator coordinator = await RecyclingCoordinator.OpenAsync(
                    DryingConfiguration(residenceTicks, tickSeconds), new PhysicalFileSystemProvider(root),
                    "runtime-line", clock, telemetry).ConfigureAwait(false);
                CommittedSnapshot before = coordinator.Current;
                var principal = new RecyclingPrincipal(
                    "drying-view", RecyclingAccess.ProcessView, RecyclingProcess.Drying, "drying-session");
                Mock<IRecyclingMembershipClient> client = MembershipClient(
                    coordinator, principal, omitConfigurationId);
                var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
                adapter.Setup(value => value.ApplyAsync(
                        It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.CompletedTask);
                adapter.Setup(value => value.MarkUnavailableAsync(
                        It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.CompletedTask);
                await using var runtime = new RecyclingRuntime(
                    new RecyclingHostOptions
                    {
                        Role = RecyclingHostRole.ProcessView,
                        Process = RecyclingProcess.Drying,
                        LineId = coordinator.LineId,
                        Simulation = new RecyclingConfiguration()
                    },
                    new RecyclingAuthorization([]), adapter.Object, Mock.Of<IRecyclingUserCommandForwarder>(),
                    services, clock, telemetry);
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);

                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(() =>
                    runtime.RefreshMembershipAsync(client.Object).AsTask())!;

                Assert.Multiple(() =>
                {
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    Assert.That(runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.Faulted));
                    Assert.That(runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    Assert.That(runtime.IsAvailable, Is.False);
                    Assert.That(runtime.Latest, Is.Null);
                    Assert.That(coordinator.Current, Is.SameAs(before));
                });
                client.Verify(value => value.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
                adapter.Verify(value => value.ApplyAsync(
                    It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Matching nondefault configuration permits registration, initial projection and acknowledgement in order.
        /// </summary>
        [Test]
        public async Task MembershipInitializesMatchingConfigurationAfterProjection()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "r",
                Guid.NewGuid().ToString("N")[..8]);
            RecyclingConfiguration configuration = DryingConfiguration(8, 60);
            var clock = new TestClock();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            try
            {
                await using RecyclingCoordinator coordinator = await RecyclingCoordinator.OpenAsync(
                    configuration, new PhysicalFileSystemProvider(root), "runtime-line", clock, telemetry)
                    .ConfigureAwait(false);
                var principal = new RecyclingPrincipal(
                    "drying-view", RecyclingAccess.ProcessView, RecyclingProcess.Drying, "drying-session");
                var sequence = new List<string>();
                Mock<IRecyclingMembershipClient> client = MembershipClient(
                    coordinator, principal, omitConfigurationId: false);
                client.Setup(value => value.ExecuteAsync(
                        It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                    .Callback<RecyclingCommand, CancellationToken>((command, _) =>
                        sequence.Add(command.Kind.ToString()))
                    .Returns((RecyclingCommand command, CancellationToken ct) =>
                        coordinator.ExecuteAsync(principal, command, ct));
                var applied = new List<CommittedSnapshot>();
                var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
                adapter.Setup(value => value.ApplyAsync(
                        It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                    .Callback<CommittedSnapshot, CancellationToken>((snapshot, _) =>
                    {
                        sequence.Add("Apply");
                        applied.Add(snapshot);
                    })
                    .Returns(ValueTask.CompletedTask);
                adapter.Setup(value => value.MarkUnavailableAsync(
                        It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.CompletedTask);
                await using var runtime = new RecyclingRuntime(
                    new RecyclingHostOptions
                    {
                        Role = RecyclingHostRole.ProcessView,
                        Process = RecyclingProcess.Drying,
                        LineId = coordinator.LineId,
                        Simulation = configuration
                    },
                    new RecyclingAuthorization([]), adapter.Object, Mock.Of<IRecyclingUserCommandForwarder>(),
                    services, clock, telemetry);
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);

                RecyclingMembershipState result = await runtime.RefreshMembershipAsync(client.Object)
                    .ConfigureAwait(false);

                string[] expected = ["Register", "Apply", "Initialize"];
                Assert.That(sequence, Is.EqualTo(expected));
                Assert.That(applied, Has.Count.EqualTo(1));
                Assert.That(coordinator.Current.Participants.Count, Is.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(RecyclingMembershipState.Ready));
                    Assert.That(runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(applied[0].Participants[0].Initialized, Is.False);
                    Assert.That(applied[0].ConfigurationId, Is.EqualTo(coordinator.Current.ConfigurationId));
                    Assert.That(runtime.Latest, Is.SameAs(applied[0]));
                    Assert.That(coordinator.Current.Participants[0].Initialized, Is.True);
                    Assert.That(coordinator.Current.Participants[0].Process, Is.EqualTo(RecyclingProcess.Drying));
                    Assert.That(coordinator.Current.Participants[0].ConnectionId, Is.EqualTo(principal.ConnectionId));
                    Assert.That(coordinator.Current.Tick, Is.Zero);
                    Assert.That(coordinator.Current.Lifecycle, Is.EqualTo(RunLifecycle.WaitingForParticipants));
                });
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// A newer incompatible image cannot replace already published material values or their source time.
        /// </summary>
        [Test]
        public async Task ProjectionRejectsConfigurationMismatchAndRetainsLastCommittedImage()
        {
            string root = Path.Combine(TestContext.CurrentContext.TestDirectory, "r",
                Guid.NewGuid().ToString("N")[..8]);
            var configuration = new RecyclingConfiguration();
            var clock = new TestClock();
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            try
            {
                await using RecyclingCoordinator coordinator = await RecyclingCoordinator.OpenAsync(
                    configuration, new PhysicalFileSystemProvider(Path.Combine(root, "matching")),
                    "runtime-line", clock, telemetry).ConfigureAwait(false);
                await CoordinatorTests.InitializeAllAsync(coordinator).ConfigureAwait(false);
                CommittedSnapshot committed = await coordinator.TickAsync().ConfigureAwait(false);
                var applied = new List<CommittedSnapshot>();
                var adapter = new Mock<IRecyclingProcessViewAdapter>(MockBehavior.Strict);
                adapter.Setup(value => value.ApplyAsync(
                        It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()))
                    .Callback<CommittedSnapshot, CancellationToken>((snapshot, _) => applied.Add(snapshot))
                    .Returns(ValueTask.CompletedTask);
                adapter.Setup(value => value.MarkUnavailableAsync(
                        It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .Returns(ValueTask.CompletedTask);
                await using var runtime = new RecyclingRuntime(
                    new RecyclingHostOptions
                    {
                        Role = RecyclingHostRole.ProcessView,
                        Process = RecyclingProcess.Drying,
                        LineId = coordinator.LineId,
                        Simulation = configuration
                    },
                    new RecyclingAuthorization([]), adapter.Object, Mock.Of<IRecyclingUserCommandForwarder>(),
                    services, clock, telemetry);
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
                await runtime.ApplyProjectionAsync(committed, available: true).ConfigureAwait(false);

                await using RecyclingCoordinator replacement = await RecyclingCoordinator.OpenAsync(
                    configuration with { TickSeconds = 60 },
                    new PhysicalFileSystemProvider(Path.Combine(root, "mismatched")),
                    coordinator.LineId, clock, telemetry).ConfigureAwait(false);
                await CoordinatorTests.InitializeAllAsync(replacement).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                await replacement.TickAsync().ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(1));
                CommittedSnapshot incompatible = await replacement.TickAsync().ConfigureAwait(false);
                Assert.That(incompatible.Revision, Is.GreaterThan(committed.Revision));
                Assert.That(incompatible.SourceTimestampUtc, Is.GreaterThan(committed.SourceTimestampUtc));
                Assert.That(incompatible.ConfigurationId, Is.Not.EqualTo(committed.ConfigurationId));

                ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(() =>
                    runtime.ApplyProjectionAsync(incompatible, available: true).AsTask())!;

                Assert.Multiple(() =>
                {
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    Assert.That(runtime.MembershipState, Is.EqualTo(RecyclingMembershipState.Faulted));
                    Assert.That(runtime.MembershipStatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                    Assert.That(runtime.Latest, Is.SameAs(committed));
                    Assert.That(applied, Has.Count.EqualTo(1));
                    Assert.That(applied[0], Is.SameAs(committed));
                    Assert.That(applied[0].SourceBatches, Is.EqualTo(1));
                    Assert.That(applied[0].SourceMass.Total, Is.EqualTo(5_250_000));
                    Assert.That(applied[0].LogicalSeconds, Is.EqualTo(30));
                });
                adapter.Verify(value => value.ApplyAsync(
                    It.IsAny<CommittedSnapshot>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        /// <summary>
        /// Changes physical residence or tick duration without changing the public scenario label.
        /// </summary>
        private static RecyclingConfiguration DryingConfiguration(int residenceTicks, int tickSeconds)
        {
            var configuration = new RecyclingConfiguration { TickSeconds = tickSeconds };
            StageRecipe[] stages = configuration.Stages.Span.ToArray();
            stages[14] = stages[14] with { ResidenceTicks = residenceTicks };
            return configuration with { Stages = stages.ToArrayOf() };
        }

        /// <summary>
        /// Uses real coordinator commits and identities through the existing authenticated-client seam.
        /// </summary>
        private static Mock<IRecyclingMembershipClient> MembershipClient(
            RecyclingCoordinator coordinator,
            RecyclingPrincipal principal,
            bool omitConfigurationId)
        {
            var client = new Mock<IRecyclingMembershipClient>(MockBehavior.Strict);
            client.SetupGet(static value => value.ConnectionId).Returns(principal.ConnectionId);
            client.SetupGet(static value => value.IsConnected).Returns(true);
            client.SetupGet(static value => value.CertificateSha256).Returns(string.Empty);
            client.Setup(value => value.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<CommittedSnapshot>(omitConfigurationId
                    ? coordinator.Current with { ConfigurationId = string.Empty }
                    : coordinator.Current));
            client.Setup(value => value.ExecuteAsync(
                    It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
                .Returns((RecyclingCommand command, CancellationToken ct) =>
                    coordinator.ExecuteAsync(principal, command, ct));
            return client;
        }

        /// <summary>
        /// Controls wall time and timer wakes without sleeps, leaving periodic coalescing to PeriodicTimer.
        /// </summary>
        private static TimeProvider ControlledClock(out Action<TimeSpan> advance, out Task timerCreated)
        {
            long ticks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).Ticks;
            var callbacks = new ConcurrentBag<Action>();
            var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = new Mock<TimeProvider> { CallBase = true };
            clock.Setup(value => value.GetUtcNow()).Returns(() =>
                new DateTimeOffset(Interlocked.Read(ref ticks), TimeSpan.Zero));
            clock.Setup(value => value.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan _, TimeSpan _) =>
                {
                    int disposed = 0;
                    var timer = new Mock<ITimer>();
                    timer.Setup(value => value.Dispose()).Callback(() => Interlocked.Exchange(ref disposed, 1));
                    callbacks.Add(() =>
                    {
                        if (Volatile.Read(ref disposed) == 0)
                        {
                            callback(state);
                        }
                    });
                    created.TrySetResult();
                    return timer.Object;
                });
            advance = duration =>
            {
                Interlocked.Add(ref ticks, duration.Ticks);
                foreach (Action callback in callbacks)
                {
                    callback();
                }
            };
            timerCreated = created.Task;
            return clock.Object;
        }
    }
}
