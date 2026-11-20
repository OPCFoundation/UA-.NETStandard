// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Tests for <see cref="SharedStorePubSubWriterCheckpointStore"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class SharedStorePubSubWriterCheckpointStoreTests
    {
        [Test]
        public async Task GetReturnsNullWhenAbsentAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1).ConfigureAwait(false);

            Assert.That(sequence, Is.Null);
        }

        [Test]
        public async Task SetThenGetRoundTripsSequenceNumberAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 7, 4200u).ConfigureAwait(false);
            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 7).ConfigureAwait(false);

            Assert.That(sequence, Is.EqualTo(4200u));
        }

        [Test]
        public async Task WritersAreCheckpointedIndependentlyAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1, 100u).ConfigureAwait(false);
            await store.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 2, 200u).ConfigureAwait(false);

            Assert.That(
                await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1).ConfigureAwait(false),
                Is.EqualTo(100u));
            Assert.That(
                await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 2).ConfigureAwait(false),
                Is.EqualTo(200u));
        }

        [Test]
        public async Task GetReturnsNullWhenStoredValueIsTruncatedAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);
            const string key = PubSubRedundancyStoreKeys.CheckpointPrefix + "pubsub:writergroup:WriterGroup1/1";
            await backend.SetAsync(key, new ByteString(new byte[] { 0x01 })).ConfigureAwait(false);

            uint? sequence = await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1).ConfigureAwait(false);

            Assert.That(sequence, Is.Null);
        }

        [Test]
        public void SetSequenceNumberThrowsWhenComponentIdIsEmpty()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            Assert.That(
                async () => await store.SetSequenceNumberAsync(string.Empty, 1, 5u).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public async Task PromotedStandbyReadsTheActivesCheckpointAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var active = new SharedStorePubSubWriterCheckpointStore(backend);
            var standby = new SharedStorePubSubWriterCheckpointStore(backend);

            await active.SetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1, 999u).ConfigureAwait(false);
            uint? seen = await standby.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1).ConfigureAwait(false);

            Assert.That(seen, Is.EqualTo(999u));
        }

        [Test]
        public async Task SetSequenceNumberRejectsStaleFencingTokenAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubWriterCheckpointStore(backend);

            await store.SetSequenceNumberAsync(
                "pubsub:writergroup:WriterGroup1",
                1,
                123u,
                10).ConfigureAwait(false);

            Assert.That(
                async () => await store.SetSequenceNumberAsync(
                    "pubsub:writergroup:WriterGroup1",
                    1,
                    456u,
                    9).ConfigureAwait(false),
                Throws.InvalidOperationException);
            Assert.That(
                await store.GetSequenceNumberAsync("pubsub:writergroup:WriterGroup1", 1).ConfigureAwait(false),
                Is.EqualTo(123u));
        }
    }
}
