/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Encoding.Tests
{
    /// <summary>
    /// Verifies the Part 14 configuration model additions for the Arrow message mapping (§6.5).
    /// </summary>
    [TestFixture]
    public sealed class ArrowConfigurationModelTests
    {
        private const string Ns = "{http://opcfoundation.org/UA/2011/03/UANodeSet.xsd}";

        private static XDocument LoadNodeSet()
        {
            Assembly assembly = typeof(ArrowWellKnown).Assembly;
            const string name = "Opc.Ua.PubSub.Encoding.Arrow.Opc.Ua.Arrow.NodeSet2.xml";
            using Stream? stream = assembly.GetManifestResourceStream(name);
            Assert.That(stream, Is.Not.Null, $"embedded resource '{name}' should ship with the assembly");
            return XDocument.Load(stream!);
        }

        private static XElement Node(XDocument document, string element, string browseName)
        {
            XElement? node = document.Root!
                .Elements(Ns + element)
                .FirstOrDefault(e => (string?)e.Attribute("BrowseName") == browseName);
            Assert.That(node, Is.Not.Null, $"{element} '{browseName}' should exist");
            return node!;
        }

        private static string SubtypeOf(XElement node)
        {
            return node.Element(Ns + "References")!
                .Elements(Ns + "Reference")
                .First(e => (string?)e.Attribute("ReferenceType") == "HasSubtype"
                    && (string?)e.Attribute("IsForward") == "false")
                .Value;
        }

        [Test]
        public void TheModelDeclaresTheArrowNamespace()
        {
            XDocument document = LoadNodeSet();
            string uri = document.Root!
                .Element(Ns + "Models")!
                .Element(Ns + "Model")!
                .Attribute("ModelUri")!.Value;

            Assert.That(uri, Is.EqualTo(ArrowWellKnown.ArrowNamespaceUri));
        }

        [TestCase("1:ArrowWriterGroupMessageDataType", "i=15616")]
        [TestCase("1:ArrowDataSetWriterMessageDataType", "i=15605")]
        [TestCase("1:ArrowDataSetReaderMessageDataType", "i=15629")]
        public void MessageSettingsDataTypesSubtypeThePart14Bases(string browseName, string expectedBase)
        {
            XDocument document = LoadNodeSet();
            Assert.That(SubtypeOf(Node(document, "UADataType", browseName)), Is.EqualTo(expectedBase));
        }

        [TestCase("1:ArrowWriterGroupMessageType", "i=17998")]
        [TestCase("1:ArrowDataSetWriterMessageType", "i=21096")]
        [TestCase("1:ArrowDataSetReaderMessageType", "i=21104")]
        public void ObjectTypesSubtypeThePart14Bases(string browseName, string expectedBase)
        {
            XDocument document = LoadNodeSet();
            Assert.That(SubtypeOf(Node(document, "UAObjectType", browseName)), Is.EqualTo(expectedBase));
        }

        [TestCase("1:ArrowIpcFormatEnum", typeof(ArrowIpcFormat))]
        [TestCase("1:ArrowDeltaFrameModeEnum", typeof(ArrowDeltaFrameMode))]
        [TestCase("1:ArrowCompressionEnum", typeof(ArrowCompression))]
        public void EnumerationValuesMatchTheModel(string browseName, Type enumType)
        {
            // The model and the code must agree on every value, not merely on the member names.
            XDocument document = LoadNodeSet();
            XElement node = Node(document, "UADataType", browseName);
            Assert.That(SubtypeOf(node), Is.EqualTo("i=29"), "an enumeration subtypes Enumeration");

            foreach (XElement field in node.Element(Ns + "Definition")!.Elements(Ns + "Field"))
            {
                string name = (string)field.Attribute("Name")!;
                int value = int.Parse((string)field.Attribute("Value")!, CultureInfo.InvariantCulture);
                Assert.That((int)Enum.Parse(enumType, name), Is.EqualTo(value), $"{browseName}.{name}");
            }
        }

        [Test]
        public void WellKnownNodeIdsMatchTheNodeSet()
        {
            XDocument document = LoadNodeSet();

            void Check(string element, string browseName, uint expected)
            {
                string nodeId = (string)Node(document, element, browseName).Attribute("NodeId")!;
                Assert.That(nodeId, Is.EqualTo("ns=1;i=" + expected.ToString(CultureInfo.InvariantCulture)));
            }

            Check("UADataType", "1:ArrowIpcFormatEnum", ArrowWellKnown.ArrowIpcFormatEnum);
            Check("UADataType", "1:ArrowDeltaFrameModeEnum", ArrowWellKnown.ArrowDeltaFrameModeEnum);
            Check("UADataType", "1:ArrowCompressionEnum", ArrowWellKnown.ArrowCompressionEnum);
            Check("UADataType", "1:ArrowWriterGroupMessageDataType", ArrowWellKnown.ArrowWriterGroupMessageDataType);
            Check("UADataType", "1:ArrowDataSetWriterMessageDataType", ArrowWellKnown.ArrowDataSetWriterMessageDataType);
            Check("UADataType", "1:ArrowDataSetReaderMessageDataType", ArrowWellKnown.ArrowDataSetReaderMessageDataType);
            Check("UAObjectType", "1:ArrowWriterGroupMessageType", ArrowWellKnown.ArrowWriterGroupMessageType);
            Check("UAObjectType", "1:ArrowDataSetWriterMessageType", ArrowWellKnown.ArrowDataSetWriterMessageType);
            Check("UAObjectType", "1:ArrowDataSetReaderMessageType", ArrowWellKnown.ArrowDataSetReaderMessageType);
        }

        [Test]
        public void FramingConversionIsByNameNotByValue()
        {
            // The configuration model numbers Batch = 0 and Stream = 1, while the internal
            // ArrowIpcFraming declares Stream first. A cast between them would compile, run, and
            // select exactly the wrong framing, so the conversion is written out - and asserted.
            int configurationBatch = (int)ArrowIpcFormat.Batch;
            int internalBatch = (int)ArrowIpcFraming.Batch;
            Assert.That(configurationBatch, Is.Not.EqualTo(internalBatch),
                "the two enumerations are numbered differently, which is why a cast is unsafe");

            Assert.That(ArrowMessageSettings.ToFraming(ArrowIpcFormat.Batch), Is.EqualTo(ArrowIpcFraming.Batch));
            Assert.That(ArrowMessageSettings.ToFraming(ArrowIpcFormat.Stream), Is.EqualTo(ArrowIpcFraming.Stream));
            Assert.That(ArrowMessageSettings.FromFraming(ArrowIpcFraming.Batch), Is.EqualTo(ArrowIpcFormat.Batch));
            Assert.That(ArrowMessageSettings.FromFraming(ArrowIpcFraming.Stream), Is.EqualTo(ArrowIpcFormat.Stream));
        }

        [Test]
        public void TheFileFramingIsRejectedRatherThanSilentlyDowngraded()
        {
            // The specification defines File framing, but this encoder does not emit it. Mapping it
            // to Batch would produce a payload the configuration did not ask for.
            Assert.That(
                () => ArrowMessageSettings.ToFraming(ArrowIpcFormat.File),
                Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void DefaultsMatchTheSpecification()
        {
            var settings = new ArrowWriterGroupMessageSettings();

            Assert.That(settings.ArrowIpcFormat, Is.EqualTo(ArrowIpcFormat.Batch), "batch is the default framing");
            Assert.That(settings.DeltaFrameMode, Is.EqualTo(ArrowDeltaFrameMode.NullableColumns),
                "nullable-columns is the default so sparse frames keep one stable SchemaId");
            Assert.That(settings.Compression, Is.EqualTo(ArrowCompression.None));
        }

        [Test]
        public void TheModelAddsNoDataTypeEncodingObject()
        {
            XDocument document = LoadNodeSet();
            Assert.That(
                document.Descendants().Any(e => (string?)e.Attribute("ReferenceType") == "HasEncoding"),
                Is.False);
        }
    }
}
#endif
