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
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
        public async Task AuthOptInIsEnforcedByRealHttpsListenerAsync(
            string authMode,
            string credential,
            HttpStatusCode expectedStatus)
        {
            await using AuthListener listener = await OpenAuthListenerAsync(authMode).ConfigureAwait(false);

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

        private async Task<AuthListener> OpenAuthListenerAsync(string authMode)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(m_telemetry!);
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddWebApiTransport();
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
                case "mtls":
                    builder.AddWebApiMutualTlsAuth();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(authMode));
            }

            // Wire the listener from the application container exactly as
            // AddWebApiTransport() does for the registered HTTPS factories.
            ServiceProvider provider = services.BuildServiceProvider();
            var callback = new StubTransportListenerCallback();
            var factory = new HttpsTransportListenerFactory();
            factory.StartupContributors.Add(provider.GetRequiredService<WebApiHttpsStartupContributor>());
            var listener = (HttpsTransportListener)factory.Create(m_telemetry!);
            var result = new AuthListener(
                provider,
                provider.GetRequiredService<WebApiServer>(),
                listener,
                callback);
            try
            {
                int port = FindAvailableTcpPort();
                await listener.OpenAsync(
                    new Uri($"https://localhost:{port}/"),
                    CreateListenerSettings(m_certificateRegistry!, port),
                    callback).ConfigureAwait(false);
                await WaitForListenerReadyAsync(port).ConfigureAwait(false);
                result.Connect(port);
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
                "none" => null,
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

            public void Connect(int port)
            {
                m_handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
                };
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
