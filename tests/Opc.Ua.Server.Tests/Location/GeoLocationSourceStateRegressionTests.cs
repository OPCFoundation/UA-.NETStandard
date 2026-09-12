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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Location
{
    [TestFixture]
    [Category("GeoLocation")]
    public sealed class GeoLocationSourceStateRegressionTests
    {
        [Test]
        public async Task WatchingUnknownSourceDoesNotInventAGoodSampleAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            using var cancellation = new CancellationTokenSource();
            await using IAsyncEnumerator<GeoLocationSample> watch =
                provider.WatchAsync("new", cancellation.Token).GetAsyncEnumerator();
            Task<bool> pending = watch.MoveNextAsync().AsTask();
            try
            {
                AssertMissing(provider);
                Assert.That(pending.IsCompleted, Is.False);
                GeoLocationSample expected = GeoLocationSample.Good(
                    new GeoPosition(48, 8), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                provider.Update("new", expected);
                Assert.That(await pending.ConfigureAwait(false), Is.True);
                Assert.That(watch.Current, Is.EqualTo(expected));
                Assert.That(await provider.ReadAsync("new").ConfigureAwait(false), Is.EqualTo(expected));
            }
            finally
            {
                cancellation.Cancel();
                await pending.ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancelingAnUnknownWatchDoesNotMakeTheSourceReadableAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            using var cancellation = new CancellationTokenSource();
            await using IAsyncEnumerator<GeoLocationSample> watch =
                provider.WatchAsync("new", cancellation.Token).GetAsyncEnumerator();
            Task<bool> pending = watch.MoveNextAsync().AsTask();
            cancellation.Cancel();
            Assert.That(await pending.ConfigureAwait(false), Is.False);
            AssertMissing(provider);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FaultingUnknownSourcePreservesTheInjectedFailureAndUpdateClearsItAsync(bool watchFirst)
        {
            using var provider = new InMemoryGeoLocationProvider();
            using var cancellation = new CancellationTokenSource();
            await using IAsyncEnumerator<GeoLocationSample> watch =
                provider.WatchAsync("new", cancellation.Token).GetAsyncEnumerator();
            Task<bool> pending = watchFirst ? watch.MoveNextAsync().AsTask() : Task.FromResult(false);
            try
            {
                var failure = new InvalidOperationException("location unavailable");
                provider.Fault("new", failure);
                InvalidOperationException actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await provider.ReadAsync("new").ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));

                GeoLocationSample expected = GeoLocationSample.Unavailable(StatusCodes.BadNoDataAvailable);
                provider.Update("new", expected);
                Assert.That(await provider.ReadAsync("new").ConfigureAwait(false), Is.EqualTo(expected));
                if (watchFirst)
                {
                    Assert.That(await pending.ConfigureAwait(false), Is.True);
                    Assert.That(watch.Current.StatusCode, Is.EqualTo(StatusCodes.BadNoDataAvailable));
                }
            }
            finally
            {
                cancellation.Cancel();
                await pending.ConfigureAwait(false);
            }
        }

        private static void AssertMissing(InMemoryGeoLocationProvider provider)
        {
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ReadAsync("new").ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }
    }
}
