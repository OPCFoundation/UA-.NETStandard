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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowResultsTests
    {
        [Test]
        public async Task ResultPropertyNotBrowseNameDeterminesCorrelation()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId result = fixture.AddResult("exact-result");
            NodeId actual = await VisionWorkflowResults.FindAsync(
                fixture.Context, fixture.Pipeline, "exact-result", CancellationToken.None).ConfigureAwait(false);
            Assert.That(actual, Is.EqualTo(result));

            fixture.Set(result, "ResultId", Variant.From("another-result"));
            await Assert.ThatAsync(() => VisionWorkflowResults.FindAsync(
                fixture.Context, fixture.Pipeline, "exact-result", CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotFound))
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase(1)]
        [TestCase(32)]
        [TestCase(33)]
        public async Task ResultSearchHonorsTheExactCandidateCapEvenAfterFindingAMatch(int count)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId expected = fixture.AddResult("wanted");
            for (int index = 1; index < count; index++)
            {
                fixture.AddResult("other-" + index);
            }
            if (count <= 32)
            {
                Assert.That(await VisionWorkflowResults.FindAsync(
                    fixture.Context, fixture.Pipeline, "wanted", CancellationToken.None).ConfigureAwait(false),
                    Is.EqualTo(expected));
            }
            else
            {
                await Assert.ThatAsync(() => VisionWorkflowResults.FindAsync(
                    fixture.Context, fixture.Pipeline, "wanted", CancellationToken.None),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AmbiguousOrForeignPipelineResultIdentityIsRejected(bool foreign)
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.AddResult("wanted");
            NodeId other = fixture.AddResult("other");
            fixture.Set(other, "ResultId", Variant.From("wanted"));
            if (foreign)
            {
                fixture.Set(other, "Pipeline", Variant.From(new NodeId("different", 1)));
            }
            await Assert.ThatAsync(() => VisionWorkflowResults.FindAsync(
                fixture.Context, fixture.Pipeline, "wanted", CancellationToken.None),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch))
                .ConfigureAwait(false);
        }

        [TestCase(VisionResultKind.Detection)]
        [TestCase(VisionResultKind.Inspection)]
        [TestCase(VisionResultKind.Segmentation)]
        public async Task TypedResultKindsKeepExactIdentityAndPayload(VisionResultKind kind)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId node = fixture.AddResult(kind: kind);
            CompanionOperationResult result = await ReadAsync(fixture, node).ConfigureAwait(false);

            Assert.That(Field(result.Values, "Result ID").TryGetValue(out string? id), Is.True);
            Assert.That(id, Is.EqualTo("result-7"));
            Assert.That(Field(result.Values, "Sensor").TryGetValue(out NodeId sensor), Is.True);
            Assert.That(sensor, Is.EqualTo(fixture.Sensor));
            Assert.That(Field(result.Values, "Pipeline").TryGetValue(out NodeId pipeline), Is.True);
            Assert.That(pipeline, Is.EqualTo(fixture.Pipeline));
            switch (kind)
            {
                case VisionResultKind.Detection:
                    Assert.That(Field(result.Values, "Detections").TryGetValue(
                        out ArrayOf<VisionDetectionDataType> detections, fixture.Context.Session.MessageContext),
                        Is.True);
                    Assert.That(detections.Count, Is.EqualTo(1));
                    Assert.That(detections[0].Confidence, Is.EqualTo(0.875));
                    break;
                case VisionResultKind.Inspection:
                    Assert.That(Field(result.Values, "Characteristics").TryGetValue(
                        out ArrayOf<VisionCharacteristicDataType> measurements,
                        fixture.Context.Session.MessageContext), Is.True);
                    Assert.That(measurements.Count, Is.EqualTo(1));
                    Assert.That(measurements[0].Actual, Is.EqualTo(10.25));
                    break;
                case VisionResultKind.Segmentation:
                    Assert.That(Field(result.Values, "Label classes").TryGetValue(out ArrayOf<string> labels), Is.True);
                    Assert.That(labels.Count, Is.EqualTo(2));
                    Assert.That(labels[1], Is.EqualTo("part"));
                    Assert.That(Field(result.Values, "Mask SHA-256").TryGetValue(out string? digest), Is.True);
                    Assert.That(digest, Is.EqualTo(Convert.ToHexString(Image().Digest.Span)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [TestCase("id")]
        [TestCase("sensor")]
        [TestCase("pipeline")]
        [TestCase("created")]
        [TestCase("bad")]
        [TestCase("uncertain")]
        [TestCase("wrong-type")]
        [TestCase("missing-array")]
        [TestCase("too-many")]
        [TestCase("pose-frame")]
        public async Task ResultEvidenceCannotBecomeSuccessfulDefaults(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId node = fixture.AddResult();
            switch (fault)
            {
                case "id":
                    fixture.Set(node, "ResultId", Variant.From("other"));
                    break;
                case "sensor":
                    fixture.Set(node, "Sensor", Variant.From(new NodeId("other", 1)));
                    break;
                case "pipeline":
                    fixture.Set(node, "Pipeline", Variant.From(new NodeId("other", 1)));
                    break;
                case "created":
                    fixture.Set(node, "CreationTime", Variant.From(default(DateTimeUtc)));
                    break;
                case "bad":
                case "uncertain":
                    fixture.Set(node, "Detections", Variant.FromStructure([Detection()]),
                        fault == "bad" ? StatusCodes.BadUserAccessDenied : StatusCodes.Uncertain);
                    break;
                case "wrong-type":
                    fixture.Set(node, "Detections", Variant.From("not detections"));
                    break;
                case "missing-array":
                    fixture.Fixture.Children.Remove((node, "Detections"));
                    break;
                case "too-many":
                    fixture.Set(node, "Detections", Variant.FromStructure(
                        new ArrayOf<VisionDetectionDataType>(Enumerable.Range(0, 33).Select(index =>
                            Detection("item-" + index)).ToArray())));
                    break;
                case "pose-frame":
                    VisionDetectionDataType detection = Detection();
                    detection.HasPose = true;
                    detection.Pose = Pose();
                    detection.Pose.FrameId = "different-frame";
                    fixture.Set(node, "Detections", Variant.FromStructure([detection]));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }

            await Assert.ThatAsync(() => ReadAsync(fixture, node), Throws.TypeOf<ServiceResultException>())
                .ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        [Test]
        public async Task ResultSnapshotDoesNotAliasCallerOwnedDetectionsOrGeometry()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId node = fixture.AddResult();
            VisionDetectionDataType detection = Detection();
            fixture.Set(node, "Detections", Variant.FromStructure([detection]));
            CompanionOperationResult result = await ReadAsync(fixture, node).ConfigureAwait(false);
            detection.ClassLabel = "changed";
            detection.BoundingBox2D.CenterX = 999;

            Assert.That(Field(result.Values, "Detections").TryGetValue(
                out ArrayOf<VisionDetectionDataType> captured, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(captured[0].ClassLabel, Is.EqualTo("component"));
            Assert.That(captured[0].BoundingBox2D.CenterX, Is.EqualTo(30.5));
        }

        [Test]
        public async Task ExplicitEmptyDetectionsRemainAnEmptyObservation()
        {
            var fixture = new VisionWorkflowTestSupport();
            NodeId node = fixture.AddResult();
            fixture.Set(node, "Detections", Variant.FromStructure(ArrayOf<VisionDetectionDataType>.Empty));
            CompanionOperationResult result = await ReadAsync(fixture, node).ConfigureAwait(false);
            Assert.That(Field(result.Values, "Detections").TryGetValue(
                out ArrayOf<VisionDetectionDataType> captured, fixture.Context.Session.MessageContext), Is.True);
            Assert.That(captured.IsNull, Is.False);
            Assert.That(captured.IsEmpty, Is.True);
        }

        private static Task<CompanionOperationResult> ReadAsync(VisionWorkflowTestSupport fixture, NodeId node)
        {
            return VisionWorkflowResults.ReadAsync(
                fixture.Context, node, "result-7", fixture.Sensor, fixture.Pipeline, CancellationToken.None);
        }
    }
}
