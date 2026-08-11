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
    /// Closes the coverage gap on the stream-endpoint side of
    /// <see cref="VisionMethodDispatcher"/>: <c>GetStreamEndpoint</c>,
    /// <c>ReleaseStreamEndpoint</c>, <c>ConfigureStreamEndpoint</c> and
    /// <c>SelectEndpoint</c> — every branch (missing provider, provider throws
    /// <c>OperationCanceledException</c>, provider throws general exception,
    /// good result forwarding, and the <c>SelectEndpoint</c> side effect of
    /// updating the preferred-endpoint properties) is asserted.
    /// </summary>
    [TestFixture]
    public sealed class VisionMethodDispatcherStreamEndpointTests
    {
        [Test]
        public async Task GetStreamEndpointReturnsBadNotSupportedWhenNoMediaProviderIsRegistered()
        {
            var harness = new StreamHarness(mediaProvider: null);

            GetStreamEndpointMethodStateResult result = await harness.InvokeGetStreamEndpoint(
                harness.EndpointNodeId,
                "default",
                VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task GetStreamEndpointForwardsLeaseFromProviderOnSuccess()
        {
            var lease = new VisionStreamLease(
                ServiceResult.Good,
                new VisionStreamSessionDataType
                {
                    SessionToken = new ByteString(new byte[] { 9, 9 }),
                    Uri = "rtsp://cam.local/main",
                    Protocol = VisionStreamProtocolEnum.Rtsp,
                    ExpiresAt = new DateTimeUtc(new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc))
                },
                new NodeId(9999u, 4));
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetStreamAsync(
                    It.IsAny<VisionStreamRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(lease);
            var harness = new StreamHarness(mediaProvider.Object);

            GetStreamEndpointMethodStateResult result = await harness.InvokeGetStreamEndpoint(
                harness.EndpointNodeId,
                "high",
                VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(result.Session, Is.Not.Null);
                Assert.That(result.Session.Uri, Is.EqualTo("rtsp://cam.local/main"));
                Assert.That(result.EndpointOut, Is.EqualTo(new NodeId(9999u, 4)),
                    "the dispatcher must not rewrite the resolved endpoint the provider returned");
            });
            mediaProvider.Verify(p => p.GetStreamAsync(
                It.Is<VisionStreamRequest>(r =>
                    r.Endpoint == harness.EndpointNodeId &&
                    r.ProfileName == "high" &&
                    r.PreferredProtocol == VisionStreamProtocolEnum.Rtsp),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GetStreamEndpointReturnsBadInternalErrorWhenProviderThrowsUnexpected()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetStreamAsync(
                    It.IsAny<VisionStreamRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("provider blew up"));
            var harness = new StreamHarness(mediaProvider.Object);

            GetStreamEndpointMethodStateResult result = await harness.InvokeGetStreamEndpoint(
                harness.EndpointNodeId,
                "default",
                VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInternalError));
        }

        [Test]
        public void GetStreamEndpointPropagatesOperationCanceled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.GetStreamAsync(
                    It.IsAny<VisionStreamRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new StreamHarness(mediaProvider.Object);

            Assert.That(
                async () => await harness.InvokeGetStreamEndpoint(
                    harness.EndpointNodeId,
                    "default",
                    VisionStreamProtocolEnum.Rtsp).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>(),
                "OperationCanceled must propagate — a caller who cancelled the call must " +
                "not receive a fabricated status code");
        }

        [Test]
        public async Task ReleaseStreamEndpointReturnsBadNotSupportedWhenNoMediaProviderIsRegistered()
        {
            var harness = new StreamHarness(mediaProvider: null);

            ReleaseStreamEndpointMethodStateResult result = await harness.InvokeReleaseStreamEndpoint(
                new ByteString(new byte[] { 1, 2, 3 })).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task ReleaseStreamEndpointForwardsGoodResultFromProvider()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ReleaseStreamAsync(
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            var harness = new StreamHarness(mediaProvider.Object);

            ReleaseStreamEndpointMethodStateResult result = await harness.InvokeReleaseStreamEndpoint(
                new ByteString(new byte[] { 1 })).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            mediaProvider.Verify(p => p.ReleaseStreamAsync(
                It.IsAny<ByteString>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ReleaseStreamEndpointReturnsBadInternalErrorWhenProviderThrowsUnexpected()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ReleaseStreamAsync(
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var harness = new StreamHarness(mediaProvider.Object);

            ReleaseStreamEndpointMethodStateResult result = await harness.InvokeReleaseStreamEndpoint(
                ByteString.Empty).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInternalError));
        }

        [Test]
        public void ReleaseStreamEndpointPropagatesOperationCanceled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ReleaseStreamAsync(
                    It.IsAny<ByteString>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new StreamHarness(mediaProvider.Object);

            Assert.That(
                async () => await harness.InvokeReleaseStreamEndpoint(
                    ByteString.Empty).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task ConfigureStreamEndpointReturnsBadNotSupportedWhenNoMediaProviderIsRegistered()
        {
            var harness = new StreamHarness(mediaProvider: null);

            ConfigureStreamEndpointMethodStateResult result = await harness.InvokeConfigureStreamEndpoint(
                harness.EndpointNodeId,
                VisionVideoCodecEnum.H264,
                1920, 1080, 30.0, 8_000_000).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task ConfigureStreamEndpointForwardsConfigurationRequestToProvider()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ConfigureStreamAsync(
                    It.IsAny<VisionStreamConfigurationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            var harness = new StreamHarness(mediaProvider.Object);

            ConfigureStreamEndpointMethodStateResult result = await harness.InvokeConfigureStreamEndpoint(
                harness.EndpointNodeId,
                VisionVideoCodecEnum.H264,
                1920, 1080, 30.0, 8_000_000).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            mediaProvider.Verify(p => p.ConfigureStreamAsync(
                It.Is<VisionStreamConfigurationRequest>(r =>
                    r.Endpoint == harness.EndpointNodeId &&
                    r.Codec == VisionVideoCodecEnum.H264 &&
                    r.Width == 1920 &&
                    r.Height == 1080 &&
                    r.FrameRate == 30.0 &&
                    r.Bitrate == 8_000_000),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ConfigureStreamEndpointReturnsBadInternalErrorWhenProviderThrowsUnexpected()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ConfigureStreamAsync(
                    It.IsAny<VisionStreamConfigurationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var harness = new StreamHarness(mediaProvider.Object);

            ConfigureStreamEndpointMethodStateResult result = await harness.InvokeConfigureStreamEndpoint(
                harness.EndpointNodeId,
                VisionVideoCodecEnum.H265,
                1280, 720, 25.0, 4_000_000).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInternalError));
        }

        [Test]
        public void ConfigureStreamEndpointPropagatesOperationCanceled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.ConfigureStreamAsync(
                    It.IsAny<VisionStreamConfigurationRequest>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new StreamHarness(mediaProvider.Object);

            Assert.That(
                async () => await harness.InvokeConfigureStreamEndpoint(
                    harness.EndpointNodeId,
                    VisionVideoCodecEnum.H264,
                    1920, 1080, 30.0, 8_000_000).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task SelectEndpointReturnsBadNotSupportedWhenNoMediaProviderIsRegistered()
        {
            var harness = new StreamHarness(mediaProvider: null);

            SelectEndpointMethodStateResult result = await harness.InvokeSelectEndpoint(
                harness.EndpointNodeId, harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public async Task SelectEndpointUpdatesPreferredEndpointsWhenGoodResult()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.SelectEndpointAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            var harness = new StreamHarness(mediaProvider.Object);

            SelectEndpointMethodStateResult result = await harness.InvokeSelectEndpoint(
                harness.EndpointNodeId, harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                Assert.That(harness.Media.PreferredStreamEndpoint!.Value,
                    Is.EqualTo(harness.EndpointNodeId),
                    "a good SelectEndpoint must have written the new preferred stream endpoint");
                Assert.That(harness.Media.PreferredClipEndpoint!.Value,
                    Is.EqualTo(harness.ClipEndpointNodeId),
                    "a good SelectEndpoint must have written the new preferred clip endpoint");
            });
        }

        [Test]
        public async Task SelectEndpointReturnsBadInternalErrorWhenProviderThrowsUnexpected()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.SelectEndpointAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            var harness = new StreamHarness(mediaProvider.Object);

            SelectEndpointMethodStateResult result = await harness.InvokeSelectEndpoint(
                harness.EndpointNodeId, harness.ClipEndpointNodeId).ConfigureAwait(false);

            Assert.That(result.ServiceResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInternalError));
        }

        [Test]
        public void SelectEndpointPropagatesOperationCanceled()
        {
            var mediaProvider = new Mock<IVisionMediaProvider>();
            mediaProvider
                .Setup(p => p.SelectEndpointAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
            var harness = new StreamHarness(mediaProvider.Object);

            Assert.That(
                async () => await harness.InvokeSelectEndpoint(
                    harness.EndpointNodeId, harness.ClipEndpointNodeId).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        private sealed class StreamHarness
        {
            public StreamHarness(IVisionMediaProvider? mediaProvider)
            {
                SensorNodeId = new NodeId(701u, 4);
                EndpointNodeId = new NodeId(801u, 4);
                ClipEndpointNodeId = new NodeId(802u, 4);
                var sensor = new VisionSensorState(null!);
                Media = new VisionMediaManagementState(null!)
                {
                    GetStreamEndpoint = new GetStreamEndpointMethodState(null!),
                    ReleaseStreamEndpoint = new ReleaseStreamEndpointMethodState(null!),
                    ConfigureStreamEndpoint = new ConfigureStreamEndpointMethodState(null!),
                    SelectEndpoint = new SelectEndpointMethodState(null!),
                    PreferredStreamEndpoint = PropertyState<NodeId>.With<VariantBuilder>(
                        null!, NodeId.Null),
                    PreferredClipEndpoint = PropertyState<NodeId>.With<VariantBuilder>(
                        null!, NodeId.Null)
                };
                sensor.Media = Media;
                var registration = new SensorRegistration(
                    "cam",
                    SensorNodeId,
                    sensor,
                    VisionSensorModalityEnum.Area2D,
                    VisionRealityKindEnum.Physical,
                    new HashSet<string>(StringComparer.Ordinal),
                    mediaProvider);
                m_registry = new VisionRegistry();
                m_registry.AddSensor(registration);
                var dispatcher = new VisionMethodDispatcher(m_registry, NullLogger.Instance);
                dispatcher.AttachMediaMethods(SensorNodeId, Media);
                m_getStream = Media.GetStreamEndpoint.OnCallAsync;
                m_releaseStream = Media.ReleaseStreamEndpoint.OnCallAsync;
                m_configureStream = Media.ConfigureStreamEndpoint.OnCallAsync;
                m_selectEndpoint = Media.SelectEndpoint.OnCallAsync;
                Assert.Multiple(() =>
                {
                    Assert.That(m_getStream, Is.Not.Null,
                        "AttachMediaMethods must wire a GetStreamEndpoint handler");
                    Assert.That(m_releaseStream, Is.Not.Null);
                    Assert.That(m_configureStream, Is.Not.Null);
                    Assert.That(m_selectEndpoint, Is.Not.Null);
                });
            }

            public NodeId SensorNodeId { get; }

            public NodeId EndpointNodeId { get; }

            public NodeId ClipEndpointNodeId { get; }

            public VisionMediaManagementState Media { get; }

            public async Task<GetStreamEndpointMethodStateResult> InvokeGetStreamEndpoint(
                NodeId endpoint, string profileName, VisionStreamProtocolEnum protocol)
            {
                return await m_getStream!(
                    null!,
                    Media.GetStreamEndpoint!,
                    SensorNodeId,
                    endpoint,
                    profileName,
                    protocol,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<ReleaseStreamEndpointMethodStateResult> InvokeReleaseStreamEndpoint(
                ByteString sessionToken)
            {
                return await m_releaseStream!(
                    null!,
                    Media.ReleaseStreamEndpoint!,
                    SensorNodeId,
                    sessionToken,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<ConfigureStreamEndpointMethodStateResult> InvokeConfigureStreamEndpoint(
                NodeId endpoint, VisionVideoCodecEnum codec,
                uint width, uint height, double frameRate, uint bitrate)
            {
                return await m_configureStream!(
                    null!,
                    Media.ConfigureStreamEndpoint!,
                    SensorNodeId,
                    endpoint,
                    codec,
                    width,
                    height,
                    frameRate,
                    bitrate,
                    CancellationToken.None).ConfigureAwait(false);
            }

            public async Task<SelectEndpointMethodStateResult> InvokeSelectEndpoint(
                NodeId streamEndpoint, NodeId clipEndpoint)
            {
                return await m_selectEndpoint!(
                    null!,
                    Media.SelectEndpoint!,
                    SensorNodeId,
                    streamEndpoint,
                    clipEndpoint,
                    CancellationToken.None).ConfigureAwait(false);
            }

            private readonly VisionRegistry m_registry;
            private readonly GetStreamEndpointMethodStateMethodAsyncCallHandler? m_getStream;
            private readonly ReleaseStreamEndpointMethodStateMethodAsyncCallHandler? m_releaseStream;
            private readonly ConfigureStreamEndpointMethodStateMethodAsyncCallHandler? m_configureStream;
            private readonly SelectEndpointMethodStateMethodAsyncCallHandler? m_selectEndpoint;
        }
    }
}
