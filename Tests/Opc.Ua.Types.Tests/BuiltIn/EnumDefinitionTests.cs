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
    /// Tests for the <see cref="EnumDefinition"/> class.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EnumDefinitionTests
    {
        [Test]
        public void DefaultConstructorSetsDefaultValues()
        {
            var definition = new EnumDefinition();

            Assert.That(definition.Fields, Is.Default);
            Assert.That(definition.IsOptionSet, Is.False);
        }

        [Test]
        public void TypeIdReturnsCorrectValue()
        {
            var definition = new EnumDefinition();

            Assert.That(definition.TypeId, Is.EqualTo(DataTypeIds.EnumDefinition));
        }

        [Test]
        public void BinaryEncodingIdReturnsCorrectValue()
        {
            var definition = new EnumDefinition();

            Assert.That(
                definition.BinaryEncodingId,
                Is.EqualTo(ObjectIds.EnumDefinition_Encoding_DefaultBinary));
        }

        [Test]
        public void XmlEncodingIdReturnsCorrectValue()
        {
            var definition = new EnumDefinition();

            Assert.That(
                definition.XmlEncodingId,
                Is.EqualTo(ObjectIds.EnumDefinition_Encoding_DefaultXml));
        }


        [Test]
        public void EncodeDecodeRoundTripWithAllFieldsSet()
        {
            EnumDefinition original = CreatePopulatedDefinition();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new EnumDefinition();
            decoded.Decode(decoder);

            Assert.That(decoded.Fields.Count, Is.EqualTo(original.Fields.Count));
            for (int i = 0; i < original.Fields.Count; i++)
            {
                Assert.That(decoded.Fields[i].Name, Is.EqualTo(original.Fields[i].Name));
                Assert.That(decoded.Fields[i].Value, Is.EqualTo(original.Fields[i].Value));
                Assert.That(
                    decoded.Fields[i].DisplayName,
                    Is.EqualTo(original.Fields[i].DisplayName));
                Assert.That(
                    decoded.Fields[i].Description,
                    Is.EqualTo(original.Fields[i].Description));
            }
        }

        [Test]
        public void EncodeDecodeRoundTripWithDefaultFields()
        {
            var original = new EnumDefinition();

            var messageContext = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            using var encoder = new BinaryEncoder(messageContext);
            original.Encode(encoder);
            byte[] buffer = encoder.CloseAndReturnBuffer();

            using var decoder = new BinaryDecoder(buffer, messageContext);
            var decoded = new EnumDefinition();
            decoded.Decode(decoder);

            Assert.That(decoded.Fields, Is.EqualTo(original.Fields));
        }

        [Test]
        public void IsEqualSameReferenceReturnsTrue()
        {
            EnumDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.IsEqual(definition), Is.True);
        }

        [Test]
        public void IsEqualNullReturnsFalse()
        {
            EnumDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.IsEqual(null), Is.False);
        }

        [Test]
        public void IsEqualWrongTypeReturnsFalse()
        {
            EnumDefinition definition = CreatePopulatedDefinition();
            var other = new Argument();

            Assert.That(definition.IsEqual(other), Is.False);
        }

        [Test]
        public void IsEqualEqualObjectsReturnsTrue()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1.IsEqual(def2), Is.True);
        }

        [Test]
        public void IsEqualDifferentFieldsReturnsFalse()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();
            def2.Fields = new EnumField[] {
                new() { Name = "Other", Value = 99 }
            }.ToArrayOf();

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void IsEqualEmptyVsPopulatedFieldsReturnsFalse()
        {
            var def1 = new EnumDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1.IsEqual(def2), Is.False);
        }

        [Test]
        public void EqualsObjectWithEqualObjectReturnsTrue()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            object def2 = CreatePopulatedDefinition();

            Assert.That(def1.Equals(def2), Is.True);
        }

        [Test]
        public void EqualsObjectWithNonEqualObjectReturnsFalse()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            var def2 = new EnumDefinition();

            Assert.That(def1.Equals((object)def2), Is.False);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalse()
        {
            EnumDefinition definition = CreatePopulatedDefinition();

            Assert.That(definition.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsEnumDefinitionWithEqualReturnsTrue()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1.Equals(def2), Is.True);
        }

        [Test]
        public void EqualsEnumDefinitionWithNullReturnsFalse()
        {
            EnumDefinition definition = CreatePopulatedDefinition();

#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(definition.Equals((EnumDefinition)null), Is.False);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetHashCodeSameInstanceReturnsConsistentHash()
        {
            EnumDefinition definition = CreatePopulatedDefinition();

            int hash1 = definition.GetHashCode();
            int hash2 = definition.GetHashCode();

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void GetHashCodeDefaultInstanceReturnsConsistentHash()
        {
            var definition = new EnumDefinition();

            int hash1 = definition.GetHashCode();
            int hash2 = definition.GetHashCode();

            Assert.That(hash1, Is.EqualTo(hash2));
        }

        [Test]
        public void OperatorEqualWithEqualObjectsReturnsTrue()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1 == def2, Is.True);
        }

        [Test]
        public void OperatorEqualWithBothNullReturnsTrue()
        {
            EnumDefinition def1 = null;
            EnumDefinition def2 = null;

#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(def1 == def2, Is.True);
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void OperatorEqualWithOneNullReturnsFalse()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = null;

            Assert.That(def1 == def2, Is.False);
            Assert.That(def2 == def1, Is.False);
        }

        [Test]
        public void OperatorNotEqualWithDifferentValuesReturnsTrue()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            var def2 = new EnumDefinition();

            Assert.That(def1 != def2, Is.True);
        }

        [Test]
        public void OperatorNotEqualWithEqualValuesReturnsFalse()
        {
            EnumDefinition def1 = CreatePopulatedDefinition();
            EnumDefinition def2 = CreatePopulatedDefinition();

            Assert.That(def1 != def2, Is.False);
        }

        [Test]
        public void CloneProducesIndependentCopy()
        {
            EnumDefinition original = CreatePopulatedDefinition();

            var clone = (EnumDefinition)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);

            clone.Fields = new EnumField[] {
                new() { Name = "Modified", Value = 99 }
            }.ToArrayOf();
            Assert.That(original.Fields.Count, Is.EqualTo(2));
        }

        [Test]
        public void MemberwiseCloneDeepCopiesFields()
        {
            EnumDefinition original = CreatePopulatedDefinition();

            var clone = (EnumDefinition)original.MemberwiseClone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.Fields.Count, Is.EqualTo(original.Fields.Count));
            Assert.That(clone.Fields[0].Name, Is.EqualTo(original.Fields[0].Name));
            Assert.That(clone.Fields[1].Name, Is.EqualTo(original.Fields[1].Name));
        }

        [Test]
        public void CloneWithDefaultValuesProducesEqualCopy()
        {
            var original = new EnumDefinition();

            var clone = (EnumDefinition)original.Clone();

            Assert.That(clone, Is.Not.SameAs(original));
            Assert.That(clone.IsEqual(original), Is.True);
        }

        private static EnumDefinition CreatePopulatedDefinition()
        {
            return new EnumDefinition
            {
                Fields = new EnumField[]
                {
                    new() {
                        Name = "Field1",
                        Value = 0,
                        DisplayName = new LocalizedText("en-US", "First"),
                        Description = new LocalizedText("en-US", "First field")
                    },
                    new() {
                        Name = "Field2",
                        Value = 1,
                        DisplayName = new LocalizedText("en-US", "Second"),
                        Description = new LocalizedText("en-US", "Second field")
                    }
                }.ToArrayOf(),
                IsOptionSet = true
            };
        }
    }
}
