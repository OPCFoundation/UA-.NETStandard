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
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools that surface the latest Vision-sensor frame to the language
    /// model as image content it can actually see.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionSeeingTools
    {
        /// <summary>
        /// Returns the latest still frame from a sensor as MCP image content.
        /// </summary>
        [McpServerTool(Name = "vision_get_frame")]
        [Description("Returns the latest still frame from a Vision sensor as an MCP ImageContentBlock the " +
            "model can inspect directly. The image is delivered at the sensor's own resolution and encoding " +
            "and is not resampled by this tool; if the encoded bytes exceed the model's context, request a " +
            "smaller PNG/JPEG format on the sensor. Use vision_get_frame_metadata when you only need the " +
            "frame descriptor (URI, dimensions, timestamp, digest) without the pixels. When the server has " +
            "no rendering backend, has inline delivery disabled, or has not yet published a frame, returns " +
            "a TextContentBlock explaining the reason rather than an empty image. Never retries silently " +
            "and never asks the server to switch mode as a side-effect.")]
        public static async Task<CallToolResult> GetFrameAsync(
            VisionClientAccessor accessor,
            [Description("Sensor NodeId, for example ns=2;s=Vision/Sensors/Camera1.")] string sensorNodeId,
            [Description("Preferred encoded image format when the sensor supports selection: Jpeg, Png, " +
                "Tiff, Bmp, WebP, GenDc or Other. Defaults to Jpeg.")]
            VisionClipFormatEnum format = VisionClipFormatEnum.Jpeg,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            (ByteString bytes, VisionClipFormatEnum actualFormat, string? reason) = await AcquireFrameAsync(
                accessor, sensorNodeId, format, ct).ConfigureAwait(false);

            if (reason is not null)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = reason }]
                };
            }

            return new CallToolResult
            {
                Content =
                [
                    // FromBytes base64-encodes for the wire. Assigning the encoded frame
                    // straight to Data put raw bytes where the protocol requires base64:
                    // every byte that is not valid UTF-8 was serialised as U+FFFD, so the
                    // image reached the model corrupted beyond recovery - and six times
                    // larger, because each byte became a \uXXXX escape.
                    ImageContentBlock.FromBytes(bytes.Memory, MimeTypeFor(actualFormat))
                ]
            };
        }

        /// <summary>
        /// Returns the metadata descriptor for the latest still frame from a
        /// sensor, without the pixel bytes.
        /// </summary>
        [McpServerTool(Name = "vision_get_frame_metadata")]
        [Description("Returns the latest still-frame descriptor from a Vision sensor without transferring " +
            "pixels: URI, MIME type, width, height, size, timestamp and digest. Use this to check that a " +
            "frame is available and inspect its dimensions before calling vision_get_frame, or to log the " +
            "URI a downstream tool should fetch out of band. This tool never returns the encoded image; " +
            "use vision_get_frame for that. Returns a VisionFrameMetadata record; when the sensor has no " +
            "clip metadata, StatusMessage explains why and Available is false.")]
        public static async Task<VisionFrameMetadata> GetFrameMetadataAsync(
            VisionClientAccessor accessor,
            [Description("Sensor NodeId, for example ns=2;s=Vision/Sensors/Camera1.")] string sensorNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionSensorClient sensor = accessor.OpenSensor(sensorNodeId, sessionName);
            VisionMediaClient? media = await sensor.OpenMediaAsync(ct).ConfigureAwait(false);
            if (media is null)
            {
                return VisionFrameMetadata.Unavailable(
                    "Sensor exposes no Media object; there are no clip endpoints to read a frame from.");
            }

            NodeId clipEndpoint = await FirstClipEndpointAsync(media, ct).ConfigureAwait(false);
            if (clipEndpoint.IsNull)
            {
                return VisionFrameMetadata.Unavailable(
                    "Sensor's Media object exposes no ClipEndpoint; no still frame descriptors are published.");
            }

            try
            {
                VisionImageReferenceDataType? metadata = await media
                    .ReadLatestClipMetadataAsync(clipEndpoint, ct).ConfigureAwait(false);
                if (metadata is null)
                {
                    return VisionFrameMetadata.Unavailable(
                        "Clip endpoint has no LatestClipMetadata; the server has not published a frame yet.");
                }
                return new VisionFrameMetadata
                {
                    Available = true,
                    Uri = metadata.Uri,
                    MimeType = MimeTypeFor(metadata.Format),
                    Format = metadata.Format,
                    Width = metadata.Width,
                    Height = metadata.Height,
                    SizeBytes = metadata.SizeBytes,
                    PixelFormat = metadata.PixelFormat,
                    Timestamp = metadata.Timestamp,
                    Digest = metadata.Digest,
                    DigestAlgorithm = metadata.DigestAlgorithm,
                    StatusMessage = null
                };
            }
            catch (ServiceResultException ex)
            {
                return VisionFrameMetadata.Unavailable(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Server refused reading the latest clip metadata: {ex.Result}"));
            }
        }

        private static async Task<(ByteString Bytes, VisionClipFormatEnum Format, string? Reason)>
            AcquireFrameAsync(
                VisionClientAccessor accessor,
                string sensorNodeId,
                VisionClipFormatEnum format,
                CancellationToken ct)
        {
            VisionSensorClient sensor = accessor.OpenSensor(sensorNodeId);
            VisionMediaClient? media = await sensor.OpenMediaAsync(ct).ConfigureAwait(false);
            if (media is null)
            {
                return (
                    ByteString.Empty,
                    format,
                    "Sensor exposes no Media object; there are no clip endpoints to acquire a frame from.");
            }

            NodeId clipEndpoint = await FirstClipEndpointAsync(media, ct).ConfigureAwait(false);
            if (clipEndpoint.IsNull)
            {
                return (
                    ByteString.Empty,
                    format,
                    "Sensor's Media object exposes no ClipEndpoint; no still frame can be acquired.");
            }

            try
            {
                VisionClipResult clip = await media.GetClipAsync(
                    clipEndpoint,
                    resultId: null,
                    timestamp: default,
                    format: format,
                    requestInline: true,
                    cancellationToken: ct).ConfigureAwait(false);
                if (clip.HasInlineImage)
                {
                    return (clip.InlineImage, clip.Image.Format, null);
                }
                return (
                    ByteString.Empty,
                    clip.Image.Format,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Server returned only an out-of-band image reference at '{clip.Image.Uri}'. Fetch it out of band or ask the server to enable inline delivery."));
            }
            catch (ServiceResultException ex)
            {
                VisionInlineClipReading fallback;
                try
                {
                    fallback = await media.ReadLatestClipAsync(clipEndpoint, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException inner)
                {
                    return (
                        ByteString.Empty,
                        format,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Server refused GetClip ({ex.Result}) and refused LatestClip ({inner.Result}). Likely no rendering backend is attached to the server."));
                }
                return ClassifyFallback(fallback, format, ex.Result);
            }
        }

        private static (ByteString Bytes, VisionClipFormatEnum Format, string? Reason) ClassifyFallback(
            VisionInlineClipReading fallback,
            VisionClipFormatEnum format,
            ServiceResult getClipError)
        {
            switch (fallback.State)
            {
                case VisionInlineClipState.Available:
                    VisionClipFormatEnum actualFormat = fallback.Metadata?.Format ?? format;
                    return (fallback.Bytes, actualFormat, null);
                case VisionInlineClipState.NotYetAvailable:
                    return (
                        ByteString.Empty,
                        format,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Server has not published a frame yet (LatestClip = Bad_NoDataAvailable). GetClip returned {getClipError}. Wait or trigger acquisition."));
                case VisionInlineClipState.InlineDisabled:
                    return (
                        ByteString.Empty,
                        format,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Inline image delivery is disabled on this clip endpoint (LatestClip = Bad_NotSupported). Fall back to the out-of-band URI or enable inline delivery. GetClip returned {getClipError}."));
                case VisionInlineClipState.Overflow:
                    return (
                        ByteString.Empty,
                        format,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Last frame exceeded the inline delivery limit (LatestClip = Bad_EncodingLimitsExceeded). Request a smaller PNG/JPEG format or fetch out of band. GetClip returned {getClipError}."));
                default:
                    return (
                        ByteString.Empty,
                        format,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Server cannot render a frame right now. GetClip returned {getClipError}; LatestClip returned {fallback.StatusCode}. This is the pattern reported when a scene camera has no rendering backend attached."));
            }
        }

        private static async Task<NodeId> FirstClipEndpointAsync(
            VisionMediaClient media,
            CancellationToken ct)
        {
            await foreach (VisionNodeEntry entry in media.EnumerateClipEndpointsAsync(ct)
                .ConfigureAwait(false))
            {
                if (!entry.NodeId.IsNull)
                {
                    return entry.NodeId;
                }
            }
            return NodeId.Null;
        }

        private static string MimeTypeFor(VisionClipFormatEnum format)
        {
            return format switch
            {
                VisionClipFormatEnum.Jpeg => "image/jpeg",
                VisionClipFormatEnum.Png => "image/png",
                VisionClipFormatEnum.Tiff => "image/tiff",
                VisionClipFormatEnum.Bmp => "image/bmp",
                VisionClipFormatEnum.WebP => "image/webp",
                _ => "application/octet-stream"
            };
        }
    }

    /// <summary>
    /// Descriptor of the latest still frame available on a Vision sensor. Returned
    /// by the vision_get_frame_metadata tool without transferring the encoded
    /// pixels.
    /// </summary>
    public sealed record VisionFrameMetadata
    {
        /// <summary>
        /// True when the server published a frame descriptor for the sensor.
        /// </summary>
        public required bool Available { get; init; }

        /// <summary>
        /// The out-of-band URI a downstream tool can fetch to obtain the encoded
        /// image bytes, or null when no metadata is available.
        /// </summary>
        public string? Uri { get; init; }

        /// <summary>
        /// The MIME type derived from the encoded image format, for example
        /// image/jpeg.
        /// </summary>
        public string? MimeType { get; init; }

        /// <summary>
        /// The encoded image format the server used.
        /// </summary>
        public VisionClipFormatEnum Format { get; init; }

        /// <summary>
        /// Image width in pixels, or zero when unknown.
        /// </summary>
        public uint Width { get; init; }

        /// <summary>
        /// Image height in pixels, or zero when unknown.
        /// </summary>
        public uint Height { get; init; }

        /// <summary>
        /// Encoded image size in bytes, or zero when unknown.
        /// </summary>
        public uint SizeBytes { get; init; }

        /// <summary>
        /// Pixel format string as declared by the server, or null when not set.
        /// </summary>
        public string? PixelFormat { get; init; }

        /// <summary>
        /// Timestamp associated with the frame.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// Content digest of the encoded image, or empty when not set.
        /// </summary>
        public ByteString Digest { get; init; } = ByteString.Empty;

        /// <summary>
        /// The digest algorithm the server used, defaulting to SHA-256.
        /// </summary>
        public string? DigestAlgorithm { get; init; }

        /// <summary>
        /// Human-readable status message when Available is false; otherwise null.
        /// </summary>
        public string? StatusMessage { get; init; }

        internal static VisionFrameMetadata Unavailable(string message)
        {
            return new VisionFrameMetadata
            {
                Available = false,
                StatusMessage = message
            };
        }
    }
}
