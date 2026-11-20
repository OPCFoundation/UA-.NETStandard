// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Georeferencing tests (§5.8, Annex B): a recognised Cesium anchor is dual-authored into
    /// the portable well-known types, fails closed on a partial anchor, and can be suppressed.
    /// </summary>
    [TestFixture]
    public class GeoreferenceTests
    {
        private const double Latitude = 47.6062;
        private const double Longitude = -122.3321;
        private const double Height = 56.0;

        private const double AnchorLatitude = 47.6205;
        private const double AnchorLongitude = -122.3493;
        private const double AnchorHeight = 12.5;

        // ---- Dual authoring (Annex B.3) ------------------------------------------------

        [Test]
        public void GeoreferencePrim_DualAuthorsPortableAnchor()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(AnnexBScene());

            List<UsdGeoreferenceApiState> portable = ms.AppliedSchemas<UsdGeoreferenceApiState>("/Site");
            Assert.That(portable, Has.Count.EqualTo(1));

            UsdGeoreferenceApiState node = portable[0];
            Assert.That(
                node.BrowseName.Name,
                Is.EqualTo(Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGeoreferenceApiType));
            Assert.That(node.Latitude!.Value, Is.EqualTo(Latitude));
            Assert.That(node.Longitude!.Value, Is.EqualTo(Longitude));
            Assert.That(node.Height!.Value, Is.EqualTo(Height));
            Assert.That(node.EpsgCode!.Value, Is.EqualTo((uint)4326));
            Assert.That(node.TangentPlane!.Value, Is.EqualTo("ENU"));

            // The vendor schema is still materialized alongside the portable one.
            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>("/Site");
            Assert.That(all.Any(s => s.SchemaName!.Value == "CesiumGeoreferencePrim"), Is.True);
        }

        [Test]
        public void GlobeAnchorApi_DualAuthorsPortableAnchor_OnChild()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(AnnexBScene());

            List<UsdGlobeAnchorApiState> portable =
                ms.AppliedSchemas<UsdGlobeAnchorApiState>("/Site/Anchor");
            Assert.That(portable, Has.Count.EqualTo(1));

            UsdGlobeAnchorApiState node = portable[0];
            Assert.That(
                node.BrowseName.Name,
                Is.EqualTo(Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGlobeAnchorApiType));
            Assert.That(node.Latitude!.Value, Is.EqualTo(AnchorLatitude));
            Assert.That(node.Longitude!.Value, Is.EqualTo(AnchorLongitude));
            Assert.That(node.Height!.Value, Is.EqualTo(AnchorHeight));

            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>("/Site/Anchor");
            Assert.That(all.Any(s => s.SchemaName!.Value == "CesiumGlobeAnchorAPI"), Is.True);
        }

        // ---- Fail closed on a partial anchor -------------------------------------------

        [Test]
        public void PartialAnchor_MissingLongitude_PublishesNoPortableAnchor()
        {
            var stage = new UsdStage("Geo") { DefaultPrim = "Site" };
            var site = new UsdPrim("Site", "Xform");
            site.ApiSchemas.Add(new UsdApiSchema("CesiumGeoreferencePrim"));
            // Latitude only — a partial anchor would place the prim at a wrong position.
            site.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            site.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(Height) });
            stage.AddRootPrim(site);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            Assert.That(ms.AppliedSchemas<UsdGeoreferenceApiState>("/Site"), Is.Empty);
            // The vendor schema is not suppressed — only the portable dual-author is withheld.
            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>("/Site");
            Assert.That(all.Any(s => s.SchemaName!.Value == "CesiumGeoreferencePrim"), Is.True);
        }

        // ---- Suppression by option -----------------------------------------------------

        [Test]
        public void DualAuthorDisabled_PublishesNoPortableAnchor_ButKeepsVendorSchema()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                AnnexBScene(),
                new UsdMaterializationOptions { DualAuthorPortableGeoreference = false });

            Assert.That(ms.AppliedSchemas<UsdGeoreferenceApiState>("/Site"), Is.Empty);
            Assert.That(ms.AppliedSchemas<UsdGlobeAnchorApiState>("/Site/Anchor"), Is.Empty);

            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>("/Site");
            Assert.That(all.Any(s => s.SchemaName!.Value == "CesiumGeoreferencePrim"), Is.True);
        }

        // ---- helpers -------------------------------------------------------------------

        private static UsdStage AnnexBScene()
        {
            var stage = new UsdStage("Geo") { DefaultPrim = "Site", UpAxis = "Z", MetersPerUnit = 1.0 };

            var site = new UsdPrim("Site", "Xform");
            site.ApiSchemas.Add(new UsdApiSchema("CesiumGeoreferencePrim"));
            site.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            site.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(Longitude) });
            site.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(Height) });

            var anchor = new UsdPrim("Anchor", "Xform");
            anchor.ApiSchemas.Add(new UsdApiSchema("CesiumGlobeAnchorAPI"));
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(AnchorLatitude) });
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(AnchorLongitude) });
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(AnchorHeight) });

            site.AddChild(anchor);
            stage.AddRootPrim(site);
            return stage;
        }
    }
}
