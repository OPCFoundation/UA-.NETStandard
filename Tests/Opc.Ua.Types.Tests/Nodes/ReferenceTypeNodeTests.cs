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

using NUnit.Framework;

#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.Nodes
{
    [TestFixture]
    [Category("Node")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ReferenceTypeNodeTests
    {
        /// <summary>
        /// Creates a fully populated ReferenceTypeNode for testing.
        /// </summary>
        private static ReferenceTypeNode CreatePopulatedNode()
        {
            return new ReferenceTypeNode
            {
                NodeId = new NodeId(42u),
                BrowseName = new QualifiedName("TestRefType"),
                DisplayName = new LocalizedText("Test Reference Type"),
                Description = new LocalizedText("A test reference type node"),
                IsAbstract = false,
                Symmetric = false,
                InverseName = new LocalizedText("InverseOf")
            };
        }

        [Test]
        public void DefaultConstructorSetsExpectedDefaults()
        {
            var node = new ReferenceTypeNode();

            Assert.That(node.IsAbstract, Is.True);
            Assert.That(node.Symmetric, Is.True);
            Assert.That(node.InverseName, Is.Default);
        }

        [Test]
        public void CopyConstructorFromReferenceTypeCopiesAllProperties()
        {
            var source = new ReferenceTypeNode
            {
                NodeId = new NodeId(99u),
                BrowseName = new QualifiedName("SourceRef"),
                DisplayName = new LocalizedText("Source Reference"),
                IsAbstract = false,
                Symmetric = false,
                InverseName = new LocalizedText("Inverse")
            };

            var copy = new ReferenceTypeNode(source);

            Assert.That(copy.NodeClass, Is.EqualTo(NodeClass.ReferenceType));
            Assert.That(copy.IsAbstract, Is.EqualTo(source.IsAbstract));
            Assert.That(copy.Symmetric, Is.EqualTo(source.Symmetric));
            Assert.That(copy.InverseName, Is.EqualTo(source.InverseName));
        }

        [Test]
        public void CopyConstructorFromNonReferenceTypeNodeSetsNodeClass()
        {
            var source = new ObjectTypeNode
            {
                NodeId = new NodeId(77u),
                BrowseName = new QualifiedName("ObjType"),
                DisplayName = new LocalizedText("Object Type")
            };

            var node = new ReferenceTypeNode(source);

            Assert.That(node.NodeClass, Is.EqualTo(NodeClass.ReferenceType));
            Assert.That(node.IsAbstract, Is.False);
            Assert.That(node.Symmetric, Is.False);
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.TypeId, Is.EqualTo(DataTypeIds.ReferenceTypeNode));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.BinaryEncodingId, Is.EqualTo(ObjectIds.ReferenceTypeNode_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.XmlEncodingId, Is.EqualTo(ObjectIds.ReferenceTypeNode_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            ReferenceTypeNode original = CreatePopulatedNode();

            var context = new ServiceMessageContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ReferenceTypeNode();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaults()
        {
            var original = new ReferenceTypeNode();

            var context = new ServiceMessageContext();

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ReferenceTypeNode();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            ReferenceTypeNode node = CreatePopulatedNode();
            Assert.That(node.IsEqual(node), Is.True);
        }

        [Test]
        public void IsEqualWithIdenticalNodeReturnsTrue()
        {
            ReferenceTypeNode node1 = CreatePopulatedNode();
            var node2 = (ReferenceTypeNode)node1.Clone();

            Assert.That(node1.IsEqual(node2), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWithWrongTypeReturnsFalse()
        {
            var node = new ReferenceTypeNode();
            var other = new ObjectTypeNode();
            Assert.That(node.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentIsAbstractReturnsFalse()
        {
            var node1 = new ReferenceTypeNode { IsAbstract = true };
            var node2 = new ReferenceTypeNode { IsAbstract = false };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentSymmetricReturnsFalse()
        {
            var node1 = new ReferenceTypeNode { Symmetric = true };
            var node2 = new ReferenceTypeNode { Symmetric = false };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentInverseNameReturnsFalse()
        {
            var node1 = new ReferenceTypeNode { InverseName = new LocalizedText("A") };
            var node2 = new ReferenceTypeNode { InverseName = new LocalizedText("B") };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentBaseFieldReturnsFalse()
        {
            var node1 = new ReferenceTypeNode { NodeId = new NodeId(1u) };
            var node2 = new ReferenceTypeNode { NodeId = new NodeId(2u) };

            Assert.That(node1.IsEqual(node2), Is.False);
        }

        [Test]
        public void EqualsObjectWithSameReferenceReturnsTrue()
        {
            ReferenceTypeNode node = CreatePopulatedNode();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node.Equals((object)node), Is.True);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void EqualsObjectWithDifferentReferenceReturnsFalse()
        {
            ReferenceTypeNode node1 = CreatePopulatedNode();
            var node2 = (ReferenceTypeNode)node1.Clone();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node1.Equals((object)node2), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
#pragma warning disable CA1508
        public void EqualsObjectWithNullReturnsFalse()
        {
            var node = new ReferenceTypeNode();
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(node.Equals((object)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }
#pragma warning restore CA1508

        [Test]
        public void GetHashCodeIsDeterministic()
        {
            ReferenceTypeNode node = CreatePopulatedNode();
            int hash = node.GetHashCode();

            Assert.That(node.GetHashCode(), Is.EqualTo(hash));
        }

        [Test]
        public void CloneCreatesEqualCopy()
        {
            ReferenceTypeNode original = CreatePopulatedNode();

            var clone = (ReferenceTypeNode)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
            Assert.That(clone.IsAbstract, Is.EqualTo(original.IsAbstract));
            Assert.That(clone.Symmetric, Is.EqualTo(original.Symmetric));
            Assert.That(clone.InverseName, Is.EqualTo(original.InverseName));
        }

        [Test]
        public void CloneIsIndependentOfOriginal()
        {
            var original = new ReferenceTypeNode
            {
                IsAbstract = false,
                Symmetric = false,
                InverseName = new LocalizedText("Original")
            };

            var clone = (ReferenceTypeNode)original.Clone();

            original.IsAbstract = true;
            original.Symmetric = true;
            original.InverseName = new LocalizedText("Changed");

            Assert.That(clone.IsAbstract, Is.False);
            Assert.That(clone.Symmetric, Is.False);
            Assert.That(clone.InverseName, Is.EqualTo(new LocalizedText("Original")));
        }

        [Test]
        public void MemberwiseCloneCreatesEqualCopy()
        {
            ReferenceTypeNode original = CreatePopulatedNode();

            var clone = (ReferenceTypeNode)original.MemberwiseClone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void SupportsAttributeIsAbstractReturnsTrue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.IsAbstract), Is.True);
        }

        [Test]
        public void SupportsAttributeSymmetricReturnsTrue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.Symmetric), Is.True);
        }

        [Test]
        public void SupportsAttributeInverseNameWithValueReturnsTrue()
        {
            var node = new ReferenceTypeNode { InverseName = new LocalizedText("Inv") };
            Assert.That(node.SupportsAttribute(Attributes.InverseName), Is.True);
        }

        [Test]
        public void SupportsAttributeInverseNameWithNullReturnsTrue()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.InverseName), Is.True);
        }

        [Test]
        public void SupportsAttributeBaseAttributeDelegatesToBase()
        {
            var node = new ReferenceTypeNode();
            Assert.That(node.SupportsAttribute(Attributes.NodeId), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.BrowseName), Is.True);
            Assert.That(node.SupportsAttribute(Attributes.DisplayName), Is.True);
        }
    }
}
