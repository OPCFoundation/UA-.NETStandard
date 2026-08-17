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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// A backend that speaks the REST chat-completions contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One class serves both the hosted service and the on-device runtime, because
    /// both expose the same contract. What differs is the base address, what
    /// authenticates the call, and which models are available - which is precisely
    /// the claim <c>InferenceLocation</c> makes, so it would have been suspicious to
    /// need two implementations.
    /// </para>
    /// <para>
    /// The wire format is handled directly rather than through a vendor SDK. For a
    /// sample against a specification whose <c>ApiDialectEnum</c> names this contract
    /// by shape, showing the shape is the point; it also keeps the sample free of a
    /// dependency that would date faster than the contract does.
    /// </para>
    /// </remarks>
    public sealed class RestChatCompletionsBackend : IInferenceBackend, IDisposable
    {
        private readonly HttpClient m_http;
        private readonly InferenceBackendOptions m_options;
        private readonly ICredentialResolver m_credentials;
        private readonly ILogger<RestChatCompletionsBackend> m_logger;
        private readonly bool m_ownsClient;

        /// <summary>
        /// Creates a backend over the configured endpoint.
        /// </summary>
        public RestChatCompletionsBackend(
            InferenceBackendOptions options,
            ICredentialResolver credentials,
            ILogger<RestChatCompletionsBackend> logger,
            HttpClient? http = null)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_ownsClient = http is null;
            m_http = http ?? new HttpClient();
            if (m_http.BaseAddress is null && !string.IsNullOrEmpty(options.EndpointUri))
            {
                m_http.BaseAddress = new Uri(options.EndpointUri, UriKind.Absolute);
            }
        }

        /// <inheritdoc/>
        public InferenceSite Site => m_options.Site;

        /// <inheritdoc/>
        /// <inheritdoc/>
        /// <remarks>
        /// Asked of the endpoint, not of configuration. The question the caller is
        /// asking is what this source OFFERS, and configuration can only answer what
        /// this Server was told about - which is a different and usually smaller set.
        /// Where the endpoint declines to say, the configured list is the fallback
        /// rather than the answer.
        /// </remarks>
        public async ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
            string? filter, uint maxResults, CancellationToken ct)
        {
            IEnumerable<BackendModel> models =
                await DiscoverModelsAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(filter))
            {
                models = models.Where(m =>
                    m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    m.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }
            if (maxResults > 0)
            {
                models = models.Take((int)maxResults);
            }
            return models.ToList();
        }

        /// <summary>
        /// Reads the endpoint's model list.
        /// </summary>
        /// <remarks>
        /// Metadata the endpoint does not carry is taken from configuration where a
        /// configured entry names the same model, so an operator can supply the
        /// publisher, the digest and the task kind that an OpenAI-compatible listing
        /// has no field for - without that supplement overriding what the endpoint
        /// actually reports.
        /// </remarks>
        private async ValueTask<IReadOnlyList<BackendModel>> DiscoverModelsAsync(
            CancellationToken ct)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, m_options.ProbePath);
            await AuthenticateAsync(message, ct).ConfigureAwait(false);

            try
            {
                using HttpResponseMessage response = await m_http
                    .SendAsync(message, ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return [.. m_options.Models];
                }

                using Stream body = await response.Content
                    .ReadAsStreamAsync(ct)
                    .ConfigureAwait(false);

                using JsonDocument document = await JsonDocument
                    .ParseAsync(body, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                    data.ValueKind != JsonValueKind.Array)
                {
                    return [.. m_options.Models];
                }

                var discovered = new List<BackendModel>();

                foreach (JsonElement element in data.EnumerateArray())
                {
                    // ValueKind is checked before GetString, which throws rather
                    // than returning null when the value is not a string. The
                    // endpoint is not under this Server's control, so "the field is
                    // there" and "the field is what it should be" are separate
                    // questions.
                    if (element.ValueKind != JsonValueKind.Object ||
                        !element.TryGetProperty("id", out JsonElement id) ||
                        id.ValueKind != JsonValueKind.String ||
                        id.GetString() is not { Length: > 0 } name)
                    {
                        continue;
                    }

                    BackendModel? configured = m_options.Models.FirstOrDefault(
                        m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

                    discovered.Add(configured is null
                        ? new BackendModel
                        {
                            Publisher = element.TryGetProperty("owned_by", out JsonElement owner)
                                && owner.ValueKind == JsonValueKind.String
                                    ? owner.GetString() ?? "unknown"
                                    : "unknown",
                            Name = name,
                            Version = "unknown",
                            Framework = "rest-chat-completions"
                        }
                        : configured with { Name = name });
                }

                return discovered.Count > 0 ? discovered : [.. m_options.Models];
            }
            catch (HttpRequestException)
            {
                return [.. m_options.Models];
            }
            catch (JsonException)
            {
                return [.. m_options.Models];
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // HttpClient's own timeout surfaces as TaskCanceledException, not
                // HttpRequestException. Without this a hung endpoint faults the
                // ListModels call instead of the source simply reporting nothing -
                // and a hung endpoint is the ordinary way a remote one fails.
                return [.. m_options.Models];
            }
        }

        /// <inheritdoc/>
        public async ValueTask<InferenceResult> InvokeAsync(
            InferenceRequest request, CancellationToken ct)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using var message = new HttpRequestMessage(
                HttpMethod.Post, m_options.ChatCompletionsPath);
            message.Content = BuildContent(request);
            await AuthenticateAsync(message, ct).ConfigureAwait(false);

            using CancellationTokenSource? timeout = request.Timeout > TimeSpan.Zero
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeout?.CancelAfter(request.Timeout);

            HttpResponseMessage response;
            try
            {
                response = await m_http
                    .SendAsync(message, timeout?.Token ?? ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new InferenceResult
                {
                    Ok = false,
                    Finish = InferenceFinish.Cancelled,
                    Message = "The call exceeded the caller's timeout."
                };
            }
            catch (HttpRequestException ex)
            {
                m_logger.LogWarning(ex, "Inference endpoint unreachable.");
                return new InferenceResult
                {
                    Ok = false,
                    Finish = InferenceFinish.Error,
                    Message = ex.Message
                };
            }

            using (response)
            {
                byte[] body = await response.Content
                    .ReadAsByteArrayAsync(ct)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return Failure(response, body);
                }
                return Success(request, body);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, m_options.ProbePath);
            await AuthenticateAsync(message, ct).ConfigureAwait(false);
            try
            {
                using HttpResponseMessage response = await m_http
                    .SendAsync(message, ct)
                    .ConfigureAwait(false);

                // Throttled is reported separately from unreachable because the two
                // look alike from outside and call for opposite responses: failing
                // over a throttled endpoint moves load onto a weaker model for no
                // reason, since it will serve again shortly.
                if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    return new BackendProbe
                    {
                        Reachable = true,
                        Throttled = true,
                        RetryAfter = RetryAfterOf(response),
                        Detail = "The endpoint is refusing work for capacity reasons."
                    };
                }
                return new BackendProbe
                {
                    Reachable = true,
                    Detail = FormattableString.Invariant($"HTTP {(int)response.StatusCode}.")
                };
            }
            catch (HttpRequestException ex)
            {
                return new BackendProbe { Reachable = false, Detail = ex.Message };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // A hung endpoint is unreachable for every purpose a caller has, and
                // saying so is more useful than faulting the probe that exists to
                // answer exactly this question.
                return new BackendProbe
                {
                    Reachable = false,
                    Detail = "The endpoint did not answer within the client timeout."
                };
            }
        }

        private async Task AuthenticateAsync(HttpRequestMessage message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(m_options.CredentialReference))
            {
                return;
            }
            string? secret = await m_credentials
                .ResolveAsync(m_options.CredentialReference, ct)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(secret))
            {
                return;
            }
            switch (m_options.Authentication)
            {
                case BackendAuthentication.ApiKey:
                    message.Headers.TryAddWithoutValidation(m_options.ApiKeyHeader, secret);
                    break;
                case BackendAuthentication.BearerToken:
                case BackendAuthentication.WorkloadIdentity:
                    message.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
                    break;
                default:
                    break;
            }
        }

        private static ByteArrayContent BuildContent(InferenceRequest request)
        {
            // The payload is opaque: the caller supplies the body and its media type,
            // and this backend adds only the routing the contract requires. A backend
            // that rewrote the body would be typing a payload the specification
            // deliberately leaves untyped.
            var content = new ByteArrayContent(request.Payload.ToArray());
            content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
            return content;
        }

        private InferenceResult Failure(HttpResponseMessage response, byte[] body)
        {
            bool capacity = response.StatusCode == HttpStatusCode.TooManyRequests ||
                            response.StatusCode == HttpStatusCode.ServiceUnavailable;
            return new InferenceResult
            {
                Ok = false,
                Finish = InferenceFinish.Error,
                // A capacity refusal is the one failure worth retrying, and the only
                // one where the endpoint tells the caller when.
                RetryAfter = capacity ? RetryAfterOf(response) : TimeSpan.Zero,
                Message = FormattableString.Invariant(
                    $"HTTP {(int)response.StatusCode}: {Describe(body)}")
            };
        }

        private InferenceResult Success(InferenceRequest request, byte[] body)
        {
            string modelUsed = request.Model;
            string unit = "tokens";
            ulong input = 0, output = 0, total = 0;
            InferenceFinish finish = InferenceFinish.Stop;

            // The response envelope is read for the fields the specification requires
            // a Server to pass through. It is read defensively: an endpoint that omits
            // usage is not an endpoint that failed, and a sample that threw on a
            // missing optional field would be brittle against every real service.
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                // Which model ACTUALLY answered. Reported rather than assumed,
                // because a service that substituted one says so here and the Server
                // must pass that through to ModelUsed.
                if (root.TryGetProperty("model", out JsonElement m) &&
                    m.ValueKind == JsonValueKind.String)
                {
                    modelUsed = m.GetString() ?? request.Model;
                }
                if (root.TryGetProperty("usage", out JsonElement u) &&
                    u.ValueKind == JsonValueKind.Object)
                {
                    input = ReadCount(u, "prompt_tokens", "input_tokens");
                    output = ReadCount(u, "completion_tokens", "output_tokens");
                    total = ReadCount(u, "total_tokens", null);
                }
                if (root.TryGetProperty("choices", out JsonElement choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("finish_reason", out JsonElement fr) &&
                    fr.ValueKind == JsonValueKind.String)
                {
                    finish = MapFinish(fr.GetString());
                }
            }
            catch (JsonException)
            {
                // A non-JSON body is legitimate for an endpoint returning something
                // else entirely. The payload still reaches the caller intact.
            }

            return new InferenceResult
            {
                Ok = true,
                Payload = body,
                ContentType = "application/json",
                ModelUsed = modelUsed,
                UsageUnit = unit,
                InputUnits = input,
                OutputUnits = output,
                TotalUnits = total == 0 ? input + output : total,
                Finish = finish
            };
        }

        /// <summary>
        /// Reads a token count, tolerating anything the endpoint might actually send.
        /// </summary>
        /// <remarks>
        /// <c>ValueKind == Number</c> does not mean <c>GetUInt64</c> will succeed: a
        /// negative or fractional value is still a Number and throws
        /// <see cref="FormatException"/>, which is not a <see cref="JsonException"/>
        /// and so escapes the caller's guard. This is on the SUCCESS path, so a
        /// 200 response carrying <c>"prompt_tokens": -1</c> would fault an inference
        /// that had otherwise worked.
        /// </remarks>
        private static ulong ReadCount(JsonElement usage, string first, string? second)
        {
            if (usage.TryGetProperty(first, out JsonElement a) &&
                a.ValueKind == JsonValueKind.Number &&
                a.TryGetUInt64(out ulong firstValue))
            {
                return firstValue;
            }
            if (second != null &&
                usage.TryGetProperty(second, out JsonElement b) &&
                b.ValueKind == JsonValueKind.Number &&
                b.TryGetUInt64(out ulong secondValue))
            {
                return secondValue;
            }
            return 0;
        }

        private static InferenceFinish MapFinish(string? reason)
        {
            return reason switch
            {
                "stop" => InferenceFinish.Stop,
                "length" => InferenceFinish.Length,
                "tool_calls" => InferenceFinish.ToolCall,
                "content_filter" => InferenceFinish.Filtered,
                _ => InferenceFinish.Stop
            };
        }

        private static TimeSpan RetryAfterOf(HttpResponseMessage response)
        {
            System.Net.Http.Headers.RetryConditionHeaderValue? h =
                response.Headers.RetryAfter;
            if (h?.Delta is { } delta)
            {
                return delta;
            }
            if (h?.Date is { } date)
            {
                TimeSpan wait = date - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
            return TimeSpan.Zero;
        }

        private static string Describe(byte[] body)
        {
            const int Limit = 256;
            string text = System.Text.Encoding.UTF8.GetString(body);
            return text.Length <= Limit ? text : text.Substring(0, Limit);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_ownsClient)
            {
                m_http.Dispose();
            }
        }
    }
}
