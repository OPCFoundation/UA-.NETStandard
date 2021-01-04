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
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets or sets the actual certificate.
        /// </summary>
        /// <value>The X509 certificate used by this instance.</value>
        public X509Certificate2 Certificate
        {
            get { return m_certificate;  }
            set { m_certificate = value; }
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        public async Task<X509Certificate2> Find()
        {
            return await Find(false);
        }

        /// <summary>
        /// Loads the private key for the certificate with an optional password.
        /// </summary>
        public async Task<X509Certificate2> LoadPrivateKey(String password)
        {
            if (this.StoreType == CertificateStoreType.Directory)
            {                
                using (DirectoryCertificateStore store = new DirectoryCertificateStore())
                {
                    store.Open(this.StorePath);
                    m_certificate = store.LoadPrivateKey(this.Thumbprint, this.SubjectName, password);
                    return m_certificate;
                }
            }
            
            return await Find(true);
        }

        /// <summary>
        /// Finds a certificate in a store.
        /// </summary>
        /// <param name="needPrivateKey">if set to <c>true</c> the returned certificate must contain the private key.</param>
        /// <returns>An instance of the <see cref="X509Certificate2"/> that is emebeded by this instance or find it in 
        /// the selected strore pointed out by the <see cref="StorePath"/> using selected <see cref="SubjectName"/>.</returns>
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

                    X509Certificate2Collection collection = await store.Enumerate();

                    certificate = Find(collection, m_thumbprint, m_subjectName, needPrivateKey);

                    if (certificate != null)
                    {
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
        /// <param name="needPrivateKey">if set to <c>true</c> [need private key].</param>
        /// <returns></returns>
        public static X509Certificate2 Find(X509Certificate2Collection collection, string thumbprint, string subjectName, bool needPrivateKey)
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
                    if (X509Utils.CompareDistinguishedName(certificate, subjectName2))
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
                    if (!needPrivateKey || certificate.HasPrivateKey)
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
                byte[] buffer = new byte[encodedData.Length-processedBytes];

                do
                {
                    Array.Copy(encodedData, processedBytes, buffer, 0, encodedData.Length-processedBytes);

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

                if (2+length < rawData.Length)
                {
                    return false;
                }

                return true;
            }

            // extract number of bytes for the length.
            int lengthBytes = octet & 0x7F;
            
            if (rawData.Length <= 2+lengthBytes)
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

            for (int ii = 0; ii < lengthBytes-1; ii++)
            {
                length <<= 8;
                length |= rawData[ii+3];
            }

            if (2+lengthBytes+length > rawData.Length)
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
                X509Certificate2 certificate = await this[ii].Find(false);

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
                X509Certificate2 current = await this[ii].Find(false);

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
                X509Certificate2 certificate = await this[ii].Find(false);

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
                X509Certificate2 certificate = await this[ii].Find(false);

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
}
