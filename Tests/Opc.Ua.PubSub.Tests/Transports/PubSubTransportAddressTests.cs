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
using NUnit.Framework;
using Opc.Ua.PubSub.Transports;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Coverage for <see cref="PubSubTransportAddress"/>: the
    /// dedicated PubSub URL parser that fronts every transport
    /// implementation per Part 14 §7.3.2 / §7.3.4.
    /// </summary>
    [TestFixture]
    [TestSpec("7.3.2", Summary = "PubSub UDP address parsing")]
    [TestSpec("7.3.4", Summary = "PubSub MQTT broker addressing")]
    public class PubSubTransportAddressTests
    {
        [Test]
        public void ConstructorRejectsNullScheme()
        {
            Assert.That(
                () => new PubSubTransportAddress(scheme: null!, host: "h", port: 1, path: null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsEmptyScheme()
        {
            Assert.That(
                () => new PubSubTransportAddress(scheme: string.Empty, host: "h", port: 1, path: null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ConstructorRejectsNullHost()
        {
            Assert.That(
                () => new PubSubTransportAddress(scheme: "opc.udp", host: null!, port: 1, path: null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ConstructorRejectsEmptyHost()
        {
            Assert.That(
                () => new PubSubTransportAddress(scheme: "opc.udp", host: string.Empty, port: 1, path: null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ConstructorAssignsAllFields()
        {
            var addr = new PubSubTransportAddress(
                scheme: "opc.udp", host: "1.2.3.4", port: 4840, path: "/x");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Scheme, Is.EqualTo("opc.udp"));
                Assert.That(addr.Host, Is.EqualTo("1.2.3.4"));
                Assert.That(addr.Port, Is.EqualTo(4840));
                Assert.That(addr.Path, Is.EqualTo("/x"));
            });
        }

        [Test]
        public void ParseUdpUnicastReturnsAllFields()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "opc.udp://192.168.0.1:4840");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Scheme, Is.EqualTo("opc.udp"));
                Assert.That(addr.Host, Is.EqualTo("192.168.0.1"));
                Assert.That(addr.Port, Is.EqualTo(4840));
                Assert.That(addr.Path, Is.Null);
            });
        }

        [Test]
        public void ParseUdpMulticastReturnsAllFields()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "opc.udp://224.0.0.22:4840");
            Assert.That(addr.Host, Is.EqualTo("224.0.0.22"));
            Assert.That(addr.Port, Is.EqualTo(4840));
        }

        [Test]
        public void ParseMqttsAcceptsTlsScheme()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "mqtts://broker.example.com:8883");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Scheme, Is.EqualTo("mqtts"));
                Assert.That(addr.Host, Is.EqualTo("broker.example.com"));
                Assert.That(addr.Port, Is.EqualTo(8883));
            });
        }

        [Test]
        public void ParseMqttWithPathExtractsPath()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "mqtt://broker.example.com:1883/some/topic");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Scheme, Is.EqualTo("mqtt"));
                Assert.That(addr.Host, Is.EqualTo("broker.example.com"));
                Assert.That(addr.Port, Is.EqualTo(1883));
                Assert.That(addr.Path, Is.EqualTo("/some/topic"));
            });
        }

        [Test]
        public void ParseHostNoPortYieldsZeroPort()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "opc.udp://hostname");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Host, Is.EqualTo("hostname"));
                Assert.That(addr.Port, Is.Zero);
                Assert.That(addr.Path, Is.Null);
            });
        }

        [Test]
        public void ParseIpv6Literal()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "opc.udp://[::1]:4840");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Host, Is.EqualTo("::1"));
                Assert.That(addr.Port, Is.EqualTo(4840));
            });
        }

        [Test]
        public void ParseIpv6LiteralWithoutPort()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "opc.udp://[fe80::1]");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Host, Is.EqualTo("fe80::1"));
                Assert.That(addr.Port, Is.Zero);
            });
        }

        [Test]
        public void ParseIpv6LiteralWithPathPreservesPath()
        {
            PubSubTransportAddress addr = PubSubTransportAddress.Parse(
                "mqtts://[2001:db8::1]:8883/foo");
            Assert.Multiple(() =>
            {
                Assert.That(addr.Host, Is.EqualTo("2001:db8::1"));
                Assert.That(addr.Port, Is.EqualTo(8883));
                Assert.That(addr.Path, Is.EqualTo("/foo"));
            });
        }

        [Test]
        public void ParseNullThrowsArgumentNullException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseEmptyThrowsArgumentException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse(string.Empty),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ParseMissingSchemeSeparatorThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("noscheme:thing"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseMissingHostThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseEmptyHostWithPathThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp:///path"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseUnterminatedIpv6LiteralThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://[::1"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseIpv6FollowedByGarbageThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://[::1]x4840"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseInvalidPortThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://h:notaport"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParseNegativePortThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://h:-1"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ParsePortAboveMaxThrowsFormatException()
        {
            Assert.That(
                () => PubSubTransportAddress.Parse("opc.udp://h:65536"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void ToStringRoundTripsUdpUnicast()
        {
            var addr = new PubSubTransportAddress("opc.udp", "1.2.3.4", 4840);
            Assert.That(addr.ToString(), Is.EqualTo("opc.udp://1.2.3.4:4840"));
        }

        [Test]
        public void ToStringRoundTripsMqttWithPath()
        {
            var addr = new PubSubTransportAddress("mqtt", "broker.example.com", 1883, "/x");
            Assert.That(addr.ToString(),
                Is.EqualTo("mqtt://broker.example.com:1883/x"));
        }

        [Test]
        public void ToStringWithoutPortOmitsColon()
        {
            var addr = new PubSubTransportAddress("opc.udp", "host", 0);
            Assert.That(addr.ToString(), Is.EqualTo("opc.udp://host"));
        }

        [Test]
        public void ToStringIpv6LiteralWrapsBrackets()
        {
            var addr = new PubSubTransportAddress("opc.udp", "::1", 4840);
            Assert.That(addr.ToString(), Is.EqualTo("opc.udp://[::1]:4840"));
        }

        [Test]
        public void RoundTripParseEmitsParseableString()
        {
            const string url = "mqtts://broker.example.com:8883/topic/path";
            PubSubTransportAddress first = PubSubTransportAddress.Parse(url);
            PubSubTransportAddress second = PubSubTransportAddress.Parse(first.ToString());
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void EqualityHonoursAllFields()
        {
            var a = new PubSubTransportAddress("opc.udp", "h", 1, "/p");
            var b = new PubSubTransportAddress("opc.udp", "h", 1, "/p");
            var c = new PubSubTransportAddress("opc.udp", "h", 2, "/p");
            Assert.Multiple(() =>
            {
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a, Is.Not.EqualTo(c));
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            });
        }
    }
}
