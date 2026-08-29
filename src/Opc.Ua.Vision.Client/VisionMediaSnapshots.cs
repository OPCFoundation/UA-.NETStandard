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

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// The result of a <c>GetClip</c> call. §6.4 defines the by-reference default and
    /// the optional inline delivery: <see cref="Image"/> always carries a URI, and
    /// <see cref="InlineImage"/> is populated only when the caller asked for inline
    /// delivery and the encoded still fits the Server's effective inline limit.
    /// </summary>
    public sealed record VisionClipResult
    {
        /// <summary>
        /// The by-reference image descriptor. Its <c>Uri</c> is always populated when
        /// the Server returned <c>Good</c>; the <c>Timestamp</c> and <c>Digest</c>
        /// together are the correlation key of §6.4 rule 4.
        /// </summary>
        public required VisionImageReferenceDataType Image { get; init; }

        /// <summary>
        /// The endpoint the Server actually used, after applying the §6.3 selection
        /// rule (a null <c>Endpoint</c> argument first falls back to
        /// <c>PreferredClipEndpoint</c>, then to the first endpoint in BrowseName
        /// order). Non-null when the Server named one.
        /// </summary>
        public NodeId EndpointNodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The encoded image bytes when inline delivery was requested and the still
        /// fit the Server's effective inline limit; an empty <see cref="ByteString"/>
        /// otherwise.
        /// </summary>
        public ByteString InlineImage { get; init; } = ByteString.Empty;

        /// <summary>
        /// Gets a value indicating whether <see cref="InlineImage"/> carries bytes.
        /// </summary>
        public bool HasInlineImage
            => !InlineImage.IsNull && InlineImage.Length > 0;
    }

    /// <summary>
    /// The result of a <see cref="VisionMediaClient.ReadLatestClipAsync"/> call
    /// against an inline-delivery clip endpoint. §6.4 rule 5 fixes the initial and
    /// disabled states, and §6.4 rule 3 fixes the overflow state, so a caller can
    /// distinguish them without having to inspect a raw <see cref="StatusCode"/>.
    /// </summary>
    /// <param name="Bytes">
    /// The encoded image bytes on success, or an empty <see cref="ByteString"/>
    /// otherwise.
    /// </param>
    /// <param name="Metadata">
    /// The concurrent <c>LatestClipMetadata</c> descriptor, or <c>null</c> when the
    /// Server did not report one.
    /// </param>
    /// <param name="StatusCode">
    /// The <see cref="StatusCode"/> the Server reported on <c>LatestClip</c>.
    /// </param>
    /// <param name="State">
    /// The <see cref="VisionInlineClipState"/> classification of
    /// <paramref name="StatusCode"/>. §6.4 makes the classification well-defined; a
    /// client should branch on this rather than reinterpreting the raw code.
    /// </param>
    public sealed record VisionInlineClipReading(
        ByteString Bytes,
        VisionImageReferenceDataType? Metadata,
        StatusCode StatusCode,
        VisionInlineClipState State);

    /// <summary>
    /// The state of a <c>LatestClip</c> read against an inline-delivery clip endpoint,
    /// derived from §6.4.
    /// </summary>
    public enum VisionInlineClipState
    {
        /// <summary>
        /// The clip endpoint returned a fresh image within the inline size limit.
        /// </summary>
        Available = 0,

        /// <summary>
        /// The Server has not published a clip yet — §6.4 rule 5 requires
        /// <c>Bad_NoDataAvailable</c> before the first acquisition. A client should
        /// wait rather than treating this as a hard error.
        /// </summary>
        NotYetAvailable,

        /// <summary>
        /// The Server has <c>InlineDeliveryEnabled = false</c> — §6.4 rule 5 requires
        /// <c>Bad_NotSupported</c> in that case. A client should fall back to the
        /// out-of-band URI in the metadata.
        /// </summary>
        InlineDisabled,

        /// <summary>
        /// The last acquisition exceeded the effective inline size limit — §6.4 rule
        /// 3 requires <c>Bad_EncodingLimitsExceeded</c> without truncation. A client
        /// should fall back to the out-of-band URI in the metadata.
        /// </summary>
        Overflow,

        /// <summary>
        /// The clip endpoint reported a different error. The raw <see cref="StatusCode"/>
        /// is available in <see cref="VisionInlineClipReading.StatusCode"/>.
        /// </summary>
        Faulted
    }
}
