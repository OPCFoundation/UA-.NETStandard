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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Pins the SubmitInspectionResult and SubmitImageReference dispatch
    /// paths, mirroring the guarantees that <see cref="VisionFeedbackDispatcherTests"/>
    /// pins for SubmitDetections and SubmitCorrection.
    /// </summary>
    /// <remarks>
    /// The dispatcher's job for both methods is a thin transformation from
    /// the generated OPC UA delegate signature to the provider's
    /// <see cref="IVisionFeedbackSink"/> contract, with these public-visible
    /// invariants:
    /// <list type="bullet">
    /// <item><description><see cref="StatusCodes.BadNotSupported"/> when no
    /// feedback sink is bound.</description></item>
    /// <item><description>The sink's own <see cref="ServiceResult"/> when
    /// the sink runs — good or bad, verbatim.</description></item>
    /// <item><description><see cref="StatusCodes.BadInternalError"/> when
    /// the sink throws a non-cancellation exception.</description></item>
    /// <item><description><see cref="OperationCanceledException"/> is
    /// re-thrown so cooperative cancellation is honoured end-to-end.</description></item>
    /// <item><description>The caller's arguments are forwarded to the sink
    /// verbatim so the sink sees the same request the client sent.</description></item>
    /// </list>
    /// The <c>resultId</c>-may-be-null branch of these two handlers is
    /// intentionally not asserted here: see the coverage report for the
    /// "answers instead of refuses" finding.
    /// </remarks>
    [TestFixture]
    public sealed class VisionFeedbackSubmitInspectionAndImageReferenceTests
    {
        [Test]
        public async Task SubmitInspectionWhenFeedbackSinkIsNullReturnsBadNotSupported()
        {
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 801,
                feedbackSink: null);

            SubmitInspectionResultMethodStateResult result = await harness.InvokeSubmitInspection(
                resultId: "r-1",
                evaluation: VisionResultEvaluationEnum.Ok,
                characteristics: ArrayOf<VisionCharacteristicDataType>.Empty).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "Without a feedback sink the dispatcher must refuse the call with BadNotSupported — " +
                "this is a configuration gap, not a client fault.");
        }

        [Test]
        public async Task SubmitInspectionForwardsRequestArgumentsToSink()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            VisionSubmitInspectionResultRequest? captured = null;
            sink.Setup(s => s.SubmitInspectionResultAsync(
                    It.IsAny<VisionSubmitInspectionResultRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitInspectionResultRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 802,
                feedbackSink: sink.Object);
            var characteristics = new ArrayOf<VisionCharacteristicDataType>(
                new[] { new VisionCharacteristicDataType { CharacteristicId = "measure/length" } });

            SubmitInspectionResultMethodStateResult result = await harness.InvokeSubmitInspection(
                resultId: "insp-42",
                evaluation: VisionResultEvaluationEnum.NotOk,
                characteristics: characteristics).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True,
                    "A Good ServiceResult from the sink must be forwarded to the caller unchanged.");
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Pipeline, Is.EqualTo(harness.PipelineNodeId),
                    "The pipeline NodeId the delegate was wired for must be forwarded to the sink.");
                Assert.That(captured!.ResultId, Is.EqualTo("insp-42"),
                    "The caller-supplied ResultId must be forwarded to the sink verbatim.");
                Assert.That(captured!.Evaluation, Is.EqualTo(VisionResultEvaluationEnum.NotOk),
                    "The caller-supplied Evaluation must be forwarded to the sink verbatim.");
            });
        }

        [Test]
        public async Task SubmitInspectionReturnsSinkFailureCodeVerbatim()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitInspectionResultAsync(
                    It.IsAny<VisionSubmitInspectionResultRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceResult(StatusCodes.BadUserAccessDenied));
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 803,
                feedbackSink: sink.Object);

            SubmitInspectionResultMethodStateResult result = await harness.InvokeSubmitInspection(
                resultId: "r-1",
                evaluation: VisionResultEvaluationEnum.Ok,
                characteristics: ArrayOf<VisionCharacteristicDataType>.Empty).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied),
                "The dispatcher must not rewrite a sink's own failure code; the sink is the domain owner.");
        }

        [Test]
        public async Task SubmitInspectionWhenSinkThrowsGeneralExceptionReturnsBadInternalError()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitInspectionResultAsync(
                    It.IsAny<VisionSubmitInspectionResultRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sink failure"));
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 804,
                feedbackSink: sink.Object);

            SubmitInspectionResultMethodStateResult result = await harness.InvokeSubmitInspection(
                resultId: "r-1",
                evaluation: VisionResultEvaluationEnum.Ok,
                characteristics: ArrayOf<VisionCharacteristicDataType>.Empty).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError),
                "A sink exception must be mapped to BadInternalError so the caller sees a clean failure code.");
        }

        [Test]
        public void SubmitInspectionPropagatesOperationCanceledExceptionFromSink()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitInspectionResultAsync(
                    It.IsAny<VisionSubmitInspectionResultRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 805,
                feedbackSink: sink.Object);

            Assert.That(async () => await harness.InvokeSubmitInspection(
                    resultId: "r-1",
                    evaluation: VisionResultEvaluationEnum.Ok,
                    characteristics: ArrayOf<VisionCharacteristicDataType>.Empty).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "The dispatcher must let OperationCanceledException flow through so cooperative cancellation is honoured.");
        }

        [Test]
        public async Task SubmitImageReferenceWhenFeedbackSinkIsNullReturnsBadNotSupported()
        {
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 811,
                feedbackSink: null);

            SubmitImageReferenceMethodStateResult result = await harness.InvokeSubmitImageReference(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                image: new VisionImageReferenceDataType(),
                resultId: "img-1").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SubmitImageReferenceForwardsRequestArgumentsToSink()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            VisionSubmitImageReferenceRequest? captured = null;
            sink.Setup(s => s.SubmitImageReferenceAsync(
                    It.IsAny<VisionSubmitImageReferenceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitImageReferenceRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 812,
                feedbackSink: sink.Object);
            var image = new VisionImageReferenceDataType { Uri = "opc.tcp://cam/frame/99" };

            SubmitImageReferenceMethodStateResult result = await harness.InvokeSubmitImageReference(
                purpose: VisionFeedbackPurposeEnum.Overlay,
                image: image,
                resultId: "img-42").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True,
                    "A Good ServiceResult from the sink must be forwarded to the caller unchanged.");
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Pipeline, Is.EqualTo(harness.PipelineNodeId),
                    "The pipeline NodeId the delegate was wired for must be forwarded to the sink.");
                Assert.That(captured!.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.Overlay),
                    "The caller-supplied Purpose must be forwarded to the sink verbatim.");
                Assert.That(captured!.Image.Uri, Is.EqualTo(image.Uri),
                    "The caller-supplied image reference must be forwarded to the sink verbatim.");
                Assert.That(captured!.ResultId, Is.EqualTo("img-42"),
                    "The caller-supplied ResultId must be forwarded to the sink verbatim.");
            });
        }

        [Test]
        public async Task SubmitImageReferenceReturnsSinkFailureCodeVerbatim()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitImageReferenceAsync(
                    It.IsAny<VisionSubmitImageReferenceRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceResult(StatusCodes.BadInvalidArgument));
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 813,
                feedbackSink: sink.Object);

            SubmitImageReferenceMethodStateResult result = await harness.InvokeSubmitImageReference(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                image: new VisionImageReferenceDataType(),
                resultId: "r-1").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SubmitImageReferenceWhenSinkThrowsGeneralExceptionReturnsBadInternalError()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitImageReferenceAsync(
                    It.IsAny<VisionSubmitImageReferenceRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sink failure"));
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 814,
                feedbackSink: sink.Object);

            SubmitImageReferenceMethodStateResult result = await harness.InvokeSubmitImageReference(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                image: new VisionImageReferenceDataType(),
                resultId: "r-1").ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public void SubmitImageReferencePropagatesOperationCanceledExceptionFromSink()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            sink.Setup(s => s.SubmitImageReferenceAsync(
                    It.IsAny<VisionSubmitImageReferenceRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new InspectionAndImageRefHarness(
                pipelineId: 815,
                feedbackSink: sink.Object);

            Assert.That(async () => await harness.InvokeSubmitImageReference(
                    purpose: VisionFeedbackPurposeEnum.Reconciliation,
                    image: new VisionImageReferenceDataType(),
                    resultId: "r-1").ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        private sealed class InspectionAndImageRefHarness
        {
            public InspectionAndImageRefHarness(
                uint pipelineId,
                IVisionFeedbackSink? feedbackSink)
            {
                PipelineNodeId = new NodeId(pipelineId, 4);
                var pipeline = new InferencePipelineState(null!);
                var feedback = new VisionFeedbackState(null!)
                {
                    SubmitDetections = new SubmitDetectionsMethodState(null!),
                    SubmitCorrection = new SubmitCorrectionMethodState(null!),
                    SubmitInspectionResult = new SubmitInspectionResultMethodState(null!),
                    SubmitImageReference = new SubmitImageReferenceMethodState(null!),
                };
                var registration = new PipelineRegistration(
                    "pipe",
                    PipelineNodeId,
                    pipeline,
                    new HashSet<string>(StringComparer.Ordinal))
                {
                    FeedbackSink = feedbackSink,
                };
                var registry = new VisionRegistry();
                registry.AddPipeline(registration);
                var dispatcher = new VisionMethodDispatcher(registry, NullLogger.Instance);
                dispatcher.AttachFeedbackMethods(PipelineNodeId, feedback);
                m_submitInspection = feedback.SubmitInspectionResult!.OnCallAsync;
                m_submitImageReference = feedback.SubmitImageReference!.OnCallAsync;
                Assert.That(m_submitInspection, Is.Not.Null);
                Assert.That(m_submitImageReference, Is.Not.Null);
            }

            public NodeId PipelineNodeId { get; }

            public async Task<SubmitInspectionResultMethodStateResult> InvokeSubmitInspection(
                string resultId,
                VisionResultEvaluationEnum evaluation,
                ArrayOf<VisionCharacteristicDataType> characteristics)
            {
                return await m_submitInspection!(
                    null!,
                    null!,
                    PipelineNodeId,
                    resultId,
                    evaluation,
                    characteristics,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<SubmitImageReferenceMethodStateResult> InvokeSubmitImageReference(
                VisionFeedbackPurposeEnum purpose,
                VisionImageReferenceDataType image,
                string resultId)
            {
                return await m_submitImageReference!(
                    null!,
                    null!,
                    PipelineNodeId,
                    purpose,
                    image,
                    resultId,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private readonly SubmitInspectionResultMethodStateMethodAsyncCallHandler? m_submitInspection;
            private readonly SubmitImageReferenceMethodStateMethodAsyncCallHandler? m_submitImageReference;
        }
    }
}
