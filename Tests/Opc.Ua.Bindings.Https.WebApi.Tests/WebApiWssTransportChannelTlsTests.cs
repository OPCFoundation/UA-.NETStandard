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
using System.Net.WebSockets;
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
    /// TLS regression tests for <see cref="WebApiWssTransportChannel"/>.
    /// The channel must delegate server certificate validation to the
    /// configured <see cref="TransportChannelSettings.CertificateValidator"/>
    /// (or, when none is configured, fall back to the default TLS
    /// chain check). Installing an unconditional pass-through callback
    /// would silently accept any server certificate — including an
    /// attacker MITM — defeating TLS server authentication.
    /// </summary>
    /// <remarks>
    /// Each test spins up a minimal Kestrel HTTPS host on
    /// <c>127.0.0.1:0</c> with a throw-away self-signed certificate
    /// that is NOT in the OS trust store, so the default TLS chain
    /// build always fails. The expected behaviour is:
    /// <list type="bullet">
    /// <item><description>Without a configured
    /// <see cref="TransportChannelSettings.CertificateValidator"/> the
    /// channel rejects the connection (the legacy bypass would have
    /// accepted it).</description></item>
    /// <item><description>With an OPC UA
    /// <see cref="ICertificateValidatorEx"/> that explicitly accepts
    /// the test certificate, the connection succeeds — proving the
    /// validator is consulted, not ignored.</description></item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [Category("WebApiWssTransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WebApiWssTransportChannelTlsTests
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
                    })));
                    webHost.ConfigureServices(s => { });
                    webHost.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(async context =>
                        {
                            if (!context.WebSockets.IsWebSocketRequest)
                            {
                                context.Response.StatusCode =
                                    Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                                return;
                            }
                            string? sub = context.WebSockets.WebSocketRequestedProtocols
                                .FirstOrDefault();
                            if (sub == null)
                            {
                                context.Response.StatusCode =
                                    Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                                return;
                            }
                            using WebSocket ws = await context.WebSockets
                                .AcceptWebSocketAsync(sub)
                                .ConfigureAwait(false);
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "tls-test",
                                context.RequestAborted).ConfigureAwait(false);
                        });
                    });
                });

            m_host = hostBuilder.Build();
            await m_host.StartAsync().ConfigureAwait(false);

            IServer server = m_host.Services.GetRequiredService<IServer>();
            string baseAddress = server.Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            m_baseUri = new Uri(baseAddress.Replace("https://", "wss://", StringComparison.Ordinal));
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
        public void OpenAsyncRejectsUntrustedServerCertificateByDefault()
        {
            // No CertificateValidator on the settings — the channel must
            // fall back to the default TLS chain result, which rejects
            // the self-signed test cert.
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            TransportChannelSettings settings = CreateSettings(certificateValidator: null);

            WebSocketException? ex = Assert.ThrowsAsync<WebSocketException>(async () =>
                await channel
                    .OpenAsync(m_baseUri, settings, CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null,
                "WebApiWssTransportChannel must not silently accept untrusted " +
                "server certificates — TLS server authentication is required.");
        }

        [Test]
        public async Task OpenAsyncSucceedsWhenValidatorAcceptsServerCertificateAsync()
        {
            // Custom validator returns IsValid=true for any cert: pins
            // that the channel actually consults the validator rather
            // than silently bypassing TLS validation.
            using var channel = new WebApiWssTransportChannel(new TelemetryStub());
            TransportChannelSettings settings = CreateSettings(
                certificateValidator: new PermissiveCertificateValidator());

            await channel
                .OpenAsync(m_baseUri, settings, CancellationToken.None)
                .ConfigureAwait(false);

            await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private TransportChannelSettings CreateSettings(ICertificateValidatorEx? certificateValidator)
        {
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    TransportProfileUri = Profiles.WssOpenApiTransport,
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
            // Persist with the private key so Kestrel can use it for TLS.
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
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
