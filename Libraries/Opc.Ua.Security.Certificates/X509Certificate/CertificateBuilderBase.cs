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
#if NETSTANDARD2_1
        , ICertificateBuilderCreateForECDsa
        , ICertificateBuilderECDsaPublicKey
        , ICertificateBuilderECCParameter
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
        /// Create the RSA certificate with signature.
        /// </summary>
        /// <returns>The signed certificate.</returns>
        public abstract X509Certificate2 CreateForRSA(X509SignatureGenerator generator);
#if NETSTANDARD2_1
        /// <summary>
        /// Create the RSA certificate with signature.
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
        /// <param name="length"></param>
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
        /// Set the length of the serial number.
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
            serialNumber[0] &= 0x7f;
            m_serialNumber = serialNumber;
            m_serialNumberLength = serialNumber.Length;
            m_presetSerial = true;
            return this;
        }

        public ICertificateBuilder CreateSerialNumber()
        {
            NewSerialNumber();
            m_presetSerial = true;
            return this;
        }

        public ICertificateBuilder SetNotBefore(DateTime notBefore)
        {
            m_notBefore = notBefore;
            return this;
        }

        public ICertificateBuilder SetNotAfter(DateTime notAfter)
        {
            m_notAfter = notAfter;
            return this;
        }

        public ICertificateBuilder SetLifeTime(TimeSpan lifeTime)
        {
            m_notAfter = m_notBefore.Add(lifeTime);
            return this;
        }

        public ICertificateBuilder SetLifeTime(ushort months)
        {
            m_notAfter = m_notBefore.AddMonths(months == 0 ? Defaults.LifeTime : (int)months);
            return this;
        }

        public ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            if (hashAlgorithmName == null) throw new ArgumentNullException(nameof(hashAlgorithmName));
            m_hashAlgorithmName = hashAlgorithmName;
            return this;
        }

        public ICertificateBuilder SetCAConstraint(int pathLengthConstraint = -1)
        {
            m_isCA = true;
            m_pathLengthConstraint = pathLengthConstraint;
            m_serialNumberLength = Defaults.SerialNumberLengthMax;
            return this;
        }

        public virtual ICertificateBuilderCreateForRSA SetRSAKeySize(int keySize)
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


        public virtual ICertificateBuilder AddExtension(X509Extension extension)
        {
            m_extensions.Add(extension);
            return this;
        }

#if NETSTANDARD2_1
        public virtual ICertificateBuilderCreateForECDsa SetECCurve(ECCurve curve)
        {
            m_curve = curve;
            return this;
        }

        public abstract ICertificateBuilderCreateForECDsa SetECDsaPublicKey(byte[] publicKey);

        public virtual ICertificateBuilderCreateForECDsa SetECDsaPublicKey(ECDsa publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_ecdsaPublicKey = publicKey;
            return this;
        }
#endif
        public abstract ICertificateBuilderCreateForRSA SetRSAPublicKey(byte[] publicKey);

        public virtual ICertificateBuilderCreateForRSA SetRSAPublicKey(RSA publicKey)
        {
            if (publicKey == null) throw new ArgumentNullException(nameof(publicKey));
            m_rsaPublicKey = publicKey;
            return this;
        }

#if NETSTANDAR2_1
        public virtual ICertificateBuilderCreateForECDsa SetECCurve(ECCurve curve)
        {
            m_curve = curve;
            return this;
        }
#endif

        public virtual ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate)
        {
            if (issuerCertificate == null) throw new ArgumentNullException(nameof(issuerCertificate));
            m_issuerCAKeyCert = issuerCertificate;
            return this;
        }
        #endregion

        #region Protected Methods
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

        protected abstract void NewSerialNumber();
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
