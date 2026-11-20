/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// SecurityFlags byte from the UADP NetworkMessage SecurityHeader.
    /// </summary>
    /// <remarks>
    /// Mirrors the bit layout from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6">
    /// Part 14 §A.2.1.6 (NetworkMessage signed and encrypted)</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// Part 14 §A.2.2.5 (NetworkMessage signed)</see>.
    /// </remarks>
    [Flags]
    public enum UadpSecurityFlagsEncodingMask : byte
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// NetworkMessage Signed.
        /// </summary>
        NetworkMessageSigned = 0x01,

        /// <summary>
        /// NetworkMessage Encrypted.
        /// </summary>
        NetworkMessageEncrypted = 0x02,

        /// <summary>
        /// SecurityFooter present.
        /// </summary>
        SecurityFooterEnabled = 0x04,

        /// <summary>
        /// Force key reset.
        /// </summary>
        ForceKeyReset = 0x08
    }
}
