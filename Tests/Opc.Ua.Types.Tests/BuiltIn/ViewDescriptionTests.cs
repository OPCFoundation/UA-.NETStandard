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
    /// Tests for <see cref="ViewDescription"/>.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ViewDescriptionTests
    {
        private static ViewDescription CreatePopulated()
        {
            return new ViewDescription
            {
                ViewId = new NodeId(42),
                Timestamp = new DateTimeUtc(2024, 6, 15, 10, 30, 0),
                ViewVersion = 7
            };
        }

        [Test]
        public void DefaultConstructorSetsViewIdDefault()
        {
            var vd = new ViewDescription();

            Assert.That(vd.ViewId, Is.Default);
        }

        [Test]
        public void DefaultConstructorSetsTimestampMinValue()
        {
            var vd = new ViewDescription();

            Assert.That(vd.Timestamp, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public void DefaultConstructorSetsViewVersionZero()
        {
            var vd = new ViewDescription();

            Assert.That(vd.ViewVersion, Is.Zero);
        }

        [Test]
        public void DefaultConstructorSetsHandleNull()
        {
            var vd = new ViewDescription();

            Assert.That(vd.Handle, Is.Null);
        }

        [Test]
        public void TypeIdReturnsExpectedValue()
        {
            var vd = new ViewDescription();

            Assert.That(vd.TypeId, Is.EqualTo(DataTypeIds.ViewDescription));
        }

        [Test]
        public void BinaryEncodingIdReturnsExpectedValue()
        {
            var vd = new ViewDescription();

            Assert.That(vd.BinaryEncodingId, Is.EqualTo(ObjectIds.ViewDescription_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsExpectedValue()
        {
            var vd = new ViewDescription();

            Assert.That(vd.XmlEncodingId, Is.EqualTo(ObjectIds.ViewDescription_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripPreservesAllProperties()
        {
            ViewDescription original = CreatePopulated();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ViewDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultValues()
        {
            var original = new ViewDescription();
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ViewDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(original.IsEqual(decoded), Is.True);
        }

        [Test]
        public void IsEqualWithSameReferenceReturnsTrue()
        {
            ViewDescription vd = CreatePopulated();

            Assert.That(vd.IsEqual(vd), Is.True);
        }

        [Test]
        public void IsEqualWithEqualValuesReturnsTrue()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1.IsEqual(vd2), Is.True);
        }

        [Test]
        public void IsEqualWithNullReturnsFalse()
        {
            ViewDescription vd = CreatePopulated();

            Assert.That(vd.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWithWrongTypeReturnsFalse()
        {
            ViewDescription vd = CreatePopulated();
            var other = new Argument();

            Assert.That(vd.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentViewIdReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewId = new NodeId(999);

            Assert.That(vd1.IsEqual(vd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentTimestampReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.Timestamp = new DateTimeUtc(2025, 1, 1, 0, 0, 0);

            Assert.That(vd1.IsEqual(vd2), Is.False);
        }

        [Test]
        public void IsEqualWithDifferentViewVersionReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewVersion = 99;

            Assert.That(vd1.IsEqual(vd2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            ViewDescription vd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(vd.Equals((object)null), Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsObjectWithNonViewDescriptionReturnsFalse()
        {
            ViewDescription vd = CreatePopulated();

            Assert.That(vd.Equals("not a ViewDescription"), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualValueReturnsTrue()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1.Equals((object)vd2), Is.True);
        }

        [Test]
        public void EqualsViewDescriptionWithEqualValueReturnsTrue()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1.Equals(vd2), Is.True);
        }

        [Test]
        public void EqualsViewDescriptionWithNullReturnsFalse()
        {
            ViewDescription vd = CreatePopulated();

#pragma warning disable CA1508 // Avoid dead conditional code
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(vd.Equals((ViewDescription)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void EqualsViewDescriptionWithDifferentReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewVersion = 99;

            Assert.That(vd1.Equals(vd2), Is.False);
        }

        [Test]
        public void GetHashCodeEqualForEqualObjects()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1.GetHashCode(), Is.EqualTo(vd2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeDiffersForDifferentObjects()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewId = new NodeId(999);

            Assert.That(vd1.GetHashCode(), Is.Not.EqualTo(vd2.GetHashCode()));
        }

        [Test]
        public void OperatorEqualWithEqualValuesReturnsTrue()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1 == vd2, Is.True);
        }

        [Test]
        public void OperatorEqualWithDifferentValuesReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewVersion = 99;

            Assert.That(vd1 == vd2, Is.False);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            ViewDescription vd1 = null;
            ViewDescription vd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(vd1 == vd2, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(vd1 == vd2, Is.False);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd2.ViewId = new NodeId(999);

            Assert.That(vd1 != vd2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();

            Assert.That(vd1 != vd2, Is.False);
        }

        [Test]
        public void CloneProducesEqualCopy()
        {
            ViewDescription original = CreatePopulated();

            var clone = (ViewDescription)original.Clone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            ViewDescription original = CreatePopulated();

            var clone = (ViewDescription)original.Clone();
            clone.ViewId = new NodeId(777);

            Assert.That(original.ViewId, Is.EqualTo(new NodeId(42)));
        }

        [Test]
        public void MemberwiseCloneProducesEqualCopy()
        {
            ViewDescription original = CreatePopulated();

            var clone = (ViewDescription)original.MemberwiseClone();

            Assert.That(original.IsEqual(clone), Is.True);
        }

        [Test]
        public void MemberwiseCloneProducesIndependentCopy()
        {
            ViewDescription original = CreatePopulated();

            var clone = (ViewDescription)original.MemberwiseClone();
            clone.ViewVersion = 999;

            Assert.That(original.ViewVersion, Is.EqualTo(7u));
        }

        [Test]
        public void HandleNotIncludedInEquality()
        {
            ViewDescription vd1 = CreatePopulated();
            ViewDescription vd2 = CreatePopulated();
            vd1.Handle = "handle1";
            vd2.Handle = "handle2";

            Assert.That(vd1.IsEqual(vd2), Is.True);
        }

        [Test]
        public void HandleNotPreservedByEncodeDecode()
        {
            ViewDescription original = CreatePopulated();
            original.Handle = "myHandle";
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());

            byte[] buffer;
            using (var encoder = new BinaryEncoder(context))
            {
                original.Encode(encoder);
                buffer = encoder.CloseAndReturnBuffer();
            }

            var decoded = new ViewDescription();
            using (var decoder = new BinaryDecoder(buffer, context))
            {
                decoded.Decode(decoder);
            }

            Assert.That(decoded.Handle, Is.Null);
        }

        [Test]
        public void IsDefaultWithNullReturnsTrue()
        {
            Assert.That(ViewDescription.IsDefault(null), Is.True);
        }

        [Test]
        public void IsDefaultWithDefaultInstanceReturnsTrue()
        {
            var vd = new ViewDescription();

            Assert.That(ViewDescription.IsDefault(vd), Is.True);
        }

        [Test]
        public void IsDefaultWithViewIdSetReturnsFalse()
        {
            var vd = new ViewDescription
            {
                ViewId = new NodeId(1)
            };

            Assert.That(ViewDescription.IsDefault(vd), Is.False);
        }

        [Test]
        public void IsDefaultWithTimestampSetReturnsFalse()
        {
            var vd = new ViewDescription
            {
                Timestamp = new DateTimeUtc(2024, 1, 1, 0, 0, 0)
            };

            Assert.That(ViewDescription.IsDefault(vd), Is.False);
        }

        [Test]
        public void IsDefaultWithViewVersionSetReturnsFalse()
        {
            var vd = new ViewDescription
            {
                ViewVersion = 1
            };

            Assert.That(ViewDescription.IsDefault(vd), Is.False);
        }
    }
}
