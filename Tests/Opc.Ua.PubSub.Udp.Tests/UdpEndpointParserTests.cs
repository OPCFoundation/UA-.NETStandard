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
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Udp.Tests
{
    /// <summary>
    /// Validates the <c>opc.udp://</c> URL parser produced by
    /// <see cref="UdpEndpointParser"/> for the full address matrix defined by
    /// Part 14 §7.3.2.2 (multicast / broadcast) and §7.3.2.3 (unicast).
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.2.2")]
    [TestSpec("7.3.2.3")]
    public sealed class UdpEndpointParserTests
    {
        [Test]
        public void Parse_DefaultPort_AssignsSpecPort()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://224.0.0.1");
            Assert.That(endpoint.Port, Is.EqualTo(UdpEndpointParser.DefaultPort));
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));
            Assert.That(endpoint.IsValid, Is.True);
            Assert.That(endpoint.OriginalUrl, Is.EqualTo("opc.udp://224.0.0.1"));
        }

        [Test]
        public void Parse_Ipv4Multicast_ClassifiedAsMulticast()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://239.255.0.1:5000");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));
            Assert.That(endpoint.Port, Is.EqualTo(5000));
            Assert.That(endpoint.Address.AddressFamily, Is.EqualTo(AddressFamily.InterNetwork));
        }

        [Test]
        public void Parse_Ipv4LimitedBroadcast_ClassifiedAsBroadcast()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://255.255.255.255:4840");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Broadcast));
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Broadcast));
        }

        [Test]
        public void Parse_Ipv4SubnetBroadcast_ClassifiedAsSubnetBroadcast()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://192.168.1.255:4840");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.SubnetBroadcast));
        }

        [Test]
        public void Parse_Ipv4Unicast_ClassifiedAsUnicast()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://127.0.0.1:4841");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Unicast));
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
        }

        [Test]
        public void Parse_LocalhostHostname_ResolvesToLoopback()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://localhost:5050");
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
            Assert.That(endpoint.Port, Is.EqualTo(5050));
        }

        [Test]
        public void Parse_LocalhostHostnameCaseInsensitive()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("OPC.UDP://Localhost");
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.Loopback));
            Assert.That(endpoint.Port, Is.EqualTo(UdpEndpointParser.DefaultPort));
        }

        [Test]
        public void Parse_Ipv6Literal_ResolvesIPv6Address()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://[::1]:4840");
            Assert.That(endpoint.Address.AddressFamily, Is.EqualTo(AddressFamily.InterNetworkV6));
            Assert.That(endpoint.Address, Is.EqualTo(IPAddress.IPv6Loopback));
            Assert.That(endpoint.Port, Is.EqualTo(4840));
        }

        [Test]
        public void Parse_Ipv6Multicast_ClassifiedAsMulticast()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://[ff02::1]:4840");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));
            Assert.That(endpoint.Address.IsIPv6Multicast, Is.True);
        }

        [Test]
        public void Parse_PathSuffix_Ignored()
        {
            UdpEndpoint endpoint = UdpEndpointParser.Parse("opc.udp://239.0.0.1:4840/some/path");
            Assert.That(endpoint.AddressType, Is.EqualTo(UdpAddressType.Multicast));
            Assert.That(endpoint.Port, Is.EqualTo(4840));
        }

        [Test]
        public void Parse_NullUrl_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Parse_EmptyUrl_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse(string.Empty),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_WrongScheme_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("mqtt://broker:1883"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_MissingHost_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_OnlySlashAfterScheme_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp:///path"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_OnlyColon_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://:4840"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_PortZero_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://192.168.0.1:0"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_PortTooLarge_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://192.168.0.1:70000"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_PortNonNumeric_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://192.168.0.1:abc"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_MissingPortAfterColon_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://192.168.0.1:"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_Ipv6Unterminated_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://[::1:4840"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_Ipv6EmptyLiteral_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://[]:4840"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_Ipv6UnexpectedCharAfterBracket_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://[::1]x4840"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_UnknownHost_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.Parse("opc.udp://this-host-does-not-exist.invalid"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Classify_NullAddress_Throws()
        {
            Assert.That(
                () => UdpEndpointParser.ClassifyAddress(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Classify_Ipv6Unicast_ReturnsUnicast()
        {
            Assert.That(
                UdpEndpointParser.ClassifyAddress(IPAddress.IPv6Loopback),
                Is.EqualTo(UdpAddressType.Unicast));
        }

        [Test]
        public void Endpoint_IsValid_FalseWhenPortOutOfRange()
        {
            var endpoint = new UdpEndpoint(IPAddress.Loopback, 0, UdpAddressType.Unicast, null);
            Assert.That(endpoint.IsValid, Is.False);
        }

        [Test]
        public void Endpoint_IsValid_FalseWhenAddressNull()
        {
            var endpoint = new UdpEndpoint(null!, 4840, UdpAddressType.Unicast, null);
            Assert.That(endpoint.IsValid, Is.False);
        }
    }
}
