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
    /// Exercises actual TLS handshakes at every sibling transport channel boundary.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TlsEndpointHostnameTests
    {
        [TestCase("binary-wss", false)]
        [TestCase("json-wss", false)]
        [TestCase("webapi-https", false)]
        [TestCase("webapi-wss", false)]
        [TestCase("binary-wss", true)]
        [TestCase("json-wss", true)]
        [TestCase("webapi-https", true)]
        [TestCase("webapi-wss", true)]
        public async Task HostnameMustMatchBeforeTrustedValidatorRunsAsync(string transport, bool nameMatches)
        {
            var telemetry = new TestTelemetry();
            using Certificate certificate = CertificateBuilder.Create("CN=TLS Hostname Regression")
                .AddExtension(new X509SubjectAltNameExtension(
                    ["urn:opcfoundation:test:tls-hostname"],
                    [nameMatches ? "127.0.0.1" : "wrong-host.example"]))
                .SetRSAKeySize(2048).CreateForRSA();
            using X509Certificate2 serverCertificate = certificate.AsX509Certificate2();
            var requestReachedServer = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using IHost host = new HostBuilder().ConfigureWebHost(web =>
            {
                web.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
                    listen.UseHttps(new HttpsConnectionAdapterOptions
                    {
                        ServerCertificate = serverCertificate,
                        ClientCertificateMode = ClientCertificateMode.NoCertificate
                    })));
                web.ConfigureServices(_ => { });
                web.Configure(app =>
                {
                    app.UseWebSockets();
                    app.Run(async context =>
                    {
                        requestReachedServer.TrySetResult(true);
                        if (context.WebSockets.IsWebSocketRequest)
                        {
                            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(
                                context.WebSockets.WebSocketRequestedProtocols[0]).ConfigureAwait(false);
                            await socket.CloseOutputAsync(
                                WebSocketCloseStatus.ProtocolError, "TLS-only test peer", context.RequestAborted)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            context.Response.StatusCode = 418;
                        }
                    });
                });
            }).Build();
            await host.StartAsync().ConfigureAwait(false);
            try
            {
                string address = host.Services.GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single();
                var uri = new UriBuilder(address)
                {
                    Scheme = transport == "webapi-https" ? "https" : "wss"
                };
                var validator = new AcceptingValidator();
                ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
                messageContext.Factory.Builder.AddEncodeableTypes(typeof(ReadRequest).Assembly).Commit();
                var settings = new TransportChannelSettings
                {
                    Description = new EndpointDescription
                    {
                        EndpointUrl = uri.Uri.AbsoluteUri,
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        ServerCertificate = certificate.RawData.ToByteString(),
                        TransportProfileUri = transport switch
                        {
                            "binary-wss" => Profiles.UaWssTransport,
                            "json-wss" => Profiles.UaWssJsonTransport,
                            "webapi-wss" => Profiles.WssOpenApiTransport,
                            _ => Profiles.HttpsJsonTransport
                        }
                    },
                    Configuration = EndpointConfiguration.Create(),
                    Factory = messageContext.Factory,
                    NamespaceUris = messageContext.NamespaceUris,
                    CertificateValidator = validator
                };
                settings.Configuration.OperationTimeout = 2000;
                using ITransportChannel channel = transport switch
                {
                    "binary-wss" => new WssTransportChannel(telemetry),
                    "json-wss" => new WssJsonTransportChannel(telemetry),
                    "webapi-https" => new WebApiTransportChannel(telemetry),
                    "webapi-wss" => new WebApiWssTransportChannel(telemetry),
                    _ => throw new ArgumentOutOfRangeException(nameof(transport))
                };
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                Assert.CatchAsync<Exception>(async () =>
                {
                    await ((ISecureChannel)channel).OpenAsync(uri.Uri, settings, deadline.Token).ConfigureAwait(false);
                    await channel.SendRequestAsync(
                        new ReadRequest { RequestHeader = new RequestHeader() }, deadline.Token).ConfigureAwait(false);
                });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(deadline.IsCancellationRequested, Is.False);
                    Assert.That(requestReachedServer.Task.IsCompleted, Is.EqualTo(nameMatches),
                        "Only a hostname-matching certificate may complete TLS and reach HTTP/WebSocket dispatch.");
                    Assert.That(validator.Calls, nameMatches ? Is.GreaterThan(0) : Is.Zero,
                        "Name mismatch must be rejected before consulting even an accepting UA trust validator.");
                }
            }
            finally
            {
                await host.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class AcceptingValidator : ICertificateValidatorEx
        {
            public Func<Certificate, ServiceResult, bool>? AcceptError { get; set; }

            public int Calls => Volatile.Read(ref m_calls);

            public Task<CertificateValidationResult> ValidateAsync(
                CertificateCollection chain,
                TrustListIdentifier? trustList = null,
                Security.Certificates.CertificateValidationOptions? options = null,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_calls);
                return Task.FromResult(CertificateValidationResult.Success);
            }

            public Task<CertificateValidationResult> ValidateAsync(
                Certificate certificate,
                TrustListIdentifier? trustList = null,
                CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_calls);
                return Task.FromResult(CertificateValidationResult.Success);
            }

            private int m_calls;
        }

        private sealed class TestTelemetry : TelemetryContextBase
        {
            public TestTelemetry()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
