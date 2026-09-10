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
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    public sealed class WotConvertedAffordanceTests
    {
        [Test]
        public void NativeNodeIdentityIsSelectedByItsQualifiedOwnedDeclaration()
        {
            using WotDocument document = CreateDocument();
            UANodeSet nodeSet = CreateNodeSet();

            ArrayOf<WotConvertedAffordance> mapped = WotNodeSetConverter.ResolveAffordanceNodes(
                document, nodeSet, new ExpandedNodeId(1000, "urn:mapped-model"));

            Assert.That(mapped.Count, Is.EqualTo(1));
            Assert.That(mapped[0].NodeId, Is.EqualTo(new ExpandedNodeId(7, "urn:mapped-model")));
            Assert.That(mapped[0].OwnerNodeId, Is.EqualTo(new ExpandedNodeId(1000, "urn:mapped-model")));
            Assert.That(mapped[0].JsonPointer, Is.EqualTo("/properties/sensor"));
            Assert.That(nodeSet.Items![1].NodeId, Is.EqualTo("ns=1;i=7"));
        }

        [TestCase("explicit-missing")]
        [TestCase("unowned")]
        [TestCase("ambiguous")]
        [TestCase("wrong-class")]
        [TestCase("foreign")]
        public void MappingRejectsMissingOrAmbiguousAuthority(string scenario)
        {
            using WotDocument document = CreateDocument(scenario == "explicit-missing");
            UANodeSet nodeSet = CreateNodeSet();
            UANode[] nodes = nodeSet.Items!;
            if (scenario == "unowned")
            {
                ((UAVariable)nodes[1]).ParentNodeId = "ns=1;i=2000";
                nodes[0].References = [];
            }
            else if (scenario == "ambiguous")
            {
                nodeSet.Items =
                [
                    .. nodes,
                    new UAVariable
                    {
                        NodeId = "ns=1;i=8",
                        BrowseName = "1:Value",
                        ParentNodeId = "ns=1;i=1000",
                        DataType = "i=11"
                    }
                ];
            }
            else if (scenario == "wrong-class")
            {
                nodes[1] = new UAMethod
                {
                    NodeId = "ns=1;i=7",
                    BrowseName = "1:Value",
                    ParentNodeId = "ns=1;i=1000"
                };
            }
            else if (scenario == "foreign")
            {
                nodes[1].NodeId = "svr=1;nsu=urn:mapped-model;i=7";
            }

            Assert.That(
                () => WotNodeSetConverter.ResolveAffordanceNodes(
                    document, nodeSet, new ExpandedNodeId(1000, "urn:mapped-model")),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void NamespaceZeroUriIdentitiesMatchLocalNodeSetIdentifiers()
        {
            using WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """
                {
                  "@type": "uav:object",
                  "uav:id": "nsu=http://opcfoundation.org/UA/;i=2253",
                  "properties": {
                    "time": { "uav:id": "nsu=http://opcfoundation.org/UA/;i=2258", "type": "string" }
                  }
                }
                """));
            var nodeSet = new UANodeSet
            {
                Models = [new ModelTableEntry { ModelUri = "http://opcfoundation.org/UA/" }],
                Items =
                [
                    new UAObject { NodeId = "i=2253", BrowseName = "Server" },
                    new UAVariable
                    {
                        NodeId = "i=2258",
                        BrowseName = "CurrentTime",
                        ParentNodeId = "i=2253",
                        DataType = "i=13"
                    }
                ]
            };

            ArrayOf<WotConvertedAffordance> mapped = WotNodeSetConverter.ResolveAffordanceNodes(
                document, nodeSet, new ExpandedNodeId(2253, "http://opcfoundation.org/UA/"));

            Assert.That(mapped.Count, Is.EqualTo(1));
            Assert.That(mapped[0].NodeId, Is.EqualTo(new ExpandedNodeId(2258)));
            Assert.That(mapped[0].OwnerNodeId, Is.EqualTo(new ExpandedNodeId(2253)));
        }

        [Test]
        public void PayloadCaptureRetainsOriginalScopedTypesAndNestedRanksAfterDisposal()
        {
            WotPayloadSchema captured;
            using (WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                """
                {
                  "@context":{"native":"urn:wrong-root","model":"urn:payload-fields"},
                  "actions":{"exchange":{
                    "input":{
                      "type":"object","uav:argumentLayout":"named","uav:fieldOrder":["Values","Target"],
                      "properties":{
                        "Values":{
                          "@context":{"native":"http://opcfoundation.org/UA/"},
                          "type":"array","uav:valueRank":1,"uav:browseName":"model:Values",
                          "items":{"type":"integer","uav:dataTypeName":"native:UInt16"}
                        },
                        "Target":{"type":"string","uav:dataTypeId":"i=17"}
                      }
                    }
                  }}
                }
                """)))
            {
                JsonElement action = document.Actions["exchange"];
                captured = WotNodeSetConverter.CapturePayloadSchema(document, WotAffordanceKind.Action, action);
                Assert.That(WotNodeSetConverter.CapturePayloadSchema(document, WotAffordanceKind.Action, action),
                    Is.SameAs(captured));
            }

            Assert.That(captured.Diagnostics, Is.Empty);
            Assert.That(
                captured.TryGetTypeBinding("/input/properties/Values", out WotPayloadTypeBinding values), Is.True);
            Assert.That(values!.DataTypeId, Is.EqualTo(new ExpandedNodeId(5)));
            Assert.That(values.TypeInfo, Is.EqualTo(TypeInfo.Create(BuiltInType.UInt16, ValueRanks.OneDimension)));
            Assert.That(values.ResolvedBrowseName, Is.EqualTo("nsu=urn:payload-fields;Values"));
            Assert.That(captured.TryGetTypeBinding("/input/properties/Values/items", out WotPayloadTypeBinding item),
                Is.True);
            Assert.That(item!.DataTypeId, Is.EqualTo(new ExpandedNodeId(5)));
            Assert.That(item.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(
                captured.TryGetTypeBinding("/input/properties/Target", out WotPayloadTypeBinding target), Is.True);
            Assert.That(target!.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.NodeId));
            Assert.That(captured.Definition.GetProperty("input").GetProperty("uav:fieldOrder")[0].GetString(),
                Is.EqualTo("Values"));
        }

        [TestCase("input")]
        [TestCase("output")]
        public void ConvertedInferredPayloadTypesKeepTheNativeArgumentIdentityAfterDisposal(string member)
        {
            WotPayloadSchema captured;
            UANodeSet nodeSet;
            using (WotDocument document = WotDocument.Parse(Encoding.UTF8.GetBytes(
                $$"""
                {
                  "@context":{"model":"urn:payload-capture"},
                  "@type":["tm:ThingModel","uav:objectType"],
                  "uav:id":"nsu=urn:payload-capture;i=2","uav:browseName":"model:Consumer",
                  "actions":{
                    "Exchange":{
                      "{{member}}":{
                        "type":"object","uav:dataTypeName":"model:Reading","uav:argumentLayout":"single",
                        "properties":{
                          "Value":{"type":"boolean"}
                        },
                        "required":["Value"]
                      }
                    }
                  }
                }
                """)))
            {
                WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
                Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
                nodeSet = result.Value!;
                ArrayOf<WotConvertedAffordance> converted =
                    WotNodeSetConverter.ResolveAffordanceNodes(document, nodeSet);
                Assert.That(converted.Count, Is.EqualTo(1));
                Assert.That(converted[0].PayloadSchema, Is.Not.Null);
                captured = converted[0].PayloadSchema!;
                Assert.That(WotNodeSetConverter.CapturePayloadSchema(
                    document, WotAffordanceKind.Action, document.Actions["Exchange"]), Is.SameAs(captured));
            }

            Assert.That(captured.TryGetTypeBinding("/" + member, out WotPayloadTypeBinding payload), Is.True);
            Assert.That(payload!.DataTypeId,
                Is.EqualTo(new ExpandedNodeId("DataTypes/Reading", "urn:payload-capture")));
            Assert.That(payload.TypeInfo, Is.EqualTo(TypeInfo.Create(BuiltInType.ExtensionObject, ValueRanks.Scalar)));
            UADataType nativeType = nodeSet.Items!.OfType<UADataType>().Single();
            Assert.That(nativeType.NodeId, Is.EqualTo("ns=1;s=DataTypes/Reading"));
            Assert.That(nativeType.Definition!.Field![0].Name, Is.EqualTo("Value"));
            Assert.That(nativeType.Definition.Field[0].DataType, Is.EqualTo("i=1"));
            UAVariable arguments = nodeSet.Items!.OfType<UAVariable>().Single();
            XNamespace ua = Namespaces.OpcUaXsd;
            XElement argument = XElement.Parse(arguments.Value!.OuterXml).Descendants(ua + "Argument").Single();
            Assert.That(argument.Element(ua + "DataType")!.Element(ua + "Identifier")!.Value,
                Is.EqualTo(nativeType.NodeId));
            Assert.That(captured.Definition.GetProperty(member).GetProperty("properties").GetProperty("Value")
                .GetProperty("type").GetString(), Is.EqualTo("boolean"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PayloadCaptureRejectsForeignOrClonedElementsInsteadOfGuessingContext(bool clone)
        {
            using WotDocument document = CreateDocument();
            using WotDocument foreign = CreateDocument();
            JsonElement schema = clone ? document.Properties["sensor"].Clone() : foreign.Properties["sensor"];

            Assert.That(() => WotNodeSetConverter.CapturePayloadSchema(document, WotAffordanceKind.Property, schema),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("affordance"));
        }

        [Test]
        public void ReplacingAnEventBrowsePathCannotReuseItsPreviousContextResolution()
        {
            using WotDocument document = CreateDocument();
            WotPayloadSchema schema = WotNodeSetConverter.CapturePayloadSchema(
                document, WotAffordanceKind.Property, document.Properties["sensor"]);
            WotResolvedEventSelectClause original = new WotResolvedEventSelectClause("i=2041", "old:Value")
                .WithPayloadSchema(schema, "nsu=urn:old;Value");

            WotResolvedEventSelectClause replacement = original.WithBrowsePath("new:Value");

            Assert.That(replacement.BrowsePath, Is.EqualTo("new:Value"));
            Assert.That(replacement.ResolvedBrowsePath, Is.Null);
            Assert.That(replacement.PayloadSchema, Is.SameAs(schema));
            Assert.That(original.ResolvedBrowsePath, Is.EqualTo("nsu=urn:old;Value"));
        }

        private static WotDocument CreateDocument(bool explicitMissingIdentity = false)
        {
            string identity = explicitMissingIdentity ? """, "uav:id": "nsu=urn:mapped-model;i=99" """ : string.Empty;
            return WotDocument.Parse(Encoding.UTF8.GetBytes(
                $$"""
                {
                  "@context": { "m": "urn:mapped-model" },
                  "@type": "uav:object",
                  "uav:id": "nsu=urn:mapped-model;i=1000",
                  "uav:browseName": "m:Device",
                  "properties": {
                    "sensor": { "uav:browseName": "m:Value", "type": "number"{{identity}} }
                  }
                }
                """));
        }

        private static UANodeSet CreateNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:mapped-model"],
                Models = [new ModelTableEntry { ModelUri = "urn:mapped-model" }],
                Items =
                [
                    new UAObject
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:Device",
                        References = [new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=7" }]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=7",
                        BrowseName = "1:Value",
                        ParentNodeId = "ns=1;i=1000",
                        DataType = "i=11"
                    }
                ]
            };
        }
    }
}
