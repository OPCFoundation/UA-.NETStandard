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
            using (var publicKey = new X509Certificate2(appCertificate.RawData))
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
            const string storeType = CertificateStoreType.Directory;
            appCertificate.AddToStore(
                storeType, storePath, password
                );

            using (var publicKey = new X509Certificate2(appCertificate.RawData))
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
                    store.Open(storePath);
                    await store.Delete(publicKey.Thumbprint).ConfigureAwait(false);
                }
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

                //make shure IsRevoked cant find crl anymore
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
        #endregion
    }
}
