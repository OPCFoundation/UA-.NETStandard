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

using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="EnumField"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EnumFieldTests
    {
        [Test]
        public void DefaultConstructorSetsDefaultValues()
        {
            var field = new EnumField();

            Assert.That(field.Name, Is.Null);
            Assert.That(field.Value, Is.Zero);
            Assert.That(field.DisplayName, Is.Default);
            Assert.That(field.Description, Is.Default);
        }

        [Test]
        public void TypeIdReturnsCorrectValue()
        {
            var field = new EnumField();

            Assert.That(field.TypeId, Is.EqualTo(DataTypeIds.EnumField));
        }

        [Test]
        public void BinaryEncodingIdReturnsCorrectValue()
        {
            var field = new EnumField();

            Assert.That(
                field.BinaryEncodingId,
                Is.EqualTo(ObjectIds.EnumField_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsCorrectValue()
        {
            var field = new EnumField();

            Assert.That(
                field.XmlEncodingId,
                Is.EqualTo(ObjectIds.EnumField_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripWithAllFieldsSet()
        {
            EnumField original = CreatePopulatedField();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new EnumField();
            decoded.Decode(decoder);

            Assert.That(decoded.Name, Is.EqualTo(original.Name));
            Assert.That(decoded.Value, Is.EqualTo(original.Value));
            Assert.That(decoded.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(decoded.Description, Is.EqualTo(original.Description));
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultFields()
        {
            var original = new EnumField();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new EnumField();
            decoded.Decode(decoder);

            Assert.That(decoded.Name, Is.EqualTo(original.Name));
            Assert.That(decoded.Value, Is.EqualTo(original.Value));
            Assert.That(decoded.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(decoded.Description, Is.EqualTo(original.Description));
        }

        [Test]
        public void IsEqualSameReferenceReturnsTrue()
        {
            EnumField field = CreatePopulatedField();

            Assert.That(field.IsEqual(field), Is.True);
        }

        [Test]
        public void IsEqualNullReturnsFalse()
        {
            EnumField field = CreatePopulatedField();

            Assert.That(field.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWrongTypeReturnsFalse()
        {
            EnumField field = CreatePopulatedField();
            var other = new Argument();

            Assert.That(field.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualEqualObjectsReturnsTrue()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();

            Assert.That(field1.IsEqual(field2), Is.True);
        }

        [Test]
        public void IsEqualDifferentNameReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.Name = "DifferentName";

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentValueReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.Value = 99;

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentDisplayNameReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.DisplayName = new LocalizedText("de-DE", "Testfeld");

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void IsEqualDifferentDescriptionReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.Description = new LocalizedText("de-DE", "Ein Testfeld");

            Assert.That(field1.IsEqual(field2), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualObjectReturnsTrue()
        {
            EnumField field1 = CreatePopulatedField();
            object field2 = CreatePopulatedField();

            Assert.That(field1.Equals(field2), Is.True);
        }

        [Test]
        public void EqualsObjectWithNonEqualObjectReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.Name = "Other";

            Assert.That(field1.Equals((object)field2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            EnumField field = CreatePopulatedField();

            Assert.That(field.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectWithNonEnumFieldReturnsFalse()
        {
            EnumField field = CreatePopulatedField();

            Assert.That(field.Equals("not an EnumField"), Is.False);
        }

        [Test]
        public void EqualsEnumFieldWithEqualReturnsTrue()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();

            Assert.That(field1.Equals(field2), Is.True);
        }

        [Test]
        public void EqualsEnumFieldWithNullReturnsFalse()
        {
            EnumField field = CreatePopulatedField();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(field.Equals((EnumField)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetHashCodeEqualObjectsReturnSameHash()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();

            Assert.That(field1.GetHashCode(), Is.EqualTo(field2.GetHashCode()));
        }

        [Test]
        public void GetHashCodeDefaultObjectsReturnSameHash()
        {
            var field1 = new EnumField();
            var field2 = new EnumField();

            Assert.That(field1.GetHashCode(), Is.EqualTo(field2.GetHashCode()));
        }

        [Test]
        public void OperatorEqualWithEqualObjectsReturnsTrue()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();

            Assert.That(field1 == field2, Is.True);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            EnumField field1 = null;
            EnumField field2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(field1 == field2, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = null;

            Assert.That(field1 == field2, Is.False);
            Assert.That(field2 == field1, Is.False);
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();
            field2.Name = "Different";

            Assert.That(field1 != field2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            EnumField field1 = CreatePopulatedField();
            EnumField field2 = CreatePopulatedField();

            Assert.That(field1 != field2, Is.False);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            EnumField original = CreatePopulatedField();

            var clone = (EnumField)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);

            clone.Name = "Modified";
            Assert.That(original.Name, Is.EqualTo("TestField"));
        }

        [Test]
        public void MemberwiseCloneDeepCopiesAllFields()
        {
            EnumField original = CreatePopulatedField();

            var clone = (EnumField)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Name, Is.EqualTo(original.Name));
            Assert.That(clone.Value, Is.EqualTo(original.Value));
            Assert.That(clone.DisplayName, Is.EqualTo(original.DisplayName));
            Assert.That(clone.Description, Is.EqualTo(original.Description));

            // Verify independence
            clone.Name = "ClonedField";
            clone.Value = 999;
            clone.DisplayName = new LocalizedText("fr-FR", "Clone");

            Assert.That(original.Name, Is.EqualTo("TestField"));
            Assert.That(original.Value, Is.EqualTo(42L));
            Assert.That(
                original.DisplayName,
                Is.EqualTo(new LocalizedText("en-US", "Test Field")));
        }

        [Test]
        public void CloneWithDefaultValuesProducesEqualCopy()
        {
            var original = new EnumField();

            var clone = (EnumField)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);
        }

        private static EnumField CreatePopulatedField()
        {
            return new EnumField
            {
                Name = "TestField",
                Value = 42,
                DisplayName = new LocalizedText("en-US", "Test Field"),
                Description = new LocalizedText("en-US", "A test enum field")
            };
        }
    }
}
