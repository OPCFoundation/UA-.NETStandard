// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.Location
{
    /// <summary>
    /// Tests for <see cref="InMemoryGeoLocationProvider"/>, the reference
    /// implementation of the shared <see cref="IGeoLocationProvider"/> seam.
    /// </summary>
    [TestFixture]
    [Category("GeoLocation")]
    public class InMemoryGeoLocationProviderTests
    {
        private const string SourceId = "plant";

        [Test]
        public async Task ReadReturnsTheCurrentSampleAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, new GeoPosition(47.0, 8.0));

            GeoLocationSample sample = await provider
                .ReadAsync(SourceId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(sample.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(sample.Position, Is.Not.Null);
            Assert.That(sample.Position!.Value.Latitude, Is.EqualTo(47.0));
            Assert.That(sample.Position!.Value.Longitude, Is.EqualTo(8.0));
        }

        [Test]
        public void ReadingAnUnknownSourceFails()
        {
            using var provider = new InMemoryGeoLocationProvider();

            Assert.That(
                async () => await provider
                    .ReadAsync("missing", CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task UpdatesReachWatchersInOrderAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, new GeoPosition(47.0, 8.0));
            using var cts = new CancellationTokenSource();

            await using IAsyncEnumerator<GeoLocationSample> enumerator = provider
                .WatchAsync(SourceId, cts.Token)
                .GetAsyncEnumerator(cts.Token);

            ValueTask<bool> first = enumerator.MoveNextAsync();
            provider.Update(SourceId, new GeoPosition(48.0, 8.0));
            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Position!.Value.Latitude, Is.EqualTo(48.0));

            ValueTask<bool> second = enumerator.MoveNextAsync();
            provider.Update(SourceId, new GeoPosition(49.0, 8.0));
            Assert.That(await second.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Position!.Value.Latitude, Is.EqualTo(49.0));

            await cts.CancelAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WatchersOnlySeeTheirOwnSourceAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update("a", new GeoPosition(1.0, 1.0));
            provider.Update("b", new GeoPosition(2.0, 2.0));
            using var cts = new CancellationTokenSource();

            await using IAsyncEnumerator<GeoLocationSample> enumerator = provider
                .WatchAsync("a", cts.Token)
                .GetAsyncEnumerator(cts.Token);

            ValueTask<bool> next = enumerator.MoveNextAsync();
            // An update to the other source must not wake this watcher.
            provider.Update("b", new GeoPosition(3.0, 3.0));
            provider.Update("a", new GeoPosition(4.0, 4.0));

            Assert.That(await next.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Position!.Value.Latitude, Is.EqualTo(4.0));

            await cts.CancelAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WatchEndsOnCancellationAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, new GeoPosition(47.0, 8.0));
            using var cts = new CancellationTokenSource();

            var consume = Task.Run(async () =>
            {
                await foreach (GeoLocationSample _ in provider
                    .WatchAsync(SourceId, cts.Token).ConfigureAwait(false))
                {
                    // Drain until cancelled.
                }
            });

            await cts.CancelAsync().ConfigureAwait(false);
            await consume.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(consume.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void AFaultPropagatesToReaders()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, new GeoPosition(47.0, 8.0));
            provider.Fault(SourceId, new InvalidOperationException("boom"));

            Assert.That(
                async () => await provider
                    .ReadAsync(SourceId, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task AnUpdateClearsAFaultAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, new GeoPosition(47.0, 8.0));
            provider.Fault(SourceId, new InvalidOperationException("boom"));

            provider.Update(SourceId, new GeoPosition(50.0, 8.0));
            GeoLocationSample sample = await provider
                .ReadAsync(SourceId, CancellationToken.None).ConfigureAwait(false);

            Assert.That(sample.Position!.Value.Latitude, Is.EqualTo(50.0));
        }

        [Test]
        public void AnEmptySourceIdIsRejected()
        {
            using var provider = new InMemoryGeoLocationProvider();

            Assert.That(
                () => provider.Update(" ", new GeoPosition(47.0, 8.0)),
                Throws.ArgumentException);
        }
    }
}
