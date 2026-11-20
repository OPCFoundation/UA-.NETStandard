/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// An <see cref="IRecordProtector"/> that supports staged, zero-downtime
    /// key rotation. New records are always written (and authenticated) with a
    /// single <em>active</em> key, while reads are verified against the active
    /// key first and then any number of <em>retired</em> keys still present in
    /// the store. This lets an operator introduce a new key fleet-wide, let
    /// records re-write under it over time, and only then drop the old key —
    /// without a flag-day re-encryption (staged key
    /// rotation). Each member key is identified by its <c>keyId</c>, so a record
    /// is only ever decrypted by the key version that produced it.
    /// </summary>
    public sealed class KeyRingRecordProtector :
        IOwnedRecordProtector,
        IDisposable
    {
        /// <summary>
        /// Creates a key ring.
        /// </summary>
        /// <param name="active">
        /// The active protector used to protect (write) every new record.
        /// </param>
        /// <param name="retired">
        /// Older protectors retained only to unprotect (read) records written
        /// before the most recent rotation. May be empty.
        /// </param>
        public KeyRingRecordProtector(IRecordProtector active, params IRecordProtector[] retired)
        {
            m_active = active ?? throw new ArgumentNullException(nameof(active));
            var all = new List<IRecordProtector>(1 + (retired?.Length ?? 0)) { active };
            if (retired != null)
            {
                foreach (IRecordProtector protector in retired)
                {
                    if (protector == null)
                    {
                        throw new ArgumentException(
                            "Retired protectors must not be null.", nameof(retired));
                    }
                    all.Add(protector);
                }
            }
            m_all = all;
        }

        /// <inheritdoc/>
        public ByteString Protect(ByteString context, ByteString plaintext)
        {
            return m_active.Protect(context, plaintext);
        }

        /// <inheritdoc/>
        public bool TryUnprotect(
            ByteString context,
            ByteString protectedRecord,
            out ByteString plaintext)
        {
            foreach (IRecordProtector protector in m_all)
            {
                if (protector.TryUnprotect(context, protectedRecord, out plaintext))
                {
                    return true;
                }
            }

            plaintext = default;
            return false;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Only members implementing <see cref="IOwnedRecordProtector"/> are consulted.
        /// Other members are skipped without decrypting or copying immutable plaintext.
        /// If no capable member authenticates the record, the read fails closed.
        /// </remarks>
        public bool TryUnprotectOwned(
            ByteString context,
            ByteString protectedRecord,
            out byte[] plaintext)
        {
            foreach (IRecordProtector protector in m_all)
            {
                if (protector is IOwnedRecordProtector ownedProtector &&
                    ownedProtector.TryUnprotectOwned(context, protectedRecord, out plaintext))
                {
                    return true;
                }
            }

            plaintext = [];
            return false;
        }

        /// <summary>
        /// Disposes every member protector that owns key material.
        /// </summary>
        public void Dispose()
        {
            foreach (IRecordProtector protector in m_all)
            {
                if (protector is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private readonly IRecordProtector m_active;
        private readonly IReadOnlyList<IRecordProtector> m_all;
    }
}
