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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Wraps an <see cref="X509Certificate2"/> providing a managed
    /// lifetime with reference counting and implementing
    /// <see cref="IX509Certificate"/>.
    /// </summary>
    /// <remarks>
    /// The inner <see cref="X509Certificate2"/> is disposed only when
    /// the last reference is released. Use <see cref="AddRef"/> to
    /// increment the reference count before sharing, and
    /// <see cref="Dispose()"/> to decrement it.
    /// </remarks>
    public class Certificate : IX509Certificate, IDisposable, IEquatable<Certificate>
    {
        /// <summary>
        /// Creates a public-key-only certificate from DER or PEM encoded data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        public Certificate(byte[] rawData)
        {
            m_core = new CertificateCore(
                X509CertificateLoader.LoadCertificate(rawData));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Creates a public-key-only certificate from DER or PEM encoded data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        public Certificate(ReadOnlySpan<byte> rawData)
        {
            m_core = new CertificateCore(
                X509CertificateLoader.LoadCertificate(rawData));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }
#endif

        /// <summary>
        /// Creates a public-key-only certificate from a file.
        /// </summary>
        /// <param name="fileName">
        /// The path to a file containing DER or PEM encoded certificate data.
        /// </param>
        /// <remarks>
        /// The file is read before it is parsed rather than handing the path to
        /// the platform loader. On Windows that loader reaches CryptoAPI, which
        /// is not long-path aware, so a certificate sitting deeper than
        /// <c>MAX_PATH</c> fails with <c>CryptographicException: The system
        /// cannot find the path specified</c> even though the directory
        /// enumeration that produced the path succeeded.
        /// </remarks>
        public Certificate(string fileName)
        {
            m_core = new CertificateCore(
                X509CertificateLoader.LoadCertificate(File.ReadAllBytes(fileName)));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

        /// <summary>
        /// Creates a certificate from PKCS#12 encoded data with a password.
        /// </summary>
        /// <param name="rawData">The PKCS#12 encoded certificate data.</param>
        /// <param name="password">The password for the PKCS#12 data.</param>
        /// <param name="keyStorageFlags">
        /// The storage flags to use when loading the certificate.
        /// </param>
        public Certificate(
            byte[] rawData,
            ReadOnlySpan<char> password,
            X509KeyStorageFlags keyStorageFlags = default)
        {
            m_core = new CertificateCore(X509CertificateLoader.LoadPkcs12(
                rawData, password, keyStorageFlags));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

        /// <summary>
        /// Creates a certificate from a PKCS#12 file with a password.
        /// </summary>
        /// <param name="fileName">The path to the PKCS#12 file.</param>
        /// <param name="password">The password for the PKCS#12 file.</param>
        /// <param name="keyStorageFlags">
        /// The storage flags to use when loading the certificate.
        /// </param>
        /// <remarks>
        /// Reads the file rather than passing the path to the platform loader,
        /// for the same long-path reason described on
        /// <see cref="Certificate(string)"/>.
        /// </remarks>
        public Certificate(
            string fileName,
            ReadOnlySpan<char> password,
            X509KeyStorageFlags keyStorageFlags = default)
        {
            m_core = new CertificateCore(X509CertificateLoader.LoadPkcs12(
                File.ReadAllBytes(fileName), password, keyStorageFlags));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

        /// <summary>
        /// Private constructor that takes ownership of the provided
        /// <see cref="X509Certificate2"/> instance.
        /// </summary>
        /// <param name="certificate">
        /// The certificate to wrap. Must not be <c>null</c>.
        /// </param>
        private Certificate(X509Certificate2 certificate)
        {
            m_core = new CertificateCore(certificate ??
                throw new ArgumentNullException(nameof(certificate)));
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

        /// <summary>
        /// Private constructor that takes ownership of the provided
        /// <see cref="X509Certificate2"/> and holds a private key alongside it
        /// rather than attaching the key to the certificate.
        /// </summary>
        /// <param name="certificate">
        /// The certificate to wrap. Must not be <c>null</c>.
        /// </param>
        /// <param name="detachedPrivateKey">
        /// The private key held alongside the certificate.
        /// </param>
        /// <param name="ownsDetachedPrivateKey">
        /// <c>true</c> if the key is disposed with the last reference.
        /// </param>
        private Certificate(
            X509Certificate2 certificate,
            AsymmetricAlgorithm detachedPrivateKey,
            bool ownsDetachedPrivateKey)
        {
            m_core = new CertificateCore(
                certificate ?? throw new ArgumentNullException(nameof(certificate)),
                detachedPrivateKey,
                ownsDetachedPrivateKey);
            Interlocked.Increment(ref s_instancesCreated);
            InitializeLeakTracking();
        }

        /// <summary>
        /// Private constructor that creates an additional owning handle over
        /// an existing shared <see cref="CertificateCore"/>. Does NOT create a
        /// new core and therefore does NOT increment <see cref="InstancesCreated"/>;
        /// the caller has already incremented the core's reference count.
        /// </summary>
        /// <param name="core">The shared certificate core. Must not be <c>null</c>.</param>
        private Certificate(CertificateCore core)
        {
            m_core = core;
            InitializeLeakTracking();
        }

        /// <summary>
        /// The inner <see cref="X509Certificate2"/> instance.
        /// Internal access is available to friends via InternalsVisibleTo.
        /// </summary>
        internal X509Certificate2 X509 => m_core.X509;

        /// <summary>
        /// Creates a <see cref="Certificate"/> that takes ownership of the
        /// provided <see cref="X509Certificate2"/>. The caller must NOT
        /// dispose the certificate after calling this method.
        /// </summary>
        /// <param name="certificate">
        /// The certificate to wrap. Must not be <c>null</c>.
        /// </param>
        /// <returns>
        /// A new <see cref="Certificate"/> that owns the inner certificate.
        /// </returns>
        public static Certificate From(X509Certificate2 certificate)
        {
            return new Certificate(certificate);
        }

        /// <summary>
        /// Creates a public-key-only <see cref="Certificate"/> from
        /// DER or PEM encoded raw data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        /// <returns>
        /// A new <see cref="Certificate"/> containing only the public key.
        /// </returns>
        public static Certificate FromRawData(byte[] rawData)
        {
            return new Certificate(rawData);
        }

        /// <summary>
        /// Creates a public-key-only <see cref="Certificate"/> from
        /// DER or PEM encoded raw data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        /// <returns>
        /// A new <see cref="Certificate"/> containing only the public key.
        /// </returns>
        public static Certificate FromRawData(ReadOnlyMemory<byte> rawData)
        {
            return new Certificate(rawData.ToArray());
        }

        /// <summary>
        /// Creates a copy of the inner <see cref="X509Certificate2"/>.
        /// The caller owns the returned instance and must dispose it.
        /// Private keys are preserved if present.
        /// </summary>
        /// <returns>
        /// A new <see cref="X509Certificate2"/> that is a copy of the
        /// wrapped certificate.
        /// </returns>
        public X509Certificate2 AsX509Certificate2()
        {
            if (X509.HasPrivateKey)
            {
                try
                {
                    byte[] pfxData = Export(X509ContentType.Pfx);
                    return X509CertificateLoader.LoadPkcs12(
                        pfxData,
                        [],
                        X509KeyStorageFlags.Exportable);
                }
                catch (CryptographicException)
                {
                    // Private key is not exportable (e.g., loaded without
                    // X509KeyStorageFlags.Exportable). Fall back to the
                    // legacy copy constructor which creates an
                    // independently disposable wrapper that shares the
                    // underlying OS certificate handle (and therefore the
                    // private key handle). The result is usable for sign /
                    // decrypt / TLS handshakes without requiring an
                    // exportable key.
#pragma warning disable SYSLIB0057 // Type or member is obsolete
                    return new X509Certificate2(X509);
#pragma warning restore SYSLIB0057
                }
            }

            return X509CertificateLoader.LoadCertificate(X509.RawData);
        }

        /// <inheritdoc/>
        public X500DistinguishedName SubjectName => X509.SubjectName;

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName => X509.IssuerName;

        /// <inheritdoc/>
        public DateTime NotBefore => X509.NotBefore;

        /// <inheritdoc/>
        public DateTime NotAfter => X509.NotAfter;

        /// <inheritdoc/>
        public string SerialNumber => X509.SerialNumber;

        /// <inheritdoc/>
        public byte[] GetSerialNumber()
        {
            return X509.GetSerialNumber();
        }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName =>
            Oids.GetHashAlgorithmName(X509.SignatureAlgorithm.Value
                ?? throw new CryptographicException("Signature algorithm OID value is null."));

        /// <inheritdoc/>
        public X509ExtensionCollection Extensions => X509.Extensions;

        /// <summary>
        /// The subject of the certificate as a string.
        /// </summary>
        public string Subject => X509.Subject;

        /// <summary>
        /// The SHA-1 thumbprint of the certificate as a hex string.
        /// </summary>
        public string Thumbprint => X509.Thumbprint;

        /// <summary>
        /// The DER encoded raw data of the certificate.
        /// </summary>
        public byte[] RawData => X509.RawData;

        /// <summary>
        /// Whether the certificate has an associated private key.
        /// </summary>
        /// <remarks>
        /// This is <c>true</c> both when the certificate itself owns a private
        /// key and when a private key is held alongside it in detached form,
        /// for example a key that resides in a TPM, an HSM or a remote key
        /// service and can never be attached to the certificate object.
        /// </remarks>
        public bool HasPrivateKey => m_core.DetachedPrivateKey is not null || X509.HasPrivateKey;

        /// <summary>
        /// Whether the private key is held in detached form and therefore
        /// cannot be exported with the certificate.
        /// </summary>
        public bool HasDetachedPrivateKey => m_core.DetachedPrivateKey is not null;

        /// <summary>
        /// The public key of the certificate.
        /// </summary>
        public PublicKey PublicKey => X509.PublicKey;

        /// <summary>
        /// The issuer of the certificate as a string.
        /// </summary>
        public string Issuer => X509.Issuer;

        /// <summary>
        /// The friendly name of the certificate (Windows only, may be empty).
        /// </summary>
        public string FriendlyName => X509.FriendlyName;

        /// <summary>
        /// The OID of the signature algorithm used to sign the certificate.
        /// </summary>
        public Oid SignatureAlgorithm => X509.SignatureAlgorithm;

        /// <inheritdoc/>
        // CA1063: this Dispose() delegates to Dispose(bool). CA1816: SuppressFinalize is
        // called inside Dispose(bool) only on the first disposal of THIS handle, so a
        // finalizer-based leak reporter still triggers on handles abandoned without Dispose.
#pragma warning disable CA1063, CA1816
        public void Dispose()
#pragma warning restore CA1063, CA1816
        {
            Dispose(disposing: true);
        }

        /// <summary>
        /// Releases this handle's reference to the shared certificate core.
        /// Idempotent per handle: a second call on the same handle is a safe
        /// no-op. The inner <see cref="X509Certificate2"/> is disposed only
        /// when the last owning handle is released (refcount reaches zero).
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release managed resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            // Idempotent per handle: only the first Dispose of THIS handle
            // releases its reference to the shared core. This prevents a
            // double-Dispose of one logical owner from over-decrementing the
            // shared reference count (SA-CERT-01).
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_core.Release();

            if (m_allocationInfo != null)
            {
                s_allocationTracker.TryRemove(m_allocationInfo.AllocationId, out _);
                m_allocationInfo = null;
            }
        }

        /// <summary>
        /// Exports the certificate to a byte array in the specified format.
        /// </summary>
        /// <param name="contentType">
        /// The format to export (e.g., <see cref="X509ContentType.Cert"/>,
        /// <see cref="X509ContentType.Pfx"/>, <see cref="X509ContentType.Pkcs12"/>).
        /// </param>
        /// <returns>The exported certificate bytes.</returns>
        public byte[] Export(X509ContentType contentType)
        {
            ThrowIfDetachedKeyCannotBeExported(contentType);
            return X509.Export(contentType);
        }

        /// <summary>
        /// Exports the certificate to a byte array in the specified format,
        /// protected with a secure password.
        /// </summary>
        /// <param name="contentType">The format to export.</param>
        /// <param name="password">The password to protect the exported data.</param>
        /// <returns>The exported certificate bytes.</returns>
        public byte[] Export(X509ContentType contentType, ReadOnlySpan<char> password)
        {
            ThrowIfDetachedKeyCannotBeExported(contentType);
#if NETFRAMEWORK
            return X509.Export(contentType, new string(password.ToArray()));
#else
            return X509.Export(contentType, new string(password));
#endif
        }

        /// <summary>
        /// Gets the RSA private key from the certificate, if available.
        /// </summary>
        /// <returns>
        /// The RSA private key, or <c>null</c> if none is present.
        /// </returns>
        public RSA? GetRSAPrivateKey()
        {
            if (m_core.DetachedPrivateKey is RSA detached)
            {
                return new NonOwningRsa(detached);
            }

            return X509.GetRSAPrivateKey();
        }

        /// <summary>
        /// Gets the RSA public key from the certificate.
        /// </summary>
        /// <returns>
        /// The RSA public key, or <c>null</c> if the certificate does
        /// not use an RSA key.
        /// </returns>
        public RSA? GetRSAPublicKey()
        {
            return X509.GetRSAPublicKey();
        }

        /// <summary>
        /// Gets the ECDsa private key from the certificate, if available.
        /// </summary>
        /// <returns>
        /// The ECDsa private key, or <c>null</c> if none is present.
        /// </returns>
        public ECDsa? GetECDsaPrivateKey()
        {
            if (m_core.DetachedPrivateKey is ECDsa detached)
            {
                return new NonOwningECDsa(detached);
            }

            return X509.GetECDsaPrivateKey();
        }

        /// <summary>
        /// Gets the ECDsa public key from the certificate.
        /// </summary>
        /// <returns>
        /// The ECDsa public key, or <c>null</c> if the certificate does
        /// not use an ECDsa key.
        /// </returns>
        public ECDsa? GetECDsaPublicKey()
        {
            return X509.GetECDsaPublicKey();
        }

        /// <summary>
        /// Creates a new <see cref="Certificate"/> by combining this
        /// certificate with an RSA private key.
        /// </summary>
        /// <param name="privateKey">The RSA private key to attach.</param>
        /// <returns>
        /// A new <see cref="Certificate"/> with the private key attached.
        /// </returns>
        public Certificate CopyWithPrivateKey(RSA privateKey)
        {
            return new Certificate(X509.CopyWithPrivateKey(privateKey));
        }

        /// <summary>
        /// Creates a new <see cref="Certificate"/> by combining this
        /// certificate with an ECDsa private key.
        /// </summary>
        /// <param name="privateKey">The ECDsa private key to attach.</param>
        /// <returns>
        /// A new <see cref="Certificate"/> with the private key attached.
        /// </returns>
        public Certificate CopyWithPrivateKey(ECDsa privateKey)
        {
            return new Certificate(X509.CopyWithPrivateKey(privateKey));
        }

        /// <summary>
        /// Creates a new <see cref="Certificate"/> that holds an RSA private key
        /// alongside this certificate without attaching it to the certificate.
        /// </summary>
        /// <param name="privateKey">
        /// The RSA private key to hold. Must not be <c>null</c>.
        /// </param>
        /// <param name="ownsPrivateKey">
        /// <c>true</c> if the returned certificate should dispose
        /// <paramref name="privateKey"/> when its last reference is released;
        /// otherwise <c>false</c>.
        /// </param>
        /// <returns>
        /// A new public-key-only <see cref="Certificate"/> that reports a private
        /// key and returns it from <see cref="GetRSAPrivateKey"/>.
        /// </returns>
        /// <remarks>
        /// Use this instead of <see cref="CopyWithPrivateKey(RSA)"/> when the key
        /// cannot be attached to an <see cref="X509Certificate2"/>. That is the
        /// case for every key whose private material is not extractable and which
        /// is not a platform key object: on Windows
        /// <c>X509Certificate2.CopyWithPrivateKey</c> only has fast paths for
        /// <c>RSACng</c> and <c>RSACryptoServiceProvider</c>, and otherwise falls
        /// back to exporting the private parameters, which a key held in a TPM,
        /// an HSM, a PKCS#11 token or a remote key service will refuse.
        /// <para>
        /// The detached key is shared by every handle created with
        /// <see cref="AddRef"/> and is disposed once, with the last handle, when
        /// <paramref name="ownsPrivateKey"/> is <c>true</c>. Each call to
        /// <see cref="GetRSAPrivateKey"/> returns an independent non owning view
        /// that the caller may dispose safely.
        /// </para>
        /// </remarks>
        public Certificate CopyWithDetachedPrivateKey(RSA privateKey, bool ownsPrivateKey = true)
        {
            return CreateWithDetachedPrivateKey(
                privateKey ?? throw new ArgumentNullException(nameof(privateKey)),
                ownsPrivateKey);
        }

        /// <summary>
        /// Creates a new <see cref="Certificate"/> that holds an ECDsa private key
        /// alongside this certificate without attaching it to the certificate.
        /// </summary>
        /// <param name="privateKey">
        /// The ECDsa private key to hold. Must not be <c>null</c>.
        /// </param>
        /// <param name="ownsPrivateKey">
        /// <c>true</c> if the returned certificate should dispose
        /// <paramref name="privateKey"/> when its last reference is released;
        /// otherwise <c>false</c>.
        /// </param>
        /// <returns>
        /// A new public-key-only <see cref="Certificate"/> that reports a private
        /// key and returns it from <see cref="GetECDsaPrivateKey"/>.
        /// </returns>
        /// <remarks>
        /// See <see cref="CopyWithDetachedPrivateKey(RSA, bool)"/> for the rationale.
        /// </remarks>
        public Certificate CopyWithDetachedPrivateKey(ECDsa privateKey, bool ownsPrivateKey = true)
        {
            return CreateWithDetachedPrivateKey(
                privateKey ?? throw new ArgumentNullException(nameof(privateKey)),
                ownsPrivateKey);
        }

        private Certificate CreateWithDetachedPrivateKey(
            AsymmetricAlgorithm privateKey,
            bool ownsPrivateKey)
        {
            // A fresh public-key-only certificate is loaded so that the new handle
            // owns its own X509Certificate2 and never aliases this one.
            return new Certificate(
                X509CertificateLoader.LoadCertificate(X509.RawData),
                privateKey,
                ownsPrivateKey);
        }

        /// <summary>
        /// Rejects an export that would silently drop a detached private key.
        /// </summary>
        /// <param name="contentType">The requested export format.</param>
        /// <exception cref="CryptographicException">
        /// Thrown when a key bearing format is requested and the private key is
        /// held in detached form.
        /// </exception>
        /// <remarks>
        /// The inner <see cref="X509Certificate2"/> of a detached key certificate
        /// carries no private key, so a PKCS#12 export would succeed and quietly
        /// produce a file without one. Failing loudly is safer: a key held in a
        /// TPM, an HSM or a remote key service is not exportable by design, and a
        /// caller that wanted the key would otherwise be handed a useless blob.
        /// </remarks>
        private void ThrowIfDetachedKeyCannotBeExported(X509ContentType contentType)
        {
            if (m_core.DetachedPrivateKey is null)
            {
                return;
            }

            // X509ContentType.Pkcs12 and X509ContentType.Pfx are the same value.
            if (contentType == X509ContentType.Pfx)
            {
                throw new CryptographicException(
                    "The private key is held in detached form and cannot be exported. " +
                    "Export the certificate without the private key, or keep the key " +
                    "where it resides.");
            }
        }

        /// <summary>
        /// Gets the key algorithm OID as a string.
        /// </summary>
        /// <returns>The key algorithm OID.</returns>
        public string GetKeyAlgorithm()
        {
            return X509.GetKeyAlgorithm();
        }

        /// <summary>
        /// Gets name information from the certificate subject or issuer.
        /// </summary>
        /// <param name="nameType">The type of name to retrieve.</param>
        /// <param name="forIssuer">
        /// <c>true</c> to retrieve issuer name information;
        /// <c>false</c> for subject name information.
        /// </param>
        /// <returns>The requested name information.</returns>
        public string GetNameInfo(X509NameType nameType, bool forIssuer)
        {
            return X509.GetNameInfo(nameType, forIssuer);
        }

        /// <inheritdoc/>
        public bool Equals(Certificate? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return string.Equals(
                Thumbprint, other.Thumbprint,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as Certificate);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Thumbprint);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            try
            {
                StringBuilder sb = new StringBuilder(128)
                    .Append("[Subject=").Append(Subject)
                    .Append(", Thumbprint=").Append(Thumbprint)
                    .Append(", NotBefore=").Append(
                        NotBefore.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append(", NotAfter=").Append(
                        NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Append(", KeyAlgorithm=").Append(GetKeyAlgorithm());
                if (HasPrivateKey)
                {
                    sb.Append(", HasPrivateKey");
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch
            {
                return "[Disposed Certificate]";
            }
        }

        /// <summary>
        /// Increments the reference count on the shared certificate core and
        /// returns a NEW owning handle over it. Each returned handle is an
        /// independent owner that must be balanced by exactly one call to
        /// <see cref="Dispose()"/>. The inner <see cref="X509Certificate2"/>
        /// is disposed only when the last handle is released. A double-Dispose
        /// of one handle is a safe no-op and does not affect other handles.
        /// </summary>
        /// <returns>A new owning handle over the same certificate core.</returns>
        /// <exception cref="ObjectDisposedException">
        /// The underlying certificate core has already been fully released.
        /// </exception>
        public Certificate AddRef()
        {
            m_core.AddRef();
            return new Certificate(m_core);
        }

        /// <summary>
        /// Whether per-certificate allocation tracking is enabled for this process.
        /// </summary>
        internal static bool LeakTrackingEnabled => s_leakTrackingEnabled;

        /// <summary>
        /// Gets or sets the diagnostic scope captured by newly allocated handles.
        /// </summary>
        internal static string? LeakTrackingScope
        {
            get => s_leakTrackingScope.Value;
            set => s_leakTrackingScope.Value = value;
        }

        /// <summary>
        /// Enables allocation tracking for this handle when requested.
        /// </summary>
        private void InitializeLeakTracking()
        {
            if (s_leakTrackingEnabled)
            {
                Track();
            }
        }

        /// <summary>
        /// Tracks the allocation until this handle is disposed.
        /// </summary>
        private void Track()
        {
            long allocationId = Interlocked.Increment(ref s_nextAllocationId);
            m_allocationInfo = new CertificateAllocationInfo(
                allocationId,
                this,
                new System.Diagnostics.StackTrace(fNeedFileInfo: false).ToString(),
                s_leakTrackingScope.Value);
            s_allocationTracker[allocationId] = m_allocationInfo;
        }

        /// <summary>
        /// Captures allocation context for leak-detection diagnostics.
        /// </summary>
        internal sealed class CertificateAllocationInfo
        {
            /// <summary>
            /// Creates allocation diagnostics for a certificate handle.
            /// </summary>
            /// <param name="allocationId">Unique allocation identifier.</param>
            /// <param name="certificate">The allocated certificate handle.</param>
            /// <param name="stackTrace">Allocation stack trace.</param>
            /// <param name="fixtureName">Optional test fixture attribution.</param>
            public CertificateAllocationInfo(
                long allocationId,
                Certificate certificate,
                string stackTrace,
                string? fixtureName)
            {
                AllocationId = allocationId;
                Reference = new WeakReference<Certificate>(certificate);
                StackTrace = stackTrace;
                FixtureName = fixtureName;
                CreatedAt = DateTime.UtcNow;
            }

            /// <summary>
            /// Gets the unique allocation identifier.
            /// </summary>
            public long AllocationId { get; }

            /// <summary>
            /// Gets a weak reference to the allocated certificate handle.
            /// </summary>
            public WeakReference<Certificate> Reference { get; }

            /// <summary>
            /// Gets the allocation stack trace.
            /// </summary>
            public string StackTrace { get; }

            /// <summary>
            /// Gets the optional test fixture attribution.
            /// </summary>
            public string? FixtureName { get; }

            /// <summary>
            /// Gets the UTC allocation time.
            /// </summary>
            public DateTime CreatedAt { get; }
        }

        /// <summary>
        /// Dumps allocation info for live <see cref="Certificate"/>
        /// instances that are still reachable. Useful in tests to
        /// surface the call site that created a leaking certificate.
        /// </summary>
        internal static IEnumerable<(
            string Thumbprint,
            int RefCount,
            DateTime CreatedAt,
            string StackTrace,
            string? FixtureName)>
            EnumerateLiveCertificates()
        {
            foreach (CertificateAllocationInfo info in s_allocationTracker.Values)
            {
                if (info.Reference.TryGetTarget(out Certificate? cert) &&
                    Volatile.Read(ref cert.m_disposed) == 0 &&
                    cert.m_core.RefCount > 0)
                {
                    yield return (
                        GetThumbprintForDiagnostics(cert),
                        cert.m_core.RefCount,
                        info.CreatedAt,
                        info.StackTrace,
                        info.FixtureName);
                }
            }
        }

        /// <summary>
        /// Reads the thumbprint without allowing a concurrent disposal to abort a leak dump.
        /// </summary>
        private static string GetThumbprintForDiagnostics(Certificate certificate)
        {
            try
            {
                return certificate.Thumbprint ?? "(no thumbprint)";
            }
            catch (CryptographicException)
            {
                return "(unavailable after disposal)";
            }
        }

        /// <summary>
        /// Dumps allocation info for undisposed <see cref="Certificate"/>
        /// handles that are no longer reachable.
        /// </summary>
        internal static IEnumerable<(
            DateTime CreatedAt,
            string StackTrace,
            string? FixtureName)>
            EnumerateUnreachableUndisposedCertificates()
        {
            foreach (CertificateAllocationInfo info in s_allocationTracker.Values)
            {
                if (!info.Reference.TryGetTarget(out _))
                {
                    yield return (
                        info.CreatedAt,
                        info.StackTrace,
                        info.FixtureName);
                }
            }
        }

        /// <summary>
        /// Resolves whether allocation tracking is enabled.
        /// </summary>
        private static bool ResolveLeakTrackingEnabled()
        {
            bool defaultValue;
#if DEBUG
            defaultValue = true;
#else
            defaultValue = false;
#endif
            bool switchFound = AppContext.TryGetSwitch(
                c_leakTrackingSwitchName,
                out bool switchValue);
            return ResolveLeakTrackingEnabled(
                switchFound,
                switchValue,
                Environment.GetEnvironmentVariable(c_leakTrackingEnvironmentVariable),
                defaultValue);
        }

        /// <summary>
        /// Resolves the allocation-tracking setting from its inputs.
        /// </summary>
        internal static bool ResolveLeakTrackingEnabled(
            bool switchFound,
            bool switchValue,
            string? environmentValue,
            bool defaultValue)
        {
            if (switchFound)
            {
                return switchValue;
            }

            if (string.Equals(environmentValue, "1", StringComparison.Ordinal) ||
                string.Equals(environmentValue, bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(environmentValue, "0", StringComparison.Ordinal) ||
                string.Equals(environmentValue, bool.FalseString, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }

        /// <summary>
        /// Total number of <see cref="Certificate"/> instances created
        /// since the last <see cref="ResetLeakCounters"/> call.
        /// </summary>
        public static long InstancesCreated => Volatile.Read(ref s_instancesCreated);

        /// <summary>
        /// Total number of <see cref="Certificate"/> instances whose
        /// inner X509Certificate2 was disposed (refcount reached zero).
        /// </summary>
        public static long InstancesDisposed => Volatile.Read(ref s_instancesDisposed);

        /// <summary>
        /// Number of Certificate instances that were created but not
        /// yet disposed. A positive value after GC indicates a leak.
        /// </summary>
        public static long InstancesLeaked => InstancesCreated - InstancesDisposed;

        /// <summary>
        /// Resets the leak-detection counters. Call at the start of a
        /// test run to get a clean baseline.
        /// </summary>
        public static void ResetLeakCounters()
        {
            Interlocked.Exchange(ref s_instancesCreated, 0);
            Interlocked.Exchange(ref s_instancesDisposed, 0);
            s_allocationTracker.Clear();
        }

        /// <summary>
        /// Test-only hook used by the leak-detector self-tests to account
        /// for a certificate that is deliberately abandoned (never disposed)
        /// in order to exercise unreachable-handle tracking. Balances
        /// the global leak counters so the intentional leak does not trip
        /// the assembly-level leak assertion and removes unreachable test
        /// allocations from the tracker. Visible to friend test assemblies
        /// via <c>InternalsVisibleTo</c>.
        /// </summary>
        /// <param name="allocationStackMarker">
        /// Method name that uniquely identifies the deliberate test allocation.
        /// </param>
        internal static void AccountForDeliberatelyLeakedInstanceForTest(
            string allocationStackMarker)
        {
            Interlocked.Increment(ref s_instancesDisposed);
            foreach (KeyValuePair<long, CertificateAllocationInfo> entry in s_allocationTracker)
            {
                if (!entry.Value.Reference.TryGetTarget(out _) &&
                    entry.Value.StackTrace.Contains(
                        allocationStackMarker,
                        StringComparison.Ordinal))
                {
                    s_allocationTracker.TryRemove(entry.Key, out _);
                    return;
                }
            }
        }

        /// <summary>
        /// The shared, reference-counted state for a logical certificate. One
        /// core is created per <c>new Certificate(...)</c> and may be owned by
        /// many <see cref="Certificate"/> handles (each created via
        /// <see cref="AddRef"/>). The inner <see cref="X509Certificate2"/> is
        /// disposed exactly once, when the last owning handle is released.
        /// </summary>
        private sealed class CertificateCore
        {
            public CertificateCore(
                X509Certificate2 x509,
                AsymmetricAlgorithm? detachedPrivateKey = null,
                bool ownsDetachedPrivateKey = true)
            {
                X509 = x509;
                DetachedPrivateKey = detachedPrivateKey;
                m_ownsDetachedPrivateKey = ownsDetachedPrivateKey;
            }

            /// <summary>
            /// The wrapped certificate. Valid until the last reference is released.
            /// </summary>
            public X509Certificate2 X509 { get; }

            /// <summary>
            /// A private key held alongside the certificate rather than owned by
            /// it, or <c>null</c> when the certificate owns its own key.
            /// </summary>
            public AsymmetricAlgorithm? DetachedPrivateKey { get; }

            /// <summary>
            /// The current number of owning handles. For diagnostics only.
            /// </summary>
            public int RefCount => Volatile.Read(ref m_refCount);

            /// <summary>
            /// Adds an owning reference. Each call must be balanced by exactly
            /// one <see cref="Release"/>.
            /// </summary>
            /// <exception cref="ObjectDisposedException">
            /// The core has already been fully released (refcount was zero).
            /// </exception>
            public void AddRef()
            {
                int current = Interlocked.Increment(ref m_refCount);
                if (current <= 1)
                {
                    // Was already at 0 (released) — undo and throw.
                    Interlocked.Decrement(ref m_refCount);
                    throw new ObjectDisposedException(nameof(Certificate));
                }
            }

            /// <summary>
            /// Releases one owning reference; disposes the inner certificate
            /// when the last reference is released.
            /// </summary>
            public void Release()
            {
                int remaining = Interlocked.Decrement(ref m_refCount);
                if (remaining == 0)
                {
                    X509.Dispose();
                    if (m_ownsDetachedPrivateKey)
                    {
                        DetachedPrivateKey?.Dispose();
                    }
                    Interlocked.Increment(ref s_instancesDisposed);
                }
            }

            private readonly bool m_ownsDetachedPrivateKey;
            private int m_refCount = 1;
        }

        /// <summary>
        /// The shared reference-counted core. Many handles may point at one core.
        /// </summary>
        private readonly CertificateCore m_core;

        private CertificateAllocationInfo? m_allocationInfo;

        /// <summary>
        /// 0 while this handle is live, 1 once this handle has been disposed.
        /// Makes Dispose idempotent per handle (SA-CERT-01).
        /// </summary>
        private int m_disposed;

        private const string c_leakTrackingSwitchName =
            "Opc.Ua.Security.Certificates.CertificateLeakTracking";
        private const string c_leakTrackingEnvironmentVariable =
            "OPCUA_CERTIFICATE_LEAK_TRACKING";

        /// <summary>
        /// Outstanding tracked allocations, removed when their owning handle is disposed.
        /// </summary>
        private static readonly ConcurrentDictionary<long, CertificateAllocationInfo>
            s_allocationTracker = new();
        private static readonly AsyncLocal<string?> s_leakTrackingScope = new();
        private static readonly bool s_leakTrackingEnabled = ResolveLeakTrackingEnabled();
        private static long s_instancesCreated;
        private static long s_instancesDisposed;
        private static long s_nextAllocationId;
    }
}
