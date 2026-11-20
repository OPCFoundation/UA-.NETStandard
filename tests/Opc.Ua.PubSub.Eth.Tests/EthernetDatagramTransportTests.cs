/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

            await transport.OpenAsync().ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.True);
            await transport.CloseAsync().ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task OpenTwiceIsIdempotent()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            await transport.OpenAsync().ConfigureAwait(false);
            await transport.OpenAsync().ConfigureAwait(false);

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task DoubleCloseIsIdempotent()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            await transport.OpenAsync().ConfigureAwait(false);
            await transport.CloseAsync().ConfigureAwait(false);
            await transport.CloseAsync().ConfigureAwait(false);

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

            await transport.OpenAsync().ConfigureAwait(false);
            Assert.That(lastConnected, Is.True);
            await transport.CloseAsync().ConfigureAwait(false);
            Assert.That(lastConnected, Is.False);
        }

        [Test]
        public async Task SendBeforeOpenThrows()
        {
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send);

            Assert.That(
                async () => await transport.SendAsync(EthTestHelpers.MakePayload(10)).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task SendOversizedFrameThrows()
        {
            var options = new EthTransportOptions { MaxFrameSize = 100, ReceiveQueueCapacity = 8 };
            var factory = new InMemoryEthernetFrameChannelFactory();
            await using EthernetDatagramTransport transport = NewTransport(
                factory, "opc.eth://01-00-5E-00-00-01", "Pub", PubSubTransportDirection.Send, options);

            await transport.OpenAsync().ConfigureAwait(false);

            Assert.That(
                async () => await transport.SendAsync(EthTestHelpers.MakePayload(200)).ConfigureAwait(false),
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

            await subscriber.OpenAsync().ConfigureAwait(false);
            await publisher.OpenAsync().ConfigureAwait(false);

            byte[] payload = EthTestHelpers.MakePayload(64);
            await publisher.SendAsync(payload).ConfigureAwait(false);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

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

            await subscriber.OpenAsync().ConfigureAwait(false);
            await publisher.OpenAsync().ConfigureAwait(false);

            byte[] payload = EthTestHelpers.MakePayload(80);
            await publisher.SendAsync(payload).ConfigureAwait(false);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

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

            await subscriber.OpenAsync().ConfigureAwait(false);
            await publisher.OpenAsync().ConfigureAwait(false);

            Assert.That(publisher.DiscoveryAnnounceRate, Is.EqualTo(2000u));

            byte[] announcement = EthTestHelpers.MakePayload(48);
            await publisher.SendDiscoveryAnnouncementAsync(announcement).ConfigureAwait(false);

            PubSubTransportFrame? frame = await EthTestHelpers.ReceiveOneAsync(
                subscriber, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Value.Payload.ToArray(), Is.EqualTo(announcement));
        }
    }
}
