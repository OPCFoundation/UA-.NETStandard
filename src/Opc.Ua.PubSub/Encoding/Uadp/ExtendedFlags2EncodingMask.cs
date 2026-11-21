// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// ExtendedFlags2 byte of a UADP NetworkMessage. Selects between
    /// regular DataSetMessages, chunked transfers, discovery
    /// NetworkMessage subtypes, and ActionHeader presence.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.4">
    /// Part 14 §A.2.2.4 — UADP NetworkMessage Header Layout</see>
    /// (Table 160). The low 2 bits distinguish chunked messages and
    /// the optional promoted-fields header; bits 2-4 carry the UADP
    /// NetworkMessage type, and bit 5 marks an ActionHeader.
    /// </remarks>
    [Flags]
    public enum ExtendedFlags2EncodingMask : byte
    {
        /// <summary>
        /// No ExtendedFlags2 bits set; the message is a plain
        /// DataSetMessage transfer.
        /// </summary>
        None = 0,

        /// <summary>
        /// Bit 0 — Chunk message. When set, the payload is one chunk of
        /// a DataSetMessage (or discovery announcement) that did not fit
        /// into a single NetworkMessage (Part 14 §7.2.4.4.4); full
        /// reassembly is required before decoding it.
        /// </summary>
        ChunkMessage = 0x01,

        /// <summary>
        /// Bit 1 — PromotedFields. When set, the NetworkMessage
        /// header carries a length-prefixed array of promoted field
        /// values usable by middleware that filters without
        /// decrypting.
        /// </summary>
        PromotedFields = 0x02,

        /// <summary>
        /// Bit 2 — NetworkMessage carries a DiscoveryRequest.
        /// </summary>
        NetworkMessageWithDiscoveryRequest = 0x04,

        /// <summary>
        /// Bit 3 — NetworkMessage carries a DiscoveryResponse.
        /// </summary>
        NetworkMessageWithDiscoveryResponse = 0x08,

        /// <summary>
        /// Bit 5 — NetworkMessage carries an ActionHeader for an
        /// ActionRequest or ActionResponse. Part 14 v1.05 Table 154
        /// keeps action request/response payloads under the default
        /// DataSetMessage NetworkMessage type and uses ActionFlags bit 0
        /// to distinguish request from response.
        /// </summary>
        ActionHeaderEnabled = 0x20
    }
}
