/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Eth.Channels;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Eth.Tests
{
    /// <summary>
    /// Lifecycle, loopback round-trip, and discovery tests for
    /// <see cref="EthernetDatagramTransport"/> using the in-memory frame
    /// channel backend.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.3", Summary = "Ethernet datagram transport")]
    [CancelAfter(15000)]
    public sealed class EthernetDatagramTransportTests
    {
        private static EthernetDatagramTransport NewTransport(
            InMemoryEthernetFrameChannelFactory factory,
            string url,
            string name,
            PubSubTransportDirection direction,
            EthTransportOptions? options = null)
        {
            options ??= EthTestHelpers.LoopbackOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            EthEndpoint endpoint = EthEndpointParser.Parse(url);
            IEthernetFrameChannel channel = factory.Create(
                EthTestHelpers.LoopbackParameters(),
                telemetry,
                TimeProvider.System);
            return new EthernetDatagramTransport(
                EthTestHelpers.NewConnection(url, name),
                endpoint,
                direction,
                channel,
                options,
                telemetry,
                TimeProvider.System);
        }

        [Test]
        public async Task OpenCloseCycleSucceeds()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            await transport.OpenAsync();
            Assert.That(transport.IsConnected, Is.True);
            await transport.CloseAsync();
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task OpenTwiceIsIdempotent()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            await transport.OpenAsync();
            await transport.OpenAsync();

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task DoubleCloseIsIdempotent()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            await transport.OpenAsync();
            await transport.CloseAsync();
            await transport.CloseAsync();

            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task StateChangedFiresOnOpenAndClose()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            bool? lastConnected = null;
            transport.StateChanged += (_, e) => lastConnected = e.IsConnected;

            await transport.OpenAsync();
            Assert.That(lastConnected, Is.True);
            await transport.CloseAsync();
            Assert.That(lastConnected, Is.False);
        }

        [Test]
        public async Task SendBeforeOpenThrows()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            Assert.That(
                async () => await transport.SendAsync(EthTestHelpers.MakePayload(10)),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task SendOversizedFrameThrows()
        {
            var options = new EthTransportOptions { MaxFrameSize = 100, ReceiveQueueCapacity = 8 };
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send, options);

            await transport.OpenAsync();

            Assert.That(
                async () => await transport.SendAsync(EthTestHelpers.MakePayload(200)),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task LoopbackRoundTripDeliversPayload()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            const string url = "opc.eth://01-00-5E-7F-00-01";

            await using EthernetDatagramTransport subscriber = NewTransport(
                factory, url, "Sub", PubSubTransportDirection.Receive);
            await using EthernetDatagramTransport publisher = NewTransport(
                factory, url, "Pub", PubSubTransportDirection.Send);

            await subscriber.OpenAsync();
            await publisher.OpenAsync();

            byte[] payload = EthTestHelpers.MakePayload(64);
            await publisher.SendAsync(payload);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5));

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task LoopbackRoundTripPreservesVlanTaggedPayload()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            const string url = "opc.eth://01-00-5E-7F-00-02?vid=7&pcp=4";

            await using EthernetDatagramTransport subscriber = NewTransport(
                factory, url, "Sub", PubSubTransportDirection.Receive);
            await using EthernetDatagramTransport publisher = NewTransport(
                factory, url, "Pub", PubSubTransportDirection.Send);

            await subscriber.OpenAsync();
            await publisher.OpenAsync();

            byte[] payload = EthTestHelpers.MakePayload(80);
            await publisher.SendAsync(payload);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5));

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(payload));
        }

        [Test]
        public async Task DiscoveryAnnouncementIsDelivered()
        {
            var options = new EthTransportOptions
            {
                ReceiveQueueCapacity = 16,
                MaxFrameSize = 1500,
                DiscoveryAnnounceRate = 2000,
                DiscoveryMulticastAddress = "01-1B-19-00-00-00"
            };
            var factory = new InMemoryEthernetFrameChannelFactory();
            const string url = "opc.eth://01-00-5E-7F-00-03";

            await using EthernetDatagramTransport subscriber = NewTransport(
                factory, url, "Sub", PubSubTransportDirection.Receive, options);
            await using EthernetDatagramTransport publisher = NewTransport(
                factory, url, "Pub", PubSubTransportDirection.Send, options);

            await subscriber.OpenAsync();
            await publisher.OpenAsync();

            Assert.That(publisher.DiscoveryAnnounceRate, Is.EqualTo(2000u));

            byte[] announcement = EthTestHelpers.MakePayload(48);
            await publisher.SendDiscoveryAnnouncementAsync(announcement);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5));

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(announcement));
        }
    }
}
