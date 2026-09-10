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
    public class UsdaReaderCellTests
    {
        private const string R1 = "/Cell/Robots/R1";
        private const string R2 = "/Cell/Robots/R2";
        private const string R1Flange = "/Cell/Robots/R1/Base/J1/J2/J3/J4/J5/J6/Flange";

        private UsdStage _stage = null!;

        [OneTimeSetUp]
        public void LoadStage()
        {
            _stage = TestAssets.Load("Cell.usda");
        }

        [Test]
        public void StageMetadata_IsParsed()
        {
            Assert.That(_stage.DefaultPrim, Is.EqualTo("Cell"));
            Assert.That(_stage.UpAxis, Is.EqualTo("Z"));
            Assert.That(_stage.MetersPerUnit, Is.EqualTo(1.0));
            Assert.That(_stage.RootLayerIdentifier, Is.EqualTo("Cell.usda"));
        }

        [Test]
        public void ComposedPrimCount_MatchesReference()
        {
            Assert.That(_stage.AllPrims().Count(), Is.EqualTo(53));
        }

        [Test]
        public void CellRoot_HasCustomOverrideAttribute()
        {
            UsdPrim cell = UsdTestHelpers.RequirePrim(_stage, "/Cell");
            UsdAttribute over = UsdTestHelpers.RequireAttribute(cell, "inputs:speedOverride");
            Assert.That(over.Custom, Is.True);
            Assert.That(over.TypeName, Is.EqualTo("double"));
            UsdTestHelpers.AssertInteger(over.Value, 100L);
        }

        [Test]
        public void SafetyBeacon_TokenAndColorValues()
        {
            UsdPrim beacon = UsdTestHelpers.RequirePrim(_stage, "/Cell/SafetyBeacon");
            Assert.That(beacon.TypeName, Is.EqualTo("Sphere"));

            UsdAttribute visibility = UsdTestHelpers.RequireAttribute(beacon, "visibility");
            Assert.That(visibility.TypeName, Is.EqualTo("token"));
            UsdTestHelpers.AssertText(visibility.Value, "invisible");

            UsdAttribute color = UsdTestHelpers.RequireAttribute(beacon, "primvars:displayColor");
            Assert.That(color.Value.TryGetArray(out ArrayOf<UsdValue> outer), Is.True);
            Assert.That(outer.Count, Is.EqualTo(1));
            Assert.That(outer[0].TryGetTuple(out ArrayOf<UsdValue> tuple), Is.True);
            Assert.That(tuple.ToArray()!.Select(v => v.TryGetInteger(out long integer) ? integer : -1L).ToArray(),
                Is.EqualTo(s_longValues1));
        }

        [Test]
        public void KeyLight_NumericAndTupleValues()
        {
            UsdPrim key = UsdTestHelpers.RequirePrim(_stage, "/Cell/KeyLight");
            Assert.That(key.TypeName, Is.EqualTo("DistantLight"));

            UsdAttribute intensity = UsdTestHelpers.RequireAttribute(key, "intensity");
            Assert.That(intensity.TypeName, Is.EqualTo("float"));
            UsdTestHelpers.AssertInteger(intensity.Value, 650L);

            UsdAttribute rotate = UsdTestHelpers.RequireAttribute(key, "xformOp:rotateXYZ");
            Assert.That(rotate.TypeName, Is.EqualTo("double3"));
            Assert.That(rotate.Value.TryGetTuple(out ArrayOf<UsdValue> rotateValues), Is.True);
            Assert.That(rotateValues.ToArray()!.Select(v => v.TryGetInteger(out long integer) ? integer : 0L).ToArray(),
                Is.EqualTo(new[] { -45L, 0L, 35L }));
        }

        [TestCase(R1)]
        [TestCase(R2)]
        public void RobotMount_IsComponentWithReferenceAndInstanceArcs(string mountPath)
        {
            UsdPrim mount = UsdTestHelpers.RequirePrim(_stage, mountPath);
            Assert.That(mount.TypeName, Is.EqualTo("Xform"));
            Assert.That(mount.Kind, Is.EqualTo(UsdPrimKindEnum.Component));

            Assert.That(mount.Composition, Has.Count.EqualTo(2));
            Assert.That(mount.Composition[0].ArcKind, Is.EqualTo(UsdArcKindEnum.Reference));
            Assert.That(mount.Composition[0].AssetPath, Is.EqualTo("robot.usda"));
            Assert.That(mount.Composition[0].PrimPath, Is.EqualTo("/Robot"));
            Assert.That(mount.Composition[0].ListPosition, Is.EqualTo(UsdListOpTypeEnum.Append));
            Assert.That(mount.Composition[1].ArcKind, Is.EqualTo(UsdArcKindEnum.Instance));
            Assert.That(mount.Composition[1].AssetPath, Is.EqualTo("robot.usda"));
            Assert.That(mount.Composition[1].PrimPath, Is.EqualTo("/Robot"));
        }

        [TestCase(R1)]
        [TestCase(R2)]
        public void MergedRobotSubtree_IsPresentUnderMount(string mountPath)
        {
            Assert.That(_stage.Find(mountPath + "/Base"), Is.Not.Null);
            Assert.That(_stage.Find(mountPath + "/Warning"), Is.Not.Null);
            Assert.That(_stage.Find(mountPath + "/Materials/BaseGreen/Surface"), Is.Not.Null);
            Assert.That(_stage.Find(mountPath + "/Base/J1/J2/J3/J4/J5/J6/Flange"), Is.Not.Null);
        }

        [Test]
        public void MergedBase_HasCollectionApiAndRemappedBinding()
        {
            UsdPrim basePrim = UsdTestHelpers.RequirePrim(_stage, R1 + "/Base");

            List<string> schemas = basePrim.ApiSchemas.Select(s => s.SchemaName).ToList();
            Assert.That(schemas, Is.EqualTo(s_stringValues1));

            UsdApiSchema collection = basePrim.ApiSchemas.First(s => s.SchemaName == "CollectionAPI");
            Assert.That(collection.ExpansionRule, Is.EqualTo("expandPrims"));

            UsdRelationship binding = UsdTestHelpers.RequireRelationship(basePrim, "material:binding");
            Assert.That(binding.Targets, Is.EqualTo(new[] { R1 + "/Materials/BaseGreen" }));
        }

        [Test]
        public void MergedMaterial_ConnectionIsRemappedToMount()
        {
            UsdPrim green = UsdTestHelpers.RequirePrim(_stage, R1 + "/Materials/BaseGreen");
            UsdAttribute surface = UsdTestHelpers.RequireAttribute(green, "outputs:surface");
            Assert.That(surface.Connections, Is.EqualTo(new[]
            {
                R1 + "/Materials/BaseGreen/Surface.outputs:surface",
            }));
        }

        [Test]
        public void MergedJoint_RotateAttributeIsLive()
        {
            UsdPrim j1 = UsdTestHelpers.RequirePrim(_stage, R1 + "/Base/J1");
            UsdAttribute rotateZ = UsdTestHelpers.RequireAttribute(j1, "xformOp:rotateZ");
            Assert.That(rotateZ.Live, Is.True);

            UsdPrim j2 = UsdTestHelpers.RequirePrim(_stage, R1 + "/Base/J1/J2");
            UsdAttribute rotateY = UsdTestHelpers.RequireAttribute(j2, "xformOp:rotateY");
            Assert.That(rotateY.Live, Is.True);
            UsdTestHelpers.AssertInteger(rotateY.Value, -30L);
        }

        [Test]
        public void Tool_IsReferencedOnR1FlangeOnly()
        {
            UsdPrim tool = UsdTestHelpers.RequirePrim(_stage, R1Flange + "/Tool");
            Assert.That(tool.TypeName, Is.EqualTo("Xform"));
            Assert.That(tool.Kind, Is.EqualTo(UsdPrimKindEnum.Component));

            Assert.That(tool.Composition, Has.Count.EqualTo(1));
            Assert.That(tool.Composition[0].ArcKind, Is.EqualTo(UsdArcKindEnum.Reference));
            Assert.That(tool.Composition[0].AssetPath, Is.EqualTo("tool.usda"));
            Assert.That(tool.Composition[0].PrimPath, Is.EqualTo("/Gripper"));

            Assert.That(_stage.Find(R1Flange + "/Tool/Body"), Is.Not.Null);
            Assert.That(_stage.Find(R1Flange + "/Tool/JawUpper"), Is.Not.Null);
            Assert.That(_stage.Find(R1Flange + "/Tool/JawLower"), Is.Not.Null);

            // The tool is only attached to R1, never to R2.
            Assert.That(_stage.Find(R2 + "/Base/J1/J2/J3/J4/J5/J6/Flange/Tool"), Is.Null);
        }

        [Test]
        public void Tool_IsNotInstanceable()
        {
            UsdPrim tool = UsdTestHelpers.RequirePrim(_stage, R1Flange + "/Tool");
            Assert.That(tool.Composition.Any(a => a.ArcKind == UsdArcKindEnum.Instance), Is.False);
        }

        private static readonly long[] s_longValues1 = [1L, 0L, 0L];
        private static readonly string[] s_stringValues1 = ["MaterialBindingAPI", "CollectionAPI"];
    }
}
