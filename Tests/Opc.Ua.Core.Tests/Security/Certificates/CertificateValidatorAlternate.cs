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

#if EMBED_IO_INCLUDED
using System;
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
using Opc.Ua.Tests;
using X509AuthorityKeyIdentifierExtension = Opc.Ua.Security.Certificates.X509AuthorityKeyIdentifierExtension;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for the CertificateValidator class with
    /// permutations of the AuthorityKeyIdentifier.
    /// </summary>
    [TestFixture]
    [Category("CertificateValidator")]
    [NonParallelizable]
    [TestFixtureSource(nameof(FixtureArgs))]
    [SetCulture("en-us")]
    public class CertificateValidatorAlternate
    {
        private static readonly ICertificateFactory s_factory = new DefaultCertificateFactory();
        private static readonly ICertificateIssuer s_issuer = new DefaultCertificateIssuer();

        /// <summary>
        /// the root and alternate root CA
        /// </summary>
        private const string kCaSubject = "CN=Root";
        private const string kCaAltSubject = "CN=rOOT";

        public static readonly object[] FixtureArgs = [new object[] { kCaSubject }, new object[] {
            kCaAltSubject }];

        public CertificateValidatorAlternate(string altSubject = kCaSubject)
        {
            m_altSubject = altSubject;
        }

        private readonly string m_altSubject;
        private Certificate m_rootCert;
        private Certificate m_rootAltCert;
        private X509CRL m_rootCrl;
        private TemporaryCertValidator m_validator;
        private CertificateValidator m_certValidator;

        /// <summary>
        /// A web server to host root CA and CRL
        /// </summary>
        private WebServer m_webServer;
        private string m_webServerUrl;
        private string m_webServerPath;
        private string m_altCertFilename;

        /// <summary>
        /// Set up a web server and root CAs.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string crlName = "root.crl";
            m_webServerUrl = "http://127.0.0.1:9696/";

            X509Extension crlExtension = X509Extensions.BuildX509CRLDistributionPoints(
                m_webServerUrl + crlName);

            // create the root cert
            m_rootCert = s_factory
                .CreateCertificate(kCaSubject)
                .AddExtension(crlExtension)
                .SetLifeTime(25 * 12)
                .SetCAConstraint()
                .CreateForRSA();

            // default empty root CRL
            m_rootCrl = s_issuer.RevokeCertificates(m_rootCert, null, null);

            // create cert validator for test, add trusted root cert
            m_validator = TemporaryCertValidator.Create(telemetry);
            await m_validator.TrustedStore.AddAsync(m_rootCert).ConfigureAwait(false);
            await m_validator.TrustedStore.AddCRLAsync(m_rootCrl).ConfigureAwait(false);
            m_certValidator = m_validator.Update();

            // create a root with same serial number but modified Subject / key pair
            m_rootAltCert = s_factory
                .CreateCertificate(m_altSubject)
                .SetLifeTime(25 * 12)
                .SetSerialNumber(m_rootCert.GetSerialNumber())
                .SetCAConstraint()
                .CreateForRSA();

            try
            {
                // temp location for hosted CA cert
                m_webServerPath = Path.GetTempPath() +
                    Path.GetRandomFileName() +
                    Path.DirectorySeparatorChar;

                // Start a disposable web server.
                m_webServer = CreateWebServer(
                    m_webServerUrl,
                    m_webServerPath,
                    CancellationToken.None);

                // filename for the alternate certificate
                m_altCertFilename = m_rootAltCert.SerialNumber + ".der";

                // write alternate root cert to http store
                File.WriteAllBytes(m_webServerPath + m_altCertFilename, m_rootAltCert.RawData);

                // write crl to http store
                File.WriteAllBytes(m_webServerPath + crlName, m_rootCrl.RawData);
            }
            catch
            {
                Assert.Ignore($"Web server could not start at: {m_webServerUrl}");
            }
        }

        /// <summary>
        /// Tear down the server fixture.
        /// </summary>
        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_rootCert?.Dispose();
            m_rootAltCert?.Dispose();
            m_validator?.Dispose();
            m_webServer?.Dispose();
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

        /// <summary>
        /// A signed app cert that has no keyid information.
        /// </summary>
        [Test]
        public async Task CertificateWithoutKeyIDAsync()
        {
            // a valid app cert
            using Certificate appCert = s_factory
                .CreateCertificate("CN=AppCert")
                .SetIssuer(m_rootCert)
                .CreateForRSA();
            Assert.That(appCert, Is.Not.Null);

            m_certValidator.RejectUnknownRevocationStatus = true;
            await m_certValidator.ValidateAsync(appCert, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Certificate with combinations of optional fields in the AKI.
        /// </summary>
        [Theory]
        public async Task CertificateWithAuthorityKeyIDAsync(
            bool subjectKeyIdentifier,
            bool issuerName,
            bool serialNumber)
        {
            ICertificateBuilder certBuilder = s_factory.CreateCertificate("CN=AppCert");

            // force exception if SKI is not present
            X509SubjectKeyIdentifierExtension ski = m_rootCert
                .Extensions.OfType<X509SubjectKeyIdentifierExtension>()
                .Single();

            // create a certificate with special key info
            var authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null,
                issuerName ? m_rootCert.IssuerName : null,
                serialNumber ? m_rootCert.GetSerialNumber() : null);

            TestContext.Out.WriteLine("Extension: {0}", authorityKeyIdentifier.Format(true));

            certBuilder.AddExtension(authorityKeyIdentifier);

            // a valid app cert
            using Certificate appCert = certBuilder.SetIssuer(m_rootCert).CreateForRSA();
            m_certValidator.RejectUnknownRevocationStatus = false;
            if (!subjectKeyIdentifier && !serialNumber)
            {
                ServiceResultException result = Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await m_certValidator.ValidateAsync(appCert, CancellationToken.None).ConfigureAwait(false));
                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
            else
            {
                await m_certValidator.ValidateAsync(appCert, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// App cert from alternate Root without KeyID.
        /// </summary>
        [Theory]
        public void AlternateRootCertificateWithoutAuthorityKeyID(
            bool rejectUnknownRevocationStatus)
        {
            ICertificateBuilder certBuilder = s_factory.CreateCertificate(
                "CN=AlternateAppCert");
            var caIssuerUrl = new List<string> { m_webServerUrl + m_altCertFilename };
            X509Extension extension = caIssuerUrl.ToArray().BuildX509AuthorityInformationAccess();
            certBuilder.AddExtension(extension);
            TestContext.Out.WriteLine("Extension: {0}", extension.Format(true));

            // create app cert from alternate root
            using Certificate altAppCert = certBuilder.SetIssuer(m_rootAltCert).CreateForRSA();
            Assert.That(altAppCert, Is.Not.Null);

            m_certValidator.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;
            ServiceResultException result = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await m_certValidator.ValidateAsync(altAppCert, CancellationToken.None).ConfigureAwait(false));

            TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
        }

        /// <summary>
        /// Create an app certificate from the alternate root,
        /// validate that any combination of AKI is not validated.
        /// </summary>
        [Theory]
        public void AlternateRootCertificateWithAuthorityKeyID(
            bool subjectKeyIdentifier,
            bool issuerName,
            bool serialNumber)
        {
            ICertificateBuilder certBuilder = s_factory.CreateCertificate("CN=AltAppCert");

            // force exception if SKI is not present
            X509SubjectKeyIdentifierExtension ski = m_rootAltCert
                .Extensions.OfType<X509SubjectKeyIdentifierExtension>()
                .Single();

            // create a certificate with special key info / no key id
            var authorityKeyIdentifier = new X509AuthorityKeyIdentifierExtension(
                subjectKeyIdentifier ? ski.SubjectKeyIdentifier.FromHexString() : null,
                issuerName ? m_rootAltCert.IssuerName : null,
                serialNumber ? m_rootAltCert.GetSerialNumber() : null);
            certBuilder.AddExtension(authorityKeyIdentifier);
            TestContext.Out.WriteLine("Extension: {0}", authorityKeyIdentifier.Format(true));

            var caIssuerUrl = new List<string> { m_webServerUrl + m_altCertFilename };
            X509Extension extension = caIssuerUrl.ToArray().BuildX509AuthorityInformationAccess();
            certBuilder.AddExtension(extension);
            TestContext.Out.WriteLine("Extension: {0}", extension.Format(true));

            // create the certificate from the alternate root
            using Certificate altAppCert = certBuilder.SetIssuer(m_rootAltCert).CreateForRSA();
            Assert.That(altAppCert, Is.Not.Null);

            // should not pass!
            m_certValidator.RejectUnknownRevocationStatus = false;
            ServiceResultException result = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await m_certValidator.ValidateAsync(altAppCert, CancellationToken.None).ConfigureAwait(false));
            TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
        }

        /// <summary>
        /// Validate a chain with a loop is detected.
        /// </summary>
        [Test]
        [CancelAfter(10000)]
        public async Task VerifyLoopChainIsDetectedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const string rootSubject = "CN=Root";
            const string subCASubject = "CN=Sub";
            const string leafSubject = "CN=Leaf";

            using var rsa = RSA.Create();
            var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);

            using Certificate rootCert = s_factory
                .CreateCertificate(rootSubject)
                .SetCAConstraint()
                .SetRSAPublicKey(rsa)
                .CreateForRSA(generator);
            using Certificate subCACert = s_factory
                .CreateCertificate(subCASubject)
                .SetCAConstraint()
                .SetIssuer(rootCert)
                .CreateForRSA(generator);
            using Certificate rootReverseCert = s_factory
                .CreateCertificate(rootSubject)
                .SetCAConstraint()
                .SetSerialNumber(rootCert.GetSerialNumber())
                .SetIssuer(subCACert)
                .SetRSAPublicKey(rsa)
                .CreateForRSA();
            using Certificate leafCert = s_factory
                .CreateCertificate(leafSubject)
                .SetIssuer(subCACert)
                .CreateForRSA();
            // validate cert chain
            using (var validator = TemporaryCertValidator.Create(telemetry))
            {
                await validator.IssuerStore.AddAsync(rootCert).ConfigureAwait(false);
                await validator.TrustedStore.AddAsync(subCACert).ConfigureAwait(false);
                CertificateValidator certValidator = validator.Update();
                await certValidator.ValidateAsync(leafCert, CancellationToken.None).ConfigureAwait(false);
            }

            // validate using server/client chain sent over the wire
            using var collection = new CertificateCollection {
                leafCert,
                subCACert,
                rootReverseCert };
            using (var validator = TemporaryCertValidator.Create(telemetry))
            {
                CertificateValidator certValidator = validator.Update();
                ServiceResultException result = Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await certValidator.ValidateAsync(collection, CancellationToken.None).ConfigureAwait(false));

                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }

            // validate using cert chain in issuer and trusted store
            using (var validator = TemporaryCertValidator.Create(telemetry))
            {
                await validator.IssuerStore.AddAsync(rootReverseCert).ConfigureAwait(false);
                await validator.TrustedStore.AddAsync(subCACert).ConfigureAwait(false);
                CertificateValidator certValidator = validator.Update();
                ServiceResultException result = Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await certValidator.ValidateAsync(collection, CancellationToken.None).ConfigureAwait(false));

                TestContext.Out.WriteLine($"{result.Result}: {result.Message}");
            }
        }

        /// <summary>
        /// Create and configure web server.
        /// </summary>
        private static WebServer CreateWebServer(string url, string tempPath, CancellationToken ct)
        {
            if (!Directory.Exists(tempPath))
            {
                TestContext.Out.WriteLine("Create folder: {0}", tempPath);
                Directory.CreateDirectory(tempPath);
            }

            TestContext.Out.WriteLine("Start Web server at: {0}", url);

            // Tiny web server does not respond to localhost or ::1, use 127.0.0.1
            string embedioUrl = url.Replace("localhost", "*", StringComparison.Ordinal);
            WebServer server = new WebServer(
                o => o.WithUrlPrefix(embedioUrl).WithMode(HttpListenerMode.EmbedIO))
                .WithModule(
                    new ActionModule(
                        "/",
                        HttpVerbs.Get,
                        async ctx =>
                        {
                            TestContext.Out.WriteLine("GET: {0}", ctx.RequestedPath);
                            // return the certificate as binary blob
                            string path = Path.Combine(tempPath, ctx.RequestedPath[1..]);
                            byte[] certBlob = File.ReadAllBytes(path);
                            ctx.Response.ContentEncoding = null;
                            ctx.Response.ContentType = "application/x-x509-ca-cert";
                            ctx.Response.OutputStream.Write(certBlob, 0, certBlob.Length);
                            ctx.SetHandled();
                            await Task.Delay(0).ConfigureAwait(false);
                        }))
#if STATIC_FOLDER // returns error 406 when GET certificate is called by .NET ChainBuilder
                .WithStaticFolder(
                    "/",
                    tempPath,
                    false,
                    m =>
                        m.WithDirectoryLister(DirectoryLister.Html)
                            .WithCustomMimeType(".der", "application/x-x509-ca-cert"))
#endif
            ;

            TestContext.Out.WriteLine("Hosting content at: {0}", tempPath);

            // Listen for state changes.
            server.StateChanged += (s, e) => TestContext.Out
                .WriteLine($"WebServer New State - {e.NewState}");
            server.Start(ct);

            TestContext.Out.WriteLine("Server started.");

            return server;
        }
    }
}

#endif
