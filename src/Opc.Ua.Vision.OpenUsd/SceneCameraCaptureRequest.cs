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
    /// One capture request handed to an
    /// <see cref="ISceneCameraCaptureProvider"/>. Mirrors the fields that
    /// a Vision <c>IVisionSimulatedType</c> exposes plus the frame size
    /// and encoding the caller wants back.
    /// </summary>
    public sealed record class SceneCameraCaptureRequest
    {
        /// <summary>
        /// Path or URI to the USD stage that hosts the camera prim. Passed
        /// verbatim to <c>UsdStage.Open</c>. Required.
        /// </summary>
        public string StageIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Absolute prim path to a <c>UsdGeomCamera</c> on the stage (for
        /// example <c>"/World/Cam"</c>). When empty or <c>null</c> the
        /// provider renders with the stage's automatic default framing
        /// (<c>CameraState.Default</c>).
        /// </summary>
        public string? PrimPath { get; init; }

        /// <summary>
        /// Requested output width in pixels. Must be positive.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Requested output height in pixels. Must be positive.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Stage time code the frame is captured at. Defaults to zero,
        /// which matches most single-frame USD stages.
        /// </summary>
        public double TimeCode { get; init; }

        /// <summary>
        /// Encoded image format requested from the provider.
        /// </summary>
        public SceneCameraImageFormat Format { get; init; } = SceneCameraImageFormat.Png;

        /// <summary>
        /// Optional caller-supplied timestamp attached to the resulting
        /// frame; when <c>null</c> the provider stamps the frame with
        /// <see cref="DateTime.UtcNow"/> when the capture completes.
        /// </summary>
        public DateTime? TimestampUtc { get; init; }
    }
}
