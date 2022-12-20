/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Actions;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateValidator class with
    /// permutations of the AuthorityKeyIdentifier.
    /// </summary>
    [TestFixture, Category("CertificateValidator")]
    [NonParallelizable]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    class CertificateValidatorAlternate
    {
        // the root and alternate root CA
        const string kCaSubject = "CN=Root";
        const string kCaAltSubject = "CN=rOOT";

        public static readonly object[] FixtureArgs = {
            new object [] { kCaSubject },
            new object [] { kCaAltSubject }
        };

        public CertificateValidatorAlternate(string altSubject = kCaSubject)
        {
            m_altSubject = altSubject;
        }

        private string m_altSubject;
        private X509Certificate2 m_rootCert;
        private X509Certificate2 m_rootAltCert;
        private X509CRL m_rootCrl;
        private TemporaryCertValidator m_validator;
        private CertificateValidator m_certValidator;
        // A web server to host root CA and CRL
        private IWebServer m_webServer = null;
        private string m_webServerUrl;
        private string m_webServerPath;
        private string m_altCertFilename;

        #region Test Setup
        /// <summary>
        /// Set up a web server and root CAs.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            var crlName = "root.crl";
            m_webServerUrl = "http://127.0.0.1:9696/";

            var crlExtension = X509Extensions.BuildX509CRLDistributionPoints(m_webServerUrl + crlName);

            // create the root cert
            m_rootCert = CertificateFactory.CreateCertificate(kCaSubject)
                .AddExtension(crlExtension)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .CreateForRSA();

            // default empty root CRL
            m_rootCrl = CertificateFactory.RevokeCertificate(m_rootCert, null, null);

            // create cert validator for test, add trusted root cert
            m_validator = TemporaryCertValidator.Create();
            await m_validator.TrustedStore.Add(m_rootCert).ConfigureAwait(false);
            await m_validator.TrustedStore.AddCRL(m_rootCrl).ConfigureAwait(false);
            m_certValidator = m_validator.Update();

            // create a root with same serial number but modified Subject / key pair
            m_rootAltCert = CertificateFactory.CreateCertificate(m_altSubject)
                .SetLifeTime(25 * 12)
                .SetSerialNumber(m_rootCert.GetSerialNumber())
                .SetCAConstraint()
                .CreateForRSA();

            try
            {
                // temp location for hosted CA cert
                m_webServerPath = Path.GetTempPath() + Path.GetRandomFileName() + Path.DirectorySeparatorChar;

                // Start a disposable web server.
                m_webServer = CreateWebServer(m_webServerUrl, m_webServerPath, CancellationToken.None);

                // filename for the alternate certificate
                m_altCertFilename = m_rootAltCert.SerialNumber + ".der";

                // write alternate root cert to http store
                File.WriteAllBytes(m_webServerPath + m_altCertFilename, m_rootAltCert.RawData);

                // write crl to http store
                File.WriteAllBytes(m_webServerPath + crlName, m_rootCrl.RawData);
            }
            catch
            {
                Assert.Ignore("Web server could not start at: {0}", m_webServerUrl);
            }
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Utils.SilentDispose(m_rootCert);
            Utils.SilentDispose(m_rootAltCert);
            Utils.SilentDispose(m_certValidator);
            Utils.SilentDispose(m_webServer);
            Directory.Delete(m_webServerPath, true);
        }

        /// <summary>
        /// Create a Setup for a test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
        }

        /// <summary>
        /// Tear down the test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
        }
        #endregion

        /// <summary>
        /// A signed app cert that has no keyid information.
        /// </summary>
        [Test]
        public void CertificateWithoutKeyID()
        {
            // a valid app cert
            using (var appCert = CertificateFactory.CreateCertificate("CN=AppCert")
                .SetIssuer(m_rootCert)
                .CreateForRSA())
            {
                Assert.NotNull(appCert);

                m_certValidator.RejectUnknownRevocationStatus = true;
                m_certValidator.Validate(appCert);
            }
        }

        /// <summary>
        /// Certificate with combinations of optional fields in the AKI.
        /// </summary>
        [Theory]
        public void CertificateWithAuthorityKeyID(bool subjectKeyIdentifier, bool issuerName, bool serialNumber)
        {
            var certBuilder = CertificateFactory.CreateCertificate("CN=AppCert");

            // force exception if SKI is not present
            var ski = m_rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

            // create a certificate with special key info
            X509Extension authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                    (byte[])(subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null),
                    (X500DistinguishedName)(issuerName ? m_rootCert.IssuerName : null),
                    (byte[])(serialNumber ? m_rootCert.GetSerialNumber() : null));

            TestContext.Out.WriteLine("Extension: {0}", authorityKeyIdentifier.Format(true));

            certBuilder.AddExtension(authorityKeyIdentifier);

            // a valid app cert
            using (var appCert = certBuilder
                .SetIssuer(m_rootCert)
                .CreateForRSA())
            {
                m_certValidator.RejectUnknownRevocationStatus = false;
                if (!subjectKeyIdentifier && !serialNumber)
                {
                    var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(appCert));
                    TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
                }
                else
                {
                    m_certValidator.Validate(appCert);
                }
            }
        }

        /// <summary>
        /// App cert from alternate Root without KeyID.
        /// </summary>
        [Theory]
        public void AlternateRootCertificateWithoutAuthorityKeyID(bool rejectUnknownRevocationStatus)
        {
            var certBuilder = CertificateFactory.CreateCertificate("CN=AlternateAppCert");
            var caIssuerUrl = new List<string>();
            caIssuerUrl.Add(m_webServerUrl + m_altCertFilename);
            var extension = X509Extensions.BuildX509AuthorityInformationAccess(caIssuerUrl.ToArray());
            certBuilder.AddExtension(extension);
            TestContext.Out.WriteLine("Extension: {0}", extension.Format(true));

            // create app cert from alternate root
            using (var altAppCert = certBuilder.SetIssuer(m_rootAltCert).CreateForRSA())
            {
                Assert.NotNull(altAppCert);

                m_certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;
                var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(altAppCert));

                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
        }

        /// <summary>
        /// Create an app certificate from the alternate root,
        /// validate that any combination of AKI is not validated.
        /// </summary>
        [Theory]
        public void AlternateRootCertificateWithAuthorityKeyID(bool subjectKeyIdentifier, bool issuerName, bool serialNumber)
        {
            var certBuilder = CertificateFactory.CreateCertificate("CN=AltAppCert");

            // force exception if SKI is not present
            var ski = m_rootAltCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single();

            // create a certificate with special key info / no key id
            X509Extension authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                    (byte[])(subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null),
                    (X500DistinguishedName)(issuerName ? m_rootAltCert.IssuerName : null),
                    (byte[])(serialNumber ? m_rootAltCert.GetSerialNumber() : null));
            certBuilder.AddExtension(authorityKeyIdentifier);
            TestContext.Out.WriteLine("Extension: {0}", authorityKeyIdentifier.Format(true));

            var caIssuerUrl = new List<string>();
            caIssuerUrl.Add(m_webServerUrl + m_altCertFilename);
            var extension = X509Extensions.BuildX509AuthorityInformationAccess(caIssuerUrl.ToArray());
            certBuilder.AddExtension(extension);
            TestContext.Out.WriteLine("Extension: {0}", extension.Format(true));

            // create the certificate from the alternate root
            using (var altAppCert = certBuilder
                .SetIssuer(m_rootAltCert)
                .CreateForRSA())
            {
                Assert.NotNull(altAppCert);

                // should not pass!
                m_certValidator.RejectUnknownRevocationStatus = false;
                var result = Assert.Throws<ServiceResultException>(() => m_certValidator.Validate(altAppCert));
                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
        }

        /// <summary>
        /// Validate a chain with a loop is detected.
        /// </summary>
        [Test, Timeout(10000)]
        public async Task VerifyLoopChainIsDetected()
        {
            const string rootSubject = "CN=Root";
            const string subCASubject = "CN=Sub";
            const string leafSubject = "CN=Leaf";

            RSA rsa = RSA.Create();
            var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);

            using (var rootCert = CertificateFactory.CreateCertificate(rootSubject)
                .SetCAConstraint()
                .SetRSAPublicKey(rsa)
                .CreateForRSA(generator))
            using (var subCACert = CertificateFactory.CreateCertificate(subCASubject)
                .SetCAConstraint()
                .SetIssuer(rootCert)
                .CreateForRSA(generator))
            using (var rootReverseCert = CertificateFactory.CreateCertificate(rootSubject)
                .SetCAConstraint()
                .SetSerialNumber(rootCert.GetSerialNumber())
                .SetIssuer(subCACert)
                .SetRSAPublicKey(rsa)
                .CreateForRSA())
            using (var leafCert = CertificateFactory.CreateCertificate(leafSubject)
                .SetIssuer(subCACert)
                .CreateForRSA())
            {
                // validate cert chain
                using (var validator = TemporaryCertValidator.Create())
                {
                    await validator.IssuerStore.Add(rootCert).ConfigureAwait(false);
                    await validator.TrustedStore.Add(subCACert).ConfigureAwait(false);
                    var certValidator = validator.Update();
                    certValidator.Validate(leafCert);
                }

                // validate using server/client chain sent over the wire
                var collection = new X509Certificate2Collection {
                    leafCert,
                    subCACert,
                    rootReverseCert
                };
                using (var validator = TemporaryCertValidator.Create())
                {
                    var certValidator = validator.Update();
                    var result = Assert.Throws<ServiceResultException>(() => certValidator.Validate(collection));

                    TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
                }

                // validate using cert chain in issuer and trusted store
                using (var validator = TemporaryCertValidator.Create())
                {
                    await validator.IssuerStore.Add(rootReverseCert).ConfigureAwait(false);
                    await validator.TrustedStore.Add(subCACert).ConfigureAwait(false);
                    var certValidator = validator.Update();
                    var result = Assert.Throws<ServiceResultException>(() => certValidator.Validate(collection));

                    TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
                }
            }
        }

        /// <summary>
        /// Create and configure web server. 
        /// </summary>
        private static IWebServer CreateWebServer(string url, string tempPath, CancellationToken ct)
        {
            if (!Directory.Exists(tempPath))
            {
                TestContext.Out.WriteLine("Create folder: {0}", tempPath);
                Directory.CreateDirectory(tempPath);
            }

            TestContext.Out.WriteLine("Start Web server at: {0}", url);

            // Tiny web server does not respond to localhost or ::1, use 127.0.0.1
            string embedioUrl = url.Replace("localhost", "*");
            var server = new WebServer(o => o
                    .WithUrlPrefix(embedioUrl)
                    .WithMode(HttpListenerMode.EmbedIO))
                .WithModule(new ActionModule("/", HttpVerbs.Get, async ctx => {
                    TestContext.Out.WriteLine("GET: {0}", ctx.RequestedPath);
                    // return the certificate as binary blob
                    var path = Path.Combine(tempPath, ctx.RequestedPath.Substring(1));
                    var certBlob = File.ReadAllBytes(path);
                    ctx.Response.ContentEncoding = null;
                    ctx.Response.ContentType = "application/x-x509-ca-cert";
                    ctx.Response.OutputStream.Write(certBlob, 0, certBlob.Length);
                    ctx.SetHandled();
                    await Task.Delay(0);
                }));
#if STATIC_FOLDER // returns error 406 when GET certificate is called by .NET ChainBuilder 
                .WithStaticFolder("/", tempPath, false, m => m
                    .WithDirectoryLister(DirectoryLister.Html)
                    .WithCustomMimeType(".der", "application/x-x509-ca-cert")
                    )
#endif

            TestContext.Out.WriteLine("Hosting content at: {0}", tempPath);

            // Listen for state changes.
            server.StateChanged += (s, e) => {
                TestContext.Out.WriteLine($"WebServer New State - {e.NewState}");
            };
            server.Start(ct);

            TestContext.Out.WriteLine("Server started.");

            return server;
        }
    }
}
