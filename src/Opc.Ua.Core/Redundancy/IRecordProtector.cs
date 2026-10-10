/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
