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

#if NET8_0_OR_GREATER

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// A Session-less request (OPC 10000-4 §6.3) over HTTPS has to meet the
    /// channel security the endpoint demands for CreateSession: a request
    /// without an Access Token is anonymous only where application
    /// authentication through the channel is sufficient (§6.3.1), which on
    /// HTTPS is the TLS client certificate, and only where the endpoint
    /// offers an anonymous user token policy.
    /// </summary>
    [TestFixture(true)]
    [TestFixture(false)]
    [Category("Session")]
    [Category("SessionlessInvocation")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class SessionlessChannelSecurityTests
    {
        private const int kTimeout = 30_000;
        private const string kAccessToken = "channel.security.access-token";

        private readonly bool m_mutualTls;
        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ReferenceServer m_server;
        private IServiceMessageContext m_messageContext;
        private SessionManager m_sessionManager;
        private AccessTokenAuthenticator m_authenticator;
        private X509Certificate2 m_clientCertificate;
        private string m_pkiRoot;
        private Uri m_restBase;
        private Uri m_binaryUrl;

        public SessionlessChannelSecurityTests(bool mutualTls)
        {
            m_mutualTls = mutualTls;
        }

        public enum Transport
        {
            Rest,
            BinaryHttps
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            m_messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);

            var restServer = new WebApiServer(m_messageContext, "sessionless-channel-security");
            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterChannelFactory(new TcpTransportChannelFactory());
            registry.RegisterListenerFactory(new TcpTransportListenerFactory());
            var https = new HttpsTransportListenerFactory();
            https.StartupContributors.Add(new WebApiHttpsStartupContributor(restServer));
            registry.RegisterListenerFactory(https);
            var opcHttps = new OpcHttpsTransportListenerFactory();
            opcHttps.StartupContributors.Add(new WebApiHttpsStartupContributor(restServer));
            registry.RegisterListenerFactory(opcHttps);
            registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
            registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                // With mutual TLS the endpoint is SignAndEncrypt only, without
                // a SecurityMode=None policy.
                SecurityNone = !m_mutualTls,
                UriScheme = Utils.UriSchemeOpcHttps,
                HttpsMutualTls = m_mutualTls,
                MaxChannelCount = 103,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security,
                TransportBindingRegistry = registry
            };
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.ServerConfiguration!.UserTokenPolicies =
            [
                new UserTokenPolicy(UserTokenType.Anonymous),
                new UserTokenPolicy(UserTokenType.UserName),
                new UserTokenPolicy(UserTokenType.IssuedToken) { IssuedTokenType = Profiles.JwtUserToken }
            ];
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            string host = new Uri(Utils.ReplaceLocalhost("https://localhost/")).Host;
            string port = m_serverFixture.Port.ToString(CultureInfo.InvariantCulture);
            m_restBase = new Uri($"opc.https://{host}:{port}/");
            m_binaryUrl = new Uri($"https://{host}:{port}/{nameof(ReferenceServer)}/");

            m_clientCertificate = AcquireApplicationCertificate();
            m_sessionManager = (SessionManager)m_server.CurrentInstance.SessionManager;
            m_authenticator = new AccessTokenAuthenticator(kAccessToken);
            m_server.CurrentInstance.IdentityRegistry.Register(m_authenticator);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_sessionManager != null)
            {
                m_sessionManager.SessionlessInvocation = null;
            }
            if (m_authenticator != null)
            {
                m_server?.CurrentInstance.IdentityRegistry.Unregister(m_authenticator);
            }
            m_clientCertificate?.Dispose();
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }

        [TearDown]
        public void DisableSessionlessInvocation()
        {
            m_sessionManager.SessionlessInvocation = null;
        }

        [Test]
        public void TheEndpointOffersAnAnonymousUserTokenPolicyOnlyWithMutualTls()
        {
            EndpointDescription endpoint = m_server.GetEndpoints().ToArray()!
                .First(ep => Utils.IsUriHttpsScheme(ep.EndpointUrl!) &&
                    ep.TransportProfileUri == Profiles.HttpsBinaryTransport);

            Assert.That(
                endpoint.UserIdentityTokens.ToArray()!.Any(t => t.TokenType == UserTokenType.Anonymous),
                Is.EqualTo(m_mutualTls));
            Assert.That(
                endpoint.SecurityMode,
                Is.EqualTo(m_mutualTls ? MessageSecurityMode.SignAndEncrypt : MessageSecurityMode.None));
        }

        [Test]
        public async Task AnAnonymousRequestWithoutAClientCertificateIsRejectedAsync(
            [Values] Transport transport)
        {
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = true };

            StatusCode result = await ReadAsync(transport, NodeId.Null, withCertificate: false)
                .ConfigureAwait(false);

            // With mutual TLS the channel carries no application authentication,
            // as CreateSession reports it (Bad_SecurityChecksFailed). Without it
            // the endpoint offers no anonymous user token policy.
            Assert.That(
                result,
                Is.EqualTo(m_mutualTls ? StatusCodes.BadSecurityChecksFailed : StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AnAnonymousRequestWithAClientCertificateIsAcceptedOnlyWithMutualTlsAsync(
            [Values] Transport transport)
        {
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = true };

            StatusCode result = await ReadAsync(transport, NodeId.Null, withCertificate: true)
                .ConfigureAwait(false);

            Assert.That(
                result,
                Is.EqualTo(m_mutualTls ? StatusCodes.Good : StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task AnAnonymousRequestIsRejectedUnlessAnonymousIsAllowedAsync(
            [Values] Transport transport,
            [Values] bool withCertificate)
        {
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = false };

            StatusCode result = await ReadAsync(transport, NodeId.Null, withCertificate)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AnAccessTokenIsAuthenticatedByTheTokenAloneAsync(
            [Values] Transport transport,
            [Values] bool withCertificate,
            [Values] bool allowAnonymous)
        {
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions
            {
                AllowAnonymous = allowAnonymous
            };

            StatusCode result = await ReadAsync(transport, new NodeId(kAccessToken, 0), withCertificate)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ABogusAccessTokenIsRejectedWithOrWithoutAClientCertificateAsync(
            [Values] Transport transport,
            [Values] bool withCertificate)
        {
            m_sessionManager.SessionlessInvocation = new SessionlessInvocationOptions { AllowAnonymous = true };

            StatusCode result = await ReadAsync(transport, new NodeId("bogus.token", 0), withCertificate)
                .ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadIdentityTokenRejected));
        }

        [Test]
        public async Task CreateSessionOverBinaryHttpsRequiresTheClientCertificateAsync()
        {
            if (!m_mutualTls)
            {
                Assert.Pass("CreateSession only matches the TLS client certificate with mutual TLS.");
            }

            using HttpClient http = CreateHttpClient(withCertificate: false);
            IServiceResponse response = await SendBinaryAsync(
                http,
                new CreateSessionRequest
                {
                    RequestHeader = CreateHeader(NodeId.Null),
                    ClientDescription = new ApplicationDescription
                    {
                        ApplicationUri = "urn:sessionless:channel:security",
                        ApplicationType = ApplicationType.Client
                    },
                    EndpointUrl = m_binaryUrl.ToString(),
                    SessionName = "channel-security",
                    ClientNonce = ByteString.From(new byte[32]),
                    ClientCertificate = ByteString.From(m_clientCertificate.RawData),
                    RequestedSessionTimeout = 60_000
                }).ConfigureAwait(false);

            Assert.That(
                response.ResponseHeader.ServiceResult,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
        }

        private async Task<StatusCode> ReadAsync(Transport transport, NodeId authenticationToken, bool withCertificate)
        {
            var request = new ReadRequest
            {
                RequestHeader = CreateHeader(authenticationToken),
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead = new ArrayOf<ReadValueId>(new ReadValueId[]
                {
                    new()
                    {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                }.AsMemory())
            };

            IServiceResponse response;
            if (transport == Transport.Rest)
            {
                using WebApiClient client = WebApiClient.Create(
                    m_restBase,
                    new WebApiClientOptions
                    {
                        HttpMessageHandler = CreateHandler(withCertificate),
                        DisposeHandler = true
                    });
                response = await client.ReadAsync(request).ConfigureAwait(false);
            }
            else
            {
                using HttpClient http = CreateHttpClient(withCertificate);
                response = await SendBinaryAsync(http, request).ConfigureAwait(false);
            }

            StatusCode serviceResult = response.ResponseHeader.ServiceResult;
            if (StatusCode.IsGood(serviceResult) &&
                response is ReadResponse read)
            {
                Assert.That(read.Results, Has.Count.EqualTo(1));
                return read.Results[0].StatusCode;
            }
            return serviceResult;
        }

        private async Task<IServiceResponse> SendBinaryAsync(HttpClient http, IServiceRequest request)
        {
            byte[] body = BinaryEncoder.EncodeMessage(request, m_server.MessageContext);
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (m_mutualTls)
            {
                content.Headers.TryAddWithoutValidation(
                    Profiles.HttpsSecurityPolicyHeader,
                    SecurityPolicies.Basic256Sha256);
            }
            using HttpResponseMessage reply = await http.PostAsync(m_binaryUrl, content).ConfigureAwait(false);
            byte[] data = await reply.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            Assert.That(data, Is.Not.Empty, $"HTTP {(int)reply.StatusCode}");
            return BinaryDecoder.DecodeMessage<IServiceResponse>(data, m_server.MessageContext);
        }

        private static RequestHeader CreateHeader(NodeId authenticationToken)
        {
            return new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = 1,
                TimeoutHint = kTimeout,
                AuthenticationToken = authenticationToken
            };
        }

        private HttpClient CreateHttpClient(bool withCertificate)
        {
            return new HttpClient(CreateHandler(withCertificate), disposeHandler: true);
        }

        private HttpClientHandler CreateHandler(bool withCertificate)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            if (withCertificate)
            {
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.ClientCertificates.Add(m_clientCertificate);
            }
            return handler;
        }

        private X509Certificate2 AcquireApplicationCertificate()
        {
            // The application certificate of the server fixture is trusted by
            // its own trust lists, so the TLS handshake accepts it as a client
            // certificate.
            ICertificateManager certificateManager = m_serverFixture.Config.CertificateManager
                ?? throw new AssertionException("The server configuration must expose a certificate manager.");
            using CertificateEntry entry = certificateManager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType)
                ?? throw new AssertionException("The server has no RSA application certificate.");
            using Certificate certificate = entry.Certificate!.AddRef();
            return X509CertificateLoader.LoadPkcs12(
                certificate.AsX509Certificate2().Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        private sealed class AccessTokenAuthenticator : Identity.IUserTokenAuthenticator
        {
            public AccessTokenAuthenticator(string accessToken)
            {
                m_accessToken = accessToken;
            }

            public UserTokenType TokenType => UserTokenType.IssuedToken;

            public string IssuedTokenProfileUri => Profiles.JwtUserToken;

            public ValueTask<Identity.AuthenticationResult> AuthenticateAsync(
                Identity.AuthenticationContext context,
                CancellationToken ct = default)
            {
                if (context.TokenHandler is IssuedIdentityTokenHandler issued &&
                    issued.DecryptedTokenData != null &&
                    System.Text.Encoding.UTF8.GetString(issued.DecryptedTokenData) == m_accessToken)
                {
                    return new ValueTask<Identity.AuthenticationResult>(
                        Identity.AuthenticationResult.Accept(new UserIdentity(issued)));
                }
                return new ValueTask<Identity.AuthenticationResult>(
                    Identity.AuthenticationResult.Reject(new ServiceResult(StatusCodes.BadIdentityTokenRejected)));
            }

            private readonly string m_accessToken;
        }
    }
}

#endif
