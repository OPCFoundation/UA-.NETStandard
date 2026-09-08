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
    /// Composition provenance — variant <em>branches</em> in the address space (§5.6): a
    /// materialized <c>UsdVariantSetType</c> exposes every authored <c>&lt;Variant&gt;</c> branch
    /// as a <c>UsdPrimType</c> Object under the set, in addition to <c>SetName</c>/<c>Selection</c>,
    /// so a client can browse into any branch — not only the selected one — and recover its full
    /// prim-shaped body. Branches come from <see cref="UsdVariantSet.Variants"/>; a set with no
    /// captured branches materializes none (fail closed — nothing is invented from the selection
    /// name).
    /// </summary>
    /// <remarks>
    /// This fixture supersedes the earlier stopgap that synthesized a single branch object from the
    /// resolved <c>Selection</c> string. Full-fidelity branch capture changed that behaviour: the
    /// materializer now authors exactly the branches present in <see cref="UsdVariantSet.Variants"/>
    /// with their bodies, so these tests were updated to the corrected contract.
    /// </remarks>
    [TestFixture]
    public class VariantBranchTests
    {
        // ---- Every authored branch is materialized -------------------------------------

        [Test]
        public void VariantSet_MaterializesEveryAuthoredBranchObject()
        {
            var set = new UsdVariantSet("lod", "high");
            set.Variants.Add(new UsdPrim("high"));
            set.Variants.Add(new UsdPrim("low"));
            (MaterializedScene ms, UsdVariantSetState node) = MaterializeSet(set);

            // SetName/Selection are authored exactly as before branches were captured.
            Assert.That(node.SetName!.Value, Is.EqualTo("lod"));
            Assert.That(node.Selection!.Value, Is.EqualTo("high"));

            List<UsdPrimState> branches =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, node);
            // Both branches are present, in authored order, not just the selected one.
            Assert.That(branches.Select(b => b.BrowseName.Name), Is.EqualTo(s_stringValues1));

            UsdPrimState high = branches[0];
            Assert.That(high.BrowseName.NamespaceIndex, Is.EqualTo(ms.Namespace));
            Assert.That(high.NodeId.IsNull, Is.False);
            // A branch browses like any other prim — it is a UsdPrimType Object (§5.6).
            Assert.That(
                high.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
        }

        [Test]
        public void UnselectedBranch_IsStillMaterialized()
        {
            // Nothing is selected, yet both authored branches must still be materialized: §5.6
            // covers the full branch structure, independent of the resolved selection.
            var set = new UsdVariantSet("shading", string.Empty);
            set.Variants.Add(new UsdPrim("pbr"));
            set.Variants.Add(new UsdPrim("unlit"));
            (MaterializedScene ms, UsdVariantSetState node) = MaterializeSet(set);

            Assert.That(node.Selection!.Value, Is.EqualTo(string.Empty));
            List<UsdPrimState> branches =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, node);
            Assert.That(branches.Select(b => b.BrowseName.Name), Is.EqualTo(s_stringValues2));
        }

        // ---- Fail closed when no branch was captured -----------------------------------

        [Test]
        public void SelectionWithoutCapturedBranches_MaterializesNoBranch()
        {
            // A set that recorded only a resolved selection (no captured branches) invents no
            // branch object from the selection name — the selection stays on the Selection
            // property alone (fail closed).
            var set = new UsdVariantSet("lod", "high");
            (MaterializedScene ms, UsdVariantSetState node) = MaterializeSet(set);

            Assert.That(
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, node), Is.Empty);
            Assert.That(node.SetName!.Value, Is.EqualTo("lod"));
            Assert.That(node.Selection!.Value, Is.EqualTo("high"));
        }

        // ---- Branch bodies are materialized with full prim fidelity --------------------

        [Test]
        public void BranchBody_MaterializesAttributes()
        {
            var high = new UsdPrim("high");
            high.Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(1024L) });
            var set = new UsdVariantSet("lod", "high");
            set.Variants.Add(high);
            (MaterializedScene ms, UsdVariantSetState node) = MaterializeSet(set);

            UsdPrimState branch =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, node).Single();

            UsdAttributeState attr =
                MaterializationHarness.ChildrenOfType<UsdAttributeState>(ms.Context, branch).Single();
            Assert.That(attr.BrowseName.Name, Is.EqualTo("resolution"));
            Assert.That(attr.UsdTypeName!.Value, Is.EqualTo("int"));
        }

        [Test]
        public void BranchBody_MaterializesChildPrims()
        {
            var high = new UsdPrim("high");
            high.AddChild(new UsdPrim("Detail", "Xform"));
            var set = new UsdVariantSet("lod", "high");
            set.Variants.Add(high);
            (MaterializedScene ms, UsdVariantSetState node) = MaterializeSet(set);

            UsdPrimState branch =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, node).Single();
            UsdPrimState childPrim =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, branch).Single();
            Assert.That(childPrim.BrowseName.Name, Is.EqualTo("Detail"));
        }

        // ---- Branches on different sets get distinct identities ------------------------

        [Test]
        public void BranchesOnDifferentSets_ProduceDistinctBranchNodeIds()
        {
            var stage = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            var lod = new UsdVariantSet("lod", "high");
            lod.Variants.Add(new UsdPrim("high"));
            var shading = new UsdVariantSet("shading", "pbr");
            shading.Variants.Add(new UsdPrim("pbr"));
            prim.VariantSets.Add(lod);
            prim.VariantSets.Add(shading);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            FolderState variantSets = ms.Prim("/P").VariantSets!;
            List<UsdVariantSetState> sets =
                MaterializationHarness.ChildrenOfType<UsdVariantSetState>(ms.Context, variantSets);
            Assert.That(sets, Has.Count.EqualTo(2));

            var branchIds = new List<NodeId>();
            foreach (UsdVariantSetState set in sets)
            {
                UsdPrimState branch =
                    MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, set).Single();
                Assert.That(branch.NodeId.IsNull, Is.False);
                branchIds.Add(branch.NodeId);
            }

            // Regression guard: AddVariant_Placeholder leaves the type's placeholder NodeId, so without the
            // explicit per-instance NodeId assignment both branches would collide on one id.
            Assert.That(branchIds[0], Is.Not.EqualTo(branchIds[1]));
        }

        // ---- The branch does not leak into the prim tree -------------------------------

        [Test]
        public void VariantBranch_IsNotADirectPrimChild()
        {
            var set = new UsdVariantSet("lod", "high");
            set.Variants.Add(new UsdPrim("high"));
            (MaterializedScene ms, _) = MaterializeSet(set);

            // The branch hangs under VariantSets/<set>, never directly under the prim, so a
            // prim-tree walk (as the exporter does) never double-counts it as a child prim.
            List<UsdPrimState> directChildPrims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, ms.Prim("/P"));
            Assert.That(directChildPrims, Is.Empty);
        }

        // ---- Round trip: branches survive materialize -> export, add no phantom child --

        [Test]
        public void Branches_RoundTripThroughMaterializeAndExport()
        {
            var high = new UsdPrim("high");
            high.Attributes.Add(new UsdAttribute("resolution", "int") { Value = UsdValue.From(1024L) });
            var set = new UsdVariantSet("lod", "high");
            set.Variants.Add(high);
            set.Variants.Add(new UsdPrim("low"));

            var stage = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.VariantSets.Add(set);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);

            UsdPrim exportedPrim = exported.RootPrims.Single();
            Assert.That(exportedPrim.VariantSets, Has.Count.EqualTo(1));
            UsdVariantSet exportedSet = exportedPrim.VariantSets[0];
            Assert.That(exportedSet.SetName, Is.EqualTo("lod"));
            Assert.That(exportedSet.Selection, Is.EqualTo("high"));

            // Every authored branch is recovered, in order, with its body content.
            Assert.That(exportedSet.Variants.Select(v => v.Name), Is.EqualTo(s_stringValues1));
            Assert.That(exportedSet.Variants[0].Attributes.Single().Name, Is.EqualTo("resolution"));

            // The materialized <Variant> branches must not be re-exported as child prims (§7.4).
            Assert.That(exportedPrim.Children, Is.Empty);
        }

        // ---- helpers -------------------------------------------------------------------

        private static (MaterializedScene Scene, UsdVariantSetState Set) MaterializeSet(
            UsdVariantSet variantSet)
        {
            var stage = new UsdStage("V") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.VariantSets.Add(variantSet);
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            FolderState folder = ms.Prim("/P").VariantSets!;
            UsdVariantSetState set =
                MaterializationHarness.ChildrenOfType<UsdVariantSetState>(ms.Context, folder).Single();
            return (ms, set);
        }

        private static readonly string[] s_stringValues1 = ["high", "low"];
        private static readonly string[] s_stringValues2 = ["pbr", "unlit"];
    }
}
