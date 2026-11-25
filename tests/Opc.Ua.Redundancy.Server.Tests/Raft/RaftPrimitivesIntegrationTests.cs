// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration tests for conditional writes, lease election and consistency-mode DI over the Raft store.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RaftPrimitivesIntegrationTests
    {
        [Test]
        public async Task RaftStorePermitsOneConditionalInsertUnderContentionAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            var value = ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            const int contenders = 24;

            IEnumerable<Task<bool>> races = Enumerable.Range(0, contenders)
                .Select(_ => store.CompareAndSwapAsync("election/claim", default, value).AsTask());
            bool[] results = await Task.WhenAll(races).ConfigureAwait(false);

            Assert.That(results.Count(committed => committed), Is.EqualTo(1));
        }

        [Test]
        public async Task BareRaftStoreDoesNotBackSharedLeaseAsync()
        {
            await using var store = new RaftSharedKeyValueStore();
            Assert.That(
                () => new SharedStoreLeaseElection(
                    store, "lease/leader", "node-1", TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10)),
                Throws.ArgumentException);
        }

        [Test]
        public async Task ConsistencyRegistrationProvidesConditionalWritesAsync()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(_ => { })
                .UseRedundancyConsistency();
            await using ServiceProvider provider = services.BuildServiceProvider();

            ISharedKeyValueStore store = provider.GetRequiredService<ISharedKeyValueStore>();
            var value = ByteString.From(new byte[] { 9, 9, 9, 9 });

            bool first = await store.CompareAndSwapAsync("election/claim", default, value).ConfigureAwait(false);
            bool second = await store.CompareAndSwapAsync("election/claim", default, value).ConfigureAwait(false);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False, "the key is no longer absent");
        }
    }
}
