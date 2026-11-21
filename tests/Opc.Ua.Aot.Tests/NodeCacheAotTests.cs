// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for NodeCache operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class NodeCacheAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task FetchNodeAsync()
        {
            Node node = await fixture.Session.NodeCache
                .FetchNodeAsync(ObjectIds.Server, CancellationToken.None)
                .ConfigureAwait(false) ??
                throw new InvalidOperationException("The server node was not found.");

            await Assert.That(node).IsNotNull();
            await Assert.That(node.NodeId.IsNull).IsFalse();
            await Assert.That(node.BrowseName.Name).IsNotNull();
        }

        [Test]
        public async Task FetchNodesAsync()
        {
            ArrayOf<ExpandedNodeId> nodeIds =
            [
                ObjectIds.Server,
                ObjectIds.ObjectsFolder,
                ObjectIds.TypesFolder
            ];

            ArrayOf<Node?> nodes = await fixture.Session.NodeCache
                .FetchNodesAsync(nodeIds, CancellationToken.None)
                .ConfigureAwait(false);

            await Assert.That(nodes.Count).IsEqualTo(nodeIds.Count);

            foreach (Node? node in nodes.ToList())
            {
                await Assert.That(node).IsNotNull();
                await Assert.That(node!.NodeId.IsNull).IsFalse();
            }
        }

        [Test]
        public async Task FindReferencesAsync()
        {
            // Ensure reference types are loaded in the NodeCache
            NamespaceTable namespaceUris = fixture.Session.NamespaceUris;
            var referenceTypes = ReferenceTypeIds.Identifiers
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
        public async Task FetchTypeTreeAsync()
        {
            var baseDataTypeId = NodeId.ToExpandedNodeId(
                DataTypeIds.BaseDataType,
                fixture.Session.NamespaceUris);

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
