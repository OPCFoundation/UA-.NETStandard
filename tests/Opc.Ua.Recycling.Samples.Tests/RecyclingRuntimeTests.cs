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
            CommittedSnapshot snapshot = new RecyclingEngine(new RecyclingConfiguration()).CreateRun("timeout") with
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
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
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
            var engine = new RecyclingEngine(new RecyclingConfiguration { PlannedSourceBatches = 1 });
            CommittedSnapshot snapshot = engine.CreateRun("projection-run") with
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
            using var telemetry = (DefaultTelemetry)DefaultTelemetry.Create(
                static logging => logging.ClearProviders());
            using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            await using var runtime = new RecyclingRuntime(
                new RecyclingHostOptions { Role = RecyclingHostRole.ProcessView },
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
