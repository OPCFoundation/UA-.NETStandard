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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Vision.OpenUsd.Encoding;
using Opc.Ua.Vision.OpenUsd.Rendering;
using OpenUsd;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// <see cref="ISceneCameraCaptureProvider"/> backed by the OpenUSD
    /// Silk rendering stack. Probes a graphics device on construction and
    /// then serves capture requests by opening the requested stage, resolving
    /// the camera prim to a <c>CameraState</c>, rendering through a retained
    /// <c>SilkFrameCapturer</c>, and encoding the RGBA8 buffer as PNG.
    /// </summary>
    /// <remarks>
    /// A capturer retains the scene between captures, which is what makes
    /// repeated rendering from one session correct. Before OpenUSD
    /// 0.8.0-alpha the one-shot <c>SilkFrameCapture.Capture</c> silently
    /// returned an all-zero frame on any second capture, indistinguishable
    /// from a black scene, and this provider worked around it by building a
    /// session per request. That defect is fixed upstream
    /// (openusd-dotnet#13), so the workaround is gone. The blank-frame guard
    /// stays: a frame with no draws is still worth refusing rather than
    /// serving as though it were a picture of nothing.
    /// </remarks>
    public sealed class OpenUsdSceneCameraCaptureProvider : ISceneCameraCaptureProvider, IDisposable
    {
        /// <summary>
        /// Initializes a provider with default options and no telemetry.
        /// </summary>
        public OpenUsdSceneCameraCaptureProvider()
            : this(new OpenUsdSceneCaptureOptions(), telemetry: null)
        {
        }

        /// <summary>
        /// Initializes a provider with the supplied options, threading the
        /// host's <see cref="ITelemetryContext"/> for source-generated
        /// logging. The device probe runs synchronously here so
        /// <see cref="Backend"/> is populated by the time the constructor
        /// returns.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
        public OpenUsdSceneCameraCaptureProvider(
            OpenUsdSceneCaptureOptions options, ITelemetryContext? telemetry)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_logger = telemetry.CreateLogger<OpenUsdSceneCameraCaptureProvider>();
            PluginPath = ResolvePluginPath(options.PluginPath);

            if (DeviceSelector.TrySelectDevice(
                options, m_logger, out SelectedSilkDevice selected, out string reason))
            {
                m_device = selected.Device;
                Backend = selected.Backend;
                m_backendUnavailableReason = null;
            }
            else
            {
                m_device = null;
                m_backendUnavailableReason = reason;
                Backend = new SceneCameraCaptureBackend
                {
                    Name = "None",
                    IsAvailable = false,
                    IsSoftware = false,
                    UnavailableReason = reason
                };
            }
        }

        /// <inheritdoc/>
        public SceneCameraCaptureBackend Backend { get; }

        /// <summary>
        /// The plugin path the provider probed for on construction; useful
        /// for a diagnostic /health endpoint.
        /// </summary>
        public string? PluginPath { get; }

        /// <inheritdoc/>
        public async ValueTask<SceneCameraCaptureResult> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            long start = Stopwatch.GetTimestamp();
            DateTime timestamp = request.TimestampUtc ?? DateTime.UtcNow;

            SceneCameraCaptureResult? validation = ValidateRequest(request, timestamp, start);
            if (validation is not null)
            {
                return validation;
            }
            if (m_device is null)
            {
                return NoBackend(request, timestamp, start);
            }

            await m_captureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(
                    () => CaptureCore(request, timestamp, start, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_captureGate.Release();
            }
        }

        /// <summary>
        /// Disposes the shared graphics device and internal synchronization
        /// primitive. In-flight captures are allowed to finish; further
        /// calls throw <see cref="ObjectDisposedException"/>.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            m_captureGate.Dispose();

            // The capturer holds the retained scene and must go before the session it rendered
            // from; the device outlives both.
            m_capturer?.Dispose();
            m_session?.Dispose();
            m_capturer = null;
            m_session = null;
            m_sessionStageIdentifier = null;
            m_device?.Dispose();
        }

        private SceneCameraCaptureResult CaptureCore(
            SceneCameraCaptureRequest request,
            DateTime timestamp,
            long start,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UsdStage? stage = null;
            try
            {
                try
                {
                    stage = UsdStage.Open(request.StageIdentifier);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.StageOpenFailed(request.StageIdentifier, ex);
                    return Failure(SceneCameraCaptureStatus.StageOpenFailed,
                        $"UsdStage.Open('{request.StageIdentifier}') failed: {ex.Message}",
                        request, timestamp, start);
                }

                CameraState camera;
                try
                {
                    // CameraState.FromStageCamera landed in OpenUSD 0.8.0-alpha (openusd-dotnet#14),
                    // so the projection maths is the package's own rather than derived here from
                    // the prim's window and clipping values.
                    camera = string.IsNullOrEmpty(request.PrimPath)
                        ? CameraState.Default
                        : CameraState.FromStageCamera(
                            stage, request.PrimPath!, request.TimeCode, request.Width, request.Height);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.CameraResolveFailed(request.PrimPath ?? string.Empty,
                        request.StageIdentifier, ex);
                    return Failure(SceneCameraCaptureStatus.CameraResolveFailed,
                        $"Resolving camera prim '{request.PrimPath}' failed: {ex.Message}",
                        request, timestamp, start);
                }

                try
                {
                    // A capturer retains the scene between captures, so the session and the
                    // capturer are kept for as long as the requests keep naming the same stage.
                    // Rebuilding them per request would discard that scene and pay the stage-open
                    // cost every perception cycle.
                    if (m_session is null ||
                        !string.Equals(m_sessionStageIdentifier, request.StageIdentifier, StringComparison.Ordinal))
                    {
                        m_capturer?.Dispose();
                        m_session?.Dispose();
                        m_capturer = null;
                        m_session = null;
                        m_sessionStageIdentifier = null;

                        m_session = PluginPath is null
                            ? OpenUsdSilkRuntime.Create(string.Empty, request.StageIdentifier)
                            : OpenUsdSilkRuntime.Create(PluginPath, request.StageIdentifier);
                        m_capturer = new SilkFrameCapturer(m_device!);
                        m_sessionStageIdentifier = request.StageIdentifier;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_capturer?.Dispose();
                    m_session?.Dispose();
                    m_capturer = null;
                    m_session = null;
                    m_sessionStageIdentifier = null;
                    m_logger.RenderFailed(Backend.Name, ex);
                    return Failure(SceneCameraCaptureStatus.RenderFailed,
                        $"OpenUsdSilkRuntime.Create failed: {ex.Message}",
                        request, timestamp, start);
                }

                cancellationToken.ThrowIfCancellationRequested();

                SilkFrameCaptureResult frame;
                try
                {
                    frame = m_capturer!.Capture(
                        m_session!, request.Width, request.Height, request.TimeCode, camera);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    m_logger.RenderFailed(Backend.Name, ex);
                    return Failure(SceneCameraCaptureStatus.RenderFailed,
                        $"SilkFrameCapturer.Capture failed on {Backend.Name}: {ex.Message}",
                        request, timestamp, start);
                }

                BlankFrameCheck guard = BlankFrameGuard.Check(frame);
                if (guard.IsBlank)
                {
                    m_logger.BlankFrameDetected(guard.DrawCount, guard.MeshCount, guard.IsUniform);
                    return Failure(SceneCameraCaptureStatus.BlankFrame,
                        guard.Reason ?? "render produced no visible geometry",
                        request, timestamp, start, frame.Width, frame.Height);
                }

                byte[] png;
                try
                {
                    png = PngEncoder.EncodeRgba8(frame.Width, frame.Height, frame.Rgba.Span);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
                {
                    m_logger.EncodingFailed(frame.Width, frame.Height, ex);
                    return Failure(SceneCameraCaptureStatus.EncodingFailed,
                        $"PNG encoding failed: {ex.Message}",
                        request, timestamp, start, frame.Width, frame.Height);
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
                m_logger.CaptureSucceeded(frame.Width, frame.Height, (long)elapsed.TotalMilliseconds,
                    guard.DrawCount, guard.MeshCount, Backend.Name);
                return new SceneCameraCaptureResult
                {
                    Status = SceneCameraCaptureStatus.Succeeded,
                    Reason = null,
                    Image = ByteString.From(png),
                    Format = request.Format,
                    Width = frame.Width,
                    Height = frame.Height,
                    TimestampUtc = timestamp,
                    Elapsed = elapsed,
                    Backend = Backend
                };
            }
            finally
            {
                stage?.Dispose();
            }
        }

        private static bool IsLocalPath(string stageIdentifier)
        {
            // Only a plain filesystem path is checkable here. Anything carrying a URI scheme
            // (an asset-resolver path, a remote stage) is left to the native resolver.
            return !Uri.TryCreate(stageIdentifier, UriKind.Absolute, out Uri? uri) || uri.IsFile;
        }

        private SceneCameraCaptureResult? ValidateRequest(
            SceneCameraCaptureRequest request, DateTime timestamp, long start)
        {
            if (string.IsNullOrWhiteSpace(request.StageIdentifier))
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    "StageIdentifier must not be empty.", request, timestamp, start);
            }

            // A stage identifier that names no readable file has been observed to tear the
            // process down inside the native UsdStage.Open rather than returning an error, and
            // an AccessViolationException cannot be caught here. Reject it in managed code so a
            // bad request costs a failed capture rather than the whole server.
            if (IsLocalPath(request.StageIdentifier) && !File.Exists(request.StageIdentifier))
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    $"StageIdentifier '{request.StageIdentifier}' does not name a readable file.",
                    request, timestamp, start);
            }
            if (request.Width <= 0 || request.Height <= 0)
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    $"Width and height must be positive (got {request.Width}x{request.Height}).",
                    request, timestamp, start);
            }
            if (request.Width > m_options.MaxFrameWidth || request.Height > m_options.MaxFrameHeight)
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    $"Requested frame size {request.Width}x{request.Height} exceeds the configured maximum " +
                    $"{m_options.MaxFrameWidth}x{m_options.MaxFrameHeight}.",
                    request, timestamp, start);
            }
            if (request.Format != SceneCameraImageFormat.Png)
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    $"Image format {request.Format} is not supported by this provider.",
                    request, timestamp, start);
            }
            return null;
        }

        private SceneCameraCaptureResult NoBackend(
            SceneCameraCaptureRequest request, DateTime timestamp, long start)
        {
            return new SceneCameraCaptureResult
            {
                Status = SceneCameraCaptureStatus.NoRenderingBackend,
                Reason = m_backendUnavailableReason
                    ?? "No graphics backend is available on this host.",
                Image = default,
                Format = request.Format,
                Width = 0,
                Height = 0,
                TimestampUtc = timestamp,
                Elapsed = Stopwatch.GetElapsedTime(start),
                Backend = Backend
            };
        }

        private SceneCameraCaptureResult Failure(
            SceneCameraCaptureStatus status,
            string reason,
            SceneCameraCaptureRequest request,
            DateTime timestamp,
            long start,
            int width = 0,
            int height = 0)
        {
            return new SceneCameraCaptureResult
            {
                Status = status,
                Reason = reason,
                Image = default,
                Format = request.Format,
                Width = width == 0 ? request.Width : width,
                Height = height == 0 ? request.Height : height,
                TimestampUtc = timestamp,
                Elapsed = Stopwatch.GetElapsedTime(start),
                Backend = Backend
            };
        }

        private static string? ResolvePluginPath(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Directory.Exists(configured) ? configured : null;
            }
            string candidate = Path.Combine(AppContext.BaseDirectory, "plugin", "usd");
            return Directory.Exists(candidate) ? candidate : null;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(OpenUsdSceneCameraCaptureProvider));
            }
        }

        private readonly OpenUsdSceneCaptureOptions m_options;
        private readonly ILogger m_logger;
        private readonly ISilkGraphicsDevice? m_device;
        private readonly string? m_backendUnavailableReason;
        private readonly SemaphoreSlim m_captureGate = new(1, 1);
        private OpenUsdSilkSession? m_session;
        private SilkFrameCapturer? m_capturer;
        private string? m_sessionStageIdentifier;
        private int m_disposed;
    }
}
