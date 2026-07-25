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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning;
using Opc.Ua.Positioning.Server;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("Robotics")]
    [Category("Positioning")]
    public sealed class RobotMobilityTests
    {
        [Test]
        public void FixedModeUsesConfiguredPose()
        {
            global::Robotics.RobotMobilityOptions options = CreateOptions();
            options.R1.Mode = global::Robotics.RobotMotionMode.Fixed;
            options.R1.OriginX = 4.0;
            options.R1.OriginY = -3.0;
            options.R1.OriginZ = 2.0;
            options.R1.FixedHeadingDegrees = 35.0;

            ThreeDFrame frame = CreateProvider(options).EvaluateLocalFrame(
                "R1",
                TimeSpan.FromSeconds(123));

            Assert.Multiple(() =>
            {
                Assert.That(frame.CartesianCoordinates.X, Is.EqualTo(4.0));
                Assert.That(frame.CartesianCoordinates.Y, Is.EqualTo(-3.0));
                Assert.That(frame.CartesianCoordinates.Z, Is.EqualTo(2.0));
                Assert.That(frame.Orientation.C, Is.EqualTo(35.0));
            });
        }

        [Test]
        public void FigureEightModeFollowsConfiguredAmplitudes()
        {
            global::Robotics.RobotMobilityOptions options = CreateOptions();
            options.R1.Mode = global::Robotics.RobotMotionMode.FigureEight;
            options.R1.OriginX = 1.0;
            options.R1.OriginY = 2.0;
            options.R1.AmplitudeX = 0.6;
            options.R1.AmplitudeY = 0.4;
            options.R1.PeriodSeconds = 20.0;

            ThreeDFrame frame = CreateProvider(options).EvaluateLocalFrame(
                "R1",
                TimeSpan.FromSeconds(5.0));

            Assert.Multiple(() =>
            {
                Assert.That(frame.CartesianCoordinates.X, Is.EqualTo(1.6).Within(1e-12));
                Assert.That(frame.CartesianCoordinates.Y, Is.EqualTo(2.0).Within(1e-12));
                Assert.That(
                    !double.IsNaN(frame.Orientation.C) &&
                    !double.IsInfinity(frame.Orientation.C),
                    Is.True);
            });
        }

        [Test]
        public void CircleModeUsesRadiusAndTangentHeading()
        {
            global::Robotics.RobotMobilityOptions options = CreateOptions();
            options.R1.Mode = global::Robotics.RobotMotionMode.Circle;
            options.R1.Radius = 0.75;
            options.R1.PeriodSeconds = 8.0;

            ThreeDFrame frame = CreateProvider(options).EvaluateLocalFrame(
                "R1",
                TimeSpan.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(
                    frame.CartesianCoordinates.X,
                    Is.EqualTo(options.R1.OriginX + 0.75).Within(1e-12));
                Assert.That(
                    frame.CartesianCoordinates.Y,
                    Is.EqualTo(options.R1.OriginY).Within(1e-12));
                Assert.That(frame.Orientation.C, Is.EqualTo(90.0).Within(1e-12));
            });
        }

        [Test]
        public void ShuttleModeReversesHeadingWithDirection()
        {
            global::Robotics.RobotMobilityOptions options = CreateOptions();
            options.R1.Mode = global::Robotics.RobotMotionMode.Shuttle;
            options.R1.ShuttleDistance = 1.0;
            options.R1.PeriodSeconds = 4.0;

            global::Robotics.MobileRobotPositionProvider provider =
                CreateProvider(options);
            ThreeDFrame forward = provider.EvaluateLocalFrame(
                "R1",
                TimeSpan.Zero);
            ThreeDFrame reverse = provider.EvaluateLocalFrame(
                "R1",
                TimeSpan.FromSeconds(2.0));

            Assert.Multiple(() =>
            {
                Assert.That(forward.Orientation.C, Is.Zero.Within(1e-12));
                Assert.That(
                    Math.Abs(reverse.Orientation.C),
                    Is.EqualTo(180.0).Within(1e-12));
            });
        }

        [Test]
        public void RobotsUseIndependentModesAndOrigins()
        {
            global::Robotics.RobotMobilityOptions options = CreateOptions();
            options.R1.Mode = global::Robotics.RobotMotionMode.Fixed;
            options.R2.Mode = global::Robotics.RobotMotionMode.Circle;
            options.R2.Radius = 0.25;

            global::Robotics.MobileRobotPositionProvider provider =
                CreateProvider(options);
            ThreeDFrame r1 = provider.EvaluateLocalFrame("R1", TimeSpan.Zero);
            ThreeDFrame r2 = provider.EvaluateLocalFrame("R2", TimeSpan.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(r1.CartesianCoordinates.X, Is.EqualTo(-1.2));
                Assert.That(r2.CartesianCoordinates.X, Is.Not.EqualTo(r1.CartesianCoordinates.X));
                Assert.That(r2.CartesianCoordinates.X, Is.EqualTo(1.45).Within(1e-12));
            });
        }

        [Test]
        public async Task GlobalSamplePreservesElevationAndLocalHeightAsync()
        {
            global::Robotics.MobileRobotPositionProvider provider =
                CreateProvider(CreateOptions());
            GlobalPositionSample sample = await provider.ReadAsync(
                "R1",
                CancellationToken.None).ConfigureAwait(false);
            S3DGeographicCoordinateDataType geographic =
                sample.Location.Position;
            ThreeDCartesianCoordinates local =
                provider.Scenario.Fit.GlobalToLocal(
                    geographic,
                    AngleUnit.Degrees);

            Assert.Multiple(() =>
            {
                Assert.That(
                    geographic.EncodingMask &
                        (uint)S3DGeographicCoordinateDataTypeFields.Elevation,
                    Is.Not.Zero);
                Assert.That(local.Z, Is.Zero.Within(1e-5));
            });
        }

        private static global::Robotics.RobotMobilityOptions CreateOptions()
        {
            return new global::Robotics.RobotMobilityOptions
            {
                R1 = new global::Robotics.RobotMotionOptions
                {
                    OriginX = -1.2
                },
                R2 = new global::Robotics.RobotMotionOptions
                {
                    OriginX = 1.2
                }
            };
        }

        private static global::Robotics.MobileRobotPositionProvider CreateProvider(
            global::Robotics.RobotMobilityOptions options)
        {
            return new global::Robotics.MobileRobotPositionProvider(
                options,
                new global::Robotics.RobotPositioningScenario(),
                TimeProvider.System);
        }
    }
}
