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
        [Test]
        public void DefaultConstructorCreatesEmptyElements()
        {
            var path = new RelativePath();

            Assert.That(path.Elements.IsNull, Is.True);
            Assert.That(path.Elements.Count, Is.Zero);
        }

        [Test]
        public void ConstructorWithBrowseNameCreatesOneElement()
        {
            var browseName = new QualifiedName("TestNode", 0);
            var path = new RelativePath(browseName);

            Assert.That(path.Elements.IsNull, Is.False);
            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(path.Elements[0].IsInverse, Is.False);
            Assert.That(path.Elements[0].IncludeSubtypes, Is.True);
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(browseName));
        }

        [Test]
        public void ConstructorWithRefTypeAndBrowseNameCreatesOneElement()
        {
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
            NodeId refTypeId = ReferenceTypeIds.HasProperty;
            var browseName = new QualifiedName("Property1", 2);
            var path = new RelativePath(refTypeId, true, false, browseName);

            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].ReferenceTypeId, Is.EqualTo(refTypeId));
            Assert.That(path.Elements[0].IsInverse, Is.True);
            Assert.That(path.Elements[0].IncludeSubtypes, Is.False);
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(browseName));
        }

        [Test]
        public void ElementsSetterAcceptsCollection()
        {
            var path = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        TargetName = new QualifiedName("Child")
                    }
                ]
            };

            Assert.That(path.Elements.Count, Is.EqualTo(1));
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var path = new RelativePath();
            Assert.That(path.TypeId, Is.EqualTo(DataTypeIds.RelativePath));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var path = new RelativePath();
            Assert.That(path.BinaryEncodingId, Is.EqualTo(ObjectIds.RelativePath_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var path = new RelativePath();
            Assert.That(path.XmlEncodingId, Is.EqualTo(ObjectIds.RelativePath_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeWritesElementsArray()
        {
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
            var mockDecoder = new Mock<IDecoder>();
            ArrayOf<RelativePathElement> elements =
            [
                new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TargetName = new QualifiedName("Prop1")
                }
            ];
            mockDecoder.Setup(d => d.ReadEncodeableArray<RelativePathElement>("Elements"))
                .Returns(elements);

            var path = new RelativePath();
            path.Decode(mockDecoder.Object);

            mockDecoder.Verify(d => d.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            mockDecoder.Verify(d => d.PopNamespace(), Times.Once);
            Assert.That(path.Elements.Count, Is.EqualTo(1));
            Assert.That(path.Elements[0].TargetName, Is.EqualTo(new QualifiedName("Prop1")));
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            var path = new RelativePath(new QualifiedName("Test"));
            Assert.That(path.IsEqual(path), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            var path = new RelativePath(new QualifiedName("Test"));
            Assert.That(path.IsEqual(null!), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentTypeReturnsFalse()
        {
            var path = new RelativePath(new QualifiedName("Test"));
            var element = new RelativePathElement();
            Assert.That(path.IsEqual(element), Is.False);
        }

        [Test]
        public void IsEqualWithEqualElementsReturnsTrue()
        {
            var path1 = new RelativePath(new QualifiedName("Test"));
            var path2 = new RelativePath(new QualifiedName("Test"));
            Assert.That(path1.IsEqual(path2), Is.True);
        }

        [Test]
        public void IsEqualWithDifferentElementsReturnsFalse()
        {
            var path1 = new RelativePath(new QualifiedName("Node1"));
            var path2 = new RelativePath(new QualifiedName("Node2"));
            Assert.That(path1.IsEqual(path2), Is.False);
        }

        [Test]
        public void IsEqualWithEmptyPathsReturnsTrue()
        {
            var path1 = new RelativePath();
            var path2 = new RelativePath();
            Assert.That(path1.IsEqual(path2), Is.True);
        }

        [Test]
        public void CloneReturnsDeepCopy()
        {
            var path = new RelativePath(ReferenceTypeIds.HasComponent, true, false, new QualifiedName("Target", 1));

            var clone = (RelativePath)path.Clone();

            Assert.That(clone, Is.Not.SameAs(path));
            Assert.That(clone.Elements.Count, Is.EqualTo(path.Elements.Count));
            Assert.That(clone.Elements[0].TargetName, Is.EqualTo(path.Elements[0].TargetName));
            Assert.That(clone.Elements[0].ReferenceTypeId, Is.EqualTo(path.Elements[0].ReferenceTypeId));
            Assert.That(clone.Elements[0].IsInverse, Is.EqualTo(path.Elements[0].IsInverse));
            Assert.That(clone.Elements[0].IncludeSubtypes, Is.EqualTo(path.Elements[0].IncludeSubtypes));
        }

        [Test]
        public void MemberwiseCloneClonesElementsCollection()
        {
            var path = new RelativePath(new QualifiedName("Original"));

            var clone = (RelativePath)path.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(path));
            Assert.That(clone.Elements.Count, Is.EqualTo(1));
        }

        [Test]
        public void FormatWithEmptyPathReturnsEmptyString()
        {
            var path = new RelativePath();
            var mockTypeTree = new Mock<ITypeTable>();

            string result = path.Format(mockTypeTree.Object);

            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatWithHierarchicalElementReturnsPath()
        {
            var path = new RelativePath(new QualifiedName("ServerStatus"));
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree.Setup(t => t.FindReferenceTypeName(ReferenceTypeIds.HierarchicalReferences))
                .Returns(new QualifiedName("HierarchicalReferences"));

            string result = path.Format(mockTypeTree.Object);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Not.Empty);
        }

        [Test]
        public void IsEmptyWithNullReturnsTrue()
        {
            Assert.That(RelativePath.IsEmpty(null!), Is.True);
        }

        [Test]
        public void IsEmptyWithEmptyPathReturnsTrue()
        {
            var path = new RelativePath();
            Assert.That(RelativePath.IsEmpty(path), Is.True);
        }

        [Test]
        public void IsEmptyWithElementsReturnsFalse()
        {
            var path = new RelativePath(new QualifiedName("Node"));
            Assert.That(RelativePath.IsEmpty(path), Is.False);
        }

        [Test]
        public void ParseWithNullTypeTreeThrowsArgumentNullException()
        {
            Assert.That(
                () => RelativePath.Parse("/TestNode", null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ParseHierarchicalPathCreatesCorrectElements()
        {
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
            var mockTypeTree = new Mock<ITypeTable>();

            var result = RelativePath.Parse("/Node1.Property1", mockTypeTree.Object);

            Assert.That(result.Elements.Count, Is.EqualTo(2));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
            Assert.That(result.Elements[1].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.Aggregates));
        }

        [Test]
        public void ParseWithNamespaceTablesHierarchicalPath()
        {
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
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            Assert.That(
                () => RelativePath.Parse("<HasComponent>Target", null!, currentTable, targetTable),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ParseWithNamespaceTablesNullRefTypeIdThrows()
        {
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
            var currentTable = new NamespaceTable();
            var targetTable = new NamespaceTable();

            var result = RelativePath.Parse("/Node", null!, currentTable, targetTable);

            Assert.That(result.Elements.Count, Is.EqualTo(1));
            Assert.That(result.Elements[0].ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
        }

        [Test]
        public void RelativePathElementDefaultConstructorInitializesDefaults()
        {
            // Defaults: IsInverse=true, IncludeSubtypes=true, ReferenceTypeId=default, TargetName=default
            var element = new RelativePathElement();

            Assert.That(element.ReferenceTypeId, Is.Default);
            Assert.That(element.IsInverse, Is.True);
            Assert.That(element.IncludeSubtypes, Is.True);
            Assert.That(element.TargetName, Is.Default);
        }

        [Test]
        public void RelativePathElementPropertiesCanBeSet()
        {
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
        public void RelativePathElementEncodeWritesAllFields()
        {
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

        [Test]
        public void RelativePathElementIsEqualWithSameReferenceReturnsTrue()
        {
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
            var element = new RelativePathElement();
            Assert.That(element.IsEqual(null!), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithDifferentTypeReturnsFalse()
        {
            var element = new RelativePathElement();
            var path = new RelativePath();
            Assert.That(element.IsEqual(path), Is.False);
        }

        [Test]
        public void RelativePathElementIsEqualWithMatchingPropertiesReturnsTrue()
        {
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

        [Test]
        public void RelativePathElementCloneReturnsDeepCopy()
        {
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

        [Test]
        public void ParseAndFormatHierarchicalPathRoundTrips()
        {
            var mockTypeTree = new Mock<ITypeTable>();

            var parsed = RelativePath.Parse("/ServerStatus", mockTypeTree.Object);
            string formatted = parsed.Format(mockTypeTree.Object);

            Assert.That(formatted, Does.Contain("ServerStatus"));
        }

        [Test]
        public void ParseAndFormatMultiSegmentPath()
        {
            var mockTypeTree = new Mock<ITypeTable>();

            var parsed = RelativePath.Parse("/Server/Status", mockTypeTree.Object);

            Assert.That(parsed.Elements.Count, Is.EqualTo(2));

            string formatted = parsed.Format(mockTypeTree.Object);
            Assert.That(formatted, Does.Contain("Server"));
            Assert.That(formatted, Does.Contain("Status"));
        }
    }
}
