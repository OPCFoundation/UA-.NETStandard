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
using System.Numerics;
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
        public void CompareToOrdersTheNullVariantConsistentlyWithEquals()
        {
            // CompareTo substituted the typed side's type info and then read
            // the null variant's zeroed union storage, so it reported equality
            // for every zero valued scalar while Equals reported them distinct.
            // A SortedSet keyed on CompareTo therefore still collapsed them.
            Variant nullVariant = Variant.Null;

            Assert.Multiple(() =>
            {
                foreach (Variant zero in new[]
                {
                    new Variant(0),
                    new Variant(false),
                    new Variant(0.0),
                    new Variant(0L)
                })
                {
                    Assert.That(
                        nullVariant.CompareTo(zero),
                        Is.LessThan(0),
                        $"{zero.TypeInfo.BuiltInType} must sort after the null variant");
                    Assert.That(
                        zero.CompareTo(nullVariant),
                        Is.GreaterThan(0),
                        $"{zero.TypeInfo.BuiltInType} must sort after the null variant");
                }

                Assert.That(
                    new SortedSet<Variant> { nullVariant, new Variant(0), new Variant(false) },
                    Has.Count.EqualTo(3));

                // and the order agrees with Equals where Equals says equal.
                Assert.That(nullVariant.CompareTo(new Variant((string)null)), Is.Zero);
                Assert.That(new Variant((string)null).CompareTo(nullVariant), Is.Zero);
                Assert.That(nullVariant.CompareTo(nullVariant), Is.Zero);
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

                // .NET Framework's Double.GetHashCode folds the two zeroes
                // together but hashes the raw bits of a NaN, so a variant has
                // to canonicalize NaN itself to keep the hash in agreement with
                // Equals on every target framework.
                var nan = new Variant(double.NaN);
                var otherNan = new Variant(BitConverter.Int64BitsToDouble(
                    BitConverter.DoubleToInt64Bits(double.NaN) | 0x1));
                Assert.That(nan, Is.EqualTo(otherNan));
                Assert.That(nan.GetHashCode(), Is.EqualTo(otherNan.GetHashCode()));

                var nanFloat = new Variant(float.NaN);
                var otherNanFloat = new Variant(BitConverter.ToSingle(
                    BitConverter.GetBytes(
                        BitConverter.ToInt32(BitConverter.GetBytes(float.NaN), 0) | 0x1),
                    0));
                Assert.That(nanFloat, Is.EqualTo(otherNanFloat));
                Assert.That(nanFloat.GetHashCode(), Is.EqualTo(otherNanFloat.GetHashCode()));
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
                // The result takes the size of the larger operand.
                Assert.That((lhs & rhs).GetInt32(), Is.Zero);
                Assert.That((lhs | rhs).GetInt32(), Is.EqualTo(0x01FF));

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
        public void BitwiseOperatorsWidenToTheLargerOperandType()
        {
            // OPC 10000-4 7.7.3: the result matches the size of the largest
            // operand, so bits of a wider right hand operand are not lost.
            Variant or = new Variant((byte)0x01) | new Variant(0x100u);
            Variant and = new Variant((short)-1) & new Variant(0x1_0000_000FL);
            Assert.Multiple(() =>
            {
                Assert.That(or.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
                Assert.That(or.GetUInt32(), Is.EqualTo(0x101u));
                Assert.That(and.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int64));
                Assert.That(and.GetInt64(), Is.EqualTo(0x1_0000_000FL));
            });
        }

        [Test]
        public void CompareToOrdersStringsOrdinally()
        {
            // string.CompareTo(object) is culture aware: "a" sorts before "B"
            // there, but after it ordinally.
            Assert.Multiple(() =>
            {
                Assert.That(new Variant("a").CompareTo(new Variant("B")), Is.EqualTo(1));
                Assert.That(new Variant("B").CompareTo(new Variant("a")), Is.EqualTo(-1));
                Assert.That(new Variant("a­b").CompareTo(new Variant("ab")), Is.Not.Zero);
                Assert.That(new Variant("ab").CompareTo(new Variant("ab")), Is.Zero);
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

        [Test]
        public void LegacyMatrixHashIsConsistentWithEquals()
        {
            // T2-1: GetHashCode hashed the array references while Equals
            // compares the contents.
#pragma warning disable CS0618 // Type or member is obsolete
            var a = new Matrix(new int[,] { { 1, 2 }, { 3, 4 } }, BuiltInType.Int32);
            var b = new Matrix(new int[,] { { 1, 2 }, { 3, 4 } }, BuiltInType.Int32);
#pragma warning restore CS0618 // Type or member is obsolete

            Assert.Multiple(() =>
            {
                Assert.That(IsEqual(a, b), Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            });
        }

        [Test]
        public void EmptyMatricesOfDifferentShapeAreNotEqual()
        {
            // T2-2: [0,5] and [5,0] were equal but hashed differently.
            var a = new MatrixOf<int>(new ReadOnlyMemory<int>(Array.Empty<int>()), [0, 5]);
            var b = new MatrixOf<int>(new ReadOnlyMemory<int>(Array.Empty<int>()), [5, 0]);
            var c = new MatrixOf<int>(new ReadOnlyMemory<int>(Array.Empty<int>()), [0, 5]);

            Assert.Multiple(() =>
            {
                Assert.That(IsEqual(a, b), Is.False);
                Assert.That(IsEqual(a, c), Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(c.GetHashCode()));
            });
        }

        [Test]
        public void MatrixEqualsForeignArrayReturnsFalse()
        {
            // T2-3: Equals(object) threw for arrays of another element type,
            // arrays with null entries and non zero based arrays.
            MatrixOf<int> matrix = new int[,] { { 1 } };
            var nonZeroBased = Array.CreateInstance(typeof(int), [1, 1], [1, 1]);

            Assert.Multiple(() =>
            {
                Assert.That(IsEqual(matrix, (object)new string[,] { { "x" } }), Is.False);
                Assert.That(IsEqual(matrix, (object)new int?[1, 1]), Is.False);
                Assert.That(IsEqual(matrix, (object)nonZeroBased), Is.False);
                Assert.That(IsEqual(matrix, (object)new int[,] { { 1 } }), Is.True);
            });
        }

        [Test]
        public void VariantWithTypedNullHashesLikeVariantNull()
        {
            // T2-4: typed null payloads equal Variant.Null and must hash to 0.
            Variant[] typedNulls =
            [
                Variant.From(ArrayOf<int>.Null),
                Variant.From(MatrixOf<int>.Null),
                Variant.From(default(ByteString)),
                Variant.From(QualifiedName.Null),
                new Variant(LocalizedText.Null)
            ];

            Assert.Multiple(() =>
            {
                foreach (Variant typedNull in typedNulls)
                {
                    Assert.That(IsEqual(typedNull, Variant.Null), Is.True, typedNull.TypeInfo.ToString());
                    Assert.That(
                        typedNull.GetHashCode(),
                        Is.EqualTo(Variant.Null.GetHashCode()),
                        typedNull.TypeInfo.ToString());
                }

                // A null byte string also equals the empty one.
                Variant empty = Variant.From(ByteString.Empty);
                Variant nullBytes = Variant.From(default(ByteString));
                Assert.That(IsEqual(empty, nullBytes), Is.True);
                Assert.That(empty.GetHashCode(), Is.EqualTo(nullBytes.GetHashCode()));
            });
        }

        [Test]
        public void XmlElementToXElementDoesNotProcessDtd()
        {
            // T2-5: XElement.Load(Stream) expanded DTD entities.
            var xml = (XmlElement)"<!DOCTYPE a [<!ENTITY x \"expanded\">]><a>&x;</a>";
            var plain = (XmlElement)"<a> <b>text</b> </a>";

            Assert.Multiple(() =>
            {
                Assert.That(xml.AsXElement(), Is.Null);
                Assert.Throws<System.Xml.XmlException>(() => xml.ToXElement());
                Assert.That(plain.ToXElement().Element("b")?.Value, Is.EqualTo("text"));
            });
        }

        [Test]
        public void ByteStringCompareToEmptyArrayIsPositive()
        {
            // T2-6: a non empty byte string sorted before an empty array.
            ByteString value = ByteString.From(1, 2, 3);

            Assert.Multiple(() =>
            {
                Assert.That(value.CompareTo(Array.Empty<byte>()), Is.GreaterThan(0));
                Assert.That(value.CompareTo((byte[])null), Is.GreaterThan(0));
                Assert.That(value.CompareTo(ByteString.Empty), Is.GreaterThan(0));
                Assert.That(ByteString.Empty.CompareTo(Array.Empty<byte>()), Is.Zero);
            });
        }

        [Test]
        public void DecimalEqualityAndHashWithExtremeScales()
        {
            // T2-7: equality and hashing canonicalized with one division
            // (or multiplication) per scale step.
            var sevenAtMaxScale = new Opc.Ua.Decimal(
                BigInteger.Pow(10, short.MaxValue) * 7,
                short.MaxValue);
            var seven = new Opc.Ua.Decimal(7, 0);
            var oneAtMinScale = new Opc.Ua.Decimal(BigInteger.One, short.MinValue);
            var oneExpanded = new Opc.Ua.Decimal(BigInteger.Pow(10, -short.MinValue), 0);

            Assert.Multiple(() =>
            {
                Assert.That(IsEqual(sevenAtMaxScale, seven), Is.True);
                Assert.That(sevenAtMaxScale.GetHashCode(), Is.EqualTo(seven.GetHashCode()));
                Assert.That(IsEqual(oneAtMinScale, oneExpanded), Is.True);
                Assert.That(oneAtMinScale.GetHashCode(), Is.EqualTo(oneExpanded.GetHashCode()));
                Assert.That(IsEqual(oneAtMinScale, seven), Is.False);
                Assert.That(IsEqual(new Opc.Ua.Decimal(15, 1), new Opc.Ua.Decimal(150, 2)), Is.True);
                Assert.That(IsEqual(new Opc.Ua.Decimal(15, 1), new Opc.Ua.Decimal(151, 2)), Is.False);
                Assert.That(IsEqual(new Opc.Ua.Decimal(-15, 1), new Opc.Ua.Decimal(150, 2)), Is.False);
                Assert.That(new Opc.Ua.Decimal(5, -3).Canonicalize().UnscaledValue, Is.EqualTo(new BigInteger(5000)));
                Assert.That(new Opc.Ua.Decimal(100_000_000_000, 5).Canonicalize().UnscaledValue, Is.EqualTo(new BigInteger(1_000_000)));
                Assert.That(new Opc.Ua.Decimal(100_000_000_000, 5).Canonicalize().Scale, Is.Zero);
                Assert.That(new Opc.Ua.Decimal(1_500, 3).Canonicalize().Scale, Is.EqualTo((short)1));
            });
        }

        [Test]
        public void DecimalEqualityMatchesCanonicalFormForManyValues()
        {
            // T2-7: the scale independent comparison must agree with the
            // canonical form for ordinary values.
            var random = new Random(4242);
            for (int ii = 0; ii < 500; ii++)
            {
                var left = new Opc.Ua.Decimal(
                    new BigInteger(random.Next(-1000, 1000)) * BigInteger.Pow(10, random.Next(0, 6)),
                    (short)random.Next(-4, 8));
                var right = new Opc.Ua.Decimal(
                    new BigInteger(random.Next(-1000, 1000)) * BigInteger.Pow(10, random.Next(0, 6)),
                    (short)random.Next(-4, 8));
                Opc.Ua.Decimal cl = left.Canonicalize();
                Opc.Ua.Decimal cr = right.Canonicalize();
                bool expected = cl.Scale == cr.Scale && cl.UnscaledValue == cr.UnscaledValue;

                Assert.That(IsEqual(left, right), Is.EqualTo(expected), $"{left} == {right}");
                Assert.That(IsEqual(left, cl), Is.True, $"{left} == {cl}");
                Assert.That(left.GetHashCode(), Is.EqualTo(cl.GetHashCode()), $"{left} hash");
            }
        }

        [Test]
        public void SerializableMatrixOfRoundTripsNullMatrix()
        {
            // T2-8: a null matrix could not be deserialized.
            Assert.Multiple(() =>
            {
                Assert.That(new SerializableMatrixOf<int>(MatrixOf<int>.Null).Value.IsNull, Is.True);
                Assert.That(new SerializableMatrixOf<int>().Value.IsNull, Is.True);
                MatrixOf<int> matrix = new int[,] { { 1, 2 }, { 3, 4 } };
                Assert.That(new SerializableMatrixOf<int>(matrix).Value, Is.EqualTo(matrix));
            });
        }

        [Test]
        public void MatrixDimensionsCannotBeMutated()
        {
            // T2-9: Dimensions handed out the private array.
            var matrix = new MatrixOf<int>(new int[4], [2, 2]);
            matrix.Dimensions[0] = 4;
            matrix.ToArrayOf(out int[] dimensions);
            dimensions[1] = 7;

            Assert.That(matrix.Dimensions, Is.EqualTo(new[] { 2, 2 }));
        }

        [Test]
        public void OneDimensionalMatrixVariantHashesLikeArrayVariant()
        {
            // T2-10: equal variants hashed differently.
            ArrayOf<int> array = s_oneTwoThree.ToArrayOf();
            var matrix = new MatrixOf<int>(s_oneTwoThree, [3]);
            Variant a = Variant.From(array);
            Variant b = Variant.From(matrix);

            Assert.Multiple(() =>
            {
                Assert.That(IsEqual(a, b), Is.True);
                Assert.That(IsEqual(b, a), Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
                Assert.That(matrix.GetHashCode(), Is.EqualTo(array.GetHashCode()));
            });
        }

        [Test]
        public void ConvertToKeepsOneElementAndEmptyArraysAsArrays()
        {
            // T2-11: Part 4 7.7.3 - arrays convert element-wise to arrays.
            Variant single = Variant.From(new[] { 5 }.ToArrayOf()).ConvertTo(BuiltInType.Double);
            Variant empty = Variant.From(ArrayOf<int>.Empty).ConvertTo(BuiltInType.Double);
            Variant number = Variant.From(new[] { 5 }.ToArrayOf()).ConvertTo(BuiltInType.Number);
            Variant two = Variant.From(new[] { 5, 6 }.ToArrayOf()).ConvertTo(BuiltInType.String);

            Assert.Multiple(() =>
            {
                Assert.That(single.TypeInfo, Is.EqualTo(TypeInfo.Arrays.Double));
                Assert.That(single.GetDoubleArray().ToArray(), Is.EqualTo(new[] { 5.0 }));
                Assert.That(empty.TypeInfo, Is.EqualTo(TypeInfo.Arrays.Double));
                Assert.That(empty.IsNull, Is.False);
                Assert.That(empty.GetDoubleArray().Count, Is.Zero);
                Assert.That(number.TypeInfo, Is.EqualTo(TypeInfo.Arrays.Double));
                Assert.That(two.GetStringArray().ToArray(), Is.EqualTo(new[] { "5", "6" }));
                Assert.That(
                    () => Variant.From(ArrayOf<int>.Empty).ConvertTo(BuiltInType.DiagnosticInfo),
                    Throws.TypeOf<InvalidCastException>());
            });
        }

        [Test]
        public void ConstructRecognizesMultiDimensionalEncodeableAndEnumArrays()
        {
            // T2-12: the multi-dimensional branch tested the array type.
            TypeInfo encodeables = TypeInfo.Construct(typeof(Argument[,]));
            TypeInfo enums = TypeInfo.Construct(typeof(AuditTestEnum[,,]));

            Assert.Multiple(() =>
            {
                Assert.That(encodeables.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
                Assert.That(encodeables.ValueRank, Is.EqualTo(2));
                Assert.That(TypeInfo.GetValueRank(typeof(Argument[,])), Is.EqualTo(2));
                Assert.That(enums.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
                Assert.That(enums.ValueRank, Is.EqualTo(3));
            });
        }

        [Test]
        public void UnknownTypeInfoIsNeitherScalarNorArray()
        {
            // T2-13: Unknown reported ValueRank 0 (OneOrMoreDimensions).
            Assert.Multiple(() =>
            {
                Assert.That(TypeInfo.Unknown.IsArray, Is.False);
                Assert.That(TypeInfo.Unknown.IsScalar, Is.False);
                Assert.That(TypeInfo.Unknown.IsMatrix, Is.False);
                Assert.That(TypeInfo.Unknown.ValueRank, Is.EqualTo(ValueRanks.Any));
                Assert.That(Variant.Null.TypeInfo.IsArray, Is.False);
                Assert.That(Variant.Null.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.Any));
                Assert.That(TypeInfo.Unknown, Is.Default);
            });
        }

        [Test]
        public void TryCastToReportsTypeMismatch()
        {
            // T2-14: every built-in branch reported success with default(T).
            Assert.Multiple(() =>
            {
                Assert.That(Variant.From("abc").TryCastTo(out int _), Is.False);
                Assert.That(Variant.From(5).TryCastTo(out ArrayOf<int> _), Is.False);
                Assert.That(Variant.From(5).TryCastTo(out int[] _), Is.False);
                Assert.That(Variant.From(5).TryCastTo(out MatrixOf<int> _), Is.False);
                Assert.That(Variant.From(1.5).TryCastTo(out string _), Is.False);
                Assert.That(Variant.From(5).TryCastTo(out Argument _), Is.False);
                Assert.That(
                    () => Variant.From(1.5).CastTo<string>(),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(new DataValue(Variant.From("abc")).GetValue(-1), Is.EqualTo(-1));

                Assert.That(Variant.From(5).TryCastTo(out int five), Is.True);
                Assert.That(five, Is.EqualTo(5));
                Assert.That(Variant.From("abc").TryCastTo(out string abc), Is.True);
                Assert.That(abc, Is.EqualTo("abc"));
                Assert.That(Variant.From(s_oneTwoThree.ToArrayOf()).TryCastTo(out int[] ints), Is.True);
                Assert.That(ints, Is.EqualTo(s_oneTwoThree));
            });
        }

        [Test]
        public void TryCastToAndFromSupportSystemDateTime()
        {
            // T2-15: System.DateTime had no branch.
            var now = new DateTime(2026, 9, 24, 12, 30, 0, DateTimeKind.Utc);
            var value = new DataValue(Variant.From((DateTimeUtc)now));

            Assert.Multiple(() =>
            {
                Assert.That(value.GetValue(DateTime.MinValue), Is.EqualTo(now));
                Assert.That(Variant.From((DateTimeUtc)now).TryCastTo(out DateTime[] _), Is.False);
                Assert.That(VariantHelper.CastFrom(now).TypeInfo, Is.EqualTo(TypeInfo.Scalars.DateTime));
                Assert.That(VariantHelper.CastFrom(now).GetDateTime(), Is.EqualTo((DateTimeUtc)now));
                Assert.That(
                    VariantHelper.CastFrom(new[] { now }).TypeInfo,
                    Is.EqualTo(TypeInfo.Arrays.DateTime));
                Assert.That(
                    Variant.From(new[] { (DateTimeUtc)now }.ToArrayOf()).CastTo<DateTime[]>(),
                    Is.EqualTo(new[] { now }));
            });
        }

        [Test]
        public void TryCastToConvertsEnumsOfAnyWidth()
        {
            // T2-16: an Int32 was reinterpreted as an 8 byte enum.
            Assert.Multiple(() =>
            {
                Assert.That(Variant.From(5).TryCastTo(out AuditLongEnum longEnum), Is.True);
                Assert.That(longEnum, Is.EqualTo(AuditLongEnum.Five));
                Assert.That(Variant.From(5).TryCastTo(out AuditByteEnum byteEnum), Is.True);
                Assert.That(byteEnum, Is.EqualTo(AuditByteEnum.Five));
                Assert.That(Variant.From(2).TryCastTo(out AuditTestEnum intEnum), Is.True);
                Assert.That(intEnum, Is.EqualTo(AuditTestEnum.Two));
                Assert.That(Variant.From("x").TryCastTo(out AuditTestEnum _), Is.False);
            });
        }

        private static bool IsEqual(object left, object right)
        {
            return left.Equals(right);
        }

        public enum AuditTestEnum
        {
            One = 1,
            Two = 2
        }

        public enum AuditLongEnum : long
        {
            Five = 5
        }

        public enum AuditByteEnum : byte
        {
            Five = 5
        }
    }
}
