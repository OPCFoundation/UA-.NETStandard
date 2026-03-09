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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    /// <summary>
    /// Coverage tests for <see cref="TypeTable"/>.
    /// </summary>
    [TestFixture]
    [Category("TypeTable")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeTableTests
    {
        private static readonly NodeId RootTypeId = new NodeId(1000);
        private static readonly NodeId ChildTypeId = new NodeId(1001);
        private static readonly NodeId GrandchildTypeId = new NodeId(1002);
        private static readonly NodeId UnknownTypeId = new NodeId(9999);

        private static readonly NodeId RefTypeId = new NodeId(2000);
        private static readonly NodeId RefChildTypeId = new NodeId(2001);

        private static readonly NodeId DataTypeId = new NodeId(3000);
        private static readonly NodeId EncodingId1 = new NodeId(3001);
        private static readonly NodeId EncodingId2 = new NodeId(3002);

        private static readonly QualifiedName RefBrowseName = new QualifiedName("TestReference");
        private static readonly QualifiedName RefChildBrowseName = new QualifiedName("TestChildReference");

        private NamespaceTable m_namespaceTable;
        private TypeTable m_typeTable;

        [SetUp]
        public void SetUp()
        {
            m_namespaceTable = new NamespaceTable();
            m_typeTable = new TypeTable(m_namespaceTable);

            // Build a basic type hierarchy:
            //   RootTypeId (root) -> ChildTypeId -> GrandchildTypeId
            m_typeTable.AddSubtype(RootTypeId, NodeId.Null);
            m_typeTable.AddSubtype(ChildTypeId, RootTypeId);
            m_typeTable.AddSubtype(GrandchildTypeId, ChildTypeId);

            // Add reference types with browse names
            m_typeTable.AddReferenceSubtype(RefTypeId, NodeId.Null, RefBrowseName);
            m_typeTable.AddReferenceSubtype(RefChildTypeId, RefTypeId, RefChildBrowseName);

            // Add a data type (no encoding yet, tests add as needed)
            m_typeTable.AddSubtype(DataTypeId, NodeId.Null);
        }
        [Test]
        public void AddSubtypeCreatesRootType()
        {
            var id = new NodeId(5000);
            m_typeTable.AddSubtype(id, NodeId.Null);

            Assert.That(m_typeTable.IsKnown(id), Is.True);
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void AddSubtypeCreatesChildType()
        {
            var parentId = new NodeId(5000);
            var childId = new NodeId(5001);
            m_typeTable.AddSubtype(parentId, NodeId.Null);
            m_typeTable.AddSubtype(childId, parentId);

            Assert.That(m_typeTable.IsKnown(childId), Is.True);
            Assert.That(m_typeTable.FindSuperType(childId), Is.EqualTo(parentId));
        }

        [Test]
        public void AddSubtypeThrowsForUnknownSuperType()
        {
            Assert.Throws<ServiceResultException>(() =>
                m_typeTable.AddSubtype(new NodeId(5000), new NodeId(9998)));
        }

        [Test]
        public void AddSubtypeUpdatesExistingEntryAndRemovesOldEncodings()
        {
            var dtId = new NodeId(5000);
            var encId = new NodeId(5001);
            m_typeTable.AddSubtype(dtId, NodeId.Null);
            m_typeTable.AddEncoding(dtId, new ExpandedNodeId(encId));
            Assert.That(m_typeTable.FindDataTypeId(encId), Is.EqualTo(dtId));

            // Re-add the same type — old encodings should be removed from the lookup
            m_typeTable.AddSubtype(dtId, NodeId.Null);
            Assert.That(m_typeTable.FindDataTypeId(encId), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void AddReferenceSubtypeRegistersTypeWithBrowseName()
        {
            var refId = new NodeId(4000);
            var browseName = new QualifiedName("CustomRef");
            m_typeTable.AddReferenceSubtype(refId, NodeId.Null, browseName);

            Assert.That(m_typeTable.IsKnown(refId), Is.True);
            Assert.That(m_typeTable.FindReferenceType(browseName), Is.EqualTo(refId));
            Assert.That(m_typeTable.FindReferenceTypeName(refId), Is.EqualTo(browseName));
        }

        [Test]
        public void AddReferenceSubtypeThrowsForUnknownSuperType()
        {
            Assert.Throws<ServiceResultException>(() =>
                m_typeTable.AddReferenceSubtype(
                    new NodeId(4000),
                    new NodeId(9998),
                    new QualifiedName("BadRef")));
        }
        [Test]
        public void AddEncodingReturnsFalseForNullEncodingId()
        {
            Assert.That(m_typeTable.AddEncoding(DataTypeId, ExpandedNodeId.Null), Is.False);
        }

        [Test]
        public void AddEncodingReturnsFalseForUnknownNamespaceUri()
        {
            var enc = new ExpandedNodeId(3001, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.AddEncoding(DataTypeId, enc), Is.False);
        }

        [Test]
        public void AddEncodingReturnsFalseForUnknownDataType()
        {
            Assert.That(
                m_typeTable.AddEncoding(UnknownTypeId, new ExpandedNodeId(EncodingId1)),
                Is.False);
        }

        [Test]
        public void AddEncodingReturnsTrueAndRegistersEncoding()
        {
            Assert.That(
                m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1)),
                Is.True);
            Assert.That(m_typeTable.FindDataTypeId(EncodingId1), Is.EqualTo(DataTypeId));
        }

        [Test]
        public void AddEncodingAppendsMultipleEncodings()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            Assert.That(
                m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId2)),
                Is.True);

            Assert.That(m_typeTable.FindDataTypeId(EncodingId1), Is.EqualTo(DataTypeId));
            Assert.That(m_typeTable.FindDataTypeId(EncodingId2), Is.EqualTo(DataTypeId));
        }
        [Test]
        public void IsKnownNodeIdReturnsFalseForNull()
        {
            Assert.That(m_typeTable.IsKnown(NodeId.Null), Is.False);
        }

        [Test]
        public void IsKnownNodeIdReturnsFalseForUnknown()
        {
            Assert.That(m_typeTable.IsKnown(UnknownTypeId), Is.False);
        }

        [Test]
        public void IsKnownNodeIdReturnsTrueForKnownType()
        {
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
            Assert.That(m_typeTable.IsKnown(ChildTypeId), Is.True);
            Assert.That(m_typeTable.IsKnown(GrandchildTypeId), Is.True);
        }
        [Test]
        public void IsKnownExpandedReturnsFalseForNull()
        {
            Assert.That(m_typeTable.IsKnown(ExpandedNodeId.Null), Is.False);
        }

        [Test]
        public void IsKnownExpandedReturnsFalseForNonZeroServerIndex()
        {
            var id = new ExpandedNodeId(1000, 0, null, 1);
            Assert.That(m_typeTable.IsKnown(id), Is.False);
        }

        [Test]
        public void IsKnownExpandedReturnsFalseForUnresolvableNamespace()
        {
            var id = new ExpandedNodeId(1000, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.IsKnown(id), Is.False);
        }

        [Test]
        public void IsKnownExpandedReturnsFalseForUnknownType()
        {
            ExpandedNodeId id = UnknownTypeId;
            Assert.That(m_typeTable.IsKnown(id), Is.False);
        }

        [Test]
        public void IsKnownExpandedReturnsTrueForKnownType()
        {
            ExpandedNodeId id = RootTypeId;
            Assert.That(m_typeTable.IsKnown(id), Is.True);
        }
        [Test]
        public void FindSuperTypeNodeIdReturnsNullForNullId()
        {
            Assert.That(m_typeTable.FindSuperType(NodeId.Null), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeNodeIdReturnsNullForUnknownId()
        {
            Assert.That(m_typeTable.FindSuperType(UnknownTypeId), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeNodeIdReturnsNullForRootType()
        {
            Assert.That(m_typeTable.FindSuperType(RootTypeId), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeNodeIdReturnsParent()
        {
            Assert.That(m_typeTable.FindSuperType(ChildTypeId), Is.EqualTo(RootTypeId));
        }

        [Test]
        public void FindSuperTypeNodeIdReturnsImmediateParent()
        {
            Assert.That(m_typeTable.FindSuperType(GrandchildTypeId), Is.EqualTo(ChildTypeId));
        }
        [Test]
        public void FindSuperTypeExpandedReturnsNullForNull()
        {
            Assert.That(m_typeTable.FindSuperType(ExpandedNodeId.Null), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeExpandedReturnsNullForNonZeroServerIndex()
        {
            var id = new ExpandedNodeId(1001, 0, null, 1);
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeExpandedReturnsNullForUnresolvableNamespace()
        {
            var id = new ExpandedNodeId(1001, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeExpandedReturnsNullForUnknownType()
        {
            ExpandedNodeId id = UnknownTypeId;
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeExpandedReturnsNullForRootType()
        {
            ExpandedNodeId id = RootTypeId;
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindSuperTypeExpandedReturnsParent()
        {
            ExpandedNodeId id = ChildTypeId;
            Assert.That(m_typeTable.FindSuperType(id), Is.EqualTo(RootTypeId));
        }
        [Test]
        public async Task FindSuperTypeAsyncExpandedReturnsParent()
        {
            ExpandedNodeId id = ChildTypeId;
            NodeId result = await m_typeTable.FindSuperTypeAsync(id, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(RootTypeId));
        }

        [Test]
        public async Task FindSuperTypeAsyncNodeIdReturnsParent()
        {
            NodeId result = await m_typeTable.FindSuperTypeAsync(ChildTypeId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(RootTypeId));
        }

        [Test]
        public async Task FindSuperTypeAsyncExpandedReturnsNullForNull()
        {
            NodeId result = await m_typeTable.FindSuperTypeAsync(ExpandedNodeId.Null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public async Task FindSuperTypeAsyncNodeIdReturnsNullForNull()
        {
            NodeId result = await m_typeTable.FindSuperTypeAsync(NodeId.Null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }
        [Test]
        public void FindSubTypesReturnsEmptyForNull()
        {
            IList<NodeId> result = m_typeTable.FindSubTypes(ExpandedNodeId.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindSubTypesReturnsEmptyForUnresolvableNamespace()
        {
            var id = new ExpandedNodeId(1000, 0, "http://unknown.example.com", 0);
            IList<NodeId> result = m_typeTable.FindSubTypes(id);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindSubTypesReturnsEmptyForUnknownType()
        {
            ExpandedNodeId id = UnknownTypeId;
            IList<NodeId> result = m_typeTable.FindSubTypes(id);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindSubTypesReturnsDirectChildren()
        {
            ExpandedNodeId id = RootTypeId;
            IList<NodeId> result = m_typeTable.FindSubTypes(id);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result, Does.Contain(ChildTypeId));
        }

        [Test]
        public void FindSubTypesReturnsEmptyForLeafType()
        {
            ExpandedNodeId id = GrandchildTypeId;
            IList<NodeId> result = m_typeTable.FindSubTypes(id);
            Assert.That(result, Is.Empty);
        }
        [Test]
        public void IsTypeOfNodeIdReturnsFalseForNullSubType()
        {
            Assert.That(m_typeTable.IsTypeOf(NodeId.Null, RootTypeId), Is.False);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsFalseForNullSuperType()
        {
            Assert.That(m_typeTable.IsTypeOf(ChildTypeId, NodeId.Null), Is.False);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsTrueForExactMatch()
        {
            Assert.That(m_typeTable.IsTypeOf(RootTypeId, RootTypeId), Is.True);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsTrueForDirectChild()
        {
            Assert.That(m_typeTable.IsTypeOf(ChildTypeId, RootTypeId), Is.True);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsTrueForTransitiveChild()
        {
            Assert.That(m_typeTable.IsTypeOf(GrandchildTypeId, RootTypeId), Is.True);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsFalseForReversedRelation()
        {
            Assert.That(m_typeTable.IsTypeOf(RootTypeId, ChildTypeId), Is.False);
        }

        [Test]
        public void IsTypeOfNodeIdReturnsFalseForUnknownSubType()
        {
            Assert.That(m_typeTable.IsTypeOf(UnknownTypeId, RootTypeId), Is.False);
        }
        [Test]
        public void IsTypeOfExpandedReturnsFalseForNullSubType()
        {
            Assert.That(
                m_typeTable.IsTypeOf(ExpandedNodeId.Null, new ExpandedNodeId(RootTypeId)),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForNullSuperType()
        {
            Assert.That(
                m_typeTable.IsTypeOf(new ExpandedNodeId(ChildTypeId), ExpandedNodeId.Null),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForNonZeroServerIndexOnSubType()
        {
            var subId = new ExpandedNodeId(1001, 0, null, 1);
            Assert.That(
                m_typeTable.IsTypeOf(subId, new ExpandedNodeId(RootTypeId)),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForNonZeroServerIndexOnSuperType()
        {
            var superId = new ExpandedNodeId(1000, 0, null, 1);
            Assert.That(
                m_typeTable.IsTypeOf(new ExpandedNodeId(ChildTypeId), superId),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsTrueForExactMatch()
        {
            ExpandedNodeId id = RootTypeId;
            Assert.That(m_typeTable.IsTypeOf(id, id), Is.True);
        }

        [Test]
        public void IsTypeOfExpandedReturnsTrueForChild()
        {
            Assert.That(
                m_typeTable.IsTypeOf(
                    new ExpandedNodeId(ChildTypeId),
                    new ExpandedNodeId(RootTypeId)),
                Is.True);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForUnresolvableSubType()
        {
            var subId = new ExpandedNodeId(1001, 0, "http://unknown.example.com", 0);
            Assert.That(
                m_typeTable.IsTypeOf(subId, new ExpandedNodeId(RootTypeId)),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForUnresolvableSuperType()
        {
            var superId = new ExpandedNodeId(1000, 0, "http://unknown.example.com", 0);
            Assert.That(
                m_typeTable.IsTypeOf(new ExpandedNodeId(ChildTypeId), superId),
                Is.False);
        }

        [Test]
        public void IsTypeOfExpandedReturnsFalseForUnknownSubType()
        {
            Assert.That(
                m_typeTable.IsTypeOf(
                    new ExpandedNodeId(UnknownTypeId),
                    new ExpandedNodeId(RootTypeId)),
                Is.False);
        }
        [Test]
        public void FindReferenceTypeNameReturnsDefaultForUnknownType()
        {
            QualifiedName result = m_typeTable.FindReferenceTypeName(UnknownTypeId);
            Assert.That(result, Is.EqualTo(default(QualifiedName)));
        }

        [Test]
        public void FindReferenceTypeNameReturnsBrowseName()
        {
            Assert.That(m_typeTable.FindReferenceTypeName(RefTypeId), Is.EqualTo(RefBrowseName));
            Assert.That(
                m_typeTable.FindReferenceTypeName(RefChildTypeId),
                Is.EqualTo(RefChildBrowseName));
        }
        [Test]
        public void FindReferenceTypeReturnsDefaultForNullBrowseName()
        {
            NodeId result = m_typeTable.FindReferenceType(default);
            Assert.That(result, Is.EqualTo(default(NodeId)));
        }

        [Test]
        public void FindReferenceTypeReturnsDefaultForUnknownBrowseName()
        {
            NodeId result = m_typeTable.FindReferenceType(new QualifiedName("NoSuchRef"));
            Assert.That(result, Is.EqualTo(default(NodeId)));
        }

        [Test]
        public void FindReferenceTypeReturnsNodeIdForKnownBrowseName()
        {
            Assert.That(m_typeTable.FindReferenceType(RefBrowseName), Is.EqualTo(RefTypeId));
            Assert.That(
                m_typeTable.FindReferenceType(RefChildBrowseName),
                Is.EqualTo(RefChildTypeId));
        }
        [Test]
        public void IsEncodingOfReturnsFalseForNullEncodingId()
        {
            Assert.That(
                m_typeTable.IsEncodingOf(ExpandedNodeId.Null, new ExpandedNodeId(DataTypeId)),
                Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsFalseForNullDataTypeId()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            Assert.That(
                m_typeTable.IsEncodingOf(new ExpandedNodeId(EncodingId1), ExpandedNodeId.Null),
                Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsFalseForUnresolvableEncodingNamespace()
        {
            var enc = new ExpandedNodeId(3001, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.IsEncodingOf(enc, new ExpandedNodeId(DataTypeId)), Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsFalseForUnresolvableDataTypeNamespace()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            var dt = new ExpandedNodeId(3000, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.IsEncodingOf(new ExpandedNodeId(EncodingId1), dt), Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsFalseForUnknownEncoding()
        {
            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(UnknownTypeId),
                    new ExpandedNodeId(DataTypeId)),
                Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsTrueForDirectMatch()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(EncodingId1),
                    new ExpandedNodeId(DataTypeId)),
                Is.True);
        }

        [Test]
        public void IsEncodingOfReturnsTrueForSuperTypeMatch()
        {
            // Hierarchy: SuperDT -> ChildDT, encoding registered on ChildDT
            var superDtId = new NodeId(6000);
            var childDtId = new NodeId(6001);
            var encId = new NodeId(6002);

            m_typeTable.AddSubtype(superDtId, NodeId.Null);
            m_typeTable.AddSubtype(childDtId, superDtId);
            m_typeTable.AddEncoding(childDtId, new ExpandedNodeId(encId));

            // Encoding belongs to childDtId, but superDtId is an ancestor → true
            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(encId),
                    new ExpandedNodeId(superDtId)),
                Is.True);
        }

        [Test]
        public void IsEncodingOfReturnsFalseForUnrelatedDataType()
        {
            var unrelatedId = new NodeId(6010);
            m_typeTable.AddSubtype(unrelatedId, NodeId.Null);
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));

            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(EncodingId1),
                    new ExpandedNodeId(unrelatedId)),
                Is.False);
        }

        [Test]
        public void IsEncodingOfReturnsFalseWhenSuperTypeIsDeleted()
        {
            // Hierarchy: A -> B -> C, encoding on C, then remove B
            var aId = new NodeId(6020);
            var bId = new NodeId(6021);
            var cId = new NodeId(6022);
            var encId = new NodeId(6023);

            m_typeTable.AddSubtype(aId, NodeId.Null);
            m_typeTable.AddSubtype(bId, aId);
            m_typeTable.AddSubtype(cId, bId);
            m_typeTable.AddEncoding(cId, new ExpandedNodeId(encId));

            // Remove B — marks it as Deleted
            m_typeTable.Remove(new ExpandedNodeId(bId));

            // Encoding walks up from C, hits B (Deleted → skipped), then A
            // Querying for B should return false because B is deleted
            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(encId),
                    new ExpandedNodeId(bId)),
                Is.False);

            // Querying for A should still return true (A is not deleted)
            Assert.That(
                m_typeTable.IsEncodingOf(
                    new ExpandedNodeId(encId),
                    new ExpandedNodeId(aId)),
                Is.True);
        }
        [Test]
        public void IsEncodingForExtensionObjectReturnsFalseForNullValue()
        {
            Assert.That(m_typeTable.IsEncodingFor(DataTypeId, ExtensionObject.Null), Is.False);
        }

        [Test]
        public void IsEncodingForExtensionObjectReturnsTrueWhenEncodingMatches()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            var ext = new ExtensionObject(new ExpandedNodeId(EncodingId1));
            Assert.That(m_typeTable.IsEncodingFor(DataTypeId, ext), Is.True);
        }

        [Test]
        public void IsEncodingForExtensionObjectReturnsFalseWhenEncodingDoesNotMatch()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            var ext = new ExtensionObject(new ExpandedNodeId(UnknownTypeId));
            Assert.That(m_typeTable.IsEncodingFor(DataTypeId, ext), Is.False);
        }
        [Test]
        public void IsEncodingForVariantReturnsFalseForNullVariant()
        {
            Assert.That(m_typeTable.IsEncodingFor(DataTypeId, Variant.Null), Is.False);
        }

        [Test]
        public void IsEncodingForVariantReturnsTrueForNullExpectedTypeId()
        {
            // Null expected type matches any non-null variant
            var variant = new Variant(42);
            Assert.That(m_typeTable.IsEncodingFor(NodeId.Null, variant), Is.True);
        }

        [Test]
        public void IsEncodingForVariantReturnsTrueWhenActualIsSubtypeOfExpected()
        {
            // Register the standard OPC UA numeric hierarchy that matches Int32 variant
            m_typeTable.AddSubtype(DataTypeIds.Number, NodeId.Null);
            m_typeTable.AddSubtype(DataTypeIds.Integer, DataTypeIds.Number);
            m_typeTable.AddSubtype(DataTypeIds.Int32, DataTypeIds.Integer);

            // new Variant(42) → actualTypeId = DataTypeIds.Int32
            // IsTypeOf(Int32, Integer) → true
            var variant = new Variant(42);
            Assert.That(m_typeTable.IsEncodingFor(DataTypeIds.Integer, variant), Is.True);
        }

        [Test]
        public void IsEncodingForVariantReturnsTrueWhenExpectedIsSubtypeOfActual()
        {
            // Register Int32 and a custom subtype of Int32
            m_typeTable.AddSubtype(DataTypeIds.Int32, NodeId.Null);
            var customSubType = new NodeId(7000);
            m_typeTable.AddSubtype(customSubType, DataTypeIds.Int32);

            // new Variant(42) → actualTypeId = DataTypeIds.Int32
            // IsTypeOf(Int32, customSubType) → false (Int32 is NOT subtype of custom)
            // actualTypeId != Structure → enters reverse check
            // IsTypeOf(customSubType, Int32) → true
            var variant = new Variant(42);
            Assert.That(m_typeTable.IsEncodingFor(customSubType, variant), Is.True);
        }

        [Test]
        public void IsEncodingForVariantHandlesExtensionObjectStructure()
        {
            // Register Structure data type and encoding
            m_typeTable.AddSubtype(DataTypeIds.Structure, NodeId.Null);
            var structDtId = new NodeId(7100);
            var structEncId = new NodeId(7101);
            m_typeTable.AddSubtype(structDtId, DataTypeIds.Structure);
            m_typeTable.AddEncoding(structDtId, new ExpandedNodeId(structEncId));

            // Create a variant containing an ExtensionObject with matching encoding
            var ext = new ExtensionObject(new ExpandedNodeId(structEncId));
            var variant = new Variant(ext);

            Assert.That(m_typeTable.IsEncodingFor(structDtId, variant), Is.True);
        }

        [Test]
        public void IsEncodingForVariantHandlesExtensionObjectArray()
        {
            // Register Structure data type and encoding
            m_typeTable.AddSubtype(DataTypeIds.Structure, NodeId.Null);
            var structDtId = new NodeId(7200);
            var structEncId = new NodeId(7201);
            m_typeTable.AddSubtype(structDtId, DataTypeIds.Structure);
            m_typeTable.AddEncoding(structDtId, new ExpandedNodeId(structEncId));

            // Create array of ExtensionObjects
            var ext1 = new ExtensionObject(new ExpandedNodeId(structEncId));
            var ext2 = new ExtensionObject(new ExpandedNodeId(structEncId));
            ArrayOf<ExtensionObject> array = new ExtensionObject[] { ext1, ext2 };
            var variant = new Variant(array);

            Assert.That(m_typeTable.IsEncodingFor(structDtId, variant), Is.True);
        }

        [Test]
        public void IsEncodingForVariantReturnsFalseWhenArrayElementDoesNotMatch()
        {
            // Register Structure data type and encoding
            m_typeTable.AddSubtype(DataTypeIds.Structure, NodeId.Null);
            var structDtId = new NodeId(7300);
            var structEncId = new NodeId(7301);
            m_typeTable.AddSubtype(structDtId, DataTypeIds.Structure);
            m_typeTable.AddEncoding(structDtId, new ExpandedNodeId(structEncId));

            // Create array with one matching and one non-matching encoding
            var extGood = new ExtensionObject(new ExpandedNodeId(structEncId));
            var extBad = new ExtensionObject(new ExpandedNodeId(UnknownTypeId));
            ArrayOf<ExtensionObject> array = new ExtensionObject[] { extGood, extBad };
            var variant = new Variant(array);

            Assert.That(m_typeTable.IsEncodingFor(structDtId, variant), Is.False);
        }

        [Test]
        public void IsEncodingForVariantReturnsFalseForNonMatchingNonStructure()
        {
            // Register Int32 only (no hierarchy linking to Double)
            m_typeTable.AddSubtype(DataTypeIds.Int32, NodeId.Null);
            m_typeTable.AddSubtype(DataTypeIds.Double, NodeId.Null);

            // new Variant(42) → actualTypeId = DataTypeIds.Int32
            // IsTypeOf(Int32, Double) → false (no parent-child link)
            // actualTypeId != Structure → reverse check
            // IsTypeOf(Double, Int32) → false (no parent-child link)
            var variant = new Variant(42);
            Assert.That(m_typeTable.IsEncodingFor(DataTypeIds.Double, variant), Is.False);
        }
        [Test]
        public void FindDataTypeIdExpandedReturnsNullForNullId()
        {
            Assert.That(m_typeTable.FindDataTypeId(ExpandedNodeId.Null), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindDataTypeIdExpandedReturnsNullForUnresolvableNamespace()
        {
            var enc = new ExpandedNodeId(3001, 0, "http://unknown.example.com", 0);
            Assert.That(m_typeTable.FindDataTypeId(enc), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindDataTypeIdExpandedReturnsNullForUnknownEncoding()
        {
            ExpandedNodeId id = UnknownTypeId;
            Assert.That(m_typeTable.FindDataTypeId(id), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindDataTypeIdExpandedReturnsDataType()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            ExpandedNodeId id = EncodingId1;
            Assert.That(m_typeTable.FindDataTypeId(id), Is.EqualTo(DataTypeId));
        }
        [Test]
        public void FindDataTypeIdNodeIdReturnsNullForUnknownEncoding()
        {
            Assert.That(m_typeTable.FindDataTypeId(UnknownTypeId), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void FindDataTypeIdNodeIdReturnsDataType()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            Assert.That(m_typeTable.FindDataTypeId(EncodingId1), Is.EqualTo(DataTypeId));
        }
        [Test]
        public void ClearRemovesAllTypesEncodingsAndReferenceTypes()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));

            m_typeTable.Clear();

            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.False);
            Assert.That(m_typeTable.IsKnown(ChildTypeId), Is.False);
            Assert.That(m_typeTable.IsKnown(GrandchildTypeId), Is.False);
            Assert.That(m_typeTable.FindReferenceType(RefBrowseName), Is.EqualTo(default(NodeId)));
            Assert.That(m_typeTable.FindDataTypeId(EncodingId1), Is.EqualTo(NodeId.Null));
        }
        [Test]
        public void RemoveDoesNothingForNullId()
        {
            m_typeTable.Remove(ExpandedNodeId.Null);
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
        }

        [Test]
        public void RemoveDoesNothingForNonZeroServerIndex()
        {
            var id = new ExpandedNodeId(1000, 0, null, 1);
            m_typeTable.Remove(id);
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
        }

        [Test]
        public void RemoveDoesNothingForUnresolvableNamespace()
        {
            var id = new ExpandedNodeId(1000, 0, "http://unknown.example.com", 0);
            m_typeTable.Remove(id);
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
        }

        [Test]
        public void RemoveDoesNothingForUnknownType()
        {
            ExpandedNodeId id = UnknownTypeId;
            m_typeTable.Remove(id);
            // Should not throw
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
        }

        [Test]
        public void RemoveRemovesKnownType()
        {
            m_typeTable.Remove(new ExpandedNodeId(GrandchildTypeId));
            Assert.That(m_typeTable.IsKnown(GrandchildTypeId), Is.False);
        }

        [Test]
        public void RemoveRemovesEncodings()
        {
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId1));
            m_typeTable.AddEncoding(DataTypeId, new ExpandedNodeId(EncodingId2));

            m_typeTable.Remove(new ExpandedNodeId(DataTypeId));

            Assert.That(m_typeTable.FindDataTypeId(EncodingId1), Is.EqualTo(NodeId.Null));
            Assert.That(m_typeTable.FindDataTypeId(EncodingId2), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void RemoveRemovesReferenceTypeBrowseName()
        {
            m_typeTable.Remove(new ExpandedNodeId(RefTypeId));
            Assert.That(m_typeTable.FindReferenceType(RefBrowseName), Is.EqualTo(default(NodeId)));
        }

        [Test]
        public void RemoveUpdatesParentSubTypeList()
        {
            // Before removal: RootTypeId has ChildTypeId as a subtype
            IList<NodeId> subtypesBefore = m_typeTable.FindSubTypes(new ExpandedNodeId(RootTypeId));
            Assert.That(subtypesBefore, Does.Contain(ChildTypeId));

            m_typeTable.Remove(new ExpandedNodeId(ChildTypeId));

            // After removal: ChildTypeId should be gone from parent's subtype list
            IList<NodeId> subtypesAfter = m_typeTable.FindSubTypes(new ExpandedNodeId(RootTypeId));
            Assert.That(subtypesAfter, Does.Not.Contain(ChildTypeId));
        }

        [Test]
        public void RemoveLastSubtypeClearsParentSubTypeList()
        {
            // GrandchildTypeId is the only subtype of ChildTypeId
            m_typeTable.Remove(new ExpandedNodeId(GrandchildTypeId));

            // ChildTypeId should now have no subtypes (SubTypes set to null internally)
            IList<NodeId> subtypes = m_typeTable.FindSubTypes(new ExpandedNodeId(ChildTypeId));
            Assert.That(subtypes, Is.Empty);
        }
        private static Mock<ILocalNode> CreateMockNode(
            NodeId nodeId,
            NodeClass nodeClass,
            QualifiedName browseName = default,
            ExpandedNodeId superTypeTarget = default,
            IList<IReference> encodings = null)
        {
            var mockNode = new Mock<ILocalNode>();
            mockNode.Setup(n => n.NodeId).Returns(nodeId);
            mockNode.Setup(n => n.NodeClass).Returns(nodeClass);
            mockNode.Setup(n => n.BrowseName).Returns(browseName);

            var mockRefs = new Mock<IReferenceCollection>();

            // FindTarget for HasSubtype (inverse=true) returns supertype
            mockRefs.Setup(r => r.FindTarget(
                    ReferenceTypeIds.HasSubtype, true, false, null, 0))
                .Returns(superTypeTarget);

            // Find for HasEncoding returns encoding references
            mockRefs.Setup(r => r.Find(
                    ReferenceTypeIds.HasEncoding, false, false, null))
                .Returns(encodings ?? new List<IReference>());

            mockNode.Setup(n => n.References).Returns(mockRefs.Object);
            return mockNode;
        }

        private static IReference CreateMockReference(ExpandedNodeId targetId)
        {
            var mockRef = new Mock<IReference>();
            mockRef.Setup(r => r.TargetId).Returns(targetId);
            return mockRef.Object;
        }

        [Test]
        public void AddIgnoresNullNode()
        {
            m_typeTable.Add(null);
            // No exception — table unchanged
            Assert.That(m_typeTable.IsKnown(RootTypeId), Is.True);
        }

        [Test]
        public void AddIgnoresNodeWithNullId()
        {
            Mock<ILocalNode> node = CreateMockNode(NodeId.Null, NodeClass.ObjectType);
            m_typeTable.Add(node.Object);
            // No exception
        }

        [Test]
        public void AddIgnoresNonTypeNodeClass()
        {
            var nodeId = new NodeId(8000);
            Mock<ILocalNode> node = CreateMockNode(nodeId, NodeClass.Object);
            m_typeTable.Add(node.Object);

            Assert.That(m_typeTable.IsKnown(nodeId), Is.False);
        }

        [Test]
        public void AddObjectTypeWithNoSuperType()
        {
            var nodeId = new NodeId(8001);
            Mock<ILocalNode> node = CreateMockNode(nodeId, NodeClass.ObjectType);
            m_typeTable.Add(node.Object);

            Assert.That(m_typeTable.IsKnown(nodeId), Is.True);
            Assert.That(m_typeTable.FindSuperType(nodeId), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void AddVariableTypeWithSuperType()
        {
            // First register the super type
            var superNodeId = new NodeId(8010);
            var childNodeId = new NodeId(8011);
            m_typeTable.AddSubtype(superNodeId, NodeId.Null);

            Mock<ILocalNode> node = CreateMockNode(
                childNodeId,
                NodeClass.VariableType,
                superTypeTarget: new ExpandedNodeId(superNodeId));
            m_typeTable.Add(node.Object);

            Assert.That(m_typeTable.IsKnown(childNodeId), Is.True);
            Assert.That(m_typeTable.FindSuperType(childNodeId), Is.EqualTo(superNodeId));
        }

        [Test]
        public void AddDataTypeWithEncodings()
        {
            var dtNodeId = new NodeId(8020);
            var encNodeId1 = new NodeId(8021);
            var encNodeId2 = new NodeId(8022);

            var encodings = new List<IReference>
            {
                CreateMockReference(new ExpandedNodeId(encNodeId1)),
                CreateMockReference(new ExpandedNodeId(encNodeId2))
            };

            Mock<ILocalNode> node = CreateMockNode(
                dtNodeId,
                NodeClass.DataType,
                encodings: encodings);
            m_typeTable.Add(node.Object);

            Assert.That(m_typeTable.IsKnown(dtNodeId), Is.True);
            Assert.That(m_typeTable.FindDataTypeId(encNodeId1), Is.EqualTo(dtNodeId));
            Assert.That(m_typeTable.FindDataTypeId(encNodeId2), Is.EqualTo(dtNodeId));
        }

        [Test]
        public void AddReferenceTypeRegistersBrowseName()
        {
            var refNodeId = new NodeId(8030);
            var browseName = new QualifiedName("CustomReference");

            Mock<ILocalNode> node = CreateMockNode(
                refNodeId,
                NodeClass.ReferenceType,
                browseName: browseName);
            m_typeTable.Add(node.Object);

            Assert.That(m_typeTable.IsKnown(refNodeId), Is.True);
            Assert.That(m_typeTable.FindReferenceType(browseName), Is.EqualTo(refNodeId));
            Assert.That(m_typeTable.FindReferenceTypeName(refNodeId), Is.EqualTo(browseName));
        }

        [Test]
        public void AddThrowsWhenSuperTypeNotResolvable()
        {
            var nodeId = new NodeId(8040);
            var badSuper = new ExpandedNodeId(9999, 0, "http://unknown.example.com", 0);
            Mock<ILocalNode> node = CreateMockNode(
                nodeId,
                NodeClass.ObjectType,
                superTypeTarget: badSuper);

            Assert.Throws<ServiceResultException>(() => m_typeTable.Add(node.Object));
        }

        [Test]
        public void AddThrowsWhenSuperTypeNotInTable()
        {
            var nodeId = new NodeId(8050);
            // Reference a super type that's not registered in the table
            var missingSuperType = new ExpandedNodeId(new NodeId(9997));
            Mock<ILocalNode> node = CreateMockNode(
                nodeId,
                NodeClass.ObjectType,
                superTypeTarget: missingSuperType);

            Assert.Throws<ServiceResultException>(() => m_typeTable.Add(node.Object));
        }

        [Test]
        public void AddUpdatesExistingTypeAndReplacesEncodings()
        {
            var dtNodeId = new NodeId(8060);
            var enc1 = new NodeId(8061);
            var enc2 = new NodeId(8062);

            // First add with encoding 1
            var encodings1 = new List<IReference>
            {
                CreateMockReference(new ExpandedNodeId(enc1))
            };
            Mock<ILocalNode> node1 = CreateMockNode(dtNodeId, NodeClass.DataType, encodings: encodings1);
            m_typeTable.Add(node1.Object);
            Assert.That(m_typeTable.FindDataTypeId(enc1), Is.EqualTo(dtNodeId));

            // Re-add with encoding 2 (should remove encoding 1, add encoding 2)
            var encodings2 = new List<IReference>
            {
                CreateMockReference(new ExpandedNodeId(enc2))
            };
            Mock<ILocalNode> node2 = CreateMockNode(dtNodeId, NodeClass.DataType, encodings: encodings2);
            m_typeTable.Add(node2.Object);

            Assert.That(m_typeTable.FindDataTypeId(enc1), Is.EqualTo(NodeId.Null));
            Assert.That(m_typeTable.FindDataTypeId(enc2), Is.EqualTo(dtNodeId));
        }

        [Test]
        public void AddReferenceTypeReplacesOldBrowseName()
        {
            var refNodeId = new NodeId(8070);
            var oldName = new QualifiedName("OldRefName");
            var newName = new QualifiedName("NewRefName");

            Mock<ILocalNode> node1 = CreateMockNode(refNodeId, NodeClass.ReferenceType, browseName: oldName);
            m_typeTable.Add(node1.Object);
            Assert.That(m_typeTable.FindReferenceType(oldName), Is.EqualTo(refNodeId));

            // Re-add with new browse name
            Mock<ILocalNode> node2 = CreateMockNode(refNodeId, NodeClass.ReferenceType, browseName: newName);
            m_typeTable.Add(node2.Object);

            Assert.That(m_typeTable.FindReferenceType(oldName), Is.EqualTo(default(NodeId)));
            Assert.That(m_typeTable.FindReferenceType(newName), Is.EqualTo(refNodeId));
        }

        [Test]
        public void AddWithSuperTypeCreatesSubtypeRelationship()
        {
            var parentId = new NodeId(8080);
            var childId = new NodeId(8081);
            m_typeTable.AddSubtype(parentId, NodeId.Null);

            Mock<ILocalNode> node = CreateMockNode(
                childId,
                NodeClass.ObjectType,
                superTypeTarget: new ExpandedNodeId(parentId));
            m_typeTable.Add(node.Object);

            IList<NodeId> subtypes = m_typeTable.FindSubTypes(new ExpandedNodeId(parentId));
            Assert.That(subtypes, Does.Contain(childId));
        }
    }
}
