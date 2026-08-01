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
using Opc.Ua.OpenUsdScene.Conversion;

namespace Opc.Ua.OpenUsdScene.Tests
{
    [TestFixture]
    public class UsdaValueParsingTests
    {
        [TestCase("42", 42L)]
        [TestCase("-5", -5L)]
        [TestCase("+7", 7L)]
        [TestCase("0", 0L)]
        public void Integers_ParseAsLong(string raw, long expected)
        {
            Assert.That(UsdaReader.ParseValue(raw), Is.EqualTo(expected));
        }

        [TestCase("3.14", 3.14)]
        [TestCase("-2.5", -2.5)]
        [TestCase("1.0", 1.0)]
        [TestCase(".25", 0.25)]
        [TestCase("-2.5e-3", -0.0025)]
        [TestCase("6.02E2", 602.0)]
        public void Floats_ParseAsDouble(string raw, double expected)
        {
            object? value = UsdaReader.ParseValue(raw);
            Assert.That(value, Is.TypeOf<double>());
            Assert.That((double)value!, Is.EqualTo(expected).Within(1e-12));
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        public void Booleans_ParseAsBool(string raw, bool expected)
        {
            Assert.That(UsdaReader.ParseValue(raw), Is.EqualTo(expected));
        }

        [Test]
        public void QuotedString_IsUnwrapped()
        {
            Assert.That(UsdaReader.ParseValue("\"hello world\""), Is.EqualTo("hello world"));
        }

        [Test]
        public void QuotedString_WithSpecialCharacters()
        {
            Assert.That(UsdaReader.ParseValue("\"a, (b) [c] </d>\""), Is.EqualTo("a, (b) [c] </d>"));
        }

        [Test]
        public void QuotedString_WithEscapedQuotes()
        {
            Assert.That(UsdaReader.ParseValue("\"say \\\"hi\\\"\""), Is.EqualTo("say \"hi\""));
        }

        [Test]
        public void BareToken_IsReturnedAsString()
        {
            Assert.That(UsdaReader.ParseValue("inherited"), Is.EqualTo("inherited"));
        }

        [TestCase("@pump.usda@", "pump.usda")]
        [TestCase("@./sub/robot.usda@", "./sub/robot.usda")]
        public void AssetPath_IsUnwrapped(string raw, string expected)
        {
            Assert.That(UsdaReader.ParseValue(raw), Is.EqualTo(expected));
        }

        [Test]
        public void PathReference_IsUnwrapped()
        {
            Assert.That(UsdaReader.ParseValue("</Plant/Pumps/P101>"), Is.EqualTo("/Plant/Pumps/P101"));
        }

        [Test]
        public void IntegerTuple_ParsesToObjectArray()
        {
            Assert.That(UsdaReader.ParseValue("(0, 0, 0)"), Is.EqualTo(new object?[] { 0L, 0L, 0L }));
            Assert.That(UsdaReader.ParseValue("(-45, 0, 35)"), Is.EqualTo(new object?[] { -45L, 0L, 35L }));
        }

        [Test]
        public void FloatTuple_ParsesToObjectArray()
        {
            Assert.That(UsdaReader.ParseValue("(0.1, 0.1, 0.1)"), Is.EqualTo(new object?[] { 0.1, 0.1, 0.1 }));
        }

        [Test]
        public void IntegerArray_ParsesToList()
        {
            Assert.That(UsdaReader.ParseValue("[1, 2, 3]"), Is.EqualTo(new List<object?> { 1L, 2L, 3L }));
        }

        [Test]
        public void NestedTupleArray_ParsesToListOfArray()
        {
            object? value = UsdaReader.ParseValue("[(0, 0, 1)]");
            var list = value as List<object?>;
            Assert.That(list, Is.Not.Null);
            Assert.That(list!, Has.Count.EqualTo(1));
            Assert.That(list[0] as object?[], Is.EqualTo(new object?[] { 0L, 0L, 1L }));
        }

        [Test]
        public void StringArray_ParsesToList()
        {
            Assert.That(
                UsdaReader.ParseValue("[\"xformOp:translate\", \"xformOp:rotateZ\"]"),
                Is.EqualTo(new List<object?> { "xformOp:translate", "xformOp:rotateZ" }));
        }

        [Test]
        public void MixedFloatTuple_HandlesLeadingDotAndExponent()
        {
            object? value = UsdaReader.ParseValue("(0.5, -1.5e2, .25)");
            var tuple = value as object?[];
            Assert.That(tuple, Is.Not.Null);
            Assert.That(tuple!, Has.Length.EqualTo(3));
            Assert.That((double)tuple[0]!, Is.EqualTo(0.5).Within(1e-12));
            Assert.That((double)tuple[1]!, Is.EqualTo(-150.0).Within(1e-12));
            Assert.That((double)tuple[2]!, Is.EqualTo(0.25).Within(1e-12));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void EmptyOrWhitespace_ParsesToNull(string raw)
        {
            Assert.That(UsdaReader.ParseValue(raw), Is.Null);
        }

        [Test]
        public void Null_ParsesToNull()
        {
            Assert.That(UsdaReader.ParseValue(null), Is.Null);
        }

        [Test]
        public void StripComments_RemovesHashCommentsButKeepsQuotedHash()
        {
            string input = string.Join("\n",
                "def Xform \"A\"  # trailing comment",
                "{",
                "    token label = \"value # not a comment\"",
                "}");
            string stripped = UsdaReader.StripComments(input);

            Assert.That(stripped, Does.Not.Contain("trailing comment"));
            Assert.That(stripped, Does.Contain("value # not a comment"));
        }
    }
}
