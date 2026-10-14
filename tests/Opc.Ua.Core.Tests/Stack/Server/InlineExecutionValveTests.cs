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
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// The valve that pauses running the requests of a connection on its receive
    /// loop once they hold it longer than a budget.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public class InlineExecutionValveTests
    {
        private static readonly TimeSpan kBudget = TimeSpan.FromMilliseconds(5);
        private static readonly TimeSpan kOver = TimeSpan.FromMilliseconds(20);
        private static readonly TimeSpan kUnder = TimeSpan.FromMilliseconds(1);

        [Test]
        public void ASingleOverrunDoesNotPause()
        {
            var time = new FakeTimeProvider();
            var valve = new InlineExecutionValve(time);

            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Run(valve, time, kUnder), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.Zero));
            Assert.That(valve.IsPaused, Is.False);
        }

        [Test]
        public void TwoOverrunsInARowPauseForASecond()
        {
            var time = new FakeTimeProvider();
            var valve = new InlineExecutionValve(time);

            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.FromSeconds(1)));

            Assert.That(valve.IsPaused, Is.True);
            Assert.That(valve.TryBegin(out _), Is.False);
            time.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(valve.TryBegin(out _), Is.False);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(valve.IsPaused, Is.False);
            Assert.That(valve.TryBegin(out _), Is.True);
        }

        [Test]
        public void AnOverrunAfterAPauseDoublesIt()
        {
            var time = new FakeTimeProvider();
            var valve = new InlineExecutionValve(time);
            Run(valve, time, kOver);
            Run(valve, time, kOver);

            TimeSpan expected = TimeSpan.FromSeconds(1);
            for (int ii = 0; ii < InlineExecutionValve.kMaxPauseDoublings + 2; ii++)
            {
                time.Advance(expected);
                expected = TimeSpan.FromSeconds(Math.Min(2 * expected.TotalSeconds, 64));
                Assert.That(Run(valve, time, kOver), Is.EqualTo(expected), $"pause {ii + 2}");
            }
        }

        [Test]
        public void ARequestWithinTheBudgetAfterAPauseResetsTheDoubling()
        {
            var time = new FakeTimeProvider();
            var valve = new InlineExecutionValve(time);
            Run(valve, time, kOver);
            Run(valve, time, kOver);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.FromSeconds(2)));
            time.Advance(TimeSpan.FromSeconds(2));

            Assert.That(Run(valve, time, kUnder), Is.EqualTo(TimeSpan.Zero));
            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.Zero), "a single overrun again");
            Assert.That(Run(valve, time, kOver), Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void TheValveIsCurrentBetweenEnterAndExit()
        {
            var outer = new InlineExecutionValve(TimeProvider.System);
            var inner = new InlineExecutionValve(TimeProvider.System);

            InlineExecutionValve? none = InlineExecutionValve.Enter(outer);
            InlineExecutionValve? previous = InlineExecutionValve.Enter(inner);
            Assert.That(InlineExecutionValve.Current, Is.SameAs(inner));
            InlineExecutionValve.Exit(previous);
            Assert.That(InlineExecutionValve.Current, Is.SameAs(outer));
            InlineExecutionValve.Exit(none);

            Assert.That(none, Is.Null);
            Assert.That(InlineExecutionValve.Current, Is.Null);
        }

        /// <summary>
        /// Runs a request that holds the thread for <paramref name="held"/>.
        /// </summary>
        /// <returns>The pause the valve reports.</returns>
        private static TimeSpan Run(InlineExecutionValve valve, FakeTimeProvider time, TimeSpan held)
        {
            Assert.That(valve.TryBegin(out long started), Is.True, "the request runs inline");
            time.Advance(held);
            TimeSpan pause = valve.Complete(started, kBudget, out TimeSpan measured);
            Assert.That(measured, Is.EqualTo(held));
            return pause;
        }
    }
}
