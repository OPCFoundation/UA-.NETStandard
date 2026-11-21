// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Location
{
    /// <summary>
    /// Verifies that geolocation watches, faults, and updates preserve the distinction between missing and sampled
    /// sources.
    /// </summary>
    [TestFixture]
    [Category("GeoLocation")]
    public sealed class GeoLocationSourceStateRegressionTests
    {
        /// <summary>
        /// Verifies that watching a missing source waits for its first update without making a sample readable.
        /// </summary>
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
                await AssertMissing(provider);
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

        /// <summary>
        /// Verifies that cancelling a watch on a missing source leaves reads reporting not found.
        /// </summary>
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
            await AssertMissing(provider);
        }

        /// <summary>
        /// Verifies that faults on missing sources are preserved until an update replaces them with a real sample.
        /// </summary>
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
                InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
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

        /// <summary>
        /// Verifies that the watched source remains absent from readable geolocation state.
        /// </summary>
        private static async Task AssertMissing(InMemoryGeoLocationProvider provider)
        {
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ReadAsync("new").ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }
    }
}
