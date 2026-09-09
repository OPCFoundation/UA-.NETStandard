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
using System.Text;
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
