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

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Configuration for <see cref="OpenUsdSceneCameraCaptureProvider"/>.
    /// The defaults are chosen so a Vision server host does nothing beyond
    /// registering the provider - the OpenUSD plugin tree is auto-discovered
    /// from <c>AppContext.BaseDirectory</c> and the best available graphics
    /// backend is picked at construction time.
    /// </summary>
    public sealed record class OpenUsdSceneCaptureOptions
    {
        /// <summary>
        /// Absolute path to the OpenUSD plugin tree (<c>plugin/usd</c>).
        /// When <c>null</c> the provider probes <c>{AppContext.BaseDirectory}/plugin/usd</c>
        /// (the layout the OpenUSD runtime packages stage for the RID the
        /// host was published with) and finally falls back to no plugin path
        /// at all, in which case <c>UsdStage.Open</c> only understands the
        /// built-in file formats.
        /// </summary>
        public string? PluginPath { get; init; }

        /// <summary>
        /// When <c>true</c> the provider prefers the D3D12 WARP software
        /// rasterizer over D3D12 hardware. Useful for CI / diagnostics
        /// where the test needs a deterministic backend independent of the
        /// host GPU. On non-Windows hosts this flag has no effect (only the
        /// Vulkan backend is tried, which itself falls back to a software
        /// ICD when no hardware Vulkan loader is present).
        /// </summary>
        public bool PreferSoftware { get; init; }

        /// <summary>
        /// When <c>true</c> the provider falls back to a software backend
        /// (D3D12 WARP on Windows, or a Vulkan software ICD) if no hardware
        /// backend can be created. Defaults to <c>true</c>; set to <c>false</c>
        /// only when a caller explicitly requires hardware acceleration and
        /// would rather fail fast than serve WARP frames.
        /// </summary>
        public bool AllowSoftwareFallback { get; init; } = true;

        /// <summary>
        /// Maximum frame width, in pixels, that <see cref="OpenUsdSceneCameraCaptureProvider"/>
        /// will honour. Requests larger than this are rejected with
        /// <see cref="SceneCameraCaptureStatus.InvalidRequest"/> to keep a
        /// single capture from monopolising a shared software rasterizer.
        /// Defaults to 8192.
        /// </summary>
        public int MaxFrameWidth { get; init; } = 8192;

        /// <summary>
        /// Maximum frame height, in pixels. See <see cref="MaxFrameWidth"/>.
        /// </summary>
        public int MaxFrameHeight { get; init; } = 8192;
    }
}
