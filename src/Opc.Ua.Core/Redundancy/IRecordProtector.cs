/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Protects extension state written to a shared store with authenticated encryption.
    /// </summary>
    /// <remarks>
    /// OPC 10000-4 §6.6 defines redundant-server behaviour but not a shared-store protection format. The shared store
    /// is treated as an untrusted conduit: persisted records are encrypted and authenticated, and a tampered or forged
    /// record fails closed so it is never decrypted or applied. External shared stores used for mirrored sessions,
    /// subscriptions, retransmission queues, continuation points, or CRDT session records require a protector.
    /// Every operation authenticates an explicit context. Shared-store callers use
    /// <see cref="RecordProtectionContext.Create"/> with the record type and complete store key.
    /// Pass <see langword="default"/> or <see cref="ByteString.Empty"/> only when no binding is required;
    /// both denote the same empty context. Readers must never retry with an empty context after rejection.
    /// </remarks>
    public interface IRecordProtector
    {
        /// <summary>
        /// Encrypts and authenticates <paramref name="plaintext"/> while binding the
        /// envelope to <paramref name="context"/>.
        /// </summary>
        /// <param name="context">The exact context to authenticate; null and empty bytes are equivalent.</param>
        /// <param name="plaintext">The record to protect; null and empty bytes both represent an empty record.</param>
        /// <returns>
        /// The protected envelope.
        /// </returns>
        ByteString Protect(ByteString context, ByteString plaintext);

        /// <summary>
        /// Verifies and decrypts a record only when its context and protection key match.
        /// Missing, malformed or unauthenticated envelopes fail closed.
        /// </summary>
        /// <param name="context">The exact context to authenticate; null and empty bytes are equivalent.</param>
        /// <param name="protectedRecord">The protected envelope.</param>
        /// <param name="plaintext">The recovered plaintext on success; null bytes on failure.</param>
        /// <returns>
        /// <c>true</c> if the record was authenticated for the context and decrypted; otherwise <c>false</c>.
        /// </returns>
        bool TryUnprotect(
            ByteString context,
            ByteString protectedRecord,
            out ByteString plaintext);
    }
}
