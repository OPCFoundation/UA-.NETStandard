// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// NetworkMessage subtype indicator (UADP). Stored as the high
    /// nibble of the legacy UADPNetworkMessageType byte; mirrors the
    /// values previously surfaced in the v1.5 stack so
    /// downstream code paths remain comparable.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.4">
    /// Part 14 §A.2.2.4 — UADP NetworkMessage Header Layout</see>
    /// Table 154. Discovery-* values are NetworkMessage type tag bits
    /// in the <see cref="ExtendedFlags2EncodingMask"/> byte; Action
    /// request/response use the ActionHeader bit plus ActionFlags.
    /// </remarks>
#pragma warning disable CA1027 // not a flags enum: values are discrete tag codes from Part 14 Table 160
    public enum UadpNetworkMessageType
    {
        /// <summary>
        /// A regular data NetworkMessage carrying one or more
        /// <see cref="UadpDataSetMessage"/> payloads.
        /// </summary>
        DataSetMessage = 0,

        /// <summary>
        /// A discovery request NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest"/>
        /// bit is set; the payload identifies the request type and the
        /// addressed DataSetWriterId list.
        /// </summary>
        DiscoveryRequest = 4,

        /// <summary>
        /// A discovery response NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse"/>
        /// bit is set; the payload carries metadata, configuration, or
        /// endpoint descriptions.
        /// </summary>
        DiscoveryResponse = 8,

        /// <summary>
        /// An action request NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.ActionHeaderEnabled"/>
        /// bit is set and ActionFlags bit 0 identifies the request.
        /// </summary>
        ActionRequest = 0x20,

        /// <summary>
        /// An action response NetworkMessage. The
        /// <see cref="ExtendedFlags2EncodingMask.ActionHeaderEnabled"/>
        /// bit is set and ActionFlags bit 0 is clear.
        /// </summary>
        ActionResponse = 0x21
    }
#pragma warning restore CA1027
}
