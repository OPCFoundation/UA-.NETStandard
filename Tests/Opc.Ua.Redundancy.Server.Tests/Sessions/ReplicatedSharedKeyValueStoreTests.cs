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
using System.Net;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Tests for the CRDT-backed <see cref="ReplicatedSharedKeyValueStore"/> used to
    /// replicate mirrored session entries active/active.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedSharedKeyValueStoreTests
    {
        [Test]
        public async Task ReplicatesSetAndDeleteAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore storeA = CreateStore(network, 1);
            await using ReplicatedSharedKeyValueStore storeB = CreateStore(network, 2);

            // Warm up both transports so neither misses the other's broadcasts.
            await storeA.TryGetAsync("warmup").ConfigureAwait(false);
            await storeB.TryGetAsync("warmup").ConfigureAwait(false);

            var value = new ByteString(new byte[] { 1, 2, 3, 4 });
            await storeA.SetAsync("session/abc", value).ConfigureAwait(false);

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, ByteString stored) = await storeB.TryGetAsync("session/abc").ConfigureAwait(false);
                    return found && stored.ToArray().AsSpan().SequenceEqual(value.ToArray());
                },
                "a value set on A should replicate to B").ConfigureAwait(false);

            await storeA.DeleteAsync("session/abc").ConfigureAwait(false);

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, _) = await storeB.TryGetAsync("session/abc").ConfigureAwait(false);
                    return !found;
                },
                "a delete on A should replicate to B").ConfigureAwait(false);
        }

        [Test]
        public async Task CompareAndSwapThrowsNotSupportedAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network, 1);

            Assert.That(
                async () => await store.CompareAndSwapAsync("k", default, new ByteString(new byte[] { 1 })).ConfigureAwait(false),
                Throws.TypeOf<NotSupportedException>(),
                "CRDT stores cannot provide a linearizable compare-and-swap");
        }

        [Test]
        public async Task ScanReturnsOnlyEntriesWithMatchingPrefixAsync()
        {
            await using var network = new InMemoryNetwork();
            await using ReplicatedSharedKeyValueStore store = CreateStore(network, 1);

            await store.SetAsync("session/a", new ByteString(new byte[] { 1 })).ConfigureAwait(false);
            await store.SetAsync("session/b", new ByteString(new byte[] { 2 })).ConfigureAwait(false);
            await store.SetAsync("other/c", new ByteString(new byte[] { 3 })).ConfigureAwait(false);

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
            await using ReplicatedSharedKeyValueStore store = CreateStore(network, 1);

            Assert.That(() => store.WatchAsync("session/"), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public async Task ReplicatesOverRealTcpGossipAsync()
        {
            // Regression for the reported runtime failure: SetAsync mirrored through a real
            // TcpGossipTransport threw "Frame length does not match the encoded body length"
            // because the store sent a raw CRDT snapshot instead of a FrameCodec frame. The
            // in-memory transport used by the other tests is a transparent passthrough and
            // hid the bug. This test exercises two real TCP transports over loopback.
            int portA = FreeTcpPort();
            int portB = FreeTcpPort(portA);

            var transportA = new TcpGossipTransport(
                IPAddress.Loopback, portA, TimeSpan.FromMilliseconds(100));
            var transportB = new TcpGossipTransport(
                IPAddress.Loopback, portB, TimeSpan.FromMilliseconds(100));
            transportA.AddPeer(new IPEndPoint(IPAddress.Loopback, portB));
            transportB.AddPeer(new IPEndPoint(IPAddress.Loopback, portA));

            await using var storeA = new ReplicatedSharedKeyValueStore(
                ReplicaId.FromUInt64(1), transportA, TimeProvider.System, CrdtReaderOptions.Default);
            await using var storeB = new ReplicatedSharedKeyValueStore(
                ReplicaId.FromUInt64(2), transportB, TimeProvider.System, CrdtReaderOptions.Default);

            // Warm up both transports so neither misses the other's broadcasts.
            await storeA.TryGetAsync("warmup").ConfigureAwait(false);
            await storeB.TryGetAsync("warmup").ConfigureAwait(false);

            var value = new ByteString(new byte[] { 9, 8, 7, 6 });
            await storeA.SetAsync("session/tcp", value).ConfigureAwait(false);

            await AssertEventuallyAsync(
                async () =>
                {
                    (bool found, ByteString stored) = await storeB.TryGetAsync("session/tcp").ConfigureAwait(false);
                    return found && stored.ToArray().AsSpan().SequenceEqual(value.ToArray());
                },
                "a value set on A should replicate to B over real TCP gossip").ConfigureAwait(false);
        }

        private static ReplicatedSharedKeyValueStore CreateStore(InMemoryNetwork network, ulong replica)
        {
            return new ReplicatedSharedKeyValueStore(
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
                if (await condition().ConfigureAwait(false))
                {
                    return;
                }
                await Task.Delay(25).ConfigureAwait(false);
            }
            Assert.Fail(message);
        }

        private static readonly string[] s_sessionKeys = ["session/a", "session/b"];

        private static int FreeTcpPort(int exclude = -1)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                if (port != exclude)
                {
                    return port;
                }
            }
            throw new InvalidOperationException("Could not find a free TCP port.");
        }
    }
}
