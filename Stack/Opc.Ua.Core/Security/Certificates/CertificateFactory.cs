/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Creates a manages certificates.
    /// </summary>
    public class CertificateFactory
    {
        #region Public Methods
        /// <summary>
        /// Creates a certificate from a buffer with DER encoded certificate.
        /// </summary>
        /// <param name="encodedData">The encoded data.</param>
        /// <param name="useCache">if set to <c>true</c> the copy of the certificate in the cache is used.</param>
        /// <returns>The certificate.</returns>
        public static X509Certificate2 Create(byte[] encodedData, bool useCache)
        {
            if (useCache)
            {
                return Load(new X509Certificate2(encodedData), false);
            }

            return new X509Certificate2(encodedData);
        }

        /// <summary>
        /// Loads the cached version of a certificate.
        /// </summary>
        /// <param name="certificate">The certificate to load.</param>
        /// <param name="ensurePrivateKeyAccessible">If true a key conatiner is created for a certificate that must be deleted by calling Cleanup.</param>
        /// <returns>The cached certificate.</returns>
        /// <remarks>
        /// This function is necessary because all private keys used for cryptography operations must be in a key conatiner. 
        /// Private keys stored in a PFX file have no key conatiner by default.
        /// </remarks>
        public static X509Certificate2 Load(X509Certificate2 certificate, bool ensurePrivateKeyAccessible)
        {
            if (certificate == null)
            {
                return null;
            }

            lock (m_certificates)
            {
                X509Certificate2 cachedCertificate = null;

                // check for existing cached certificate.
                if (m_certificates.TryGetValue(certificate.Thumbprint, out cachedCertificate))
                {
                    return cachedCertificate;
                }

                // nothing more to do if no private key or dont care about accessibility.
                if (!certificate.HasPrivateKey || !ensurePrivateKeyAccessible)
                {
                    return certificate;
                }

                // update the cache.
                m_certificates[certificate.Thumbprint] = certificate;

                if (m_certificates.Count > 100)
                {
                    Utils.Trace("WARNING - Process certificate cache has {0} certificates in it.", m_certificates.Count);
                }

                // save the key container so it can be deleted later.
                m_temporaryKeyContainers.Add(certificate);
        }

            return certificate;
        }

        /// <summary>
        /// Cleans up temporary key containers created by the application.
        /// </summary>
        public static void Cleanup()
        {
            lock (m_certificates)
            {
                foreach (X509Certificate2 certificate in m_temporaryKeyContainers)
                {
                    Utils.Trace("Could not delete key container.");
                }
            }
        }

        private static Dictionary<string, X509Certificate2> m_certificates = new Dictionary<string, X509Certificate2>();
        private static List<X509Certificate2> m_temporaryKeyContainers = new List<X509Certificate2>();
        
        /// <summary>
        /// Creates an instance from a certificate with zero or more supporting certificates.
        /// </summary>
        public static X509Certificate2 Create(params X509Certificate2[] certificates)
        {
            return certificates[0];
        }

        /// <summary>
        /// Creates a self signed application instance certificate.
        /// </summary>
        /// <param name="applicationUri">The application uri (created if not specified).</param>
        /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
        /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
        /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
        /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
        /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
        /// <returns>The certificate with a private key.</returns>
        public async static Task<X509Certificate2> CreateCertificate(
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            ushort lifetimeInMonths)
        {
            return await CreateCertificate(
                null, 
                null, 
                null, 
                applicationUri, 
                applicationName, 
                subjectName, 
                domainNames, 
                keySize, 
                DateTime.MinValue,
                lifetimeInMonths,
                0,
                false,
                false,
                null, 
                null);
        }
        
        /// <summary>
        /// Creates a self signed application instance certificate.
        /// </summary>
        /// <param name="storeType">Type of certificate store (Windows or Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="storePath">The store path (syntax depends on storeType).</param>
        /// <param name="applicationUri">The application uri (created if not specified).</param>
        /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
        /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
        /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
        /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
        /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
        /// <returns>The certificate with a private key.</returns>
        public static async Task<X509Certificate2> CreateCertificate(
            string storeType,
            string storePath,
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            ushort lifetimeInMonths)
        {
            return await CreateCertificate(
                storeType,
                storePath,
                null,
                applicationUri,
                applicationName,
                subjectName,
                domainNames, 
                keySize,
                DateTime.MinValue,
                lifetimeInMonths,
                0,
                false,
                false, 
                null, 
                null);
        }

        /// <summary>
        /// Creates a self signed application instance certificate.
        /// </summary>
        public static async Task<X509Certificate2> CreateCertificate(
            string storeType,
            string storePath,
            string password,
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            ushort lifetimeInMonths,
            bool isCA,
            string issuerKeyFilePath,
            string issuerKeyFilePassword)
        {
            return await CreateCertificate(
                 storeType,
                 storePath,
                 password,
                 applicationUri,
                 applicationName,
                 subjectName,
                 domainNames,
                 keySize,
                 DateTime.MinValue,
                 lifetimeInMonths,
                 0,
                 isCA,
                 false,
                 issuerKeyFilePath,
                 issuerKeyFilePassword);
        }

        /// <summary>
        /// Creates a self signed application instance certificate.
        /// </summary>
        /// <param name="storeType">Type of certificate store (Windows or Directory) <see cref="CertificateStoreType"/>.</param>
        /// <param name="storePath">The store path (syntax depends on storeType).</param>
        /// <param name="password">The password to use to protect the certificate.</param>
        /// <param name="applicationUri">The application uri (created if not specified).</param>
        /// <param name="applicationName">Name of the application (optional if subjectName is specified).</param>
        /// <param name="subjectName">The subject used to create the certificate (optional if applicationName is specified).</param>
        /// <param name="domainNames">The domain names that can be used to access the server machine (defaults to local computer name if not specified).</param>
        /// <param name="keySize">Size of the key (1024, 2048 or 4096).</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="lifetimeInMonths">The lifetime of the key in months.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <param name="isCA">if set to <c>true</c> the a CA certificate is created.</param>
        /// <param name="usePEMFormat">if set to <c>true</c> the private ket is store in the PEM format.</param>
        /// <param name="issuerKeyFilePath">The path to the PFX file containing the CA private key.</param>
        /// <param name="issuerKeyFilePassword">The  password for the PFX file containing the CA private key.</param>
        /// <returns>The certificate with a private key.</returns>
        public static async Task<X509Certificate2> CreateCertificate(
            string storeType,
            string storePath,
            string password,
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            DateTime startTime,
            ushort lifetimeInMonths,
            ushort hashSizeInBits,
            bool isCA,
            bool usePEMFormat,
            string issuerKeyFilePath,
            string issuerKeyFilePassword)
        {
            // use the proxy if create a certificate in a directory store.
            if (storeType == CertificateStoreType.Directory)
            {
                string executablePath = GetCertificateGeneratorPath();

                if (String.IsNullOrEmpty(executablePath))
                {
                    throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "The CertificateGenerator utility is not installed.");
                }

                X509Certificate2Collection certificates = await CreateCertificateViaProxy(
                    executablePath,
                    storePath,
                    password,
                    applicationUri,
                    applicationName,
                    subjectName,
                    domainNames,
                    keySize,
                    startTime,
                    lifetimeInMonths,
                    hashSizeInBits,
                    isCA,
                    usePEMFormat,
                    issuerKeyFilePath,
                    issuerKeyFilePassword);
                if (certificates.Count > 0)
                {
                    return certificates[0];
                }
            }

            if (isCA)
            {
                throw new NotSupportedException("Cannot create a CA certificate in a Windows store at this time.");
            }

            if (!String.IsNullOrEmpty(password))
            {
                throw new NotSupportedException("Cannot add password when creating a certificate in a Windows store at this time.");
            }

            if (!String.IsNullOrEmpty(issuerKeyFilePath))
            {
                throw new NotSupportedException("Cannot use a CA certificate to create a certificate in a Windows store at this time.");
            }

            // set default values.
            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames,
                ref keySize,
                ref lifetimeInMonths);

            // create the certificate.
            X509Certificate2 certificate = await CreateCertificate(
                applicationUri,
                applicationName,
                subjectName.ToString(),
                domainNames,
                keySize,
                lifetimeInMonths);
            
            // add it to the store.
            if (!String.IsNullOrEmpty(storePath))
            {
                ICertificateStore store = null;

                if (storeType == CertificateStoreType.Windows)
                {
#if TODO
                    store = new Opc.Ua.WindowsCertificateStore();
#endif
                }
                else
                {
                    store = new Opc.Ua.DirectoryCertificateStore();
                }

                await store.Open(storePath);
                await store.Add(certificate);
            }

            return certificate;
        }

        /// <summary>
        /// Returns the path to the CertificateGenerator utility.
        /// </summary>
        private static string GetCertificateGeneratorPath()
        {
            string executablePath = null;

            //first check on the same folder as the current executable
            executablePath = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Opc.Ua.CertificateGenerator.exe");
            executablePath = Utils.GetAbsoluteFilePath(executablePath, false, false, false);

            if (executablePath != null)
            {
                return executablePath;
            }

            // recursively go up the tree looking for /Bin directories.
            if (executablePath == null)
            {
                DirectoryInfo dirInfo = new DirectoryInfo(ApplicationData.Current.LocalFolder.Path);

                while (dirInfo != null)
                {
                    executablePath = dirInfo.FullName;
                    executablePath += "\\Bin\\Opc.Ua.CertificateGenerator.exe";
                    executablePath = Utils.GetAbsoluteFilePath(executablePath, false, false, false);

                    if (executablePath != null)
                    {
                        return executablePath;
                    }

                    dirInfo = dirInfo.Parent;
                }
            }

            // hard the the proxy path name for now.
            string commonFilesDir = Utils.GetAbsoluteDirectoryPath(@"%CommonProgramFiles%", false, false);
            string relativePath = @"\OPC Foundation\UA\v1.0\Bin\Opc.Ua.CertificateGenerator.exe";
            executablePath = Utils.GetAbsoluteFilePath(commonFilesDir + relativePath, false, false, false);

            if (executablePath == null)
            {
                // try the x86 directory.
                int index = commonFilesDir.LastIndexOf('\\', commonFilesDir.Length - 2);

                if (index != -1)
                {
                    string root = commonFilesDir.Substring(0, index);
                    root += " (x86)";
                    root += commonFilesDir.Substring(index);
                    commonFilesDir = root;
                }

                executablePath = Utils.GetAbsoluteFilePath(commonFilesDir + relativePath, false, false, false);
            }

            return executablePath;
        }

        /// <summary>
        /// Creates the certificate via a proxy instead of calling the CryptoAPI directly.
        /// </summary>
        /// <param name="executablePath">The executable path.</param>
        /// <param name="storePath">The store path.</param>
        /// <param name="password">The password used to protect the certificate.</param>
        /// <param name="applicationUri">The application URI.</param>
        /// <param name="applicationName">Name of the application.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="domainNames">The domain names.</param>
        /// <param name="keySize">Size of the key.</param>
        /// <param name="startTime">The start time.</param>
        /// <param name="lifetimeInMonths">The lifetime in months.</param>
        /// <param name="hashSizeInBits">The hash size in bits.</param>
        /// <param name="isCA">if set to <c>true</c> if creating a certificate authority.</param>
        /// <param name="usePEMFormat">if set to <c>true</c> the private ket is store in the PEM format.</param>
        /// <param name="issuerKeyFilePath">The path to the PFX file containing the CA private key.</param>
        /// <param name="issuerKeyFilePassword">The  password for the PFX file containing the CA private key.</param>
        /// <returns></returns>
        public static async Task<X509Certificate2Collection> CreateCertificateViaProxy(
            string executablePath,
            string storePath,
            string password,
            string applicationUri,
            string applicationName,
            string subjectName,
            IList<String> domainNames,
            ushort keySize,
            DateTime startTime,
            ushort lifetimeInMonths,
            ushort hashSizeInBits,
            bool isCA,
            bool usePEMFormat,
            string issuerKeyFilePath,
            string issuerKeyFilePassword)
        {
            // check if the proxy exists.
            FileInfo filePath = new FileInfo(executablePath);

            if (!filePath.Exists)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Cannnot find the Opc.Ua.CertificateGenerator utility: {0}", executablePath);
            }
            
            // expand any strings.
            storePath = Utils.GetAbsoluteDirectoryPath(storePath, true, false, true);

            if (storePath == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Certificate store does not exist: {0}", storePath);
            }

            // set default values.
            SetSuitableDefaults(
                ref applicationUri,
                ref applicationName,
                ref subjectName,
                ref domainNames,
                ref keySize,
                ref lifetimeInMonths);

            // reconstruct name using slash as delimeter.
            subjectName = ChangeSubjectNameDelimiter(subjectName, '/');

            string tempFile = Path.GetTempFileName();

            try
            {
                StreamWriter writer = new StreamWriter(new FileStream(tempFile, FileMode.OpenOrCreate));

                writer.WriteLine("-cmd issue", storePath);

                if (!String.IsNullOrEmpty(storePath))
                {
                    writer.WriteLine("-storePath {0}", storePath);
                }

                if (!String.IsNullOrEmpty(applicationName))
                {
                    writer.WriteLine("-applicationName {0} ", applicationName);
                }

                if (!String.IsNullOrEmpty(subjectName))
                {
                    writer.WriteLine("-subjectName {0}", subjectName);
                }

                if (!String.IsNullOrEmpty(password))
                {
                    writer.WriteLine("-password {0}", password);
                }

                if (!isCA)
                {
                    if (!String.IsNullOrEmpty(applicationUri))
                    {
                        writer.WriteLine("-applicationUri {0}", applicationUri);
                    }

                    if (domainNames != null && domainNames.Count > 0)
                    {
                        StringBuilder buffer = new StringBuilder();

                        for (int ii = 0; ii < domainNames.Count; ii++)
                        {
                            if (buffer.Length > 0)
                            {
                                buffer.Append(",");
                            }

                            buffer.Append(domainNames[ii]);
                        }

                        writer.WriteLine("-domainNames {0}", buffer.ToString());
                    }
                }

                writer.WriteLine("-keySize {0}", keySize);

                if (startTime > DateTime.MinValue)
                {
                    writer.WriteLine("-startTime {0}", startTime.Ticks - new DateTime(1601, 1, 1).Ticks);
                }

                writer.WriteLine("-lifetimeInMonths {0}", lifetimeInMonths);
                writer.WriteLine("-hashSize {0}", hashSizeInBits);

                if (isCA)
                {
                    writer.WriteLine("-ca true");
                }

                if (usePEMFormat)
                {
                    writer.WriteLine("-pem true");
                }

                if (!String.IsNullOrEmpty(issuerKeyFilePath))
                {
                    writer.WriteLine("-issuerKeyFilePath {0}", issuerKeyFilePath);
                }

                if (!String.IsNullOrEmpty(issuerKeyFilePassword))
                {
                    writer.WriteLine("-issuerKeyPassword {0}", issuerKeyFilePassword);
                }

                writer.WriteLine("");
                writer.Flush();
                writer.Dispose();

                string result = null;
                string thumbprint = null;

                StreamReader reader = new StreamReader(new FileStream(tempFile, FileMode.Open));

                try
                {
                    while ((result = reader.ReadLine()) != null)
                    {
                        if (String.IsNullOrEmpty(result))
                        {
                            continue;
                        }

                        if (result.StartsWith("-cmd"))
                        {
                            throw new ServiceResultException("Input file was not processed properly.");
                        }

                        if (result.StartsWith("-thumbprint"))
                        {
                            thumbprint = result.Substring("-thumbprint".Length).Trim();
                            break;
                        }

                        if (result.StartsWith("-error"))
                        {
                            throw new ServiceResultException(result);
                        }
                    }
                }
                finally
                {
                    reader.Dispose();
                }

                // load the new certificate from the store.
                ICertificateStore store = new Opc.Ua.DirectoryCertificateStore();
                await store.Open(storePath);
                X509Certificate2Collection certificates = await store.FindByThumbprint(thumbprint);
                store.Close();

                return certificates;
            }
            catch (Exception e)
            {
                throw new ServiceResultException("Could not create a certificate via a proxy: " + e.Message, e);
            }
            finally
            {
                if (tempFile != null)
                {
                    try { File.Delete(tempFile); } catch {}
                }
            }
        }

        /// <summary>
        /// Revokes a certificate by proxy.
        /// </summary>
        public static void RevokeCertificate(
            string storePath,
            X509Certificate2 certificate,
            string issuerKeyFilePath,
            string issuerKeyFilePassword)
        {
            string executablePath = GetCertificateGeneratorPath();

            // check if the proxy exists.
            FileInfo filePath = new FileInfo(executablePath);

            if (!filePath.Exists)
            {
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError, "Cannnot find the Opc.Ua.CertificateGenerator utility: {0}", executablePath);
            }

            // expand any strings.
            storePath = Utils.GetAbsoluteDirectoryPath(storePath, true, false, true);

            if (storePath == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadInvalidArgument, "Certificate store does not exist: {0}", storePath);
            }

            string certificateFile = Path.GetTempFileName();
            string tempFile = Path.GetTempFileName();

            try
            {
                // write the certificate to a temp file.
                File.WriteAllBytes(certificateFile, certificate.RawData);

                // write the arguments to a temp file.
                StreamWriter writer = new StreamWriter(new FileStream(tempFile, FileMode.CreateNew));

                writer.WriteLine("-cmd revoke", storePath);

                if (!String.IsNullOrEmpty(storePath))
                {
                    writer.WriteLine("-storePath {0}", storePath);
                }

                if (!String.IsNullOrEmpty(certificateFile))
                {
                    writer.WriteLine("-publicKeyFilePath {0}", certificateFile);
                }

                if (!String.IsNullOrEmpty(issuerKeyFilePath))
                {
                    writer.WriteLine("-issuerKeyFilePath {0}", issuerKeyFilePath);
                }

                if (!String.IsNullOrEmpty(issuerKeyFilePassword))
                {
                    writer.WriteLine("-issuerKeyPassword {0}", issuerKeyFilePassword);
                }

                writer.WriteLine("");
                writer.Flush();
                writer.Dispose();

                string result = null;
                string thumbprint = null;

                StreamReader reader = new StreamReader(new FileStream(tempFile, FileMode.CreateNew));

                try
                {
                    while ((result = reader.ReadLine()) != null)
                    {
                        if (String.IsNullOrEmpty(result))
                        {
                            continue;
                        }

                        if (result.StartsWith("-cmd"))
                        {
                            throw new ServiceResultException("Input file was not processed properly.");
                        }

                        if (result.StartsWith("-thumbprint"))
                        {
                            thumbprint = result.Substring("-thumbprint".Length).Trim();
                            break;
                        }

                        if (result.StartsWith("-error"))
                        {
                            throw new ServiceResultException(result);
                        }
                    }
                }
                finally
                {
                    reader.Dispose();
                }
            }
            catch (Exception e)
            {
                throw new ServiceResultException("Could not revoke a certificate via a proxy: " + e.Message, e);
            }
            finally
            {
                if (certificateFile != null)
                {
                    try { File.Delete(certificateFile); }
                    catch { }
                }

                if (tempFile != null)
                {
                    try { File.Delete(tempFile); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Creates a self signed application instance certificate.
        /// </summary>
        [Obsolete("Use the static version which takes a storeType/storePath")]
        public async Task<X509Certificate2> CreateCertificate(
            string storeLocation,
            string storeName,
            string applicationName,
            string applicationUri,
            IList<string> hostNames,
            string organization,
            ushort keySize,
            ushort lifetimeInYears)
        {
            X509Certificate2 certificate = await CreateCertificate(
                CertificateStoreType.Windows,
                storeLocation + "\\" + storeName,
                applicationUri,
                applicationName,
                null,
                hostNames,
                keySize,
                (ushort)(lifetimeInYears * 12));

            return certificate;
        }

#endregion
#region PInvoke Declarations
        private const string KEY_CONTAINER_NAME = "UASDKDefaultKeyContainer3";
        private const string MS_STRONG_PROV_W = "Microsoft Strong Cryptographic Provider";
        private const string DEFAULT_CRYPTO_PROVIDER = MS_STRONG_PROV_W;
        private const int X509_ASN_ENCODING = 0x00000001;
        private const int PKCS_7_ASN_ENCODING = 0x00010000;
        private const int CRYPT_DECODE_ALLOC_FLAG = 0x8000;
        private const int CRYPT_DECODE_NOCOPY_FLAG = 0x1;
        
        private const int AT_KEYEXCHANGE = 1;
        private const int AT_SIGNATURE = 2;
        
        private const int REPORT_NO_PRIVATE_KEY = 0x0001;
        private const int REPORT_NOT_ABLE_TO_EXPORT_PRIVATE_KEY = 0x0002;
        private const int EXPORT_PRIVATE_KEYS = 0x0004;
                
        private const int CERT_SET_KEY_PROV_HANDLE_PROP_ID = 0x00000001;
        private const int CERT_SET_KEY_CONTEXT_PROP_ID = 0x00000001;

        //+-------------------------------------------------------------------------
        //  Certificate name string types
        //--------------------------------------------------------------------------
        private const int CERT_SIMPLE_NAME_STR = 1;
        private const int CERT_OID_NAME_STR = 2;
        private const int CERT_X500_NAME_STR = 3;

        //+-------------------------------------------------------------------------
        //  Certificate name string type flags OR'ed with the above types
        //--------------------------------------------------------------------------
        private const int CERT_NAME_STR_SEMICOLON_FLAG = 0x40000000;
        private const int CERT_NAME_STR_NO_PLUS_FLAG = 0x20000000;
        private const int CERT_NAME_STR_NO_QUOTING_FLAG = 0x10000000;
        private const int CERT_NAME_STR_CRLF_FLAG = 0x08000000;
        private const int CERT_NAME_STR_COMMA_FLAG = 0x04000000;
        private const int CERT_NAME_STR_REVERSE_FLAG = 0x02000000;
        
        private const int CERT_NAME_STR_DISABLE_IE4_UTF8_FLAG = 0x00010000;
        private const int CERT_NAME_STR_ENABLE_T61_UNICODE_FLAG = 0x00020000;
        private const int CERT_NAME_STR_ENABLE_UTF8_UNICODE_FLAG = 0x00040000;
        
        // dwFlags definitions for CryptAcquireContext
        private const int CRYPT_VERIFYCONTEXT = unchecked((int)0xF0000000);
        private const int CRYPT_NEWKEYSET = 0x00000008;
        private const int CRYPT_DELETEKEYSET = 0x00000010;
        private const int CRYPT_MACHINE_KEYSET = 0x00000020;
        private const int CRYPT_USER_KEYSET = 0x00001000;
        private const int CRYPT_SILENT = 0x00000040;

        // dwFlag definitions for CryptGenKey
        private const int CRYPT_EXPORTABLE = 0x00000001;
        private const int CRYPT_USER_PROTECTED = 0x00000002;
        private const int CRYPT_CREATE_SALT = 0x00000004;
        private const int CRYPT_UPDATE_KEY = 0x00000008;
        private const int CRYPT_NO_SALT = 0x00000010;
        private const int CRYPT_PREGEN = 0x00000040;
        private const int CRYPT_RECIPIENT = 0x00000010;
        private const int CRYPT_INITIATOR = 0x00000040;
        private const int CRYPT_ONLINE = 0x00000080;
        private const int CRYPT_SF = 0x00000100;
        private const int CRYPT_CREATE_IV = 0x00000200;
        private const int CRYPT_KEK = 0x00000400;
        private const int CRYPT_DATA_KEY = 0x00000800;
        private const int CRYPT_VOLATILE = 0x00001000;
        private const int CRYPT_SGCKEY = 0x00002000;
        private const int CRYPT_ARCHIVABLE = 0x00004000;

        private const int CRYPT_FIRST = 0x00000001;
        private const int CRYPT_NEXT = 0x00000002;
        private const int PP_ENUMCONTAINERS = 0x00000002;
        private const int PP_CONTAINER = 6;
        private const int ERROR_MORE_DATA = 234;
        private const int KP_CERTIFICATE = 26;
        private const int KP_KEYLEN = 9;

        private const int PROV_RSA_FULL = 1;

        private const int NTE_EXISTS = -0x7FF6FFF1; // 0x8009000F
        private const int NTE_BAD_KEYSET = -0x7FF6FFEA; // 0x80090016
        private const int CRYPT_E_NOT_FOUND = -0x7FF6DFFC; // 0x80092004L

        //+-------------------------------------------------------------------------
        //  Certificate Store open/property flags
        //--------------------------------------------------------------------------
        private const int CERT_STORE_NO_CRYPT_RELEASE_FLAG = 0x00000001;
        private const int CERT_STORE_SET_LOCALIZED_NAME_FLAG = 0x00000002;
        private const int CERT_STORE_DEFER_CLOSE_UNTIL_LAST_FREE_FLAG = 0x00000004;
        private const int CERT_STORE_DELETE_FLAG = 0x00000010;
        private const int CERT_STORE_UNSAFE_PHYSICAL_FLAG = 0x00000020;
        private const int CERT_STORE_SHARE_STORE_FLAG = 0x00000040;
        private const int CERT_STORE_SHARE_CONTEXT_FLAG = 0x00000080;
        private const int CERT_STORE_MANIFOLD_FLAG = 0x00000100;
        private const int CERT_STORE_ENUM_ARCHIVED_FLAG = 0x00000200;
        private const int CERT_STORE_UPDATE_KEYID_FLAG = 0x00000400;
        private const int CERT_STORE_BACKUP_RESTORE_FLAG = 0x00000800;
        private const int CERT_STORE_READONLY_FLAG = 0x00008000;
        private const int CERT_STORE_OPEN_EXISTING_FLAG = 0x00004000;
        private const int CERT_STORE_CREATE_NEW_FLAG = 0x00002000;
        private const int CERT_STORE_MAXIMUM_ALLOWED_FLAG = 0x00001000;
        
        // Location of the system store:
        private const int CERT_SYSTEM_STORE_LOCATION_SHIFT = 16;

        //  Registry: HKEY_CURRENT_USER or HKEY_LOCAL_MACHINE
        private const int CERT_SYSTEM_STORE_CURRENT_USER_ID = 1;
        private const int CERT_SYSTEM_STORE_LOCAL_MACHINE_ID = 2;
        //  Registry: HKEY_LOCAL_MACHINE\Software\Microsoft\Cryptography\Services
        private const int CERT_SYSTEM_STORE_CURRENT_SERVICE_ID = 4;
        private const int CERT_SYSTEM_STORE_SERVICES_ID = 5;
        //  Registry: HKEY_USERS
        private const int CERT_SYSTEM_STORE_USERS_ID = 6;

        private const int CERT_SYSTEM_STORE_CURRENT_USER = (CERT_SYSTEM_STORE_CURRENT_USER_ID << CERT_SYSTEM_STORE_LOCATION_SHIFT);
        private const int CERT_SYSTEM_STORE_LOCAL_MACHINE = (CERT_SYSTEM_STORE_LOCAL_MACHINE_ID << CERT_SYSTEM_STORE_LOCATION_SHIFT);
        private const int CERT_SYSTEM_STORE_CURRENT_SERVICE = (CERT_SYSTEM_STORE_CURRENT_SERVICE_ID << CERT_SYSTEM_STORE_LOCATION_SHIFT);
        private const int CERT_SYSTEM_STORE_SERVICES = (CERT_SYSTEM_STORE_SERVICES_ID << CERT_SYSTEM_STORE_LOCATION_SHIFT);
        private const int CERT_SYSTEM_STORE_USERS = (CERT_SYSTEM_STORE_USERS_ID << CERT_SYSTEM_STORE_LOCATION_SHIFT);

        //+--------------------------------"2.5.29.7"-----------------------------------------
        //  Extension Object Identifiers
        //--------------------------------------------------------------------------
        private const string szOID_AUTHORITY_KEY_IDENTIFIER = "2.5.29.1";
        private const string szOID_KEY_ATTRIBUTES = "2.5.29.2";
        private const string szOID_CERT_POLICIES_95 = "2.5.29.3";
        private const string szOID_KEY_USAGE_RESTRICTION = "2.5.29.4";
        // private const string szOID_SUBJECT_ALT_NAME = "2.5.29.7";
        private const string szOID_ISSUER_ALT_NAME = "2.5.29.8";
        private const string szOID_BASIC_CONSTRAINTS = "2.5.29.10";
        private const string szOID_KEY_USAGE = "2.5.29.15";
        private const string szOID_PRIVATEKEY_USAGE_PERIOD = "2.5.29.16";
        private const string szOID_BASIC_CONSTRAINTS2 = "2.5.29.19";

        private const string szOID_CERT_POLICIES = "2.5.29.32";
        private const string szOID_ANY_CERT_POLICY = "2.5.29.32.0";

        private const string szOID_AUTHORITY_KEY_IDENTIFIER2 = "2.5.29.35";
        private const string szOID_SUBJECT_KEY_IDENTIFIER = "2.5.29.14";
        private const string szOID_SUBJECT_ALT_NAME2 = "2.5.29.17";
        private const string szOID_ISSUER_ALT_NAME2 = "2.5.29.18";
        private const string szOID_CRL_REASON_CODE = "2.5.29.21";
        private const string szOID_REASON_CODE_HOLD = "2.5.29.23";
        private const string szOID_CRL_DIST_POINTS = "2.5.29.31";
        private const string szOID_ENHANCED_KEY_USAGE = "2.5.29.37";
        
        private const string szOID_RSA_RSA = "1.2.840.113549.1.1.1";
                
        //+-------------------------------------------------------------------------
        //  Enhanced Key Usage (Purpose) Object Identifiers
        //--------------------------------------------------------------------------
        private const string szOID_PKIX_KP = "1.3.6.1.5.5.7.3";

        // Consistent key usage bits: DIGITAL_SIGNATURE, KEY_ENCIPHERMENT
        // or KEY_AGREEMENT
        private const string szOID_PKIX_KP_SERVER_AUTH = "1.3.6.1.5.5.7.3.1";

        // Consistent key usage bits: DIGITAL_SIGNATURE
        private const string szOID_PKIX_KP_CLIENT_AUTH = "1.3.6.1.5.5.7.3.2";

        // Consistent key usage bits: DIGITAL_SIGNATURE
        private const string szOID_PKIX_KP_CODE_SIGNING = "1.3.6.1.5.5.7.3.3";

        // Consistent key usage bits: DIGITAL_SIGNATURE, NON_REPUDIATION and/or
        // (KEY_ENCIPHERMENT or KEY_AGREEMENT)
        private const string szOID_PKIX_KP_EMAIL_PROTECTION = "1.3.6.1.5.5.7.3.4";

        // Consistent key usage bits: DIGITAL_SIGNATURE and/or
        // (KEY_ENCIPHERMENT or KEY_AGREEMENT)
        private const string szOID_PKIX_KP_IPSEC_END_SYSTEM = "1.3.6.1.5.5.7.3.5";

        // Consistent key usage bits: DIGITAL_SIGNATURE and/or
        // (KEY_ENCIPHERMENT or KEY_AGREEMENT)
        private const string szOID_PKIX_KP_IPSEC_TUNNEL = "1.3.6.1.5.5.7.3.6";

        // Consistent key usage bits: DIGITAL_SIGNATURE and/or
        // (KEY_ENCIPHERMENT or KEY_AGREEMENT)
        private const string szOID_PKIX_KP_IPSEC_USER = "1.3.6.1.5.5.7.3.7";

        // Consistent key usage bits: DIGITAL_SIGNATURE or NON_REPUDIATION
        private const string szOID_PKIX_KP_TIMESTAMP_SIGNING = "1.3.6.1.5.5.7.3.8";

        // IKE (Internet Key Exchange) Intermediate KP for an IPsec end entity.
        // Defined in draft-ietf-ipsec-pki-req-04.txt, December 14, 1999.
        private const string szOID_IPSEC_KP_IKE_INTERMEDIATE = "1.3.6.1.5.5.8.2.2";

        //+-------------------------------------------------------------------------
        //  Predefined X509 certificate extension data structures that can be
        //  encoded / decoded.
        //--------------------------------------------------------------------------
        private const int X509_CERT = 1;
        private const int X509_CERT_CRL_TO_BE_SIGNED = 3;
        private const int X509_AUTHORITY_KEY_ID = 9;
        private const int X509_KEY_ATTRIBUTES = 10;
        private const int X509_KEY_USAGE_RESTRICTION = 11;
        private const int X509_ALTERNATE_NAME = 12;
        private const int X509_BASIC_CONSTRAINTS = 13;
        private const int X509_KEY_USAGE = 14;
        private const int X509_BASIC_CONSTRAINTS2 = 15;
        private const int X509_CERT_POLICIES = 16;

        //+-------------------------------------------------------------------------
        // Certificate comparison functions
        //--------------------------------------------------------------------------
        private const int  CERT_COMPARE_MASK = 0xFFFF;
        private const int  CERT_COMPARE_SHIFT = 16;
        private const int  CERT_COMPARE_ANY = 0;
        private const int  CERT_COMPARE_SHA1_HASH = 1;
        private const int  CERT_COMPARE_NAME = 2;
        private const int  CERT_COMPARE_ATTR = 3;
        private const int  CERT_COMPARE_MD5_HASH = 4;
        private const int  CERT_COMPARE_PROPERTY = 5;
        private const int  CERT_COMPARE_PUBLIC_KEY = 6;
        private const int  CERT_COMPARE_HASH = CERT_COMPARE_SHA1_HASH;
        private const int  CERT_COMPARE_NAME_STR_A = 7;
        private const int  CERT_COMPARE_NAME_STR_W = 8;
        private const int  CERT_COMPARE_KEY_SPEC = 9;
        private const int  CERT_COMPARE_ENHKEY_USAGE = 10;
        private const int  CERT_COMPARE_CTL_USAGE = CERT_COMPARE_ENHKEY_USAGE;
        private const int  CERT_COMPARE_SUBJECT_CERT = 11;
        private const int  CERT_COMPARE_ISSUER_OF = 12;
        private const int  CERT_COMPARE_EXISTING = 13;
        private const int  CERT_COMPARE_SIGNATURE_HASH = 14;
        private const int  CERT_COMPARE_KEY_IDENTIFIER = 15;
        private const int  CERT_COMPARE_CERT_ID = 16;
        private const int  CERT_COMPARE_CROSS_CERT_DIST_POINTS = 17;
        private const int  CERT_COMPARE_PUBKEY_MD5_HASH = 18;
        
        //+-------------------------------------------------------------------------
        //  Certificate Information Flags
        //--------------------------------------------------------------------------
        private const int CERT_INFO_VERSION_FLAG = 1;
        private const int CERT_INFO_SERIAL_NUMBER_FLAG = 2;
        private const int CERT_INFO_SIGNATURE_ALGORITHM_FLAG = 3;
        private const int CERT_INFO_ISSUER_FLAG = 4;
        private const int CERT_INFO_NOT_BEFORE_FLAG = 5;
        private const int CERT_INFO_NOT_AFTER_FLAG = 6;
        private const int CERT_INFO_SUBJECT_FLAG = 7;
        private const int CERT_INFO_SUBJECT_PUBLIC_KEY_INFO_FLAG = 8;
        private const int CERT_INFO_ISSUER_UNIQUE_ID_FLAG = 9;
        private const int CERT_INFO_SUBJECT_UNIQUE_ID_FLAG = 10;
        private const int CERT_INFO_EXTENSION_FLAG = 11;

        //+-------------------------------------------------------------------------
        //  dwFindType
        //
        //  The dwFindType definition consists of two components:
        //   - comparison function
        //   - certificate information flag
        //--------------------------------------------------------------------------
        private const int  CERT_FIND_ANY = (CERT_COMPARE_ANY << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_SHA1_HASH = (CERT_COMPARE_SHA1_HASH << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_MD5_HASH = (CERT_COMPARE_MD5_HASH << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_SIGNATURE_HASH = (CERT_COMPARE_SIGNATURE_HASH << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_KEY_IDENTIFIER = (CERT_COMPARE_KEY_IDENTIFIER << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_HASH = CERT_FIND_SHA1_HASH;
        private const int  CERT_FIND_PROPERTY = (CERT_COMPARE_PROPERTY << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_PUBLIC_KEY = (CERT_COMPARE_PUBLIC_KEY << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_SUBJECT_NAME = (CERT_COMPARE_NAME << CERT_COMPARE_SHIFT | CERT_INFO_SUBJECT_FLAG);
        private const int  CERT_FIND_SUBJECT_ATTR = (CERT_COMPARE_ATTR << CERT_COMPARE_SHIFT | CERT_INFO_SUBJECT_FLAG);
        private const int  CERT_FIND_ISSUER_NAME = (CERT_COMPARE_NAME << CERT_COMPARE_SHIFT | CERT_INFO_ISSUER_FLAG);
        private const int  CERT_FIND_ISSUER_ATTR = (CERT_COMPARE_ATTR << CERT_COMPARE_SHIFT | CERT_INFO_ISSUER_FLAG);
        private const int  CERT_FIND_SUBJECT_STR_A = (CERT_COMPARE_NAME_STR_A << CERT_COMPARE_SHIFT | CERT_INFO_SUBJECT_FLAG);
        private const int  CERT_FIND_SUBJECT_STR_W = (CERT_COMPARE_NAME_STR_W << CERT_COMPARE_SHIFT | CERT_INFO_SUBJECT_FLAG);
        private const int  CERT_FIND_SUBJECT_STR = CERT_FIND_SUBJECT_STR_W;
        private const int  CERT_FIND_ISSUER_STR_A = (CERT_COMPARE_NAME_STR_A << CERT_COMPARE_SHIFT | CERT_INFO_ISSUER_FLAG);
        private const int  CERT_FIND_ISSUER_STR_W = (CERT_COMPARE_NAME_STR_W << CERT_COMPARE_SHIFT | CERT_INFO_ISSUER_FLAG);
        private const int  CERT_FIND_ISSUER_STR = CERT_FIND_ISSUER_STR_W;
        private const int  CERT_FIND_KEY_SPEC = (CERT_COMPARE_KEY_SPEC << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_ENHKEY_USAGE = (CERT_COMPARE_ENHKEY_USAGE << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_CTL_USAGE = CERT_FIND_ENHKEY_USAGE;
        private const int  CERT_FIND_SUBJECT_CERT = (CERT_COMPARE_SUBJECT_CERT << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_ISSUER_OF = (CERT_COMPARE_ISSUER_OF << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_EXISTING = (CERT_COMPARE_EXISTING << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_CERT_ID = (CERT_COMPARE_CERT_ID << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_CROSS_CERT_DIST_POINTS = (CERT_COMPARE_CROSS_CERT_DIST_POINTS << CERT_COMPARE_SHIFT);
        private const int  CERT_FIND_PUBKEY_MD5_HASH  = (CERT_COMPARE_PUBKEY_MD5_HASH << CERT_COMPARE_SHIFT);
        
        // Byte[0]
        private const int CERT_DIGITAL_SIGNATURE_KEY_USAGE = 0x80;
        private const int CERT_NON_REPUDIATION_KEY_USAGE = 0x40;
        private const int CERT_KEY_ENCIPHERMENT_KEY_USAGE = 0x20;
        private const int CERT_DATA_ENCIPHERMENT_KEY_USAGE = 0x10;
        private const int CERT_KEY_AGREEMENT_KEY_USAGE = 0x08;
        private const int CERT_KEY_CERT_SIGN_KEY_USAGE = 0x04;
        private const int CERT_OFFLINE_CRL_SIGN_KEY_USAGE = 0x02;
        private const int CERT_CRL_SIGN_KEY_USAGE = 0x02;
        private const int CERT_ENCIPHER_ONLY_KEY_USAGE = 0x01;
        // Byte[1]
        private const int CERT_DECIPHER_ONLY_KEY_USAGE = 0x80;

        // Algorithm classes
        private const int ALG_CLASS_ANY = (0);
        private const int ALG_CLASS_SIGNATURE = (1 << 13);
        private const int ALG_CLASS_MSG_ENCRYPT = (2 << 13);
        private const int ALG_CLASS_DATA_ENCRYPT = (3 << 13);
        private const int ALG_CLASS_HASH = (4 << 13);
        private const int ALG_CLASS_KEY_EXCHANGE = (5 << 13);
        private const int ALG_CLASS_ALL = (7 << 13);

        // Algorithm types
        private const int ALG_TYPE_ANY = (0);
        private const int ALG_TYPE_DSS = (1 << 9);
        private const int ALG_TYPE_RSA = (2 << 9);
        private const int ALG_TYPE_BLOCK = (3 << 9);
        private const int ALG_TYPE_STREAM = (4 << 9);
        private const int ALG_TYPE_DH = (5 << 9);
        private const int ALG_TYPE_SECURECHANNEL = (6 << 9);

        // Generic sub-ids
        private const int ALG_SID_ANY = (0);

        // Some RSA sub-ids
        private const int ALG_SID_RSA_ANY = 0;
        private const int ALG_SID_RSA_PKCS = 1;
        private const int ALG_SID_RSA_MSATWORK = 2;
        private const int ALG_SID_RSA_ENTRUST = 3;
        private const int ALG_SID_RSA_PGP = 4;

        // Some DSS sub-ids
        //
        private const int ALG_SID_DSS_ANY = 0;
        private const int ALG_SID_DSS_PKCS = 1;
        private const int ALG_SID_DSS_DMS = 2;

        // Block cipher sub ids
        // DES sub_ids
        private const int ALG_SID_DES = 1;
        private const int ALG_SID_3DES = 3;
        private const int ALG_SID_DESX = 4;
        private const int ALG_SID_IDEA = 5;
        private const int ALG_SID_CAST = 6;
        private const int ALG_SID_SAFERSK64 = 7;
        private const int ALG_SID_SAFERSK128 = 8;
        private const int ALG_SID_3DES_112 = 9;
        private const int ALG_SID_CYLINK_MEK = 12;
        private const int ALG_SID_RC5 = 13;
        private const int ALG_SID_AES_128 = 14;
        private const int ALG_SID_AES_192 = 15;
        private const int ALG_SID_AES_256 = 16;
        private const int ALG_SID_AES = 17;

        // Fortezza sub-ids
        private const int ALG_SID_SKIPJACK = 10;
        private const int ALG_SID_TEK = 11;

        // KP_MODE
        private const int CRYPT_MODE_CBCI = 6;       // ANSI CBC Interleaved
        private const int CRYPT_MODE_CFBP = 7;       // ANSI CFB Pipelined
        private const int CRYPT_MODE_OFBP = 8;       // ANSI OFB Pipelined
        private const int CRYPT_MODE_CBCOFM = 9;       // ANSI CBC + OF Masking
        private const int CRYPT_MODE_CBCOFMI = 10;      // ANSI CBC + OFM Interleaved

        // RC2 sub-ids
        private const int ALG_SID_RC2 = 2;

        // Stream cipher sub-ids
        private const int ALG_SID_RC4 = 1;
        private const int ALG_SID_SEAL = 2;

        // Diffie-Hellman sub-ids
        private const int ALG_SID_DH_SANDF = 1;
        private const int ALG_SID_DH_EPHEM = 2;
        private const int ALG_SID_AGREED_KEY_ANY = 3;
        private const int ALG_SID_KEA = 4;

        // Hash sub ids
        private const int ALG_SID_MD2 = 1;
        private const int ALG_SID_MD4 = 2;
        private const int ALG_SID_MD5 = 3;
        private const int ALG_SID_SHA = 4;
        private const int ALG_SID_SHA1 = 4;
        private const int ALG_SID_MAC = 5;
        private const int ALG_SID_RIPEMD = 6;
        private const int ALG_SID_RIPEMD160 = 7;
        private const int ALG_SID_SSL3SHAMD5 = 8;
        private const int ALG_SID_HMAC = 9;
        private const int ALG_SID_TLS1PRF = 10;
        private const int ALG_SID_HASH_REPLACE_OWF = 11;
        private const int ALG_SID_SHA_256 = 12;
        private const int ALG_SID_SHA_384 = 13;
        private const int ALG_SID_SHA_512 = 14;

        // secure channel sub ids
        private const int ALG_SID_SSL3_MASTER = 1;
        private const int ALG_SID_SCHANNEL_MASTER_HASH = 2;
        private const int ALG_SID_SCHANNEL_MAC_KEY = 3;
        private const int ALG_SID_PCT1_MASTER = 4;
        private const int ALG_SID_SSL2_MASTER = 5;
        private const int ALG_SID_TLS1_MASTER = 6;
        private const int ALG_SID_SCHANNEL_ENC_KEY = 7;

        // Our silly example sub-id
        private const int ALG_SID_EXAMPLE = 80;

        // algorithm identifier definitions
        private const int CALG_MD2 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_MD2);
        private const int CALG_MD4 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_MD4);
        private const int CALG_MD5 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_MD5);
        private const int CALG_SHA = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA);
        private const int CALG_SHA1 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA1);
        private const int CALG_MAC = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_MAC);
        private const int CALG_RSA_SIGN = (ALG_CLASS_SIGNATURE | ALG_TYPE_RSA | ALG_SID_RSA_ANY);
        private const int CALG_DSS_SIGN = (ALG_CLASS_SIGNATURE | ALG_TYPE_DSS | ALG_SID_DSS_ANY);
        private const int CALG_NO_SIGN = (ALG_CLASS_SIGNATURE | ALG_TYPE_ANY | ALG_SID_ANY);
        private const int CALG_RSA_KEYX = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_RSA|ALG_SID_RSA_ANY);
        private const int CALG_DES = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_DES);
        private const int CALG_3DES_112 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_3DES_112);
        private const int CALG_3DES = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_3DES);
        private const int CALG_DESX = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_DESX);
        private const int CALG_RC2 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_RC2);
        private const int CALG_RC4 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_STREAM|ALG_SID_RC4);
        private const int CALG_SEAL = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_STREAM|ALG_SID_SEAL);
        private const int CALG_DH_SF = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_DH|ALG_SID_DH_SANDF);
        private const int CALG_DH_EPHEM = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_DH|ALG_SID_DH_EPHEM);
        private const int CALG_AGREEDKEY_ANY = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_DH|ALG_SID_AGREED_KEY_ANY);
        private const int CALG_KEA_KEYX = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_DH|ALG_SID_KEA);
        private const int CALG_HUGHES_MD5 = (ALG_CLASS_KEY_EXCHANGE|ALG_TYPE_ANY|ALG_SID_MD5);
        private const int CALG_SKIPJACK = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_SKIPJACK);
        private const int CALG_TEK = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_TEK);
        private const int CALG_CYLINK_MEK = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_CYLINK_MEK);
        private const int CALG_SSL3_SHAMD5 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SSL3SHAMD5);
        private const int CALG_SSL3_MASTER = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_SSL3_MASTER);
        private const int CALG_SCHANNEL_MASTER_HASH = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_SCHANNEL_MASTER_HASH);
        private const int CALG_SCHANNEL_MAC_KEY = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_SCHANNEL_MAC_KEY);
        private const int CALG_SCHANNEL_ENC_KEY = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_SCHANNEL_ENC_KEY);
        private const int CALG_PCT1_MASTER = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_PCT1_MASTER);
        private const int CALG_SSL2_MASTER = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_SSL2_MASTER);
        private const int CALG_TLS1_MASTER = (ALG_CLASS_MSG_ENCRYPT|ALG_TYPE_SECURECHANNEL|ALG_SID_TLS1_MASTER);
        private const int CALG_RC5 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_RC5);
        private const int CALG_HMAC = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_HMAC);
        private const int CALG_TLS1PRF = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_TLS1PRF);
        private const int CALG_HASH_REPLACE_OWF = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_HASH_REPLACE_OWF);
        private const int CALG_AES_128 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_AES_128);
        private const int CALG_AES_192 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_AES_192);
        private const int CALG_AES_256 = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_AES_256);
        private const int CALG_AES = (ALG_CLASS_DATA_ENCRYPT|ALG_TYPE_BLOCK|ALG_SID_AES);
        private const int CALG_SHA_256 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA_256);
        private const int CALG_SHA_384 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA_384);
        private const int CALG_SHA_512 = (ALG_CLASS_HASH | ALG_TYPE_ANY | ALG_SID_SHA_512);
        
        //+-------------------------------------------------------------------------
        // Add certificate/CRL, encoded, context or element disposition values.
        //--------------------------------------------------------------------------
        private const int CERT_STORE_ADD_NEW = 1;
        private const int CERT_STORE_ADD_USE_EXISTING = 2;
        private const int CERT_STORE_ADD_REPLACE_EXISTING = 3;
        private const int CERT_STORE_ADD_ALWAYS = 4;
        private const int CERT_STORE_ADD_REPLACE_EXISTING_INHERIT_PROPERTIES = 5;
        private const int CERT_STORE_ADD_NEWER = 6;
        private const int CERT_STORE_ADD_NEWER_INHERIT_PROPERTIES = 7;
                
        //+-------------------------------------------------------------------------
        //  Certificate, CRL and CTL property IDs
        //
        //  See CertSetCertificateContextProperty or CertGetCertificateContextProperty
        //  for usage information.
        //--------------------------------------------------------------------------
        private const int CERT_KEY_PROV_HANDLE_PROP_ID = 1;
        private const int CERT_KEY_PROV_INFO_PROP_ID = 2;
        private const int CERT_SHA1_HASH_PROP_ID = 3;
        private const int CERT_MD5_HASH_PROP_ID = 4;
        private const int CERT_HASH_PROP_ID = CERT_SHA1_HASH_PROP_ID;
        private const int CERT_KEY_CONTEXT_PROP_ID = 5;
        private const int CERT_KEY_SPEC_PROP_ID = 6;
        private const int CERT_IE30_RESERVED_PROP_ID = 7;
        private const int CERT_PUBKEY_HASH_RESERVED_PROP_ID = 8;
        private const int CERT_ENHKEY_USAGE_PROP_ID = 9;
        private const int CERT_CTL_USAGE_PROP_ID = CERT_ENHKEY_USAGE_PROP_ID;
        private const int CERT_NEXT_UPDATE_LOCATION_PROP_ID = 10;
        private const int CERT_FRIENDLY_NAME_PROP_ID = 11;
        private const int CERT_PVK_FILE_PROP_ID = 12;
        private const int CERT_DESCRIPTION_PROP_ID = 13;
        private const int CERT_ACCESS_STATE_PROP_ID = 14;
        private const int CERT_SIGNATURE_HASH_PROP_ID = 15;
        private const int CERT_SMART_CARD_DATA_PROP_ID = 16;
        private const int CERT_EFS_PROP_ID = 17;
        private const int CERT_FORTEZZA_DATA_PROP_ID = 18;
        private const int CERT_ARCHIVED_PROP_ID = 19;
        private const int CERT_KEY_IDENTIFIER_PROP_ID = 20;
        private const int CERT_AUTO_ENROLL_PROP_ID = 21;
        private const int CERT_PUBKEY_ALG_PARA_PROP_ID = 22;
        private const int CERT_CROSS_CERT_DIST_POINTS_PROP_ID = 23;
        private const int CERT_ISSUER_PUBLIC_KEY_MD5_HASH_PROP_ID = 24;
        private const int CERT_SUBJECT_PUBLIC_KEY_MD5_HASH_PROP_ID = 25;
        private const int CERT_ENROLLMENT_PROP_ID = 26;
        private const int CERT_DATE_STAMP_PROP_ID = 27;
        private const int CERT_ISSUER_SERIAL_NUMBER_MD5_HASH_PROP_ID = 28;
        private const int CERT_SUBJECT_NAME_MD5_HASH_PROP_ID = 29;
        private const int CERT_EXTENDED_ERROR_INFO_PROP_ID = 30;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        };


        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_NAME_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_OBJID_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1049:TypesThatOwnNativeResourcesShouldBeDisposable")]
        private struct CRYPT_DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_INTEGER_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_BIT_BLOB
        {
            public int cbData;
            public IntPtr pbData;
            public int cUnusedBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_EXTENSION
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pszObjId;
            public int fCritical;
            public CRYPT_OBJID_BLOB Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_EXTENSIONS
        {
            public int cExtension;
            public IntPtr rgExtension;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_AUTHORITY_KEY_ID_INFO
        {
            public CRYPT_DATA_BLOB KeyId;
            public CERT_NAME_BLOB CertIssuer;
            public CRYPT_INTEGER_BLOB CertSerialNumber;
        };
        
        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_AUTHORITY_KEY_ID2_INFO
        {
            public CRYPT_DATA_BLOB    KeyId;
            public CERT_ALT_NAME_INFO AuthorityCertIssuer;
            public CRYPT_INTEGER_BLOB AuthorityCertSerialNumber;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_BASIC_CONSTRAINTS2_INFO
        {
            public int fCA;
            public int fPathLenConstraint;
            public int dwPathLenConstraint;
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1049:TypesThatOwnNativeResourcesShouldBeDisposable")]
        private struct CERT_ENHKEY_USAGE
        {
            public int cUsageIdentifier;
            public IntPtr rgpszUsageIdentifier;
        }

        [StructLayout(LayoutKind.Explicit)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1049:TypesThatOwnNativeResourcesShouldBeDisposable")]
        private struct CERT_ALT_NAME_ENTRY_UNION
        {
            /*
            [FieldOffset(0)]
            public IntPtr pOtherName;         // 1
            [FieldOffset(0)]
            public IntPtr pwszRfc822Name;     // 2  (encoded IA5)
            */
            
            [FieldOffset(0)]
            public IntPtr pwszDNSName;        // 3  (encoded IA5)
            
            /*
            [FieldOffset(0)]
            public CERT_NAME_BLOB DirectoryName;      // 5
            */
            
            [FieldOffset(0)]
            public IntPtr pwszURL;            // 7  (encoded IA5)
            [FieldOffset(0)]
            public CRYPT_DATA_BLOB IPAddress;          // 8  (Octet String)
            
            /*
            [FieldOffset(0)]
            public IntPtr pszRegisteredID;    // 9  (Object Identifer)
            */
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_ALT_NAME_ENTRY
        {
            public int  dwAltNameChoice;
            public CERT_ALT_NAME_ENTRY_UNION Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_ALT_NAME_INFO
        {
            public int cAltEntry;
            public IntPtr rgAltEntry;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_ALGORITHM_IDENTIFIER
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pszObjId;
            public CRYPT_OBJID_BLOB Parameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_PUBLIC_KEY_INFO
        {
            public CRYPT_ALGORITHM_IDENTIFIER Algorithm;
            public CRYPT_BIT_BLOB PublicKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_KEY_PROV_INFO
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwszContainerName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pwszProvName;
            public int dwProvType;
            public int dwFlags;
            public int cProvParam;
            public IntPtr rgProvParam;
            public int dwKeySpec;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_SIGNED_CONTENT_INFO
        {
            public CRYPT_DATA_BLOB ToBeSigned;
            public CRYPT_ALGORITHM_IDENTIFIER SignatureAlgorithm;
            public CRYPT_BIT_BLOB Signature;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct CRL_INFO
        {
            public int dwVersion;
            public CRYPT_ALGORITHM_IDENTIFIER SignatureAlgorithm;
            public CERT_NAME_BLOB Issuer;
            public System.Runtime.InteropServices.ComTypes.FILETIME ThisUpdate;
            public System.Runtime.InteropServices.ComTypes.FILETIME NextUpdate;
            public int cCRLEntry;
            public IntPtr rgCRLEntry;
            public int cExtension;
            public IntPtr rgExtension;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct CRL_ENTRY
        {
            public CRYPT_INTEGER_BLOB SerialNumber;
            public System.Runtime.InteropServices.ComTypes.FILETIME RevocationDate;
            public int cExtension;
            public IntPtr rgExtension;
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_INFO
        {
            public int dwVersion;
            public CRYPT_INTEGER_BLOB SerialNumber;
            public CRYPT_ALGORITHM_IDENTIFIER SignatureAlgorithm;
            public CERT_NAME_BLOB Issuer;
            public System.Runtime.InteropServices.ComTypes.FILETIME NotBefore;
            public System.Runtime.InteropServices.ComTypes.FILETIME NotAfter;
            public CERT_NAME_BLOB Subject;
            public CERT_PUBLIC_KEY_INFO SubjectPublicKeyInfo;
            public CRYPT_BIT_BLOB IssuerUniqueId;
            public CRYPT_BIT_BLOB SubjectUniqueId;
            public int cExtension;
            public IntPtr rgExtension;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CERT_CONTEXT
        {
            public int dwCertEncodingType;
            public IntPtr pbCertEncoded;
            public int cbCertEncoded;
            public IntPtr pCertInfo;
            public IntPtr hCertStore;
        }

        private const int CERT_ALT_NAME_OTHER_NAME = 1;
        private const int CERT_ALT_NAME_RFC822_NAME = 2;
        private const int CERT_ALT_NAME_DNS_NAME = 3;
        private const int CERT_ALT_NAME_X400_ADDRESS = 4;
        private const int CERT_ALT_NAME_DIRECTORY_NAME = 5;
        private const int CERT_ALT_NAME_EDI_PARTY_NAME = 6;
        private const int CERT_ALT_NAME_URL = 7;
        private const int CERT_ALT_NAME_IP_ADDRESS = 8;
        private const int CERT_ALT_NAME_REGISTERED_ID = 9;
  
        [StructLayout(LayoutKind.Sequential)]
        private struct CRYPT_ENCODE_PARA
        {
            public int cbSize;
            public IntPtr pfnAlloc;
            public IntPtr pfnFree;
        };

        private const int CERT_STORE_PROV_MEMORY = 2;
        private const int CERT_STORE_PROV_SYSTEM_W = 10;
        private const int CERT_STORE_PROV_SYSTEM = CERT_STORE_PROV_SYSTEM_W;
                
        private const int CERT_CLOSE_STORE_FORCE_FLAG = 0x00000001;
        private const int CERT_CLOSE_STORE_CHECK_FLAG = 0x00000002;
#endregion

#region Private Methods
        /// <summary>
        /// Sets the parameters to suitable defaults.
        /// </summary>
        private static void SetSuitableDefaults(
            ref string applicationUri,
            ref string applicationName,
            ref string subjectName,
            ref IList<String> domainNames,
            ref ushort keySize,
            ref ushort lifetimeInMonths)
        {
            // enforce minimum keysize.
            if (keySize < 1024)
            {
                keySize = 1024;
            }

            if (keySize%1024 != 0)
            {
                throw new ArgumentNullException("keySize", "KeySize must be a multiple of 1024.");
            }

            // enforce minimum lifetime.
            if (lifetimeInMonths < 1)
            {
                lifetimeInMonths = 1;
            }

            // parse the subject name if specified.
            List<string> subjectNameEntries = null;

            if (!String.IsNullOrEmpty(subjectName))
            {
                subjectNameEntries = Utils.ParseDistinguishedName(subjectName);
            }

            // check the application name.
            if (String.IsNullOrEmpty(applicationName))
            {
                if (subjectNameEntries == null)
                {
                    throw new ArgumentNullException("applicationName", "Must specify a applicationName or a subjectName.");
                }

                // use the common name as the application name.
                for (int ii = 0; ii < subjectNameEntries.Count; ii++)
                {
                    if (subjectNameEntries[ii].StartsWith("CN="))
                    {
                        applicationName = subjectNameEntries[ii].Substring(3).Trim();
                        break;
                    }
                }
            }

            // remove special characters from name.
            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < applicationName.Length; ii++)
            {
                char ch = applicationName[ii];

                if (Char.IsControl(ch) || ch == '/' || ch == ',' || ch == ';')
                {
                    ch = '+';
                }

                buffer.Append(ch);
            }

            applicationName = buffer.ToString();

            // ensure at least one host name.
            if (domainNames == null || domainNames.Count == 0)
            {
                domainNames = new List<string>();
                domainNames.Add(Utils.GetHostName());
            }

            // create the application uri.
            if (String.IsNullOrEmpty(applicationUri))
            {
                StringBuilder builder = new StringBuilder();

                builder.Append("urn:");
                builder.Append(domainNames[0]);
                builder.Append(":");
                builder.Append(applicationName);

                applicationUri = builder.ToString();
            }
            
            Uri uri = Utils.ParseUri(applicationUri);

            if (uri == null)
            {
                throw new ArgumentNullException("applicationUri", "Must specify a valid URL.");
            }

            // create the subject name,
            if (String.IsNullOrEmpty(subjectName))
            {
                subjectName = Utils.Format("CN={0}/DC={1}", applicationName, domainNames[0]);
            }
        }

        /// <summary>
        /// Combines the flags for use in an operation.
        /// </summary>
        private static int GetFlags(bool useMachineStore, params int[] choices)
        {
            int flags = 0;

            if (choices != null)
            {
                if (choices.Length > 0)
                {
                    flags |= choices[0];
                }

                if (useMachineStore && choices.Length > 1)
                {
                    flags |= choices[1];
                }

                if (!useMachineStore && choices.Length > 2)
                {
                    flags |= choices[2];
                }
            }

            return flags;
        }

        /// <summary>
        /// Combines the flags for use in an operation.
        /// </summary>
        private static void Throw(string format, params object[] args)
        {
            throw ServiceResultException.Create(StatusCodes.BadUnexpectedError, format, args);
        }
        
        /// <summary>
        /// Changes the delimiter used to seperate fields in a subject name.
        /// </summary>
        private static string ChangeSubjectNameDelimiter(string name, char delimiter)
        {
            StringBuilder buffer = new StringBuilder();
            List<string> elements = Utils.ParseDistinguishedName(name);

            for (int ii = 0; ii < elements.Count; ii++)
            {
                string element = elements[ii];

                if (buffer.Length > 0)
                {
                    buffer.Append(delimiter);
                }

                if (element.IndexOf(delimiter) != -1)
                {
                    int index = element.IndexOf('=');

                    buffer.Append(element.Substring(0, index+1));

                    if (element.Length > index+1 && element[index+1] != '"')
                    {
                        buffer.Append('"');
                    }

                    buffer.Append(element.Substring(index+1));

                    if (element.Length > 0 && element[element.Length-1] != '"')
                    {
                        buffer.Append('"');
                    }

                    continue;
                }

                buffer.Append(elements[ii]);
            }

            return buffer.ToString();
        }

        // frees the memory used by a X500 name blob.
        private static void DeleteX500Name(ref CERT_NAME_BLOB pName)
        {
	        Marshal.FreeHGlobal(pName.pbData);
	        pName.pbData = IntPtr.Zero;
	        pName.cbData = 0;
        }

       // allocates a copy of an array of bytes.
        static IntPtr AllocBytes(byte[] bytes)
        {
	        if (bytes == null)
	        {
		        return IntPtr.Zero;
	        }

	        IntPtr pCopy = (IntPtr)Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pCopy, bytes.Length);

	        return pCopy;
        }

        /// <summary>
        /// Parses an array of alternate names.
        /// </summary>
        private static void ParseAltNameInfo(
            CERT_ALT_NAME_INFO names,
            List<string> fields)
        {
            IntPtr pPos = names.rgAltEntry;

            for (int ii = 0; ii < names.cAltEntry; ii++)
            {
                CERT_ALT_NAME_ENTRY pEntry = (CERT_ALT_NAME_ENTRY)Marshal.PtrToStructure< CERT_ALT_NAME_ENTRY>(pPos);
                pPos = new IntPtr(pPos.ToInt64() + Marshal.SizeOf<CERT_ALT_NAME_ENTRY>());

                switch (pEntry.dwAltNameChoice)
                {
                    case CERT_ALT_NAME_URL:
                    {
                        string url = Marshal.PtrToStringUni(pEntry.Value.pwszURL);
                        fields.Add("URL=" + url);
                        break;
                    }

                    case CERT_ALT_NAME_DNS_NAME:
                    {
                        string dns = Marshal.PtrToStringUni(pEntry.Value.pwszURL);
                        fields.Add("DNSName=" + dns);
                        break;
                    }

                    case CERT_ALT_NAME_RFC822_NAME:
                    {
                        string email = Marshal.PtrToStringUni(pEntry.Value.pwszURL);
                        fields.Add("Email=" + email);
                        break;
                    }

                    case CERT_ALT_NAME_REGISTERED_ID:
                    {
                        string oid = Marshal.PtrToStringUni(pEntry.Value.pwszURL);
                        fields.Add("OID=" + oid);
                        break;
                    }

                    case CERT_ALT_NAME_IP_ADDRESS:
                    {
                        byte[] addressBytes = new byte[pEntry.Value.IPAddress.cbData];
                        Marshal.Copy(pEntry.Value.IPAddress.pbData, addressBytes, 0, addressBytes.Length);
                        System.Net.IPAddress address = new System.Net.IPAddress(addressBytes);
                        fields.Add("IPAddress=" + address.ToString());
                        break;
                    }
                }
            }
        }

        // frees the memory used by an encoded certificate extension.
        private static void DeleteExtensions(ref IntPtr pExtensions, int count)
        {
	        if (pExtensions != null)
	        {
                IntPtr pPos = pExtensions;

		        for (int ii = 0; ii < count; ii++)
		        {
                    CERT_EXTENSION pExtension = (CERT_EXTENSION)Marshal.PtrToStructure<CERT_EXTENSION>(pPos);
                    Marshal.DestroyStructure< CERT_EXTENSION>(pPos);

			        if (pExtension.Value.pbData != IntPtr.Zero)
			        {
				        Marshal.FreeHGlobal(pExtension.Value.pbData);
			        }

                    pPos = new IntPtr(pPos.ToInt64() + Marshal.SizeOf<CERT_EXTENSION>());
		        }
				        
                Marshal.FreeHGlobal(pExtensions);
                pExtensions = IntPtr.Zero;
	        }
        }

#endregion
      
    }
}

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Dummmy class designed to prevent compile errors.
    /// </summary>
    [Obsolete("Class moved to Opc.Ua namespace.")]
    public class CertificateFactory : Opc.Ua.CertificateFactory
    {
    }
}
