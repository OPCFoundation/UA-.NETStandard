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

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Wraps an <see cref="X509Certificate2"/> providing a managed
    /// lifetime and implementing <see cref="IX509Certificate"/>.
    /// </summary>
    public class Certificate : IX509Certificate, IDisposable, IEquatable<Certificate>
    {
        /// <summary>
        /// The inner <see cref="X509Certificate2"/> instance.
        /// Internal access is available to friends via InternalsVisibleTo.
        /// </summary>
        internal X509Certificate2 X509 { get; }

        private bool _disposed;

        /// <summary>
        /// Creates a public-key-only certificate from DER or PEM encoded data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        public Certificate(byte[] rawData)
        {
            X509 = X509CertificateLoader.LoadCertificate(rawData);
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Creates a public-key-only certificate from DER or PEM encoded data.
        /// </summary>
        /// <param name="rawData">The DER or PEM encoded certificate data.</param>
        public Certificate(ReadOnlySpan<byte> rawData)
        {
            X509 = X509CertificateLoader.LoadCertificate(rawData);
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
        }

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
        /// Private keys are preserved if present and exportable.
        /// </summary>
        /// <returns>
        /// A new <see cref="X509Certificate2"/> that is a copy of the
        /// wrapped certificate.
        /// </returns>
        public X509Certificate2 AsX509Certificate2()
        {
            if (X509.HasPrivateKey)
            {
                byte[] pfxData = X509.Export(X509ContentType.Pfx);
                return X509CertificateLoader.LoadPkcs12(
                    pfxData,
                    ReadOnlySpan<char>.Empty,
                    X509KeyStorageFlags.Exportable);
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
        public byte[] GetSerialNumber() => X509.GetSerialNumber();

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName =>
            Oids.GetHashAlgorithmName(X509.SignatureAlgorithm.Value);

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
        /// The OID of the signature algorithm used to sign the certificate.
        /// </summary>
        public Oid SignatureAlgorithm => X509.SignatureAlgorithm;

        /// <summary>
        /// Gets the RSA private key from the certificate, if available.
        /// </summary>
        /// <returns>
        /// The RSA private key, or <c>null</c> if none is present.
        /// </returns>
        public RSA? GetRSAPrivateKey() => X509.GetRSAPrivateKey();

        /// <summary>
        /// Gets the RSA public key from the certificate.
        /// </summary>
        /// <returns>
        /// The RSA public key, or <c>null</c> if the certificate does
        /// not use an RSA key.
        /// </returns>
        public RSA? GetRSAPublicKey() => X509.GetRSAPublicKey();

        /// <summary>
        /// Gets the ECDsa private key from the certificate, if available.
        /// </summary>
        /// <returns>
        /// The ECDsa private key, or <c>null</c> if none is present.
        /// </returns>
        public ECDsa? GetECDsaPrivateKey() => X509.GetECDsaPrivateKey();

        /// <summary>
        /// Gets the ECDsa public key from the certificate.
        /// </summary>
        /// <returns>
        /// The ECDsa public key, or <c>null</c> if the certificate does
        /// not use an ECDsa key.
        /// </returns>
        public ECDsa? GetECDsaPublicKey() => X509.GetECDsaPublicKey();

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
        public string GetKeyAlgorithm() => X509.GetKeyAlgorithm();

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
            return StringComparer.OrdinalIgnoreCase
                .GetHashCode(Thumbprint);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return X509.ToString();
        }

        /// <summary>
        /// Releases the resources used by the <see cref="Certificate"/>.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release managed resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    X509.Dispose();
                }

                _disposed = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
