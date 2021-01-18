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
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
#if NETCOREAPP2_1
using X509SignatureGenerator = Opc.Ua.Security.Certificates.X509SignatureGenerator;
#endif

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
        public const string RootCASubject = "CN=Root CA Test Cert";

        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            // set max RSA key size and max SHA-2 hash size
            ushort keySize = 4096;
            ushort hashSize = 512;

            // good applications test set
            var appTestDataGenerator = new ApplicationTestDataGenerator(1);
            m_goodApplicationTestSet = appTestDataGenerator.ApplicationTestSet(kGoodApplicationsTestCount);

            // create all certs and CRL
            m_caChain = new X509Certificate2[kCaChainCount];
            m_caDupeChain = new X509Certificate2[kCaChainCount];
            m_crlChain = new X509CRL[kCaChainCount];
            m_crlDupeChain = new X509CRL[kCaChainCount];
            m_crlRevokedChain = new X509CRL[kCaChainCount];
            m_appCerts = new X509Certificate2Collection();
            m_appSelfSignedCerts = new X509Certificate2Collection();

            DateTime rootCABaseTime = DateTime.UtcNow;
            rootCABaseTime = new DateTime(rootCABaseTime.Year - 1, 1, 1);
            var rootCert = CertificateFactory.CreateCertificate(RootCASubject)
                .SetNotBefore(rootCABaseTime)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSize))
                .SetRSAKeySize(keySize)
                .CreateForRSA();

            m_caChain[0] = rootCert;
            m_crlChain[0] = CertificateFactory.RevokeCertificate(rootCert, null, null);

            // to save time, the dupe chain uses just the default key size/hash
            m_caDupeChain[0] = CertificateFactory.CreateCertificate(RootCASubject)
                .SetNotBefore(rootCABaseTime)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .CreateForRSA();

            m_crlDupeChain[0] = CertificateFactory.RevokeCertificate(m_caDupeChain[0], null, null);
            m_crlRevokedChain[0] = null;

            var signingCert = rootCert;
            DateTime subCABaseTime = DateTime.UtcNow;
            subCABaseTime = new DateTime(subCABaseTime.Year, subCABaseTime.Month, subCABaseTime.Day, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 1; i < kCaChainCount; i++)
            {
                if (keySize > 2048) { keySize -= 1024; }
                if (hashSize > 256) { hashSize -= 128; }
                var subject = $"CN=Sub CA {i} Test Cert";
                var subCACert = CertificateFactory.CreateCertificate(subject)
                    .SetNotBefore(subCABaseTime)
                    .SetLifeTime(5 * 12)
                    .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSize))
                    .SetCAConstraint(kCaChainCount - 1 - i)
                    .SetIssuer(signingCert)
                    .SetRSAKeySize(keySize)
                    .CreateForRSA();
                m_caChain[i] = subCACert;
                m_crlChain[i] = CertificateFactory.RevokeCertificate(subCACert, null, null, subCABaseTime, subCABaseTime + TimeSpan.FromDays(10));
                var subCADupeCert = CertificateFactory.CreateCertificate(subject)
                    .SetNotBefore(subCABaseTime)
                    .SetLifeTime(5 * 12)
                    .SetCAConstraint(kCaChainCount - 1 - i)
                    .SetIssuer(signingCert)
                    .CreateForRSA();
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
            foreach (var app in m_goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    app.ApplicationUri,
                    app.ApplicationName,
                    subject,
                    app.DomainNames)
                    .CreateForRSA();
                m_appSelfSignedCerts.Add(appCert);
            }

            // create signed app certs
            foreach (var app in m_goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    app.ApplicationUri,
                    app.ApplicationName,
                    subject,
                    app.DomainNames)
                    .SetIssuer(signingCert)
                    .CreateForRSA();
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
        /// Clean up the Test PKI folder.
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
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
        [Test]
        public void VerifySelfSignedAppCertsNotTrusted()
        {
            // verify cert with issuer chain
            using (var validator = TemporaryCertValidator.Create(true))
            {
                var certValidator = validator.Update();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                    Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
                }
                Assert.AreEqual(m_appSelfSignedCerts.Count, validator.RejectedStore.Enumerate().GetAwaiter().GetResult().Count);

                // add auto approver
                var approver = new CertValidationApprover(new StatusCode[] { StatusCodes.BadCertificateUntrusted });
                certValidator.CertificateValidation += approver.OnCertificateValidation;
                foreach (var cert in m_appSelfSignedCerts)
                {
                    using (var publicKey = new X509Certificate2(cert))
                    {
                        certValidator.Validate(publicKey);
                    }
                }
                // count certs written to rejected store
                Assert.AreEqual(m_appSelfSignedCerts.Count, approver.AcceptedCount);
            }
        }

        /// <summary>
        /// Verify self signed app certs are not trusted with other CA chains
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsNotTrustedWithCA()
        {
            using (var validator = TemporaryCertValidator.Create())
            {
                // add random issuer certs
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == kCaChainCount / 2)
                    {
                        await validator.TrustedStore.Add(m_caChain[i]).ConfigureAwait(false);
                        validator.TrustedStore.AddCRL(m_crlChain[i]);
                    }
                    else
                    {
                        await validator.IssuerStore.Add(m_caChain[i]).ConfigureAwait(false);
                        validator.IssuerStore.AddCRL(m_crlChain[i]);
                    }
                }

                var certValidator = validator.Update();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                    Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify self signed app certs throw by default.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsThrow()
        {
            // verify cert with issuer chain
            {
                // add all certs to issuer store, make sure validation fails.
                using (var validator = TemporaryCertValidator.Create(true))
                {
                    foreach (var cert in m_appSelfSignedCerts)
                    {
                        await validator.IssuerStore.Add(cert).ConfigureAwait(false);
                    }
                    Assert.AreEqual(m_appSelfSignedCerts.Count, validator.IssuerStore.Enumerate().Result.Count);
                    var certValidator = validator.Update();
                    foreach (var cert in m_appSelfSignedCerts)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(cert)); });
                        Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                    Assert.AreEqual(m_appSelfSignedCerts.Count, validator.RejectedStore.Enumerate().Result.Count);
                }
            }
        }

        /// <summary>
        /// Verify self signed app certs are trusted.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsTrusted()
        {
            // add all certs to trusted store
            using (var validator = TemporaryCertValidator.Create())
            {

                foreach (var cert in m_appSelfSignedCerts)
                {
                    await validator.TrustedStore.Add(cert).ConfigureAwait(false);
                }
                Assert.AreEqual(m_appSelfSignedCerts.Count, validator.TrustedStore.Enumerate().Result.Count);
                var certValidator = validator.Update();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }
            }
        }

        /// <summary>
        /// Verify self signed app certs validate if added to all stores.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsAllStores()
        {
            // add all certs to trusted and issuer store
            using (var validator = TemporaryCertValidator.Create())
            {
                foreach (var cert in m_appSelfSignedCerts)
                {
                    await validator.TrustedStore.Add(cert).ConfigureAwait(false);
                    await validator.IssuerStore.Add(cert);
                }
                var certValidator = validator.Update();
                foreach (var cert in m_appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }
            }
        }

        /// <summary>
        /// Verify signed app certs validate. One of all trusted.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsOneTrusted()
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                long start = stopWatch.ElapsedMilliseconds;
                TestContext.Out.WriteLine($"Chain Number {v}, Total Elapsed: {start}");
                using (var validator = TemporaryCertValidator.Create())
                {
                    TestContext.Out.WriteLine($"Cleanup: {stopWatch.ElapsedMilliseconds - start}");
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        ICertificateStore store = i == v ? validator.TrustedStore : validator.IssuerStore;
                        await store.Add(m_caChain[i]).ConfigureAwait(false);
                        store.AddCRL(m_crlChain[i]);
                    }
                    TestContext.Out.WriteLine($"AddChains: {stopWatch.ElapsedMilliseconds - start}");
                    var certValidator = validator.Update();
                    TestContext.Out.WriteLine($"InitValidator: {stopWatch.ElapsedMilliseconds - start}");
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        certValidator.Validate(new X509Certificate2(app.Certificate));
                    }
                    TestContext.Out.WriteLine($"Validation: {stopWatch.ElapsedMilliseconds - start}");
                }
            }
            TestContext.Out.WriteLine($"Total: {stopWatch.ElapsedMilliseconds}");
        }

        /// <summary>
        /// Verify signed app certs validate. All but one in trusted.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsAllButOneTrusted()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        ICertificateStore store = i != v ? validator.TrustedStore : validator.IssuerStore;
                        await store.Add(m_caChain[i]);
                        store.AddCRL(m_crlChain[i]);
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        certValidator.Validate(new X509Certificate2(app.Certificate));
                    }
                }
            }
        }

        /// <summary>
        /// Verify app certs with incomplete chain throw.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsIncompleteChain()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i != v)
                        {
                            await validator.TrustedStore.Add(m_caChain[i]);
                            validator.TrustedStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify app certs do not validate with invalid chain.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsInvalidChain()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i != v)
                        {
                            await validator.TrustedStore.Add(m_caChain[i]);
                            validator.TrustedStore.AddCRL(m_crlChain[i]);
                        }
                        else
                        {
                            await validator.TrustedStore.Add(m_caDupeChain[i]);
                            validator.TrustedStore.AddCRL(m_crlDupeChain[i]);
                        }
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify app certs with good and invalid chain
        /// </summary>
        [Test]
        public async Task VerifyAppChainsWithGoodAndInvalidChain()
        {
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        ICertificateStore store = i == v ? validator.TrustedStore : validator.IssuerStore;
                        await store.Add(m_caChain[i]);
                        store.AddCRL(m_crlChain[i]);
                        await store.Add(m_caDupeChain[i]);
                        store.AddCRL(m_crlDupeChain[i]);
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        certValidator.Validate(new X509Certificate2(app.Certificate));
                    }
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreAppChains()
        {
            // verify cert is revoked with CRL in trusted store
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i == v)
                        {
                            await validator.TrustedStore.Add(m_caChain[i]);
                            validator.TrustedStore.AddCRL(m_crlRevokedChain[i]);
                        }
                        else
                        {
                            await validator.IssuerStore.Add(m_caChain[i]);
                            validator.IssuerStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in issuer store
        /// </summary>
        [Test]
        public async Task VerifyRevokedIssuerStoreAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i == v)
                        {
                            await validator.IssuerStore.Add(m_caChain[i]);
                            validator.IssuerStore.AddCRL(m_crlRevokedChain[i]);
                        }
                        else
                        {
                            await validator.TrustedStore.Add(m_caChain[i]);
                            validator.TrustedStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify trusted app cert is revoked with CRL in issuer store
        /// </summary>
        [Test]
        public async Task VerifyRevokedIssuerStoreTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i == v)
                        {
                            await validator.IssuerStore.Add(m_caChain[i]);
                            validator.IssuerStore.AddCRL(m_crlRevokedChain[i]);
                        }
                        else
                        {
                            await validator.IssuerStore.Add(m_caChain[i]);
                            validator.IssuerStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        await validator.TrustedStore.Add(new X509Certificate2(app.Certificate));
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreNotTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        await validator.TrustedStore.Add(m_caChain[i]);
                        if (i == v)
                        {
                            validator.TrustedStore.AddCRL(m_crlRevokedChain[i]);
                        }
                        else
                        {
                            validator.TrustedStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify trusted cert is revoked with CRL in trusted store.
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreTrustedAppChains()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                using (var validator = TemporaryCertValidator.Create())
                {

                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        await validator.TrustedStore.Add(m_caChain[i]);
                        if (i == v)
                        {
                            validator.TrustedStore.AddCRL(m_crlRevokedChain[i]);
                        }
                        else
                        {
                            validator.TrustedStore.AddCRL(m_crlChain[i]);
                        }
                    }
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        await validator.TrustedStore.Add(new X509Certificate2(app.Certificate));
                    }
                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateRevoked, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify trusted app certs with issuer chain.
        /// </summary>
        [Test]
        public async Task VerifyIssuerChainIncompleteTrustedAppCerts()
        {
            using (var validator = TemporaryCertValidator.Create())
            {

                // issuer chain
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await validator.IssuerStore.Add(m_caChain[i]);
                    validator.IssuerStore.AddCRL(m_crlChain[i]);
                }

                // all app certs are trusted
                foreach (var app in m_goodApplicationTestSet)
                {
                    await validator.TrustedStore.Add(new X509Certificate2(app.Certificate));
                }

                var certValidator = validator.Update();
                foreach (var app in m_goodApplicationTestSet)
                {
                    certValidator.Validate(new X509Certificate2(app.Certificate));
                }
            }
        }

        /// <summary>
        /// Verify trusted app certs with incomplete issuer chain.
        /// </summary>
        [Test]
        public async Task VerifyIssuerChainTrustedAppCerts()
        {
            for (int v = 0; v < kCaChainCount; v++)
            {
                TestContext.Out.WriteLine("Chain cert {0} not in issuer store.", v);
                using (var validator = TemporaryCertValidator.Create())
                {
                    // issuer chain
                    for (int i = 0; i < kCaChainCount; i++)
                    {
                        if (i != v)
                        {
                            await validator.IssuerStore.Add(m_caChain[i]);
                            validator.IssuerStore.AddCRL(m_crlChain[i]);
                        }
                    }

                    // all app certs are trusted
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        await validator.TrustedStore.Add(new X509Certificate2(app.Certificate));
                    }

                    var certValidator = validator.Update();
                    foreach (var app in m_goodApplicationTestSet)
                    {
                        var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); });
                        Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Verify the PEM Writer, no password
        /// </summary>
        [Test]
        public void VerifyPemWriterPrivateKeys()
        {
            // all app certs are trusted
            foreach (var appCert in m_appSelfSignedCerts)
            {
                var pemDataBlob = PEMWriter.ExportPrivateKeyAsPEM(appCert);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert.RawData), pemDataBlob);
                // note: password is ignored
                var newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert.RawData), pemDataBlob, "password");
                X509Utils.VerifyRSAKeyPair(newCert, newCert, true);
            }
        }

        /// <summary>
        /// Verify the PEM Writer, no password.
        /// </summary>
        [Test]
        public void VerifyPemWriterPublicKeys()
        {
            // all app certs are trusted
            foreach (var appCert in m_appSelfSignedCerts)
            {
                var pemDataBlob = PEMWriter.ExportCertificateAsPEM(appCert);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
#if NETCOREAPP3_1
                var exception = Assert.Throws<ArgumentException>(() => { CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob); });
#endif
            }
        }

#if NETCOREAPP3_1
        /// <summary>
        /// Verify the PEM Writer, no password.
        /// </summary>
        [Test, Order(530)]
        public void VerifyPemWriterRSAPrivateKeys()
        {
            // all app certs are trusted
            foreach (var appCert in m_appSelfSignedCerts)
            {
                var pemDataBlob = PEMWriter.ExportRSAPrivateKeyAsPEM(appCert);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                var cert = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert.RawData), pemDataBlob);
                Assert.NotNull(cert);
                // note: password is ignored
                var newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert.RawData), pemDataBlob, "password");
                X509Utils.VerifyRSAKeyPair(newCert, newCert, true);
            }
        }

        /// <summary>
        /// Verify the PEM Writer, with password
        /// </summary>
        [Test]
        public void VerifyPemWriterPasswordPrivateKeys()
        {
            // all app certs are trusted
            foreach (var appCert in m_appSelfSignedCerts)
            {
                var password = Guid.NewGuid().ToString().Substring(0, 8);
                TestContext.Out.WriteLine("Password: {0}", password);
                var pemDataBlob = PEMWriter.ExportPrivateKeyAsPEM(appCert, password);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                var newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob, password);
                var exception = Assert.Throws<CryptographicException>(() => { CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob); });
                X509Utils.VerifyRSAKeyPair(newCert, newCert, true);
            }
        }
#endif

        /// <summary>
        /// Verify self signed certs, not yet valid.
        /// </summary>
        [Theory]
        public async Task VerifyNotBeforeInvalid(bool trusted)
        {
            var applicationName = "App Test Cert";
            var cert = CertificateFactory.CreateCertificate(
                null, applicationName, null, null)
                .SetNotBefore(DateTime.Today.AddDays(14))
                .CreateForRSA();
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName("CN=" + applicationName, cert.Subject));
            var validator = TemporaryCertValidator.Create();
            if (!trusted)
            {
                await validator.IssuerStore.Add(cert);
            }
            else
            {
                await validator.TrustedStore.Add(cert);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            if (!trusted)
            {
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
                // check the chained service result
                ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
                Assert.NotNull(innerResult);
                Assert.AreEqual(StatusCodes.BadCertificateTimeInvalid,
                    innerResult.StatusCode.Code,
                    innerResult.LocalizedText.Text);
            }
            else
            {
                Assert.AreEqual(StatusCodes.BadCertificateTimeInvalid, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed certs, not after invalid.
        /// </summary>
        [Theory]
        public async Task VerifyNotAfterInvalid(bool trusted)
        {
            var applicationName = "App Test Cert";
            var cert = CertificateFactory.CreateCertificate(
                null, applicationName, null, null)
                .SetNotBefore(new DateTime(2010, 1, 1))
                .SetLifeTime(12)
                .CreateForRSA();
            TestContext.Out.WriteLine($"{cert}:");
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName("CN=" + applicationName, cert.Subject));
            var validator = TemporaryCertValidator.Create();
            if (!trusted)
            {
                await validator.IssuerStore.Add(cert);
            }
            else
            {
                await validator.TrustedStore.Add(cert);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            if (!trusted)
            {
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
            else
            {
                Assert.AreEqual(StatusCodes.BadCertificateTimeInvalid, serviceResultException.StatusCode, serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify signed cert, not after invalid and chain missing.
        /// </summary>
        [Theory]
        public async Task VerifySignedNotAfterInvalid(bool trusted)
        {
            var subject = "CN=Signed App Test Cert";
            var cert = CertificateFactory.CreateCertificate(
                null, null, subject, null)
                .SetNotBefore(DateTime.Today.AddDays(30))
                .SetLifeTime(12)
                .SetIssuer(m_caChain[0])
                .CreateForRSA();
            TestContext.Out.WriteLine($"{cert}:");
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName(subject, cert.Subject));
            var validator = TemporaryCertValidator.Create();
            if (!trusted)
            {
                await validator.IssuerStore.Add(cert);
            }
            else
            {
                await validator.TrustedStore.Add(cert);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
            // approver tries to suppress error which is not suppressable
            var approver = new CertValidationApprover(new StatusCode[] {
                StatusCodes.BadCertificateTimeInvalid,
                StatusCodes.BadCertificateChainIncomplete
            });
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            Assert.AreEqual(StatusCodes.BadCertificateChainIncomplete, serviceResultException.StatusCode, serviceResultException.Message);
            certValidator.CertificateValidation -= approver.OnCertificateValidation;
        }

        /// <summary>
        /// Validate various null parameter return null exception.
        /// </summary>
        [Test]
        public void TestNullParameters()
        {
            var validator = TemporaryCertValidator.Create();
            var certValidator = validator.Update();
            Assert.Throws<ArgumentNullException>(() => { certValidator.Update((SecurityConfiguration)null).GetAwaiter().GetResult(); });
            Assert.Throws<ArgumentNullException>(() => { certValidator.Update((ApplicationConfiguration)null).GetAwaiter().GetResult(); });
        }

        /// <summary>
        /// Validate the event handlers can be used.
        /// </summary>
        [Test]
        public void TestEventHandler()
        {
            var validator = TemporaryCertValidator.Create();
            var certValidator = validator.Update();
            certValidator.CertificateUpdate += OnCertificateUpdate;
            certValidator.CertificateValidation += OnCertificateValidation;
            certValidator.CertificateUpdate -= OnCertificateUpdate;
            certValidator.CertificateValidation -= OnCertificateValidation;
        }

        /// <summary>
        /// Validate Sha1 signed certificates cause a policy check failed.
        /// </summary>
        [Theory]
        public async Task TestSHA1Rejected(bool trusted)
        {
#if NETCOREAPP3_1
            Assert.Ignore("SHA1 is unsupported on .NET Core 3.1");
#endif
            var cert = CertificateFactory.CreateCertificate(null, null, "CN=SHA1 signed", null)
                .SetHashAlgorithm(HashAlgorithmName.SHA1)
                .CreateForRSA();
            var validator = TemporaryCertValidator.Create();
            if (trusted)
            {
                await validator.TrustedStore.Add(cert).ConfigureAwait(false);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            Assert.AreEqual(StatusCodes.BadCertificatePolicyCheckFailed, serviceResultException.StatusCode, serviceResultException.Message);
            Assert.NotNull(serviceResultException.InnerResult);
            ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
            if (!trusted)
            {
                Assert.NotNull(innerResult);
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted,
                    innerResult.StatusCode.Code,
                    innerResult.LocalizedText.Text);
            }
            else
            {
                Assert.Null(innerResult);
            }
        }

        /// <summary>
        /// Validate invalid key usage flags cause use not allowed.
        /// </summary>
        [Theory]
        public async Task TestInvalidKeyUsage(bool trusted)
        {
            var subject = "CN=Invalid Signature Cert";
            // self signed but key usage is not valid for app cert
            var cert = CertificateFactory.CreateCertificate(null, null, subject, null)
                .SetCAConstraint(0)
                .CreateForRSA();

            Assert.True(X509Utils.VerifySelfSigned(cert));
            var validator = TemporaryCertValidator.Create();
            if (trusted)
            {
                await validator.TrustedStore.Add(cert).ConfigureAwait(false);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            Assert.AreEqual(StatusCodes.BadCertificateUseNotAllowed, serviceResultException.StatusCode, serviceResultException.Message);
            Assert.NotNull(serviceResultException.InnerResult);
            var innerResult = serviceResultException.InnerResult.InnerResult;
            if (trusted)
            {
                Assert.Null(innerResult);
            }
            else
            {
                Assert.NotNull(innerResult);
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, innerResult.StatusCode.Code, innerResult.LocalizedText.Text);
            }
        }

        /// <summary>
        /// Validate certificates with invalid signature are returned as invalid.
        /// </summary>
        [Theory]
        public async Task TestInvalidSignature(bool ca, bool trusted)
        {
            var subject = "CN=Invalid Signature Cert";
            var certBase = CertificateFactory.CreateCertificate(null, null, subject, null)
                .CreateForRSA();

            var generator = X509SignatureGenerator.CreateForRSA(m_caChain[0].GetRSAPrivateKey(), RSASignaturePadding.Pkcs1);
            // generate a self signed cert with invalid signature
            var builder = CertificateFactory.CreateCertificate(null, null, subject, null);
            if (ca)
            {
                // set the CA flag changes the key usage to sign only
                builder.SetCAConstraint(0);
            }
            var cert = builder.SetIssuer(certBase)
            .SetRSAPublicKey(certBase.GetRSAPublicKey())
            .CreateForRSA(generator);

            Assert.False(X509Utils.VerifySelfSigned(cert));
            var validator = TemporaryCertValidator.Create();
            if (trusted)
            {
                await validator.TrustedStore.Add(cert).ConfigureAwait(false);
            }
            var certValidator = validator.Update();
            var approver = new CertValidationApprover(new StatusCode[] { StatusCodes.BadCertificateUntrusted });
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            ServiceResult innerResult;
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            if (ca)
            {
                // The CA version fails for the key usage flags
                Assert.AreEqual(StatusCodes.BadCertificateUseNotAllowed, serviceResultException.StatusCode, serviceResultException.Message);
                Assert.NotNull(serviceResultException.InnerResult);
                innerResult = serviceResultException.InnerResult.InnerResult;
            }
            else
            {
                innerResult = serviceResultException.InnerResult;
            }
            if (!trusted)
            {
                // for the untrusted case, the untrusted error is also reported.
                Assert.NotNull(innerResult);
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted,
                    innerResult.StatusCode.Code,
                    innerResult.LocalizedText.Text);
                innerResult = innerResult.InnerResult;
            }
            // However, all cert versions got an invalid signature, must fail...
            Assert.NotNull(innerResult);
            Assert.AreEqual(StatusCodes.BadCertificateInvalid,
                innerResult.StatusCode.Code,
                innerResult.LocalizedText.Text);
            Assert.AreEqual(0, approver.Count);
        }

        /// <summary>
        /// Test if a key below min length is detected.
        /// </summary>
        [Theory]
        public async Task TestMinimumKeyRejected(bool trusted)
        {
            var cert = CertificateFactory.CreateCertificate(null, null, "CN=1k Key", null)
                .SetRSAKeySize(1024)
                .CreateForRSA();
            var validator = TemporaryCertValidator.Create();
            if (trusted)
            {
                await validator.TrustedStore.Add(cert);
            }
            var certValidator = validator.Update();
            var serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
            Assert.AreEqual(StatusCodes.BadCertificatePolicyCheckFailed, serviceResultException.StatusCode, serviceResultException.Message);
            Assert.NotNull(serviceResultException.InnerResult);
            ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
            if (!trusted)
            {
                Assert.NotNull(innerResult);
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted,
                    innerResult.StatusCode.Code,
                    innerResult.LocalizedText.Text);
            }
            else
            {
                Assert.Null(innerResult);
            }

            // approve suppression of smaller key
            var approver = new CertValidationApprover(new StatusCode[] {
                StatusCodes.BadCertificatePolicyCheckFailed
            });
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            if (trusted)
            {
                certValidator.Validate(cert);
            }
            else
            {
                serviceResultException = Assert.Throws<ServiceResultException>(() => { certValidator.Validate(cert); });
                Assert.AreEqual(StatusCodes.BadCertificateUntrusted, serviceResultException.StatusCode, serviceResultException.Message);
            }
            certValidator.CertificateValidation -= approver.OnCertificateValidation;
        }

        /// <summary>
        /// Verify the certificate validator can be assigned.
        /// </summary>
        [Test]
        public void CertificateValidatorAssignableFromAppConfig() => Assert.DoesNotThrow(() => {
            var appConfig = new ApplicationConfiguration() {
                CertificateValidator = new CertificateValidator()
            };
            Assert.NotNull(appConfig);
            Assert.NotNull(appConfig.CertificateValidator);
        });
        #endregion

        #region Private Methods
        private void OnCertificateUpdate(object sender, CertificateUpdateEventArgs e)
        {
        }
        private void OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
        }
        #endregion

        #region Private Fields
        private const int kCaChainCount = 3;
        private const int kGoodApplicationsTestCount = 3;
        private IList<ApplicationTestData> m_goodApplicationTestSet;
        private X509Certificate2[] m_caChain;
        private X509Certificate2[] m_caDupeChain;
        private X509CRL[] m_crlChain;
        private X509CRL[] m_crlDupeChain;
        private X509CRL[] m_crlRevokedChain;
        private X509Certificate2Collection m_appCerts;
        private X509Certificate2Collection m_appSelfSignedCerts;
        #endregion
    }

    /// <summary>
    /// Helper to approve suppressable errors in test cases.
    /// To catch cases where unsuppressable errors should not
    /// call for approvals.
    /// </summary>
    class CertValidationApprover
    {
        public StatusCode[] ApprovedCodes { get; }
        public int Count { get; private set; }
        public int AcceptedCount { get; private set; }
        public CertValidationApprover(StatusCode[] approvedCodes)
        {
            ApprovedCodes = approvedCodes;
            AcceptedCount = Count = 0;
        }

        public void OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            Count++;
            if (ApprovedCodes.Contains(e.Error.StatusCode))
            {
                e.Accept = true;
                AcceptedCount++;
            }
        }

    }
}
