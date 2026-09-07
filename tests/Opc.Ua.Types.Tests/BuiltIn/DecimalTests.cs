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
using System.Numerics;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests the OPC UA <c>Decimal</c> DataType of OPC 10000-6 clause 5.1.10:
    /// its scale semantics, its two's complement little-endian octets, the
    /// xsd lexical space and the XSD 1.1 canonical form.
    /// </summary>
    [TestFixture]
    [Category("BuiltIn")]
    public class DecimalTests
    {
        [Test]
        public void ScaleIsTheInversePowerOfTen()
        {
            // Clause 5.1.10: the number represented is UnscaledValue x 10^-Scale,
            // so 150 with a scale of 2 is 1.50 rather than 15000.
            var value = new Opc.Ua.Decimal(new BigInteger(150), 2);

            Assert.That(value.ToString(), Is.EqualTo("1.50"));
        }

        [TestCase("0", 0, "0")]
        [TestCase("1", 0, "1")]
        [TestCase("-1", 0, "-1")]
        [TestCase("1.5", 1, "15")]
        [TestCase("1.500000", 6, "1500000")]
        [TestCase("+42", 0, "42")]
        [TestCase("-0.001", 3, "-1")]
        [TestCase("0.5", 1, "5")]
        [TestCase(".5", 1, "5")]
        [TestCase("5.", 0, "5")]
        public void ParseRetainsTheAuthoredScale(string lexical, int scale, string unscaled)
        {
            Opc.Ua.Decimal value = Opc.Ua.Decimal.Parse(lexical);

            Assert.Multiple(() =>
            {
                Assert.That(value.Scale, Is.EqualTo((short)scale));
                Assert.That(
                    value.UnscaledValue,
                    Is.EqualTo(BigInteger.Parse(unscaled, System.Globalization.CultureInfo.InvariantCulture)));
            });
        }

        [TestCase("1.500000")]
        [TestCase("-1.500000")]
        [TestCase("0.0000")]
        [TestCase("123456789012345678901234567890.123456789")]
        [TestCase("-99999999999999999999999999999999")]
        public void ParseAndFormatPreserveTheAuthoredForm(string lexical)
        {
            Assert.That(Opc.Ua.Decimal.Parse(lexical).ToString(), Is.EqualTo(lexical));
        }

        [Test]
        public void AnArbitraryPrecisionValueSurvivesThatNoFixedWidthTypeCouldHold()
        {
            // This is the reason the type exists: neither long nor
            // System.Decimal can carry every xs:decimal.
            const string lexical =
                "123456789012345678901234567890123456789012345678901234567890.0987654321";

            Assert.That(Opc.Ua.Decimal.Parse(lexical).ToString(), Is.EqualTo(lexical));
        }

        [TestCase("1.500000", "1.5")]
        [TestCase("1.5", "1.5")]
        [TestCase("1.0", "1")]
        [TestCase("1", "1")]
        [TestCase("+42", "42")]
        [TestCase("0.0000", "0")]
        [TestCase("-0.0", "0")]
        [TestCase("-1.2300", "-1.23")]
        [TestCase("100.00", "100")]
        public void CanonicalizeProducesTheXsd11Form(string lexical, string expected)
        {
            // XSD 1.1's decimalCanonicalMap emits no fractional part for an
            // integral value, so "1.0" canonicalizes to "1" rather than "1.0".
            Assert.That(Opc.Ua.Decimal.Parse(lexical).ToString("C", null), Is.EqualTo(expected));
        }

        [Test]
        public void CanonicalizeIsIdempotent()
        {
            Opc.Ua.Decimal once = Opc.Ua.Decimal.Parse("1.500000").Canonicalize();
            Opc.Ua.Decimal twice = once.Canonicalize();

            Assert.Multiple(() =>
            {
                Assert.That(twice.Scale, Is.EqualTo(once.Scale));
                Assert.That(twice.UnscaledValue, Is.EqualTo(once.UnscaledValue));
            });
        }

        [Test]
        public void ANegativeScaleIsSpelledOutBecauseTheLexicalSpaceHasNoExponent()
        {
            var value = new Opc.Ua.Decimal(new BigInteger(15), -2);

            Assert.Multiple(() =>
            {
                Assert.That(value.ToString(), Is.EqualTo("1500"));
                Assert.That(value.Canonicalize().Scale, Is.Zero);
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" 1")]
        [TestCase("1 ")]
        [TestCase("1.2.3")]
        [TestCase("1e5")]
        [TestCase("abc")]
        [TestCase("+")]
        [TestCase("-")]
        [TestCase(".")]
        [TestCase("--1")]
        public void AMalformedLexicalFormIsRejected(string lexical)
        {
            Assert.That(Opc.Ua.Decimal.TryParse(lexical, out _), Is.False);
        }

        [Test]
        public void ParseThrowsOnAMalformedLexicalForm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => Opc.Ua.Decimal.Parse("1e5"), Throws.TypeOf<FormatException>());
                Assert.That(() => Opc.Ua.Decimal.Parse(null!), Throws.ArgumentNullException);
            });
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("-1")]
        [TestCase("127")]
        [TestCase("128")]
        [TestCase("-128")]
        [TestCase("-129")]
        [TestCase("255")]
        [TestCase("256")]
        [TestCase("1.500000")]
        [TestCase("-123456789012345678901234567890.0001")]
        public void TheLittleEndianOctetsRoundTrip(string lexical)
        {
            Opc.Ua.Decimal value = Opc.Ua.Decimal.Parse(lexical);

            byte[] octets = value.ToLittleEndian();
            Opc.Ua.Decimal restored = Opc.Ua.Decimal.FromLittleEndian(value.Scale, octets);

            Assert.Multiple(() =>
            {
                Assert.That(restored.Scale, Is.EqualTo(value.Scale));
                Assert.That(restored.UnscaledValue, Is.EqualTo(value.UnscaledValue));
                Assert.That(restored.ToString(), Is.EqualTo(lexical));
            });
        }

        [Test]
        public void TheOctetsAreLeastSignificantByteFirst()
        {
            // Clause 5.1.10: "The integer is encoded with the least
            // significant byte first."
            var value = new Opc.Ua.Decimal(new BigInteger(0x0102), 0);

            Assert.That(value.ToLittleEndian(), Is.EqualTo(new byte[] { 0x02, 0x01 }));
        }

        [Test]
        public void TheOctetsAreTwosComplement()
        {
            var value = new Opc.Ua.Decimal(BigInteger.MinusOne, 0);

            Assert.That(value.ToLittleEndian(), Is.EqualTo(new byte[] { 0xFF }));
        }

        [Test]
        public void EmptyOctetsAreTheValueZero()
        {
            Opc.Ua.Decimal value = Opc.Ua.Decimal.FromLittleEndian(3, ReadOnlySpan<byte>.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(value.IsZero, Is.True);
                Assert.That(value.Scale, Is.EqualTo((short)3));
            });
        }

        [Test]
        public void EqualityIsOnTheNumberNotTheSpelling()
        {
            Opc.Ua.Decimal scaled = Opc.Ua.Decimal.Parse("1.50");
            Opc.Ua.Decimal terse = Opc.Ua.Decimal.Parse("1.5");
            Opc.Ua.Decimal different = Opc.Ua.Decimal.Parse("1.55");

            bool equalOperator = scaled == terse;
            bool notEqualOperator = scaled != different;

            Assert.Multiple(() =>
            {
                Assert.That(scaled, Is.EqualTo(terse));
                Assert.That(equalOperator, Is.True);
                Assert.That(notEqualOperator, Is.True);
                Assert.That(scaled.GetHashCode(), Is.EqualTo(terse.GetHashCode()));
                Assert.That(scaled, Is.Not.EqualTo(different));
            });
        }

        [Test]
        public void ZeroAtAnyScaleIsZero()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Opc.Ua.Decimal.Parse("0.000"), Is.EqualTo(Opc.Ua.Decimal.Zero));
                Assert.That(Opc.Ua.Decimal.Parse("-0.0").IsZero, Is.True);
                Assert.That(Opc.Ua.Decimal.Zero.Sign, Is.Zero);
            });
        }

        [TestCase("1", 1)]
        [TestCase("-1", -1)]
        [TestCase("-0.001", -1)]
        public void SignReportsTheSignOfTheNumber(string lexical, int expected)
        {
            Assert.That(Opc.Ua.Decimal.Parse(lexical).Sign, Is.EqualTo(expected));
        }

        [Test]
        public void SignOfZeroIsZero()
        {
            Assert.That(Opc.Ua.Decimal.Parse("0").Sign, Is.Zero);
        }

        [Test]
        public void AnUnsupportedFormatIsRejected()
        {
            Assert.That(
                () => Opc.Ua.Decimal.Parse("1").ToString("X", null),
                Throws.TypeOf<FormatException>());
        }
    }
}
