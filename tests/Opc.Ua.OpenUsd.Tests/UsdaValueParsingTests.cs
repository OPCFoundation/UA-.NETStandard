// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
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
            UsdTestHelpers.AssertInteger(UsdaReader.ParseValue(raw), expected);
        }

        [TestCase("3.14", 3.14)]
        [TestCase("-2.5", -2.5)]
        [TestCase("1.0", 1.0)]
        [TestCase(".25", 0.25)]
        [TestCase("-2.5e-3", -0.0025)]
        [TestCase("6.02E2", 602.0)]
        public void Floats_ParseAsDouble(string raw, double expected)
        {
            UsdValue value = UsdaReader.ParseValue(raw);
            Assert.That(value.TryGetDouble(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
        }

        [TestCase("true", true)]
        [TestCase("false", false)]
        public void Booleans_ParseAsBool(string raw, bool expected)
        {
            UsdTestHelpers.AssertBoolean(UsdaReader.ParseValue(raw), expected);
        }

        [Test]
        public void QuotedString_IsUnwrapped()
        {
            UsdTestHelpers.AssertString(UsdaReader.ParseValue("\"hello world\""), "hello world");
        }

        [Test]
        public void QuotedString_WithSpecialCharacters()
        {
            UsdTestHelpers.AssertString(UsdaReader.ParseValue("\"a, (b) [c] </d>\""), "a, (b) [c] </d>");
        }

        [Test]
        public void QuotedString_WithEscapedQuotes()
        {
            UsdTestHelpers.AssertString(UsdaReader.ParseValue("\"say \\\"hi\\\"\""), "say \"hi\"");
        }

        [Test]
        public void BareToken_IsReturnedAsString()
        {
            UsdTestHelpers.AssertToken(UsdaReader.ParseValue("inherited"), "inherited");
        }

        [TestCase("@pump.usda@", "pump.usda")]
        [TestCase("@./sub/robot.usda@", "./sub/robot.usda")]
        public void AssetPath_IsUnwrapped(string raw, string expected)
        {
            UsdTestHelpers.AssertAssetPath(UsdaReader.ParseValue(raw), expected);
        }

        [Test]
        public void PathReference_IsUnwrapped()
        {
            UsdTestHelpers.AssertPathReference(
                UsdaReader.ParseValue("</Plant/Pumps/P101>"),
                "/Plant/Pumps/P101");
        }

        [Test]
        public void IntegerTuple_ParsesToObjectArray()
        {
            UsdTestHelpers.AssertIntegerItems(UsdaReader.ParseValue("(0, 0, 0)"), 0L, 0L, 0L);
            UsdTestHelpers.AssertIntegerItems(UsdaReader.ParseValue("(-45, 0, 35)"), -45L, 0L, 35L);
        }

        [Test]
        public void FloatTuple_ParsesToObjectArray()
        {
            UsdTestHelpers.AssertDoubleItems(UsdaReader.ParseValue("(0.1, 0.1, 0.1)"), 0.1, 0.1, 0.1);
        }

        [Test]
        public void IntegerArray_ParsesToList()
        {
            UsdTestHelpers.AssertIntegerItems(UsdaReader.ParseValue("[1, 2, 3]"), 1L, 2L, 3L);
        }

        [Test]
        public void NestedTupleArray_ParsesToListOfArray()
        {
            UsdValue value = UsdaReader.ParseValue("[(0, 0, 1)]");
            UsdTestHelpers.AssertNestedIntegerItems(value, s_longValues1);
        }

        [Test]
        public void StringArray_ParsesToList()
        {
            Assert.That(
                UsdaReader.ParseValue("[\"xformOp:translate\", \"xformOp:rotateZ\"]"),
                Is.EqualTo(UsdTestHelpers.StringArray("xformOp:translate", "xformOp:rotateZ")));
        }

        [Test]
        public void MixedFloatTuple_HandlesLeadingDotAndExponent()
        {
            UsdValue value = UsdaReader.ParseValue("(0.5, -1.5e2, .25)");
            Assert.That(value.TryGetTuple(out ArrayOf<UsdValue> tuple), Is.True);
            Assert.That(tuple, Has.Count.EqualTo(3));
            Assert.That(tuple[0].TryGetDouble(out double first), Is.True);
            Assert.That(tuple[1].TryGetDouble(out double second), Is.True);
            Assert.That(tuple[2].TryGetDouble(out double third), Is.True);
            Assert.That(first, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(second, Is.EqualTo(-150.0).Within(1e-12));
            Assert.That(third, Is.EqualTo(0.25).Within(1e-12));
        }

        [TestCase("")]
        [TestCase("   ")]
        public void EmptyOrWhitespace_ParsesToNull(string raw)
        {
            Assert.That(UsdaReader.ParseValue(raw).IsNull, Is.True);
        }

        [Test]
        public void Null_ParsesToNull()
        {
            Assert.That(UsdaReader.ParseValue(null).IsNull, Is.True);
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

        private static readonly long[] s_longValues1 = [0L, 0L, 1L];
    }
}
