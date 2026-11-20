// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
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
                /*lang=json,strict*/
                "{\"@context\":[],\"title\":\"T\",\"vendor:unknown\":{\"b\":2,\"a\":1}}");

            using var document = WotDocument.Parse(json);
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
            JsonNode root = JsonNode.Parse(json)!;
            JsonObject envelope = root["uav:nodeSet"]!.AsObject();
            string data = envelope["data"]!.GetValue<string>();
            char replacement = data[0] == 'A' ? 'B' : 'A';
            envelope["data"] = replacement + data.Substring(1);
            json = root.ToJsonString();

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
                Aliases =
                [
                    // A NodeSet2 document has to declare every name it uses
                    // where a NodeId is expected; without this the document
                    // cannot be imported.
                    new NodeIdAlias { Alias = "HasSubtype", Value = "i=45" }
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
                            new Export.LocalizedText
                            {
                                Value = "MachineType"
                            }
                        ],
                        Description =
                        [
                            new Export.LocalizedText
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
