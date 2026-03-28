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

using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.AotTests
{
    /// <summary>
    /// AOT integration tests for NodeCache operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class NodeCacheAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task FetchNode()
        {
            Node node = await fixture.Session!.NodeCache
                .FetchNodeAsync(ObjectIds.Server, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(node).IsNotNull();
            await Assert.That(node!.NodeId.IsNull).IsFalse();
            await Assert.That(node.BrowseName.Name).IsNotNull();
        }

        [Test]
        public async Task FetchNodes()
        {
            ArrayOf<ExpandedNodeId> nodeIds =
            [
                ObjectIds.Server,
                ObjectIds.ObjectsFolder,
                ObjectIds.TypesFolder
            ];

            ArrayOf<Node> nodes = await fixture.Session!.NodeCache
                .FetchNodesAsync(nodeIds, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(nodes.Count).IsEqualTo(nodeIds.Count);

            foreach (Node node in nodes.ToList())
            {
                await Assert.That(node).IsNotNull();
                await Assert.That(node!.NodeId.IsNull).IsFalse();
            }
        }

        [Test]
        public async Task FindReferences()
        {
            // Ensure reference types are loaded in the NodeCache
            NamespaceTable namespaceUris = fixture.Session!.NamespaceUris;
            ArrayOf<ExpandedNodeId> referenceTypes = ReferenceTypeIds.Identifiers
                .Select(nodeId => NodeId.ToExpandedNodeId(nodeId, namespaceUris))
                .ToArrayOf();
            await fixture.Session.FetchTypeTreeAsync(
                referenceTypes, CancellationToken.None).ConfigureAwait(false);

            ArrayOf<INode> references = await fixture.Session.NodeCache
                .FindReferencesAsync(
                    ObjectIds.Server,
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(references.Count).IsGreaterThan(0);
        }

        [Test]
        public async Task FetchTypeTree()
        {
            ExpandedNodeId baseDataTypeId = NodeId.ToExpandedNodeId(
                DataTypeIds.BaseDataType,
                fixture.Session!.NamespaceUris);

            await fixture.Session.FetchTypeTreeAsync(
                baseDataTypeId, CancellationToken.None)
                .ConfigureAwait(false);

            // Verify the type tree was populated by checking a known
            // subtype (Int32 is a subtype of BaseDataType).
            bool isKnown = await fixture.Session.NodeCache.TypeTree
                .IsKnownAsync(DataTypeIds.Int32, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(isKnown).IsTrue();
        }
    }
}
