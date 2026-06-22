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
using Opc.Ua.PubSub.Tests;

namespace Opc.Ua.PubSub.Mqtt.Tests
{
    /// <summary>
    /// Validates <see cref="MqttEndpointParser"/> against the
    /// <c>mqtt://</c> / <c>mqtts://</c> address shapes used by
    /// Part 14 §7.3.4 broker mappings.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.4.4")]
    public sealed class MqttEndpointParserTests
    {
        [Test]
        public void Parse_MqttScheme_DefaultPortIs1883()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com");
            Assert.That(endpoint.Host, Is.EqualTo("broker.example.com"));
            Assert.That(endpoint.Port, Is.EqualTo(1883));
            Assert.That(endpoint.UseTls, Is.False);
        }

        [Test]
        public void Parse_MqttsScheme_DefaultPortIs8883_TlsIsTrue()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtts://broker.example.com");
            Assert.That(endpoint.Host, Is.EqualTo("broker.example.com"));
            Assert.That(endpoint.Port, Is.EqualTo(8883));
            Assert.That(endpoint.UseTls, Is.True);
        }

        [Test]
        public void Parse_WssScheme_DefaultPortIs443_TlsIsTrue()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("wss://broker.example.com");
            Assert.That(endpoint.Host, Is.EqualTo("broker.example.com"));
            Assert.That(endpoint.Port, Is.EqualTo(443));
            Assert.That(endpoint.UseTls, Is.True);
        }

        [Test]
        public void Parse_ExplicitPort_OverridesDefault()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:9999");
            Assert.That(endpoint.Port, Is.EqualTo(9999));
        }

        [Test]
        public void Parse_Ipv4Host_PreservesHost()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://127.0.0.1:1883");
            Assert.That(endpoint.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(endpoint.Port, Is.EqualTo(1883));
        }

        [Test]
        public void Parse_Ipv6Host_PreservesBracketedHost()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://[::1]:1883");
            Assert.That(endpoint.Host, Does.Contain(":"));
            Assert.That(endpoint.Port, Is.EqualTo(1883));
        }

        [Test]
        [TestCase("http://broker.example.com")]
        [TestCase("ftp://broker.example.com")]
        [TestCase("mqtt:/missing-slash")]
        [TestCase("notaurl")]
        [TestCase("")]
        public void Parse_InvalidScheme_ThrowsFormatException(string url)
        {
            Assert.That(
                () => MqttEndpointParser.Parse(url),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Parse_NullUrl_ThrowsArgumentNullException()
        {
            Assert.That(
                () => MqttEndpointParser.Parse(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Parse_EmptyUrl_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse(string.Empty),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Parse_PortOutOfRange_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://broker:70000"),
                Throws.InstanceOf<Exception>());
        }

        [Test]
        public void Parse_Ipv6Host_DefaultPort()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://[::1]");
            Assert.That(endpoint.Port, Is.EqualTo(1883));
            Assert.That(endpoint.UseTls, Is.False);
        }

        [Test]
        public void Parse_Ipv6Host_UnterminatedBracket_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://[::1"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_Ipv6Host_EmptyBrackets_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://[]:1883"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_Ipv6Host_UnexpectedCharAfterBracket_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://[::1]x"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_EmptyPortComponent_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://broker:"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_ZeroPort_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://broker:0"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_NonNumericPort_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://broker:abc"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_MissingHostBeforePort_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt://:1883"),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void Parse_PathSuffix_IsStripped()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("mqtt://broker.example.com:1883/topic");
            Assert.That(endpoint.Host, Is.EqualTo("broker.example.com"));
            Assert.That(endpoint.Port, Is.EqualTo(1883));
        }

        [Test]
        public void Parse_SchemeCaseInsensitive()
        {
            MqttEndpoint endpoint = MqttEndpointParser.Parse("MQTTS://broker.example.com:8883");
            Assert.That(endpoint.UseTls, Is.True);
        }

        [Test]
        public void Parse_EmptyAuthority_Throws()
        {
            Assert.That(
                () => MqttEndpointParser.Parse("mqtt:///some/path"),
                Throws.TypeOf<FormatException>());
        }
    }
}
