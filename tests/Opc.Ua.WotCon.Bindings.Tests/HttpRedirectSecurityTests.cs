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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings.Http;
using Opc.Ua.WotCon.Bindings.Planners;
using Opc.Ua.WotCon.Bindings.Tests.Support;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// End-to-end tests for the HTTP executor's redirect-safe credential policy:
    /// the executor-owned client disables automatic redirects and ambient cookies,
    /// then applies a bounded, origin-aware redirect policy that drops custom header
    /// / query credentials across origins, refuses loops and unsafe schemes, and
    /// honours a redirect limit. Default headers follow the same sensitive-data
    /// policy and are snapshotted per channel. Every caller-supplied client fails
    /// closed unless the caller confirms safe redirect handling.
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
        {
            return new WotProtocolBinderRegistry(
                        [new HttpBindingPlanner()],
                        [new HttpWotBindingExecutor(options ?? new HttpWotBindingOptions())],
                        credentials: credentials,
                        endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
        }

        private static WotCompiledForm ReadForm(WotProtocolBinderRegistry registry, string href)
        {
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription,
                Encoding.UTF8.GetBytes(SecuredTdTemplate.Replace("{HREF}", href, StringComparison.Ordinal))));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
        }

        [Test]
        public async Task CrossOriginRedirectDropsHeaderAndQueryCredentials()
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
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadForm(registry, originServer.BaseUrl + "/p")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
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
        public async Task CrossOriginRedirectDropsDefaultHeaders()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "8");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return TestHttpResponse.Redirect(targetServer.BaseUrl + "/p");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry(
                options: DefaultHeaderOptions());
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, originServer.BaseUrl + "/p")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True, "The read must follow the redirect and succeed.");
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(8L));
            }

            Assert.Multiple(() =>
            {
                Assert.That(origin.SawDefaultHeaderToken, Is.True,
                    "The origin request must carry the configured default header.");
                Assert.That(target.SawDefaultHeaderToken, Is.False,
                    "A cross-origin redirect must not forward the configured default header.");
            });
        }

        [Test]
        public async Task CrossOriginRedirectDoesNotReplaySetCookie()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "17");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return RedirectWithCookie(targetServer.BaseUrl + "/p");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, originServer.BaseUrl + "/p")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(17L));
            }

            Assert.Multiple(() =>
            {
                Assert.That(origin.SawHeaderContaining("Cookie", "session=secret"), Is.False);
                Assert.That(target.SawHeaderContaining("Cookie", "session=secret"), Is.False,
                    "An ambient cookie from one origin must not cross to another port.");
            });
        }

        [Test]
        public async Task SameOriginRedirectKeepsCredentials()
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
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadForm(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(9L));
            }

            Assert.That(recorder.PathsSeen.Any(p => p.StartsWith("/b", StringComparison.Ordinal) &&
                p.Contains("token=secret", StringComparison.Ordinal)), Is.True,
                "A same-origin redirect must keep the query credential on the follow-up request.");
        }

        [Test]
        public async Task SameOriginRedirectKeepsDefaultHeaders()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                if (request.Path.StartsWith("/a", StringComparison.Ordinal))
                {
                    return TestHttpResponse.Redirect("/b");
                }
                return TestHttpResponse.Json(200, "10");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry(
                options: DefaultHeaderOptions());
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(10L));
            }

            Assert.That(recorder.PathsWithDefaultHeader.Any(
                p => p.StartsWith("/b", StringComparison.Ordinal)), Is.True,
                "A same-origin redirect must keep the configured default header.");
        }

        [Test]
        public async Task SameOriginRedirectDoesNotReplaySetCookie()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? RedirectWithCookie("/b")
                    : TestHttpResponse.Json(200, "18");
            });

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(18L));
            }

            Assert.That(recorder.SawHeaderContainingOnPath(
                "/b", "Cookie", "session=secret"), Is.False,
                "The executor-owned client must not maintain an ambient cookie jar.");
        }

        [Test]
        public async Task SameOriginRedirectKeepsExplicitCookieHeader()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? TestHttpResponse.Redirect("/b")
                    : TestHttpResponse.Json(200, "19");
            });
            var options = new HttpWotBindingOptions
            {
                DefaultHeaders = Headers("Cookie", "explicit=secret")
            };

            WotProtocolBinderRegistry registry = OwnedRegistry(options: options);
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(19L));
            }

            Assert.That(recorder.SawHeaderContainingOnPath(
                "/b", "Cookie", "explicit=secret"), Is.True,
                "An explicit cookie header follows the normal same-origin header policy.");
        }

        [Test]
        public async Task RedirectLoopIsRejected()
        {
            using var server = new TestHttpServer(request =>
                request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? TestHttpResponse.Redirect("/b")
                    : TestHttpResponse.Redirect("/a"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("loop").IgnoreCase);
            }
        }

        [Test]
        public async Task RedirectToDisallowedSchemeIsRejected()
        {
            using var server = new TestHttpServer(_ => TestHttpResponse.Redirect("ftp://evil.example.com/x"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/p")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
        }

        [Test]
        public async Task RedirectLimitIsEnforced()
        {
            int counter = 0;
            using var server = new TestHttpServer(_ =>
                TestHttpResponse.Redirect("/r" + Interlocked.Increment(ref counter)));

            WotProtocolBinderRegistry registry = OwnedRegistry(
                options: new HttpWotBindingOptions { MaxAutomaticRedirects = 2 });
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/start")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("redirect limit").IgnoreCase);
            }
        }

        [Test]
        public async Task OwnedClientFollowsTemporaryRedirectToSuccess()
        {
            using var server = new TestHttpServer(request =>
                request.Path.StartsWith("/a", StringComparison.Ordinal)
                    ? TestHttpResponse.Redirect("/final", status: 307)
                    : TestHttpResponse.Json(200, "5"));

            WotProtocolBinderRegistry registry = OwnedRegistry();
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/a")).ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(5L));
            }
        }

        [Test]
        public async Task DefaultHeadersAddedAfterActivationAreIgnored()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return TestHttpResponse.Json(200, "12");
            });
            var options = new HttpWotBindingOptions();
            WotProtocolBinderRegistry registry = OwnedRegistry(options: options);
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/p")).ConfigureAwait(false);

            options.DefaultHeaders = Headers("X-Added-Secret", "added");
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.That(recorder.SawHeader("X-Added-Secret", "added"), Is.False,
                "Headers configured after activation must not enter the channel snapshot.");
        }

        [Test]
        public async Task DefaultHeadersReplacedAfterActivationUseSnapshot()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return TestHttpResponse.Json(200, "13");
            });
            var options = new HttpWotBindingOptions
            {
                DefaultHeaders = Headers("X-Snapshot-Secret", "original")
            };
            WotProtocolBinderRegistry registry = OwnedRegistry(options: options);
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/p")).ConfigureAwait(false);

            options.DefaultHeaders = Headers("X-Replacement-Secret", "replacement");
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(recorder.SawHeader("X-Snapshot-Secret", "original"), Is.True);
                Assert.That(recorder.SawHeader("X-Replacement-Secret", "replacement"), Is.False);
            });
        }

        [Test]
        public async Task DefaultHeaderCollectionMutationAfterActivationUsesSnapshot()
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return TestHttpResponse.Json(200, "14");
            });
            var mutableHeaders = new Dictionary<string, string>
            {
                ["X-Mutable-Secret"] = "original"
            };
            var options = new HttpWotBindingOptions
            {
                DefaultHeaders = mutableHeaders
            };
            WotProtocolBinderRegistry registry = OwnedRegistry(options: options);
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, server.BaseUrl + "/p")).ConfigureAwait(false);

            mutableHeaders["X-Mutable-Secret"] = "mutated";
            mutableHeaders["X-Late-Secret"] = "late";
            await using (channel.ConfigureAwait(false))
            {
                WotReadResult result = await channel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            Assert.Multiple(() =>
            {
                Assert.That(recorder.SawHeader("X-Mutable-Secret", "original"), Is.True);
                Assert.That(recorder.SawHeader("X-Mutable-Secret", "mutated"), Is.False);
                Assert.That(recorder.SawHeader("X-Late-Secret", "late"), Is.False);
            });
        }

        [Test]
        public void CallerSuppliedClientWithCredentialFormFailsClosed()
        {
            using var server = new TestHttpServer((method, path, body) => TestHttpResponse.Json(200, "1"));
            using var client = new HttpClient();
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [
                    new HttpWotBindingExecutor(new HttpWotBindingOptions { ClientFactory = () => client })
                ],
                credentials: new HeaderQueryCredentialProvider(),
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm read = ReadForm(registry, server.BaseUrl + "/p");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(read).ConfigureAwait(false));
        }

        [Test]
        public void CallerSuppliedAutoRedirectClientWithDefaultHeadersFailsClosed()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "1");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return TestHttpResponse.Redirect(targetServer.BaseUrl + "/p");
            });
            using var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                CheckCertificateRevocationList = true
            });
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [
                    new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () => client,
                        DefaultHeaders = DefaultHeaderOptions().DefaultHeaders
                    })
                ],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm read = ReadFormNoSecurity(registry, originServer.BaseUrl + "/p");

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(read).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(exception?.Message,
                    Does.Contain(nameof(HttpWotBindingOptions.CallerClientHandlesRedirectSafety)));
                Assert.That(origin.PathsSeen, Is.Empty,
                    "Rejected caller-owned clients must not send the origin request.");
                Assert.That(target.PathsSeen, Is.Empty,
                    "Rejected caller-owned clients must not reach the redirect target.");
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CallerSuppliedClientWithoutHeadersFailsClosed(bool configureEmptyHeaders)
        {
            var recorder = new Recorder();
            using var server = new TestHttpServer(request =>
            {
                recorder.Record(request);
                return TestHttpResponse.Json(200, "11");
            });
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = true,
                CheckCertificateRevocationList = true
            };
            using var client = new HttpClient(handler);
            int factoryCalls = 0;
            var options = new HttpWotBindingOptions
            {
                ClientFactory = () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    return client;
                }
            };
            if (configureEmptyHeaders)
            {
                options.DefaultHeaders = ImmutableDictionary<string, string>.Empty;
            }
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(options)],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm read = ReadFormNoSecurity(registry, server.BaseUrl + "/p");

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(read).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(exception?.Message,
                    Does.Contain(nameof(HttpWotBindingOptions.CallerClientHandlesRedirectSafety)));
                Assert.That(factoryCalls, Is.Zero);
                Assert.That(recorder.PathsSeen, Is.Empty);
                Assert.That(handler.UseCookies, Is.True,
                    "Opaque caller-owned cookie behavior requires explicit safety confirmation.");
            });
        }

        [Test]
        public async Task CallerDefaultRequestHeadersCannotLeakWithoutSafetyConfirmation()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "15");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return TestHttpResponse.Redirect(targetServer.BaseUrl + "/p");
            });
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                CheckCertificateRevocationList = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Client-Secret", "client-secret");
            int factoryCalls = 0;
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [
                    new HttpWotBindingExecutor(new HttpWotBindingOptions
                    {
                        ClientFactory = () =>
                        {
                            Interlocked.Increment(ref factoryCalls);
                            return client;
                        }
                    })
                ],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            WotCompiledForm read = ReadFormNoSecurity(registry, originServer.BaseUrl + "/p");

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.OpenChannelAsync(read).ConfigureAwait(false));
            Assert.Multiple(() =>
            {
                Assert.That(exception?.Message,
                    Does.Contain(nameof(HttpWotBindingOptions.CallerClientHandlesRedirectSafety)));
                Assert.That(factoryCalls, Is.Zero);
                Assert.That(origin.PathsSeen, Is.Empty);
                Assert.That(target.PathsSeen, Is.Empty);
                Assert.That(handler.AllowAutoRedirect, Is.True);
                Assert.That(handler.UseCookies, Is.True);
                Assert.That(client.DefaultRequestHeaders.GetValues("X-Client-Secret").Single(),
                    Is.EqualTo("client-secret"));
            });

            using HttpResponseMessage response = await client
                .GetAsync(new Uri(targetServer.BaseUrl + "/probe"))
                .ConfigureAwait(false);
            Assert.That(response.IsSuccessStatusCode, Is.True,
                "Rejecting activation must not dispose the caller-owned client.");
        }

        [Test]
        public async Task ConfirmedSafeCallerClientIsNotMutatedOrDisposed()
        {
            var origin = new Recorder();
            var target = new Recorder();
            using var targetServer = new TestHttpServer(request =>
            {
                target.Record(request);
                return TestHttpResponse.Json(200, "16");
            });
            using var originServer = new TestHttpServer(request =>
            {
                origin.Record(request);
                return TestHttpResponse.Redirect(targetServer.BaseUrl + "/p");
            });
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CheckCertificateRevocationList = true
            };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Client-Secret", "existing");
            var options = new HttpWotBindingOptions
            {
                ClientFactory = () => client,
                CallerClientHandlesRedirectSafety = true,
                DefaultHeaders = Headers("X-Option-Snapshot", "original")
            };
            var registry = new WotProtocolBinderRegistry(
                [new HttpBindingPlanner()],
                [new HttpWotBindingExecutor(options)],
                endpointPolicy: new WotEndpointPolicy { AllowLoopback = true });
            IWotBindingChannel channel = await registry.OpenChannelAsync(
                ReadFormNoSecurity(registry, originServer.BaseUrl + "/p")).ConfigureAwait(false);

            options.DefaultHeaders = Headers("X-Option-Replacement", "replacement");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Late-Client-Secret", "late");
            WotReadResult result;
            await using (channel.ConfigureAwait(false))
            {
                result = await channel.ReadAsync().ConfigureAwait(false);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False,
                    "A confirmed-safe client with redirects disabled must not follow the redirect.");
                Assert.That(origin.SawHeader("X-Option-Snapshot", "original"), Is.True);
                Assert.That(origin.SawHeader("X-Option-Replacement", "replacement"), Is.False);
                Assert.That(origin.SawHeader("X-Client-Secret", "existing"), Is.True);
                Assert.That(origin.SawHeader("X-Late-Client-Secret", "late"), Is.True);
                Assert.That(target.PathsSeen, Is.Empty);
                Assert.That(handler.AllowAutoRedirect, Is.False);
                Assert.That(handler.UseCookies, Is.True,
                    "The executor must not mutate caller-owned cookie behavior.");
                Assert.That(client.DefaultRequestHeaders.Contains("X-Option-Snapshot"), Is.False);
                Assert.That(client.DefaultRequestHeaders.GetValues("X-Client-Secret").Single(),
                    Is.EqualTo("existing"));
                Assert.That(client.DefaultRequestHeaders.GetValues("X-Late-Client-Secret").Single(),
                    Is.EqualTo("late"));
            });

            using HttpResponseMessage response = await client
                .GetAsync(new Uri(targetServer.BaseUrl + "/probe"))
                .ConfigureAwait(false);
            Assert.That(response.IsSuccessStatusCode, Is.True,
                "Disposing the channel must not dispose the caller-owned client.");
        }

        private static WotCompiledForm ReadFormNoSecurity(WotProtocolBinderRegistry registry, string href)
        {
            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"p\":{\"type\":\"number\",\"forms\":[{\"href\":\"" +
                href +
                "\"}]}}}";
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));
            return plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);
        }

        private static HttpWotBindingOptions DefaultHeaderOptions()
        {
            return new HttpWotBindingOptions
            {
                DefaultHeaders = Headers("X-Default-Secret", "secret")
            };
        }

        private static ImmutableDictionary<string, string> Headers(string name, string value)
        {
            return ImmutableDictionary<string, string>.Empty.Add(name, value);
        }

        private static TestHttpResponse RedirectWithCookie(string location)
        {
            return new TestHttpResponse(
                302,
                "text/plain",
                [],
                ImmutableDictionary<string, string>.Empty
                    .Add("Location", location)
                    .Add("Set-Cookie", "session=secret; Path=/"));
        }

        private sealed class Recorder
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> m_paths = new();
            private readonly System.Collections.Concurrent.ConcurrentQueue<string> m_pathsWithDefaultHeader = new();
            private readonly System.Collections.Concurrent.ConcurrentQueue<TestHttpRequest> m_requests = new();
            private int m_sawDefaultHeaderToken;
            private int m_sawQueryToken;
            private int m_sawHeaderToken;

            public bool SawDefaultHeaderToken => Volatile.Read(ref m_sawDefaultHeaderToken) != 0;

            public bool SawQueryToken => Volatile.Read(ref m_sawQueryToken) != 0;

            public bool SawHeaderToken => Volatile.Read(ref m_sawHeaderToken) != 0;

            public System.Collections.Generic.IReadOnlyCollection<string> PathsSeen => [.. m_paths];

            public System.Collections.Generic.IReadOnlyCollection<string> PathsWithDefaultHeader =>
                [.. m_pathsWithDefaultHeader];

            public bool SawHeader(string name, string value)
            {
                return m_requests.Any(request =>
                    request.Headers.TryGetValue(name, out string? actual) &&
                    string.Equals(actual, value, StringComparison.Ordinal));
            }

            public bool SawHeaderContaining(string name, string value)
            {
                return m_requests.Any(request =>
                    request.Headers.TryGetValue(name, out string? actual) &&
                    actual.Contains(value, StringComparison.Ordinal));
            }

            public bool SawHeaderContainingOnPath(
                string path, string name, string value)
            {
                return m_requests.Any(request =>
                    request.Path.StartsWith(path, StringComparison.Ordinal) &&
                    request.Headers.TryGetValue(name, out string? actual) &&
                    actual.Contains(value, StringComparison.Ordinal));
            }

            public void Record(TestHttpRequest request)
            {
                m_requests.Enqueue(request);
                m_paths.Enqueue(request.Path);
                if (request.Headers.TryGetValue("X-Default-Secret", out string? defaultValue) &&
                    string.Equals(defaultValue, "secret", StringComparison.Ordinal))
                {
                    m_pathsWithDefaultHeader.Enqueue(request.Path);
                    Interlocked.Exchange(ref m_sawDefaultHeaderToken, 1);
                }
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
            {
                return new ValueTask<WotCredential?>(new WotCredential(
                                WotSecurityScheme.ApiKey,
                                headers: ImmutableDictionary<string, string>.Empty.Add("X-Api-Key", "secret"),
                                queryParameters: ImmutableDictionary<string, string>.Empty.Add("token", "secret")));
            }
        }
    }
}
