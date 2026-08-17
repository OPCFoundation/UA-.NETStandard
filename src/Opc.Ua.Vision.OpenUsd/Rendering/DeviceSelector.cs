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
using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Vulkan;

namespace Opc.Ua.Vision.OpenUsd.Rendering
{
    /// <summary>
    /// Result of a single successful device probe.
    /// </summary>
    internal readonly record struct SelectedSilkDevice(
        ISilkGraphicsDevice Device,
        SceneCameraCaptureBackend Backend);

    /// <summary>
    /// Picks the best available <see cref="ISilkGraphicsDevice"/> for the
    /// host. Order is fixed by <see cref="OpenUsdSceneCaptureOptions"/>:
    /// <list type="bullet">
    /// <item><description>Windows: D3D12 hardware -> D3D12 WARP -> Vulkan.
    /// When <see cref="OpenUsdSceneCaptureOptions.PreferSoftware"/> is set,
    /// WARP is tried first.</description></item>
    /// <item><description>Non-Windows: Vulkan only. The OpenUSD runtime
    /// packages bundle SwiftShader on linux-x64, so this succeeds even on
    /// a CI host without a GPU.</description></item>
    /// </list>
    /// The probe order is deliberately tolerant: every backend that throws
    /// during <c>Create</c> is caught and turned into a
    /// <see cref="OpenUsdCaptureLog.BackendUnavailable"/> warning, and the
    /// next backend is tried.
    /// </summary>
    internal static class DeviceSelector
    {
        public static bool TrySelectDevice(
            OpenUsdSceneCaptureOptions options,
            ILogger logger,
            out SelectedSilkDevice selected,
            out string aggregateReason)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            List<BackendProbe> probes = BuildProbeOrder(options);
            List<string> reasons = [];
            foreach (BackendProbe probe in probes)
            {
                ISilkGraphicsDevice? device = null;
                try
                {
                    device = probe.Factory();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    string reason = FormatException(ex);
                    reasons.Add($"{probe.Name}: {reason}");
                    logger.BackendUnavailable(probe.Name, reason);
                    continue;
                }

                SilkGraphicsCapabilities caps;
                try
                {
                    caps = device.Capabilities;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    string reason = FormatException(ex);
                    reasons.Add($"{probe.Name}: {reason}");
                    logger.BackendUnavailable(probe.Name, reason);
                    device.Dispose();
                    continue;
                }

                if (!options.AllowSoftwareFallback && caps.IsSoftware && !probe.PreferredAsSoftware)
                {
                    string reason = "created a software device but AllowSoftwareFallback is false";
                    reasons.Add($"{probe.Name}: {reason}");
                    logger.BackendUnavailable(probe.Name, reason);
                    device.Dispose();
                    continue;
                }

                var backend = new SceneCameraCaptureBackend
                {
                    Name = probe.Name,
                    DeviceName = caps.DeviceName ?? string.Empty,
                    ApiVersion = caps.ApiVersion ?? string.Empty,
                    IsSoftware = caps.IsSoftware,
                    IsAvailable = true,
                    UnavailableReason = null,
                };
                logger.BackendSelected(backend.Name, backend.DeviceName, backend.IsSoftware);
                selected = new SelectedSilkDevice(device, backend);
                aggregateReason = string.Empty;
                return true;
            }

            aggregateReason = reasons.Count == 0
                ? "no graphics backend is registered for this host"
                : string.Join(" | ", reasons);
            logger.NoBackendAvailable(aggregateReason);
            selected = default;
            return false;
        }

        private static List<BackendProbe> BuildProbeOrder(OpenUsdSceneCaptureOptions options)
        {
            List<BackendProbe> probes = [];
            if (OperatingSystem.IsWindows())
            {
                AddWindowsD3D12Probes(probes, options);
            }
            probes.Add(new BackendProbe(
                Name: "Vulkan",
                Factory: () => VulkanSilkGraphicsDevice.Create(),
                PreferredAsSoftware: false));
            return probes;
        }

        [SupportedOSPlatform("windows")]
        private static void AddWindowsD3D12Probes(List<BackendProbe> probes, OpenUsdSceneCaptureOptions options)
        {
            if (options.PreferSoftware)
            {
                probes.Add(new BackendProbe(
                    Name: "D3D12 (WARP)",
                    Factory: () => D3D12SilkGraphicsDevice.Create(useWarp: true),
                    PreferredAsSoftware: true));
                probes.Add(new BackendProbe(
                    Name: "D3D12",
                    Factory: () => D3D12SilkGraphicsDevice.Create(useWarp: false),
                    PreferredAsSoftware: false));
                return;
            }
            probes.Add(new BackendProbe(
                Name: "D3D12",
                Factory: () => D3D12SilkGraphicsDevice.Create(useWarp: false),
                PreferredAsSoftware: false));
            if (options.AllowSoftwareFallback)
            {
                probes.Add(new BackendProbe(
                    Name: "D3D12 (WARP)",
                    Factory: () => D3D12SilkGraphicsDevice.Create(useWarp: true),
                    PreferredAsSoftware: true));
            }
        }

        private static string FormatException(Exception ex)
        {
            string inner = ex.InnerException is null
                ? string.Empty
                : $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
            return $"{ex.GetType().Name}: {ex.Message}{inner}";
        }

        private readonly record struct BackendProbe(
            string Name,
            Func<ISilkGraphicsDevice> Factory,
            bool PreferredAsSoftware);
    }
}
