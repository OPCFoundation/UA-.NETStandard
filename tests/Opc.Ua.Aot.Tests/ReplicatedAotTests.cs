// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Net;
using Crdt;
using Crdt.Transport;
using Opc.Ua.Redundancy.Server;

// CA2007: AOT tests run without a SynchronizationContext.
#pragma warning disable CA2007

using Opc.Ua.Redundancy;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests that exercise the CRDT active/active building
    /// blocks, ensuring they are reachable and functional under NativeAOT.
    /// </summary>
    public class ReplicatedAotTests
    {
        [Test]
        public async Task ReplicatedKeyValueStoreRoundTripsUnderAotAsync()
        {
            await using var network = new InMemoryNetwork();
            await using var store = new ReplicatedSharedKeyValueStore(
                ReplicaId.FromUInt64(1),
                network.CreateTransport(),
                TimeProvider.System,
                CrdtReaderOptions.Default);

            var value = new ByteString(new byte[] { 10, 20, 30 });
            await store.SetAsync("session/aot", value).ConfigureAwait(false);

            (bool found, ByteString stored) = await store.TryGetAsync("session/aot").ConfigureAwait(false);

            await Assert.That(found).IsTrue();
            byte[] bytes = stored.ToArray();
            await Assert.That(bytes.Length).IsEqualTo(3);
            await Assert.That(bytes[0]).IsEqualTo((byte)10);
            await Assert.That(bytes[1]).IsEqualTo((byte)20);
            await Assert.That(bytes[2]).IsEqualTo((byte)30);
        }

        [Test]
        public async Task ReplicatedOptionsConfigureTransportUnderAotAsync()
        {
            var options = new ReplicatedAddressSpaceOptions
            {
                ReplicaId = ReplicaId.New(),
                AllowUnauthenticatedGossip = true
            };
            options.AddPeer(new IPEndPoint(IPAddress.Loopback, 4999));
            options.UseUdpGossip(IPAddress.Loopback, 0);

            await Assert.That(options.TransportFactory).IsNotNull();
            await using ITransport transport = options.TransportFactory!(NullServiceProvider.Instance);
            await Assert.That(transport).IsTypeOf<UdpGossipTransport>();
        }

        private sealed class NullServiceProvider : IServiceProvider
        {
            public static NullServiceProvider Instance { get; } = new NullServiceProvider();

            public object? GetService(Type serviceType)
            {
                return null;
            }
        }
    }
}
