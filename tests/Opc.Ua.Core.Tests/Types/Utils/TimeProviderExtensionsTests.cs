// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public sealed class TimeProviderExtensionsTests
    {
        [Test]
        public void CreateCancellationTokenSourceSupportsMaximumTimerDelay()
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(uint.MaxValue - 1u);

            using CancellationTokenSource cts =
                TimeProvider.System.CreateCancellationTokenSource(delay);

            Assert.That(cts.IsCancellationRequested, Is.False);
        }

        [Test]
        public void CreateCancellationTokenSourceClampsDelayAboveMaximumTimerDelay()
        {
            var timeProvider = new FakeTimeProvider();
            TimeSpan maximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1u);

            using CancellationTokenSource cts = timeProvider.CreateCancellationTokenSource(
                TimeSpan.FromMilliseconds(uint.MaxValue));

            timeProvider.Advance(maximumTimerDelay - TimeSpan.FromTicks(1));
            Assert.That(cts.IsCancellationRequested, Is.False);

            timeProvider.Advance(TimeSpan.FromTicks(1));
            Assert.That(cts.IsCancellationRequested, Is.True);
        }

        [Test]
        public void CreateCancellationTokenSourceUsesProvidedTimeProvider()
        {
            var timeProvider = new FakeTimeProvider();
            TimeSpan delay = TimeSpan.FromMinutes(1);

            using CancellationTokenSource cts =
                timeProvider.CreateCancellationTokenSource(delay);

            timeProvider.Advance(delay - TimeSpan.FromTicks(1));
            Assert.That(cts.IsCancellationRequested, Is.False);

            timeProvider.Advance(TimeSpan.FromTicks(1));
            Assert.That(cts.IsCancellationRequested, Is.True);
        }
    }
}
