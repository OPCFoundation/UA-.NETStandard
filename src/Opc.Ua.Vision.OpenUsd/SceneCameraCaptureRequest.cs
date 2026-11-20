/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
