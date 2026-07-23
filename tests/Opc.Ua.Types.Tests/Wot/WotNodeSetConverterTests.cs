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
 *
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
using System.Text;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotNodeSetConverterTests
    {
        [Test]
        public void PreservationEnvelopeRoundTripsCanonicalNodeSet()
        {
            UANodeSet source = CreateNodeSet();

            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(document);

            Assert.That(Write(restored), Is.EqualTo(Write(source)));
            Assert.That(
                document.RootElement.GetProperty("uav:nodeSet").GetProperty("encoding").GetString(),
                Is.EqualTo("base64"));
        }

        [Test]
        public void GeneratedEnvelopeIsDeterministic()
        {
            UANodeSet source = CreateNodeSet();

            using WotDocument first = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());
            using WotDocument second = WotNodeSetConverter.FromNodeSet(
                source,
                options: AlwaysPreserve());

            Assert.That(first.Utf8Json.ToArray(), Is.EqualTo(second.Utf8Json.ToArray()));
        }

        [Test]
        public void WotDocumentPreservesUnknownMembersLexically()
        {
            byte[] json = Encoding.UTF8.GetBytes(
                "{\"@context\":[],\"title\":\"T\",\"vendor:unknown\":{\"b\":2,\"a\":1}}");

            using WotDocument document = WotDocument.Parse(json);
            using var output = new MemoryStream();
            document.Write(output);

            Assert.That(output.ToArray(), Is.EqualTo(json));
        }

        [Test]
        public void DigestMismatchIsRejected()
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                CreateNodeSet(),
                options: AlwaysPreserve());
            string json = Encoding.UTF8.GetString(document.Utf8Json.ToArray());
            const string marker = "\"data\": \"";
            int valueIndex = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            char[] characters = json.ToCharArray();
            characters[valueIndex] = characters[valueIndex] == 'A' ? 'B' : 'A';
            json = new string(characters);

            Assert.Throws<FormatException>(
                () => WotNodeSetConverter.ToNodeSet(Encoding.UTF8.GetBytes(json)));
        }

        private static UANodeSet CreateNodeSet()
        {
            var xml = new XmlDocument();
            System.Xml.XmlElement extension = xml.CreateElement("test", "Metadata", "urn:test");
            extension.SetAttribute("key", "value");
            extension.InnerText = "payload";

            return new UANodeSet
            {
                NamespaceUris = ["urn:test:model"],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = "urn:test:model",
                        Version = "1.0.0",
                        PublicationDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                        PublicationDateSpecified = true
                    }
                ],
                Extensions = [extension],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1001",
                        BrowseName = "1:MachineType",
                        SymbolicName = "MachineType",
                        DisplayName =
                        [
                            new Opc.Ua.Export.LocalizedText
                            {
                                Value = "MachineType"
                            }
                        ],
                        Description =
                        [
                            new Opc.Ua.Export.LocalizedText
                            {
                                Locale = "en",
                                Value = "A test type."
                            }
                        ],
                        References =
                        [
                            new Reference
                            {
                                ReferenceType = "HasSubtype",
                                IsForward = false,
                                Value = "i=58"
                            }
                        ]
                    }
                ]
            };
        }

        private static WotNodeSetConverterOptions AlwaysPreserve()
        {
            return new WotNodeSetConverterOptions
            {
                PreservationMode = WotNodeSetPreservationMode.Always
            };
        }

        private static byte[] Write(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }
    }
}
