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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Pure UTF-8 conversion conveniences for the canonical byte-context protection contract.
    /// </summary>
    public static class RecordProtectorExtensions
    {
        /// <summary>
        /// Converts a textual context to UTF-8 and protects the plaintext.
        /// </summary>
        /// <param name="protector">The protector to invoke.</param>
        /// <param name="context">The context; null and empty strings both encode as empty bytes.</param>
        /// <param name="plaintext">The record to protect.</param>
        /// <returns>The protected envelope.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="protector"/> is <c>null</c>.</exception>
        public static ByteString Protect(this IRecordProtector protector, string? context, ByteString plaintext)
        {
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            return protector.Protect(RecordProtectionContext.Encode(context), plaintext);
        }

        /// <summary>
        /// Converts a textual context to UTF-8 and authenticates and decrypts the record exactly once.
        /// </summary>
        /// <param name="protector">The protector to invoke.</param>
        /// <param name="context">The context; null and empty strings both encode as empty bytes.</param>
        /// <param name="protectedRecord">The protected envelope.</param>
        /// <param name="plaintext">The recovered plaintext on success; null bytes on failure.</param>
        /// <returns>Whether the canonical protection operation succeeded.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="protector"/> is <c>null</c>.</exception>
        public static bool TryUnprotect(
            this IRecordProtector protector,
            string? context,
            ByteString protectedRecord,
            out ByteString plaintext)
        {
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            return protector.TryUnprotect(RecordProtectionContext.Encode(context), protectedRecord, out plaintext);
        }

        /// <summary>
        /// Converts a textual context to UTF-8 and transfers the original owned plaintext buffer without copying it.
        /// </summary>
        /// <param name="protector">The owned-buffer protector to invoke.</param>
        /// <param name="context">The context; null and empty strings both encode as empty bytes.</param>
        /// <param name="protectedRecord">The protected envelope.</param>
        /// <param name="plaintext">The caller-owned plaintext to wipe on success; an empty buffer on failure.</param>
        /// <returns>Whether the canonical owned protection operation succeeded.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="protector"/> is <c>null</c>.</exception>
        public static bool TryUnprotectOwned(
            this IOwnedRecordProtector protector,
            string? context,
            ByteString protectedRecord,
            out byte[] plaintext)
        {
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }
            return protector.TryUnprotectOwned(RecordProtectionContext.Encode(context), protectedRecord, out plaintext);
        }
    }
}
