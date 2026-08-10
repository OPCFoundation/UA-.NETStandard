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
    /// Describes the graphics backend an
    /// <see cref="ISceneCameraCaptureProvider"/> resolved at construction
    /// time. Callers use it to advertise the sensor's capabilities and to
    /// decide whether to bother requesting frames at all: when
    /// <see cref="IsAvailable"/> is <c>false</c> every subsequent capture
    /// returns <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
    /// </summary>
    public sealed record class SceneCameraCaptureBackend
    {
        /// <summary>
        /// A backend descriptor for hosts where no rendering path was
        /// available; used both as a sentinel on
        /// <see cref="SceneCameraCaptureResult"/> and as the value the
        /// provider exposes when device probing failed on every backend.
        /// </summary>
        public static SceneCameraCaptureBackend None { get; } = new()
        {
            Name = "None",
            IsAvailable = false,
            IsSoftware = false,
            UnavailableReason = "No graphics backend has been probed yet."
        };

        /// <summary>
        /// Short human-readable backend name for logging and diagnostics
        /// (for example <c>"D3D12"</c>, <c>"D3D12 (WARP)"</c>,
        /// <c>"Vulkan"</c>, or <c>"None"</c>).
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Backend adapter description (GPU name, driver / API version),
        /// as reported by the Silk device. Empty when
        /// <see cref="IsAvailable"/> is <c>false</c>.
        /// </summary>
        public string DeviceName { get; init; } = string.Empty;

        /// <summary>
        /// Backend API version string as reported by the Silk device.
        /// Empty when <see cref="IsAvailable"/> is <c>false</c>.
        /// </summary>
        public string ApiVersion { get; init; } = string.Empty;

        /// <summary>
        /// <c>true</c> when the backend is a software rasterizer (D3D12
        /// WARP, or a Vulkan software ICD such as SwiftShader). Software
        /// backends render the same output as hardware ones, but much more
        /// slowly, and the Vision server may choose to lower cadence when
        /// this is <c>true</c>.
        /// </summary>
        public bool IsSoftware { get; init; }

        /// <summary>
        /// <c>true</c> when the provider will attempt capture; <c>false</c>
        /// when every backend probe failed and the provider will short-circuit
        /// every capture to <see cref="SceneCameraCaptureStatus.NoRenderingBackend"/>.
        /// </summary>
        public bool IsAvailable { get; init; }

        /// <summary>
        /// Set to a human-readable reason (aggregated from every backend
        /// that was tried) when <see cref="IsAvailable"/> is <c>false</c>;
        /// <c>null</c> otherwise.
        /// </summary>
        public string? UnavailableReason { get; init; }
    }
}
