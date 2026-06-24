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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.Threading;
using System.Collections.Concurrent;
using System.Text;

#if DEBUG
using System.Collections.Generic;
#endif

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Wraps an <see cref="X509Certificate2"/> providing a managed
    /// lifetime and implementing <see cref="IX509Certificate"/>.
    /// </summary>
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
#if DEBUG
            Track();
#endif
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
#if DEBUG
            Track();
#endif
        }
#endif

        /// <summary>
        /// Creates a public-key-only certificate from a file.
        /// </summary>
        /// <param name="fileName">
        /// The path to a file containing DER or PEM encoded certificate data.
        /// </param>
        public Certificate(string fileName)
        {
            m_core = new CertificateCore(
                X509CertificateLoader.LoadCertificateFromFile(fileName));
            Interlocked.Increment(ref s_instancesCreated);
#if DEBUG
            Track();
#endif
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
#if DEBUG
            Track();
#endif
        }

        /// <summary>
        /// Creates a certificate from a PKCS#12 file with a password.
        /// </summary>
        /// <param name="fileName">The path to the PKCS#12 file.</param>
        /// <param name="password">The password for the PKCS#12 file.</param>
        /// <param name="keyStorageFlags">
        /// The storage flags to use when loading the certificate.
        /// </param>
        public Certificate(
            string fileName,
            ReadOnlySpan<char> password,
            X509KeyStorageFlags keyStorageFlags = default)
        {
            m_core = new CertificateCore(X509CertificateLoader.LoadPkcs12FromFile(
                fileName, password, keyStorageFlags));
            Interlocked.Increment(ref s_instancesCreated);
#if DEBUG
            Track();
#endif
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
#if DEBUG
            Track();
#endif
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
#if DEBUG
            Track();
#endif
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
        public bool HasPrivateKey => X509.HasPrivateKey;

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

            // Only this handle has been finalised; suppress its finalizer
            // (the DEBUG leak reporter). Other handles over the same core
            // remain finalizable until they too are disposed.
#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
            GC.SuppressFinalize(this);
#pragma warning restore CA1816 // Dispose methods should call SuppressFinalize
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

#if DEBUG
        /// <summary>
        /// Track the allocation
        /// </summary>
        private void Track()
        {
            // Cache the allocation info on the instance so the finalizer
            // can report it even after the static tracker has lost the
            // weak reference.
            m_allocationInfo = new CertificateAllocationInfo(
                this,
                new System.Diagnostics.StackTrace(true).ToString(),
                X509.Thumbprint);
            s_allocationTracker.Add(m_allocationInfo);
        }

        /// <summary>
        /// Detects leaked certificates: a handle that was finalized without
        /// being disposed (its reference to the shared core was never
        /// released). Only compiled in DEBUG builds.
        /// </summary>
#pragma warning disable CA1063 // Implement IDisposable Correctly
        ~Certificate()
#pragma warning restore CA1063 // Implement IDisposable Correctly
        {
            if (m_disposed == 0 && m_allocationInfo != null)
            {
                s_finalizedWithLeakedRef.Add(m_allocationInfo);
            }
        }

        private CertificateAllocationInfo? m_allocationInfo;

        /// <summary>
        /// Captures allocation context for leak-detection diagnostics.
        /// </summary>
        internal sealed class CertificateAllocationInfo
        {
            public WeakReference<Certificate> Reference { get; }
            public string StackTrace { get; }
            public string? Thumbprint { get; }
            public DateTime CreatedAt { get; }

            public CertificateAllocationInfo(
                Certificate certificate,
                string stackTrace,
                string? thumbprint)
            {
                Reference = new WeakReference<Certificate>(certificate);
                StackTrace = stackTrace;
                Thumbprint = thumbprint;
                CreatedAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Use a list of weak references for live-leak diagnostics.
        /// ConditionalWeakTable doesn't expose enumeration on .NET
        /// Framework, and we want the per-instance list anyway.
        /// </summary>
        private static readonly ConcurrentBag<CertificateAllocationInfo> s_allocationTracker = [];

        /// <summary>
        /// Set of allocation infos for Certificates that were finalised
        /// while still holding a positive refcount (a real leak —
        /// someone called AddRef without a matching Dispose). Cached so
        /// the finalizer can record it before the instance dies.
        /// </summary>
        private static readonly ConcurrentBag<CertificateAllocationInfo> s_finalizedWithLeakedRef = [];

        /// <summary>
        /// Dumps allocation info for live <see cref="Certificate"/>
        /// instances that are still reachable. Useful in tests to
        /// surface the call site that created a leaking certificate.
        /// </summary>
        public static IEnumerable<(string Thumbprint, int RefCount, DateTime CreatedAt, string StackTrace)> EnumerateLiveCertificates()
        {
            foreach (CertificateAllocationInfo info in s_allocationTracker)
            {
                if (info.Reference.TryGetTarget(out Certificate? cert))
                {
                    yield return (
                        info.Thumbprint ?? "(no thumbprint)",
                        cert.m_core.RefCount,
                        info.CreatedAt,
                        info.StackTrace);
                }
            }
        }

        /// <summary>
        /// Dumps allocation info for <see cref="Certificate"/> instances
        /// that were finalized while still holding a positive refcount
        /// (i.e., AddRef without matching Dispose).
        /// </summary>
        public static IEnumerable<(string Thumbprint, DateTime CreatedAt, string StackTrace)> EnumerateFinalizedLeakedCertificates()
        {
            foreach (CertificateAllocationInfo info in s_finalizedWithLeakedRef)
            {
                yield return (
                    info.Thumbprint ?? "(no thumbprint)",
                    info.CreatedAt,
                    info.StackTrace);
            }
        }
#endif

        private static long s_instancesCreated;
        private static long s_instancesDisposed;

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
        }

#if DEBUG
        /// <summary>
        /// Test-only hook used by the leak-detector self-tests to account
        /// for a certificate that is deliberately abandoned (never disposed)
        /// in order to exercise the finalizer-based leak tracking. Balances
        /// the global leak counters so the intentional leak does not trip
        /// the assembly-level leak assertion. DEBUG-only and visible to
        /// friend test assemblies via <c>InternalsVisibleTo</c>.
        /// </summary>
        internal static void AccountForDeliberatelyLeakedInstanceForTest()
        {
            Interlocked.Increment(ref s_instancesDisposed);
        }
#endif

        /// <summary>
        /// The shared, reference-counted state for a logical certificate. One
        /// core is created per <c>new Certificate(...)</c> and may be owned by
        /// many <see cref="Certificate"/> handles (each created via
        /// <see cref="AddRef"/>). The inner <see cref="X509Certificate2"/> is
        /// disposed exactly once, when the last owning handle is released.
        /// </summary>
        private sealed class CertificateCore
        {
            public CertificateCore(X509Certificate2 x509)
            {
                X509 = x509;
            }

            /// <summary>
            /// The wrapped certificate. Valid until the last reference is released.
            /// </summary>
            public X509Certificate2 X509 { get; }

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
                    Interlocked.Increment(ref s_instancesDisposed);
                }
            }

            private int m_refCount = 1;
        }

        // The shared reference-counted core. Many handles may point at one core.
        private readonly CertificateCore m_core;

        // 0 while this handle is live, 1 once this handle has been disposed.
        // Makes Dispose idempotent per handle (SA-CERT-01).
        private int m_disposed;
    }
}
