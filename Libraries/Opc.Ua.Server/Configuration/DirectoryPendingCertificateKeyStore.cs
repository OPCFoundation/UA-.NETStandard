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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Production-default <see cref="IPendingCertificateKeyStore"/>. Persists
    /// the pending private key using the same <see cref="ICertificateStore"/>
    /// mechanism used for every other certificate in the stack: a dedicated
    /// directory certificate store, one exclusive sub-folder per
    /// (certificate group, certificate type) scope, protected with the
    /// configured <see cref="ICertificatePasswordProvider"/> exactly like the
    /// active application certificate.
    /// </summary>
    /// <remarks>
    /// Only directory-backed certificate stores support a private
    /// sub-scope. When the certificate group's configured application
    /// certificate store is not a <see cref="CertificateStoreType.Directory"/>
    /// store (for example, a platform <see cref="CertificateStoreType.X509Store"/>),
    /// <see cref="SaveAsync"/> returns <see langword="false"/> so the caller
    /// can reject <c>regeneratePrivateKey=true</c> with
    /// <see cref="StatusCodes.BadNotSupported"/> instead of silently keeping
    /// the key only in memory.
    /// </remarks>
    public sealed class DirectoryPendingCertificateKeyStore : IPendingCertificateKeyStore
    {
        private const string PendingFolderName = "pending";

        /// <inheritdoc/>
        public async ValueTask<bool> SaveAsync(
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

            CertificateStoreIdentifier? pendingStore = TryCreatePendingStoreIdentifier(context);
            if (pendingStore == null)
            {
                return false;
            }

            using ICertificateStore store = pendingStore.OpenStore(context.Telemetry);
            await ClearAsync(store, cancellationToken).ConfigureAwait(false);

            char[]? password = context.PasswordProvider?.GetPassword(
                CreatePasswordIdentifier(context, pendingStore));
            await store.AddAsync(certificateWithPrivateKey, password, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc/>
        public async ValueTask<Certificate?> TryTakeAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            CertificateStoreIdentifier? pendingStore = TryCreatePendingStoreIdentifier(context);
            if (pendingStore == null)
            {
                return null;
            }

            using ICertificateStore store = pendingStore.OpenStore(context.Telemetry);
            using CertificateCollection entries = await store.EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);

            Certificate? pendingEntry = null;
            foreach (Certificate entry in entries)
            {
                pendingEntry = entry;
                break;
            }

            if (pendingEntry == null)
            {
                return null;
            }

            try
            {
                char[]? password = context.PasswordProvider?.GetPassword(
                    CreatePasswordIdentifier(context, pendingStore));

                // NodeId.Null bypasses LoadPrivateKeyAsync's certificate-type
                // cryptographic re-validation: this store is already
                // exclusively scoped to the (certificate group, certificate
                // type) pair via its dedicated sub-folder (see
                // ComputeScopeFolderName), so matching by thumbprint alone
                // is unambiguous and does not depend on the regenerated
                // certificate's signature algorithm happening to satisfy
                // CertificateIdentifier.ValidateCertificateType for the
                // caller-supplied type.
                Certificate? withPrivateKey = await store.LoadPrivateKeyAsync(
                    pendingEntry.Thumbprint,
                    pendingEntry.Subject,
                    applicationUri: null,
                    NodeId.Null,
                    password,
                    cancellationToken).ConfigureAwait(false);

                await store.DeleteAsync(pendingEntry.Thumbprint, cancellationToken)
                    .ConfigureAwait(false);
                return withPrivateKey;
            }
            finally
            {
                pendingEntry.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            PendingCertificateKeyContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            CertificateStoreIdentifier? pendingStore = TryCreatePendingStoreIdentifier(context);
            if (pendingStore == null)
            {
                return;
            }

            using ICertificateStore store = pendingStore.OpenStore(context.Telemetry);
            await ClearAsync(store, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask ClearAsync(ICertificateStore store, CancellationToken cancellationToken)
        {
            using CertificateCollection entries = await store.EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (Certificate entry in entries)
            {
                try
                {
                    await store.DeleteAsync(entry.Thumbprint, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    entry.Dispose();
                }
            }
        }

        private static CertificateStoreIdentifier? TryCreatePendingStoreIdentifier(
            PendingCertificateKeyContext context)
        {
            CertificateStoreIdentifier baseStore = context.BaseStore;
            if (baseStore == null ||
                string.IsNullOrEmpty(baseStore.StorePath) ||
                !string.Equals(baseStore.StoreType, CertificateStoreType.Directory, StringComparison.Ordinal))
            {
                // Only directory-backed application certificate stores can
                // host a dedicated, file-system-protected pending-key
                // sub-store. Platform stores (X509Store) and other custom
                // store types do not support a private sub-scope.
                return null;
            }

            string scope = ComputeScopeFolderName(context.CertificateGroupId, context.CertificateTypeId);
            string pendingPath = Path.Combine(baseStore.StorePath, PendingFolderName, scope);
            return new CertificateStoreIdentifier(pendingPath, CertificateStoreType.Directory, noPrivateKeys: false);
        }

        private static CertificateIdentifier CreatePasswordIdentifier(
            PendingCertificateKeyContext context,
            CertificateStoreIdentifier pendingStore)
        {
            return new CertificateIdentifier
            {
                StorePath = pendingStore.StorePath,
                StoreType = pendingStore.StoreType,
                CertificateType = context.CertificateTypeId
            };
        }

        /// <summary>
        /// Derives a stable, filesystem-safe folder name for the
        /// (certificate group, certificate type) scope so each scope gets
        /// its own exclusive sub-store without relying on encoding a
        /// <see cref="NodeId"/>'s textual form into a path segment.
        /// </summary>
        private static string ComputeScopeFolderName(NodeId certificateGroupId, NodeId certificateTypeId)
        {
            string key = certificateGroupId.ToString() + "|" + certificateTypeId.ToString();
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(keyBytes);
#else
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(keyBytes);
#endif
            return Utils.ToHexString(hash)[..16];
        }
    }
}
