// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Output formats supported by the diagnostics pipeline.
    /// </summary>
    public enum FormatKind
    {
        /// <summary>
        /// libpcap (binary) - openable in Wireshark.
        /// </summary>
        Pcap = 0,

        /// <summary>
        /// PcapNg (binary) - openable in Wireshark.
        /// </summary>
        PcapNg = 1,

        /// <summary>
        /// Per-frame JSON.
        /// </summary>
        Json = 2,

        /// <summary>
        /// Per-frame CSV.
        /// </summary>
        Csv = 3,

        /// <summary>
        /// Human-readable per-frame text.
        /// </summary>
        Text = 4,

        /// <summary>
        /// OPC UA-specific decoded service-call timeline (text).
        /// </summary>
        ServiceTimeline = 5
    }
}
