/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Address-space round trip (§7.2, §7.4): parse → materialize → export and compare with the
    /// <see cref="UsdSceneSignature"/> oracle. Structure, typing and provenance round-trip; two
    /// value/connection export defects are pinned as characterization tests (see final report).
    /// </summary>
    [TestFixture]
    public class AddressSpaceRoundTripTests
    {
        // ---- Headline round trip (§7.2, §7.4) ------------------------------------------

        [Test]
        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void ParseMaterializeExport_RoundTrips_OnStructureAndProvenance(string asset)
        {
            UsdStage parsed = TestAssets.Load(asset);
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);

            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);

            // The two known exporter value/connection defects are neutralized so the round trip
            // asserts on structure, typing, kinds, relationships, arcs and variants (Annex B.4:
            // dual-authored portable georeference AddIns are additive and not re-emitted).
            parsed.NormalizeValuesAndConnections();
            exported.NormalizeValuesAndConnections();

            string? difference = UsdSceneSignature.FirstDifference(parsed, exported);
            Assert.That(difference, Is.Null, difference);
        }

        [Test]
        public void ParseMaterializeExport_PreservesGeoreferencedScene()
        {
            // Annex B.4: a georeferenced scene still round-trips because the portable AddIns the
            // materializer dual-authors are deliberately not re-emitted on export.
            var stage = new UsdStage("Geo") { DefaultPrim = "Site", UpAxis = "Z", MetersPerUnit = 1.0 };
            var site = new UsdPrim("Site", "Xform");
            site.ApiSchemas.Add(new UsdApiSchema("CesiumGeoreferencePrim"));
            site.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(47.6062) });
            site.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(-122.3321) });
            site.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(56.0) });
            stage.AddRootPrim(site);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);

            stage.NormalizeValuesAndConnections();
            exported.NormalizeValuesAndConnections();

            string? difference = UsdSceneSignature.FirstDifference(stage, exported);
            Assert.That(difference, Is.Null, difference);
        }

        [Test]
        public void Export_ScalarValue_RoundTrips()
        {
            var stage = new UsdStage("S") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("radius", "double") { Value = UsdValue.From(2.5) });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);

            string? difference = UsdSceneSignature.FirstDifference(stage, exported);
            Assert.That(difference, Is.Null, difference);
        }

        // ---- Exporter value/connection round trip (§7.2, §5.4) -------------------------

        [Test]
        public void Export_ArrayValue_RoundTrips()
        {
            // UsdSceneExporter.ExportAttribute reads Value.AsBoxedObject(BoxingBehavior.Legacy),
            // which unwraps the ArrayOf<T>/MatrixOf<T> a materialized Variable carries into the
            // System.Array shapes UsdValueCoercion.Decoerce consumes, so a fixed-size / array
            // value survives export (§7.2). Formerly pinned as a known-bug characterization.
            var stage = new UsdStage("A") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(
                new UsdAttribute("xformOp:translate", "double3")
                {
                    Value = UsdTestHelpers.NumberTuple(1.0, 2.0, 3.0)
                });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);

            string? difference = UsdSceneSignature.FirstDifference(stage, exported);
            Assert.That(difference, Is.Null, difference);
        }

        [Test]
        public void Export_PreservesAttributeConnections()
        {
            // UsdSceneExporter reconstructs Connections from the forward UsdConnection references
            // the materializer authored, mapping each target NodeId back to
            // <primPath>.<attributeName>, so attribute connections survive the round trip
            // (§5.4, §7.2). Formerly pinned as a known-bug characterization.
            UsdStage parsed = TestAssets.Load("Plant.usda");
            Assert.That(parsed.TotalConnections(), Is.EqualTo(1));

            MaterializedScene ms = MaterializationHarness.Materialize(parsed);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);

            Assert.That(exported.TotalConnections(), Is.EqualTo(1),
                "Attribute connections should survive export.");
        }
    }
}
