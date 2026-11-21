// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
