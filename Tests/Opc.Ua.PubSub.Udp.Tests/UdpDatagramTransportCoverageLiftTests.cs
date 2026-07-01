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
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Targeted coverage for UDP datagram branches that depend on host
    /// networking capabilities and Datagram v2 transport settings.
    /// </summary>
    [TestFixture]
    [TestSpec("6.4.1.2.7")]
    [CancelAfter(10000)]
    public sealed class UdpDatagramTransportCoverageLiftTests
    {
        [Test]
        public async Task DatagramV2QosCategoryIsAppliedDuringOpen()
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

            string url = $"opc.udp://127.0.0.1:{port}";
            PubSubConnectionDataType connection = UdpIntegrationTestHelpers.NewConnection(url, "Qos");
            connection.TransportSettings = new ExtensionObject(new DatagramConnectionTransport2DataType
            {
                DiscoveryAnnounceRate = 250,
                DiscoveryMaxMessageSize = 2048,
                QosCategory = "Reliable"
            });

            await using var transport = new UdpDatagramTransport(
                connection,
                UdpEndpointParser.Parse(url),
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            Assert.Multiple(() =>
            {
                Assert.That(transport.DiscoveryAnnounceRate, Is.EqualTo(250));
                Assert.That(transport.DiscoveryMaxMessageSize, Is.EqualTo(2048));
                Assert.That(transport.QosCategory, Is.EqualTo("Reliable"));
            });

            try
            {
                await transport.OpenAsync();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"UDP open with QosCategory failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public async Task Ipv6LoopbackOpenCoversHopLimitConfiguration()
        {
            if (!Socket.OSSupportsIPv6)
            {
                Assert.Ignore("IPv6 sockets are not supported on this host.");
                return;
            }

            int port;
            try
            {
                port = UdpIntegrationTestHelpers.ReserveEphemeralPort(IPAddress.IPv6Loopback);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"IPv6 loopback UDP socket bind failed: {ex.Message}");
                return;
            }

            string url = $"opc.udp://[::1]:{port}";
            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "IPv6"),
                UdpEndpointParser.Parse(url),
                PubSubTransportDirection.SendReceive,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());

            try
            {
                await transport.OpenAsync();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"IPv6 loopback open failed: {ex.Message}");
                return;
            }

            Assert.That(transport.IsConnected, Is.True);
        }

        [Test]
        public void PrivateNetworkInterfaceFallbacksReturnNeutralValues()
        {
            MethodInfo selectIPv6 = typeof(UdpDatagramTransport).GetMethod(
                "SelectIPv6InterfaceIndex",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo selectIPv4 = typeof(UdpDatagramTransport).GetMethod(
                "SelectLocalIPv4",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            object? ipv6Index = selectIPv6.Invoke(null, [null]);
            object? ipv4Address = selectIPv4.Invoke(null, [null]);

            Assert.Multiple(() =>
            {
                Assert.That(ipv6Index, Is.Zero);
                Assert.That(ipv4Address, Is.Null);
            });
        }

        [Test]
        public void PrivateNetworkInterfaceSelectorsReadAvailableAddresses()
        {
            NetworkInterface? nic = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (nic is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
                return;
            }

            MethodInfo selectIPv4 = typeof(UdpDatagramTransport).GetMethod(
                "SelectLocalIPv4",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            object? address = selectIPv4.Invoke(null, [nic]);

            Assert.That(address, Is.InstanceOf<IPAddress>());
        }
    }
}
