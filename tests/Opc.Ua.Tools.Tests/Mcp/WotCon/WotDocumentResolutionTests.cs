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

#if NET10_0
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Wot;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises the resolver and HTTP policy through fake responses, never public-network requests.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WotDocumentResolutionTests
    {
        /// <summary>
        /// Default deny and exact normalized origin matching are independent of endpoint-path equality.
        /// </summary>
        [Test]
        public void AllowedOriginsNormalizeIdnaCaseAndDefaultPortButNotAnotherPort()
        {
            Assert.That(WotDependencyHttp.ParseOrigin("https://BÜCHER.example:443/"),
                Is.EqualTo("https://xn--bcher-kva.example:443"));
            var options = new WotMcpOptions { AllowedDependencyOrigins = ["https://bücher.example"] };
            Uri accepted = WotDependencyHttp.RequireAllowed(
                new Uri("https://xn--bcher-kva.example/path.json#field"), options);
            Assert.That(accepted.Fragment, Is.Empty);
            Assert.That(accepted.AbsolutePath, Is.EqualTo("/path.json"));
            Assert.That(() => WotDependencyHttp.RequireAllowed(
                new Uri("https://xn--bcher-kva.example:444/path.json"), options),
                Throws.TypeOf<UnauthorizedAccessException>());
            Assert.That(() => WotDependencyHttp.RequireAllowed(
                new Uri("https://example.test/path.json"), new WotMcpOptions()),
                Throws.TypeOf<UnauthorizedAccessException>());
        }

        /// <summary>
        /// A host entry is an exact HTTPS origin, never a wildcard, path, credential or query.
        /// </summary>
        [TestCase("")]
        [TestCase("http://example.test")]
        [TestCase("https://*.example.test")]
        [TestCase("https://user:password@example.test")]
        [TestCase("https://example.test/path")]
        [TestCase("https://example.test?secret=value")]
        [TestCase("https://example.test#fragment")]
        public void InvalidAllowlistOriginsAreRejected(string value)
        {
            Assert.That(() => WotDependencyHttp.ParseOrigin(value), Throws.ArgumentException);
        }

        /// <summary>
        /// Address policy normalizes mapped IPv4 and never grants loopback, unspecified or multicast access.
        /// </summary>
        [TestCase("8.8.8.8", false, true)]
        [TestCase("2001:4860:4860::8888", false, true)]
        [TestCase("10.1.2.3", false, false)]
        [TestCase("10.1.2.3", true, true)]
        [TestCase("192.168.1.1", false, false)]
        [TestCase("172.16.1.1", false, false)]
        [TestCase("100.64.0.1", false, false)]
        [TestCase("169.254.169.254", false, false)]
        [TestCase("fd00::1", false, false)]
        [TestCase("fd00::1", true, true)]
        [TestCase("fe80::1", false, false)]
        [TestCase("127.0.0.1", true, false)]
        [TestCase("::1", true, false)]
        [TestCase("::ffff:127.0.0.1", true, false)]
        [TestCase("::ffff:192.168.0.1", false, false)]
        [TestCase("::ffff:192.168.0.1", true, true)]
        [TestCase("0.0.0.0", true, false)]
        [TestCase("::", true, false)]
        [TestCase("255.255.255.255", true, false)]
        [TestCase("224.0.0.1", true, false)]
        [TestCase("ff02::1", true, false)]
        public void AddressClassificationAppliesToTheExactConnectedAddress(
            string value, bool allowPrivate, bool allowed)
        {
            Assert.That(WotDependencyHttp.IsAddressAllowed(IPAddress.Parse(value),
                new WotMcpOptions { AllowPrivateDependencyAddresses = allowPrivate }), Is.EqualTo(allowed));
        }

        /// <summary>
        /// Dedicated handlers disable ambient transport mechanisms that could bypass the address policy.
        /// </summary>
        [Test]
        public void HandlerDisablesRedirectCookiesCredentialsProxyAndUnboundedDecompression()
        {
            using SocketsHttpHandler handler = WotDependencyHttp.CreateHandler(new WotMcpOptions());
            Assert.That(handler.AllowAutoRedirect, Is.False);
            Assert.That(handler.UseCookies, Is.False);
            Assert.That(handler.UseProxy, Is.False);
            Assert.That(handler.Credentials, Is.Null);
            Assert.That(handler.AutomaticDecompression, Is.EqualTo(DecompressionMethods.None));
            Assert.That(handler.ConnectCallback, Is.Not.Null);
        }

        /// <summary>
        /// Locally supplied schemas resolve without remote opt-in and retain precedence even with opt-in.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LocalDependenciesTakePrecedenceWithoutHttpAsync(bool remote)
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = remote;
            input.Dependencies =
            [
                new WotDocumentSource
                {
                    Reference = "schema.json", Text = /*lang=json,strict*/ """{"type":"number"}"""
                }
            ];
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            WotResolverResult result = await scope.ResolveSchemaAsync(
                "schema.json#/type", new WotResolutionContext(), default).ConfigureAwait(false);
            Assert.That(result.Found, Is.True);
            Assert.That(Encoding.UTF8.GetString(result.Content.Span),
                Is.EqualTo(/*lang=json,strict*/ """{"type":"number"}"""));
            Assert.That(scope.Provenance()["documents"]!.AsArray(), Has.Count.EqualTo(2));
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Relative chained references resolve against their document owner rather than the primary root.
        /// </summary>
        [Test]
        public async Task NestedRelativeReferencesUseOwningDocumentAndAmbiguitiesFailClosedAsync()
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input(
                /*lang=json,strict*/ """{"events":{"e":{"tm:ref":"sub/type.json"}}}""");
            input.Dependencies =
            [
                new WotDocumentSource
                {
                    Reference = "sub/type.json", Text = /*lang=json,strict*/ """{"tm:ref":"next.json"}"""
                },
                new WotDocumentSource
                {
                    Reference = "sub/next.json", Text = /*lang=json,strict*/ """{"title":"nested"}"""
                },
                new WotDocumentSource { Reference = "next.json", Text = /*lang=json,strict*/ """{"title":"wrong"}""" }
            ];
            using (WotDocumentScope scope = context.Scope(input))
            {
                await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
                WotResolverResult resolved = await scope.ResolveThingAsync(
                    "next.json", new WotResolutionContext(), default).ConfigureAwait(false);
                Assert.That(Encoding.UTF8.GetString(resolved.Content.Span),
                    Is.EqualTo(/*lang=json,strict*/ """{"title":"nested"}"""));
            }
            input.Dependencies =
            [
                new WotDocumentSource
                {
                    Reference = "a/type.json", Text = /*lang=json,strict*/ """{"tm:ref":"next.json"}"""
                },
                new WotDocumentSource
                {
                    Reference = "b/type.json", Text = /*lang=json,strict*/ """{"tm:ref":"next.json"}"""
                }
            ];
            using WotDocumentScope ambiguous = context.Scope(input);
            await ambiguous.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            Assert.That(async () => await ambiguous.ResolveThingAsync(
                "next.json", new WotResolutionContext(), default).ConfigureAwait(false),
                Throws.TypeOf<FormatException>().With.Message.Contains("ambiguous"));
        }

        /// <summary>
        /// Missing references are cached and consume the same bounded request budget as found documents.
        /// </summary>
        [Test]
        public async Task RemoteNotFoundIsCachedAndUniqueMissesConsumeBudgetAsync()
        {
            int requests = 0;
            var options = new WotMcpOptions { AllowedDependencyOrigins = ["https://example.test"], MaxDocuments = 2 };
            using var context = new WotDocumentTestContext(options, (_, _) =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            for (int index = 0; index < 2; index++)
            {
                WotResolverResult missing = await scope.ResolveThingAsync(
                    "https://example.test/missing", new WotResolutionContext(), default).ConfigureAwait(false);
                Assert.That(missing.Found, Is.False);
            }
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(scope.Provenance()["missingDependencies"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(async () => await scope.ResolveThingAsync(
                "https://example.test/another", new WotResolutionContext(), default).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("count"));
            Assert.That(requests, Is.EqualTo(1));
        }

        /// <summary>
        /// Both call-level opt-in and host origin permission are required before HTTP dispatch.
        /// </summary>
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task RemoteResolutionRequiresCallAndHostPermissionAsync(bool optedIn, bool allowed)
        {
            var options = new WotMcpOptions
            {
                AllowedDependencyOrigins = allowed ? ["https://example.test"] : []
            };
            using var context = new WotDocumentTestContext(options);
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = optedIn;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            if (optedIn)
            {
                Assert.That(async () => await scope.ResolveThingAsync(
                    "https://example.test/model", new WotResolutionContext(), default).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>());
            }
            else
            {
                WotResolverResult result = await scope.ResolveThingAsync(
                    "https://example.test/model", new WotResolutionContext(), default).ConfigureAwait(false);
                Assert.That(result.Found, Is.False);
            }
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Redirects and non-JSON media never become parsed or cached successful documents.
        /// </summary>
        [TestCase(302, "application/json", "HttpRequestException")]
        [TestCase(500, "application/json", "HttpRequestException")]
        [TestCase(200, "text/html", "FormatException")]
        [TestCase(200, "", "FormatException")]
        public async Task RemoteStatusAndContentTypeFailuresAreExplicitAsync(int status, string media, string error)
        {
            int requests = 0;
            using var context = new WotDocumentTestContext(AllowedOptions(), (_, _) =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
                {
                    Headers = { Location = new Uri("https://elsewhere.test/redirect") },
                    Content = new StringContent("{}")
                    {
                        Headers = { ContentType = media.Length == 0 ? null : new MediaTypeHeaderValue(media) }
                    }
                });
            });
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                await scope.ResolveThingAsync(
                    "https://example.test/model", new WotResolutionContext(), default).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or FormatException)
            {
                failure = exception;
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.GetType().Name, Is.EqualTo(error));
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(scope.Provenance()["documents"]!.AsArray(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Named-client defaults never forward credentials, cookies or unrelated headers.
        /// </summary>
        [Test]
        public async Task RemoteRequestsRemoveAmbientHeadersAndRedactQueryProvenanceAsync()
        {
            using var context = new WotDocumentTestContext(AllowedOptions());
            using var handler = new WotDocumentHttpHandler((request, _) =>
            {
                Assert.That(request.Headers.Authorization, Is.Null);
                Assert.That(request.Headers.Contains("Cookie"), Is.False);
                Assert.That(request.Headers.Contains("X-Secret"), Is.False);
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Get));
                Assert.That(request.RequestUri!.Query, Is.EqualTo("?token=sensitive"));
                Assert.That(request.Headers.Accept.ToString(), Does.Contain("application/json"));
                return Task.FromResult(JsonResponse(/*lang=json,strict*/ """{"type":"number"}"""));
            });
            context.Clients.Setup(factory => factory.CreateClient(WotDependencyHttp.ClientName)).Returns(() =>
            {
                var client = new HttpClient(handler, disposeHandler: false);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ambient");
                client.DefaultRequestHeaders.Add("Cookie", "secret=value");
                client.DefaultRequestHeaders.Add("X-Secret", "hidden");
                return client;
            });
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            WotResolverResult result = await scope.ResolveSchemaAsync(
                "https://example.test/schema?token=sensitive", new WotResolutionContext(), default)
                .ConfigureAwait(false);
            Assert.That(result.Found, Is.True);
            Assert.That(scope.Provenance().ToJsonString(), Does.Not.Contain("token").And.Not.Contain("sensitive"));
            Assert.That(scope.Provenance()["documents"]![1]!["reference"]!.GetValue<string>(),
                Is.EqualTo("https://example.test/schema"));
        }

        /// <summary>
        /// HTTP exception text cannot disclose credential-bearing dependency addresses to MCP callers.
        /// </summary>
        [Test]
        public async Task TransportErrorsAreActionableWithoutRawCredentialsOrQueryAsync()
        {
            using var context = new WotDocumentTestContext(AllowedOptions(), (_, _) =>
                throw new HttpRequestException("GET https://user:secret@example.test/schema?token=sensitive failed"));
            WotDocumentInput input = SchemaInput("https://example.test/schema?token=sensitive");
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.ValidateDocumentAsync(input).ConfigureAwait(false), true);
            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo("DependencyHttpError"));
            Assert.That(result["message"]!.GetValue<string>(), Does.Contain("allowed origin"));
            Assert.That(result.ToJsonString(),
                Does.Not.Contain("secret").And.Not.Contain("user:").And.Not.Contain("sensitive"));
        }

        /// <summary>
        /// Per-document streamed limits and decompressed response limits apply before growing the target buffer.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RemoteResponsesEnforceDecodedByteBoundsAsync(bool compressed)
        {
            WotMcpOptions options = AllowedOptions();
            options.MaxDocumentBytes = 128;
            byte[] payload = Encoding.UTF8.GetBytes("{\"x\":\"" + new string('a', 1024) + "\"}");
            if (compressed)
            {
                using var encoded = new MemoryStream();
                using (var gzip = new GZipStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    gzip.Write(payload);
                }
                payload = encoded.ToArray();
                Assert.That(payload, Has.Length.LessThan(128));
            }
            using var context = new WotDocumentTestContext(options, (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = EncodedContent(payload, "application/json", compressed ? "gzip" : null)
                }));
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            Assert.That(async () => await scope.ResolveSchemaAsync(
                "https://example.test/schema", new WotResolutionContext(), default).ConfigureAwait(false),
                Throws.TypeOf<IOException>().With.Message.Contains("budget"));
            Assert.That(scope.Provenance()["documents"]!.AsArray(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A chunked response cannot evade the limit by omitting Content-Length.
        /// </summary>
        [Test]
        public async Task ChunkedResponseReadsOnlyOneBytePastBudgetThenStopsAsync()
        {
            WotMcpOptions options = AllowedOptions();
            options.MaxDocumentBytes = 16;
            using var source = new WotChunkedReadStream(Encoding.UTF8.GetBytes(new string('a', 4096)));
            using var content = new StreamContent(source)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") }
            };
            Assert.That(content.Headers.ContentLength, Is.Null);
            using var context = new WotDocumentTestContext(options, (_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            Assert.That(async () => await scope.ResolveThingAsync(
                "https://example.test/body", new WotResolutionContext(), default).ConfigureAwait(false),
                Throws.TypeOf<IOException>());
            Assert.That(source.BytesRead, Is.EqualTo(17));
            Assert.That(scope.Provenance()["documents"]!.AsArray(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Fully decompressed successful dependencies are digested and reused without another HTTP request.
        /// </summary>
        [TestCase("gzip")]
        [TestCase("deflate")]
        [TestCase("br")]
        public async Task SupportedCompressionDecodesAndCachesTheExactDocumentAsync(string encoding)
        {
            byte[] expected = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"type":"number"}""");
            using var buffer = new MemoryStream();
            using (Stream encoder = encoding switch
            {
                "gzip" => new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                "deflate" => new DeflateStream(buffer, CompressionLevel.Fastest, leaveOpen: true),
                _ => new BrotliStream(buffer, CompressionLevel.Fastest, leaveOpen: true)
            })
            {
                encoder.Write(expected);
            }
            byte[] compressed = buffer.ToArray();
            int requests = 0;
            using var context = new WotDocumentTestContext(AllowedOptions(), (_, _) =>
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = EncodedContent(compressed, "application/schema+json", encoding)
                });
            });
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.ResolveRemoteDependencies = true;
            using WotDocumentScope scope = context.Scope(input);
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            for (int index = 0; index < 2; index++)
            {
                WotResolverResult result = await scope.ResolveSchemaAsync(
                    "https://example.test/schema", new WotResolutionContext(), default).ConfigureAwait(false);
                Assert.That(result.Content.ToArray(), Is.EqualTo(expected));
                Assert.That(result.ContentType, Is.EqualTo("application/schema+json"));
            }
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(scope.Provenance()["totalInputBytes"]!.GetValue<long>(),
                Is.EqualTo(2 + compressed.Length + expected.Length));
        }

        /// <summary>
        /// The running byte budget includes the primary source and every distinct local dependency.
        /// </summary>
        [TestCase(13, false)]
        [TestCase(12, true)]
        public async Task CumulativeBudgetIncludesRootAndDependenciesAsync(long limit, bool error)
        {
            using var context = new WotDocumentTestContext(new WotMcpOptions { MaxTotalBytes = limit });
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.Dependencies =
                [new WotDocumentSource { Reference = "dep", Text = /*lang=json,strict*/ """{"x":12345}""" }];
            JsonObject result = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), error);
            if (!error)
            {
                Assert.That(result["provenance"]!["totalInputBytes"]!.GetValue<long>(), Is.EqualTo(13));
            }
        }

        /// <summary>
        /// Whole-operation deadlines cancel a dependency body wait and return a specific timeout.
        /// </summary>
        [Test]
        public async Task OperationDeadlineCancelsFakeTransportAsync()
        {
            WotMcpOptions options = AllowedOptions();
            options.OperationTimeoutMilliseconds = 100;
            using var context = new WotDocumentTestContext(options, async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                throw new InvalidOperationException("Cancellation must end the wait.");
            });
            JsonObject result = WotDocumentTestContext.Payload(await context.Tools.ValidateDocumentAsync(
                SchemaInput("https://example.test/schema")).ConfigureAwait(false), true);
            Assert.That(result["errorType"]!.GetValue<string>(), Is.EqualTo("OperationTimeout"));
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Caller cancellation releases admission and the next invocation can use the sole concurrency slot.
        /// </summary>
        [Test]
        public async Task CallerCancellationReleasesAdmissionAndDisposalIsIdempotentAsync()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WotMcpOptions options = AllowedOptions();
            options.MaxConcurrentOperations = 1;
            using var context = new WotDocumentTestContext(options, async (_, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                throw new InvalidOperationException("Cancellation must end the wait.");
            });
            using var cancellation = new CancellationTokenSource();
            Task<ModelContextProtocol.Protocol.CallToolResult> pending = context.Tools.ValidateDocumentAsync(
                SchemaInput("https://example.test/schema"), cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => pending,
                Throws.InstanceOf<OperationCanceledException>());
            WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                WotDocumentTestContext.Input("{}")).ConfigureAwait(false));
            context.Tools.Dispose();
            context.Tools.Dispose();
            JsonObject disposed = WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(
                WotDocumentTestContext.Input("{}")).ConfigureAwait(false), true);
            Assert.That(disposed["errorType"]!.GetValue<string>(), Is.EqualTo("ObjectDisposedException"));
        }

        /// <summary>
        /// A queued operation cannot dispatch HTTP and cancellation releases it independently of the active call.
        /// </summary>
        [Test]
        public async Task ConcurrencyGateBoundsRequestsAndCancelsQueuedOperationAsync()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WotMcpOptions options = AllowedOptions();
            options.MaxConcurrentOperations = 1;
            int requests = 0;
            using var context = new WotDocumentTestContext(options, async (_, token) =>
            {
                requests++;
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                throw new InvalidOperationException("Cancellation must end the wait.");
            });
            using var activeCancellation = new CancellationTokenSource();
            using var queuedCancellation = new CancellationTokenSource();
            Task<ModelContextProtocol.Protocol.CallToolResult> active = context.Tools.ValidateDocumentAsync(
                SchemaInput("https://example.test/schema"), activeCancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Task<ModelContextProtocol.Protocol.CallToolResult> queued = context.Tools.ValidateDocumentAsync(
                SchemaInput("https://example.test/schema"), queuedCancellation.Token);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(queued.IsCompleted, Is.False);
            await queuedCancellation.CancelAsync().ConfigureAwait(false);
            Assert.That(() => queued,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(active.IsCompleted, Is.False);
            context.Tools.Dispose();
            Assert.That(() => active,
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(context.Root), Is.Empty);
        }

        /// <summary>
        /// Error and provenance formatting redact both valid and truncated credential-bearing addresses.
        /// </summary>
        [Test]
        public void DocumentMetadataRedactsCredentialsQueryAndOversizedValues()
        {
            Assert.That(WotDocumentJson.Text(
                "Dependency 'https://user:secret@example.test/schema?token=private#pointer' failed."),
                Is.EqualTo("Dependency 'https://example.test/schema' failed."));
            Assert.That(WotDocumentJson.Text("https://user:" + new string('x', 1500) + "@example.test/schema"),
                Does.Not.Contain("user").And.Not.Contain(new string('x', 20)));
            Assert.That(WotDependencyHttp.SafeReference("schema.json?token=private#pointer"),
                Is.EqualTo("schema.json"));
        }

        /// <summary>
        /// Source exclusivity and required-conformance authoring choices are enforced before any transport use.
        /// </summary>
        [Test]
        public async Task InvalidSourcesAndConflictingDependenciesFailBeforeTransportAsync()
        {
            using var context = new WotDocumentTestContext();
            WotDocumentInput input = WotDocumentTestContext.Input("{}");
            input.Source.FilePath = "source.json";
            WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            input.Source.Text = null;
            input.Source.FilePath = null;
            WotDocumentTestContext.Payload(await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            input.Source.Text = "{}";
            input.Dependencies =
            [
                new WotDocumentSource { Reference = "same", Text = "{}" },
                new WotDocumentSource { Reference = "same", Text = /*lang=json,strict*/ """{"different":true}""" }
            ];
            JsonObject conflict = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            Assert.That(conflict["message"]!.GetValue<string>(), Does.Contain("different content"));
            input.Dependencies = [];
            input.RequiredConformance = ["Core"];
            JsonObject authoring = WotDocumentTestContext.Payload(
                await context.Tools.InspectDocumentAsync(input).ConfigureAwait(false), true);
            Assert.That(authoring["message"]!.GetValue<string>(), Does.Contain("StrictAuthoring"));
            context.Clients.Verify(factory => factory.CreateClient(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// Host depth/count ceilings cannot be raised through a resolver context supplied by a caller.
        /// </summary>
        [Test]
        public async Task ResolverContextCannotBypassHostDepthAndDocumentLimitsAsync()
        {
            using var context = new WotDocumentTestContext(new WotMcpOptions
            {
                MaxDocuments = 2,
                MaxResolverDepth = 1
            });
            WotDocumentInput excessive = WotDocumentTestContext.Input("{}");
            excessive.Dependencies =
            [
                new WotDocumentSource { Reference = "a", Text = "{}" },
                new WotDocumentSource { Reference = "b", Text = "{}" }
            ];
            Assert.That(() => context.Scope(excessive), Throws.ArgumentException);
            using WotDocumentScope scope = context.Scope(WotDocumentTestContext.Input("{}"));
            await scope.InitializeAsync(nodeSetInput: false).ConfigureAwait(false);
            var resolution = new WotResolutionContext();
            Assert.That(resolution.TryEnter(WotResolutionKind.Thing, "first", out _), Is.True);
            Assert.That(resolution.TryEnter(WotResolutionKind.Thing, "second", out _), Is.True);
            Assert.That(async () => await scope.ResolveThingAsync("third", resolution, default).ConfigureAwait(false),
                Throws.InvalidOperationException.With.Message.Contains("depth"));
        }

        /// <summary>
        /// Creates a host policy allowing only the fake transport's HTTPS origin.
        /// </summary>
        private static WotMcpOptions AllowedOptions()
        {
            return new WotMcpOptions { AllowedDependencyOrigins = ["https://example.test"] };
        }

        /// <summary>
        /// Adds a schema reference that the async converter must resolve.
        /// </summary>
        private static WotDocumentInput SchemaInput(string reference)
        {
            JsonObject document = JsonNode.Parse(WotDocumentTestContext.Model)!.AsObject();
            document["properties"]!["speed"]!["uav:externalSchema"] = reference;
            WotDocumentInput result = WotDocumentTestContext.Input(document.ToJsonString());
            result.ResolveRemoteDependencies = true;
            return result;
        }

        /// <summary>
        /// Creates a supported fixed JSON response without reflection-based serialization.
        /// </summary>
        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>
        /// Creates content whose encoded and decoded lengths are independently testable.
        /// </summary>
        private static ByteArrayContent EncodedContent(byte[] bytes, string mediaType, string? encoding)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            if (encoding is not null)
            {
                content.Headers.ContentEncoding.Add(encoding);
            }
            return content;
        }
    }
}
#endif
