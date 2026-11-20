/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#pragma warning disable CA2007

using System;
using System.Net;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Redundancy.Server.Tests
{
    /// <summary>
    /// Unit tests for the replicated gossip options (transport selection,
    /// peers, and decoding limits).
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    public sealed class ReplicatedOptionsTests
    {
        [Test]
        public void DefaultsAreSet()
        {
            var options = new ReplicatedAddressSpaceOptions();

            Assert.That(options.ReplicaId, Is.Not.EqualTo(ReplicaId.Empty));
            Assert.That(options.TimeProvider, Is.SameAs(TimeProvider.System));
            Assert.That(options.TransportFactory, Is.Null);
            Assert.That(options.MaxEntryCount, Is.GreaterThan(0));
            Assert.That(options.MaxPayloadBytes, Is.GreaterThan(0));
        }

        [Test]
        public void SessionOptionsExposeFastReconnectDefault()
        {
            var options = new ReplicatedSessionOptions();

            Assert.That(options.Session, Is.Not.Null);
            Assert.That(options.Session.EnableFastReconnect, Is.False);
        }

        [Test]
        public void CreateReaderOptionsHonorsLimits()
        {
            var options = new ReplicatedAddressSpaceOptions
            {
                MaxEntryCount = 5,
                MaxPayloadBytes = 99
            };

            CrdtReaderOptions reader = options.CreateReaderOptions();

            Assert.That(reader.MaxCollectionCount, Is.EqualTo(5));
            Assert.That(reader.MaxStringBytes, Is.EqualTo(99));
        }

        [Test]
        public async Task UseTcpGossipConfiguresTransportFactoryAsync()
        {
            var options = new ReplicatedAddressSpaceOptions();
            options.AddPeer(new IPEndPoint(IPAddress.Loopback, 4999));
            options.AllowUnauthenticatedGossip = true;
            options.UseTcpGossip(IPAddress.Loopback, 0, TimeSpan.FromMilliseconds(50));

            Assert.That(options.TransportFactory, Is.Not.Null);
            ITransport transport = options.CreateTransport(EmptyServices(), out InMemoryNetwork? network);
            try
            {
                Assert.That(transport, Is.InstanceOf<TcpGossipTransport>());
                Assert.That(network, Is.Null);
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                if (network != null)
                {
                    await network.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task UseUdpGossipConfiguresTransportFactoryAsync()
        {
            var options = new ReplicatedSessionOptions();
            options.AddPeer(new IPEndPoint(IPAddress.Loopback, 4998));
            options.AllowUnauthenticatedGossip = true;
            options.UseUdpGossip(IPAddress.Loopback, 0);

            Assert.That(options.TransportFactory, Is.Not.Null);
            ITransport transport = options.CreateTransport(EmptyServices(), out InMemoryNetwork? network);
            try
            {
                Assert.That(transport, Is.InstanceOf<UdpGossipTransport>());
                Assert.That(network, Is.Null);
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                if (network != null)
                {
                    await network.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateTransportDefaultsToInProcessNetworkAsync()
        {
            var options = new ReplicatedAddressSpaceOptions();

            ITransport transport = options.CreateTransport(EmptyServices(), out InMemoryNetwork? network);
            try
            {
                Assert.That(transport, Is.InstanceOf<InMemoryTransport>());
                Assert.That(network, Is.Not.Null);
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                if (network != null)
                {
                    await network.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public void UnauthenticatedTcpGossipFailsClosedByDefault()
        {
            var options = new ReplicatedAddressSpaceOptions();
            options.UseTcpGossip(IPAddress.Loopback, 0);

            Assert.That(
                () => options.CreateTransport(EmptyServices(), out _),
                Throws.InvalidOperationException
                    .With.Message.Contains(nameof(ReplicatedGossipOptions.AllowUnauthenticatedGossip)));
        }

        [Test]
        public void UnauthenticatedUdpGossipFailsClosedByDefault()
        {
            var options = new ReplicatedAddressSpaceOptions();
            options.UseUdpGossip(IPAddress.Loopback, 0);

            Assert.That(
                () => options.CreateTransport(EmptyServices(), out _),
                Throws.InvalidOperationException
                    .With.Message.Contains(nameof(ReplicatedGossipOptions.AllowUnauthenticatedGossip)));
        }

        [Test]
        public void AddPeerRejectsNull()
        {
            var options = new ReplicatedAddressSpaceOptions();
            Assert.That(() => options.AddPeer(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void UseTcpGossipRejectsNullAddress()
        {
            var options = new ReplicatedAddressSpaceOptions();
            Assert.That(() => options.UseTcpGossip(null!, 0), Throws.ArgumentNullException);
        }

        private static IServiceProvider EmptyServices()
        {
            return Mock.Of<IServiceProvider>();
        }

        [Test]
        public async Task CreateTransportRegistersTcpPeerSinkAsync()
        {
            var options = new ReplicatedAddressSpaceOptions { AllowUnauthenticatedGossip = true };
            options.UseTcpGossip(IPAddress.Loopback, 0);
            var registry = new GossipPeerRegistry();
            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IGossipPeerSink>(registry)
                .BuildServiceProvider();

            ITransport transport = options.CreateTransport(services, out InMemoryNetwork? network);
            try
            {
                Assert.That(transport, Is.InstanceOf<TcpGossipTransport>());
                // Offset 0 (address-space fabric): the sink callback forwards the endpoint unchanged.
                registry.AddPeer(new IPEndPoint(IPAddress.Loopback, 4999));
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                if (network != null)
                {
                    await network.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task CreateTransportRegistersUdpPeerSinkWithOffsetAsync()
        {
            var options = new ReplicatedSessionOptions
            {
                AllowUnauthenticatedGossip = true,
                GossipPeerPortOffset = 1
            };
            options.UseUdpGossip(IPAddress.Loopback, 0);
            var registry = new GossipPeerRegistry();
            await using ServiceProvider services = new ServiceCollection()
                .AddSingleton<IGossipPeerSink>(registry)
                .BuildServiceProvider();

            ITransport transport = options.CreateTransport(services, out InMemoryNetwork? network);
            try
            {
                Assert.That(transport, Is.InstanceOf<UdpGossipTransport>());
                // Offset 1 (session fabric): the sink callback forwards the endpoint at port + 1.
                registry.AddPeer(new IPEndPoint(IPAddress.Loopback, 4999));
            }
            finally
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                if (network != null)
                {
                    await network.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
