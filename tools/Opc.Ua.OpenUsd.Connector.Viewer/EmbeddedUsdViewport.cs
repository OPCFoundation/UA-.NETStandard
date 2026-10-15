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

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using OpenUsd.Viewer;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// An embeddable, typed OpenUSD composition surface for an existing Avalonia application.
    /// Construction is inert. Only StartAsync opens the host-validated local asset closure.
    /// The caller drains telemetry before disposal and retains ownership of its OPC UA session.
    /// </summary>
    public sealed class EmbeddedUsdViewport : IRetainedSceneFrameSource, IAsyncDisposable
    {
        /// <summary>
        /// Creates an inert surface without a native stage, graphics device, timer or subscription.
        /// </summary>
        public EmbeddedUsdViewport(ITelemetryContext telemetry, UsdViewportLimits? limits = null)
        {
            ArgumentNullException.ThrowIfNull(telemetry);
            m_limits = limits ?? new UsdViewportLimits();
            m_limits.Validate();
            m_logger = telemetry.CreateLogger<EmbeddedUsdViewport>();
            m_control.StatusChanged += OnStatusChanged;
        }

        /// <summary>
        /// Gets the control to embed in the application's existing visual tree.
        /// </summary>
        public Control Surface => m_surface;

        /// <inheritdoc/>
        public string StageIdentifier { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public SceneCameraCaptureBackend Backend => Volatile.Read(ref m_backend);

        /// <summary>
        /// Gets the active camera path; empty means automatic stage framing.
        /// </summary>
        public string CameraPath => Volatile.Read(ref m_cameraPath);

        /// <summary>
        /// Gets the actual composition control status, including unavailable interop diagnostics.
        /// </summary>
        public string Status => m_control.Status;

        /// <summary>
        /// Gets the existing connector sink that authors into this exact retained stage.
        /// </summary>
        public IUsdSink Sink => m_sink ?? throw new InvalidOperationException("Start the viewport before authoring.");

        /// <summary>
        /// Gets the latest frame and current native ring-allocation evidence.
        /// </summary>
        public UsdViewportStatistics Statistics
        {
            get
            {
                UsdViewportStatistics snapshot = m_renderer?.Statistics ?? new(0, 0, 0, 0, 0, 0, 0, 0);
                return snapshot with
                {
                    ActiveGenerations = m_presenter?.ActiveGenerations ?? 0,
                    ActiveFrames = m_presenter?.ActiveFrames ?? 0
                };
            }
        }

        /// <summary>
        /// Reports the presenter's explicit status rather than substituting a blank successful surface.
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Reports a stage, renderer or device failure to the owning document.
        /// </summary>
        public event EventHandler<Exception>? Failed;

        /// <summary>
        /// Opens one already-validated private local stage closure after explicit user authorization.
        /// Does not run another Application, change desktop lifetime, or subscribe to a server.
        /// </summary>
        public async Task StartAsync(
            string stagePath,
            string stageIdentifier,
            string? cameraPath = null,
            CancellationToken cancellationToken = default)
        {
            Dispatcher.UIThread.VerifyAccess();
            ArgumentException.ThrowIfNullOrWhiteSpace(stagePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(stageIdentifier);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(m_disposed, this);
            if (m_scheduler is not null || !Path.IsPathFullyQualified(stagePath) || !File.Exists(stagePath))
            {
                throw new InvalidOperationException("Start once, using an existing host-validated local stage.");
            }
            StageIdentifier = stageIdentifier;
            UsdViewportRuntime.Initialize();
            m_scheduler = UsdStageScheduler.Open(stagePath, 16, 16);
            try
            {
                await m_scheduler.EditAsync(
                    static stage => stage.SetEditTargetToSessionLayer(),
                    UsdStageInvalidationKind.Full, cancellationToken).ConfigureAwait(true);
                m_source = await m_scheduler.AcquireRenderSourceAsync(cancellationToken).ConfigureAwait(true);
                await SetCameraAsync(cameraPath, cancellationToken).ConfigureAwait(true);
                m_sink = new UsdStageSink(m_scheduler, OnFailure, requireActiveTargets: true);
                cancellationToken.ThrowIfCancellationRequested();
                m_control.PresenterFactory = CreatePresenter;
                m_surface.Child = m_control;
            }
            catch
            {
                await DisposeAsync().ConfigureAwait(true);
                throw;
            }
        }

        /// <summary>
        /// Waits for actual geometry to render, not merely for a presenter to be constructed.
        /// </summary>
        public async Task<UsdViewportStatistics> WaitForFirstFrameAsync(CancellationToken cancellationToken)
        {
            if (m_scheduler is null)
            {
                throw new InvalidOperationException("Start the viewport first.");
            }
            _ = await m_firstFrame.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return Statistics;
        }

        /// <summary>
        /// Selects a camera on the retained stage without opening a second scene or renderer.
        /// The camera is resolved again before each frame so live transforms remain visible.
        /// </summary>
        public async ValueTask SetCameraAsync(string? path, CancellationToken cancellationToken)
        {
            string camera = path ?? string.Empty;
            if (camera.Length > 512 || (camera.Length != 0 && !UsdNames.IsValidPrimPath(camera)))
            {
                throw new ArgumentException("Use an absolute USD camera prim path.", nameof(path));
            }
            UsdStageScheduler scheduler = m_scheduler ??
                throw new InvalidOperationException("Start the retained stage before selecting a camera.");
            if (camera.Length != 0)
            {
                await scheduler.InvokeAsync(stage =>
                {
                    _ = CameraState.FromStageCamera(stage, camera, 0, 128, 128);
                }, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref m_cameraPath, camera);
        }

        /// <summary>
        /// Picks a prim inside the rendered physical-pixel frame. No command callback or command-prim watcher exists.
        /// </summary>
        public ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken)
        {
            return (m_renderer ?? throw new InvalidOperationException("The viewport has not rendered a frame."))
                .PickAsync(x, y, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<RetainedSceneFrame> CaptureFrameAsync(
            SceneCameraCaptureRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!string.Equals(request.StageIdentifier, StageIdentifier, StringComparison.Ordinal))
            {
                throw new ArgumentException("Capture can only use this retained stage.", nameof(request));
            }
            return (m_renderer ?? throw new InvalidOperationException("The viewport has not rendered a frame."))
                .CaptureAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_firstFrame.TrySetCanceled();
            try
            {
                await m_control.DisposeAsync().ConfigureAwait(true);
                m_surface.Child = null;
            }
            finally
            {
                m_control.StatusChanged -= OnStatusChanged;
                try
                {
                    if (m_presenter is not null)
                    {
                        await m_presenter.DisposeAsync().ConfigureAwait(true);
                    }
                    if (m_sink is not null)
                    {
                        await m_sink.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_source?.Dispose();
                    if (m_scheduler is not null)
                    {
                        await m_scheduler.DisposeAsync().ConfigureAwait(false);
                    }
                    Volatile.Write(ref m_backend, SceneCameraCaptureBackend.None);
                }
            }
        }

        private BoundedCompositionViewportPresenter CreatePresenter()
        {
            try
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                if (OperatingSystem.IsWindows())
                {
                    return CreateD3D12Presenter();
                }
                if (OperatingSystem.IsLinux())
                {
                    UsdViewportRenderer renderer = CreateRenderer();
                    try
                    {
                        var native = VulkanCompositionViewportPresenter.Create(context =>
                            renderer.Render(context.Renderer, context.ColorTarget, context.DepthTarget),
                            required: true);
                        return Wrap(native, renderer, new SceneCameraCaptureBackend
                        {
                            Name = "Vulkan",
                            DeviceName = "Backend-owned compositor device; adapter classification unavailable",
                            IsSoftwareKnown = false
                        }, () =>
                        {
                            VulkanCompositionPresenterDiagnostics statistics = native.GetDiagnostics();
                            return (statistics.ActiveGenerations, statistics.ActiveFrames);
                        });
                    }
                    catch
                    {
                        renderer.Dispose();
                        throw;
                    }
                }
                if (OperatingSystem.IsMacOS())
                {
                    return CreateMetalPresenter();
                }
                throw new PlatformNotSupportedException("Embedded OpenUSD supports Windows, Linux and macOS only.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                OnFailure(error);
                throw;
            }
        }

        [SupportedOSPlatform("windows")]
        private BoundedCompositionViewportPresenter CreateD3D12Presenter()
        {
            D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: false);
            UsdViewportRenderer? renderer = null;
            try
            {
                renderer = CreateRenderer(device);
                var native = new D3D12CompositionViewportPresenter(device, renderer);
                return Wrap(native, renderer,
                    new SceneCameraCaptureBackend
                    {
                        Name = "D3D12",
                        DeviceName = device.Capabilities.DeviceName,
                        IsSoftware = device.Capabilities.IsSoftware
                    }, () =>
                    {
                        D3D12CompositionPresenterStatistics statistics = native.GetStatistics();
                        return (statistics.ActiveGenerations, statistics.ActiveFrames);
                    });
            }
            catch
            {
                if (renderer is null)
                {
                    device.Dispose();
                }
                else
                {
                    renderer.Dispose();
                }
                throw;
            }
        }

        [SupportedOSPlatform("macos")]
        private BoundedCompositionViewportPresenter CreateMetalPresenter()
        {
            UsdViewportRenderer renderer = CreateRenderer();
            try
            {
                var native = new MetalCompositionViewportPresenter(context =>
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    SilkMeshRenderResult frame =
                        renderer.Render(context.Renderer, context.ColorTarget, context.DepthTarget);
                    return new MetalCompositionRenderResult(renderer.Statistics.SceneRevision, frame);
                }, required: true);
                return Wrap(native, renderer, new SceneCameraCaptureBackend
                {
                    Name = "Metal",
                    DeviceName = "Backend-owned compositor device; adapter classification unavailable",
                    IsSoftwareKnown = false
                }, () =>
                {
                    MetalCompositionPresenterDiagnostics statistics = native.GetDiagnostics();
                    return (statistics.ActiveGenerations, statistics.ActiveFrames);
                });
            }
            catch
            {
                renderer.Dispose();
                throw;
            }
        }

        private UsdViewportRenderer CreateRenderer(ISilkGraphicsDevice? device = null)
        {
            return new UsdViewportRenderer(
                m_scheduler ?? throw new InvalidOperationException("The stage is not open."),
                m_source ?? throw new InvalidOperationException("The retained stage source is unavailable."),
                Path.Combine(AppContext.BaseDirectory, "plugin", "usd"),
                m_limits, () => CameraPath, OnFrame, device);
        }

        private BoundedCompositionViewportPresenter Wrap(
            ICompositionViewportPresenter presenter, UsdViewportRenderer renderer, SceneCameraCaptureBackend backend,
            Func<(int Generations, int Frames)> statistics)
        {
            m_renderer = renderer;
            Volatile.Write(ref m_backend, backend);
            m_presenter = new BoundedCompositionViewportPresenter(
                presenter, m_limits, renderer.PrepareAsync, renderer.Dispose, OnFailure, statistics);
            return m_presenter;
        }

        private void OnFrame(UsdViewportStatistics statistics)
        {
            if (!Backend.IsAvailable)
            {
                Volatile.Write(ref m_backend, Backend with { IsAvailable = true, UnavailableReason = null });
            }
            m_firstFrame.TrySetResult(statistics);
        }

        private void OnStatusChanged(object? sender, string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnFailure(Exception error)
        {
            Volatile.Write(ref m_backend, Backend with
            {
                IsAvailable = false,
                UnavailableReason = "The embedded renderer failed; inspect its explicit diagnostic."
            });
            EmbeddedUsdViewportLog.RendererFailed(m_logger, error);
            m_firstFrame.TrySetException(error);
            Failed?.Invoke(this, error);
        }

        private readonly CompositionViewportControl m_control = new();
        private readonly Border m_surface = new();
        private readonly UsdViewportLimits m_limits;
        private readonly ILogger m_logger;
        private readonly TaskCompletionSource<UsdViewportStatistics> m_firstFrame =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private UsdStageScheduler? m_scheduler;
        private UsdStageRenderSource? m_source;
        private UsdStageSink? m_sink;
        private UsdViewportRenderer? m_renderer;
        private BoundedCompositionViewportPresenter? m_presenter;
        private SceneCameraCaptureBackend m_backend = SceneCameraCaptureBackend.None;
        private string m_cameraPath = string.Empty;
        private bool m_disposed;
    }

    /// <summary>
    /// Source-generated diagnostics for the embedded, document-owned viewport.
    /// </summary>
    internal static partial class EmbeddedUsdViewportLog
    {
        [LoggerMessage(EventId = OpenUsdViewerEventIds.ViewHost + 4, Level = LogLevel.Error,
            Message = "The embedded OpenUSD renderer failed.")]
        public static partial void RendererFailed(ILogger logger, Exception exception);
    }
}
