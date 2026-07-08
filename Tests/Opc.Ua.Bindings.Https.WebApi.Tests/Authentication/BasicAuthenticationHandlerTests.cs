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
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Bindings.WebApi.Authentication;

namespace Opc.Ua.Bindings.Https.WebApi.Tests.Authentication
{
    /// <summary>
    /// Unit tests for the RFC 7617 Basic authentication handler used by
    /// the REST binding.
    /// </summary>
    [TestFixture]
    [Category("WebApiAuthentication")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BasicAuthenticationHandlerTests
    {
        [Test]
        public async Task MissingAuthorizationHeaderReturnsNoResultLeavingRequestAnonymous()
        {
            using IHost host = await CreateHostAsync().ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative))
                .ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.Empty);
        }

        [Test]
        public async Task RequireHttpsRejectsPlainHttp()
        {
            using IHost host = await CreateHostAsync(options =>
            {
                options.RequireHttps = true;
                options.ValidateCredentials = AcceptAliceAsync;
            }).ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = CreateBasicHeader("alice", "secret");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task MalformedBase64ReturnsFail()
        {
            using IHost host = await CreateHostAsync().ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "!@#");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task MalformedCredentialsReturnsFail()
        {
            using IHost host = await CreateHostAsync().ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolonseparator")));

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task ValidCredentialsProduceAuthenticatedPrincipal()
        {
            using IHost host = await CreateHostAsync(options =>
            {
                options.ValidateCredentials = AcceptAliceAsync;
            }).ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = CreateBasicHeader("alice", "secret");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body, Is.EqualTo("alice"));
        }

        [Test]
        public async Task WrongPasswordReturnsFail()
        {
            using IHost host = await CreateHostAsync(
                options => options.ValidateCredentials =
                    (_, _) => Task.FromResult<ClaimsPrincipal?>(null)).ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = CreateBasicHeader("alice", "wrong");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task WwwAuthenticateChallengeIncludesRealm()
        {
            using IHost host = await CreateHostAsync().ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", "!@#");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);
            string challenge = response.Headers.WwwAuthenticate.First().ToString();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(challenge, Does.Contain("Basic realm=\"OPC UA REST\", charset=\"UTF-8\""));
        }

        [Test]
        public async Task MissingValidateCredentialsCallbackReturnsFail()
        {
            using IHost host = await CreateHostAsync().ConfigureAwait(false);
            using HttpClient client = host.GetTestClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Authorization = CreateBasicHeader("alice", "secret");

            HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        private static Task<IHost> CreateHostAsync(Action<BasicAuthenticationOptions>? configure = null)
        {
            IHostBuilder hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddAuthentication("Test")
                            .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
                                "Test",
                                options =>
                                {
                                    options.RequireHttps = false;
                                    configure?.Invoke(options);
                                });
                        services.AddAuthorization();
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.Use(ChallengeFailedAuthenticationAsync);
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/", (HttpContext context) =>
                                Results.Text(context.User.Identity?.Name ?? string.Empty));
                        });
                    });
                });

            return hostBuilder.StartAsync();
        }

        private static async Task ChallengeFailedAuthenticationAsync(
            HttpContext context,
            Func<Task> next)
        {
            AuthenticateResult result = await context.AuthenticateAsync("Test").ConfigureAwait(false);
            if (result.Failure != null)
            {
                await context.ChallengeAsync("Test").ConfigureAwait(false);
                return;
            }

            if (result.Principal != null)
            {
                context.User = result.Principal;
            }

            await next().ConfigureAwait(false);
        }

        private static AuthenticationHeaderValue CreateBasicHeader(string username, string password)
        {
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            return new AuthenticationHeaderValue("Basic", encoded);
        }

        private static Task<ClaimsPrincipal?> AcceptAliceAsync(string username, string password)
        {
            if (username == "alice" && password == "secret")
            {
                ClaimsIdentity identity = new(
                    [new Claim(ClaimTypes.Name, "alice")],
                    "Test");
                return Task.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
            }

            return Task.FromResult<ClaimsPrincipal?>(null);
        }
    }
}
