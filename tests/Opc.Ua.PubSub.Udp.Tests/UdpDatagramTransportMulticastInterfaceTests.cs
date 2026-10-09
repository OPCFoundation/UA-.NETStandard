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
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Verifies that a multicast <see cref="UdpDatagramTransport"/> sends on the
    /// network interface that it joins the multicast group on, instead of on the
    /// operating system's default multicast interface.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [TestSpec("7.3.2.2")]
    [CancelAfter(10000)]
    public sealed class UdpDatagramTransportMulticastInterfaceTests
    {
        [Test]
        public async Task MulticastSenderUsesResolvedIPv4Interface()
        {
            NetworkInterface? networkInterface = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            IPAddress? interfaceAddress = GetFirstIPv4Address(networkInterface);
            if (interfaceAddress is null)
            {
                Assert.Ignore("No network interface with an IPv4 address is available.");
                return;
            }

            int port = ReservePortOrIgnore(IPAddress.Any);
            string url = $"opc.udp://239.255.43.{(port % 250) + 1}:{port}";
            await using UdpDatagramTransport publisher = CreateTransport(
                url,
                PubSubTransportDirection.Send,
                networkInterface);
            await OpenOrIgnoreAsync(publisher).ConfigureAwait(false);

            Socket? socket = publisher.InnerSocket;
            Assert.That(socket, Is.Not.Null);
            byte[] egress = socket!.GetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                4);

            Assert.That(new IPAddress(egress), Is.EqualTo(interfaceAddress));
        }

        [Test]
        public async Task MulticastSenderUsesResolvedIPv6Interface()
        {
            if (!Socket.OSSupportsIPv6)
            {
                Assert.Ignore("IPv6 sockets are not supported on this host.");
                return;
            }
            NetworkInterface? networkInterface = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetworkV6);
            int interfaceIndex = GetIPv6InterfaceIndex(networkInterface);
            if (interfaceIndex == 0)
            {
                Assert.Ignore("No network interface with IPv6 is available.");
                return;
            }

            int port = ReservePortOrIgnore(IPAddress.IPv6Any);
            string url = $"opc.udp://[ff15::4f50:{port:x}]:{port}";
            await using UdpDatagramTransport publisher = CreateTransport(
                url,
                PubSubTransportDirection.Send,
                networkInterface);
            await OpenOrIgnoreAsync(publisher).ConfigureAwait(false);

            Socket? socket = publisher.InnerSocket;
            Assert.That(socket, Is.Not.Null);
            byte[] egress = socket!.GetSocketOption(
                SocketOptionLevel.IPv6,
                SocketOptionName.MulticastInterface,
                4);

            Assert.That(BitConverter.ToInt32(egress, 0), Is.EqualTo(interfaceIndex));
        }

        /// <summary>
        /// Publisher and subscriber both use the loopback interface, which is not the
        /// default multicast interface on a host with a network connection. On Linux and
        /// macOS the subscriber, which joins the group on loopback, receives only the
        /// datagrams that the publisher sends on loopback. Windows also delivers local
        /// copies of datagrams sent on other interfaces, so there the test only confirms
        /// the exchange.
        /// </summary>
        [Test]
        public async Task MulticastExchangeOnNonDefaultInterfaceDeliversPayload()
        {
            NetworkInterface? loopback = UdpNetworkInterfaceResolver.Resolve(
                IPAddress.Loopback.ToString(),
                AddressFamily.InterNetwork);
            if (!IPAddress.Loopback.Equals(GetFirstIPv4Address(loopback)))
            {
                Assert.Ignore("No loopback interface with an IPv4 address is available.");
                return;
            }

            int port = ReservePortOrIgnore(IPAddress.Any);
            var group = IPAddress.Parse($"239.255.44.{(port % 250) + 1}");
            string url = $"opc.udp://{group}:{port}";
            await using UdpDatagramTransport subscriber = CreateTransport(
                url,
                PubSubTransportDirection.Receive,
                loopback);
            await using UdpDatagramTransport publisher = CreateTransport(
                url,
                PubSubTransportDirection.Send,
                loopback);
            await OpenOrIgnoreAsync(subscriber).ConfigureAwait(false);
            await OpenOrIgnoreAsync(publisher).ConfigureAwait(false);

            byte[] payload = [0x4F, 0x50, 0x43, 0x55];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await publisher.SendAsync(payload).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    break;
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

            // Tell a host that cannot deliver multicast on loopback apart from a
            // publisher that sends on another interface: a control sender selects
            // the loopback interface itself.
            using var control = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                control.SetSocketOption(
                    SocketOptionLevel.IP,
                    SocketOptionName.MulticastInterface,
                    IPAddress.Loopback.GetAddressBytes());
                control.MulticastLoopback = true;
                await control.SendToAsync(
                    new ArraySegment<byte>(payload),
                    SocketFlags.None,
                    new IPEndPoint(group, port)).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"The host cannot send multicast on the loopback interface: {ex.Message}");
                return;
            }
            PubSubTransportFrame? controlFrame = await UdpIntegrationTestHelpers.ReceiveOneAsync(
                subscriber,
                TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            if (controlFrame is null)
            {
                Assert.Ignore("The host does not deliver multicast on the loopback interface.");
                return;
            }
            Assert.Fail(
                "A sender on the loopback interface reached the subscriber, " +
                "but the publisher's multicast did not.");
        }

        private static UdpDatagramTransport CreateTransport(
            string url,
            PubSubTransportDirection direction,
            NetworkInterface? networkInterface)
        {
            return new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, direction.ToString()),
                UdpEndpointParser.Parse(url),
                direction,
                networkInterface,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
        }

        private static async Task OpenOrIgnoreAsync(UdpDatagramTransport transport)
        {
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Opening the multicast transport failed: {ex.Message}");
            }
        }

        private static int ReservePortOrIgnore(IPAddress bindAddress)
        {
            try
            {
                return UdpIntegrationTestHelpers.ReserveEphemeralPort(bindAddress);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP socket bind failed: {ex.Message}");
                return 0;
            }
        }

        private static IPAddress? GetFirstIPv4Address(NetworkInterface? networkInterface)
        {
            if (networkInterface is null)
            {
                return null;
            }
            foreach (UnicastIPAddressInformation info in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return info.Address;
                }
            }
            return null;
        }

        private static int GetIPv6InterfaceIndex(NetworkInterface? networkInterface)
        {
            if (networkInterface is null)
            {
                return 0;
            }
            IPv6InterfaceProperties? properties = networkInterface.GetIPProperties().GetIPv6Properties();
            return properties?.Index ?? 0;
        }
    }
}
