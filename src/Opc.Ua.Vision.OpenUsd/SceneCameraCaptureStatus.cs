// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// Rendering or retained readback failed. Typically means the
        /// graphics device was lost or refused the requested frame.
        /// </summary>
        RenderFailed = 5,

        /// <summary>
        /// The render pipeline reported no drawn geometry, or the returned
        /// pixels are uniform. Serving this frame would be misleading, so
        /// the provider surfaces it as a distinct failure. The most common
        /// cause is a scene or camera with no visible geometry. Capture
        /// callers retain their renderer rather than discarding incremental
        /// scene data between requests.
        /// </summary>
        BlankFrame = 6,

        /// <summary>
        /// Encoding the raw RGBA8 pixels to the requested output format
        /// failed. Rare; a defensive escape hatch for encoder bugs.
        /// </summary>
        EncodingFailed = 7
    }
}
