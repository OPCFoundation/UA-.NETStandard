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
    /// Tests the clause 6.1.2 canonical value representation: a value
    /// materializes into the DataType of clause 6.3.1 and serializes back as
    /// the XSD 1.1 canonical lexical representation of its declared type.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasLexicalCanonicalizerTests
    {
        [Test]
        public void TheSpecificationsDecimalExampleCanonicalizes()
        {
            // "A Property authored as "1.500000" with ValueType xs:decimal
            // therefore serializes as "1.5"".
            Assert.That(Canonicalize("1.500000", AASDataTypeDefXsdDataType.Decimal),
                Is.EqualTo("1.5"));
        }

        [Test]
        public void TheSpecificationsIntExampleCanonicalizes()
        {
            // "and one authored "+42" with xs:int serializes as "42"".
            Assert.That(Canonicalize("+42", AASDataTypeDefXsdDataType.Int),
                Is.EqualTo("42"));
        }

        [Test]
        public void TheSpecificationsBooleanExampleCanonicalizes()
        {
            // "so "1" declared xs:boolean returns as "true"".
            Assert.That(Canonicalize("1", AASDataTypeDefXsdDataType.Boolean),
                Is.EqualTo("true"));
        }

        [Test]
        public void AnIntegralDecimalKeepsNoFractionalPart()
        {
            // The XSD 1.1 decimalCanonicalMap omits the fractional part of an
            // integral value. Under XSD 1.0 this would have been "1.0", and
            // picking the wrong version disagrees on every integral decimal.
            Assert.Multiple(() =>
            {
                Assert.That(Canonicalize("1.0", AASDataTypeDefXsdDataType.Decimal),
                    Is.EqualTo("1"));
                Assert.That(Canonicalize("100.00", AASDataTypeDefXsdDataType.Decimal),
                    Is.EqualTo("100"));
                Assert.That(Canonicalize("0.0000", AASDataTypeDefXsdDataType.Decimal),
                    Is.EqualTo("0"));
            });
        }

        [TestCase("true", "true")]
        [TestCase("false", "false")]
        [TestCase("1", "true")]
        [TestCase("0", "false")]
        public void BooleanCanonicalizesToTheWordForm(string lexical, string expected)
        {
            Assert.That(Canonicalize(lexical, AASDataTypeDefXsdDataType.Boolean),
                Is.EqualTo(expected));
        }

        [TestCase(AASDataTypeDefXsdDataType.Byte, "+7", "7")]
        [TestCase(AASDataTypeDefXsdDataType.Byte, "-0", "0")]
        [TestCase(AASDataTypeDefXsdDataType.Short, "0007", "7")]
        [TestCase(AASDataTypeDefXsdDataType.Int, "-0042", "-42")]
        [TestCase(AASDataTypeDefXsdDataType.Long, "+9223372036854775807", "9223372036854775807")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedByte, "007", "7")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedShort, "+65535", "65535")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedInt, "0", "0")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedLong, "018446744073709551615", "18446744073709551615")]
        [TestCase(AASDataTypeDefXsdDataType.Integer, "+1", "1")]
        [TestCase(AASDataTypeDefXsdDataType.NonNegativeInteger, "0", "0")]
        [TestCase(AASDataTypeDefXsdDataType.PositiveInteger, "+1", "1")]
        [TestCase(AASDataTypeDefXsdDataType.NonPositiveInteger, "-0", "0")]
        [TestCase(AASDataTypeDefXsdDataType.NegativeInteger, "-01", "-1")]
        public void AnIntegerLosesItsSignAndLeadingZeroes(
            AASDataTypeDefXsdDataType valueType,
            string lexical,
            string expected)
        {
            Assert.That(Canonicalize(lexical, valueType), Is.EqualTo(expected));
        }

        [TestCase(AASDataTypeDefXsdDataType.Byte, "128")]
        [TestCase(AASDataTypeDefXsdDataType.Byte, "-129")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedByte, "256")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedByte, "-1")]
        [TestCase(AASDataTypeDefXsdDataType.Short, "32768")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedShort, "65536")]
        [TestCase(AASDataTypeDefXsdDataType.Int, "2147483648")]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedInt, "4294967296")]
        [TestCase(AASDataTypeDefXsdDataType.PositiveInteger, "0")]
        [TestCase(AASDataTypeDefXsdDataType.NegativeInteger, "0")]
        [TestCase(AASDataTypeDefXsdDataType.NonPositiveInteger, "1")]
        [TestCase(AASDataTypeDefXsdDataType.NonNegativeInteger, "-1")]
        public void AValueOutsideTheRangeIsRejectedRatherThanTruncated(
            AASDataTypeDefXsdDataType valueType,
            string lexical)
        {
            // Clause 6.3.3: a value outside the representable range shall be
            // rejected rather than truncated. Truncation would silently change
            // the number a passport reports.
            Assert.That(
                AasLexicalCanonicalizer.TryParse(lexical, valueType, out _, out string? error),
                Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void XsIntegerIsBoundedByTheAbstractUnionsRange()
        {
            // xs:integer is unbounded, but Integer is the abstract union of the
            // concrete signed types, so Int64 is the representable range and
            // clause 6.3.1 says so explicitly.
            Assert.Multiple(() =>
            {
                Assert.That(Canonicalize("9223372036854775807", AASDataTypeDefXsdDataType.Integer),
                    Is.EqualTo("9223372036854775807"));
                Assert.That(
                    AasLexicalCanonicalizer.TryParse(
                        "9223372036854775808", AASDataTypeDefXsdDataType.Integer, out _, out _),
                    Is.False);
            });
        }

        [Test]
        public void ADateTimeBefore1601IsRejected()
        {
            // OPC UA DateTime begins in 1601 while xs:dateTime does not.
            Assert.Multiple(() =>
            {
                Assert.That(
                    AasLexicalCanonicalizer.TryParse(
                        "1500-01-01T00:00:00Z", AASDataTypeDefXsdDataType.DateTime, out _, out _),
                    Is.False);
                Assert.That(
                    AasLexicalCanonicalizer.TryParse(
                        "2026-08-11T06:39:30Z", AASDataTypeDefXsdDataType.DateTime, out _, out _),
                    Is.True);
            });
        }

        [TestCase("INF")]
        [TestCase("-INF")]
        [TestCase("NaN")]
        public void TheSpecialDoubleValuesUseTheirXsdSpellings(string lexical)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Canonicalize(lexical, AASDataTypeDefXsdDataType.Double),
                    Is.EqualTo(lexical));
                Assert.That(Canonicalize(lexical, AASDataTypeDefXsdDataType.Float),
                    Is.EqualTo(lexical));
            });
        }

        [TestCase("Infinity")]
        [TestCase("inf")]
        [TestCase("nan")]
        public void AForeignSpellingOfASpecialValueIsRejected(string lexical)
        {
            // XML Schema is case sensitive about these and does not accept the
            // .NET spellings.
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    lexical, AASDataTypeDefXsdDataType.Double, out _, out _),
                Is.False);
        }

        [Test]
        public void HexBinaryCanonicalizesToUppercase()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Canonicalize("0a1b", AASDataTypeDefXsdDataType.HexBinary),
                    Is.EqualTo("0A1B"));
                Assert.That(Canonicalize("0A1B", AASDataTypeDefXsdDataType.HexBinary),
                    Is.EqualTo("0A1B"));
            });
        }

        [TestCase("0A1")]
        [TestCase("zz")]
        public void AMalformedHexBinaryIsRejected(string lexical)
        {
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    lexical, AASDataTypeDefXsdDataType.HexBinary, out _, out _),
                Is.False);
        }

        [Test]
        public void HexBinaryAndBase64BinaryCarryTheSameOctets()
        {
            // Clause 6.3.1: "the octets are identical to a xs:base64Binary
            // value's and only the written form differs". That is exactly why
            // hexBinary needs its own DataType.
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    "0A1B", AASDataTypeDefXsdDataType.HexBinary, out Variant hex, out _),
                Is.True);
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    "Chs=", AASDataTypeDefXsdDataType.Base64Binary, out Variant base64, out _),
                Is.True);

            Assert.That(hex.TryGetValue(out ByteString hexOctets), Is.True);
            Assert.That(base64.TryGetValue(out ByteString base64Octets), Is.True);
            Assert.That(hexOctets.Span.ToArray(), Is.EqualTo(base64Octets.Span.ToArray()));
        }

        [TestCase(AASDataTypeDefXsdDataType.String, "  spaced  ")]
        [TestCase(AASDataTypeDefXsdDataType.AnyUri, "https://example.com/a?b=c#d")]
        [TestCase(AASDataTypeDefXsdDataType.Date, "2026-08-11")]
        [TestCase(AASDataTypeDefXsdDataType.Time, "06:39:30Z")]
        [TestCase(AASDataTypeDefXsdDataType.Duration, "P1M")]
        [TestCase(AASDataTypeDefXsdDataType.GYear, "2026")]
        [TestCase(AASDataTypeDefXsdDataType.GYearMonth, "2026-08")]
        [TestCase(AASDataTypeDefXsdDataType.GMonth, "--08")]
        [TestCase(AASDataTypeDefXsdDataType.GMonthDay, "--08-11")]
        [TestCase(AASDataTypeDefXsdDataType.GDay, "---11")]
        public void AStringCarriedTypePassesThroughUnchanged(
            AASDataTypeDefXsdDataType valueType,
            string lexical)
        {
            // Their value space is their lexical space, so there is nothing to
            // normalize. A duration in particular must not be reduced to
            // milliseconds: P1M is not thirty days.
            Assert.That(Canonicalize(lexical, valueType), Is.EqualTo(lexical));
        }

        [TestCase(AASDataTypeDefXsdDataType.Boolean, "yes")]
        [TestCase(AASDataTypeDefXsdDataType.Int, "4.2")]
        [TestCase(AASDataTypeDefXsdDataType.Int, "")]
        [TestCase(AASDataTypeDefXsdDataType.Int, "1e3")]
        [TestCase(AASDataTypeDefXsdDataType.Decimal, "1e3")]
        [TestCase(AASDataTypeDefXsdDataType.Decimal, "abc")]
        [TestCase(AASDataTypeDefXsdDataType.Base64Binary, "not base64!")]
        [TestCase(AASDataTypeDefXsdDataType.DateTime, "not a date")]
        public void AMalformedLexicalFormIsRejectedWithAReason(
            AASDataTypeDefXsdDataType valueType,
            string lexical)
        {
            Assert.That(
                AasLexicalCanonicalizer.TryParse(lexical, valueType, out _, out string? error),
                Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void ANullLexicalFormIsRejected()
        {
            Assert.That(
                AasLexicalCanonicalizer.TryParse(
                    null, AASDataTypeDefXsdDataType.String, out _, out string? error),
                Is.False);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void CanonicalizationIsIdempotent()
        {
            // Clause 6.4's negative control asserts that rewriting a value into
            // its canonical form is not reported as a difference, which only
            // holds if canonicalizing twice changes nothing.
            foreach (AASDataTypeDefXsdDataType valueType in AasXsdTypeMap.ValueTypes)
            {
                string? sample = SampleFor(valueType);
                if (sample is null)
                {
                    continue;
                }

                string? once = Canonicalize(sample, valueType);
                string? twice = Canonicalize(once!, valueType);

                Assert.That(twice, Is.EqualTo(once), $"{valueType} is not idempotent.");
            }
        }

        private static string? Canonicalize(string lexical, AASDataTypeDefXsdDataType valueType)
        {
            Assert.That(
                AasLexicalCanonicalizer.TryCanonicalizeLexical(
                    lexical, valueType, out string? canonical, out string? error),
                Is.True,
                error);
            return canonical;
        }

        private static string? SampleFor(AASDataTypeDefXsdDataType valueType)
        {
            return valueType switch
            {
                AASDataTypeDefXsdDataType.Boolean => "1",
                AASDataTypeDefXsdDataType.Byte or AASDataTypeDefXsdDataType.Short or
                AASDataTypeDefXsdDataType.Int or AASDataTypeDefXsdDataType.Long or
                AASDataTypeDefXsdDataType.Integer or
                AASDataTypeDefXsdDataType.NonPositiveInteger or
                AASDataTypeDefXsdDataType.NegativeInteger => "-01",
                AASDataTypeDefXsdDataType.UnsignedByte or AASDataTypeDefXsdDataType.UnsignedShort or
                AASDataTypeDefXsdDataType.UnsignedInt or AASDataTypeDefXsdDataType.UnsignedLong or
                AASDataTypeDefXsdDataType.NonNegativeInteger or
                AASDataTypeDefXsdDataType.PositiveInteger => "007",
                AASDataTypeDefXsdDataType.Float or AASDataTypeDefXsdDataType.Double => "1.5",
                AASDataTypeDefXsdDataType.Decimal => "1.500000",
                AASDataTypeDefXsdDataType.DateTime => "2026-08-11T06:39:30Z",
                AASDataTypeDefXsdDataType.Base64Binary => "Chs=",
                AASDataTypeDefXsdDataType.HexBinary => "0a1b",
                AASDataTypeDefXsdDataType.String => "text",
                AASDataTypeDefXsdDataType.AnyUri => "https://example.com/",
                AASDataTypeDefXsdDataType.Date => "2026-08-11",
                AASDataTypeDefXsdDataType.Time => "06:39:30Z",
                AASDataTypeDefXsdDataType.Duration => "P1M",
                AASDataTypeDefXsdDataType.GYear => "2026",
                AASDataTypeDefXsdDataType.GYearMonth => "2026-08",
                AASDataTypeDefXsdDataType.GMonth => "--08",
                AASDataTypeDefXsdDataType.GMonthDay => "--08-11",
                AASDataTypeDefXsdDataType.GDay => "---11",
                _ => null
            };
        }
    }
}
