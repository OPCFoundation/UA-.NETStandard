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
    /// Pins the feedback publication path. §9 feedback is how an
    /// off-Server model publishes results the Server itself did not
    /// compute — every rejection code the dispatcher returns is a
    /// public contract:
    /// <list type="bullet">
    /// <item><description><see cref="StatusCodes.BadNotSupported"/> when
    /// no <see cref="IVisionFeedbackSink"/> is bound to the pipeline
    /// (missing configuration, not a client fault).</description></item>
    /// <item><description>The sink's own <see cref="ServiceResult"/> when
    /// the sink runs — good or bad, verbatim.</description></item>
    /// <item><description><see cref="StatusCodes.BadInternalError"/> when
    /// the sink throws a non-cancellation exception.</description></item>
    /// <item><description>Propagates
    /// <see cref="OperationCanceledException"/> unchanged so cooperative
    /// cancellation of the caller's context is honoured end-to-end.</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public sealed class VisionFeedbackDispatcherTests
    {
        [Test]
        public async Task SubmitDetectionsWhenFeedbackSinkIsNullReturnsBadNotSupported()
        {
            var harness = new FeedbackHarness(pipelineId: 601, feedbackSink: null);

            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: ArrayOf<VisionDetectionDataType>.Empty,
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SubmitDetectionsForwardsGoodResultFromSinkVerbatimAndCarriesRequestArguments()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            VisionSubmitDetectionsRequest? captured = null;
            sink.Setup(s => s.SubmitDetectionsAsync(It.IsAny<VisionSubmitDetectionsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitDetectionsRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new FeedbackHarness(pipelineId: 602, feedbackSink: sink.Object);
            var frameRef = new VisionImageReferenceDataType { Uri = "opc.tcp://cam/frame/42" };
            ByteString inline = ByteString.From(new byte[] { 1, 2, 3, 4 });
            ArrayOf<VisionDetectionDataType> detections = OneDetection();

            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: detections,
                frameReference: frameRef,
                inlineImage: inline).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Pipeline, Is.EqualTo(harness.PipelineNodeId));
                Assert.That(captured!.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.Reconciliation));
                Assert.That(captured!.InlineImage, Is.EqualTo(inline));
            });
        }

        [Test]
        public async Task SubmitDetectionsReturnsSinkFailureCodeVerbatim()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            sink.Setup(s => s.SubmitDetectionsAsync(It.IsAny<VisionSubmitDetectionsRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceResult(StatusCodes.BadInvalidArgument));
            var harness = new FeedbackHarness(pipelineId: 603, feedbackSink: sink.Object);

            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: ArrayOf<VisionDetectionDataType>.Empty,
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument),
                "The dispatcher must not rewrite a sink's own failure code; the sink is the domain owner.");
        }

        [Test]
        public async Task SubmitDetectionsWhenSinkThrowsGeneralExceptionReturnsBadInternalError()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            sink.Setup(s => s.SubmitDetectionsAsync(It.IsAny<VisionSubmitDetectionsRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var harness = new FeedbackHarness(pipelineId: 604, feedbackSink: sink.Object);

            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: OneDetection(),
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public void SubmitDetectionsPropagatesOperationCanceledExceptionFromSink()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            sink.Setup(s => s.SubmitDetectionsAsync(It.IsAny<VisionSubmitDetectionsRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new FeedbackHarness(pipelineId: 605, feedbackSink: sink.Object);

            Assert.That(
                async () => await harness.InvokeSubmitDetections(
                    purpose: VisionFeedbackPurposeEnum.Reconciliation,
                    detections: OneDetection(),
                    frameReference: new VisionImageReferenceDataType(),
                    inlineImage: default).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "The dispatcher must let OperationCanceledException flow through so cooperative cancellation is honoured.");
        }

        [Test]
        public async Task SubmitCorrectionWhenFeedbackSinkIsNullReturnsBadNotSupported()
        {
            var harness = new FeedbackHarness(pipelineId: 606, feedbackSink: null);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.Overlay,
                correctedDetections: ArrayOf<VisionDetectionDataType>.Empty,
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: new LocalizedText("en", "test"),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SubmitCorrectionForwardsRequestArgumentsToSink()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            VisionSubmitCorrectionRequest? captured = null;
            sink.Setup(s => s.SubmitCorrectionAsync(It.IsAny<VisionSubmitCorrectionRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitCorrectionRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new FeedbackHarness(pipelineId: 607, feedbackSink: sink.Object);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-42",
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                correctedDetections: OneDetection(),
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: new LocalizedText("en", "manual correction"),
                inlineImage: default).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Pipeline, Is.EqualTo(harness.PipelineNodeId));
                Assert.That(captured!.ResultId, Is.EqualTo("r-42"));
                Assert.That(captured!.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.GroundTruthLabel));
            });
        }

        [Test]
        public async Task SubmitCorrectionRefusesAMissingResultIdRatherThanInventingOne()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            VisionSubmitCorrectionRequest? captured = null;
            sink.Setup(s => s.SubmitCorrectionAsync(It.IsAny<VisionSubmitCorrectionRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitCorrectionRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new FeedbackHarness(pipelineId: 608, feedbackSink: sink.Object);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: null!,
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                correctedDetections: ArrayOf<VisionDetectionDataType>.Empty,
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: new LocalizedText("en", "no result-id supplied"),
                inlineImage: default).ConfigureAwait(false);

            // ResultId names the result being corrected. Substituting an empty string would ask
            // the sink to correct an unnamed result, so the dispatcher refuses instead and the
            // sink is never called.
            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
                Assert.That(captured, Is.Null);
            });
        }

        [Test]
        public async Task SubmitCorrectionReturnsSinkFailureVerbatim()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            sink.Setup(s => s.SubmitCorrectionAsync(It.IsAny<VisionSubmitCorrectionRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceResult(StatusCodes.BadUserAccessDenied));
            var harness = new FeedbackHarness(pipelineId: 609, feedbackSink: sink.Object);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                correctedDetections: OneDetection(),
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: default,
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task SubmitCorrectionWhenSinkThrowsGeneralExceptionReturnsBadInternalError()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            sink.Setup(s => s.SubmitCorrectionAsync(It.IsAny<VisionSubmitCorrectionRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("sink failure"));
            var harness = new FeedbackHarness(pipelineId: 610, feedbackSink: sink.Object);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                correctedDetections: OneDetection(),
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: default,
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public async Task AttachFeedbackMethodsIsSafeWhenIndividualMethodsAreMissing()
        {
            var harness = new FeedbackHarness(pipelineId: 611, feedbackSink: null, feedbackWithSubmitOnly: true);

            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: ArrayOf<VisionDetectionDataType>.Empty,
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported),
                "AttachFeedbackMethods must tolerate a partial feedback surface — a missing SubmitCorrection method must not cause an NRE when SubmitDetections is invoked.");
        }

        [Test]
        public async Task SubmitDetectionsRefusesAnEmptyDetectionArrayWithoutTheFlag()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            var harness = new FeedbackHarness(pipelineId: 612, feedbackSink: sink.Object);

            // Part 9.5 pairs the array with the flag: an empty array without
            // SceneIsEmpty is a lost payload, not an observation. The strict mock
            // proves the sink is never consulted - the dispatcher owns this rule.
            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.Reconciliation,
                detections: ArrayOf<VisionDetectionDataType>.Empty,
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SubmitDetectionsAcceptsAnEmptyDetectionArrayWhenSceneIsEmpty()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            VisionSubmitDetectionsRequest? captured = null;
            sink.Setup(s => s.SubmitDetectionsAsync(It.IsAny<VisionSubmitDetectionsRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitDetectionsRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new FeedbackHarness(pipelineId: 615, feedbackSink: sink.Object);

            // "I examined this frame and there is nothing in it" is the terminating
            // condition of a bin-picking task and a valid negative training label.
            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                detections: ArrayOf<VisionDetectionDataType>.Empty,
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default,
                sceneIsEmpty: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.SceneIsEmpty, Is.True,
                    "The sink has to see the flag; it is what distinguishes the " +
                    "observation from an empty submission it should discard.");
            });
        }

        [Test]
        public async Task SubmitDetectionsRefusesSceneIsEmptyWithDetectionsAttached()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            var harness = new FeedbackHarness(pipelineId: 616, feedbackSink: sink.Object);

            // Asserting the frame is empty while attaching what was found in it says
            // two contradictory things about one frame.
            SubmitDetectionsMethodStateResult result = await harness.InvokeSubmitDetections(
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                detections: OneDetection(),
                frameReference: new VisionImageReferenceDataType(),
                inlineImage: default,
                sceneIsEmpty: true).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SubmitCorrectionRefusesWhenNeitherCorrectedArrayIsPopulated()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            var harness = new FeedbackHarness(pipelineId: 613, feedbackSink: sink.Object);

            // Part 9.5 asks for at most one non-empty array, and both empty means
            // something only when RetractAll says so.
            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                correctedDetections: ArrayOf<VisionDetectionDataType>.Empty,
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: default,
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SubmitCorrectionAcceptsBothArraysEmptyWhenRetractAllIsSet()
        {
            var sink = new Mock<IVisionFeedbackSink>();
            VisionSubmitCorrectionRequest? captured = null;
            sink.Setup(s => s.SubmitCorrectionAsync(It.IsAny<VisionSubmitCorrectionRequest>(), It.IsAny<CancellationToken>()))
                .Returns<VisionSubmitCorrectionRequest, CancellationToken>((req, _) =>
                {
                    captured = req;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            var harness = new FeedbackHarness(pipelineId: 617, feedbackSink: sink.Object);

            // The false-positive retraction: correcting a result down to nothing.
            // It is the error class an operator is most able to label with
            // confidence, and it was inexpressible before this flag existed.
            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                correctedDetections: ArrayOf<VisionDetectionDataType>.Empty,
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: default,
                inlineImage: default,
                retractAll: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.RetractAll, Is.True);
            });
        }

        [Test]
        public async Task SubmitCorrectionRefusesRetractAllWithAReplacementAttached()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            var harness = new FeedbackHarness(pipelineId: 618, feedbackSink: sink.Object);

            // RetractAll asserts the result should contain nothing at all, so
            // carrying a replacement contradicts it.
            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                correctedDetections: OneDetection(),
                correctedCharacteristics: ArrayOf<VisionCharacteristicDataType>.Empty,
                reason: default,
                inlineImage: default,
                retractAll: true).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task SubmitCorrectionRefusesWhenBothCorrectedArraysArePopulated()
        {
            var sink = new Mock<IVisionFeedbackSink>(MockBehavior.Strict);
            var harness = new FeedbackHarness(pipelineId: 614, feedbackSink: sink.Object);

            SubmitCorrectionMethodStateResult result = await harness.InvokeSubmitCorrection(
                resultId: "r-1",
                purpose: VisionFeedbackPurposeEnum.GroundTruthLabel,
                correctedDetections: OneDetection(),
                correctedCharacteristics: OneCharacteristic(),
                reason: default,
                inlineImage: default).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        private static ArrayOf<VisionDetectionDataType> OneDetection()
        {
            return new[]
            {
                new VisionDetectionDataType { ClassLabel = "Part", Confidence = 0.9 }
            }.ToArrayOf();
        }

        private static ArrayOf<VisionCharacteristicDataType> OneCharacteristic()
        {
            return new[]
            {
                new VisionCharacteristicDataType { Name = "Diameter" }
            }.ToArrayOf();
        }

        private sealed class FeedbackHarness
        {
            public FeedbackHarness(
                uint pipelineId,
                IVisionFeedbackSink? feedbackSink,
                bool feedbackWithSubmitOnly = false)
            {
                PipelineNodeId = new NodeId(pipelineId, 4);
                var pipeline = new InferencePipelineState(null);
                var feedback = new VisionFeedbackState(null)
                {
                    SubmitDetections = new SubmitDetectionsMethodState(null)
                };
                if (!feedbackWithSubmitOnly)
                {
                    feedback.SubmitCorrection = new SubmitCorrectionMethodState(null);
                    feedback.SubmitInspectionResult = new SubmitInspectionResultMethodState(null);
                    feedback.SubmitImageReference = new SubmitImageReferenceMethodState(null);
                }

                var registration = new PipelineRegistration(
                    "pipe",
                    PipelineNodeId,
                    pipeline,
                    new HashSet<string>(StringComparer.Ordinal))
                {
                    FeedbackSink = feedbackSink
                };

                m_registry = new VisionRegistry();
                m_registry.AddPipeline(registration);
                var dispatcher = new VisionMethodDispatcher(m_registry, NullLogger.Instance);
                dispatcher.AttachFeedbackMethods(PipelineNodeId, feedback);
                m_submitDetections = feedback.SubmitDetections!.OnCallAsync;
                m_submitCorrection = feedback.SubmitCorrection?.OnCallAsync;

                Assert.That(m_submitDetections, Is.Not.Null,
                    "AttachFeedbackMethods must wire an OnCallAsync handler onto SubmitDetections.");
            }

            public NodeId PipelineNodeId { get; }

            public async Task<SubmitDetectionsMethodStateResult> InvokeSubmitDetections(
                VisionFeedbackPurposeEnum purpose,
                ArrayOf<VisionDetectionDataType> detections,
                VisionImageReferenceDataType frameReference,
                ByteString inlineImage,
                bool sceneIsEmpty = false)
            {
                return await m_submitDetections!(
                    null!,
                    null!,
                    PipelineNodeId,
                    purpose,
                    detections,
                    frameReference,
                    inlineImage,
                    sceneIsEmpty,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<SubmitCorrectionMethodStateResult> InvokeSubmitCorrection(
                string resultId,
                VisionFeedbackPurposeEnum purpose,
                ArrayOf<VisionDetectionDataType> correctedDetections,
                ArrayOf<VisionCharacteristicDataType> correctedCharacteristics,
                LocalizedText reason,
                ByteString inlineImage,
                bool retractAll = false)
            {
                Assert.That(m_submitCorrection, Is.Not.Null,
                    "This helper requires a full feedback surface; construct FeedbackHarness with feedbackWithSubmitOnly=false.");
                return await m_submitCorrection!(
                    null!,
                    null!,
                    PipelineNodeId,
                    resultId,
                    purpose,
                    correctedDetections,
                    correctedCharacteristics,
                    reason,
                    inlineImage,
                    retractAll,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private readonly VisionRegistry m_registry;
            private readonly SubmitDetectionsMethodStateMethodAsyncCallHandler? m_submitDetections;
            private readonly SubmitCorrectionMethodStateMethodAsyncCallHandler? m_submitCorrection;
        }
    }
}
