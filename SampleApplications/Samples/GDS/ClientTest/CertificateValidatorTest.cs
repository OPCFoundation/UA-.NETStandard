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

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds.Test;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NUnit.Opc.Ua.Gds.Test
{
    /// <summary>
    /// Tests for the CertificateValidator class.
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
            // set max RSA key size and SHA-2 hash size
            ushort keySize = 4096;
            ushort hashSize = 512;

            // pki directory root for test runs. 
            _pkiRoot = "%LocalApplicationData%/OPC/CertValidatorTest/" + ((DateTime.UtcNow.Ticks / 10000) % 3600000).ToString() + "/";
            _issuerStore = new DirectoryCertificateStore();
            _issuerStore.Open(_pkiRoot + "issuer");
            _trustedStore = new DirectoryCertificateStore();
            _trustedStore.Open(_pkiRoot + "trusted");

            // good applications test set
            var appTestDataGenerator = new ApplicationTestDataGenerator(1);
            _goodApplicationTestSet = appTestDataGenerator.ApplicationTestSet(goodApplicationsTestCount, false);
            _invalidApplicationTestSet = appTestDataGenerator.ApplicationTestSet(invalidApplicationsTestCount, true);

            // create all certs and CRL
            _caChain = new X509Certificate2[caChainCount];
            _caDupeChain = new X509Certificate2[caChainCount];
            _caAllSameIssuerChain = new X509Certificate2[caChainCount];
            _crlChain = new X509CRL[caChainCount];
            _crlDupeChain = new X509CRL[caChainCount];
            _crlRevokedChain = new X509CRL[caChainCount];
            _appCerts = new X509Certificate2Collection();
            _appSelfSignedCerts = new X509Certificate2Collection();

            DateTime rootCABaseTime = DateTime.UtcNow;
            rootCABaseTime = new DateTime(rootCABaseTime.Year - 1, 1, 1);
            var rootCert = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=Root CA Test Cert",
                null, keySize, rootCABaseTime, 25 * 12, hashSize, true,
                pathLengthConstraint: -1);
            _caChain[0] = rootCert;
            _crlChain[0] = CertificateFactory.RevokeCertificate(rootCert, null, null);
            _caDupeChain[0] = CertificateFactory.CreateCertificate(
                null, null, null,
                null, null, "CN=Root CA Test Cert",
                null, keySize, rootCABaseTime, 25 * 12, hashSize, true,
                pathLengthConstraint: -1);
            _crlDupeChain[0] = CertificateFactory.RevokeCertificate(_caDupeChain[0], null, null);
            _crlRevokedChain[0] = null;

            var signingCert = rootCert;
            DateTime subCABaseTime = DateTime.UtcNow;
            subCABaseTime = new DateTime(subCABaseTime.Year, subCABaseTime.Month, subCABaseTime.Day);
            for (int i = 1; i < caChainCount; i++)
            {
                if (keySize > 2048) { keySize -= 1024; }
                if (hashSize > 256) { hashSize -= 128; }
                var subject = $"CN=Sub CA {i} Test Cert";
                var subCACert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    null, null, subject,
                    null, keySize, subCABaseTime, 5 * 12, hashSize, true,
                    signingCert, pathLengthConstraint: caChainCount - 1 - i);
                _caChain[i] = subCACert;
                _crlChain[i] = CertificateFactory.RevokeCertificate(subCACert, null, null, subCABaseTime, subCABaseTime + TimeSpan.FromDays(10));
                var subCADupeCert = CertificateFactory.CreateCertificate(
                    null, null, null,
                    null, null, subject,
                    null, keySize, subCABaseTime, 5 * 12, hashSize, true,
                    signingCert, pathLengthConstraint: caChainCount - 1 - i);
                _caDupeChain[i] = subCADupeCert;
                _crlDupeChain[i] = CertificateFactory.RevokeCertificate(subCADupeCert, null, null, subCABaseTime, subCABaseTime + TimeSpan.FromDays(10));
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
            Directory.Delete(Utils.ReplaceSpecialFolderNames(_pkiRoot), true);
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
            foreach (var cert in _appSelfSignedCerts)
            {
                Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
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
            for (int i = 0; i < caChainCount; i++)
            {
                if (i == caChainCount / 2)
                {
                    await _trustedStore.Add(_caChain[i]);
                    _trustedStore.AddCRL(_crlChain[i]);
                }
                else
                {
                    await _issuerStore.Add(_caChain[i]);
                    _issuerStore.AddCRL(_crlChain[i]);
                }
            }

            var certValidator = InitValidatorWithStores();
            foreach (var cert in _appSelfSignedCerts)
            {
                Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
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
                foreach (var cert in _appSelfSignedCerts)
                {
                    await _issuerStore.Add(cert);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var cert in _appSelfSignedCerts)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(cert)); }, Throws.Exception);
                }

                // add all certs to trusted store
                CleanupValidatorAndStores();
                foreach (var cert in _appSelfSignedCerts)
                {
                    await _trustedStore.Add(cert);
                }
                certValidator = InitValidatorWithStores();
                foreach (var cert in _appSelfSignedCerts)
                {
                    certValidator.Validate(new X509Certificate2(cert));
                }

                // add all certs to trusted and issuer store
                CleanupValidatorAndStores();
                foreach (var cert in _appSelfSignedCerts)
                {
                    await _trustedStore.Add(cert);
                    await _issuerStore.Add(cert);
                }
                certValidator = InitValidatorWithStores();
                foreach (var cert in _appSelfSignedCerts)
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    ICertificateStore store;
                    if (i == v)
                    {
                        store = _trustedStore;
                    }
                    else
                    {
                        store = _issuerStore;
                    }
                    await store.Add(_caChain[i]);
                    store.AddCRL(_crlChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    ICertificateStore store;
                    if (i != v)
                    {
                        store = _trustedStore;
                    }
                    else
                    {
                        store = _issuerStore;
                    }
                    await store.Add(_caChain[i]);
                    store.AddCRL(_crlChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i != v)
                    {
                        await _trustedStore.Add(_caChain[i]);
                        _trustedStore.AddCRL(_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i != v)
                    {
                        await _trustedStore.Add(_caChain[i]);
                        _trustedStore.AddCRL(_crlChain[i]);
                    }
                    else
                    {
                        await _trustedStore.Add(_caDupeChain[i]);
                        _trustedStore.AddCRL(_crlDupeChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    ICertificateStore store;
                    if (i == v)
                    {
                        store = _trustedStore;
                    }
                    else
                    {
                        store = _issuerStore;
                    }
                    await store.Add(_caChain[i]);
                    store.AddCRL(_crlChain[i]);
                    await store.Add(_caDupeChain[i]);
                    store.AddCRL(_crlDupeChain[i]);
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i == v)
                    {
                        await _trustedStore.Add(_caChain[i]);
                        _trustedStore.AddCRL(_crlRevokedChain[i]);
                    }
                    else
                    {
                        await _issuerStore.Add(_caChain[i]);
                        _issuerStore.AddCRL(_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
                }
            }
        }

        /// <summary>
        /// // verify cert is revoked with CRL in issuer store
        /// </summary>
        [Test, Order(310)]
        public async Task VerifyRevokedIssuerStoreAppChains()
        {
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i == v)
                    {
                        await _issuerStore.Add(_caChain[i]);
                        _issuerStore.AddCRL(_crlRevokedChain[i]);
                    }
                    else
                    {
                        await _trustedStore.Add(_caChain[i]);
                        _trustedStore.AddCRL(_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
                }
            }
        }
        /// <summary>
        /// Verify trusted app cert is revoked with CRL in issuer store
        /// </summary>
        [Test, Order(320)]
        public async Task VerifyRevokedIssuerStoreTrustedAppChains()
        {
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i == v)
                    {
                        await _issuerStore.Add(_caChain[i]);
                        _issuerStore.AddCRL(_crlRevokedChain[i]);
                    }
                    else
                    {
                        await _issuerStore.Add(_caChain[i]);
                        _issuerStore.AddCRL(_crlChain[i]);
                    }
                }
                foreach (var app in _goodApplicationTestSet)
                {
                    await _trustedStore.Add(new X509Certificate2(app.Certificate));
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
                }
            }
        }

        /// <summary>
        /// Verify cert is revoked with CRL in trusted store
        /// </summary>
        [Test, Order(330)]
        public async Task VerifyRevokedTrustedStoreNotTrustedAppChains()
        {
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    await _trustedStore.Add(_caChain[i]);
                    if (i == v)
                    {
                        _trustedStore.AddCRL(_crlRevokedChain[i]);
                    }
                    else
                    {
                        _trustedStore.AddCRL(_crlChain[i]);
                    }
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
                }
            }
        }

        /// <summary>
        /// Verify trusted cert is revoked with CRL in trusted store.
        /// </summary>
        [Test, Order(340)]
        public async Task VerifyRevokedTrustedStoreTrustedAppChains()
        {
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                for (int i = 0; i < caChainCount; i++)
                {
                    await _trustedStore.Add(_caChain[i]);
                    if (i == v)
                    {
                        _trustedStore.AddCRL(_crlRevokedChain[i]);
                    }
                    else
                    {
                        _trustedStore.AddCRL(_crlChain[i]);
                    }
                }
                foreach (var app in _goodApplicationTestSet)
                {
                    await _trustedStore.Add(new X509Certificate2(app.Certificate));
                }
                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
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
            for (int i = 0; i < caChainCount; i++)
            {
                await _issuerStore.Add(_caChain[i]);
                _issuerStore.AddCRL(_crlChain[i]);
            }

            // all app certs are trusted
            foreach (var app in _goodApplicationTestSet)
            {
                await _trustedStore.Add(new X509Certificate2(app.Certificate));
            }

            var certValidator = InitValidatorWithStores();
            foreach (var app in _goodApplicationTestSet)
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
            for (int v = 0; v < caChainCount; v++)
            {
                CleanupValidatorAndStores();
                // issuer chain
                for (int i = 0; i < caChainCount; i++)
                {
                    if (i != v)
                    {
                        await _issuerStore.Add(_caChain[i]);
                        _issuerStore.AddCRL(_crlChain[i]);
                    }
                }

                // all app certs are trusted
                foreach (var app in _goodApplicationTestSet)
                {
                    await _trustedStore.Add(new X509Certificate2(app.Certificate));
                }

                var certValidator = InitValidatorWithStores();
                foreach (var app in _goodApplicationTestSet)
                {
                    Assert.That(() => { certValidator.Validate(new X509Certificate2(app.Certificate)); }, Throws.Exception);
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
            foreach (var appCert in _appSelfSignedCerts)
            {
                var pemDataBlob = CertificateFactory.ExportPrivateKeyAsPEM(appCert);
                var pemString = Encoding.UTF8.GetString(pemDataBlob);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob);
                CertificateFactory.CreateCertificateWithPEMPrivateKey(new X509Certificate2(appCert), pemDataBlob, "password");
            }
        }
        #endregion
        #region Private Methods
        private CertificateValidator InitValidatorWithStores()
        {
            CertificateValidator certValidator = new CertificateValidator();
            CertificateTrustList issuerTrustList = new CertificateTrustList();
            issuerTrustList.StoreType = "Directory";
            issuerTrustList.StorePath = _issuerStore.Directory.FullName;
            CertificateTrustList trustedTrustList = new CertificateTrustList();
            trustedTrustList.StoreType = "Directory";
            trustedTrustList.StorePath = _trustedStore.Directory.FullName;
            certValidator.Update(issuerTrustList, trustedTrustList, null);
            return certValidator;
        }

        private void CleanupValidatorAndStores()
        {
            TestUtils.CleanupTrustList(_issuerStore, false);
            TestUtils.CleanupTrustList(_trustedStore, false);
        }
        #endregion
        #region Private Fields
        private const int caChainCount = 4;
        private const int goodApplicationsTestCount = 10;
        private const int invalidApplicationsTestCount = 10;
        private string _pkiRoot;
        private DirectoryCertificateStore _issuerStore;
        private DirectoryCertificateStore _trustedStore;
        private IList<ApplicationTestData> _goodApplicationTestSet;
        private IList<ApplicationTestData> _invalidApplicationTestSet;
        private X509Certificate2[] _caChain;
        private X509Certificate2[] _caDupeChain;
        private X509Certificate2[] _caAllSameIssuerChain;
        private X509CRL[] _crlChain;
        private X509CRL[] _crlDupeChain;
        private X509CRL[] _crlRevokedChain;
        private X509Certificate2Collection _appCerts;
        private X509Certificate2Collection _appSelfSignedCerts;
        #endregion
    }

}