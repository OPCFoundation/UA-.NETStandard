/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for the Raft-backed client shared-store dependency-injection registrations.
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class RaftClientSharedStoreTests
    {
        [Test]
        public async Task AddRaftClientSharedStoreRegistersStoreAndElectionAsync()
        {
            var services = new ServiceCollection();
            services.AddRaftClientSharedStore();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            ILeaderElection election = provider.GetRequiredService<ILeaderElection>();

            Assert.That(store, Is.InstanceOf<RaftSharedKeyValueStore>());
            Assert.That(election, Is.InstanceOf<RaftLeaderElection>());

            bool created = await store.CompareAndSwapAsync(
                "session/a", default, new ByteString(new byte[] { 1, 2, 3 })).ConfigureAwait(false);
            Assert.That(created, Is.True, "the client store provides a linearizable compare-and-swap");
        }

        [Test]
        public async Task AddRedundantClientSharedStoreStrongUsesRaftStoreAsync()
        {
            var services = new ServiceCollection();
            services.AddRedundantClientSharedStore(RedundancyConsistencyMode.Strong, ReplicaId.New());
            await using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(
                provider.GetRequiredService<ISharedKeyValueStore>(),
                Is.InstanceOf<RaftSharedKeyValueStore>());
        }

        [Test]
        public async Task AddRedundantClientSharedStoreEventualUsesHybridStoreAsync()
        {
            await using var network = new InMemoryNetwork();
            var services = new ServiceCollection();
            services.AddRedundantClientSharedStore(
                RedundancyConsistencyMode.Eventual, ReplicaId.New(), _ => network.CreateTransport());
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            Assert.That(store, Is.InstanceOf<HybridSharedKeyValueStore>());

            // The strong-prefix keyspace gets linearizable CAS via Raft.
            bool created = await store.CompareAndSwapAsync(
                "nonce/x", default, new ByteString(new byte[] { 9 })).ConfigureAwait(false);
            Assert.That(created, Is.True);
        }

        [Test]
        public void AddRedundantClientSharedStoreEventualRequiresCrdtTransport()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => services.AddRedundantClientSharedStore(
                    RedundancyConsistencyMode.Eventual, ReplicaId.New()),
                Throws.TypeOf<ArgumentNullException>());
        }
    }
}
