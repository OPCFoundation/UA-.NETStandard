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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Volatile, process-memory-only <see cref="IPendingCertificateKeyStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This implementation exists solely so unit tests can exercise the
    /// pending-key lifecycle (replace-on-repeat, cross-Session consumption,
    /// cleanup) deterministically and without touching the file system. It
    /// does <b>not</b> satisfy OPC 10000-12 §7.10.10 ("survives ... process
    /// restart") and must never be used as a production default; hosts that
    /// need injectable behavior in production should implement
    /// <see cref="IPendingCertificateKeyStore"/> against their own secret
    /// store instead of relying on this type.
    /// </para>
    /// <para>
    /// <see cref="ConfigurationNodeManager"/> only falls back to the
    /// certificate-store-backed <see cref="DirectoryPendingCertificateKeyStore"/>
    /// by default; this type must be supplied explicitly (direct
    /// construction or DI) to be used at all.
    /// </para>
    /// </remarks>
    public sealed class InMemoryPendingCertificateKeyStore : IPendingCertificateKeyStore
    {
        /// <inheritdoc/>
        public ValueTask<bool> SaveAsync(
            PendingCertificateKeyContext context,
            Certificate certificateWithPrivateKey,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (certificateWithPrivateKey == null)
            {
                throw new ArgumentNullException(nameof(certificateWithPrivateKey));
            }

            var key = new ScopeKey(context.CertificateGroupId, context.CertificateTypeId);
            Certificate owned = certificateWithPrivateKey.AddRef();

            lock (m_lock)
            {
                if (m_entries.Remove(key, out Certificate? previous))
                {
                    previous.Dispose();
                }

                m_entries[key] = owned;
            }

            return new ValueTask<bool>(true);
        }

        /// <inheritdoc/>
        public ValueTask<Certificate?> TryTakeAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var key = new ScopeKey(context.CertificateGroupId, context.CertificateTypeId);

            // CA2000: ownership of `entry` transfers to the caller per the
            // IPendingCertificateKeyStore.TryTakeAsync contract ("the
            // caller owns and must dispose the returned Certificate");
            // this store must not dispose it.
#pragma warning disable CA2000
            lock (m_lock)
            {
                if (m_entries.Remove(key, out Certificate? entry))
                {
                    return new ValueTask<Certificate?>(entry);
                }
            }
#pragma warning restore CA2000

            return new ValueTask<Certificate?>((Certificate?)null);
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var key = new ScopeKey(context.CertificateGroupId, context.CertificateTypeId);

            lock (m_lock)
            {
                if (m_entries.Remove(key, out Certificate? entry))
                {
                    entry.Dispose();
                }
            }

            return default;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<ScopeKey, Certificate> m_entries = [];

        private readonly record struct ScopeKey(NodeId CertificateGroupId, NodeId CertificateTypeId);
    }
}
