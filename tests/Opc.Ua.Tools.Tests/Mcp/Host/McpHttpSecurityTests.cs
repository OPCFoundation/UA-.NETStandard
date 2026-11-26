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

#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Host, Origin and bearer-token protection of the Streamable HTTP endpoint.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    [NonParallelizable]
    public sealed class McpHttpSecurityTests
    {
        private const string kToken = "0123456789abcdef0123456789abcdef-test";
        private const string kTokenVariable = "OPCUA_MCP_HTTP_TOKEN";
        private string? m_originalTokenVariable;

        [SetUp]
        public void SetUp()
        {
            // Read also consults the process environment; isolate tests from a developer's token.
            m_originalTokenVariable = Environment.GetEnvironmentVariable(kTokenVariable);
            Environment.SetEnvironmentVariable(kTokenVariable, null);
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable(kTokenVariable, m_originalTokenVariable);
        }
        private static readonly string[] s_loopback = ["localhost", "127.0.0.1", "[::1]"];
        private static readonly string[] s_configuredHosts = ["mcp.example", "localhost"];
        private static readonly string[] s_inspectorOrigin = ["http://localhost:6274"];

        [Test]
        public void ReadGeneratesUnguessableTokenWhenNoneConfigured()
        {
            McpHttpSecurityOptions first = McpHttpSecurityOptions.Read(Configuration());
            McpHttpSecurityOptions second = McpHttpSecurityOptions.Read(Configuration());

            Assert.That(first.TokenGenerated, Is.True);
            Assert.That(first.Token, Has.Length.GreaterThanOrEqualTo(McpHttpSecurityOptions.MinimumTokenLength));
            Assert.That(first.Token, Is.Not.EqualTo(second.Token));
            Assert.That(first.AllowedHosts, Is.EquivalentTo(s_loopback));
            Assert.That(first.AllowedOrigins, Is.Empty);
        }

        [Test]
        public void ReadUsesConfiguredTokenSubjectHostsAndOrigins()
        {
            McpHttpSecurityOptions options = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken),
                ("McpServer:Http:Subject", "ide"),
                ("McpServer:Http:AllowedHosts", "mcp.example; localhost"),
                ("McpServer:Http:AllowedOrigins:0", "http://localhost:6274/")));

            Assert.That(options.TokenGenerated, Is.False);
            Assert.That(options.Token, Is.EqualTo(kToken));
            Assert.That(options.Subject, Is.EqualTo("ide"));
            Assert.That(options.AllowedHosts, Is.EqualTo(s_configuredHosts));
            Assert.That(options.AllowedOrigins, Is.EqualTo(s_inspectorOrigin));
            Assert.That(McpHttpSecurity.StartupNotice(options, 5100), Does.Not.Contain(kToken));
        }

        [TestCase("short")]
        [TestCase("0123456789abcdef 0123456789abcdef-test")]
        public void ReadRejectsWeakToken(string token)
        {
            Assert.That(() => McpHttpSecurityOptions.Read(Configuration(("McpServer:Http:Token", token))),
                Throws.InvalidOperationException);
        }

        [TestCase("*")]
        [TestCase("http://localhost:6274/path")]
        [TestCase("file://x")]
        public void ReadRejectsMalformedOrigin(string origin)
        {
            Assert.That(() => McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:AllowedOrigins:0", origin))),
                Throws.InvalidOperationException);
        }

        [TestCase("localhost:5100", true)]
        [TestCase("LOCALHOST", true)]
        [TestCase("127.0.0.1:5100", true)]
        [TestCase("[::1]:5100", true)]
        [TestCase("attacker.example:5100", false)]
        [TestCase("localhost.attacker.example", false)]
        [TestCase("127.0.0.1.nip.io", false)]
        [TestCase("", false)]
        public void HostCheckAllowsOnlyLoopbackNamesByDefault(string host, bool allowed)
        {
            McpHttpSecurityOptions options = McpHttpSecurityOptions.Read(Configuration(("McpServer:Http:Token", kToken)));

            Assert.That(options.IsAllowedHost(new HostString(host)), Is.EqualTo(allowed));
        }

        [TestCase(null, true)]
        [TestCase("http://localhost:6274", true)]
        [TestCase("http://localhost:5100", false)]
        [TestCase("http://attacker.example", false)]
        [TestCase("null", false)]
        public void OriginCheckAllowsAbsentOrConfiguredOriginOnly(string? origin, bool allowed)
        {
            McpHttpSecurityOptions options = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:AllowedOrigins:0", "http://localhost:6274")));

            Assert.That(options.IsAllowedOrigin(origin), Is.EqualTo(allowed));
        }

        [Test]
        public void TokenComparisonIsExact()
        {
            McpHttpSecurityOptions options = McpHttpSecurityOptions.Read(Configuration(("McpServer:Http:Token", kToken)));

            Assert.That(options.TokenMatches(kToken), Is.True);
            Assert.That(options.TokenMatches(kToken + "x"), Is.False);
            Assert.That(options.TokenMatches(kToken[..^1]), Is.False);
            Assert.That(options.TokenMatches(kToken.ToUpperInvariant()), Is.False);
            Assert.That(options.TokenMatches(null), Is.False);
        }

        [TestCase(null, null, null, HttpStatusCode.Unauthorized)]
        [TestCase(null, null, "wrong-token-wrong-token-wrong-token", HttpStatusCode.Unauthorized)]
        [TestCase(null, null, kToken, HttpStatusCode.OK)]
        [TestCase("attacker.example", null, kToken, HttpStatusCode.BadRequest)]
        [TestCase(null, "http://attacker.example", kToken, HttpStatusCode.Forbidden)]
        [TestCase(null, "http://localhost:6274", kToken, HttpStatusCode.OK)]
        [TestCase("attacker.example", "http://attacker.example", null, HttpStatusCode.BadRequest)]
        public async Task PipelineRejectsRebindingCrossOriginAndUnauthenticatedRequestsAsync(
            string? host, string? origin, string? token, HttpStatusCode expected)
        {
            McpHttpSecurityOptions security = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:AllowedOrigins:0", "http://localhost:6274")));
            await using WebApplication app = await StartAsync(security).ConfigureAwait(false);
            using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            if (host is not null)
            {
                request.Headers.Host = host;
            }
            if (origin is not null)
            {
                request.Headers.Add("Origin", origin);
            }
            if (token is not null)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(expected));
            if (expected == HttpStatusCode.Unauthorized)
            {
                Assert.That(response.Headers.WwwAuthenticate.Select(value => value.Scheme), Does.Contain("Bearer"));
            }
            if (expected == HttpStatusCode.OK)
            {
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(body, Is.EqualTo("http-token|" + McpHttpSecurityOptions.Issuer));
            }
        }

        [TestCase("http://localhost:6274", HttpStatusCode.NoContent)]
        [TestCase("http://attacker.example", HttpStatusCode.Forbidden)]
        public async Task PreflightSucceedsOnlyForAllowedOriginWithoutTokenAsync(
            string origin, HttpStatusCode expected)
        {
            McpHttpSecurityOptions security = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:AllowedOrigins:0", "http://localhost:6274")));
            await using WebApplication app = await StartAsync(security).ConfigureAwait(false);
            using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
            using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,mcp-session-id");

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(expected));
            if (expected == HttpStatusCode.NoContent)
            {
                Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(), Is.EqualTo(origin));
                string allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
                Assert.That(allowedHeaders, Does.Contain("authorization").IgnoreCase);
                Assert.That(allowedHeaders, Does.Contain("mcp-session-id").IgnoreCase);
            }
        }

        [Test]
        public async Task AuthenticatedCrossOriginResponseCarriesCorsHeadersAsync()
        {
            McpHttpSecurityOptions security = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:AllowedOrigins:0", "http://localhost:6274")));
            await using WebApplication app = await StartAsync(security).ConfigureAwait(false);
            using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", "http://localhost:6274");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", kToken);

            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(),
                Is.EqualTo("http://localhost:6274"));
            Assert.That(string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers")),
                Does.Contain("Mcp-Session-Id"));
        }

        [Test]
        public async Task AuthenticatedPrincipalMapsToMcpCallerIdentityAsync()
        {
            McpHttpSecurityOptions security = McpHttpSecurityOptions.Read(Configuration(
                ("McpServer:Http:Token", kToken), ("McpServer:Http:Subject", "ide")));
            ClaimsPrincipal? captured = null;
            await using WebApplication app = await StartAsync(security, user => captured = user).ConfigureAwait(false);
            using var client = new HttpClient { BaseAddress = new Uri(Address(app)) };
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", kToken);

            using HttpResponseMessage response = await client.PostAsync(
                "/mcp", new StringContent("{}")).ConfigureAwait(false);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(McpCallerContext.FromPrincipal(captured),
                Is.EqualTo(new McpCallerIdentity("ide", McpHttpSecurityOptions.Issuer)));
        }

        private static async Task<WebApplication> StartAsync(
            McpHttpSecurityOptions security, Action<ClaimsPrincipal>? observe = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddMcpHttpSecurity(security);
            WebApplication app = builder.Build();
            app.UseMcpHttpSecurity(security);
            app.MapPost("/mcp", (HttpContext context) =>
            {
                observe?.Invoke(context.User);
                Claim subject = context.User.FindFirst(ClaimTypes.NameIdentifier)!;
                return subject.Value + "|" + subject.Issuer;
            }).RequireAuthorization();
            await app.StartAsync().ConfigureAwait(false);
            return app;
        }

        private static string Address(WebApplication app)
        {
            return app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.First();
        }

        private static IConfiguration Configuration(params (string Key, string Value)[] values)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(value =>
                    new KeyValuePair<string, string?>(value.Key, value.Value)))
                .Build();
        }
    }
}
#endif
