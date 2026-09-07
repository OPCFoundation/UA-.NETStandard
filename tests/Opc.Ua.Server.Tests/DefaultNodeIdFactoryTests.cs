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
        public void NewKeepsAnAuthoredNodeIdOnANodeThatStandsOnItsOwn()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var authored = new NodeId("Authored", kNamespaceIndex);
            var node = new BaseObjectState(null)
            {
                NodeId = authored,
                BrowseName = new QualifiedName("Authored", kNamespaceIndex)
            };

            Assert.That(factory.New(m_context, node), Is.EqualTo(authored));
        }

        [Test]
        public void NewRebasesAChildThatStillCarriesADeclarationNodeId()
        {
            // a child arrives here through AssignNodeIds, walking a subtree
            // copied from a type declaration. The declaration lives in the
            // model's own namespace, which is not this NodeManager's, so the
            // identifier is not one the caller can have chosen.
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var declaration = new NodeId("TypeDeclaration", kOtherNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");
            node.NodeId = declaration;

            NodeId nodeId = factory.New(m_context, node);

            Assert.Multiple(() =>
            {
                Assert.That(nodeId, Is.Not.EqualTo(declaration));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
                Assert.That(nodeId.IdentifierAsString, Does.Contain("Child"));
            });
        }

        [Test]
        public void NewKeepsAChildNodeIdThatIsAlreadyInThisNamespace()
        {
            // NodeState.Create hands the root the NodeId it was given and
            // then runs the assignment pass over the subtree, so a caller
            // naming a parented node explicitly arrives here with an
            // identifier that is already ours. Re-minting it would rename
            // the node out from under the caller.
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var authored = new NodeId("Pump1.Events.Cavitation.Alarm", kNamespaceIndex);
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
        public void EveryNodeIsMintedIntoTheConfiguredNamespace()
        {
            // neither the parent's namespace nor the browse name's decides
            // this: a parent can belong to a companion-specification model
            // whose identifiers are fixed by its NodeSet, and a browse name
            // only names the type that declared the child.
            var factory = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.String,
                kNamespaceIndex);
            BaseObjectState node = CreateChild(
                new NodeId("Root", kOtherNamespaceIndex),
                new QualifiedName("Child", 0));

            NodeId nodeId = factory.New(m_context, node);

            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
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
        public void ANodeWithoutABrowseNameFallsBackToTheCounter()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var node = new BaseObjectState(null);

            NodeId nodeId = factory.New(m_context, node);

            Assert.Multiple(() =>
            {
                Assert.That(factory.HasDerivablePath(node), Is.False);
                Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
                Assert.That(nodeId.IsNull, Is.False);
            });
        }

        [Test]
        public void AParentWithoutANodeIdFallsBackToTheCounter()
        {
            // a transient parent gives the child no stable path, so deriving
            // one would silently alias it onto a root.
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            BaseObjectState node = CreateChild(NodeId.Null, "Child");

            NodeId nodeId = factory.New(m_context, node);

            Assert.Multiple(() =>
            {
                Assert.That(factory.HasDerivablePath(node), Is.False);
                Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
                Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
            });
        }

        [Test]
        public void CounterModeMintsUniqueNumericIdentifiers()
        {
            var factory = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.Counter,
                kNamespaceIndex);
            var minted = new HashSet<NodeId>(NodeIdComparer.Default);

            for (int i = 0; i < 500; i++)
            {
                // the same browse path every time: a counter is what keeps
                // repeated paths distinct.
                NodeId nodeId = factory.New(
                    m_context,
                    CreateChild(new NodeId("Root", kNamespaceIndex), "Child"));

                Assert.Multiple(() =>
                {
                    Assert.That(nodeId.IdType, Is.EqualTo(IdType.Numeric));
                    Assert.That(nodeId.NamespaceIndex, Is.EqualTo(kNamespaceIndex));
                    Assert.That(minted.Add(nodeId), Is.True, "Duplicate NodeId minted.");
                });
            }
        }

        [Test]
        public void CounterModeStaysClearOfAuthoredIdentifiers()
        {
            // a NodeManager usually mints into the namespace its NodeSet
            // occupies. Identifiers below this bound belong to the model.
            const uint authoredCeiling = 0x40000000;
            var factory = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.Counter,
                kNamespaceIndex);

            for (int i = 0; i < 100; i++)
            {
                Assert.That(
                    factory.NextCounterNodeId().TryGetValue(out uint identifier),
                    Is.True);
                Assert.That(identifier, Is.GreaterThanOrEqualTo(authoredCeiling));
            }
        }

        [Test]
        public void WithModeSwitchesTypeWithoutMutating()
        {
            var original = new DefaultNodeIdFactory(
                NodeIdAssignmentMode.String,
                kNamespaceIndex);

            DefaultNodeIdFactory switched = original.WithMode(NodeIdAssignmentMode.Counter);

            Assert.Multiple(() =>
            {
                Assert.That(original.Mode, Is.EqualTo(NodeIdAssignmentMode.String));
                Assert.That(switched.Mode, Is.EqualTo(NodeIdAssignmentMode.Counter));
                Assert.That(switched.DefaultNamespaceIndex, Is.EqualTo(kNamespaceIndex));
                Assert.That(
                    original.WithMode(NodeIdAssignmentMode.String),
                    Is.SameAs(original));
            });
        }

        [Test]
        public void TheDefaultModeIsNumeric()
        {
            Assert.That(new DefaultNodeIdFactory().Mode, Is.EqualTo(NodeIdAssignmentMode.Numeric));
        }

        [Test]
        public void APathLongerThanTheStackBufferIsBuiltIdentically()
        {
            // long enough to push the builder off the stack and onto the
            // pool. The pooled buffer is rented, so it is longer than the
            // path and carries whatever the previous tenant left behind -
            // this pins that only the written part is read back.
            string longName = new('n', 512);

            string path = DefaultNodeIdFactory.CreateCanonicalPath(
                new NodeId("Root", kNamespaceIndex),
                new QualifiedName(longName, kNamespaceIndex),
                kNamespaceIndex,
                m_namespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(path, Does.EndWith(":512:" + longName));
                Assert.That(path, Does.StartWith("v1:10:l:6:s=Root:"));
                Assert.That(path, Has.Length.EqualTo(path.TrimEnd('\0').Length));
            });
        }

        [Test]
        public void MintingTheSamePathTwiceIsNotACollision()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Numeric, kNamespaceIndex);
            BaseObjectState node = CreateChild(new NodeId("Root", kNamespaceIndex), "Child");

            NodeId first = factory.New(m_context, node);

            // AssignNodeIds walks a subtree on every create pass, so the same
            // node reaches the factory repeatedly. That must not read as two
            // paths landing on one identifier.
            node.NodeId = NodeId.Null;
            NodeId second = factory.New(m_context, node);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        [Category("LongRunning")]
        public void TwoBrowsePathsOnOneNumericIdentifierAreReported()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.Numeric, kNamespaceIndex);
            var parent = new NodeId("Root", kNamespaceIndex);

            // A 32 bit identifier is a birthday problem: distinct browse
            // paths are expected to land on one after roughly 2^16 of them.
            // That is the whole reason the guard exists, so the test provokes
            // the real thing rather than a synthetic stand-in. Not finding
            // one within this many is itself a failure - it would mean the
            // identifiers are not spread over the space they claim to be.
            ServiceResultException collision = null;

            for (int ii = 0; ii < kCollisionSearchLimit && collision is null; ii++)
            {
                try
                {
                    factory.New(
                        m_context,
                        CreateChild(parent, "Node" + ii.ToString(CultureInfo.InvariantCulture)));
                }
                catch (ServiceResultException exception)
                {
                    collision = exception;
                }
            }

            Assert.That(
                collision,
                Is.Not.Null,
                "no two browse paths shared an identifier within the search limit");
            Assert.Multiple(() =>
            {
                Assert.That(collision.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));

                // the identifier alone says nothing about which node was
                // refused, so the message has to carry the browse path.
                Assert.That(collision.Message, Does.Contain("v1:"));
                Assert.That(collision.Message, Does.Contain("NodeIdAssignmentMode.String"));
            });
        }

        [Test]
        public void AModeThatCannotCollideKeepsNoRecord()
        {
            var factory = new DefaultNodeIdFactory(NodeIdAssignmentMode.String, kNamespaceIndex);
            var parent = new NodeId("Root", kNamespaceIndex);

            NodeId first = factory.New(m_context, CreateChild(parent, "First"));
            NodeId second = factory.New(m_context, CreateChild(parent, "Second"));

            // String keeps the whole path, so two paths can never share an
            // identifier and there is nothing to check against.
            Assert.That(second, Is.Not.EqualTo(first));
        }

        /// <summary>
        /// How many browse paths the collision test mints before giving up.
        /// A 32 bit space is expected to collide within ~77k, so reaching
        /// this bound without one would be evidence of a defect.
        /// </summary>
        private const int kCollisionSearchLimit = 1_000_000;

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
