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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Distributed.Crdt.Tests
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
                await transport.DisposeAsync();
                if (network != null)
                {
                    await network.DisposeAsync();
                }
            }
        }

        [Test]
        public async Task UseUdpGossipConfiguresTransportFactoryAsync()
        {
            var options = new ReplicatedSessionOptions();
            options.AddPeer(new IPEndPoint(IPAddress.Loopback, 4998));
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
                await transport.DisposeAsync();
                if (network != null)
                {
                    await network.DisposeAsync();
                }
            }
        }

        [Test]
        public void CreateTransportDefaultsToInProcessNetwork()
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
                transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
                network!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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
    }
}
