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
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Http;
using Opc.Ua.WotCon.Binding.Planners;
using Opc.Ua.WotCon.Binding.Tests.Support;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// End-to-end tests for the HTTP executor's redirect-safe credential policy:
    /// the executor-owned client disables automatic redirects and applies a
    /// bounded, origin-aware redirect policy that drops custom header / query
    /// credentials across origins, refuses loops and unsafe schemes, and honours a
    /// redirect limit. A caller-supplied client with a credential-bearing form
    /// fails closed unless the caller confirms safe redirect handling.
    /// </summary>
    [TestFixture]
    public sealed class HttpRedirectSecurityTests
    {
        private const string SecuredTdTemplate =
            "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
            "\"securityDefinitions\":{\"apikey_sc\":{\"scheme\":\"apikey\",\"in\":\"query\",\"name\":\"token\"}}," +
            "\"security\":\"apikey_sc\"," +
            "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"{HREF}\"}]}}}";

        private static WotProtocolBinderRegistry OwnedRegistry(
            IWotCredentialProvider? credentials = null, HttpWotBindingOptions? options = null)
            => new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[] { new HttpWotBindingExecutor(options ?? new HttpWotBindingOptions()) },
                credentials: credentials);

        private static WotCompiledForm ReadForm(WotProtocolBinderRegistry registry, string href)
        {
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(SecuredTdTemplate.Replace("{HREF}", href, StringComparison.Ordinal))));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
        }

        [Test]
        public async Task CrossOriginRedirect_DropsHeaderAndQueryCredentials()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "7");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return TestHttpResponse.Redirect(targetServer.BaseUrl + "/p");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry(new HeaderQueryCredentialProvider());
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadForm(registry, originServer.BaseUrl + "/p"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.True, "The read must follow the redirect and succeed.");
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(7L));
            }

            Assert.Multiple(() =>
            {
                Assert.That(origin.SawQueryToken, Is.True, "The origin request must carry the query credential.");
                Assert.That(origin.SawHeaderToken, Is.True, "The origin request must carry the header credential.");
                Assert.That(target.SawQueryToken, Is.False,
                    "A cross-origin redirect must not forward the query credential.");
                Assert.That(target.SawHeaderToken, Is.False,
                    "A cross-origin redirect must not forward the header credential.");
            });
        }

        [Test]
        public async Task SameOriginRedirect_KeepsCredentials()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                if (request.Path.StartsWith("/a", StringComparison.Ordinal))
                {
                    return TestHttpResponse.Redirect("/b");
                }
                return TestHttpResponse.Json(200, "9");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry(new HeaderQueryCredentialProvider());
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadForm(registry, server.BaseUrl + "/a"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(9L));
            }

            Assert.That(recorder.PathsSeen.Any(p => p.StartsWith("/b", StringComparison.Ordinal) &&
                p.Contains("token=secret", StringComparison.Ordinal)), Is.True,
                "A same-origin redirect must keep the query credential on the follow-up request.");
        }

        [Test]
        public async Task RedirectLoop_IsRejected()
        {
            using var server = new TestHttpServer(request =>
                request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? TestHttpResponse.Redirect("/b")
                    : TestHttpResponse.Redirect("/a"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadFormNoSecurity(registry, server.BaseUrl + "/a"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("loop").IgnoreCase);
            }
        }

        [Test]
        public async Task RedirectToDisallowedScheme_IsRejected()
        {
            using var server = new TestHttpServer(_ => TestHttpResponse.Redirect("ftp://evil.example.com/x"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadFormNoSecurity(registry, server.BaseUrl + "/p"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo((StatusCode)StatusCodes.BadSecurityChecksFailed));
            }
        }

        [Test]
        public async Task RedirectLimit_IsEnforced()
        {
            int counter = 0;
            using var server = new TestHttpServer(_ =>
                TestHttpResponse.Redirect("/r" + Interlocked.Increment(ref counter)));

            WotProtocolBinderRegistry registry = OwnedRegistry(
                options: new HttpWotBindingOptions { MaxAutomaticRedirects = 2 });
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadFormNoSecurity(registry, server.BaseUrl + "/start"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("redirect limit").IgnoreCase);
            }
        }

        [Test]
        public async Task OwnedClient_FollowsTemporaryRedirect_ToSuccess()
        {
            using var server = new TestHttpServer(request =>
                request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? TestHttpResponse.Redirect("/final", status: 307)
                    : TestHttpResponse.Json(200, "5"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadFormNoSecurity(registry, server.BaseUrl + "/a"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(5L));
            }
        }

        [Test]
        public void CallerSuppliedClient_WithCredentialForm_FailsClosed()
        {
            using var server = new TestHttpServer((method, path, body) => TestHttpResponse.Json(200, "1"));
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[]
                {
                    new HttpWotBindingExecutor(new HttpWotBindingOptions { ClientFactory = () => new HttpClient() })
                },
                credentials: new HeaderQueryCredentialProvider());
            WotCompiledForm read = ReadForm(registry, server.BaseUrl + "/p");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(read).ConfigureAwait(false));
        }

        [Test]
        public async Task CallerSuppliedClient_WithCredentialForm_AllowedWhenSafetyConfirmed()
        {
            using var server = new TestHttpServer(request => TestHttpResponse.Json(200, "1"));
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[]
                {
                    new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => new HttpClient(new HttpClientHandler
                        {
                            AllowAutoRedirect = false,
                            CheckCertificateRevocationList = true
                        }),
                        CallerClientHandlesRedirectSafety = true
                    })
                },
                credentials: new HeaderQueryCredentialProvider());

            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadForm(registry, server.BaseUrl + "/p"));
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync();
                Assert.That(result.Success, Is.True);
            }
        }

        private static WotCompiledForm ReadFormNoSecurity(WotProtocolBinderRegistry registry, string href)
        {
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" + href + "\"}]}}}";
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
        }

        private sealed class Recorder
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> m_paths = new();
            private int m_sawQueryToken;
            private int m_sawHeaderToken;

            public bool SawQueryToken => Volatile.Read(ref m_sawQueryToken) != 0;

            public bool SawHeaderToken => Volatile.Read(ref m_sawHeaderToken) != 0;

            public System.Collections.Generic.IReadOnlyCollection<string> PathsSeen => m_paths.ToArray();

            public void Record(TestHttpRequest request)
            {
                m_paths.Enqueue(request.Path);
                if (request.Path.Contains("token=secret", StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref m_sawQueryToken, 1);
                }
                if (request.Headers.TryGetValue("X-Api-Key", out string? value) &&
                    string.Equals(value, "secret", StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref m_sawHeaderToken, 1);
                }
            }
        }

        private sealed class HeaderQueryCredentialProvider : IWotCredentialProvider
        {
            public ValueTask<WotCredential?> ResolveAsync(
                WotCredentialReference reference, CancellationToken cancellationToken = default)
                => new ValueTask<WotCredential?>(new WotCredential(
                    WotSecurityScheme.ApiKey,
                    headers: ImmutableDictionary<string, string>.Empty.Add("X-Api-Key", "secret"),
                    queryParameters: ImmutableDictionary<string, string>.Empty.Add("token", "secret")));
        }
    }
}
