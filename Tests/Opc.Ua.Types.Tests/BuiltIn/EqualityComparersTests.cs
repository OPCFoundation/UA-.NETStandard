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

using System;
using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for all EqualityComparer classes in the Opc.Ua namespace.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EqualityComparersTests
    {
        [Test]
        public void NodeIdComparerDefaultInstanceExists()
        {
            Assert.That(NodeIdComparer.Default, Is.Not.Null);
            Assert.That(NodeIdComparer.Default, Is.SameAs(NodeIdComparer.Default));
        }

        [Test]
        public void NodeIdComparerEqualsSameNodeId()
        {
            var nodeId = new NodeId(123, 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId, nodeId), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsEqualNodeIds()
        {
            var nodeId1 = new NodeId(123, 1);
            var nodeId2 = new NodeId(123, 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsDifferentNodeIds()
        {
            var nodeId1 = new NodeId(123, 1);
            var nodeId2 = new NodeId(456, 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void NodeIdComparerEqualsDifferentNamespaceIndex()
        {
            var nodeId1 = new NodeId(123, 1);
            var nodeId2 = new NodeId(123, 2);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void NodeIdComparerEqualsNullNodeIds()
        {
            Assert.That(NodeIdComparer.Default.Equals(NodeId.Null, NodeId.Null), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsStringNodeIds()
        {
            var nodeId1 = new NodeId("TestString", 1);
            var nodeId2 = new NodeId("TestString", 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsDifferentStringNodeIds()
        {
            var nodeId1 = new NodeId("TestString1", 1);
            var nodeId2 = new NodeId("TestString2", 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void NodeIdComparerEqualsGuidNodeIds()
        {
            var guid = Guid.NewGuid();
            var nodeId1 = new NodeId(guid, 1);
            var nodeId2 = new NodeId(guid, 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsDifferentGuidNodeIds()
        {
            var nodeId1 = new NodeId(Guid.NewGuid(), 1);
            var nodeId2 = new NodeId(Guid.NewGuid(), 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void NodeIdComparerEqualsOpaqueNodeIds()
        {
            byte[] opaque = [1, 2, 3, 4];
            var nodeId1 = new NodeId(opaque, 1);
            var nodeId2 = new NodeId(opaque, 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.True);
        }

        [Test]
        public void NodeIdComparerEqualsDifferentOpaqueNodeIds()
        {
            var nodeId1 = new NodeId(ByteString.From([1, 2, 3, 4]), 1);
            var nodeId2 = new NodeId(ByteString.From([5, 6, 7, 8]), 1);
            Assert.That(NodeIdComparer.Default.Equals(nodeId1, nodeId2), Is.False);
        }

        [Test]
        public void NodeIdComparerGetHashCodeConsistency()
        {
            var nodeId = new NodeId(123, 1);
            int hash1 = NodeIdComparer.Default.GetHashCode(nodeId);
            int hash2 = NodeIdComparer.Default.GetHashCode(nodeId);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void NodeIdComparerGetHashCodeEqualNodeIdsHaveSameHashCode()
        {
            var nodeId1 = new NodeId(123, 1);
            var nodeId2 = new NodeId(123, 1);
            Assert.That(
                NodeIdComparer.Default.GetHashCode(nodeId1),
                Is.EqualTo(NodeIdComparer.Default.GetHashCode(nodeId2)));
        }

        [Test]
        public void NodeIdComparerCanBeUsedInDictionary()
        {
            var dictionary = new Dictionary<NodeId, string>(NodeIdComparer.Default);
            var nodeId = new NodeId(123, 1);
            dictionary[nodeId] = "Test";
            Assert.That(dictionary.ContainsKey(new NodeId(123, 1)), Is.True);
            Assert.That(dictionary[new NodeId(123, 1)], Is.EqualTo("Test"));
        }

        [Test]
        public void NodeIdComparerCanBeUsedInHashSet()
        {
            var hashSet = new HashSet<NodeId>(NodeIdComparer.Default)
            {
                new(123, 1)
            };
            Assert.That(hashSet.Contains(new NodeId(123, 1)), Is.True);
            Assert.That(hashSet.Contains(new NodeId(456, 1)), Is.False);
        }

        [Test]
        public void SequenceEqualityComparerDefaultInstanceExists()
        {
            Assert.That(SequenceEqualityComparer<int>.Default, Is.Not.Null);
            Assert.That(
                SequenceEqualityComparer<int>.Default,
                Is.SameAs(SequenceEqualityComparer<int>.Default));
        }

        [Test]
        public void SequenceEqualityComparerEqualsSameReference()
        {
            int[] array = [1, 2, 3, 4, 5];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array, array), Is.True);
        }

        [Test]
        public void SequenceEqualityComparerEqualsEqualArrays()
        {
            int[] array1 = [1, 2, 3, 4, 5];
            int[] array2 = [1, 2, 3, 4, 5];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void SequenceEqualityComparerEqualsDifferentArrays()
        {
            int[] array1 = [1, 2, 3, 4, 5];
            int[] array2 = [1, 2, 3, 4, 6];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void SequenceEqualityComparerEqualsDifferentLengths()
        {
            int[] array1 = [1, 2, 3, 4, 5];
            int[] array2 = [1, 2, 3, 4];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void SequenceEqualityComparerEqualsNullFirstArray()
        {
            int[] array = [1, 2, 3];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(null, array), Is.False);
        }

        [Test]
        public void SequenceEqualityComparerEqualsNullSecondArray()
        {
            int[] array = [1, 2, 3];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array, null), Is.False);
        }

        [Test]
        public void SequenceEqualityComparerEqualsBothNull()
        {
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(null, null), Is.True);
        }

        [Test]
        public void SequenceEqualityComparerEqualsEmptyArrays()
        {
            int[] array1 = [];
            int[] array2 = [];
            Assert.That(SequenceEqualityComparer<int>.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void SequenceEqualityComparerGetHashCodeConsistency()
        {
            int[] array = [1, 2, 3, 4, 5];
            int hash1 = SequenceEqualityComparer<int>.Default.GetHashCode(array);
            int hash2 = SequenceEqualityComparer<int>.Default.GetHashCode(array);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void SequenceEqualityComparerGetHashCodeEqualArraysHaveSameHashCode()
        {
            int[] array1 = [1, 2, 3, 4, 5];
            int[] array2 = [1, 2, 3, 4, 5];
            Assert.That(
                SequenceEqualityComparer<int>.Default.GetHashCode(array1),
                Is.EqualTo(SequenceEqualityComparer<int>.Default.GetHashCode(array2)));
        }

        [Test]
        public void SequenceEqualityComparerGetHashCodeNullArray()
        {
            int hash = SequenceEqualityComparer<int>.Default.GetHashCode(null);
            Assert.That(hash, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        [Test]
        public void SequenceEqualityComparerWorksWithDifferentTypes()
        {
            double[] array1 = [1.0, 2.0, 3.0];
            double[] array2 = [1.0, 2.0, 3.0];
            Assert.That(SequenceEqualityComparer<double>.Default.Equals(array1, array2), Is.True);

            byte[] byteArray1 = [0x01, 0x02, 0x03];
            byte[] byteArray2 = [0x01, 0x02, 0x03];
            Assert.That(
                SequenceEqualityComparer<byte>.Default.Equals(byteArray1, byteArray2),
                Is.True);
        }

        [Test]
        public void SequenceEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary =
                new Dictionary<int[], string>(SequenceEqualityComparer<int>.Default);
            int[] key = [1, 2, 3];
            dictionary[key] = "Test";
            Assert.That(dictionary.ContainsKey([1, 2, 3]), Is.True);
            Assert.That(dictionary[[1, 2, 3]], Is.EqualTo("Test"));
        }

        [Test]
        public void ArrayEqualityComparerDefaultInstanceExists()
        {
            Assert.That(ArrayEqualityComparer<string>.Default, Is.Not.Null);
            Assert.That(
                ArrayEqualityComparer<string>.Default,
                Is.SameAs(ArrayEqualityComparer<string>.Default));
        }

        [Test]
        public void ArrayEqualityComparerEqualsSameReference()
        {
            string[] array = ["a", "b", "c"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array, array), Is.True);
        }

        [Test]
        public void ArrayEqualityComparerEqualsEqualArrays()
        {
            string[] array1 = ["a", "b", "c"];
            string[] array2 = ["a", "b", "c"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ArrayEqualityComparerEqualsDifferentArrays()
        {
            string[] array1 = ["a", "b", "c"];
            string[] array2 = ["a", "b", "d"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ArrayEqualityComparerEqualsDifferentLengths()
        {
            string[] array1 = ["a", "b", "c"];
            string[] array2 = ["a", "b"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ArrayEqualityComparerEqualsNullFirstArray()
        {
            string[] array = ["a", "b", "c"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(null, array), Is.False);
        }

        [Test]
        public void ArrayEqualityComparerEqualsNullSecondArray()
        {
            string[] array = ["a", "b", "c"];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array, null), Is.False);
        }

        [Test]
        public void ArrayEqualityComparerEqualsBothNull()
        {
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(null, null), Is.True);
        }

        [Test]
        public void ArrayEqualityComparerEqualsEmptyArrays()
        {
            string[] array1 = [];
            string[] array2 = [];
            Assert.That(ArrayEqualityComparer<string>.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ArrayEqualityComparerGetHashCodeConsistency()
        {
            string[] array = ["a", "b", "c"];
            int hash1 = ArrayEqualityComparer<string>.Default.GetHashCode(array);
            int hash2 = ArrayEqualityComparer<string>.Default.GetHashCode(array);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ArrayEqualityComparerGetHashCodeEqualArraysHaveSameHashCode()
        {
            string[] array1 = ["a", "b", "c"];
            string[] array2 = ["a", "b", "c"];
            Assert.That(
                ArrayEqualityComparer<string>.Default.GetHashCode(array1),
                Is.EqualTo(ArrayEqualityComparer<string>.Default.GetHashCode(array2)));
        }

        [Test]
        public void ArrayEqualityComparerGetHashCodeNullArray()
        {
            int hash = ArrayEqualityComparer<string>.Default.GetHashCode(null);
            Assert.That(hash, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        [Test]
        public void ArrayEqualityComparerWorksWithNodeIds()
        {
            NodeId[] array1 = [new NodeId(1), new NodeId(2), new NodeId(3)];
            NodeId[] array2 = [new NodeId(1), new NodeId(2), new NodeId(3)];
            Assert.That(ArrayEqualityComparer<NodeId>.Default.Equals(array1, array2), Is.True);

            NodeId[] array3 = [new NodeId(1), new NodeId(2), new NodeId(4)];
            Assert.That(ArrayEqualityComparer<NodeId>.Default.Equals(array1, array3), Is.False);
        }

        [Test]
        public void ArrayEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary =
                new Dictionary<string[], string>(ArrayEqualityComparer<string>.Default);
            string[] key = ["a", "b", "c"];
            dictionary[key] = "Test";
            Assert.That(dictionary.ContainsKey(["a", "b", "c"]), Is.True);
            Assert.That(dictionary[["a", "b", "c"]], Is.EqualTo("Test"));
        }

        [Test]
        public void ByteStringEqualityComparerDefaultInstanceExists()
        {
            Assert.That(ByteStringEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                ByteStringEqualityComparer.Default,
                Is.SameAs(ByteStringEqualityComparer.Default));
        }

        [Test]
        public void ByteStringEqualityComparerEqualsSameReference()
        {
            byte[] array = [1, 2, 3, 4, 5];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array, array), Is.True);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsEqualArrays()
        {
            byte[] array1 = [1, 2, 3, 4, 5];
            byte[] array2 = [1, 2, 3, 4, 5];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsDifferentArrays()
        {
            byte[] array1 = [1, 2, 3, 4, 5];
            byte[] array2 = [1, 2, 3, 4, 6];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsDifferentLengths()
        {
            byte[] array1 = [1, 2, 3, 4, 5];
            byte[] array2 = [1, 2, 3, 4];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsNullFirstArray()
        {
            byte[] array = [1, 2, 3];
            Assert.That(ByteStringEqualityComparer.Default.Equals(null, array), Is.False);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsNullSecondArray()
        {
            byte[] array = [1, 2, 3];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array, null), Is.False);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsBothNull()
        {
            Assert.That(ByteStringEqualityComparer.Default.Equals(null, null), Is.True);
        }

        [Test]
        public void ByteStringEqualityComparerEqualsEmptyArrays()
        {
            byte[] array1 = [];
            byte[] array2 = [];
            Assert.That(ByteStringEqualityComparer.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ByteStringEqualityComparerGetHashCodeConsistency()
        {
            byte[] array = [1, 2, 3, 4, 5];
            int hash1 = ByteStringEqualityComparer.Default.GetHashCode(array);
            int hash2 = ByteStringEqualityComparer.Default.GetHashCode(array);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ByteStringEqualityComparerGetHashCodeEqualArraysHaveSameHashCode()
        {
            byte[] array1 = [1, 2, 3, 4, 5];
            byte[] array2 = [1, 2, 3, 4, 5];
            Assert.That(
                ByteStringEqualityComparer.Default.GetHashCode(array1),
                Is.EqualTo(ByteStringEqualityComparer.Default.GetHashCode(array2)));
        }

        [Test]
        public void ByteStringEqualityComparerGetHashCodeNullArray()
        {
            int hash = ByteStringEqualityComparer.Default.GetHashCode(null);
            Assert.That(hash, Is.EqualTo(0));
        }

        [Test]
        public void ByteStringEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary = new Dictionary<byte[], string>(ByteStringEqualityComparer.Default);
            byte[] key = [1, 2, 3];
            dictionary[key] = "Test";
            Assert.That(dictionary.ContainsKey([1, 2, 3]), Is.True);
            Assert.That(dictionary[[1, 2, 3]], Is.EqualTo("Test"));
        }

        [Test]
        public void ByteStringEqualityComparerCanBeUsedInHashSet()
        {
            var hashSet = new HashSet<byte[]>([[1, 2, 3]], ByteStringEqualityComparer.Default);
            Assert.That(hashSet.Contains([1, 2, 3]), Is.True);
            Assert.That(hashSet.Contains([4, 5, 6]), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerDefaultInstanceExists()
        {
            Assert.That(ByteStringArrayEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                ByteStringArrayEqualityComparer.Default,
                Is.SameAs(ByteStringArrayEqualityComparer.Default));
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsSameReference()
        {
            byte[][] array = [[1, 2, 3], [4, 5, 6]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array, array), Is.True);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsEqualArrays()
        {
            byte[][] array1 = [[1, 2, 3], [4, 5, 6]];
            byte[][] array2 = [[1, 2, 3], [4, 5, 6]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsDifferentInnerArrays()
        {
            byte[][] array1 = [[1, 2, 3], [4, 5, 6]];
            byte[][] array2 = [[1, 2, 3], [4, 5, 7]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsDifferentOuterLengths()
        {
            byte[][] array1 = [[1, 2, 3], [4, 5, 6]];
            byte[][] array2 = [[1, 2, 3]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsDifferentInnerLengths()
        {
            byte[][] array1 = [[1, 2, 3], [4, 5, 6]];
            byte[][] array2 = [[1, 2, 3], [4, 5]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsNullFirstArray()
        {
            byte[][] array = [[1, 2, 3]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(null, array), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsNullSecondArray()
        {
            byte[][] array = [[1, 2, 3]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array, null), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsBothNull()
        {
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(null, null), Is.True);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsEmptyArrays()
        {
            byte[][] array1 = [];
            byte[][] array2 = [];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsWithNullInnerArrays()
        {
            byte[][] array1 = [[1, 2, 3], null];
            byte[][] array2 = [[1, 2, 3], null];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.True);
        }

        [Test]
        public void ByteStringArrayEqualityComparerEqualsMismatchedNullInnerArrays()
        {
            byte[][] array1 = [[1, 2, 3], null];
            byte[][] array2 = [[1, 2, 3], [4, 5, 6]];
            Assert.That(ByteStringArrayEqualityComparer.Default.Equals(array1, array2), Is.False);
        }

        [Test]
        public void ByteStringArrayEqualityComparerGetHashCodeConsistency()
        {
            byte[][] array = [[1, 2, 3], [4, 5, 6]];
            int hash1 = ByteStringArrayEqualityComparer.Default.GetHashCode(array);
            int hash2 = ByteStringArrayEqualityComparer.Default.GetHashCode(array);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ByteStringArrayEqualityComparerGetHashCodeEqualArraysHaveSameHashCode()
        {
            byte[][] array1 = [[1, 2, 3], [4, 5, 6]];
            byte[][] array2 = [[1, 2, 3], [4, 5, 6]];
            Assert.That(
                ByteStringArrayEqualityComparer.Default.GetHashCode(array1),
                Is.EqualTo(ByteStringArrayEqualityComparer.Default.GetHashCode(array2)));
        }

        [Test]
        public void ByteStringArrayEqualityComparerGetHashCodeNullArray()
        {
            int hash = ByteStringArrayEqualityComparer.Default.GetHashCode(null);
            Assert.That(hash, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        [Test]
        public void ByteStringArrayEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary =
                new Dictionary<byte[][], string>(ByteStringArrayEqualityComparer.Default);
            byte[][] key = [[1, 2, 3], [4, 5, 6]];
            dictionary[key] = "Test";
            Assert.That(dictionary.ContainsKey([[1, 2, 3], [4, 5, 6]]), Is.True);
            Assert.That(dictionary[[[1, 2, 3], [4, 5, 6]]], Is.EqualTo("Test"));
        }

        [Test]
        public void XmlElementStringEqualityComparerDefaultInstanceExists()
        {
            Assert.That(XmlElementStringEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                XmlElementStringEqualityComparer.Default,
                Is.SameAs(XmlElementStringEqualityComparer.Default));
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsSameReference()
        {
            System.Xml.XmlElement element = CreateXmlElement("test");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element, element),
                Is.True);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsEqualElements()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "value");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element1, element2),
                Is.True);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsDifferentElements()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "value1");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "value2");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element1, element2),
                Is.False);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsDifferentTagNames()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test1", "value");
            System.Xml.XmlElement element2 = CreateXmlElement("test2", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element1, element2),
                Is.False);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsNullFirstElement()
        {
            System.Xml.XmlElement element = CreateXmlElement("test", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(null, element),
                Is.False);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsNullSecondElement()
        {
            System.Xml.XmlElement element = CreateXmlElement("test", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element, null),
                Is.False);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsBothNull()
        {
            Assert.That(XmlElementStringEqualityComparer.Default.Equals(null, null), Is.True);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsWithAttributes()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "content", "attr", "value");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "content", "attr", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element1, element2),
                Is.True);
        }

        [Test]
        public void XmlElementStringEqualityComparerEqualsDifferentAttributes()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "content", "attr", "value1");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "content", "attr", "value2");
            Assert.That(
                XmlElementStringEqualityComparer.Default.Equals(element1, element2),
                Is.False);
        }

        [Test]
        public void XmlElementStringEqualityComparerGetHashCodeConsistency()
        {
            System.Xml.XmlElement element = CreateXmlElement("test", "value");
            int hash1 = XmlElementStringEqualityComparer.Default.GetHashCode(element);
            int hash2 = XmlElementStringEqualityComparer.Default.GetHashCode(element);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void XmlElementStringEqualityComparerGetHashCodeEqualElementsHaveSameHashCode()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "value");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "value");
            Assert.That(
                XmlElementStringEqualityComparer.Default.GetHashCode(element1),
                Is.EqualTo(
                    XmlElementStringEqualityComparer.Default.GetHashCode(element2)));
        }

        [Test]
        public void XmlElementStringEqualityComparerGetHashCodeNullElement()
        {
            int hash = XmlElementStringEqualityComparer.Default.GetHashCode(null);
            Assert.That(hash, Is.EqualTo(EqualityComparer<string>.Default.GetHashCode(null)));
        }

        [Test]
        public void XmlElementStringEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary =
                new Dictionary<System.Xml.XmlElement, string>(XmlElementStringEqualityComparer.Default);
            System.Xml.XmlElement element1 = CreateXmlElement("test", "value");
            dictionary[element1] = "Test";

            System.Xml.XmlElement element2 = CreateXmlElement("test", "value");
            Assert.That(dictionary.ContainsKey(element2), Is.True);
            Assert.That(dictionary[element2], Is.EqualTo("Test"));
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerDefaultInstanceExists()
        {
            Assert.That(XmlElementArrayStringEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default,
                Is.SameAs(XmlElementArrayStringEqualityComparer.Default));
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsSameReference()
        {
            System.Xml.XmlElement[] array = [
                CreateXmlElement("test1"),
                CreateXmlElement("test2")
            ];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array, array),
                Is.True);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsEqualArrays()
        {
            System.Xml.XmlElement[] array1 = [CreateXmlElement("test", "value")];
            System.Xml.XmlElement[] array2 = [CreateXmlElement("test", "value")];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array1, array2),
                Is.True);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsDifferentArrays()
        {
            System.Xml.XmlElement[] array1 = [CreateXmlElement("test", "value1")];
            System.Xml.XmlElement[] array2 = [CreateXmlElement("test", "value2")];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array1, array2),
                Is.False);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsDifferentLengths()
        {
            System.Xml.XmlElement element1 = CreateXmlElement("test", "value");
            System.Xml.XmlElement element2 = CreateXmlElement("test", "value");
            System.Xml.XmlElement[] array1 = [element1, element1];
            System.Xml.XmlElement[] array2 = [element2];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array1, array2),
                Is.False);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsNullFirstArray()
        {
            System.Xml.XmlElement[] array = [CreateXmlElement("test", "value")];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(null, array),
                Is.False);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsNullSecondArray()
        {
            System.Xml.XmlElement[] array = [CreateXmlElement("test", "value")];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array, null),
                Is.False);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsBothNull()
        {
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(null, null),
                Is.True);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsEmptyArrays()
        {
            System.Xml.XmlElement[] array1 = [];
            System.Xml.XmlElement[] array2 = [];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array1, array2),
                Is.True);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerEqualsWithNullElements()
        {
            System.Xml.XmlElement element = CreateXmlElement("test", "value");
            System.Xml.XmlElement[] array1 = [element, null];
            System.Xml.XmlElement[] array2 = [CreateXmlElement("test", "value"), null];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.Equals(array1, array2),
                Is.True);
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerGetHashCodeConsistency()
        {
            System.Xml.XmlElement[] array = [CreateXmlElement("test", "value")];
            int hash1 = XmlElementArrayStringEqualityComparer.Default.GetHashCode(array);
            int hash2 = XmlElementArrayStringEqualityComparer.Default.GetHashCode(array);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerGetHashCodeEqualArraysHaveSameHashCode()
        {
            System.Xml.XmlElement[] array1 = [CreateXmlElement("test", "value")];
            System.Xml.XmlElement[] array2 = [CreateXmlElement("test", "value")];
            Assert.That(
                XmlElementArrayStringEqualityComparer.Default.GetHashCode(array1),
                Is.EqualTo(
                    XmlElementArrayStringEqualityComparer.Default.GetHashCode(array2)));
        }

        [Test]
        public void XmlElementArrayStringEqualityComparerGetHashCodeNullArray()
        {
            int hash = XmlElementArrayStringEqualityComparer.Default.GetHashCode(null);
            Assert.That(hash, Is.Not.EqualTo(0).Or.EqualTo(0));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerDefaultInstanceExists()
        {
            Assert.That(XmlQualifiedNameEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default,
                Is.SameAs(XmlQualifiedNameEqualityComparer.Default));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsSameReference()
        {
            var name = new XmlQualifiedName("test", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name, name),
                Is.True);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsEqualNames()
        {
            var name1 = new XmlQualifiedName("test", "http://example.com");
            var name2 = new XmlQualifiedName("test", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name1, name2),
                Is.True);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsDifferentLocalNames()
        {
            var name1 = new XmlQualifiedName("test1", "http://example.com");
            var name2 = new XmlQualifiedName("test2", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name1, name2),
                Is.False);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsDifferentNamespaces()
        {
            var name1 = new XmlQualifiedName("test", "http://example1.com");
            var name2 = new XmlQualifiedName("test", "http://example2.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name1, name2),
                Is.False);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsNullFirstName()
        {
            var name = new XmlQualifiedName("test", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(null, name),
                Is.False);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsNullSecondName()
        {
            var name = new XmlQualifiedName("test", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name, null),
                Is.False);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsBothNull()
        {
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(null, null),
                Is.True);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsEmptyNames()
        {
            var name1 = new XmlQualifiedName();
            var name2 = new XmlQualifiedName();
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name1, name2),
                Is.True);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerEqualsEmptyNamespace()
        {
            var name1 = new XmlQualifiedName("test", string.Empty);
            var name2 = new XmlQualifiedName("test", string.Empty);
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.Equals(name1, name2),
                Is.True);
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerGetHashCodeConsistency()
        {
            var name = new XmlQualifiedName("test", "http://example.com");
            int hash1 = XmlQualifiedNameEqualityComparer.Default.GetHashCode(name);
            int hash2 = XmlQualifiedNameEqualityComparer.Default.GetHashCode(name);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerGetHashCodeEqualNamesHaveSameHashCode()
        {
            var name1 = new XmlQualifiedName("test", "http://example.com");
            var name2 = new XmlQualifiedName("test", "http://example.com");
            Assert.That(
                XmlQualifiedNameEqualityComparer.Default.GetHashCode(name1),
                Is.EqualTo(XmlQualifiedNameEqualityComparer.Default.GetHashCode(name2)));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerGetHashCodeNullName()
        {
            int hash = XmlQualifiedNameEqualityComparer.Default.GetHashCode(null);
            Assert.That(hash, Is.EqualTo(HashCode.Combine((string)null, (string)null)));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary = new Dictionary<XmlQualifiedName, string>(
                XmlQualifiedNameEqualityComparer.Default);
            var name = new XmlQualifiedName("test", "http://example.com");
            dictionary[name] = "Test";

            var name2 = new XmlQualifiedName("test", "http://example.com");
            Assert.That(dictionary.ContainsKey(name2), Is.True);
            Assert.That(dictionary[name2], Is.EqualTo("Test"));
        }

        [Test]
        public void XmlQualifiedNameEqualityComparerCanBeUsedInHashSet()
        {
            var hashSet =
                new HashSet<XmlQualifiedName>(XmlQualifiedNameEqualityComparer.Default)
                {
                    new("test", "http://example.com")
                };
            Assert.That(
                hashSet.Contains(new XmlQualifiedName("test", "http://example.com")),
                Is.True);
            Assert.That(
                hashSet.Contains(new XmlQualifiedName("other", "http://example.com")),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerDefaultInstanceExists()
        {
            Assert.That(ReferenceEqualityComparer.Default, Is.Not.Null);
            Assert.That(
                ReferenceEqualityComparer.Default,
                Is.SameAs(ReferenceEqualityComparer.Default));
        }

        [Test]
        public void ReferenceEqualityComparerEqualsSameReference()
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference, reference),
                Is.True);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsEqualReferences()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference2),
                Is.True);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsDifferentTargetId()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(3)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference2),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsDifferentReferenceTypeId()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(2),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference2),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsDifferentIsInverse()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = true,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference2),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsNullFirstReference()
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(null, reference),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsNullSecondReference()
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference, null),
                Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerEqualsBothNull()
        {
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(null, null),
                Is.True);
        }

        [Test]
        public void ReferenceEqualityComparerGetHashCodeConsistency()
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            int hash1 = ReferenceEqualityComparer.Default.GetHashCode(reference);
            int hash2 = ReferenceEqualityComparer.Default.GetHashCode(reference);
            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void ReferenceEqualityComparerGetHashCodeEqualReferencesHaveSameHashCode()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(
                ReferenceEqualityComparer.Default.GetHashCode(reference1),
                Is.EqualTo(ReferenceEqualityComparer.Default.GetHashCode(reference2)));
        }

        [Test]
        public void ReferenceEqualityComparerCanBeUsedInDictionary()
        {
            var dictionary =
                new Dictionary<IReference, string>(ReferenceEqualityComparer.Default);
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            dictionary[reference] = "Test";

            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(dictionary.ContainsKey(reference2), Is.True);
            Assert.That(dictionary[reference2], Is.EqualTo("Test"));
        }

        [Test]
        public void ReferenceEqualityComparerCanBeUsedInHashSet()
        {
            var hashSet = new HashSet<IReference>(ReferenceEqualityComparer.Default);
            var reference = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            hashSet.Add(reference);

            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2)
            };
            Assert.That(hashSet.Contains(reference2), Is.True);

            var reference3 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(3),
                IsInverse = false,
                TargetId = new ExpandedNodeId(4)
            };
            Assert.That(hashSet.Contains(reference3), Is.False);
        }

        [Test]
        public void ReferenceEqualityComparerWithExpandedNodeIdNamespaceUri()
        {
            var reference1 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2, "http://example.com")
            };
            var reference2 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2, "http://example.com")
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference2),
                Is.True);

            var reference3 = new ReferenceNode
            {
                ReferenceTypeId = new NodeId(1),
                IsInverse = false,
                TargetId = new ExpandedNodeId(2, "http://different.com")
            };
            Assert.That(
                ReferenceEqualityComparer.Default.Equals(reference1, reference3),
                Is.False);
        }

        private static System.Xml.XmlElement CreateXmlElement(string name, string value = null, string attributeName = null, string attributeValue = null)
        {
            var doc = new XmlDocument();
            System.Xml.XmlElement element = doc.CreateElement(name);
            if (value != null)
            {
                element.InnerText = value;
            }
            if (attributeName != null)
            {
                element.SetAttribute(attributeName, attributeValue);
            }
            return element;
        }
    }
}
