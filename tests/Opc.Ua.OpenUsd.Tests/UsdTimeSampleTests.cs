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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Tests for USD time-sample support in the conversion layer (spec §7.1 step 3, §7.2, §9):
    /// the reader parses a <c>.timeSamples = { … }</c> block into an ordered map kept separate
    /// from the authored default, the writer re-emits it, and the §7.4 signature preserves it.
    /// </summary>
    [TestFixture]
    public class UsdTimeSampleTests
    {
        private static string Wrap(params string[] bodyLines)
        {
            var lines = new List<string>
            {
                "#usda 1.0",
                "(",
                "    defaultPrim = \"P\"",
                "    metersPerUnit = 1.0",
                "    upAxis = \"Z\"",
                ")",
                string.Empty,
                "def Xform \"P\"",
                "{",
            };
            lines.AddRange(bodyLines);
            lines.Add("}");
            lines.Add(string.Empty);
            return string.Join("\n", lines);
        }

        private static UsdAttribute ParseSingleAttribute(string stageName, params string[] bodyLines)
        {
            UsdStage stage = UsdaReader.Parse(Wrap(bodyLines), stageName);
            UsdPrim prim = stage.Find("/P")!;
            return prim.Attributes[0];
        }

        private static void AssertRoundTripSignature(UsdStage expected)
        {
            string written = UsdaWriter.Write(expected);
            UsdStage reparsed = UsdaReader.Parse(written, expected.StageName);

            string? difference = UsdSceneSignature.FirstDifference(expected, reparsed);
            Assert.That(
                UsdSceneSignature.Compute(reparsed),
                Is.EqualTo(UsdSceneSignature.Compute(expected)),
                difference ?? "signatures are unexpectedly equal");
        }

        // ---- parsing ------------------------------------------------------------------

        [Test]
        public void MultiLineBlock_ParsesScalarSamples_SeparateFromDefault()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    double xformOp:rotateZ.timeSamples = {",
                "        0: 0.0,",
                "        24: 90.0,",
                "        48: 180.0,",
                "    }");

            Assert.That(attr.Value.IsNull, Is.True, "no authored default was declared");
            Assert.That(attr.TimeSamples.Keys, Is.EqualTo(s_doubleValues1));
            Assert.That(attr.TimeSamples.Values, Is.EqualTo(new[]
            {
                UsdValue.From(0.0),
                UsdValue.From(90.0),
                UsdValue.From(180.0)
            }));
        }

        [Test]
        public void SingleLineBlock_ParsesSamples()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    double a.timeSamples = { 0: 0.0, 24: 90.0 }");

            Assert.That(attr.TimeSamples.Keys, Is.EqualTo(s_doubleValues2));
            Assert.That(attr.TimeSamples.Values, Is.EqualTo(new[] { UsdValue.From(0.0), UsdValue.From(90.0) }));
        }

        [Test]
        public void NegativeAndFractionalTimeCodes_AreParsed()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    double a.timeSamples = {",
                "        -12: -1.0,",
                "        0.5: 5.0,",
                "        2.25: 7.5,",
                "    }");

            Assert.That(attr.TimeSamples.Keys, Is.EqualTo(new[] { -12.0, 0.5, 2.25 }));
            UsdTestHelpers.AssertDouble(attr.TimeSamples[-12.0], -1.0);
            UsdTestHelpers.AssertDouble(attr.TimeSamples[0.5], 5.0);
        }

        [Test]
        public void SamplesAreOrderedByTimeCode_EvenWhenAuthoredOutOfOrder()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    double a.timeSamples = {",
                "        48: 180.0,",
                "        0: 0.0,",
                "        24: 90.0,",
                "    }");

            Assert.That(attr.TimeSamples.Keys, Is.EqualTo(s_doubleValues1));
        }

        [Test]
        public void TupleValuedSamples_AreParsed()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    float3 xformOp:translate.timeSamples = {",
                "        0: (0, 0, 0),",
                "        24: (1, 2, 3),",
                "    }");

            UsdTestHelpers.AssertIntegerItems(attr.TimeSamples[0.0], 0L, 0L, 0L);
            UsdTestHelpers.AssertIntegerItems(attr.TimeSamples[24.0], 1L, 2L, 3L);
        }

        [Test]
        public void ArrayValuedSamples_AreParsed()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    int[] a.timeSamples = {",
                "        0: [1, 2, 3],",
                "        24: [4, 5, 6],",
                "    }");

            UsdTestHelpers.AssertIntegerItems(attr.TimeSamples[0.0], 1L, 2L, 3L);
            UsdTestHelpers.AssertIntegerItems(attr.TimeSamples[24.0], 4L, 5L, 6L);
        }

        [Test]
        public void AssetValuedSamples_AreUnwrapped()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    asset a.timeSamples = {",
                "        0: @./a.usda@,",
                "        24: @./b.usda@,",
                "    }");

            UsdTestHelpers.AssertAssetPath(attr.TimeSamples[0.0], "./a.usda");
            UsdTestHelpers.AssertAssetPath(attr.TimeSamples[24.0], "./b.usda");
        }

        [Test]
        public void TokenValuedSamples_AreUnwrapped()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    token door:state.timeSamples = {",
                "        0: \"open\",",
                "        24: \"closed\",",
                "    }");

            UsdTestHelpers.AssertString(attr.TimeSamples[0.0], "open");
            UsdTestHelpers.AssertString(attr.TimeSamples[24.0], "closed");
        }

        [Test]
        public void DefaultAndSamples_CoexistOnOneAttribute()
        {
            UsdAttribute attr = ParseSingleAttribute(
                "TS",
                "    double a = 5.0",
                "    double a.timeSamples = {",
                "        0: 0.0,",
                "        24: 90.0,",
                "    }");

            UsdTestHelpers.AssertDouble(attr.Value, 5.0);
            Assert.That(attr.TimeSamples.Keys, Is.EqualTo(s_doubleValues2));
        }

        [Test]
        public void SamplesOnlyAttribute_HasNoBareDefaultDeclaration()
        {
            // An attribute declared only through its .timeSamples must not also gain a spurious
            // valueless declaration that would re-parse to a second attribute.
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    double a.timeSamples = {",
                    "        0: 0.0,",
                    "    }"),
                "TS");

            UsdPrim prim = stage.Find("/P")!;
            Assert.That(prim.Attributes.Count(a => a.Name == "a"), Is.EqualTo(1));
        }

        // ---- round-trip ---------------------------------------------------------------

        [Test]
        public void ScalarSamples_RoundTripThroughWriteAndReparse()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    double xformOp:rotateZ.timeSamples = {",
                    "        0: 0.0,",
                    "        24: 90.0,",
                    "        48: 180.0,",
                    "    }"),
                "TS");

            AssertRoundTripSignature(stage);
        }

        [Test]
        public void DefaultPlusSamples_RoundTripThroughWriteAndReparse()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    double a = 5.0",
                    "    double a.timeSamples = {",
                    "        -6: -1.0,",
                    "        0.5: 0.0,",
                    "        24: 90.0,",
                    "    }"),
                "TS");

            AssertRoundTripSignature(stage);
        }

        [TestCase("float3 xformOp:translate", "(0, 0, 0)", "(1, 2, 3)")]
        [TestCase("int[] a", "[1, 2, 3]", "[4, 5, 6]")]
        [TestCase("asset a", "@./a.usda@", "@./b.usda@")]
        [TestCase("token a", "\"open\"", "\"closed\"")]
        public void TypedSamples_RoundTripThroughWriteAndReparse(string decl, string v0, string v24)
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    " + decl + ".timeSamples = {",
                    "        0: " + v0 + ",",
                    "        24: " + v24 + ",",
                    "    }"),
                "TS");

            AssertRoundTripSignature(stage);
        }

        [Test]
        public void WriterOutputIsAFixedPoint_ForTimeSamples()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    double a = 5.0",
                    "    double a.timeSamples = {",
                    "        0: 0.0,",
                    "        24: 90.0,",
                    "    }"),
                "TS");

            string firstWrite = UsdaWriter.Write(stage);
            string secondWrite = UsdaWriter.Write(UsdaReader.Parse(firstWrite, stage.StageName));

            Assert.That(secondWrite, Is.EqualTo(firstWrite));
        }

        [Test]
        public void WriterEmitsTimeCodesWithoutRedundantDecimalPoint()
        {
            UsdStage stage = UsdaReader.Parse(
                Wrap(
                    "    double a.timeSamples = {",
                    "        0: 0.0,",
                    "        24: 90.0,",
                    "    }"),
                "TS");

            string written = UsdaWriter.Write(stage);

            Assert.That(written, Does.Contain(".timeSamples = {"));
            Assert.That(written, Does.Contain("        0: 0.0,"));
            Assert.That(written, Does.Contain("        24: 90.0,"));
        }

        // ---- signature ----------------------------------------------------------------

        [Test]
        public void Signature_DistinguishesDifferingSampleValues()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            var attrA = new UsdAttribute("v", "double");
            attrA.TimeSamples[0.0] = UsdValue.From(0.0);
            attrA.TimeSamples[24.0] = UsdValue.From(90.0);
            pa.Attributes.Add(attrA);

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            var attrB = new UsdAttribute("v", "double");
            attrB.TimeSamples[0.0] = UsdValue.From(0.0);
            attrB.TimeSamples[24.0] = UsdValue.From(91.0);
            pb.Attributes.Add(attrB);

            Assert.That(UsdSceneSignature.Compute(b), Is.Not.EqualTo(UsdSceneSignature.Compute(a)));
            Assert.That(UsdSceneSignature.FirstDifference(a, b), Is.Not.Null);
        }

        [Test]
        public void Signature_DistinguishesPresenceOfSamples()
        {
            var withSamples = new UsdStage("S");
            UsdPrim ps = withSamples.AddRootPrim(new UsdPrim("X", "Xform"));
            var sampled = new UsdAttribute("v", "double") { Value = UsdValue.From(1.0) };
            sampled.TimeSamples[0.0] = UsdValue.From(0.0);
            ps.Attributes.Add(sampled);

            var withoutSamples = new UsdStage("S");
            UsdPrim pn = withoutSamples.AddRootPrim(new UsdPrim("X", "Xform"));
            pn.Attributes.Add(new UsdAttribute("v", "double") { Value = UsdValue.From(1.0) });

            Assert.That(
                UsdSceneSignature.Compute(withoutSamples),
                Is.Not.EqualTo(UsdSceneSignature.Compute(withSamples)));
        }

        [Test]
        public void Signature_EqualForIdenticalSamples()
        {
            var a = new UsdStage("S");
            UsdPrim pa = a.AddRootPrim(new UsdPrim("X", "Xform"));
            var attrA = new UsdAttribute("v", "double");
            attrA.TimeSamples[0.0] = UsdValue.From(0.0);
            attrA.TimeSamples[24.0] = UsdValue.From(90.0);
            pa.Attributes.Add(attrA);

            var b = new UsdStage("S");
            UsdPrim pb = b.AddRootPrim(new UsdPrim("X", "Xform"));
            var attrB = new UsdAttribute("v", "double");
            attrB.TimeSamples[0.0] = UsdValue.From(0.0);
            attrB.TimeSamples[24.0] = UsdValue.From(90.0);
            pb.Attributes.Add(attrB);

            Assert.That(UsdSceneSignature.Compute(b), Is.EqualTo(UsdSceneSignature.Compute(a)));
        }

        [Test]
        public void Signature_OfSampleLessAttribute_IsUnchangedByTheFeature()
        {
            // A sample-less attribute's signature must be byte-for-byte what it was before time
            // samples existed, so the existing round-trip corpus cannot be perturbed.
            var stage = new UsdStage("S");
            UsdPrim prim = stage.AddRootPrim(new UsdPrim("X", "Xform"));
            prim.Attributes.Add(new UsdAttribute("v", "int") { Value = UsdValue.From(1L) });

            string signature = UsdSceneSignature.Compute(stage);

            Assert.That(signature, Does.Not.Contain("TS("));
        }

        private static readonly double[] s_doubleValues1 = [0.0, 24.0, 48.0];
        private static readonly double[] s_doubleValues2 = [0.0, 24.0];
    }
}
