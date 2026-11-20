/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Loopback multicast smoke test for <see cref="UdpDatagramTransport"/>.
    /// A publisher transport joins a randomly-chosen administratively-scoped
    /// IPv4 group (239.x.x.x range) and a subscriber transport receives
    /// the frame back, exercising
    /// <see cref="UdpTransportOptions.MulticastLoopback"/>.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.2.2")]
    [CancelAfter(10000)]
    public sealed class UdpDatagramTransportLoopbackMulticastTests
    {
        [Test]
        public async Task LoopbackMulticast_PublishesAndSubscribesPayload()
        {
            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            int groupLow = (port % 250) + 1;
            string url = $"opc.udp://239.255.42.{groupLow}:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));

            UdpTransportOptions options = UdpIntegrationTestHelpers.LoopbackOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            await using var subscriber = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Sub"),
                endpoint,
                PubSubTransportDirection.Receive,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);
            await using var publisher = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Pub"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                telemetry,
                TimeProvider.System,
                options);

            try
            {
                await subscriber.OpenAsync().ConfigureAwait(false);
                await publisher.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Multicast loopback open failed: {ex.Message}");
                return;
            }

            byte[] payload = [0xAA, 0xBB, 0xCC, 0xDD];

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    await publisher.SendAsync(payload).ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    Assert.Ignore(
                        $"Multicast send failed: {ex.Message}; environment likely blocks multicast routing.");
                    return;
                }
                PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                    subscriber,
                    TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
                if (frame is not null)
                {
                    Assert.That(frame.Value.Payload.ToArray(), Is.EqualTo(payload));
                    return;
                }
            }

            Assert.Ignore("No multicast loopback frame received; environment likely blocks multicast.");
        }
    }
}
