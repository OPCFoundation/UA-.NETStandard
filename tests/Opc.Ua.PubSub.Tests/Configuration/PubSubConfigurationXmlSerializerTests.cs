// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
                    [],
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
