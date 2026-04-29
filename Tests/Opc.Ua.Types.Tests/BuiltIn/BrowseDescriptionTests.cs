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
using Opc.Ua.Tests;

#pragma warning disable NUnit2010

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for <see cref="BrowseDescription"/>.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BrowseDescriptionTests
    {
        [Test]
        public void DefaultConstructorSetsBrowseDirectionForward()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
        }

        [Test]
        public void DefaultConstructorSetsIncludeSubtypesTrue()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.IncludeSubtypes, Is.True);
        }

        [Test]
        public void DefaultConstructorSetsDefaultValues()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.NodeId, Is.Default);
            Assert.That(bd.ReferenceTypeId, Is.Default);
            Assert.That(bd.NodeClassMask, Is.Zero);
            Assert.That(bd.ResultMask, Is.Zero);
            Assert.That(bd.Handle, Is.Null);
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.TypeId, Is.EqualTo(DataTypeIds.BrowseDescription));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.BinaryEncodingId, Is.EqualTo(ObjectIds.BrowseDescription_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var bd = new BrowseDescription();

            Assert.That(bd.XmlEncodingId, Is.EqualTo(ObjectIds.BrowseDescription_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            BrowseDescription original = CreatePopulated();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new BrowseDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultValues()
        {
            var original = new BrowseDescription();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new BrowseDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            BrowseDescription bd = CreatePopulated();

            Assert.That(bd.IsEqual(bd), Is.True);
        }

        [Test]
        public void IsEqualWithEqualValuesReturnsTrue()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1.IsEqual(bd2), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            BrowseDescription bd = CreatePopulated();

            Assert.That(bd.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWithWrongTypeReturnsFalse()
        {
            BrowseDescription bd = CreatePopulated();
            var other = new Argument();

            Assert.That(bd.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentNodeIdReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.NodeId = new NodeId(999);

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentBrowseDirectionReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.BrowseDirection = BrowseDirection.Both;

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentReferenceTypeIdReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.ReferenceTypeId = new NodeId(888);

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentIncludeSubtypesReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.IncludeSubtypes = !bd1.IncludeSubtypes;

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentNodeClassMaskReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.NodeClassMask = 0x01;

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentResultMaskReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.ResultMask = 0x01;

            Assert.That(bd1.IsEqual(bd2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            BrowseDescription bd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(bd.Equals((object)null), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsObjectWithNonBrowseDescriptionReturnsFalse()
        {
            BrowseDescription bd = CreatePopulated();

            Assert.That(bd.Equals("not a BrowseDescription"), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualValueReturnsTrue()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1.Equals((object)bd2), Is.True);
        }

        [Test]
        public void EqualsBrowseDescriptionWithEqualValueReturnsTrue()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1.Equals(bd2), Is.True);
        }

        [Test]
        public void EqualsBrowseDescriptionWithNullReturnsFalse()
        {
            BrowseDescription bd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(bd.Equals((BrowseDescription)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsBrowseDescriptionWithDifferentReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.NodeClassMask = 0x01;

            Assert.That(bd1.Equals(bd2), Is.False);
        }

        [Test]
        public void GetHashCodeEqualForEqualObjects()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1.GetHashCode(), Is.EqualTo(bd2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeDiffersForDifferentObjects()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.NodeId = new NodeId(999);

            Assert.That(bd1.GetHashCode(), Is.Not.EqualTo(bd2.GetHashCode()));
        }

        [Test]
        public void OperatorEqualWithEqualValuesReturnsTrue()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1 == bd2, Is.True);
        }

        [Test]
        public void OperatorEqualWithDifferentValuesReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.BrowseDirection = BrowseDirection.Forward;

            Assert.That(bd1 == bd2, Is.False);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            BrowseDescription bd1 = null;
            BrowseDescription bd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(bd1 == bd2, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(bd1 == bd2, Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd2.ResultMask = 0x01;

            Assert.That(bd1 != bd2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();

            Assert.That(bd1 != bd2, Is.False);
        }

        [Test]
        public void CloneProducesEqualCopy()
        {
            BrowseDescription original = CreatePopulated();

            var clone = (BrowseDescription)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            BrowseDescription original = CreatePopulated();

            var clone = (BrowseDescription)original.Clone();
            clone.NodeId = new NodeId(777);

            Assert.That(original.NodeId, Is.EqualTo(new NodeId(42)));
        }

        [Test]
        public void MemberwiseCloneProducesEqualCopy()
        {
            BrowseDescription original = CreatePopulated();

            var clone = (BrowseDescription)original.MemberwiseClone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void MemberwiseCloneProducesIndependentCopy()
        {
            BrowseDescription original = CreatePopulated();

            var clone = (BrowseDescription)original.MemberwiseClone();
            clone.ReferenceTypeId = new NodeId(777);

            Assert.That(original.ReferenceTypeId, Is.EqualTo(new NodeId(33)));
        }

        [Test]
        public void HandleNotIncludedInEquality()
        {
            BrowseDescription bd1 = CreatePopulated();
            BrowseDescription bd2 = CreatePopulated();
            bd1.Handle = "handle1";
            bd2.Handle = "handle2";

            Assert.That(bd1.IsEqual(bd2), Is.True);
        }

        [Test]
        public void HandleNotPreservedByEncodeDecode()
        {
            BrowseDescription original = CreatePopulated();
            original.Handle = "myHandle";
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new BrowseDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(decoded.Handle, Is.Null);
        }

        private static BrowseDescription CreatePopulated()
        {
            return new BrowseDescription
            {
                NodeId = new NodeId(42),
                BrowseDirection = BrowseDirection.Inverse,
                ReferenceTypeId = new NodeId(33),
                IncludeSubtypes = false,
                NodeClassMask = 0xFF,
                ResultMask = 0x3F
            };
        }
    }
}
