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

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// An <see cref="IRecordProtector"/> that supports staged, zero-downtime
    /// key rotation. New records are always written (and authenticated) with a
    /// single <em>active</em> key, while reads are verified against the active
    /// key first and then any number of <em>retired</em> keys still present in
    /// the store. This lets an operator introduce a new key fleet-wide, let
    /// records re-write under it over time, and only then drop the old key —
    /// without a flag-day re-encryption (IEC 62443 CR4.3 / Microsoft SDL key
    /// rotation). Each member key is identified by its <c>keyId</c>, so a record
    /// is only ever decrypted by the key version that produced it.
    /// </summary>
    public sealed class KeyRingRecordProtector : IRecordProtector, IDisposable
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
        public ByteString Protect(ByteString plaintext)
        {
            return m_active.Protect(plaintext);
        }

        /// <inheritdoc/>
        public bool TryUnprotect(ByteString protectedRecord, out ByteString plaintext)
        {
            // A record carries the key-id of the key that produced it; each
            // member rejects (fail-closed) any record it did not produce, so the
            // first success is unambiguous.
            foreach (IRecordProtector protector in m_all)
            {
                if (protector.TryUnprotect(protectedRecord, out plaintext))
                {
                    return true;
                }
            }
            plaintext = default;
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
