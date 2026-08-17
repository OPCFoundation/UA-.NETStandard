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
    /// Pins the §6.4 inline-clip gating rule end-to-end through the
    /// server's <see cref="VisionMethodDispatcher"/>. The dispatcher is
    /// the only place these rules are enforced: <c>LatestClip</c> must
    /// report <see cref="StatusCodes.BadNotSupported"/> when
    /// <c>InlineDeliveryEnabled</c> is <c>false</c>, and inline bytes
    /// that exceed <c>MaxInlineClipSize</c> must be nulled with
    /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/>. Both rules
    /// must fire before the provider is consulted for the gating case
    /// and after the provider returns for the overflow case.
    /// </summary>
    [TestFixture]
    public sealed class VisionMediaGatingTests
    {
        [Test]
        public async Task GetClipWithRequestInlineTrueAndInlineDeliveryDisabledReturnsBadNotSupportedBeforeProviderIsCalled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>(MockBehavior.Strict);
            var harness = new MediaHarness(
                sensorId: 101, endpointId: 501, inlineDeliveryEnabled: false, maxInlineClipSize: 4096,
                mediaProvider.Object);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                mediaProvider.Verify(
                    p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()),
                    Times.Never,
                    "Inline gating must short-circuit before consulting the media provider.");
            });
        }

        [Test]
        public async Task GetClipWithRequestInlineFalseIsAllowedEvenWhenInlineDeliveryDisabled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType { Uri = "urn:test:clip" },
                    default,
                    default));
            var harness = new MediaHarness(
                sensorId: 102, endpointId: 502, inlineDeliveryEnabled: false, maxInlineClipSize: 4096,
                mediaProvider.Object);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: false).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.InlineImage.IsNull, Is.True,
                    "A caller that did not request inline delivery must not receive inline bytes on the way out.");
            });
        }

        [Test]
        public async Task GetClipWithInlineEnabledAndPayloadWithinLimitReturnsInlineBytes()
        {
            byte[] payload = new byte[512];
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType { Uri = "urn:test:clip" },
                    default,
                    ByteString.From(payload)));
            var harness = new MediaHarness(
                sensorId: 103, endpointId: 503, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.InlineImage.IsNull, Is.False);
                Assert.That(result.InlineImage.Length, Is.EqualTo(512));
            });
        }

        [Test]
        public async Task GetClipWithInlineEnabledButPayloadExceedingLimitReturnsBadEncodingLimitsAndNullsInlineImage()
        {
            byte[] payload = new byte[8192];
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType { Uri = "urn:test:big-clip" },
                    default,
                    ByteString.From(payload)));
            var harness = new MediaHarness(
                sensorId: 104, endpointId: 504, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.ServiceResult.StatusCode,
                    Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(result.InlineImage.IsNull, Is.True,
                    "Overflow must null the inline image so a naive client does not read partial bytes.");
                Assert.That(result.Image, Is.Not.Null,
                    "The out-of-band image reference must survive the overflow so callers can still fetch the clip through the URI channel.");
            });
        }

        [Test]
        public async Task GetClipWithNoMediaProviderReturnsBadNotSupported()
        {
            var harness = new MediaHarness(
                sensorId: 105, endpointId: 505, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider: null);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task GetClipWhenProviderThrowsUnexpectedExceptionReturnsBadInternalError()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var harness = new MediaHarness(
                sensorId: 106, endpointId: 506, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            GetClipMethodStateResult result = await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo(StatusCodes.BadInternalError));
        }

        [Test]
        public void GetClipWhenProviderThrowsOperationCanceledPropagates()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new MediaHarness(
                sensorId: 107, endpointId: 507, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            Assert.That(
                async () => await harness.InvokeGetClip(
                    endpoint: harness.EndpointNodeId,
                    resultId: "any",
                    requestInline: true).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task GetClipPublishesTheEncodedFrameOnLatestClipAndItsDescriptorOnLatestClipMetadata()
        {
            byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
            var descriptor = new VisionImageReferenceDataType
            {
                Uri = "opcua-inline://cell/frames/42",
                Width = 612u,
                Height = 512u
            };
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good, descriptor, default, ByteString.From(payload)));
            var harness = new MediaHarness(
                sensorId: 108, endpointId: 508, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            Assert.That(harness.Clip.LatestClip!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good),
                "Precondition: the harness starts with an unpublished LatestClip.");

            await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "42",
                requestInline: true).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.Clip.LatestClip!.Value.IsNull, Is.False,
                    "A clip the Server has just encoded is by definition the latest one; leaving LatestClip "
                    + "unwritten makes a consumer that reads the published frame first wait forever.");
                Assert.That(harness.Clip.LatestClip!.Value.Length, Is.EqualTo(payload.Length));
                Assert.That(StatusCode.IsGood(harness.Clip.LatestClip!.StatusCode), Is.True);
                Assert.That(harness.Clip.LatestClipMetadata!.Value, Is.Not.Null);
                Assert.That(harness.Clip.LatestClipMetadata!.Value.Uri, Is.EqualTo("opcua-inline://cell/frames/42"),
                    "The descriptor beside the published frame is how a consumer learns which image the "
                    + "detections are expressed in.");
                Assert.That(harness.Clip.LatestClipMetadata!.Value.Width, Is.EqualTo(612u));
                Assert.That(StatusCode.IsGood(harness.Clip.LatestClipMetadata!.StatusCode), Is.True);
            });
        }

        [Test]
        public async Task GetClipDoesNotPublishLatestClipWhenThePayloadOverflowsTheInlineLimit()
        {
            byte[] payload = new byte[8192];
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType { Uri = "urn:test:big-clip" },
                    default,
                    ByteString.From(payload)));
            var harness = new MediaHarness(
                sensorId: 109, endpointId: 509, inlineDeliveryEnabled: true, maxInlineClipSize: 4096,
                mediaProvider.Object);

            await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: true).ConfigureAwait(false);

            Assert.That(harness.Clip.LatestClip!.Value.IsNull, Is.True,
                "A clip the Server refused to deliver must not be published as the latest one.");
        }

        [Test]
        public async Task GetClipDoesNotPublishLatestClipWhenInlineDeliveryIsDisabled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetClipAsync(It.IsAny<VisionClipRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new VisionClipResult(
                    ServiceResult.Good,
                    new VisionImageReferenceDataType { Uri = "urn:test:clip" },
                    default,
                    ByteString.From([1, 2, 3, 4])));
            var harness = new MediaHarness(
                sensorId: 110, endpointId: 510, inlineDeliveryEnabled: false, maxInlineClipSize: 4096,
                mediaProvider.Object);

            await harness.InvokeGetClip(
                endpoint: harness.EndpointNodeId,
                resultId: "any",
                requestInline: false).ConfigureAwait(false);

            Assert.That(harness.Clip.LatestClip!.Value.IsNull, Is.True,
                "LatestClip is the inline channel, so a Server with inline delivery off must leave it alone.");
        }

        [Test]
        public void ClipEndpointExposesLatestClipMetadataAlongsideLatestClipInlineDeliveryEnabledAndMaxInlineSize()
        {
            var clip = new ClipEndpointState(null!)
            {
                InlineDeliveryEnabled = PropertyState<bool>.With<VariantBuilder>(null!, false),
                MaxInlineClipSize = PropertyState<uint>.With<VariantBuilder>(null!, 1024u),
                LatestClip = BaseDataVariableState<ByteString>.With<VariantBuilder>(null!),
                LatestClipMetadata = BaseDataVariableState<VisionImageReferenceDataType>.With<StructureBuilder<VisionImageReferenceDataType>>(null!),
            };

            Assert.Multiple(() =>
            {
                Assert.That(clip.InlineDeliveryEnabled, Is.Not.Null);
                Assert.That(clip.MaxInlineClipSize, Is.Not.Null);
                Assert.That(clip.LatestClip, Is.Not.Null,
                    "The inline byte channel must remain present on the type surface even when it is administratively disabled.");
                Assert.That(clip.LatestClipMetadata, Is.Not.Null,
                    "The metadata channel is the fallback that clients read when inline delivery is off.");
            });
        }

        private sealed class MediaHarness
        {
            public MediaHarness(
                uint sensorId,
                uint endpointId,
                bool inlineDeliveryEnabled,
                uint maxInlineClipSize,
                IVisionMediaProvider? mediaProvider)
            {
                SensorNodeId = new NodeId(sensorId, 4);
                EndpointNodeId = new NodeId(endpointId, 4);
                var sensor = new VisionSensorState(null!);
                var media = new VisionMediaManagementState(null!)
                {
                    GetClip = new GetClipMethodState(null!)
                };
                var clip = new ClipEndpointState(null!)
                {
                    NodeId = EndpointNodeId,
                    InlineDeliveryEnabled = PropertyState<bool>.With<VariantBuilder>(null!, inlineDeliveryEnabled),
                    MaxInlineClipSize = PropertyState<uint>.With<VariantBuilder>(null!, maxInlineClipSize),
                    LatestClip = BaseDataVariableState<ByteString>.With<VariantBuilder>(null!),
                    LatestClipMetadata = BaseDataVariableState<VisionImageReferenceDataType>
                        .With<StructureBuilder<VisionImageReferenceDataType>>(null!),
                };

                var registration = new SensorRegistration(
                    "cam",
                    SensorNodeId,
                    sensor,
                    VisionSensorModalityEnum.Area2D,
                    VisionRealityKindEnum.Physical,
                    new HashSet<string>(StringComparer.Ordinal),
                    mediaProvider);
                registration.ClipEndpoints.Add(clip);
                sensor.Media = media;
                Clip = clip;

                m_registry = new VisionRegistry();
                m_registry.AddSensor(registration);
                var dispatcher = new VisionMethodDispatcher(m_registry, NullLogger.Instance);
                dispatcher.AttachMediaMethods(SensorNodeId, media);
                m_getClip = media.GetClip.OnCallAsync;
                Assert.That(m_getClip, Is.Not.Null,
                    "AttachMediaMethods must wire an OnCallAsync handler onto GetClip.");
                Media = media;
            }

            public NodeId SensorNodeId { get; }

            public NodeId EndpointNodeId { get; }

            public VisionMediaManagementState Media { get; }

            public ClipEndpointState Clip { get; }

            public async Task<GetClipMethodStateResult> InvokeGetClip(
                NodeId endpoint, string resultId, bool requestInline)
            {
                return await m_getClip!(
                    null!,
                    Media.GetClip!,
                    SensorNodeId,
                    endpoint,
                    resultId,
                    DateTimeUtc.Now,
                    VisionClipFormatEnum.Png,
                    requestInline,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private readonly VisionRegistry m_registry;
            private readonly GetClipMethodStateMethodAsyncCallHandler? m_getClip;
        }
    }
}
