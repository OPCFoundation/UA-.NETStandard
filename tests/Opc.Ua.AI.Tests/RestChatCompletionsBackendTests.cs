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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Covers the OpenAI-compatible REST chat-completions backend.
    /// </summary>
    /// <remarks>
    /// This backend sits at the boundary a deployment usually discovers last:
    /// model catalogues, credentials, throttling and timeout behavior all come from
    /// a service outside the Server. The tests keep that boundary under a stub
    /// <see cref="HttpMessageHandler"/>, so they pin the wire decisions without a
    /// network or a vendor account.
    /// </remarks>
    [TestFixture]
    public sealed class RestChatCompletionsBackendTests
    {
        [Test]
        public async Task ListModelsUsesEndpointCatalogueAndAppliesFilterAndBound()
        {
            using var http = Http(
                out StubHttpMessageHandler handler,
                ModelList(
                    """
                    {"data":[
                      {"id":"alpha-small","owned_by":"endpoint-owner"},
                      {"id":"beta-large","owned_by":"endpoint-owner"}]}
                    """),
                ModelList(
                    """
                    {"data":[
                      {"id":"alpha-small","owned_by":"endpoint-owner"},
                      {"id":"beta-large","owned_by":"endpoint-owner"}]}
                    """),
                ModelList(
                    """
                    {"data":[
                      {"id":"alpha-small","owned_by":"endpoint-owner"},
                      {"id":"beta-large","owned_by":"endpoint-owner"}]}
                    """));
            InferenceBackendOptions options = Options();
            options.Models.Add(new BackendModel
            {
                Name = "alpha-small",
                Publisher = "configured-publisher",
                Version = "configured-version",
                Framework = "configured-framework"
            });
            using var backend = Backend(options, http);

            IReadOnlyList<BackendModel> all = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> filtered = await backend
                .ListModelsAsync("configured", 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> bounded = await backend
                .ListModelsAsync(null, 1, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(all, Has.Count.EqualTo(2));
                Assert.That(all[0].Name, Is.EqualTo("alpha-small"));
                Assert.That(all[0].Publisher, Is.EqualTo("configured-publisher"));
                Assert.That(all[0].Version, Is.EqualTo("configured-version"));
                Assert.That(all[0].Framework, Is.EqualTo("configured-framework"));
                Assert.That(all[1].Publisher, Is.EqualTo("endpoint-owner"));
                Assert.That(filtered.Select(m => m.Name), Has.One.EqualTo("alpha-small"));
                Assert.That(bounded, Has.Count.EqualTo(1));
                Assert.That(handler.Requests, Has.Count.EqualTo(3));
                Assert.That(handler.Requests.All(r => r.Method == HttpMethod.Get), Is.True);
            });
        }

        [Test]
        public async Task ListModelsFallsBackToConfigurationWhenEndpointListIsAbsentOrEmpty()
        {
            using var http = Http(
                out _,
                ModelList("""{"object":"list"}"""),
                ModelList("""{"data":[]}"""));
            InferenceBackendOptions options = Options();
            options.Models.Add(new BackendModel { Name = "configured-one", Publisher = "operator" });
            options.Models.Add(new BackendModel { Name = "configured-two", Publisher = "operator" });
            using var backend = Backend(options, http);

            IReadOnlyList<BackendModel> absent = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> empty = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);

            // An endpoint that declines to publish its catalogue should not erase
            // the operator's configured catalogue from the address space.
            Assert.Multiple(() =>
            {
                Assert.That(string.Join("|", absent.Select(m => m.Name)), Is.EqualTo("configured-one|configured-two"));
                Assert.That(string.Join("|", empty.Select(m => m.Name)), Is.EqualTo("configured-one|configured-two"));
            });
        }

        [Test]
        public async Task ListModelsFallsBackToConfigurationWhenEndpointIsUnreachableOrTimesOut()
        {
            using var http = Http(
                out _,
                (_, _) => throw new HttpRequestException("synthetic route failure"),
                (_, _) => throw new TaskCanceledException("synthetic timeout"));
            InferenceBackendOptions options = Options();
            options.Models.Add(new BackendModel { Name = "configured", Publisher = "operator" });
            using var backend = Backend(options, http);

            IReadOnlyList<BackendModel> unreachable = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);
            IReadOnlyList<BackendModel> timedOut = await backend
                .ListModelsAsync(null, 0, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(unreachable.Select(m => m.Name), Has.One.EqualTo("configured"));
                Assert.That(timedOut.Select(m => m.Name), Has.One.EqualTo("configured"));
            });
        }

        [Test]
        public async Task InvokeSendsTheOpaquePayloadAndReportsWhatTheEndpointAnswered()
        {
            using var http = Http(
                out StubHttpMessageHandler handler,
                Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "model":"served-model",
                      "usage":{"prompt_tokens":11,"completion_tokens":22,"total_tokens":40},
                      "choices":[{"finish_reason":"length"}]
                    }
                    """));
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"ping"}]}""", "asked-model"),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(result.Payload.ToArray(), Is.EqualTo(ResponseBytes(handler)).AsCollection);
                Assert.That(result.ContentType, Is.EqualTo("application/json"));
                Assert.That(result.ModelUsed, Is.EqualTo("served-model"),
                    "A service can route to a different model than the one requested; " +
                    "callers need the model that actually answered.");
                Assert.That(result.InputUnits, Is.EqualTo(11UL));
                Assert.That(result.OutputUnits, Is.EqualTo(22UL));
                Assert.That(result.TotalUnits, Is.EqualTo(40UL));
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Length));
                Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(handler.Requests[0].Path, Is.EqualTo("/v1/chat/completions"));
                Assert.That(
                    handler.Requests[0].Body,
                    Is.EqualTo("""{"messages":[{"role":"user","content":"ping"}]}"""));
                Assert.That(handler.Requests[0].ContentType, Is.EqualTo("application/json"));
            });
        }

        [Test]
        public async Task InvokeReportsZeroUsageWhenEndpointReturnsNone()
        {
            using var http = Http(
                out _,
                (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(Json(HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop"}]}"""));
                });
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}""", "asked-model"),
                CancellationToken.None).ConfigureAwait(false);

            // Estimating usage a backend did not report would produce a number that
            // looks metered and is not, and it would be billed against.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(result.ModelUsed, Is.EqualTo("asked-model"));
                Assert.That(result.InputUnits, Is.Zero);
                Assert.That(result.OutputUnits, Is.Zero);
                Assert.That(result.TotalUnits, Is.Zero);
            });
        }

        [Test]
        public async Task InvokeReportsHttpFailureWithResponseBody()
        {
            using var http = Http(out _, Json(HttpStatusCode.InternalServerError, "synthetic backend fault"));
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}"""),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Error));
                Assert.That(result.RetryAfter, Is.EqualTo(TimeSpan.Zero));
                Assert.That(result.Message, Does.Contain("HTTP 500"));
                Assert.That(result.Message, Does.Contain("synthetic backend fault"));
            });
        }

        [Test]
        public async Task InvokeReportsRetryAfterForCapacityFailures()
        {
            HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "synthetic capacity limit");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(13));
            using var http = Http(out _, response);
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}"""),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Error));
                Assert.That(result.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(13)));
                Assert.That(result.Message, Does.Contain("HTTP 429"));
            });
        }

        [Test]
        public async Task InvokeReturnsCancelledWhenTheRequestTimeoutExpires()
        {
            using var http = Http(
                out _,
                async (_, ct) =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
                    return Json(HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop"}]}""");
                });
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}""", timeout: TimeSpan.FromMilliseconds(20)),
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.False);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Cancelled));
                Assert.That(result.Message, Does.Contain("timeout"));
            });
        }

        [Test]
        public void InvokePropagatesCallerCancellation()
        {
            using var cts = new CancellationTokenSource();
            using var http = Http(
                out _,
                (_, ct) =>
                {
                    cts.Cancel();
                    throw new OperationCanceledException(ct);
                });
            using var backend = Backend(Options(), http);

            Assert.That(
                async () => await backend.InvokeAsync(
                    Request("""{"messages":[{"role":"user","content":"hi"}]}"""),
                    cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task InvokePassesMalformedSuccessfulBodyThroughWithoutInventingMetadata()
        {
            using var http = Http(out _, Json(HttpStatusCode.OK, "{ not json"));
            using var backend = Backend(Options(), http);

            InferenceResult result = await backend.InvokeAsync(
                Request("""{"messages":[{"role":"user","content":"hi"}]}""", "asked-model"),
                CancellationToken.None).ConfigureAwait(false);

            // The implementation treats a 200 response body as the caller's payload
            // even when the optional metadata envelope cannot be parsed.
            Assert.Multiple(() =>
            {
                Assert.That(result.Ok, Is.True);
                Assert.That(Encoding.UTF8.GetString(result.Payload.Span), Is.EqualTo("{ not json"));
                Assert.That(result.ModelUsed, Is.EqualTo("asked-model"));
                Assert.That(result.InputUnits, Is.Zero);
                Assert.That(result.OutputUnits, Is.Zero);
                Assert.That(result.TotalUnits, Is.Zero);
                Assert.That(result.Finish, Is.EqualTo(InferenceFinish.Stop));
            });
        }

        [Test]
        public async Task ProbeReportsReachableHttpStatus()
        {
            using var http = Http(out _, Json(HttpStatusCode.OK, """{"data":[]}"""));
            using var backend = Backend(Options(), http);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.True);
                Assert.That(probe.Throttled, Is.False);
                Assert.That(probe.Detail, Is.EqualTo("HTTP 200."));
            });
        }

        [Test]
        public async Task ProbeReportsUnreachableRatherThanThrowing()
        {
            using var http = Http(
                out _,
                (_, _) => throw new HttpRequestException("synthetic route failure"));
            using var backend = Backend(Options(), http);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            // A probe exists so a commissioning engineer learns the endpoint is
            // wrong before a deployment depends on it, so it reports rather than
            // throws.
            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.False);
                Assert.That(probe.Throttled, Is.False);
                Assert.That(probe.Detail, Does.Contain("synthetic route failure"));
            });
        }

        [Test]
        public async Task ProbeReportsThrottledAndRetryAfter()
        {
            HttpResponseMessage response = Json(HttpStatusCode.TooManyRequests, "synthetic capacity limit");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            using var http = Http(out _, response);
            using var backend = Backend(Options(), http);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.True);
                Assert.That(probe.Throttled, Is.True);
                Assert.That(probe.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(17)));
                Assert.That(probe.Detail, Does.Contain("capacity"));
            });
        }

        [Test]
        public async Task ProbeReportsTimeoutAsUnreachable()
        {
            using var http = Http(out _, (_, _) => throw new TaskCanceledException("synthetic timeout"));
            using var backend = Backend(Options(), http);

            BackendProbe probe = await backend.ProbeAsync(CancellationToken.None)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(probe.Reachable, Is.False);
                Assert.That(probe.Throttled, Is.False);
                Assert.That(probe.Detail, Does.Contain("did not answer"));
            });
        }

        [Test]
        public async Task ApiKeyAuthenticationAddsConfiguredHeaderFromResolvedCredential()
        {
            var credentials = new StubCredentialResolver("synthetic-api-key-value-for-test-only");
            InferenceBackendOptions options = Options();
            options.Authentication = BackendAuthentication.ApiKey;
            options.CredentialReference = "synthetic-api-key-reference";
            options.ApiKeyHeader = "x-test-api-key";
            using var http = Http(out StubHttpMessageHandler handler, Json(HttpStatusCode.OK, """{"data":[]}"""));
            using var backend = Backend(options, http, credentials);

            await backend.ProbeAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(credentials.LastReference, Is.EqualTo("synthetic-api-key-reference"));
                Assert.That(
                    handler.Requests[0].Headers["x-test-api-key"],
                    Has.One.EqualTo("synthetic-api-key-value-for-test-only"));
                Assert.That(handler.Requests[0].Authorization, Is.Null);
            });
        }

        [Test]
        public async Task BearerStyleAuthenticationAddsAuthorizationHeader()
        {
            var bearerCredentials = new StubCredentialResolver("synthetic-bearer-token-for-test-only");
            InferenceBackendOptions bearer = Options();
            bearer.Authentication = BackendAuthentication.BearerToken;
            bearer.CredentialReference = "synthetic-bearer-reference";
            using var bearerHttp = Http(out StubHttpMessageHandler bearerHandler, Json(HttpStatusCode.OK, "{}"));
            using var bearerBackend = Backend(bearer, bearerHttp, bearerCredentials);

            var workloadCredentials = new StubCredentialResolver("synthetic-workload-token-for-test-only");
            InferenceBackendOptions workload = Options();
            workload.Authentication = BackendAuthentication.WorkloadIdentity;
            workload.CredentialReference = "synthetic-workload-reference";
            using var workloadHttp = Http(out StubHttpMessageHandler workloadHandler, Json(HttpStatusCode.OK, "{}"));
            using var workloadBackend = Backend(workload, workloadHttp, workloadCredentials);

            await bearerBackend.ProbeAsync(CancellationToken.None).ConfigureAwait(false);
            await workloadBackend.ProbeAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(bearerHandler.Requests[0].Authorization, Is.EqualTo(
                    "Bearer synthetic-bearer-token-for-test-only"));
                Assert.That(workloadHandler.Requests[0].Authorization, Is.EqualTo(
                    "Bearer synthetic-workload-token-for-test-only"));
            });
        }

        [Test]
        public async Task AnonymousAuthenticationSendsNoCredentialHeaders()
        {
            var credentials = new StubCredentialResolver("synthetic-credential-value-for-test-only");
            InferenceBackendOptions options = Options();
            options.Authentication = BackendAuthentication.Anonymous;
            options.CredentialReference = "synthetic-reference";
            using var http = Http(out StubHttpMessageHandler handler, Json(HttpStatusCode.OK, "{}"));
            using var backend = Backend(options, http, credentials);

            await backend.ProbeAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(credentials.LastReference, Is.EqualTo("synthetic-reference"));
                Assert.That(handler.Requests[0].Authorization, Is.Null);
                Assert.That(handler.Requests[0].Headers.ContainsKey("api-key"), Is.False);
            });
        }

        [Test]
        public void ConstructorRefusesNullDependencies()
        {
            InferenceBackendOptions options = Options();
            var credentials = new StubCredentialResolver(null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new RestChatCompletionsBackend(
                        null!,
                        credentials,
                        NullLogger<RestChatCompletionsBackend>.Instance),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new RestChatCompletionsBackend(
                        options,
                        null!,
                        NullLogger<RestChatCompletionsBackend>.Instance),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => new RestChatCompletionsBackend(options, credentials, null!),
                    Throws.ArgumentNullException);
            });
        }

        private static RestChatCompletionsBackend Backend(
            InferenceBackendOptions options,
            HttpClient http,
            ICredentialResolver? credentials = null)
        {
            return new RestChatCompletionsBackend(
                options,
                credentials ?? new StubCredentialResolver(null),
                NullLogger<RestChatCompletionsBackend>.Instance,
                http);
        }

        private static HttpClient Http(
            out StubHttpMessageHandler handler,
            params HttpResponseMessage[] responses)
        {
            handler = new StubHttpMessageHandler(
                responses.Select<
                    HttpResponseMessage,
                    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
                    response => (_, _) => Task.FromResult(response)).ToArray());
            return new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") };
        }

        private static HttpClient Http(
            out StubHttpMessageHandler handler,
            params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responders)
        {
            handler = new StubHttpMessageHandler(responders);
            return new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") };
        }

        private static InferenceBackendOptions Options()
        {
            return new InferenceBackendOptions
            {
                EndpointUri = "https://unit.test/",
                ChatCompletionsPath = "v1/chat/completions",
                ProbePath = "v1/models"
            };
        }

        private static InferenceRequest Request(
            string body,
            string model = "",
            TimeSpan timeout = default)
        {
            return new InferenceRequest
            {
                Model = model,
                Payload = Encoding.UTF8.GetBytes(body),
                ContentType = "application/json",
                Timeout = timeout
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage ModelList(string body)
        {
            return Json(HttpStatusCode.OK, body);
        }

        private static byte[] ResponseBytes(StubHttpMessageHandler handler)
        {
            return handler.LastResponseBody ?? Array.Empty<byte>();
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> m_responders;

            public StubHttpMessageHandler(
                IEnumerable<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responders)
            {
                m_responders = new Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(
                    responders);
            }

            public List<RequestSnapshot> Requests { get; } = [];

            public byte[]? LastResponseBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(await RequestSnapshot.CreateAsync(request, cancellationToken).ConfigureAwait(false));
                if (m_responders.Count == 0)
                {
                    throw new InvalidOperationException("No synthetic response was configured.");
                }

                HttpResponseMessage response = await m_responders
                    .Dequeue()(request, cancellationToken).ConfigureAwait(false);
                LastResponseBody = response.Content is null
                    ? []
                    : await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }
        }

        private sealed record RequestSnapshot
        {
            public HttpMethod Method { get; init; } = HttpMethod.Get;

            public string Path { get; init; } = string.Empty;

            public string? Body { get; init; }

            public string? ContentType { get; init; }

            public Dictionary<string, string[]> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

            public string? Authorization { get; init; }

            public static async Task<RequestSnapshot> CreateAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                {
                    headers.Add(header.Key, header.Value.ToArray());
                }

                if (request.Content != null)
                {
                    foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                    {
                        headers.Add(header.Key, header.Value.ToArray());
                    }
                }

                return new RequestSnapshot
                {
                    Method = request.Method,
                    Path = request.RequestUri?.AbsolutePath ?? string.Empty,
                    Body = request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
                    ContentType = request.Content?.Headers.ContentType?.MediaType,
                    Headers = headers,
                    Authorization = request.Headers.Authorization?.ToString()
                };
            }
        }

        private sealed class StubCredentialResolver : ICredentialResolver
        {
            private readonly string? m_value;

            public StubCredentialResolver(string? value)
            {
                m_value = value;
            }

            public string? LastReference { get; private set; }

            public ValueTask<string?> ResolveAsync(string reference, CancellationToken ct)
            {
                LastReference = reference;
                return new ValueTask<string?>(m_value);
            }
        }
    }
}
