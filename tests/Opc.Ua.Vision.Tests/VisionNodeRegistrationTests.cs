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
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the address-space registration contract. The node manager adds
    /// the Vision root to its index before configurators run, so everything
    /// the fluent builder grafts on afterwards has to be registered too.
    /// Browsing forward from a parent walks <c>NodeState.Children</c> in
    /// memory and therefore succeeds either way; only a lookup by the node's
    /// own <see cref="NodeId"/> — which is how an ordinary client and the MCP
    /// discovery tools navigate — can tell the difference.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionNodeRegistrationTests
    {
        [Test]
        public async Task ConfigureVisionRegistersEveryNodeTheBuilderCreated()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            NodeId sensorId = NodeId.Null;
            NodeId frameId = NodeId.Null;
            NodeId pipelineId = NodeId.Null;

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddImageSensor("Camera", s => s
                    .WithSensorId("SN-CAM")
                    .WithModality(VisionSensorModalityEnum.Area2D));
                context.Nodes.AddFrame("World", f => f
                    .WithFrameId("world")
                    .WithRole(VisionFrameRoleEnum.World));
                context.Nodes.AddPipeline("Detector", p => p
                    .WithPipelineId("detector"));

                sensorId = FindChild(context.Root.Sensors!, "Camera").NodeId;
                frameId = FindChild(context.Root.Frames!, "World").NodeId;
                pipelineId = FindChild(context.Root.Pipelines!, "Detector").NodeId;
            }).ConfigureAwait(false);

            VisionRootState root = fixture.Manager.Root;
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(root.Sensors!.NodeId),
                    Is.Not.Null, "the Sensors folder must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(root.Frames!.NodeId),
                    Is.Not.Null, "the Frames folder must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(root.Pipelines!.NodeId),
                    Is.Not.Null, "the Pipelines folder must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(sensorId),
                    Is.Not.Null, "the sensor must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(frameId),
                    Is.Not.Null, "the frame must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(pipelineId),
                    Is.Not.Null, "the pipeline must be reachable by its own NodeId");
            });
        }

        [Test]
        public async Task ConfigureVisionRegistersNodesNestedBelowASensor()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            NodeId calibrationsId = NodeId.Null;
            NodeId handEyeId = NodeId.Null;

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddImageSensor("Camera", s => s
                    .WithSensorId("SN-CAM")
                    .WithModality(VisionSensorModalityEnum.Area2D)
                    .AddExtrinsicCalibration("HandEye", c => c
                        .WithCalibrationId("hand-eye")
                        .WithMount(VisionCalibrationMountEnum.EyeInHand)
                        .WithFrames("flange", "camera_eih")));

                NodeState sensor = FindChild(context.Root.Sensors!, "Camera");
                NodeState calibrations = FindChild(sensor, "Calibrations");
                calibrationsId = calibrations.NodeId;
                handEyeId = FindChild(calibrations, "HandEye").NodeId;
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(calibrationsId),
                    Is.Not.Null, "the Calibrations folder must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(handEyeId),
                    Is.Not.Null, "the calibration must be reachable by its own NodeId");
            });
        }

        [Test]
        public async Task ConfigureVisionRegistersAPipelinesFeedbackAndResultsChildren()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            NodeId feedbackId = NodeId.Null;
            NodeId resultsId = NodeId.Null;
            NodeId submitDetectionsId = NodeId.Null;

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddPipeline("Detector", p => p
                    .WithPipelineId("detector")
                    .UseInferenceProvider(new StubInferenceProvider())
                    .UseFeedbackSink(new StubFeedbackSink()));

                NodeState pipeline = FindChild(context.Root.Pipelines!, "Detector");
                NodeState feedback = FindChild(pipeline, "Feedback");
                feedbackId = feedback.NodeId;
                resultsId = FindChild(pipeline, "Results").NodeId;
                submitDetectionsId = FindChild(feedback, "SubmitDetections").NodeId;
            }).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(feedbackId),
                    Is.Not.Null,
                    "the Feedback object is created after the pipeline is added to its parent, " +
                    "so it must still be registered by its own NodeId - a client resolves it " +
                    "with TranslateBrowsePathsToNodeIds, which yields no target for an " +
                    "unregistered node even though Browse still lists it.");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(resultsId),
                    Is.Not.Null, "the Results folder must be reachable by its own NodeId");
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(submitDetectionsId),
                    Is.Not.Null, "a Feedback method must be reachable by its own NodeId");
            });
        }

        [Test]
        public async Task ConfigureVisionRejectsNullConfigureDelegate()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            Assert.That(
                async () => await fixture.Manager.ConfigureVisionAsync(null!)
                    .ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        private static NodeState FindChild(NodeState parent, string browseName)
        {
            NodeState? match = null;
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName.Name == browseName)
                {
                    match = children[ii];
                    break;
                }
            }
            Assert.That(match, Is.Not.Null,
                $"'{browseName}' must exist below '{parent.BrowseName.Name}'.");
            return match!;
        }

        private sealed class StubInferenceProvider : IVisionInferenceProvider
        {
            public ValueTask<VisionInferenceRunResult> RunInferenceAsync(
                VisionInferenceRunRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<VisionInferenceRunResult>(
                    new VisionInferenceRunResult(ServiceResult.Good, string.Empty));
            }

            public ValueTask<ServiceResult> StartContinuousAsync(
                NodeId pipeline, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> StopAsync(
                NodeId pipeline, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }

        private sealed class StubFeedbackSink : IVisionFeedbackSink
        {
            public ValueTask<ServiceResult> SubmitDetectionsAsync(
                VisionSubmitDetectionsRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitInspectionResultAsync(
                VisionSubmitInspectionResultRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitCorrectionAsync(
                VisionSubmitCorrectionRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            public ValueTask<ServiceResult> SubmitImageReferenceAsync(
                VisionSubmitImageReferenceRequest request, CancellationToken cancellationToken)
            {
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }
        }
    }
}
