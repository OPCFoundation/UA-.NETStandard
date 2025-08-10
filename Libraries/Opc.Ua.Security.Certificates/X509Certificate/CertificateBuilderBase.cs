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
        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        protected CertificateBuilderBase(X500DistinguishedName subjectName)
        {
            IssuerName = SubjectName = subjectName;
            Initialize();
        }

        /// <summary>
        /// Initialize a Certificate builder.
        /// </summary>
        protected CertificateBuilderBase(string subjectName)
        {
            IssuerName = SubjectName = new X500DistinguishedName(subjectName);
            Initialize();
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        protected virtual void Initialize()
        {
            NotBefore = DateTime.UtcNow.AddDays(-1).Date;
            NotAfter = NotBefore.AddMonths(X509Defaults.LifeTime);
            HashAlgorithmName = X509Defaults.HashAlgorithmName;
            m_serialNumberLength = X509Defaults.SerialNumberLengthMin;
            m_extensions = [];
        }

        /// <inheritdoc/>
        public X500DistinguishedName SubjectName { get; }

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName { get; private set; }

        /// <inheritdoc/>
        public DateTime NotBefore { get; private set; }

        /// <inheritdoc/>
        public DateTime NotAfter { get; private set; }

        /// <inheritdoc/>
        public string SerialNumber => m_serialNumber.ToHexString(true);

        /// <inheritdoc/>
        public byte[] GetSerialNumber() { return m_serialNumber; }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName { get; private set; }

        /// <inheritdoc/>
        public X509ExtensionCollection Extensions => m_extensions;

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
                throw new ArgumentOutOfRangeException(nameof(length), "SerialNumber length out of Range");
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
                throw new ArgumentOutOfRangeException(nameof(serialNumber), "SerialNumber array exceeds supported length.");
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
            NotBefore = notBefore;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetNotAfter(DateTime notAfter)
        {
            NotAfter = notAfter;
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetLifeTime(TimeSpan lifeTime)
        {
            NotAfter = NotBefore.Add(lifeTime);
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetLifeTime(ushort months)
        {
            NotAfter = NotBefore.AddMonths(months == 0 ? X509Defaults.LifeTime : months);
            return this;
        }

        /// <inheritdoc/>
        public ICertificateBuilder SetHashAlgorithm(HashAlgorithmName hashAlgorithmName)
        {
            HashAlgorithmName = hashAlgorithmName;
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
        public virtual ICertificateBuilderCreateForRSAAny SetRSAKeySize(ushort keySize)
        {
            if (keySize == 0)
            {
                keySize = X509Defaults.RSAKeySize;
            }

            if (keySize % 1024 != 0 || keySize < X509Defaults.RSAKeySizeMin || keySize > X509Defaults.RSAKeySizeMax)
            {
                throw new ArgumentException("KeySize must be a multiple of 1024 or is not in the allowed range.", nameof(keySize));
            }

            m_keySize = keySize;
            return this;
        }

        /// <inheritdoc/>
        public virtual ICertificateBuilder AddExtension(X509Extension extension)
        {
            if (extension == null)
            {
                throw new ArgumentNullException(nameof(extension));
            }

            m_extensions.Add(extension);
            return this;
        }

#if ECC_SUPPORT
        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForECDsaAny SetECCurve(ECCurve curve)
        {
            m_curve = curve;

            // HashAlgorithmName.SHA256 is the default value
            if (HashAlgorithmName == X509Defaults.HashAlgorithmName)
            {
                SetHashAlgorithmSize(curve);
            }
            return this;
        }

        /// <inheritdoc/>
        public abstract ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(byte[] publicKey);

        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForECDsaAny SetECDsaPublicKey(ECDsa publicKey)
        {
            m_ecdsaPublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
            return this;
        }
#endif

        /// <inheritdoc/>
        public abstract ICertificateBuilderCreateForRSAAny SetRSAPublicKey(byte[] publicKey);

        /// <inheritdoc/>
        public virtual ICertificateBuilderCreateForRSAAny SetRSAPublicKey(RSA publicKey)
        {
            m_rsaPublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
            return this;
        }

        /// <inheritdoc/>
        public virtual ICertificateBuilderIssuer SetIssuer(X509Certificate2 issuerCertificate)
        {
            IssuerCAKeyCert = issuerCertificate ?? throw new ArgumentNullException(nameof(issuerCertificate));
            IssuerName = issuerCertificate.SubjectName;
            return this;
        }

#if ECC_SUPPORT

        /// <summary>
        /// Set the hash algorithm depending on the curve size
        /// </summary>
        /// <param name="curve"></param>
        private void SetHashAlgorithmSize(ECCurve curve)
        {
            if (curve.Oid.FriendlyName.CompareTo(ECCurve.NamedCurves.nistP384.Oid.FriendlyName) == 0 ||
                curve.Oid.FriendlyName.CompareTo(ECCurve.NamedCurves.brainpoolP384r1.Oid.FriendlyName) == 0 ||
                // special case for linux where friendly name could be ECDSA_P384 instead of nistP384
                (curve.Oid?.Value != null && curve.Oid.Value.CompareTo(ECCurve.NamedCurves.nistP384.Oid.Value) == 0))
            {
                SetHashAlgorithm(HashAlgorithmName.SHA384);
            }
            if (curve.Oid.FriendlyName.CompareTo(ECCurve.NamedCurves.nistP521.Oid.FriendlyName) == 0 ||
               (curve.Oid.FriendlyName.CompareTo(ECCurve.NamedCurves.brainpoolP512r1.Oid.FriendlyName) == 0))
            {
                SetHashAlgorithm(HashAlgorithmName.SHA512);
            }
        }

#endif

        /// <summary>
        /// The issuer CA certificate.
        /// </summary>
        protected X509Certificate2 IssuerCAKeyCert { get; private set; }

        /// <summary>
        /// Validate and adjust settings to avoid creation of invalid certificates.
        /// </summary>
        protected void ValidateSettings()
        {
            // lifetime must be in range of issuer
            if (IssuerCAKeyCert != null)
            {
                if (NotAfter.ToUniversalTime() > IssuerCAKeyCert.NotAfter.ToUniversalTime())
                {
                    NotAfter = IssuerCAKeyCert.NotAfter.ToUniversalTime();
                }
                if (NotBefore.ToUniversalTime() < IssuerCAKeyCert.NotBefore.ToUniversalTime())
                {
                    NotBefore = IssuerCAKeyCert.NotBefore.ToUniversalTime();
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

        /// <summary>
        /// If the certificate is a CA.
        /// </summary>
        private protected bool m_isCA;
        /// <summary>
        /// The path length constraint to sue for a CA.
        /// </summary>
        private protected int m_pathLengthConstraint;
        /// <summary>
        /// The serial number length in octets.
        /// </summary>
        private protected int m_serialNumberLength;
        /// <summary>
        /// If the serial number is preset by the user.
        /// </summary>
        private protected bool m_presetSerial;
        /// <summary>
        /// The serial number as a little endian byte array.
        /// </summary>
        private protected byte[] m_serialNumber;
        /// <summary>
        /// The collection of X509Extension to add to the certificate.
        /// </summary>
        private protected X509ExtensionCollection m_extensions;
        /// <summary>
        /// The RSA public to use when if a certificate is signed.
        /// </summary>
        private protected RSA m_rsaPublicKey;
        /// <summary>
        /// The size of a RSA key pair to create.
        /// </summary>
        private protected int m_keySize;
#if ECC_SUPPORT
        /// <summary>
        /// The ECDsa public to use when if a certificate is signed.
        /// </summary>
        private protected ECDsa m_ecdsaPublicKey;
        /// <summary>
        /// The ECCurve to use.
        /// </summary>
        private protected ECCurve? m_curve;
#endif

    }
}
