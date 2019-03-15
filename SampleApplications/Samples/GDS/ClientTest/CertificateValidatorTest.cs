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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Test;
using Opc.Ua.Test;

namespace NUnit.Opc.Ua.Gds.Test
{


    /// <summary>
    /// 
    /// </summary>
    /// 
    [TestFixture, Category("CertificateValidator")]
    public class CertificateValidatorTest
    {
        #region Test Setup
        /// <summary>
        /// Set up a Global Discovery Server and Client instance and connect the session
        /// </summary>
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            // work around travis issue by selecting different ports on every run

            // good applications test set
            var appTestDataGenerator = new ApplicationTestDataGenerator(1);
            _goodApplicationTestSet = appTestDataGenerator.ApplicationTestSet(goodApplicationsTestCount, false);
            _invalidApplicationTestSet = appTestDataGenerator.ApplicationTestSet(invalidApplicationsTestCount, true);

            // create all certs and CRL
            _caChain = new X509Certificate2[caChainCount];
            _crlChain = new X509CRL[caChainCount];
            _crlRevokedChain = new X509CRL[caChainCount];
            _appCerts = new X509Certificate2Collection();
            _appSelfSignedCerts = new X509Certificate2Collection();

            DateTime rootCABaseTime = DateTime.UtcNow;
            rootCABaseTime = new DateTime(rootCABaseTime.Year - 1, 1, 1);
            var rootCert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=Root CA Test Cert",
                null, 4096, rootCABaseTime, 25 * 12, 512, true,
                pathLengthConstraint: -1);
            _caChain[0] = rootCert;
            _crlChain[0] = CertificateFactory.RevokeCertificate(rootCert, null, null);
            _crlRevokedChain[0] = null;

            var signingCert = rootCert;
            DateTime subCABaseTime = DateTime.UtcNow;
            subCABaseTime = new DateTime(subCABaseTime.Year, subCABaseTime.Month, subCABaseTime.Day);
            for (int i = 1; i < caChainCount; i++)
            {
                var subject = $"CN=Sub CA {i} Test Cert";
                var subCACert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    null, null, subject,
                    null, 2048, subCABaseTime, 5 * 12, 256, true,
                    signingCert, pathLengthConstraint: caChainCount - 1 - i);
                _caChain[i] = subCACert;
                _crlChain[i] = CertificateFactory.RevokeCertificate(subCACert, null, null);
                _crlRevokedChain[i] = null;
                signingCert = subCACert;
            }

            // create a CRL with a revoked Sub CA
            for (int i = 0; i < caChainCount - 1; i++)
            {
                _crlRevokedChain[i] = CertificateFactory.RevokeCertificate(
                    _caChain[i], 
                    new List<X509CRL>() { _crlChain[i] }, 
                    new X509Certificate2Collection { _caChain[i + 1] });
            }

            // create self signed app certs
            DateTime appBaseTime = DateTime.UtcNow - TimeSpan.FromDays(1);
            foreach (var app in _goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    app.ApplicationRecord.ApplicationUri,
                    app.ApplicationRecord.ApplicationNames[0].Key,
                    subject,
                    app.DomainNames,
                    2048, appBaseTime, 2 * 12,
                    256);
                _appSelfSignedCerts.Add(appCert);
            }

            // create signed app certs
            foreach (var app in _goodApplicationTestSet)
            {
                var subject = app.Subject;
                var appCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    app.ApplicationRecord.ApplicationUri,
                    app.ApplicationRecord.ApplicationNames[0].Key, 
                    subject,
                    app.DomainNames, 
                    2048, appBaseTime, 2 * 12, 
                    256, false, signingCert);
                app.Certificate = appCert.RawData;
                _appCerts.Add(appCert);
            }

            // create a CRL with all apps revoked
            _crlRevokedChain[caChainCount - 1] = CertificateFactory.RevokeCertificate(
                    _caChain[caChainCount - 1],
                    new List<X509CRL>() { _crlChain[caChainCount - 1] },
                    _appCerts);

        }

        /// <summary>
        /// Tear down the Global Discovery Server and disconnect the Client
        /// </summary>
        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            Thread.Sleep(1000);
        }

        [TearDown]
        protected void TearDown()
        {
        }
        #endregion
        #region Test Methods
        /// <summary>
        /// Verify self signed app certs
        /// </summary>
        [Test, Order(10)]
        public async Task VerifySelfSignedAppCerts()
        {
            // verify cert with issuer chain
            {
                CertificateValidator certValidator = new CertificateValidator();
                CertificateTrustList issuerStore = new CertificateTrustList();
                CertificateTrustList trustedStore = new CertificateTrustList();

                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var cert in _appSelfSignedCerts)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
                }

                // add random issuer certs
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i == caChainCount/2)
                    {
                        await trustedStore.TrustedCertificates.Add(_caChain[0]);
                        //trustedStore.TrustedCertificates.AddCRL(_crlChain[0]);
                    }
                    else
                    {
                        await issuerStore.TrustedCertificates.Add(_caChain[i]);
                        //issuerStore.TrustedCertificates.AddCRL(_crlChain[i]);
                    }
                }

                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var cert in _appSelfSignedCerts)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
                }

                // add all certs to issuer store
                foreach (var cert in _appSelfSignedCerts)
                {
                    await issuerStore.TrustedCertificates.Add(cert);
                }
                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var cert in _appSelfSignedCerts)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
                }

                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var cert in _appSelfSignedCerts)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
                }

                issuerStore = new CertificateTrustList();
                trustedStore = new CertificateTrustList();
                // add all certs to trusted store
                foreach (var cert in _appSelfSignedCerts)
                {
                    await trustedStore.TrustedCertificates.Add(cert);
                }

                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var cert in _appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }
            }
        }


        /// <summary>
        /// Verify app certs
        /// </summary>
        [Test, Order(10)]
        public async Task VerifyAppChains()
        {
            // verify cert with issuer chain
            for (int v = 0; v < caChainCount; v++)
            {
                CertificateValidator certValidator = new CertificateValidator();
                CertificateTrustList issuerStore = new CertificateTrustList();
                CertificateTrustList trustedStore = new CertificateTrustList();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i == v)
                    {
                        await trustedStore.TrustedCertificates.Add(_caChain[i]);
                        //trustedStore.TrustedCertificates.AddCRL(_crlChain[i]);
                    }
                    else
                    {
                        await issuerStore.TrustedCertificates.Add(_caChain[i]);
                        //issuerStore.TrustedCertificates.AddCRL(_crlChain[i]);
                    }
                }

                certValidator.Update(issuerStore, trustedStore, null);
                foreach (var app in _goodApplicationTestSet)
                {
                    certValidator.Validate(new X509Certificate2(app.Certificate));
                }
            }
        }

        /// <summary>
        /// Verify app certs
        /// </summary>
        [Test, Order(10)]
        public async Task VerifyTrustedAppCerts()
        {
            // verify cert with issuer chain
            CertificateValidator certValidator = new CertificateValidator();
            CertificateTrustList issuerStore = new CertificateTrustList();
            CertificateTrustList trustedStore = new CertificateTrustList();
            for (int i = 0; i < caChainCount; i++)
            {
                await issuerStore.TrustedCertificates.Add(_caChain[i]);
                //issuerStore.TrustedCertificates.AddCRL(_crlChain[i]);
            }
            foreach (var app in _goodApplicationTestSet)
            {
                await trustedStore.TrustedCertificates.Add(new X509Certificate2(app.Certificate));
            }

            certValidator.Update(issuerStore, trustedStore, null);
            foreach (var app in _goodApplicationTestSet)
            {
                certValidator.Validate(new X509Certificate2(app.Certificate));
            }
        }

        #endregion
        #region Private Methods
        #endregion
        #region Private Fields
        private const int caChainCount = 3;
        private const int goodApplicationsTestCount = 10;
        private const int invalidApplicationsTestCount = 10;
        private IList<ApplicationTestData> _goodApplicationTestSet;
        private IList<ApplicationTestData> _invalidApplicationTestSet;
        private X509Certificate2 [] _caChain;
        private X509CRL [] _crlChain;
        private X509Certificate2[] _revokedChain;
        private X509CRL [] _crlRevokedChain;
        private X509Certificate2Collection _appCerts;
        private X509Certificate2Collection _appSelfSignedCerts;
        #endregion
    }

}