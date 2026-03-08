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

#nullable enable

using System;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="RelativePath"/> and <see cref="RelativePathElement"/> classes
    /// covering constructors, Parse, Format, IsEqual, Clone, Encode/Decode, and edge cases.
    /// </summary>
    [TestFixture]
    [Category("RelativePath")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RelativePathTests
    {
        #region RelativePath Constructors

        [Test]
        public void DefaultConstructorCreatesEmptyElements()
        {
            // Covers: RelativePath() - lines 81-84, Initialize - lines 92-95
            var path = new RelativePath();

            Assert.That(path.Elements, Is.Not.Null);
            Assert.That(path.Elements.Count, Is.EqualTo(0));
        }

        [Test]
        public void ConstructorWithBrowseNameCreatesOneElement()
        {
            // Covers: RelativePath(QualifiedName) - lines 45-48 -> chain to 4-param ctor
            var browseName = new QualifiedName("TestNode", 0);
            var path = new RelativePath(browseName);

            Assert.That(path.Elements, Is.Not.Null);
            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(path.Elements[0].IsInverse, Is.False);
            Assert.That(path.Elements[0].IncludeSubtypes, Is.True);
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(browseName));
        }

        [Test]
        public void ConstructorWithRefTypeAndBrowseNameCreatesOneElement()
        {
            // Covers: RelativePath(NodeId, QualifiedName) - lines 53-56
            NodeId refTypeId = ReferenceTypeIds.HasComponent;
            var browseName = new QualifiedName("MyComponent", 0);
            var path = new RelativePath(refTypeId, browseName);

            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].ReferenceTypeId, Is.EqualTo(refTypeId));
            Assert.That(path.Elements[0].IsInverse, Is.False);
            Assert.That(path.Elements[0].IncludeSubtypes, Is.True);
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(browseName));
        }

        [Test]
        public void ConstructorWithAllParametersCreatesElement()
        {
            // Covers: RelativePath(NodeId, bool, bool, QualifiedName) - lines 61-78
            NodeId refTypeId = ReferenceTypeIds.HasProperty;
            var browseName = new QualifiedName("Property1", 2);
            var path = new RelativePath(refTypeId, true, false, browseName);

            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].ReferenceTypeId, Is.EqualTo(refTypeId));
            Assert.That(path.Elements[0].IsInverse, Is.True);
            Assert.That(path.Elements[0].IncludeSubtypes, Is.False);
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(browseName));
        }

        #endregion

        #region Elements Property

        [Test]
        public void ElementsSetterAcceptsCollection()
        {
            // Covers: set_Elements - lines 105-113 (non-null path)
            var path = new RelativePath();
            var collection = new RelativePathElementCollection
            {
                new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    TargetName = new QualifiedName("Child")
                }
            };

            path.Elements = collection;

            Assert.That(path.Elements, Is.SameAs(collection));
            Assert.That(path.Elements.Count, Is.EqualTo(1));
        }

        [Test]
        public void ElementsSetterWithNullInitializesEmptyCollection()
        {
            // Covers: set_Elements null branch - lines 109-112
            var path = new RelativePath();
            path.Elements.Add(new RelativePathElement { TargetName = new QualifiedName("A") });

            path.Elements = null!;

            Assert.That(path.Elements, Is.Not.Null);
            Assert.That(path.Elements.Count, Is.EqualTo(0));
        }

        #endregion

        #region TypeId / EncodingId Properties

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            // Covers: TypeId property - line 117
            var path = new RelativePath();
            Assert.That(path.TypeId, Is.EqualTo(DataTypeIds.RelativePath));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            // Covers: BinaryEncodingId property - line 120
            var path = new RelativePath();
            Assert.That(path.BinaryEncodingId, Is.EqualTo(ObjectIds.RelativePath_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            // Covers: XmlEncodingId property - line 123
            var path = new RelativePath();
            Assert.That(path.XmlEncodingId, Is.EqualTo(ObjectIds.RelativePath_Encoding_DefaultXml));
        }

        [Test]
        public void JsonEncodingIdReturnsExpectedValue()
        {
            // Covers: JsonEncodingId property - line 126
            var path = new RelativePath();
            Assert.That(path.JsonEncodingId, Is.EqualTo(ObjectIds.RelativePath_Encoding_DefaultJson));
        }

        #endregion

        #region Encode / Decode

        [Test]
        public void EncodeWritesElementsArray()
        {
            // Covers: Encode - lines 129-136
            var path = new RelativePath(new QualifiedName("Node1"));
            var mockEncoder = new Mock<IEncoder>();

            path.Encode(mockEncoder.Object);

            mockEncoder.Verify(e => e.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            mockEncoder.Verify(e => e.WriteEncodeableArray(
                "Elements",
                It.IsAny<ArrayOf<RelativePathElement>>()), Times.Once);
            mockEncoder.Verify(e => e.PopNamespace(), Times.Once);
        }

        [Test]
        public void DecodeReadsElementsArray()
        {
            // Covers: Decode - lines 139-146
            var mockDecoder = new Mock<IDecoder>();
            var elements = new RelativePathElementCollection
            {
                new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TargetName = new QualifiedName("Prop1")
                }
            };
            mockDecoder.Setup(d => d.ReadEncodeableArray<RelativePathElement>("Elements"))
                .Returns((ArrayOf<RelativePathElement>)elements);

            var path = new RelativePath();
            path.Decode(mockDecoder.Object);

            mockDecoder.Verify(d => d.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            mockDecoder.Verify(d => d.PopNamespace(), Times.Once);
            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(new QualifiedName("Prop1")));
        }

        #endregion

        #region IsEqual

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            // Covers: IsEqual ReferenceEquals branch - lines 151-154
            var path = new RelativePath(new QualifiedName("Test"));
            Assert.That(path.IsEqual(path), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            // Covers: IsEqual null check - lines 156-159
            var path = new RelativePath(new QualifiedName("Test"));
            Assert.That(path.IsEqual(null!), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentTypeReturnsFalse()
        {
            // Covers: IsEqual type mismatch (is not RelativePath) - lines 156-159
            var path = new RelativePath(new QualifiedName("Test"));
            var element = new RelativePathElement();
            Assert.That(path.IsEqual(element), Is.False);
        }

        [Test]
        public void IsEqualWithEqualElementsReturnsTrue()
        {
            // Covers: IsEqual with matching elements - lines 161-167
            var path1 = new RelativePath(new QualifiedName("Test"));
            var path2 = new RelativePath(new QualifiedName("Test"));
            Assert.That(path1.IsEqual(path2), Is.True);
        }

        [Test]
        public void IsEqualWithDifferentElementsReturnsFalse()
        {
            // Covers: IsEqual elements not equal branch - lines 161-163
            var path1 = new RelativePath(new QualifiedName("Node1"));
            var path2 = new RelativePath(new QualifiedName("Node2"));
            Assert.That(path1.IsEqual(path2), Is.False);
        }

        [Test]
        public void IsEqualWithEmptyPathsReturnsTrue()
        {
            // Covers: IsEqual with two empty paths - lines 161-167
            var path1 = new RelativePath();
            var path2 = new RelativePath();
            Assert.That(path1.IsEqual(path2), Is.True);
        }

        #endregion

        #region Clone / MemberwiseClone

        [Test]
        public void CloneReturnsDeepCopy()
        {
            // Covers: Clone - lines 170-173, MemberwiseClone - lines 176-183
            var path = new RelativePath(ReferenceTypeIds.HasComponent, true, false, new QualifiedName("Target", 1));

            var clone = (RelativePath)path.Clone();

            Assert.That(clone, Is.Not.SameAs(path));
            Assert.That(clone.Elements.Count, Is.EqualTo(path.Elements.Count));
            Assert.That(clone.Elements, Is.Not.SameAs(path.Elements));
            Assert.That(clone.Elements[0].TargetName, Is.EqualTo(path.Elements[0].TargetName));
            Assert.That(clone.Elements[0].ReferenceTypeId, Is.EqualTo(path.Elements[0].ReferenceTypeId));
            Assert.That(clone.Elements[0].IsInverse, Is.EqualTo(path.Elements[0].IsInverse));
            Assert.That(clone.Elements[0].IncludeSubtypes, Is.EqualTo(path.Elements[0].IncludeSubtypes));
        }

        [Test]
        public void MemberwiseCloneClonesElementsCollection()
        {
            // Covers: MemberwiseClone - lines 176-183
            var path = new RelativePath(new QualifiedName("Original"));

            var clone = (RelativePath)path.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(path));
            Assert.That(clone.Elements, Is.Not.SameAs(path.Elements));
            Assert.That(clone.Elements.Count, Is.EqualTo(1));
        }

        #endregion

        #region Format

        [Test]
        public void FormatWithEmptyPathReturnsEmptyString()
        {
            // Covers: Format - lines 188-192
            var path = new RelativePath();
            var mockTypeTree = new Mock<ITypeTable>();

            var result = path.Format(mockTypeTree.Object);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithHierarchicalElementReturnsPath()
        {
            // Covers: Format - lines 188-192
            var path = new RelativePath(new QualifiedName("ServerStatus"));
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree.Setup(t => t.FindReferenceTypeName(ReferenceTypeIds.HierarchicalReferences))
                .Returns(new QualifiedName("HierarchicalReferences"));

            var result = path.Format(mockTypeTree.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Length, Is.GreaterThan(0));
        }

        #endregion

        #region IsEmpty

        [Test]
        public void IsEmptyWithNullReturnsTrue()
        {
            // Covers: IsEmpty null branch - lines 199-204
            Assert.That(RelativePath.IsEmpty(null!), Is.True);
        }

        [Test]
        public void IsEmptyWithEmptyPathReturnsTrue()
        {
            // Covers: IsEmpty empty elements branch - lines 199-201
            var path = new RelativePath();
            Assert.That(RelativePath.IsEmpty(path), Is.True);
        }

        [Test]
        public void IsEmptyWithElementsReturnsFalse()
        {
            // Covers: IsEmpty non-empty branch - lines 199-201
            var path = new RelativePath(new QualifiedName("Node"));
            Assert.That(RelativePath.IsEmpty(path), Is.False);
        }

        #endregion

        #region Parse(string, ITypeTable)

        [Test]
        public void ParseWithNullTypeTreeThrowsArgumentNullException()
        {
            // Covers: Parse null check - lines 214-217
            Assert.That(
                () => RelativePath.Parse("/TestNode", null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseHierarchicalPathCreatesCorrectElements()
        {
            // Covers: Parse AnyHierarchical case - lines 220-268, switch case lines 237-239
            var mockTypeTree = new Mock<ITypeTable>();

            var result = RelativePath.Parse("/TestNode", mockTypeTree.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[0].IncludeSubtypes, Is.True);
            Assert.That(result.Elements[0].IsInverse, Is.False);
            Assert.That(result.Elements[0].TargetName, Is.EqualTo(new QualifiedName("TestNode")));
        }

        [Test]
        public void ParseComponentPathCreatesCorrectElements()
        {
            // Covers: Parse AnyComponent case - lines 240-242
            var mockTypeTree = new Mock<ITypeTable>();

            var result = RelativePath.Parse(".ComponentNode", mockTypeTree.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
            Assert.That(result.Elements[0].IsInverse, Is.False);
        }

        [Test]
        public void ParseForwardReferenceUsesTypeTree()
        {
            // Covers: Parse ForwardReference case - lines 243-246
            var mockTypeTree = new Mock<ITypeTable>();
            var refNodeId = new NodeId(999);
            mockTypeTree.Setup(t => t.FindReferenceType(new QualifiedName("HasComponent")))
                .Returns(refNodeId);

            var result = RelativePath.Parse("<HasComponent>Target", mockTypeTree.Object);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(refNodeId));
            Assert.That(result.Elements[0].IsInverse, Is.False);
        }

        [Test]
        public void ParseInverseReferenceUsesTypeTree()
        {
            // Covers: Parse InverseReference case - lines 248-251
            var mockTypeTree = new Mock<ITypeTable>();
            var refNodeId = new NodeId(888);
            mockTypeTree.Setup(t => t.FindReferenceType(new QualifiedName("Organizes")))
                .Returns(refNodeId);

            var result = RelativePath.Parse("<!Organizes>Target", mockTypeTree.Object);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(refNodeId));
            Assert.That(result.Elements[0].IsInverse, Is.True);
        }

        [Test]
        public void ParseWithNullReferenceTypeIdThrowsServiceResultException()
        {
            // Covers: Parse IsNull check - lines 257-263
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree.Setup(t => t.FindReferenceType(It.IsAny<QualifiedName>()))
                .Returns(NodeId.Null);

            Assert.That(
                () => RelativePath.Parse("<UnknownRef>Target", mockTypeTree.Object),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseMultipleElementsReturnsAllElements()
        {
            // Covers: Parse foreach loop - lines 225-266
            var mockTypeTree = new Mock<ITypeTable>();

            var result = RelativePath.Parse("/Node1/Node2/Node3", mockTypeTree.Object);

            Assert.That(result.Elements.Count, Is.EqualTo(3));
            Assert.That(result.Elements[0].TargetName, Is.EqualTo(new QualifiedName("Node1")));
            Assert.That(result.Elements[1].TargetName, Is.EqualTo(new QualifiedName("Node2")));
            Assert.That(result.Elements[2].TargetName, Is.EqualTo(new QualifiedName("Node3")));
        }

        [Test]
        public void ParseMixedPathElements()
        {
            // Covers: Parse mixed hierarchical and component paths
            var mockTypeTree = new Mock<ITypeTable>();

            var result = RelativePath.Parse("/Node1.Property1", mockTypeTree.Object);

            Assert.That(result.Elements.Count, Is.EqualTo(2));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
        }

        #endregion

        #region Parse(string, ITypeTable, NamespaceTable, NamespaceTable)

        [Test]
        public void ParseWithNamespaceTablesHierarchicalPath()
        {
            // Covers: Parse 4-param overload AnyHierarchical - lines 281-338, case lines 300-302
            var mockTypeTree = new Mock<ITypeTable>();
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("/TestNode", mockTypeTree.Object, currentTable, targetTable);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[0].IsInverse, Is.False);
        }

        [Test]
        public void ParseWithNamespaceTablesComponentPath()
        {
            // Covers: Parse 4-param overload AnyComponent - lines 304-305
            var mockTypeTree = new Mock<ITypeTable>();
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse(".Component", mockTypeTree.Object, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
        }

        [Test]
        public void ParseWithNamespaceTablesForwardReference()
        {
            // Covers: Parse 4-param overload ForwardReference - lines 306-320
            var mockTypeTree = new Mock<ITypeTable>();
            var refNodeId = new NodeId(777);
            mockTypeTree.Setup(t => t.FindReferenceType(new QualifiedName("HasComponent")))
                .Returns(refNodeId);
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("<HasComponent>Target", mockTypeTree.Object, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(refNodeId));
            Assert.That(result.Elements[0].IsInverse, Is.False);
        }

        [Test]
        public void ParseWithNamespaceTablesInverseReference()
        {
            // Covers: Parse 4-param overload InverseReference - lines 306-320
            var mockTypeTree = new Mock<ITypeTable>();
            var refNodeId = new NodeId(666);
            mockTypeTree.Setup(t => t.FindReferenceType(new QualifiedName("Organizes")))
                .Returns(refNodeId);
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("<!Organizes>Target", mockTypeTree.Object, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(refNodeId));
            Assert.That(result.Elements[0].IsInverse, Is.True);
        }

        [Test]
        public void ParseWithNamespaceTablesNullTypeTreeForRefPathThrows()
        {
            // Covers: Parse 4-param overload null typeTree check - lines 308-312
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            Assert.That(
                () => RelativePath.Parse("<HasComponent>Target", null!, currentTable, targetTable),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ParseWithNamespaceTablesNullRefTypeIdThrows()
        {
            // Covers: Parse 4-param overload IsNull check - lines 326-332
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree.Setup(t => t.FindReferenceType(It.IsAny<QualifiedName>()))
                .Returns(NodeId.Null);
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            Assert.That(
                () => RelativePath.Parse("<UnknownRef>Target", mockTypeTree.Object, currentTable, targetTable),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void ParseWithNamespaceTablesMultipleElements()
        {
            // Covers: Parse 4-param foreach loop - lines 288-335
            var mockTypeTree = new Mock<ITypeTable>();
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("/A/B.C", mockTypeTree.Object, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(3));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[2].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
        }

        [Test]
        public void ParseWithNamespaceTablesNullTypeTreeForHierarchicalSucceeds()
        {
            // Covers: Parse 4-param with null typeTree but hierarchical path (no lookup needed)
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("/Node", null!, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
        }

        #endregion

        #region RelativePathElement Constructors and Properties

        [Test]
        public void RelativePathElementDefaultConstructorInitializesDefaults()
        {
            // Covers: RelativePathElement() constructor and Initialize - lines 55-61
            // Defaults: IsInverse=true, IncludeSubtypes=true, ReferenceTypeId=default, TargetName=default
            var element = new RelativePathElement();

            Assert.That(element.ReferenceTypeId, Is.EqualTo(default(NodeId)));
            Assert.That(element.IsInverse, Is.True);
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.TargetName, Is.EqualTo(default(QualifiedName)));
        }

        [Test]
        public void RelativePathElementPropertiesCanBeSet()
        {
            // Covers: Property setters for ReferenceTypeId, IsInverse, IncludeSubtypes, TargetName
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IsInverse = true,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("TestTarget", 3)
            };

            Assert.That(element.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasProperty));
            Assert.That(element.IsInverse, Is.True);
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.TargetName, Is.EqualTo(new QualifiedName("TestTarget", 3)));
        }

        #endregion

        #region RelativePathElement TypeId Properties

        [Test]
        public void RelativePathElementTypeIdReturnsExpectedValue()
        {
            var element = new RelativePathElement();
            Assert.That(element.TypeId, Is.EqualTo(DataTypeIds.RelativePathElement));
        }

        [Test]
        public void RelativePathElementBinaryEncodingIdReturnsExpectedValue()
        {
            var element = new RelativePathElement();
            Assert.That(element.BinaryEncodingId, Is.EqualTo(ObjectIds.RelativePathElement_Encoding_DefaultBinary));
        }

        [Test]
        public void RelativePathElementXmlEncodingIdReturnsExpectedValue()
        {
            var element = new RelativePathElement();
            Assert.That(element.XmlEncodingId, Is.EqualTo(ObjectIds.RelativePathElement_Encoding_DefaultXml));
        }

        [Test]
        public void RelativePathElementJsonEncodingIdReturnsExpectedValue()
        {
            var element = new RelativePathElement();
            Assert.That(element.JsonEncodingId, Is.EqualTo(ObjectIds.RelativePathElement_Encoding_DefaultJson));
        }

        #endregion

        #region RelativePathElement Encode / Decode

        [Test]
        public void RelativePathElementEncodeWritesAllFields()
        {
            // Covers: RelativePathElement.Encode - lines 100-110
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = true,
                IncludeSubtypes = false,
                TargetName = new QualifiedName("EncodedTarget", 1)
            };
            var mockEncoder = new Mock<IEncoder>();

            element.Encode(mockEncoder.Object);

            mockEncoder.Verify(e => e.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            mockEncoder.Verify(e => e.WriteNodeId("ReferenceTypeId", ReferenceTypeIds.HasComponent), Times.Once);
            mockEncoder.Verify(e => e.WriteBoolean("IsInverse", true), Times.Once);
            mockEncoder.Verify(e => e.WriteBoolean("IncludeSubtypes", false), Times.Once);
            mockEncoder.Verify(e => e.WriteQualifiedName("TargetName", new QualifiedName("EncodedTarget", 1)), Times.Once);
            mockEncoder.Verify(e => e.PopNamespace(), Times.Once);
        }

        [Test]
        public void RelativePathElementDecodeReadsAllFields()
        {
            // Covers: RelativePathElement.Decode - lines 113-123
            var mockDecoder = new Mock<IDecoder>();
            mockDecoder.Setup(d => d.ReadNodeId("ReferenceTypeId")).Returns(ReferenceTypeIds.Organizes);
            mockDecoder.Setup(d => d.ReadBoolean("IsInverse")).Returns(true);
            mockDecoder.Setup(d => d.ReadBoolean("IncludeSubtypes")).Returns(true);
            mockDecoder.Setup(d => d.ReadQualifiedName("TargetName")).Returns(new QualifiedName("Decoded", 2));

            var element = new RelativePathElement();
            element.Decode(mockDecoder.Object);

            Assert.That(element.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(element.IsInverse, Is.True);
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.TargetName, Is.EqualTo(new QualifiedName("Decoded", 2)));
            mockDecoder.Verify(d => d.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            mockDecoder.Verify(d => d.PopNamespace(), Times.Once);
        }

        #endregion

        #region RelativePathElement IsEqual

        [Test]
        public void RelativePathElementIsEqualWithSameReferenceReturnsTrue()
        {
            // Covers: RelativePathElement.IsEqual ReferenceEquals - lines 127-130
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TargetName = new QualifiedName("Test")
            };
            Assert.That(element.IsEqual(element), Is.True);
        }

        [Test]
        public void RelativePathElementIsEqualWithNullReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual null/type check - lines 133-135
            var element = new RelativePathElement();
            Assert.That(element.IsEqual(null!), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentTypeReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual type mismatch - lines 133-135
            var element = new RelativePathElement();
            var path = new RelativePath();
            Assert.That(element.IsEqual(path), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithMatchingPropertiesReturnsTrue()
        {
            // Covers: RelativePathElement.IsEqual all property comparisons - lines 138-159
            var element1 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = true,
                IncludeSubtypes = false,
                TargetName = new QualifiedName("Match", 1)
            };
            var element2 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = true,
                IncludeSubtypes = false,
                TargetName = new QualifiedName("Match", 1)
            };

            Assert.That(element1.IsEqual(element2), Is.True);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentReferenceTypeIdReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual ReferenceTypeId mismatch - lines 138-140
            var element1 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TargetName = new QualifiedName("Test")
            };
            var element2 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TargetName = new QualifiedName("Test")
            };

            Assert.That(element1.IsEqual(element2), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentIsInverseReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual IsInverse mismatch - lines 143-145
            var element1 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = false,
                TargetName = new QualifiedName("Test")
            };
            var element2 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IsInverse = true,
                TargetName = new QualifiedName("Test")
            };

            Assert.That(element1.IsEqual(element2), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentIncludeSubtypesReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual IncludeSubtypes mismatch - lines 148-150
            var element1 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("Test")
            };
            var element2 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                TargetName = new QualifiedName("Test")
            };

            Assert.That(element1.IsEqual(element2), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentTargetNameReturnsFalse()
        {
            // Covers: RelativePathElement.IsEqual TargetName mismatch - lines 153-155
            var element1 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TargetName = new QualifiedName("Name1")
            };
            var element2 = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TargetName = new QualifiedName("Name2")
            };

            Assert.That(element1.IsEqual(element2), Is.False);
        }

        #endregion

        #region RelativePathElement Clone / MemberwiseClone

        [Test]
        public void RelativePathElementCloneReturnsDeepCopy()
        {
            // Covers: RelativePathElement.Clone - lines 162-165, MemberwiseClone - lines 168-178
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                IsInverse = true,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("CloneTest", 2)
            };

            var clone = (RelativePathElement)element.Clone();

            Assert.That(clone, Is.Not.SameAs(element));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(element.ReferenceTypeId));
            Assert.That(clone.IsInverse, Is.EqualTo(element.IsInverse));
            Assert.That(clone.IncludeSubtypes, Is.EqualTo(element.IncludeSubtypes));
            Assert.That(clone.TargetName, Is.EqualTo(element.TargetName));
        }

        [Test]
        public void RelativePathElementMemberwiseCloneReturnsCopy()
        {
            // Covers: RelativePathElement.MemberwiseClone - lines 168-178
            var element = new RelativePathElement
            {
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IsInverse = false,
                IncludeSubtypes = true,
                TargetName = new QualifiedName("MemberwiseTest", 0)
            };

            var clone = (RelativePathElement)element.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(element));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Organizes));
            Assert.That(clone.IsInverse, Is.False);
            Assert.That(clone.IncludeSubtypes, Is.True);
            Assert.That(clone.TargetName, Is.EqualTo(new QualifiedName("MemberwiseTest", 0)));
        }

        #endregion

        #region Round-trip Parse and Format

        [Test]
        public void ParseAndFormatHierarchicalPathRoundTrips()
        {
            // Covers: Parse + Format integration for hierarchical paths
            var mockTypeTree = new Mock<ITypeTable>();

            var parsed = RelativePath.Parse("/ServerStatus", mockTypeTree.Object);
            var formatted = parsed.Format(mockTypeTree.Object);

            Assert.That(formatted, Does.Contain("ServerStatus"));
        }

        [Test]
        public void ParseAndFormatMultiSegmentPath()
        {
            // Covers: Parse + Format with multiple segments
            var mockTypeTree = new Mock<ITypeTable>();

            var parsed = RelativePath.Parse("/Server/Status", mockTypeTree.Object);

            Assert.That(parsed.Elements.Count, Is.EqualTo(2));

            var formatted = parsed.Format(mockTypeTree.Object);
            Assert.That(formatted, Does.Contain("Server"));
            Assert.That(formatted, Does.Contain("Status"));
        }

        #endregion
    }
}
