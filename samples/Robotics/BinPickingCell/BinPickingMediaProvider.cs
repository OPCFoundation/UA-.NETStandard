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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Vision;
using Opc.Ua.Vision.OpenUsd;
using Opc.Ua.Vision.Server;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Bridges the Vision media surface (<see cref="IVisionMediaProvider"/>)
    /// to the OpenUSD offscreen renderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider serves the eye-in-hand camera as clip frames rendered
    /// from the sample cell stage; it does not serve a live RTSP stream and
    /// therefore reports <see cref="StatusCodes.BadNotSupported"/> from
    /// <see cref="GetStreamAsync"/>. That is the correct sample behaviour:
    /// the sample models the mandatory RTSP stream endpoint in the address
    /// space per spec §6.2, but a real RTSP server is not part of a
    /// self-contained sample.
    /// </para>
    /// <para>
    /// When the OpenUSD capture provider has no graphics backend
    /// (<see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>), this
    /// provider surfaces it as <see cref="StatusCodes.BadResourceUnavailable"/>
    /// so the Vision server can report the condition to callers rather than
    /// fail to start. The provider itself starts and reports the condition
    /// through <see cref="Backend"/>.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed partial class BinPickingMediaProvider : IVisionMediaProvider
    {
        public BinPickingMediaProvider(
            ISceneCameraCaptureProvider capture,
            BinPickingSensorSpec spec,
            ILogger<BinPickingMediaProvider> logger)
        {
            m_capture = capture ?? throw new ArgumentNullException(nameof(capture));
            m_spec = spec ?? throw new ArgumentNullException(nameof(spec));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the capture backend the provider is bound to.
        /// </summary>
        public SceneCameraCaptureBackend Backend => m_capture.Backend;

        /// <inheritdoc/>
        public ValueTask<VisionStreamLease> GetStreamAsync(
            VisionStreamRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new VisionStreamSessionDataType
            {
                SessionToken = ByteString.Empty,
                Uri = string.Empty,
                Protocol = request.PreferredProtocol,
                ExpiresAt = DateTimeUtc.MinValue
            };
            return ValueTask.FromResult(new VisionStreamLease(
                new ServiceResult(StatusCodes.BadNotSupported,
                    LocalizedText.From(
                        "The bin-picking sample does not host a live RTSP stream. " +
                        "Use GetClip to fetch one-shot rendered frames.")),
                session,
                request.Endpoint));
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ReleaseStreamAsync(
            ByteString sessionToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> ConfigureStreamAsync(
            VisionStreamConfigurationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ServiceResult(StatusCodes.BadNotSupported,
                LocalizedText.From(
                    "The bin-picking sample renders single frames per GetClip; the stream endpoint " +
                    "is a modelling placeholder and cannot be reconfigured.")));
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> SelectEndpointAsync(
            NodeId streamEndpoint, NodeId clipEndpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public async ValueTask<VisionClipResult> GetClipAsync(
            VisionClipRequest request, CancellationToken cancellationToken)
        {
            DateTime timestamp = request.Timestamp.IsNull
                ? DateTime.UtcNow
                : request.Timestamp.ToDateTime();
            var captureRequest = new SceneCameraCaptureRequest
            {
                StageIdentifier = m_spec.StageIdentifier,
                PrimPath = m_spec.CameraPrimPath,
                Width = m_spec.CaptureWidth,
                Height = m_spec.CaptureHeight,
                TimeCode = 0.0,
                Format = SceneCameraImageFormat.Png,
                TimestampUtc = timestamp
            };
            SceneCameraCaptureResult result = await m_capture
                .CaptureAsync(captureRequest, cancellationToken)
                .ConfigureAwait(false);
            ServiceResult serviceResult = MapStatus(result);
            if (result.Status != SceneCameraCaptureStatus.Succeeded)
            {
                m_logger.CaptureFailed(result.Status, result.Reason ?? string.Empty);
                var failureImage = new VisionImageReferenceDataType
                {
                    Uri = string.Empty,
                    Digest = ByteString.Empty,
                    DigestAlgorithm = string.Empty,
                    Format = VisionClipFormatEnum.Jpeg,
                    PixelFormat = m_spec.PixelFormat,
                    Width = (uint)m_spec.CaptureWidth,
                    Height = (uint)m_spec.CaptureHeight,
                    SizeBytes = 0u,
                    Timestamp = DateTimeUtc.From(timestamp)
                };
                return new VisionClipResult(
                    serviceResult, failureImage, request.Endpoint, ByteString.Empty);
            }
            ByteString png = result.Image;
            byte[] digest = ComputeDigest(png);
            var image = new VisionImageReferenceDataType
            {
                // A reference, not a container. Embedding the encoded frame here as a
                // base64 data URI sent the image twice - once in this String and once in
                // the inline ByteString below - and a 1.3 MB PNG becomes a 1.7 MB string
                // against a 64 KB MaxStringLength, so the Server could not encode its own
                // camera output and every read failed with BadEncodingLimitsExceeded.
                Uri = FormattableString.Invariant(
                    $"opcua-inline://binpicking-cell/frames/{timestamp:yyyyMMddHHmmssfff}"),
                Digest = ByteString.From(digest),
                DigestAlgorithm = "SHA-256",
                Format = VisionClipFormatEnum.Png,
                PixelFormat = m_spec.PixelFormat,
                Width = (uint)result.Width,
                Height = (uint)result.Height,
                SizeBytes = (uint)png.Length,
                Timestamp = DateTimeUtc.From(timestamp)
            };
            ByteString inline = request.RequestInline ? png : ByteString.Empty;
            m_logger.CaptureSucceeded(result.Width, result.Height, png.Length);
            return new VisionClipResult(ServiceResult.Good, image, request.Endpoint, inline);
        }

        private static ServiceResult MapStatus(SceneCameraCaptureResult result)
        {
            return result.Status switch
            {
                SceneCameraCaptureStatus.Succeeded => ServiceResult.Good,
                SceneCameraCaptureStatus.NoRenderingBackend => new ServiceResult(
                    StatusCodes.BadResourceUnavailable,
                    LocalizedText.From(result.Reason ?? "No graphics backend is available on this host.")),
                SceneCameraCaptureStatus.InvalidRequest => new ServiceResult(
                    StatusCodes.BadInvalidArgument,
                    LocalizedText.From(result.Reason ?? "The capture request was rejected as invalid.")),
                SceneCameraCaptureStatus.StageOpenFailed => new ServiceResult(
                    StatusCodes.BadResourceUnavailable,
                    LocalizedText.From(result.Reason ?? "The USD stage could not be opened.")),
                SceneCameraCaptureStatus.CameraResolveFailed => new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From(result.Reason ?? "The camera prim could not be resolved on the stage.")),
                SceneCameraCaptureStatus.RenderFailed => new ServiceResult(
                    StatusCodes.BadInternalError,
                    LocalizedText.From(result.Reason ?? "The scene renderer failed.")),
                SceneCameraCaptureStatus.BlankFrame => new ServiceResult(
                    StatusCodes.BadNoDataAvailable,
                    LocalizedText.From(result.Reason ?? "The renderer produced a blank frame.")),
                SceneCameraCaptureStatus.EncodingFailed => new ServiceResult(
                    StatusCodes.BadEncodingError,
                    LocalizedText.From(result.Reason ?? "The rendered frame could not be encoded.")),
                _ => new ServiceResult(StatusCodes.BadInternalError,
                    LocalizedText.From(result.Reason ?? "The scene camera capture provider reported an unknown status.")),
            };
        }

        private static byte[] ComputeDigest(ByteString png)
        {
            return System.Security.Cryptography.SHA256.HashData(png.Span);
        }

        private readonly ISceneCameraCaptureProvider m_capture;
        private readonly BinPickingSensorSpec m_spec;
        private readonly ILogger<BinPickingMediaProvider> m_logger;
    }

    /// <summary>
    /// Static description of the eye-in-hand sensor used by the sample.
    /// </summary>
    /// <param name="StageIdentifier">
    /// Path or URI to the USD stage the sensor renders from.
    /// </param>
    /// <param name="CameraPrimPath">
    /// Absolute prim path of the <c>UsdGeomCamera</c> that acts as the
    /// camera view.
    /// </param>
    /// <param name="PixelFormat">
    /// GenICam PFNC pixel-format string reported on frames the provider
    /// returns.
    /// </param>
    /// <param name="CaptureWidth">
    /// Rendered frame width in pixels.
    /// </param>
    /// <param name="CaptureHeight">
    /// Rendered frame height in pixels.
    /// </param>
    internal sealed record BinPickingSensorSpec(
        string StageIdentifier,
        string CameraPrimPath,
        string PixelFormat,
        int CaptureWidth,
        int CaptureHeight);

    internal static partial class BinPickingMediaProviderLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.MediaProvider + 1,
            Level = LogLevel.Information,
            Message = "Rendered eye-in-hand frame {Width}x{Height} ({Bytes} bytes).")]
        public static partial void CaptureSucceeded(
            this ILogger<BinPickingMediaProvider> logger, int width, int height, int bytes);

        [LoggerMessage(EventId = BinPickingCellEventIds.MediaProvider + 2,
            Level = LogLevel.Warning,
            Message = "Eye-in-hand capture failed: {Status} - {Reason}.")]
        public static partial void CaptureFailed(
            this ILogger<BinPickingMediaProvider> logger,
            SceneCameraCaptureStatus status,
            string reason);
    }
}
