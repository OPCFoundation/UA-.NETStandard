/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.Diagnostics;
using System.Linq;
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
        /// <param name="format">The <see cref="T:System.String"/> specifying the format to use.
        /// -or-
        /// null to use the default format defined for the type of the <see cref="T:System.IFormattable"/> implementation.</param>
        /// <param name="formatProvider">The <see cref="T:System.IFormatProvider"/> to use to format the value.
        /// -or-
        /// null to obtain the numeric format information from the current locale setting of the operating system.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current instance in the specified format.
        /// </returns>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (!String.IsNullOrEmpty(format))
            {
                throw new FormatException();
            }

            return ToString();
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
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

            CertificateIdentifier id = obj as CertificateIdentifier;

            if (id == null)
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
            return base.GetHashCode();
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
            set { m_certificate = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        public Task<X509Certificate2> Find()
        {
            return Find(false);
        }

        /// <summary>
        /// Loads the private key for the certificate with an optional password.
        /// </summary>
        public Task<X509Certificate2> LoadPrivateKey(string password)
            => LoadPrivateKeyEx(password != null ? new CertificatePasswordProvider(password) : null);

        /// <summary>
        /// Loads the private key for the certificate with an optional password provider.
        /// </summary>
        public Task<X509Certificate2> LoadPrivateKeyEx(ICertificatePasswordProvider passwordProvider)
        {
            if (this.StoreType == CertificateStoreType.Directory)
            {
                using (DirectoryCertificateStore store = new DirectoryCertificateStore())
                {
                    store.Open(this.StorePath);
                    string password = passwordProvider?.GetPassword(this);
                    m_certificate = store.LoadPrivateKey(this.Thumbprint, this.SubjectName, this.CertificateType, password);
                    return Task.FromResult(m_certificate);
                }
            }

            return Find(true);
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        /// <remarks>The certificate type is used to match the signature and public key type.</remarks>
        /// <param name="needPrivateKey">if set to <c>true</c> the returned certificate must contain the private key.</param>
        /// <returns>An instance of the <see cref="X509Certificate2"/> that is embedded by this instance or find it in 
        /// the selected store pointed out by the <see cref="StorePath"/> using selected <see cref="SubjectName"/>.</returns>
        public async Task<X509Certificate2> Find(bool needPrivateKey)
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
                using (ICertificateStore store = CertificateStoreIdentifier.CreateStore(StoreType))
                {
                    store.Open(StorePath);

                    X509Certificate2Collection collection = await store.Enumerate().ConfigureAwait(false);

                    certificate = Find(collection, m_thumbprint, m_subjectName, m_certificateType, needPrivateKey);

                    if (certificate != null)
                    {
                        m_certificate = certificate;

                        if (needPrivateKey && this.StoreType == CertificateStoreType.Directory)
                        {
                            var message = new StringBuilder();
                            message.AppendLine("Loaded a certificate with private key from the directory store.");
                            message.AppendLine("Ensure to call LoadPrivateKeyEx with password provider before calling Find(true).");
                            Utils.Trace(Utils.TraceMasks.Error, message.ToString());
                        }
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
        /// Updates the object from another object (usage is not updated).
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        private void Paste(CertificateIdentifier certificate)
        {
            this.SubjectName = certificate.SubjectName;
            this.Thumbprint = certificate.Thumbprint;
            this.RawData = certificate.RawData;
            this.ValidationOptions = certificate.ValidationOptions;
            this.Certificate = certificate.Certificate;
            this.CertificateType = certificate.CertificateType;
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
        /// <param name="certificateType">The certificate type.</param>
        /// <param name="needPrivateKey">if set to <c>true</c> [need private key].</param>
        /// <returns></returns>
        public static X509Certificate2 Find(
            X509Certificate2Collection collection,
            string thumbprint,
            string subjectName,
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
        /// Returns an object that can be used to access the store containing the certificate.
        /// </summary>
        /// <returns>An instance of the <see cref="ICertificateStore"/> poined out by the current value of </returns>
        public ICertificateStore OpenStore()
        {
            ICertificateStore store = CertificateStoreIdentifier.CreateStore(this.StoreType);
            store.Open(this.StorePath);
            return store;
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
                    var keyAlgorithm = certificate.GetKeyAlgorithm();
                    if (keyAlgorithm != Oids.ECPublicKey)
                    {
                        break;
                    }

                    PublicKey encodedPublicKey = certificate.PublicKey;
                    string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);
                    switch (keyParameters)
                    {
                        // nistP256
                        case "06-08-2A-86-48-CE-3D-03-01-07": return ObjectTypeIds.EccNistP256ApplicationCertificateType;
                        // nistP384
                        case "06-05-2B-81-04-00-22": return ObjectTypeIds.EccNistP384ApplicationCertificateType;
                        // brainpoolP256r1
                        case "06-09-2B-24-03-03-02-08-01-01-07": return ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType;
                        // brainpoolP384r1
                        case "06-09-2B-24-03-03-02-08-01-01-0B": return ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType;
                        default: return NodeId.Null;
                    }

                case Oids.RsaPkcs1Sha1: return ObjectTypeIds.RsaMinApplicationCertificateType;
                case Oids.RsaPkcs1Sha256:
                case Oids.RsaPkcs1Sha384:
                case Oids.RsaPkcs1Sha512: return ObjectTypeIds.RsaSha256ApplicationCertificateType;
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
            bool result = false;

            switch (certificate.SignatureAlgorithm.Value)
            {
                case Oids.ECDsaWithSha1:
                case Oids.ECDsaWithSha384:
                case Oids.ECDsaWithSha256:
                case Oids.ECDsaWithSha512:
                    var keyAlgorithm = certificate.GetKeyAlgorithm();
                    if (keyAlgorithm != Oids.ECPublicKey)
                    {
                        break;
                    }

                    PublicKey encodedPublicKey = certificate.PublicKey;
                    string keyParameters = BitConverter.ToString(encodedPublicKey.EncodedParameters.RawData);
                    switch (keyParameters)
                    {
                        // nistP256
                        case "06-08-2A-86-48-CE-3D-03-01-07":
                        {
                            if (certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
                            {
                                result = true;
                            }
                            break;
                        }

                        // nistP384
                        case "06-05-2B-81-04-00-22":
                        {
                            if (certificateType == ObjectTypeIds.EccNistP384ApplicationCertificateType ||
                                certificateType == ObjectTypeIds.EccNistP256ApplicationCertificateType)
                            {
                                result = true;
                            }
                            break;
                        }

                        // brainpoolP256r1
                        case "06-09-2B-24-03-03-02-08-01-01-07":
                        {
                            if (certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                            {
                                result = true;
                            }
                            break;
                        }

                        // brainpoolP384r1
                        case "06-09-2B-24-03-03-02-08-01-01-0B":
                        {
                            if (certificateType == ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType ||
                                certificateType == ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType)
                            {
                                result = true;
                            }
                            break;
                        }
                    }
                    break;

                default:
                    if (certificateType == null ||
                        certificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType ||
                        certificateType == ObjectTypeIds.RsaMinApplicationCertificateType ||
                        certificateType == ObjectTypeIds.ApplicationCertificateType)
                    {
                        result = true;
                    }
                    break;
            }
            return result;
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
                case SecurityPolicies.Aes128_Sha256_nistP256:
                    result.Add(ObjectTypeIds.EccNistP256ApplicationCertificateType);
                    goto case SecurityPolicies.Aes256_Sha384_nistP384;
                case SecurityPolicies.Aes256_Sha384_nistP384:
                    result.Add(ObjectTypeIds.EccNistP384ApplicationCertificateType);
                    break;
                case SecurityPolicies.Aes128_Sha256_brainpoolP256r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
                    goto case SecurityPolicies.Aes256_Sha384_brainpoolP384r1;
                case SecurityPolicies.Aes256_Sha384_brainpoolP384r1:
                    result.Add(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
                    break;
                case SecurityPolicies.ChaCha20Poly1305_curve25519:
                    result.Add(ObjectTypeIds.EccCurve25519ApplicationCertificateType);
                    break;
                case SecurityPolicies.ChaCha20Poly1305_curve448:
                    result.Add(ObjectTypeIds.EccCurve448ApplicationCertificateType);
                    break;
                case SecurityPolicies.Https:
                    result.Add(ObjectTypeIds.HttpsCertificateType);
                    result.Add(ObjectTypeIds.ApplicationCertificateType);
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

        #region Private Methods
        /// <summary>
        /// Checks if the certificate data represents a valid X509v3 certificate.
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
            int length = 0;
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
        #endregion
    }

    #region CertificateIdentifierCollection Class
    /// <summary>
    /// A collection of CertificateIdentifier objects.
    /// </summary>
    public partial class CertificateIdentifierCollection : ICertificateStore
    {
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

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
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
        /// <summary>
        /// Opens the store at the specified location.
        /// </summary>
        /// <param name="location">The location.</param>
        /// <remarks>
        /// The syntax depends on the store implementation.
        /// </remarks>
        public void Open(string location)
        {
            // nothing to do.
        }

        /// <summary>
        /// Closes the store.
        /// </summary>
        public void Close()
        {
            // nothing to do.
        }

        /// <summary>
        /// Enumerates the certificates in the store.
        /// </summary>
        /// <remarks>
        /// Identifiers which do not refer to valid certificates are ignored.
        /// </remarks>
        /// <returns>The list of valid certificates in the store.</returns>
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

        /// <summary>
        /// Adds a certificate to the store.
        /// </summary>
        /// <param name="certificate">The certificate.</param>
        /// <param name="password">The password of the certificate.</param>
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

        /// <summary>
        /// Deletes a certificate from the store.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>True if the certificate exists.</returns>
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

        /// <summary>
        /// Finds the certificate with the specified thumprint.
        /// </summary>
        /// <param name="thumbprint">The thumbprint.</param>
        /// <returns>The matching certificate</returns>
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

        /// <summary>
        /// Whether the store support CRLs.
        /// </summary>
        public bool SupportsCRLs { get { return false; } }

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            return StatusCodes.BadNotSupported;
        }

        /// <summary>
        /// Returns the CRLs in the store.
        /// </summary>
        public List<X509CRL> EnumerateCRLs()
        {
            return new List<X509CRL>();
        }

        /// <summary>
        /// Returns the CRLs for the issuer.
        /// </summary>
        public List<X509CRL> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            return new List<X509CRL>();
        }

        /// <summary>
        /// Adds a CRL to the store.
        /// </summary>
        public void AddCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Removes a CRL from the store.
        /// </summary>
        public bool DeleteCRL(X509CRL crl)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
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

    #region CertificateTypesProvider
    /// <summary>
    /// The identifier for an X509 certificate.
    /// </summary>
    public class CertificateTypesProvider
    {
        /// <summary>
        /// 
        /// </summary>
        public CertificateTypesProvider(ApplicationConfiguration config)
        {
            m_securityConfiguration = config.SecurityConfiguration;
            m_certificateValidator = config.CertificateValidator;
        }

#if mist
        public async Task<X509Certificate2Collection> GetInstanceCertificateChain(string securityPolicy)
        {
            var certificateTypes = CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicy);
            var certificate = await GetInstanceCertificateAsync(certificateTypes, false).ConfigureAwait(false);
            return await LoadCertificateChainAsync(certificate).ConfigureAwait(false);
        }
#endif
        /// <summary>
        /// Return the instance certificate for a security policy.
        /// </summary>
        /// <param name="securityPolicyUri">The security policy Uri</param>
        public X509Certificate2 GetInstanceCertificate(string securityPolicyUri)
        {
            if (securityPolicyUri == SecurityPolicies.None)
            {
                // return the default certificate for None
                return m_securityConfiguration.ApplicationCertificate.Certificate;
            }
            var certificateTypes = CertificateIdentifier.MapSecurityPolicyToCertificateTypes(securityPolicyUri);
            foreach (var certType in certificateTypes)
            {
                var instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault(id => id.CertificateType == certType);
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.RsaSha256ApplicationCertificateType)
                {
                    instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault(id => id.CertificateType == null);
                }
                if (instanceCertificate == null &&
                    certType == ObjectTypeIds.ApplicationCertificateType)
                {
                    instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault();
                }
                if (instanceCertificate != null)
                {
                    return instanceCertificate.Certificate;
                }
            }
            return null;
        }

        /// <summary>
        /// Load the instance certificate with a private key.
        /// </summary>
        /// <param name="certificateTypes"></param>
        /// <param name="privateKey"></param>
        public Task<X509Certificate2> GetInstanceCertificateAsync(IList<NodeId> certificateTypes, bool privateKey)
        {
            foreach (var certType in certificateTypes)
            {
                var instanceCertificate = m_securityConfiguration.ApplicationCertificates.FirstOrDefault(id => id.CertificateType == certType);
                if (instanceCertificate != null)
                {
                    return instanceCertificate.Find(privateKey);
                }
            }
            return Task.FromResult<X509Certificate2>(null);
        }

        /// <summary>
        /// Loads the certificate chain of a certificate for use in a secure channel as raw byte array.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public async Task<byte[]> LoadCertificateChainRawAsync(X509Certificate2 certificate)
        {
            var instanceCertificateChain = await LoadCertificateChainAsync(certificate);
            if (instanceCertificateChain != null)
            {
                List<byte> serverCertificateChain = new List<byte>();
                for (int i = 0; i < instanceCertificateChain.Count; i++)
                {
                    serverCertificateChain.AddRange(instanceCertificateChain[i].RawData);
                }
                return serverCertificateChain.ToArray();
            }
            return null;
        }

        /// <summary>
        /// Loads the certificate chain for an application certificate.
        /// </summary>
        /// <param name="certificate">The application certificate.</param>
        public async Task<X509Certificate2Collection> LoadCertificateChainAsync(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return null;
            }

            // load certificate chain.
            var certificateChain = new X509Certificate2Collection(certificate);
            List<CertificateIdentifier> issuers = new List<CertificateIdentifier>();
            if (await m_certificateValidator.GetIssuers(certificate, issuers))
            {
                for (int i = 0; i < issuers.Count; i++)
                {
                    certificateChain.Add(issuers[i].Certificate);
                }
            }
            return certificateChain;
        }

        /// <summary>
        /// Update the security configuration of the cert type provider.
        /// </summary>
        /// <param name="securityConfiguration">The new security configuration.</param>
        public void Update(SecurityConfiguration securityConfiguration)
        {
            m_securityConfiguration = securityConfiguration;
        }

        CertificateValidator m_certificateValidator;
        SecurityConfiguration m_securityConfiguration;
    }
    #endregion
}
