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
    /// the device already is the durable store. What has to survive a restart is
    /// the association between the pending certificate and its scope, and that is
    /// kept where the contract says it should be - in a storage location derived
    /// from the group's store rather than mixed into the active certificates.
    /// Holding it in memory would not survive the restart the contract requires,
    /// so nothing is held in memory at all: the pending entry is exactly what is
    /// present at that location, and the key is re-attached on the way back by
    /// asking the device to load it.
    /// </para>
    /// <para>
    /// A store that cannot hold or later remove such an entry - a raw PKCS#11
    /// token, for instance, whose objects are provisioned out of band - cannot
    /// stage anything durably, so <see cref="SaveAsync"/> declines rather than
    /// claiming a persistence it does not provide.
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

            using (ICertificateStore? device = OpenDeviceStore(context))
            {
                // The key has to be loadable back from where it lives, or there
                // is nothing durable to stage a reference to.
                if (device == null || !device.SupportsLoadPrivateKey)
                {
                    return false;
                }
            }

            using ICertificateStore? staging = OpenStagingStore(context);
            if (staging == null)
            {
                return false;
            }

            // Replace whatever was staged for this scope before, per the
            // contract's one-entry-per-scope rule.
            if (!await TryClearAsync(staging, cancellationToken).ConfigureAwait(false))
            {
                // Without removal the entry could never be consumed or replaced,
                // so this store cannot stage durably for this base store.
                return false;
            }

            // Only the public certificate is written. The key stays where it was
            // generated, and the device can find it again by thumbprint.
            using (Certificate publicOnly = Certificate.FromRawData(certificateWithPrivateKey.RawData))
            {
                await staging.AddAsync(publicOnly, null, cancellationToken).ConfigureAwait(false);
            }

            // Prove the association is actually readable back, so a store that
            // silently dropped it cannot be reported as durable.
            using CertificateCollection staged = await staging
                .FindByThumbprintAsync(certificateWithPrivateKey.Thumbprint, cancellationToken)
                .ConfigureAwait(false);

            return staged.Count > 0;
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

            using ICertificateStore? staging = OpenStagingStore(context);
            if (staging == null)
            {
                return null;
            }

            string? thumbprint = null;

            using (CertificateCollection staged = await staging
                .EnumerateAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (Certificate candidate in staged)
                {
                    thumbprint = candidate.Thumbprint;
                    break;
                }
            }

            if (thumbprint == null)
            {
                return null;
            }

            char[]? password = context.PasswordProvider?.GetPassword(
                new CertificateIdentifier { Thumbprint = thumbprint });

            // The association lives in the staging location, but the key itself
            // never left the device, so it is loaded from the group's own store.
            Certificate? pending = null;

            using (ICertificateStore? device = OpenDeviceStore(context))
            {
                if (device != null)
                {
                    pending = await device
                        .LoadPrivateKeyAsync(
                            thumbprint,
                            null,
                            null,
                            context.CertificateTypeId,
                            password,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // Consume it: the contract hands a pending key out exactly once.
            await staging.DeleteAsync(thumbprint, cancellationToken).ConfigureAwait(false);

            return pending;
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            using ICertificateStore? staging = OpenStagingStore(context);
            if (staging == null)
            {
                return;
            }

            await TryClearAsync(staging, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Empties the staging location, reporting whether removal is possible.
        /// </summary>
        /// <param name="staging">The staging store.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// <c>false</c> when the store cannot remove entries, which means it
        /// cannot be used for staging at all.
        /// </returns>
        private static async ValueTask<bool> TryClearAsync(
            ICertificateStore staging,
            CancellationToken cancellationToken)
        {
            var thumbprints = new List<string>();

            using (CertificateCollection staged = await staging
                .EnumerateAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (Certificate candidate in staged)
                {
                    thumbprints.Add(candidate.Thumbprint);
                }
            }

            foreach (string thumbprint in thumbprints)
            {
                try
                {
                    await staging.DeleteAsync(thumbprint, cancellationToken).ConfigureAwait(false);
                }
                catch (NotSupportedException)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Opens the storage location this scope stages into.
        /// </summary>
        /// <param name="context">The scope to stage for.</param>
        /// <returns>The store, or <c>null</c> when none can be opened.</returns>
        /// <remarks>
        /// The location is derived from the group's store rather than being the
        /// group's store, so a staged certificate is never mistaken for an active
        /// one and consuming it cannot disturb the certificates in use.
        /// </remarks>
        private ICertificateStore? OpenStagingStore(PendingCertificateKeyContext context)
        {
            return OpenAt(context, StagingPath(context.BaseStore?.StorePath ?? string.Empty, context));
        }

        /// <summary>
        /// Opens the group's own store, where the device holds the key.
        /// </summary>
        /// <param name="context">The scope being staged for.</param>
        /// <returns>The store, or <c>null</c> when none can be opened.</returns>
        private ICertificateStore? OpenDeviceStore(PendingCertificateKeyContext context)
        {
            return OpenAt(context, context.BaseStore?.StorePath ?? string.Empty);
        }

        private ICertificateStore? OpenAt(PendingCertificateKeyContext context, string path)
        {
            if (m_provider != null)
            {
                ICertificateStore store = m_provider.CreateStore(context.Telemetry);
                store.Open(path, false);
                return store;
            }

            if (context.BaseStore?.StoreType == null)
            {
                return null;
            }

            ICertificateStore? baseStore = CertificateStoreIdentifier.CreateStore(
                context.BaseStore.StoreType, context.Telemetry);

            if (baseStore == null)
            {
                return null;
            }

            baseStore.Open(path, false);
            return baseStore;
        }

        private static string StagingPath(string basePath, PendingCertificateKeyContext context)
        {
            string separator = basePath.Contains('\\', StringComparison.Ordinal) &&
                !basePath.Contains('/', StringComparison.Ordinal) ? "\\" : "/";

            return string.Concat(
                basePath.TrimEnd('/', '\\'),
                separator,
                "pending",
                separator,
                Sanitize(context.CertificateGroupId.ToString()),
                separator,
                Sanitize(context.CertificateTypeId.ToString()));
        }

        /// <summary>
        /// Reduces a NodeId to characters every store path accepts.
        /// </summary>
        /// <param name="value">The value to sanitize.</param>
        /// <returns>The sanitized value.</returns>
        private static string Sanitize(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length);

            foreach (char character in value)
            {
                builder.Append(
                    char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
            }

            return builder.ToString();
        }

        private readonly ICertificateStoreProvider? m_provider;
    }
}
