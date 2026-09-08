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
    /// Conversion-layer tests for full <c>&lt;Variant&gt;</c> branch fidelity (§5.6, Composition
    /// Provenance CU): the reader captures <em>every</em> authored branch of a
    /// <c>variantSet "name" = { … }</c> block — not only the selected one — into
    /// <see cref="UsdVariantSet.Variants"/>, the writer re-emits the block preserving branch order,
    /// and the §7.4 signature deliberately ignores non-selected branches (they are un-composed
    /// authoring provenance) while still distinguishing the resolved selection.
    /// </summary>
    [TestFixture]
    public class VariantBranchConversionTests
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

        private static UsdVariantSet ParseSingleVariantSet(params string[] bodyLines)
        {
            UsdStage stage = UsdaReader.Parse(Wrap(bodyLines), "V");
            UsdPrim prim = stage.Find("/P")!;
            return prim.VariantSets.Single();
        }

        // ---- parsing ------------------------------------------------------------------

        [Test]
        public void MultipleBranches_WithNoSelection_AreAllParsed()
        {
            UsdVariantSet set = ParseSingleVariantSet(
                "    variantSet \"lod\" = {",
                "        \"high\" {",
                "            int resolution = 1024",
                "        }",
                "        \"low\" {",
                "            int resolution = 256",
                "        }",
                "    }");

            // No 'variants = {...}' metadata was authored, so nothing is selected.
            Assert.That(set.SetName, Is.EqualTo("lod"));
            Assert.That(set.Selection, Is.EqualTo(string.Empty));
            // Both branches captured, in authored order.
            Assert.That(set.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void BranchBody_CapturesAttributes()
        {
            UsdVariantSet set = ParseSingleVariantSet(
                "    variantSet \"lod\" = {",
                "        \"high\" {",
                "            int resolution = 1024",
                "            token quality = \"best\"",
                "        }",
                "    }");

            UsdPrim high = set.Variants.Single();
            Assert.That(high.Attributes.Select(a => a.Name), Is.EqualTo(s_stringValues2));
            UsdTestHelpers.AssertInteger(high.Attributes[0].Value, 1024L);
            UsdTestHelpers.AssertString(high.Attributes[1].Value, "best");
        }

        [Test]
        public void BranchBody_CapturesChildPrims()
        {
            UsdVariantSet set = ParseSingleVariantSet(
                "    variantSet \"lod\" = {",
                "        \"high\" {",
                "            def Xform \"Detail\"",
                "            {",
                "                int subdivisions = 3",
                "            }",
                "        }",
                "    }");

            UsdPrim high = set.Variants.Single();
            Assert.That(high.Children.Select(c => c.Name), Is.EqualTo(s_stringValues3));
            Assert.That(high.Children[0].TypeName, Is.EqualTo("Xform"));
            Assert.That(high.Children[0].Attributes.Single().Name, Is.EqualTo("subdivisions"));
        }

        [Test]
        public void SingleLineVariantSetBlock_IsParsed()
        {
            UsdVariantSet set = ParseSingleVariantSet(
                "    variantSet \"lod\" = { \"high\" { } \"low\" { } }");

            Assert.That(set.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void SelectionAndBranches_Coexist()
        {
            // The selection lives in the prim metadata header; the branches live in the body.
            var usda = string.Join(
                "\n",
                "#usda 1.0",
                "(",
                "    defaultPrim = \"P\"",
                "    metersPerUnit = 1.0",
                "    upAxis = \"Z\"",
                ")",
                string.Empty,
                "def Xform \"P\" (",
                "    variants = {",
                "        string lod = \"high\"",
                "    }",
                ")",
                "{",
                "    variantSet \"lod\" = {",
                "        \"high\" {",
                "            int resolution = 1024",
                "        }",
                "        \"low\" {",
                "            int resolution = 256",
                "        }",
                "    }",
                "}",
                string.Empty);

            UsdPrim prim = UsdaReader.Parse(usda, "V").Find("/P")!;
            UsdVariantSet set = prim.VariantSets.Single();
            Assert.That(set.Selection, Is.EqualTo("high"));
            Assert.That(set.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
        }

        // ---- writing / round-trip -----------------------------------------------------

        [Test]
        public void Writer_ReEmitsBranches_PreservingOrder()
        {
            UsdStage stage = BuildStageWithBranches("first", "second", "third");
            string written = UsdaWriter.Write(stage);

            Assert.That(written, Does.Contain("variantSet \"lod\" = {"));
            int firstIdx = written.IndexOf("\"first\" {", System.StringComparison.Ordinal);
            int secondIdx = written.IndexOf("\"second\" {", System.StringComparison.Ordinal);
            int thirdIdx = written.IndexOf("\"third\" {", System.StringComparison.Ordinal);
            Assert.That(firstIdx, Is.GreaterThan(0));
            Assert.That(secondIdx, Is.GreaterThan(firstIdx));
            Assert.That(thirdIdx, Is.GreaterThan(secondIdx));
        }

        [Test]
        public void Branches_RoundTripThroughWriteAndReparse()
        {
            UsdStage stage = BuildStageWithBranches("high", "low");
            // Give the branches distinct bodies so a lost or reordered branch is observable.
            UsdVariantSet set = stage.Find("/P")!.VariantSets.Single();
            set.Variants[0].Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(1024L) });
            set.Variants[1].Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(256L) });

            string written = UsdaWriter.Write(stage);
            UsdStage reparsed = UsdaReader.Parse(written, stage.StageName);

            UsdVariantSet reSet = reparsed.Find("/P")!.VariantSets.Single();
            Assert.That(reSet.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
            UsdTestHelpers.AssertInteger(reSet.Variants[0].Attributes.Single().Value, 1024L);
            UsdTestHelpers.AssertInteger(reSet.Variants[1].Attributes.Single().Value, 256L);
        }

        [Test]
        public void WriterOutputIsAFixedPoint_ForVariantBranches()
        {
            UsdStage stage = BuildStageWithBranches("high", "low");
            UsdVariantSet set = stage.Find("/P")!.VariantSets.Single();
            set.Variants[0].Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(1024L) });
            set.Variants[0].AddChild(new UsdPrim("Detail", "Xform"));

            string firstWrite = UsdaWriter.Write(stage);
            string secondWrite = UsdaWriter.Write(UsdaReader.Parse(firstWrite, stage.StageName));

            Assert.That(secondWrite, Is.EqualTo(firstWrite));
        }

        // ---- signature ----------------------------------------------------------------

        [Test]
        public void Signature_IgnoresNonSelectedBranches()
        {
            // Two stages identical in name/selection but with different branch *bodies* must sign
            // identically: the branches are un-composed authoring provenance, out of §7.4 scope.
            UsdStage a = BuildStageWithBranches("high", "low");
            a.Find("/P")!.VariantSets.Single().Selection = "high";
            a.Find("/P")!.VariantSets.Single().Variants[0]
                .Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(1024L) });

            UsdStage b = BuildStageWithBranches("high", "low");
            b.Find("/P")!.VariantSets.Single().Selection = "high";
            b.Find("/P")!.VariantSets.Single().Variants[0]
                .Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(256L) });
            b.Find("/P")!.VariantSets.Single().Variants[1]
                .Attributes.Add(new UsdAttribute("extra", "double") { Value = UsdValue.From(3.5) });

            Assert.That(
                UsdSceneSignature.Compute(b),
                Is.EqualTo(UsdSceneSignature.Compute(a)),
                "non-selected branch bodies must not affect the composed-scene signature");
        }

        [Test]
        public void Signature_IgnoresBranchPresenceEntirely()
        {
            // A stage with captured branches and one with none (same set name/selection) sign the
            // same, so adding branch capture never perturbs an existing signature (§7.4).
            UsdStage withBranches = BuildStageWithBranches("high", "low");
            withBranches.Find("/P")!.VariantSets.Single().Selection = "high";

            var withoutBranches = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.VariantSets.Add(new UsdVariantSet("lod", "high"));
            withoutBranches.AddRootPrim(prim);

            Assert.That(
                UsdSceneSignature.Compute(withBranches),
                Is.EqualTo(UsdSceneSignature.Compute(withoutBranches)));
        }

        [Test]
        public void Signature_DistinguishesSelection()
        {
            UsdStage a = BuildStageWithBranches("high", "low");
            a.Find("/P")!.VariantSets.Single().Selection = "high";

            UsdStage b = BuildStageWithBranches("high", "low");
            b.Find("/P")!.VariantSets.Single().Selection = "low";

            Assert.That(
                UsdSceneSignature.Compute(b),
                Is.Not.EqualTo(UsdSceneSignature.Compute(a)),
                "the resolved selection is a composed-scene property and must be signed");
            Assert.That(UsdSceneSignature.FirstDifference(a, b), Is.Not.Null);
        }

        // ---- helpers -------------------------------------------------------------------

        private static UsdStage BuildStageWithBranches(params string[] branchNames)
        {
            var stage = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var set = new UsdVariantSet("lod");
            foreach (string name in branchNames)
            {
                set.Variants.Add(new UsdPrim(name));
            }
            prim.VariantSets.Add(set);
            stage.AddRootPrim(prim);
            return stage;
        }

        private static readonly string[] s_stringValues1 = ["high", "low"];
        private static readonly string[] s_stringValues2 = ["resolution", "quality"];
        private static readonly string[] s_stringValues3 = ["Detail"];
    }
}
