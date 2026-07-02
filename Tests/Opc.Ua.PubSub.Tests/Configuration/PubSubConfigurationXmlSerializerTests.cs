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
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Configuration
{
    /// <summary>
    /// Coverage for the internal
    /// <c>PubSubConfigurationXmlSerializer</c> primitives shared by the
    /// store and tooling: encode produces non-empty UTF-8 XML, decode
    /// recovers an equivalent configuration via both
    /// <see cref="ReadOnlySpan{T}"/> and <see cref="Stream"/> overloads.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.6", Summary = "PubSubConfigurationDataType XML codec")]
    public class PubSubConfigurationXmlSerializerTests
    {
        private static ServiceMessageContext NewContext()
        {
            return ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
        }

        private static PubSubConfigurationDataType NewConfig()
        {
            return new PubSubConfigurationDataType
            {
                Enabled = true,
                PublishedDataSets = new ArrayOf<PublishedDataSetDataType>(
                    new[] { new PublishedDataSetDataType { Name = "DS1" } }),
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[]
                    {
                        new PubSubConnectionDataType
                        {
                            Name = "Conn",
                            Enabled = true,
                            PublisherId = new Variant((ushort)5),
                            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
                            Address = new ExtensionObject(
                                new NetworkAddressUrlDataType
                                {
                                    Url = "opc.udp://239.0.0.1:4840"
                                })
                        }
                    })
            };
        }

        [Test]
        public void EncodeXml_ProducesNonEmptyUtf8XmlDocument()
        {
            IServiceMessageContext ctx = NewContext();
            byte[] xml = PubSubConfigurationXmlSerializer.EncodeXml(NewConfig(), ctx);
            Assert.That(xml, Is.Not.Empty);
            string text = System.Text.Encoding.UTF8.GetString(xml);
            Assert.That(text, Does.Contain("PubSubConfigurationDataType").Or.Contain("Connections"));
        }

        [Test]
        public void EncodeThenDecode_RoundTripPreservesStructure()
        {
            IServiceMessageContext ctx = NewContext();
            PubSubConfigurationDataType original = NewConfig();
            byte[] xml = PubSubConfigurationXmlSerializer.EncodeXml(original, ctx);
            PubSubConfigurationDataType decoded = PubSubConfigurationXmlSerializer.DecodeXml(
                xml,
                ctx);
            Assert.That(decoded.Connections.Count, Is.EqualTo(original.Connections.Count));
            Assert.That(decoded.Connections[0].Name, Is.EqualTo("Conn"));
            Assert.That(
                decoded.Connections[0].TransportProfileUri,
                Is.EqualTo(Profiles.PubSubUdpUadpTransport));
        }

        [Test]
        public void DecodeXml_StreamOverload_ReturnsSameStructure()
        {
            IServiceMessageContext ctx = NewContext();
            PubSubConfigurationDataType original = NewConfig();
            byte[] xml = PubSubConfigurationXmlSerializer.EncodeXml(original, ctx);
            using var memory = new MemoryStream(xml, writable: false);
            PubSubConfigurationDataType decoded = PubSubConfigurationXmlSerializer.DecodeXml(
                memory,
                ctx);
            Assert.That(decoded.Connections.Count, Is.EqualTo(1));
            Assert.That(decoded.Connections[0].Name, Is.EqualTo("Conn"));
        }

        [Test]
        public void EncodeXml_NullConfig_Throws()
        {
            IServiceMessageContext ctx = NewContext();
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationXmlSerializer.EncodeXml(null!, ctx));
        }

        [Test]
        public void EncodeXml_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationXmlSerializer.EncodeXml(NewConfig(), null!));
        }

        [Test]
        public void DecodeXml_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationXmlSerializer.DecodeXml(
                    ReadOnlySpan<byte>.Empty,
                    null!));
        }

        [Test]
        public void DecodeXml_StreamNull_Throws()
        {
            IServiceMessageContext ctx = NewContext();
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationXmlSerializer.DecodeXml(
                    (Stream)null!,
                    ctx));
        }

        [Test]
        public void DecodeXml_StreamWithNullContext_Throws()
        {
            using var memory = new MemoryStream();
            Assert.Throws<ArgumentNullException>(
                () => PubSubConfigurationXmlSerializer.DecodeXml(memory, null!));
        }
    }
}
