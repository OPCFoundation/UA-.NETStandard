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
            m_notAfter = NotBefore.AddMonths(X509Defaults.LifeTime);
            m_hashAlgorithmName = X509Defaults.HashAlgorithmName;
            m_serialNumberLength = X509Defaults.SerialNumberLengthMin;
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
        /// <inheritdoc/>
        public abstract X509Certificate2 CreateForRSA();

        /// <inheritdoc/>
        public abstract X509Certificate2 CreateForRSA(X509SignatureGenerator generator);

#if ECC_SUPPORT
        /// <inheritdoc/>
        public abstract X509Certificate2 CreateForECDsa();

        /// <inheritdoc/>
        public abstract X509Certificate2 CreateForECDsa(X509SignatureGenerator generator);
#endif
        /// <inheritdoc/>
        public ICertificateBuilder SetSerialNumberLength(int length)
        {
            if (length > X509Defaults.SerialNumberLengthMax || length == 0)
            {
                throw new ArgumentOutOfRangeException("SerialNumber length out of Range");
            }
            m_serialNumberLength = length;
            m_presetSerial = false;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetSerialNumber(byte[] serialNumber)
        {
            if (serialNumber.Length > X509Defaults.SerialNumberLengthMax ||
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

        /// <inheritdoc/>
        public ICertificateBuilder CreateSerialNumber()
        {
            NewSerialNumber();
            m_presetSerial = true;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetNotBefore(DateTime notBefore)
        {
            m_notBefore = notBefore;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetNotAfter(DateTime notAfter)
        {
            m_notAfter = notAfter;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetLifeTime(TimeSpan lifeTime)
        {
            m_notAfter = m_notBefore.Add(lifeTime);
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetLifeTime(ushort months)
        {
            m_notAfter = m_notBefore.AddMonths(months == 0 ? X509Defaults.LifeTime : (int)months);
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == null) throw new ArgumentNullException(nameof(hashAlgorithmName));
            m_hashAlgorithmName = hashAlgorithmName;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetCAConstraint(int pathLengthConstraint = -1)
        {
            m_isCA = true;
            m_pathLengthConstraint = pathLengthConstraint;
            m_serialNumberLength = X509Defaults.SerialNumberLengthMax;
            return this;
        }

        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForRSAAny SetRSAKeySize(int keySize)
        {
            if (keySize == 0)
            {
                keySize = X509Defaults.RSAKeySize;
            }

            if (keySize % 1024 != 0 || keySize < X509Defaults.RSAKeySizeMin || keySize > X509Defaults.RSAKeySizeMax)
            {
                throw new ArgumentException(nameof(keySize), "KeySize must be a multiple of 1024 or is not in the allowed range.");
            }

            m_keySize = keySize;
            return this;
        }

        /// <inheritdoc/>
        public virtual ICertificateBuilder AddExtension(X509Extension extension)
        {
            if (extension == null) throw new ArgumentNullException(nameof(extension));
            m_extensions.Add(extension);
            return this;
        }

#if ECC_SUPPORT
        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForECDsaAny SetECCurve(ECCurve curve)
        {
            m_curve = curve;
            return this;
        }

        /// <inheritdoc/>
        public abstract ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(byte[] publicKey);

        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(ECDsa publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_ecdsaPublicKey = publicKey;
            return this;
        }
#endif

        /// <inheritdoc/>
        public abstract ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey);

        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForRSAAny SetRSAPublicKey(RSA publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_rsaPublicKey = publicKey;
            return this;
        }

        /// <inheritdoc/>
        public virtual ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate)
        {
            if (issuerCertificate == null) throw new ArgumentNullException(nameof(issuerCertificate));
            m_issuerCAKeyCert = issuerCertificate;
            m_issuerName = issuerCertificate.SubjectName;
            return this;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// The issuer CA certificate.
        /// </summary>
        protected X509Certificate2 IssuerCAKeyCert => m_issuerCAKeyCert;

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
            using (var rnd = RandomNumberGenerator.Create())
            {
                m_serialNumber = new byte[m_serialNumberLength];
                rnd.GetBytes(m_serialNumber);
            }
            // A compliant certificate uses a positive serial number.
            m_serialNumber[m_serialNumberLength - 1] &= 0x7F;
        }
        #endregion

        #region Protected Fields
        /// <summary>
        /// If the certificate is a CA.
        /// </summary>
        protected bool m_isCA;
        /// <summary>
        /// The path length constraint to sue for a CA.
        /// </summary>
        protected int m_pathLengthConstraint;
        /// <summary>
        /// The serial number length in octets.
        /// </summary>
        protected int m_serialNumberLength;
        /// <summary>
        /// If the serial number is preset by the user.
        /// </summary>
        protected bool m_presetSerial;
        /// <summary>
        /// The serial number as a little endian byte array.
        /// </summary>
        protected byte[] m_serialNumber;
        /// <summary>
        /// The collection of X509Extension to add to the certificate.
        /// </summary>
        protected X509ExtensionCollection m_extensions;
        /// <summary>
        /// The RSA public to use when if a certificate is signed.
        /// </summary>
        protected RSA m_rsaPublicKey;
        /// <summary>
        /// The size of a RSA key pair to create.
        /// </summary>
        protected int m_keySize;
#if ECC_SUPPORT
        /// <summary>
        /// The ECDsa public to use when if a certificate is signed.
        /// </summary>
        protected ECDsa m_ecdsaPublicKey;
        /// <summary>
        /// The ECCurve to use.
        /// </summary>
        protected ECCurve? m_curve;
#endif
        #endregion

        #region Private Fields
        private X509Certificate2 m_issuerCAKeyCert;
        private DateTime m_notBefore;
        private DateTime m_notAfter;
        private HashAlgorithmName m_hashAlgorithmName;
        private X500DistinguishedName m_subjectName;
        private X500DistinguishedName m_issuerName;
        #endregion
    }
}
