/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Describes a local network interface returned by
    /// <c>list_interfaces</c>.
    /// </summary>
    public sealed class NetworkInterfaceInfo
    {
        /// <summary>
        /// Device name as understood by libpcap (e.g.
        /// <c>\Device\NPF_{guid}</c>).
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Friendly name (e.g. <c>Ethernet</c>).
        /// </summary>
        public string? FriendlyName { get; init; }

        /// <summary>
        /// Free-form description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Bound IP addresses.
        /// </summary>
        public IReadOnlyList<string> Addresses { get; init; } = [];

        /// <summary>
        /// The link-layer type (e.g. <c>Ethernet</c>), or <c>null</c>
        /// when the device is not open.
        /// </summary>
        public string? LinkType { get; init; }

        /// <summary>
        /// Whether this is the loopback interface.
        /// </summary>
        public bool IsLoopback { get; init; }
    }
}
