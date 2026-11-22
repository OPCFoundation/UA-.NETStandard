// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            IsSoftwareKnown = false,
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
        /// Gets whether the owning renderer exposes a reliable software/hardware classification.
        /// When false, <see cref="IsSoftware"/> must not be interpreted as a hardware guarantee.
        /// </summary>
        public bool IsSoftwareKnown { get; init; } = true;

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
