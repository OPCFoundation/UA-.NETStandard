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
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.TestFramework
{
    /// <summary>
    /// A certificate store that behaves like a hardware security token: it holds
    /// certificates whose private keys can be used but never extracted.
    /// </summary>
    /// <remarks>
    /// Real tokens differ from each other in how they are addressed and how a
    /// session is opened, but they agree on the part that matters to the stack:
    /// the store hands back a certificate whose private key is a handle rather
    /// than key material, and refuses to give the key up. This store reproduces
    /// exactly that contract, in memory, on every platform, so the code paths a
    /// TPM, an HSM, a PKCS#11 token or a remote key service exercise can be
    /// covered by ordinary tests.
    /// <para>
    /// Register it through <c>CertificateManagerOptions.AddStoreProvider</c> with
    /// <see cref="SimulatedHardwareCertificateStoreProvider"/>, or construct it
    /// directly. Store paths use the <c>simhw:</c> scheme.
    /// </para>
    /// </remarks>
    public sealed class SimulatedHardwareCertificateStore : ICertificateStore
    {
        /// <summary>
        /// The store type name used to select this store.
        /// </summary>
        public const string StoreTypeName = "SimulatedHardware";

        /// <summary>
        /// The store path prefix that identifies a simulated hardware store.
        /// </summary>
        public const string StoreScheme = "simhw:";

        /// <inheritdoc/>
        public string StoreType => StoreTypeName;

        /// <inheritdoc/>
        public string StorePath => m_storePath;

        /// <inheritdoc/>
        /// <remarks>
        /// The store holds private keys, but they are never extractable.
        /// </remarks>
        public bool NoPrivateKeys => false;

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => true;

        /// <inheritdoc/>
        public bool SupportsCRLs => false;

        /// <summary>
        /// Gets the number of times a caller attempted to persist a private key.
        /// </summary>
        /// <remarks>
        /// A token cannot accept key material it did not generate, so these
        /// attempts are recorded rather than performed. Tests assert on this to
        /// prove the stack does not silently rely on writing keys back.
        /// </remarks>
        public int RejectedPrivateKeyWrites => m_rejectedPrivateKeyWrites;

        /// <inheritdoc/>
        public void Open(string location, bool noPrivateKeys = true)
        {
            m_storePath = location ?? string.Empty;
        }

        /// <inheritdoc/>
        public void Close()
        {
        }

        /// <summary>
        /// Generates a key pair inside the simulated token and stores a self
        /// signed certificate for it.
        /// </summary>
        /// <param name="subjectName">The subject name of the certificate.</param>
        /// <param name="keySizeInBits">The RSA key size.</param>
        /// <returns>The stored certificate, including its non extractable key.</returns>
        /// <remarks>
        /// The certificate is signed by the key that is about to become non
        /// extractable, which mirrors a token generating a key and issuing a self
        /// signed certificate for it before the key is locked down.
        /// </remarks>
        public Certificate CreateRsaCertificate(string subjectName, int keySizeInBits = 2048)
        {
            RSA softwareKey = RSA.Create(keySizeInBits);
            var request = new CertificateRequest(
                subjectName, softwareKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            Certificate stored = publicOnly.CopyWithDetachedPrivateKey(
                new NonExportableRsa(softwareKey, ownsKey: true));

            Store(stored);
            return stored.AddRef();
        }

        /// <summary>
        /// Generates an elliptic curve key pair inside the simulated token and
        /// stores a self signed certificate for it.
        /// </summary>
        /// <param name="subjectName">The subject name of the certificate.</param>
        /// <param name="curve">The named curve to use.</param>
        /// <returns>The stored certificate, including its non extractable key.</returns>
        public Certificate CreateEcdsaCertificate(string subjectName, ECCurve curve)
        {
            ECDsa softwareKey = ECDsa.Create(curve);
            var request = new CertificateRequest(subjectName, softwareKey, HashAlgorithmName.SHA256);
            using X509Certificate2 selfSigned = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

            using Certificate publicOnly = Certificate.FromRawData(selfSigned.RawData);
            Certificate stored = publicOnly.CopyWithDetachedPrivateKey(
                new NonExportableECDsa(softwareKey, ownsKey: true));

            Store(stored);
            return stored.AddRef();
        }

        /// <inheritdoc/>
        public Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
        {
            var results = new CertificateCollection();
            lock (m_lock)
            {
                foreach (Certificate certificate in m_certificates.Values)
                {
                    results.Add(Certificate.FromRawData(certificate.RawData));
                }
            }
            return Task.FromResult(results);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Only the public certificate is retained. A token cannot import key
        /// material it did not generate, so a private key offered here is
        /// recorded as rejected rather than stored.
        /// </remarks>
        public Task AddAsync(
            Certificate certificate,
            char[]? password = null,
            CancellationToken ct = default)
        {
            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            if (certificate.HasPrivateKey)
            {
                Interlocked.Increment(ref m_rejectedPrivateKeyWrites);
            }

            lock (m_lock)
            {
                // A token does not discard a key it generated because someone
                // handed it the matching public certificate. Only genuinely new
                // certificates are taken.
                if (m_certificates.TryGetValue(certificate.Thumbprint, out Certificate? existing) &&
                    existing.HasPrivateKey)
                {
                    return Task.CompletedTask;
                }
            }

            Store(Certificate.FromRawData(certificate.RawData));
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task AddRejectedAsync(
            CertificateCollection certificates,
            int maxCertificates,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
        {
            lock (m_lock)
            {
                if (m_certificates.TryGetValue(thumbprint, out Certificate? certificate))
                {
                    m_certificates.Remove(thumbprint);
                    certificate.Dispose();
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        /// <inheritdoc/>
        public Task<CertificateCollection> FindByThumbprintAsync(
            string thumbprint,
            CancellationToken ct = default)
        {
            var results = new CertificateCollection();
            lock (m_lock)
            {
                if (thumbprint != null &&
                    m_certificates.TryGetValue(thumbprint, out Certificate? certificate))
                {
                    results.Add(Certificate.FromRawData(certificate.RawData));
                }
            }
            return Task.FromResult(results);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Returns the certificate with its key handle attached in detached form.
        /// The key itself never leaves the simulated token.
        /// </remarks>
        public Task<Certificate?> LoadPrivateKeyAsync(
            string thumbprint,
            string? subjectName,
            string? applicationUri,
            NodeId certificateType,
            char[]? password,
            CancellationToken ct = default)
        {
            lock (m_lock)
            {
                foreach (Certificate certificate in m_certificates.Values)
                {
                    if (!certificate.HasPrivateKey)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(thumbprint) &&
                        !string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(subjectName) &&
                        !X509Utils.CompareDistinguishedName(certificate.Subject, subjectName) &&
                        !certificate.Subject.Contains(subjectName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return Task.FromResult<Certificate?>(certificate.AddRef());
                }
            }

            return Task.FromResult<Certificate?>(null);
        }

        /// <inheritdoc/>
        public Task<StatusCode> IsRevokedAsync(
            Certificate issuer,
            Certificate certificate,
            CancellationToken ct = default)
        {
            return Task.FromResult((StatusCode)StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLsAsync(
            Certificate issuer,
            bool validateUpdateTime = true,
            CancellationToken ct = default)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new NotSupportedException("The simulated hardware store does not support CRLs.");
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
        {
            throw new NotSupportedException("The simulated hardware store does not support CRLs.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                foreach (Certificate certificate in m_certificates.Values)
                {
                    certificate.Dispose();
                }
                m_certificates.Clear();
            }
        }

        private void Store(Certificate certificate)
        {
            lock (m_lock)
            {
                if (m_certificates.TryGetValue(certificate.Thumbprint, out Certificate? existing))
                {
                    existing.Dispose();
                }
                m_certificates[certificate.Thumbprint] = certificate;
            }
        }

        private readonly Dictionary<string, Certificate> m_certificates = [];
        private readonly Lock m_lock = new();
        private string m_storePath = string.Empty;
        private int m_rejectedPrivateKeyWrites;
    }

    /// <summary>
    /// Creates <see cref="SimulatedHardwareCertificateStore"/> instances for the
    /// <c>simhw:</c> store scheme.
    /// </summary>
    /// <remarks>
    /// Instances are cached per store path so that a certificate generated in the
    /// simulated token stays visible to every component that opens the same path,
    /// which is how a real token behaves.
    /// </remarks>
    public sealed class SimulatedHardwareCertificateStoreProvider : ICertificateStoreProvider
    {
        /// <inheritdoc/>
        public string StoreTypeName => SimulatedHardwareCertificateStore.StoreTypeName;

        /// <inheritdoc/>
        public bool SupportsStorePath(string storePath)
        {
            return storePath != null &&
                storePath.StartsWith(
                    SimulatedHardwareCertificateStore.StoreScheme,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ICertificateStore CreateStore(ITelemetryContext telemetry)
        {
            return new SharedStoreHandle(this);
        }

        /// <summary>
        /// Returns the store backing a path, creating it on first use.
        /// </summary>
        /// <param name="storePath">The store path.</param>
        /// <returns>The store for that path.</returns>
        public SimulatedHardwareCertificateStore GetStore(string storePath)
        {
            lock (m_lock)
            {
                if (!m_stores.TryGetValue(storePath, out SimulatedHardwareCertificateStore? store))
                {
                    store = new SimulatedHardwareCertificateStore();
                    store.Open(storePath, false);
                    m_stores[storePath] = store;
                }
                return store;
            }
        }

        private readonly Dictionary<string, SimulatedHardwareCertificateStore> m_stores = [];
        private readonly Lock m_lock = new();

        /// <summary>
        /// Delegates to the shared store for whichever path it is opened with.
        /// </summary>
        /// <remarks>
        /// The stack opens and disposes stores freely, so a handle must not take
        /// the backing token down with it.
        /// </remarks>
        private sealed class SharedStoreHandle : ICertificateStore
        {
            public SharedStoreHandle(SimulatedHardwareCertificateStoreProvider owner)
            {
                m_owner = owner;
            }

            public string StoreType => SimulatedHardwareCertificateStore.StoreTypeName;

            public string StorePath => m_storePath;

            public bool NoPrivateKeys => false;

            public bool SupportsLoadPrivateKey => true;

            public bool SupportsCRLs => false;

            public void Open(string location, bool noPrivateKeys = true)
            {
                m_storePath = location ?? string.Empty;
                m_store = m_owner.GetStore(m_storePath);
            }

            public void Close()
            {
            }

            public Task<CertificateCollection> EnumerateAsync(CancellationToken ct = default)
                => Store.EnumerateAsync(ct);

            public Task AddAsync(Certificate certificate, char[]? password = null, CancellationToken ct = default)
                => Store.AddAsync(certificate, password, ct);

            public Task AddRejectedAsync(CertificateCollection certificates, int maxCertificates, CancellationToken ct = default)
                => Store.AddRejectedAsync(certificates, maxCertificates, ct);

            public Task<bool> DeleteAsync(string thumbprint, CancellationToken ct = default)
                => Store.DeleteAsync(thumbprint, ct);

            public Task<CertificateCollection> FindByThumbprintAsync(string thumbprint, CancellationToken ct = default)
                => Store.FindByThumbprintAsync(thumbprint, ct);

            public Task<Certificate?> LoadPrivateKeyAsync(
                string thumbprint,
                string? subjectName,
                string? applicationUri,
                NodeId certificateType,
                char[]? password,
                CancellationToken ct = default)
                => Store.LoadPrivateKeyAsync(
                    thumbprint, subjectName, applicationUri, certificateType, password, ct);

            public Task<StatusCode> IsRevokedAsync(Certificate issuer, Certificate certificate, CancellationToken ct = default)
                => Store.IsRevokedAsync(issuer, certificate, ct);

            public Task<X509CRLCollection> EnumerateCRLsAsync(CancellationToken ct = default)
                => Store.EnumerateCRLsAsync(ct);

            public Task<X509CRLCollection> EnumerateCRLsAsync(Certificate issuer, bool validateUpdateTime = true, CancellationToken ct = default)
                => Store.EnumerateCRLsAsync(issuer, validateUpdateTime, ct);

            public Task AddCRLAsync(X509CRL crl, CancellationToken ct = default)
                => Store.AddCRLAsync(crl, ct);

            public Task<bool> DeleteCRLAsync(X509CRL crl, CancellationToken ct = default)
                => Store.DeleteCRLAsync(crl, ct);

            public void Dispose()
            {
                // The backing token outlives this handle by design.
            }

            private SimulatedHardwareCertificateStore Store
                => m_store ?? throw new InvalidOperationException("The store has not been opened.");

            private readonly SimulatedHardwareCertificateStoreProvider m_owner;

            // CA2213: the handle deliberately does not own the store. A real token
            // is not torn down because one consumer closed its session, and the
            // stack opens and disposes stores freely. The provider owns the store.
#pragma warning disable CA2213
            private SimulatedHardwareCertificateStore? m_store;
#pragma warning restore CA2213

            private string m_storePath = string.Empty;
        }
    }
}
