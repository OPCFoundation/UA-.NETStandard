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

using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Nodes
{
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceNodeTests
    {
        private static ReferenceNode CreateNode(
            uint refTypeId = 42, bool isInverse = false, uint targetId = 100)
        {
            return new ReferenceNode(new NodeId(refTypeId), isInverse, new ExpandedNodeId(targetId));
        }

        [Test]
        public void CloneReturnsNewInstanceWithSameValues()
        {
            ReferenceNode original = CreateNode(42, true, 100);
            var clone = (ReferenceNode)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(original.ReferenceTypeId));
            Assert.That(clone.IsInverse, Is.EqualTo(original.IsInverse));
            Assert.That(clone.TargetId, Is.EqualTo(original.TargetId));
        }

        [Test]
        public void MemberwiseCloneReturnsNewInstanceWithSameValues()
        {
            ReferenceNode original = CreateNode(42, false, 200);
            var clone = (ReferenceNode)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.ReferenceTypeId, Is.EqualTo(original.ReferenceTypeId));
            Assert.That(clone.IsInverse, Is.EqualTo(original.IsInverse));
            Assert.That(clone.TargetId, Is.EqualTo(original.TargetId));
        }

        [Test]
        public void IsEqualReturnsTrueForSameReference()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.IsEqual(node), Is.True);
        }

        [Test]
        public void IsEqualReturnsTrueForEqualValues()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(42, true, 100);
            Assert.That(a.IsEqual(b), Is.True);
        }

        [Test]
        public void IsEqualReturnsFalseForNonReferenceNode()
        {
            ReferenceNode node = CreateNode();
            var mock = new Mock<IEncodeable>();
            Assert.That(node.IsEqual(mock.Object), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseWhenReferenceTypeIdDiffers()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(99, true, 100);
            Assert.That(a.IsEqual(b), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseWhenIsInverseDiffers()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a.IsEqual(b), Is.False);
        }

        [Test]
        public void IsEqualReturnsFalseWhenTargetIdDiffers()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(42, true, 200);
            Assert.That(a.IsEqual(b), Is.False);
        }

        [Test]
        public void ToStringForInverseReferenceContainsInverseMarker()
        {
            ReferenceNode node = CreateNode(isInverse: true);
            string result = node.ToString();

            Assert.That(result, Is.Not.Null.And.Not.Empty);
            Assert.That(result, Does.Contain("<!"));
        }

        [Test]
        public void ToStringForForwardReferenceDoesNotContainInverseMarker()
        {
            ReferenceNode node = CreateNode(isInverse: false);
            string result = node.ToString();

            Assert.That(result, Is.Not.Null.And.Not.Empty);
            Assert.That(result, Does.StartWith("<"));
            Assert.That(result, Does.Not.Contain("<!"));
        }

        [Test]
        public void EqualsObjectWithEqualReferenceNodeReturnsTrue()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            object b = CreateNode(42, true, 100);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualsObjectWithNonReferenceNodeReturnsFalse()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.Equals("not a reference node"), Is.False);
        }

        [Test]
        public void EqualsReferenceNodeWithNullReturnsFalse()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.Equals((ReferenceNode)null), Is.False);
        }

        [Test]
        public void EqualsReferenceNodeWithEqualValuesReturnsTrue()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualsReferenceNodeWithDifferentReferenceTypeIdReturnsFalse()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(99, false, 100);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void EqualsReferenceNodeWithDifferentIsInverseReturnsFalse()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void EqualsReferenceNodeWithDifferentTargetIdReturnsFalse()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(42, false, 200);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void GetHashCodeReturnsSameValueOnMultipleCalls()
        {
            ReferenceNode node = CreateNode(42, true, 100);
            Assert.That(node.GetHashCode(), Is.Not.Zero);
        }

        [Test]
        public void GetHashCodeReturnsDifferentValuesForDifferentInstances()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(99, false, 200);
            // Different property values should produce different hash codes
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void EqualityOperatorReturnsTrueForEqualNodes()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            object b = CreateNode(42, true, 100);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualityOperatorReturnsTrueForBothNull()
        {
            ReferenceNode a = null;
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a, Is.EqualTo((object)null));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualityOperatorReturnsFalseForNullAndNonNull()
        {
            ReferenceNode a = null;
            object b = CreateNode();
            Assert.That(a == b, Is.False);
        }

        [Test]
        public void InequalityOperatorReturnsTrueForNullAndNonNull()
        {
            ReferenceNode a = null;
            object b = CreateNode();
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void InequalityOperatorReturnsFalseForBothNull()
        {
            ReferenceNode a = null;
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(a, Is.EqualTo((object)null));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void InequalityOperatorWithNonNullNodeEvaluatesNonNullBranch()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            object b = CreateNode(99, false, 200);
            // Exercises the non-null branch of the != operator
            bool result = a != b;
            Assert.That(result, Is.False);
        }

        [Test]
        public void CompareToObjectWithNullReturnsPositive()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.CompareTo((object)null), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToObjectWithSameReferenceReturnsZero()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.CompareTo((object)node), Is.Zero);
        }

        [Test]
        public void CompareToObjectWithNonReferenceNodeReturnsNegative()
        {
            ReferenceNode node = CreateNode();
            Assert.That(node.CompareTo("not a node"), Is.LessThan(0));
        }

        [Test]
        public void CompareToObjectWithEqualReferenceNodeReturnsZero()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            object b = CreateNode(42, false, 100);
            Assert.That(a.CompareTo(b), Is.Zero);
        }

        [Test]
        public void CompareToNodeBothNullReferenceTypeIdReturnsZero()
        {
            var a = new ReferenceNode(NodeId.Null, false, new ExpandedNodeId(100));
            var b = new ReferenceNode(NodeId.Null, false, new ExpandedNodeId(100));
            Assert.That(a.CompareTo(b), Is.Zero);
        }

        [Test]
        public void CompareToNodeNullRefTypeVsNonNullReturnsNegative()
        {
            var a = new ReferenceNode(NodeId.Null, false, new ExpandedNodeId(100));
            ReferenceNode b = CreateNode();
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void CompareToNodeByDifferentReferenceTypeIdReturnsNonZero()
        {
            ReferenceNode a = CreateNode(10, false, 100);
            ReferenceNode b = CreateNode(20, false, 100);
            Assert.That(a.CompareTo(b), Is.Not.Zero);
        }

        [Test]
        public void CompareToNodeSameTypeIdThisInverseOtherNotReturnsPositive()
        {
            ReferenceNode a = CreateNode(42, true, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a.CompareTo(b), Is.GreaterThan(0));
        }

        [Test]
        public void CompareToNodeSameTypeIdThisNotInverseOtherInverseReturnsNegative()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(42, true, 100);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void CompareToNodeBothNullTargetIdReturnsZero()
        {
            var a = new ReferenceNode(new NodeId(42), false, ExpandedNodeId.Null);
            var b = new ReferenceNode(new NodeId(42), false, ExpandedNodeId.Null);
            Assert.That(a.CompareTo(b), Is.Zero);
        }

        [Test]
        public void CompareToNodeNullTargetIdVsNonNullReturnsNegative()
        {
            var a = new ReferenceNode(new NodeId(42), false, ExpandedNodeId.Null);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void CompareToNodeByTargetIdReturnsComparisonResult()
        {
            ReferenceNode a = CreateNode(42, false, 10);
            ReferenceNode b = CreateNode(42, false, 20);
            Assert.That(a.CompareTo(b), Is.LessThan(0));
        }

        [Test]
        public void LessThanOperatorReturnsTrueForSmallerNode()
        {
            ReferenceNode a = CreateNode(10, false, 100);
            ReferenceNode b = CreateNode(20, false, 100);
            Assert.That(a < b, Is.True);
        }

        [Test]
        public void LessThanOperatorReturnsTrueForNullLeftNonNullRight()
        {
            ReferenceNode a = null;
            ReferenceNode b = CreateNode();
            Assert.That(a < b, Is.True);
        }

        [Test]
        public void LessThanOperatorReturnsFalseForBothNull()
        {
            ReferenceNode a = null;
            ReferenceNode b = null;
            Assert.That(a < b, Is.False);
        }

        [Test]
        public void LessThanOrEqualOperatorReturnsTrueForEqualNodes()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a <= b, Is.True);
        }

        [Test]
        public void LessThanOrEqualOperatorReturnsTrueForNullLeft()
        {
            ReferenceNode a = null;
            ReferenceNode b = CreateNode();
            Assert.That(a <= b, Is.True);
        }

        [Test]
        public void GreaterThanOperatorReturnsTrueForLargerNode()
        {
            ReferenceNode a = CreateNode(20, false, 100);
            ReferenceNode b = CreateNode(10, false, 100);
            Assert.That(a, Is.GreaterThan(b));
        }

        [Test]
        public void GreaterThanOperatorReturnsFalseForNullLeft()
        {
            ReferenceNode a = null;
            ReferenceNode b = CreateNode();
            Assert.That(a > b, Is.False);
        }

        [Test]
        public void GreaterThanOrEqualOperatorReturnsTrueForEqualNodes()
        {
            ReferenceNode a = CreateNode(42, false, 100);
            ReferenceNode b = CreateNode(42, false, 100);
            Assert.That(a >= b, Is.True);
        }

        [Test]
        public void GreaterThanOrEqualOperatorReturnsTrueForBothNull()
        {
            ReferenceNode a = null;
            ReferenceNode b = null;
            Assert.That(a >= b, Is.True);
        }

        [Test]
        public void GreaterThanOrEqualOperatorReturnsFalseForNullLeftNonNullRight()
        {
            ReferenceNode a = null;
            ReferenceNode b = CreateNode();
            Assert.That(a >= b, Is.False);
        }

        [Test]
        public void EncodeWritesAllProperties()
        {
            var refTypeId = new NodeId(42);
            var targetId = new ExpandedNodeId(100);
            var node = new ReferenceNode(refTypeId, true, targetId);

            var encoder = new Mock<IEncoder>();

            node.Encode(encoder.Object);

            encoder.Verify(e => e.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            encoder.Verify(e => e.WriteNodeId("ReferenceTypeId", refTypeId), Times.Once);
            encoder.Verify(e => e.WriteBoolean("IsInverse", true), Times.Once);
            encoder.Verify(e => e.WriteExpandedNodeId("TargetId", targetId), Times.Once);
            encoder.Verify(e => e.PopNamespace(), Times.Once);
        }

        [Test]
        public void DecodeReadsAllProperties()
        {
            var expectedRefTypeId = new NodeId(42);
            var expectedTargetId = new ExpandedNodeId(100);

            var decoder = new Mock<IDecoder>();
            decoder.Setup(d => d.ReadNodeId("ReferenceTypeId")).Returns(expectedRefTypeId);
            decoder.Setup(d => d.ReadBoolean("IsInverse")).Returns(true);
            decoder.Setup(d => d.ReadExpandedNodeId("TargetId")).Returns(expectedTargetId);

            var node = new ReferenceNode();
            node.Decode(decoder.Object);

            Assert.That(node.ReferenceTypeId, Is.EqualTo(expectedRefTypeId));
            Assert.That(node.IsInverse, Is.True);
            Assert.That(node.TargetId, Is.EqualTo(expectedTargetId));
            decoder.Verify(d => d.PushNamespace(Namespaces.OpcUaXsd), Times.Once);
            decoder.Verify(d => d.PopNamespace(), Times.Once);
        }
    }
}
