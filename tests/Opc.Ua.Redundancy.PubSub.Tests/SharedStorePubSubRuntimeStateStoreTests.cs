// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Tests for <see cref="SharedStorePubSubRuntimeStateStore"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class SharedStorePubSubRuntimeStateStoreTests
    {
        [Test]
        public async Task GetStateReturnsNullWhenAbsentAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            PubSubState? state = await store.GetStateAsync("pubsub:connection:Connection1").ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task SetThenGetRoundTripsStateAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            await store.SetStateAsync("pubsub:connection:Connection1", PubSubState.Operational).ConfigureAwait(false);
            PubSubState? state = await store.GetStateAsync("pubsub:connection:Connection1").ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task SetOverwritesPreviousStateAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            await store.SetStateAsync("pubsub:writergroup:WriterGroup1", PubSubState.Paused).ConfigureAwait(false);
            await store.SetStateAsync("pubsub:writergroup:WriterGroup1", PubSubState.Error).ConfigureAwait(false);
            PubSubState? state = await store.GetStateAsync("pubsub:writergroup:WriterGroup1").ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(PubSubState.Error));
        }

        [Test]
        public async Task SharedBackendIsVisibleToAnotherStoreInstanceAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var writer = new SharedStorePubSubRuntimeStateStore(backend);
            var reader = new SharedStorePubSubRuntimeStateStore(backend);

            await writer.SetStateAsync("pubsub:datasetwriter:Writer1", PubSubState.Operational).ConfigureAwait(false);
            PubSubState? state = await reader.GetStateAsync("pubsub:datasetwriter:Writer1").ConfigureAwait(false);

            Assert.That(state, Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task SetStateRejectsStaleFencingTokenAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            await store.SetStateAsync(
                "pubsub:datasetwriter:Writer1",
                PubSubState.Operational,
                10).ConfigureAwait(false);

            Assert.That(
                async () => await store.SetStateAsync(
                    "pubsub:datasetwriter:Writer1",
                    PubSubState.Error,
                    9).ConfigureAwait(false),
                Throws.InvalidOperationException);
            Assert.That(
                await store.GetStateAsync("pubsub:datasetwriter:Writer1").ConfigureAwait(false),
                Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public void SetStateRejectsEmptyComponentId()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubRuntimeStateStore(backend);

            Assert.That(
                async () => await store.SetStateAsync(string.Empty, PubSubState.Operational).ConfigureAwait(false),
                Throws.ArgumentException);
        }
    }
}
