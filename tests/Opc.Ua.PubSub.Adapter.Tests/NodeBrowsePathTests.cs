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
