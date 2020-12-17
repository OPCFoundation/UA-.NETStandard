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
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

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
        private bool NoPrivateKeys { get; set; }
        #endregion

        #region ICertificateStore Members
        /// <summary cref="ICertificateStore.Open(string)" />
        public void Open(string location)
        {
            lock (m_lock)
            {
                location = Utils.ReplaceSpecialFolderNames(location);
                m_directory = new DirectoryInfo(location);
                m_certificateSubdir = new DirectoryInfo(m_directory.FullName + Path.DirectorySeparatorChar + "certs");
                m_privateKeySubdir = new DirectoryInfo(m_directory.FullName + Path.DirectorySeparatorChar + "private");
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
                IDictionary<string, Entry> certificatesInStore = Load(null);
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

        /// <summary cref="ICertificateStore.Add(X509Certificate2, String)" />
        public Task Add(X509Certificate2 certificate, string password = null)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

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
                    string passcode = password ?? String.Empty;
                    data = certificate.Export(X509ContentType.Pkcs12, passcode);
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

        /// <summary>
        /// Returns the path to the public key file.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate.</param>
        /// <returns>The path.</returns>
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

        /// <summary>
        /// Returns the path to the private key file.
        /// </summary>
        /// <param name="thumbprint">The thumbprint of the certificate.</param>
        /// <returns>The path.</returns>
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

        /// <summary>
        /// Loads the private key from a PFX file in the certificate store.
        /// </summary>
        public X509Certificate2 LoadPrivateKey(string thumbprint, string subjectName, string password)
        {
            if (m_certificateSubdir == null || !m_certificateSubdir.Exists)
            {
                return null;
            }

            if (string.IsNullOrEmpty(thumbprint) && string.IsNullOrEmpty(subjectName))
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
                        if (!string.Equals(certificate.Thumbprint, thumbprint, StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (!String.IsNullOrEmpty(subjectName))
                    {
                        if (!X509Utils.CompareDistinguishedName(subjectName, certificate.Subject))
                        {
                            if (subjectName.Contains("="))
                            {
                                continue;
                            }

                            if (!X509Utils.ParseDistinguishedName(certificate.Subject).Any(s => s.Equals("CN=" + subjectName, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                        }
                    }

                    string fileRoot = file.Name.Substring(0, file.Name.Length - file.Extension.Length);

                    StringBuilder filePath = new StringBuilder();
                    filePath.Append(m_privateKeySubdir.FullName);
                    filePath.Append(Path.DirectorySeparatorChar);
                    filePath.Append(fileRoot);

                    X509KeyStorageFlags[] storageFlags = {
                        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet,
                        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet
                    };

                    FileInfo privateKeyFile = new FileInfo(filePath.ToString() + ".pfx");
                    password = password ?? String.Empty;
                    foreach (var flag in storageFlags)
                    {
                        try
                        {
                            certificate = new X509Certificate2(
                                privateKeyFile.FullName,
                                password,
                                flag);
                            if (X509Utils.VerifyRSAKeyPair(certificate, certificate, true))
                            {
                                return certificate;
                            }
                        }
                        catch (Exception)
                        {
                            certificate?.Dispose();
                            certificate = null;
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
        /// Checks if issuer has revoked the certificate.
        /// </summary>
        public StatusCode IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            // check for CRL.
            DirectoryInfo info = new DirectoryInfo(this.Directory.FullName + Path.DirectorySeparatorChar + "crl");

            if (info.Exists)
            {
                bool crlExpired = true;

                foreach (FileInfo file in info.GetFiles("*.crl"))
                {
                    X509CRL crl = null;

                    try
                    {
                        crl = new X509CRL(file.FullName);
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Could not parse CRL file.");
                        continue;
                    }

                    if (!X509Utils.CompareDistinguishedName(crl.Issuer, issuer.Subject))
                    {
                        continue;
                    }

                    if (!crl.VerifySignature(issuer, false))
                    {
                        continue;
                    }

                    if (crl.IsRevoked(certificate))
                    {
                        return StatusCodes.BadCertificateRevoked;
                    }

                    if (crl.ThisUpdate <= DateTime.UtcNow && (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                    {
                        crlExpired = false;
                    }
                }

                // certificate is fine.
                if (!crlExpired)
                {
                    return StatusCodes.Good;
                }
            }

            // can't find a valid CRL.
            return StatusCodes.BadCertificateRevocationUnknown;
        }

        /// <summary>
        /// Whether the the store support CRLs.
        /// </summary>
        public bool SupportsCRLs { get { return true; } }

        /// <summary>
        /// Returns the CRLs in the store.
        /// </summary>
        public List<X509CRL> EnumerateCRLs()
        {
            List<X509CRL> crls = new List<X509CRL>();

            // check for CRL.
            DirectoryInfo info = new DirectoryInfo(this.Directory.FullName + Path.DirectorySeparatorChar + "crl");

            if (info.Exists)
            {
                foreach (FileInfo file in info.GetFiles("*.crl"))
                {
                    X509CRL crl = new X509CRL(file.FullName);
                    crls.Add(crl);
                }
            }

            return crls;
        }

        /// <summary>
        /// Returns the CRLs for the issuer.
        /// </summary>
        public List<X509CRL> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            List<X509CRL> crls = new List<X509CRL>();

            foreach (X509CRL crl in EnumerateCRLs())
            {
                if (!X509Utils.CompareDistinguishedName(crl.Issuer, issuer.Subject))
                {
                    continue;
                }

                if (!crl.VerifySignature(issuer, false))
                {
                    continue;
                }

                if (!validateUpdateTime ||
                    crl.ThisUpdate <= DateTime.UtcNow && (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                {
                    crls.Add(crl);
                }
            }

            return crls;
        }

        /// <summary>
        /// Adds a CRL to the store.
        /// </summary>
        public void AddCRL(X509CRL crl)
        {
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            X509Certificate2 issuer = null;
            X509Certificate2Collection certificates = null;
            certificates = Enumerate().Result;
            foreach (X509Certificate2 certificate in certificates)
            {
                if (X509Utils.CompareDistinguishedName(certificate.Subject, crl.Issuer))
                {
                    if (crl.VerifySignature(certificate, false))
                    {
                        issuer = certificate;
                        break;
                    }
                }
            }

            if (issuer == null)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid, "Could not find issuer of the CRL.");
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(m_directory.FullName);

            builder.Append(Path.DirectorySeparatorChar + "crl" + Path.DirectorySeparatorChar);
            builder.Append(GetFileName(issuer));
            builder.Append(".crl");

            FileInfo fileInfo = new FileInfo(builder.ToString());

            if (!fileInfo.Directory.Exists)
            {
                fileInfo.Directory.Create();
            }

            File.WriteAllBytes(fileInfo.FullName, crl.RawData);
        }

        /// <summary>
        /// Removes a CRL from the store.
        /// </summary>
        public bool DeleteCRL(X509CRL crl)
        {
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            string filePath = m_directory.FullName;
            filePath += Path.DirectorySeparatorChar + "crl";

            DirectoryInfo dirInfo = new DirectoryInfo(filePath);

            if (dirInfo.Exists)
            {
                foreach (FileInfo fileInfo in dirInfo.GetFiles("*.crl"))
                {
                    if (fileInfo.Length == crl.RawData.Length)
                    {
                        byte[] bytes = File.ReadAllBytes(fileInfo.FullName);

                        if (Utils.IsEqual(bytes, crl.RawData))
                        {
                            fileInfo.Delete();
                            return true;
                        }
                    }
                }
            }

            return false;
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
                if ((m_certificateSubdir.LastWriteTimeUtc < m_lastDirectoryCheck) &&
                    (NoPrivateKeys || !m_privateKeySubdir.Exists || m_privateKeySubdir.LastWriteTimeUtc < m_lastDirectoryCheck))
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
                            filePath.Append(Path.DirectorySeparatorChar);
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

            List<string> names = X509Utils.ParseDistinguishedName(certificate.Subject);

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

            filePath.Append(Path.DirectorySeparatorChar);
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

        #region Private Class
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
