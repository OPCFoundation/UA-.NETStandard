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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.Threading;
#if DEBUG
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
            X509 = X509CertificateLoader.LoadCertificate(rawData);
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
            X509 = X509CertificateLoader.LoadCertificate(rawData);
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
            X509 = X509CertificateLoader.LoadCertificateFromFile(fileName);
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
            X509 = X509CertificateLoader.LoadPkcs12(
                rawData, password, keyStorageFlags);
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
            X509 = X509CertificateLoader.LoadPkcs12FromFile(
                fileName, password, keyStorageFlags);
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
            X509 = certificate ??
                throw new ArgumentNullException(nameof(certificate));
            Interlocked.Increment(ref s_instancesCreated);
#if DEBUG
            Track();
#endif
        }

        /// <summary>
        /// The inner <see cref="X509Certificate2"/> instance.
        /// Internal access is available to friends via InternalsVisibleTo.
        /// </summary>
        internal X509Certificate2 X509 { get; }

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
                        ReadOnlySpan<char>.Empty,
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
        public void Dispose()
        {
            Dispose(disposing: true);
        }

        /// <summary>
        /// Releases the resources used by the <see cref="Certificate"/>.
        /// The inner <see cref="X509Certificate2"/> is disposed only
        /// when the reference count reaches zero.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release managed resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                int remaining = System.Threading.Interlocked
                    .Decrement(ref m_refCount);
                if (remaining == 0)
                {
                    X509.Dispose();
                    Interlocked.Increment(ref s_instancesDisposed);
                    // Only suppress finalisation now that the
                    // refcount has reached zero. Suppressing earlier
                    // would mask AddRef-without-Dispose leaks: the
                    // managed wrapper would be reclaimed without
                    // running our finalizer-based leak reporter.
                    GC.SuppressFinalize(this);
                }
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
                var sb = new System.Text.StringBuilder(128);
                sb.Append("[Subject=").Append(Subject);
                sb.Append(", Thumbprint=").Append(Thumbprint);
                sb.Append(", NotBefore=").Append(NotBefore
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                sb.Append(", NotAfter=").Append(NotAfter
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                sb.Append(", KeyAlgorithm=").Append(GetKeyAlgorithm());
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
        /// Increments the reference count on this certificate.
        /// Each call must be balanced by a call to <see cref="Dispose()"/>.
        /// The inner <see cref="X509Certificate2"/> is disposed only
        /// when the last reference is released.
        /// </summary>
        /// <returns>This certificate instance for fluent usage.</returns>
        /// <exception cref="ObjectDisposedException"></exception>
        public Certificate AddRef()
        {
            int current = System.Threading.Interlocked.Increment(ref m_refCount);
            if (current <= 1)
            {
                // Was already at 0 (disposed) — undo and throw.
                System.Threading.Interlocked.Decrement(ref m_refCount);
                throw new ObjectDisposedException(nameof(Certificate));
            }
            return this;
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
        /// Detects leaked certificates that were never disposed.
        /// Only compiled in DEBUG builds.
        /// </summary>
#pragma warning disable CA1063 // Implement IDisposable Correctly
        ~Certificate()
#pragma warning restore CA1063 // Implement IDisposable Correctly
        {
            if (m_refCount > 0 && m_allocationInfo != null)
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

        // Use a list of weak references for live-leak diagnostics.
        // ConditionalWeakTable doesn't expose enumeration on .NET
        // Framework, and we want the per-instance list anyway.
        private static readonly System.Collections.Concurrent.ConcurrentBag<CertificateAllocationInfo> s_allocationTracker
            = new();

        // Set of allocation infos for Certificates that were finalised
        // while still holding a positive refcount (a real leak —
        // someone called AddRef without a matching Dispose). Cached so
        // the finalizer can record it before the instance dies.
        private static readonly System.Collections.Concurrent.ConcurrentBag<CertificateAllocationInfo> s_finalizedWithLeakedRef
            = new();

        /// <summary>
        /// Dumps allocation info for live <see cref="Certificate"/>
        /// instances that are still reachable. Useful in tests to
        /// surface the call site that created a leaking certificate.
        /// </summary>
        public static IEnumerable<(string Thumbprint, int RefCount, DateTime CreatedAt, string StackTrace)>
            EnumerateLiveCertificates()
        {
            foreach (CertificateAllocationInfo info in s_allocationTracker)
            {
                if (info.Reference.TryGetTarget(out Certificate? cert))
                {
                    yield return (
                        info.Thumbprint ?? "(no thumbprint)",
                        cert.m_refCount,
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
        public static IEnumerable<(string Thumbprint, DateTime CreatedAt, string StackTrace)>
            EnumerateFinalizedLeakedCertificates()
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

        private int m_refCount = 1;
    }
}
