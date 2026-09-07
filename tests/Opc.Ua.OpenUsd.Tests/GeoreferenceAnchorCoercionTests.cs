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
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Unit tests for the numeric coercion the portable georeference dual-authoring applies to
    /// a Cesium anchor (§5.8). A vendor stage may author latitude, longitude and height in any
    /// numeric spelling — or as text — and every one of those must yield the same portable
    /// anchor, while a value of a kind that carries no number must fail closed and publish no
    /// anchor at all.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class GeoreferenceAnchorCoercionTests
    {
        private const double Latitude = 47.6062;
        private const double Longitude = -122.3321;

        [Test]
        public void AnAnchorAuthoredInMixedNumericTypesIsCoercedToDouble()
        {
            UsdPrim world = TypedGeoreference(
                latitude: UsdValue.From(47.0),
                longitude: UsdValue.From(-122L),
                height: UsdValue.From(56L));

            List<UsdGeoreferenceApiState> portable = PortableGeoreference(world);

            Assert.That(portable, Has.Count.EqualTo(1));
            Assert.That(portable[0].Latitude!.Value, Is.EqualTo(47.0).Within(1e-5));
            Assert.That(portable[0].Longitude!.Value, Is.EqualTo(-122.0).Within(1e-12));
            Assert.That(portable[0].Height!.Value, Is.EqualTo(56.0).Within(1e-12));
        }

        [Test]
        public void AnAnchorAuthoredAsInvariantTextIsParsed()
        {
            UsdPrim world = TypedGeoreference(
                latitude: UsdValue.FromString("47.6062"),
                longitude: UsdValue.FromString("-122.3321"),
                height: UsdValue.FromString("56.0"));

            List<UsdGeoreferenceApiState> portable = PortableGeoreference(world);

            Assert.That(portable, Has.Count.EqualTo(1));
            Assert.That(portable[0].Latitude!.Value, Is.EqualTo(Latitude).Within(1e-12));
            Assert.That(portable[0].Longitude!.Value, Is.EqualTo(Longitude).Within(1e-12));
        }

        [Test]
        public void AnAnchorWithAnUnparsableComponentPublishesNoPortableAnchor()
        {
            UsdPrim world = TypedGeoreference(
                latitude: UsdValue.FromString("north"),
                longitude: UsdValue.From(Longitude),
                height: UsdValue.From(56.0));

            Assert.That(PortableGeoreference(world), Is.Empty);
        }

        [Test]
        public void AGlobeAnchorMissingItsHeightPublishesNoPortableAnchor()
        {
            var anchor = new UsdPrim("Anchor", "CesiumGlobeAnchorAPI");
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(Longitude) });

            var stage = new UsdStage("Test");
            stage.AddRootPrim(anchor);
            MaterializedScene scene = MaterializationHarness.Materialize(stage);

            Assert.That(scene.AppliedSchemas<UsdGlobeAnchorApiState>("/Anchor"), Is.Empty);
        }

        [Test]
        public void AGlobeAnchorWithACompleteAnchorPublishesThePortableAnchor()
        {
            var anchor = new UsdPrim("Anchor", "CesiumGlobeAnchorAPI");
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:latitude", "double") { Value = UsdValue.From(Latitude) });
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = UsdValue.From(Longitude) });
            anchor.Attributes.Add(
                new UsdAttribute("cesium:anchor:height", "double") { Value = UsdValue.From(56.0) });

            var stage = new UsdStage("Test");
            stage.AddRootPrim(anchor);
            MaterializedScene scene = MaterializationHarness.Materialize(stage);

            List<UsdGlobeAnchorApiState> portable =
                scene.AppliedSchemas<UsdGlobeAnchorApiState>("/Anchor");
            Assert.That(portable, Has.Count.EqualTo(1));
            Assert.That(portable[0].Height!.Value, Is.EqualTo(56.0).Within(1e-12));
        }

        private static UsdPrim TypedGeoreference(UsdValue latitude, UsdValue longitude, UsdValue height)
        {
            var world = new UsdPrim("World", "CesiumGeoreferencePrim");
            world.Attributes.Add(
                new UsdAttribute("cesium:anchor:latitude", "double") { Value = latitude });
            world.Attributes.Add(
                new UsdAttribute("cesium:anchor:longitude", "double") { Value = longitude });
            world.Attributes.Add(
                new UsdAttribute("cesium:anchor:height", "double") { Value = height });
            return world;
        }

        private static List<UsdGeoreferenceApiState> PortableGeoreference(UsdPrim world)
        {
            var stage = new UsdStage("Test");
            stage.AddRootPrim(world);
            return MaterializationHarness.Materialize(stage)
                .AppliedSchemas<UsdGeoreferenceApiState>("/World");
        }
    }
}
