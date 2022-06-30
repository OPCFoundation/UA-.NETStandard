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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
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
                lock (m_lock)
                {
                    m_certificates.Clear();
                    m_directory = null;
                    m_certificateSubdir = null;
                    m_privateKeySubdir = null;
                    m_lastDirectoryCheck = DateTime.MinValue;
                }
            }
            Close();
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
        #endregion

        #region ICertificateStore Members
        /// <inheritdoc/>
        public void Open(string location, bool noPrivateKeys = false)
        {
            lock (m_lock)
            {
                var trimmedLocation = Utils.ReplaceSpecialFolderNames(location);
                if (m_directory?.FullName.Equals(trimmedLocation, StringComparison.Ordinal) != true ||
                    NoPrivateKeys != noPrivateKeys)
                {
                    NoPrivateKeys = noPrivateKeys;
                    StorePath = location;
                    m_directory = new DirectoryInfo(trimmedLocation);
                    m_certificateSubdir = new DirectoryInfo(Path.Combine(m_directory.FullName, "certs"));
                    m_privateKeySubdir = new DirectoryInfo(Path.Combine(m_directory.FullName, "private"));
                    m_certificates.Clear();
                    m_lastDirectoryCheck = DateTime.MinValue;
                }
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            // intentionally keep information cached, dispose frees up resources
        }

        /// <inheritdoc/>
        public string StoreType => CertificateStoreType.Directory;

        /// <inheritdoc/>
        public string StorePath { get; private set; }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

                bool writePrivateKey = !NoPrivateKeys && certificate.HasPrivateKey;
                if (writePrivateKey)
                {
                    string passcode = password ?? string.Empty;
                    data = certificate.Export(X509ContentType.Pkcs12, passcode);
                }
                else
                {
                    data = certificate.RawData;
                }

                // build file name.
                string fileName = GetFileName(certificate);

                // write the private and public key.
                WriteFile(data, fileName, writePrivateKey);

                if (writePrivateKey)
                {
                    WriteFile(certificate.RawData, fileName, false);
                }

                m_lastDirectoryCheck = DateTime.MinValue;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<bool> Delete(string thumbprint)
        {
            const int kRetries = 5;
            const int kRetryDelay = 100;

            int retry = kRetries;
            bool found = false;

            do
            {
                lock (m_lock)
                {
                    Entry entry = Find(thumbprint);
                    try
                    {
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
                        retry = 0;
                    }
                    catch (IOException)
                    {
                        // file to delete may still be in use, retry
                        Utils.LogWarning("Failed to delete cert [{0}], retry.", thumbprint);
                        retry--;
                    }

                    if (found)
                    {
                        m_lastDirectoryCheck = DateTime.MinValue;
                    }
                }

                if (retry > 0)
                {
                    await Task.Delay(kRetryDelay).ConfigureAwait(false);
                }

            } while (retry > 0);

            return found;
        }

        /// <inheritdoc/>
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
                    }
                    else
                    {
                        certificates.Add(entry.Certificate);
                    }
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

        /// <inheritdoc/>
        public bool SupportsLoadPrivateKey => true;

        /// <summary>
        /// Loads the private key from a PFX/PEM file in the certificate store.
        /// </summary>
        public async Task<X509Certificate2> LoadPrivateKey(string thumbprint, string subjectName, string password)
        {
            if (NoPrivateKeys || m_certificateSubdir == null || !m_certificateSubdir.Exists)
            {
                return null;
            }

            if (string.IsNullOrEmpty(thumbprint) && string.IsNullOrEmpty(subjectName))
            {
                return null;
            }

            // on some platforms, specifically in virtualized environments,
            // reloading a previously created and saved private key may fail on the first attempt.
            const int retryDelay = 100;
            int retryCounter = 3;
            while (retryCounter-- > 0)
            {
                bool certificateFound = false;
                Exception importException = null;
                foreach (FileInfo file in m_certificateSubdir.GetFiles("*.der"))
                {
                    try
                    {
                        X509Certificate2 certificate = new X509Certificate2(file.FullName);

                        if (!String.IsNullOrEmpty(thumbprint))
                        {
                            if (!string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        if (!String.IsNullOrEmpty(subjectName))
                        {
                            if (!X509Utils.CompareDistinguishedName(subjectName, certificate.Subject))
                            {
                                if (subjectName.Contains('='))
                                {
                                    continue;
                                }

                                if (!X509Utils.ParseDistinguishedName(certificate.Subject).Any(s => s.Equals("CN=" + subjectName, StringComparison.Ordinal)))
                                {
                                    continue;
                                }
                            }
                        }

                        // skip if not RSA certificate
                        if (X509Utils.GetRSAPublicKeySize(certificate) < 0)
                        {
                            continue;
                        }

                        string fileRoot = file.Name.Substring(0, file.Name.Length - file.Extension.Length);

                        StringBuilder filePath = new StringBuilder()
                            .Append(m_privateKeySubdir.FullName)
                            .Append(Path.DirectorySeparatorChar)
                            .Append(fileRoot);

                        X509KeyStorageFlags[] storageFlags = {
                            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet,
                            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet
                        };

                        FileInfo privateKeyFilePfx = new FileInfo(filePath + ".pfx");
                        FileInfo privateKeyFilePem = new FileInfo(filePath + ".pem");
                        password = password ?? String.Empty;
                        if (privateKeyFilePfx.Exists)
                        {
                            certificateFound = true;
                            foreach (var flag in storageFlags)
                            {
                                try
                                {
                                    certificate = new X509Certificate2(
                                        privateKeyFilePfx.FullName,
                                        password,
                                        flag);
                                    if (X509Utils.VerifyRSAKeyPair(certificate, certificate, true))
                                    {
                                        Utils.LogInfo(Utils.TraceMasks.Security, "Imported the PFX private key for [{0}].", certificate.Thumbprint);
                                        return certificate;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    importException = ex;
                                    certificate?.Dispose();
                                }
                            }
                        }
                        // if PFX file doesn't exist, check for PEM file.
                        else if (privateKeyFilePem.Exists)
                        {
                            certificateFound = true;
                            try
                            {
                                byte[] pemDataBlob = File.ReadAllBytes(privateKeyFilePem.FullName);
                                certificate = CertificateFactory.CreateCertificateWithPEMPrivateKey(certificate, pemDataBlob, password);
                                if (X509Utils.VerifyRSAKeyPair(certificate, certificate, true))
                                {
                                    Utils.LogInfo(Utils.TraceMasks.Security, "Imported the PEM private key for [{0}].", certificate.Thumbprint);
                                    return certificate;
                                }
                            }
                            catch (Exception exception)
                            {
                                certificate?.Dispose();
                                importException = exception;
                            }
                        }
                        else
                        {
                            Utils.LogError(Utils.TraceMasks.Security, "A private key for the certificate with thumbprint [{0}] does not exist.", certificate.Thumbprint);
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(e, "Could not load private key for certificate {0}", subjectName);
                    }
                }

                // found a certificate, but some error occurred
                if (certificateFound)
                {
                    Utils.LogError(Utils.TraceMasks.Security, "The private key for the certificate with subject {0} failed to import.", subjectName);
                    if (importException != null)
                    {
                        Utils.LogError(importException, "Certificate import failed.");
                    }
                }
                else
                {
                    if (!String.IsNullOrEmpty(thumbprint))
                    {
                        Utils.LogError(Utils.TraceMasks.Security, "A Private key for the certificate with thumbpint {0} was not found.", thumbprint);
                    }
                    // if no private key was found, no need to retry
                    break;
                }

                // retry within a few ms
                if (retryCounter > 0)
                {
                    Utils.LogInfo(Utils.TraceMasks.Security, "Retry to import private key after {0} ms.", retryDelay);
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public Task<StatusCode> IsRevoked(X509Certificate2 issuer, X509Certificate2 certificate)
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
                        Utils.LogError(e, "Could not parse CRL file.");
                        continue;
                    }

                    if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
                    {
                        continue;
                    }

                    if (!crl.VerifySignature(issuer, false))
                    {
                        continue;
                    }

                    if (crl.IsRevoked(certificate))
                    {
                        return Task.FromResult((StatusCode)StatusCodes.BadCertificateRevoked);
                    }

                    if (crl.ThisUpdate <= DateTime.UtcNow && (crl.NextUpdate == DateTime.MinValue || crl.NextUpdate >= DateTime.UtcNow))
                    {
                        crlExpired = false;
                    }
                }

                // certificate is fine.
                if (!crlExpired)
                {
                    return Task.FromResult((StatusCode)StatusCodes.Good);
                }
            }

            // can't find a valid CRL.
            return Task.FromResult((StatusCode)StatusCodes.BadCertificateRevocationUnknown);
        }

        /// <inheritdoc/>
        public bool SupportsCRLs { get { return true; } }

        /// <inheritdoc/>
        public Task<X509CRLCollection> EnumerateCRLs()
        {
            var crls = new X509CRLCollection();

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

            return Task.FromResult(crls);
        }

        /// <inheritdoc/>
        public async Task<X509CRLCollection> EnumerateCRLs(X509Certificate2 issuer, bool validateUpdateTime = true)
        {
            if (issuer == null)
            {
                throw new ArgumentNullException(nameof(issuer));
            }

            var crls = new X509CRLCollection();
            foreach (X509CRL crl in await EnumerateCRLs().ConfigureAwait(false))
            {
                if (!X509Utils.CompareDistinguishedName(crl.IssuerName, issuer.SubjectName))
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

        /// <inheritdoc/>
        public async Task AddCRL(X509CRL crl)
        {
            if (crl == null)
            {
                throw new ArgumentNullException(nameof(crl));
            }

            X509Certificate2 issuer = null;
            X509Certificate2Collection certificates = null;
            certificates = await Enumerate().ConfigureAwait(false);
            foreach (X509Certificate2 certificate in certificates)
            {
                if (X509Utils.CompareDistinguishedName(certificate.SubjectName, crl.IssuerName))
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
            builder.Append(Path.DirectorySeparatorChar).Append("crl").Append(Path.DirectorySeparatorChar);
            builder.Append(GetFileName(issuer));
            builder.Append(".crl");

            FileInfo fileInfo = new FileInfo(builder.ToString());

            if (!fileInfo.Directory.Exists)
            {
                fileInfo.Directory.Create();
            }

            File.WriteAllBytes(fileInfo.FullName, crl.RawData);
        }

        /// <inheritdoc/>
        public Task<bool> DeleteCRL(X509CRL crl)
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
                            return Task.FromResult(true);
                        }
                    }
                }
            }

            return Task.FromResult(false);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets or sets a value indicating whether any private keys are found in the store.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [no private keys]; otherwise, <c>false</c>.
        /// </value>
        private bool NoPrivateKeys { get; set; }

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
                        Entry entry = new Entry {
                            Certificate = new X509Certificate2(file.FullName),
                            CertificateFile = file,
                            PrivateKeyFile = null,
                            CertificateWithPrivateKey = null
                        };

                        if (!NoPrivateKeys)
                        {
                            string fileRoot = file.Name.Substring(0, entry.CertificateFile.Name.Length - entry.CertificateFile.Extension.Length);

                            StringBuilder filePath = new StringBuilder();
                            filePath.Append(m_privateKeySubdir.FullName);
                            filePath.Append(Path.DirectorySeparatorChar);
                            filePath.Append(fileRoot);

                            // check for PFX file.
                            entry.PrivateKeyFile = new FileInfo(filePath.ToString() + ".pfx");

                            // note: only obtain the filenames for delete, loading the private keys
                            // without authorization causes false negatives (LogErrors)
                            if (!entry.PrivateKeyFile.Exists)
                            {
                                // check for PEM file.
                                entry.PrivateKeyFile = new FileInfo(filePath.ToString() + ".pem");

                                if (!entry.PrivateKeyFile.Exists)
                                {
                                    entry.PrivateKeyFile = null;
                                }
                            }
                        }

                        m_certificates[entry.Certificate.Thumbprint] = entry;

                        if (!String.IsNullOrEmpty(thumbprint) &&
                            thumbprint.Equals(entry.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                        {
                            incompleteSearch = true;
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(e, "Could not load certificate from file: {0}", file.FullName);
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
                if (names[ii].StartsWith("CN=", StringComparison.Ordinal))
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
            fileName.Append(']');

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
