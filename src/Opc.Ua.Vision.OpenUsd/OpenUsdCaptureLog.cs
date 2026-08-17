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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Source-generated log messages for
    /// <see cref="OpenUsdSceneCameraCaptureProvider"/>.
    /// </summary>
    internal static partial class OpenUsdCaptureLog
    {
        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 0, Level = LogLevel.Information,
            Message = "OpenUSD capture backend '{BackendName}' selected (device='{DeviceName}', software={IsSoftware}).")]
        public static partial void BackendSelected(
            this ILogger logger, string backendName, string deviceName, bool isSoftware);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 1, Level = LogLevel.Warning,
            Message = "OpenUSD capture backend '{BackendName}' unavailable ({Reason}). Trying next backend.")]
        public static partial void BackendUnavailable(
            this ILogger logger, string backendName, string reason);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 2, Level = LogLevel.Warning,
            Message = "No OpenUSD capture backend is available on this host: {Reason}. " +
                "Captures will report NoRenderingBackend so the Vision server can degrade.")]
        public static partial void NoBackendAvailable(this ILogger logger, string reason);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 3, Level = LogLevel.Debug,
            Message = "Captured {Width}x{Height} PNG in {ElapsedMs} ms " +
                "(drawCount={DrawCount}, meshCount={MeshCount}, backend={BackendName}).")]
        public static partial void CaptureSucceeded(this ILogger logger,
            int width, int height, long elapsedMs, int drawCount, int meshCount, string backendName);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 4, Level = LogLevel.Warning,
            Message = "OpenUSD capture rendered no geometry (drawCount={DrawCount}, meshCount={MeshCount}, uniform={IsUniform}). " +
                "Surfacing BlankFrame so the caller does not serve a misleading picture.")]
        public static partial void BlankFrameDetected(
            this ILogger logger, int drawCount, int meshCount, bool isUniform);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 5, Level = LogLevel.Warning,
            Message = "Failed to open USD stage '{StageIdentifier}'.")]
        public static partial void StageOpenFailed(
            this ILogger logger, string stageIdentifier, Exception exception);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 6, Level = LogLevel.Warning,
            Message = "Failed to resolve camera prim '{PrimPath}' on stage '{StageIdentifier}'.")]
        public static partial void CameraResolveFailed(
            this ILogger logger, string primPath, string stageIdentifier, Exception exception);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 7, Level = LogLevel.Warning,
            Message = "SilkFrameCapture.Capture threw on backend '{BackendName}'.")]
        public static partial void RenderFailed(
            this ILogger logger, string backendName, Exception exception);

        [LoggerMessage(EventId = VisionOpenUsdEventIds.CaptureProvider + 8, Level = LogLevel.Warning,
            Message = "PNG encoding failed for a {Width}x{Height} RGBA8 frame.")]
        public static partial void EncodingFailed(
            this ILogger logger, int width, int height, Exception exception);
    }
}
