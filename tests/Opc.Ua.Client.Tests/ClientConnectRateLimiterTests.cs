// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for client-wide initial connect admission limiting.
    /// </summary>
    [TestFixture]
    [Category("RateLimiting")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ClientConnectRateLimiterTests
    {
        [Test]
        public async Task AcquireAsyncWaitsUntilPermitIsReleased()
        {
            using var gate = new RateLimiterClientConnectGate(maxConcurrency: 1);
            IDisposable first = await gate.AcquireAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Task<IDisposable> secondAcquire = gate
                .AcquireAsync(CancellationToken.None)
                .AsTask();

            Assert.That(secondAcquire.IsCompleted, Is.False);

            first.Dispose();

            Task completed = await Task
                .WhenAny(secondAcquire, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(secondAcquire));

            using IDisposable second = await secondAcquire.ConfigureAwait(false);
            Assert.That(second, Is.Not.Null);
        }

        [Test]
        public void ConstructorRejectsInvalidMaxConcurrency()
        {
            Assert.That(
                () => new RateLimiterClientConnectGate(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
