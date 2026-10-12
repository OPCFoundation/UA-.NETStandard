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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Tests.Sync;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests
{
    [TestFixture]
    public sealed class XRegistryBridgeRunnerTests
    {
        [Test]
        public async Task FailedHealthDoesNotEraseTheLastVerifiedSuccessAsync()
        {
            await using var fixture = new XRegistrySyncFixture();
            var runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions(),
                [new XRegistryBridgeUpstream("native", fixture.Native, XRegistrySyncFixture.Writer)],
                fixture.Telemetry, timeProvider: fixture.Clock);
            XRegistryBridgeStatus first = await runner.RunOnceAsync().ConfigureAwait(false);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            fixture.Native.FailInspection = true;
            XRegistryBridgeStatus failed = await runner.RunOnceAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.Ready, Is.True);
                Assert.That(first.CanWrite, Is.False, "A non-prepared endpoint cannot back a writable HTTP gateway.");
                Assert.That(failed.Ready, Is.False);
                Assert.That(failed.CanWrite, Is.False);
                Assert.That(failed.LastSuccess, Is.EqualTo(first.LastSuccess));
                Assert.That(first.LastAttempt, Is.Not.Null);
                Assert.That(failed.LastAttempt, Is.Not.Null);
                Assert.That(
                    failed.LastAttempt.GetValueOrDefault(), Is.GreaterThan(first.LastAttempt.GetValueOrDefault()));
                Assert.That(failed.Failure, Is.Not.Empty);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OnceAndDryRunShareTheSameReconciliationLifecycleAsync(bool dryRun)
        {
            await using var fixture = new XRegistrySyncFixture();
            await fixture.Native.ChangeAsync(XRegistrySyncFixture.Group, "{}").ConfigureAwait(false);
            var runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions
            {
                Mode = XRegistryBridgeMode.Synchronization,
                Once = true,
                DryRun = dryRun
            },
                [new XRegistryBridgeUpstream("native", fixture.Native, XRegistrySyncFixture.Writer),
                    new XRegistryBridgeUpstream("http", fixture.Http, XRegistrySyncFixture.Writer)],
                fixture.Telemetry, fixture.Engine(), timeProvider: fixture.Clock);
            int exitCode = await runner.RunAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Zero);
                Assert.That(runner.Status.Stopped, Is.True);
                Assert.That(runner.Status.Synchronization?.DryRun, Is.EqualTo(dryRun));
                Assert.That(fixture.Http.Mutations, Has.Count.EqualTo(dryRun ? 0 : 1));
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PollsAndCoalescedHintsBothTriggerFullPassesAndReleaseTheirWatchAsync(bool useHint)
        {
            await using var fixture = new XRegistrySyncFixture();
            var clock = new ObservableClock();
            var feed = new HintEndpoint(fixture.Native);
            var runner = new XRegistryBridgeRunner(new XRegistryBridgeRunOptions
            {
                PollInterval = TimeSpan.FromSeconds(2),
                MinimumPassInterval = TimeSpan.Zero
            }, [new XRegistryBridgeUpstream("native", useHint ? feed : fixture.Native, XRegistrySyncFixture.Writer)],
                fixture.Telemetry, timeProvider: clock);
            using var stopped = new CancellationTokenSource();
            int passes = 0;
            Task<int> run = runner.RunAsync(async (_, _) =>
            {
                if (Interlocked.Increment(ref passes) == 2)
                {
                    await stopped.CancelAsync().ConfigureAwait(false);
                }
            }, stopped.Token);
            try
            {
                await clock.PollScheduled.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (useHint)
                {
                    await feed.Watching.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    feed.Signal();
                }
                else
                {
                    clock.Advance(TimeSpan.FromSeconds(2));
                }
                Assert.That(await run.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false), Is.Zero);
                Assert.That(passes, Is.EqualTo(2));
                Assert.That(fixture.Native.Inspections, Is.EqualTo(2));
                if (useHint)
                {
                    Assert.That(feed.Released.Task.IsCompleted, Is.True);
                }
            }
            finally
            {
#if NET8_0_OR_GREATER
                await stopped.CancelAsync().ConfigureAwait(false);
#else
                await Task.Run(stopped.Cancel).ConfigureAwait(false);
#endif
                await run.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        private sealed class ObservableClock : TimeProvider
        {
            public TaskCompletionSource<bool> PollScheduled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override DateTimeOffset GetUtcNow()
            {
                return m_clock.GetUtcNow();
            }

            public void Advance(TimeSpan duration)
            {
                m_clock.Advance(duration);
            }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer = m_clock.CreateTimer(callback, state, dueTime, period);
                if (dueTime == TimeSpan.FromSeconds(2))
                {
                    PollScheduled.TrySetResult(true);
                }
                return timer;
            }

            private readonly SyncClock m_clock = new();
        }

        private sealed class HintEndpoint(IXRegistryEndpoint endpoint) : IXRegistryEndpoint, IXRegistryChangeFeed
        {
            public TaskCompletionSource<bool> Watching { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Released { get; } =
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
                Watching.TrySetResult(true);
                try
                {
                    await foreach (bool value in m_channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        _ = value;
                        yield return new XRegistryChangeHint(null, "coalesced");
                    }
                }
                finally
                {
                    Released.TrySetResult(true);
                }
            }

            public void Signal()
            {
                m_channel.Writer.TryWrite(true);
            }

            private readonly Channel<bool> m_channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
        }
    }
}
