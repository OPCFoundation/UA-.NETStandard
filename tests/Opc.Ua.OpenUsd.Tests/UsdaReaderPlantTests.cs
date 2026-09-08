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
using Opc.Ua;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    [TestFixture]
    public class UsdaReaderPlantTests
    {
        private UsdStage _stage = null!;

        [OneTimeSetUp]
        public void LoadStage()
        {
            _stage = TestAssets.Load("Plant.usda");
        }

        [Test]
        public void StageMetadata_IsParsed()
        {
            Assert.That(_stage.DefaultPrim, Is.EqualTo("Plant"));
            Assert.That(_stage.UpAxis, Is.EqualTo("Z"));
            Assert.That(_stage.MetersPerUnit, Is.EqualTo(1.0));
            Assert.That(_stage.RootLayerIdentifier, Is.EqualTo("Plant.usda"));
            Assert.That(_stage.Documentation, Does.Contain("Base pump asset"));
        }

        [Test]
        public void PrimCountAndPaths_MatchReference()
        {
            List<string> paths = _stage.AllPrims().Select(p => p.Path).OrderBy(p => p, System.StringComparer.Ordinal).ToList();
            Assert.That(paths, Has.Count.EqualTo(12));
            Assert.That(paths, Is.EqualTo(s_stringValues1));
        }

        [Test]
        public void RootPrim_IsPlantXform()
        {
            Assert.That(_stage.RootPrims, Has.Count.EqualTo(1));
            UsdPrim plant = _stage.RootPrims[0];
            Assert.That(plant.Name, Is.EqualTo("Plant"));
            Assert.That(plant.TypeName, Is.EqualTo("Xform"));
            Assert.That(plant.Specifier, Is.EqualTo(UsdSpecifierEnum.Def));
        }

        [Test]
        public void Pumps_IsScope()
        {
            UsdPrim pumps = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps");
            Assert.That(pumps.TypeName, Is.EqualTo("Scope"));
            Assert.That(pumps.Specifier, Is.EqualTo(UsdSpecifierEnum.Def));
        }

        [Test]
        public void P101_HasKindAndReconstructedInstanceArc()
        {
            UsdPrim p101 = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101");
            Assert.That(p101.TypeName, Is.EqualTo("Xform"));
            Assert.That(p101.Kind, Is.EqualTo(UsdPrimKindEnum.Component));

            Assert.That(p101.Composition, Has.Count.EqualTo(2));

            UsdCompositionArc reference = p101.Composition[0];
            Assert.That(reference.ArcKind, Is.EqualTo(UsdArcKindEnum.Reference));
            Assert.That(reference.AssetPath, Is.EqualTo("pump.usda"));
            Assert.That(reference.PrimPath, Is.EqualTo("/Pump"));
            Assert.That(reference.ListPosition, Is.EqualTo(UsdListOpTypeEnum.Append));

            UsdCompositionArc instance = p101.Composition[1];
            Assert.That(instance.ArcKind, Is.EqualTo(UsdArcKindEnum.Instance));
            Assert.That(instance.AssetPath, Is.EqualTo("pump.usda"));
            Assert.That(instance.PrimPath, Is.EqualTo("/Pump"));
            Assert.That(instance.ListPosition, Is.EqualTo(UsdListOpTypeEnum.Append));
        }

        [Test]
        public void Body_AttributeTypeNamesAndValues()
        {
            UsdPrim body = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101/Body");
            Assert.That(body.TypeName, Is.EqualTo("Cylinder"));

            UsdAttribute axis = UsdTestHelpers.RequireAttribute(body, "axis");
            Assert.That(axis.TypeName, Is.EqualTo("token"));
            Assert.That(axis.Variability, Is.EqualTo(UsdVariabilityEnum.Uniform));
            UsdTestHelpers.AssertText(axis.Value, "Z");

            UsdAttribute radius = UsdTestHelpers.RequireAttribute(body, "radius");
            Assert.That(radius.TypeName, Is.EqualTo("double"));
            UsdTestHelpers.AssertDouble(radius.Value, 0.5);

            UsdAttribute color = UsdTestHelpers.RequireAttribute(body, "primvars:displayColor");
            Assert.That(color.TypeName, Is.EqualTo("color3f[]"));
            Assert.That(color.Value.TryGetArray(out ArrayOf<UsdValue> outer), Is.True);
            Assert.That(outer, Has.Count.EqualTo(1));
            Assert.That(outer[0].TryGetTuple(out ArrayOf<UsdValue> tuple), Is.True);
            Assert.That(tuple.ToArray()!.Select(v => v.TryGetInteger(out long integer) ? integer : -1L).ToArray(),
                Is.EqualTo(s_longValues1));

            UsdAttribute order = UsdTestHelpers.RequireAttribute(body, "xformOpOrder");
            Assert.That(order.TypeName, Is.EqualTo("token[]"));
            Assert.That(order.Variability, Is.EqualTo(UsdVariabilityEnum.Uniform));
            Assert.That(order.Value.TryGetArray(out ArrayOf<UsdValue> orderValues), Is.True);
            Assert.That(orderValues.ToArray()!.Select(v => v.TryGetText(out string token) ? token : string.Empty).ToArray(),
                Is.EqualTo(s_stringValues2));
        }

        [Test]
        public void Impeller_LiveAndCustomAttributes()
        {
            UsdPrim impeller = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101/Impeller");

            UsdAttribute rotateZ = UsdTestHelpers.RequireAttribute(impeller, "xformOp:rotateZ");
            Assert.That(rotateZ.TypeName, Is.EqualTo("double"));
            Assert.That(rotateZ.Variability, Is.EqualTo(UsdVariabilityEnum.Varying));
            Assert.That(rotateZ.Live, Is.True);
            UsdTestHelpers.AssertInteger(rotateZ.Value, 0L);

            UsdAttribute setpoint = UsdTestHelpers.RequireAttribute(impeller, "inputs:speedSetpoint");
            Assert.That(setpoint.Custom, Is.True);
            Assert.That(setpoint.TypeName, Is.EqualTo("double"));

            UsdAttribute order = UsdTestHelpers.RequireAttribute(impeller, "xformOpOrder");
            Assert.That(order.Value.TryGetArray(out ArrayOf<UsdValue> orderValues), Is.True);
            Assert.That(orderValues.ToArray()!.Select(v => v.TryGetText(out string token) ? token : string.Empty).ToArray(),
                Is.EqualTo(s_stringValues3));
        }

        [Test]
        public void Bulb_HasAppliedApiSchemaAndRelationship()
        {
            UsdPrim bulb = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101/StatusLight/Bulb");
            Assert.That(bulb.TypeName, Is.EqualTo("Sphere"));
            Assert.That(bulb.ApiSchemas.Select(s => s.SchemaName), Is.EqualTo(s_stringValues4));

            UsdRelationship binding = UsdTestHelpers.RequireRelationship(bulb, "material:binding");
            Assert.That(binding.Targets, Is.EqualTo(s_stringValues5));
        }

        [Test]
        public void Material_ConnectionIsParsed()
        {
            UsdPrim mat = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101/StatusLight/Mat");
            Assert.That(mat.TypeName, Is.EqualTo("Material"));

            UsdAttribute surface = UsdTestHelpers.RequireAttribute(mat, "outputs:surface");
            Assert.That(surface.TypeName, Is.EqualTo("token"));
            Assert.That(surface.Value.IsNull, Is.True);
            Assert.That(surface.Connections, Is.EqualTo(s_stringValues6));
        }

        [Test]
        public void Shader_ScalarTupleAndUnvaluedAttributes()
        {
            UsdPrim shader = UsdTestHelpers.RequirePrim(_stage, "/Plant/Pumps/P101/StatusLight/Mat/Surface");
            Assert.That(shader.TypeName, Is.EqualTo("Shader"));

            UsdAttribute id = UsdTestHelpers.RequireAttribute(shader, "info:id");
            Assert.That(id.TypeName, Is.EqualTo("token"));
            Assert.That(id.Variability, Is.EqualTo(UsdVariabilityEnum.Uniform));
            UsdTestHelpers.AssertText(id.Value, "UsdPreviewSurface");

            UsdAttribute diffuse = UsdTestHelpers.RequireAttribute(shader, "inputs:diffuseColor");
            Assert.That(diffuse.TypeName, Is.EqualTo("color3f"));
            Assert.That(diffuse.Value.TryGetTuple(out ArrayOf<UsdValue> diffuseValues), Is.True);
            Assert.That(diffuseValues.ToArray()!.Select(v => v.TryGetDouble(out double d) ? d : double.NaN).ToArray(),
                Is.EqualTo(s_doubleValues1));

            UsdAttribute emissive = UsdTestHelpers.RequireAttribute(shader, "inputs:emissiveColor");
            Assert.That(emissive.Value.TryGetTuple(out ArrayOf<UsdValue> emissiveValues), Is.True);
            Assert.That(emissiveValues.ToArray()!.Select(v => v.TryGetInteger(out long integer) ? integer : -1L).ToArray(),
                Is.EqualTo(s_longValues2));

            UsdAttribute outputs = UsdTestHelpers.RequireAttribute(shader, "outputs:surface");
            Assert.That(outputs.Value.IsNull, Is.True);
            Assert.That(outputs.Connections, Is.Empty);
        }

        private static readonly string[] s_stringValues1 =
        [
            "/Plant",
            "/Plant/Pumps",
            "/Plant/Pumps/P101",
            "/Plant/Pumps/P101/Bearing",
            "/Plant/Pumps/P101/Body",
            "/Plant/Pumps/P101/Impeller",
            "/Plant/Pumps/P101/Impeller/BladeA",
            "/Plant/Pumps/P101/Impeller/BladeB",
            "/Plant/Pumps/P101/StatusLight",
            "/Plant/Pumps/P101/StatusLight/Bulb",
            "/Plant/Pumps/P101/StatusLight/Mat",
            "/Plant/Pumps/P101/StatusLight/Mat/Surface",
        ];
        private static readonly long[] s_longValues1 = [0L, 0L, 1L];
        private static readonly string[] s_stringValues2 = ["xformOp:translate"];
        private static readonly string[] s_stringValues3 = ["xformOp:translate", "xformOp:rotateZ"];
        private static readonly string[] s_stringValues4 = ["MaterialBindingAPI"];
        private static readonly string[] s_stringValues5 = ["/Plant/Pumps/P101/StatusLight/Mat"];
        private static readonly string[] s_stringValues6 =
        [
            "/Plant/Pumps/P101/StatusLight/Mat/Surface.outputs:surface",
        ];
        private static readonly double[] s_doubleValues1 = [0.1, 0.1, 0.1];
        private static readonly long[] s_longValues2 = [0L, 0L, 0L];
    }
}
