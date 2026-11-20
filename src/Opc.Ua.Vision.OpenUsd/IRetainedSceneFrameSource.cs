// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Borrows an explicitly started renderer without opening another stage or graphics device.
    /// The owner must serialize capture with presentation and enforce bounds before readback allocation.
    /// </summary>
    public interface IRetainedSceneFrameSource
    {
        /// <summary>
        /// Gets the identity of the retained stage, not an instruction to resolve a file or URI.
        /// </summary>
        string StageIdentifier { get; }

        /// <summary>
        /// Gets current backend availability without probing or allocating a graphics device.
        /// </summary>
        SceneCameraCaptureBackend Backend { get; }

        /// <summary>
        /// Captures the requested camera from the retained scene. Cancellation drains the owned readback.
        /// </summary>
        ValueTask<RetainedSceneFrame> CaptureFrameAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// A bounded detached RGBA8 frame and the evidence produced by the retained renderer.
    /// Rows are top-down, tightly packed, and never backed by an unowned native pointer.
    /// </summary>
    public sealed record RetainedSceneFrame(
        int Width,
        int Height,
        ByteString Rgba,
        int DrawCount,
        int MeshCount,
        ulong SceneRevision);
}
