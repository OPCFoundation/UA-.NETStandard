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
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Unit tests for the optional members the materializer authors only when the source scene
    /// actually carries them — stage time-code and mass metadata, per-prim instanceability and
    /// documentation, attribute interpolation, custom relationships, and variant-selecting
    /// composition arcs — plus the fail-fast argument validation of the entry point.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OptionalMemberMaterializationTests
    {
        [Test]
        public void MaterializeUsdStageRejectsANullContext()
        {
            (SystemContext _, ushort ns, BaseObjectState root) = MaterializationHarness.NewContext();

            Assert.That(
                () => ((ISystemContext)null!).MaterializeUsdStage(root, new UsdStage("Test"), ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void MaterializeUsdStageRejectsANullParent()
        {
            (SystemContext context, ushort ns, BaseObjectState _) =
                MaterializationHarness.NewContext();

            Assert.That(
                () => context.MaterializeUsdStage(null!, new UsdStage("Test"), ns),
                Throws.ArgumentNullException);
        }

        [Test]
        public void MaterializeUsdStageRejectsANullStage()
        {
            (SystemContext context, ushort ns, BaseObjectState root) =
                MaterializationHarness.NewContext();

            Assert.That(
                () => context.MaterializeUsdStage(root, null!, ns), Throws.ArgumentNullException);
        }

        [Test]
        public void AStageAuthorsItsOptionalTimeCodeAndMassMetadata()
        {
            var stage = new UsdStage("Test")
            {
                KilogramsPerUnit = 0.001,
                TimeCodesPerSecond = 48.0,
                StartTimeCode = -12.0,
                EndTimeCode = 96.0
            };

            MaterializedScene scene = MaterializationHarness.Materialize(stage);

            Assert.That(scene.Stage.KilogramsPerUnit!.Value, Is.EqualTo(0.001).Within(1e-12));
            Assert.That(scene.Stage.TimeCodesPerSecond!.Value, Is.EqualTo(48.0).Within(1e-12));
            Assert.That(scene.Stage.StartTimeCode!.Value, Is.EqualTo(-12.0).Within(1e-12));
            Assert.That(scene.Stage.EndTimeCode!.Value, Is.EqualTo(96.0).Within(1e-12));
        }

        [Test]
        public void AStageWithoutOptionalTimeCodeMetadataAuthorsNoSuchMembers()
        {
            MaterializedScene scene = MaterializationHarness.Materialize(new UsdStage("Test"));

            Assert.That(scene.Stage.KilogramsPerUnit, Is.Null);
            Assert.That(scene.Stage.TimeCodesPerSecond, Is.Null);
            Assert.That(scene.Stage.StartTimeCode, Is.Null);
            Assert.That(scene.Stage.EndTimeCode, Is.Null);
        }

        [Test]
        public void APrimAuthorsInstanceableAndDocumentationWhenAuthored()
        {
            var prim = new UsdPrim("Cube", "Cube")
            {
                Instanceable = true,
                Documentation = "A reusable cube."
            };

            UsdPrimState node = MaterializeSinglePrim(prim, out _);

            Assert.That(node.Instanceable!.Value, Is.True);
            Assert.That(node.Documentation!.Value, Is.EqualTo("A reusable cube."));
        }

        [Test]
        public void APrimWithoutInstanceableOrDocumentationAuthorsNoSuchMembers()
        {
            UsdPrimState node = MaterializeSinglePrim(new UsdPrim("Cube", "Cube"), out _);

            Assert.That(node.Instanceable, Is.Null);
            Assert.That(node.Documentation, Is.Null);
        }

        [Test]
        public void AnAttributeAuthorsItsInterpolationWhenAuthored()
        {
            var attribute = new UsdAttribute("points", "point3f[]")
            {
                Interpolation = "vertex"
            };
            var prim = new UsdPrim("Mesh", "Mesh");
            prim.Attributes.Add(attribute);

            UsdPrimState node = MaterializeSinglePrim(prim, out MaterializedScene scene);
            List<UsdAttributeState> attributes =
                MaterializationHarness.ChildrenOfType<UsdAttributeState>(scene.Context, node);

            Assert.That(attributes, Has.Count.EqualTo(1));
            Assert.That(attributes[0].Interpolation!.Value, Is.EqualTo("vertex"));
        }

        [Test]
        public void ACustomRelationshipAuthorsTheCustomFlag()
        {
            var relationship = new UsdRelationship("material:binding") { Custom = true };
            relationship.Targets.Add("/Materials/Steel");
            var prim = new UsdPrim("Mesh", "Mesh");
            prim.Relationships.Add(relationship);

            UsdPrimState node = MaterializeSinglePrim(prim, out MaterializedScene scene);
            List<UsdRelationshipState> relationships =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(scene.Context, node);

            Assert.That(relationships, Has.Count.EqualTo(1));
            Assert.That(relationships[0].Custom!.Value, Is.True);
        }

        [Test]
        public void ARelationshipTargetingAnAttributeResolvesToThatAttributeNode()
        {
            var target = new UsdPrim("Mesh", "Mesh");
            target.Attributes.Add(new UsdAttribute("size", "double") { Value = UsdValue.From(2.0) });
            var source = new UsdPrim("Binding", "Scope");
            var relationship = new UsdRelationship("drivenBy");
            relationship.Targets.Add("/Mesh.size");
            source.Relationships.Add(relationship);

            var stage = new UsdStage("Test");
            stage.AddRootPrim(target);
            stage.AddRootPrim(source);
            MaterializedScene scene = MaterializationHarness.Materialize(stage);

            List<UsdPrimState> prims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(scene.Context, scene.Stage);
            UsdPrimState bindingNode = prims[1];
            List<UsdRelationshipState> relationships =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(
                    scene.Context, bindingNode);

            Assert.That(relationships, Has.Count.EqualTo(1));
            ArrayOf<NodeId> targets = relationships[0].Targets!.Value;
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].IsNull, Is.False, "An in-subtree attribute target must resolve.");
        }

        [Test]
        public void ARelationshipTargetingAnUnknownPathKeepsAPlaceholder()
        {
            var source = new UsdPrim("Binding", "Scope");
            var relationship = new UsdRelationship("drivenBy");
            relationship.Targets.Add("/Nowhere.size");
            source.Relationships.Add(relationship);

            UsdPrimState node = MaterializeSinglePrim(source, out MaterializedScene scene);
            List<UsdRelationshipState> relationships =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(scene.Context, node);

            ArrayOf<NodeId> targets = relationships[0].Targets!.Value;
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].IsNull, Is.True, "An out-of-subtree target keeps a placeholder.");
        }

        [Test]
        public void AVariantSelectingCompositionArcAuthorsItsSetAndSelection()
        {
            var arc = new UsdCompositionArc(UsdArcKindEnum.VariantSet)
            {
                VariantSet = "modelingVariant",
                VariantSelection = "highRes"
            };
            var prim = new UsdPrim("Cube", "Cube");
            prim.Composition.Add(arc);

            UsdPrimState node = MaterializeSinglePrim(prim, out MaterializedScene scene);
            List<UsdCompositionArcState> arcs =
                MaterializationHarness.ChildrenOfType<UsdCompositionArcState>(
                    scene.Context, node.Composition!);

            Assert.That(arcs, Has.Count.EqualTo(1));
            Assert.That(arcs[0].VariantSet!.Value, Is.EqualTo("modelingVariant"));
            Assert.That(arcs[0].VariantSelection!.Value, Is.EqualTo("highRes"));
        }

        [Test]
        public void ACompositionArcWithoutAVariantSelectionAuthorsNoSuchMembers()
        {
            var arc = new UsdCompositionArc(UsdArcKindEnum.Reference)
            {
                AssetPath = "@cube.usda@",
                PrimPath = "/Cube"
            };
            var prim = new UsdPrim("Cube", "Cube");
            prim.Composition.Add(arc);

            UsdPrimState node = MaterializeSinglePrim(prim, out MaterializedScene scene);
            List<UsdCompositionArcState> arcs =
                MaterializationHarness.ChildrenOfType<UsdCompositionArcState>(
                    scene.Context, node.Composition!);

            Assert.That(arcs[0].VariantSet, Is.Null);
            Assert.That(arcs[0].VariantSelection, Is.Null);
        }

        private static UsdPrimState MaterializeSinglePrim(UsdPrim prim, out MaterializedScene scene)
        {
            var stage = new UsdStage("Test");
            stage.AddRootPrim(prim);
            scene = MaterializationHarness.Materialize(stage);
            List<UsdPrimState> prims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(scene.Context, scene.Stage);
            Assert.That(prims, Has.Count.EqualTo(1));
            return prims[0];
        }
    }
}
