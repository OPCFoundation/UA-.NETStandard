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
    /// Composition provenance (§5.6) and applied API schemas (§8.2): the arcs that composed a
    /// prim and the schemas applied to it are materialized as inspectable provenance.
    /// </summary>
    [TestFixture]
    public class CompositionAndSchemaTests
    {
        [Test]
        public void ReferenceAndInstanceArcs_AreMaterialized_ForReferencedPrim()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            List<UsdCompositionArcState> arcs = ms.CompositionArcs("/Plant/Pumps/P101");
            Assert.That(arcs, Has.Count.EqualTo(2));

            UsdCompositionArcState reference =
                arcs.Single(a => a.ArcKind!.Value == UsdArcKindEnum.Reference);
            Assert.That(reference.AssetPath!.Value, Is.EqualTo("pump.usda"));
            Assert.That(reference.PrimPath!.Value, Is.EqualTo("/Pump"));
            Assert.That(reference.ListPosition!.Value, Is.EqualTo(UsdListOpTypeEnum.Append));
            Assert.That(reference.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));

            UsdCompositionArcState instance =
                arcs.Single(a => a.ArcKind!.Value == UsdArcKindEnum.Instance);
            Assert.That(instance.AssetPath!.Value, Is.EqualTo("pump.usda"));
            Assert.That(instance.PrimPath!.Value, Is.EqualTo("/Pump"));
            Assert.That(instance.ListPosition!.Value, Is.EqualTo(UsdListOpTypeEnum.Append));
        }

        [Test]
        public void CompositionFolder_IsAbsentForNonComposedPrim()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            // A prim with no composition arcs gets no Composition folder.
            Assert.That(ms.Prim("/Plant/Pumps/P101/Body").Composition, Is.Null);
            Assert.That(ms.CompositionArcs("/Plant/Pumps/P101/Body"), Is.Empty);
        }

        [Test]
        public void VariantSet_IsMaterialized_WhenAuthored()
        {
            var stage = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.VariantSets.Add(new UsdVariantSet("lod", "high"));
            prim.Composition.Add(new UsdCompositionArc(UsdArcKindEnum.Reference)
            {
                AssetPath = "ext.usda",
                PrimPath = "/Root",
                ListPosition = UsdListOpTypeEnum.Append
            });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            FolderState variantSets = ms.Prim("/P").VariantSets!;
            List<UsdVariantSetState> sets =
                MaterializationHarness.ChildrenOfType<UsdVariantSetState>(ms.Context, variantSets);
            Assert.That(sets, Has.Count.EqualTo(1));
            Assert.That(sets[0].SetName!.Value, Is.EqualTo("lod"));
            Assert.That(sets[0].Selection!.Value, Is.EqualTo("high"));

            List<UsdCompositionArcState> arcs = ms.CompositionArcs("/P");
            Assert.That(arcs, Has.Count.EqualTo(1));
            Assert.That(arcs[0].ArcKind!.Value, Is.EqualTo(UsdArcKindEnum.Reference));
            Assert.That(arcs[0].AssetPath!.Value, Is.EqualTo("ext.usda"));
        }

        [Test]
        public void CompositionOff_SuppressesArcs()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                TestAssets.Load("Plant.usda"),
                new UsdMaterializationOptions { MaterializeComposition = false });

            Assert.That(ms.Prim("/Plant/Pumps/P101").Composition, Is.Null);
        }

        [Test]
        [TestCase("/Cell/Robots/R1/Base")]
        [TestCase("/Cell/Robots/R2/Base")]
        public void CollectionApi_MaterializesAsUsdCollectionApiType(string basePath)
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Cell.usda"));

            // Two schemas are applied: MaterialBindingAPI (generic) and CollectionAPI.
            List<UsdApiSchemaState> all = ms.AppliedSchemas<UsdApiSchemaState>(basePath);
            Assert.That(all, Has.Count.EqualTo(2));

            List<UsdCollectionAPIState> collections =
                ms.AppliedSchemas<UsdCollectionAPIState>(basePath);
            Assert.That(collections, Has.Count.EqualTo(1));
            UsdCollectionAPIState collection = collections[0];
            Assert.That(collection.SchemaName!.Value, Is.EqualTo("CollectionAPI"));
            // Applied schemas hang off the prim by HasAddIn.
            Assert.That(collection.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasAddIn));

            Assert.That(
                all.Any(s => s.SchemaName!.Value == "MaterialBindingAPI"),
                Is.True);
        }

        [Test]
        public void AppliedSchemasOff_SuppressesFolder()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                TestAssets.Load("Cell.usda"),
                new UsdMaterializationOptions { MaterializeAppliedSchemas = false });

            Assert.That(ms.Prim("/Cell/Robots/R1/Base").AppliedSchemas, Is.Null);
        }
    }
}
