// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Text;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Creates canonical authenticated contexts for records in a shared key/value store.
    /// </summary>
    public static class RecordProtectionContext
    {
        /// <summary>
        /// Encodes the record type, a separator and the complete store key as strict UTF-8.
        /// </summary>
        /// <remarks>
        /// Writers and readers must supply the same type and exact key, including configured prefixes.
        /// Keys are not normalized or case-folded. Type labels cannot contain the separator,
        /// so different type/key pairs cannot encode to the same context.
        /// </remarks>
        /// <param name="recordType">A nonempty record type label without a vertical bar.</param>
        /// <param name="storeKey">The nonempty, complete key passed to the shared store.</param>
        /// <returns>The context bytes authenticated by the record protector.</returns>
        /// <exception cref="ArgumentException">The type label or store key is invalid.</exception>
        /// <exception cref="EncoderFallbackException">An input contains invalid UTF-16.</exception>
        public static ByteString Create(string recordType, string storeKey)
        {
            if (string.IsNullOrEmpty(recordType) || recordType.Contains('|', StringComparison.Ordinal))
            {
                throw new ArgumentException("A record type without '|' is required.", nameof(recordType));
            }
            if (string.IsNullOrEmpty(storeKey))
            {
                throw new ArgumentException("A complete store key is required.", nameof(storeKey));
            }
            return Encode(recordType + "|" + storeKey);
        }

        /// <summary>
        /// Converts a textual context without changing it; null and empty strings encode as empty bytes.
        /// </summary>
        internal static ByteString Encode(string? context)
        {
            return string.IsNullOrEmpty(context) ? ByteString.Empty : ByteString.From(s_utf8.GetBytes(context));
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
