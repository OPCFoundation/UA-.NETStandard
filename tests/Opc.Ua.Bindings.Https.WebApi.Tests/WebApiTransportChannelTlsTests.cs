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
using Opc.Ua.Bindings;
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
        private int m_requestCount;
        /// <summary>Whether the test endpoint redirects instead of returning an empty response.</summary>
        private bool m_returnRedirect;

        /// <summary>
        /// Starts a loopback TLS endpoint with replaceable certificates and no reusable HTTP connections.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            m_returnRedirect = false;
            m_messageContext = ServiceMessageContext.CreateEmpty(new TelemetryStub());
            m_messageContext.Factory.Builder
                .AddEncodeableTypes(typeof(ReadResponse).Assembly)
                .Commit();

            m_serverCert = CreateSelfSignedTlsCertificate("127.0.0.1");

            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel(opts => opts.Listen(
                        IPAddress.Loopback, 0, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
                        {
                            ServerCertificateSelector = (_, _) => m_serverCert,
                            ClientCertificateMode = ClientCertificateMode.NoCertificate
                        })))
                        .ConfigureServices(_ => { });
                    webHost.Configure(app => app.Run(ctx =>
                        {
                            Interlocked.Increment(ref m_requestCount);
                            ctx.Response.Headers.Connection = "close";
                            // The TLS regression tests assert at the
                            // channel-open stage; we never need to
                            // dispatch a real OPC UA request here.
                            ctx.Response.StatusCode = m_returnRedirect ? 307 : 204;
                            if (m_returnRedirect)
                            {
                                ctx.Response.Headers.Location = "/redirected";
                            }
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
            Exception? ex = await Assert.CatchAsync(async () => await channel
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

        /// <summary>
        /// HTTPS records the TLS certificate independently from the selected application certificate.
        /// </summary>
        [Test]
        public async Task HttpsChannelCapturesTlsCertificateForSignEndpointAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            TransportChannelSettings settings = CreateHttpsEndpointSettings(
                m_serverCert!.RawData, MessageSecurityMode.Sign);

            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(m_requestCount, Is.EqualTo(1));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(m_serverCert!.RawData));
        }

        /// <summary>
        /// A secure HTTPS endpoint may use a TLS certificate distinct from its OPC UA application certificate.
        /// </summary>
        [Test]
        public async Task HttpsChannelAllowsSeparateTlsAndApplicationCertificatesAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using X509Certificate2 otherCertificate = CreateSelfSignedTlsCertificate("127.0.0.1");
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            TransportChannelSettings settings = CreateHttpsEndpointSettings(
                otherCertificate.RawData, MessageSecurityMode.Sign);

            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(m_requestCount, Is.EqualTo(1));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(m_serverCert!.RawData));
        }

        /// <summary>
        /// An explicit TLS pin accepts a separate self-signed TLS peer without consulting the UA trust validator.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPinAcceptsSeparateApplicationCertificateAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using X509Certificate2 applicationCertificate = CreateSelfSignedTlsCertificate("application");
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            TransportChannelSettings settings = CreateHttpsEndpointSettings(
                applicationCertificate.RawData, MessageSecurityMode.Sign);
            settings.CertificateValidator = new RejectingCertificateValidator();
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(m_serverCert!.RawData));

            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new ReadRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(m_requestCount, Is.EqualTo(1));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(m_serverCert.RawData));
            Assert.That(channel.ServerChannelCertificate, Is.Not.EqualTo(applicationCertificate.RawData));
        }

        /// <summary>
        /// Selecting independent TLS authentication replaces an injected HTTP pipeline that cannot enforce it.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPolicyReplacesInjectedHttpPipelineBeforeRequestAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using var suppliedClient = new HttpClient();
            var factory = new SingleHttpClientFactory(suppliedClient);
            using var channel = new HttpsTransportChannel(
                Utils.UriSchemeHttps, new TelemetryStub(), httpClientFactory: factory);
            TransportChannelSettings settings = CreateHttpsEndpointSettings(
                m_serverCert!.RawData, MessageSecurityMode.Sign);
            settings.CertificateValidator = null;
            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);
            Assert.That(factory.CreateCalls, Is.EqualTo(1));
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(m_serverCert.RawData));

            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(m_requestCount, Is.EqualTo(1));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(m_serverCert.RawData));
            Assert.That(factory.CreateCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Neither UA trust nor an application certificate pin substitutes for the selected TLS policy.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task HttpsIndependentTlsPolicyRejectsUntrustedOrUnexpectedPeerBeforeRequestAsync(bool wrongPin)
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using X509Certificate2 unexpected = CreateSelfSignedTlsCertificate("127.0.0.1");
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(m_serverCert!.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                wrongPin ? ByteString.From(unexpected.RawData) : default);

            Exception? error = await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(error, Is.Not.Null);
            Assert.That(m_requestCount, Is.Zero);
            Assert.That(channel.ServerChannelCertificate, Is.Empty);
        }

        /// <summary>
        /// A replacement pooled connection must revalidate the TLS pin before delivering another request.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPinRejectsChangedCertificateOnReplacementConnectionAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using X509Certificate2 initialCertificate = m_serverCert!;
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(initialCertificate.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(initialCertificate.RawData));
            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));
            Assert.That(m_requestCount, Is.EqualTo(1));

            m_serverCert = CreateSelfSignedTlsCertificate("127.0.0.1");
            Exception? error = await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new ActivateSessionRequest
                {
                    RequestHeader = new RequestHeader(),
                    UserIdentityToken = new ExtensionObject(new UserNameIdentityToken
                    {
                        UserName = "tls-regression-user",
                        Password = ByteString.From("not-a-real-secret"u8)
                    })
                },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(error, Is.Not.Null);
            Assert.That(m_requestCount, Is.EqualTo(1), "Replacement TLS rejection must precede credential delivery.");
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(initialCertificate.RawData));
        }

        /// <summary>
        /// Even an exact pin cannot disable the TLS hostname check.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPinRejectsHostnameMismatchBeforeRequestAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            m_serverCert!.Dispose();
            m_serverCert = CreateSelfSignedTlsCertificate("wrong.example.test", includeLoopback: false);
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(m_serverCert.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(m_serverCert.RawData));

            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(m_requestCount, Is.Zero);
            Assert.That(channel.ServerChannelCertificate, Is.Empty);
        }

        /// <summary>
        /// An exact TLS pin does not extend the validity period of an expired certificate.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPinRejectsExpiredCertificateBeforeRequestAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            m_serverCert!.Dispose();
            m_serverCert = CreateSelfSignedTlsCertificate("127.0.0.1", expired: true);
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(m_serverCert.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(m_serverCert.RawData));

            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(m_requestCount, Is.Zero);
            Assert.That(channel.ServerChannelCertificate, Is.Empty);
        }

        /// <summary>
        /// A TLS-authenticated endpoint cannot redirect a request to another endpoint.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPolicyDoesNotFollowRedirectsAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            m_returnRedirect = true;
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(m_serverCert!.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                ByteString.From(m_serverCert.RawData));

            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new CreateSessionRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            Assert.That(m_requestCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A TLS policy cannot be replaced after the first handshake or request.
        /// </summary>
        [Test]
        public async Task HttpsIndependentTlsPolicyCannotChangeAfterRequestAsync()
        {
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            await channel.OpenAsync(
                m_baseUri,
                CreateHttpsEndpointSettings(m_serverCert!.RawData, MessageSecurityMode.Sign),
                CancellationToken.None).ConfigureAwait(false);
            await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                new ReadRequest { RequestHeader = new RequestHeader() },
                CancellationToken.None).ConfigureAwait(false));

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                ((IHttpsServerCertificateValidation)channel).RequireServerCertificateValidation(
                    ByteString.From(m_serverCert.RawData)));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// SecurityPolicy None does not apply endpoint-certificate pinning.
        /// </summary>
        [Test]
        public async Task HttpsChannelPreservesSecurityNoneWithDifferentEndpointCertificateAsync()
        {
            Interlocked.Exchange(ref m_requestCount, 0);
            using X509Certificate2 otherCertificate = CreateSelfSignedTlsCertificate("127.0.0.1");
            using var channel = new HttpsTransportChannel(Utils.UriSchemeHttps, new TelemetryStub());
            TransportChannelSettings settings = CreateHttpsEndpointSettings(
                otherCertificate.RawData, MessageSecurityMode.None);

            await channel.OpenAsync(m_baseUri, settings, CancellationToken.None).ConfigureAwait(false);
            try
            {
                await Assert.CatchAsync(async () => await channel.SendRequestAsync(
                    new ReadRequest { RequestHeader = new RequestHeader() },
                    CancellationToken.None).ConfigureAwait(false));
            }
            finally
            {
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(m_requestCount, Is.EqualTo(1));
            Assert.That(channel.ServerChannelCertificate, Is.EqualTo(m_serverCert!.RawData));
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

        private TransportChannelSettings CreateHttpsEndpointSettings(
            byte[] expectedCertificate,
            MessageSecurityMode securityMode)
        {
            bool secure = securityMode != MessageSecurityMode.None;
            return new TransportChannelSettings
            {
                Description = new EndpointDescription
                {
                    EndpointUrl = m_baseUri.AbsoluteUri,
                    SecurityMode = securityMode,
                    SecurityPolicyUri = secure ? SecurityPolicies.Basic256Sha256 : SecurityPolicies.None,
                    TransportProfileUri = Profiles.HttpsBinaryTransport,
                    ServerCertificate = ByteString.From(expectedCertificate)
                },
                Configuration = EndpointConfiguration.Create(),
                ServerCertificate = Certificate.FromRawData(expectedCertificate),
                Factory = m_messageContext!.Factory,
                NamespaceUris = new NamespaceTable(),
                CertificateValidator = new PermissiveCertificateValidator()
            };
        }

        /// <summary>
        /// Creates a test TLS credential with independently selectable hostname and validity failures.
        /// </summary>
        private static X509Certificate2 CreateSelfSignedTlsCertificate(
            string commonName,
            bool includeLoopback = true,
            bool expired = false)
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
            if (includeLoopback)
            {
                san.AddIpAddress(IPAddress.Loopback);
            }
            san.AddDnsName(commonName);
            req.CertificateExtensions.Add(san.Build());
            using X509Certificate2 cert = req.CreateSelfSigned(
                expired ? DateTimeOffset.UtcNow.AddHours(-2) : DateTimeOffset.UtcNow.AddMinutes(-5),
                expired ? DateTimeOffset.UtcNow.AddHours(-1) : DateTimeOffset.UtcNow.AddHours(1));
            return X509CertificateLoader.LoadPkcs12(
                cert.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        /// <summary>
        /// Supplies a host-owned HTTP client without exposing its TLS validation pipeline.
        /// </summary>
        private sealed class SingleHttpClientFactory : IOpcUaHttpClientFactory
        {
            /// <summary>
            /// Creates a factory borrowing the host's HTTP client.
            /// </summary>
            public SingleHttpClientFactory(HttpClient client)
            {
                m_client = client ?? throw new ArgumentNullException(nameof(client));
            }

            /// <summary>
            /// Gets the number of requested clients.
            /// </summary>
            public int CreateCalls { get; private set; }

            /// <inheritdoc/>
            public HttpClient CreateClient(string name)
            {
                CreateCalls++;
                return m_client;
            }

            /// <summary>The host-owned HTTP pipeline.</summary>
            private readonly HttpClient m_client;
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
