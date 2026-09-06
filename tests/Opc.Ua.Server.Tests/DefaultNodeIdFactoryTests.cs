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
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that <see cref="DefaultNodeIdFactory"/> mints deterministic,
    /// non-colliding NodeIds for every identifier type it supports.
    /// </summary>
    [TestFixture]
    [Category("DefaultNodeIdFactory")]
    [Parallelizable(ParallelScope.All)]
    public class DefaultNodeIdFactoryTests
    {
        private const string kNamespaceUri = "http://test.org/UA/factory/";
        private const string kOtherNamespaceUri = "http://test.org/UA/Other/";
        private const ushort kNamespaceIndex = 1;
        private const ushort kOtherNamespaceIndex = 2;

        private NamespaceTable m_namespaceUris;
        private SystemContext m_context;

        [SetUp]
        public void SetUp()
        {
            m_namespaceUris = new NamespaceTable();
            m_namespaceUris.Append(kNamespaceUri);
            m_namespaceUris.Append(kOtherNamespaceUri);

            m_context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = m_namespaceUris
            };
        }

        [Test]
        public void NewKeepsAnExplicitlyAuthoredNodeId()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var authored = new NodeId("Authored", kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");
            node.NodeId = authored;

            Assert.That(factory.New(m_context, node), Is.EqualTo(authored));
        }

        [Test]
        public void NoneModeRefusesToMintAnIdentifier()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.None, kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => factory.New(m_context, node));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));

            exception = Assert.Throws<ServiceResultException>(
                () => factory.CreateChildNodeId(
                    new NodeId("Root", kNamespaceIndex),
                    new QualifiedName("Child", kNamespaceIndex),
                    kNamespaceIndex,
                    m_namespaceUris));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void StringModeMintsTheCanonicalPathVerbatim()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);

            NodeId nodeId = factory.CreateChildNodeId(
                new NodeId("Root", kNamespaceIndex),
                new QualifiedName("Group1", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(nodeId.IdType, Is.EqualTo(IdType.String));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
                // the parent segment carries the "s=" type prefix, so a
                // string parent "Root" never reads as a numeric parent.
                Assert.That(
                    nodeId.IdentifierAsString,
                    Is.EqualTo("v1:10:l:6:s=Root:10:l:6:Group1"));
            });
        }

        [Test]
        public void CanonicalPathIsNotAmbiguousOnSeparators()
        {
            // the classic "{parent}_{child}" convention gave both of these
            // the identifier "A_B_C".
            string first = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("A_B", kNamespaceIndex),
                new QualifiedName("C", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            string second = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("A", kNamespaceIndex),
                new QualifiedName("B_C", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void CanonicalPathPreservesTheParentIdentifierType()
        {
            string numericParent = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId(42u, kNamespaceIndex),
                new QualifiedName("Child", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            string stringParent = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("42", kNamespaceIndex),
                new QualifiedName("Child", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.That(numericParent, Is.Not.EqualTo(stringParent));
        }

        [Test]
        public void CanonicalPathQualifiesForeignNamespacesByUri()
        {
            var parentNodeId = new NodeId("Root", kOtherNamespaceIndex);

            string path = DefaultNodeIdFactory.CreateCanonicalPath(
                parentNodeId,
                new QualifiedName("Child", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.That(path, Does.Contain(kOtherNamespaceUri));

            // the same parent reached through a differently ordered namespace
            // table keeps its identity.
            var reordered = new NamespaceTable();
            reordered.Append(kOtherNamespaceUri);
            reordered.Append(kNamespaceUri);

            string reorderedPath = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("Root", 1),
                new QualifiedName("Child", 2),
                2,
                reordered);

            Assert.That(reorderedPath, Is.EqualTo(path));
        }

        [Test]
        public void CanonicalPathMarksBrowseNamesInNamespaceZero()
        {
            string path = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("Root", kNamespaceIndex),
                new QualifiedName("EnabledState", 0),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(path, Does.Contain("z:12:EnabledState"));
                Assert.That(path, Does.Not.Contain("l:12:EnabledState"));
            });
        }

        [Test]
        public void CanonicalPathLeavesTheParentSegmentEmptyForARoot()
        {
            string path = DefaultNodeIdFactory.CreateCanonicalPath(
                NodeId.Null,
                new QualifiedName("Root", kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.That(path, Is.EqualTo("v1:0::8:l:4:Root"));
        }

        [Test]
        public void NewTreatsTheObjectsFolderAsNoParent()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            BaseObjectState node = CreateChild(ObjectIds.ObjectsFolder, "Root");

            NodeId nodeId = factory.New(m_context, node);

            Assert.That(nodeId.IdentifierAsString, Is.EqualTo("v1:0::8:l:4:Root"));
        }

        [TestCase(NodeIdAssignmentMode.Numeric, IdType.Numeric)]
        [TestCase(NodeIdAssignmentMode.String, IdType.String)]
        [TestCase(NodeIdAssignmentMode.Guid, IdType.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque, IdType.Opaque)]
        public void EveryModeMintsItsOwnIdentifierType(
            NodeIdAssignmentMode mode,
            IdType expected)
        {
            var factory = new DefaultNodeIdFactory(mode, kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");

            NodeId nodeId = factory.New(m_context, node);

            Assert.Multiple(() =>
            {
                Assert.That(nodeId.IdType, Is.EqualTo(expected));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
                Assert.That(nodeId.IsNull, Is.False);
            });
        }

        [TestCase(NodeIdAssignmentMode.Numeric)]
        [TestCase(NodeIdAssignmentMode.String)]
        [TestCase(NodeIdAssignmentMode.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque)]
        public void EveryModeIsStableAcrossAssigners(NodeIdAssignmentMode mode)
        {
            NodeId first = new DefaultNodeIdFactory(mode, kNamespaceIndex)
                .New(m_context, CreateChild(new NodeId("Root", kNamespaceIndex), "Child"));

            NodeId second = new DefaultNodeIdFactory(mode, kNamespaceIndex)
                .New(m_context, CreateChild(new NodeId("Root", kNamespaceIndex), "Child"));

            Assert.That(second, Is.EqualTo(first));
        }

        [TestCase(NodeIdAssignmentMode.Numeric)]
        [TestCase(NodeIdAssignmentMode.String)]
        [TestCase(NodeIdAssignmentMode.Guid)]
        [TestCase(NodeIdAssignmentMode.Opaque)]
        public void EveryModeSeparatesSiblingsAndBranches(NodeIdAssignmentMode mode)
        {
            var factory = new DefaultNodeIdFactory(mode, kNamespaceIndex);
            var minted = new HashSet<NodeId>(NodeIdComparer.Default);

            for (int i = 0; i < 200; i++)
            {
                var parentNodeId = new NodeId(
                    "Root" + (i % 10).ToString(CultureInfo.InvariantCulture),
                    kNamespaceIndex);
                BaseObjectState node = CreateChild(
                    parentNodeId,
                    "Child" + i.ToString(CultureInfo.InvariantCulture));

                Assert.That(
                    minted.Add(factory.New(m_context, node)),
                    Is.True,
                    $"Duplicate NodeId minted in mode {mode}.");
            }
        }

        [Test]
        public void GuidModeMarksTheIdentifierAsNameBased()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Guid, kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");

            Assert.That(
                factory.New(m_context, node).TryGetValue(out Guid identifier),
                Is.True);
            byte[] bytes = identifier.ToByteArray();

            Assert.Multiple(() =>
            {
                // ToByteArray writes the first three fields little endian, so
                // the version nibble lands in byte 7 and the variant in byte 8.
                Assert.That(bytes[7] & 0xF0, Is.EqualTo(0x80), "version");
                Assert.That(bytes[8] & 0xC0, Is.EqualTo(0x80), "variant");
            });
        }

        [Test]
        public void OpaqueModeMintsSixteenBytes()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Opaque, kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");

            Assert.That(
                factory.New(m_context, node).TryGetValue(out ByteString identifier),
                Is.True);

            Assert.That(identifier.Length, Is.EqualTo(16));
        }

        [Test]
        public void AChildIsMintedIntoItsParentsNamespace()
        {
            // the browse name's namespace names the type that declared the
            // child, so it must not decide where the instance lives.
            var factory = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.String,
                kNamespaceIndex);
            BaseObjectState node = CreateChild(
                new NodeId("Root", kOtherNamespaceIndex),
                new QualifiedName("Child", 0));

            NodeId nodeId = factory.New(m_context, node);

            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kOtherNamespaceIndex));
        }

        [Test]
        public void ARootIsMintedIntoTheDefaultNamespace()
        {
            var factory = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.String,
                kOtherNamespaceIndex);
            BaseObjectState node = CreateChild(
                ObjectIds.ObjectsFolder,
                new QualifiedName("Root", kNamespaceIndex));

            NodeId nodeId = factory.New(m_context, node);

            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kOtherNamespaceIndex));
        }

        [Test]
        public void WithDefaultNamespaceIndexRebasesWithoutMutating()
        {
            var original = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.Numeric,
                kNamespaceIndex);

            DefaultNodeIdFactory rebased = original.WithDefaultNamespaceIndex(
                kOtherNamespaceIndex);

            Assert.Multiple(() =>
            {
                Assert.That(original.DefaultNamespaceIndex, Is.EqualTo(kNamespaceIndex));
                Assert.That(rebased.DefaultNamespaceIndex, Is.EqualTo(kOtherNamespaceIndex));
                Assert.That(rebased.Mode, Is.EqualTo(NodeIdAssignmentMode.Numeric));
                Assert.That(
                    original.WithDefaultNamespaceIndex(kNamespaceIndex),
                    Is.SameAs(original));
            });
        }

        [Test]
        public void ANodeWithoutABrowseNameCannotBeAssigned()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var node = new BaseObjectState(null);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => factory.New(m_context, node));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public void AParentWithoutANodeIdIsRejectedRatherThanTreatedAsARoot()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            BaseObjectState node = CreateChild(NodeId.Null, "Child");

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => factory.New(m_context, node));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void TheDefaultModeIsString()
        {
            Assert.That(new DefaultNodeIdFactory().Mode, Is.EqualTo(NodeIdAssignmentMode.String));
        }

        /// <summary>
        /// Creates a child of a parent that carries the specified NodeId.
        /// </summary>
        private static BaseObjectState CreateChild(NodeId parentNodeId, string browseName)
        {
            return CreateChild(parentNodeId, new QualifiedName(browseName, kNamespaceIndex));
        }

        /// <summary>
        /// Creates a child of a parent that carries the specified NodeId.
        /// </summary>
        private static BaseObjectState CreateChild(NodeId parentNodeId, QualifiedName browseName)
        {
            var parent = new BaseObjectState(null) { NodeId = parentNodeId };

            return new BaseObjectState(parent) { BrowseName = browseName };
        }
    }
}
