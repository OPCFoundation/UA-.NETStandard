/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("NodeCache")]
    public sealed class NodeCacheExtensionsCoverageTests
    {
        [Test]
        public async Task ExpandedNodeIdGettersConvertThroughNamespaceTableAsync()
        {
            NamespaceTable namespaceUris = CreateNamespaceTable();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            var node = new VariableNode
            {
                NodeId = new NodeId("node", 1),
                BrowseName = new QualifiedName("node", 1),
                DisplayName = new LocalizedText("Node")
            };
            DataValue value = new(new Variant(123));
            cache.SetupGet(c => c.NamespaceUris).Returns(namespaceUris);
            cache.Setup(c => c.GetNodeAsync(new NodeId("node", 1), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<INode>(node));
            cache.Setup(c => c.GetNodesAsync(
                    It.Is<ArrayOf<NodeId>>(ids => ids.Count == 1 && ids[0] == new NodeId("node", 1)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>([node]));
            cache.Setup(c => c.GetValueAsync(new NodeId("node", 1), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<DataValue>(value));
            cache.Setup(c => c.GetValuesAsync(
                    It.Is<ArrayOf<NodeId>>(ids => ids.Count == 1 && ids[0] == new NodeId("node", 1)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<DataValue>>([value]));

            ExpandedNodeId expanded = new("node", "urn:test");

            INode gotNode = await cache.Object.GetNodeAsync(expanded).ConfigureAwait(false);
            ArrayOf<INode> gotNodes = await cache.Object.GetNodesAsync([expanded]).ConfigureAwait(false);
            DataValue gotValue = await cache.Object.GetValueAsync(expanded).ConfigureAwait(false);
            ArrayOf<DataValue> gotValues = await cache.Object.GetValuesAsync([expanded]).ConfigureAwait(false);

            Assert.That(gotNode, Is.SameAs(node));
            Assert.That(gotNodes, Has.Count.EqualTo(1));
            Assert.That(gotNodes[0], Is.SameAs(node));
            Assert.That(gotValue.WrappedValue, Is.EqualTo(new Variant(123)));
            Assert.That(gotValues[0].WrappedValue, Is.EqualTo(new Variant(123)));
            cache.Verify();
        }

        [Test]
        public async Task ReferenceHelpersDelegateToNodeIdBasedCacheAsync()
        {
            NamespaceTable namespaceUris = CreateNamespaceTable();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            var target = new ObjectNode
            {
                NodeId = new NodeId("target", 1),
                BrowseName = new QualifiedName("target", 1),
                DisplayName = new LocalizedText("Target")
            };
            cache.SetupGet(c => c.NamespaceUris).Returns(namespaceUris);
            cache.Setup(c => c.GetReferencesAsync(
                    new NodeId("source", 1),
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>([target]));
            cache.Setup(c => c.GetReferencesAsync(
                    It.Is<ArrayOf<NodeId>>(ids => ids.Count == 1 && ids[0] == new NodeId("source", 1)),
                    It.Is<ArrayOf<NodeId>>(ids => ids.Count == 1 && ids[0] == ReferenceTypeIds.HierarchicalReferences),
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>([target]));

            ExpandedNodeId source = new("source", "urn:test");

            ArrayOf<INode> single = await cache.Object.GetReferencesAsync(
                source,
                ReferenceTypeIds.HierarchicalReferences,
                false).ConfigureAwait(false);
            ArrayOf<INode> many = await cache.Object.GetReferencesAsync(
                [source],
                ReferenceTypeIds.HierarchicalReferences,
                false).ConfigureAwait(false);
            ArrayOf<INode> manyTypes = await cache.Object.GetReferencesAsync(
                [source],
                [ReferenceTypeIds.HierarchicalReferences],
                false).ConfigureAwait(false);
            ArrayOf<INode> found = await cache.Object.FindReferencesAsync(
                [source],
                [ReferenceTypeIds.HierarchicalReferences],
                false,
                true).ConfigureAwait(false);

            Assert.That(single, Has.Count.EqualTo(1));
            Assert.That(single[0], Is.SameAs(target));
            Assert.That(many, Has.Count.EqualTo(1));
            Assert.That(many[0], Is.SameAs(target));
            Assert.That(manyTypes, Has.Count.EqualTo(1));
            Assert.That(manyTypes[0], Is.SameAs(target));
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0], Is.SameAs(target));
            cache.Verify();
        }

        [Test]
        public async Task FindAndDisplayHelpersHandleMissesAndNodesAsync()
        {
            NamespaceTable namespaceUris = CreateNamespaceTable();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            var node = new ObjectNode
            {
                NodeId = new NodeId("node", 1),
                BrowseName = new QualifiedName("browse", 1),
                DisplayName = new LocalizedText("display")
            };
            cache.SetupGet(c => c.NamespaceUris).Returns(namespaceUris);
            cache.Setup(c => c.FindAsync(ExpandedNodeId.Null, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<INode?>((INode?)null));
            cache.Setup(c => c.FindAsync(
                    It.Is<ExpandedNodeId>(id => HasIdentifierNode(id)),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<INode?>(node));

            ArrayOf<INode?> empty = await cache.Object.FindAsync([], CancellationToken.None).ConfigureAwait(false);
            ArrayOf<INode?> found = await cache.Object.FindAsync(
                [new ExpandedNodeId("node", "urn:test")],
                CancellationToken.None).ConfigureAwait(false);
            string? nullText = await cache.Object.GetDisplayTextAsync((INode?)null).ConfigureAwait(false);
            string? nodeText = await cache.Object.GetDisplayTextAsync(node).ConfigureAwait(false);
            string? nullExpandedText = await cache.Object.GetDisplayTextAsync(ExpandedNodeId.Null).ConfigureAwait(false);
            string? expandedText = await cache.Object.GetDisplayTextAsync(
                new ExpandedNodeId("node", "urn:test")).ConfigureAwait(false);
            string? referenceText = await cache.Object.GetDisplayTextAsync(
                new ReferenceDescription
                {
                    NodeId = new ExpandedNodeId("node", "urn:test"),
                    DisplayName = new LocalizedText("ref"),
                    BrowseName = new QualifiedName("browse", 1)
                }).ConfigureAwait(false);

            Assert.That(empty, Is.Empty);
            Assert.That(found[0], Is.SameAs(node));
            Assert.That(nullText, Is.Empty);
            Assert.That(nodeText, Is.EqualTo("display"));
            Assert.That(nullExpandedText, Is.Empty);
            Assert.That(expandedText, Is.EqualTo("display"));
            Assert.That(referenceText, Is.EqualTo("ref"));
            cache.Verify();
        }

        [Test]
        public async Task BrowsePathAndTypeHelpersFollowReferencesAndSupertypesAsync()
        {
            NamespaceTable namespaceUris = CreateNamespaceTable();
            var cache = new Mock<INodeCache>(MockBehavior.Strict);
            var child = new ObjectNode
            {
                NodeId = new NodeId("child", 1),
                BrowseName = new QualifiedName("Child", 1),
                DisplayName = new LocalizedText("Child")
            };
            cache.SetupGet(c => c.NamespaceUris).Returns(namespaceUris);
            cache.Setup(c => c.GetReferencesAsync(
                    new NodeId("root", 1),
                    ReferenceTypeIds.HierarchicalReferences,
                    false,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>([child]));
            cache.Setup(c => c.FindSuperTypeAsync(new NodeId("root", 1), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(NodeId.Null));
            cache.Setup(c => c.FindSuperTypeAsync(new NodeId("customInt32", 1), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(DataTypeIds.Int32));
            cache.Setup(c => c.FindSuperTypeAsync(DataTypeIds.Int32, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeId>(NodeId.Null));

            INode? found = await cache.Object.GetNodeWithBrowsePathAsync(
                new NodeId("root", 1),
                [new QualifiedName("Child", 1)]).ConfigureAwait(false);
            BuiltInType builtIn = await cache.Object.GetBuiltInTypeAsync(
                new NodeId("customInt32", 1)).ConfigureAwait(false);
            await cache.Object.FetchSuperTypesAsync(
                new ExpandedNodeId("customInt32", "urn:test")).ConfigureAwait(false);

            Assert.That(found, Is.SameAs(child));
            Assert.That(builtIn, Is.EqualTo(BuiltInType.Int32));
            cache.Verify();
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test");
            return namespaceUris;
        }

        private static bool HasIdentifierNode(ExpandedNodeId id)
        {
            return id.TryGetValue(out string value) && value == "node";
        }
    }
}
