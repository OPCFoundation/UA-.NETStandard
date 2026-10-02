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
using System.IO;
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// Regression tests for the WSS clients (opcua+uacp and opcua+uajson):
    /// the mutual-TLS client certificate must stay alive for the handshake,
    /// and the JSON client must not wait unbounded for the server's Close
    /// frame after a response.
    /// </summary>
    [TestFixture]
    [Category("WssTransport")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class WssClientTlsAndCloseTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();
            m_serverCert = CreateSelfSignedCertificate("127.0.0.1", serverAuth: true);
            m_clientCert = CreateSelfSignedCertificate("wss-test-client", serverAuth: false);
            m_presentedThumbprint = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts => opts.Listen(IPAddress.Loopback, 0, listen =>
                        listen.UseHttps(new HttpsConnectionAdapterOptions
                        {
                            ServerCertificate = m_serverCert,
                            ClientCertificateMode = ClientCertificateMode.RequireCertificate,
                            ClientCertificateValidation = (_, _, _) => true
                        })))
                        .ConfigureServices(_ => { });
                    webHost.Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(async context =>
                        {
                            m_presentedThumbprint.TrySetResult(context.Connection.ClientCertificate?.Thumbprint);
                            string? sub = context.WebSockets.WebSocketRequestedProtocols.FirstOrDefault();
                            if (!context.WebSockets.IsWebSocketRequest || sub == null)
                            {
                                context.Response.StatusCode =
                                    Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest;
                                return;
                            }
                            using WebSocket ws = await context.WebSockets
                                .AcceptWebSocketAsync(sub)
                                .ConfigureAwait(false);
                            if (m_respondAndIgnoreClose)
                            {
                                await RespondAndIgnoreCloseAsync(ws, context.RequestAborted).ConfigureAwait(false);
                                return;
                            }
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
            string baseAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.First();
            m_baseUri = new Uri(baseAddress.Replace("https://", "wss://", StringComparison.Ordinal));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_host != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await m_host.StopAsync(cts.Token).ConfigureAwait(false);
                m_host.Dispose();
                m_host = null;
            }
            m_serverCert?.Dispose();
            m_clientCert?.Dispose();
        }

        /// <summary>
        /// The opcua+uacp client presents its TLS client certificate.
        /// </summary>
        [Test]
        public async Task UacpClientPresentsTlsClientCertificateAsync()
        {
            ITelemetryContext telemetry = new TelemetryStub();
            var buffers = new BufferManager("wss-client-tls", 8192, telemetry);
            using var transport = new WebSocketClientByteTransport(buffers, 8192, telemetry)
            {
                CertificateValidator = new PermissiveCertificateValidator(),
                ClientTlsCertificate = Certificate.From(CloneClientCertificate())
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await transport.ConnectAsync(m_baseUri, cts.Token).ConfigureAwait(false);

            string? thumbprint = await m_presentedThumbprint.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            Assert.That(thumbprint, Is.EqualTo(m_clientCert!.Thumbprint));
            transport.ClientTlsCertificate.Dispose();
        }

        /// <summary>
        /// The opcua+uajson client presents its TLS client certificate.
        /// </summary>
        [Test]
        public async Task UaJsonClientPresentsTlsClientCertificateAsync()
        {
            using var channel = new WssJsonTransportChannel(new TelemetryStub());
            await channel.OpenAsync(m_baseUri, CreateSettings(), CancellationToken.None).ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await channel.SendRequestAsync(new ReadRequest(), cts.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // The test server closes without a response; only the TLS handshake matters.
            }

            string? thumbprint = await m_presentedThumbprint.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            Assert.That(thumbprint, Is.EqualTo(m_clientCert!.Thumbprint));
        }

        /// <summary>
        /// A server that answers but never completes the close handshake must
        /// not hang the request after the response arrived.
        /// </summary>
        [Test]
        public async Task UaJsonRequestReturnsWhenServerIgnoresCloseHandshakeAsync()
        {
            m_respondAndIgnoreClose = true;
            using var channel = new WssJsonTransportChannel(new TelemetryStub());
            TransportChannelSettings settings = CreateSettings();
            settings.Configuration!.OperationTimeout = 60000;
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);

            Task<IServiceResponse> request = channel.SendRequestAsync(new ReadRequest()).AsTask();
            Task completed = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(10)))
                .ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(request), "Request hung waiting for the server's Close frame.");
            Assert.That(await request.ConfigureAwait(false), Is.InstanceOf<ReadResponse>());
        }

        private async Task RespondAndIgnoreCloseAsync(WebSocket ws, CancellationToken ct)
        {
            byte[] buffer = new byte[65536];
            WebSocketReceiveResult received;
            do
            {
                received = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            }
            while (!received.EndOfMessage);

            var response = new ReadResponse { ResponseHeader = new ResponseHeader() };
            byte[] payload;
            using (var memory = new MemoryStream())
            {
                using (var encoder = new JsonEncoder(memory, m_messageContext!, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(response, response.TypeId);
                }
                payload = memory.ToArray();
            }
            await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);

            // Never read the client's Close frame, so the close handshake never completes.
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Connection torn down.
            }
        }

        private TransportChannelSettings CreateSettings()
        {
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    TransportProfileUri = Profiles.UaWssJsonTransport,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None
                },
                Configuration = EndpointConfiguration.Create(),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable(),
                CertificateValidator = new PermissiveCertificateValidator(),
                ClientCertificate = Certificate.From(CloneClientCertificate())
            };
        }

        private X509Certificate2 CloneClientCertificate()
        {
            return X509CertificateLoader.LoadPkcs12(
                m_clientCert!.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string commonName, bool serverAuth)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                $"CN={commonName}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: false));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid(serverAuth ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")],
                critical: false));
            if (serverAuth)
            {
                var san = new SubjectAlternativeNameBuilder();
                san.AddIpAddress(IPAddress.Loopback);
                san.AddDnsName(commonName);
                req.CertificateExtensions.Add(san.Build());
            }
            using X509Certificate2 cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1));
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        private IHost? m_host;
        private Uri m_baseUri = null!;
        private ServiceMessageContext? m_messageContext;
        private X509Certificate2? m_serverCert;
        private X509Certificate2? m_clientCert;
        private TaskCompletionSource<string?> m_presentedThumbprint = null!;
        private volatile bool m_respondAndIgnoreClose;

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
