/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Collections.Generic;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a Certificate.
    /// </summary>
    public abstract class CertificateBuilderBase
        : IX509Certificate
        , ICertificateBuilder
        , ICertificateBuilderConfig
        , ICertificateBuilderSetIssuer
        , ICertificateBuilderParameter
        , ICertificateBuilderIssuer
        , ICertificateBuilderRSAParameter
        , ICertificateBuilderPublicKey
        , ICertificateBuilderRSAPublicKey
        , ICertificateBuilderCreateForRSA
        , ICertificateBuilderCreateForRSAAny
#if ECC_SUPPORT
        , ICertificateBuilderCreateForECDsa
        , ICertificateBuilderECDsaPublicKey
        , ICertificateBuilderECCParameter
        , ICertificateBuilderCreateForECDsaAny
#endif
    {
        #region Constructors
        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilderBase(X500DistinguishedName subjectName)
        {
            m_issuerName = m_subjectName = subjectName;
            Initialize();
        }

        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        public CertificateBuilderBase(string subjectName)
        {
            m_issuerName = m_subjectName = new X500DistinguishedName(subjectName);
            Initialize();
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        protected virtual void Initialize()
        {
            m_notBefore = DateTime.UtcNow.AddDays(-1).Date;
            m_notAfter = NotBefore.AddMonths(Defaults.LifeTime);
            m_hashAlgorithmName = Defaults.HashAlgorithmName;
            m_serialNumberLength = Defaults.SerialNumberLengthMin;
            m_extensions = new X509ExtensionCollection();
        }
        #endregion

        #region IX509Certificate Interface
        /// <inheritdoc/>
        public X500DistinguishedName SubjectName => m_subjectName;

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName => m_issuerName;

        /// <inheritdoc/>
        public DateTime NotBefore => m_notBefore;

        /// <inheritdoc/>
        public DateTime NotAfter => m_notAfter;

        /// <inheritdoc/>
        public string SerialNumber => m_serialNumber.ToHexString(true);

        /// <inheritdoc/>
        public byte[] GetSerialNumber() { return m_serialNumber; }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName => m_hashAlgorithmName;

        /// <inheritdoc/>
        public X509ExtensionCollection Extensions => m_extensions;
        #endregion

        #region Public Methods
        /// <summary>
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public abstract X509Certificate2 CreateForRSA();

        /// <summary>
        /// Create the RSA certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public abstract X509Certificate2 CreateForRSA(X509SignatureGenerator generator);

#if ECC_SUPPORT
        /// <summary>
        /// Create the ECC certificate based on selected parameters.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public abstract X509Certificate2 CreateForECDsa();

        /// <summary>
        /// Create the ECC certificate with signature using an external generator.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public abstract X509Certificate2 CreateForECDsa(X509SignatureGenerator generator);
#endif
        /// <summary>
        /// Set the length of the serial number.
        /// </summary>
        /// <remarks>
        /// The length of the serial number shall
        /// not exceed <see cref="Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="length">The length of the serial number in octets.</param>
        public ICertificateBuilder SetSerialNumberLength(int length)
        {
            if (length > Defaults.SerialNumberLengthMax || length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber length out of Range");
            }
            m_serialNumberLength = length;
            m_presetSerial = false;
            return this;
        }

        /// <summary>
        /// Set the value of the serial number directly
        /// using a byte array.
        /// </summary>
        /// <remarks>
        /// The length of the serial number shall
        /// not exceed <see cref="Defaults.SerialNumberLengthMax"/> octets.
        /// </remarks>
        /// <param name="serialNumber">The serial number as an array of bytes in little endian order.</param>
        public ICertificateBuilder SetSerialNumber(byte[] serialNumber)
        {
            if (serialNumber.Length > Defaults.SerialNumberLengthMax ||
                serialNumber.Length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber array exceeds supported length.");
            }
            m_serialNumberLength = serialNumber.Length;
            m_serialNumber = new byte[serialNumber.Length];
            Array.Copy(serialNumber, m_serialNumber, serialNumber.Length);
            m_serialNumber[m_serialNumberLength - 1] &= 0x7f;
            m_presetSerial = true;
            return this;
        }

        /// <summary>
        /// Create a new serial number and preserve
        /// it until the certificate is created.
        /// </summary>
        /// <remarks>
        /// The serial number may be needed to create an extension.
        /// This function makes it available before the
        /// cert is created.
        /// </remarks>
        public ICertificateBuilder CreateSerialNumber()
        {
            NewSerialNumber();
            m_presetSerial = true;
            return this;
        }

        /// <summary>
        /// Set the date when the certificate becomes valid.
        /// </summary>
        /// <param name="notBefore">The date.</param>
        public ICertificateBuilder SetNotBefore(DateTime notBefore)
        {
            m_notBefore = notBefore;
            return this;
        }

        /// <summary>
        /// Set the certificate expiry date.
        /// </summary>
        /// <param name="notAfter">The date after which the certificate is expired.</param>
        public ICertificateBuilder SetNotAfter(DateTime notAfter)
        {
            m_notAfter = notAfter;
            return this;
        }

        /// <summary>
        /// Set the lifetime of the certificate using Timespan.
        /// </summary>
        /// <param name="lifeTime">The lifetime as <see cref="System.Timespan"/>.</param>
        public ICertificateBuilder SetLifeTime(TimeSpan lifeTime)
        {
            m_notAfter = m_notBefore.Add(lifeTime);
            return this;
        }

        /// <summary>
        /// Set the lifetime of the certificate in month starting now.
        /// </summary>
        /// <param name="months">The lifetime in month.</param>
        public ICertificateBuilder SetLifeTime(ushort months)
        {
            m_notAfter = m_notBefore.AddMonths(months == 0 ? Defaults.LifeTime : (int)months);
            return this;
        }

        /// <summary>
        /// Set the hash algorithm to use for the signature.
        /// </summary>
        /// <param name="hashAlgorithmName">The hash algorithm name.</param>
        public ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == null) throw new ArgumentNullException(nameof(hashAlgorithmName));
            m_hashAlgorithmName = hashAlgorithmName;
            return this;
        }

        /// <summary>
        /// Set the CA constraints of the certificate.
        /// </summary>
        /// <param name="pathLengthConstraint">
        /// The path length constraint to use.
        /// -1 corresponds to None, other values constrain the chain length.
        /// </param>
        public ICertificateBuilder SetCAConstraint(int pathLengthConstraint = -1)
        {
            m_isCA = true;
            m_pathLengthConstraint = pathLengthConstraint;
            m_serialNumberLength = Defaults.SerialNumberLengthMax;
            return this;
        }

        /// <summary>
        /// Set the RSA key size in bits.
        /// </summary>
        /// <param name="keySize">The size of the RSA key.</param>
        /// <returns></returns>
        public virtual ICertificateBuilderCreateForRSAAny SetRSAKeySize(int keySize)
        {
            if (keySize == 0)
            {
                keySize = Defaults.RSAKeySize;
            }

            if (keySize % 1024 != 0 || keySize < Defaults.RSAKeySizeMin || keySize > Defaults.RSAKeySizeMax)
            {
                throw new ArgumentException(nameof(keySize), "KeySize must be a multiple of 1024 or is not in the allowed range.");
            }

            m_keySize = keySize;
            return this;
        }

        /// <summary>
        /// Add a extension to the certificate in addition to the default extensions.
        /// </summary>
        /// <remarks>
        /// By default the following X509 extensions are added to a certificate,
        /// some depending on certificate type:
        /// CA/SubCA/OPC UA application:
        ///     X509BasicConstraintsExtension
        ///     X509SubjectKeyIdentifierExtension
        ///     X509AuthorityKeyIdentifierExtension
        ///     X509KeyUsageExtension
        /// OPC UA application:
        ///     X509SubjectAltNameExtension
        ///     X509EnhancedKeyUsageExtension
        /// </remarks>
        /// <param name="extension"></param>
        /// <returns></returns>
        public virtual ICertificateBuilder AddExtension(X509Extension extension)
        {
            m_extensions.Add(extension);
            return this;
        }

#if ECC_SUPPORT
        /// <summary>
        /// Set the ECC curve to use for the certificate.
        /// </summary>
        /// <param name="curve">The ECC curve.</param>
        public virtual ICertificateBuilderCreateForECDsaAny SetECCurve(ECCurve curve)
        {
            m_curve = curve;
            return this;
        }

        /// <summary>
        /// Set the ECDsa public key to be used in the signed certificate.
        /// </summary>
        /// <param name="publicKey">The ASN encoded ECDsa public key.</param>
        public abstract ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(byte[] publicKey);

        /// <summary>
        /// Set the ECDsa public key to be used in the signed certificate.
        /// </summary>
        /// <param name="publicKey">The ECDsa object with the public key.</param>
        public virtual ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(ECDsa publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_ecdsaPublicKey = publicKey;
            return this;
        }
#endif
        /// <summary>
        /// Set the RSA public key to be used in the signed certificate.
        /// </summary>
        /// <param name="publicKey">The ASN encoded RSA public key.</param>
        public abstract ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey);

        /// <summary>
        /// Set the RSA public key to be used in the signed certificate.
        /// </summary>
        /// <param name="publicKey">The RSA object with the public key.</param>
        public virtual ICertificateBuilderCreateForRSAAny SetRSAPublicKey(RSA publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_rsaPublicKey = publicKey;
            return this;
        }

        /// <summary>
        /// Set the issuer certificate which is used to sign the certificate.
        /// </summary>
        /// <remarks>
        /// The issuer certificate must contain a private key which matches
        /// the selected sign algorithm if no generator is avilable.
        /// If a <see cref="X509SignatureGenerator"/> is used for signing the
        /// the issuer certificate can be set with a public key to create
        /// the X509 extensions.
        /// </remarks>
        /// <param name="issuerCertificate">The issuer certificate.</param>
        public virtual ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate)
        {
            if (issuerCertificate == null) throw new ArgumentNullException(nameof(issuerCertificate));
            m_issuerCAKeyCert = issuerCertificate;
            return this;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Validate and adjust settings to avoid creation of invalid certificates.
        /// </summary>
        protected void ValidateSettings()
        {
            // lifetime must be in range of issuer
            if (m_issuerCAKeyCert != null)
            {
                if (NotAfter > m_issuerCAKeyCert.NotAfter)
                {
                    m_notAfter = m_issuerCAKeyCert.NotAfter;
                }
                if (NotBefore < m_issuerCAKeyCert.NotBefore)
                {
                    m_notBefore = m_issuerCAKeyCert.NotBefore;
                }
            }
        }

        /// <summary>
        /// Create a new cryptographic random serial number.
        /// </summary>
        protected virtual void NewSerialNumber()
        {
            // new serial number
            var rnd = RandomNumberGenerator.Create();
            m_serialNumber = new byte[m_serialNumberLength];
            rnd.GetBytes(m_serialNumber);
            // A compliant certificate uses a positive serial number.
            m_serialNumber[m_serialNumberLength - 1] &= 0x7F;
        }
        #endregion

        #region Protected Fields
        protected bool m_isCA;
        protected int m_pathLengthConstraint;
        protected int m_serialNumberLength;
        protected bool m_presetSerial;
        protected X509Certificate2 m_issuerCAKeyCert;
        protected DateTime m_notBefore;
        protected DateTime m_notAfter;
        protected byte[] m_serialNumber;
        protected HashAlgorithmName m_hashAlgorithmName;
        protected X500DistinguishedName m_subjectName;
        protected X500DistinguishedName m_issuerName;
        protected X509ExtensionCollection m_extensions;
        protected RSA m_rsaPublicKey;
        protected int m_keySize;
#if NETSTANDARD2_1
        protected ECDsa m_ecdsaPublicKey;
        protected ECCurve? m_curve;
#endif
        #endregion
    }
}
