/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NodeBrowsePath"/> sentinel creation,
    /// detection and conversion to OPC UA relative paths.
    /// </summary>
    [TestFixture]
    public sealed class NodeBrowsePathTests
    {
        [Test]
        public void ToNodeIdCreatesNamespaceZeroStringSentinel()
        {
            const string path = "/2:Demo/2:CurrentTime";

            var nodeId = NodeBrowsePath.ToNodeId(path);

            Assert.That(NodeBrowsePath.IsBrowsePath(nodeId), Is.True);
            Assert.That(nodeId.NamespaceIndex, Is.Zero);
            Assert.That(nodeId.IdType, Is.EqualTo(IdType.String));
            Assert.That(nodeId.IdentifierAsString, Is.EqualTo(path));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Plain")]
        [TestCase("2:Demo/2:CurrentTime")]
        public void ToNodeIdRejectsInvalidRelativePath(string? path)
        {
            Assert.That(
                () => NodeBrowsePath.ToNodeId(path!),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void IsBrowsePathRecognizesOnlyNamespaceZeroStringSentinels()
        {
            Assert.That(NodeBrowsePath.IsBrowsePath(NodeBrowsePath.ToNodeId("/2:Demo")), Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(NodeBrowsePath.ToNodeId(".2:Prop")), Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(new NodeId(11u)), Is.False);
            Assert.That(NodeBrowsePath.IsBrowsePath(new NodeId("Plain", 0)), Is.False);
            Assert.That(NodeBrowsePath.IsBrowsePath(NodeId.Null), Is.False);
            Assert.That(NodeBrowsePath.IsBrowsePath(new NodeId("/2:Demo", 2)), Is.False);
        }

        [Test]
        public void ToRelativePathMapsSlashSegmentsToHierarchicalReferences()
        {
            var nodeId = NodeBrowsePath.ToNodeId("/2:Demo/2:CurrentTime");

            var relativePath = NodeBrowsePath.ToRelativePath(nodeId);

            Assert.That(relativePath.Elements.Count, Is.EqualTo(2));
            AssertRelativePathElement(
                relativePath.Elements[0],
                ReferenceTypeIds.HierarchicalReferences,
                QualifiedName.Parse("2:Demo"));
            AssertRelativePathElement(
                relativePath.Elements[1],
                ReferenceTypeIds.HierarchicalReferences,
                QualifiedName.Parse("2:CurrentTime"));
        }

        [Test]
        public void ToRelativePathMapsDotSegmentsToAggregates()
        {
            var nodeId = NodeBrowsePath.ToNodeId("/2:Obj.2:Prop");

            var relativePath = NodeBrowsePath.ToRelativePath(nodeId);

            Assert.That(relativePath.Elements.Count, Is.EqualTo(2));
            AssertRelativePathElement(
                relativePath.Elements[0],
                ReferenceTypeIds.HierarchicalReferences,
                QualifiedName.Parse("2:Obj"));
            AssertRelativePathElement(
                relativePath.Elements[1],
                ReferenceTypeIds.Aggregates,
                QualifiedName.Parse("2:Prop"));
        }

        [Test]
        public void ToRelativePathRejectsNonBrowsePathNodeId()
        {
            Assert.That(
                () => NodeBrowsePath.ToRelativePath(new NodeId(5u)),
                Throws.InstanceOf<ArgumentException>());
        }

        private static void AssertRelativePathElement(
            RelativePathElement element,
            NodeId referenceTypeId,
            QualifiedName targetName)
        {
            Assert.That(element.ReferenceTypeId, Is.EqualTo(referenceTypeId));
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.IsInverse, Is.False);
            Assert.That(element.TargetName, Is.EqualTo(targetName));
        }
    }
}
