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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests
{
    /// <summary>
    /// Exercises runner deadlines, overlap, readiness and subscription lifetimes with explicit clock barriers.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryBridgeRunnerBoundaryTests
    {
        /// <summary>
        /// Invalid modes and unbounded timer inputs are rejected before an upstream is contacted.
        /// </summary>
        [TestCase("mode-low")]
        [TestCase("mode-high")]
        [TestCase("synchronizer")]
        [TestCase("poll-zero")]
        [TestCase("request-infinite")]
        [TestCase("shutdown-overflow")]
        [TestCase("minimum-negative")]
        [TestCase("minimum-overflow")]
        public async Task InvalidRuntimeConfigurationNeverInspectsAnUpstreamAsync(string invalid)
        {
            await using var fixture = new XRegistrySyncFixture();
            XRegistryBridgeRunOptions options = invalid switch
            {
                "mode-low" => Options() with { Mode = (XRegistryBridgeMode)(-1) },
                "mode-high" => Options() with { Mode = (XRegistryBridgeMode)3 },
                "synchronizer" => Options() with { Mode = XRegistryBridgeMode.Synchronization },
                "poll-zero" => Options() with { PollInterval = TimeSpan.Zero },
                "request-infinite" => Options() with { RequestTimeout = Timeout.InfiniteTimeSpan },
                "shutdown-overflow" => Options() with { ShutdownTimeout = TimeSpan.FromMilliseconds(uint.MaxValue) },
                "minimum-negative" => Options() with { MinimumPassInterval = TimeSpan.FromTicks(-1) },
                _ => Options() with { MinimumPassInterval = TimeSpan.FromMilliseconds(uint.MaxValue) }
            };

            Assert.Throws<ArgumentException>(() => new XRegistryBridgeRunner(options,
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer)], fixture.Telemetry));

            Assert.That(fixture.Native.Inspections, Is.Zero);
        }

        /// <summary>
        /// Upstream names are nonblank and distinct even when their endpoint and caller are identical.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("native")]
        public async Task InvalidUpstreamNamesAreRejectedBeforeDispatchAsync(string secondName)
        {
            await using var fixture = new XRegistrySyncFixture();

            Assert.Throws<ArgumentException>(() => new XRegistryBridgeRunner(Options(),
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer),
                    new XRegistryBridgeUpstream(secondName, fixture.Native, Writer)], fixture.Telemetry));

            Assert.That(fixture.Native.Inspections, Is.Zero);
        }

        /// <summary>
        /// A rejected overlapping pass cannot publish status or release the first pass's ownership.
        /// </summary>
        [Test]
        public async Task OverlappingPassesAreRejectedUntilTheOriginalPassCompletesAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            XRegistryEndpointDescription description = await fixture.Native.InspectAsync(Writer).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<XRegistryEndpointDescription>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Native.InspectOverrideAsync = _ =>
            {
                entered.TrySetResult(true);
                return new ValueTask<XRegistryEndpointDescription>(release.Task);
            };
            var runner = new XRegistryBridgeRunner(Options(),
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer)], fixture.Telemetry,
                timeProvider: fixture.Clock);
            Task<XRegistryBridgeStatus> first = runner.RunOnceAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunOnceAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunOnceAsync().ConfigureAwait(false));
                Assert.Multiple(() =>
                {
                    Assert.That(first.IsCompleted, Is.False);
                    Assert.That(runner.Status.LastAttempt, Is.Null);
                    Assert.That(runner.Status.Ready, Is.False);
                });
            }
            finally
            {
                release.TrySetResult(description);
                await first.WaitAsync(s_wait).ConfigureAwait(false);
            }
            fixture.Native.InspectOverrideAsync = null;

            XRegistryBridgeStatus next = await runner.RunOnceAsync().ConfigureAwait(false);
            Assert.That(next.Ready, Is.True);
        }

        /// <summary>
        /// Both health and projection deadlines retain the last success and release the pass for later recovery.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InjectedRequestAndProjectionTimeoutsRetainSuccessAndAllowRecoveryAsync(bool projectionTimeout)
        {
            await using var fixture = new XRegistrySyncFixture();
            var clock = new BoundaryClock();
            DateTimeOffset initial = clock.GetUtcNow();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var inspection = new TaskCompletionSource<XRegistryEndpointDescription>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool block = false;
            XRegistryEndpointDescription description = WritableDescription();
            var backend = new Mock<IXRegistryPreparedEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.InspectAsync(Writer, It.IsAny<CancellationToken>())).Returns(() =>
            {
                if (block && !projectionTimeout)
                {
                    entered.TrySetResult(true);
                    return new ValueTask<XRegistryEndpointDescription>(inspection.Task);
                }
                return new ValueTask<XRegistryEndpointDescription>(description);
            });
            var projection = new Mock<IXRegistryBridgeProjection>(MockBehavior.Strict);
            projection.SetupGet(value => value.IsDegraded).Returns(false);
            projection.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                if (block && projectionTimeout)
                {
                    entered.TrySetResult(true);
                    return new ValueTask(refresh.Task);
                }
                return default;
            });
            var runner = new XRegistryBridgeRunner(Options(),
                [new XRegistryBridgeUpstream("native", backend.Object, Writer)], fixture.Telemetry,
                projection: projection.Object, timeProvider: clock);
            XRegistryBridgeStatus successful = await runner.RunOnceAsync().ConfigureAwait(false);
            Assert.That(successful.LastSuccess, Is.EqualTo(initial));
            clock.Advance(TimeSpan.FromSeconds(1));
            block = true;
            Task<XRegistryBridgeStatus> pending = runner.RunOnceAsync().AsTask();
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(7));
                XRegistryBridgeStatus failed = await pending.WaitAsync(s_wait).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(failed.Ready, Is.False);
                    Assert.That(failed.CanWrite, Is.False);
                    Assert.That(failed.LastSuccess, Is.EqualTo(initial));
                    Assert.That(failed.LastAttempt, Is.EqualTo(initial.AddSeconds(8)));
                    Assert.That(failed.Failure, Is.EqualTo("An upstream health or repair operation failed."));
                    Assert.That(runner.Status.Lag, Is.EqualTo(TimeSpan.FromSeconds(8)));
                });
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunOnceAsync().ConfigureAwait(false));
                Assert.That(runner.WaitForPendingOperationsAsync().AsTask().IsCompleted, Is.False);
            }
            finally
            {
                block = false;
                inspection.TrySetResult(description);
                refresh.TrySetResult(true);
                await pending.WaitAsync(s_wait).ConfigureAwait(false);
                await runner.WaitForPendingOperationsAsync().AsTask().WaitAsync(s_wait).ConfigureAwait(false);
            }

            XRegistryBridgeStatus recovered = await runner.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Ready, Is.True);
                Assert.That(recovered.CanWrite, Is.True);
                Assert.That(recovered.Failure, Is.Null);
                Assert.That(recovered.LastSuccess, Is.EqualTo(initial.AddSeconds(8)));
            });
        }

        [TestCase(XRegistryBridgeMode.HttpGateway)]
        [TestCase(XRegistryBridgeMode.OpcUaGateway)]
        public async Task GatewayModesNeverResolveOrExecuteAnUnselectedSynchronizationJobAsync(XRegistryBridgeMode mode)
        {
            await using var fixture = new XRegistrySyncFixture();
            var services = new ServiceCollection();
            services.AddSingleton(fixture.Telemetry);
            services.AddSingleton<XRegistrySynchronizer>(_ =>
                throw new AssertionException("A gateway must not resolve a synchronization job."));
            XRegistryBridgeRunOptions options = Options() with { Mode = mode };
            ArrayOf<XRegistryBridgeUpstream> upstreams = [new("native", fixture.Native, Writer)];
            services.AddXRegistryBridgeRunner(options, upstreams);
            using ServiceProvider container = services.BuildServiceProvider();
            XRegistryBridgeStatus status = await container.GetRequiredService<XRegistryBridgeRunner>()
                .RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(status.Synchronization, Is.Null);
                Assert.That(fixture.Native.Mutations, Is.Empty);
                Assert.That(fixture.Http.Mutations, Is.Empty);
                Assert.Throws<ArgumentException>(() =>
                    new XRegistryBridgeRunner(options, upstreams, fixture.Telemetry, fixture.Engine()));
            });
        }

        [Test]
        public async Task BoundedShutdownRetainsOutstandingProjectionAndExposesItsActualCompletionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var clock = new BoundaryClock();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var projection = new Mock<IXRegistryBridgeProjection>();
            projection.SetupGet(value => value.IsDegraded).Returns(false);
            projection.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.TrySetResult(true);
                return new ValueTask(release.Task);
            });
            var runner = new XRegistryBridgeRunner(Options() with { Once = true },
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer)], fixture.Telemetry,
                projection: projection.Object, timeProvider: clock);
            Task<int> run = runner.RunAsync();
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(7));
                await clock.WaitForTimersAsync(TimeSpan.FromSeconds(2), 1).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(2));
                Assert.That(await run.WaitAsync(s_wait).ConfigureAwait(false), Is.EqualTo(1));
                Assert.That(runner.Status.Stopped, Is.True);
                Assert.That(runner.WaitForPendingOperationsAsync().AsTask().IsCompleted, Is.False);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunOnceAsync().ConfigureAwait(false));
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunAsync().ConfigureAwait(false));
                projection.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                release.TrySetResult(true);
                await run.WaitAsync(s_wait).ConfigureAwait(false);
                await runner.WaitForPendingOperationsAsync().AsTask().WaitAsync(s_wait).ConfigureAwait(false);
            }
            Assert.That(await runner.RunAsync().ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Degradation does not overwrite the last verified success and reservation diagnostics remain live.
        /// </summary>
        [Test]
        public async Task DegradedProjectionRetainsSuccessAndReportsLiveResourceUsageAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            DateTimeOffset initial = fixture.Clock.GetUtcNow();
            bool degraded = false;
            var usage = new XRegistryBridgeResourceUsage(2, 16, 32);
            var projection = new Mock<IXRegistryBridgeProjection>(MockBehavior.Strict);
            projection.SetupGet(value => value.IsDegraded).Returns(() => degraded);
            projection.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            projection.As<IXRegistryBridgeResourceUsage>().SetupGet(value => value.ResourceUsage).Returns(() => usage);
            var runner = new XRegistryBridgeRunner(Options() with { Mode = XRegistryBridgeMode.OpcUaGateway },
                [new XRegistryBridgeUpstream("http", fixture.Http, Writer)], fixture.Telemetry,
                projection: projection.Object, timeProvider: fixture.Clock);

            XRegistryBridgeStatus successful = await runner.RunOnceAsync().ConfigureAwait(false);
            degraded = true;
            fixture.Clock.Advance(TimeSpan.FromSeconds(3));
            XRegistryBridgeStatus failed = await runner.RunOnceAsync().ConfigureAwait(false);
            usage = new XRegistryBridgeResourceUsage(1, 0, 8);
            XRegistryBridgeStatus observed = runner.Status;

            Assert.Multiple(() =>
            {
                Assert.That(successful.Ready, Is.True);
                Assert.That(failed.Ready, Is.False);
                Assert.That(failed.CanWrite, Is.True);
                Assert.That(failed.LastSuccess, Is.EqualTo(initial));
                Assert.That(failed.LastAttempt, Is.EqualTo(initial.AddSeconds(3)));
                Assert.That(failed.Failure, Is.EqualTo("Projection or reconciliation requires attention."));
                Assert.That(observed.ResourceUsage, Is.EqualTo(new XRegistryBridgeResourceUsage(1, 0, 8)));
                Assert.That(observed.Lag, Is.EqualTo(TimeSpan.FromSeconds(3)));
            });
            projection.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// An active continuous runner rejects another lifecycle and still shuts down through caller cancellation.
        /// </summary>
        [Test]
        public async Task OverlappingContinuousRunsAreRejectedWithoutInterruptingTheOwnerAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            using var stopped = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runner = new XRegistryBridgeRunner(Options(),
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer)], fixture.Telemetry,
                timeProvider: fixture.Clock);
            Task<int> run = runner.RunAsync(async (_, ct) =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }, stopped.Token);
            try
            {
                await entered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await runner.RunAsync().ConfigureAwait(false));
                Assert.That(run.IsCompleted, Is.False);
                await CancelAsync(stopped).ConfigureAwait(false);
                Assert.That(await run.WaitAsync(s_wait).ConfigureAwait(false), Is.Zero);
                Assert.Multiple(() =>
                {
                    Assert.That(runner.Status.Stopped, Is.True);
                    Assert.That(runner.Status.Ready, Is.False);
                });
            }
            finally
            {
                release.TrySetResult(true);
                await CancelAsync(stopped).ConfigureAwait(false);
                await run.WaitAsync(s_wait).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A failed one-pass gateway reports failure before publishing its final stopped status.
        /// </summary>
        [Test]
        public async Task UnreadyOnePassReportsNonzeroExitAndStopsAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            fixture.Native.FailInspection = true;
            var runner = new XRegistryBridgeRunner(Options() with { Once = true },
                [new XRegistryBridgeUpstream("native", fixture.Native, Writer)], fixture.Telemetry,
                timeProvider: fixture.Clock);
            XRegistryBridgeStatus? observed = null;
            int observations = 0;

            int exitCode = await runner.RunAsync((status, _) =>
            {
                observations++;
                observed = status;
                return default;
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.EqualTo(1));
                Assert.That(observations, Is.EqualTo(1));
                Assert.That(observed?.Ready, Is.False);
                Assert.That(observed?.Stopped, Is.False);
                Assert.That(runner.Status.Stopped, Is.True);
                Assert.That(runner.Status.LastSuccess, Is.Null);
            });
        }

        /// <summary>
        /// Failed, unsupported and completed streams retain full repair; hints obey the bounded minimum interval.
        /// </summary>
        [TestCase("unsupported", 3, 11, 1)]
        [TestCase("failure", 3, 3, 1)]
        [TestCase("complete", 3, 3, 1)]
        [TestCase("hint", 3, 3, 2)]
        [TestCase("hint", 17, 11, 3)]
        public async Task FeedBoundariesRetainFullRepairsAtTheExpectedInjectedCadenceAsync(
            string behavior, int minimumSeconds, int advanceSeconds, int timerCount)
        {
            await using var fixture = new XRegistrySyncFixture();
            var clock = new BoundaryClock();
            var feed = new BoundaryFeed(fixture.Native, behavior);
            var runner = new XRegistryBridgeRunner(
                Options() with { MinimumPassInterval = TimeSpan.FromSeconds(minimumSeconds) },
                [new XRegistryBridgeUpstream("native", feed, Writer)], fixture.Telemetry, timeProvider: clock);
            using var stopped = new CancellationTokenSource();
            int passes = 0;
            Task<int> run = runner.RunAsync(async (_, _) =>
            {
                if (Interlocked.Increment(ref passes) == 2)
                {
                    await CancelAsync(stopped).ConfigureAwait(false);
                }
            }, stopped.Token);
            try
            {
                await feed.Watching.Task.WaitAsync(s_wait).ConfigureAwait(false);
                await clock.WaitForTimersAsync(TimeSpan.FromSeconds(advanceSeconds), timerCount).ConfigureAwait(false);
                Assert.That(passes, Is.EqualTo(1));

                clock.Advance(TimeSpan.FromSeconds(advanceSeconds));

                Assert.That(await run.WaitAsync(s_wait).ConfigureAwait(false), Is.Zero);
                Assert.Multiple(() =>
                {
                    Assert.That(passes, Is.EqualTo(2));
                    Assert.That(fixture.Native.Inspections, Is.EqualTo(2));
                    Assert.That(feed.CleanupCompleted.Task.IsCompleted, Is.True);
                    Assert.That(runner.Status.Stopped, Is.True);
                });
            }
            finally
            {
                await CancelAsync(stopped).ConfigureAwait(false);
                await run.WaitAsync(s_wait).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A late subscription disposal cannot hold shutdown past its deadline, even if cleanup subsequently fails.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ShutdownDeadlineRetainsAndObservesLateSubscriptionCleanupAsync(bool failCleanup)
        {
            await using var fixture = new XRegistrySyncFixture();
            var clock = new BoundaryClock();
            var feed = new BoundaryFeed(fixture.Native, "cleanup") { FailCleanup = failCleanup };
            var runner = new XRegistryBridgeRunner(Options(),
                [new XRegistryBridgeUpstream("native", feed, Writer)], fixture.Telemetry, timeProvider: clock);
            using var stopped = new CancellationTokenSource();
            Task<int> run = runner.RunAsync(cancellationToken: stopped.Token);
            try
            {
                await feed.Watching.Task.WaitAsync(s_wait).ConfigureAwait(false);
                await clock.WaitForTimersAsync(TimeSpan.FromSeconds(11), 1).ConfigureAwait(false);
                await CancelAsync(stopped).ConfigureAwait(false);
                await feed.CleanupEntered.Task.WaitAsync(s_wait).ConfigureAwait(false);
                await clock.WaitForTimersAsync(TimeSpan.FromSeconds(2), 1).ConfigureAwait(false);

                clock.Advance(TimeSpan.FromSeconds(2));

                Assert.That(await run.WaitAsync(s_wait).ConfigureAwait(false), Is.Zero);
                Assert.Multiple(() =>
                {
                    Assert.That(runner.Status.Stopped, Is.True);
                    Assert.That(runner.Status.Ready, Is.False);
                    Assert.That(feed.CleanupCompleted.Task.IsCompleted, Is.False);
                });
                feed.ReleaseCleanup.TrySetResult(true);
                await feed.CleanupCompleted.Task.WaitAsync(s_wait).ConfigureAwait(false);
            }
            finally
            {
                feed.ReleaseCleanup.TrySetResult(true);
                await CancelAsync(stopped).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(2));
                await run.WaitAsync(s_wait).ConfigureAwait(false);
                await feed.CleanupCompleted.Task.WaitAsync(s_wait).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The service registration retains explicit options and resolves the registered projection and clock.
        /// </summary>
        [Test]
        public async Task DependencyInjectionResolvesOneRunnerWithTheRegisteredClockAndProjectionAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var backend = new Mock<IXRegistryPreparedEndpoint>(MockBehavior.Strict);
            backend.Setup(value => value.InspectAsync(Writer, It.IsAny<CancellationToken>()))
                .ReturnsAsync(WritableDescription());
            var projection = new Mock<IXRegistryBridgeProjection>(MockBehavior.Strict);
            projection.SetupGet(value => value.IsDegraded).Returns(false);
            projection.Setup(value => value.RefreshAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var services = new ServiceCollection();
            services.AddSingleton(fixture.Telemetry);
            services.AddSingleton<TimeProvider>(fixture.Clock);
            services.AddSingleton(projection.Object);
            XRegistryBridgeRunOptions options = Options();
            Assert.That(services.AddXRegistryBridgeRunner(options,
                [new XRegistryBridgeUpstream("native", backend.Object, Writer)]), Is.SameAs(services));
            using ServiceProvider container = services.BuildServiceProvider();
            XRegistryBridgeRunner runner = container.GetRequiredService<XRegistryBridgeRunner>();

            XRegistryBridgeStatus status = await runner.RunOnceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(container.GetRequiredService<XRegistryBridgeRunner>(), Is.SameAs(runner));
                Assert.That(container.GetRequiredService<XRegistryBridgeRunOptions>(), Is.SameAs(options));
                Assert.That(status.Ready, Is.True);
                Assert.That(status.CanWrite, Is.True);
                Assert.That(status.LastSuccess, Is.EqualTo(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)));
            });
            projection.Verify(value => value.RefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            backend.Verify(value => value.InspectAsync(Writer, It.IsAny<CancellationToken>()), Times.Once);
        }

        private static XRegistryBridgeRunOptions Options()
        {
            return new XRegistryBridgeRunOptions
            {
                PollInterval = TimeSpan.FromSeconds(11),
                RequestTimeout = TimeSpan.FromSeconds(7),
                ShutdownTimeout = TimeSpan.FromSeconds(2),
                MinimumPassInterval = TimeSpan.Zero
            };
        }

        private static XRegistryEndpointDescription WritableDescription()
        {
            return new XRegistryEndpointDescription("native")
            {
                SupportsAtomicMutations = true,
                SupportsConditionalMutations = true,
                SupportsPreparedMutations = true,
                SupportsWriteTouch = true
            };
        }

        private static Task CancelAsync(CancellationTokenSource source)
        {
#if NET8_0_OR_GREATER
            return source.CancelAsync();
#else
            return Task.Run(source.Cancel);
#endif
        }

        private sealed class BoundaryClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow()
            {
                lock (m_gate)
                {
                    return m_now;
                }
            }

            public void Advance(TimeSpan duration)
            {
                BoundaryTimer[] timers;
                DateTimeOffset now;
                lock (m_gate)
                {
                    m_now += duration;
                    now = m_now;
                    timers = [.. m_activeTimers];
                }
                foreach (BoundaryTimer timer in timers)
                {
                    timer.Fire(now);
                }
            }

            public async Task WaitForTimersAsync(TimeSpan duration, int count)
            {
                using var timeout = new CancellationTokenSource(s_wait);
                while (count != 0)
                {
                    TimeSpan due = await m_timers.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
                    if (due == duration)
                    {
                        count--;
                    }
                }
            }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new BoundaryTimer(this, callback, state);
                lock (m_gate)
                {
                    m_activeTimers.Add(timer);
                    timer.Change(dueTime, period);
                }
                m_timers.Writer.TryWrite(dueTime);
                return timer;
            }

            private sealed class BoundaryTimer(BoundaryClock owner, TimerCallback callback, object? state) : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    lock (owner.m_gate)
                    {
                        if (m_disposed)
                        {
                            return false;
                        }
                        m_due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.m_now + dueTime;
                        m_period = period;
                        return true;
                    }
                }

                public void Fire(DateTimeOffset now)
                {
                    lock (owner.m_gate)
                    {
                        if (m_disposed || now < m_due)
                        {
                            return;
                        }
                        m_due = m_period > TimeSpan.Zero ? now + m_period : DateTimeOffset.MaxValue;
                        callback(state);
                    }
                }

                public void Dispose()
                {
                    lock (owner.m_gate)
                    {
                        m_disposed = true;
                        owner.m_activeTimers.Remove(this);
                    }
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                private DateTimeOffset m_due;
                private TimeSpan m_period;
                private bool m_disposed;
            }

            private DateTimeOffset m_now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
            private readonly Lock m_gate = new();
            private readonly List<BoundaryTimer> m_activeTimers = [];
            private readonly Channel<TimeSpan> m_timers = Channel.CreateUnbounded<TimeSpan>();
        }

        private sealed class BoundaryFeed(IXRegistryEndpoint endpoint, string behavior)
            : IXRegistryEndpoint, IXRegistryChangeFeed
        {
            public bool FailCleanup { get; init; }

            public TaskCompletionSource<bool> Watching { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> CleanupEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseCleanup { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> CleanupCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<XRegistryEndpointDescription> InspectAsync(
                XRegistryCallContext context, CancellationToken cancellationToken = default)
            {
                return endpoint.InspectAsync(context, cancellationToken);
            }

            public ValueTask<XRegistryResponse> ExecuteAsync(
                XRegistryRequest request, CancellationToken cancellationToken = default)
            {
                return endpoint.ExecuteAsync(request, cancellationToken);
            }

            public async IAsyncEnumerable<XRegistryChangeHint> WatchAsync(
                XRegistryCallContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                try
                {
                    Watching.TrySetResult(true);
                    if (behavior == "unsupported")
                    {
                        throw new NotSupportedException("The upstream has no feed.");
                    }
                    if (behavior == "failure")
                    {
                        throw new IOException("The upstream stream failed.");
                    }
                    if (behavior == "complete")
                    {
                        yield break;
                    }
                    if (behavior == "hint")
                    {
                        yield return new XRegistryChangeHint(Group, "coalesced");
                    }
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    await CompleteCleanupAsync().ConfigureAwait(false);
                }
            }

            private async ValueTask CompleteCleanupAsync()
            {
                if (behavior == "cleanup")
                {
                    CleanupEntered.TrySetResult(true);
                    await ReleaseCleanup.Task.ConfigureAwait(false);
                }
                CleanupCompleted.TrySetResult(true);
                if (FailCleanup)
                {
                    throw new IOException("Late subscription cleanup failed.");
                }
            }
        }

        private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);
    }
}
