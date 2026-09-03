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
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Georeferencing on a <em>typed</em> Cesium prim (§5.8, Annex B.1): a real Cesium stage
    /// authors the anchor as <c>def CesiumGeoreferencePrim "World" { … }</c> — a prim whose
    /// <c>TypeName</c> carries the schema rather than an applied <c>apiSchemas</c> entry — and
    /// the portable <c>UsdGeoreferenceApiType</c> dual-authoring must fire for that spelling too,
    /// so a generic client always finds a vendor-neutral anchor. This complements
    /// <see cref="GeoreferenceTests"/>, which exercises the applied-schema spelling.
    /// </summary>
    [TestFixture]
    public class GeoreferenceTypedPrimTests
    {
        private const double Latitude = 47.6062;
        private const double Longitude = -122.3321;
        private const double Height = 56.0;

        // ---- Typed georeference prim (Annex B.1 spelling) ------------------------------

        [Test]
        public void TypedGeoreferencePrim_DualAuthorsPortableAnchor()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TypedGeoreferenceScene());

            List<UsdGeoreferenceApiState> portable =
                ms.AppliedSchemas<UsdGeoreferenceApiState>("/World");
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
        }

        [Test]
        public void TypedGeoreferencePrim_KeepsTokenAndFallsBackToUsdPrimType()
        {
            // The schema token lives in TypeName, and because CesiumGeoreferencePrim is not one
            // of the built-in typed schemas the prim degrades to a concrete UsdPrimType (§8.4)
            // rather than being retyped to anything abstract.
            MaterializedScene ms = MaterializationHarness.Materialize(TypedGeoreferenceScene());

            UsdPrimState world = ms.Prim("/World");
            Assert.That(world.TypeName!.Value, Is.EqualTo("CesiumGeoreferencePrim"));
            Assert.That(
                world.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
            // A typed georeference prim declares no applied apiSchemas, so the only AddIn in the
            // folder is the portable anchor the materializer dual-authored — the vendor schema is
            // *not* invented as a phantom applied schema.
            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>("/World");
            Assert.That(all, Has.Count.EqualTo(1));
            Assert.That(all.Any(s => s.SchemaName!.Value == "CesiumGeoreferencePrim"), Is.False);
            Assert.That(
                all[0].SchemaName!.Value,
                Is.EqualTo(Opc.Ua.OpenUsd.Scene.BrowseNames.UsdGeoreferenceApiType));
        }

        [Test]
        public void TypedGlobeAnchorPrim_DualAuthorsPortableAnchor()
        {
            var stage = new UsdStage("Geo") { DefaultPrim = "Anchor" };
            var anchor = new UsdPrim("Anchor", "CesiumGlobeAnchorAPI");
            anchor.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            anchor.Attributes.Add(new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(Longitude) });
            anchor.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(Height) });
            stage.AddRootPrim(anchor);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            List<UsdGlobeAnchorApiState> portable =
                ms.AppliedSchemas<UsdGlobeAnchorApiState>("/Anchor");
            Assert.That(portable, Has.Count.EqualTo(1));
            Assert.That(portable[0].Latitude!.Value, Is.EqualTo(Latitude));
            Assert.That(portable[0].Longitude!.Value, Is.EqualTo(Longitude));
            Assert.That(portable[0].Height!.Value, Is.EqualTo(Height));
        }

        // ---- Fail closed on a typed prim with a partial anchor -------------------------

        [Test]
        public void TypedGeoreferencePrim_MissingLongitude_PublishesNoPortableAnchor()
        {
            var stage = new UsdStage("Geo") { DefaultPrim = "World" };
            var world = new UsdPrim("World", "CesiumGeoreferencePrim");
            // Longitude omitted — a partial anchor would place the prim at a wrong position, so
            // the portable anchor is withheld entirely (fail closed).
            world.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            world.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(Height) });
            stage.AddRootPrim(world);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            Assert.That(ms.AppliedSchemas<UsdGeoreferenceApiState>("/World"), Is.Empty);
            // The typed prim itself still materialises and keeps its token.
            Assert.That(ms.Prim("/World").TypeName!.Value, Is.EqualTo("CesiumGeoreferencePrim"));
        }

        // ---- Suppression by option still honoured for the typed spelling ---------------

        [Test]
        public void TypedGeoreferencePrim_DualAuthorDisabled_PublishesNoPortableAnchor()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                TypedGeoreferenceScene(),
                new UsdMaterializationOptions { DualAuthorPortableGeoreference = false });

            Assert.That(ms.AppliedSchemas<UsdGeoreferenceApiState>("/World"), Is.Empty);
            // With no applied schema and dual-authoring off, the georeference prim needs no
            // AppliedSchemas folder at all.
            Assert.That(ms.Prim("/World").AppliedSchemas, Is.Null);
        }

        // ---- helpers -------------------------------------------------------------------

        private static UsdStage TypedGeoreferenceScene()
        {
            var stage = new UsdStage("Geo") { DefaultPrim = "World", UpAxis = "Z", MetersPerUnit = 1.0 };
            var world = new UsdPrim("World", "CesiumGeoreferencePrim");
            world.Attributes.Add(new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            world.Attributes.Add(new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(Longitude) });
            world.Attributes.Add(new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(Height) });
            stage.AddRootPrim(world);
            return stage;
        }
    }
}
