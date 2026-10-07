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
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    /// <summary>
    /// The <c>AddWebApi*Auth()</c> opt-ins register their schemes on the
    /// application container while the listener serves the REST routes
    /// from its own Kestrel host and container. These tests drive the
    /// opt-ins through a real <see cref="HttpsTransportListener"/> so a
    /// listener that does not enforce the configured credential fails.
    /// </summary>
    public sealed partial class RealHttpsListenerIntegrationTests
    {
        private const string kJwtIssuer = "https://issuer.example";
        private const string kJwtAudience = "opcua-rest";
        private static readonly byte[] s_jwtSigningKey = RandomNumberGenerator.GetBytes(64);
        private static readonly string[] s_expectedUsers = ["alice", "alice"];

        [TestCase("basic", "none", HttpStatusCode.Unauthorized)]
        [TestCase("basic", "basic-wrong", HttpStatusCode.Unauthorized)]
        [TestCase("basic", "bearer-valid", HttpStatusCode.Unauthorized)]
        [TestCase("basic", "basic-valid", HttpStatusCode.OK)]
        [TestCase("bearer", "none", HttpStatusCode.Unauthorized)]
        [TestCase("bearer", "bearer-bogus", HttpStatusCode.Unauthorized)]
        [TestCase("bearer", "bearer-forged", HttpStatusCode.Unauthorized)]
        [TestCase("bearer", "basic-valid", HttpStatusCode.Unauthorized)]
        [TestCase("bearer", "bearer-valid", HttpStatusCode.OK)]
        [TestCase("mtls", "none", HttpStatusCode.Unauthorized)]
        [TestCase("mtls", "basic-valid", HttpStatusCode.Unauthorized)]
        [TestCase("mtls", "client-cert", HttpStatusCode.OK)]
        public async Task AuthOptInIsEnforcedByRealHttpsListenerAsync(
            string authMode,
            string credential,
            HttpStatusCode expectedStatus)
        {
            using X509Certificate2? clientCertificate = credential == "client-cert"
                ? CreateClientCertificate()
                : null;
            await using AuthListener listener = await OpenAuthListenerAsync(authMode, clientCertificate)
                .ConfigureAwait(false);

            using HttpResponseMessage response = await listener
                .PostReadAsync(CreateAuthorizationHeader(credential))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            if (expectedStatus == HttpStatusCode.OK)
            {
                Assert.That(listener.Callback.LastRequest, Is.InstanceOf<ReadRequest>());
                return;
            }

            Assert.That(listener.Callback.LastRequest, Is.Null,
                "A request without a valid credential must not reach the server.");
            string[] challenges = [.. response.Headers.WwwAuthenticate.Select(h => h.Scheme)];
            switch (authMode)
            {
                case "basic":
                    Assert.That(challenges, Does.Contain("Basic"));
                    break;
                case "bearer":
                    Assert.That(challenges, Does.Contain("Bearer"));
                    break;
            }
        }

        [TestCase("basic", "none", HttpStatusCode.Unauthorized)]
        [TestCase("basic", "basic-wrong", HttpStatusCode.Unauthorized)]
        [TestCase("basic", "basic-valid", HttpStatusCode.OK)]
        [TestCase("bearer", "none", HttpStatusCode.Unauthorized)]
        [TestCase("bearer", "bearer-valid", HttpStatusCode.OK)]
        public async Task OpenApiDocumentIsEnforcedLikeTheServiceRoutesOnRealHttpsListenerAsync(
            string authMode,
            string credential,
            HttpStatusCode expectedStatus)
        {
            await using AuthListener listener = await OpenAuthListenerAsync(
                authMode,
                configureWebApi: options => options.OpenApiDocumentPath = "/openapi.json")
                .ConfigureAwait(false);

            using HttpResponseMessage response = await listener
                .GetAsync("/openapi.json", CreateAuthorizationHeader(credential))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
            if (expectedStatus == HttpStatusCode.OK)
            {
                string document = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(document, Does.Contain("\"operationId\":\"Read\""));
                return;
            }

            string[] challenges = [.. response.Headers.WwwAuthenticate.Select(h => h.Scheme)];
            Assert.That(challenges, Does.Contain(authMode == "basic" ? "Basic" : "Bearer"));
        }

        [Test]
        public async Task OpenApiDocumentIsNotServedUnlessConfiguredOnRealHttpsListenerAsync()
        {
            await using AuthListener listener = await OpenAuthListenerAsync("basic").ConfigureAwait(false);

            using HttpResponseMessage response = await listener
                .GetAsync("/openapi.json", CreateAuthorizationHeader("basic-valid"))
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task CustomBearerChallengeIsNotOverwrittenOnRealHttpsListenerAsync()
        {
            await using AuthListener listener = await OpenAuthListenerAsync("bearer-custom-challenge")
                .ConfigureAwait(false);

            using HttpResponseMessage response = await listener
                .PostReadAsync(authorization: null)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(body, Is.EqualTo("custom challenge"));
            Assert.That(listener.Callback.LastRequest, Is.Null);
        }

        [Test]
        public async Task ListenerHostWithoutReplayedSchemeFailsClosedAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(m_telemetry!);
            services.AddOpcUa()
                .AddWebApiTransport()
                .AddWebApiBasicAuth((_, _) => Task.FromResult<ClaimsPrincipal?>(null));
            await using ServiceProvider provider = services.BuildServiceProvider();
            WebApiHttpsStartupContributor contributor =
                provider.GetRequiredService<WebApiHttpsStartupContributor>();

            // A listener host whose services never went through the
            // contributor's ConfigureServices (no replayed scheme).
            await using ServiceProvider listenerServices = new ServiceCollection()
                .AddLogging()
                .AddRouting()
                .BuildServiceProvider();
            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(contributor);
            await using var listener = (HttpsTransportListener)factory.Create(m_telemetry!);

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
                () => contributor.Configure(new ApplicationBuilder(listenerServices), listener));
            Assert.That(ex!.Message, Does.Contain(Opc.Ua.Bindings.WebApi.Authentication.WebApiAuthSchemes.Basic));
        }

        [Test]
        public async Task SharedHostSettingsDistinguishContributorInstancesAsync()
        {
            IServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(m_telemetry!);
            var first = new WebApiHttpsStartupContributor(new WebApiServer(messageContext, "first"));
            var second = new WebApiHttpsStartupContributor(new WebApiServer(messageContext, "second"));

            await using HttpsTransportListener firstListener = CreateListener(first);
            await using HttpsTransportListener sameListener = CreateListener(first);
            await using HttpsTransportListener otherListener = CreateListener(second);

            Assert.That(
                sameListener.GetSharedHostSettings(),
                Is.EqualTo(firstListener.GetSharedHostSettings()),
                "Listeners wired with the same contributor instance may share a host.");
            Assert.That(
                otherListener.GetSharedHostSettings(),
                Is.Not.EqualTo(firstListener.GetSharedHostSettings()),
                "A shared host only registers the first listener's contributor services, so " +
                "a listener with another contributor instance (e.g. other REST auth) must not share it.");

            HttpsTransportListener CreateListener(WebApiHttpsStartupContributor contributor)
            {
                var factory = new HttpsTransportListenerFactory();
                factory.StartupContributors.Add(contributor);
                return (HttpsTransportListener)factory.Create(m_telemetry!);
            }
        }

        [Test]
        public async Task ScopedIdentityProviderResolvesPerRequestOnRealHttpsListenerAsync()
        {
            var recorder = new IdentityProviderRecorder();
            await using AuthListener listener = await OpenAuthListenerAsync(
                "basic",
                configureServices: services =>
                {
                    services.AddSingleton(recorder);
                    services.AddScoped<ScopedDependency>();
                    services.AddScoped<ISessionlessIdentityProvider, RecordingIdentityProvider>();
                }).ConfigureAwait(false);
            AuthenticationHeaderValue? credential = CreateAuthorizationHeader("basic-valid");

            for (int ii = 0; ii < 2; ii++)
            {
                using HttpResponseMessage response = await listener
                    .PostReadAsync(credential)
                    .ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }

            Assert.That(recorder.Users, Is.EqualTo(s_expectedUsers),
                "The application's provider must see the principal authenticated on the listener.");
            Assert.That(recorder.Dependencies.Distinct().Count(), Is.EqualTo(2),
                "A scoped provider must get a fresh scope per request.");
            // The request scope is disposed after the response is sent.
            Assert.That(() => recorder.Dependencies.All(d => d.Disposed), Is.True.After(5000, 50),
                "The per-request application scope must be disposed with the request.");
        }

        [Test]
        public async Task DiscoveryStaysAnonymousWhenAuthIsEnabledOnRealHttpsListenerAsync()
        {
            await using AuthListener listener = await OpenAuthListenerAsync("basic").ConfigureAwait(false);

            using HttpResponseMessage response = await listener
                .PostAsync(
                    "/findservers",
                    new FindServersRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 1, Timestamp = DateTime.UtcNow }
                    },
                    authorization: null)
                .ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(listener.Callback.LastRequest, Is.InstanceOf<FindServersRequest>());
        }

        private async Task<AuthListener> OpenAuthListenerAsync(
            string authMode,
            X509Certificate2? clientCertificate = null,
            Action<IServiceCollection>? configureServices = null,
            Action<WebApiTransportOptions>? configureWebApi = null)
        {
            var services = new ServiceCollection();
            configureServices?.Invoke(services);
            services.AddLogging();
            services.AddSingleton(m_telemetry!);
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWebApiTransport(configureWebApi);
            switch (authMode)
            {
                case "basic":
                    builder.AddWebApiBasicAuth((user, password) => Task.FromResult(
                        user == "alice" && password == "secret"
                            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], "Basic"))
                            : null));
                    break;
                case "bearer":
                    builder.AddWebApiBearerAuth(o =>
                    {
                        o.RequireHttpsMetadata = false;
                        o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidIssuer = kJwtIssuer,
                            ValidAudience = kJwtAudience,
                            IssuerSigningKey = new SymmetricSecurityKey(s_jwtSigningKey)
                        };
                    });
                    break;
                case "bearer-custom-challenge":
                    builder.AddWebApiBearerAuth(o =>
                    {
                        o.RequireHttpsMetadata = false;
                        o.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidIssuer = kJwtIssuer,
                            ValidAudience = kJwtAudience,
                            IssuerSigningKey = new SymmetricSecurityKey(s_jwtSigningKey)
                        };
                        // A handler that writes the challenge response
                        // itself, after which the status is read-only.
                        o.Events = new JwtBearerEvents
                        {
                            OnChallenge = async context =>
                            {
                                context.HandleResponse();
                                await context.Response.WriteAsync("custom challenge").ConfigureAwait(false);
                                await context.Response.Body.FlushAsync().ConfigureAwait(false);
                            }
                        };
                    });
                    break;
                case "mtls":
                    builder.AddWebApiMutualTlsAuth(o =>
                    {
                        o.AllowedCertificateTypes = CertificateTypes.SelfSigned;
                        o.RevocationMode = X509RevocationMode.NoCheck;
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(authMode));
            }

            // Wire the listener from the application container exactly as
            // AddWebApiTransport() does for the registered HTTPS factories.
            ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });
            var callback = new StubTransportListenerCallback();
            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(provider.GetRequiredService<WebApiHttpsStartupContributor>());
            HttpsTransportListener listener;
            int port;
            try
            {
                (listener, port) = await OpenListenerOnFreePortAsync(
                    () => (HttpsTransportListener)factory.Create(m_telemetry!),
                    p => CreateListenerSettings(m_certificateRegistry!, p, mutualTls: authMode == "mtls"),
                    callback).ConfigureAwait(false);
            }
            catch
            {
                await provider.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            var result = new AuthListener(
                provider,
                provider.GetRequiredService<WebApiServer>(),
                listener,
                callback);
            try
            {
                await WaitForListenerReadyAsync(port).ConfigureAwait(false);
                result.Connect(port, clientCertificate);
                return result;
            }
            catch
            {
                await result.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private static AuthenticationHeaderValue? CreateAuthorizationHeader(string credential)
        {
            return credential switch
            {
                "none" or "client-cert" => null,
                "basic-valid" => new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret"))),
                "basic-wrong" => new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:wrong"))),
                "bearer-valid" => new AuthenticationHeaderValue("Bearer", CreateJwt(s_jwtSigningKey)),
                "bearer-forged" => new AuthenticationHeaderValue(
                    "Bearer", CreateJwt(RandomNumberGenerator.GetBytes(64))),
                "bearer-bogus" => new AuthenticationHeaderValue("Bearer", "not.a.jwt"),
                _ => throw new ArgumentOutOfRangeException(nameof(credential))
            };
        }

        private static string CreateJwt(byte[] signingKey)
        {
            DateTime now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: kJwtIssuer,
                audience: kJwtAudience,
                claims: [new Claim(JwtRegisteredClaimNames.Sub, "alice")],
                notBefore: now.AddMinutes(-1),
                expires: now.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(signingKey),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static X509Certificate2 CreateClientCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=webapi-mtls-client",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], critical: false));
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddHours(1));
            // Reload from PFX so SChannel can use the private key for client auth.
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pfx),
                password: null,
                keyStorageFlags: X509KeyStorageFlags.Exportable);
        }

        private sealed class IdentityProviderRecorder
        {
            public ConcurrentQueue<string?> Users { get; } = new();
            public ConcurrentQueue<ScopedDependency> Dependencies { get; } = new();
        }

        // Instantiated by the DI container through AddScoped registrations.
        // TODO: Remove the pragma when CA1812 tracks DI registrations.
#pragma warning disable CA1812
        private sealed class ScopedDependency : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class RecordingIdentityProvider : ISessionlessIdentityProvider
        {
            private readonly IdentityProviderRecorder m_recorder;
            private readonly ScopedDependency m_dependency;

            public RecordingIdentityProvider(IdentityProviderRecorder recorder, ScopedDependency dependency)
            {
                m_recorder = recorder;
                m_dependency = dependency;
            }

            public IUserIdentity? Resolve(HttpContext context)
            {
                m_recorder.Users.Enqueue(context.User.Identity?.Name);
                m_recorder.Dependencies.Enqueue(m_dependency);
                return null;
            }
        }
#pragma warning restore CA1812

        /// <summary>
        /// A listener whose REST authentication comes from an application
        /// container configured the way an operator would.
        /// </summary>
        private sealed class AuthListener : IAsyncDisposable
        {
            private readonly ServiceProvider m_provider;
            private readonly WebApiServer m_server;
            private readonly HttpsTransportListener m_listener;
            private HttpClientHandler? m_handler;
            private HttpClient? m_client;

            public AuthListener(
                ServiceProvider provider,
                WebApiServer server,
                HttpsTransportListener listener,
                StubTransportListenerCallback callback)
            {
                m_provider = provider;
                m_server = server;
                m_listener = listener;
                Callback = callback;
            }

            public StubTransportListenerCallback Callback { get; }

            public void Connect(int port, X509Certificate2? clientCertificate)
            {
                m_handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
                };
                if (clientCertificate != null)
                {
                    m_handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                    m_handler.ClientCertificates.Add(clientCertificate);
                }
                m_client = new HttpClient(m_handler)
                {
                    BaseAddress = new Uri($"https://localhost:{port}/")
                };
            }

            public Task<HttpResponseMessage> PostReadAsync(AuthenticationHeaderValue? authorization)
            {
                return PostAsync(
                    "/read",
                    new ReadRequest
                    {
                        RequestHeader = new RequestHeader { RequestHandle = 1, Timestamp = DateTime.UtcNow }
                    },
                    authorization);
            }

            public async Task<HttpResponseMessage> GetAsync(
                string path,
                AuthenticationHeaderValue? authorization)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, path);
                message.Headers.Authorization = authorization;
                return await m_client!.SendAsync(message, HttpCompletionOption.ResponseContentRead)
                    .ConfigureAwait(false);
            }

            public async Task<HttpResponseMessage> PostAsync(
                string path,
                IServiceRequest request,
                AuthenticationHeaderValue? authorization)
            {
                byte[] body = WebApiBodyCodec.EncodeBody(
                    (IEncodeable)request,
                    m_server.MessageContext,
                    WebApiMediaType.ToEncoderOptions(WebApiEncoding.Compact));
                using var content = new ByteArrayContent(body);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    WebApiMediaType.FormatContentType(WebApiEncoding.Compact));
                using var message = new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = content
                };
                message.Headers.Authorization = authorization;
                return await m_client!.SendAsync(message, HttpCompletionOption.ResponseContentRead)
                    .ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                m_client?.Dispose();
                m_handler?.Dispose();
                await m_listener.CloseAsync().ConfigureAwait(false);
                await m_listener.DisposeAsync().ConfigureAwait(false);
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
