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

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Typed schemas (§5.3): a prim of a known typed schema is instantiated as the generated
    /// State subclass (so the node <em>is</em> the type it declares and carries the subtype's
    /// members and accessors), while an unknown or untyped prim degrades to a concrete
    /// <c>UsdPrimType</c> (§8.4). The subtype's Optional members are deliberately not pre-created,
    /// because §7.1 step 3 makes the generic <c>&lt;UsdAttribute&gt;</c> pass authoritative for
    /// every attribute value.
    /// </summary>
    [TestFixture]
    public class TypedPrimInstanceTests
    {
        // ---- Known typed schema -> generated State subclass ----------------------------

        [Test]
        [TestCase("Xform", typeof(UsdGeomXformState))]
        [TestCase("Scope", typeof(UsdGeomScopeState))]
        [TestCase("Mesh", typeof(UsdGeomMeshState))]
        [TestCase("Cylinder", typeof(UsdGeomCylinderState))]
        [TestCase("Sphere", typeof(UsdGeomSphereState))]
        [TestCase("Cube", typeof(UsdGeomCubeState))]
        [TestCase("Cone", typeof(UsdGeomConeState))]
        [TestCase("Capsule", typeof(UsdGeomCapsuleState))]
        [TestCase("Material", typeof(UsdShadeMaterialState))]
        [TestCase("Shader", typeof(UsdShadeShaderState))]
        public void KnownTypedPrim_InstantiatesGeneratedStateSubclass(string typeName, Type expected)
        {
            var stage = new UsdStage("Typed") { DefaultPrim = "P" };
            stage.AddRootPrim(new UsdPrim("P", typeName));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/P");
            // The node's runtime type *is* the generated State subclass, not a retyped UsdPrimState.
            Assert.That(node.GetType(), Is.EqualTo(expected));
            // ...and it derives from UsdPrimState, so every prim-typed walk keeps working.
            Assert.That(node, Is.InstanceOf<UsdPrimState>());
            // The exact USD token survives and it is indexed for binding resolution.
            Assert.That(node.TypeName!.Value, Is.EqualTo(typeName));
            Assert.That(ms.Result.PrimsByPath.ContainsKey("/P"), Is.True);
            Assert.That(ReferenceEquals(ms.Result.PrimsByPath["/P"], node), Is.True);
        }

        // ---- Browsability: still a HasComponent child with a real NodeId ----------------

        [Test]
        public void KnownTypedPrim_IsBrowsableAsHasComponentChildOfStage()
        {
            var stage = new UsdStage("Typed") { DefaultPrim = "P" };
            stage.AddRootPrim(new UsdPrim("P", "Mesh"));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            List<UsdPrimState> stagePrims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, ms.Stage);
            Assert.That(stagePrims, Has.Count.EqualTo(1));

            UsdPrimState node = stagePrims[0];
            Assert.That(node, Is.InstanceOf<UsdGeomMeshState>());
            Assert.That(node.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(node.NodeId.IsNull, Is.False);
            // The browsed node and the indexed node are the same instance.
            Assert.That(ReferenceEquals(node, ms.Prim("/P")), Is.True);
        }

        [Test]
        public void NestedTypedPrim_IsBrowsableUnderTypedParent()
        {
            var stage = new UsdStage("Typed") { DefaultPrim = "P" };
            var parent = new UsdPrim("P", "Xform");
            parent.AddChild(new UsdPrim("C", "Sphere"));
            stage.AddRootPrim(parent);
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState parentNode = ms.Prim("/P");
            Assert.That(parentNode, Is.InstanceOf<UsdGeomXformState>());

            List<UsdPrimState> childPrims =
                MaterializationHarness.ChildrenOfType<UsdPrimState>(ms.Context, parentNode);
            Assert.That(childPrims, Has.Count.EqualTo(1));
            Assert.That(childPrims[0], Is.InstanceOf<UsdGeomSphereState>());
            Assert.That(childPrims[0].ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(ReferenceEquals(childPrims[0], ms.Prim("/P/C")), Is.True);
        }

        // ---- §8.4 fallback for unknown / untyped prims ---------------------------------

        [Test]
        public void UnknownTypedPrim_FallsBackToConcreteUsdPrimType_KeepingToken()
        {
            var stage = new UsdStage("Vendor") { DefaultPrim = "Thing" };
            stage.AddRootPrim(new UsdPrim("Thing", "MyVendorThing"));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/Thing");
            // Unknown schema degrades to *exactly* UsdPrimState — never a subtype and never an
            // abstract type — while keeping its token.
            Assert.That(node.GetType(), Is.EqualTo(typeof(UsdPrimState)));
            Assert.That(
                node.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
            Assert.That(node.TypeName!.Value, Is.EqualTo("MyVendorThing"));
        }

        [Test]
        public void UntypedPrim_FallsBackToConcreteUsdPrimType_WithNoToken()
        {
            var stage = new UsdStage("U") { DefaultPrim = "P" };
            stage.AddRootPrim(new UsdPrim("P", string.Empty));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/P");
            Assert.That(node.GetType(), Is.EqualTo(typeof(UsdPrimState)));
            Assert.That(
                node.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
            // An untyped prim authors no TypeName at all.
            Assert.That(node.TypeName, Is.Null);
        }

        // ---- No duplication between typed members and generic attributes ---------------

        [Test]
        public void TypedPrim_DoesNotPreCreateOptionalMembers_SoAttributesStayAuthoritative()
        {
            var stage = new UsdStage("M") { DefaultPrim = "P" };
            var mesh = new UsdPrim("P", "Mesh");
            // The generic attribute pass authors the value; the typed member "Points" must not
            // also be pre-created, or the node would carry the value twice (once empty).
            mesh.Attributes.Add(new UsdAttribute("points", "point3f[]"));
            stage.AddRootPrim(mesh);
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/P");
            Assert.That(node, Is.InstanceOf<UsdGeomMeshState>());

            // Exactly one authored attribute, the generic lowercase "points".
            List<UsdAttributeState> attrs =
                MaterializationHarness.ChildrenOfType<UsdAttributeState>(ms.Context, node);
            Assert.That(attrs.Select(a => a.BrowseName.Name), Is.EquivalentTo(s_stringValues1));

            // The capitalized typed member "Points" is deliberately absent — no duplicate.
            var allChildren = new List<BaseInstanceState>();
            node.GetChildren(ms.Context, allChildren);
            Assert.That(allChildren.Any(c => c.BrowseName.Name == "points"), Is.True);
            Assert.That(allChildren.Any(c => c.BrowseName.Name == "Points"), Is.False);
        }

        // ---- Exporter still walks every typed prim -------------------------------------

        [Test]
        public void Exporter_FindsEveryTypedPrim_AndRoundTripsTokens()
        {
            var stage = new UsdStage("Typed") { DefaultPrim = "World" };
            var world = new UsdPrim("World", "Xform");
            world.AddChild(new UsdPrim("Ball", "Sphere"));
            world.AddChild(new UsdPrim("Box", "Cube"));
            var mat = new UsdPrim("Mat", "Material");
            mat.AddChild(new UsdPrim("Surface", "Shader"));
            world.AddChild(mat);
            stage.AddRootPrim(world);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            Assert.That(ms.Prim("/World"), Is.InstanceOf<UsdGeomXformState>());
            Assert.That(ms.Prim("/World/Ball"), Is.InstanceOf<UsdGeomSphereState>());
            Assert.That(ms.Prim("/World/Mat/Surface"), Is.InstanceOf<UsdShadeShaderState>());

            // The exporter walks prims via ChildrenOfType<UsdPrimState>; the typed subclasses
            // derive from UsdPrimState, so every prim must still be found.
            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);
            List<string> exportedPaths = exported.AllPrims().Select(p => p.PathOf()).ToList();
            Assert.That(
                exportedPaths,
                Is.EquivalentTo(s_stringValues2));

            UsdPrim surface = exported.AllPrims().Single(p => p.Name == "Surface");
            Assert.That(surface.TypeName, Is.EqualTo("Shader"));
        }

        private static readonly string[] s_stringValues1 = ["points"];
        private static readonly string[] s_stringValues2 =
        [
            "/World",
            "/World/Ball",
            "/World/Box",
            "/World/Mat",
            "/World/Mat/Surface",
        ];
    }
}
