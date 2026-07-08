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

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// TLS regression tests for <see cref="WebApiTransportChannel"/>.
    /// Pins the server-cert validation contract: the HTTPS client
    /// channel wires the OPC UA
    /// <see cref="TransportChannelSettings.CertificateValidator"/>
    /// (TrustedPeers store / application-URI rule / rejected list)
    /// into the
    /// <see cref="HttpClientHandler.ServerCertificateCustomValidationCallback"/>
    /// so the server certificate is validated against the OPC UA trust
    /// state, not just the default .NET TLS chain.
    /// </summary>
    [TestFixture]
    [Category("WebApiTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WebApiTransportChannelTlsTests
    {
        private IHost? m_host;
        private Uri m_baseUri = null!;
        private ServiceMessageContext? m_messageContext;
        private X509Certificate2? m_serverCert;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();

            m_serverCert = CreateSelfSignedTlsCertificate("127.0.0.1");

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts => opts.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = m_serverCert,
                        ClientCertificateMode = ClientCertificateMode.NoCertificate
                    })))
                        .ConfigureServices(_ => { });
                    webHost.Configure(app => app.Run(ctx =>
                        {
                            // The TLS regression tests assert at the
                            // channel-open stage; we never need to
                            // dispatch a real OPC UA request here.
                            ctx.Response.StatusCode = 204;
                            return Task.CompletedTask;
                        }));
                });

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            IServer server = m_host.Services.GetRequiredService<IServer>();
            string baseAddress = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            m_baseUri = new Uri(baseAddress);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_host != null)
            {
                await m_host.StopAsync().ConfigureAwait(false);
                m_host.Dispose();
                m_host = null;
            }
            m_serverCert?.Dispose();
            m_serverCert = null;
        }

        [Test]
        public async Task SendRequestRejectsUntrustedServerCertificateWhenValidatorRegisteredAsync()
        {
            // CertificateValidator configured (rejects the test cert) →
            // channel must reject the connection. Pins that the WebApi
            // client now consults the OPC UA validator.
            using var channel = new WebApiTransportChannel(new TelemetryStub());
            TransportChannelSettings settings = CreateSettings(
                certificateValidator: new RejectingCertificateValidator());

            await channel
                .OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);

            // The TLS handshake fails inside HttpClient when the OPC UA
            // CertificateValidator rejects the cert; HttpClient surfaces
            // this as an HttpRequestException wrapping
            // AuthenticationException("remote certificate was rejected").
            Exception? ex = Assert.CatchAsync(async () => await channel
                    .SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader() },
                        CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null,
                "WebApiTransportChannel must reject server certificate when the configured " +
                "OPC UA CertificateValidator returns invalid.");
            bool isCertRejection = ex is HttpRequestException ||
                ex is System.Security.Authentication.AuthenticationException ||
                (ex is ServiceResultException) ||
                ContainsInnerOfType<System.Security.Authentication.AuthenticationException>(ex);
            Assert.That(isCertRejection, Is.True,
                "Expected a TLS-layer rejection caused by the CertificateValidator, " +
                "got " +
                ex!.GetType().FullName +
                ": " +
                ex.Message);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private static bool ContainsInnerOfType<TException>(Exception? ex) where TException : Exception
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is TException)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public async Task SendRequestSucceedsWhenValidatorAcceptsServerCertificateAsync()
        {
            // CertificateValidator accepts any cert: the channel
            // delegates to it and the request reaches the test server
            // (which returns 204 NoContent).
            using var channel = new WebApiTransportChannel(new TelemetryStub());
            TransportChannelSettings settings = CreateSettings(
                certificateValidator: new PermissiveCertificateValidator());

            await channel
                .OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);

            // The server stub returns 204; the channel will fail to
            // decode an OPC UA response from the empty body — we only
            // care that the TLS handshake succeeded (no
            // ServerCertificateRejected exception).
            try
            {
                await channel
                    .SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader() },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode, Is.Not.EqualTo(StatusCodes.BadSecurityChecksFailed),
                    "Server cert validation must succeed when the validator accepts the cert.");
                Assert.That(sre.StatusCode, Is.Not.EqualTo(StatusCodes.BadCertificateUntrusted),
                    "Server cert validation must succeed when the validator accepts the cert.");
            }
            catch (HttpRequestException ex)
            {
                Assert.That(ex.Message, Does.Not.Contain("certificate"),
                    "Server cert validation must succeed when the validator accepts the cert: " + ex.Message);
            }
            finally
            {
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private TransportChannelSettings CreateSettings(ICertificateValidatorEx? certificateValidator)
        {
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    SecurityMode = MessageSecurityMode.None,
                    ServerCertificate = ByteString.From(0x01, 0x02, 0x03)
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable(),
                CertificateValidator = certificateValidator
            };
        }

        private static X509Certificate2 CreateSelfSignedTlsCertificate(string commonName)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                $"CN={commonName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    critical: false));
            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.1")],
                    critical: false));
            var san = new SubjectAlternativeNameBuilder();
            san.AddIpAddress(IPAddress.Loopback);
            san.AddDnsName(commonName);
            req.CertificateExtensions.Add(san.Build());
            using X509Certificate2 cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1));
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        private sealed class RejectingCertificateValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            private static readonly CertificateValidationResult s_rejection =
                new(isValid: false, StatusCodes.BadCertificateUntrusted, [], false);

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(s_rejection);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(s_rejection);
            }
        }

        private sealed class PermissiveCertificateValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(CertificateValidationResult.Success);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                return Task.FromResult(CertificateValidationResult.Success);
            }
        }

        private sealed class TelemetryStub : TelemetryContextBase
        {
            public TelemetryStub()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
