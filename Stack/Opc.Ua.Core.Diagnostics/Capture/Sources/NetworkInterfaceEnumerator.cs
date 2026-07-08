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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Models;
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
            try
            {
                return LibPcapLiveDeviceList.Instance
                    .Select(CreateInfo)
                    .ToArray();
            }
            catch (Exception ex) when (ex is not PcapDiagnosticsException)
            {
                throw new PcapDiagnosticsException(
                    "Unable to enumerate devices — is libpcap / Npcap installed?", ex);
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
                LinkType = device.LinkType.ToString(),
                IsLoopback = device.Loopback
            };
        }
    }
}
