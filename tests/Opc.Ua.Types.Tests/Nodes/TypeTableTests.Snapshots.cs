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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    /// <summary>
    /// Contains tests for type table.
    /// </summary>
    public partial class TypeTableTests
    {
        /// <summary>
        /// Verifies that the unchanged-capture shortcut still produces independently owned hierarchy storage.
        /// </summary>
        [Test]
        public void UnchangedAdditiveMergeOwnsItsHierarchy()
        {
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            TypeTable merged = m_typeTable.PrepareAdditiveMerge(proposed, source, revision);
            merged.Remove(s_grandchildTypeId);

            Assert.That(proposed.IsKnown(s_grandchildTypeId), Is.True);
            Assert.That(m_typeTable.IsKnown(s_grandchildTypeId), Is.True);
        }

        /// <summary>
        /// Verifies that existing deleted ancestors remain usable without resurrecting their registrations.
        /// </summary>
        [Test]
        public void AdditiveMergePreservesExistingDeletedAncestors()
        {
            m_typeTable.Remove(s_childTypeId);
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            NodeId added = new(5000);
            proposed.AddSubtype(added, s_rootTypeId);
            TypeTable merged = m_typeTable.PrepareAdditiveMerge(proposed, source, revision);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(merged.IsKnown(s_childTypeId), Is.False);
                Assert.That(merged.FindSuperType(s_grandchildTypeId), Is.EqualTo(s_childTypeId));
                Assert.That(merged.IsTypeOf(s_grandchildTypeId, s_rootTypeId), Is.True);
                Assert.That(merged.IsTypeOf(added, s_rootTypeId), Is.True);
            }
        }

        /// <summary>
        /// Verifies that additive preparation preserves serving registrations and independent ownership.
        /// </summary>
        [Test]
        public void AdditiveMergePreservesServingRegistrationsAndIndependentOwnership()
        {
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            proposed.AddSubtype(s_dataTypeId, NodeId.Null);
            Assert.That(proposed.AddEncoding(s_dataTypeId, s_encodingId2), Is.True);
            proposed.Remove(s_refChildTypeId);
            NodeId added = new(5000);
            proposed.AddSubtype(added, s_grandchildTypeId);

            TypeTable merged = m_typeTable.PrepareAdditiveMerge(proposed, source, revision);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(merged.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
                Assert.That(merged.FindDataTypeId(s_encodingId2), Is.EqualTo(s_dataTypeId));
                Assert.That(merged.FindReferenceType(s_refChildBrowseName), Is.EqualTo(s_refChildTypeId));
                Assert.That(merged.IsTypeOf(added, s_rootTypeId), Is.True);
                Assert.That(m_typeTable.IsKnown(added), Is.False);
                Assert.That(m_typeTable.FindDataTypeId(s_encodingId2).IsNull, Is.True);
                Assert.That(proposed.FindDataTypeId(s_encodingId1).IsNull, Is.True);
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
            }
            merged.Remove(added);
            Assert.That(proposed.IsKnown(added), Is.True);
        }

        /// <summary>
        /// Verifies that a serving hierarchy cannot be redefined by a staged graph.
        /// </summary>
        [Test]
        public void AdditiveMergeRejectsServingSupertypeChanges()
        {
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            proposed.AddSubtype(s_grandchildTypeId, s_rootTypeId);

            Assert.Throws<InvalidOperationException>(() =>
                m_typeTable.PrepareAdditiveMerge(proposed, source, revision));
            Assert.That(m_typeTable.FindSuperType(s_grandchildTypeId), Is.EqualTo(s_childTypeId));
        }

        /// <summary>
        /// Verifies that reference names cannot be renamed or reassigned while serving.
        /// </summary>
        /// <param name="rename">Whether to rename an existing reference rather than reuse its name.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsServingReferenceNameChanges(bool rename)
        {
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            proposed.AddReferenceSubtype(
                rename ? s_refTypeId : new NodeId(5000),
                NodeId.Null,
                rename ? new QualifiedName("RenamedReference") : s_refBrowseName);

            Assert.Throws<InvalidOperationException>(() =>
                m_typeTable.PrepareAdditiveMerge(proposed, source, revision));
            Assert.That(m_typeTable.FindReferenceType(s_refBrowseName), Is.EqualTo(s_refTypeId));
        }

        /// <summary>
        /// Verifies that a serving encoding cannot be reassigned to another data type.
        /// </summary>
        [Test]
        public void AdditiveMergeRejectsServingEncodingReassignment()
        {
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            Assert.That(proposed.AddEncoding(s_rootTypeId, s_encodingId1), Is.True);

            Assert.Throws<InvalidOperationException>(() =>
                m_typeTable.PrepareAdditiveMerge(proposed, source, revision));
            Assert.That(m_typeTable.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
        }

        /// <summary>
        /// Verifies that entirely new declarations cannot introduce a hierarchy cycle.
        /// </summary>
        [Test]
        public void AdditiveMergeRejectsCyclicNewTypes()
        {
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            NodeId first = new(5000);
            NodeId second = new(5001);
            proposed.AddSubtype(first, NodeId.Null);
            proposed.AddSubtype(second, first);
            proposed.AddSubtype(first, second);

            Assert.Throws<InvalidOperationException>(() =>
                m_typeTable.PrepareAdditiveMerge(proposed, source, revision));
            Assert.That(m_typeTable.IsKnown(first), Is.False);
        }

        /// <summary>
        /// Verifies that preparation cannot merge against a different or changed source.
        /// </summary>
        /// <param name="changeRevision">Whether the revision rather than the source identity changes.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void AdditiveMergeRejectsChangedSource(bool changeRevision)
        {
            TypeTable proposed = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            if (changeRevision)
            {
                m_typeTable.AddSubtype(new NodeId(5000), NodeId.Null);
            }
            else
            {
                source = new TypeTable(m_namespaceTable);
            }
            Assert.Throws<InvalidOperationException>(() =>
                m_typeTable.PrepareAdditiveMerge(proposed, source, revision));
        }

        /// <summary>
        /// Verifies that snapshot owns hierarchy encodings and reference names.
        /// </summary>
        [Test]
        public void SnapshotOwnsHierarchyEncodingsAndReferenceNames()
        {
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            TypeTable snapshot = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);

            snapshot.AddSubtype(s_grandchildTypeId, s_rootTypeId);
            snapshot.Remove(s_refChildTypeId);
            Assert.That(snapshot.AddEncoding(s_dataTypeId, s_encodingId2), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(source, Is.SameAs(m_typeTable));
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
                Assert.That(snapshot.FindSuperType(s_grandchildTypeId), Is.EqualTo(s_rootTypeId));
                Assert.That(m_typeTable.FindSuperType(s_grandchildTypeId), Is.EqualTo(s_childTypeId));
                Assert.That(m_typeTable.FindSubTypes(s_childTypeId).ToList(), Does.Contain(s_grandchildTypeId));
                Assert.That(m_typeTable.FindReferenceType(s_refChildBrowseName), Is.EqualTo(s_refChildTypeId));
                Assert.That(snapshot.FindReferenceType(s_refChildBrowseName).IsNull, Is.True);
                Assert.That(snapshot.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
                Assert.That(snapshot.FindDataTypeId(s_encodingId2), Is.EqualTo(s_dataTypeId));
                Assert.That(m_typeTable.FindDataTypeId(s_encodingId2).IsNull, Is.True);
            }
            m_typeTable.Clear();
            Assert.That(snapshot.IsKnown(s_rootTypeId), Is.True);
            Assert.That(snapshot.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
        }

        /// <summary>
        /// Verifies that snapshot preserves deleted ancestor links without sharing them.
        /// </summary>
        [Test]
        public void SnapshotPreservesDeletedAncestorLinksWithoutSharingThem()
        {
            m_typeTable.Remove(s_childTypeId);
            TypeTable snapshot = m_typeTable.CaptureSnapshot(out _, out _);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(snapshot.IsKnown(s_childTypeId), Is.False);
                Assert.That(snapshot.FindSuperType(s_grandchildTypeId), Is.EqualTo(s_childTypeId));
                Assert.That(snapshot.IsTypeOf(s_grandchildTypeId, s_childTypeId), Is.False);
                Assert.That(snapshot.IsTypeOf(s_grandchildTypeId, s_rootTypeId), Is.True);
            }
            snapshot.Remove(s_rootTypeId);
            Assert.That(snapshot.IsTypeOf(s_grandchildTypeId, s_rootTypeId), Is.False);
            Assert.That(m_typeTable.IsTypeOf(s_grandchildTypeId, s_rootTypeId), Is.True);
        }

        /// <summary>
        /// Verifies that cached staging handles observe compatible publication and future additions.
        /// </summary>
        [Test]
        public async Task PublishedStagingHandlesObserveCanonicalAdditionsAsync()
        {
            TypeTable staged = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            NodeId child = new(5000);
            NodeId reference = new(5001);
            QualifiedName name = new("SelectedReference");
            staged.AddSubtype(child, s_childTypeId);
            staged.AddReferenceSubtype(reference, s_refTypeId, name);
            Assert.That(staged.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            Assert.That(m_typeTable.IsKnown(child), Is.False);
            TypeTable merged = m_typeTable.PrepareAdditiveMerge(staged, source, revision);
            using (TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision))
            {
                publication.Complete(merged);
                staged.BindTo(m_typeTable);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(m_typeTable.IsKnown(child), Is.True);
                Assert.That(m_typeTable.IsKnown(new ExpandedNodeId(child)), Is.True);
                Assert.That(m_typeTable.FindSuperType(child), Is.EqualTo(s_childTypeId));
                Assert.That(m_typeTable.FindSuperType(new ExpandedNodeId(child)), Is.EqualTo(s_childTypeId));
                Assert.That(m_typeTable.FindSubTypes(s_childTypeId).ToList(), Does.Contain(child));
                Assert.That(m_typeTable.IsTypeOf(child, s_rootTypeId), Is.True);
                Assert.That(m_typeTable.IsTypeOf(new ExpandedNodeId(child), s_rootTypeId), Is.True);
                Assert.That(m_typeTable.FindReferenceTypeName(reference), Is.EqualTo(name));
                Assert.That(m_typeTable.FindReferenceType(name), Is.EqualTo(reference));
                Assert.That(m_typeTable.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
                Assert.That(m_typeTable.FindDataTypeId(new ExpandedNodeId(s_encodingId1)), Is.EqualTo(s_dataTypeId));
                Assert.That(m_typeTable.IsEncodingOf(s_encodingId1, s_dataTypeId), Is.True);
                var extension = new ExtensionObject(new ExpandedNodeId(s_encodingId1));
                Assert.That(m_typeTable.IsEncodingFor(s_dataTypeId, extension), Is.True);
                Assert.That(m_typeTable.IsEncodingFor(s_dataTypeId, new Variant(extension)), Is.True);
            }
            Assert.That(await m_typeTable.FindSuperTypeAsync(child, CancellationToken.None).ConfigureAwait(false),
                Is.EqualTo(s_childTypeId));
            Assert.That(await m_typeTable.FindSuperTypeAsync(new ExpandedNodeId(child), CancellationToken.None)
                .ConfigureAwait(false), Is.EqualTo(s_childTypeId));
            m_typeTable.AddSubtype(new NodeId(6000), child);
            Assert.That(staged.IsTypeOf(new NodeId(6000), s_rootTypeId), Is.True);
            staged.Detach();
            staged.Clear();
            Assert.That(m_typeTable.IsKnown(child), Is.True);
            m_typeTable.Remove(reference);
            Assert.That(m_typeTable.FindReferenceType(name).IsNull, Is.True);
        }

        /// <summary>
        /// Verifies that publication invalidates the captured revision, not the canonical identity.
        /// </summary>
        [Test]
        public void SharedPublicationInvalidatesCapturedRevision()
        {
            TypeTable next = m_typeTable.CaptureSnapshot(out TypeTable owner, out long revision);
            Assert.That(m_typeTable.IsCurrentSnapshot(owner, revision), Is.True);
            using TypeTable.Publication publication = m_typeTable.BeginPublication(owner, revision);
            publication.Complete(next);
            Assert.That(m_typeTable.IsCurrentSnapshot(owner, revision), Is.False);
        }

        /// <summary>
        /// Verifies that unrelated private copies never acquire canonical storage implicitly.
        /// </summary>
        [Test]
        public void PrivateCopiesDoNotObserveLaterServingMutations()
        {
            TypeTable selected = m_typeTable.CaptureSnapshot(out _, out _);
            NodeId addedId = new(6000);
            m_typeTable.AddSubtype(addedId, s_rootTypeId);
            Assert.That(selected.IsKnown(addedId), Is.False);
            Assert.That(m_typeTable.IsKnown(addedId), Is.True);
        }

        /// <summary>
        /// Verifies that serving mutation invalidates captured type revision.
        /// </summary>
        [TestCase("clear")]
        [TestCase("node")]
        [TestCase("subtype")]
        [TestCase("reference")]
        [TestCase("encoding")]
        [TestCase("remove")]
        public void ServingMutationInvalidatesCapturedTypeRevision(string operation)
        {
            _ = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
            MutateTypeTable(operation);
            Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.False);
        }

        /// <summary>
        /// Verifies that publication protects type image until decision.
        /// </summary>
        /// <param name="publish">The publish flag for this test case.</param>
        [TestCase("clear", false)]
        [TestCase("clear", true)]
        [TestCase("node", false)]
        [TestCase("node", true)]
        [TestCase("subtype", false)]
        [TestCase("subtype", true)]
        [TestCase("reference", false)]
        [TestCase("reference", true)]
        [TestCase("encoding", false)]
        [TestCase("encoding", true)]
        [TestCase("remove", false)]
        [TestCase("remove", true)]
        public void PublicationProtectsTypeImageUntilDecision(string operation, bool publish)
        {
            TypeTable next = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            using (TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision))
            {
                Assert.Throws<InvalidOperationException>(() => MutateTypeTable(operation));
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
                Assert.That(m_typeTable.FindSuperType(s_childTypeId), Is.EqualTo(s_rootTypeId));
                if (publish)
                {
                    publication.Complete(next);
                }
            }
            MutateTypeTable(operation);
            Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.False);
        }

        /// <summary>
        /// Verifies that publication allows idempotent encoding registration.
        /// </summary>
        [Test]
        public void PublicationAllowsIdempotentEncodingRegistration()
        {
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            _ = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            using TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision);
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
            Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
            Assert.That(m_typeTable.FindDataTypeId(s_encodingId1), Is.EqualTo(s_dataTypeId));
        }

        /// <summary>
        /// Verifies that publication rejects another owner and releases exactly once.
        /// </summary>
        [Test]
        public void PublicationRejectsAnotherOwnerAndReleasesExactlyOnce()
        {
            _ = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            using TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision);
            Assert.Throws<InvalidOperationException>(() => m_typeTable.BeginPublication(source, revision));
            publication.Dispose();
            publication.Dispose();
            Assert.Throws<ObjectDisposedException>(() => publication.Complete(m_typeTable));
            m_typeTable.AddSubtype(new NodeId(6000), s_rootTypeId);
            Assert.That(m_typeTable.FindSuperType(new NodeId(6000)), Is.EqualTo(s_rootTypeId));
        }

        private void MutateTypeTable(string operation)
        {
            switch (operation)
            {
                case "clear":
                    m_typeTable.Clear();
                    break;
                case "node":
                    m_typeTable.Add(CreateMockNode(new NodeId(6000), NodeClass.ObjectType).Object);
                    break;
                case "subtype":
                    m_typeTable.AddSubtype(new NodeId(6000), s_rootTypeId);
                    break;
                case "reference":
                    m_typeTable.AddReferenceSubtype(new NodeId(6000), s_refTypeId, new QualifiedName("NewReference"));
                    break;
                case "encoding":
                    Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
                    break;
                case "remove":
                    m_typeTable.Remove(s_childTypeId);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        /// <summary>
        /// Verifies that two cached handles keep observing one canonical graph.
        /// </summary>
        [Test]
        public void MultipleCachedHandlesShareCanonicalHierarchy()
        {
            TypeTable selected = m_typeTable.CaptureSnapshot(out _, out _);
            selected.BindTo(m_typeTable);
            NodeId child = new(5100);
            m_typeTable.AddSubtype(child, s_childTypeId);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(m_typeTable.FindSuperType(child), Is.EqualTo(s_childTypeId));
                Assert.That(selected.IsKnown(child), Is.True);
            }
            TypeTable next = m_typeTable.CaptureSnapshot(out _, out _);
            next.BindTo(m_typeTable);
            selected.AddSubtype(new NodeId(5101), child);
            Assert.That(next.IsKnown(new NodeId(5101)), Is.True);
        }

        /// <summary>
        /// Verifies that detaching a retired handle does not detach another live handle.
        /// </summary>
        [Test]
        public void DetachingOneHandlePreservesOtherLiveHandles()
        {
            TypeTable first = m_typeTable.CaptureSnapshot(out _, out _);
            TypeTable second = m_typeTable.CaptureSnapshot(out _, out _);
            first.BindTo(m_typeTable);
            second.BindTo(m_typeTable);
            first.Detach();
            first.Remove(s_childTypeId);
            Assert.That(second.IsKnown(s_childTypeId), Is.True);
            Assert.That(m_typeTable.IsKnown(s_childTypeId), Is.True);
        }

        /// <summary>
        /// Verifies that a cached handle can be safely rebound to the same canonical storage.
        /// </summary>
        [Test]
        public void RebindingCanonicalHandlePreservesRegistrations()
        {
            m_typeTable.BindTo(m_typeTable);
            Assert.That(m_typeTable.IsKnown(s_childTypeId), Is.True);
        }
    }
}
