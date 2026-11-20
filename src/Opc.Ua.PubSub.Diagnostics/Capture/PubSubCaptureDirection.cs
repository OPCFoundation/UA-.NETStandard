// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Direction of a captured PubSub transport frame relative to the
    /// local node.
    /// </summary>
    public enum PubSubCaptureDirection
    {
        /// <summary>
        /// Direction not determined.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The frame was sent by the local node (publisher / discovery
        /// response).
        /// </summary>
        Outbound = 1,

        /// <summary>
        /// The frame was received by the local node (subscriber /
        /// discovery request).
        /// </summary>
        Inbound = 2
    }
}
