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
    /// Unknown-type fallback (§8.4) and failure-mode tests: nothing unrecognised is dropped,
    /// and a value that cannot be honoured fails closed rather than being coerced to a wrong one.
    /// </summary>
    [TestFixture]
    public class MaterializerFallbackTests
    {
        // ---- Unknown typed prim (§8.4) -------------------------------------------------

        [Test]
        public void UnknownTypedPrim_StaysUsdPrimType_AndKeepsToken()
        {
            var stage = new UsdStage("Vendor") { DefaultPrim = "Thing" };
            stage.AddRootPrim(new UsdPrim("Thing", "MyVendorThing"));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            UsdPrimState node = ms.Prim("/Thing");
            Assert.That(
                node.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
            Assert.That(node.TypeName!.Value, Is.EqualTo("MyVendorThing"));
        }

        [Test]
        public void UnknownTypedPrim_HoldsOnRealAsset_DistantLight()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Cell.usda"));

            UsdPrimState keyLight = ms.Prim("/Cell/KeyLight");
            Assert.That(
                keyLight.TypeDefinitionId,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.ObjectTypes.UsdPrimType, ms.Namespace)));
            Assert.That(keyLight.TypeName!.Value, Is.EqualTo("DistantLight"));

            // Its float attribute still maps onto a concrete DataType.
            UsdAttributeState intensity = ms.Attr("/Cell/KeyLight.intensity");
            Assert.That(intensity.DataType, Is.EqualTo(new NodeId(Opc.Ua.DataTypes.Float)));
            Assert.That(intensity.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            Assert.That(intensity.UsdTypeName!.Value, Is.EqualTo("float"));
        }

        // ---- Unknown API schema (§8.4) -------------------------------------------------

        [Test]
        public void UnknownApiSchema_DegradesToGenericAddIn_KeepingSchemaName()
        {
            var stage = new UsdStage("Vendor") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.ApiSchemas.Add(new UsdApiSchema("MyVendorWidgetAPI"));
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            List<UsdApiSchemaState> schemas = ms.AppliedSchemas<UsdApiSchemaState>("/P");
            Assert.That(schemas, Has.Count.EqualTo(1));

            UsdApiSchemaState node = schemas[0];
            // The generic base type, not a specialization — the schema is unrecognised.
            Assert.That(node, Is.TypeOf<UsdApiSchemaState>());
            Assert.That(node.SchemaName!.Value, Is.EqualTo("MyVendorWidgetAPI"));
        }

        [Test]
        public void UnknownApiSchema_HoldsOnRealAsset_MaterialBindingApi()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            List<UsdApiSchemaState> schemas =
                ms.AppliedSchemas<UsdApiSchemaState>("/Plant/Pumps/P101/StatusLight/Bulb");

            Assert.That(schemas, Has.Count.EqualTo(1));
            Assert.That(schemas[0], Is.TypeOf<UsdApiSchemaState>());
            Assert.That(schemas[0].SchemaName!.Value, Is.EqualTo("MaterialBindingAPI"));
        }

        // ---- Unknown value type (§8.4) -------------------------------------------------

        [Test]
        public void UnknownValueType_IsOpaque_ButKeepsUsdTypeName()
        {
            var stage = new UsdStage("Vendor") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("vendor:blob", "mvtype"));
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdAttributeState a = ms.Attr("/P.vendor:blob");

            Assert.That(a.DataType, Is.EqualTo(new NodeId(Opc.Ua.DataTypes.BaseDataType)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.Scalar));
            // The exact SdfValueTypeName is still recorded, so the mapping stays reversible.
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("mvtype"));
        }

        // ---- Fixed-size math type with the wrong arity fails closed --------------------

        [Test]
        public void WrongArityFixedMathType_LeavesValueUnset_ButKeepsType()
        {
            var stage = new UsdStage("Arity") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            // float3 declares three components; only two are authored — it cannot be honoured.
            prim.Attributes.Add(
                new UsdAttribute("badVec", "float3") { Value = UsdTestHelpers.NumberTuple(1.0, 2.0) });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdAttributeState a = ms.Attr("/P.badVec");

            // Fail closed: no value rather than a plausible-but-wrong one.
            Assert.That(a.BoxedValue(), Is.Null);
            // The declared type metadata is still materialized.
            Assert.That(a.DataType, Is.EqualTo(new NodeId(Opc.Ua.DataTypes.Float)));
            Assert.That(a.ValueRank, Is.EqualTo(Opc.Ua.ValueRanks.OneDimension));
            Assert.That(a.Dims(), Is.EqualTo(new uint[] { 3 }));
        }

        [Test]
        public void CorrectArityFixedMathType_SetsValue()
        {
            var stage = new UsdStage("Arity") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(
                new UsdAttribute("goodVec", "float3") { Value = UsdTestHelpers.NumberTuple(1.0, 2.0, 3.0) });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            Assert.That(ms.Attr("/P.goodVec").BoxedValue(), Is.Not.Null);
        }

        // ---- Empty / minimal scenes ----------------------------------------------------

        [Test]
        public void EmptyStage_Materializes_WithNoPrimsOrAttributes()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(new UsdStage("Empty"));

            Assert.That(ms.Stage.NodeId.IsNull, Is.False);
            Assert.That(ms.Result.PrimsByPath, Is.Empty);
            Assert.That(ms.Result.AttributesByPath, Is.Empty);

            var rootChildren = new List<BaseInstanceState>();
            ms.Root.GetChildren(ms.Context, rootChildren);
            Assert.That(rootChildren, Does.Contain(ms.Stage));
        }

        [Test]
        public void PrimWithNoAttributes_Materializes_WithNoAttributeChildren()
        {
            var stage = new UsdStage("Bare") { DefaultPrim = "P" };
            stage.AddRootPrim(new UsdPrim("P", "Xform"));
            MaterializedScene ms = MaterializationHarness.Materialize(stage);

            Assert.That(ms.Result.PrimsByPath.ContainsKey("/P"), Is.True);
            List<UsdAttributeState> attrs =
                MaterializationHarness.ChildrenOfType<UsdAttributeState>(ms.Context, ms.Prim("/P"));
            Assert.That(attrs, Is.Empty);
        }

        [Test]
        public void AttributeWithNoAuthoredValue_MaterializesTypeButNoValue()
        {
            MaterializedScene ms = MaterializationHarness.Materialize(TestAssets.Load("Plant.usda"));
            // Surface.outputs:surface is declared with no authored value.
            UsdAttributeState a =
                ms.Attr("/Plant/Pumps/P101/StatusLight/Mat/Surface.outputs:surface");

            Assert.That(a.BoxedValue(), Is.Null);
            Assert.That(
                a.DataType,
                Is.EqualTo(new NodeId(Opc.Ua.OpenUsd.Scene.DataTypes.UsdToken, ms.Namespace)));
            Assert.That(a.UsdTypeName!.Value, Is.EqualTo("token"));
        }

        // ---- Odd / duplicate BrowseNames -----------------------------------------------

        [Test]
        public void DuplicateAttributeNames_CollapseToSingleVariable_LastWins()
        {
            var stage = new UsdStage("Dup") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("dup", "double") { Value = UsdValue.From(1.0) });
            prim.Attributes.Add(new UsdAttribute("dup", "token") { Value = UsdValue.FromString("x") });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            List<UsdAttributeState> attrs =
                MaterializationHarness.ChildrenOfType<UsdAttributeState>(ms.Context, ms.Prim("/P"));

            // OPC UA requires unique BrowseNames among a node's children, so the collision does
            // not throw and does not duplicate — it collapses onto one Variable, last author wins.
            Assert.That(attrs, Has.Count.EqualTo(1));
            Assert.That(ms.Result.AttributesByPath.ContainsKey("/P.dup"), Is.True);
            Assert.That(ms.Attr("/P.dup").UsdTypeName!.Value, Is.EqualTo("token"));
        }

        [Test]
        public void OddlyNamespacedAttribute_SplitsOnLastColon()
        {
            var stage = new UsdStage("Odd") { DefaultPrim = "P" };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(new UsdAttribute("a:b:c", "double") { Value = UsdValue.From(1.0) });
            stage.AddRootPrim(prim);

            MaterializedScene ms = MaterializationHarness.Materialize(stage);
            UsdAttributeState a = ms.Attr("/P.a:b:c");

            Assert.That(a.Namespace!.Value, Is.EqualTo("a:b"));
        }
    }
}
