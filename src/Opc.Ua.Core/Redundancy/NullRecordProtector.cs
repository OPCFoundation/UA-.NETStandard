// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// A no-op <see cref="IRecordProtector"/> that passes records through
    /// unchanged. It is the default for single-process / in-memory demos and
    /// tests. Production deployments backed by a network store (e.g. Redis)
    /// must configure an authenticated-encryption protector
    /// (<see cref="AesCbcHmacRecordProtector"/>); see
    /// <c>docs/HighAvailability.md</c>.
    /// </summary>
    public sealed class NullRecordProtector :
        IOwnedRecordProtector
    {
        /// <summary>
        /// The shared singleton instance.
        /// </summary>
        public static NullRecordProtector Instance { get; } = new();

        /// <inheritdoc/>
        public ByteString Protect(ByteString context, ByteString plaintext)
        {
            return plaintext;
        }

        /// <inheritdoc/>
        public bool TryUnprotect(
            ByteString context,
            ByteString protectedRecord,
            out ByteString plaintext)
        {
            plaintext = protectedRecord;
            return true;
        }

        /// <inheritdoc/>
        public bool TryUnprotectOwned(
            ByteString context,
            ByteString protectedRecord,
            out byte[] plaintext)
        {
            // Pass-through: hand back an independent copy so the caller can wipe
            // it without ever mutating the shared-store input buffer this
            // protector would otherwise alias.
            plaintext = protectedRecord.IsNull ? [] : protectedRecord.ToArray();
            return true;
        }
    }
}
