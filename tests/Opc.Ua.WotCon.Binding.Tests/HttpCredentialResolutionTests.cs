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

namespace Opc.Ua.WotCon.Binding.Tests
{
    /// <summary>
    /// Tests that HTTP credential resolution is race-free: concurrent requests on
    /// a single channel resolve the credential exactly once and never send a
    /// request before the credential is applied, and a failed resolution is
    /// retried on the next request.
    /// </summary>
    [TestFixture]
    public sealed class HttpCredentialResolutionTests
    {
        private const string SecuredTd =
            "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
            "\"securityDefinitions\":{\"apikey_sc\":{\"scheme\":\"apikey\",\"in\":\"query\",\"name\":\"token\"}}," +
            "\"security\":\"apikey_sc\"," +
            "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"{BASE}/p\"}]}}}";

        private static WotCompiledForm ReadForm(WotProtocolBinderRegistry registry, string baseUrl)
        {
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(SecuredTd.Replace("{BASE}", baseUrl, StringComparison.Ordinal))));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
        }

        [Test]
        public async Task ConcurrentRequests_NeverSendUnauthenticated_AndResolveOnce()
        {
            var authenticated = new ConcurrentQueue<bool>();
            using var server = new TestHttpServer((method, path, body) =>
            {
                // The resolved API key is carried as a query parameter, so an
                // authenticated request has "token=secret" in its target.
                authenticated.Enqueue(path.Contains("token=secret", StringComparison.Ordinal));
                return TestHttpResponse.Json(200, "1");
            });

            var credentials = new SlowQueryCredentialProvider(TimeSpan.FromMilliseconds(150));
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[]
                {
                    new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => new HttpClient(),
                        CallerClientHandlesRedirectSafety = true
                    })
                },
                credentials: credentials);

            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadForm(registry, server.BaseUrl))
                .ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                Task<WotReadResult>[] reads = Enumerable.Range(0, 12)
                    .Select(_ => channel.ReadAsync().AsTask())
                    .ToArray();
                WotReadResult[] results = await Task.WhenAll(reads).ConfigureAwait(false);
                Assert.That(results.All(r => r.Success), Is.True, "Every concurrent read must succeed.");
            }

            Assert.That(authenticated.Count, Is.EqualTo(12));
            Assert.That(authenticated.All(a => a), Is.True,
                "No request may be sent before the credential is resolved and applied.");
            Assert.That(credentials.ResolveCount, Is.EqualTo(1),
                "The credential must be resolved exactly once and shared across concurrent requests.");
        }

        [Test]
        public async Task CredentialResolutionFailure_IsRetriedOnNextRequest()
        {
            using var server = new TestHttpServer((method, path, body) => TestHttpResponse.Json(200, "1"));

            var credentials = new FailOnceCredentialProvider();
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new HttpBindingPlanner() },
                new IWotBindingExecutor[]
                {
                    new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => new HttpClient(),
                        CallerClientHandlesRedirectSafety = true
                    })
                },
                credentials: credentials);

            IWotBindingChannel channel = await registry.OpenChannelAsync(ReadForm(registry, server.BaseUrl))
                .ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                // The first resolution faults and must surface, not be cached.
                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await channel.ReadAsync().ConfigureAwait(false));

                // The next request retries resolution and succeeds.
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.That(credentials.ResolveCount, Is.EqualTo(2),
                "A failed resolution must not be cached; the next request retries it.");
        }

        private sealed class SlowQueryCredentialProvider : IWotCredentialProvider
        {
            public SlowQueryCredentialProvider(TimeSpan delay) => m_delay = delay;

            public int ResolveCount => Volatile.Read(ref m_resolveCount);

            public async ValueTask<WotCredential?> ResolveAsync(
                WotCredentialReference reference, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_resolveCount);
                await Task.Delay(m_delay, cancellationToken).ConfigureAwait(false);
                return new WotCredential(
                    WotSecurityScheme.ApiKey,
                    queryParameters: ImmutableDictionary<string, string>.Empty.Add("token", "secret"));
            }

            private readonly TimeSpan m_delay;
            private int m_resolveCount;
        }

        private sealed class FailOnceCredentialProvider : IWotCredentialProvider
        {
            public int ResolveCount => Volatile.Read(ref m_resolveCount);

            public ValueTask<WotCredential?> ResolveAsync(
                WotCredentialReference reference, CancellationToken cancellationToken = default)
            {
                if (Interlocked.Increment(ref m_resolveCount) == 1)
                {
                    throw new InvalidOperationException("Transient credential resolution failure.");
                }
                return new ValueTask<WotCredential?>(new WotCredential(
                    WotSecurityScheme.ApiKey,
                    queryParameters: ImmutableDictionary<string, string>.Empty.Add("token", "secret")));
            }

            private int m_resolveCount;
        }
    }
}
