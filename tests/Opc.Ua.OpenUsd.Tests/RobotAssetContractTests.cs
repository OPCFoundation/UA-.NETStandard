/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Guards the USD assets that <c>MinimalRobotServer</c> ships and serves. The server's
    /// OpenUSD bindings address these prims and properties by name, so an asset edit that
    /// renames or drops one silently stops the twin from articulating. Parsing the shipped
    /// files here turns that into a build failure.
    /// </summary>
    /// <remarks>
    /// These are the files the server embeds, linked in from
    /// <c>samples/Robotics/MinimalRobotServer/Assets</c>. They are deliberately separate from the
    /// frozen reader/materializer fixtures in <c>Assets</c>.
    /// </remarks>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class RobotAssetContractTests
    {
        /// <summary>
        /// The six axis link prims and the rotate op each one's ActualPosition drives, as
        /// declared by <c>RobotCell.s_axisTemplate</c>.
        /// </summary>
        private static readonly (string PrimPath, string RotateOp)[] s_axisContract =
        [
            ("/Robot/Base/J1", "xformOp:rotateZ"),
            ("/Robot/Base/J1/J2", "xformOp:rotateY"),
            ("/Robot/Base/J1/J2/J3", "xformOp:rotateY"),
            ("/Robot/Base/J1/J2/J3/J4", "xformOp:rotateX"),
            ("/Robot/Base/J1/J2/J3/J4/J5", "xformOp:rotateY"),
            ("/Robot/Base/J1/J2/J3/J4/J5/J6", "xformOp:rotateX")
        ];

        private static string SamplePath(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Sample", name);
        }

        private static UsdStage LoadSample(string name)
        {
            return UsdaReader.ParseFile(SamplePath(name));
        }

        /// <summary>
        /// Renders a parsed attribute value as text regardless of how the reader models
        /// arrays, so an assertion does not depend on that representation.
        /// </summary>
        private static string Flatten(object? value)
        {
            return value switch
            {
                null => string.Empty,
                UsdValue usdValue => Flatten(usdValue),
                string text => text,
                System.Collections.IEnumerable items =>
                    string.Join(",", items.Cast<object?>().Select(Flatten)),
                _ => value.ToString() ?? string.Empty
            };
        }

        private static string Flatten(UsdValue value)
        {
            if (value.TryGetText(out string text))
            {
                return text;
            }
            if (value.TryGetItems(out ArrayOf<UsdValue> items))
            {
                return string.Join(",", items.ToArray()!.Select(Flatten));
            }
            return value.ToString();
        }

        [Test]
        [TestCase("robot.usda", "Robot")]
        [TestCase("tool.usda", "Gripper")]
        [TestCase("Cell.usda", "Cell")]
        public void ShippedAssetParsesWithTheExpectedStageMetadata(string asset, string defaultPrim)
        {
            UsdStage stage = LoadSample(asset);

            Assert.That(stage.DefaultPrim, Is.EqualTo(defaultPrim));
            Assert.That(stage.UpAxis, Is.EqualTo("Z"));
            Assert.That(stage.MetersPerUnit, Is.EqualTo(1.0));
        }

        [Test]
        public void RobotExposesEveryAxisLinkAndItsRotateOp()
        {
            UsdStage stage = LoadSample("robot.usda");

            foreach ((string primPath, string rotateOp) in s_axisContract)
            {
                UsdPrim? link = stage.Find(primPath);
                Assert.That(link, Is.Not.Null, $"Axis link prim {primPath} is missing.");

                UsdAttribute? rotate = link!.Attributes.FirstOrDefault(a => a.Name == rotateOp);
                Assert.That(rotate, Is.Not.Null,
                    $"Axis link prim {primPath} does not carry {rotateOp}.");
                Assert.That(rotate!.TypeName, Is.EqualTo("double"),
                    $"{primPath}.{rotateOp} must stay a scalar double; the connector writes it as one.");

                UsdAttribute? order = link.Attributes.FirstOrDefault(a => a.Name == "xformOpOrder");
                Assert.That(order, Is.Not.Null, $"{primPath} has no xformOpOrder.");
                Assert.That(
                    Flatten(order!.Value),
                    Does.Contain(rotateOp),
                    $"{primPath} does not list {rotateOp} in its xformOpOrder.");
            }
        }

        [Test]
        public void RobotExposesTheToolFlangeMountPoint()
        {
            UsdStage stage = LoadSample("robot.usda");

            // RobotCell.ToolSuffix composes the gripper at this path plus "/Tool".
            Assert.That(stage.Find("/Robot/Base/J1/J2/J3/J4/J5/J6/Flange"), Is.Not.Null,
                "The tool flange mount point is missing.");
        }

        [Test]
        public void RobotExposesTheEmergencyStopWarningVisibility()
        {
            UsdStage stage = LoadSample("robot.usda");

            UsdPrim? warning = stage.Find("/Robot/Warning");
            Assert.That(warning, Is.Not.Null, "The emergency-stop warning prim is missing.");

            UsdAttribute? visibility =
                warning!.Attributes.FirstOrDefault(a => a.Name == "visibility");
            Assert.That(visibility, Is.Not.Null, "/Robot/Warning has no visibility attribute.");
            Assert.That(visibility!.TypeName, Is.EqualTo("token"));
            UsdTestHelpers.AssertText(visibility.Value, "invisible");
        }

        [Test]
        public void GripperExposesTheReferencedRootPrim()
        {
            UsdStage stage = LoadSample("tool.usda");

            // The connector references @tool.usda@</Gripper> onto the flange.
            Assert.That(stage.Find("/Gripper"), Is.Not.Null, "The gripper root prim is missing.");
        }

        [Test]
        public void CellExposesTheSpeedOverrideCommandTarget()
        {
            UsdStage stage = LoadSample("Cell.usda");

            UsdPrim? cell = stage.Find("/Cell");
            Assert.That(cell, Is.Not.Null);

            UsdAttribute? speed =
                cell!.Attributes.FirstOrDefault(a => a.Name == "inputs:speedOverride");
            Assert.That(speed, Is.Not.Null, "/Cell has no inputs:speedOverride command target.");
            Assert.That(speed!.Custom, Is.True);
            Assert.That(speed.TypeName, Is.EqualTo("double"));
        }

        [Test]
        public void CellExposesTheSafetyBeaconVisibility()
        {
            UsdStage stage = LoadSample("Cell.usda");

            UsdPrim? beacon = stage.Find("/Cell/SafetyBeacon");
            Assert.That(beacon, Is.Not.Null, "The safety beacon prim is missing.");

            UsdAttribute? visibility =
                beacon!.Attributes.FirstOrDefault(a => a.Name == "visibility");
            Assert.That(visibility, Is.Not.Null, "/Cell/SafetyBeacon has no visibility attribute.");
            UsdTestHelpers.AssertText(visibility!.Value, "invisible");
        }

        [Test]
        [TestCase("/Cell/Robots/R1")]
        [TestCase("/Cell/Robots/R2")]
        public void CellExposesTheRobotMountPointWithItsPositioningAttributes(string primPath)
        {
            UsdStage stage = LoadSample("Cell.usda");

            UsdPrim? mount = stage.Find(primPath);
            Assert.That(mount, Is.Not.Null, $"Robot mount point {primPath} is missing.");

            foreach (string expected in new[]
            {
                "inputs:longitude",
                "inputs:latitude",
                "inputs:elevation"
            })
            {
                Assert.That(
                    mount!.Attributes.Any(a => a.Name == expected), Is.True,
                    $"{primPath} does not carry {expected}.");
            }
        }

        [Test]
        public void TheStageOffersItsEstablishingShotFirst()
        {
            UsdStage stage = LoadSample("Cell.usda");

            // A connector opens on the first camera the served root layer authors, because
            // framing the bounds of an enclosed scene automatically puts the eye inside the
            // fence. The overview therefore has to come before the overhead camera.
            List<UsdPrim> cameras = stage.AllPrims()
                .Where(p => string.Equals(p.TypeName, "Camera", StringComparison.Ordinal))
                .ToList();

            Assert.That(cameras, Is.Not.Empty, "The cell authors no camera to open on.");
            Assert.That(cameras[0].Path, Is.EqualTo("/Cell/OverviewCamera"));
        }

        [Test]
        [TestCase("Cell.usda", "/Cell/Robots/R1")]
        [TestCase("Cell.usda", "/Cell/Robots/R2")]
        [TestCase("Cell.usda", "/Cell/Parts/Part01")]
        [TestCase("Cell.usda", "/Cell/Parts/Part02")]
        [TestCase("Cell.usda", "/Cell/Parts/Part03")]
        [TestCase("tool.usda", "/Gripper/JawUpper")]
        [TestCase("tool.usda", "/Gripper/JawLower")]
        public void LiveBoundPrimDeclaresASingleMatrixTransformOp(string asset, string primPath)
        {
            UsdStage stage = LoadSample(asset);

            UsdPrim? mount = stage.Find(primPath);
            Assert.That(mount, Is.Not.Null);

            // The connector authors this prim's pose into the root layer. A translate or
            // rotate op order declared here, in a weaker sublayer, could not be cleared
            // from there, so the mount point must expose exactly one matrix op to set.
            Assert.That(
                mount!.Attributes.Any(a => a.Name == "xformOp:transform"), Is.True,
                $"{primPath} must declare xformOp:transform for the connector to set.");

            UsdAttribute? order =
                mount.Attributes.FirstOrDefault(a => a.Name == "xformOpOrder");
            Assert.That(order, Is.Not.Null, $"{primPath} has no xformOpOrder.");
            Assert.That(
                Flatten(order!.Value), Is.EqualTo("xformOp:transform"),
                $"{primPath} must order exactly the matrix op.");
        }

        [Test]
        [TestCase("/Gripper/JawUpper")]
        [TestCase("/Gripper/JawLower")]
        public void GripperJawCarriesItsFingerSoTheWholeJawStrokes(string jawPath)
        {
            UsdStage stage = LoadSample("tool.usda");

            // The jaw is driven as one prim. If the carrier and the finger were siblings
            // under the gripper body, stroking the bound prim would slide the carrier out
            // from under the finger it is bolted to.
            Assert.That(stage.Find(jawPath + "/Carrier"), Is.Not.Null,
                $"{jawPath} must own its carrier.");
            Assert.That(stage.Find(jawPath + "/Finger"), Is.Not.Null,
                $"{jawPath} must own its finger.");
        }

        [Test]
        public void RobotKeepsTheKukaKr16ReachSoTheCellStaysCorrectlyScaled()
        {
            UsdStage stage = LoadSample("robot.usda");

            // 260 + 680 + 670 mm between the A1, A2, A3 and A4 axes is the published
            // 1611 mm reach of the reference robot.
            Assert.That(TranslateX(stage, "/Robot/Base/J1/J2"), Is.EqualTo(0.26).Within(1e-6));
            Assert.That(TranslateX(stage, "/Robot/Base/J1/J2/J3"), Is.EqualTo(0.68).Within(1e-6));
            Assert.That(TranslateX(stage, "/Robot/Base/J1/J2/J3/J4"), Is.EqualTo(0.67).Within(1e-6));
        }

        private static double TranslateX(UsdStage stage, string primPath)
        {
            UsdPrim? prim = stage.Find(primPath);
            Assert.That(prim, Is.Not.Null, $"{primPath} is missing.");

            UsdAttribute? translate =
                prim!.Attributes.FirstOrDefault(a => a.Name == "xformOp:translate");
            Assert.That(translate, Is.Not.Null, $"{primPath} has no xformOp:translate.");

            Assert.That(translate!.Value.TryGetItems(out ArrayOf<UsdValue> components), Is.True);
            Assert.That(components.Count, Is.EqualTo(3));
            Assert.That(components[0].TryGetNumber(out double x), Is.True);
            return x;
        }
    }
}
