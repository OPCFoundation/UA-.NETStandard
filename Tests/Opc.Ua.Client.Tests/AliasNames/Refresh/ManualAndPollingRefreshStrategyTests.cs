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
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);

            await strategy.StartAsync(
                client,
                () => invalidations++,
                CancellationToken.None);

            await Task.Delay(50);
            Assert.That(invalidations, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            var strategy = new ManualAliasNameRefreshStrategy();
            await strategy.DisposeAsync();
            await strategy.DisposeAsync();
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
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);

            uint nextReturn = 5;
            harness.ReadHandler = _ => new DataValue(new Variant(nextReturn), StatusCodes.Good);

            int invalidations = 0;
            await using var strategy = new PollingAliasNameRefreshStrategy(
                TimeSpan.FromMilliseconds(120));
            await strategy.StartAsync(client, () => invalidations++,
                CancellationToken.None);

            await WaitForAsync(() => invalidations >= 1);
            int firstInvalidations = invalidations;

            // Same value — must NOT bump.
            await Task.Delay(300);
            Assert.That(invalidations, Is.EqualTo(firstInvalidations));

            // Wraparound — must bump.
            nextReturn = 0;
            await WaitForAsync(() => invalidations > firstInvalidations);
            Assert.That(invalidations, Is.GreaterThan(firstInvalidations));
        }

        [Test]
        public async Task DisposeAsyncStopsTimerAsync()
        {
            AliasNameSessionHarness harness = AliasNameSessionHarness.Create();
            AliasNameClient client = AliasNameClient.OpenStandardAliases(harness.Session);
            harness.ReadHandler = _ => new DataValue(new Variant((uint)1), StatusCodes.Good);

            int invalidations = 0;
            var strategy = new PollingAliasNameRefreshStrategy(
                TimeSpan.FromMilliseconds(100));
            await strategy.StartAsync(client, () => invalidations++,
                CancellationToken.None);
            await Task.Delay(200);
            await strategy.DisposeAsync();
            int snapshot = invalidations;
            await Task.Delay(300);
            Assert.That(invalidations, Is.EqualTo(snapshot));
        }

        private static async Task WaitForAsync(Func<bool> predicate)
        {
            for (int i = 0; i < 50 && !predicate(); i++)
            {
                await Task.Delay(50);
            }
            Assert.That(predicate(), Is.True, "Predicate did not become true within 2.5s.");
        }
    }
}
