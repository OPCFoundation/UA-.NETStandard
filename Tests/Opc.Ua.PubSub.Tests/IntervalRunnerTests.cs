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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for IntervalRunner start/stop/dispose and interval clamping")]
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
            Func<bool> canExecute = () => true;
            Func<Task> action = () => Task.CompletedTask;

            using var runner = new IntervalRunner(id, 100, canExecute, action, m_telemetry);

            Assert.That(runner.Id, Is.EqualTo("runner1"));
            Assert.That(runner.Interval, Is.EqualTo(100));
            Assert.That(runner.CanExecuteFunc, Is.SameAs(canExecute));
            Assert.That(runner.IntervalActionAsync, Is.SameAs(action));
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

            Assert.DoesNotThrow(() => runner.Stop());
        }

        [Test]
        public void DisposeDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            Assert.DoesNotThrow(() => runner.Dispose());
        }

        [Test]
        public void DisposeAfterStartDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Start();
            Assert.DoesNotThrow(() => runner.Dispose());
        }

        [Test]
        public void DoubleDisposeDoesNotThrow()
        {
            var runner = new IntervalRunner(
                "runner", 100, () => true, () => Task.CompletedTask, m_telemetry);

            runner.Dispose();
            Assert.DoesNotThrow(() => runner.Dispose());
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

            Assert.That(executionCount, Is.EqualTo(0));
        }

        [Test]
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
    }
}
