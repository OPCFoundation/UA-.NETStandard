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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowFeedbackTests
    {
        [Test]
        public async Task ImageProvenanceBindingDoesNotRequirePublishedResult()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId folder = AddEmptyResultFolder(fixture);
            ArrayOf<CompanionValue> inputs = Replace(
                fixture.Inputs("submit-image-reference"), "resultId", Variant.From("inspection-upcoming-7"));

            VisionWorkflowBinding binding = await VisionWorkflowAccess.ReadBindingAsync(
                fixture.Context, fixture.PipelineTarget, fixture.Definition("submit-image-reference"),
                inputs, CancellationToken.None).ConfigureAwait(false);

            Assert.That(binding.Sensor, Is.EqualTo(fixture.Sensor));
            Assert.That(binding.Component, Is.EqualTo(fixture.Feedback));
            Assert.That(binding.Identity, Is.EqualTo("pipeline-identity-7"));
            Assert.That(binding.Metadata.Length, Is.EqualTo(32));
            VerifyNoResultBrowse(fixture, folder);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ImageProvenanceIsDispatchedBeforeInspectionPublishesItsResult()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId folder = AddEmptyResultFolder(fixture);
            (IPreparedCompanionProvider provider, Mock<IVisionExecutionPolicy> policy) = CreateProvider(fixture);
            const string resultId = "inspection-upcoming-7";
            bool provenanceReceived = false;
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Feedback));
                Assert.That(request.InputArguments.Count, Is.EqualTo(3));
                if (request.MethodId == fixture.Fixture.Children[(fixture.Feedback, "SubmitImageReference")])
                {
                    Assert.That(provenanceReceived, Is.False);
                    Assert.That(request.InputArguments[0].TryGetValue(out int purpose), Is.True);
                    Assert.That(purpose, Is.EqualTo((int)VisionFeedbackPurposeEnum.Overlay));
                    Assert.That(request.InputArguments[1].TryGetValue<VisionImageReferenceDataType>(
                        out VisionImageReferenceDataType? image, fixture.Context.Session.MessageContext), Is.True);
                    Assert.That(image!.Uri, Is.EqualTo("https://localhost/frames/frame-7"));
                    Assert.That(image.Digest.ToArray(), Is.EqualTo(Image().Digest.ToArray()));
                    Assert.That(request.InputArguments[2].TryGetValue(out string? id), Is.True);
                    Assert.That(id, Is.EqualTo(resultId));
                    provenanceReceived = true;
                }
                else
                {
                    Assert.That(request.MethodId,
                        Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback, "SubmitInspectionResult")]));
                    Assert.That(provenanceReceived, Is.True, "The sample requires provenance before publication.");
                    Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                    Assert.That(id, Is.EqualTo(resultId));
                    Assert.That(request.InputArguments[1].TryGetValue(out int evaluation), Is.True);
                    Assert.That(evaluation, Is.EqualTo((int)VisionResultEvaluationEnum.Ok));
                    Assert.That(request.InputArguments[2].TryGetValue(
                        out ArrayOf<VisionCharacteristicDataType> measurements,
                        fixture.Context.Session.MessageContext), Is.True);
                    Assert.That(measurements.Count, Is.EqualTo(1));
                    Assert.That(measurements[0].CharacteristicId, Is.EqualTo("length-7"));
                    Assert.That(measurements[0].Nominal, Is.EqualTo(10));
                    Assert.That(measurements[0].Actual, Is.EqualTo(10.25));
                }
                return ValueTask.FromResult(new CallMethodResult { OutputArguments = [] });
            };
            ArrayOf<CompanionValue> imageInputs =
                Replace(fixture.Inputs("submit-image-reference"), "resultId", Variant.From(resultId));
            CompanionTaskInput imageTask = await provider.PrepareInputAsync(
                fixture.Context, fixture.PipelineTarget, "submit-image-reference", imageInputs, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.Zero);

            CompanionOperationResult imageResult = await provider.ExecutePreparedAsync(
                fixture.Context, fixture.PipelineTarget, "submit-image-reference",
                imageTask, null, CancellationToken.None).ConfigureAwait(false);
            ArrayOf<CompanionValue> inspectionInputs =
                Replace(fixture.Inputs("submit-inspection"), "resultId", Variant.From(resultId));
            CompanionTaskInput inspectionTask = await provider.PrepareInputAsync(
                fixture.Context, fixture.PipelineTarget, "submit-inspection", inspectionInputs, CancellationToken.None)
                .ConfigureAwait(false);
            CompanionOperationResult inspectionResult = await provider.ExecutePreparedAsync(
                fixture.Context, fixture.PipelineTarget, "submit-inspection",
                inspectionTask, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
            Assert.That(fixture.Calls[0].MethodId,
                Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback, "SubmitImageReference")]));
            Assert.That(fixture.Calls[1].MethodId,
                Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback, "SubmitInspectionResult")]));
            AssertAccepted(imageResult, fixture.Pipeline, imageTask);
            AssertAccepted(inspectionResult, fixture.Pipeline, inspectionTask);
            policy.Verify(execution => execution.AuthorizeAsync(
                fixture.Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
            VerifyNoResultBrowse(fixture, folder);
            fixture.VerifyBorrowedSession();
        }

        [TestCase("submit-detections")]
        [TestCase("submit-image-reference")]
        public async Task AdvertisedInlineFrameCanBePreparedAndSubmitted(string operation)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId folder = AddEmptyResultFolder(fixture);
            fixture.Set(fixture.Clip, "EndpointUri", Variant.From(kInlineOrigin));
            VisionImageReferenceDataType image = Image();
            image.Uri = kInlineFrame;
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs(operation), "image", Variant.FromStructure(image));
            (IPreparedCompanionProvider provider, Mock<IVisionExecutionPolicy> policy) = CreateProvider(fixture);
            fixture.OnCall = (request, token) =>
            {
                Assert.That(token.IsCancellationRequested, Is.False);
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Feedback));
                Assert.That(request.MethodId, Is.EqualTo(fixture.Fixture.Children[
                    (fixture.Feedback,
                        operation == "submit-detections" ? "SubmitDetections" : "SubmitImageReference")]));
                Assert.That(request.InputArguments.Count, Is.EqualTo(operation == "submit-detections" ? 5 : 3));
                int imageIndex = operation == "submit-detections" ? 2 : 1;
                Assert.That(request.InputArguments[imageIndex].TryGetValue<VisionImageReferenceDataType>(
                    out VisionImageReferenceDataType? sent, fixture.Context.Session.MessageContext), Is.True);
                Assert.That(sent!.Uri, Is.EqualTo(kInlineFrame));
                Assert.That(sent.Digest.ToArray(), Is.EqualTo(Image().Digest.ToArray()));
                Assert.That(sent.SizeBytes, Is.EqualTo(4));
                if (operation == "submit-detections")
                {
                    Assert.That(request.InputArguments[3].TryGetValue(out ByteString inline), Is.True);
                    Assert.That(inline.IsEmpty, Is.True);
                    Assert.That(request.InputArguments[4].TryGetValue(out bool empty), Is.True);
                    Assert.That(empty, Is.False);
                }
                else
                {
                    Assert.That(request.InputArguments[2].TryGetValue(out string? id), Is.True);
                    Assert.That(id, Is.EqualTo("result-7"));
                }
                return ValueTask.FromResult(new CallMethodResult { OutputArguments = [] });
            };

            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                fixture.Context, fixture.PipelineTarget, operation, inputs, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.Zero);
            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                fixture.Context, fixture.PipelineTarget, operation, prepared, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            AssertAccepted(result, fixture.Pipeline, prepared);
            policy.Verify(execution => execution.AuthorizeAsync(
                fixture.Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            VerifyNoResultBrowse(fixture, folder);
            fixture.VerifyBorrowedSession();
        }

        [TestCase("userinfo")]
        [TestCase("query")]
        [TestCase("fragment")]
        [TestCase("foreign-authority")]
        [TestCase("lookalike-authority")]
        [TestCase("unadvertised-root")]
        [TestCase("lookalike-root")]
        [TestCase("only-stream-advertises")]
        [TestCase("only-other-sensor-advertises")]
        [TestCase("missing-clip-folder")]
        [TestCase("physical")]
        [TestCase("hybrid")]
        public async Task InlineFramesRequireAnAdvertisedOriginFromTheSelectedSensor(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Set(fixture.Clip, "EndpointUri", Variant.From(kInlineOrigin));
            VisionImageReferenceDataType image = Image();
            image.Uri = kInlineFrame;
            switch (fault)
            {
                case "userinfo":
                    image.Uri = "opcua-inline://test-user@visual-inspection-cell/fixtures/bracket-ok.png";
                    break;
                case "query":
                    image.Uri += "?untrusted=1";
                    break;
                case "fragment":
                    image.Uri += "#untrusted";
                    break;
                case "foreign-authority":
                    image.Uri = "opcua-inline://different-cell/fixtures/bracket-ok.png";
                    break;
                case "lookalike-authority":
                    image.Uri = "opcua-inline://visual-inspection-cell.invalid/fixtures/bracket-ok.png";
                    break;
                case "unadvertised-root":
                    image.Uri = "opcua-inline://visual-inspection-cell/unadvertised/bracket-ok.png";
                    break;
                case "lookalike-root":
                    image.Uri = "opcua-inline://visual-inspection-cell/fixtures-other/bracket-ok.png";
                    break;
                case "only-stream-advertises":
                    fixture.Set(fixture.Clip, "EndpointUri", Variant.From("https://localhost/other-clips"));
                    fixture.Set(fixture.Stream, "EndpointUri", Variant.From(kInlineOrigin));
                    break;
                case "only-other-sensor-advertises":
                    fixture.Set(fixture.Clip, "EndpointUri", Variant.From("https://localhost/other-clips"));
                    AddForeignSensorOrigin(fixture);
                    break;
                case "missing-clip-folder":
                    fixture.Fixture.Children.Remove((fixture.Media, "ClipEndpoints"));
                    break;
                case "physical":
                    fixture.Set(fixture.Sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Physical));
                    break;
                case "hybrid":
                    fixture.Set(fixture.Sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Hybrid));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            ArrayOf<CompanionValue> inputs =
                Replace(fixture.Inputs("submit-detections"), "image", Variant.FromStructure(image));
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);

            await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).AsTask(),
                Throws.Exception.Matches<Exception>(IsPreflightRejection)).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count, Is.Zero);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ChangedInlineOriginInvalidatesPreparedFeedback()
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Set(fixture.Clip, "EndpointUri", Variant.From(kInlineOrigin));
            VisionImageReferenceDataType image = Image();
            image.Uri = kInlineFrame;
            ArrayOf<CompanionValue> inputs =
                Replace(fixture.Inputs("submit-detections"), "image", Variant.FromStructure(image));
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                fixture.Context, fixture.PipelineTarget, "submit-detections", inputs, CancellationToken.None)
                .ConfigureAwait(false);
            fixture.Set(fixture.Clip, "EndpointUri", Variant.From("opcua-inline://different-cell/fixtures"));

            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                fixture.Context, fixture.PipelineTarget, "submit-detections", prepared, null, CancellationToken.None)
                .AsTask(), Throws.Exception.Matches<Exception>(error =>
                    error is InvalidOperationException || IsPreflightRejection(error))).ConfigureAwait(false);

            Assert.That(fixture.Calls.Count, Is.Zero);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("valid")]
        [TestCase("missing")]
        [TestCase("id")]
        [TestCase("sensor")]
        [TestCase("pipeline")]
        [TestCase("kind")]
        public async Task CorrectionRequiresExistingExactResultIdentityAndKind(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId folder = AddEmptyResultFolder(fixture);
            NodeId result = AddDetectionResult(fixture, folder);
            ArrayOf<CompanionValue> inputs = fixture.Inputs("submit-correction");
            switch (fault)
            {
                case "valid":
                    break;
                case "missing":
                    fixture.Fixture.Browses[folder] = [];
                    break;
                case "id":
                    fixture.Set(result, "ResultId", Variant.From("not-the-reviewed-id"));
                    break;
                case "sensor":
                    fixture.Set(result, "Sensor",
                        Variant.From(new NodeId("other-sensor", fixture.Fixture.NamespaceIndex)));
                    break;
                case "pipeline":
                    fixture.Set(result, "Pipeline",
                        Variant.From(new NodeId("other-pipeline", fixture.Fixture.NamespaceIndex)));
                    break;
                case "kind":
                    inputs = Replace(inputs, "detections",
                        Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty));
                    inputs = Replace(inputs, "characteristics",
                        Variant.FromStructure([Characteristic()]));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            if (fault == "valid")
            {
                CompanionTaskInput prepared = await provider.PrepareInputAsync(
                    fixture.Context, fixture.PipelineTarget, "submit-correction", inputs, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(prepared, Is.TypeOf<VisionWorkflowTask>());
                var task = (VisionWorkflowTask)prepared;
                Assert.That(task.Target, Is.EqualTo(fixture.PipelineTarget));
                Assert.That(task.Binding.Sensor, Is.EqualTo(fixture.Sensor));
                Assert.That(task.Binding.Component, Is.EqualTo(fixture.Feedback));
                Assert.That(task.Inputs[0].Value.TryGetValue(out string? id), Is.True);
                Assert.That(id, Is.EqualTo("result-7"));
            }
            else if (fault == "kind")
            {
                await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    "submit-correction", inputs, CancellationToken.None).AsTask(), Throws.ArgumentException)
                    .ConfigureAwait(false);
            }
            else
            {
                StatusCode status = fault is "missing" or "id" ? StatusCodes.BadNotFound : StatusCodes.BadTypeMismatch;
                await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    "submit-correction", inputs, CancellationToken.None).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                    .ConfigureAwait(false);
            }
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ExplicitEmptySceneFlagReachesTheFeedbackMethod()
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-detections"),
                "detections", Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty));
            inputs = Replace(inputs, "sceneIsEmpty", Variant.From(true));
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Feedback));
                Assert.That(request.MethodId,
                    Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback, "SubmitDetections")]));
                Assert.That(request.InputArguments.Count, Is.EqualTo(5));
                Assert.That(request.InputArguments[0].TryGetValue(out int purpose), Is.True);
                Assert.That(purpose, Is.EqualTo((int)VisionFeedbackPurposeEnum.GroundTruthLabel));
                Assert.That(request.InputArguments[1].TryGetValue(
                    out ArrayOf<VisionDetectionDataType> detections, fixture.Context.Session.MessageContext), Is.True);
                Assert.That(detections.IsEmpty, Is.True);
                Assert.That(request.InputArguments[2].TryGetValue<VisionImageReferenceDataType>(
                    out VisionImageReferenceDataType? image, fixture.Context.Session.MessageContext), Is.True);
                Assert.That(image!.IsEqual(new VisionImageReferenceDataType()), Is.True);
                Assert.That(request.InputArguments[3].TryGetValue(out ByteString inline), Is.True);
                Assert.That(inline.IsEmpty, Is.True);
                Assert.That(request.InputArguments[4].TryGetValue(out bool empty), Is.True);
                Assert.That(empty, Is.True);
                return ValueTask.FromResult(new CallMethodResult());
            };
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).ConfigureAwait(false);

            CompanionOperationResult result = await provider.ExecutePreparedAsync(fixture.Context,
                fixture.PipelineTarget, "submit-detections", task, null, CancellationToken.None).ConfigureAwait(false);

            AssertAccepted(result, fixture.Pipeline, task);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase(Opc.Ua.Vision.Client.VisionResultKind.Detection)]
        [TestCase(Opc.Ua.Vision.Client.VisionResultKind.Inspection)]
        public async Task ExplicitRetractionReachesTheExactReviewedResult(Opc.Ua.Vision.Client.VisionResultKind kind)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.AddResult(kind: kind);
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-correction"),
                "detections", Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty));
            inputs = Replace(inputs, "retractAll", Variant.From(true));
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Feedback));
                Assert.That(request.MethodId,
                    Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback, "SubmitCorrection")]));
                Assert.That(request.InputArguments.Count, Is.EqualTo(7));
                Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                Assert.That(id, Is.EqualTo("result-7"));
                Assert.That(request.InputArguments[1].TryGetValue(out int purpose), Is.True);
                Assert.That(purpose, Is.EqualTo((int)VisionFeedbackPurposeEnum.GroundTruthLabel));
                Assert.That(request.InputArguments[2].TryGetValue(
                    out ArrayOf<VisionDetectionDataType> detections, fixture.Context.Session.MessageContext), Is.True);
                Assert.That(detections.IsEmpty, Is.True);
                Assert.That(request.InputArguments[3].TryGetValue(
                    out ArrayOf<VisionCharacteristicDataType> measurements,
                    fixture.Context.Session.MessageContext), Is.True);
                Assert.That(measurements.IsEmpty, Is.True);
                Assert.That(request.InputArguments[4].TryGetValue(out LocalizedText reason), Is.True);
                Assert.That(reason.Locale, Is.EqualTo("en"));
                Assert.That(reason.Text, Is.EqualTo("Verified against the selected frame."));
                Assert.That(request.InputArguments[5].TryGetValue(out ByteString inline), Is.True);
                Assert.That(inline.IsEmpty, Is.True);
                Assert.That(request.InputArguments[6].TryGetValue(out bool retract), Is.True);
                Assert.That(retract, Is.True);
                return ValueTask.FromResult(new CallMethodResult());
            };
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-correction", inputs, CancellationToken.None).ConfigureAwait(false);

            CompanionOperationResult result = await provider.ExecutePreparedAsync(fixture.Context,
                fixture.PipelineTarget, "submit-correction", task, null, CancellationToken.None).ConfigureAwait(false);

            AssertAccepted(result, fixture.Pipeline, task);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase("submit-detections", 0u, false)]
        [TestCase("submit-detections", 3u, false)]
        [TestCase("submit-detections", 4u, true)]
        [TestCase("submit-detections", 5u, true)]
        [TestCase("submit-correction", 0u, false)]
        [TestCase("submit-correction", 3u, false)]
        [TestCase("submit-correction", 4u, true)]
        [TestCase("submit-correction", 5u, true)]
        public async Task InlineFeedbackHonorsInclusiveAdvertisedLimit(string operation, uint maximum, bool accepted)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Set(fixture.Feedback, "MaxInlineFeedbackImageSize", Variant.From(maximum));
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs(operation),
                "inlineImage", Variant.From(ByteString.From(1, 2, 3, 4)));
            if (operation == "submit-detections")
            {
                inputs = Replace(inputs, "image", Variant.FromStructure(Image()));
            }
            else
            {
                fixture.AddResult();
            }
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Feedback));
                Assert.That(request.MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Feedback,
                    operation == "submit-detections" ? "SubmitDetections" : "SubmitCorrection")]));
                Assert.That(request.InputArguments[operation == "submit-detections" ? 3 : 5]
                    .TryGetValue(out ByteString inline), Is.True);
                Assert.That(inline, Is.EqualTo(ByteString.From(1, 2, 3, 4)));
                return ValueTask.FromResult(new CallMethodResult());
            };
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            if (accepted)
            {
                CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    operation, inputs, CancellationToken.None).ConfigureAwait(false);
                CompanionOperationResult result = await provider.ExecutePreparedAsync(fixture.Context,
                    fixture.PipelineTarget, operation, task, null, CancellationToken.None).ConfigureAwait(false);
                AssertAccepted(result, fixture.Pipeline, task);
                Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            }
            else
            {
                await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    operation, inputs, CancellationToken.None).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
                Assert.That(fixture.Calls.Count, Is.Zero);
            }
            fixture.VerifyBorrowedSession();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AbsentFeedbackLimitAllowsOnlyRequestsWithoutInlineBytes(bool inline)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.Fixture.Children.Remove((fixture.Feedback, "MaxInlineFeedbackImageSize"));
            ArrayOf<CompanionValue> inputs = fixture.Inputs("submit-detections");
            if (inline)
            {
                inputs = Replace(inputs, "inlineImage", Variant.From(ByteString.From(1, 2, 3, 4)));
                inputs = Replace(inputs, "image", Variant.FromStructure(Image()));
            }
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            if (inline)
            {
                await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                    "submit-detections", inputs, CancellationToken.None).AsTask(),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
                fixture.Fixture.VerifyNoMutationOrSessionOwnership();
                return;
            }
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.InputArguments[3].TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.IsEmpty, Is.True);
                return ValueTask.FromResult(new CallMethodResult());
            };
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult result = await provider.ExecutePreparedAsync(fixture.Context,
                fixture.PipelineTarget, "submit-detections", task, null, CancellationToken.None).ConfigureAwait(false);

            AssertAccepted(result, fixture.Pipeline, task);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FeedbackLimitMustBeUnsigned32AndRemainPinned(bool statusCode)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("submit-detections"),
                "inlineImage", Variant.From(ByteString.From(1, 2, 3, 4)));
            inputs = Replace(inputs, "image", Variant.FromStructure(Image()));
            (IPreparedCompanionProvider provider, _) = CreateProvider(fixture);
            fixture.Set(fixture.Feedback, "MaxInlineFeedbackImageSize",
                statusCode ? Variant.From(StatusCodes.BadNotFound) : Variant.From(4UL));
            await Assert.ThatAsync(() => provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).AsTask(),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            fixture.Set(fixture.Feedback, "MaxInlineFeedbackImageSize", Variant.From(4u));
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", inputs, CancellationToken.None).ConfigureAwait(false);
            fixture.Set(fixture.Feedback, "MaxInlineFeedbackImageSize", Variant.From(5u));

            await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.PipelineTarget,
                "submit-detections", task, null, CancellationToken.None).AsTask(),
                Throws.InvalidOperationException.With.Message.Contains("changed")).ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        private static (IPreparedCompanionProvider, Mock<IVisionExecutionPolicy>) CreateProvider(
            VisionWorkflowTestSupport fixture)
        {
            var policy = new Mock<IVisionExecutionPolicy>(MockBehavior.Strict);
            policy.Setup(execution => execution.AuthorizeAsync(
                fixture.Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return (new VisionCompanionProvider(policy.Object, fixture.Clock.Object), policy);
        }

        private static NodeId AddEmptyResultFolder(VisionWorkflowTestSupport fixture)
        {
            var folder = new NodeId("results", fixture.Fixture.NamespaceIndex);
            fixture.AddChild(fixture.Pipeline, "Results", folder);
            fixture.Fixture.Browses[folder] = [];
            return folder;
        }

        private static NodeId AddDetectionResult(VisionWorkflowTestSupport fixture, NodeId folder)
        {
            var result = new NodeId("published-detection", fixture.Fixture.NamespaceIndex);
            fixture.SetType(result, Opc.Ua.Vision.ObjectTypeIds.DetectionResultType);
            fixture.AddChild(folder, "DifferentBrowseName", result);
            var detectionType = ExpandedNodeId.ToNodeId(
                Opc.Ua.Vision.ObjectTypeIds.DetectionResultType, fixture.Fixture.NamespaceUris);
            var resultType = ExpandedNodeId.ToNodeId(
                Opc.Ua.Vision.ObjectTypeIds.VisionResultType, fixture.Fixture.NamespaceUris);
            fixture.Fixture.Browses[detectionType] =
            [
                new ReferenceDescription
                {
                    NodeId = resultType,
                    NodeClass = NodeClass.ObjectType,
                    IsForward = false,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasSubtype
                }
            ];
            fixture.Set(result, "ResultId", Variant.From("result-7"));
            fixture.Set(result, "CreationTime", Variant.From(new DateTimeUtc(fixture.UtcNow)));
            fixture.Set(result, "Sensor", Variant.From(fixture.Sensor));
            fixture.Set(result, "Pipeline", Variant.From(fixture.Pipeline));
            fixture.Set(result, "FrameId", Variant.From("camera-frame-7"));
            fixture.Set(result, "Frame", Variant.FromStructure(Image()));
            fixture.Set(result, "ModelVersionUsed", Variant.From("unit-test-v7"));
            fixture.Set(result, "Detections", Variant.FromStructure([Detection()]));
            return result;
        }

        private static void AddForeignSensorOrigin(VisionWorkflowTestSupport fixture)
        {
            var sensor = new NodeId("foreign-sensor", fixture.Fixture.NamespaceIndex);
            var media = new NodeId("foreign-media", fixture.Fixture.NamespaceIndex);
            var folder = new NodeId("foreign-clips", fixture.Fixture.NamespaceIndex);
            var endpoint = new NodeId("foreign-clip", fixture.Fixture.NamespaceIndex);
            fixture.SetType(sensor, Opc.Ua.Vision.ObjectTypeIds.VisionSensorType);
            fixture.SetType(media, Opc.Ua.Vision.ObjectTypeIds.VisionMediaManagementType);
            fixture.SetType(endpoint, Opc.Ua.Vision.ObjectTypeIds.ClipEndpointType);
            fixture.AddChild(sensor, "Media", media);
            fixture.AddChild(media, "ClipEndpoints", folder);
            fixture.AddChild(folder, "ForeignClip", endpoint);
            fixture.Set(endpoint, "EndpointId", Variant.From("foreign-clip"));
            fixture.Set(endpoint, "EndpointUri", Variant.From(kInlineOrigin));
            fixture.Set(endpoint, "State", Variant.From((int)VisionEndpointStateEnum.Ready));
            fixture.Set(endpoint, "SecureTransport", Variant.From(true));
            fixture.Set(endpoint, "Authentication", Variant.From(0));
        }

        private static bool IsPreflightRejection(Exception error)
        {
            return error is ArgumentException or UnauthorizedAccessException ||
                (error is ServiceResultException service &&
                    (service.StatusCode == StatusCodes.BadTypeMismatch ||
                        service.StatusCode == StatusCodes.BadNodeIdInvalid ||
                        service.StatusCode == StatusCodes.BadNotFound ||
                        service.StatusCode == StatusCodes.BadNoMatch ||
                        service.StatusCode == StatusCodes.BadInvalidState));
        }

        private static void AssertAccepted(
            CompanionOperationResult result, NodeId pipeline, CompanionTaskInput prepared)
        {
            Assert.That(CellProviderTestSession.Field(result.Values, "Method returned").TryGetValue(
                out bool accepted), Is.True);
            Assert.That(accepted, Is.True);
            Assert.That(CellProviderTestSession.Field(result.Values, "Target").TryGetValue(
                out NodeId target), Is.True);
            Assert.That(target, Is.EqualTo(pipeline));
            Assert.That(CellProviderTestSession.Field(result.Values, "Request ID").TryGetValue(
                out string? requestId), Is.True);
            Assert.That(prepared, Is.TypeOf<VisionWorkflowTask>());
            Assert.That(requestId, Is.EqualTo(((VisionWorkflowTask)prepared).RequestId));
        }

        private static void VerifyNoResultBrowse(VisionWorkflowTestSupport fixture, NodeId folder)
        {
            fixture.Session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.Is<ArrayOf<BrowseDescription>>(requests => requests.Count == 1 && requests[0].NodeId == folder),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private const string kInlineOrigin = "opcua-inline://visual-inspection-cell/fixtures";
        private const string kInlineFrame = "opcua-inline://visual-inspection-cell/fixtures/bracket-ok.png";
    }
}
