/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Conversions of the read-only channel evidence in <see cref="SecureChannelContext"/>
    /// for APIs that take arrays.
    /// </summary>
    internal static class ChannelEvidenceExtensions
    {
        /// <summary>
        /// Returns a copy of the bytes, or <c>null</c> for a null byte string, matching
        /// the nullable arrays the signature helpers expect.
        /// </summary>
        public static byte[]? ToArrayOrNull(this ByteString value)
        {
            return value.IsNull ? null : value.ToArray();
        }
    }
}
