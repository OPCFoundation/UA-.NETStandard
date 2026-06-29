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

#nullable enable

using System;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for the CRDT-backed <see cref="CrdtSharedKeyValueStore"/> used to
    /// replicate mirrored session entries active/active.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class CrdtSharedKeyValueStoreTests
    {
        [Test]
        public async Task ReplicatesSetAndDeleteAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var storeA = CreateStore(network, 1);
            await using var storeB = CreateStore(network, 2);

            // Warm up both transports so neither misses the other's broadcasts.
            await storeA.TryGetAsync("warmup");
            await storeB.TryGetAsync("warmup");

            var value = new ByteString(new byte[] { 1, 2, 3, 4 });
            await storeA.SetAsync("session/abc", value);

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, ByteString stored) = await storeB.TryGetAsync("session/abc");
                    return found && stored.ToArray().AsSpan().SequenceEqual(value.ToArray());
                },
                "a value set on A should replicate to B");

            await storeA.DeleteAsync("session/abc");

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, _) = await storeB.TryGetAsync("session/abc");
                    return !found;
                },
                "a delete on A should replicate to B");
        }

        [Test]
        public async Task CompareAndSwapThrowsNotSupportedAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var store = CreateStore(network, 1);

            Assert.That(
                async () => await store.CompareAndSwapAsync("k", default, new ByteString(new byte[] { 1 })),
                Throws.TypeOf<NotSupportedException>(),
                "CRDT stores cannot provide a linearizable compare-and-swap");
        }

        [Test]
        public async Task ScanReturnsOnlyEntriesWithMatchingPrefixAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var store = CreateStore(network, 1);

            await store.SetAsync("session/a", new ByteString(new byte[] { 1 }));
            await store.SetAsync("session/b", new ByteString(new byte[] { 2 }));
            await store.SetAsync("other/c", new ByteString(new byte[] { 3 }));

            var found = new System.Collections.Generic.List<string>();
            await foreach (System.Collections.Generic.KeyValuePair<string, ByteString> pair in
                store.ScanAsync("session/"))
            {
                found.Add(pair.Key);
            }

            Assert.That(found, Is.EquivalentTo(s_sessionKeys));
        }

        [Test]
        public async Task WatchThrowsNotSupported()
        {
            await using var network = new InMemoryNetwork();
            await using var store = CreateStore(network, 1);

            Assert.That(() => store.WatchAsync("session/"), Throws.TypeOf<NotSupportedException>());
        }

        private static CrdtSharedKeyValueStore CreateStore(InMemoryNetwork network, ulong replica)
        {
            return new CrdtSharedKeyValueStore(
                ReplicaId.FromUInt64(replica),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);
        }

        private static async Task AssertEventuallyAsync(Func<Task<bool>> condition, string message)
        {
            // Generous deadline: CRDT convergence over the in-memory gossip network is normally
            // sub-second, but background loops can be CPU-starved on a loaded CI runner.
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return;
                }
                await Task.Delay(25);
            }
            Assert.Fail(message);
        }

        private static readonly string[] s_sessionKeys = ["session/a", "session/b"];
    }
}