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
using Moq;
using NUnit.Framework;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Exercises the simple request/result value records exposed to
    /// providers and sinks. These records are part of the public API
    /// surface; their positional-property behaviour must remain intact.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed class VisionProviderContractRecordsTests
    {
        [Test]
        public void VisionInferenceRunRequestRoundTripsPositionalProperties()
        {
            var pipeline = new NodeId(1, 1);
            var sensor = new NodeId(2, 1);
            var deployment = new NodeId(3, 1);
            DateTimeUtc timestamp = DateTimeUtc.From(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

            var request = new VisionInferenceRunRequest(pipeline, sensor, deployment, timestamp);

            Assert.Multiple(() =>
            {
                Assert.That(request.Pipeline, Is.EqualTo(pipeline));
                Assert.That(request.Sensor, Is.EqualTo(sensor));
                Assert.That(request.Deployment, Is.EqualTo(deployment));
                Assert.That(request.Timestamp, Is.EqualTo(timestamp));
            });
        }

        [Test]
        public void VisionInferenceRunRequestEqualityIsStructural()
        {
            var a = new VisionInferenceRunRequest(
                new NodeId(1, 1), new NodeId(2, 1), new NodeId(3, 1),
                DateTimeUtc.From(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
            var b = new VisionInferenceRunRequest(
                new NodeId(1, 1), new NodeId(2, 1), new NodeId(3, 1),
                DateTimeUtc.From(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void VisionInferenceRunResultRoundTripsPositionalProperties()
        {
            ServiceResult sr = ServiceResult.Good;
            var result = new VisionInferenceRunResult(sr, "result-42");

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult, Is.EqualTo(sr));
                Assert.That(result.ResultId, Is.EqualTo("result-42"));
            });
        }

        [Test]
        public void VisionStreamRequestRoundTripsPositionalProperties()
        {
            var endpoint = new NodeId(10, 1);
            var request = new VisionStreamRequest(endpoint, "profile-1", VisionStreamProtocolEnum.Rtsp);

            Assert.Multiple(() =>
            {
                Assert.That(request.Endpoint, Is.EqualTo(endpoint));
                Assert.That(request.ProfileName, Is.EqualTo("profile-1"));
                Assert.That(request.PreferredProtocol, Is.EqualTo(VisionStreamProtocolEnum.Rtsp));
            });
        }

        [Test]
        public void VisionStreamLeaseRoundTripsPositionalProperties()
        {
            ServiceResult sr = ServiceResult.Good;
            var session = new VisionStreamSessionDataType
            {
                Uri = "rtsp://x",
                SessionToken = ByteString.Empty
            };
            var endpointOut = new NodeId(1, 1);

            var lease = new VisionStreamLease(sr, session, endpointOut);

            Assert.Multiple(() =>
            {
                Assert.That(lease.ServiceResult, Is.EqualTo(sr));
                Assert.That(lease.Session, Is.SameAs(session));
                Assert.That(lease.EndpointOut, Is.EqualTo(endpointOut));
            });
        }

        [Test]
        public void VisionStreamConfigurationRequestRoundTripsPositionalProperties()
        {
            var endpoint = new NodeId(10, 1);
            var request = new VisionStreamConfigurationRequest(
                endpoint, VisionVideoCodecEnum.H264, 1920, 1080, 30.0, 8_000_000);

            Assert.Multiple(() =>
            {
                Assert.That(request.Endpoint, Is.EqualTo(endpoint));
                Assert.That(request.Codec, Is.EqualTo(VisionVideoCodecEnum.H264));
                Assert.That(request.Width, Is.EqualTo((uint)1920));
                Assert.That(request.Height, Is.EqualTo((uint)1080));
                Assert.That(request.FrameRate, Is.EqualTo(30.0));
                Assert.That(request.Bitrate, Is.EqualTo((uint)8_000_000));
            });
        }

        [Test]
        public void VisionClipRequestRoundTripsPositionalProperties()
        {
            var endpoint = new NodeId(10, 1);
            DateTimeUtc timestamp = DateTimeUtc.From(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

            var request = new VisionClipRequest(
                endpoint, "result-1", timestamp, VisionClipFormatEnum.Jpeg, RequestInline: true);

            Assert.Multiple(() =>
            {
                Assert.That(request.Endpoint, Is.EqualTo(endpoint));
                Assert.That(request.ResultId, Is.EqualTo("result-1"));
                Assert.That(request.Timestamp, Is.EqualTo(timestamp));
                Assert.That(request.Format, Is.EqualTo(VisionClipFormatEnum.Jpeg));
                Assert.That(request.RequestInline, Is.True);
            });
        }

        [Test]
        public void VisionClipResultRoundTripsPositionalProperties()
        {
            ServiceResult sr = ServiceResult.Good;
            var image = new VisionImageReferenceDataType();
            var endpointOut = new NodeId(1, 1);
            ByteString inline = ByteString.From(new byte[] { 0x01, 0x02 });

            var result = new VisionClipResult(sr, image, endpointOut, inline);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult, Is.EqualTo(sr));
                Assert.That(result.Image, Is.SameAs(image));
                Assert.That(result.EndpointOut, Is.EqualTo(endpointOut));
                Assert.That(result.InlineImage, Is.EqualTo(inline));
            });
        }

        [Test]
        public void VisionSubmitDetectionsRequestRoundTripsPositionalProperties()
        {
            var pipeline = new NodeId(10, 1);
            ArrayOf<VisionDetectionDataType> detections =
                new List<VisionDetectionDataType>().ToArrayOf();
            var frameRef = new VisionImageReferenceDataType();
            ByteString inline = ByteString.Empty;

            var request = new VisionSubmitDetectionsRequest(
                pipeline, VisionFeedbackPurposeEnum.Overlay, detections, frameRef, inline);

            Assert.Multiple(() =>
            {
                Assert.That(request.Pipeline, Is.EqualTo(pipeline));
                Assert.That(request.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.Overlay));
                Assert.That(request.FrameReference, Is.SameAs(frameRef));
                Assert.That(request.InlineImage, Is.EqualTo(inline));
            });
        }

        [Test]
        public void VisionSubmitInspectionResultRequestRoundTripsPositionalProperties()
        {
            var pipeline = new NodeId(10, 1);
            ArrayOf<VisionCharacteristicDataType> characteristics =
                new List<VisionCharacteristicDataType>().ToArrayOf();

            var request = new VisionSubmitInspectionResultRequest(
                pipeline, "result-1", VisionResultEvaluationEnum.Ok, characteristics);

            Assert.Multiple(() =>
            {
                Assert.That(request.Pipeline, Is.EqualTo(pipeline));
                Assert.That(request.ResultId, Is.EqualTo("result-1"));
                Assert.That(request.Evaluation, Is.EqualTo(VisionResultEvaluationEnum.Ok));
            });
        }

        [Test]
        public void VisionSubmitCorrectionRequestRoundTripsPositionalProperties()
        {
            var pipeline = new NodeId(10, 1);
            ArrayOf<VisionDetectionDataType> dets =
                new List<VisionDetectionDataType>().ToArrayOf();
            ArrayOf<VisionCharacteristicDataType> chars =
                new List<VisionCharacteristicDataType>().ToArrayOf();
            var reason = new LocalizedText("en", "bad");
            ByteString inline = ByteString.Empty;

            var request = new VisionSubmitCorrectionRequest(
                pipeline, "result-1", VisionFeedbackPurposeEnum.GroundTruthLabel,
                dets, chars, reason, inline);

            Assert.Multiple(() =>
            {
                Assert.That(request.Pipeline, Is.EqualTo(pipeline));
                Assert.That(request.ResultId, Is.EqualTo("result-1"));
                Assert.That(request.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.GroundTruthLabel));
                Assert.That(request.Reason, Is.EqualTo(reason));
                Assert.That(request.InlineImage, Is.EqualTo(inline));
            });
        }

        [Test]
        public void VisionSubmitImageReferenceRequestRoundTripsPositionalProperties()
        {
            var pipeline = new NodeId(10, 1);
            var image = new VisionImageReferenceDataType();

            var request = new VisionSubmitImageReferenceRequest(
                pipeline, VisionFeedbackPurposeEnum.Overlay, image, "result-1");

            Assert.Multiple(() =>
            {
                Assert.That(request.Pipeline, Is.EqualTo(pipeline));
                Assert.That(request.Purpose, Is.EqualTo(VisionFeedbackPurposeEnum.Overlay));
                Assert.That(request.Image, Is.SameAs(image));
                Assert.That(request.ResultId, Is.EqualTo("result-1"));
            });
        }
    }
}
