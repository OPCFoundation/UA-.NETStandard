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

#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stateless helpers that resolve a <see cref="CertificateIdentifier"/>
    /// into a <see cref="Certificate"/> without mutating the identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Historically <see cref="CertificateIdentifier"/> cached the most
    /// recently loaded <see cref="Certificate"/> on its
    /// <c>m_certificate</c> field, which forced the type to implement
    /// <see cref="System.IDisposable"/> and produced subtle staleness bugs
    /// (the cache could survive a delete-and-replace in the underlying
    /// store). The authoritative cache for application certificates is the
    /// <see cref="ICertificateRegistry"/> (a.k.a.
    /// <c>CertificateManager.m_applicationCertificates</c>); identifier-
    /// level caching duplicated it.
    /// </para>
    /// <para>
    /// This resolver concentrates the lookup in one place and never
    /// mutates the identifier. Each method returns an
    /// <see cref="Certificate.AddRef"/>'d certificate; the caller owns the
    /// resulting reference and is responsible for disposing it.
    /// </para>
    /// </remarks>
    public static class CertificateIdentifierResolver
    {
        /// <summary>
        /// Resolves a <see cref="CertificateIdentifier"/> to a
        /// <see cref="Certificate"/>.
        /// </summary>
        /// <remarks>
        /// Resolution order:
        /// <list type="number">
        /// <item><description>
        /// If <paramref name="registry"/> is supplied and the identifier
        /// has a non-empty <see cref="CertificateIdentifier.Thumbprint"/>,
        /// the registry's
        /// <see cref="ICertificateRegistry.ApplicationCertificates"/> are
        /// scanned for a thumbprint match. The borrowed entry's certificate
        /// is <see cref="Certificate.AddRef"/>'d and returned.
        /// </description></item>
        /// <item><description>
        /// Otherwise the identifier's
        /// <see cref="CertificateIdentifier.RawData"/> bytes are
        /// materialised when present.
        /// </description></item>
        /// <item><description>
        /// Otherwise the identifier's underlying
        /// <see cref="ICertificateStore"/> is opened (via
        /// <see cref="CertificateStoreIdentifier"/>) and a matching
        /// certificate is located by thumbprint, subject, or
        /// applicationUri.
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <param name="identifier">The identifier to resolve.</param>
        /// <param name="registry">
        /// Optional registry (typically the application's
        /// <see cref="ICertificateRegistry"/>) consulted before any store
        /// access.
        /// </param>
        /// <param name="needPrivateKey">
        /// When <see langword="true"/>, only certificates that carry a
        /// private key are returned.
        /// </param>
        /// <param name="applicationUri">
        /// Optional application URI used as a final fallback when neither
        /// thumbprint nor subject narrows the lookup.
        /// </param>
        /// <param name="telemetry">Telemetry context used for store I/O.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// An <see cref="Certificate.AddRef"/>'d certificate, or
        /// <see langword="null"/> when no match is found. The caller owns
        /// the reference and must dispose it.
        /// </returns>
        public static async Task<Certificate?> ResolveAsync(
            CertificateIdentifier identifier,
            ICertificateRegistry? registry = null,
            bool needPrivateKey = false,
            string? applicationUri = null,
            ITelemetryContext? telemetry = null,
            CancellationToken ct = default)
        {
            if (identifier == null)
            {
                return null;
            }

            // 1) Registry lookup by thumbprint.
            if (registry != null && !string.IsNullOrEmpty(identifier.Thumbprint))
            {
                foreach (CertificateEntry entry in registry.ApplicationCertificates)
                {
                    if (string.Equals(
                            entry.Certificate.Thumbprint,
                            identifier.Thumbprint,
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (!needPrivateKey || entry.Certificate.HasPrivateKey)
                        {
                            return entry.Certificate.AddRef();
                        }
                    }
                }
            }

            // 2) Inline raw data.
            if (identifier.RawData != null && identifier.RawData.Length > 0)
            {
                Certificate inline = Certificate.FromRawData(identifier.RawData);
                if (!needPrivateKey || inline.HasPrivateKey)
                {
                    return inline;
                }
                inline.Dispose();
            }

            // 3) Open the identifier's store and search.
            using ICertificateStore? store = OpenStore(identifier, telemetry);
            if (store == null)
            {
                return null;
            }

            using CertificateCollection collection = await store.EnumerateAsync(ct)
                .ConfigureAwait(false);

            return CertificateIdentifier.Find(
                collection,
                identifier.Thumbprint,
                identifier.SubjectName,
                applicationUri,
                identifier.CertificateType,
                needPrivateKey);
        }

        /// <summary>
        /// Loads the private-key-bearing <see cref="Certificate"/> for the
        /// identifier from its underlying store.
        /// </summary>
        /// <remarks>
        /// Mirrors the legacy
        /// <see cref="CertificateIdentifier.LoadPrivateKeyExAsync(ICertificatePasswordProvider?, string?, ITelemetryContext?, CancellationToken)"/>
        /// semantics — including password-provider support, the X509Store
        /// fallback to <c>FindAsync(true)</c>, and the applicationUri
        /// fallback when the subject changed — but does not mutate the
        /// identifier. The returned certificate is owned by the caller.
        /// </remarks>
        /// <param name="identifier">The identifier to load the key for.</param>
        /// <param name="passwordProvider">
        /// Optional provider used to obtain the PFX password.
        /// </param>
        /// <param name="applicationUri">
        /// Optional application URI used as a fallback when the thumbprint
        /// match fails (e.g. after the certificate was rotated).
        /// </param>
        /// <param name="telemetry">Telemetry context used for store I/O.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// An <see cref="Certificate.AddRef"/>'d certificate carrying the
        /// private key, or <see langword="null"/> when no match exists.
        /// </returns>
        public static async Task<Certificate?> LoadPrivateKeyAsync(
            CertificateIdentifier identifier,
            ICertificatePasswordProvider? passwordProvider = null,
            string? applicationUri = null,
            ITelemetryContext? telemetry = null,
            CancellationToken ct = default)
        {
            if (identifier == null)
            {
                return null;
            }

            if (identifier.StoreType != CertificateStoreType.X509Store)
            {
                using ICertificateStore? store = OpenStore(identifier, telemetry);
                if (store?.SupportsLoadPrivateKey != true)
                {
                    return null;
                }

                char[]? password = passwordProvider?.GetPassword(identifier);

                Certificate? cert = await store.LoadPrivateKeyAsync(
                        identifier.Thumbprint!,
                        identifier.SubjectName!,
                        applicationUri: null!,
                        identifier.CertificateType,
                        password!,
                        ct)
                    .ConfigureAwait(false);

                // Find by applicationUri when subject changed (post-rotation).
                if (cert == null && !string.IsNullOrEmpty(applicationUri))
                {
                    cert = await store.LoadPrivateKeyAsync(
                            identifier.Thumbprint!,
                            subjectName: null!,
                            applicationUri!,
                            identifier.CertificateType,
                            password!,
                            ct)
                        .ConfigureAwait(false);
                }

                // Last-chance: drop the (possibly stale) thumbprint and search
                // by applicationUri only. Rotations that replaced the
                // certificate under the configured identifier — where the
                // configured thumbprint no longer matches anything in the
                // store — would otherwise return null even though the new
                // certificate is present and matches the application URI.
                if (cert == null && !string.IsNullOrEmpty(applicationUri))
                {
                    cert = await store.LoadPrivateKeyAsync(
                            thumbprint: null!,
                            subjectName: null!,
                            applicationUri!,
                            identifier.CertificateType,
                            password!,
                            ct)
                        .ConfigureAwait(false);
                }

                return cert;
            }

            // X509Store: fall through to a registry-less store search that
            // requires a private key.
            return await ResolveAsync(
                    identifier,
                    registry: null,
                    needPrivateKey: true,
                    applicationUri: applicationUri,
                    telemetry: telemetry,
                    ct: ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Opens the <see cref="ICertificateStore"/> referenced by the
        /// identifier's <see cref="CertificateIdentifier.StoreType"/> and
        /// <see cref="CertificateIdentifier.StorePath"/>.
        /// </summary>
        /// <param name="identifier">The identifier whose store to open.</param>
        /// <param name="telemetry">Telemetry context to use.</param>
        /// <returns>
        /// The opened store, or <see langword="null"/> when the identifier
        /// has no usable store metadata.
        /// </returns>
        public static ICertificateStore? OpenStore(
            CertificateIdentifier identifier,
            ITelemetryContext? telemetry)
        {
            if (identifier == null || string.IsNullOrEmpty(identifier.StorePath))
            {
                return null;
            }

            var storeIdentifier = string.IsNullOrEmpty(identifier.StoreType)
                ? new CertificateStoreIdentifier(identifier.StorePath, false)
                : new CertificateStoreIdentifier(
                    identifier.StorePath,
                    identifier.StoreType!,
                    false);

            return storeIdentifier.OpenStore(telemetry);
        }
    }
}
