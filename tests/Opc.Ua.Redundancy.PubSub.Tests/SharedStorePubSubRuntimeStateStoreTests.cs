// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

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
                async () => await store.SetStateAsync("pubsub:datasetwriter:Writer1", PubSubState.Error)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException);
            Assert.That(
                await store.GetStateAsync("pubsub:datasetwriter:Writer1").ConfigureAwait(false),
                Is.EqualTo(PubSubState.Operational));
        }

        [Test]
        public async Task ProtectedRuntimeStateOnlyAcceptsCurrentOrHigherFenceAsync()
        {
            const string componentId = "pubsub:writer:Connection1:Group1:Writer1";
            using var payload = new SimulatedSharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(new byte[32]);
            var fresh = new FreshnessProtectedKeyValueStore(
                payload, authority, protector, Guid.NewGuid(), Guid.NewGuid());
            await fresh.EnrollAsync().ConfigureAwait(false);
            var active = new SharedStorePubSubRuntimeStateStore(fresh);
            var standby = new SharedStorePubSubRuntimeStateStore(fresh);

            Assert.That(
                async () => await standby.SetStateAsync(componentId, PubSubState.Paused).ConfigureAwait(false),
                Throws.InvalidOperationException);
            await active.SetStateAsync(componentId, PubSubState.Operational, 1).ConfigureAwait(false);
            await standby.SetStateAsync(componentId, PubSubState.PreOperational, 2).ConfigureAwait(false);
            Assert.That(
                async () => await active.SetStateAsync(componentId, PubSubState.Disabled, 1).ConfigureAwait(false),
                Throws.InvalidOperationException);
            await standby.SetStateAsync(componentId, PubSubState.Operational, 2).ConfigureAwait(false);
            Assert.That(
                await active.GetStateAsync(componentId).ConfigureAwait(false), Is.EqualTo(PubSubState.Operational));
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
