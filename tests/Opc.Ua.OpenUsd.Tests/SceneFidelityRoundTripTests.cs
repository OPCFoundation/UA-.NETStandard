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

using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// End-to-end fidelity across the whole pipeline, exercising both closed gaps at once —
    /// variant branches (§5.6) and time samples (§7.1 step 3, §7.2, §9). A hand-authored
    /// <c>.usda</c> is parsed, materialized into an address space, exported back, and the composed
    /// scene is asserted <em>variant-selection-equivalent</em> and sample-preserving through the
    /// §7.4 signature, while the un-composed branch structure is confirmed to survive structurally.
    /// </summary>
    [TestFixture]
    public class SceneFidelityRoundTripTests
    {
        private const string Source =
            "#usda 1.0\n" +
            "(\n" +
            "    defaultPrim = \"Turntable\"\n" +
            "    metersPerUnit = 1.0\n" +
            "    upAxis = \"Z\"\n" +
            "    timeCodesPerSecond = 24\n" +
            ")\n" +
            "\n" +
            "def Xform \"Turntable\" (\n" +
            "    variants = {\n" +
            "        string lod = \"high\"\n" +
            "    }\n" +
            ")\n" +
            "{\n" +
            "    double spin = 5.0\n" +
            "    double spin.timeSamples = {\n" +
            "        0: 0.0,\n" +
            "        24: 90.0,\n" +
            "        48: 180.0,\n" +
            "    }\n" +
            "    variantSet \"lod\" = {\n" +
            "        \"high\" {\n" +
            "            int resolution = 1024\n" +
            "        }\n" +
            "        \"low\" {\n" +
            "            int resolution = 256\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        [Test]
        public void ParseMaterializeExport_IsSignatureEquivalent()
        {
            UsdStage parsed = UsdaReader.Parse(Source, "Turntable");

            MaterializedScene ms = MaterializationHarness.Materialize(parsed);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            // §7.4: the composed scene must be variant-selection-equivalent and preserve its
            // sampled data across materialize -> export.
            string? difference = UsdSceneSignature.FirstDifference(parsed, exported);
            Assert.That(
                UsdSceneSignature.Compute(exported),
                Is.EqualTo(UsdSceneSignature.Compute(parsed)),
                difference ?? "signatures are unexpectedly equal");
        }

        [Test]
        public void ParseMaterializeExportWrite_ReparsesToTheSameSignature()
        {
            UsdStage parsed = UsdaReader.Parse(Source, "Turntable");

            MaterializedScene ms = MaterializationHarness.Materialize(parsed);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            // Close the loop through the writer: the exported scene must re-serialize and re-parse
            // to an identical signature, so the full .usda -> materialize -> export -> .usda cycle
            // is lossless for the composed scene.
            string written = UsdaWriter.Write(exported);
            UsdStage reparsed = UsdaReader.Parse(written, "Turntable");

            Assert.That(
                UsdSceneSignature.Compute(reparsed),
                Is.EqualTo(UsdSceneSignature.Compute(parsed)),
                UsdSceneSignature.FirstDifference(parsed, reparsed) ?? "signatures equal");
        }

        [Test]
        public void ExportedScene_RecoversSamples_AndBranches_Structurally()
        {
            UsdStage parsed = UsdaReader.Parse(Source, "Turntable");
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            UsdPrim prim = exported.Find("/Turntable")!;

            // Time samples: the default and every sample survive the address-space round trip.
            UsdAttribute spin = prim.Attributes.Single(a => a.Name == "spin");
            UsdTestHelpers.AssertDouble(spin.Value, 5.0);
            Assert.That(spin.TimeSamples.Keys, Is.EqualTo(s_doubleValues1));
            UsdTestHelpers.AssertDouble(spin.TimeSamples[48.0], 180.0);

            // Variant branches: the selection and every authored branch (with body) survive.
            UsdVariantSet set = prim.VariantSets.Single();
            Assert.That(set.SetName, Is.EqualTo("lod"));
            Assert.That(set.Selection, Is.EqualTo("high"));
            Assert.That(set.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
            Assert.That(
                set.Variants[0].Attributes.Single(a => a.Name == "resolution").Value,
                Is.EqualTo(UsdValue.From(1024L)));
            Assert.That(
                set.Variants[1].Attributes.Single(a => a.Name == "resolution").Value,
                Is.EqualTo(UsdValue.From(256L)));
        }

        [Test]
        public void MaterializedScene_ExposesHistoricalAccess_ForTheSampledAttribute()
        {
            UsdStage parsed = UsdaReader.Parse(Source, "Turntable");
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);

            // The composed-scene HistoricalAccess surface carries exactly the one sampled
            // attribute — the variant-branch attribute ("resolution") is authoring provenance and
            // is deliberately excluded (§7.4).
            Assert.That(
                ms.Result.HistoricalAccessByPath.Keys, Is.EqualTo(s_stringValues2));
            UsdHistoricalAccess ha = ms.Result.HistoricalAccessByPath["/Turntable.spin"];
            Assert.That(ha.Samples.Select(s => s.TimeCode), Is.EqualTo(s_doubleValues1));
            Assert.That(ms.Attr("/Turntable.spin").Historizing, Is.True);
        }

        private static readonly double[] s_doubleValues1 = [0.0, 24.0, 48.0];
        private static readonly string[] s_stringValues1 = ["high", "low"];
        private static readonly string[] s_stringValues2 = ["/Turntable.spin"];
    }
}
