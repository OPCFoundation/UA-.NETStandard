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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stages a regenerated private key that lives in a hardware token.
    /// </summary>
    /// <remarks>
    /// The Part 12 push flow regenerates a key with <c>CreateSigningRequest</c>
    /// and consumes it later with <c>UpdateCertificate</c>, possibly in another
    /// Session or after a restart. The directory backed store does this by
    /// exporting the key to a PKCS#12 file, which a key held in a TPM, an HSM or
    /// a PKCS#11 token will refuse, so that store declines any base store it
    /// does not recognise and the request fails with <c>BadNotSupported</c>.
    /// <para>
    /// For a device held key there is nothing to export and nothing to protect:
    /// the device already is the durable store. All that has to survive is the
    /// association between the pending certificate and its scope, which is kept
    /// as the public certificate in the group's own store. On the way back the
    /// key is re-attached by asking that store to load it, exactly as it would
    /// be for an active certificate.
    /// </para>
    /// </remarks>
    public sealed class HardwarePendingCertificateKeyStore : IPendingCertificateKeyStore
    {
        /// <summary>
        /// Initializes a store that opens the group's configured store.
        /// </summary>
        public HardwarePendingCertificateKeyStore()
        {
        }

        /// <summary>
        /// Initializes a store that opens the device through an explicit provider.
        /// </summary>
        /// <param name="provider">
        /// The provider for the device's certificate store.
        /// </param>
        /// <remarks>
        /// <c>CertificateStoreIdentifier.OpenStore</c> resolves store types
        /// through the built-in set and the obsolete static registry, so it cannot
        /// see a provider registered through dependency injection. Supplying the
        /// provider here avoids that path entirely.
        /// </remarks>
        public HardwarePendingCertificateKeyStore(ICertificateStoreProvider provider)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns <c>false</c> when the key is not held in a device, so the
        /// caller can fall back to a store that knows how to protect exportable
        /// key material rather than this one silently mishandling it.
        /// </remarks>
        public async ValueTask<bool> SaveAsync(
            PendingCertificateKeyContext context,
            Certificate certificateWithPrivateKey,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (certificateWithPrivateKey is null)
            {
                throw new ArgumentNullException(nameof(certificateWithPrivateKey));
            }

            if (!certificateWithPrivateKey.HasDetachedPrivateKey)
            {
                return false;
            }

            using ICertificateStore? store = OpenStore(context);
            if (store == null || !store.SupportsLoadPrivateKey)
            {
                return false;
            }

            // Only the public certificate is written. The key stays where it was
            // generated, and the store can find it again by thumbprint.
            using (Certificate publicOnly = Certificate.FromRawData(certificateWithPrivateKey.RawData))
            {
                await store.AddAsync(publicOnly, null, cancellationToken).ConfigureAwait(false);
            }

            lock (m_lock)
            {
                m_pending[Scope(context)] = certificateWithPrivateKey.Thumbprint;
            }

            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<Certificate?> TryTakeAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            string? thumbprint;
            lock (m_lock)
            {
                if (!m_pending.TryGetValue(Scope(context), out thumbprint))
                {
                    return null;
                }
                m_pending.Remove(Scope(context));
            }

            using ICertificateStore? store = OpenStore(context);
            if (store == null)
            {
                return null;
            }

            char[]? password = context.PasswordProvider?.GetPassword(
                new CertificateIdentifier { Thumbprint = thumbprint });

            return await store
                .LoadPrivateKeyAsync(
                    thumbprint,
                    null,
                    null,
                    context.CertificateTypeId,
                    password,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (m_lock)
            {
                m_pending.Remove(Scope(context));
            }

            return default;
        }

        private ICertificateStore? OpenStore(PendingCertificateKeyContext context)
        {
            if (m_provider != null)
            {
                ICertificateStore store = m_provider.CreateStore(context.Telemetry);
                store.Open(context.BaseStore.StorePath ?? string.Empty, false);
                return store;
            }

            return context.BaseStore?.OpenStore(context.Telemetry);
        }

        private static (string, string) Scope(PendingCertificateKeyContext context)
        {
            return (
                context.CertificateGroupId.ToString(),
                context.CertificateTypeId.ToString());
        }

        private readonly Dictionary<(string, string), string> m_pending = [];
        private readonly Lock m_lock = new();
        private readonly ICertificateStoreProvider? m_provider;
    }
}
