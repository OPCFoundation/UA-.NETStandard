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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Scheduling
{
    /// <summary>
    /// Covers the argument-validation paths of
    /// <see cref="PubSubScheduler.ScheduleAsync"/>, the timer-fire and
    /// back-pressure paths inside the private <c>ScheduledTimer</c>, and
    /// the <c>DisposeAsync</c> lifecycle.
    /// </summary>
    /// <remarks>
    /// Tests use <see cref="FakeTimeProvider"/> so every timer advance is
    /// deterministic and no real wall-clock delay is needed.
    /// </remarks>
    [TestFixture]
    [TestSpec("6.4.1", Summary = "PubSubScheduler periodic callback and back-pressure")]
    public class PubSubSchedulerTests
    {
        private static readonly PubSubSchedule s_period100ms = new(
            period: TimeSpan.FromMilliseconds(100),
            keepAliveTime: TimeSpan.Zero,
            publishingOffset: TimeSpan.Zero,
            receiveOffset: TimeSpan.Zero);

        // ── Constructor ──────────────────────────────────────────────────────

        [Test]
        public void Constructor_NullTelemetryAndTimeProvider_DoesNotThrow()
        {
            Assert.That(
                () => new PubSubScheduler(telemetry: null, timeProvider: null),
                Throws.Nothing);
        }

        // ── ScheduleAsync – argument validation ──────────────────────────────

        [Test]
        public async Task ScheduleAsync_NullAction_ThrowsArgumentNullExceptionAsync()
        {
            var scheduler = new PubSubScheduler();
            Assert.That(
                async () => await scheduler.ScheduleAsync(
                    s_period100ms,
                    null!).ConfigureAwait(false),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("action"));

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ScheduleAsync_ZeroPeriod_ThrowsArgumentExceptionAsync()
        {
            var scheduler = new PubSubScheduler();
            var zeroPeriod = new PubSubSchedule(
                period: TimeSpan.Zero,
                keepAliveTime: TimeSpan.Zero,
                publishingOffset: TimeSpan.Zero,
                receiveOffset: TimeSpan.Zero);

            Assert.That(
                async () => await scheduler.ScheduleAsync(
                    zeroPeriod,
                    _ => default).ConfigureAwait(false),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("schedule"));

            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ScheduleAsync_NegativePeriod_ThrowsArgumentExceptionAsync()
        {
            var scheduler = new PubSubScheduler();
            var negativePeriod = new PubSubSchedule(
                period: TimeSpan.FromMilliseconds(-1),
                keepAliveTime: TimeSpan.Zero,
                publishingOffset: TimeSpan.Zero,
                receiveOffset: TimeSpan.Zero);

            Assert.That(
                async () => await scheduler.ScheduleAsync(
                    negativePeriod,
                    _ => default).ConfigureAwait(false),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("schedule"));

            await Task.CompletedTask.ConfigureAwait(false);
        }

        // ── Timer fires the action ────────────────────────────────────────────

        [Test]
        public async Task ScheduleAsync_TimerFires_InvokesActionOnceAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            int callCount = 0;

            await using var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return default;
                }).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100));

            Assert.That(callCount, Is.EqualTo(1),
                "Action must be invoked once when the period elapses.");
        }

        [Test]
        public async Task ScheduleAsync_TimerFiresTwice_InvokesActionTwiceAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            int callCount = 0;

            await using var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return default;
                }).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100)); // first tick
            clock.Advance(TimeSpan.FromMilliseconds(100)); // second tick

            Assert.That(callCount, Is.EqualTo(2));
        }

        [Test]
        public async Task ScheduleAsync_PublishingOffset_FirstFiresAtOffsetNotPeriodAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            int callCount = 0;

            // PublishingOffset = 50 ms < Period = 200 ms
            var scheduleWithOffset = new PubSubSchedule(
                period: TimeSpan.FromMilliseconds(200),
                keepAliveTime: TimeSpan.Zero,
                publishingOffset: TimeSpan.FromMilliseconds(50),
                receiveOffset: TimeSpan.Zero);

            await using var handle = await scheduler.ScheduleAsync(
                scheduleWithOffset,
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return default;
                }).ConfigureAwait(false);

            // Advance by the PublishingOffset only — must fire before the Period.
            clock.Advance(TimeSpan.FromMilliseconds(50));

            Assert.That(callCount, Is.EqualTo(1),
                "Action must fire at PublishingOffset (50 ms), not at Period (200 ms).");
        }

        // ── Back-pressure ────────────────────────────────────────────────────

        [Test]
        public async Task ScheduleAsync_BackPressure_SkipsTickWhileActionRunningAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            int callCount = 0;
            var gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                async ct =>
                {
                    Interlocked.Increment(ref callCount);
                    await gate.Task.ConfigureAwait(false);
                }).ConfigureAwait(false);

            try
            {
                clock.Advance(TimeSpan.FromMilliseconds(100)); // first tick: action starts and blocks
                clock.Advance(TimeSpan.FromMilliseconds(100)); // second tick: must be skipped

                Assert.That(callCount, Is.EqualTo(1),
                    "Second tick must be skipped while the first action is still running.");
            }
            finally
            {
                gate.SetResult(true);
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ── Action throws ────────────────────────────────────────────────────

        [Test]
        public async Task ScheduleAsync_ActionThrowsNonOce_ExceptionSwallowedAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            bool actionRan = false;

            await using var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                ct =>
                {
                    actionRan = true;
                    throw new InvalidOperationException("deliberate test exception");
                }).ConfigureAwait(false);

            // The exception must NOT propagate — it is logged and swallowed internally.
            Assert.That(
                () => clock.Advance(TimeSpan.FromMilliseconds(100)),
                Throws.Nothing);

            Assert.That(actionRan, Is.True,
                "Action must have run even though it then threw.");
        }

        // ── DisposeAsync ─────────────────────────────────────────────────────

        [Test]
        public async Task DisposeAsync_StopsSubsequentTicksAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            int callCount = 0;

            var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                ct =>
                {
                    Interlocked.Increment(ref callCount);
                    return default;
                }).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100)); // one tick before dispose
            Assert.That(callCount, Is.EqualTo(1));

            await handle.DisposeAsync().ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100)); // must not tick after dispose
            Assert.That(callCount, Is.EqualTo(1),
                "No further ticks must occur after DisposeAsync.");
        }

        [Test]
        public async Task DisposeAsync_WithRunningAction_DrainsBeforeReturningAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);
            bool actionCompleted = false;

            var actionStarted = new SemaphoreSlim(0, 1);
            var gate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                async ct =>
                {
                    actionStarted.Release();
                    // Wait until the CTS is cancelled (by DisposeAsync) or gate is set.
                    await gate.Task.ConfigureAwait(false);
                    actionCompleted = true;
                }).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100)); // starts the action

            // Wait until the action has started before kicking off dispose.
            await actionStarted.WaitAsync().ConfigureAwait(false);

            // Begin dispose (cancels CTS, awaits the running task).
            var disposeTask = handle.DisposeAsync().AsTask();

            // Unblock the action so dispose can drain.
            gate.SetResult(true);

            await disposeTask.ConfigureAwait(false);

            Assert.That(actionCompleted, Is.True,
                "DisposeAsync must wait for the in-flight action to finish.");
        }

        [Test]
        public async Task DisposeAsync_CalledTwice_IsIdempotentAsync()
        {
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);

            var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                _ => default).ConfigureAwait(false);

            await handle.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                async () => await handle.DisposeAsync().ConfigureAwait(false),
                Throws.Nothing,
                "Second DisposeAsync must be a silent no-op.");
        }

        [Test]
        public async Task DisposeAsync_CancelsInFlightActionTokenAsync()
        {
            // Verify the OCE-catch branch inside RunActionAsync: the action observes
            // ct.IsCancellationRequested (via WaitAsync(ct)) after DisposeAsync
            // cancels the internal CTS, and the OCE is silently swallowed.
            var clock = new FakeTimeProvider();
            var scheduler = new PubSubScheduler(NUnitTelemetryContext.Create(), clock);

            var actionStarted = new SemaphoreSlim(0, 1);
            bool oceCaught = false;

            var handle = await scheduler.ScheduleAsync(
                s_period100ms,
                async ct =>
                {
                    actionStarted.Release();
                    try
                    {
                        // Block until the token provided by DisposeAsync is cancelled.
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        oceCaught = true;
                    }
                }).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromMilliseconds(100)); // start the action
            await actionStarted.WaitAsync().ConfigureAwait(false);

            // DisposeAsync cancels the internal CTS → the action's Task.Delay(Infinite, ct)
            // throws OCE → RunActionAsync's catch(OperationCanceledException) swallows it.
            await handle.DisposeAsync().ConfigureAwait(false);

            Assert.That(oceCaught, Is.True,
                "The in-flight action must receive an OperationCanceledException when " +
                "DisposeAsync cancels the scheduler's internal CTS.");
        }
    }
}
