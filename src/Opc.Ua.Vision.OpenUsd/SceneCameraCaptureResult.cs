/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Outcome of one <see cref="ISceneCameraCaptureProvider.CaptureAsync"/>
    /// call. Discriminated by <see cref="Status"/>; the encoded image is
    /// only meaningful when <see cref="Status"/> is
    /// <see cref="SceneCameraCaptureStatus.Succeeded"/>. Every non-success
    /// value populates <see cref="Reason"/> with a human-readable diagnostic
    /// so the Vision server can propagate it into its own status codes
    /// without inventing text.
    /// </summary>
    public sealed record class SceneCameraCaptureResult
    {
        /// <summary>
        /// The outcome discriminator. Callers should switch on this to
        /// decide whether <see cref="Image"/> is usable.
        /// </summary>
        public SceneCameraCaptureStatus Status { get; init; }

        /// <summary>
        /// Human-readable diagnostic; <c>null</c> when
        /// <see cref="Status"/> is <see cref="SceneCameraCaptureStatus.Succeeded"/>.
        /// Never a secret and safe to log or forward to a client. The
        /// underlying exception, when there is one, is redacted; look at
        /// the provider's log for the full stack trace.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Encoded image bytes. <see cref="ByteString.IsNull"/> when the
        /// capture did not succeed.
        /// </summary>
        public ByteString Image { get; init; }

        /// <summary>
        /// Encoded image format. Meaningless when
        /// <see cref="Status"/> is not
        /// <see cref="SceneCameraCaptureStatus.Succeeded"/>.
        /// </summary>
        public SceneCameraImageFormat Format { get; init; }

        /// <summary>
        /// Actual pixel width of the rendered frame - typically the value
        /// from the request, but the provider may clamp very small requests
        /// upward to a minimum the graphics backend supports.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Actual pixel height of the rendered frame.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Wall-clock UTC timestamp attached to the frame. When the request
        /// supplies <see cref="SceneCameraCaptureRequest.TimestampUtc"/> the
        /// provider echoes it back; otherwise this is when the capture
        /// completed.
        /// </summary>
        public DateTime TimestampUtc { get; init; }

        /// <summary>
        /// Wall-clock time it took to render, guard, and encode this frame.
        /// A useful health signal for the caller regardless of outcome.
        /// </summary>
        public TimeSpan Elapsed { get; init; }

        /// <summary>
        /// The graphics backend that produced this frame (or would have,
        /// if it had succeeded), for observability.
        /// </summary>
        public SceneCameraCaptureBackend Backend { get; init; } = SceneCameraCaptureBackend.None;
    }
}
