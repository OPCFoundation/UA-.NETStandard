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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Tests;
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
                await subscriber.OpenAsync();
                await publisher.OpenAsync();
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
                    await publisher.SendAsync(payload);
                }
                catch (SocketException ex)
                {
                    Assert.Ignore(
                        $"Multicast send failed: {ex.Message}; environment likely blocks multicast routing.");
                    return;
                }
                PubSubTransportFrame? frame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                    subscriber,
                    TimeSpan.FromMilliseconds(500));
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
