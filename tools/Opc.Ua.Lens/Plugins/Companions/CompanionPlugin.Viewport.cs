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
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd.Rendering;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.Companions
{
    internal sealed partial class CompanionPlugin
    {
        /// <summary>
        /// Gets whether the explicit embedded-preview controls apply to this provider.
        /// </summary>
        public bool ShowViewport => SelectedProvider?.Id == "openusd";

        /// <summary>
        /// Gets simulated camera metadata fetched only by the Discover cameras action.
        /// </summary>
        public ObservableCollection<RenderedVisionCamera> SimulatedCameras { get; } = [];

        [ObservableProperty]
        public partial Control? ViewportSurface { get; set; }

        [ObservableProperty]
        public partial Bitmap? RenderedVisionFrame { get; set; }

        [ObservableProperty]
        public partial string ViewportStatus { get; set; } =
            "Stopped. Start explicitly to fetch verified assets, render and observe telemetry.";

        [ObservableProperty]
        public partial string ViewportCameraPath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SelectedUsdPrim { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartViewportCommand), nameof(StopViewportCommand))]
        public partial bool HasViewport { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(
            nameof(ApplyViewportCameraCommand), nameof(CaptureViewportFrameCommand),
            nameof(DiscoverSimulatedCamerasCommand), nameof(RenderSimulatedVisionCommand))]
        public partial bool IsViewportRunning { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RenderSimulatedVisionCommand))]
        public partial RenderedVisionCamera? SelectedSimulatedCamera { get; set; }

        private bool CanStartViewport()
        {
            return CanInspect() && !HasViewport && ShowViewport && m_valueBinding is not null;
        }

        private bool CanStopViewport() => HasViewport;

        private bool CanUseViewport() => !m_closing && !IsBusy && !IsOffline && IsViewportRunning;

        private bool CanRenderSimulatedVision() => CanUseViewport() && SelectedSimulatedCamera is not null;

        [RelayCommand(CanExecute = nameof(CanStartViewport))]
        private Task StartViewportAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Start OpenUSD viewport", async () =>
            {
                await StopViewportAsync().ConfigureAwait(true);
                CompanionTarget target = SelectedTarget ??
                    throw new InvalidOperationException("Inspect a stage first.");
                CompanionContext context = CurrentViewportContext();
                int version = m_selectionVersion;
                OpenUsdViewportSession owned = m_viewportFactory.Create(m_host.Telemetry);
                m_viewport = owned;
                owned.SurfaceChanged += OnViewportSurfaceChanged;
                owned.Failed += OnViewportFailed;
                HasViewport = true;
                ViewportStatus = "Verifying assets and warming the native composition renderer...";
                try
                {
                    await owned.StartAsync(context, target, ViewportCameraPath, cancellationToken).ConfigureAwait(true);
                    RequireViewportOwner(owned, context, version);
                    IsViewportRunning = true;
                    ViewportStatus = $"Rendering {owned.StageIdentifier}; " +
                        $"{owned.Statistics.Width} × {owned.Statistics.Height} physical pixels. " +
                        "Live telemetry is read-only. Click selects a prim; it never commands a robot.";
                    Status = "The retained OpenUSD viewport is running until Stop, selection change, rebind or close.";
                }
                catch
                {
                    await StopViewportAsync().ConfigureAwait(true);
                    throw;
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanStopViewport))]
        private Task StopViewportAsync()
        {
            OpenUsdViewportSession? owned = m_viewport;
            m_viewport = null;
            if (owned is not null)
            {
                owned.Cancel();
                owned.SurfaceChanged -= OnViewportSurfaceChanged;
                owned.Failed -= OnViewportFailed;
            }
            if (ViewportSurface is { } surface)
            {
                surface.PointerPressed -= OnViewportPointerPressed;
            }
            ViewportSurface = null;
            HasViewport = false;
            IsViewportRunning = false;
            SelectedUsdPrim = string.Empty;
            SimulatedCameras.Clear();
            SelectedSimulatedCamera = null;
            ClearRenderedFrame();
            if (owned is null)
            {
                return m_viewportStopWork;
            }
            m_viewportStopWork = StopViewportCoreAsync(owned, m_viewportStopWork);
            return m_viewportStopWork;
        }

        private async Task StopViewportCoreAsync(OpenUsdViewportSession owned, Task previous)
        {
            try
            {
                await previous.ConfigureAwait(true);
            }
            finally
            {
                await owned.DisposeAsync().ConfigureAwait(true);
                ViewportStatus = $"Stopped; {owned.Statistics.RenderedFrames} rendered frames, " +
                    $"{owned.AppliedValues} applied telemetry values. " +
                    $"Active native frames: {owned.Statistics.ActiveFrames}; " +
                    $"generations: {owned.Statistics.ActiveGenerations}.";
            }
        }

        private void InvalidateViewport()
        {
            _ = StopViewportAsync();
        }

        [RelayCommand(CanExecute = nameof(CanUseViewport))]
        private Task ApplyViewportCameraAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Select viewport camera", async () =>
            {
                OpenUsdViewportSession owned = RequireViewport();
                ClearRenderedFrame();
                await owned.SetCameraAsync(ViewportCameraPath, cancellationToken).ConfigureAwait(true);
                ViewportStatus = string.IsNullOrEmpty(owned.CameraPath)
                    ? "Automatic framing selected on the retained stage."
                    : $"Camera {owned.CameraPath} selected on the retained stage.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanUseViewport))]
        private Task CaptureViewportFrameAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Capture retained scene", async () =>
            {
                OpenUsdViewportSession owned = RequireViewport();
                ClearRenderedFrame();
                CompanionContext context = CurrentViewportContext();
                int version = m_selectionVersion;
                SceneCameraCaptureResult result = await owned.CaptureAsync(cancellationToken).ConfigureAwait(true);
                RequireViewportOwner(owned, context, version);
                ShowRenderedFrame(result, "Local rendered scene");
            });
        }

        [RelayCommand(CanExecute = nameof(CanUseViewport))]
        private Task DiscoverSimulatedCamerasAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Discover simulated cameras", async () =>
            {
                OpenUsdViewportSession owned = RequireViewport();
                CompanionContext context = CurrentViewportContext();
                int version = m_selectionVersion;
                ArrayOf<RenderedVisionCamera> cameras =
                    await m_visionCameraSource.DiscoverAsync(context, cancellationToken).ConfigureAwait(true);
                RequireViewportOwner(owned, context, version);
                SimulatedCameras.Clear();
                foreach (RenderedVisionCamera camera in cameras)
                {
                    SimulatedCameras.Add(camera);
                }
                SelectedSimulatedCamera = cameras.Count == 0 ? null : cameras[0];
                Status = $"Found {cameras.Count} simulated camera(s). Rendering remains a separate explicit action.";
            });
        }

        [RelayCommand(CanExecute = nameof(CanRenderSimulatedVision))]
        private Task RenderSimulatedVisionAsync(CancellationToken cancellationToken)
        {
            return PresentOperationAsync("Render simulated Vision", async () =>
            {
                RenderedVisionCamera camera = SelectedSimulatedCamera ??
                    throw new InvalidOperationException("Discover and select a simulated camera first.");
                ClearRenderedFrame();
                OpenUsdViewportSession owned = RequireViewport();
                CompanionContext context = CurrentViewportContext();
                int version = m_selectionVersion;
                await m_visionCameraSource.RequireCurrentAsync(context, camera, cancellationToken).ConfigureAwait(true);
                RequireViewportOwner(owned, context, version);
                await owned.SetCameraAsync(camera.PrimPath, cancellationToken).ConfigureAwait(true);
                SceneCameraCaptureResult result = await owned.CaptureAsync(cancellationToken).ConfigureAwait(true);
                await m_visionCameraSource.RequireCurrentAsync(context, camera, cancellationToken).ConfigureAwait(true);
                RequireViewportOwner(owned, context, version);
                ViewportCameraPath = camera.PrimPath;
                ShowRenderedFrame(result,
                    $"Local simulated Vision for {camera.SensorId}, frame {camera.FrameId}; " +
                    "sensor camera mapped into the selected verified scene, not a server acquisition");
            });
        }

        private void ShowRenderedFrame(SceneCameraCaptureResult result, string origin)
        {
            if (result.Status != SceneCameraCaptureStatus.Succeeded)
            {
                throw new InvalidOperationException($"Rendered Vision: {result.Status}: {result.Reason}");
            }
            using var stream = new MemoryStream(result.Image.ToArray(), writable: false);
            var image = new Bitmap(stream);
            Bitmap? previous = RenderedVisionFrame;
            RenderedVisionFrame = image;
            previous?.Dispose();
            ViewportStatus = $"{origin}. {result.Width} × {result.Height}; {result.Backend.Name}; " +
                $"PNG SHA-256 {Convert.ToHexString(SHA256.HashData(result.Image.Span))}.";
        }

        private void ClearRenderedFrame()
        {
            Bitmap? previous = RenderedVisionFrame;
            RenderedVisionFrame = null;
            previous?.Dispose();
        }

        private void OnViewportSurfaceChanged(object? sender, EventArgs args)
        {
            if (ReferenceEquals(sender, m_viewport))
            {
                if (ViewportSurface is { } previous)
                {
                    previous.PointerPressed -= OnViewportPointerPressed;
                }
                ViewportSurface = m_viewport?.Surface;
                if (ViewportSurface is { } current)
                {
                    current.PointerPressed += OnViewportPointerPressed;
                }
            }
        }

        private void OnViewportFailed(object? sender, Exception error)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(sender, m_viewport))
                {
                    m_viewportFaultWork = PresentOperationAsync("Viewport stopped after failure", async () =>
                    {
                        await StopViewportAsync().ConfigureAwait(true);
                        throw new InvalidOperationException(error.Message, error);
                    });
                }
            });
        }

        private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs args)
        {
            if (ViewportSurface is not { } surface || !CanUseViewport() || !m_viewportPickWork.IsCompleted ||
                !args.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
            {
                return;
            }
            Point point = args.GetPosition(surface);
            if (surface.Bounds.Width <= 0 || surface.Bounds.Height <= 0)
            {
                return;
            }
            OpenUsdViewportSession owned = RequireViewport();
            int x = (int)Math.Floor(point.X * owned.Statistics.Width / surface.Bounds.Width);
            int y = (int)Math.Floor(point.Y * owned.Statistics.Height / surface.Bounds.Height);
            args.Handled = true;
            m_viewportPickWork = PickViewportAsync(x, y);
        }

        /// <summary>
        /// Selects a prim at physical viewport coordinates without dispatching server actions.
        /// </summary>
        internal Task PickViewportAsync(int x, int y)
        {
            return PresentOperationAsync("Select USD prim", async () =>
            {
                OpenUsdViewportSession owned = RequireViewport();
                int version = m_selectionVersion;
                SelectedUsdPrim = string.Empty;
                RenderPickResult result = await owned.PickAsync(x, y, CancellationToken.None).ConfigureAwait(true);
                if (ReferenceEquals(owned, m_viewport) && version == m_selectionVersion)
                {
                    SelectedUsdPrim = result.Status == RenderPickStatus.Hit
                        ? result.PrimPath ?? string.Empty : string.Empty;
                    ViewportStatus = $"Pick: {result.Status}" +
                        (result.Status == RenderPickStatus.Stale
                            ? $" ({result.StaleReasons}; " +
                                $"state {result.RequestedStateRevision} → {result.StateRevision}; " +
                                $"scene {result.RequestedSceneRevision} → {result.SceneRevision})" : string.Empty) +
                        (SelectedUsdPrim.Length == 0 ? "." : $" — {SelectedUsdPrim}.") +
                        " Selection only; no Call, Write or command-prim mutation was issued.";
                }
            });
        }

        private OpenUsdViewportSession RequireViewport()
        {
            return m_viewport is { IsStarted: true } viewport
                ? viewport : throw new InvalidOperationException("Start the viewport first.");
        }

        private CompanionContext CurrentViewportContext()
        {
            if (m_host.Session is not { } session || m_valueBinding is null ||
                !m_valueBinding.MatchesSession(session) || !ReferenceEquals(session, m_valueSession))
            {
                throw new InvalidOperationException("Inspect the current primary-session representation first.");
            }
            return new CompanionContext(session, m_host.Telemetry);
        }

        private void RequireViewportOwner(OpenUsdViewportSession owned, CompanionContext context, int version)
        {
            if (m_closing || version != m_selectionVersion || !ReferenceEquals(m_viewport, owned) ||
                !ReferenceEquals(context.Session, m_host.Session))
            {
                throw new OperationCanceledException("The viewport selection or primary session changed.");
            }
            _ = CurrentViewportContext();
        }

        private readonly OpenUsdViewportFactory m_viewportFactory;
        private readonly IRenderedVisionCameraSource m_visionCameraSource;
        private OpenUsdViewportSession? m_viewport;
        private Task m_viewportStopWork = Task.CompletedTask;
        private Task m_viewportFaultWork = Task.CompletedTask;
        private Task m_viewportPickWork = Task.CompletedTask;
    }
}
