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
    public partial class TypeTableTests
    {
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

        [Test]
        public async Task SelectedTypeImageRoutesReadsAndMutationsAsync()
        {
            TypeTable selected = m_typeTable.CaptureSnapshot(out _, out _);
            m_typeTable.SetViewSelector(() => selected);
            NodeId child = new(5000);
            NodeId reference = new(5001);
            QualifiedName name = new("SelectedReference");
            m_typeTable.AddSubtype(child, s_childTypeId);
            m_typeTable.AddReferenceSubtype(reference, s_refTypeId, name);
            Assert.That(m_typeTable.AddEncoding(s_dataTypeId, s_encodingId1), Is.True);
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
            m_typeTable.Add(CreateMockNode(new NodeId(6000), NodeClass.ObjectType).Object);
            Assert.That(selected.IsKnown(new NodeId(6000)), Is.True);
            m_typeTable.Remove(reference);
            Assert.That(selected.FindReferenceType(name).IsNull, Is.True);
            m_typeTable.Clear();
            Assert.That(selected.IsKnown(s_rootTypeId), Is.False);
            selected = null;
            Assert.That(m_typeTable.IsKnown(s_rootTypeId), Is.True);
            Assert.That(m_typeTable.IsKnown(child), Is.False);
            Assert.That(m_typeTable.IsKnown(new NodeId(6000)), Is.False);
            Assert.That(m_typeTable.FindDataTypeId(s_encodingId1).IsNull, Is.True);
        }

        [Test]
        public void SelectedImageChangeInvalidatesCapturedOwner()
        {
            TypeTable selected = m_typeTable.CaptureSnapshot(out _, out _);
            m_typeTable.SetViewSelector(() => selected);
            TypeTable next = m_typeTable.CaptureSnapshot(out TypeTable owner, out long revision);
            Assert.That(m_typeTable.IsCurrentSnapshot(owner, revision), Is.True);
            selected = next;
            Assert.That(m_typeTable.IsCurrentSnapshot(owner, revision), Is.False);
        }

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
            _ = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            using (TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision))
            {
                Assert.Throws<InvalidOperationException>(() => MutateTypeTable(operation));
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
                Assert.That(m_typeTable.FindSuperType(s_childTypeId), Is.EqualTo(s_rootTypeId));
                if (publish)
                {
                    publication.Complete();
                }
            }
            if (publish)
            {
                Assert.Throws<InvalidOperationException>(() => MutateTypeTable(operation));
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.True);
            }
            else
            {
                MutateTypeTable(operation);
                Assert.That(m_typeTable.IsCurrentSnapshot(source, revision), Is.False);
            }
        }

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

        [Test]
        public void PublicationRejectsAnotherOwnerAndReleasesExactlyOnce()
        {
            _ = m_typeTable.CaptureSnapshot(out TypeTable source, out long revision);
            using TypeTable.Publication publication = m_typeTable.BeginPublication(source, revision);
            Assert.Throws<InvalidOperationException>(() => m_typeTable.BeginPublication(source, revision));
            publication.Dispose();
            publication.Dispose();
            Assert.Throws<ObjectDisposedException>(publication.Complete);
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
    }
}
