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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Tests for <see cref="UsdValue"/>, the union that scopes an authored USD value to the
    /// shapes a <c>.usda</c> document can express.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    [Parallelizable]
    public class UsdValueTests
    {
        [Test]
        public void DefaultValueIsNull()
        {
            UsdValue value = default;

            Assert.That(value.IsNull, Is.True);
            Assert.That(value.Kind, Is.EqualTo(UsdValueKind.Null));
            Assert.That(value, Is.EqualTo(UsdValue.Null));
        }

        [Test]
        public void BooleanRoundTrips()
        {
            UsdValue value = UsdValue.From(true);

            Assert.That(value.Kind, Is.EqualTo(UsdValueKind.Boolean));
            Assert.That(value.IsNull, Is.False);
            Assert.That(value.TryGetBoolean(out bool b), Is.True);
            Assert.That(b, Is.True);
        }

        [Test]
        public void IntegerRoundTripsWithoutPrecisionLoss()
        {
            const long large = 9007199254740993L;
            UsdValue value = UsdValue.From(large);

            Assert.That(value.TryGetInteger(out long l), Is.True);
            Assert.That(l, Is.EqualTo(large),
                "An integer must not be widened through a double.");
        }

        [Test]
        public void DoubleRoundTrips()
        {
            UsdValue value = UsdValue.From(1.5);

            Assert.That(value.TryGetDouble(out double d), Is.True);
            Assert.That(d, Is.EqualTo(1.5));
        }

        [Test]
        public void TryGetNumberWidensAnInteger()
        {
            Assert.That(UsdValue.From(7L).TryGetNumber(out double fromInteger), Is.True);
            Assert.That(fromInteger, Is.EqualTo(7.0));
            Assert.That(UsdValue.From(2.5).TryGetNumber(out double fromDouble), Is.True);
            Assert.That(fromDouble, Is.EqualTo(2.5));
        }

        [Test]
        public void AccessorsRejectTheWrongKind()
        {
            UsdValue value = UsdValue.From(1.5);

            Assert.That(value.TryGetInteger(out _), Is.False);
            Assert.That(value.TryGetBoolean(out _), Is.False);
            Assert.That(value.TryGetString(out _), Is.False);
            Assert.That(value.TryGetArray(out _), Is.False);
        }

        /// <summary>
        /// A string, a token, an asset path and a path reference all carry text but are printed
        /// differently by USD, so they must stay distinguishable.
        /// </summary>
        [Test]
        public void TextKindsStayDistinct()
        {
            UsdValue s = UsdValue.FromString("x");
            UsdValue token = UsdValue.FromToken("x");
            UsdValue asset = UsdValue.FromAssetPath("x");
            UsdValue path = UsdValue.FromPathReference("x");

            Assert.That(s.Kind, Is.EqualTo(UsdValueKind.String));
            Assert.That(token.Kind, Is.EqualTo(UsdValueKind.Token));
            Assert.That(asset.Kind, Is.EqualTo(UsdValueKind.AssetPath));
            Assert.That(path.Kind, Is.EqualTo(UsdValueKind.PathReference));

            Assert.That(s.TryGetToken(out _), Is.False);
            Assert.That(token.TryGetString(out _), Is.False);
            Assert.That(s, Is.Not.EqualTo(token));

            foreach (UsdValue value in new[] { s, token, asset, path })
            {
                Assert.That(value.TryGetText(out string text), Is.True);
                Assert.That(text, Is.EqualTo("x"));
            }
        }

        [Test]
        public void NullTextProducesANullValue()
        {
            Assert.That(UsdValue.FromString(null).IsNull, Is.True);
            Assert.That(UsdValue.FromToken(null).IsNull, Is.True);
            Assert.That(UsdValue.FromAssetPath(null).IsNull, Is.True);
            Assert.That(UsdValue.FromPathReference(null).IsNull, Is.True);
        }

        /// <summary>
        /// A tuple prints as <c>(a, b)</c> and an array as <c>[a, b]</c>, so the two must not
        /// compare equal even when they carry the same components.
        /// </summary>
        [Test]
        public void TupleAndArrayStayDistinct()
        {
            ArrayOf<UsdValue> items = new[] { UsdValue.From(1L), UsdValue.From(2L) }.ToArrayOf();
            UsdValue tuple = UsdValue.FromTuple(items);
            UsdValue array = UsdValue.FromArray(items);

            Assert.That(tuple.Kind, Is.EqualTo(UsdValueKind.Tuple));
            Assert.That(array.Kind, Is.EqualTo(UsdValueKind.Array));
            Assert.That(tuple, Is.Not.EqualTo(array));
            Assert.That(tuple.TryGetArray(out _), Is.False);
            Assert.That(array.TryGetTuple(out _), Is.False);
        }

        [Test]
        public void TryGetItemsAcceptsEveryCompositeKind()
        {
            ArrayOf<UsdValue> items = new[] { UsdValue.From(1L) }.ToArrayOf();

            Assert.That(UsdValue.FromTuple(items).TryGetItems(out _), Is.True);
            Assert.That(UsdValue.FromArray(items).TryGetItems(out _), Is.True);
            Assert.That(UsdValue.FromMatrix(items).TryGetItems(out _), Is.True);
            Assert.That(UsdValue.From(1L).TryGetItems(out _), Is.False);
        }

        /// <summary>
        /// The nesting a <see cref="Variant"/> cannot express - an array whose elements are
        /// themselves tuples, as authored for <c>color3f[]</c>.
        /// </summary>
        [Test]
        public void ArrayOfTuplesNests()
        {
            UsdValue row = UsdValue.FromTuple(
                new[] { UsdValue.From(1.0), UsdValue.From(2.0), UsdValue.From(3.0) }.ToArrayOf());
            UsdValue array = UsdValue.FromArray(new[] { row, row }.ToArrayOf());

            Assert.That(array.TryGetArray(out ArrayOf<UsdValue> rows), Is.True);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows[0].TryGetTuple(out ArrayOf<UsdValue> components), Is.True);
            Assert.That(components.Count, Is.EqualTo(3));
            Assert.That(components[2].TryGetDouble(out double third), Is.True);
            Assert.That(third, Is.EqualTo(3.0));
        }

        [Test]
        public void DictionaryRoundTrips()
        {
            var entries = new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
            {
                ["author"] = UsdValue.FromString("acme"),
                ["order"] = UsdValue.From(3L)
            };

            UsdValue value = UsdValue.FromDictionary(entries);

            Assert.That(value.Kind, Is.EqualTo(UsdValueKind.Dictionary));
            Assert.That(value.TryGetDictionary(out IReadOnlyDictionary<string, UsdValue> read),
                Is.True);
            Assert.That(read, Has.Count.EqualTo(2));
            Assert.That(read["order"].TryGetInteger(out long order), Is.True);
            Assert.That(order, Is.EqualTo(3L));
        }

        [Test]
        public void EqualValuesShareAHashCode()
        {
            UsdValue first = UsdValue.FromTuple(
                new[] { UsdValue.From(1.0), UsdValue.FromString("a") }.ToArrayOf());
            UsdValue second = UsdValue.FromTuple(
                new[] { UsdValue.From(1.0), UsdValue.FromString("a") }.ToArrayOf());

            Assert.That(first, Is.EqualTo(second));
            bool operatorEquals = first == second;
            bool operatorNotEquals = first != second;
            Assert.That(operatorEquals, Is.True);
            Assert.That(operatorNotEquals, Is.False);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void DifferingComponentsAreNotEqual()
        {
            UsdValue first = UsdValue.FromTuple(new[] { UsdValue.From(1.0) }.ToArrayOf());
            UsdValue second = UsdValue.FromTuple(new[] { UsdValue.From(2.0) }.ToArrayOf());

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void EqualDictionariesShareAHashCodeWhateverTheEntryOrder()
        {
            UsdValue first = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["author"] = UsdValue.FromString("acme"),
                    ["order"] = UsdValue.From(3L)
                });
            UsdValue second = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["order"] = UsdValue.From(3L),
                    ["author"] = UsdValue.FromString("acme")
                });

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void DictionariesOfTheSameSizeDoNotShareAHashCode()
        {
            // The hash must take the entries into account, not only their count, or every
            // dictionary of the same size would collide in a hash based collection.
            UsdValue first = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["author"] = UsdValue.FromString("acme"),
                    ["order"] = UsdValue.From(3L)
                });
            UsdValue second = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["author"] = UsdValue.FromString("globex"),
                    ["order"] = UsdValue.From(4L)
                });

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.Not.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void DictionaryRendersItsEntriesOrderedByKey()
        {
            UsdValue value = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["order"] = UsdValue.From(3L),
                    ["author"] = UsdValue.FromString("acme"),
                    ["nested"] = UsdValue.FromDictionary(
                        new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                        {
                            ["depth"] = UsdValue.From(1L)
                        })
                });

            // A dictionary must not stringify to the empty string: a caller that falls back to
            // the textual form would silently drop the authored entries.
            Assert.That(value.ToString(), Is.EqualTo("{author: acme, nested: {depth: 1}, order: 3}"));
        }

        [Test]
        public void TryGetMatrixReadsTheRows()
        {
            UsdValue value = UsdValue.FromMatrix(
                new[]
                {
                    UsdValue.FromTuple(new[] { UsdValue.From(1.0), UsdValue.From(0.0) }.ToArrayOf()),
                    UsdValue.FromTuple(new[] { UsdValue.From(0.0), UsdValue.From(1.0) }.ToArrayOf())
                }.ToArrayOf());

            Assert.That(value.Kind, Is.EqualTo(UsdValueKind.Matrix));
            Assert.That(value.TryGetMatrix(out ArrayOf<UsdValue> rows), Is.True);
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(value.TryGetArray(out ArrayOf<UsdValue> _), Is.False);
        }

        [Test]
        public void TryGetNumberRejectsANonNumericKind()
        {
            Assert.That(UsdValue.FromString("1.5").TryGetNumber(out double value), Is.False);
            Assert.That(value, Is.Zero);
            Assert.That(UsdValue.Null.TryGetNumber(out double absent), Is.False);
            Assert.That(absent, Is.Zero);
        }

        [Test]
        public void ScalarsRenderTheirInvariantForm()
        {
            Assert.That(UsdValue.Null.ToString(), Is.Empty);
            Assert.That(UsdValue.From(true).ToString(), Is.EqualTo("true"));
            Assert.That(UsdValue.From(false).ToString(), Is.EqualTo("false"));
            Assert.That(UsdValue.From(-3L).ToString(), Is.EqualTo("-3"));
            Assert.That(UsdValue.From(0.5).ToString(), Is.EqualTo("0.5"));
            Assert.That(UsdValue.FromToken("vertex").ToString(), Is.EqualTo("vertex"));
            Assert.That(UsdValue.FromAssetPath("./a.usda").ToString(), Is.EqualTo("./a.usda"));
            Assert.That(UsdValue.FromPathReference("/P/A").ToString(), Is.EqualTo("/P/A"));
        }

        [Test]
        public void CompositesRenderTheirItems()
        {
            UsdValue tuple = UsdValue.FromTuple(
                new[] { UsdValue.From(1.0), UsdValue.FromString("a") }.ToArrayOf());
            UsdValue array = UsdValue.FromArray(
                new[] { UsdValue.From(1L), UsdValue.From(2L) }.ToArrayOf());
            UsdValue matrix = UsdValue.FromMatrix(new[] { tuple }.ToArrayOf());

            Assert.That(tuple.ToString(), Is.EqualTo("(1, a)"));
            Assert.That(array.ToString(), Is.EqualTo("[1, 2]"));
            Assert.That(matrix.ToString(), Is.EqualTo("((1, a))"));
            Assert.That(UsdValue.FromArray(default).ToString(), Is.EqualTo("[]"));
        }

        [Test]
        public void AnEmptyDictionaryRendersAsEmptyBraces()
        {
            UsdValue value = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal));

            Assert.That(value.ToString(), Is.EqualTo("{}"));
        }

        [Test]
        public void CompositesOfDifferentLengthsAreNotEqual()
        {
            UsdValue first = UsdValue.FromArray(new[] { UsdValue.From(1L) }.ToArrayOf());
            UsdValue second = UsdValue.FromArray(
                new[] { UsdValue.From(1L), UsdValue.From(2L) }.ToArrayOf());

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void DictionariesOfDifferentSizesAreNotEqual()
        {
            UsdValue first = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["a"] = UsdValue.From(1L)
                });
            UsdValue second = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["a"] = UsdValue.From(1L),
                    ["b"] = UsdValue.From(2L)
                });

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void DictionariesThatDifferInAKeyAreNotEqual()
        {
            UsdValue first = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["a"] = UsdValue.From(1L)
                });
            UsdValue second = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal)
                {
                    ["b"] = UsdValue.From(1L)
                });

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void EmptyDictionariesAreEqualAndShareAHashCode()
        {
            UsdValue first = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal));
            UsdValue second = UsdValue.FromDictionary(
                new Dictionary<string, UsdValue>(System.StringComparer.Ordinal));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }

        [Test]
        public void EveryKindProducesAHashCode()
        {
            // An absent value and every text kind must hash without reading an unset payload.
            UsdValue absent = UsdValue.Null;
            UsdValue alsoAbsent = default;
            UsdValue path = UsdValue.FromPathReference("/P");
            UsdValue samePath = UsdValue.FromPathReference("/P");
            UsdValue number = UsdValue.From(1.5);
            UsdValue sameNumber = UsdValue.From(1.5);
            UsdValue flag = UsdValue.From(true);
            UsdValue sameFlag = UsdValue.From(true);

            Assert.That(absent.GetHashCode(), Is.EqualTo(alsoAbsent.GetHashCode()));
            Assert.That(path.GetHashCode(), Is.EqualTo(samePath.GetHashCode()));
            Assert.That(number.GetHashCode(), Is.EqualTo(sameNumber.GetHashCode()));
            Assert.That(flag.GetHashCode(), Is.EqualTo(sameFlag.GetHashCode()));
        }

        [Test]
        public void EqualsAcceptsABoxedValueAndRejectsAnotherType()
        {
            object boxed = UsdValue.From(1L);

            bool matchesBoxed = UsdValue.From(1L).Equals(boxed);
            bool matchesOtherType = UsdValue.From(1L).Equals("1");

            Assert.That(matchesBoxed, Is.True);
            Assert.That(matchesOtherType, Is.False);
        }

        [Test]
        public void ValuesOfDifferentKindsAreNotEqual()
        {
            Assert.That(UsdValue.From(1L), Is.Not.EqualTo(UsdValue.From(1.0)));
            Assert.That(UsdValue.From(true), Is.Not.EqualTo(UsdValue.From(1L)));
        }
    }
}
