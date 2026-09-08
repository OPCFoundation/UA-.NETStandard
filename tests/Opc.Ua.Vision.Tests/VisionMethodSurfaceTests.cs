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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the wire contract of the Vision Methods.
    /// </summary>
    /// <remarks>
    /// A Method is only callable if three things hold: the Method node
    /// exists on the instance, it carries an <c>InputArguments</c>
    /// Property so the stack knows how many arguments to expect, and its
    /// <c>MethodDeclarationId</c> names the type's declaration so a client
    /// calling with the type-declaration MethodId resolves to it. None of
    /// the three held before, and no unit test could see it, because the
    /// dispatcher tests invoke the handler delegates directly and never go
    /// through <c>MethodState.Call</c>.
    /// </remarks>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionMethodSurfaceTests
    {
        [Test]
        public async Task PipelineWithAnInferenceProviderExposesTheInferenceMethods()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: true, withFeedback: false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pipeline.RunInference, Is.Not.Null,
                    "A pipeline that can run inference must expose RunInference.");
                Assert.That(pipeline.StartContinuous, Is.Not.Null);
                Assert.That(pipeline.Stop, Is.Not.Null);
                Assert.That(pipeline.Results, Is.Not.Null,
                    "RunInference publishes into Results, so the folder must exist.");
            });
        }

        [Test]
        public async Task RunInferenceDeclaresItsArgumentsSoACallIsNotRefused()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: true, withFeedback: false).ConfigureAwait(false);
            RunInferenceMethodState method = pipeline.RunInference!;

            Assert.Multiple(() =>
            {
                Assert.That(method.InputArguments, Is.Not.Null,
                    "Without InputArguments the stack expects zero arguments and " +
                    "refuses the call with BadTooManyArguments.");
                Assert.That(method.InputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(method.InputArguments!.Value[0].Name, Is.EqualTo("Timestamp"));
                Assert.That(method.OutputArguments, Is.Not.Null);
                Assert.That(method.OutputArguments!.Value.Count, Is.EqualTo(1));
                Assert.That(method.OutputArguments!.Value[0].Name, Is.EqualTo("ResultId"));
            });
        }

        [Test]
        public async Task RunInferenceNamesTheTypeDeclarationSoAClientCallResolves()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            InferencePipelineState pipeline = await BuildPipelineAsync(
                fixture, withInference: true, withFeedback: false).ConfigureAwait(false);

            NodeId expected = ExpandedNodeId.ToNodeId(
                MethodIds.InferencePipelineType_RunInference,
                fixture.Manager.SystemContext.NamespaceUris);

            Assert.That(pipeline.RunInference!.MethodDeclarationId, Is.EqualTo(expected),
                "A client calls with the type-declaration MethodId; NodeState.FindMethod " +
                "only matches it against MethodDeclarationId.");
        }

        [Test]
        public async Task PipelineWithAFeedbackSinkExposesTheFourFeedbackMethods()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: false, withFeedback: true).ConfigureAwait(false);

            Assert.That(pipeline.Feedback, Is.Not.Null,
                "A pipeline with a feedback sink must expose the Feedback object.");
            VisionFeedbackState feedback = pipeline.Feedback!;
            Assert.Multiple(() =>
            {
                Assert.That(feedback.SubmitDetections, Is.Not.Null);
                Assert.That(feedback.SubmitInspectionResult, Is.Not.Null);
                Assert.That(feedback.SubmitCorrection, Is.Not.Null);
                Assert.That(feedback.SubmitImageReference, Is.Not.Null);
            });
        }

        [Test]
        public async Task SubmitCorrectionDeclaresAllSevenArgumentsInOrder()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: false, withFeedback: true).ConfigureAwait(false);
            SubmitCorrectionMethodState method = pipeline.Feedback!.SubmitCorrection!;

            var names = new List<string>();
            for (int ii = 0; ii < method.InputArguments!.Value.Count; ii++)
            {
                names.Add(method.InputArguments!.Value[ii].Name ?? string.Empty);
            }

            Assert.That(
                names,
                Is.EqualTo(s_stringValues1).AsCollection,
                "The order is positional on the wire, so it must match the spec.");
        }

        [Test]
        public async Task SubmitDetectionsDeclaresTheSceneIsEmptyFlagLast()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: false, withFeedback: true).ConfigureAwait(false);
            SubmitDetectionsMethodState method = pipeline.Feedback!.SubmitDetections!;

            var names = new List<string>();
            for (int ii = 0; ii < method.InputArguments!.Value.Count; ii++)
            {
                names.Add(method.InputArguments!.Value[ii].Name ?? string.Empty);
            }

            Assert.That(names, Is.EqualTo(s_stringValues2).AsCollection,
                "SceneIsEmpty is what makes an empty Detections array a real " +
                "observation, so it has to reach the wire in the position the " +
                "specification gives it.");
        }

        [Test]
        public async Task PipelineWithoutProvidersExposesNoMethods()
        {
            InferencePipelineState pipeline = await BuildPipelineAsync(
                withInference: false, withFeedback: false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(pipeline.RunInference, Is.Null,
                    "A pipeline with no inference provider must not advertise RunInference.");
                Assert.That(pipeline.Feedback, Is.Null,
                    "A pipeline with no feedback sink must not advertise Feedback.");
            });
        }

        [Test]
        public async Task EveryChildTheBuilderCreatesCarriesAReferenceType()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            InferencePipelineState pipeline = await BuildPipelineAsync(
                fixture, withInference: true, withFeedback: true).ConfigureAwait(false);

            // A child referenced by nothing cannot be browsed from its parent
            // and a browse path cannot be translated to it, so the client sees
            // an object whose optional children do not exist.
            AssertAllChildrenReferenced(pipeline);
        }

        [Test]
        public async Task SensorWithAMediaProviderExposesTheMediaMethods()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);

            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddImageSensor("Camera", s => s
                    .WithSensorId("SN-CAM")
                    .WithModality(VisionSensorModalityEnum.Area2D)
                    .UseMediaProvider(new Mock<IVisionMediaProvider>().Object));
            }).ConfigureAwait(false);

            var sensors = new List<BaseInstanceState>();
            fixture.Manager.Root.Sensors!.GetChildren(null!, sensors);
            var sensor = (ImageSensorState)sensors[0];

            Assert.That(sensor.Media, Is.Not.Null,
                "A sensor with a media provider must expose the Media object.");
            VisionMediaManagementState media = sensor.Media!;
            Assert.Multiple(() =>
            {
                Assert.That(media.GetStreamEndpoint, Is.Not.Null);
                Assert.That(media.ReleaseStreamEndpoint, Is.Not.Null);
                Assert.That(media.ConfigureStreamEndpoint, Is.Not.Null);
                Assert.That(media.SelectEndpoint, Is.Not.Null);
                Assert.That(media.GetClip, Is.Not.Null);
                Assert.That(media.GetClip!.InputArguments!.Value.Count, Is.EqualTo(5));
                Assert.That(media.GetClip!.OutputArguments!.Value.Count, Is.EqualTo(3));
            });
        }

        private static void AssertAllChildrenReferenced(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                Assert.That(children[ii].ReferenceTypeId.IsNull, Is.False,
                    $"'{children[ii].BrowseName.Name}' below '{node.BrowseName.Name}' " +
                    "has no reference type, so nothing can reach it.");
                AssertAllChildrenReferenced(children[ii]);
            }
        }

        private static async Task<InferencePipelineState> BuildPipelineAsync(
            bool withInference,
            bool withFeedback)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            return await BuildPipelineAsync(fixture, withInference, withFeedback)
                .ConfigureAwait(false);
        }

        private static async Task<InferencePipelineState> BuildPipelineAsync(
            VisionServerFixture fixture,
            bool withInference,
            bool withFeedback)
        {
            await fixture.Manager.ConfigureVisionAsync(context =>
            {
                context.Nodes.AddPipeline("Detector", p =>
                {
                    p.WithPipelineId("detector");
                    if (withInference)
                    {
                        p.UseInferenceProvider(
                            new Mock<IVisionInferenceProvider>().Object, onServer: true);
                    }
                    if (withFeedback)
                    {
                        p.UseFeedbackSink(new Mock<IVisionFeedbackSink>().Object);
                    }
                });
            }).ConfigureAwait(false);

            var pipelines = new List<BaseInstanceState>();
            fixture.Manager.Root.Pipelines!.GetChildren(null!, pipelines);
            return (InferencePipelineState)pipelines[0];
        }

        private static readonly string[] s_stringValues1 =
        [
            "ResultId",
            "Purpose",
            "CorrectedDetections",
            "CorrectedCharacteristics",
            "Reason",
            "InlineImage",
            "RetractAll",
        ];
        private static readonly string[] s_stringValues2 =
        [
            "Purpose",
            "Detections",
            "FrameReference",
            "InlineImage",
            "SceneIsEmpty",
        ];
    }
}
