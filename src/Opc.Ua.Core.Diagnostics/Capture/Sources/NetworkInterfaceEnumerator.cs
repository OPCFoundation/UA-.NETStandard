// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Opc.Ua.Pcap.Models;
using SharpPcap;
using SharpPcap.LibPcap;

namespace Opc.Ua.Pcap.Capture.Sources
{
    /// <summary>
    /// Enumerates local libpcap/Npcap network interfaces.
    /// </summary>
    [RequiresDynamicCode(kSharpPcapDynamicLoadingMessage)]
    [RequiresUnreferencedCode(kSharpPcapDynamicLoadingMessage)]
    public static class NetworkInterfaceEnumerator
    {
        private const string kSharpPcapDynamicLoadingMessage =
            "SharpPcap requires dynamic native libpcap/Npcap loading and is not NativeAOT/trimming safe.";

        /// <summary>
        /// Lists local interfaces visible to libpcap/Npcap.
        /// </summary>
        /// <exception cref="PcapDiagnosticsException">
        /// libpcap/Npcap is not installed, unavailable, or failed while enumerating interfaces.
        /// </exception>
        public static IReadOnlyList<NetworkInterfaceInfo> ListLocalInterfaces()
        {
            LibPcapLiveDeviceList devices;
            try
            {
                devices = LibPcapLiveDeviceList.Instance;
            }
            catch (Exception ex) when (ex is not PcapDiagnosticsException)
            {
                throw new PcapDiagnosticsException(
                    "Unable to enumerate devices — is libpcap / Npcap installed?", ex);
            }

            return [.. devices.Select(CreateInfo)];
        }

        internal static string? GetLinkType(bool opened, Func<string> readLinkType)
        {
            ArgumentNullException.ThrowIfNull(readLinkType);
            if (!opened)
            {
                return null;
            }

            try
            {
                return readLinkType();
            }
            catch (DeviceNotReadyException)
            {
                return null;
            }
        }

        private static NetworkInterfaceInfo CreateInfo(LibPcapLiveDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);

            return new NetworkInterfaceInfo
            {
                Name = device.Name,
                FriendlyName = device.Interface?.FriendlyName,
                Description = device.Description,
                Addresses = [.. device.Addresses.Select(a => a.Addr?.ToString() ?? string.Empty)],
                LinkType = GetLinkType(device.Opened, () => device.LinkType.ToString()),
                IsLoopback = device.Loopback
            };
        }
    }
}
