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
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// Helper to build a signing X.509 <see cref="UserIdentity"/> from a
    /// transient <see cref="Certificate"/> that carries a private key.
    /// </summary>
    /// <remarks>
    /// In v1.6 the legacy <c>new UserIdentity(Certificate)</c> constructor was
    /// removed in favour of the provider-based
    /// <see cref="UserIdentity.CreateAsync"/>: an X.509 user identity resolves
    /// its private-key certificate on demand through an
    /// <see cref="ICertificateProvider"/>. To exercise real X.509 user-token
    /// activation (which requires the client to sign the server nonce with the
    /// user certificate's private key) these conformance tests persist the
    /// transient user certificate to a shared client-side directory store and
    /// build the identity from a <see cref="CertificateIdentifier"/> pointing at
    /// it, mirroring the production signing path.
    /// </remarks>
    internal static class X509UserIdentityHelper
    {
        /// <summary>
        /// Creates a signing X.509 <see cref="UserIdentity"/> for the supplied
        /// user certificate (which must expose a private key).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <c>null</c>.</exception>
        public static async Task<UserIdentity> CreateAsync(
            Certificate certificate,
            ITelemetryContext telemetry)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            EnsureProvider(telemetry);

            bool needStoreAdd;
            lock (s_lock)
            {
                needStoreAdd = s_addedThumbprints.Add(certificate.Thumbprint!);
            }
            if (needStoreAdd)
            {
                await certificate.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    s_storePath!,
                    password: null,
                    telemetry).ConfigureAwait(false);
            }

            var identifier = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = s_storePath,
                Thumbprint = certificate.Thumbprint
            };

            return await UserIdentity.CreateAsync(
                identifier,
                s_passwordProvider!,
                s_certificateManager!.CertificateProvider).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a signing X.509 <see cref="UserIdentity"/> from an
        /// <see cref="X509Certificate2"/> that carries a private key.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is <c>null</c>.</exception>
        public static async Task<UserIdentity> CreateAsync(
            X509Certificate2 certificate,
            ITelemetryContext telemetry)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            // Materialise an independent copy so the wrapping Certificate can be
            // disposed without disposing the caller-owned X509Certificate2.
            byte[] pfx = certificate.Export(X509ContentType.Pfx, ExportPassword);
            using X509Certificate2 copy = X509CertificateLoader.LoadPkcs12(
                pfx, ExportPassword, X509KeyStorageFlags.Exportable);
            using var wrapped = Certificate.From(copy);
            return await CreateAsync(wrapped, telemetry).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the shared certificate provider/store used for signing and
        /// removes the temporary directory store. Intended to be called from the
        /// assembly leak-detection teardown so the provider's private-key cache
        /// releases its certificate references.
        /// </summary>
        public static void DisposeSharedResources()
        {
            CertificateManager? manager;
            string? storePath;
            lock (s_lock)
            {
                manager = s_certificateManager;
                storePath = s_storePath;
                s_certificateManager = null;
                s_passwordProvider = null;
                s_storePath = null;
                s_addedThumbprints.Clear();
            }

            manager?.Dispose();

            if (!string.IsNullOrEmpty(storePath) && Directory.Exists(storePath))
            {
                try
                {
                    Directory.Delete(storePath, true);
                }
                catch (IOException)
                {
                    // best-effort cleanup of the transient store
                }
            }
        }

        private static void EnsureProvider(ITelemetryContext telemetry)
        {
            lock (s_lock)
            {
                if (s_certificateManager != null)
                {
                    return;
                }

                s_storePath = Path.Combine(
                    Path.GetTempPath(),
                    "opcua-x509user-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(s_storePath);
                s_passwordProvider = new CertificatePasswordProvider();
                // Process-lifetime test infrastructure: the manager owns the
                // certificate provider used for on-demand private-key signing
                // and is intentionally not disposed for the duration of the run.
                s_certificateManager = new CertificateManager(telemetry);
            }
        }

        private static readonly Lock s_lock = new();
        private static readonly HashSet<string> s_addedThumbprints = new(StringComparer.OrdinalIgnoreCase);
        private const string ExportPassword = "test";
        private static string? s_storePath;
        private static CertificatePasswordProvider? s_passwordProvider;
        private static CertificateManager? s_certificateManager;
    }
}
