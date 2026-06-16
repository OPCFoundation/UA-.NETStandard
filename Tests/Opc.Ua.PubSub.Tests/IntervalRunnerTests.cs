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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture]
    [Category("IntervalRunner")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class IntervalRunnerTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorSetsProperties()
        {
            object id = "runner1";
            static bool canExecute() => true;
            static Task action() => Task.CompletedTask;

            using var runner = new IntervalRunner(id, 100, canExecute, action, m_telemetry);

            Assert.That(runner.Id, Is.EqualTo("runner1"));
            Assert.That(runner.Interval, Is.EqualTo(100));
            // Cast to object to suppress NUnit's auto-invocation of
            // Func<T> actuals — we want reference equality on the
            // delegate, not the bool the delegate returns.
            Assert.That((object)runner.CanExecuteFunc, Is.SameAs(canExecute));
            Assert.That((object)runner.IntervalActionAsync, Is.SameAs(action));
        }

        [Test]
        public void IntervalClampsToMinimumOf10()
        {
            using var runner = new IntervalRunner(
                "runner", 1, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.That(runner.Interval, Is.EqualTo(10));
        }

        [Test]
        public void IntervalSetterClampsNegativeToMinimum()
        {
            using var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Interval = -5;
            Assert.That(runner.Interval, Is.EqualTo(10));
        }

        [Test]
        public void IntervalSetterClampsZeroToMinimum()
        {
            using var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Interval = 0;
            Assert.That(runner.Interval, Is.EqualTo(10));
        }

        [Test]
        public void IntervalSetterAcceptsValidValue()
        {
            using var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Interval = 500;
            Assert.That(runner.Interval, Is.EqualTo(500));
        }

        [Test]
        [Explicit] // Too timing-sensitive for regular test runs
        public async Task StartExecutesActionAsync()
        {
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "runner",
                10,
                () => true,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry);

            runner.Start();
            await Task.Delay(200).ConfigureAwait(false);
            runner.Stop();

            Assert.That(executionCount, Is.GreaterThan(0));
        }

        [Test]
        [Explicit] // Too timing-sensitive for regular test runs
        public async Task StopPreventsSubsequentExecutionAsync()
        {
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "runner",
                10,
                () => true,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry);

            runner.Start();
            await Task.Delay(100).ConfigureAwait(false);
            runner.Stop();

            int countAfterStop = executionCount;
            await Task.Delay(100).ConfigureAwait(false);

            Assert.That(executionCount, Is.LessThanOrEqualTo(countAfterStop + 1));
        }

        [Test]
        public void StopWithoutStartDoesNotThrow()
        {
            using var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.DoesNotThrow(runner.Stop);
        }

        [Test]
        public void DisposeDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.DoesNotThrow(runner.Dispose);
        }

        [Test]
        public void DisposeAfterStartDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Start();
            Assert.DoesNotThrow(runner.Dispose);
        }

        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Dispose();
            Assert.DoesNotThrow(runner.Dispose);
        }

        [Test]
        public async Task CanExecuteFuncFalseSkipsActionAsync()
        {
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "runner",
                10,
                () => false,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry);

            runner.Start();
            await Task.Delay(200).ConfigureAwait(false);
            runner.Stop();

            Assert.That(executionCount, Is.Zero);
        }

        [Test]
        [Explicit] // Too timing-sensitive for regular test runs
        public async Task RestartAfterStopIsAllowedAsync()
        {
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "runner",
                10,
                () => true,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry);

            runner.Start();
            await Task.Delay(100).ConfigureAwait(false);
            runner.Stop();

            int countAfterFirstStop = executionCount;

            runner.Start();
            await Task.Delay(100).ConfigureAwait(false);
            runner.Stop();

            Assert.That(executionCount, Is.GreaterThan(countAfterFirstStop));
        }

        [Test]
        public void IdAcceptsNullValue()
        {
            using var runner = new IntervalRunner(
                null, 100, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.That(runner.Id, Is.Null);
        }

        [Test]
        public void IdAcceptsIntValue()
        {
            using var runner = new IntervalRunner(
                42, 100, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.That(runner.Id, Is.EqualTo(42));
        }

        [Test]
        [Retry(2)]
        public async Task FakeTimeProviderDrivesDeterministicSchedulingAsync()
        {
            var fake = new FakeTimeProvider();
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "fake-runner",
                100,
                () => true,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry,
                fake);

            runner.Start();

            // The first loop iteration runs synchronously (sleepCycle == 0)
            // and queues the action on the thread pool.
            await WaitForAsync(() => Volatile.Read(ref executionCount) >= 1)
                .ConfigureAwait(false);
            int afterStart = Volatile.Read(ref executionCount);

            // Advance the fake clock by one interval; the awaited Delay completes
            // deterministically and the next action fires.
            fake.Advance(TimeSpan.FromMilliseconds(100));
            await WaitForAsync(() => Volatile.Read(ref executionCount) >= afterStart + 1)
                .ConfigureAwait(false);

            // Advance three intervals (one at a time) and wait for the runner
            // to register and fire each new Delay; a single Advance(300) would
            // race because each subsequent Delay is only registered after the
            // previous timer's continuation resumes on the thread pool.
            int afterFirstAdvance = Volatile.Read(ref executionCount);
            for (int i = 0; i < 3; i++)
            {
                int before = Volatile.Read(ref executionCount);

                // Yield + small real-time wait so the worker thread that just
                // completed the previous iteration has the scheduler slot to
                // register the next Delay against the fake provider BEFORE we
                // Advance. Without this the Advance can fire into nothingness
                // on a slow CI runner (observed on a Windows GH Actions agent:
                // 5-second WaitForAsync timed out because no Delay was active
                // at the moment Advance(100) fired).
                await Task.Yield();
                await Task.Delay(20).ConfigureAwait(false);

                fake.Advance(TimeSpan.FromMilliseconds(100));
                await WaitForAsync(() => Volatile.Read(ref executionCount) >= before + 1)
                    .ConfigureAwait(false);
            }
            Assert.That(
                Volatile.Read(ref executionCount),
                Is.GreaterThanOrEqualTo(afterFirstAdvance + 3));

            runner.Stop();
        }

        [Test]
        public async Task FakeTimeProviderWithoutAdvanceDoesNotExecuteRepeatedlyAsync()
        {
            var fake = new FakeTimeProvider();
            int executionCount = 0;
            using var runner = new IntervalRunner(
                "fake-runner-no-advance",
                100,
                () => true,
                () =>
                {
                    Interlocked.Increment(ref executionCount);
                    return Task.CompletedTask;
                },
                m_telemetry,
                fake);

            runner.Start();

            // The first iteration runs immediately (sleepCycle == 0) and queues an action.
            await WaitForAsync(() => Volatile.Read(ref executionCount) >= 1)
                .ConfigureAwait(false);

            // Without advancing the fake clock, subsequent iterations stay parked in
            // Delay; assert the count stays at 1 for a long real-time window.
            await Task.Delay(200).ConfigureAwait(false);

            runner.Stop();
            Assert.That(Volatile.Read(ref executionCount), Is.EqualTo(1));
        }

        private static async Task WaitForAsync(
            Func<bool> condition,
            TimeSpan? timeout = null)
        {
            TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(5);
            DateTime end = DateTime.UtcNow + deadline;
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    Assert.Fail($"Condition not satisfied within {deadline.TotalMilliseconds}ms.");
                }
                await Task.Delay(5).ConfigureAwait(false);
            }
        }
    }
}
