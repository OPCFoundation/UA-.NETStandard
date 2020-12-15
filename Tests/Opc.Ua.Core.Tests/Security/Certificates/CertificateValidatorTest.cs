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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Org.BouncyCastle.X509;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateValidator class.
    /// </summary>
    [TestFixture, Category("CertificateValidator")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateValidatorTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            // set max RSA key size and max SHA-2 hash size
            ushort keySize = 4096;
            ushort hashSize = 512;

            // pki directory root for test runs. 
            m_pkiRoot = "%LocalApplicationData%/OPC/CertValidatorTest/" + ((DateTime.UtcNow.Ticks / 10000) % 3600000).ToString() + "/";
            m_issuerStore = new DirectoryCertificateStore();
            m_issuerStore.Open(m_pkiRoot + "issuer");
            m_trustedStore = new DirectoryCertificateStore();
            m_trustedStore.Open(m_pkiRoot + "trusted");

            // good applications test set
            var appTestDataGenerator = new ApplicationTestDataGenerator(1);
            m_goodApplicationTestSet = appTestDataGenerator.ApplicationTestSet(kGoodApplicationsTestCount);

            // create all certs and CRL
            m_caChain = new X509Certificate2[kCaChainCount];
            m_caDupeChain = new X509Certificate2[kCaChainCount];
            m_caAllSameIssuerChain = new X509Certificate2[kCaChainCount];
            m_crlChain = new X509CRL[kCaChainCount];
            m_crlDupeChain = new X509CRL[kCaChainCount];
            m_crlRevokedChain = new X509CRL[kCaChainCount];
            m_appCerts = new X509Certificate2Collection();
            m_appSelfSignedCerts = new X509Certificate2Collection();

            DateTime rootCABaseTime = DateTime.UtcNow;
            rootCABaseTime = new DateTime(rootCABaseTime.Year - 1, 1, 1);
            var rootCert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=Root CA Test Cert",
                null, keySize, rootCABaseTime, 25 * 12, hashSize, true,
                pathLengthConstraint: -1);

            m_caChain[0] = rootCert;
            m_crlChain[0] = CertificateFactory.RevokeCertificate(rootCert, null, null);
            m_caDupeChain[0] = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=Root CA Test Cert",
                null, keySize, rootCABaseTime, 25 * 12, hashSize, true,
                pathLengthConstraint: -1);
            m_crlDupeChain[0] = CertificateFactory.RevokeCertificate(m_caDupeChain[0], null, null);
            m_crlRevokedChain[0] = null;

            var signingCert = rootCert;
            DateTime subCABaseTime = DateTime.UtcNow;
            subCABaseTime = new DateTime(subCABaseTime.Year, subCABaseTime.Month, subCABaseTime.Day);
            for (int i = 1; i < kCaChainCount; i++)
            {
                if (keySize > 2048) { keySize -= 1024; }
                if (hashSize > 256) { hashSize -= 128; }
                var subject = $"CN=Sub CA {i} Test Cert";
                var subCACert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    null, null, subject,
                    null, keySize, subCABaseTime, 5 * 12, hashSize, true,
                    signingCert, pathLengthConstraint: kCaChainCount - 1 - i);
                m_caChain[i] = subCACert;

                m_crlChain[i] = CertificateFactory.RevokeCertificate(subCACert, null, null, subCABaseTime, subCABaseTime + TimeSpan.FromDays(10));
                var subCADupeCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    null, null, subject,
                    null, keySize, subCABaseTime, 5 * 12, hashSize, true,
                    signingCert, pathLengthConstraint: kCaChainCount - 1 - i);
                m_caDupeChain[i] = subCADupeCert;
                m_crlDupeChain[i] = CertificateFactory.RevokeCertificate(subCADupeCert, null, null, subCABaseTime, subCABaseTime + TimeSpan.FromDays(10));
                m_crlRevokedChain[i] = null;
                signingCert = subCACert;
            }

            // create a CRL with a revoked Sub CA
            for (int i = 0; i < kCaChainCount - 1; i++)
            {
                m_crlRevokedChain[i] = CertificateFactory.RevokeCertificate(
                    m_caChain[i],
                    new List<X509CRL>() { m_crlChain[i] },
                    new X509Certificate2Collection { m_caChain[i + 1] });
            }

            // create self signed app certs
            DateTime appBaseTime = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var app in m_goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    app.ApplicationUri,
                    app.ApplicationName,
                    subject,
                    app.DomainNames,
                    CertificateFactory.DefaultKeySize, appBaseTime, 2 * 12,
                    CertificateFactory.DefaultHashSize);
                m_appSelfSignedCerts.Add(appCert);
            }

            // create signed app certs
            foreach (var app in m_goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    app.ApplicationUri,
                    app.ApplicationName,
                    subject,
                    app.DomainNames,
                    CertificateFactory.DefaultKeySize, appBaseTime, 2 * 12,
                    CertificateFactory.DefaultHashSize, false, signingCert);
                app.Certificate = appCert.RawData;
                m_appCerts.Add(appCert);
            }

            // create a CRL with all apps revoked
            m_crlRevokedChain[kCaChainCount - 1] = CertificateFactory.RevokeCertificate(
                    m_caChain[kCaChainCount - 1],
                    new List<X509CRL>() { m_crlChain[kCaChainCount - 1] },
                    m_appCerts);

        }

        /// <summary>
        /// Clean up the Test PKI folder
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            Thread.Sleep(1000);
            Directory.Delete(Utils.ReplaceSpecialFolderNames(m_pkiRoot), true);
        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion
        #region Test Methods
        /// <summary>
        /// Verify self signed app certs are not trusted.
        /// </summary>
        [Test, Order(100)]
        public void VerifySelfSignedAppCertsNotTrusted()
        {
            // verify cert with issuer chain
            CleanupValidatorAndStores();
            var certValidator = InitValidatorWithStores();
            foreach (var cert in m_appSelfSignedCerts)
            {
                var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed app certs are not trusted with other CA chains
        /// </summary>
        [Test, Order(110)]
        public async Task VerifySelfSignedAppCertsNotTrustedWithCA()
        {
            CleanupValidatorAndStores();
            // add random issuer certs
            for (int i = 0; i < kCaChainCount; i++)
            {
                if (i == kCaChainCount / 2)
                {
                    await m_trustedStore.Add(m_caChain[i]);
                    m_trustedStore.AddCRL(m_crlChain[i]);
                }
                else
                {
                    await m_issuerStore.Add(m_caChain[i]);
                    m_issuerStore.AddCRL(m_crlChain[i]);
                }
            }

            var certValidator = InitValidatorWithStores();
            foreach (var cert in m_appSelfSignedCerts)
            {
                var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed app certs throw by default and validate if added to the trusted store.
        /// </summary>
        [Test, Order(150)]
        public async Task VerifySelfSignedAppCerts()
        {
            // verify cert with issuer chain
            {
                // add all certs to issuer store, make sure validation fails.
                CleanupValidatorAndStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    await m_issuerStore.Add(cert);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                    Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
                }

                // add all certs to trusted store
                CleanupValidatorAndStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    await m_trustedStore.Add(cert);
                }
                certValidator = InitValidatorWithStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }

                // add all certs to trusted and issuer store
                CleanupValidatorAndStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    await m_trustedStore.Add(cert);
                    await m_issuerStore.Add(cert);
                }
                certValidator = InitValidatorWithStores();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }

            }
        }


        /// <summary>
        /// Verify signed app certs validate. One of all trusted.
        /// </summary>
        [Test, Order(200)]
        public async Task VerifyAppChainsOneTrusted()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store = i == v ? m_trustedStore : m_issuerStore;
                    await store.Add(m_caChain[i]);
                    store.AddCRL(m_crlChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    certValidator.Validate(new X509Certificate2(app.Certificate));
                }
            }
        }

        /// <summary>
        /// Verify signed app certs validate. All but one in trusted.
        /// </summary>
        [Test, Order(201)]
        public async Task VerifyAppChainsAllButOneTrusted()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store = i != v ? m_trustedStore : m_issuerStore;
                    await store.Add(m_caChain[i]);
                    store.AddCRL(m_crlChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    certValidator.Validate(new X509Certificate2(app.Certificate));
                }
            }
        }

        /// <summary>
        /// Verify app certs with incomplete chain throw.
        /// </summary>
        [Test, Order(210)]
        public async Task VerifyAppChainsIncompleteChain()
        {

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await m_trustedStore.Add(m_caChain[i]);
                        m_trustedStore.AddCRL(m_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify app certs do not validate with invalid chain.
        /// </summary>
        [Test, Order(220)]
        public async Task VerifyAppChainsInvalidChain()
        {

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await m_trustedStore.Add(m_caChain[i]);
                        m_trustedStore.AddCRL(m_crlChain[i]);
                    }
                    else
                    {
                        await m_trustedStore.Add(m_caDupeChain[i]);
                        m_trustedStore.AddCRL(m_crlDupeChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify app certs with good and invalid chain
        /// </summary>
        [Test, Order(230)]
        public async Task VerifyAppChainsWithGoodAndInvalidChain()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store = i == v ? m_trustedStore : m_issuerStore;
                    await store.Add(m_caChain[i]);
                    store.AddCRL(m_crlChain[i]);
                    await store.Add(m_caDupeChain[i]);
                    store.AddCRL(m_crlDupeChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    certValidator.Validate(new X509Certificate2(app.Certificate));
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test, Order(300)]
        public async Task VerifyRevokedTrustedStoreAppChains()
        {
            // verify cert is revoked with CRL in trusted store
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await m_trustedStore.Add(m_caChain[i]);
                        m_trustedStore.AddCRL(m_crlRevokedChain[i]);
                    }
                    else
                    {
                        await m_issuerStore.Add(m_caChain[i]);
                        m_issuerStore.AddCRL(m_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in issuer store
        /// </summary>
        [Test, Order(310)]
        public async Task VerifyRevokedIssuerStoreAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await m_issuerStore.Add(m_caChain[i]);
                        m_issuerStore.AddCRL(m_crlRevokedChain[i]);
                    }
                    else
                    {
                        await m_trustedStore.Add(m_caChain[i]);
                        m_trustedStore.AddCRL(m_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted app cert is revoked with CRL in issuer store
        /// </summary>
        [Test, Order(320)]
        public async Task VerifyRevokedIssuerStoreTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await m_issuerStore.Add(m_caChain[i]);
                        m_issuerStore.AddCRL(m_crlRevokedChain[i]);
                    }
                    else
                    {
                        await m_issuerStore.Add(m_caChain[i]);
                        m_issuerStore.AddCRL(m_crlChain[i]);
                    }
                }
                foreach (var app in m_goodApplicationTestSet)
                {
                    await m_trustedStore.Add(new X509Certificate2(app.Certificate));
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test, Order(330)]
        public async Task VerifyRevokedTrustedStoreNotTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await m_trustedStore.Add(m_caChain[i]);
                    if (i == v)
                    {
                        m_trustedStore.AddCRL(m_crlRevokedChain[i]);
                    }
                    else
                    {
                        m_trustedStore.AddCRL(m_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted cert is revoked with CRL in trusted store.
        /// </summary>
        [Test, Order(340)]
        public async Task VerifyRevokedTrustedStoreTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await m_trustedStore.Add(m_caChain[i]);
                    if (i == v)
                    {
                        m_trustedStore.AddCRL(m_crlRevokedChain[i]);
                    }
                    else
                    {
                        m_trustedStore.AddCRL(m_crlChain[i]);
                    }
                }
                foreach (var app in m_goodApplicationTestSet)
                {
                    await m_trustedStore.Add(new X509Certificate2(app.Certificate));
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted app certs with issuer chain.
        /// </summary>
        [Test, Order(400)]
        public async Task VerifyIssuerChainIncompleteTrustedAppCerts()
        {
            CleanupValidatorAndStores();

            // issuer chain
            for (int i = 0; i < kCaChainCount; i++)
            {
                await m_issuerStore.Add(m_caChain[i]);
                m_issuerStore.AddCRL(m_crlChain[i]);
            }

            // all app certs are trusted
            foreach (var app in m_goodApplicationTestSet)
            {
                await m_trustedStore.Add(new X509Certificate2(app.Certificate));
            }

            var certValidator = InitValidatorWithStores();
            foreach (var app in m_goodApplicationTestSet)
            {
                certValidator.Validate(new X509Certificate2(app.Certificate));
            }
        }

        /// <summary>
        /// Verify trusted app certs with incomplete issuer chain.
        /// </summary>
        [Test, Order(410)]
        public async Task VerifyIssuerChainTrustedAppCerts()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                CleanupValidatorAndStores();
                // issuer chain
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await m_issuerStore.Add(m_caChain[i]);
                        m_issuerStore.AddCRL(m_crlChain[i]);
                    }
                }

                // all app certs are trusted
                foreach (var app in m_goodApplicationTestSet)
                {
                    await m_trustedStore.Add(new X509Certificate2(app.Certificate));
                }

                var certValidator = InitValidatorWithStores();
                foreach (var app in m_goodApplicationTestSet)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                    Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }
        /// <summary>
        /// Verify the PEM Writer, no password
        /// </summary>
        [Test, Order(500)]
        public void VerifyPemWriterPrivateKeys()
        {
            // all app certs are trusted
            foreach (var appCert in m_appSelfSignedCerts)
            {
                var pemDataBlob = CertificateFactory.ExportPrivateKeyAsPEM(appCert);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob, "password");
            }
        }

        /// <summary>
        /// Verify self signed certs, not yet valid.
        /// </summary>
        [Theory, Order(600)]
        public async Task VerifyNotBeforeInvalid(bool trusted)
        {
            var cert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, "App Test Cert", null,
                null, CertificateFactory.DefaultKeySize,
                DateTime.UtcNow + TimeSpan.FromDays(14), 12,
                CertificateFactory.DefaultHashSize);
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(Utils.CompareDistinguishedName("CN=App Test Cert", cert.Subject));
            CleanupValidatorAndStores();
            if (trusted)
            {
                await m_issuerStore.Add(cert);
            }
            else
            {
                await m_trustedStore.Add(cert);
            }
            var certValidator = InitValidatorWithStores();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            if (trusted)
            {
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
            else
            {                
                Assert.AreEqual(StatusCodes.BadCertificateTimeInvalid, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed certs, not yet valid.
        /// </summary>
        [Theory, Order(601)]
        public async Task VerifyNotAfterInvalid(bool trusted)
        {
            var cert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=App Test Cert",
                null, CertificateFactory.DefaultKeySize,
                new DateTime(2010, 1, 1), 12,
                CertificateFactory.DefaultHashSize);
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(Utils.CompareDistinguishedName("CN=App Test Cert", cert.Subject));
            CleanupValidatorAndStores();
            if (trusted)
            {
                await m_issuerStore.Add(cert);
            }
            else
            {
                await m_trustedStore.Add(cert);
            }
            var certValidator = InitValidatorWithStores();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            if (trusted)
            {
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
            else
            {
                Assert.AreEqual(StatusCodes.BadCertificateTimeInvalid, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        [Test, Order(602)]
        public void CertificateValidatorAssignableFromAppConfig() => Assert.DoesNotThrow(() => {
            var appConfig = new ApplicationConfiguration() {
                CertificateValidator = new CertificateValidator()
            };
        });

        #endregion
        #region Private Methods
        private CertificateValidator InitValidatorWithStores()
        {
            var certValidator = new CertificateValidator();
            var issuerTrustList = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = m_issuerStore.Directory.FullName
            };
            var trustedTrustList = new CertificateTrustList {
                StoreType = "Directory",
                StorePath = m_trustedStore.Directory.FullName
            };
            certValidator.Update(issuerTrustList, trustedTrustList, null);
            return certValidator;
        }

        private void CleanupValidatorAndStores()
        {
            TestUtils.CleanupTrustList(m_issuerStore, false);
            TestUtils.CleanupTrustList(m_trustedStore, false);
        }

        private byte[] GetPublicKey(X509Certificate2 certificate)
        {
            var rootBCCert = new X509CertificateParser().ReadCertificate(certificate.RawData);
            return SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(rootBCCert.GetPublicKey()).GetDerEncoded();
        }
        #endregion
        #region Private Fields
        private const int kCaChainCount = 4;
        private const int kGoodApplicationsTestCount = 10;
        private string m_pkiRoot;
        private DirectoryCertificateStore m_issuerStore;
        private DirectoryCertificateStore m_trustedStore;
        private IList<ApplicationTestData> m_goodApplicationTestSet;
        private X509Certificate2[] m_caChain;
        private X509Certificate2[] m_caDupeChain;
        private X509Certificate2[] m_caAllSameIssuerChain;
        private X509CRL[] m_crlChain;
        private X509CRL[] m_crlDupeChain;
        private X509CRL[] m_crlRevokedChain;
        private X509Certificate2Collection m_appCerts;
        private X509Certificate2Collection m_appSelfSignedCerts;
        #endregion
    }

}
