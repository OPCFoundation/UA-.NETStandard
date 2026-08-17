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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Supplies the pixel and clip source that the Vision media manager
    /// serves. The Server never sees pixels — the provider supplies a
    /// leased URI for a live stream and optional inline bytes for a clip.
    /// </summary>
    /// <remarks>
    /// One provider instance is bound to one sensor. Providers must be
    /// thread-safe: OPC UA method calls arrive concurrently.
    /// </remarks>
    public interface IVisionMediaProvider
    {
        /// <summary>
        /// Leases a live-stream session against the requested endpoint and
        /// profile.
        /// </summary>
        /// <returns>
        /// A session descriptor with a URI, expiry time and a per-session
        /// token the caller uses in
        /// <see cref="ReleaseStreamAsync"/>.
        /// </returns>
        ValueTask<VisionStreamLease> GetStreamAsync(
            VisionStreamRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Releases a session previously granted by
        /// <see cref="GetStreamAsync"/>. Returns <see cref="ServiceResult.Good"/>
        /// even when the session is unknown so idempotent callers are safe.
        /// </summary>
        ValueTask<ServiceResult> ReleaseStreamAsync(
            ByteString sessionToken,
            CancellationToken cancellationToken);

        /// <summary>
        /// Applies the requested media configuration to a stream endpoint.
        /// Servers that only support single-shot configuration must return
        /// <see cref="StatusCodes.BadNotSupported"/>.
        /// </summary>
        ValueTask<ServiceResult> ConfigureStreamAsync(
            VisionStreamConfigurationRequest request,
            CancellationToken cancellationToken);

        /// <summary>
        /// Selects the preferred stream and clip endpoints on the media
        /// manager. This is a pure address-space update; providers may
        /// simply return <see cref="ServiceResult.Good"/>.
        /// </summary>
        ValueTask<ServiceResult> SelectEndpointAsync(
            NodeId streamEndpoint,
            NodeId clipEndpoint,
            CancellationToken cancellationToken);

        /// <summary>
        /// Fetches a clip and, if requested and permitted, encodes it as
        /// inline bytes.
        /// </summary>
        /// <remarks>
        /// The Server enforces §6.4 rules over the returned response:
        /// inline delivery is refused when the clip endpoint has
        /// <c>InlineDeliveryEnabled</c> set to <c>false</c>, or when the
        /// encoded byte count exceeds <c>MaxInlineClipSize</c>.
        /// </remarks>
        ValueTask<VisionClipResult> GetClipAsync(
            VisionClipRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Input to <see cref="IVisionMediaProvider.GetStreamAsync"/>.
    /// </summary>
    public readonly record struct VisionStreamRequest(
        NodeId Endpoint,
        string ProfileName,
        VisionStreamProtocolEnum PreferredProtocol);

    /// <summary>
    /// Result of <see cref="IVisionMediaProvider.GetStreamAsync"/>.
    /// </summary>
    public sealed record VisionStreamLease(
        ServiceResult ServiceResult,
        VisionStreamSessionDataType Session,
        NodeId EndpointOut);

    /// <summary>
    /// Input to <see cref="IVisionMediaProvider.ConfigureStreamAsync"/>.
    /// </summary>
    public readonly record struct VisionStreamConfigurationRequest(
        NodeId Endpoint,
        VisionVideoCodecEnum Codec,
        uint Width,
        uint Height,
        double FrameRate,
        uint Bitrate);

    /// <summary>
    /// Input to <see cref="IVisionMediaProvider.GetClipAsync"/>.
    /// </summary>
    public readonly record struct VisionClipRequest(
        NodeId Endpoint,
        string ResultId,
        DateTimeUtc Timestamp,
        VisionClipFormatEnum Format,
        bool RequestInline);

    /// <summary>
    /// Result of <see cref="IVisionMediaProvider.GetClipAsync"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="InlineImage"/> should be the default (null) <see cref="ByteString"/>
    /// when the caller did not request inline bytes or when the provider
    /// cannot supply them; the Server surfaces
    /// <see cref="StatusCodes.BadEncodingLimitsExceeded"/> if the payload
    /// exceeds the clip endpoint's declared limit.
    /// </remarks>
    public sealed record VisionClipResult(
        ServiceResult ServiceResult,
        VisionImageReferenceDataType Image,
        NodeId EndpointOut,
        ByteString InlineImage);
}
