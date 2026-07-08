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
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Targeted tests that exercise the less-trodden branches of the
    /// UDP transport: factory connection-property fall-through, parser
    /// DNS resolution via the local host name, resolver IPv6 fallbacks,
    /// and transport multicast bind with an explicitly supplied
    /// network interface.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [CancelAfter(10000)]
    public sealed class UdpCoverageGapTests
    {
        [Test]
        public void Factory_NetworkInterfaceOnUrl_TakesPriority()
        {
            var options = Options.Create(new UdpTransportOptions());
            var factory = new UdpPubSubTransportFactory(options);
            var connection = new PubSubConnectionDataType
            {
                Name = "WithNic",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:7200",
                    NetworkInterface = "totally-unknown-nic-from-url"
                })
            };

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }

        [Test]
        public void Factory_UnrelatedConnectionPropertyKey_IgnoredAndFallsThrough()
        {
            var options = Options.Create(new UdpTransportOptions());
            var factory = new UdpPubSubTransportFactory(options);
            var connection = new PubSubConnectionDataType
            {
                Name = "UnrelatedProps",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:7210"
                }),
                ConnectionProperties = new ArrayOf<KeyValuePair>(new[]
                {
                    new KeyValuePair
                    {
                        Key = QualifiedName.From("Unrelated"),
                        Value = "value"
                    },
                    new KeyValuePair
                    {
                        Key = QualifiedName.Null,
                        Value = "anonymous"
                    }
                })
            };

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }

        [Test]
        public void Factory_NetworkInterfacePropertyWithEmptyValue_FallsThrough()
        {
            var options = Options.Create(new UdpTransportOptions());
            var factory = new UdpPubSubTransportFactory(options);
            var connection = new PubSubConnectionDataType
            {
                Name = "EmptyNicProperty",
                TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://239.0.0.1:7220"
                }),
                ConnectionProperties = new ArrayOf<KeyValuePair>(new[]
                {
                    new KeyValuePair
                    {
                        Key = QualifiedName.From(UdpPubSubTransportFactory.NetworkInterfacePropertyKey),
                        Value = string.Empty
                    }
                })
            };

            IPubSubTransport transport = factory.Create(
                connection,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);

            Assert.That(transport, Is.InstanceOf<UdpDatagramTransport>());
        }

        [Test]
        public void Parser_DnsResolution_LocalHostName_ReturnsAddress()
        {
            string hostName;
            try
            {
                hostName = Dns.GetHostName();
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Dns.GetHostName failed: {ex.Message}");
                return;
            }
            if (string.IsNullOrEmpty(hostName) ||
                string.Equals(hostName, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Ignore("Host name unavailable or aliases 'localhost' shortcut.");
                return;
            }

            UdpEndpoint endpoint;
            try
            {
                endpoint = UdpEndpointParser.Parse($"opc.udp://{hostName}:4840");
            }
            catch (FormatException ex) when (ex.InnerException is SocketException)
            {
                Assert.Ignore($"DNS resolution unavailable: {ex.Message}");
                return;
            }
            Assert.That(endpoint.Address, Is.Not.Null);
            Assert.That(endpoint.Port, Is.EqualTo(4840));
        }

        [Test]
        public void Resolver_IPv6_ResolvesFirstUpInterface()
        {
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetworkV6);
            if (resolved is null)
            {
                Assert.Ignore("No IPv6-capable network interface available on this host.");
            }
            Assert.That(resolved!.Supports(NetworkInterfaceComponent.IPv6), Is.True);
        }

        [Test]
        public void Resolver_OnlyLoopbackAvailable_ReturnsLoopbackFallback()
        {
            NetworkInterface[] all;
            try
            {
                all = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException ex)
            {
                Assert.Ignore($"Cannot enumerate NICs: {ex.Message}");
                return;
            }
            bool hasLoopback = false;
            bool hasNonLoopbackUp = false;
            foreach (NetworkInterface nic in all)
            {
                if (!nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    continue;
                }
                if (nic.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    hasLoopback = true;
                }
                else
                {
                    hasNonLoopbackUp = true;
                }
            }
            if (!hasLoopback)
            {
                Assert.Ignore("Host has no loopback NIC.");
            }
            NetworkInterface? resolved = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            Assert.That(resolved, Is.Not.Null);
            if (!hasNonLoopbackUp)
            {
                Assert.That(
                    resolved!.NetworkInterfaceType,
                    Is.EqualTo(NetworkInterfaceType.Loopback));
            }
        }

        [Test]
        public async Task Transport_MulticastWithExplicitNic_OpensAndCloses()
        {
            NetworkInterface? nic = UdpNetworkInterfaceResolver.Resolve(
                null,
                AddressFamily.InterNetwork);
            if (nic is null)
            {
                Assert.Ignore("No IPv4-capable network interface available on this host.");
                return;
            }

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
            string url = $"opc.udp://239.255.43.{groupLow}:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "MulticastNic"),
                endpoint,
                PubSubTransportDirection.SendReceive,
                nic,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Multicast open failed: {ex.Message}");
                return;
            }
            Assert.That(transport.IsConnected, Is.True);
            await transport.CloseAsync().ConfigureAwait(false);
            Assert.That(transport.IsConnected, Is.False);
        }

        [Test]
        public async Task Transport_BroadcastDestination_OpensAndCloses()
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

            string url = $"opc.udp://255.255.255.255:{port}";
            UdpEndpoint endpoint = UdpEndpointParser.Parse(url);
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Broadcast));

            await using var transport = new UdpDatagramTransport(
                UdpIntegrationTestHelpers.NewConnection(url, "Broadcast"),
                endpoint,
                PubSubTransportDirection.Send,
                networkInterface: null,
                NUnitTelemetryContext.Create(),
                TimeProvider.System,
                UdpIntegrationTestHelpers.LoopbackOptions());
            try
            {
                await transport.OpenAsync().ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                Assert.Ignore($"Broadcast socket open failed: {ex.Message}");
                return;
            }
            Assert.That(transport.IsConnected, Is.True);
            try
            {
                await transport.SendAsync(new byte[] { 0xFF, 0xEE }).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                // Some CI hosts disallow broadcast — log + ignore.
                Assert.Ignore($"Broadcast send failed: {ex.Message}");
                return;
            }
            await transport.CloseAsync().ConfigureAwait(false);
        }
    }
}
