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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// The RFC 8785 (JCS) canonicalization the WoT Binding Section 9.4 conflict
    /// test compares two JSON values with.
    /// </summary>
    /// <remarks>
    /// The number cases are the ones an "almost-JCS" implementation gets wrong:
    /// a platform's own <c>double</c> formatting, its exponent thresholds and
    /// its rendering of negative zero are all different from ECMAScript's, and
    /// two implementations that differ on any of them disagree about whether
    /// two documents hold the same value.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotJsonCanonicalizerTests
    {
        [Test]
        public void ObjectMembersAreSortedByTheirUtf16CodeUnits()
        {
            Assert.That(
                Canonicalize("{\"b\":1,\"a\":2,\"A\":3,\"\\u00e4\":4,\"Z\":5}"),
                Is.EqualTo("{\"A\":3,\"Z\":5,\"a\":2,\"b\":1,\"\u00e4\":4}"),
                "RFC 8785 Section 3.2.3 orders members by their UTF-16 code units, which " +
                "puts every upper-case ASCII letter before every lower-case one and every " +
                "non-ASCII name after both.");
        }

        [Test]
        public void NestedObjectsAreSortedAtEveryLevelAndArraysKeepTheirOrder()
        {
            Assert.That(
                Canonicalize("{\"z\":{\"b\":[3,1,2],\"a\":true},\"a\":null}"),
                Is.EqualTo("{\"a\":null,\"z\":{\"a\":true,\"b\":[3,1,2]}}"),
                "Order is part of an array's value and is never sorted.");
        }

        [Test]
        public void AnObjectThatRepeatsAMemberIsDiagnosed()
        {
            Assert.That(
                WotJsonCanonicalizer.TryCanonicalize(
                    Parse("{\"a\":1,\"a\":2}"), out _, out string error),
                Is.False,
                "RFC 8785 canonicalizes a JSON value, and an object naming one member " +
                "twice is not one value.");
            Assert.That(error, Does.Contain("repeats the member"));
        }

        [Test]
        public void StringsCarryTheMinimalEscapingOfJsonStringify()
        {
            Assert.That(
                Canonicalize("{\"a\":\"<&>'\\u0041\\u00e9\\ud83d\\ude00/\"}"),
                Is.EqualTo("{\"a\":\"<&>'A\u00e9\ud83d\ude00/\"}"),
                "No HTML-sensitive character and no non-ASCII scalar is escaped: escaping " +
                "one would make two spellings of one string.");
        }

        [Test]
        public void ControlCharactersUseTheShortFormOrLowerCaseHex()
        {
            Assert.That(
                Canonicalize("{\"a\":\"\\b\\f\\n\\r\\t\\u0000\\u001f\\\"\\\\\"}"),
                Is.EqualTo("{\"a\":\"\\b\\f\\n\\r\\t\\u0000\\u001f\\\"\\\\\"}"),
                "RFC 8785 Section 3.2.2.2 uses the seven short forms and lower-case " +
                "\\u00xx for every other C0 control.");
        }

        [Test]
        public void EquivalentEscapesAreOneString()
        {
            Assert.That(
                Canonicalize("{\"a\":\"\\u0041\\u0301\"}"),
                Is.EqualTo(Canonicalize("{\"a\":\"A\u0301\"}")),
                "An escape and the character it names are one string.");
        }

        [TestCase("1", "1")]
        [TestCase("1.0", "1")]
        [TestCase("1e0", "1")]
        [TestCase("1.0e+00", "1")]
        [TestCase("-0", "0")]
        [TestCase("-0.0", "0")]
        [TestCase("0", "0")]
        [TestCase("0.1", "0.1")]
        [TestCase("1e2", "100")]
        [TestCase("100", "100")]
        [TestCase("1.5", "1.5")]
        [TestCase("-1.5", "-1.5")]
        [TestCase("1e21", "1e+21")]
        [TestCase("1e20", "100000000000000000000")]
        [TestCase("1e-6", "0.000001")]
        [TestCase("1e-7", "1e-7")]
        [TestCase("5e-324", "5e-324")]
        [TestCase("1.7976931348623157e308", "1.7976931348623157e+308")]
        [TestCase("0.000001", "0.000001")]
        [TestCase("0.0000001", "1e-7")]
        [TestCase("1.25e3", "1250")]
        [TestCase("1.0000000000000002", "1.0000000000000002")]
        public void NumbersUseTheEcmaScriptFormOfTheirDoubleValue(string literal, string expected)
        {
            Assert.That(
                WotJsonCanonicalizer.TryFormatNumber(literal, out string formatted, out string error),
                Is.True,
                error);
            Assert.That(formatted, Is.EqualTo(expected));
        }

        [TestCase("9007199254740993")]
        [TestCase("1e400")]
        [TestCase("0.1000000000000000055511151231257827")]
        [TestCase("123456789012345678000")]
        public void ANumberOutsideTheInteroperableDomainIsDiagnosed(string literal)
        {
            Assert.That(
                WotJsonCanonicalizer.TryFormatNumber(literal, out _, out string error),
                Is.False,
                "Canonicalizing a literal an IEEE-754 double cannot hold would report two " +
                "different numbers equal. RFC 8785 converts every number to a double before " +
                "printing it, so this implementation holds a literal to being the shortest " +
                "round-trip form of the double it parses to and diagnoses the rest rather " +
                "than silently rounding them together.");
            Assert.That(error, Does.Contain("interoperable domain"));
        }

        [Test]
        public void TwoSpellingsOfOneValueAreEqualAndTwoValuesAreNot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    Equal("{\"a\":1.0,\"b\":[1,2]}", "{\"b\":[1,2],\"a\":1e0}"),
                    Is.True,
                    "Member order, number spelling and exponent form are spellings, not " +
                    "values.");
                Assert.That(
                    Equal("{\"a\":[1,2]}", "{\"a\":[2,1]}"),
                    Is.False,
                    "Array order is part of the value.");
                Assert.That(
                    Equal("{\"a\":1}", "{\"a\":1.5}"),
                    Is.False);
                Assert.That(
                    Equal("{\"a\":\"1\"}", "{\"a\":1}"),
                    Is.False,
                    "A string is not the number it spells.");
            });
        }

        [Test]
        public void TheCanonicalDocumentFormIsTheCanonicalizerOutput()
        {
            using var document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(
                    "{\n  \"@context\": \"https://www.w3.org/2022/wot/td/v1.1\",\n" +
                    "  \"b\": 1.0,\n  \"a\": \"\\u00e9\"\n}"));

            Assert.That(
                Encoding.UTF8.GetString(document.ToCanonicalUtf8()),
                Is.EqualTo(
                    "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "\"a\":\"\u00e9\",\"b\":1}"),
                "The document's canonical form is the RFC 8785 one: sorted, minimally " +
                "escaped and with numbers in the ECMAScript form.");
        }

        [Test]
        public void TheCanonicalDocumentFormRefusesANonInteroperableNumber()
        {
            using var document = WotDocument.Parse(
                Encoding.UTF8.GetBytes("{\"a\":9007199254740993}"));

            Assert.That(
                () => document.ToCanonicalUtf8(),
                Throws.TypeOf<System.FormatException>(),
                "A value RFC 8785 cannot canonicalize without changing it is reported, not " +
                "quietly rounded.");
        }

        private static string Canonicalize(string json)
        {
            Assert.That(
                WotJsonCanonicalizer.TryCanonicalize(
                    Parse(json), out string canonical, out string error),
                Is.True,
                error);
            return canonical;
        }

        private static bool Equal(string left, string right)
        {
            Assert.That(
                WotJsonCanonicalizer.TryEquals(
                    JsonNode.Parse(left), JsonNode.Parse(right), out bool equal, out string error),
                Is.True,
                error);
            return equal;
        }

        private static JsonElement Parse(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
