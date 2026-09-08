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
    /// Materializer tests for scene structure (§5, §7.1), typed prims (§5.3), the §6.2
    /// type/value mapping, relationships (§5.5) and live attributes (§9 Mode A).
    /// </summary>
    [TestFixture]
    public class MaterializerTests
    {
        // ---- Scene structure (§5, §7.1) ------------------------------------------------

        [Test]
        [TestCase("Plant.usda", "Plant")]
        [TestCase("Cell.usda", "Cell")]
        public void Stage_CarriesMetadata_AndIsBrowsableFromRoot(string asset, string defaultPrim)
        {
            UsdStage parsed = TestAssets.Load(asset);
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);

            Assert.That(ms.Stage.BrowseName.Name, Is.EqualTo(parsed.StageName));
            Assert.That(ms.Stage.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(ms.Stage.NodeId.IsNull, Is.False);

            var rootChildren = new List<BaseInstanceState>();
            ms.Root.GetChildren(ms.Context, rootChildren);
            Assert.That(rootChildren, Does.Contain(ms.Stage));

            Assert.That(ms.Stage.DefaultPrim!.Value, Is.EqualTo(defaultPrim));
            Assert.That(ms.Stage.DefaultPrim.Value, Is.EqualTo(parsed.DefaultPrim));
            Assert.That(ms.Stage.UpAxis!.Value, Is.EqualTo(parsed.UpAxis));
            Assert.That(ms.Stage.MetersPerUnit!.Value, Is.EqualTo(parsed.MetersPerUnit));
        }

        [Test]
        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void PrimTree_MatchesParsedScene_PrimForPrim(string asset)
        {
            UsdStage parsed = TestAssets.Load(asset);
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);

            List<UsdPrim> parsedPrims = parsed.AllPrims().ToList();
            Assert.That(ms.Result.PrimsByPath, Has.Count.EqualTo(parsedPrims.Count));

            foreach (UsdPrim prim in parsedPrims)
            {
                string path = prim.PathOf();
                Assert.That(ms.Result.PrimsByPath.ContainsKey(path), Is.True, path);

                UsdPrimState node = ms.Prim(path);
                Assert.That(node.BrowseName.Name, Is.EqualTo(prim.Name), path);
                Assert.That(node.BrowseName.NamespaceIndex, Is.EqualTo(ms.Namespace), path);
                Assert.That(node.NodeId.IsNull, Is.False, path);

                // Every prim is a HasComponent child, so the prim tree is the browse tree (§7.1).
                Assert.That(
                    node.ReferenceTypeId,
                    Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent),
                    path);
                if (!string.IsNullOrEmpty(prim.TypeName))
                {
                    Assert.That(node.TypeName!.Value, Is.EqualTo(prim.TypeName), path);
                }

                NodeState parent = prim.Parent == null ? ms.Stage : ms.Prim(prim.Parent.PathOf());
                var children = new List<BaseInstanceState>();
                parent.GetChildren(ms.Context, children);
                Assert.That(children, Does.Contain(node), path);
            }
        }

        [Test]
        [TestCase("Plant.usda")]
        [TestCase("Cell.usda")]
        public void AttributeIndex_IsPopulated_WithExpectedKeys(string asset)
        {
            UsdStage parsed = TestAssets.Load(asset);
            MaterializedScene ms = MaterializationHarness.Materialize(parsed);

            int expected = 0;
            foreach (UsdPrim prim in parsed.AllPrims())
            {
                string path = prim.PathOf();
                foreach (UsdAttribute attribute in prim.Attributes)
                {
                    expected++;
                    string key = path + "." + attribute.Name;
                    Assert.That(ms.Result.AttributesByPath.ContainsKey(key), Is.True, key);
                }
            }
            Assert.That(ms.Result.AttributesByPath, Has.Count.EqualTo(expected));
        }

        [Test]
        public void KnownAttributeKeys_AreExactlyAsSpecified()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            // The §10 binding index is keyed by absolute SdfPath "<primPath>.<attributeName>".
            Assert.That(
                ms.Result.AttributesByPath.ContainsKey("/Plant/Pumps/P101/Impeller.xformOp:rotateZ"),
                Is.True);
            Assert.That(
                ms.Result.AttributesByPath.ContainsKey("/Plant/Pumps/P101/Body.primvars:displayColor"),
                Is.True);
        }

        // ---- Typed prims (§5.3) --------------------------------------------------------

        [Test]
        [TestCase("Xform", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomXformType)]
        [TestCase("Scope", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomScopeType)]
        [TestCase("Mesh", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomMeshType)]
        [TestCase("Cylinder", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomCylinderType)]
        [TestCase("Sphere", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomSphereType)]
        [TestCase("Cube", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomCubeType)]
        [TestCase("Cone", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomConeType)]
        [TestCase("Capsule", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomCapsuleType)]
        [TestCase("Material", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdShadeMaterialType)]
        [TestCase("Shader", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdShadeShaderType)]
        public void TypedPrim_GetsHasTypeDefinition_AndKeepsToken(string typeName, uint objectType)
        {
            var stage = new UsdStage("Typed") { DefaultPrim = "P" };
            stage.AddRootPrim(new UsdPrim("P", typeName));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/P");
            Assert.That(node.TypeDefinitionId, Is.EqualTo(new NodeId(objectType, ms.Namespace)));
            // The exact USD token survives even when a dedicated ObjectType exists for it.
            Assert.That(node.TypeName!.Value, Is.EqualTo(typeName));
        }

        [Test]
        public void TypedPrim_TypeDefinitions_HoldOnRealAssets()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            AssertTypeDefinition(ms, "/Plant", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomXformType);
            AssertTypeDefinition(ms, "/Plant/Pumps", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomScopeType);
            AssertTypeDefinition(
                ms, "/Plant/Pumps/P101/Body", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomCylinderType);
            AssertTypeDefinition(
                ms, "/Plant/Pumps/P101/Bearing", Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdGeomSphereType);
            AssertTypeDefinition(
                ms,
                "/Plant/Pumps/P101/StatusLight/Mat",
                Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdShadeMaterialType);
            AssertTypeDefinition(
                ms,
                "/Plant/Pumps/P101/StatusLight/Mat/Surface",
                Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdShadeShaderType);
        }

        // ---- Attributes and the §6.2 mapping -------------------------------------------

        [Test]
        public void Mapping_DoubleScalar()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101/Body.radius");

            Assert.That(a.DataType, Is.EqualTo(new NodeId(Opc.Ua.DataTypes.Double)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(a.Dims(), Is.Null);
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("double"));
            Assert.That(a.Variability!.Value, Is.EqualTo(UsdVariabilityEnum.Varying));
        }

        [Test]
        public void Mapping_Double3_IsRank1_WithDims3()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101.xformOp:translate");

            Assert.That(a.DataType, Is.EqualTo(new NodeId(Opc.Ua.DataTypes.Double)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.OneDimension));
            Assert.That(a.Dims(), Is.EqualTo(new uint[] { 3 }));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("double3"));
            // xformOp:rotateZ etc. carry the authored property namespace (§5.4).
            Assert.That(a.Namespace!.Value, Is.EqualTo("xformOp"));
        }

        [Test]
        public void Mapping_TokenScalar_UsesUsdTokenDataType()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101/Body.axis");

            Assert.That(
                a.DataType,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.DataTypes.UsdToken, ms.Namespace)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("token"));
            // "uniform token axis" — authored uniform.
            Assert.That(a.Variability!.Value, Is.EqualTo(UsdVariabilityEnum.Uniform));
        }

        [Test]
        public void Mapping_TokenArray_IsRank1()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101.xformOpOrder");

            Assert.That(
                a.DataType,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.DataTypes.UsdToken, ms.Namespace)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.OneDimension));
            Assert.That(a.Dims(), Is.EqualTo(new uint[] { 0 }));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("token[]"));
        }

        [Test]
        public void Mapping_Color3f_IsRank1_WithDims3()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a =
                ms.Attr("/Plant/Pumps/P101/StatusLight/Mat/Surface.inputs:diffuseColor");

            Assert.That(
                a.DataType,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.DataTypes.UsdColor3f, ms.Namespace)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.OneDimension));
            Assert.That(a.Dims(), Is.EqualTo(new uint[] { 3 }));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("color3f"));
            Assert.That(a.Namespace!.Value, Is.EqualTo("inputs"));
        }

        [Test]
        public void Mapping_Color3fArray_IsRank2_WithDims0By3()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101/Body.primvars:displayColor");

            Assert.That(
                a.DataType,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.DataTypes.UsdColor3f, ms.Namespace)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.TwoDimensions));
            Assert.That(a.Dims(), Is.EqualTo(new uint[] { 0, 3 }));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("color3f[]"));
            Assert.That(a.Namespace!.Value, Is.EqualTo("primvars"));
        }

        [Test]
        public void Mapping_CustomAttribute_CarriesCustomFlag()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101/Impeller.inputs:speedSetpoint");

            Assert.That(a.Custom!.Value, Is.True);
            Assert.That(a.Namespace!.Value, Is.EqualTo("inputs"));
        }

        [Test]
        public void Mapping_NonNamespacedAttribute_HasNoNamespaceProperty()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            UsdAttributeState a = ms.Attr("/Plant/Pumps/P101/Body.radius");

            // "radius" has no ":" namespace, so the optional Namespace Property is not authored.
            Assert.That(a.Namespace, Is.Null);
            Assert.That(a.Custom, Is.Null);
        }

        // ---- Relationships (§5.5) ------------------------------------------------------

        [Test]
        public void Relationship_ResolvableTarget_KeepsPath_AndAddsReference()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));

            UsdPrimState bulb = ms.Prim("/Plant/Pumps/P101/StatusLight/Bulb");
            List<UsdRelationshipState> rels =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(ms.Context, bulb);
            Assert.That(rels, Has.Count.EqualTo(1));

            UsdRelationshipState rel = rels[0];
            Assert.That(rel.BrowseName.Name, Is.EqualTo("material:binding"));
            Assert.That(
                rel.TargetPaths!.Value.ToArray(),
                Is.EqualTo(s_stringValues1));

            // A target inside the subtree also becomes a browsable UsdRelationshipTarget edge.
            NodeId mat = ms.Prim("/Plant/Pumps/P101/StatusLight/Mat").NodeId;
            Assert.That(HasReference(rel, ms.Context, RelationshipTargetType(ms), mat), Is.True);
        }

        [Test]
        public void Relationship_ExternalTarget_KeepsPath_ButHasNoDanglingReference()
        {
            var stage = new UsdStage("Rel") { DefaultPrim = "A" };
            var a = new UsdPrim("A", "Xform");
            var b = new UsdPrim("B", "Xform");
            var rel = new UsdRelationship("mixed");
            rel.Targets.Add("/A/B");                 // resolvable, inside the subtree
            rel.Targets.Add("/Outside/Thing");       // outside the materialized subtree
            a.Relationships.Add(rel);
            a.AddChild(b);
            stage.AddRootPrim(a);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            List<UsdRelationshipState> rels =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(ms.Context, ms.Prim("/A"));
            UsdRelationshipState node = rels[0];

            // Ordered paths keep full fidelity, including the unresolvable one.
            Assert.That(
                node.TargetPaths!.Value.ToArray(),
                Is.EqualTo(s_stringValues2));

            NodeId inside = ms.Prim("/A/B").NodeId;
            Assert.That(HasReference(node, ms.Context, RelationshipTargetType(ms), inside), Is.True);

            // Exactly one resolved edge — the external target produced no dangling reference.
            Assert.That(CountReferences(node, ms.Context, RelationshipTargetType(ms)), Is.EqualTo(1));
        }

        [Test]
        public void Relationship_ZeroTargets_MaterializesEmptyPathList()
        {
            var stage = new UsdStage("Rel0") { DefaultPrim = "A" };
            var a = new UsdPrim("A", "Xform");
            a.Relationships.Add(new UsdRelationship("empty"));
            stage.AddRootPrim(a);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdRelationshipState node =
                MaterializationHarness.ChildrenOfType<UsdRelationshipState>(ms.Context, ms.Prim("/A"))[0];

            Assert.That(node.TargetPaths!.Value.ToArray(), Is.Empty);
            Assert.That(CountReferences(node, ms.Context, RelationshipTargetType(ms)), Is.Zero);
        }

        // ---- Live attributes (§9 Mode A) -----------------------------------------------

        [Test]
        public void LiveAttribute_WithHistorize_IsWritable_AndHistorizing()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                LiveStage(), new UsdMaterializationOptions { HistorizeLiveAttributes = true });

            UsdAttributeState a = ms.Attr("/Rig.speed");
            Assert.That(a.Historizing, Is.True);
            Assert.That(a.AccessLevel & Opc.Ua.AccessLevels.CurrentWrite, Is.Not.Zero);
            Assert.That(a.AccessLevel & Opc.Ua.AccessLevels.HistoryRead, Is.Not.Zero);
        }

        [Test]
        public void LiveAttribute_WithoutHistorize_IsWritable_ButNotHistorizing()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(
                LiveStage(), new UsdMaterializationOptions { HistorizeLiveAttributes = false });

            UsdAttributeState a = ms.Attr("/Rig.speed");
            Assert.That(a.Historizing, Is.False);
            Assert.That(a.AccessLevel & Opc.Ua.AccessLevels.CurrentWrite, Is.Not.Zero);
            Assert.That(a.AccessLevel & Opc.Ua.AccessLevels.HistoryRead, Is.Zero);
        }

        // ---- helpers -------------------------------------------------------------------

        private static UsdStage LiveStage()
        {
            var stage = new UsdStage("Live") { DefaultPrim = "Rig" };
            var rig = new UsdPrim("Rig", "Xform");
            rig.Attributes.Add(new UsdAttribute("speed", "double") { Value = UsdValue.From(0.0), Live = true });
            stage.AddRootPrim(rig);
            return stage;
        }

        private static void AssertTypeDefinition(MaterializedScene ms, string path, uint objectType)
        {
            Assert.That(
                ms.Prim(path).TypeDefinitionId,
                Is.EqualTo(new NodeId(objectType, ms.Namespace)),
                path);
        }

        private static NodeId RelationshipTargetType(MaterializedScene ms)
        {
            return ExpandedNodeId.ToNodeId(
                Opc.Ua.OpenUsd.Scene.ReferenceTypeIds.UsdRelationshipTarget, ms.Context.NamespaceUris);
        }

        private static bool HasReference(
            NodeState node, ISystemContext context, NodeId referenceType, NodeId target)
        {
            return CountReferences(node, context, referenceType, target) > 0;
        }

        private static int CountReferences(
            NodeState node, ISystemContext context, NodeId referenceType, NodeId? target = null)
        {
            var references = new List<IReference>();
            node.GetReferences(context, references);
            int count = 0;
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId != referenceType || reference.IsInverse)
                {
                    continue;
                }
                if (target == null || reference.TargetId == new ExpandedNodeId(target.Value))
                {
                    count++;
                }
            }
            return count;
        }

        private static readonly string[] s_stringValues1 = ["/Plant/Pumps/P101/StatusLight/Mat"];
        private static readonly string[] s_stringValues2 = ["/A/B", "/Outside/Thing"];
    }
}
