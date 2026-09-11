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
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Regression tests for the Variant / TypeInfo / ArrayOf / MatrixOf defects
    /// reported by the read-only bug audit of Opc.Ua.Types.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantAuditRegressionTests
    {
        private static readonly int[] s_oneTwoThree = [1, 2, 3];
        private static readonly int[] s_sevenEight = [7, 8];
        private static readonly int[] s_oneTwoNine = [1, 2, 9];

        [Test]
        public void TryGetDecimalSignExtendsNarrowSignedScalars()
        {
            // The narrow signed scalars were read through m_union.Int32, which
            // zero extends: SByte -1 became 255 and Int16 -1 became 65535.
            Assert.Multiple(() =>
            {
                Assert.That(new Variant((sbyte)-1).TryGetDecimal(out decimal sbyteValue), Is.True);
                Assert.That(sbyteValue, Is.EqualTo(-1m));

                Assert.That(new Variant((short)-1).TryGetDecimal(out decimal int16Value), Is.True);
                Assert.That(int16Value, Is.EqualTo(-1m));

                Assert.That(new Variant(short.MinValue).TryGetDecimal(out decimal minValue), Is.True);
                Assert.That(minValue, Is.EqualTo((decimal)short.MinValue));

                // Unsigned narrow types stay unsigned.
                Assert.That(new Variant(byte.MaxValue).TryGetDecimal(out decimal byteValue), Is.True);
                Assert.That(byteValue, Is.EqualTo(255m));

                Assert.That(new Variant(ushort.MaxValue).TryGetDecimal(out decimal uint16Value), Is.True);
                Assert.That(uint16Value, Is.EqualTo(65535m));
            });
        }

        [Test]
        public void CompareToDoesNotCompareRawUnionBitsOfDifferentTypes()
        {
            // UInt16 100 vs Double 500 used to read the low 16 bits of the
            // double and report "greater".
            Assert.Multiple(() =>
            {
                Assert.That(new Variant((ushort)100).CompareTo(new Variant(500.0)), Is.LessThan(0));
                Assert.That(new Variant(500.0).CompareTo(new Variant((ushort)100)), Is.GreaterThan(0));
                Assert.That(new Variant((ushort)100).CompareTo(new Variant(100.0)), Is.Zero);
            });
        }

        [Test]
        public void CompareToConvertsAcrossNumericWidthsAndSigns()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new Variant(-1).CompareTo(new Variant(1UL)), Is.LessThan(0));
                Assert.That(new Variant(1UL).CompareTo(new Variant(-1)), Is.GreaterThan(0));
                Assert.That(new Variant((sbyte)-5).CompareTo(new Variant(-5L)), Is.Zero);
                Assert.That(new Variant(1.5f).CompareTo(new Variant(1.5)), Is.Zero);
            });
        }

        [Test]
        public void CompareToIsNotComparableForUnrelatedTypes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    new Variant(1).CompareTo(new Variant(new NodeId(1u))),
                    Is.EqualTo(int.MinValue));
                Assert.That(
                    new Variant(double.NaN).CompareTo(new Variant(1)),
                    Is.EqualTo(int.MinValue));
            });
        }

        [Test]
        public void CompareToOrdersNegativeEnumerationValues()
        {
            // Enumeration was compared through m_union.UInt64, so -1 read back
            // as 4294967295 and sorted above every positive value.
            Assert.That(
                new Variant(new EnumValue(-1)).CompareTo(new Variant(new EnumValue(1))),
                Is.LessThan(0));
        }

        [Test]
        public void ValueEqualsComparesStatusCodeValue()
        {
            // StatusCode was not a "value type" for ValueEquals, so the fallback
            // compared the boxed SymbolicId - null on both sides - and reported
            // every pair of StatusCode variants as equal.
            var good = new Variant(new StatusCode(0u));
            var bad = new Variant(new StatusCode(0x80010000u));
            var alsoBad = new Variant(new StatusCode(0x80010000u));

            Assert.Multiple(() =>
            {
                Assert.That(good.ValueEquals(bad), Is.False);
                Assert.That(bad.ValueEquals(alsoBad), Is.True);
            });
        }

        [Test]
        public void ValueEqualsStillComparesNumericValuesAcrossWidths()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new Variant((byte)1).ValueEquals(new Variant(1)), Is.True);
                Assert.That(new Variant(-1).ValueEquals(new Variant(-1L)), Is.True);
                Assert.That(new Variant(1.5f).ValueEquals(new Variant(1.5)), Is.True);
                Assert.That(new Variant(1).ValueEquals(new Variant(2L)), Is.False);
            });
        }

        [Test]
        public void ArrayVariantIsNotReadBackAsScalarZero()
        {
            // TryGetValue(out EnumValue) ignored the value rank, so an Int32
            // array fell through to it and produced the array slice metadata.
            var value = Variant.From(s_oneTwoThree.ToArrayOf());

            Assert.Multiple(() =>
            {
                Assert.That(value.TryGetValue(out int scalar), Is.False);
                Assert.That(scalar, Is.Zero);
                Assert.That(value.TryGetValue(out EnumValue enumValue), Is.False);
                Assert.That(enumValue.Value, Is.Zero);
            });
        }

        [Test]
        public void EqualsIsSymmetricAgainstNullVariant()
        {
            var zero = new Variant(0);
            Variant nullVariant = Variant.Null;

            Assert.Multiple(() =>
            {
                Assert.That(Variant.Equals(zero, nullVariant), Is.EqualTo(Variant.Equals(nullVariant, zero)));
                Assert.That(
                    Variant.Equals(new Variant(5), nullVariant),
                    Is.EqualTo(Variant.Equals(nullVariant, new Variant(5))));
            });
        }

        [Test]
        public void AZeroValuedScalarDoesNotEqualTheNullVariant()
        {
            // Making the null comparison symmetric first made it symmetrically
            // TRUE, because the accessors hand out default(T) for a null
            // variant. Every zero then equalled Variant.Null while staying
            // unequal to each other, and all of them hash to zero, so a
            // HashSet<Variant> collapsed them into whichever arrived first and
            // Dictionary.Add threw for two semantically distinct keys.
            Variant nullVariant = Variant.Null;

            Assert.Multiple(() =>
            {
                Assert.That(new Variant(0), Is.Not.EqualTo(nullVariant));
                Assert.That(nullVariant, Is.Not.EqualTo(new Variant(0)));
                Assert.That(new Variant(false), Is.Not.EqualTo(nullVariant));
                Assert.That(nullVariant, Is.Not.EqualTo(new Variant(false)));
                Assert.That(new Variant(0.0), Is.Not.EqualTo(nullVariant));
                Assert.That(nullVariant, Is.Not.EqualTo(new Variant(0.0)));
                Assert.That(new Variant(0L), Is.Not.EqualTo(nullVariant));
                Assert.That(new Variant(0u), Is.Not.EqualTo(nullVariant));

                Assert.That(
                    new HashSet<Variant> { nullVariant, new Variant(0), new Variant(false) },
                    Has.Count.EqualTo(3));

                // A typed variant whose reference payload is absent still equals
                // the null variant - that is what a null bodied ExtensionObject
                // round trips to.
                Assert.That(nullVariant, Is.EqualTo(new Variant((string)null)));
                Assert.That(new Variant((string)null), Is.EqualTo(nullVariant));
            });
        }

        [Test]
        public void ComparisonOperatorsAreFalseForIncomparableVariants()
        {
            // CompareTo signals "not comparable" with int.MinValue, which is its
            // own negation, so reading it as an ordinary negative number made
            // both a < b and b < a true at the same time.
            var nan = new Variant(float.NaN);
            var one = new Variant(1.0);

            Assert.Multiple(() =>
            {
                Assert.That(nan < one, Is.False);
                Assert.That(nan <= one, Is.False);
                Assert.That(nan > one, Is.False);
                Assert.That(nan >= one, Is.False);
                Assert.That(one < nan, Is.False);
                Assert.That(one > nan, Is.False);

                // ordinary comparisons are unaffected.
                Assert.That(new Variant(1) < new Variant(2), Is.True);
                Assert.That(new Variant(2) > new Variant(1), Is.True);
                Assert.That(new Variant(1) <= new Variant(1), Is.True);
            });
        }

        [Test]
        public void ExpandEnumerationArrayYieldsItsElements()
        {
            // Expand() routed Enumeration arrays through GetVariantArray, which
            // cannot read them, and returned a null array.
            var values = new[] { new EnumValue(1), new EnumValue(2) }.ToArrayOf();
            var variant = new Variant(values);

            ArrayOf<Variant> expanded = variant.Expand();

            Assert.Multiple(() =>
            {
                Assert.That(expanded.IsNull, Is.False);
                Assert.That(expanded.Count, Is.EqualTo(2));
                Assert.That(expanded[0].GetEnumeration().Value, Is.EqualTo(1));
                Assert.That(expanded[1].GetEnumeration().Value, Is.EqualTo(2));
            });
        }

        [Test]
        public void ConvertToEnumerationArrayProducesValues()
        {
            var values = new[] { new EnumValue(7), new EnumValue(8) }.ToArrayOf();
            var variant = new Variant(values);

            Variant converted = variant.ConvertTo(BuiltInType.Int32);

            Assert.Multiple(() =>
            {
                Assert.That(converted.IsNull, Is.False);
                Assert.That(converted.GetInt32Array().ToArray(), Is.EqualTo(s_sevenEight));
            });
        }

        [Test]
        public void MatrixOfReferenceTypeAcceptsNullElements()
        {
            // MatrixOf<T>(Array) threw ArgumentException for any null element,
            // so a string matrix with unset entries could not be created.
            var source = new string[2, 2];
            source[0, 0] = "a";

            MatrixOf<string> matrix = source.ToMatrixOf();

            Assert.Multiple(() =>
            {
                Assert.That(matrix.Count, Is.EqualTo(4));
                Assert.That(matrix.Span[0], Is.EqualTo("a"));
                Assert.That(matrix.Span[1], Is.Null);
            });
        }

        [Test]
        public void MatrixOfValueTypeStillRejectsNullElements()
        {
            var source = new object[1, 1];

            Assert.Throws<ArgumentException>(() => _ = ToIntMatrix(source));

            static MatrixOf<int> ToIntMatrix(Array array)
            {
                return MatrixOf<int>.CreateFromArray(array);
            }
        }

        [Test]
        public void IsInstanceOfDataTypeAcceptsEnumerationVariantForEnumerationDataType()
        {
            var value = new Variant(new EnumValue(3));

            TypeInfo result = TypeInfo.IsInstanceOfDataType(
                value,
                new NodeId(DataTypes.Enumeration),
                ValueRanks.Scalar,
                new NamespaceTable(),
                new Mock<ITypeTable>().Object);

            Assert.That(result.IsUnknown, Is.False);
        }

        [Test]
        public void NullMatrixVariantEqualsItself()
        {
            // A default MatrixOf<T> has no dimensions, so its type info has a
            // value rank that is neither scalar, array nor matrix. Equals fell
            // through every branch and reported the variant as not equal even
            // to an identical one.
            var variant = new Variant(default(MatrixOf<bool>));
            var same = new Variant(default(MatrixOf<bool>));

            Assert.Multiple(() =>
            {
                Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
                Assert.That(variant, Is.EqualTo(same));
                Assert.That(
                    variant,
                    Is.Not.EqualTo(new Variant(default(MatrixOf<int>))));
            });
        }

        [Test]
        public void ByteArrayAndByteStringHashAlike()
        {
            byte[] bytes = [1, 2, 3];
            var asArray = Variant.From(bytes.ToArrayOf());
            var asByteString = Variant.From(new ByteString(bytes));

            Assert.Multiple(() =>
            {
                Assert.That(asArray, Is.EqualTo(asByteString));
                Assert.That(asArray.GetHashCode(), Is.EqualTo(asByteString.GetHashCode()));
            });
        }

        [Test]
        public void SignedZeroAndNaNHashConsistentlyWithEquals()
        {
            Assert.Multiple(() =>
            {
                var positiveZero = new Variant(0.0);
                var negativeZero = new Variant(-0.0);
                Assert.That(positiveZero, Is.EqualTo(negativeZero));
                Assert.That(positiveZero.GetHashCode(), Is.EqualTo(negativeZero.GetHashCode()));

                var positiveZeroFloat = new Variant(0.0f);
                var negativeZeroFloat = new Variant(-0.0f);
                Assert.That(positiveZeroFloat, Is.EqualTo(negativeZeroFloat));
                Assert.That(
                    positiveZeroFloat.GetHashCode(),
                    Is.EqualTo(negativeZeroFloat.GetHashCode()));

                var nan = new Variant(double.NaN);
                var otherNan = new Variant(BitConverter.Int64BitsToDouble(
                    BitConverter.DoubleToInt64Bits(double.NaN) | 0x1));
                Assert.That(nan, Is.EqualTo(otherNan));
                Assert.That(nan.GetHashCode(), Is.EqualTo(otherNan.GetHashCode()));
            });
        }

        [Test]
        public void BitwiseOperatorsReadTheRightHandOperandThroughItsOwnType()
        {
            // The right hand operand's payload was read through the left
            // operand's union field, so 0x100 masked as a byte became 0.
            var lhs = new Variant((byte)0xFF);
            var rhs = new Variant(0x0100);

            Assert.Multiple(() =>
            {
                Assert.That((lhs & rhs).GetByte(), Is.Zero);
                Assert.That((lhs | rhs).GetByte(), Is.EqualTo((byte)0xFF));

                // A non integer right hand operand is not usable at all.
                Assert.That((lhs & new Variant("text")).IsNull, Is.True);
            });
        }

        [Test]
        public void BitwiseOperatorsKeepTheLeftHandOperandType()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    (new Variant((sbyte)-1) & new Variant((sbyte)0x0F)).TypeInfo.BuiltInType,
                    Is.EqualTo(BuiltInType.SByte));
                Assert.That(
                    (new Variant((sbyte)-1) & new Variant((sbyte)0x0F)).GetSByte(),
                    Is.EqualTo((sbyte)0x0F));
                Assert.That(
                    (new Variant((ushort)0xFF00) | new Variant((ushort)0x00FF)).GetUInt16(),
                    Is.EqualTo((ushort)0xFFFF));
            });
        }

        [Test]
        public void NullExtensionObjectArrayStaysNullWhenReadAsStructures()
        {
            // GetStructureArray turned a null ExtensionObject array into an
            // empty array, losing the distinction.
            var variant = new Variant(default(ArrayOf<ExtensionObject>));

            ArrayOf<ReadValueId> structures = variant.GetStructureArray<ReadValueId>();

            Assert.That(structures.IsNull, Is.True);
        }

        [Test]
        public void ReplaceItemRejectsIndexEqualToCount()
        {
            ArrayOf<int> array = s_oneTwoThree.ToArrayOf();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => array.ReplaceItem(9, 3));
                Assert.That(array.ReplaceItem(9, 2).ToArray(), Is.EqualTo(s_oneTwoNine));
            });
        }
    }
}
