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

                var certificates = await store.Enumerate();

                Assert.AreEqual(3, certificates.Count);

                //Add private key for leaf cert to private folder
                File.WriteAllBytes(privatePath + Path.DirectorySeparatorChar + "Test_chain.pem", Convert.FromBase64String(s_keyPairPemBase64));

                //refresh store to obtain private key
                await store.Enumerate();

                //Load private key
                var cert = await store.LoadPrivateKey("14A630438BF775E19169D3279069BBF20419EF84", null, null, null, null);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.Delete("14A630438BF775E19169D3279069BBF20419EF84");

                //remove private key
                File.Delete(privatePath + Path.DirectorySeparatorChar + "Test_chain.pem");

                certificates = await store.Enumerate();

                Assert.AreEqual(2, certificates.Count);
                Assert.IsEmpty(certificates.Find(X509FindType.FindByThumbprint, "14A630438BF775E19169D3279069BBF20419EF84", false));

                // Add leaf cert with private key
                File.WriteAllBytes(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem", Convert.FromBase64String(s_keyPairPemBase64));
                var iterations = 0;
                do
                {
                    await Task.Delay(1000); // wait for file system to catch up
                    iterations++;
                }
                while (!File.Exists(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem") && iterations <= 5);

                if(!File.Exists(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem"))
                {
                    Assert.Fail("Test_keyPair.pem not found in certs folder after 5 seconds.");
                }


                certificates = await store.Enumerate();

                Assert.AreEqual(3, certificates.Count);

                Assert.NotNull(certificates.Find(X509FindType.FindByThumbprint, "14A630438BF775E19169D3279069BBF20419EF84", false));
                //Load private key
                cert = await store.LoadPrivateKey("14A630438BF775E19169D3279069BBF20419EF84", null, null, null, null);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.Delete("14A630438BF775E19169D3279069BBF20419EF84");

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
        private const string s_keyPairPemBase64 = "LS0tLS1CRUdJTiBSU0EgUFJJVkFURSBLRVktLS0tLQ0KTUlJRXBRSUJBQUtDQVFFQXEvcG45L21EQ0x1Y2pxazF4QWxIWGdyclE3UDhmUzE3RGw5ckNjVlg4U2ZORktrTA0KL0lPTVV3aVJHc2dKOUVLM2tkVVVJNkJnYVQxWWtVZ1BlazhyVzd0R3A0a2k1ckVCQmNnZ3IrbmRuL1haaHZyTA0KREhWRnk2YzhkNStrNEgxYVRMa1NzODF3T2tNUSttbXZNV2NVNWZuSGsrdkxyc2prcEdkYlYzbDlNaUU1Y0hncg0Kb25CMllvTFNSWUd6SEtERFFLVVpjL3FpWUtmU2lhY3BPZTI5UUxRdStHd29FNWRXM09EcmlXeUlnMHo5MjllWA0KaHlRUG52N2lZL3gwQk5wZmJGRXJHY2N3YWpkZi96WFNaV05tcUR2eGNJMEFhTE5GY2JhY3kyeEFjbUM3MUd4Yw0KNTZXYlNDc08yTi9RQm8yMW15SXhVeHpLb2o1R25CSXV4d3hycXdJREFRQUJBb0lCQUZCS3hSNjNyYzgyZEdZQQ0KcHpzQzBBQ2VuVytMQ1M5U1BCa2VRR1diN3E2SEZBNzR5OEZWazM2eXliaFV1NTBBUng4OWViMFdZOXpPaU5xdg0KWjVhRmZ1dEJlTC9BWFh5U0dEV2dWVzU4QS81cU5HaDZEN0dkMFB1L2RSSXVoVFpRSUFnaEFoUFRMdm5zbFBFcw0KeFdBTnplcC9Id2czTnUzQUdrdU5lODVQeUwxTmxKaldCcTl2c2ZWdG1ZREtWY1pWZDQ5N3R4blJoYm9LQjY2Lw0KZGx5bkVzSER6eE5XQTZjYTRUay85NzVGcnRhT1QxeDkzbXdzRUZkYVF1WHMrekRUd2RwczFmcVdMSjNIc2JUdw0Ka1U0ZjB1RmhHaFQzd1Z6SU9HSmZiVmVha2lTRjhIYjFjS2NGVk5LRGJtbFFvVVhqQ3I2TzhzeFA5Vk1DL0ZNbg0KTXRqYkVmMENnWUVBMjRXVkcwck1Cd3NSNXRkRGtPOTlaajBoTm56a0dYbGs5MEhUMHhVRW80Q2dVLzlOMXJEcw0KSTQxTThKU2NMUzNpZVNJdEtSZUcxUmRFZ0psWDEwOVZXMjF2NnJqMGZUb1d3SGkzUUQ1TCtIeCtxMVF1SStmNg0KTUx0S0s1M3hYK1JxUDI0TUhra241WTdTSkFSbjJCT1FjVDhCOFNqcEZVNGdPNTd6TkphNXFQMENnWUVBeUk1VA0KUG93NVROMlZkMGo2a21iT3lRaEtUbWZsS1pzdVBreFFKRCtZdjJJS0hpeFh3RTVQODFOUS9QdTBXU3hOQjkzVQ0KTUxiclliL3hhb0VCS2QraVFHT2I4VXYrTFFEWU9lbzIrOEJZNy9kRm5pMlhML2JwQlV2U0ozdkkzWUllZkxvZw0KbkZzUVlSMzV4SVp3d29rNk90aVprcmlXcGNWZHlrZVJVeTlTKzhjQ2dZRUFrajhMV0RSYmVySEFTbWJOQjZqVg0KaFNCaW1SZFpLek41dFZRd2w2YWdBWUYyenA3K3IzSU16NTZhVElqbEJ5QlRpZG5mOWtsTE5YbWIxSVRVUllmLw0KMkxvdTNsUTc1WldtaExHbmUvQkUwcFcyR2RRcUxSZWwwWU5rNVd3QzI1eWp3QUJEcUlXYVE5QURaYVZkdlZGRA0KWUg3V2YyQis4QWV0WjZyOFllT3NhczBDZ1lFQXE2K2pOWTFHMURWd2FXQXhHVGtuVmxOaGdRTlIyeTg4QkJyQw0KRkhYWTVpVWdjam9WbU11eGg2VFFWUEdJcnpuTWE2cUxwblJBeGpwUmlaSU1FL09jNnpBYVpCTmc4TmVqUXRqcw0KM3RFSGtjMkZiR2FzNFdPbWtXRVo4N0QxQUNNT3hFbDE5MFBCbnRIUmFscUlseEJ3cDhXYW1rNm9zQnBvTXV6WA0KVEhYYnZTc0NnWUVBZ1lvR3I3MmVBT3pWOGkyWGtlaG1CQyt0eG1WSXpoWk82K2lzWHdwKytGT2owU0JEY1E5eg0KNUQrVUp1L1BVTlNkSUVORkpWNVdIQkQxOVJ6dlNpRWJndnE1eWVCYnoyYnRINWRjQXhUVjEzZ3JJUVhraWlsdA0Kb1NzMFBjTG02NUdoTmR2MktXQnVlVjI0OGFhR0ZUOVoyR3hIYzNvOUF6Yis1b3VUOXdGYTlYUT0NCi0tLS0tRU5EIFJTQSBQUklWQVRFIEtFWS0tLS0tDQotLS0tLUJFR0lOIENFUlRJRklDQVRFLS0tLS0NCk1JSURWakNDQWo2Z0F3SUJBZ0lJVWJ0UGRGQUJKYTB3RFFZSktvWklodmNOQVFFTEJRQXdBREFlRncweU5UQTINCk1Ea3hNakV4TURCYUZ3MHlOakEyTURreE1qRXhNREJhTUJveEN6QUpCZ05WQkFZVEFrUkZNUXN3Q1FZRFZRUUcNCkV3SkVSVENDQVNJd0RRWUpLb1pJaHZjTkFRRUJCUUFEZ2dFUEFEQ0NBUW9DZ2dFQkFLdjZaL2Y1Z3dpN25JNnANCk5jUUpSMTRLNjBPei9IMHRldzVmYXduRlYvRW56UlNwQy95RGpGTUlrUnJJQ2ZSQ3Q1SFZGQ09nWUdrOVdKRkkNCkQzcFBLMXU3UnFlSkl1YXhBUVhJSUsvcDNaLzEyWWI2eXd4MVJjdW5QSGVmcE9COVdreTVFclBOY0RwREVQcHANCnJ6Rm5GT1g1eDVQcnk2N0k1S1JuVzFkNWZUSWhPWEI0SzZKd2RtS0Mwa1dCc3h5Z3cwQ2xHWFA2b21DbjBvbW4NCktUbnR2VUMwTHZoc0tCT1hWdHpnNjRsc2lJTk0vZHZYbDRja0Q1Nys0bVA4ZEFUYVgyeFJLeG5ITUdvM1gvODENCjBtVmpacWc3OFhDTkFHaXpSWEcybk10c1FISmd1OVJzWE9lbG0wZ3JEdGpmMEFhTnRac2lNVk1jeXFJK1Jwd1MNCkxzY01hNnNDQXdFQUFhT0J1VENCdGpBTUJnTlZIUk1CQWY4RUFqQUFNQjBHQTFVZERnUVdCQlNUeXM0ZW9lZXMNCmY1NUpJamNXTFREaWJ4QVpYVEFmQmdOVkhTTUVHREFXZ0JSYjF0bEc0alF6UVozaTg2aDdqVldFcFBVUFlEQUwNCkJnTlZIUThFQkFNQ0ErZ3dFd1lEVlIwbEJBd3dDZ1lJS3dZQkJRVUhBd0V3RVFZRFZSMFJCQW93Q0lJR1kyOXcNCmVXTnVNQkVHQ1dDR1NBR0crRUlCQVFRRUF3SUdRREFlQmdsZ2hrZ0JodmhDQVEwRUVSWVBlR05oSUdObGNuUnANClptbGpZWFJsTUEwR0NTcUdTSWIzRFFFQkN3VUFBNElCQVFCb05qUzQ2SGRBUE1wUVR5c2ZxNFlSVG9NZHRwK04NCnRzVElSYmNaSDBMZFV5S00zbVR2bUovVmZ3RTlpSGUrT2NPaXNha25HMG1RaG5uR2ptNGhVYnorZW1SWDlGRVMNClZkSVh4alhvMnNOTGpjcEhWL1UrRUtSdk9Tb3l2OTBhVnFiTERCOEVSYzQrZUxqcTZTeWZBS1ZnMUxnK3NGRTcNCkNUUUk0Qk5XUTJseG9FaGd5UEhmZnZ2Ris0NUl6M2dVcUUwd3poV0hYRVFiVi9BVHJBRjVoZVZiVWdacCtsbjMNCnAydG9HVnowYnRJRW1FTmVvcmJYVUJBdHZMMjFWaThqc08vcmp6TEVUUVZ6UGlZV3JzMEtiaVBYY3pHWFk5czgNCkdjVjZSbGxjNmdBckQ4aHk3SVlIVGpwaVcyN2VxVENCeWdhQ2VvY2xBTmdQUnFFeU5obUZ2OWl1DQotLS0tLUVORCBDRVJUSUZJQ0FURS0tLS0tDQo=";
        #endregion
    }
}
