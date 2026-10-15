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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Vision.OpenUsd.Encoding;
using Opc.Ua.Vision.OpenUsd.Rendering;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Adapts an existing live renderer to the Vision capture contract. Construction and
    /// availability inspection do not create a renderer, reopen a stage, or start acquisition.
    /// The borrowed frame source retains ownership of its graphics and stage lifetimes.
    /// </summary>
    public sealed class RetainedSceneCameraCaptureProvider : ISceneCameraCaptureProvider
    {
        /// <summary>
        /// Creates a bounded PNG capture adapter over an explicitly owned frame source.
        /// </summary>
        public RetainedSceneCameraCaptureProvider(
            IRetainedSceneFrameSource source,
            ITelemetryContext telemetry,
            int maximumDimension = 2048,
            int maximumFrameBytes = 16 * 1024 * 1024,
            TimeProvider? timeProvider = null)
        {
            m_source = source ?? throw new ArgumentNullException(nameof(source));
            ArgumentNullException.ThrowIfNull(telemetry);
            if (maximumDimension is < 1 or > 8192)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDimension));
            }
            if (maximumFrameBytes is < 4 or > 64 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
            }
            m_maximumDimension = maximumDimension;
            m_maximumFrameBytes = maximumFrameBytes;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_logger = telemetry.CreateLogger<RetainedSceneCameraCaptureProvider>();
        }

        /// <inheritdoc/>
        public SceneCameraCaptureBackend Backend => m_source.Backend;

        /// <inheritdoc/>
        public async ValueTask<SceneCameraCaptureResult> CaptureAsync(
            SceneCameraCaptureRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            long start = Stopwatch.GetTimestamp();
            DateTime timestamp = request.TimestampUtc ?? m_timeProvider.GetUtcNow().UtcDateTime;
            SceneCameraCaptureResult Failure(SceneCameraCaptureStatus status, string reason)
            {
                return new SceneCameraCaptureResult
                {
                    Status = status,
                    Reason = reason,
                    Backend = Backend,
                    TimestampUtc = timestamp,
                    Elapsed = Stopwatch.GetElapsedTime(start)
                };
            }
            if (string.IsNullOrEmpty(request.StageIdentifier) ||
                !string.Equals(request.StageIdentifier, m_source.StageIdentifier, StringComparison.Ordinal) ||
                request.Width < 1 || request.Width > m_maximumDimension ||
                request.Height < 1 || request.Height > m_maximumDimension ||
                (long)request.Width * request.Height * 4 > m_maximumFrameBytes ||
                request.Format != SceneCameraImageFormat.Png ||
                !double.IsFinite(request.TimeCode) ||
                request.PrimPath?.Length > 512 ||
                timestamp.Kind != DateTimeKind.Utc)
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest,
                    "Capture requires this retained stage, a bounded PNG frame, a finite time code and UTC time.");
            }
            if (!Backend.IsAvailable)
            {
                return Failure(SceneCameraCaptureStatus.NoRenderingBackend,
                    Backend.UnavailableReason ?? "Start the embedded renderer before requesting a frame.");
            }
            if (Interlocked.CompareExchange(ref m_capturing, 1, 0) != 0)
            {
                return Failure(SceneCameraCaptureStatus.InvalidRequest, "One retained capture is already in progress.");
            }
            try
            {
                RetainedSceneFrame frame;
                try
                {
                    frame = await m_source.CaptureFrameAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    m_logger.RenderFailed(Backend.Name, error);
                    return Failure(SceneCameraCaptureStatus.RenderFailed,
                        "The retained renderer could not capture the requested camera. Inspect renderer diagnostics.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (frame is null || frame.Width != request.Width || frame.Height != request.Height ||
                    frame.Rgba.Length != (long)request.Width * request.Height * 4)
                {
                    return Failure(SceneCameraCaptureStatus.RenderFailed,
                        "The renderer returned inconsistent dimensions or RGBA8 storage.");
                }
                BlankFrameCheck guard = BlankFrameGuard.Check(frame.DrawCount, frame.MeshCount, frame.Rgba.Span);
                if (guard.IsBlank)
                {
                    m_logger.BlankFrameDetected(guard.DrawCount, guard.MeshCount, guard.IsUniform);
                    return Failure(SceneCameraCaptureStatus.BlankFrame,
                        guard.Reason ?? "The retained frame contains no visible geometry.");
                }
                byte[] png = PngEncoder.EncodeRgba8(frame.Width, frame.Height, frame.Rgba.Span);
                cancellationToken.ThrowIfCancellationRequested();
                return new SceneCameraCaptureResult
                {
                    Status = SceneCameraCaptureStatus.Succeeded,
                    Image = ByteString.From(png),
                    Format = SceneCameraImageFormat.Png,
                    Width = frame.Width,
                    Height = frame.Height,
                    Backend = Backend,
                    TimestampUtc = timestamp,
                    Elapsed = Stopwatch.GetElapsedTime(start)
                };
            }
            finally
            {
                Volatile.Write(ref m_capturing, 0);
            }
        }

        private readonly IRetainedSceneFrameSource m_source;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly int m_maximumDimension;
        private readonly int m_maximumFrameBytes;
        private int m_capturing;
    }
}
