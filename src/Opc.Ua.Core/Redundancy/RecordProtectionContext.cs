/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
