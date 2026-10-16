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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision.OpenUsd;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Synchronizes one retained stage into one mesh renderer. Presentation, readback and
    /// camera resolution share the presenter's serialized frame lifetime.
    /// </summary>
    internal sealed class UsdViewportRenderer : ISilkPresentationRenderer, IDisposable
    {
        /// <summary>
        /// Creates a session over the existing stage identity, never by reopening its filename.
        /// </summary>
        public UsdViewportRenderer(
            UsdStageScheduler scheduler,
            UsdStageRenderSource source,
            string pluginPath,
            UsdViewportLimits limits,
            Func<string> cameraPath,
            Action<UsdViewportStatistics> frameRendered,
            ISilkGraphicsDevice? ownedDevice = null,
            UsdViewportCaptureQueue? captures = null)
        {
            m_scheduler = scheduler;
            m_limits = limits;
            m_geometry = new UsdViewportGeometryBudget(limits);
            m_cameraPath = cameraPath;
            m_frameRendered = frameRendered;
            m_captures = captures ?? new UsdViewportCaptureQueue();
            m_ownedDevice = ownedDevice;
            m_session = OpenUsdSilkRuntime.Create(pluginPath, source);
            try
            {
                if (ownedDevice is not null)
                {
                    m_renderer = new SilkMeshRenderer(
                        ownedDevice,
                        new SilkTextureResidencyOptions(
                            32UL * 1024 * 1024,
                            64UL * 1024 * 1024));
                }
            }
            catch
            {
                m_session.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets detached evidence without retaining native frame allocations.
        /// </summary>
        public UsdViewportStatistics Statistics
        {
            get
            {
                lock (m_state)
                {
                    return m_statistics;
                }
            }
        }

        /// <summary>
        /// Resolves the camera on the stage owner thread immediately before native synchronization.
        /// </summary>
        public async ValueTask PrepareAsync(ViewportDimensions size, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            _ = m_limits.RequireFrame(size);
            string path = m_cameraPath();
            CameraState camera = CameraState.Default;
            ulong serial = 0;
            await m_scheduler.InvokeAsync(
                stage =>
                {
                    serial = stage.ChangeSerial;
                    if (!string.IsNullOrEmpty(path))
                    {
                        camera = CameraState.FromStageCamera(stage, path, 0, size.Width, size.Height);
                    }
                },
                cancellationToken).ConfigureAwait(false);
            lock (m_state)
            {
                if (m_size != size || m_camera != camera || m_preparedCameraPath != path)
                {
                    m_stateRevision++;
                    m_needsSync = true;
                }
                m_needsSync |= m_stageSerial != serial;
                m_stageSerial = serial;
                m_size = size;
                m_camera = camera;
                m_preparedCameraPath = path;
            }
        }

        /// <inheritdoc/>
        public SilkPresentationRenderResult Render(
            ISilkGraphicsTexture color,
            ISilkGraphicsTexture depth,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SilkMeshRenderResult rendered = Render(
                m_renderer ??
                throw new InvalidOperationException("The D3D12 renderer was not constructed."),
                color,
                depth);
            return new SilkPresentationRenderResult(Statistics.SceneRevision, rendered.DrawCount, true);
        }

        /// <summary>
        /// Renders into a native presenter's retained renderer and frame targets.
        /// </summary>
        public SilkMeshRenderResult Render(
            SilkMeshRenderer renderer,
            ISilkGraphicsTexture color,
            ISilkGraphicsTexture depth)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            int width = checked((int)color.Width);
            int height = checked((int)color.Height);
            _ = m_limits.RequireFrame(new ViewportDimensions(width, height));
            if (color.Format != SilkTextureFormat.Rgba8Unorm ||
                color.MipLevelCount != 1 ||
                depth.Format != SilkTextureFormat.D32Float ||
                depth.MipLevelCount != 1 ||
                color.Width != depth.Width ||
                color.Height != depth.Height)
            {
                throw new NotSupportedException("Composition requires matching single-level RGBA8 and D32 targets.");
            }
            SilkMeshRenderResult rendered;
            if (m_needsSync)
            {
                using OpenUsdSilkPage page = m_session.Sync(width, height, 0, m_camera, RenderComplexity.Low);
                ValidatePage(page);
                rendered = renderer.ApplyAndRender(
                    page,
                    color,
                    depth,
                    new SilkPickFrameBinding(m_stateRevision, page.Revision),
                    s_options);
                m_pageRevision = page.Revision;
                m_needsSync = false;
            }
            else
            {
                rendered = renderer.Render(
                    color,
                    depth,
                    new SilkPickFrameBinding(m_stateRevision, m_pageRevision),
                    s_options);
            }
            if (rendered.DrawCount < 1 || rendered.Statistics.MeshCount < 1)
            {
                throw new InvalidOperationException("The embedded renderer produced no stage geometry.");
            }
            foreach (RenderDiagnostic diagnostic in renderer.GpuResources.Diagnostics.Entries)
            {
                if (diagnostic.Severity == RenderDiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException($"OpenUSD {diagnostic.Code}: {diagnostic.Message}");
                }
            }
            if (m_statistics.Width != width ||
                m_statistics.Height != height ||
                m_validatedCameraPath != m_preparedCameraPath)
            {
                byte[] pixels = new byte[m_limits.RequireFrame(m_size)];
                color.ReadbackForTesting(pixels);
                bool visible = false;
                for (int offset = 4; offset < pixels.Length; offset += 4)
                {
                    if (!pixels.AsSpan(0, 4).SequenceEqual(pixels.AsSpan(offset, 4)))
                    {
                        visible = true;
                        break;
                    }
                }
                if (!visible)
                {
                    throw new InvalidOperationException(
                        "The actual composition target is uniform; " +
                        "the viewport is not reported as a successful frame.");
                }
                m_validatedCameraPath = m_preparedCameraPath;
            }
            lock (m_state)
            {
                m_renderer = renderer;
                m_statistics = new UsdViewportStatistics(
                    width,
                    height,
                    m_statistics.RenderedFrames + 1,
                    m_pageRevision,
                    rendered.DrawCount,
                    rendered.Statistics.MeshCount,
                    0,
                    0);
            }
            m_frameRendered(Statistics);
            UsdViewportCaptureQueue.Readback? capture = m_captures.TryStart();
            if (capture is not null)
            {
                CaptureFrame(capture, renderer, color, depth, rendered, m_pageRevision);
            }
            return rendered;
        }

        /// <summary>
        /// Queues a single bounded readback to be fulfilled inside a serialized render callback.
        /// </summary>
        public ValueTask<RetainedSceneFrame> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed != 0, this);
                RequireCapture(request);
                return m_captures.CaptureAsync(request, cancellationToken);
            }
        }

        /// <summary>
        /// Queues a renderer pick bound to the current camera and physical dimensions.
        /// Results only identify a prim; this assembly never issues an OPC UA command.
        /// </summary>
        public async ValueTask<RenderPickResult> PickAsync(int x, int y, CancellationToken cancellationToken)
        {
            ValueTask<RenderPickResult> work;
            ulong state;
            lock (m_state)
            {
                ObjectDisposedException.ThrowIf(m_disposed != 0, this);
                if (m_renderer is null ||
                    m_statistics.RenderedFrames == 0 ||
                    x < 0 ||
                    y < 0 ||
                    x >= m_size.Width ||
                    y >= m_size.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(x), "Pick inside an already rendered frame.");
                }
                if (m_picking)
                {
                    throw new InvalidOperationException("One viewport pick is already pending.");
                }
                m_picking = true;
                state = m_stateRevision;
                work = m_renderer.PickAsync(
                    new RenderPickRequest(
                        x,
                        y,
                        m_size,
                        state,
                        null,
                        RenderPickTarget.Primitive,
                        RenderPickOptions.None),
                    cancellationToken);
            }
            try
            {
                RenderPickResult result = await work.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_state)
                {
                    if (m_disposed != 0 || m_stateRevision != state)
                    {
                        return RenderPickResult.Stale(result.Request, m_stateRevision, m_statistics.SceneRevision);
                    }
                }
                return result;
            }
            finally
            {
                lock (m_state)
                {
                    m_picking = false;
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_state)
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                m_captures.Dispose();
            }
            try
            {
                if (m_ownedDevice is not null)
                {
                    m_renderer?.Dispose();
                }
                m_session.Dispose();
            }
            finally
            {
                m_ownedDevice?.Dispose();
            }
        }

        /// <summary>
        /// Validates capture dimensions and camera identity against selected and prepared viewport state.
        /// </summary>
        internal static void RequireCapture(
            SceneCameraCaptureRequest request,
            UsdViewportLimits limits,
            ViewportDimensions size,
            string selectedCameraPath,
            string preparedCameraPath)
        {
            _ = limits.RequireFrame(new ViewportDimensions(request.Width, request.Height));
            if (request.Width != size.Width ||
                request.Height != size.Height ||
                request.TimeCode != 0 ||
                (request.PrimPath ?? string.Empty) != selectedCameraPath ||
                (request.PrimPath ?? string.Empty) != preparedCameraPath)
            {
                throw new InvalidOperationException(
                    "Retained capture requires the active viewport camera, time and physical dimensions.");
            }
        }

        private void RequireCapture(SceneCameraCaptureRequest request)
        {
            RequireCapture(
                request,
                m_limits,
                m_size,
                m_cameraPath(),
                m_preparedCameraPath);
        }

        private void CaptureFrame(
            UsdViewportCaptureQueue.Readback capture,
            SilkMeshRenderer renderer,
            ISilkGraphicsTexture color,
            ISilkGraphicsTexture depth,
            SilkMeshRenderResult rendered,
            ulong revision)
        {
            try
            {
                capture.Token.ThrowIfCancellationRequested();
                RequireCapture(capture.Request);
                ByteString rgba;
                if (m_ownedDevice is not null)
                {
                    SilkFrameCaptureResult frame = SilkFrameCapture.CaptureRetained(
                        renderer,
                        m_ownedDevice,
                        capture.Request.Width,
                        capture.Request.Height,
                        RenderSettings.Default,
                        revision);
                    rgba = ByteString.From(frame.Rgba.ToArray());
                    rendered = frame.RenderResult;
                    _ = renderer.Render(
                        color,
                        depth,
                        new SilkPickFrameBinding(m_stateRevision, revision),
                        s_options);
                }
                else
                {
                    // These public presenters own their device. Their public target readback uses
                    // the same retained frame and avoids creating a second device or session.
                    byte[] pixels = new byte[m_limits.RequireFrame(m_size)];
                    color.ReadbackForTesting(pixels);
                    rgba = ByteString.From(pixels);
                }
                capture.Token.ThrowIfCancellationRequested();
                capture.Complete(
                    new RetainedSceneFrame(
                        capture.Request.Width,
                        capture.Request.Height,
                        rgba,
                        rendered.DrawCount,
                        rendered.Statistics.MeshCount,
                        revision));
            }
            catch (Exception error)
            {
                capture.Complete(error);
            }
        }

        private void ValidatePage(OpenUsdSilkPage page)
        {
            if (page.CommandCount > 16384)
            {
                throw new InvalidOperationException("The native scene page exceeds its command budget.");
            }
            foreach (SilkCommand command in page)
            {
                if (command.Type == SilkCommandType.MeshRemove)
                {
                    SilkMeshRemoveCommand removed = command.AsMeshRemove();
                    m_geometry.Remove(removed.StableHash, removed.InstanceIndex);
                }
                else if (command.Type == SilkCommandType.MeshUpsert)
                {
                    SilkMeshUpsertCommand mesh = command.AsMeshUpsert();
                    m_geometry.Upsert(
                        mesh.StableHash,
                        mesh.InstanceIndex,
                        mesh.PointCount,
                        mesh.IndexCount,
                        mesh.AttributeCount,
                        mesh.HasDeformation);
                }
            }
        }

        private static readonly SilkMeshRenderOptions s_options =
            new(new SilkColor(0.02f, 0.02f, 0.03f, 1), 1, false, true);

        private readonly UsdStageScheduler m_scheduler;
        private readonly UsdViewportLimits m_limits;
        private readonly Func<string> m_cameraPath;
        private readonly Action<UsdViewportStatistics> m_frameRendered;
        private readonly UsdViewportCaptureQueue m_captures;
        private readonly ISilkGraphicsDevice? m_ownedDevice;
        private readonly OpenUsdSilkSession m_session;
        private readonly Lock m_state = new();
        private readonly UsdViewportGeometryBudget m_geometry;
        private UsdViewportStatistics m_statistics = new(0, 0, 0, 0, 0, 0, 0, 0);
        private SilkMeshRenderer? m_renderer;
        private ViewportDimensions m_size;
        private CameraState m_camera = CameraState.Default;
        private string m_preparedCameraPath = string.Empty;
        private string? m_validatedCameraPath;
        private ulong m_stateRevision = 1;
        private ulong m_stageSerial;
        private ulong m_pageRevision;
        private bool m_needsSync = true;
        private int m_disposed;
        private bool m_picking;
    }
}
