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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using Vision.BinPickingCell;

namespace Opc.Ua.Robotics.Tests
{
    [TestFixture]
    public class BinPickingTargetProviderTests
    {
        [Test]
        public void OnServerStaleTargetFallsBackToCurrentWorldState()
        {
            var worldState = new BinPickingWorldState();
            var provider = CreateProvider(worldState, BinPickingInferenceLocation.OnServer);
            provider.PublishWorldState(
                "old-result",
                DateTimeUtc.From(DateTime.UtcNow - TimeSpan.FromMinutes(1)),
                worldState.Snapshot());

            bool resolved = provider.TryResolve("RedCube", out BinPickingTarget target);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(target.ResultId, Is.EqualTo("simulation-world-state"));
                Assert.That(target.SourceFrameId, Is.EqualTo("world"));
                Assert.That(target.WorldX, Is.EqualTo(0.520).Within(1e-9));
                Assert.That(target.WorldY, Is.EqualTo(-0.080).Within(1e-9));
            });
        }

        [Test]
        public void OffServerStaleTargetIsNotReplacedWithSimulationTruth()
        {
            var worldState = new BinPickingWorldState();
            var provider = CreateProvider(worldState, BinPickingInferenceLocation.EdgeOffServer);
            provider.PublishWorldState(
                "old-result",
                DateTimeUtc.From(DateTime.UtcNow - TimeSpan.FromMinutes(1)),
                worldState.Snapshot());

            Assert.That(provider.TryResolve("RedCube", out _), Is.False);
        }

        [Test]
        public void OffServerDetectionComposesCameraPoseAndPreservesProvenance()
        {
            var worldState = new BinPickingWorldState();
            var provider = CreateProvider(worldState, BinPickingInferenceLocation.EdgeOffServer);
            BinPickingPart red = BinPickingPartsCatalog.TryGet("RedCube")!;
            VisionDetectionDataType detection = Detection(
                red.ClassLabel,
                "camera_eih",
                red.InitialWorldPosition[0],
                red.InitialWorldPosition[1],
                red.InitialWorldPosition[2]);

            provider.PublishDetections(
                "agent-result",
                DateTimeUtc.From(DateTime.UtcNow),
                new[] { detection }.ToArrayOf(),
                IdentityCamera(),
                "camera_eih");

            bool resolved = provider.TryResolve(red.ClassLabel, out BinPickingTarget target);
            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.True);
                Assert.That(target.ResultId, Is.EqualTo("agent-result"));
                Assert.That(target.SourceFrameId, Is.EqualTo("camera_eih"));
                Assert.That(target.WorldX, Is.EqualTo(red.InitialWorldPosition[0]).Within(1e-9));
                Assert.That(target.WorldY, Is.EqualTo(red.InitialWorldPosition[1]).Within(1e-9));
                Assert.That(target.WorldZ, Is.EqualTo(red.InitialWorldPosition[2]).Within(1e-9));
            });
        }

        [Test]
        public void OffServerDetectionRejectsAnUncalibratedSourceFrame()
        {
            var worldState = new BinPickingWorldState();
            var provider = CreateProvider(worldState, BinPickingInferenceLocation.EdgeOffServer);
            BinPickingPart red = BinPickingPartsCatalog.TryGet("RedCube")!;
            VisionDetectionDataType detection = Detection(
                red.ClassLabel,
                "wrong_camera",
                red.InitialWorldPosition[0],
                red.InitialWorldPosition[1],
                red.InitialWorldPosition[2]);

            ServiceResultException? exception = Assert.Throws<ServiceResultException>(() =>
                provider.PublishDetections(
                    "agent-result",
                    DateTimeUtc.From(DateTime.UtcNow),
                    new[] { detection }.ToArrayOf(),
                    IdentityCamera(),
                    "camera_eih"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(provider.TryResolve(red.ClassLabel, out _), Is.False);
            });
        }

        [Test]
        public void OffServerDetectionBatchRejectsResidualWithoutPublishingEarlierTargets()
        {
            var worldState = new BinPickingWorldState();
            var provider = CreateProvider(worldState, BinPickingInferenceLocation.EdgeOffServer);
            BinPickingPart red = BinPickingPartsCatalog.TryGet("RedCube")!;
            BinPickingPart green = BinPickingPartsCatalog.TryGet("GreenCylinder")!;
            VisionDetectionDataType[] detections =
            [
                Detection(
                    red.ClassLabel,
                    "camera_eih",
                    red.InitialWorldPosition[0],
                    red.InitialWorldPosition[1],
                    red.InitialWorldPosition[2]),
                Detection(
                    green.ClassLabel,
                    "camera_eih",
                    green.InitialWorldPosition[0] + 0.20,
                    green.InitialWorldPosition[1],
                    green.InitialWorldPosition[2])
            ];

            _ = Assert.Throws<ServiceResultException>(() =>
                provider.PublishDetections(
                    "agent-result",
                    DateTimeUtc.From(DateTime.UtcNow),
                    detections.ToArrayOf(),
                    IdentityCamera(),
                    "camera_eih"));

            Assert.That(provider.TryResolve(red.ClassLabel, out _), Is.False);
        }

        private static BinPickingTargetProvider CreateProvider(
            BinPickingWorldState worldState,
            BinPickingInferenceLocation inferenceLocation)
        {
            return new BinPickingTargetProvider(
                worldState,
                new BinPickingCellOptions { InferenceLocation = inferenceLocation });
        }

        private static VisionDetectionDataType Detection(
            string classLabel,
            string frameId,
            double x,
            double y,
            double z)
        {
            return new VisionDetectionDataType
            {
                ClassLabel = classLabel,
                Confidence = 0.99,
                HasPose = true,
                Pose = new VisionPose3DDataType
                {
                    FrameId = frameId,
                    Position = new[] { x, y, z }.ToArrayOf(),
                    Orientation = s_doubleValues1.ToArrayOf()
                }
            };
        }

        private static VisionPose3DDataType IdentityCamera()
        {
            return new VisionPose3DDataType
            {
                FrameId = "world",
                Position = s_doubleValues2.ToArrayOf(),
                Orientation = s_doubleValues1.ToArrayOf()
            };
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0];
    }
}
