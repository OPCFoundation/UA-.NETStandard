// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
