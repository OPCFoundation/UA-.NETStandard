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

#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages
#pragma warning disable CA1508 // Avoid dead conditional code

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="StructureField"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureFieldTests
    {
        [Test]
        public void DefaultConstructorSetsDefaultValues()
        {
            var field = new StructureField();

            Assert.That(field.Name, Is.Null);
            Assert.That(field.Description, Is.Default);
            Assert.That(field.DataType, Is.Default);
            Assert.That(field.ValueRank, Is.Zero);
            Assert.That(field.ArrayDimensions, Is.Default);
            Assert.That(field.MaxStringLength, Is.Zero);
            Assert.That(field.IsOptional, Is.False);
        }

        [Test]
        public void TypeIdReturnsCorrectValue()
        {
            var field = new StructureField();

            Assert.That(field.TypeId, Is.EqualTo(DataTypeIds.StructureField));
        }

        [Test]
        public void BinaryEncodingIdReturnsCorrectValue()
        {
            var field = new StructureField();

            Assert.That(
                field.BinaryEncodingId,
                Is.EqualTo(ObjectIds.StructureField_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsCorrectValue()
        {
            var field = new StructureField();

            Assert.That(
                field.XmlEncodingId,
                Is.EqualTo(ObjectIds.StructureField_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripWithAllFieldsSet()
        {
            StructureField original = CreatePopulatedField();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new StructureField();
            decoded.Decode(decoder);

            Assert.That(decoded.Name, Is.EqualTo(original.Name));
            Assert.That(decoded.Description, Is.EqualTo(original.Description));
            Assert.That(decoded.DataType, Is.EqualTo(original.DataType));
            Assert.That(decoded.ValueRank, Is.EqualTo(original.ValueRank));
            Assert.That(decoded.ArrayDimensions, Is.EqualTo(original.ArrayDimensions));
            Assert.That(decoded.MaxStringLength, Is.EqualTo(original.MaxStringLength));
            Assert.That(decoded.IsOptional, Is.EqualTo(original.IsOptional));
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultFields()
        {
            var original = new StructureField();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new StructureField();
            decoded.Decode(decoder);

            Assert.That(decoded.Name, Is.EqualTo(original.Name));
            Assert.That(decoded.Description, Is.EqualTo(original.Description));
            Assert.That(decoded.DataType, Is.EqualTo(original.DataType));
            Assert.That(decoded.ValueRank, Is.EqualTo(original.ValueRank));
            Assert.That(decoded.ArrayDimensions, Is.EqualTo(original.ArrayDimensions));
            Assert.That(decoded.MaxStringLength, Is.EqualTo(original.MaxStringLength));
            Assert.That(decoded.IsOptional, Is.EqualTo(original.IsOptional));
        }

        [Test]
        public void IsEqualSameReferenceReturnsTrue()
        {
            StructureField field = CreatePopulatedField();

            Assert.That(field.IsEqual(field), Is.True);
        }

        [Test]
        public void IsEqualNullReturnsFalse()
        {
            StructureField field = CreatePopulatedField();

            Assert.That(field.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWrongTypeReturnsFalse()
        {
            StructureField field = CreatePopulatedField();
            var other = new Argument();

            Assert.That(field.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualEqualObjectsReturnsTrue()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();

            Assert.That(field1.IsEqual(field2), Is.True);
        }

        [Test]
        public void IsEqualDifferentNameReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.Name = "DifferentName";

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentDescriptionReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.Description = new LocalizedText("de-DE", "Andere Beschreibung");

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentDataTypeReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.DataType = DataTypeIds.Int32;

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentValueRankReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.ValueRank = ValueRanks.Scalar;

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentArrayDimensionsReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.ArrayDimensions = new uint[] { 20 }.ToArrayOf();

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentMaxStringLengthReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.MaxStringLength = 512;

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentIsOptionalReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.IsOptional = false;

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualObjectReturnsTrue()
        {
            StructureField field1 = CreatePopulatedField();
            object field2 = CreatePopulatedField();

            Assert.That(field1.Equals(field2), Is.True);
        }

        [Test]
        public void EqualsObjectWithNonEqualObjectReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.Name = "Other";

            Assert.That(field1.Equals((object)field2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            StructureField field = CreatePopulatedField();

            Assert.That(field.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectWithNonStructureFieldReturnsFalse()
        {
            StructureField field = CreatePopulatedField();

            Assert.That(field.Equals("not a StructureField"), Is.False);
        }

        [Test]
        public void EqualsStructureFieldWithEqualReturnsTrue()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();

            Assert.That(field1.Equals(field2), Is.True);
        }

        [Test]
        public void EqualsStructureFieldWithNonEqualReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.MaxStringLength = 999;

            Assert.That(field1.Equals(field2), Is.False);
        }

        [Test]
        public void EqualsStructureFieldWithNullReturnsFalse()
        {
            StructureField field = CreatePopulatedField();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(field.Equals((StructureField)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetHashCodeEqualObjectsReturnSameHash()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();

            Assert.That(field1.GetHashCode(), Is.EqualTo(field2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeDefaultObjectsReturnSameHash()
        {
            var field1 = new StructureField();
            var field2 = new StructureField();

            Assert.That(field1.GetHashCode(), Is.EqualTo(field2.GetHashCode()));
        }

        [Test]
        public void OperatorEqualWithEqualObjectsReturnsTrue()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();

            Assert.That(field1 == field2, Is.True);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            StructureField field1 = null;
            StructureField field2 = null;

            Assert.That(field1 == field2, Is.True);
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = null;

            Assert.That(field1 == field2, Is.False);
            Assert.That(field2 == field1, Is.False);
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();
            field2.Name = "Different";

            Assert.That(field1 != field2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            StructureField field1 = CreatePopulatedField();
            StructureField field2 = CreatePopulatedField();

            Assert.That(field1 != field2, Is.False);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            StructureField original = CreatePopulatedField();

            var clone = (StructureField)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);

            clone.Name = "Modified";
            Assert.That(original.Name, Is.EqualTo("TestField"));
        }

        [Test]
        public void MemberwiseCloneDeepCopiesAllFields()
        {
            StructureField original = CreatePopulatedField();

            var clone = (StructureField)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Description, Is.EqualTo(original.Description));
            Assert.That(clone.DataType, Is.EqualTo(original.DataType));
            Assert.That(clone.ValueRank, Is.EqualTo(original.ValueRank));
            Assert.That(clone.ArrayDimensions, Is.EqualTo(original.ArrayDimensions));
            Assert.That(clone.MaxStringLength, Is.EqualTo(original.MaxStringLength));
            Assert.That(clone.IsOptional, Is.EqualTo(original.IsOptional));

            // Verify independence: modifying clone does not affect original
            clone.Name = "ClonedField";
            clone.Description = new LocalizedText("fr-FR", "Clone");
            clone.MaxStringLength = 999;
            clone.IsOptional = false;

            Assert.That(original.Name, Is.EqualTo("TestField"));
            Assert.That(original.Description, Is.EqualTo(new LocalizedText("en-US", "A test field")));
            Assert.That(original.MaxStringLength, Is.EqualTo(256u));
            Assert.That(original.IsOptional, Is.True);
        }

        [Test]
        public void CloneWithDefaultValuesProducesEqualCopy()
        {
            var original = new StructureField();

            var clone = (StructureField)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);
        }

        private static StructureField CreatePopulatedField()
        {
            return new StructureField
            {
                Name = "TestField",
                Description = new LocalizedText("en-US", "A test field"),
                DataType = DataTypeIds.Double,
                ValueRank = ValueRanks.OneDimension,
                ArrayDimensions = new uint[] { 10 }.ToArrayOf(),
                MaxStringLength = 256,
                IsOptional = true
            };
        }
    }
}
