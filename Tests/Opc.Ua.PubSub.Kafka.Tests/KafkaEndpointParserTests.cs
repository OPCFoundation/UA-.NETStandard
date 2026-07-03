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

namespace Opc.Ua.PubSub.Kafka.Tests
{
    /// <summary>
    /// Validates Kafka endpoint parsing for Part 14 Annex B.2 broker URLs.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("B.2", Summary = "Kafka endpoint URL parsing")]
    public sealed class KafkaEndpointParserTests
    {
        [Test]
        public void ParseKafkaSchemeUsesDefaultPort()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse("kafka://broker.example.com");

            Assert.That(endpoint.BootstrapServers, Is.EqualTo("broker.example.com:9092"));
            Assert.That(endpoint.UseTls, Is.False);
        }

        [Test]
        public void ParseKafkasSchemeUsesDefaultPortAndTls()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse("kafkas://broker.example.com");

            Assert.That(endpoint.BootstrapServers, Is.EqualTo("broker.example.com:9092"));
            Assert.That(endpoint.UseTls, Is.True);
        }

        [Test]
        public void ParseCommaSeparatedBootstrapListNormalizesEntries()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse(
                "kafka://broker1.example.com:19092, broker2.example.com,broker3.example.com:29092/path");

            Assert.That(
                endpoint.BootstrapServers,
                Is.EqualTo("broker1.example.com:19092,broker2.example.com:9092,broker3.example.com:29092"));
        }

        [Test]
        public void ParseIpv6BootstrapServersPreservesBrackets()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse("kafka://[::1],[2001:db8::1]:19092");

            Assert.That(endpoint.BootstrapServers, Is.EqualTo("[::1]:9092,[2001:db8::1]:19092"));
        }

        [Test]
        public void ParseSchemeIsCaseInsensitive()
        {
            KafkaEndpoint endpoint = KafkaEndpointParser.Parse("KAFKAS://broker.example.com:19092");

            Assert.That(endpoint.UseTls, Is.True);
            Assert.That(endpoint.BootstrapServers, Is.EqualTo("broker.example.com:19092"));
        }

        [Test]
        public void ParseNullUrlThrowsArgumentNullException()
        {
            Assert.That(
                () => KafkaEndpointParser.Parse(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        [TestCase("")]
        [TestCase("http://broker.example.com")]
        [TestCase("kafka:/missing-slash")]
        [TestCase("kafka://")]
        [TestCase("kafka://broker.example.com,")]
        [TestCase("kafka://:9092")]
        [TestCase("kafka://broker.example.com:0")]
        [TestCase("kafka://broker.example.com:70000")]
        [TestCase("kafka://broker.example.com:abc")]
        [TestCase("kafka://[::1")]
        [TestCase("kafka://[]:9092")]
        [TestCase("kafka://[::1]x")]
        public void ParseMalformedUrlThrowsFormatException(string url)
        {
            Assert.That(
                () => KafkaEndpointParser.Parse(url),
                Throws.TypeOf<FormatException>());
        }
    }
}
