/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Tests HTTPS TLS client-certificate validation against the configured UA validator.
    /// </summary>
    [TestFixture]
    [Category("HttpsClientCertificateValidation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class HttpsTransportListenerClientCertificateValidationTests
    {
        [Test]
        public void PresentedCertificateWithNoSslErrorsStillRequiresUaValidation()
        {
            using X509Certificate2 certificate = CreateClientCertificate();
            var validator = new RecordingCertificateValidator(s_rejection);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                certificate,
                chain: null,
                SslPolicyErrors.None);

            Assert.That(accepted, Is.False);
            Assert.That(validator.ValidationCount, Is.EqualTo(1));
            Assert.That(validator.LastTrustList, Is.EqualTo(TrustListIdentifier.Https));
            Assert.That(validator.LastChainCount, Is.EqualTo(1));
            Assert.That(validator.SingleCertificateValidationCount, Is.Zero);
        }

        [Test]
        public void PresentedCertificateWithSslErrorsCanBeAcceptedByUaValidation()
        {
            using X509Certificate2 certificate = CreateClientCertificate();
            var validator = new RecordingCertificateValidator(CertificateValidationResult.Success);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                certificate,
                chain: null,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(accepted, Is.True);
            Assert.That(validator.ValidationCount, Is.EqualTo(1));
        }

        [TestCase(SslPolicyErrors.None, true)]
        [TestCase(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
        public void MissingOptionalCertificateUsesSslPolicyResult(
            SslPolicyErrors sslPolicyErrors,
            bool expected)
        {
            var validator = new RecordingCertificateValidator(s_rejection);

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                validator,
                clientCertificate: null,
                chain: null,
                sslPolicyErrors);

            Assert.That(accepted, Is.EqualTo(expected));
            Assert.That(validator.ValidationCount, Is.Zero);
        }

        [Test]
        public async Task PresentedCertificateUsesHttpsTrustStoreAsync()
        {
            using var pki = new TemporaryPkiDirectory();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable? telemetryScope = telemetry as IDisposable;
            using var manager = new CertificateManager(telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, pki.PeersPath);
            manager.RegisterTrustList(TrustListIdentifier.Https, pki.HttpsPath);

            using Certificate peerRoot = CreateCertificateAuthority("HTTPS Regression Peer Root");
            using Certificate httpsRoot = CreateCertificateAuthority("HTTPS Regression HTTPS Root");
            using Certificate clientCertificate = CreateIssuedCertificate(
                "HTTPS Regression Client",
                httpsRoot);

            using (ICertificateStore peerStore = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await peerStore.AddAsync(peerRoot).ConfigureAwait(false);
            }
            using (ICertificateStore httpsStore = manager.OpenTrustedStore(TrustListIdentifier.Https))
            {
                await httpsStore.AddAsync(httpsRoot).ConfigureAwait(false);
            }
            using X509Certificate2 clientX509 = clientCertificate.AsX509Certificate2();

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                manager,
                clientX509,
                chain: null,
                SslPolicyErrors.None);

            Assert.That(accepted, Is.True);
        }

        [Test]
        public async Task TlsSuppliedIntermediateChainIsValidatedAndPreservedAsync()
        {
            using var pki = new TemporaryPkiDirectory();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using IDisposable? telemetryScope = telemetry as IDisposable;
            using var manager = new CertificateManager(telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Https, pki.HttpsPath);

            using Certificate root = CreateCertificateAuthority("HTTPS Regression Chain Root");
            using Certificate intermediate = CertificateBuilder
                .Create("CN=HTTPS Regression Intermediate")
                .SetCAConstraint(0)
                .SetIssuer(root)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate clientCertificate = CreateIssuedCertificate(
                "HTTPS Regression Chained Client",
                intermediate);

            using (ICertificateStore httpsStore = manager.OpenTrustedStore(TrustListIdentifier.Https))
            {
                await httpsStore.AddAsync(root).ConfigureAwait(false);
            }

            CertificateValidationResult leafOnlyResult = await manager
                .ValidateAsync(clientCertificate, TrustListIdentifier.Https)
                .ConfigureAwait(false);
            Assert.That(leafOnlyResult.IsValid, Is.False);
            Assert.That(
                leafOnlyResult.StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));

            using X509Certificate2 clientX509 = clientCertificate.AsX509Certificate2();
            using X509Certificate2 intermediateX509 = intermediate.AsX509Certificate2();
            using X509Certificate2 rootX509 = root.AsX509Certificate2();
            using var tlsChain = new X509Chain();
            tlsChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            tlsChain.ChainPolicy.VerificationFlags =
                X509VerificationFlags.AllowUnknownCertificateAuthority;
            tlsChain.ChainPolicy.ExtraStore.Add(intermediateX509);
            tlsChain.ChainPolicy.ExtraStore.Add(rootX509);
            _ = tlsChain.Build(clientX509);

            Assert.That(tlsChain.ChainElements, Has.Count.EqualTo(3));
            Assert.That(
                tlsChain.ChainElements[1].Certificate.Thumbprint,
                Is.EqualTo(intermediate.Thumbprint));

            var recordingValidator = new RecordingCertificateValidator(
                CertificateValidationResult.Success);
            bool chainForwarded = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                recordingValidator,
                clientX509,
                tlsChain,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(chainForwarded, Is.True);
            Assert.That(recordingValidator.LastTrustList, Is.EqualTo(TrustListIdentifier.Https));
            Assert.That(recordingValidator.LastChainCount, Is.EqualTo(tlsChain.ChainElements.Count));

            bool accepted = HttpsTransportListener.ValidateClientCertificateWithUaValidator(
                manager,
                clientX509,
                tlsChain,
                SslPolicyErrors.RemoteCertificateChainErrors);

            Assert.That(accepted, Is.True);
            Assert.That(
                tlsChain.ChainElements[1].Certificate.Thumbprint,
                Is.EqualTo(intermediate.Thumbprint));
        }

        private static X509Certificate2 CreateClientCertificate()
        {
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateApplicationCertificate(
                    "urn:localhost:HttpsTransportListenerClientCertificateValidationTests",
                    "HttpsTransportListenerClientCertificateValidationTests",
                    "CN=HttpsTransportListenerClientCertificateValidationTests",
                    ["localhost"])
                .SetLifeTime(TimeSpan.FromDays(1))
                .CreateForRSA();
            return certificate.AsX509Certificate2();
        }

        private static Certificate CreateCertificateAuthority(string commonName)
        {
            return CertificateBuilder
                .Create($"CN={commonName}")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateIssuedCertificate(
            string commonName,
            Certificate issuer)
        {
            return CertificateBuilder
                .Create($"CN={commonName}")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private sealed class RecordingCertificateValidator : ICertificateValidatorEx
        {
            public RecordingCertificateValidator(CertificateValidationResult result)
            {
                m_result = result;
            }

            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public int ValidationCount { get; private set; }

            public int SingleCertificateValidationCount { get; private set; }

            public TrustListIdentifier? LastTrustList { get; private set; }

            public int LastChainCount { get; private set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                global::Opc.Ua.Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                ValidationCount++;
                LastTrustList = trustList;
                LastChainCount = chain.Count;
                return Task.FromResult(m_result);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                ValidationCount++;
                SingleCertificateValidationCount++;
                LastTrustList = trustList;
                LastChainCount = 1;
                return Task.FromResult(m_result);
            }

            private readonly CertificateValidationResult m_result;
        }

        private sealed class TemporaryPkiDirectory : IDisposable
        {
            public TemporaryPkiDirectory()
            {
                m_rootPath = Path.Combine(
                    Path.GetTempPath(),
                    $"opcua-https-validation-{Guid.NewGuid():N}");
                PeersPath = Path.Combine(m_rootPath, "peers");
                HttpsPath = Path.Combine(m_rootPath, "https");
                Directory.CreateDirectory(PeersPath);
                Directory.CreateDirectory(HttpsPath);
            }

            public string PeersPath { get; }

            public string HttpsPath { get; }

            public void Dispose()
            {
                for (int attempt = 0; attempt < 5 && Directory.Exists(m_rootPath); attempt++)
                {
                    try
                    {
                        Directory.Delete(m_rootPath, recursive: true);
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(100);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Thread.Sleep(100);
                    }
                }
            }

            private readonly string m_rootPath;
        }

        private static readonly CertificateValidationResult s_rejection =
            new(isValid: false, StatusCodes.BadCertificateUntrusted, [], false);
    }
}
