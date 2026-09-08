/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Covers <see cref="UsdValueCoercion"/> across every element type the §6.2 bindings can
    /// name, in both directions and at all three ranks (scalar, array and matrix). The two
    /// directions are separately typed switches, so a per-element-type case is the only way to
    /// prove that neither drops a type or crosses two of them, and that both fail closed rather
    /// than coerce a value that does not fit.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    [Parallelizable]
    public class UsdValueCoercionTests
    {
        private static readonly BuiltInType[] s_elementTypes =
        [
            BuiltInType.Boolean,
            BuiltInType.SByte,
            BuiltInType.Int32,
            BuiltInType.Int64,
            BuiltInType.UInt32,
            BuiltInType.UInt64,
            BuiltInType.Float,
            BuiltInType.Double,
            BuiltInType.String
        ];

        private static UsdValueTypeMapping Mapping(BuiltInType elementType, int valueRank)
        {
            return new UsdValueTypeMapping(
                Opc.Ua.DataTypeIds.BaseDataType, valueRank, null, elementType, isOpaque: false);
        }

        /// <summary>
        /// The USD value a scalar of the given element type is authored as, chosen so it is
        /// representable in every type (a small non-negative integer, or text for a string).
        /// </summary>
        private static UsdValue Authored(BuiltInType elementType)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    return UsdValue.From(true);
                case BuiltInType.Float:
                case BuiltInType.Double:
                    return UsdValue.From(2.5);
                case BuiltInType.String:
                    return UsdValue.FromString("authored");
                default:
                    return UsdValue.From(7L);
            }
        }

        private static void AssertScalar(BuiltInType elementType, in Variant value)
        {
            switch (elementType)
            {
                case BuiltInType.Boolean:
                    Assert.That(value.TryGetValue(out bool b), Is.True);
                    Assert.That(b, Is.True);
                    break;
                case BuiltInType.SByte:
                    Assert.That(value.TryGetValue(out sbyte sb), Is.True);
                    Assert.That(sb, Is.EqualTo((sbyte)7));
                    break;
                case BuiltInType.Int32:
                    Assert.That(value.TryGetValue(out int i), Is.True);
                    Assert.That(i, Is.EqualTo(7));
                    break;
                case BuiltInType.Int64:
                    Assert.That(value.TryGetValue(out long l), Is.True);
                    Assert.That(l, Is.EqualTo(7L));
                    break;
                case BuiltInType.UInt32:
                    Assert.That(value.TryGetValue(out uint ui), Is.True);
                    Assert.That(ui, Is.EqualTo(7U));
                    break;
                case BuiltInType.UInt64:
                    Assert.That(value.TryGetValue(out ulong ul), Is.True);
                    Assert.That(ul, Is.EqualTo(7UL));
                    break;
                case BuiltInType.Float:
                    Assert.That(value.TryGetValue(out float f), Is.True);
                    Assert.That(f, Is.EqualTo(2.5f));
                    break;
                case BuiltInType.Double:
                    Assert.That(value.TryGetValue(out double d), Is.True);
                    Assert.That(d, Is.EqualTo(2.5));
                    break;
                default:
                    Assert.That(value.TryGetValue(out string s), Is.True);
                    Assert.That(s, Is.EqualTo("authored"));
                    break;
            }
        }

        [Test]
        public void CoerceReadsAScalarOfEveryElementType(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            bool ok = UsdValueCoercion.TryCoerce(
                Authored(elementType), Mapping(elementType, ValueRanks.Scalar), 0, out Variant v);

            Assert.That(ok, Is.True);
            AssertScalar(elementType, v);
        }

        [Test]
        public void CoerceReadsAnArrayOfEveryElementType(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            UsdValue authored = UsdTestHelpers.Array(Authored(elementType), Authored(elementType));

            bool ok = UsdValueCoercion.TryCoerce(
                authored, Mapping(elementType, ValueRanks.OneDimension), 0, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(elementType));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void CoerceReadsAMatrixOfEveryElementType(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            // Two rows of two components each, which is the array-of-tuples shape USD authors a
            // rectangular value with.
            UsdValue row = UsdTestHelpers.Tuple(Authored(elementType), Authored(elementType));
            UsdValue authored = UsdTestHelpers.Array(row, row);

            bool ok = UsdValueCoercion.TryCoerce(
                authored, Mapping(elementType, ValueRanks.TwoDimensions), 2, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(elementType));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.TwoDimensions));
        }

        [Test]
        public void CoerceFailsClosedForAnUnsupportedElementType()
        {
            // DateTime has no USD spelling, so every rank must leave the attribute unresolved
            // rather than invent a value.
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.From(1L), Mapping(BuiltInType.DateTime, ValueRanks.Scalar), 0, out _),
                Is.False);
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.IntegerArray(1L),
                    Mapping(BuiltInType.DateTime, ValueRanks.OneDimension),
                    0,
                    out _),
                Is.False);
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(UsdTestHelpers.IntegerTuple(1L)),
                    Mapping(BuiltInType.DateTime, ValueRanks.TwoDimensions),
                    1,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAnUnsupportedValueRank()
        {
            const int threeDimensions = 3;
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.From(1L),
                    Mapping(BuiltInType.Int32, threeDimensions),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceRejectsANullMapping()
        {
            Assert.That(
                () => UsdValueCoercion.TryCoerce(UsdValue.From(1L), null!, 0, out _),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CoerceRejectsAnAbsentValue()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.Null, Mapping(BuiltInType.Int32, ValueRanks.Scalar), 0, out _),
                Is.False);
        }

        [TestCase(BuiltInType.SByte, 300L)]
        [TestCase(BuiltInType.SByte, -300L)]
        [TestCase(BuiltInType.Int32, long.MaxValue)]
        [TestCase(BuiltInType.Int32, long.MinValue)]
        [TestCase(BuiltInType.UInt32, -1L)]
        [TestCase(BuiltInType.UInt32, 4294967296L)]
        [TestCase(BuiltInType.UInt64, -1L)]
        public void CoerceFailsClosedForAnIntegerOutsideTheElementRange(
            BuiltInType elementType, long authored)
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.From(authored), Mapping(elementType, ValueRanks.Scalar), 0, out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAnOutOfRangeArrayElement()
        {
            // One unrepresentable element must fail the whole array, not be silently defaulted.
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.IntegerArray(1L, 300L),
                    Mapping(BuiltInType.SByte, ValueRanks.OneDimension),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAnOutOfRangeMatrixElement()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(UsdTestHelpers.IntegerTuple(1L, 300L)),
                    Mapping(BuiltInType.SByte, ValueRanks.TwoDimensions),
                    2,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAStructuredLeafInATextArray()
        {
            // A dictionary has no faithful scalar text, so a string array holding one is left
            // unresolved rather than rendered to a plausible-but-wrong element.
            UsdValue nested = UsdTestHelpers.Dictionary(
                new System.Collections.Generic.KeyValuePair<string, UsdValue>(
                    "k", UsdValue.From(1L)));

            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(UsdValue.FromString("a"), nested),
                    Mapping(BuiltInType.String, ValueRanks.OneDimension),
                    0,
                    out _),
                Is.False);
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(UsdTestHelpers.Tuple(UsdValue.FromString("a"), nested)),
                    Mapping(BuiltInType.String, ValueRanks.TwoDimensions),
                    2,
                    out _),
                Is.False);
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    nested, Mapping(BuiltInType.String, ValueRanks.Scalar), 0, out _),
                Is.False);
        }

        [Test]
        public void CoerceRendersEveryLeafKindIntoText()
        {
            // A string-bound attribute accepts any leaf with a well-defined textual form.
            UsdValue authored = UsdTestHelpers.Array(
                UsdValue.Null,
                UsdValue.FromToken("token"),
                UsdValue.From(true),
                UsdValue.From(3L),
                UsdValue.From(0.5));

            bool ok = UsdValueCoercion.TryCoerce(
                authored, Mapping(BuiltInType.String, ValueRanks.OneDimension), 0, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ArrayOf<string> text), Is.True);
            Assert.That(text.ToArray(), Is.EqualTo(new[] { string.Empty, "token", "true", "3", "0.5" }));
        }

        [TestCase("2.5", 2.5)]
        [TestCase("-0.25", -0.25)]
        public void CoerceReadsANumberAuthoredAsText(string authored, double expected)
        {
            bool ok = UsdValueCoercion.TryCoerce(
                UsdValue.FromString(authored),
                Mapping(BuiltInType.Double, ValueRanks.Scalar),
                0,
                out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out double d), Is.True);
            Assert.That(d, Is.EqualTo(expected));
        }

        [Test]
        public void CoerceFailsClosedForTextThatIsNotANumber()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.FromString("not a number"),
                    Mapping(BuiltInType.Double, ValueRanks.Scalar),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceReadsABooleanAuthoredAsANumberOrAsText()
        {
            bool fromNumber = UsdValueCoercion.TryCoerce(
                UsdValue.From(1L), Mapping(BuiltInType.Boolean, ValueRanks.Scalar), 0, out Variant a);
            bool fromText = UsdValueCoercion.TryCoerce(
                UsdValue.FromToken("true"),
                Mapping(BuiltInType.Boolean, ValueRanks.Scalar),
                0,
                out Variant b);

            Assert.That(fromNumber, Is.True);
            Assert.That(a.TryGetValue(out bool first), Is.True);
            Assert.That(first, Is.True);
            Assert.That(fromText, Is.True);
            Assert.That(b.TryGetValue(out bool second), Is.True);
            Assert.That(second, Is.True);
        }

        [Test]
        public void CoerceFailsClosedForTextThatIsNotABoolean()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.FromToken("maybe"),
                    Mapping(BuiltInType.Boolean, ValueRanks.Scalar),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAFloatThatOverflowsTheElement()
        {
            // A double that does not fit a float must not be published as an infinity.
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.From(1e300),
                    Mapping(BuiltInType.Float, ValueRanks.Scalar),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceKeepsAnInfinityThatWasAuthoredAsOne()
        {
            bool ok = UsdValueCoercion.TryCoerce(
                UsdValue.From(double.PositiveInfinity),
                Mapping(BuiltInType.Float, ValueRanks.Scalar),
                0,
                out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out float f), Is.True);
            Assert.That(float.IsPositiveInfinity(f), Is.True);
        }

        [Test]
        public void CoerceFailsClosedForANumberTooLargeForAnInteger()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdValue.From(1e30),
                    Mapping(BuiltInType.Int64, ValueRanks.Scalar),
                    0,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAFixedSizeTypeOfTheWrongArity()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.NumberTuple(1.0, 2.0),
                    Mapping(BuiltInType.Double, ValueRanks.OneDimension),
                    3,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceFailsClosedForAMatrixRowOfTheWrongArity()
        {
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(UsdTestHelpers.NumberTuple(1.0, 2.0)),
                    Mapping(BuiltInType.Double, ValueRanks.TwoDimensions),
                    3,
                    out _),
                Is.False);
        }

        [Test]
        public void CoerceTreatsAScalarAsASingleElementSequence()
        {
            bool ok = UsdValueCoercion.TryCoerce(
                UsdValue.From(4L), Mapping(BuiltInType.Int32, ValueRanks.OneDimension), 0, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ArrayOf<int> items), Is.True);
            Assert.That(items.ToArray(), Is.EqualTo(s_intValues1));
        }

        [Test]
        public void CoerceCarriesAnAbsentArrayElementAsTheElementDefault()
        {
            bool ok = UsdValueCoercion.TryCoerce(
                UsdTestHelpers.Array(UsdValue.From(1L), UsdValue.Null),
                Mapping(BuiltInType.Int32, ValueRanks.OneDimension),
                0,
                out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out ArrayOf<int> items), Is.True);
            Assert.That(items.ToArray(), Is.EqualTo(s_intValues2));
        }

        [Test]
        public void CoerceReadsABooleanAsANumber()
        {
            // A bool authored where a number is bound widens to 1/0 rather than failing.
            bool ok = UsdValueCoercion.TryCoerce(
                UsdValue.From(true), Mapping(BuiltInType.Int32, ValueRanks.Scalar), 0, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out int i), Is.True);
            Assert.That(i, Is.EqualTo(1));
        }

        [Test]
        public void CoerceReadsAnIntegerAuthoredAsADouble()
        {
            bool ok = UsdValueCoercion.TryCoerce(
                UsdValue.From(3.0), Mapping(BuiltInType.Int64, ValueRanks.Scalar), 0, out Variant v);

            Assert.That(ok, Is.True);
            Assert.That(v.TryGetValue(out long l), Is.True);
            Assert.That(l, Is.EqualTo(3L));
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        public void CoerceFailsClosedForAStructuredValueBoundToAScalar(BuiltInType elementType)
        {
            // A dictionary is neither a number nor text, so no scalar element type may invent a
            // value for it.
            UsdValue structured = UsdTestHelpers.Dictionary(
                new System.Collections.Generic.KeyValuePair<string, UsdValue>(
                    "k", UsdValue.From(1L)));

            Assert.That(
                UsdValueCoercion.TryCoerce(
                    structured, Mapping(elementType, ValueRanks.Scalar), 0, out _),
                Is.False);
        }

        [Test]
        public void DecoerceReadsAScalarOfEveryElementType(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            bool ok = UsdValueCoercion.TryCoerce(
                Authored(elementType), Mapping(elementType, ValueRanks.Scalar), 0, out Variant v);
            Assert.That(ok, Is.True);

            UsdValue read = UsdValueCoercion.Decoerce(v);

            Assert.That(read.IsNull, Is.False);
            // Round trips through the same binding, which is what an export has to reproduce.
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    read, Mapping(elementType, ValueRanks.Scalar), 0, out Variant again),
                Is.True);
            AssertScalar(elementType, again);
        }

        [Test]
        public void DecoerceReadsAnArrayOfEveryElementType(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            UsdValue authored = UsdTestHelpers.Array(Authored(elementType), Authored(elementType));
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    authored, Mapping(elementType, ValueRanks.OneDimension), 0, out Variant v),
                Is.True);

            UsdValue read = UsdValueCoercion.Decoerce(v);

            Assert.That(read.TryGetArray(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoerceRegroupsAMatrixOfEveryElementTypeIntoRows(
            [ValueSource(nameof(s_elementTypes))] BuiltInType elementType)
        {
            UsdValue row = UsdTestHelpers.Tuple(Authored(elementType), Authored(elementType));
            Assert.That(
                UsdValueCoercion.TryCoerce(
                    UsdTestHelpers.Array(row, row),
                    Mapping(elementType, ValueRanks.TwoDimensions),
                    2,
                    out Variant v),
                Is.True);

            UsdValue read = UsdValueCoercion.Decoerce(v);

            // A matrix is handed back as one tuple per row so the writer can author "[(a, b), …]".
            Assert.That(read.TryGetArray(out ArrayOf<UsdValue> rows), Is.True);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].TryGetTuple(out ArrayOf<UsdValue> cells), Is.True);
            Assert.That(cells.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoerceReturnsNullForAnUnrepresentableValue()
        {
            // Every rank of a type with no USD spelling must decoerce to an absent value rather
            // than a CLR rendering of it.
            // UInt16 is a valid Variant type with no USD spelling: every rank must decoerce to an
            // absent value rather than a CLR rendering of it.
            Assert.That(UsdValueCoercion.Decoerce(Variant.From((ushort)1)).IsNull, Is.True);
            Assert.That(
                UsdValueCoercion.Decoerce(
                    Variant.From((ArrayOf<ushort>)new ushort[] { 1 })).IsNull,
                Is.True);
            Assert.That(
                UsdValueCoercion.Decoerce(
                    Variant.From((MatrixOf<ushort>)new ushort[,] { { 1 } })).IsNull,
                Is.True);
        }

        [Test]
        public void DecoerceReturnsNullForAnAbsentValue()
        {
            Assert.That(UsdValueCoercion.Decoerce(default).IsNull, Is.True);
        }

        private static readonly int[] s_intValues1 = [4];
        private static readonly int[] s_intValues2 = [1, 0];
    }
}
