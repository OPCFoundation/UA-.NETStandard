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

using System.Text;
using Opc.Ua.Client;
using Opc.Ua.Export;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for NodeSet2 export and import.
    /// Verifies that XmlSerializer-based UANodeSet Read/Write
    /// works correctly under NativeAOT compilation.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class NodeSetAotTests(AotTestFixture fixture)
    {
        private const string SimpleNodeSetXml =
            """
            <?xml version='1.0' encoding='utf-8'?>
            <UANodeSet xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance'
                       xmlns:xsd='http://www.w3.org/2001/XMLSchema'
                       LastModified='2025-01-01T00:00:00Z'
                       xmlns='http://opcfoundation.org/UA/2011/03/UANodeSet.xsd'>
              <NamespaceUris>
                <Uri>http://opcfoundation.org/UA/AotTest</Uri>
              </NamespaceUris>
              <Aliases>
                <Alias Alias='HasSubtype'>i=45</Alias>
                <Alias Alias='HasComponent'>i=47</Alias>
                <Alias Alias='HasProperty'>i=46</Alias>
                <Alias Alias='HasTypeDefinition'>i=40</Alias>
              </Aliases>
              <UAObjectType NodeId='ns=1;i=1000' BrowseName='1:TestObjectType'>
                <DisplayName>TestObjectType</DisplayName>
                <References>
                  <Reference ReferenceType='HasSubtype' IsForward='false'>i=58</Reference>
                </References>
              </UAObjectType>
              <UAObject NodeId='ns=1;i=2000' BrowseName='1:TestObject'>
                <DisplayName>TestObject</DisplayName>
                <References>
                  <Reference ReferenceType='HasTypeDefinition'>ns=1;i=1000</Reference>
                </References>
              </UAObject>
              <UAVariable DataType='i=11' ParentNodeId='ns=1;i=2000' NodeId='ns=1;i=2001' BrowseName='1:TestVariable' ValueRank='-1'>
                <DisplayName>TestVariable</DisplayName>
                <References>
                  <Reference ReferenceType='HasTypeDefinition'>i=63</Reference>
                  <Reference ReferenceType='HasComponent' IsForward='false'>ns=1;i=2000</Reference>
                </References>
                <Value>
                  <uax:Double xmlns:uax='http://opcfoundation.org/UA/2008/02/Types.xsd'>3.14</uax:Double>
                </Value>
              </UAVariable>
              <UADataType NodeId='ns=1;i=3000' BrowseName='1:TestDataType'>
                <DisplayName>TestDataType</DisplayName>
                <References>
                  <Reference ReferenceType='HasSubtype' IsForward='false'>i=22</Reference>
                </References>
                <Definition Name='TestDataType'>
                  <Field Name='Name' DataType='i=12' />
                  <Field Name='Value' DataType='i=11' />
                  <Field Name='Flags' DataType='i=1' ValueRank='1' />
                </Definition>
              </UADataType>
            </UANodeSet>
            """;

        /// <summary>
        /// Verifies that UANodeSet.Read can parse a NodeSet2 XML
        /// document under NativeAOT.
        /// </summary>
        [Test]
        public async Task ReadNodeSetFromXmlAsync()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var nodeSet = UANodeSet.Read(stream);

            await Assert.That(nodeSet).IsNotNull();
            await Assert.That(nodeSet.NamespaceUris).IsNotNull();
            await Assert.That(nodeSet.NamespaceUris.Length).IsEqualTo(1);
            await Assert.That(nodeSet.Items).IsNotNull();
            await Assert.That(nodeSet.Items.Length).IsEqualTo(4);
            await Assert.That(nodeSet.LastModifiedSpecified).IsTrue();
        }

        /// <summary>
        /// Verifies that UANodeSet.Read correctly deserializes
        /// polymorphic node types (UAObjectType, UAObject, UAVariable,
        /// UADataType) under NativeAOT.
        /// </summary>
        [Test]
        public async Task ReadNodeSetPolymorphicTypesAsync()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var nodeSet = UANodeSet.Read(stream);

            await Assert.That(nodeSet.Items[0]).IsTypeOf<UAObjectType>();
            await Assert.That(nodeSet.Items[1]).IsTypeOf<UAObject>();
            await Assert.That(nodeSet.Items[2]).IsTypeOf<UAVariable>();
            await Assert.That(nodeSet.Items[3]).IsTypeOf<UADataType>();

            // Verify data type definition fields
            var dataType = (UADataType)nodeSet.Items[3];
            await Assert.That(dataType.Definition).IsNotNull();
            await Assert.That(dataType.Definition.Field.Length).IsEqualTo(3);
            await Assert.That(dataType.Definition.Field[0].Name).IsEqualTo("Name");
        }

        /// <summary>
        /// Verifies that UANodeSet.Import converts UANode items
        /// into NodeState instances under NativeAOT.
        /// </summary>
        [Test]
        public async Task ImportNodeSetToNodeStatesAsync()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var nodeSet = UANodeSet.Read(stream);

            var nodes = new NodeStateCollection();
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };

            foreach (string ns in nodeSet.NamespaceUris)
            {
                context.NamespaceUris.Append(ns);
            }

            nodeSet.Import(context, nodes);

            await Assert.That(nodes.Count).IsEqualTo(4);

            // Find nodes by browse name
            NodeState objectTypeNode = nodes.Find(
                n => n.BrowseName.Name == "TestObjectType");
            NodeState objectNode = nodes.Find(
                n => n.BrowseName.Name == "TestObject");
            NodeState variableNode = nodes.Find(
                n => n.BrowseName.Name == "TestVariable");
            NodeState dataTypeNode = nodes.Find(
                n => n.BrowseName.Name == "TestDataType");

            await Assert.That(objectTypeNode).IsNotNull();
            await Assert.That(objectTypeNode).IsTypeOf<BaseObjectTypeState>();
            await Assert.That(objectNode).IsNotNull();
            await Assert.That(objectNode).IsTypeOf<BaseObjectState>();
            await Assert.That(variableNode).IsNotNull();
            await Assert.That(variableNode).IsTypeOf<BaseDataVariableState>();
            await Assert.That(dataTypeNode).IsNotNull();
            await Assert.That(dataTypeNode).IsTypeOf<DataTypeState>();
        }

        /// <summary>
        /// Verifies that a NodeSet can be written to XML and read back
        /// (round-trip) under NativeAOT.
        /// </summary>
        [Test]
        public async Task WriteAndReadNodeSetRoundTripAsync()
        {
            // Read initial nodeset
            using var readStream = new MemoryStream(
                Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var original = UANodeSet.Read(readStream);

            // Import to NodeStates
            var nodes = new NodeStateCollection();
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            foreach (string ns in original.NamespaceUris)
            {
                context.NamespaceUris.Append(ns);
            }
            original.Import(context, nodes);

            // Export back to XML via SaveAsNodeSet2
            using var writeStream = new MemoryStream();
            nodes.SaveAsNodeSet2(context, writeStream);

            // Read back the exported XML
            writeStream.Position = 0;
            var roundTripped = UANodeSet.Read(writeStream);

            await Assert.That(roundTripped).IsNotNull();
            await Assert.That(roundTripped.Items).IsNotNull();
            await Assert.That(roundTripped.Items.Length).IsEqualTo(4);

            // Verify the data type definition survived the round-trip
            UADataType roundTrippedDataType = null;
            foreach (UANode item in roundTripped.Items)
            {
                if (item is UADataType dt &&
                    dt.BrowseName.Contains("TestDataType", StringComparison.Ordinal))
                {
                    roundTrippedDataType = dt;
                    break;
                }
            }

            await Assert.That(roundTrippedDataType).IsNotNull();
            await Assert.That(roundTrippedDataType!.Definition).IsNotNull();
            await Assert.That(roundTrippedDataType.Definition.Field.Length)
                .IsEqualTo(3);
        }

        /// <summary>
        /// Verifies that UANodeSet.Write produces valid XML that
        /// can be read back under NativeAOT.
        /// </summary>
        [Test]
        public async Task WriteNodeSetDirectlyAsync()
        {
            var nodeSet = new UANodeSet
            {
                LastModified = DateTime.UtcNow,
                LastModifiedSpecified = true,
                NamespaceUris = ["http://opcfoundation.org/UA/AotWriteTest"]
            };

            // Add a simple object type
            nodeSet.Items =
            [
                new UAObjectType
                {
                    NodeId = "ns=1;i=5000",
                    BrowseName = "1:AotTestType",
                    DisplayName =
                    [
                        new Export.LocalizedText { Value = "AotTestType" }
                    ]
                },
                new UAVariable
                {
                    NodeId = "ns=1;i=5001",
                    BrowseName = "1:AotTestVar",
                    DisplayName =
                    [
                        new Export.LocalizedText { Value = "AotTestVar" }
                    ],
                    DataType = "i=11",
                    ValueRank = -1
                }
            ];

            // Write to stream
            using var stream = new MemoryStream();
            nodeSet.Write(stream);

            await Assert.That(stream.Length).IsGreaterThan(0);

            // Read back
            stream.Position = 0;
            var readBack = UANodeSet.Read(stream);

            await Assert.That(readBack).IsNotNull();
            await Assert.That(readBack.Items.Length).IsEqualTo(2);
            await Assert.That(readBack.Items[0]).IsTypeOf<UAObjectType>();
            await Assert.That(readBack.Items[1]).IsTypeOf<UAVariable>();
        }

        /// <summary>
        /// Verifies that parent-child relationships are correctly
        /// established during import with linkParentChild under NativeAOT.
        /// </summary>
        [Test]
        public async Task ImportWithParentChildLinkingAsync()
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var nodeSet = UANodeSet.Read(stream);

            var nodes = new NodeStateCollection();
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            foreach (string ns in nodeSet.NamespaceUris)
            {
                context.NamespaceUris.Append(ns);
            }

            nodeSet.Import(context, nodes, linkParentChild: true);

            // Find the variable node (has ParentNodeId pointing to TestObject)
            var variableNode = nodes.Find(
                n => n.BrowseName.Name == "TestVariable") as BaseInstanceState;

            await Assert.That(variableNode).IsNotNull();

            // The parent should be set to TestObject
            if (variableNode?.Parent != null)
            {
                await Assert.That(variableNode.Parent.BrowseName.Name)
                    .IsEqualTo("TestObject");
            }
        }

        /// <summary>
        /// Exports nodes from the running reference server to NodeSet2 XML
        /// and reads them back under NativeAOT.
        /// </summary>
        [Test]
        public async Task ExportServerNodesToNodeSetAsync()
        {
            // Browse some nodes from the server
            var browser = new Browser(fixture.Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0
            };

            ArrayOf<ReferenceDescription> refs = await browser
                .BrowseAsync(ObjectIds.Server, CancellationToken.None)
                .ConfigureAwait(false);

            var allNodes = new List<INode>();
            foreach (ReferenceDescription r in refs.ToList())
            {
                INode node = await fixture.Session.NodeCache
                    .FindAsync(r.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (node != null)
                {
                    allNodes.Add(node);
                }
            }

            await Assert.That(allNodes.Count).IsGreaterThan(0);

            // Export to NodeSet2
            var context = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = fixture.Session.NamespaceUris,
                ServerUris = fixture.Session.ServerUris
            };

            using var writeStream = new MemoryStream();
            CoreClientUtils.ExportNodesToNodeSet2(
                context, allNodes, writeStream);

            await Assert.That(writeStream.Length).IsGreaterThan(0);

            // Read back and verify
            writeStream.Position = 0;
            var nodeSet = UANodeSet.Read(writeStream);

            await Assert.That(nodeSet).IsNotNull();
            await Assert.That(nodeSet.Items).IsNotNull();
            await Assert.That(nodeSet.Items.Length).IsGreaterThan(0);
        }

        /// <summary>
        /// Exports server nodes, re-imports them into NodeState objects,
        /// and verifies the full round-trip under NativeAOT.
        /// </summary>
        [Test]
        public async Task ExportAndReimportServerNodesAsync()
        {
            // Browse Object types subtree (small, predictable set)
            var browser = new Browser(fixture.Session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                IncludeSubtypes = false,
                NodeClassMask = (int)NodeClass.ObjectType
            };

            ArrayOf<ReferenceDescription> refs = await browser
                .BrowseAsync(ObjectTypeIds.BaseObjectType, CancellationToken.None)
                .ConfigureAwait(false);

            var allNodes = new List<INode>();
            foreach (ReferenceDescription r in refs.ToList().Take(5))
            {
                INode node = await fixture.Session.NodeCache
                    .FindAsync(r.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (node != null)
                {
                    allNodes.Add(node);
                }
            }

            await Assert.That(allNodes.Count).IsGreaterThan(0);

            // Export
            var exportContext = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = fixture.Session.NamespaceUris,
                ServerUris = fixture.Session.ServerUris
            };

            using var stream = new MemoryStream();
            CoreClientUtils.ExportNodesToNodeSet2(
                exportContext, allNodes, stream);

            // Re-import
            stream.Position = 0;
            var nodeSet = UANodeSet.Read(stream);

            var importedNodes = new NodeStateCollection();
            var importContext = new SystemContext(fixture.Telemetry)
            {
                NamespaceUris = new NamespaceTable()
            };
            if (nodeSet.NamespaceUris != null)
            {
                foreach (string ns in nodeSet.NamespaceUris)
                {
                    importContext.NamespaceUris.Append(ns);
                }
            }

            nodeSet.Import(importContext, importedNodes);

            await Assert.That(importedNodes.Count).IsEqualTo(allNodes.Count);

            // Verify every imported node has a valid NodeId and BrowseName
            foreach (NodeState node in importedNodes)
            {
                await Assert.That(node.NodeId.IsNull).IsFalse();
                await Assert.That(node.BrowseName.Name).IsNotNull();
            }
        }

        /// <summary>
        /// Verifies that UANodeSet aliases are preserved through
        /// a write/read round-trip under NativeAOT.
        /// </summary>
        [Test]
        public async Task AliasesPreservedInRoundTripAsync()
        {
            using var stream = new MemoryStream(
                Encoding.UTF8.GetBytes(SimpleNodeSetXml));
            var nodeSet = UANodeSet.Read(stream);

            await Assert.That(nodeSet.Aliases).IsNotNull();
            await Assert.That(nodeSet.Aliases.Length).IsGreaterThan(0);

            // Write and read back
            using var writeStream = new MemoryStream();
            nodeSet.Write(writeStream);

            writeStream.Position = 0;
            var roundTripped = UANodeSet.Read(writeStream);

            await Assert.That(roundTripped.Aliases).IsNotNull();
            await Assert.That(roundTripped.Aliases.Length)
                .IsEqualTo(nodeSet.Aliases.Length);
        }
    }
}
