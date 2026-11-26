// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Renders a stage camera view and returns an encoded image plus the
    /// metadata a Vision <c>ClipEndpointType</c> needs (dimensions, format,
    /// timestamp). The Vision server takes a dependency on this interface
    /// so it does not have to know that any specific rendering technology
    /// is involved; a host with no rendering support at all can bind a
    /// no-op implementation that always returns
    /// <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
    /// </summary>
    /// <remarks>
    /// Implementations must be safe to call concurrently from many callers;
    /// they may serialize access to a graphics device internally.
    /// </remarks>
    public interface ISceneCameraCaptureProvider
    {
        /// <summary>
        /// Describes the graphics backend the provider will use to fulfill
        /// requests. Cheap to read: probed once and cached at construction.
        /// When <see cref="SceneCameraCaptureBackend.IsAvailable"/> is
        /// <c>false</c> every call to
        /// <see cref="CaptureAsync"/> returns
        /// <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
        /// </summary>
        SceneCameraCaptureBackend Backend { get; }

        /// <summary>
        /// Captures the requested camera view and returns an encoded image,
        /// or a <see cref="SceneCameraCaptureResult"/> whose
        /// <see cref="SceneCameraCaptureResult.Status"/> describes why no
        /// image was produced. The implementation never throws for
        /// input-driven failures (missing stage, missing prim, no backend,
        /// blank frame) - those become status codes instead - and never
        /// returns a blank frame as if it succeeded. Cancellation surfaces
        /// as <see cref="System.OperationCanceledException"/>.
        /// </summary>
        ValueTask<SceneCameraCaptureResult> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken);
    }
}
