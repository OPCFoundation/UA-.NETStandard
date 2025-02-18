/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// The identifier for an X509 certificate.
    /// </summary>
    public partial class CertificateIdentifier : IFormattable
    {
        #region IFormattable Members
        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        /// <param name="format">The <see cref="String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null) throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            return ToString();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a <see cref="String"/> that represents the current <see cref="Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="String"/> that represents the current <see cref="Object"/>.
        /// </returns>
        public override string ToString()
        {
            if (m_certificate != null)
            {
                return GetDisplayName(m_certificate);
            }

            if (m_subjectName != null)
            {
                return m_subjectName;
            }

            return m_thumbprint;
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            if (!(obj is CertificateIdentifier id))
            {
                return false;
            }

            if (m_certificate != null && id.m_certificate != null)
            {
                return m_certificate.Thumbprint == id.m_certificate.Thumbprint;
            }

            if (Thumbprint == id.Thumbprint)
            {
                return true;
            }

            if (m_storeLocation != id.m_storeLocation)
            {
                return false;
            }

            if (m_storeName != id.m_storeName)
            {
                return false;
            }

            if (SubjectName != id.SubjectName)
            {
                return false;
            }

            if (CertificateType != id.CertificateType)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a suitable hash code.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Thumbprint, m_storeLocation, m_storeName, SubjectName, CertificateType);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets or sets the validation options.
        /// </summary>
        /// <value>
        /// The validation options that can be used to suppress certificate validation errors.
        /// </value>
        public CertificateValidationOptions ValidationOptions
        {
            get { return m_validationOptions; }
            set { m_validationOptions = value; }
        }

        /// <summary>
        /// Gets or sets the actual certificate.
        /// </summary>
        /// <value>The X509 certificate used by this instance.</value>
        public X509Certificate2 Certificate
        {
            get { return m_certificate; }
            set
            {
                m_certificate = value;
                if (m_certificate != null)
                {
                    m_certificateType = GetCertificateType(m_certificate);
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        public Task<X509Certificate2> Find(string applicationUri = null)
        {
            return Find(false, applicationUri);
        }

        /// <summary>
        /// Loads the private key for the certificate with an optional password.
        /// </summary>
        public Task<X509Certificate2> LoadPrivateKey(string password, string applicationUri = null)
            => LoadPrivateKeyEx(password != null ? new CertificatePasswordProvider(password) : null, applicationUri);

        /// <summary>
        /// Loads the private key for the certificate with an optional password provider.
        /// </summary>
        public async Task<X509Certificate2> LoadPrivateKeyEx(ICertificatePasswordProvider passwordProvider, string applicationUri = null)
        {
            if (this.StoreType != CertificateStoreType.X509Store)
            {
                var certificateStoreIdentifier = new CertificateStoreIdentifier(this.StorePath, this.StoreType, false);
                using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                {
                    if (store?.SupportsLoadPrivateKey == true)
                    {
                        string password = passwordProvider?.GetPassword(this);
                        m_certificate = await store.LoadPrivateKey(this.Thumbprint, this.SubjectName, null, this.CertificateType, password).ConfigureAwait(false);

                        //find certificate by applicationUri instead of subjectName, as the subjectName could have changed after a certificate update
                        if (m_certificate == null && !string.IsNullOrEmpty(applicationUri))
                        {
                            m_certificate = await store.LoadPrivateKey(this.Thumbprint, null, applicationUri, this.CertificateType, password).ConfigureAwait(false);
                        }

                        return m_certificate;
                    }
                }
                return null;
            }
            return await Find(true).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        /// <remarks>The certificate type is used to match the signature and public key type.</remarks>
        /// <param name="needPrivateKey">if set to <c>true</c> the returned certificate must contain the private key.</param>
        /// <param name="applicationUri">the application uri in the extensions of the certificate.</param>
        /// <returns>An instance of the <see cref="X509Certificate2"/> that is embedded by this instance or find it in 
        /// the selected store pointed out by the <see cref="StorePath"/> using selected <see cref="SubjectName"/> or if specified applicationUri.</returns>
        public async Task<X509Certificate2> Find(bool needPrivateKey, string applicationUri = null)
        {
            X509Certificate2 certificate = null;

            // check if the entire certificate has been specified.
            if (m_certificate != null && (!needPrivateKey || m_certificate.HasPrivateKey))
            {
                certificate = m_certificate;
            }
            else
            {
                // open store.
                var certificateStoreIdentifier = new CertificateStoreIdentifier(StorePath, false);
                using (ICertificateStore store = certificateStoreIdentifier.OpenStore())
                {
                    if (store == null)
                    {
                        return null;
                    }

                    X509Certificate2Collection collection = await store.Enumerate().ConfigureAwait(false);

                    certificate = Find(collection, m_thumbprint, m_subjectName, applicationUri, m_certificateType, needPrivateKey);

                    if (certificate != null)
                    {
                        if (needPrivateKey && store.SupportsLoadPrivateKey)
                        {
                            var message = new StringBuilder();
                            message.AppendLine("Loaded a certificate with private key from store {0}.");
                            message.AppendLine("Ensure to call LoadPrivateKeyEx with password provider before calling Find(true).");
                            Utils.LogWarning(message.ToString(), StoreType);
                        }

                        m_certificate = certificate;
                    }
                }
            }

            // use the single instance in the certificate cache.
            if (needPrivateKey)
            {
                certificate = m_certificate = CertificateFactory.Load(certificate, true);
            }

            return certificate;
        }

        /// <summary>
        /// Returns a display name for a certificate.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <returns>
        /// A string containg FriendlyName of the <see cref="X509Certificate2"/> or created using Subject of 
        /// the <see cref="X509Certificate2"/>.
        /// </returns>
        private static string GetDisplayName(X509Certificate2 certificate)
        {
            if (!String.IsNullOrEmpty(certificate.FriendlyName))
            {
                return certificate.FriendlyName;
            }

            string name = certificate.Subject;

            // find the common name delimiter.
            int index = name.IndexOf("CN", StringComparison.Ordinal);

            if (index == -1)
            {
                return name;
            }

            StringBuilder buffer = new StringBuilder(name.Length);

            // skip characters until finding the '=' character
            for (int ii = index + 2; ii < name.Length; ii++)
            {
                if (name[ii] == '=')
                {
                    index = ii + 1;
                    break;
                }
            }

            // skip whitespace.
            for (int ii = index; ii < name.Length; ii++)
            {
                if (!Char.IsWhiteSpace(name[ii]))
                {
                    index = ii;
                    break;
                }
            }

            // read the common until finding a ','.
            for (int ii = index; ii < name.Length; ii++)
            {
                if (name[ii] == ',')
                {
                    break;
                }

                buffer.Append(name[ii]);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Finds a certificate in the specified collection.
        /// </summary>
        /// <param name="collection">The collection.</param>
        /// <param name="thumbprint">The thumbprint of the certificate.</param>
        /// <param name="subjectName">Subject name of the certificate.</param>
        /// <param name="applicationUri">ApplicationUri in the SubjectAltNameExtension of the certificate.</param>
        /// <param name="certificateType">The certificate type.</param>
        /// <param name="needPrivateKey">if set to <c>true</c> [need private key].</param>
        /// <returns></returns>
        public static X509Certificate2 Find(
            X509Certificate2Collection collection,
            string thumbprint,
            string subjectName,
            string applicationUri,
            NodeId certificateType,
            bool needPrivateKey)
        {
            // find by thumbprint.
            if (!String.IsNullOrEmpty(thumbprint))
            {
                collection = collection.Find(X509FindType.FindByThumbprint, thumbprint, false);

                foreach (X509Certificate2 certificate in collection)
                {
                    if (!needPrivateKey || certificate.HasPrivateKey)
                    {
                        if (String.IsNullOrEmpty(subjectName))
                        {
                            return certificate;
                        }

                        List<string> subjectName2 = X509Utils.ParseDistinguishedName(subjectName);

                        if (X509Utils.CompareDistinguishedName(certificate, subjectName2))
                        {
                            return certificate;
                        }
                    }
                }

                return null;
            }
            // find by subject name.
            if (!String.IsNullOrEmpty(subjectName))
            {
                List<string> subjectName2 = X509Utils.ParseDistinguishedName(subjectName);

                foreach (X509Certificate2 certificate in collection)
                {
                    if (ValidateCertificateType(certificate, certificateType) &&
                        X509Utils.CompareDistinguishedName(certificate, subjectName2))
                    {
                        if (!needPrivateKey || certificate.HasPrivateKey)
                        {
                            return certificate;
                        }
                    }
                }

                collection = collection.Find(X509FindType.FindBySubjectName, subjectName, false);

                foreach (X509Certificate2 certificate in collection)
                {
                    if (ValidateCertificateType(certificate, certificateType) &&
                        (!needPrivateKey || certificate.HasPrivateKey))
                    {
                        return certificate;
                    }
                }
            }

            //find by application uri
            if (!string.IsNullOrEmpty(applicationUri))
            {
                foreach (X509Certificate2 certificate in collection)
                {
                    if (applicationUri == X509Utils.GetApplicationUriFromCertificate(certificate) &&
                    ValidateCertificateType(certificate, certificateType) &&
                    (!needPrivateKey || certificate.HasPrivateKey))
                    {
                        return certificate;
                    }
                }
            }

            // certificate not found.
            return null;
        }

        /// <summary>
        /// Creates a DER blob from a certificate with zero or more supporting certificates.
        /// </summary>
        /// <param name="certificates">The certificates list to be returned as raw data.</param>
        /// <returns>
        /// A DER blob containing zero or more certificates.
        /// </returns>
        /// <exception cref="CryptographicException">If the <paramref name="certificates"/> is null or empty.</exception>
        [Obsolete("Use Utils.CreateCertificateChainBlob instead")]
        public static byte[] CreateBlob(IList<X509Certificate2> certificates)
        {
            if (certificates == null || certificates.Count == 0)
            {
                throw new CryptographicException("Primary certificate has not been provided.");
            }

            // copy the primary certificate.
            X509Certificate2 certificate = certificates[0];
            byte[] blobData = certificate.RawData;

            // check for any supporting certificates.
            if (certificates.Count > 1)
            {
                List<byte[]> additionalData = new List<byte[]>(certificates.Count - 1);
                int length = blobData.Length;

                for (int ii = 1; ii < certificates.Count; ii++)
                {
                    byte[] bytes = certificates[ii].RawData;
                    length += bytes.Length;
                    additionalData.Add(bytes);
                }

                // append the supporting certificates to the raw data.
                byte[] rawData = new byte[length];
                Array.Copy(blobData, rawData, blobData.Length);

                length = blobData.Length;

                for (int ii = 0; ii < additionalData.Count; ii++)
                {
                    byte[] bytes = additionalData[ii];
                    Array.Copy(bytes, 0, rawData, length, bytes.Length);
                    length += bytes.Length;
                }

                blobData = rawData;
            }

            return blobData;
        }

        /// <summary>
        /// Parses a blob with a list of DER encoded certificates.
        /// </summary>
        /// <param name="encodedData">The encoded data.</param>
        /// <returns>
        /// An object of <see cref="X509Certificate2Collection"/> containing <see cref="X509Certificate2"/>
        /// certificates created from a buffer with DER encoded certificate
        /// </returns>
        /// <remarks>
        /// Any supporting certificates found in the buffer are processed as well.
        /// </remarks>
        [Obsolete("Use Utils.ParseCertificateChainBlob instead")]
        public static X509Certificate2Collection ParseBlob(byte[] encodedData)
        {
            if (!IsValidCertificateBlob(encodedData))
            {
                throw new CryptographicException("Primary certificate in blob is not valid.");
            }

            X509Certificate2Collection collection = new X509Certificate2Collection();
            X509Certificate2 certificate = CertificateFactory.Create(encodedData, true);
            collection.Add(certificate);

            byte[] rawData = encodedData;
            byte[] data = certificate.RawData;

            int processedBytes = data.Length;

            if (encodedData.Length < processedBytes)
            {
                byte[] buffer = new byte[encodedData.Length - processedBytes];

                do
                {
                    Array.Copy(encodedData, processedBytes, buffer, 0, encodedData.Length - processedBytes);

                    if (!IsValidCertificateBlob(buffer))
                    {
                        throw new CryptographicException("Supporting certificate in blob is not valid.");
                    }

                    X509Certificate2 issuerCertificate = CertificateFactory.Create(buffer, true);
                    collection.Add(issuerCertificate);
                    data = issuerCertificate.RawData;
                    processedBytes += data.Length;
                }
                while (processedBytes < encodedData.Length);
            }

            return collection;
        }

        /// <summary>
        /// Returns an object to access the store containing the certificate.
        /// </summary>
        /// <remarks>
        /// Opens a store which contains public and private keys.
        /// </remarks>
        /// <returns>A disposable instance of the <see cref="ICertificateStore"/>.</returns>
        public ICertificateStore OpenStore()
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(this.StoreType);
            store.Open(this.StorePath, false);
            return store;
        }

        /// <summary>
        /// Retrieves the minimum accepted key size given the security configuration
        /// </summary>
        /// <param name="securityConfiguration"></param>
        /// <returns></returns>
        public ushort GetMinKeySize(SecurityConfiguration securityConfiguration)
        {
            if (CertificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                 CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                 securityConfiguration.IsDeprecatedConfiguration) // Deprecated configurations are implicitly RSA
            {
                return securityConfiguration.MinimumCertificateKeySize;
            }
            else
            {
                // non RSA
                return 0;
            }
        }


        /// <summary>
        /// Get the OPC UA CertificateType.
        /// </summary>
        /// <param name="certificate">The certificate with a signature.</param>
        public static NodeId GetCertificateType(X509Certificate2 certificate)
        {
            switch (certificate.SignatureAlgorithm.Value)
            {
                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha512:
                    return EccUtils.GetEccCertificateTypeId(certificate);

                case Oids.RsaPkcs1Sha256:
                case Oids.RsaPkcs1Sha384:
                case Oids.RsaPkcs1Sha512:
                    return ObjectTypeIds.RsaSha256ApplicationCertificateType;
                case Oids.RsaPkcs1Sha1:
                    return ObjectTypeIds.RsaMinApplicationCertificateType;
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Validate if the certificate matches the CertificateType.
        /// </summary>
        /// <param name="certificate">The certificate with a signature.</param>
        /// <param name="certificateType">The NodeId of the certificate type.</param>
        public static bool ValidateCertificateType(X509Certificate2 certificate, NodeId certificateType)
        {
            if (certificateType == null)
            {
                return true;
            }
            switch (certificate.SignatureAlgorithm.Value)
            {
                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha512:
                    var certType = EccUtils.GetEccCertificateTypeId(certificate);
                    if (certType.IsNullNodeId)
                    {
                        return false;
                    }
                    else if (certType == certificateType)
                    {
                        return true;
                    }


                    // not needed: An end entity Certificate shall use P-256.
                    // http://opcfoundation.org/UA/SecurityPolicy#ECC_nistP256
                    //if (certType == ObjectTypeIds.EccNistP384ApplicationCertificateType &&
                    //    certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
                    //{
                    //    return true;
                    //}

                    // not needed: An end entity Certificate shall use P256r1.
                    // http://opcfoundation.org/UA/SecurityPolicy#ECC_brainpoolP256r1
                    //if (certType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType &&
                    //    certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                    //{
                    //    return true;
                    //}

                    break;

                default:
                    // TODO: check SHA1/key size
                    if (certificateType == null ||
                        certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                        certificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                        certificateType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        return true;
                    }
                    break;
            }
            return false;
        }

        /// <summary>
        /// Map a security policy to a list of supported certificate types.
        /// </summary>
        /// <param name="securityPolicy"></param>
        public static IList<NodeId> MapSecurityPolicyToCertificateTypes(string securityPolicy)
        {
            var result = new List<NodeId>();
            switch (securityPolicy)
            {
                case SecurityPolicies.Basic128Rsa15:
                case SecurityPolicies.Basic256:
                    result.Add(ObjectTypeIds.RsaMinApplicationCertificateType);
                    goto case SecurityPolicies.Basic256Sha256;
                case SecurityPolicies.Basic256Sha256:
                case SecurityPolicies.Aes128_Sha256_RsaOaep:
                case SecurityPolicies.Aes256_Sha256_RsaPss:
                    result.Add(ObjectTypeIds.RsaSha256ApplicationCertificateType);
                    break;
                case SecurityPolicies.ECC_nistP256:
                    result.Add(ObjectTypeIds.EccNistP256ApplicationCertificateType);
                    goto case SecurityPolicies.ECC_nistP384;
                case SecurityPolicies.ECC_nistP384:
                    result.Add(ObjectTypeIds.EccNistP384ApplicationCertificateType);
                    break;
                case SecurityPolicies.ECC_brainpoolP256r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
                    goto case SecurityPolicies.ECC_brainpoolP384r1;
                case SecurityPolicies.ECC_brainpoolP384r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
                    break;
                case SecurityPolicies.ECC_curve25519:
                    result.Add(ObjectTypeIds.EccCurve25519ApplicationCertificateType);
                    break;
                case SecurityPolicies.ECC_curve448:
                    result.Add(ObjectTypeIds.EccCurve448ApplicationCertificateType);
                    break;
                case SecurityPolicies.Https:
                    result.Add(ObjectTypeIds.HttpsCertificateType);
                    break;
                default:
                    break;
            }
            return result;
        }


        /// <summary>
        /// Disposes and deletes the reference to the certificate.
        /// </summary>
        public void DisposeCertificate()
        {
            var certificate = m_certificate;
            m_certificate = null;
            Utils.SilentDispose(certificate);
        }
        #endregion

        #region Private Members
        /// <summary>
        /// The tags of the supported certificate types.
        /// </summary>
        private static Dictionary<uint, string> m_supportedCertificateTypes = new Dictionary<uint, string>() {
            { ObjectTypes.EccNistP256ApplicationCertificateType, "NistP256"},
            { ObjectTypes.EccNistP384ApplicationCertificateType, "NistP384"},
            { ObjectTypes.EccBrainpoolP256r1ApplicationCertificateType, "BrainpoolP256r1"},
            { ObjectTypes.EccBrainpoolP384r1ApplicationCertificateType, "BrainpoolP384r1"},
            { ObjectTypes.EccCurve25519ApplicationCertificateType, "Curve25519"},
            { ObjectTypes.EccCurve448ApplicationCertificateType, "Curve448"},
            { ObjectTypes.RsaSha256ApplicationCertificateType, "RsaSha256"},
            { ObjectTypes.RsaMinApplicationCertificateType, "RsaMin"},
            { ObjectTypes.ApplicationCertificateType, "Rsa"},
        };
        #endregion

        #region Private Methods
        /// <summary>
        /// Checks if the certificate data represents a valid X509v3 certificate header.
        /// </summary>
        /// <param name="rawData">The raw data of a <see cref="X509Certificate2"/> object.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="rawData"/> is a valid certificate BLOB; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsValidCertificateBlob(byte[] rawData)
        {
            // check for header.
            if (rawData == null || rawData.Length < 4)
            {
                return false;
            }

            // check for ASN.1 header.
            if (rawData[0] != 0x30)
            {
                return false;
            }

            // extract length.
            int length;
            byte octet = rawData[1];

            // check for short for encoding.
            if ((octet & 0x80) == 0)
            {
                length = octet & 0x7F;

                if (2 + length < rawData.Length)
                {
                    return false;
                }

                return true;
            }

            // extract number of bytes for the length.
            int lengthBytes = octet & 0x7F;

            if (rawData.Length <= 2 + lengthBytes)
            {
                return false;
            }

            // check for unexpected negative number.
            if ((rawData[2] & 0x80) != 0)
            {
                return false;
            }

            // extract length.
            length = rawData[2];

            for (int ii = 0; ii < lengthBytes - 1; ii++)
            {
                length <<= 8;
                length |= rawData[ii + 3];
            }

            if (2 + lengthBytes + length > rawData.Length)
            {
                return false;
            }

            // potentially valid.
            return true;
        }

        /// <summary>
        /// The tags of the supported certificate types used to encode the NodeId coressponding to existing value.
        /// </summary>
        // TODO: remove if not used
        private static string EncodeCertificateType(NodeId certificateType)
        {
            if (certificateType == null)
            {
                return null;
            }

            foreach (KeyValuePair<uint, string> supportedCertificateType in m_supportedCertificateTypes)
            {
                if (supportedCertificateType.Key == (uint)certificateType.Identifier)
                {
                    return supportedCertificateType.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// The tags of the supported certificate types used to decode the NodeId coressponding to existing value.
        /// </summary>
        // TODO: remove if not used
        private static NodeId DecodeCertificateType(string certificateType)
        {
            if (certificateType == null)
            {
                return null;
            }

            foreach (var supportedCertificateType in m_supportedCertificateTypes)
            {
                if (supportedCertificateType.Value == certificateType)
                {
                    return new NodeId(supportedCertificateType.Key);
                }
            }

            return null;
        }
        #endregion
    }

    #region CertificateIdentifierCollection Class
    /// <summary>
    /// A collection of CertificateIdentifier objects.
    /// </summary>
    public partial class CertificateIdentifierCollection : ICertificateStore, ICloneable
    {

        #region ICloneable
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            CertificateIdentifierCollection collection = new CertificateIdentifierCollection();

            for (int ii = 0; ii < this.Count; ii++)
            {
                collection.Add((CertificateIdentifier)Utils.Clone(this[ii]));
            }

            return collection;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // nothing to do.
            }
        }
        #endregion

        #region ICertificateStore Members
        /// <inheritdoc/>
        /// <remarks>
        /// The certificate identifier store ignores the location.
        /// </remarks>
        public void Open(string location, bool noPrivateKeys)
        {
            // nothing to do.
        }

        /// <inheritdoc/>
        public void Close()
        {
            // nothing to do.
        }

        /// <inheritdoc/>
        public string StoreType => string.Empty;

        /// <inheritdoc/>
        public string StorePath => string.Empty;

        /// <inheritdoc/>
        public bool NoPrivateKeys => true;

        /// <inheritdoc/>
        public async Task<X509Certificate2Collection> Enumerate()
        {
            X509Certificate2Collection collection = new X509Certificate2Collection();

            for (int ii = 0; ii < this.Count; ii++)
            {
                X509Certificate2 certificate = await this[ii].Find(false).ConfigureAwait(false);

                if (certificate != null)
                {
                    collection.Add(certificate);
                }
            }

            return collection;
        }

        /// <inheritdoc/>
        public async Task Add(X509Certificate2 certificate, string password = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            for (int ii = 0; ii < this.Count; ii++)
            {
                X509Certificate2 current = await this[ii].Find(false).ConfigureAwait(false);

                if (current != null && current.Thumbprint == certificate.Thumbprint)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEntryExists,
                        "A certificate with the specified thumbprint already exists. Subject={0}, Thumbprint={1}",
                        certificate.SubjectName,
                        certificate.Thumbprint);
                }
            }

            this.Add(new CertificateIdentifier(certificate));
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string thumbprint)
        {
            if (String.IsNullOrEmpty(thumbprint))
            {
                return false;
            }

            for (int ii = 0; ii < this.Count; ii++)
            {
                X509Certificate2 certificate = await this[ii].Find(false).ConfigureAwait(false);

                if (certificate != null && certificate.Thumbprint == thumbprint)
                {
                    this.RemoveAt(ii);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public async Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            if (String.IsNullOrEmpty(thumbprint))
            {
                return null;
            }

            for (int ii = 0; ii < this.Count; ii++)
            {
                X509Certificate2 certificate = await this[ii].Find(false).ConfigureAwait(false);

                if (certificate != null && certificate.Thumbprint == thumbprint)
                {
                    return new X509Certificate2Collection { certificate };
                }
            }

            return new X509Certificate2Collection();
        }

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => false;

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string password)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        public Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string applicationUri, NodeId certificateType, string password)
        {
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs => false;

        /// <inheritdoc/>
        public Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return Task.FromResult((StatusCode)StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLs()
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            return Task.FromResult(new X509CRLCollection());
        }

        /// <inheritdoc/>
        public Task AddCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public Task AddRejected(X509Certificate2Collection certificates, int maxCertificates)
        {
            return Task.CompletedTask;
        }
        #endregion
    }
    #endregion

    #region CertificateValidationOptions Class
    /// <summary>
    /// Options that can be used to suppress certificate validation errors.
    /// </summary>
    [Flags]
    public enum CertificateValidationOptions
    {
        /// <summary>
        /// Use the default options.
        /// </summary>
        Default = 0x0,

        /// <summary>
        /// Ignore expired certificates.
        /// </summary>
        SuppressCertificateExpired = 0x1,

        /// <summary>
        /// Ignore mismatches between the URL and the DNS names in the certificate.
        /// </summary>
        SuppressHostNameInvalid = 0x2,

        /// <summary>
        /// Ignore errors when it is not possible to check the revocation status for a certificate.
        /// </summary>
        SuppressRevocationStatusUnknown = 0x8,

        /// <summary>
        /// Attempt to check the revocation status online.
        /// </summary>
        CheckRevocationStatusOnline = 0x10,

        /// <summary>
        /// Attempt to check the revocation status offline.
        /// </summary>
        CheckRevocationStatusOffine = 0x20,

        /// <summary>
        /// Never trust the certificate.
        /// </summary>
        TreatAsInvalid = 0x40
    }
    #endregion
}
