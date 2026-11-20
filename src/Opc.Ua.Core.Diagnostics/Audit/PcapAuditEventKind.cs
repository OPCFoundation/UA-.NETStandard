// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Pcap.Audit
{
    /// <summary>
    /// Categorizes a <see cref="PcapAuditEvent"/>. Each kind corresponds
    /// to one user-visible MCP tool or capture-manager operation that
    /// touches secret material, opens an output file, or stands up a
    /// network endpoint.
    /// </summary>
    public enum PcapAuditEventKind
    {
        /// <summary>
        /// A capture session was started.
        /// </summary>
        StartCapture,

        /// <summary>
        /// A capture session was stopped.
        /// </summary>
        StopCapture,

        /// <summary>
        /// Channel keys were dumped to a keylog.
        /// </summary>
        DumpKeys,

        /// <summary>
        /// An offline pcap was decoded with a keylog.
        /// </summary>
        DecodePcapWithKeys,

        /// <summary>
        /// A replay session was started.
        /// </summary>
        StartReplay,

        /// <summary>
        /// A replay session was stopped.
        /// </summary>
        StopReplay,

        /// <summary>
        /// A captured frame was observed (rate-limited).
        /// </summary>
        FrameCaptured
    }
}
