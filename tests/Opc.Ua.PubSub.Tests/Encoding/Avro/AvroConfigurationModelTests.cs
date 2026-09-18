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
    /// Verifies the Part 14 configuration model additions for the Avro message mapping (§9): the
    /// NodeSet loads, the DataTypes subtype the correct Part 14 bases, and the C# constants and
    /// content masks agree with the model.
    /// </summary>
    [TestFixture]
    public sealed class AvroConfigurationModelTests
    {
        private const string Ns = "{http://opcfoundation.org/UA/2011/03/UANodeSet.xsd}";

        private static XDocument LoadNodeSet()
        {
            Assembly assembly = typeof(AvroWellKnown).Assembly;
            const string name = "Opc.Ua.PubSub.Encoding.Avro.Opc.Ua.Avro.NodeSet2.xml";
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
            XElement reference = node.Element(Ns + "References")!
                .Elements(Ns + "Reference")
                .First(e => (string?)e.Attribute("ReferenceType") == "HasSubtype"
                    && (string?)e.Attribute("IsForward") == "false");
            return reference.Value;
        }

        [Test]
        public void TheNodeSetShipsAsAnEmbeddedResourceAndParses()
        {
            XDocument document = LoadNodeSet();
            Assert.That(document.Root!.Name.LocalName, Is.EqualTo("UANodeSet"));
        }

        [Test]
        public void TheModelDeclaresTheAvroNamespace()
        {
            XDocument document = LoadNodeSet();
            string uri = document.Root!
                .Element(Ns + "Models")!
                .Element(Ns + "Model")!
                .Attribute("ModelUri")!.Value;

            Assert.That(uri, Is.EqualTo(AvroWellKnown.AvroNamespaceUri));
        }

        [TestCase("1:AvroWriterGroupMessageDataType", "i=15616")]
        [TestCase("1:AvroDataSetWriterMessageDataType", "i=15605")]
        [TestCase("1:AvroDataSetReaderMessageDataType", "i=15629")]
        public void MessageSettingsDataTypesSubtypeThePart14Bases(string browseName, string expectedBase)
        {
            // Selecting the Avro mapping works only because each of these subtypes the abstract
            // Part 14 MessageSettings base, exactly as the UADP and JSON mappings do.
            XDocument document = LoadNodeSet();
            Assert.That(SubtypeOf(Node(document, "UADataType", browseName)), Is.EqualTo(expectedBase));
        }

        [TestCase("1:AvroWriterGroupMessageType", "i=17998")]
        [TestCase("1:AvroDataSetWriterMessageType", "i=21096")]
        [TestCase("1:AvroDataSetReaderMessageType", "i=21104")]
        public void ObjectTypesSubtypeThePart14Bases(string browseName, string expectedBase)
        {
            XDocument document = LoadNodeSet();
            Assert.That(SubtypeOf(Node(document, "UAObjectType", browseName)), Is.EqualTo(expectedBase));
        }

        [TestCase("1:AvroNetworkMessageContentMask")]
        [TestCase("1:AvroDataSetMessageContentMask")]
        public void ContentMasksAreOptionSetsOverUInt32(string browseName)
        {
            XDocument document = LoadNodeSet();
            XElement node = Node(document, "UADataType", browseName);

            Assert.That(SubtypeOf(node), Is.EqualTo("i=7"), "an OptionSet mask subtypes UInt32");
            Assert.That(
                (string?)node.Element(Ns + "Definition")!.Attribute("IsOptionSet"),
                Is.EqualTo("true"));
        }

        [Test]
        public void NetworkMessageContentMaskBitsMatchTheEnum()
        {
            // The model and the code must not drift: a bit that means one thing in the NodeSet and
            // another in the enum would be configured correctly and behave incorrectly.
            XDocument document = LoadNodeSet();
            foreach (XElement field in Node(document, "UADataType", "1:AvroNetworkMessageContentMask")
                .Element(Ns + "Definition")!.Elements(Ns + "Field"))
            {
                string name = (string)field.Attribute("Name")!;
                int bit = int.Parse((string)field.Attribute("Value")!, CultureInfo.InvariantCulture);
                var expected = (AvroNetworkMessageContentMask)(1u << bit);
                Assert.That(
                    // CA2263: Enum.Parse<T> does not exist on net48, which this test project also
                    // targets, so the non-generic overload with a cast is required here.
#pragma warning disable CA2263
                    (AvroNetworkMessageContentMask)Enum.Parse(typeof(AvroNetworkMessageContentMask), name),
#pragma warning restore CA2263
                    Is.EqualTo(expected),
                    $"bit {bit} ({name})");
            }
        }

        [Test]
        public void DataSetMessageContentMaskBitsMatchTheEnum()
        {
            XDocument document = LoadNodeSet();
            foreach (XElement field in Node(document, "UADataType", "1:AvroDataSetMessageContentMask")
                .Element(Ns + "Definition")!.Elements(Ns + "Field"))
            {
                string name = (string)field.Attribute("Name")!;
                int bit = int.Parse((string)field.Attribute("Value")!, CultureInfo.InvariantCulture);
                var expected = (AvroDataSetMessageContentMask)(1u << bit);
                Assert.That(
                    // CA2263: Enum.Parse<T> does not exist on net48, which this test project also
                    // targets, so the non-generic overload with a cast is required here.
#pragma warning disable CA2263
                    (AvroDataSetMessageContentMask)Enum.Parse(typeof(AvroDataSetMessageContentMask), name),
#pragma warning restore CA2263
                    Is.EqualTo(expected),
                    $"bit {bit} ({name})");
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

            Check("UADataType", "1:AvroNetworkMessageContentMask", AvroWellKnown.AvroNetworkMessageContentMask);
            Check("UADataType", "1:AvroDataSetMessageContentMask", AvroWellKnown.AvroDataSetMessageContentMask);
            Check("UADataType", "1:AvroWriterGroupMessageDataType", AvroWellKnown.AvroWriterGroupMessageDataType);
            Check("UADataType", "1:AvroDataSetWriterMessageDataType", AvroWellKnown.AvroDataSetWriterMessageDataType);
            Check("UADataType", "1:AvroDataSetReaderMessageDataType", AvroWellKnown.AvroDataSetReaderMessageDataType);
            Check("UAObjectType", "1:AvroWriterGroupMessageType", AvroWellKnown.AvroWriterGroupMessageType);
            Check("UAObjectType", "1:AvroDataSetWriterMessageType", AvroWellKnown.AvroDataSetWriterMessageType);
            Check("UAObjectType", "1:AvroDataSetReaderMessageType", AvroWellKnown.AvroDataSetReaderMessageType);
        }

        [Test]
        public void TheModelAddsNoDataTypeEncodingObject()
        {
            // §4.2: "Default Avro" has no AddressSpace representation, so this model must not add a
            // DataTypeEncoding Object or a HasEncoding reference. Adding one would contradict the
            // specification and mislead a client into resolving payloads by encoding NodeId rather
            // than by SchemaId.
            XDocument document = LoadNodeSet();

            Assert.That(
                document.Descendants().Any(e => (string?)e.Attribute("ReferenceType") == "HasEncoding"),
                Is.False,
                "no HasEncoding reference should be declared");
            Assert.That(
                document.Descendants(Ns + "UAObject")
                    .Any(e => ((string?)e.Attribute("BrowseName"))?.Contains("Default Avro", StringComparison.Ordinal) == true),
                Is.False,
                "no Default Avro encoding Object should be declared");
        }
    }
}
