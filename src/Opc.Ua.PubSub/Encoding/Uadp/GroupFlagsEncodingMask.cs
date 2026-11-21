// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// GroupFlags byte of a UADP NetworkMessage's optional GroupHeader
    /// section. Each bit selects whether the corresponding scalar
    /// follows in the group header.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.4">
    /// Part 14 §A.2.1.4 — UADP NetworkMessage Group Header</see>
    /// (Table 161). The GroupHeader is only present when
    /// <see cref="UadpFlagsEncodingMask.GroupHeaderEnabled"/> is set on
    /// the NetworkMessage flags byte.
    /// </remarks>
    [Flags]
    public enum GroupFlagsEncodingMask : byte
    {
        /// <summary>
        /// No optional group-header fields are present.
        /// </summary>
        None = 0,

        /// <summary>
        /// Bit 0 — WriterGroupId enabled.
        /// </summary>
        WriterGroupIdEnabled = 0x01,

        /// <summary>
        /// Bit 1 — GroupVersion enabled (UA <c>UInt32</c>).
        /// </summary>
        GroupVersionEnabled = 0x02,

        /// <summary>
        /// Bit 2 — NetworkMessageNumber enabled (UA <c>UInt16</c>).
        /// </summary>
        NetworkMessageNumberEnabled = 0x04,

        /// <summary>
        /// Bit 3 — SequenceNumber enabled (UA <c>UInt16</c>).
        /// </summary>
        SequenceNumberEnabled = 0x08
    }
}
