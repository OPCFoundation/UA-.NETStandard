// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Connector.Viewer;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// Injectable native-surface boundary; no static renderer loading, second Application or session ownership.
    /// </summary>
    internal interface IOpenUsdViewportSurface : IRetainedSceneFrameSource, IAsyncDisposable
    {
        /// <summary>
        /// Gets the embeddable visual surface.
        /// </summary>
        Control View { get; }
        /// <summary>
        /// Gets the sink authoring into the retained stage.
        /// </summary>
        IUsdSink Sink { get; }
        /// <summary>
        /// Gets the currently selected camera.
        /// </summary>
        string CameraPath { get; }
        /// <summary>
        /// Gets actual presentation status.
        /// </summary>
        string Status { get; }
        /// <summary>
        /// Gets renderer and native allocation evidence.
        /// </summary>
        UsdViewportStatistics Statistics { get; }
        /// <summary>
        /// Reports renderer failures to the document owner.
        /// </summary>
        event EventHandler<Exception>? Failed;

        /// <summary>
        /// Starts the host-validated stage after explicit activation.
        /// </summary>
        Task StartAsync(
            string stagePath, string stageIdentifier, string? cameraPath, CancellationToken cancellationToken);
        /// <summary>
        /// Awaits a real rendered frame.
        /// </summary>
        Task<UsdViewportStatistics> WaitForFirstFrameAsync(CancellationToken cancellationToken);
        /// <summary>
        /// Selects a camera from the retained stage.
        /// </summary>
        ValueTask SetCameraAsync(string? path, CancellationToken cancellationToken);
        /// <summary>
        /// Selects a prim at physical frame coordinates.
        /// </summary>
        ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reflection-free binding of the reusable embedded viewport to UaLens' document surface.
    /// </summary>
    internal sealed class OpenUsdViewportSurface : IOpenUsdViewportSurface
    {
        /// <summary>
        /// Creates an inert typed adapter without an application or native device.
        /// </summary>
        public OpenUsdViewportSurface(ITelemetryContext telemetry)
        {
            m_viewport = new EmbeddedUsdViewport(telemetry);
        }

        /// <inheritdoc/>
        public Control View => m_viewport.Surface;
        /// <inheritdoc/>
        public IUsdSink Sink => m_viewport.Sink;
        /// <inheritdoc/>
        public string CameraPath => m_viewport.CameraPath;
        /// <inheritdoc/>
        public string Status => m_viewport.Status;
        /// <inheritdoc/>
        public string StageIdentifier => m_viewport.StageIdentifier;
        /// <inheritdoc/>
        public SceneCameraCaptureBackend Backend => m_viewport.Backend;
        /// <inheritdoc/>
        public UsdViewportStatistics Statistics => m_viewport.Statistics;

        /// <inheritdoc/>
        public event EventHandler<Exception>? Failed
        {
            add => m_viewport.Failed += value;
            remove => m_viewport.Failed -= value;
        }

        /// <inheritdoc/>
        public Task StartAsync(
            string stagePath, string stageIdentifier, string? cameraPath, CancellationToken cancellationToken)
        {
            return m_viewport.StartAsync(stagePath, stageIdentifier, cameraPath, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<UsdViewportStatistics> WaitForFirstFrameAsync(CancellationToken cancellationToken)
        {
            return m_viewport.WaitForFirstFrameAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SetCameraAsync(string? path, CancellationToken cancellationToken)
        {
            return m_viewport.SetCameraAsync(path, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken)
        {
            return m_viewport.PickAsync(x, y, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<RetainedSceneFrame> CaptureFrameAsync(
            SceneCameraCaptureRequest request, CancellationToken cancellationToken)
        {
            return m_viewport.CaptureFrameAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => m_viewport.DisposeAsync();

        private readonly EmbeddedUsdViewport m_viewport;
    }
}
