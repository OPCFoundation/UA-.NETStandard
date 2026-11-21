// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Protects records and transfers ownership of decrypted plaintext so callers can erase secrets.
    /// </summary>
    public interface IOwnedRecordProtector : IRecordProtector
    {
        /// <summary>
        /// Authenticates the required context and transfers the original decrypted buffer to the caller.
        /// </summary>
        /// <remarks>
        /// The buffer must not alias the protected input. Implementations must not leave another
        /// unwiped plaintext copy behind. The caller must wipe the returned buffer in a finally block.
        /// Rejection must not trigger an unbound decrypt or an immutable-plaintext fallback.
        /// </remarks>
        /// <param name="context">The exact authenticated context; null and empty bytes are equivalent.</param>
        /// <param name="protectedRecord">The protected envelope.</param>
        /// <param name="plaintext">The caller-owned plaintext buffer on success; an empty buffer on failure.</param>
        /// <returns>Whether the record was authenticated for the context and decrypted.</returns>
        bool TryUnprotectOwned(ByteString context, ByteString protectedRecord, out byte[] plaintext);
    }
}
