/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Security.Certificates.Tests;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateValidator class.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidator")]
    [Parallelizable]
    [SetCulture("en-us")]
    public class CertificateValidatorTest
    {
        [DatapointSource]
        public static readonly ECCurveHashPair[] ECCurveHashPairs = CertificateTestsForECDsa
            .GetECCurveHashPairs();

        public const string RootCASubject = "CN=Root CA Test Cert, O=OPC Foundation";

        /// <summary>
        /// Set up cert chains and validate.
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            // set max RSA key size and max SHA-2 hash size
            ushort keySize = 4096;
            ushort hashSize = 512;

            // good applications test set
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appTestDataGenerator = new ApplicationTestDataGenerator(1, telemetry);
            m_goodApplicationTestSet = appTestDataGenerator.ApplicationTestSet(
                kGoodApplicationsTestCount);
            m_notYetValidCertsApplicationTestSet = appTestDataGenerator.ApplicationTestSet(
                kGoodApplicationsTestCount);

            // create all certs and CRL
            m_caChain = new X509Certificate2[kCaChainCount];
            m_caDupeChain = new X509Certificate2[kCaChainCount];
            m_crlChain = new X509CRL[kCaChainCount];
            m_crlDupeChain = new X509CRL[kCaChainCount];
            m_crlRevokedChain = new X509CRL[kCaChainCount];
            m_appCerts = [];
            m_appSelfSignedCerts = [];
            m_notYetValidAppCerts = [];

            DateTime rootCABaseTime = DateTime.UtcNow.AddDays(-1);
            rootCABaseTime = new DateTime(rootCABaseTime.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            X509Certificate2 rootCert = CertificateFactory
                .CreateCertificate(RootCASubject)
                .SetNotBefore(rootCABaseTime)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSize))
                .SetRSAKeySize(keySize)
                .CreateForRSA();

            m_caChain[0] = rootCert;
            m_crlChain[0] = CertificateFactory.RevokeCertificate(rootCert, null, null);

            // to save time, the dupe chain uses just the default key size/hash
            m_caDupeChain[0] = CertificateFactory
                .CreateCertificate(RootCASubject)
                .SetNotBefore(rootCABaseTime)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .CreateForRSA();

            m_crlDupeChain[0] = CertificateFactory.RevokeCertificate(m_caDupeChain[0], null, null);
            m_crlRevokedChain[0] = null;

            X509Certificate2 signingCert = rootCert;
            DateTime subCABaseTime = DateTime.UtcNow.AddDays(-1);
            subCABaseTime = new DateTime(
                subCABaseTime.Year,
                subCABaseTime.Month,
                subCABaseTime.Day,
                0,
                0,
                0,
                DateTimeKind.Utc);
            for (int i = 1; i < kCaChainCount; i++)
            {
                if (keySize > 2048)
                {
                    keySize -= 1024;
                }
                if (hashSize > 256)
                {
                    hashSize -= 128;
                }
                string subject = $"CN=Sub CA {i} Test Cert, O=OPC Foundation";
                X509Certificate2 subCACert = CertificateFactory
                    .CreateCertificate(subject)
                    .SetNotBefore(subCABaseTime)
                    .SetLifeTime(5 * 12)
                    .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSize))
                    .SetCAConstraint(kCaChainCount - 1 - i)
                    .SetIssuer(signingCert)
                    .SetRSAKeySize(keySize)
                    .CreateForRSA();
                m_caChain[i] = subCACert;
                m_crlChain[i] = CertificateFactory.RevokeCertificate(
                    subCACert,
                    null,
                    null,
                    subCABaseTime,
                    subCABaseTime + TimeSpan.FromDays(10));
                X509Certificate2 subCADupeCert = CertificateFactory
                    .CreateCertificate(subject)
                    .SetNotBefore(subCABaseTime)
                    .SetLifeTime(5 * 12)
                    .SetCAConstraint(kCaChainCount - 1 - i)
                    .SetIssuer(signingCert)
                    .CreateForRSA();
                m_caDupeChain[i] = subCADupeCert;
                m_crlDupeChain[i] = CertificateFactory.RevokeCertificate(
                    subCADupeCert,
                    null,
                    null,
                    subCABaseTime,
                    subCABaseTime + TimeSpan.FromDays(10));
                m_crlRevokedChain[i] = null;
                signingCert = subCACert;
            }

            // create a CRL with a revoked Sub CA
            for (int i = 0; i < kCaChainCount - 1; i++)
            {
                m_crlRevokedChain[i] = CertificateFactory.RevokeCertificate(
                    m_caChain[i],
                    [m_crlChain[i]],
                    [m_caChain[i + 1]]);
            }

            // create self signed app certs
            foreach (ApplicationTestData app in m_goodApplicationTestSet)
            {
                string subject = app.Subject;
                X509Certificate2 appCert = CertificateFactory
                    .CreateCertificate(
                        app.ApplicationUri,
                        app.ApplicationName,
                        subject,
                        app.DomainNames)
                    .CreateForRSA();
                m_appSelfSignedCerts.Add(appCert);
            }

            // create signed app certs
            foreach (ApplicationTestData app in m_goodApplicationTestSet)
            {
                string subject = app.Subject;
                X509Certificate2 appCert = CertificateFactory
                    .CreateCertificate(
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
                [m_crlChain[kCaChainCount - 1]],
                m_appCerts);

            // create signed expired app certs
            foreach (ApplicationTestData app in m_notYetValidCertsApplicationTestSet)
            {
                string subject = app.Subject;
                X509Certificate2 expiredappcert = CertificateFactory
                    .CreateCertificate(
                        app.ApplicationUri,
                        app.ApplicationName,
                        subject,
                        app.DomainNames)
                    .SetNotAfter(subCABaseTime.AddMonths(4))
                    .SetNotBefore(subCABaseTime.AddMonths(1))
                    .SetIssuer(signingCert)
                    .CreateForRSA();
                app.Certificate = expiredappcert.RawData;
                m_notYetValidAppCerts.Add(expiredappcert);
            }
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

        /// <summary>
        /// Verify self signed app certs are not trusted.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsNotTrustedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            using var validator = TemporaryCertValidator.Create(telemetry, true);
            CertificateValidator certValidator = validator.Update();
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(new X509Certificate2(cert), CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }

            await Task.Delay(1500).ConfigureAwait(false);
            Assert.AreEqual(
                m_appSelfSignedCerts.Count,
                validator.RejectedStore.EnumerateAsync().GetAwaiter().GetResult().Count,
                "All self signed certs shall be contained in the RejectedStore");

            // add auto approver
            var approver = new CertValidationApprover([StatusCodes.BadCertificateUntrusted]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                using var publicKey = new X509Certificate2(cert);
                await certValidator.ValidateAsync(publicKey, CancellationToken.None).ConfigureAwait(false);
            }
            // count certs written to rejected store
            Assert.AreEqual(m_appSelfSignedCerts.Count, approver.AcceptedCount,
                "All self signed certs shall be accepted with StatusCode BadCertificateUntrusted");
        }

        /// <summary>
        /// Verify self signed app certs are not trusted with other CA chains
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsNotTrustedWithCAAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using var validator = TemporaryCertValidator.Create(telemetry);
            // add random issuer certs
            for (int i = 0; i < kCaChainCount; i++)
            {
                if (i == kCaChainCount / 2)
                {
                    await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    await validator.TrustedStore.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
                }
                else
                {
                    await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    await validator.IssuerStore.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
                }
            }

            CertificateValidator certValidator = validator.Update();
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(new X509Certificate2(cert), CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed app certs throw by default.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsThrowAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            {
                // add all certs to issuer store, make sure validation fails.
                using var validator = TemporaryCertValidator.Create(telemetry, true);
                foreach (X509Certificate2 cert in m_appSelfSignedCerts)
                {
                    await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
                }
                Assert.AreEqual(
                    m_appSelfSignedCerts.Count,
                    (await validator.IssuerStore.EnumerateAsync().ConfigureAwait(false)).Count);
                CertificateValidator certValidator = validator.Update();
                foreach (X509Certificate2 cert in m_appSelfSignedCerts)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(new X509Certificate2(cert), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }

                await Task.Delay(1000).ConfigureAwait(false);
                Assert.AreEqual(
                    m_appSelfSignedCerts.Count,
                    (await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false)).Count);
            }
        }

        /// <summary>
        /// Verify untrusted app certs do not overflow the rejected store.
        /// </summary>
        [Test]
        public async Task VerifyRejectedCertsDoNotOverflowStoreAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // test number of rejected certs
            const int kNumberOfRejectCertsHistory = 5;

            // add all certs to issuer store, make sure validation fails.
            using var validator = TemporaryCertValidator.Create(telemetry, true);
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
            }
            X509Certificate2Collection certificates = await validator
                .IssuerStore.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.AreEqual(m_appSelfSignedCerts.Count, certificates.Count);

            CertificateValidator certValidator = validator.Update();
            certValidator.MaxRejectedCertificates = kNumberOfRejectCertsHistory;
            try
            {
                await Task.Delay(1000).ConfigureAwait(false);

                foreach (X509Certificate2 cert in m_appCerts)
                {
                    var certs = new X509Certificate2Collection(cert);
                    certs.AddRange(m_caChain);
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(certs, CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }

                foreach (X509Certificate2 cert in m_notYetValidAppCerts)
                {
                    var certs = new X509Certificate2Collection(cert);
                    certs.AddRange(m_caChain);
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(certs, CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }

                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.GreaterOrEqual(
                    m_caChain.Length + kNumberOfRejectCertsHistory + 1,
                    certificates.Count);

                foreach (X509Certificate2 cert in m_appSelfSignedCerts)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(new X509Certificate2Collection(cert), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }

                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.GreaterOrEqual(kNumberOfRejectCertsHistory + 1, certificates.Count);

                // override with the same content
                foreach (X509Certificate2 cert in m_appSelfSignedCerts)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator
                                .ValidateAsync(
                                    new X509Certificate2Collection(cert),
                                    CancellationToken.None)
                                .ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }

                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.GreaterOrEqual(kNumberOfRejectCertsHistory + 1, certificates.Count);

                // test setter if overflow certs are not deleted
                certValidator.MaxRejectedCertificates = 300;
                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.GreaterOrEqual(kNumberOfRejectCertsHistory + 1, certificates.Count);

                // test setter if overflow certs are deleted
                certValidator.MaxRejectedCertificates = 3;
                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.GreaterOrEqual(3, certificates.Count);

                // test setter if allcerts are deleted
                certValidator.MaxRejectedCertificates = -1;
                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.LessOrEqual(0, certificates.Count);

                // ensure no certs are added to the rejected store
                foreach (X509Certificate2 cert in m_appSelfSignedCerts)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator
                                .ValidateAsync(
                                    new X509Certificate2Collection(cert),
                                    CancellationToken.None)
                                .ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
                await Task.Delay(1000).ConfigureAwait(false);
                certificates = await validator.RejectedStore.EnumerateAsync().ConfigureAwait(false);
                Assert.LessOrEqual(0, certificates.Count);
            }
            finally
            {
                certValidator.MaxRejectedCertificates = kNumberOfRejectCertsHistory;
            }
        }

        /// <summary>
        /// Verify self signed app certs are trusted.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsTrustedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // add all certs to trusted store
            using var validator = TemporaryCertValidator.Create(telemetry);
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            Assert.AreEqual(
                m_appSelfSignedCerts.Count,
                validator.TrustedStore.EnumerateAsync().Result.Count);
            CertificateValidator certValidator = validator.Update();
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                await certValidator.ValidateAsync(new X509Certificate2(cert), default).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verify self signed app certs validate if added to all stores.
        /// </summary>
        [Test]
        public async Task VerifySelfSignedAppCertsAllStoresAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // add all certs to trusted and issuer store
            using var validator = TemporaryCertValidator.Create(telemetry);
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
                await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            foreach (X509Certificate2 cert in m_appSelfSignedCerts)
            {
                await certValidator.ValidateAsync(new X509Certificate2(cert), CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verify signed app certs validate. One of all trusted.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsOneTrustedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                long start = stopWatch.ElapsedMilliseconds;
                TestContext.Out.WriteLine($"Chain Number {v}, Total Elapsed: {start}");
                using var validator = TemporaryCertValidator.Create(telemetry);
                TestContext.Out.WriteLine($"Cleanup: {stopWatch.ElapsedMilliseconds - start}");
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store = i == v
                        ? validator.TrustedStore
                        : validator.IssuerStore;
                    await store.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    await store.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
                }
                TestContext.Out.WriteLine($"AddChains: {stopWatch.ElapsedMilliseconds - start}");
                CertificateValidator certValidator = validator.Update();
                TestContext.Out
                    .WriteLine($"InitValidator: {stopWatch.ElapsedMilliseconds - start}");
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await certValidator.ValidateAsync(CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false);
                }
                TestContext.Out.WriteLine($"Validation: {stopWatch.ElapsedMilliseconds - start}");
            }
            TestContext.Out.WriteLine($"Total: {stopWatch.ElapsedMilliseconds}");
        }

        /// <summary>
        /// Verify signed app certs validate. All but one in trusted.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsAllButOneTrustedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store =
                        i != v
                            ? validator.TrustedStore
                            : validator.IssuerStore;
                    await store.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    await store.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await certValidator.ValidateAsync(CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verify app certs with incomplete chain throw.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsIncompleteChainAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateChainIncomplete,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify app certs do not validate with invalid chain.
        /// </summary>
        [Test]
        public async Task VerifyAppChainsInvalidChainAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.TrustedStore.AddAsync(m_caDupeChain[i])
                            .ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlDupeChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateChainIncomplete,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify app certs with good and invalid chain
        /// </summary>
        [Test]
        public async Task VerifyAppChainsWithGoodAndInvalidChainAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    ICertificateStore store = i == v
                        ? validator.TrustedStore
                        : validator.IssuerStore;
                    await store.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    await store.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
                    await store.AddAsync(m_caDupeChain[i]).ConfigureAwait(false);
                    await store.AddCRLAsync(m_crlDupeChain[i]).ConfigureAwait(false);
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await certValidator.ValidateAsync(CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert is revoked with CRL in trusted store
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in issuer store
        /// </summary>
        [Test]
        public async Task VerifyRevokedIssuerStoreAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted app cert is revoked with CRL in issuer store
        /// </summary>
        [Test]
        public async Task VerifyRevokedIssuerStoreTrustedAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await validator
                        .TrustedStore.AddAsync(
                            CertificateFactory.Create(app.Certificate))
                        .ConfigureAwait(false);
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreNotTrustedAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    if (i == v)
                    {
                        await validator.TrustedStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted cert is revoked with CRL in trusted store.
        /// </summary>
        [Test]
        public async Task VerifyRevokedTrustedStoreTrustedAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    if (i == v)
                    {
                        await validator.TrustedStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await validator
                        .TrustedStore.AddAsync(
                            CertificateFactory.Create(app.Certificate))
                        .ConfigureAwait(false);
                }
                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Verify trusted app certs with issuer chain.
        /// </summary>
        [Test]
        public async Task VerifyIssuerChainIncompleteTrustedAppCertsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using var validator = TemporaryCertValidator.Create(telemetry);
            // issuer chain
            for (int i = 0; i < kCaChainCount; i++)
            {
                await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                await validator.IssuerStore.AddCRLAsync(m_crlChain[i]).ConfigureAwait(false);
            }

            // all app certs are trusted
            foreach (ApplicationTestData app in m_goodApplicationTestSet)
            {
                await validator
                    .TrustedStore.AddAsync(CertificateFactory.Create(app.Certificate))
                    .ConfigureAwait(false);
            }

            CertificateValidator certValidator = validator.Update();
            foreach (ApplicationTestData app in m_goodApplicationTestSet)
            {
                await certValidator.ValidateAsync(CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verify trusted app certs with incomplete issuer chain.
        /// </summary>
        [Test]
        public async Task VerifyIssuerChainTrustedAppCertsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                TestContext.Out.WriteLine("Chain cert {0} not in issuer store.", v);
                using var validator = TemporaryCertValidator.Create(telemetry);
                // issuer chain
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }

                // all app certs are trusted
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    await validator
                        .TrustedStore.AddAsync(
                            CertificateFactory.Create(app.Certificate))
                        .ConfigureAwait(false);
                }

                CertificateValidator certValidator = validator.Update();
                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateChainIncomplete,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
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
            foreach (X509Certificate2 appCert in m_appSelfSignedCerts)
            {
                byte[] pemDataBlob = PEMWriter.ExportPrivateKeyAsPEM(appCert);
                string pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    CertificateFactory.Create(appCert.RawData),
                    pemDataBlob);
                // note: password is ignored
                X509Certificate2 newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    CertificateFactory.Create(appCert.RawData),
                    pemDataBlob,
                    "password".ToCharArray());
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
            foreach (X509Certificate2 appCert in m_appSelfSignedCerts)
            {
                byte[] pemDataBlob = PEMWriter.ExportCertificateAsPEM(appCert);
                string pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
#if NET5_0_OR_GREATER
                ArgumentException exception = NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                    CertificateFactory.CreateCertificateWithPEMPrivateKey(
                        new X509Certificate2(appCert),
                        pemDataBlob));
#endif
            }
        }

#if NET5_0_OR_GREATER
        /// <summary>
        /// Verify the PEM Writer, no password.
        /// </summary>
        [Test]
        [Order(530)]
        public void VerifyPemWriterRSAPrivateKeys()
        {
            // all app certs are trusted
            foreach (X509Certificate2 appCert in m_appSelfSignedCerts)
            {
                byte[] pemDataBlob = PEMWriter.ExportRSAPrivateKeyAsPEM(appCert);
                string pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                X509Certificate2 cert = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    CertificateFactory.Create(appCert.RawData),
                    pemDataBlob);
                Assert.NotNull(cert);
                // note: password is ignored
                X509Certificate2 newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    CertificateFactory.Create(appCert.RawData),
                    pemDataBlob,
                    "password");
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
            foreach (X509Certificate2 appCert in m_appSelfSignedCerts)
            {
                string password = Uuid.NewUuid().ToString()[..8];
                TestContext.Out.WriteLine("Password: {0}", password);
                byte[] pemDataBlob = PEMWriter.ExportPrivateKeyAsPEM(appCert, password);
                string pemString = Encoding.UTF8.GetString(pemDataBlob);
                TestContext.Out.WriteLine(pemString);
                X509Certificate2 newCert = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                    new X509Certificate2(appCert),
                    pemDataBlob,
                    password);
                CryptographicException exception = NUnit.Framework.Assert
                    .Throws<CryptographicException>(() =>
                        _ = CertificateFactory.CreateCertificateWithPEMPrivateKey(
                            new X509Certificate2(appCert),
                            pemDataBlob));
                X509Utils.VerifyRSAKeyPair(newCert, newCert, true);
            }
        }
#endif

        /// <summary>
        /// Verify self signed certs, not yet valid.
        /// </summary>
        [Theory]
        public async Task VerifyNotBeforeInvalidAsync(bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string applicationName = "App Test Cert";
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, applicationName, null, null)
                .SetNotBefore(DateTime.Today.AddDays(14))
                .CreateForRSA();
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName("CN=" + applicationName + " ,O=OPC Foundation", cert.Subject));
            var validator = TemporaryCertValidator.Create(telemetry);
            if (!trusted)
            {
                await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
            }
            else
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            if (!trusted)
            {
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
                // check the chained service result
                ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
                Assert.NotNull(innerResult);
                Assert.AreEqual(
                    StatusCodes.BadCertificateTimeInvalid,
                    innerResult.StatusCode,
                    innerResult.LocalizedText.Text);
            }
            else
            {
                Assert.AreEqual(
                    StatusCodes.BadCertificateTimeInvalid,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify self signed certs, not after invalid.
        /// </summary>
        [Theory]
        public async Task VerifyNotAfterInvalidAsync(bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string applicationName = "App Test Cert";
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, applicationName, null, null)
                .SetNotBefore(new DateTime(2010, 1, 1))
                .SetLifeTime(12)
                .CreateForRSA();
            TestContext.Out.WriteLine($"{cert}:");
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName("CN=" + applicationName + " ,O=OPC Foundation", cert.Subject));
            var validator = TemporaryCertValidator.Create(telemetry);
            if (!trusted)
            {
                await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
            }
            else
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            if (!trusted)
            {
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
            else
            {
                Assert.AreEqual(
                    StatusCodes.BadCertificateTimeInvalid,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
        }

        /// <summary>
        /// Verify signed cert, not after invalid and chain missing.
        /// </summary>
        [Theory]
        public async Task VerifySignedNotAfterInvalidAsync(bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string subject = "CN=Signed App Test Cert, O=OPC Foundation";
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, null, subject, null)
                .SetNotBefore(DateTime.Today.AddDays(30))
                .SetLifeTime(12)
                .SetIssuer(m_caChain[0])
                .CreateForRSA();
            TestContext.Out.WriteLine($"{cert}:");
            Assert.NotNull(cert);
            cert = new X509Certificate2(cert);
            Assert.NotNull(cert);
            Assert.True(X509Utils.CompareDistinguishedName(subject, cert.Subject));
            var validator = TemporaryCertValidator.Create(telemetry);
            if (!trusted)
            {
                await validator.IssuerStore.AddAsync(cert).ConfigureAwait(false);
            }
            else
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(
                StatusCodes.BadCertificateChainIncomplete,
                serviceResultException.StatusCode,
                serviceResultException.Message);
            // approver tries to suppress error which is not suppressable
            var approver = new CertValidationApprover(
                [StatusCodes.BadCertificateTimeInvalid, StatusCodes.BadCertificateChainIncomplete]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            Assert.AreEqual(
                StatusCodes.BadCertificateChainIncomplete,
                serviceResultException.StatusCode,
                serviceResultException.Message);
            certValidator.CertificateValidation -= approver.OnCertificateValidation;
        }

        /// <summary>
        /// Validate various null parameter return null exception.
        /// </summary>
        [Test]
        public void TestNullParameters()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var validator = TemporaryCertValidator.Create(telemetry);
            CertificateValidator certValidator = validator.Update();
            NUnit.Framework.Assert.Throws<ArgumentNullException>(() =>
                certValidator.UpdateAsync((SecurityConfiguration)null).GetAwaiter().GetResult());
            NUnit.Framework.Assert.Throws<ArgumentNullException>(() =>
                certValidator.UpdateAsync((ApplicationConfiguration)null).GetAwaiter().GetResult());
        }

        /// <summary>
        /// Validate the event handlers can be used.
        /// </summary>
        [Test]
        public void TestEventHandler()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var validator = TemporaryCertValidator.Create(telemetry);
            CertificateValidator certValidator = validator.Update();
            certValidator.CertificateUpdate += OnCertificateUpdate;
            certValidator.CertificateValidation += OnCertificateValidation;
            certValidator.CertificateUpdate -= OnCertificateUpdate;
            certValidator.CertificateValidation -= OnCertificateValidation;
        }

        /// <summary>
        /// Validate SHA1 signed certificates cause a policy check failed.
        /// </summary>
        [Theory]
        public async Task TestSHA1RejectedAsync(bool trusted, bool rejectSHA1)
        {
#if NET472_OR_GREATER || NET5_0_OR_GREATER
            NUnit.Framework.Assert
                .Ignore("To create SHA1 certificates is unsupported on this .NET version");
#endif
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, null, "CN=SHA1 signed, O=OPC Foundation", null)
                .SetHashAlgorithm(HashAlgorithmName.SHA1)
                .CreateForRSA();
            var validator = TemporaryCertValidator.Create(telemetry);
            if (trusted)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            certValidator.RejectSHA1SignedCertificates = rejectSHA1;
            if (rejectSHA1)
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificatePolicyCheckFailed,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
                Assert.NotNull(serviceResultException.InnerResult);
                ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
                if (!trusted)
                {
                    Assert.NotNull(innerResult);
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        innerResult.StatusCode,
                        innerResult.LocalizedText.Text);
                }
                else
                {
                    Assert.Null(innerResult);
                }
            }
            else if (trusted)
            {
                await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
                Assert.NotNull(serviceResultException.InnerResult);
            }
        }

        /// <summary>
        /// Validate invalid key usage flags cause use not allowed.
        /// </summary>
        [Theory]
        public async Task TestInvalidKeyUsageAsync(bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string subject = "CN=Invalid Signature Cert, O=OPC Foundation";
            // self signed but key usage is not valid for app cert
            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, null, subject, null)
                .SetCAConstraint(0)
                .CreateForRSA();

            Assert.True(X509Utils.VerifySelfSigned(cert));
            var validator = TemporaryCertValidator.Create(telemetry);
            if (trusted)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(
                StatusCodes.BadCertificateUseNotAllowed,
                serviceResultException.StatusCode,
                serviceResultException.Message);
            Assert.NotNull(serviceResultException.InnerResult);
            ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
            if (trusted)
            {
                Assert.Null(innerResult);
            }
            else
            {
                Assert.NotNull(innerResult);
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    (StatusCode)innerResult.StatusCode.Code,
                    innerResult.LocalizedText.Text);
            }
        }

        /// <summary>
        /// Validate certificates with invalid signature are returned as invalid.
        /// </summary>
        [Theory]
        public async Task TestInvalidSignatureAsync(bool ca, bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string subject = "CN=Invalid Signature Cert, O=OPC Foundation";
            X509Certificate2 certBase = CertificateFactory.CreateCertificate(
                null,
                null,
                subject,
                null).CreateForRSA();

            var generator = X509SignatureGenerator.CreateForRSA(
                m_caChain[0].GetRSAPrivateKey(),
                RSASignaturePadding.Pkcs1);
            // generate a self signed cert with invalid signature
            ICertificateBuilder builder = CertificateFactory.CreateCertificate(
                null,
                null,
                subject,
                null);
            if (ca)
            {
                // set the CA flag changes the key usage to sign only
                builder.SetCAConstraint(0);
            }
            X509Certificate2 cert = builder
                .SetIssuer(certBase)
                .SetRSAPublicKey(certBase.GetRSAPublicKey())
                .CreateForRSA(generator);

            Assert.False(X509Utils.VerifySelfSigned(cert));
            var validator = TemporaryCertValidator.Create(telemetry);
            if (trusted)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            var approver = new CertValidationApprover([StatusCodes.BadCertificateUntrusted]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            ServiceResult innerResult;
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            if (ca)
            {
                // The CA version fails for the key usage flags
                Assert.AreEqual(
                    StatusCodes.BadCertificateUseNotAllowed,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
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
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    innerResult.StatusCode,
                    innerResult.LocalizedText.Text);
                innerResult = innerResult.InnerResult;
            }
            // However, all cert versions got an invalid signature, must fail...
            Assert.NotNull(innerResult);
            Assert.AreEqual(
                StatusCodes.BadCertificateInvalid,
                innerResult.StatusCode,
                innerResult.LocalizedText.Text);
            Assert.AreEqual(0, approver.Count);
        }

        /// <summary>
        /// Test if a key below min length is detected.
        /// </summary>
        [Theory]
        [NonParallelizable]
        public async Task TestMinimumKeyRejectedAsync(bool trusted)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            X509Certificate2 cert = CertificateFactory
                .CreateCertificate(null, null, "CN=1k Key", null)
                .SetRSAKeySize(1024)
                .CreateForRSA();
            var validator = TemporaryCertValidator.Create(telemetry);
            if (trusted)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            ServiceResultException serviceResultException = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
            Assert.AreEqual(
                StatusCodes.BadCertificatePolicyCheckFailed,
                serviceResultException.StatusCode,
                serviceResultException.Message);
            Assert.NotNull(serviceResultException.InnerResult);
            ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
            if (!trusted)
            {
                Assert.NotNull(innerResult);
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    innerResult.StatusCode,
                    innerResult.LocalizedText.Text);
            }
            else
            {
                Assert.Null(innerResult);
            }

            // approve suppression of smaller key
            var approver = new CertValidationApprover(
                [StatusCodes.BadCertificatePolicyCheckFailed]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            if (trusted)
            {
                await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                serviceResultException = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
            certValidator.CertificateValidation -= approver.OnCertificateValidation;
        }

        /// <summary>
        /// Test that Hash sizes lower than public key sizes of certificates are not valid
        /// </summary>
        [Theory]
        public async Task ECDsaHashSizeLowerThanPublicKeySizeAsync(ECCurveHashPair ecCurveHashPair)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            if (ecCurveHashPair.HashSize > 0)
            {
                // default signing cert with custom key
                X509Certificate2 cert = CertificateBuilder
                    .Create("CN=LowHash")
                    .SetHashAlgorithm(HashAlgorithmName.SHA512)
                    .SetECCurve(ecCurveHashPair.Curve)
                    .CreateForECDsa();

                var validator = TemporaryCertValidator.Create(telemetry);
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
                CertificateValidator certValidator = validator.Update();

                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificatePolicyCheckFailed,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
                Assert.NotNull(serviceResultException.InnerResult);
                ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
                Assert.Null(innerResult);
            }
        }

        /// <summary>
        /// Test auto accept.
        /// </summary>
        [Theory]
        [NonParallelizable]
        public async Task TestAutoAcceptAsync(bool trusted, bool autoAccept)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            X509Certificate2 cert = CertificateFactory.CreateCertificate(
                null,
                null,
                "CN=Test",
                null).CreateForRSA();
            var validator = TemporaryCertValidator.Create(telemetry);
            if (trusted)
            {
                await validator.TrustedStore.AddAsync(cert).ConfigureAwait(false);
            }
            CertificateValidator certValidator = validator.Update();
            certValidator.AutoAcceptUntrustedCertificates = autoAccept;
            if (autoAccept || trusted)
            {
                await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
                Assert.NotNull(serviceResultException.InnerResult);
                ServiceResult innerResult = serviceResultException.InnerResult.InnerResult;
                Assert.Null(innerResult);
            }

            // override the autoaccept flag, always approve
            certValidator = validator.Update();
            certValidator.AutoAcceptUntrustedCertificates = autoAccept;
            CertValidationApprover approver = new([StatusCodes.BadCertificateUntrusted]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false);
            certValidator.CertificateValidation -= approver.OnCertificateValidation;

            // override the autoaccept flag, but do not approve
            certValidator = validator.Update();
            certValidator.AutoAcceptUntrustedCertificates = autoAccept;
            approver = new CertValidationApprover([]);
            certValidator.CertificateValidation += approver.OnCertificateValidation;
            if (trusted)
            {
                await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException serviceResultException = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(
                        async () =>
                            await certValidator.ValidateAsync(cert, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual(
                    StatusCodes.BadCertificateUntrusted,
                    serviceResultException.StatusCode,
                    serviceResultException.Message);
            }
            certValidator.CertificateValidation -= approver.OnCertificateValidation;
        }

        /// <summary>
        /// Verify the certificate validator can be assigned.
        /// </summary>
        [Test]
        public void CertificateValidatorAssignableFromAppConfig()
        {
            NUnit.Framework.Assert.DoesNotThrow(() =>
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var appConfig = new ApplicationConfiguration(telemetry)
                {
                    CertificateValidator = new CertificateValidator(telemetry)
                };
                Assert.NotNull(appConfig);
                Assert.NotNull(appConfig.CertificateValidator);
            });
        }

        /// <summary>
        /// Certificate chain with revoced certificate,
        /// with CA CRLs missing and revocation status enforced.
        /// </summary>
        [Theory]
        public async Task VerifySomeMissingCRLRevokedTrustedStoreAppChainsAsync(
            bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert is revoked with CRL in trusted store
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                // Discussion:
                // one CA (root or intermediate) is added to the trust store, all others to the issuer store
                // for the one in the trust store, a CRL is added revoking the certificates signed by the CA
                // All other CRLs are missing.

                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlRevokedChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));

                    Assert.AreEqual(
                        v == kCaChainCount - 1
                            ? StatusCodes.BadCertificateRevoked
                            : StatusCodes.BadCertificateIssuerRevoked,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// certificate chain with all CRLs missing and revocation enforced.
        /// </summary>
        [Test]
        public async Task VerifyAllMissingCRLRevokedTrustedStoreAppChainsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert is revoked with CRL in trusted store

            for (int v = 0; v < kCaChainCount; v++)
            {
                // Discussion:
                // no crl is placed into any store, but revocation list is required.
                // the validator (correctly) complains about a missing CRL
                // it does not detect the missing CA CRLs
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = true;

                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));

                    Assert.IsTrue(
                        StatusCodes.BadCertificateRevocationUnknown == serviceResultException
                            .StatusCode,
                        serviceResultException.Message);

                    // ensure the missing issuer certificate is detected, also.
                    int isPresentCertificateIssuerRevocationUnknown = 0;
                    ServiceResult inner = serviceResultException.InnerResult;
                    while (inner != null)
                    {
                        if (inner.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown)
                        {
                            isPresentCertificateIssuerRevocationUnknown++;
                        }
                        inner = inner.InnerResult;
                    }
                    Assert.IsTrue(isPresentCertificateIssuerRevocationUnknown == kCaChainCount - 1);
                }
            }
        }

        /// <summary>
        /// certificate chains with missing CRL for trust store and revocation list enforced.
        ///  No revoked certificate.
        /// </summary>
        [Theory]
        public async Task VerifySomeMissingCRLTrustedStoreAppChainsAsync(
            bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            //Discussion:
            // v == kCaChainCount - 1: empty CRL is placed into the issuer stores, no CRL in trust store:
            // -> validator complains about missing revocation list for CA which signed the application certificate (ok)

            // v != kCaChainCount - 1: in the trust store the CRL for one of the issuer certificates is missing.
            // all other CRLs are present and empty
            // -> validator complains correctly about missing issuer revocation list

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i == v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    if (rejectUnknownRevocationStatus)
                    {
                        ServiceResultException serviceResultException =
                            NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                                await certValidator.ValidateAsync(
                                    CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));

                        Assert.AreEqual(
                            v == kCaChainCount - 1
                                ? StatusCodes.BadCertificateRevocationUnknown
                                : StatusCodes.BadCertificateIssuerRevocationUnknown,
                            serviceResultException.StatusCode,
                            serviceResultException.Message);
                    }
                    else
                    {
                        await certValidator.ValidateAsync(
                            CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Verify app certs with incomplete chain throw.
        /// </summary>
        [Theory]
        public async Task VerifyMissingCRLANDAppChainsIncompleteChainAsync(
            bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateChainIncomplete,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                    // no need to check for inner exceptions, since an incomplete chain error cannot be suppressed.
                }
            }
        }

        /// <summary>
        /// Comparison test for the next test:
        /// verify not yet valid app certs in a chain with
        /// CRL.
        /// </summary>
        [Theory]
        public async Task VerifyExistingCRLAppChainsExpiredCertificatesAsync(
            bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v || kCaChainCount == 1)
                    {
                        await validator
                            .TrustedStore.AddAsync(
                                CertificateFactory.Create(m_caChain[i].RawData))
                            .ConfigureAwait(false);
                        await validator.TrustedStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await validator
                            .IssuerStore.AddAsync(
                                CertificateFactory.Create(m_caChain[i].RawData))
                            .ConfigureAwait(false);
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_notYetValidCertsApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateTimeInvalid,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        /// <summary>
        /// Not yet valid application certificates in a certificate chain
        /// CRLs are missing and Revocation status check is enforced.
        /// Should report BadCertificateTimeInvalid and (inner result)
        /// BadCertificateRevocationUnknown or BadCertificateIssuerRevocationUnknown.
        /// Currently misbehaves: chain
        /// </summary>
        [Theory]
        public async Task VerifyMissingCRLAppChainsExpiredCertificatesAsync(
            bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    if (i != v || kCaChainCount == 1)
                    {
                        await validator.TrustedStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                    else
                    {
                        await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_notYetValidCertsApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateTimeInvalid,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);

                    // BadCertificateTimeInvalid can be suppressed. Ensure the other issues are caught, as well:
                    int isPresentCertificateIssuerRevocationUnknown = 0;
                    int isPresentCertificateRevocationUnknown = 0;
                    ServiceResult inner = serviceResultException.InnerResult;
                    while (inner != null)
                    {
                        if (inner.StatusCode == StatusCodes.BadCertificateIssuerRevocationUnknown)
                        {
                            isPresentCertificateIssuerRevocationUnknown++;
                        }
                        else if (inner.StatusCode == StatusCodes.BadCertificateRevocationUnknown)
                        {
                            isPresentCertificateRevocationUnknown++;
                        }
                        inner = inner.InnerResult;
                    }
                    if (rejectUnknownRevocationStatus)
                    {
                        Assert.LessOrEqual(
                            kCaChainCount - 1,
                            isPresentCertificateIssuerRevocationUnknown);
                        Assert.AreEqual(1, isPresentCertificateRevocationUnknown);
                    }
                    else
                    {
                        Assert.AreEqual(0, isPresentCertificateIssuerRevocationUnknown);
                        Assert.AreEqual(0, isPresentCertificateRevocationUnknown);
                    }
                }
            }
        }

        /// <summary>
        /// No certificate is trusted, and one CRL is missing
        /// </summary>
        [Theory]
        public async Task VerifyMissingCRLNoTrustAsync(bool rejectUnknownRevocationStatus)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // verify cert with issuer chain
            for (int v = 0; v < kCaChainCount; v++)
            {
                using var validator = TemporaryCertValidator.Create(telemetry);
                for (int i = 0; i < kCaChainCount; i++)
                {
                    await validator.IssuerStore.AddAsync(m_caChain[i]).ConfigureAwait(false);
                    if (i != v)
                    {
                        await validator.IssuerStore.AddCRLAsync(m_crlChain[i])
                            .ConfigureAwait(false);
                    }
                }
                CertificateValidator certValidator = validator.Update();

                // ****** setting under test ******
                certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;

                foreach (ApplicationTestData app in m_goodApplicationTestSet)
                {
                    ServiceResultException serviceResultException =
                        NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                            await certValidator.ValidateAsync(
                                CertificateFactory.Create(app.Certificate), CancellationToken.None).ConfigureAwait(false));
                    Assert.AreEqual(
                        StatusCodes.BadCertificateUntrusted,
                        serviceResultException.StatusCode,
                        serviceResultException.Message);
                }
            }
        }

        private void OnCertificateUpdate(object sender, CertificateUpdateEventArgs e)
        {
        }

        private void OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
        }

        private const int kCaChainCount = 3;
        private const int kGoodApplicationsTestCount = 3;
        private IList<ApplicationTestData> m_goodApplicationTestSet;
        private IList<ApplicationTestData> m_notYetValidCertsApplicationTestSet;
        private X509Certificate2[] m_caChain;
        private X509Certificate2[] m_caDupeChain;
        private X509CRL[] m_crlChain;
        private X509CRL[] m_crlDupeChain;
        private X509CRL[] m_crlRevokedChain;
        private X509Certificate2Collection m_appCerts;
        private X509Certificate2Collection m_appSelfSignedCerts;
        private X509Certificate2Collection m_notYetValidAppCerts;
    }

    /// <summary>
    /// Helper to approve suppressable errors in test cases.
    /// To catch cases where unsuppressable errors should not
    /// call for approvals.
    /// </summary>
    internal sealed class CertValidationApprover
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
