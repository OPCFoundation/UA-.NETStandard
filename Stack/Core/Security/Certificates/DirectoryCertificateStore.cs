/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Windows.Storage;

namespace Opc.Ua
{
    /// <summary>
    /// Provides access to a simple file based certificate store.
    /// </summary>
    public class DirectoryCertificateStore : ICertificateStore
    {
        #region Constructors
        /// <summary>
        /// Initializes a store with the specified directory path.
        /// </summary>
        public DirectoryCertificateStore()
        {
            m_certificates = new Dictionary<string, Entry>();
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// May be called by the application to clean up resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Cleans up all resources held by the object.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // clean up managed resources.
            if (disposing)
            {
                Close();
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The directory containing the certificate store.
        /// </summary>
        public DirectoryInfo Directory
        {
            get { return m_directory; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether any private keys are found in the store.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [no private keys]; otherwise, <c>false</c>.
        /// </value>
        public bool NoPrivateKeys { get; set; }
        #endregion

        #region ICertificateStore Members
        /// <summary cref="ICertificateStore.Open(string)" />
        public async Task Open(string location)
        {
            bool certsInRemovableStorageRootFound = false;
            IReadOnlyList<StorageFolder> folders = new List<StorageFolder>();

            try
            {
                folders = await KnownFolders.RemovableDevices.GetFoldersAsync();
                if (folders.Count > 0)
                {
                    IReadOnlyList<StorageFile> files = await folders[0].GetFilesAsync();
                    if (files.Count > 0)
                    {
                        certsInRemovableStorageRootFound = true;
                    }
                }
            }
            catch (Exception)
            {
                // do nothing
            }

            lock (m_lock)
            {
                if (certsInRemovableStorageRootFound && (folders.Count > 0))
                {
                    m_directory = new DirectoryInfo(folders[0].Path);
                    m_certificateSubdir = m_directory;
                    m_privateKeySubdir = m_directory;
                }
                else
                {
                    location = Utils.ReplaceSpecialFolderNames(location);
                    m_directory = new DirectoryInfo(location);
                    m_certificateSubdir = new DirectoryInfo(m_directory.FullName + "\\certs");
                    m_privateKeySubdir = new DirectoryInfo(m_directory.FullName + "\\private");
                }
            }
        }

        /// <summary cref="ICertificateStore.Close()" />
        public void Close()
        {
            lock (m_lock)
            {
                m_directory = null;
                m_certificateSubdir = null;
                m_privateKeySubdir = null;
                m_certificates.Clear();
                m_lastDirectoryCheck = DateTime.MinValue;
            }
        }

        /// <summary cref="ICertificateStore.Enumerate()" />
        public Task<X509Certificate2Collection> Enumerate()
        {
            lock (m_lock)
            {
                IDictionary<string,Entry> certificatesInStore = Load(null);
                X509Certificate2Collection certificates = new X509Certificate2Collection();

                foreach (Entry entry in certificatesInStore.Values)
                {
                    if (entry.CertificateWithPrivateKey != null)
                    {
                        certificates.Add(entry.CertificateWithPrivateKey);
                    }
                    else if (entry.Certificate != null)
                    {
                        certificates.Add(entry.Certificate);
                    }
                }

                return Task.FromResult(certificates);
            }
        }

        /// <summary cref="ICertificateStore.Add(X509Certificate2)" />
        public Task Add(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException("certificate");
         
            lock (m_lock)
            {
                byte[] data = null;

                // check for certificate file.
                Entry entry = Find(certificate.Thumbprint);

                if (entry != null)
                {
                    throw new ArgumentException("A certificate with the same thumbprint is already in the store.");
                }

                if (certificate.HasPrivateKey)
                {
                    data = certificate.Export(X509ContentType.Pkcs12, String.Empty);
                }
                else
                {
                    data = certificate.RawData;
                }

                // build file name.
                string fileName = GetFileName(certificate);

                // write the private and public key.
                WriteFile(data, fileName, certificate.HasPrivateKey);

                if (certificate.HasPrivateKey)
                {
                    WriteFile(certificate.RawData, fileName, false);
                }

                m_lastDirectoryCheck = DateTime.MinValue;
            }
            return Task.CompletedTask;
        }

        /// <summary cref="ICertificateStore.Delete(string)" />
        public Task<bool> Delete(string thumbprint)
        {
            lock (m_lock)
            {
                bool found = false;

                Entry entry = Find(thumbprint);

                if (entry != null)
                {
                    if (entry.PrivateKeyFile != null && entry.PrivateKeyFile.Exists)
                    {
                        entry.PrivateKeyFile.Delete();
                        found = true;
                    }

                    if (entry.CertificateFile != null && entry.CertificateFile.Exists)
                    {
                        entry.CertificateFile.Delete();
                        found = true;
                    }
                }

                if (found)
                {
                    m_lastDirectoryCheck = DateTime.MinValue;
                }

                return Task.FromResult(found);
            }
        }

        /// <summary cref="ICertificateStore.FindByThumbprint(string)" />
        public Task<X509Certificate2Collection> FindByThumbprint(string thumbprint)
        {
            X509Certificate2Collection certificates = new X509Certificate2Collection();

            lock (m_lock)
            {
                Entry entry = Find(thumbprint);

                if (entry != null)
                {
                    if (entry.CertificateWithPrivateKey != null)
                    {
                        certificates.Add(entry.CertificateWithPrivateKey);
                        return Task.FromResult(certificates);
                    }

                    certificates.Add(entry.Certificate);
                }

                return Task.FromResult(certificates);
            }
        }
        
        /// <summary cref="ICertificateStore.SupportsAccessControl" />
        public bool SupportsAccessControl
        {
            get { return true; }
        }

        /// <summary cref="ICertificateStore.GetAccessRules()" />
        public IList<ApplicationAccessRule> GetAccessRules()
        {
            lock (m_lock)
            {
                return ApplicationAccessRule.GetAccessRules(m_certificateSubdir.FullName);
            }
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
            lock (m_lock)
            {
                ApplicationAccessRule.SetAccessRules(m_certificateSubdir.FullName, rules, replaceExisting);

                if (String.Compare(m_certificateSubdir.FullName, m_privateKeySubdir.FullName, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    ApplicationAccessRule.SetAccessRules(m_privateKeySubdir.FullName, rules, replaceExisting);
                }
            }
        }
        
        /// <summary cref="ICertificateStore.SupportsCertificateAccessControl" />
        public bool SupportsCertificateAccessControl
        {
            get
            {
                return true;
            }
        }
        
        /// <summary cref="ICertificateStore.SupportsPrivateKeys" />
        public bool SupportsPrivateKeys
        {
            get
            {
                return true;
            }
        }

        /// <summary cref="ICertificateStore.GetPrivateKeyFilePath" />
        public string GetPublicKeyFilePath(string thumbprint)
        {
            Entry entry = Find(thumbprint);

            if (entry == null)
            {
                return null;
            }

            if (entry.CertificateFile == null || !entry.CertificateFile.Exists)
            {
                return null;
            }

            return entry.CertificateFile.FullName;
        }

        /// <summary cref="ICertificateStore.GetPrivateKeyFilePath" />
        public string GetPrivateKeyFilePath(string thumbprint)
        {
            Entry entry = Find(thumbprint);

            if (entry == null)
            {
                return null;
            }

            if (entry.PrivateKeyFile == null || !entry.PrivateKeyFile.Exists)
            {
                return null;
            }

            return entry.PrivateKeyFile.FullName;
        }

        /// <summary cref="ICertificateStore.GetAccessRules(string)" />
        public IList<ApplicationAccessRule> GetAccessRules(string thumbprint)
        {
            lock (m_lock)
            {
                Entry entry = Find(thumbprint);

                if (entry == null)
                {
                    throw new ArgumentException("Certificate does not exist in store.");
                }

                if (entry.PrivateKeyFile == null || !entry.PrivateKeyFile.Exists)
                {
                    throw new ArgumentException("Certificate does not have a private key in the store.");
                }

                return ApplicationAccessRule.GetAccessRules(entry.PrivateKeyFile.FullName);
            }
        }
        
        /// <summary cref="ICertificateStore.SetAccessRules(string, IList{ApplicationAccessRule},bool)" />
        public void SetAccessRules(string thumbprint, IList<ApplicationAccessRule> rules, bool replaceExisting)
        {
            lock (m_lock)
            {
                Entry entry = Find(thumbprint);

                if (entry == null)
                {
                    throw new ArgumentException("Certificate does not exist in store.");
                }

                if (entry.PrivateKeyFile != null && entry.PrivateKeyFile.Exists)
                {
                    ApplicationAccessRule.SetAccessRules(entry.PrivateKeyFile.FullName, rules, replaceExisting);
                }
            }
        }

        /// <summary>
        /// Loads the private key from a PFX file in the certificate store.
        /// </summary>
        public X509Certificate2 LoadPrivateKey(string thumbprint, string subjectName, string password)
        {
            if (m_certificateSubdir == null || !m_certificateSubdir.Exists)
            {
                return null;
            }

            foreach (FileInfo file in m_certificateSubdir.GetFiles("*.der"))
            {
                try
                {
                    X509Certificate2 certificate = new X509Certificate2(file.FullName);

                    if (!String.IsNullOrEmpty(thumbprint))
                    {
                        if (certificate.Thumbprint != thumbprint)
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(subjectName))
                    {
                        if (!Utils.CompareDistinguishedName(subjectName, certificate.Subject))
                        {
                            if (subjectName.Contains("=") || !certificate.Subject.Contains("CN=" + subjectName))
                            {
                                continue;
                            }
                        }
                    }

                    string fileRoot = file.Name.Substring(0, file.Name.Length - file.Extension.Length);

                    StringBuilder filePath = new StringBuilder();
                    filePath.Append(m_privateKeySubdir.FullName);
                    filePath.Append("\\");
                    filePath.Append(fileRoot);

                    FileInfo privateKeyFile = new FileInfo(filePath.ToString() + ".pfx");

                    certificate = new X509Certificate2(
                        privateKeyFile.FullName,
                        (password == null) ? String.Empty : password,
                        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.DefaultKeySet);

                    RSA rsa = certificate.GetRSAPrivateKey();
                    if (rsa != null)
                    {
                        int inputBlockSize = rsa.KeySize / 8 - 42;
                        byte[] bytes1 = rsa.Encrypt(new byte[inputBlockSize], RSAEncryptionPadding.OaepSHA1);
                        byte[] bytes2 = rsa.Decrypt(bytes1, RSAEncryptionPadding.OaepSHA1);
                        if (bytes2 != null)
                        {
                            return certificate;
                        }
                    }
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Could not load private key for certificate " + subjectName);
                }
            }

            return null;
        }

        /// <summary>
        /// Whether the store support CRLs.
        /// </summary>
        public bool SupportsCRLs { get { return true; } }

        /// <summary>
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException("issuer");
            }

            if (certificate == null)
            {
                throw new ArgumentNullException("certificate");
            }

            // check for CRL.
            DirectoryInfo info = new DirectoryInfo(this.Directory.FullName + "\\crl");

            if (info.Exists)
            {
                bool crlExpired = true;

                // certificate is fine.
                if (!crlExpired)
                {
                    return StatusCodes.Good;
                }
            }

            // can't find a valid CRL.
            return StatusCodes.BadCertificateRevocationUnknown;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Reads the current contents of the directory from disk.
        /// </summary>
        private IDictionary<string, Entry> Load(string thumbprint)
        {
            lock (m_lock)
            {
                DateTime now = DateTime.UtcNow;

                // refresh the directories.
                if (m_certificateSubdir != null)
                {
                    m_certificateSubdir.Refresh();
                }

                if (!NoPrivateKeys)
                {
                    if (m_privateKeySubdir != null)
                    {
                        m_privateKeySubdir.Refresh();
                    }
                }

                // check if store exists.
                if (!m_certificateSubdir.Exists)
                {
                    m_certificates.Clear();
                    return m_certificates;
                }

                // check if cache is still good.
                if (m_certificateSubdir.LastWriteTimeUtc < m_lastDirectoryCheck && (NoPrivateKeys || this.m_privateKeySubdir.LastWriteTimeUtc < m_lastDirectoryCheck))
                {
                    return m_certificates;
                }

                m_certificates.Clear();
                m_lastDirectoryCheck = now;
                bool incompleteSearch = false;

                // check for public keys.
                foreach (FileInfo file in m_certificateSubdir.GetFiles("*.der"))
                {
                    try
                    {
                        Entry entry = new Entry();

                        entry.Certificate = new X509Certificate2(file.FullName);
                        entry.CertificateFile = file;
                        entry.PrivateKeyFile = null;
                        entry.CertificateWithPrivateKey = null;

                        if (!NoPrivateKeys)
                        {
                            string fileRoot = file.Name.Substring(0, entry.CertificateFile.Name.Length - entry.CertificateFile.Extension.Length);

                            StringBuilder filePath = new StringBuilder();
                            filePath.Append(m_privateKeySubdir.FullName);
                            filePath.Append("\\");
                            filePath.Append(fileRoot);

                            entry.PrivateKeyFile = new FileInfo(filePath.ToString() + ".pfx");

                            // check for PFX file.
                            if (entry.PrivateKeyFile.Exists)
                            {
                                try
                                {
                                    X509Certificate2 certificate = new X509Certificate2(
                                        entry.PrivateKeyFile.FullName
                                    );

                                    if (certificate.HasPrivateKey)
                                    {
                                        entry.CertificateWithPrivateKey = certificate;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Utils.Trace(e, "Could not load private key certificate from file: {0}", entry.PrivateKeyFile.Name);
                                }
                            }

                            // check for PEM file.
                            else
                            {
                                entry.PrivateKeyFile = new FileInfo(filePath.ToString() + ".pem");

                                if (!entry.PrivateKeyFile.Exists)
                                {
                                    entry.PrivateKeyFile = null;
                                }
                            }
                        }

                        m_certificates[entry.Certificate.Thumbprint] = entry;

                        if (!String.IsNullOrEmpty(thumbprint) && thumbprint == entry.Certificate.Thumbprint)
                        {
                            incompleteSearch = true;
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Could not load certificate from file: {0}", file.FullName);
                    }
                }

                if (incompleteSearch)
                {
                    m_lastDirectoryCheck = DateTime.MinValue;
                }

                return m_certificates;
            }
        }

        /// <summary>
        /// Finds the public key for the certificate.
        /// </summary>
        private Entry Find(string thumbprint)
        {
            IDictionary<string, Entry> certificates = Load(thumbprint);

            Entry entry = null;

            if (!String.IsNullOrEmpty(thumbprint))
            {
                if (!certificates.TryGetValue(thumbprint, out entry))
                {
                    return null;
                }
            }

            return entry;
        }

        /// <summary>
        /// Returns the file name to use for the certificate.
        /// </summary>
        private string GetFileName(X509Certificate2 certificate)
        {
            // build file name.
            string commonName = certificate.FriendlyName;

            List<string> names = Utils.ParseDistinguishedName(certificate.Subject);

            for (int ii = 0; ii < names.Count; ii++)
            {
                if (names[ii].StartsWith("CN="))
                {
                    commonName = names[ii].Substring(3).Trim();
                    break;
                }
            }

            StringBuilder fileName = new StringBuilder();

            // remove any special characters.
            for (int ii = 0; ii < commonName.Length; ii++)
            {
                char ch = commonName[ii];

                if ("<>:\"/\\|?*".IndexOf(ch) != -1)
                {
                    ch = '+';
                }

                fileName.Append(ch);
            }

            fileName.Append(" [");
            fileName.Append(certificate.Thumbprint);
            fileName.Append("]");

            return fileName.ToString();
        }

        /// <summary>
        /// Writes the data to a file.
        /// </summary>
        private void WriteFile(byte[] data, string fileName, bool includePrivateKey)
        {
            StringBuilder filePath = new StringBuilder();

            if (!m_directory.Exists)
            {
                m_directory.Create();
            }

            if (includePrivateKey)
            {
                filePath.Append(m_privateKeySubdir.FullName);
            }
            else
            {
                filePath.Append(m_certificateSubdir.FullName);
            }

            filePath.Append("\\");
            filePath.Append(fileName);

            if (includePrivateKey)
            {
                filePath.Append(".pfx");
            }
            else
            {
                filePath.Append(".der");
            }

            // create the directory.
            FileInfo fileInfo = new FileInfo(filePath.ToString());

            if (!fileInfo.Directory.Exists)
            {
                fileInfo.Directory.Create();
            }

            // write file.
            BinaryWriter writer = new BinaryWriter(fileInfo.Open(FileMode.Create));

            try
            {
                writer.Write(data);
            }
            finally
            {
                writer.Flush();
                writer.Dispose();
            }

            m_certificateSubdir.Refresh();
            m_privateKeySubdir.Refresh();
        }
        #endregion

        #region Private Fields
        private class Entry
        {
            public FileInfo CertificateFile;
            public X509Certificate2 Certificate;
            public FileInfo PrivateKeyFile;
            public X509Certificate2 CertificateWithPrivateKey;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private DirectoryInfo m_directory;
        private DirectoryInfo m_certificateSubdir;
        private DirectoryInfo m_privateKeySubdir;
        private Dictionary<string, Entry> m_certificates;
        private DateTime m_lastDirectoryCheck;
        #endregion
    }
}
