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

using NUnit.Framework;
using Opc.Ua.Aas.V3;

namespace Opc.Ua.Aas.Tests.Values
{
    /// <summary>
    /// Tests the clause 6.4 equivalence relation. The three examples the
    /// clause states are pinned here, together with the negative cases its
    /// negative control depends on.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasValueSpaceComparerTests
    {
        [Test]
        public void TheDecimalExampleFromTheClauseIsEquivalent()
        {
            // "'1.500000' and '1.5' are equivalent as xs:decimal".
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    "1.500000", "1.5", AASDataTypeDefXsdDataType.Decimal),
                Is.True);
        }

        [Test]
        public void TheBooleanExampleFromTheClauseIsEquivalent()
        {
            // "'1' and 'true' are equivalent as xs:boolean".
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    "1", "true", AASDataTypeDefXsdDataType.Boolean),
                Is.True);
        }

        [Test]
        public void TheCounterExampleFromTheClauseIsNotEquivalent()
        {
            // "'1.5' and '2.5' are not". This is the case the negative control
            // depends on: a comparison loose enough to miss it would let a
            // Server corrupt a value and still pass.
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    "1.5", "2.5", AASDataTypeDefXsdDataType.Decimal),
                Is.False);
        }

        [TestCase("+42", "42", AASDataTypeDefXsdDataType.Int)]
        [TestCase("0042", "42", AASDataTypeDefXsdDataType.Int)]
        [TestCase("-0", "0", AASDataTypeDefXsdDataType.Int)]
        [TestCase("0", "false", AASDataTypeDefXsdDataType.Boolean)]
        [TestCase("1.0", "1", AASDataTypeDefXsdDataType.Decimal)]
        [TestCase("0a1b", "0A1B", AASDataTypeDefXsdDataType.HexBinary)]
        public void TwoSpellingsOfOneValueAreEquivalent(
            string left,
            string right,
            AASDataTypeDefXsdDataType valueType)
        {
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(left, right, valueType),
                Is.True);
        }

        [TestCase("1", "2", AASDataTypeDefXsdDataType.Int)]
        [TestCase("true", "false", AASDataTypeDefXsdDataType.Boolean)]
        [TestCase("1.5", "1.6", AASDataTypeDefXsdDataType.Decimal)]
        [TestCase("0A1B", "0A1C", AASDataTypeDefXsdDataType.HexBinary)]
        [TestCase("a", "b", AASDataTypeDefXsdDataType.String)]
        public void TwoDifferentValuesAreNotEquivalent(
            string left,
            string right,
            AASDataTypeDefXsdDataType valueType)
        {
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(left, right, valueType),
                Is.False);
        }

        [Test]
        public void ADecimalThatLostDigitsIsNotEquivalent()
        {
            // The negative control canonicalizes an xs:decimal through a fixed
            // working precision so digits are lost. A comparison that widened
            // to double would call these equal.
            const string authored = "123456789012345678901234567890.123456789";
            const string lossy = "123456789012345680000000000000";

            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    authored, lossy, AASDataTypeDefXsdDataType.Decimal),
                Is.False);
        }

        [Test]
        public void TwoAbsentValuesAreEquivalent()
        {
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    null, null, AASDataTypeDefXsdDataType.String),
                Is.True);
        }

        [TestCase(null, "")]
        [TestCase("", null)]
        [TestCase(null, "x")]
        public void AnAbsentValueIsNeverEquivalentToAPresentOne(string? left, string? right)
        {
            // Clause 6.1.5 keeps absence significant. Treating an absent field
            // as an empty string would erase exactly the distinction the round
            // trip has to preserve.
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    left, right, AASDataTypeDefXsdDataType.String),
                Is.False);
        }

        [Test]
        public void AnUnparsableValueIsEquivalentOnlyToTheSameSpelling()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    AasValueSpaceComparer.AreEquivalent(
                        "not a number", "not a number", AASDataTypeDefXsdDataType.Int),
                    Is.True);
                Assert.That(
                    AasValueSpaceComparer.AreEquivalent(
                        "not a number", "also not", AASDataTypeDefXsdDataType.Int),
                    Is.False);
            });
        }

        [Test]
        public void MaterializedValuesCompareInTheValueSpace()
        {
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    "1.500000", AASDataTypeDefXsdDataType.Decimal, out Variant left, out _),
                Is.True);
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    "1.5", AASDataTypeDefXsdDataType.Decimal, out Variant right, out _),
                Is.True);

            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    left, right, AASDataTypeDefXsdDataType.Decimal),
                Is.True);
        }

        [Test]
        public void TwoNullVariantsAreEquivalent()
        {
            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    Variant.Null, Variant.Null, AASDataTypeDefXsdDataType.Int),
                Is.True);
        }

        [Test]
        public void ANullVariantIsNotEquivalentToAPresentOne()
        {
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    "1", AASDataTypeDefXsdDataType.Int, out Variant present, out _),
                Is.True);

            Assert.That(
                AasValueSpaceComparer.AreEquivalent(
                    Variant.Null, present, AASDataTypeDefXsdDataType.Int),
                Is.False);
        }

        [TestCase("1.5", AASDataTypeDefXsdDataType.Decimal, true)]
        [TestCase("1.500000", AASDataTypeDefXsdDataType.Decimal, false)]
        [TestCase("42", AASDataTypeDefXsdDataType.Int, true)]
        [TestCase("+42", AASDataTypeDefXsdDataType.Int, false)]
        [TestCase("true", AASDataTypeDefXsdDataType.Boolean, true)]
        [TestCase("1", AASDataTypeDefXsdDataType.Boolean, false)]
        public void IsCanonicalDistinguishesARewriteFromACorruption(
            string lexical,
            AASDataTypeDefXsdDataType valueType,
            bool expected)
        {
            // Clause 6.4's negative control asserts that re-writing a value
            // into its canonical form is not reported. This predicate is what
            // tells the two apart when explaining a difference.
            Assert.That(AasValueSpaceComparer.IsCanonical(lexical, valueType), Is.EqualTo(expected));
        }

        [Test]
        public void AnAbsentValueIsNotCanonical()
        {
            Assert.That(
                AasValueSpaceComparer.IsCanonical(null, AASDataTypeDefXsdDataType.String),
                Is.False);
        }

        [Test]
        public void EquivalenceIsReflexiveSymmetricAndTransitive()
        {
            const AASDataTypeDefXsdDataType valueType = AASDataTypeDefXsdDataType.Decimal;
            string[] spellings = ["1.5", "1.50", "1.500000"];

            Assert.Multiple(() =>
            {
                foreach (string a in spellings)
                {
                    Assert.That(
                        AasValueSpaceComparer.AreEquivalent(a, a, valueType), Is.True);

                    foreach (string b in spellings)
                    {
                        Assert.That(
                            AasValueSpaceComparer.AreEquivalent(a, b, valueType),
                            Is.EqualTo(AasValueSpaceComparer.AreEquivalent(b, a, valueType)));
                        Assert.That(
                            AasValueSpaceComparer.AreEquivalent(a, b, valueType), Is.True);
                    }
                }
            });
        }
    }
}
