// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the GetEndpoints load-direction peer signal publish/view
    /// (<see cref="SharedPeerDirectionPublisher"/> / <see cref="SharedPeerDirectionView"/>).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class PeerDirectionViewTests
    {
        [Test]
        public async Task ProtectedPeerSignalsRejectCrossKeySubstitutionAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions
            {
                ServiceLevelKeyPrefix = "deployment/health/",
                LoadKeyPrefix = "deployment/load/",
                PeerApplicationUris = ["urn:server:a"]
            };
            var time = new FakeTimeProvider();
            var publisher = new SharedPeerDirectionPublisher(
                store, context, protector, options, time, "urn:server:a");
            var view = new SharedPeerDirectionView(store, context, protector, options, time);
            await publisher.PublishServiceLevelAsync(200).ConfigureAwait(false);
            await publisher.PublishLoadWeightAsync(30).ConfigureAwait(false);
            ArrayOf<PeerDirectionRecord> original = await view.GetPeersAsync().ConfigureAwait(false);
            Assert.That(original, Has.Count.EqualTo(1));
            Assert.That(original[0].LoadKnown, Is.True);
            Assert.That(original[0].LoadWeight, Is.EqualTo(30));
            (bool found, ByteString health) = await store.TryGetAsync(
                options.ServiceLevelKeyPrefix + "urn:server:a").ConfigureAwait(false);
            Assert.That(found, Is.True);
            await store.SetAsync(options.LoadKeyPrefix + "urn:server:a", health).ConfigureAwait(false);

            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync().ConfigureAwait(false);

            Assert.That(peers, Has.Count.EqualTo(1));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:server:a"));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo(200));
            Assert.That(peers[0].LoadKnown, Is.False);
        }

        [Test]
        public async Task PublishAndViewRoundTripReturnsFreshPeerAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200).ConfigureAwait(false);
            await publisher.PublishLoadWeightAsync(30).ConfigureAwait(false);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServerUri, Is.EqualTo("urn:server:a"));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)200));
            Assert.That(peers[0].LoadKnown, Is.True);
            Assert.That(peers[0].LoadWeight, Is.EqualTo((byte)30));
        }

        [Test]
        public async Task ViewExcludesStaleHealthRecordAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200).ConfigureAwait(false);
            await publisher.PublishLoadWeightAsync(30).ConfigureAwait(false);

            time.Advance(TimeSpan.FromSeconds(20));

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers, Is.Empty, "a peer whose health record is stale must be aged out");
        }

        [Test]
        public async Task ViewReportsLoadUnknownWhenLoadStaleButHealthFreshAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishLoadWeightAsync(30).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(20));
            await publisher.PublishServiceLevelAsync(200).ConfigureAwait(false);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)200));
            Assert.That(peers[0].LoadKnown, Is.False, "a stale load record must be reported as unknown");
            Assert.That(peers[0].LoadWeight, Is.Zero);
        }

        [Test]
        public async Task ViewDropsMalformedHealthRecordAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:x"] };
            var time = new FakeTimeProvider();

            await store.SetAsync("svc/urn:server:x", ByteString.From(new byte[] { 9, 9, 9 })).ConfigureAwait(false);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers, Is.Empty, "an undecodable record must be dropped (fail-closed)");
        }

        [Test]
        public async Task ViewReturnsAllHealthyPeersAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a", "urn:server:b"] };
            var time = new FakeTimeProvider();

            var a = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            var b = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:b");
            await a.PublishServiceLevelAsync(200).ConfigureAwait(false);
            await b.PublishServiceLevelAsync(210).ConfigureAwait(false);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers!.Select(p => p.ServerUri), Is.EquivalentTo(["urn:server:a", "urn:server:b"]));
            Assert.That(peers.Single(p => p.ServerUri == "urn:server:b").ServiceLevel, Is.EqualTo((byte)210));
        }

        [Test]
        public async Task RepublishOverwritesPreviousSignalAsync()
        {
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = ["urn:server:a"] };
            var time = new FakeTimeProvider();

            var publisher = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, time, "urn:server:a");
            await publisher.PublishServiceLevelAsync(200).ConfigureAwait(false);
            time.Advance(TimeSpan.FromSeconds(1));
            await publisher.PublishServiceLevelAsync(120).ConfigureAwait(false);

            var view = new SharedPeerDirectionView(
                store, context, NullRecordProtector.Instance, options, time);
            PeerDirectionRecord[] peers = (await view.GetPeersAsync().ConfigureAwait(false)).ToArray()!;

            Assert.That(peers, Has.Length.EqualTo(1));
            Assert.That(peers[0].ServiceLevel, Is.EqualTo((byte)120), "the latest published health value must win");
        }

        [Test]
        public async Task ViewRequiresAgreementWithMemberKeyAsync()
        {
            const string configuredUri = "urn:server:a";
            const string otherUri = "urn:server:b";
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            IServiceMessageContext context = CreateContext();
            var time = new FakeTimeProvider();
            var options = new LoadDirectionOptions { PeerApplicationUris = [configuredUri] };
            var view = new SharedPeerDirectionView(store, context, protector, options, time);
            var other = new SharedPeerDirectionPublisher(store, context, protector, options, time, otherUri);
            await other.PublishServiceLevelAsync(200).ConfigureAwait(false);

            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync().ConfigureAwait(false);
            Assert.That(peers.Count, Is.Zero, "the member is not configured");

            string recordKey = options.ServiceLevelKeyPrefix + configuredUri;
            ByteString mismatchedSignal = PeerDirectionCodec.Encode(
                otherUri, 200, time.GetUtcNow().UtcDateTime.Ticks, context);
            await store.SetAsync(
                recordKey, protector.Protect(RecordProtectionContext.Create("peer-direction", recordKey),
                    mismatchedSignal)).ConfigureAwait(false);

            peers = await view.GetPeersAsync().ConfigureAwait(false);
            Assert.That(peers.Count, Is.Zero, "the signal names a different member");
        }

        [Test]
        public async Task ViewRejectsFutureSignalsAsync()
        {
            const string serverUri = "urn:server:a";
            using var store = new InMemorySharedKeyValueStore();
            IServiceMessageContext context = CreateContext();
            var options = new LoadDirectionOptions { PeerApplicationUris = [serverUri] };
            var now = new FakeTimeProvider();
            var ahead = new FakeTimeProvider(now.GetUtcNow().AddTicks(1));
            var future = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, ahead, serverUri);
            var current = new SharedPeerDirectionPublisher(
                store, context, NullRecordProtector.Instance, options, now, serverUri);
            var view = new SharedPeerDirectionView(store, context, NullRecordProtector.Instance, options, now);
            await future.PublishServiceLevelAsync(200).ConfigureAwait(false);
            await future.PublishLoadWeightAsync(30).ConfigureAwait(false);

            ArrayOf<PeerDirectionRecord> peers = await view.GetPeersAsync().ConfigureAwait(false);
            Assert.That(peers.Count, Is.Zero, "future health is not eligible");

            await current.PublishServiceLevelAsync(200).ConfigureAwait(false);
            peers = await view.GetPeersAsync().ConfigureAwait(false);

            Assert.That(peers.Count, Is.EqualTo(1));
            Assert.That(peers[0].LoadKnown, Is.False, "future load is not eligible even with valid health");
        }

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }
    }
}
