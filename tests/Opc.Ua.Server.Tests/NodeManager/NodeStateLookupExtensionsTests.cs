// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class NodeStateLookupExtensionsTests
    {
        private static BaseObjectState NewObject(
            string name,
            ushort ns = 2,
            NodeId? typeDefinitionId = null)
        {
            var obj = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId($"id_{name}", ns),
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name)
            };
            if (typeDefinitionId.HasValue)
            {
                obj.TypeDefinitionId = typeDefinitionId.Value;
            }
            return obj;
        }

        // ---- FindByBrowseName ---------------------------------------------

        [Test]
        public void FindByBrowseNameReturnsMatchingNode()
        {
            BaseObjectState a = NewObject("Alpha");
            BaseObjectState b = NewObject("Beta");
            NodeState[] nodes = [a, b];

            NodeState? found = nodes.FindByBrowseName(new QualifiedName("Beta", 2));

            Assert.That(found, Is.SameAs(b));
        }

        [Test]
        public void FindByBrowseNameReturnsNullWhenNoMatch()
        {
            BaseObjectState a = NewObject("Alpha");
            NodeState[] nodes = [a];

            NodeState? found = nodes.FindByBrowseName(new QualifiedName("Missing", 2));

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindByBrowseNameNullCollectionThrows()
        {
            IEnumerable<NodeState>? nodes = null;
            Assert.That(
                () => nodes!.FindByBrowseName(new QualifiedName("X", 2)),
                Throws.ArgumentNullException);
        }

        // ---- FindById -----------------------------------------------------

        [Test]
        public void FindByIdReturnsMatchingNode()
        {
            BaseObjectState a = NewObject("Alpha");
            BaseObjectState b = NewObject("Beta");
            var dict = new NodeIdDictionary<NodeState>
            {
                { a.NodeId, a },
                { b.NodeId, b }
            };

            NodeState? found = dict.FindById(b.NodeId);

            Assert.That(found, Is.SameAs(b));
        }

        [Test]
        public void FindByIdReturnsNullForUnknownNodeId()
        {
            BaseObjectState a = NewObject("Alpha");
            var dict = new NodeIdDictionary<NodeState>
            {
                { a.NodeId, a }
            };

            NodeState? found = dict.FindById(new NodeId("unknown", 2));

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindByIdNullDictionaryThrows()
        {
            IDictionary<NodeId, NodeState>? dict = null;
            Assert.That(
                () => dict!.FindById(new NodeId("any", 2)),
                Throws.ArgumentNullException);
        }

        // ---- FindByTypeDefinition ----------------------------------------

        [Test]
        public void FindByTypeDefinitionReturnsAllMatchingInstances()
        {
            var typeId = new NodeId("PumpType", 3);
            BaseObjectState p1 = NewObject("Pump #1", typeDefinitionId: typeId);
            BaseObjectState p2 = NewObject("Pump #2", typeDefinitionId: typeId);
            BaseObjectState other = NewObject("Boiler", typeDefinitionId: new NodeId("BoilerType", 3));
            NodeState[] nodes = [p1, p2, other];

            List<NodeState> matches = nodes.FindByTypeDefinition(typeId);

            Assert.That(matches, Has.Count.EqualTo(2));
            Assert.That(matches, Contains.Item(p1));
            Assert.That(matches, Contains.Item(p2));
        }

        [Test]
        public void FindByTypeDefinitionSkipsNonInstanceNodes()
        {
            var typeId = new NodeId("PumpType", 3);
            BaseObjectState instance = NewObject("Pump #1", typeDefinitionId: typeId);
            var folder = new FolderState(parent: null)
            {
                NodeId = new NodeId("Folder", 2),
                BrowseName = new QualifiedName("Folder", 2),
                DisplayName = new LocalizedText("Folder"),
                TypeDefinitionId = typeId
            };
            var raw = new NodeState[] { instance, folder };

            List<NodeState> matches = raw.FindByTypeDefinition(typeId);

            Assert.That(matches, Has.Count.EqualTo(2));
            Assert.That(matches, Contains.Item(instance));
            Assert.That(matches, Contains.Item(folder));
        }

        [Test]
        public void FindByTypeDefinitionReturnsEmptyWhenNoMatches()
        {
            BaseObjectState a = NewObject("Alpha", typeDefinitionId: new NodeId("X", 3));
            NodeState[] nodes = [a];

            List<NodeState> matches = nodes.FindByTypeDefinition(new NodeId("Y", 3));

            Assert.That(matches, Is.Empty);
        }

        [Test]
        public void FindByTypeDefinitionNullCollectionThrows()
        {
            IEnumerable<NodeState>? nodes = null;
            Assert.That(
                () => nodes!.FindByTypeDefinition(new NodeId("X", 3)),
                Throws.ArgumentNullException);
        }
    }
}
