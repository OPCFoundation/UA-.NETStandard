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

#pragma warning disable CS0618 // AsyncResultBase is obsolete but still backs server reverse connect

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies that <see cref="AsyncResultBase"/> completes exactly once when a
    /// timeout races with the real completion.
    /// </summary>
    [TestFixture]
    [Category("Transport")]
    [Parallelizable]
    public sealed class AsyncResultBaseTests
    {
        [Test]
        public void TimeoutCompletesOnceAndLateCompletionCannotOverwriteIt()
        {
            var clock = new FakeTimeProvider();
            int callbacks = 0;
            using var result = new AsyncResultBase(_ => callbacks++, null, 1000, null, null, clock);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(result.Exception, Is.InstanceOf<TimeoutException>());

            Assert.That(result.TryComplete(null), Is.False);
            result.OperationCompleted();
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(result.Exception, Is.InstanceOf<TimeoutException>());
        }

        [Test]
        public void CompletionBeforeTimeoutIsNotOverwrittenByTheTimer()
        {
            var clock = new FakeTimeProvider();
            int callbacks = 0;
            using var result = new AsyncResultBase(_ => callbacks++, null, 1000, null, null, clock);

            Assert.That(result.TryComplete(null), Is.True);
            clock.Advance(TimeSpan.FromSeconds(5));
            result.OperationCompleted();

            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(result.Exception, Is.Null);
            Assert.That(result.WaitForComplete(), Is.True);
        }

        [Test]
        public void WaitForCompleteReportsCompletionProcessedAfterTheDeadline()
        {
            var clock = new FakeTimeProvider();
            using var result = new AsyncResultBase(null, null, 1000, null, null, clock);

            // no callback, so no timer: the deadline passes without a timeout completion.
            clock.Advance(TimeSpan.FromSeconds(2));
            result.OperationCompleted();

            Assert.That(result.WaitForComplete(), Is.True);
        }
    }
}
