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
using Opc.Ua.Tests;
using Opc.Ua.X509StoreExtensions;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateFactory class.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    [SetCulture("en-us")]
    public class CertificateStoreTest
    {
        public const string X509StoreSubject
            = "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=X509Store, C=US";

        public const string X509StoreSubject2
            = "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=X509Store2, C=US";

        [DatapointSource]
        public string[] CertStores = GetCertStores();

        /// <summary>
        /// Clean up the test cert store folder.
        /// </summary>
        [OneTimeTearDown]
        protected async Task OneTimeTearDownAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            foreach (string certStore in CertStores)
            {
                using var x509Store = new X509CertificateStore(telemetry);
                x509Store.Open(certStore);
                X509Certificate2Collection collection = await x509Store.EnumerateAsync()
                    .ConfigureAwait(false);
                foreach (X509Certificate2 cert in collection)
                {
                    if (X509Utils.CompareDistinguishedName(X509StoreSubject, cert.Subject))
                    {
                        await x509Store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
                    }
                    if (X509Utils.CompareDistinguishedName(X509StoreSubject2, cert.Issuer))
                    {
                        await x509Store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
                    }
                }
                if (x509Store.SupportsCRLs)
                {
                    foreach (X509CRL crl in x509Store.EnumerateCRLsAsync().Result)
                    {
                        if (X509Utils.CompareDistinguishedName(X509StoreSubject, crl.Issuer))
                        {
                            await x509Store.DeleteCRLAsync(crl).ConfigureAwait(false);
                        }
                        if (X509Utils.CompareDistinguishedName(X509StoreSubject2, crl.Issuer))
                        {
                            await x509Store.DeleteCRLAsync(crl).ConfigureAwait(false);
                        }
                    }
                }
            }
            Utils.SilentDispose(m_testCertificate);
        }

        /// <summary>
        /// Verify new app certificate is stored in X509Store with private key.
        /// </summary>
        [Theory]
        [Order(10)]
        public async Task VerifyAppCertX509StoreAsync(string storePath)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            X509Certificate2 appCertificate = GetTestCert();
            Assert.NotNull(appCertificate);
            Assert.True(appCertificate.HasPrivateKey);
            await appCertificate.AddToStoreAsync(
                CertificateStoreType.X509Store,
                storePath,
                telemetry: telemetry)
                .ConfigureAwait(false);
            using X509Certificate2 publicKey = CertificateFactory.Create(
                appCertificate.RawData);
            Assert.NotNull(publicKey);
            Assert.False(publicKey.HasPrivateKey);

            var id = new CertificateIdentifier
            {
                Thumbprint = publicKey.Thumbprint,
                StorePath = storePath,
                StoreType = CertificateStoreType.X509Store
            };
            X509Certificate2 privateKey = await id.LoadPrivateKeyAsync(
                password: null,
                telemetry: telemetry).ConfigureAwait(false);
            Assert.NotNull(privateKey);
            Assert.True(privateKey.HasPrivateKey);

            X509Utils.VerifyRSAKeyPair(publicKey, privateKey, true);

            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);
            await x509Store.DeleteAsync(publicKey.Thumbprint).ConfigureAwait(false);
        }

        /// <summary>
        /// Verify new app certificate is stored in Directory Store
        /// with password for private key (PFX).
        /// </summary>
        [Test]
        [Order(20)]
        public async Task VerifyAppCertDirectoryStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            X509Certificate2 appCertificate = GetTestCert();
            Assert.NotNull(appCertificate);
            Assert.True(appCertificate.HasPrivateKey);

            char[] password = Guid.NewGuid().ToString().ToCharArray();

            // pki directory root for app cert
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;
            string storePath = pkiRoot + "own";
            var certificateStoreIdentifier = new CertificateStoreIdentifier(storePath, false);
            const string storeType = CertificateStoreType.Directory;
            await appCertificate.AddToStoreAsync(certificateStoreIdentifier, password, telemetry: telemetry)
                .ConfigureAwait(false);

            using X509Certificate2 publicKey = CertificateFactory.Create(
                appCertificate.RawData);
            Assert.NotNull(publicKey);
            Assert.False(publicKey.HasPrivateKey);

            var id = new CertificateIdentifier
            {
                Thumbprint = publicKey.Thumbprint,
                StorePath = storePath,
                StoreType = storeType
            };

            {
                // check no password fails to load
                X509Certificate2 nullKey = await id.LoadPrivateKeyAsync(
                    password: null,
                    telemetry: telemetry).ConfigureAwait(false);
                Assert.IsNull(nullKey);
            }

            {
                // check invalid password fails to load
                X509Certificate2 nullKey = await id.LoadPrivateKeyAsync(
                    "123".ToCharArray(),
                    telemetry: telemetry)
                    .ConfigureAwait(false);
                Assert.IsNull(nullKey);
            }

            {
                // check invalid password fails to load
                X509Certificate2 nullKey = await id.LoadPrivateKeyExAsync(
                    new CertificatePasswordProvider("123".ToCharArray()),
                    telemetry: telemetry)
                    .ConfigureAwait(false);
                Assert.IsNull(nullKey);
            }

            X509Certificate2 privateKey = await id.LoadPrivateKeyExAsync(
                new CertificatePasswordProvider(password),
                telemetry: telemetry)
                .ConfigureAwait(false);

            Assert.NotNull(privateKey);
            Assert.True(privateKey.HasPrivateKey);

            X509Utils.VerifyRSAKeyPair(publicKey, privateKey, true);

            using ICertificateStore store = CertificateStoreIdentifier.CreateStore(storeType, telemetry);
            store.Open(storePath, false);
            await store.DeleteAsync(publicKey.Thumbprint).ConfigureAwait(false);
        }

        /// <summary>
        /// Verify PEM Certs are stored in Directory Store
        /// </summary>
        [Test]
        [Order(25)]
        public async Task VerifyPEMSupportDirectoryStoreAsync()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NUnit.Framework.Assert
                    .Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for app cert
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;
            string storePath = pkiRoot + "trusted";
            string certPath = storePath + Path.DirectorySeparatorChar + "certs";
            string privatePath = storePath + Path.DirectorySeparatorChar + "private";

            Directory.CreateDirectory(certPath);
            Directory.CreateDirectory(privatePath);

            var store = new DirectoryCertificateStore(false, telemetry);

            try
            {
                store.Open(storePath, false);
                //Add Test PEM Chain
                File.Copy(
                    TestUtils.EnumerateTestAssets("Test_chain.pem").First(),
                    certPath + Path.DirectorySeparatorChar + "Test_chain.pem");

                X509Certificate2Collection certificates = await store.EnumerateAsync()
                    .ConfigureAwait(false);

                Assert.AreEqual(3, certificates.Count);

                //Add private key for leaf cert to private folder
                File.WriteAllBytes(
                    privatePath + Path.DirectorySeparatorChar + "Test_chain.pem",
                    DecryptKeyPairPemBase64());

                //refresh store to obtain private key
                await store.EnumerateAsync().ConfigureAwait(false);

                //Load private key
                X509Certificate2 cert = await store
                    .LoadPrivateKeyAsync(
                        "14A630438BF775E19169D3279069BBF20419EF84",
                        null,
                        null,
                        null,
                        null)
                    .ConfigureAwait(false);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.DeleteAsync("14A630438BF775E19169D3279069BBF20419EF84")
                    .ConfigureAwait(false);

                //remove private key
                File.Delete(privatePath + Path.DirectorySeparatorChar + "Test_chain.pem");

                certificates = await store.EnumerateAsync().ConfigureAwait(false);

                Assert.AreEqual(2, certificates.Count);
                Assert.IsEmpty(
                    certificates.Find(
                        X509FindType.FindByThumbprint,
                        "14A630438BF775E19169D3279069BBF20419EF84",
                        false));
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        /// <summary>
        /// Verify PEM Certs with private key in single file are stored in Directory Store
        /// </summary>
        [Test]
        [Order(25)]
        public async Task VerifyPEMSupportPrivateKeyPairDirectoryStoreAsync()
        {
#if !NET8_0_OR_GREATER
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NUnit.Framework.Assert
                    .Ignore("Skipped due to https://github.com/dotnet/runtime/issues/82682");
            }
#endif
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // pki directory root for app cert
            string pkiRoot = Path.GetTempPath() +
                Path.GetRandomFileName() +
                Path.DirectorySeparatorChar;
            string storePath = pkiRoot + "trusted";
            string certPath = storePath + Path.DirectorySeparatorChar + "certs";
            string privatePath = storePath + Path.DirectorySeparatorChar + "private";

            Directory.CreateDirectory(certPath);
            Directory.CreateDirectory(privatePath);

            var store = new DirectoryCertificateStore(false, telemetry);

            try
            {
                store.Open(storePath, false);
                // Add leaf cert with private key
                File.WriteAllBytes(
                    certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem",
                    DecryptKeyPairPemBase64());

                X509Certificate2Collection certificates = await store.EnumerateAsync()
                    .ConfigureAwait(false);

                Assert.AreEqual(1, certificates.Count);

                Assert.NotNull(
                    certificates.Find(
                        X509FindType.FindByThumbprint,
                        "14A630438BF775E19169D3279069BBF20419EF84",
                        false));
                //Load private key
                X509Certificate2 cert = await store
                    .LoadPrivateKeyAsync(
                        "14A630438BF775E19169D3279069BBF20419EF84",
                        null,
                        null,
                        null,
                        null)
                    .ConfigureAwait(false);

                Assert.NotNull(cert);
                Assert.True(cert.HasPrivateKey);

                // remove leaf cert
                await store.DeleteAsync("14A630438BF775E19169D3279069BBF20419EF84")
                    .ConfigureAwait(false);

                //ensure private key is removed
                Assert.False(
                    File.Exists(certPath + Path.DirectorySeparatorChar + "Test_keyPair.pem"));
            }
            finally
            {
                Directory.Delete(storePath, true);
            }
        }

        /// <summary>
        /// Verify that old invalid cert stores throw.
        /// </summary>
        [Test]
        [Order(30)]
        public void VerifyInvalidAppCertX509Store()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            X509Certificate2 appCertificate = GetTestCert();
            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                async () => await appCertificate.AddToStoreAsync(
                    CertificateStoreType.X509Store,
                    "User\\UA_MachineDefault",
                    telemetry: telemetry).ConfigureAwait(false));
            _ = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                async () => await appCertificate.AddToStoreAsync(
                    CertificateStoreType.X509Store,
                    "System\\UA_MachineDefault",
                    telemetry: telemetry).ConfigureAwait(false));
        }

        /// <summary>
        /// Verify X509 Store supports no Crls on Linux or MacOs
        /// </summary>
        [Theory]
        [Order(40)]
        public void VerifyNoCrlSupportOnLinuxOrMacOsX509Store(string storePath)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.False(x509Store.SupportsCRLs);
                NUnit.Framework.Assert
                    .Throws<ServiceResultException>(() => x509Store.EnumerateCRLsAsync());
            }
            else
            {
                Assert.True(x509Store.SupportsCRLs);
            }
        }

        /// <summary>
        /// Add an new crl to the X509 store
        /// </summary>
        [Theory]
        [Order(50)]
        public async Task AddAndEnumerateNewCrlInX509StoreOnWindowsAsync(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);

            Assert.True(x509Store.SupportsCRLs);

            //add issuer to store
            await x509Store.AddAsync(GetTestCert()).ConfigureAwait(false);
            //add Crl
            var crlBuilder = CrlBuilder.Create(GetTestCert().SubjectName);
            crlBuilder.AddRevokedCertificate(GetTestCert());
            var crl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert()));
            await x509Store.AddCRLAsync(crl).ConfigureAwait(false);

            //enumerate Crls
            X509CRLCollection crls = await x509Store.EnumerateCRLsAsync().ConfigureAwait(false);

            Assert.AreEqual(1, crls.Count);
            Assert.AreEqual(crl.RawData, crls[0].RawData);
            Assert.AreEqual(
                GetTestCert().SerialNumber,
                crls[0].RevokedCertificates[0].SerialNumber);

            //TestRevocation
            StatusCode statusCode = await x509Store.IsRevokedAsync(GetTestCert(), GetTestCert())
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevoked, statusCode);
        }

        /// <summary>
        /// Enumerate and update an existing crl to the X509 store
        /// </summary>
        [Theory]
        [Order(60)]
        public async Task UpdateExistingCrlInX509StoreOnWindowsAsync(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);
            //enumerate Crls
            X509CRL crl = (await x509Store.EnumerateCRLsAsync().ConfigureAwait(false))[0];

            //Test Revocation before adding cert
            StatusCode statusCode = await x509Store.IsRevokedAsync(GetTestCert(), GetTestCert2())
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

            var crlBuilder = CrlBuilder.Create(crl);

            crlBuilder.AddRevokedCertificate(GetTestCert2());
            var updatedCrl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert()));
            await x509Store.AddCRLAsync(updatedCrl).ConfigureAwait(false);

            X509CRLCollection crls = await x509Store.EnumerateCRLsAsync().ConfigureAwait(false);

            Assert.AreEqual(1, crls.Count);

            Assert.AreEqual(2, crls[0].RevokedCertificates.Count);
            //Test Revocation after adding cert
            StatusCode statusCode2 = await x509Store
                .IsRevokedAsync(GetTestCert(), GetTestCert2())
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevoked, statusCode2);
        }

        /// <summary>
        /// Add a second crl to the X509 store
        /// </summary>
        [Theory]
        [Order(70)]
        public async Task AddSecondCrlToX509StoreOnWindowsAsync(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);
            //add issuer to store
            await x509Store.AddAsync(GetTestCert2()).ConfigureAwait(false);
            //add Crl
            var crlBuilder = CrlBuilder.Create(GetTestCert2().SubjectName);
            crlBuilder.AddRevokedCertificate(GetTestCert2());
            var crl = new X509CRL(crlBuilder.CreateForRSA(GetTestCert2()));
            await x509Store.AddCRLAsync(crl).ConfigureAwait(false);

            //enumerate Crls
            X509CRLCollection crls = await x509Store.EnumerateCRLsAsync().ConfigureAwait(false);

            Assert.AreEqual(2, crls.Count);
            Assert.NotNull(crls.SingleOrDefault(c => c.Issuer == crl.Issuer));
        }

        /// <summary>
        /// Delete both crls from the X509 store
        /// </summary>
        [Theory]
        [Order(80)]
        public async Task DeleteCrlsFromX509StoreOnWindowsAsync(string storePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NUnit.Framework.Assert.Ignore("Crls in an X509Store are only supported on Windows");
            }
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var x509Store = new X509CertificateStore(telemetry);
            x509Store.Open(storePath);

            //Delete first crl with revoked certificates
            X509CRL crl = (await x509Store.EnumerateCRLsAsync().ConfigureAwait(false)).Single(c =>
                c.Issuer == X509StoreSubject);
            await x509Store.DeleteCRLAsync(crl).ConfigureAwait(false);

            X509CRLCollection crlsAfterFirstDelete = await x509Store.EnumerateCRLsAsync()
                .ConfigureAwait(false);

            //check the right crl was deleted
            Assert.AreEqual(1, crlsAfterFirstDelete.Count);
            Assert.Null(crlsAfterFirstDelete.FirstOrDefault(c => c == crl));

            //make shure IsRevoked can't find crl anymore
            StatusCode statusCode = await x509Store.IsRevokedAsync(GetTestCert(), GetTestCert())
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.BadCertificateRevocationUnknown, statusCode);

            //Delete second (empty) crl from store
            await x509Store.DeleteCRLAsync(crlsAfterFirstDelete[0]).ConfigureAwait(false);

            X509CRLCollection crlsAfterSecondDelete = await x509Store.EnumerateCRLsAsync()
                .ConfigureAwait(false);

            //make shure no crls remain in store
            Assert.AreEqual(0, crlsAfterSecondDelete.Count);
        }

        /// <summary>
        /// Verify X509 Store Extension methods throw on Linux or MacOs
        /// </summary>
        [Theory]
        [Order(90)]
        public void X509StoreExtensionsThrowException(string storePath)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Microsoft.Extensions.Logging.ILogger logger = telemetry.CreateLogger<CertificateStoreTest>();
            using (var x509Store = new X509Store(storePath))
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    NUnit.Framework.Assert
                        .Throws<PlatformNotSupportedException>(() => x509Store.AddCrl([], logger));
                    NUnit.Framework.Assert
                        .Throws<PlatformNotSupportedException>(() => x509Store.EnumerateCrls(logger));
                    NUnit.Framework.Assert
                        .Throws<PlatformNotSupportedException>(() => x509Store.DeleteCrl([], logger));
                }
                else
                {
                    NUnit.Framework.Assert.Ignore("Test only relevant on MacOS/Linux");
                }
            }
            using (var x509Store = new X509CertificateStore(telemetry))
            {
                x509Store.Open(storePath);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                        x509Store.AddCRLAsync(new X509CRL()));
                    NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(() => x509Store.EnumerateCRLsAsync());
                    NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                        x509Store.DeleteCRLAsync(new X509CRL()));
                }
                else
                {
                    NUnit.Framework.Assert.Ignore("Test only relevant on MacOS/Linux");
                }
            }
        }

        /// <summary>
        /// Add the test for public static X509Certificate2 Find(X509Certificate2Collection collection,
        ///     string thumbprint,
        ///     string subjectName,
        ///     string applicationUri,
        ///     NodeId certificateType,
        ///     bool needPrivateKey)
        /// </summary>
        /// <returns></returns>
        [Test]
        [Order(100)]
        public void FindInCollectionTest()
        {
            // DateTime.UtcNow is used in certificate creation, so ensure all certs
            // have different NotAfter values by creating them in sequence.
            DateTime startCreation = DateTime.UtcNow;

            X509Certificate2 certSubjectSubstring = CreateDuplicateCertificate(
                "CN=Ua.Core.Tests",
                "urn:localhost:UA:Ua.Core.Tests",
                validityMonths: 12);
            X509Certificate2 certSubjectWithCnDuplicate = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 12);
            X509Certificate2 certSubjectWithoutCnDuplicate = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 6);
            X509Certificate2 certApplicationUriDuplicate = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests Duplicate",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 24);
            X509Certificate2 certLongestDuration = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 36);
            X509Certificate2 certLongestDurationLatestNotAfterValid = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 36,
                startingFromDays: -1);
            X509Certificate2 certLongestDurationLatestNotAfterInValid = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                validityMonths: 42,
                startingFromDays: 1);

            X509Certificate2[] testCertificatesCollection =
            [
                certSubjectSubstring,
                certSubjectWithCnDuplicate,
                certSubjectWithoutCnDuplicate,
                certApplicationUriDuplicate,
                certLongestDuration,
                certLongestDurationLatestNotAfterValid,
                certLongestDurationLatestNotAfterInValid // Never to be picked, just poisoned value
            ];

            X509Certificate2 CreateDuplicateCertificate(string subjectName,
                string applicationUri,
                int validityMonths = 2,
                int startingFromDays = -2)
            {
                ICertificateBuilder certificateFactory = CertificateFactory.CreateCertificate(subjectName)
                    .SetNotBefore(startCreation.AddDays(startingFromDays))
                    .SetNotAfter(startCreation.AddDays(startingFromDays).AddMonths(validityMonths))
                    .SetHashAlgorithm(HashAlgorithmName.SHA256);

                if (!string.IsNullOrEmpty(applicationUri))
                {
                    certificateFactory.AddExtension(new X509SubjectAltNameExtension(applicationUri, [subjectName]));
                }

                return certificateFactory.CreateForRSA();
            }

            var collection = new X509Certificate2Collection();
            collection.AddRange(testCertificatesCollection);

            // Test that searching by thumbprint works
            X509Certificate2 resultThumbprint = CertificateIdentifier.Find(
                collection,
                certSubjectSubstring.Thumbprint,
                null,
                null,
                null,
                false);
            Assert.NotNull(resultThumbprint);
            Assert.AreEqual(certSubjectSubstring.Thumbprint, resultThumbprint.Thumbprint);

            // Test that searching by existing thumbprint and subject name works
            X509Certificate2 resultThumbprintAndSubject = CertificateIdentifier.Find(
                collection,
                certSubjectSubstring.Thumbprint,
                "CN=Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultThumbprintAndSubject);
            Assert.AreEqual(certSubjectSubstring.Thumbprint, resultThumbprintAndSubject.Thumbprint);

            // Test that searching by existing thumbprint and non-matching subject name fails
            X509Certificate2 resultThumbprintAndNonMatchingSubject = CertificateIdentifier.Find(
                collection,
                certSubjectSubstring.Thumbprint,
                "CN=NonMatching",
                null,
                null,
                false);
            Assert.Null(resultThumbprintAndNonMatchingSubject);

            // Test that exact match is done if CN is in subject name and
            // subject name is substring of other subject names
            X509Certificate2 resultSubjectSubstring = CertificateIdentifier.Find(
                collection,
                null,
                "CN=Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultSubjectSubstring);
            Assert.AreEqual(certSubjectSubstring.Thumbprint, resultSubjectSubstring.Thumbprint);

            // Test that exact match is done if CN is in subject name and multiple matches exist
            // and the longest remaining validity certificate is selected in that case
            X509Certificate2 resultSubjectWithCnDuplicate = CertificateIdentifier.Find(
                collection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultSubjectWithCnDuplicate);
            Assert.AreEqual(certLongestDurationLatestNotAfterValid.Thumbprint,
             resultSubjectWithCnDuplicate.Thumbprint);

            // Test that longest remaining validity certificate is selected when multiple matches exist
            // and CN is not in subject name
            X509Certificate2 resultLongestDuration = CertificateIdentifier.Find(
                collection,
                null,
                "Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultLongestDuration);
            Assert.AreEqual(certLongestDurationLatestNotAfterValid.Thumbprint,
             resultLongestDuration.Thumbprint);

            // Test search by applicationUri works for single match
            X509Certificate2 resultApplicationUri = CertificateIdentifier.Find(
                collection,
                null,
                null,
                "urn:localhost:UA:Ua.Core.Tests",
                null,
                false);
            Assert.NotNull(resultApplicationUri);
            Assert.AreEqual(certSubjectSubstring.Thumbprint, resultApplicationUri.Thumbprint);

            // Test search by applicationUri works for multiple matches and longest remaining validity is selected
            X509Certificate2 resultApplicationUriDuplicate = CertificateIdentifier.Find(
                collection,
                null,
                null,
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                null,
                false);
            Assert.NotNull(resultApplicationUriDuplicate);
            Assert.AreEqual(certLongestDurationLatestNotAfterValid.Thumbprint,
             resultApplicationUriDuplicate.Thumbprint);

            // Test that CA-signed certificate is prioritized over self-signed certificate
            // --------------------------------------------------------------------------
            // Create a CA certificate (start earlier to allow signing expired certs in tests)
            X509Certificate2 caCertificate = CertificateFactory.CreateCertificate("CN=Test CA")
                .SetNotBefore(startCreation.AddDays(-1000))
                .SetNotAfter(startCreation.AddDays(-1000).AddYears(10))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetCAConstraint()
                .CreateForRSA();

            // Create a CA-signed certificate with shorter remaining validity than the self-signed ones
            X509Certificate2 caSignedCert = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(startCreation.AddDays(-2))
                .SetNotAfter(startCreation.AddDays(540)) // Valid for ~18 months
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests", ["CN=Opc.Ua.Core.Tests"]))
                .SetIssuer(caCertificate)
                .CreateForRSA();

            var collectionWithCASigned = new X509Certificate2Collection();
            collectionWithCASigned.AddRange(testCertificatesCollection);
            collectionWithCASigned.Add(caSignedCert);

            // Test that CA-signed certificate is picked over self-signed even with shorter remaining validity
            X509Certificate2 resultCASigned = CertificateIdentifier.Find(
                collectionWithCASigned,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultCASigned);
            Assert.AreEqual(caSignedCert.Thumbprint, resultCASigned.Thumbprint,
                "Should pick CA-signed certificate over self-signed even with shorter remaining validity");

            // Test that CA-signed certificate is picked by applicationUri over self-signed
            X509Certificate2 resultCASignedByUri = CertificateIdentifier.Find(
                collectionWithCASigned,
                null,
                null,
                "urn:localhost:UA:Opc.Ua.Core.Tests",
                null,
                false);
            Assert.NotNull(resultCASignedByUri);
            Assert.AreEqual(caSignedCert.Thumbprint, resultCASignedByUri.Thumbprint);

            // Test multiple valid certificates - should pick CA-signed first, then longest remaining validity
            X509Certificate2 validShortRemaining = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ValidShortRemaining",
                validityMonths: 3,
                startingFromDays: -2); // Valid for ~3 months
            X509Certificate2 validLongRemaining = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ValidLongRemaining",
                validityMonths: 24,
                startingFromDays: -2); // Valid for ~24 months
            X509Certificate2 validEqualDurationLessRemaining = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ValidEqualDurationLessRemaining",
                validityMonths: 24,
                startingFromDays: -365); // Same 24 month validity but started 1 year ago, ~12 months remaining

            var validMultipleCollection = new X509Certificate2Collection
            {
                validShortRemaining,
                validLongRemaining,
                validEqualDurationLessRemaining
            };

            X509Certificate2 resultValidMultiple = CertificateIdentifier.Find(
                validMultipleCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultValidMultiple);
            Assert.AreEqual(validLongRemaining.Thumbprint, resultValidMultiple.Thumbprint,
                "Should pick certificate with longest remaining validity (validLongRemaining has ~24 months, validEqualDurationLessRemaining has ~12 months)");

            // Test expired certificate handling
            // --------------------------------------------------------------------------

            // Test 1: All certificates expired - should pick least expired (most recent NotAfter)

            X509Certificate2 expiredCert1 = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.Expired1",
                validityMonths: 12,
                startingFromDays: -400); // Expired ~35 days ago (-400 + 365)
            X509Certificate2 expiredCert2 = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.Expired2",
                validityMonths: 6,
                startingFromDays: -200); // Expired ~20 days ago (-200 + 180) - least expired
            X509Certificate2 expiredCert3 = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.Expired3",
                validityMonths: 24,
                startingFromDays: -800); // Expired ~70 days ago (-800 + 730)

            var expiredCollection = new X509Certificate2Collection
            {
                expiredCert1,
                expiredCert2,
                expiredCert3
            };

            X509Certificate2 resultExpired = CertificateIdentifier.Find(
                expiredCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultExpired);
            Assert.AreEqual(expiredCert2.Thumbprint, resultExpired.Thumbprint,
                "Should pick the least expired certificate (most recent NotAfter)");

            // Test 2: Mix of valid and expired - should always pick valid certificate
            X509Certificate2 validCertShort = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ValidShort",
                validityMonths: 1,
                startingFromDays: -2); // Valid for ~30 more days

            // Using explicit dates due to large time span (1800 days validity starting 1900 days ago)
            X509Certificate2 expiredCertLong = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(startCreation.AddDays(-1900))
                .SetNotAfter(startCreation.AddDays(-100)) // Expired 100 days ago
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.ExpiredLong",
                    ["CN=Opc.Ua.Core.Tests"]))
                .CreateForRSA();

            var mixedCollection = new X509Certificate2Collection
            {
                expiredCertLong,
                validCertShort
            };

            X509Certificate2 resultMixed = CertificateIdentifier.Find(
                mixedCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultMixed);
            Assert.AreEqual(validCertShort.Thumbprint, resultMixed.Thumbprint,
                "Should pick valid certificate over expired, regardless of total validity period");

            // Test 3: All expired, CA-signed vs self-signed - should prioritize CA-signed
            X509Certificate2 expiredSelfSigned = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ExpiredSelfSigned",
                validityMonths: 6,
                startingFromDays: -200); // Expired ~20 days ago - least expired self-signed

            // CA-signed cert must have dates within CA's validity period

            X509Certificate2 expiredCASigned = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(startCreation.AddDays(-500))
                .SetNotAfter(startCreation.AddDays(-320)) // Expired ~320 days ago (more expired than self-signed)
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.ExpiredCA",
                    ["CN=Opc.Ua.Core.Tests"]))
                .SetIssuer(caCertificate)
                .CreateForRSA(); // More expired but CA-signed

            var expiredCACollection = new X509Certificate2Collection
            {
                expiredSelfSigned,
                expiredCASigned
            };

            X509Certificate2 resultExpiredCA = CertificateIdentifier.Find(
                expiredCACollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultExpiredCA);
            Assert.AreEqual(expiredCASigned.Thumbprint, resultExpiredCA.Thumbprint,
                "Should prioritize CA-signed over self-signed even when CA-signed is more expired");

            // Test 4: Certificate not yet valid (NotBefore in future) - should be treated as invalid
            X509Certificate2 notYetValid = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.Future",
                validityMonths: 12,
                startingFromDays: 10); // NotBefore is 10 days in future
            X509Certificate2 currentlyValid = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.Current",
                validityMonths: 6,
                startingFromDays: -2); // Currently valid

            var futureCollection = new X509Certificate2Collection
            {
                notYetValid,
                currentlyValid
            };

            X509Certificate2 resultFuture = CertificateIdentifier.Find(
                futureCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultFuture);
            Assert.AreEqual(currentlyValid.Thumbprint, resultFuture.Thumbprint,
                "Should pick currently valid certificate over not-yet-valid certificate");

            // Test 5: All expired with same NotAfter, CA-signed should win
            DateTime sameExpiry = startCreation.AddDays(-50); // Expired 50 days ago
            DateTime sameExpiryStart = sameExpiry.AddDays(-365); // Started 365 days before expiry
            X509Certificate2 expiredSelfSigned1 = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(sameExpiryStart)
                .SetNotAfter(sameExpiry)
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.SameExpirySelf",
                    ["CN=Opc.Ua.Core.Tests"]))
                .CreateForRSA();

            X509Certificate2 expiredCASigned1 = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(sameExpiryStart)
                .SetNotAfter(sameExpiry)
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.SameExpiryCA",
                    ["CN=Opc.Ua.Core.Tests"]))
                .SetIssuer(caCertificate)
                .CreateForRSA();

            var sameExpiryCollection = new X509Certificate2Collection
            {
                expiredSelfSigned1,
                expiredCASigned1
            };

            X509Certificate2 resultSameExpiry = CertificateIdentifier.Find(
                sameExpiryCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultSameExpiry);
            Assert.AreEqual(expiredCASigned1.Thumbprint, resultSameExpiry.Thumbprint,
                "Should prioritize CA-signed over self-signed when both have same NotAfter");

            // Test 6: Mix of expired and not-yet-valid - should pick soonest to become valid
            X509Certificate2 notYetValidSoon = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.FutureSoon",
                validityMonths: 12,
                startingFromDays: 5); // Becomes valid in 5 days
            X509Certificate2 notYetValidLater = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.FutureLater",
                validityMonths: 12,
                startingFromDays: 30); // Becomes valid in 30 days
            X509Certificate2 expiredRecent = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ExpiredRecent",
                validityMonths: 6,
                startingFromDays: -200); // Expired ~20 days ago

            var mixedExpiredFutureCollection = new X509Certificate2Collection
            {
                notYetValidSoon,
                notYetValidLater,
                expiredRecent
            };

            X509Certificate2 resultMixedExpiredFuture = CertificateIdentifier.Find(
                mixedExpiredFutureCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultMixedExpiredFuture);
            Assert.AreEqual(notYetValidSoon.Thumbprint, resultMixedExpiredFuture.Thumbprint,
                "Should pick soonest to become valid when both expired and not-yet-valid exist (5 days < 20 days)");

            // Test 7: All not-yet-valid - should pick soonest to become valid
            var allNotYetValidCollection = new X509Certificate2Collection
            {
                notYetValidSoon,
                notYetValidLater
            };

            X509Certificate2 resultAllNotYetValid = CertificateIdentifier.Find(
                allNotYetValidCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultAllNotYetValid);
            Assert.AreEqual(notYetValidSoon.Thumbprint, resultAllNotYetValid.Thumbprint,
                "Should pick soonest to become valid when all are not-yet-valid");

            // Test 8: Not-yet-valid CA-signed vs self-signed - should prioritize CA-signed
            X509Certificate2 notYetValidCASigned = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(startCreation.AddDays(20))
                .SetNotAfter(startCreation.AddDays(20).AddMonths(12))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.FutureCA",
                    ["CN=Opc.Ua.Core.Tests"]))
                .SetIssuer(caCertificate)
                .CreateForRSA(); // Becomes valid in 20 days, but CA-signed

            var notYetValidCACollection = new X509Certificate2Collection
            {
                notYetValidSoon, // Self-signed, becomes valid in 5 days
                notYetValidCASigned // CA-signed, becomes valid in 20 days
            };

            X509Certificate2 resultNotYetValidCA = CertificateIdentifier.Find(
                notYetValidCACollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultNotYetValidCA);
            Assert.AreEqual(notYetValidCASigned.Thumbprint, resultNotYetValidCA.Thumbprint,
                "Should prioritize CA-signed over self-signed even when CA-signed becomes valid later");

            // Test 9: Mix of expired and not-yet-valid with CA-signed - should pick CA-signed not-yet-valid
            X509Certificate2 expiredSelfSignedRecent = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ExpiredSelfRecent",
                validityMonths: 6,
                startingFromDays: -190); // Expired ~10 days ago - least expired self-signed

            var mixedCACollection = new X509Certificate2Collection
            {
                expiredSelfSignedRecent,
                notYetValidCASigned
            };

            X509Certificate2 resultMixedCA = CertificateIdentifier.Find(
                mixedCACollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultMixedCA);
            Assert.AreEqual(notYetValidCASigned.Thumbprint, resultMixedCA.Thumbprint,
                "Should pick CA-signed not-yet-valid over self-signed expired when comparing soonest to become valid");

            // Test 10: Search by applicationUri with expired certificates
            X509Certificate2 resultExpiredByUri = CertificateIdentifier.Find(
                expiredCollection,
                null,
                null,
                "urn:localhost:UA:Opc.Ua.Core.Tests.Expired2",
                null,
                false);
            Assert.NotNull(resultExpiredByUri);
            Assert.AreEqual(expiredCert2.Thumbprint, resultExpiredByUri.Thumbprint,
                "Should find least expired certificate when searching by applicationUri");

            // Test 11: Valid CA-signed with shorter remaining validity beats self-signed with longer remaining validity
            X509Certificate2 validSelfSignedLonger = CreateDuplicateCertificate(
                "CN=Opc.Ua.Core.Tests",
                "urn:localhost:UA:Opc.Ua.Core.Tests.ValidSelfLonger",
                validityMonths: 48,
                startingFromDays: -2); // Valid for ~48 months

            X509Certificate2 validCASignedShorter = CertificateFactory.CreateCertificate("CN=Opc.Ua.Core.Tests")
                .SetNotBefore(startCreation.AddDays(-2))
                .SetNotAfter(startCreation.AddDays(180)) // Valid for ~6 months
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .AddExtension(new X509SubjectAltNameExtension("urn:localhost:UA:Opc.Ua.Core.Tests.ValidCAShorter",
                    ["CN=Opc.Ua.Core.Tests"]))
                .SetIssuer(caCertificate)
                .CreateForRSA();

            var validCAvsSelfCollection = new X509Certificate2Collection
            {
                validSelfSignedLonger,
                validCASignedShorter
            };

            X509Certificate2 resultValidCAvsSeIf = CertificateIdentifier.Find(
                validCAvsSelfCollection,
                null,
                "CN=Opc.Ua.Core.Tests",
                null,
                null,
                false);
            Assert.NotNull(resultValidCAvsSeIf);
            Assert.AreEqual(validCASignedShorter.Thumbprint, resultValidCAvsSeIf.Thumbprint,
                "Should pick CA-signed valid certificate over self-signed valid even with shorter remaining validity");
        }

        private X509Certificate2 GetTestCert()
        {
            return m_testCertificate ??= CertificateFactory.CreateCertificate(X509StoreSubject)
                .CreateForRSA();
        }

        private X509Certificate2 GetTestCert2()
        {
            return m_testCertificate2 ??= CertificateFactory.CreateCertificate(X509StoreSubject2)
                .CreateForRSA();
        }

        private static string[] GetCertStores()
        {
            var result = new List<string> { "CurrentUser\\My" };
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                result.Add("CurrentUser\\UA_MachineDefault");
            }
            return [.. result];
        }

        private X509Certificate2 m_testCertificate;
        private X509Certificate2 m_testCertificate2;

        private const string kEyPairPemBase64Encrypted =
            "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";

        /// <summary>
        /// 16 bytes for AES-128
        /// </summary>
        private static readonly byte[] s_aesKey =
        [
            0x13,
            0x5e,
            0xcf,
            0xdd,
            0x96,
            0xf2,
            0x99,
            0x63,
            0x9e,
            0x2d,
            0x50,
            0x1c,
            0x3a,
            0xbb,
            0xde,
            0x02
        ];

        /// <summary>
        /// 16 bytes
        /// </summary>
        private static readonly byte[] s_aesIV =
        [
            0xFE,
            0xDC,
            0xBA,
            0x98,
            0x76,
            0x54,
            0x32,
            0x10,
            0xEF,
            0xCD,
            0xAB,
            0x89,
            0x67,
            0x45,
            0x23,
            0x01
        ];

        private static byte[] DecryptKeyPairPemBase64()
        {
            byte[] encryptedBytes = Convert.FromBase64String(kEyPairPemBase64Encrypted);
            using var aes = Aes.Create();
            aes.Key = s_aesKey;
            aes.IV = s_aesIV;
            using ICryptoTransform decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(encryptedBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var output = new MemoryStream();
            cs.CopyTo(output);
            return output.ToArray();
        }
    }
}
