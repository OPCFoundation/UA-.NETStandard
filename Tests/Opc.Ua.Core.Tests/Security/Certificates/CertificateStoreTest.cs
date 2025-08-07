/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.X509StoreExtensions;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture, Category("CertificateStore")]
    [NonParallelizable]
    [SetCulture("en-us")]
    public class CertificateStoreTest
    {
        #region DataPointSources
        public const string X509StoreSubject = "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=X509Store, C=US";
        public const string X509StoreSubject2 = "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=X509Store2, C=US";

        [DatapointSource]
        public string[] CertStores = GetCertStores();
        #endregion

        #region Test Setup
        /// <summary>
        /// Clean up the test cert store folder.
        /// </summary>
        [OneTimeTearDown]
        protected async Task OneTimeTearDown()
        {
            foreach (var certStore in CertStores)
            {
                using (var x509Store = new X509CertificateStore())
                {
                    x509Store.Open(certStore);
                    var collection = await x509Store.Enumerate().ConfigureAwait(false);
                    foreach (var cert in collection)
                    {
                        if (X509Utils.CompareDistinguishedName(X509StoreSubject, cert.Subject))
                        {
                            await x509Store.Delete(cert.Thumbprint).ConfigureAwait(false);
                        }
                        if (X509Utils.CompareDistinguishedName(X509StoreSubject2, cert.Issuer))
                        {
                            await x509Store.Delete(cert.Thumbprint).ConfigureAwait(false);
                        }
                    }
                    if (x509Store.SupportsCRLs)
                    {
                        X509CRLCollection crls = x509Store.EnumerateCRLs().Result;
                        foreach (X509CRL crl in crls)
                        {
                            if (X509Utils.CompareDistinguishedName(X509StoreSubject, crl.Issuer))
                            {
                                await x509Store.DeleteCRL(crl).ConfigureAwait(false);
                            }
                            if (X509Utils.CompareDistinguishedName(X509StoreSubject2, crl.Issuer))
                            {
                                await x509Store.DeleteCRL(crl).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            Utils.SilentDispose(m_testCertificate);
        }
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify new app certificate is stored in X509Store with private key.
        /// </summary>
        [Theory, Order(10)]
        public async Task VerifyAppCertX509Store(string storePath)
        {
            var appCertificate = GetTestCert();
            Assert.NotNull(appCertificate);
            Assert.True(appCertificate.HasPrivateKey);
            appCertificate.AddToStore(
                    CertificateStoreType.X509Store,
                    storePath
                );
            using (var publicKey = X509CertificateLoader.LoadCertificate(appCertificate.RawData))
            {
                Assert.NotNull(publicKey);
                Assert.False(publicKey.HasPrivateKey);

                var id = new CertificateIdentifier() {
                    Thumbprint = publicKey.Thumbprint,
                    StorePath = storePath,
                    StoreType = CertificateStoreType.X509Store
                };
                var privateKey = await id.LoadPrivateKey(null).ConfigureAwait(false);
                Assert.NotNull(privateKey);
                Assert.True(privateKey.HasPrivateKey);

                X509Utils.VerifyRSAKeyPair(publicKey, privateKey, true);

                using (var x509Store = new X509CertificateStore())
                {
                    x509Store.Open(storePath);
                    await x509Store.Delete(publicKey.Thumbprint).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verify new app certificate is stored in Directory Store
        /// with password for private key (PFX).
        /// </summary>
        [Test, Order(20)]
        public async Task VerifyAppCertDirectoryStore()
        {
            var appCertificate = GetTestCert();
            Assert.NotNull(appCertificate);
            Assert.True(appCertificate.HasPrivateKey);

            string password = Guid.NewGuid().ToString();
            // pki directory root for app cert
            var pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            var storePath = pkiRoot + "own";
            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath, false);
            const string storeType = CertificateStoreType.Directory;
            appCertificate.AddToStore(
                certificateStoreIdentifier, password
                );

            using (var publicKey = X509CertificateLoader.LoadCertificate(appCertificate.RawData))
            {
                Assert.NotNull(publicKey);
                Assert.False(publicKey.HasPrivateKey);

                var id = new CertificateIdentifier() {
                    Thumbprint = publicKey.Thumbprint,
                    StorePath = storePath,
                    StoreType = storeType
                };

                {
                    // check no password fails to load
                    var nullKey = await id.LoadPrivateKey(null).ConfigureAwait(false);
                    Assert.IsNull(nullKey);
                }

                {
                    // check invalid password fails to load
                    var nullKey = await id.LoadPrivateKey("123").ConfigureAwait(false);
                    Assert.IsNull(nullKey);
                }

                {
                    // check invalid password fails to load
                    var nullKey = await id.LoadPrivateKeyEx(new CertificatePasswordProvider("123")).ConfigureAwait(false);
                    Assert.IsNull(nullKey);
                }

                var privateKey = await id.LoadPrivateKeyEx(new CertificatePasswordProvider(password)).ConfigureAwait(false);

                Assert.NotNull(privateKey);
                Assert.True(privateKey.HasPrivateKey);

                X509Utils.VerifyRSAKeyPair(publicKey, privateKey, true);

                using (ICertificateStore store = Opc.Ua.CertificateStoreIdentifier.CreateStore(storeType))
                {
                    store.Open(storePath, false);
                    await store.Delete(publicKey.Thumbprint).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verify PEM Certs are stored in Directory Store
        /// </summary>
        [Test, Order(25)]
        public async Task VerifyPEMSupportDirectoryStore()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // pki directory root for app cert
            var pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            var storePath = pkiRoot + "trusted";
            var certPath = storePath + Path.DirectorySeparatorChar + "certs";
            var privatePath = storePath + Path.DirectorySeparatorChar + "private";

            Directory.CreateDirectory(certPath);
            Directory.CreateDirectory(privatePath);

            var store = new DirectoryCertificateStore(false);

            try
            {
                store.Open(storePath, false);
                //Add Test PEM Chain
                File.Copy(TestUtils.EnumerateTestAssets("Test_chain.pem").First(), certPath + Path.DirectorySeparatorChar + "Test_chain.pem");

                var certificates = await store.Enumerate().ConfigureAwait(false);

                Assert.AreEqual(3, certificates.Count);

                //Add private key for leaf cert to private folder
                File.WriteAllBytes(privatePath + Path.DirectorySeparatorChar + "Test_chain.pem", DecryptKeyPairPemBase64());

                //refresh store to obtain private key
                await store.Enumerate().ConfigureAwait(false);


                //Load private key
                var cert = await store.LoadPrivateKey("14A630438BF775E19169D3279069BBF20419EF84", null, null, null, null).ConfigureAwait(false);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.Delete("14A630438BF775E19169D3279069BBF20419EF84").ConfigureAwait(false);

                //remove private key
                File.Delete(privatePath + Path.DirectorySeparatorChar + "Test_chain.pem");

                certificates = await store.Enumerate().ConfigureAwait(false);

                Assert.AreEqual(2, certificates.Count);
                Assert.IsEmpty(certificates.Find(X509FindType.FindByThumbprint, "14A630438BF775E19169D3279069BBF20419EF84", false));
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        /// <summary>
        /// Verify PEM Certs with private key in single file are stored in Directory Store
        /// </summary>
        [Test, Order(25)]
        public async Task VerifyPEMSupportPrivateKeyPairDirectoryStore()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            // pki directory root for app cert
            var pkiRoot = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;
            var storePath = pkiRoot + "trusted";
            var certPath = storePath + Path.DirectorySeparatorChar + "certs";
            var privatePath = storePath + Path.DirectorySeparatorChar + "private";

            Directory.CreateDirectory(certPath);
            Directory.CreateDirectory(privatePath);

            var store = new DirectoryCertificateStore(false);

            try
            {
                store.Open(storePath, false);
                // Add leaf cert with private key
                File.WriteAllBytes(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem", DecryptKeyPairPemBase64());

                var certificates = await store.Enumerate().ConfigureAwait(false);

                Assert.AreEqual(1, certificates.Count);

                Assert.NotNull(certificates.Find(X509FindType.FindByThumbprint, "14A630438BF775E19169D3279069BBF20419EF84", false));
                //Load private key
                var cert = await store.LoadPrivateKey("14A630438BF775E19169D3279069BBF20419EF84", null, null, null, null).ConfigureAwait(false);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.Delete("14A630438BF775E19169D3279069BBF20419EF84").ConfigureAwait(false);

                //ensure private key is removed
                Assert.False(File.Exists(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem"));
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        /// <summary>
        /// Verify that old invalid cert stores throw.
        /// </summary>
        [Test, Order(30)]
        public void VerifyInvalidAppCertX509Store()
        {
            var appCertificate = GetTestCert();
            _ = Assert.Throws<ServiceResultException>(
                () => appCertificate.AddToStore(
                    CertificateStoreType.X509Store,
                    "User\\UA_MachineDefault"));
            _ = Assert.Throws<ServiceResultException>(
                () => appCertificate.AddToStore(
                    CertificateStoreType.X509Store,
                    "System\\UA_MachineDefault"));
        }

        /// <summary>
        /// Verify X509 Store supports no Crls on Linux or MacOs
        /// </summary>
        /// <param name="storePath"></param>
        [Theory, Order(40)]
        public void VerifyNoCrlSupportOnLinuxOrMacOsX509Store(string storePath)
        {
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.False(x509Store.SupportsCRLs);
                    Assert.Throws<ServiceResultException>(() => x509Store.EnumerateCRLs());
                }
                else
                {
                    Assert.True(x509Store.SupportsCRLs);
                }
            }
        }

        /// <summary>
        /// Add an new crl to the X509 store
        /// </summary>
        /// <returns></returns>
        [Theory, Order(50)]
        public async Task AddAndEnumerateNewCrlInX509StoreOnWindows(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);

                Assert.True(x509Store.SupportsCRLs);

                //add issuer to store
                await x509Store.Add(GetTestCert()).ConfigureAwait(false);
                //add Crl
                var crlBuilder = CrlBuilder.Create(GetTestCert().SubjectName);
                crlBuilder.AddRevokedCertificate(GetTestCert());
                var crl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert()));
                await x509Store.AddCRL(crl).ConfigureAwait(false);

                //enumerate Crls
                X509CRLCollection crls = await x509Store.EnumerateCRLs().ConfigureAwait(false);

                Assert.AreEqual(1, crls.Count);
                Assert.AreEqual(crl.RawData, crls[0].RawData);
                Assert.AreEqual(GetTestCert().SerialNumber,
                    crls[0].RevokedCertificates.First().SerialNumber);

                //TestRevocation
                var statusCode = await x509Store.IsRevoked(GetTestCert(), GetTestCert()).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevoked, statusCode);


            }
        }

        /// <summary>
        /// Enumerate and update an existing crl to the X509 store
        /// </summary>
        /// <returns></returns>
        [Theory, Order(60)]
        public async Task UpdateExistingCrlInX509StoreOnWindows(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);
                //enumerate Crls
                X509CRL crl = (await x509Store.EnumerateCRLs().ConfigureAwait(false)).First();

                //Test Revocation before adding cert
                var statusCode = await x509Store.IsRevoked(GetTestCert(), GetTestCert2()).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

                var crlBuilder = CrlBuilder.Create(crl);

                crlBuilder.AddRevokedCertificate(GetTestCert2());
                var updatedCrl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert()));
                await x509Store.AddCRL(updatedCrl).ConfigureAwait(false);

                X509CRLCollection crls = await x509Store.EnumerateCRLs().ConfigureAwait(false);

                Assert.AreEqual(1, crls.Count);

                Assert.AreEqual(2, crls[0].RevokedCertificates.Count);
                //Test Revocation after adding cert
                var statusCode2 = await x509Store.IsRevoked(GetTestCert(), GetTestCert2()).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevoked, statusCode2);
            }
        }

        /// <summary>
        /// Add a second crl to the X509 store
        /// </summary>
        /// <returns></returns>
        [Theory, Order(70)]
        public async Task AddSecondCrlToX509StoreOnWindows(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);
                //add issuer to store
                await x509Store.Add(GetTestCert2()).ConfigureAwait(false);
                //add Crl
                var crlBuilder = CrlBuilder.Create(GetTestCert2().SubjectName);
                crlBuilder.AddRevokedCertificate(GetTestCert2());
                var crl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert2()));
                await x509Store.AddCRL(crl).ConfigureAwait(false);

                //enumerate Crls
                X509CRLCollection crls = await x509Store.EnumerateCRLs().ConfigureAwait(false);

                Assert.AreEqual(2, crls.Count);
                Assert.NotNull(crls.SingleOrDefault(c => c.Issuer == crl.Issuer));
            }
        }

        /// <summary>
        /// Delete both crls from the X509 store
        /// </summary>
        /// <returns></returns>
        [Theory, Order(80)]
        public async Task DeleteCrlsFromX509StoreOnWindows(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);

                //Delete first crl with revoked certificates
                X509CRL crl = (await x509Store.EnumerateCRLs().ConfigureAwait(false)).Single(c => c.Issuer == X509StoreSubject);
                await x509Store.DeleteCRL(crl).ConfigureAwait(false);

                X509CRLCollection crlsAfterFirstDelete = await x509Store.EnumerateCRLs().ConfigureAwait(false);

                //check the right crl was deleted
                Assert.AreEqual(1, crlsAfterFirstDelete.Count);
                Assert.Null(crlsAfterFirstDelete.FirstOrDefault(c => c == crl));

                //make shure IsRevoked can't find crl anymore
                var statusCode = await x509Store.IsRevoked(GetTestCert(), GetTestCert()).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevocationUnknown, statusCode);

                //Delete second (empty) crl from store
                await x509Store.DeleteCRL(crlsAfterFirstDelete.First()).ConfigureAwait(false);

                X509CRLCollection crlsAfterSecondDelete = await x509Store.EnumerateCRLs().ConfigureAwait(false);

                //make shure no crls remain in store
                Assert.AreEqual(0, crlsAfterSecondDelete.Count);
            }
        }


        /// <summary>
        /// Verify X509 Store Extension methods throw on Linux or MacOs
        /// </summary>
        /// <param name="storePath"></param>
        [Theory, Order(90)]
        public void X509StoreExtensionsThrowException(string storePath)
        {
            using (var x509Store = new X509Store(storePath))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.Throws<PlatformNotSupportedException>(() => x509Store.AddCrl(new byte[0]));
                    Assert.Throws<PlatformNotSupportedException>(() => x509Store.EnumerateCrls());
                    Assert.Throws<PlatformNotSupportedException>(() => x509Store.DeleteCrl(new byte[0]));
                }
                else
                {
                    Assert.Ignore("Test only relevant on MacOS/Linux");
                }
            }
            using (var x509Store = new X509CertificateStore())
            {
                x509Store.Open(storePath);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.ThrowsAsync<ServiceResultException>(() => x509Store.AddCRL(new X509CRL()));
                    Assert.ThrowsAsync<ServiceResultException>(() => x509Store.EnumerateCRLs());
                    Assert.ThrowsAsync<ServiceResultException>(() => x509Store.DeleteCRL(new X509CRL()));
                }
                else
                {
                    Assert.Ignore("Test only relevant on MacOS/Linux");
                }
            }
        }

        #endregion

        #region Private Methods
        private X509Certificate2 GetTestCert()
        {
            return m_testCertificate ??
                (m_testCertificate = CertificateFactory.CreateCertificate(X509StoreSubject).CreateForRSA());
        }

        private X509Certificate2 GetTestCert2()
        {
            return m_testCertificate2 ??
                (m_testCertificate2 = CertificateFactory.CreateCertificate(X509StoreSubject2).CreateForRSA());
        }

        private static string[] GetCertStores()
        {
            var result = new List<string> {
                "CurrentUser\\My"
            };
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                result.Add("CurrentUser\\UA_MachineDefault");
            }
            return result.ToArray();
        }
        #endregion

        #region Private Fields
        private X509Certificate2 m_testCertificate;
        private X509Certificate2 m_testCertificate2;
        private const string s_keyPairPemBase64Encrypted = "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";
        private static readonly byte[] s_aesKey = new byte[] { 0x13, 0x5e, 0xcf, 0xdd, 0x96, 0xf2, 0x99, 0x63, 0x9e, 0x2d, 0x50, 0x1c, 0x3a, 0xbb, 0xde, 0x02 }; // 16 bytes for AES-128
        private static readonly byte[] s_aesIV = new byte[] { 0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01 }; // 16 bytes

        private static byte[] DecryptKeyPairPemBase64()
        {
            var encryptedBytes = Convert.FromBase64String(s_keyPairPemBase64Encrypted);
            using var aes = Aes.Create();
            aes.Key = s_aesKey;
            aes.IV = s_aesIV;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(encryptedBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            return output.ToArray();
        }
        #endregion
    }
}
