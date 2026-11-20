// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.AliasNames.Refresh;

namespace Opc.Ua.Client.Tests.AliasNames.Refresh
{
    /// <summary>
    /// Coverage tests for <see cref="ManualAliasNameRefreshStrategy"/>:
    /// it must never call <c>onInvalidate</c> and must be idempotently
    /// disposable.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class ManualAliasNameRefreshStrategyTests
    {
        [Test]
        public async Task StartAsyncIsANoOpAsync()
        {
            await using var strategy = new ManualAliasNameRefreshStrategy();
            int invalidations = 0;
            var harness = AliasNameSessionHarness.Create();
            var client = AliasNameClient.OpenStandardAliases(harness.Session);

            await strategy.StartAsync(
                client,
                () => invalidations++,
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(invalidations, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var strategy = new ManualAliasNameRefreshStrategy();
            await strategy.DisposeAsync().ConfigureAwait(false);
            await strategy.DisposeAsync().ConfigureAwait(false);
            Assert.Pass();
        }
    }

    /// <summary>
    /// Coverage tests for <see cref="PollingAliasNameRefreshStrategy"/>:
    /// timer triggers <c>ReadAsync</c> on the session; onInvalidate fires
    /// only on a value-difference (wrap-safe). Uses the existing
    /// <c>AliasNameSessionHarness</c> as the mocked session.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class PollingAliasNameRefreshStrategyTests
    {
        [Test]
        public void ConstructorRejectsIntervalBelowMinimum()
        {
            Assert.That(
                () => new PollingAliasNameRefreshStrategy(
                    TimeSpan.FromMilliseconds(50)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task FiresOnValueDifferenceAndWrapAroundAsync()
        {
            var harness = AliasNameSessionHarness.Create();
            var client = AliasNameClient.OpenStandardAliases(harness.Session);

            uint nextReturn = 5;
            harness.ReadHandler = _ => new DataValue(new Variant(nextReturn), StatusCodes.Good);

            int invalidations = 0;
            await using var strategy = new PollingAliasNameRefreshStrategy(
                TimeSpan.FromMilliseconds(120));
            await strategy.StartAsync(client, () => invalidations++,
                CancellationToken.None).ConfigureAwait(false);

            await WaitForAsync(() => invalidations >= 1).ConfigureAwait(false);
            int firstInvalidations = invalidations;

            // Same value — must NOT bump.
            await Task.Delay(300).ConfigureAwait(false);
            Assert.That(invalidations, Is.EqualTo(firstInvalidations));

            // Wraparound — must bump.
            nextReturn = 0;
            await WaitForAsync(() => invalidations > firstInvalidations).ConfigureAwait(false);
            Assert.That(invalidations, Is.GreaterThan(firstInvalidations));
        }

        [Test]
        public async Task DisposeAsyncStopsTimerAsync()
        {
            var harness = AliasNameSessionHarness.Create();
            var client = AliasNameClient.OpenStandardAliases(harness.Session);
            harness.ReadHandler = _ => new DataValue(new Variant((uint)1), StatusCodes.Good);

            int invalidations = 0;
            var strategy = new PollingAliasNameRefreshStrategy(
                TimeSpan.FromMilliseconds(100));
            await strategy.StartAsync(client, () => invalidations++,
                CancellationToken.None).ConfigureAwait(false);
            // Wait for at least one tick to actually fire before disposing -
            // a fixed `Task.Delay(200)` raced with cold-start scheduling on
            // slow CI agents (observed on Microsoft-hosted macOS-15), letting
            // the first tick land in the post-Dispose window and triggering a
            // false-positive "DisposeAsync didn't stop the timer".
            await WaitForAsync(() => invalidations >= 1).ConfigureAwait(false);
            await strategy.DisposeAsync().ConfigureAwait(false);
            int snapshot = invalidations;
            await Task.Delay(300).ConfigureAwait(false);
            Assert.That(invalidations, Is.EqualTo(snapshot));
        }

        private static async Task WaitForAsync(Func<bool> predicate)
        {
            for (int i = 0; i < 50 && !predicate(); i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
            }
            Assert.That(predicate(), Is.True, "Predicate did not become true within 2.5s.");
        }
    }
}
