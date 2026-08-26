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

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Outcome of a <see cref="ISceneCameraCaptureProvider.CaptureAsync"/>
    /// call. Only <see cref="Succeeded"/> yields a valid encoded image on
    /// the result; every other value is a definite failure and the caller
    /// must consult <see cref="SceneCameraCaptureResult.Reason"/> for a
    /// human-readable diagnostic.
    /// </summary>
    public enum SceneCameraCaptureStatus
    {
        /// <summary>
        /// The frame rendered, the encoder produced bytes, and the guard
        /// detected drawn geometry - the image on the result is usable.
        /// </summary>
        Succeeded = 0,

        /// <summary>
        /// No graphics backend is available on this host (for example, CI
        /// on Linux without a Vulkan loader). The provider surfaces this
        /// distinctly so the Vision server can degrade to a non-rendering
        /// sensor rather than blaming the request.
        /// </summary>
        NoRenderingBackend = 1,

        /// <summary>
        /// The request is missing a required field (stage identifier,
        /// positive dimensions, and so on) or requests an unsupported
        /// image format.
        /// </summary>
        InvalidRequest = 2,

        /// <summary>
        /// The USD stage failed to open (file missing, wrong plugin path,
        /// or a syntactically malformed stage).
        /// </summary>
        StageOpenFailed = 3,

        /// <summary>
        /// The stage opened, but the requested camera prim path either
        /// does not exist or is not a <c>UsdGeomCamera</c>.
        /// </summary>
        CameraResolveFailed = 4,

        /// <summary>
        /// <c>SilkFrameCapture.Capture</c> threw. Typically means the
        /// graphics device was lost or refused the frame.
        /// </summary>
        RenderFailed = 5,

        /// <summary>
        /// The render pipeline reported no drawn geometry, or the returned
        /// pixels are uniform. Serving this frame would be misleading, so
        /// the provider surfaces it as a distinct failure. The most common
        /// cause is the known session-reuse landmine in the Silk backend,
        /// which the provider defends against by using a fresh session per
        /// request.
        /// </summary>
        BlankFrame = 6,

        /// <summary>
        /// Encoding the raw RGBA8 pixels to the requested output format
        /// failed. Rare; a defensive escape hatch for encoder bugs.
        /// </summary>
        EncodingFailed = 7
    }
}
