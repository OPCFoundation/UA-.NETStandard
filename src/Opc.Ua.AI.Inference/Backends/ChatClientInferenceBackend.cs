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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Opc.Ua.AI.Inference
{
    /// <summary>
    /// Runs inference through a <see cref="IChatClient"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Microsoft.Extensions.AI</c> is the abstraction this backend exists to
    /// reach. Its point here is the same one clause 8.1 of
    /// <c>OPC UA - AI Model Management and Inference</c> makes: where inference
    /// runs changes the trust boundary and the latency and nothing else. An
    /// <see cref="IChatClient"/> is implemented by hosted services and by
    /// on-device runtimes alike, so a Server that routes through one can move a
    /// deployment between them without the address space changing shape - and
    /// without this assembly referencing any vendor SDK, since the host supplies
    /// the client it wants from its own composition root.
    /// </para>
    /// <para>
    /// The payload stays opaque on the wire, which is the specification's position:
    /// an envelope that typed it would need extending for every domain that adopted
    /// it. This backend therefore accepts the OpenAI-compatible request shape a
    /// caller already sends, projects it onto <see cref="ChatMessage"/> for the
    /// client, and projects the response back. What it does NOT do is invent
    /// values: usage the client did not report is reported as zero rather than
    /// estimated, and a model the client did not name is reported as the one that
    /// was asked for.
    /// </para>
    /// </remarks>
    public sealed class ChatClientInferenceBackend : IInferenceBackend, IDisposable
    {
        /// <summary>
        /// Creates a backend over a chat client.
        /// </summary>
        /// <param name="client">The client to run inference through.</param>
        /// <param name="site">
        /// Where that client runs, as reported through
        /// <c>DeploymentType.InferenceLocation</c>. The client cannot be asked -
        /// an <see cref="IChatClient"/> over a local runtime and one over a hosted
        /// service are the same type - so the host states it.
        /// </param>
        /// <param name="models">
        /// The models this backend offers. An <see cref="IChatClient"/> has no
        /// enumeration in its contract, so a host that wants a catalogue supplies
        /// one; the default is the client's own default model, or nothing.
        /// </param>
        public ChatClientInferenceBackend(
            IChatClient client,
            InferenceSite site = InferenceSite.Cloud,
            IReadOnlyList<BackendModel>? models = null)
        {
            m_client = client ?? throw new ArgumentNullException(nameof(client));
            Site = site;
            m_models = models ?? BuildDefaultCatalogue(client);
        }

        /// <inheritdoc/>
        public InferenceSite Site { get; }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<BackendModel>> ListModelsAsync(
            string? filter,
            uint maxResults,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var matches = new List<BackendModel>();
            for (int ii = 0; ii < m_models.Count; ii++)
            {
                BackendModel model = m_models[ii];
                if (!string.IsNullOrEmpty(filter) &&
                    model.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                matches.Add(model);
                if (maxResults > 0 && matches.Count >= maxResults)
                {
                    break;
                }
            }
            return new ValueTask<IReadOnlyList<BackendModel>>(matches);
        }

        /// <inheritdoc/>
        public async ValueTask<InferenceResult> InvokeAsync(
            InferenceRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            List<ChatMessage> messages;
            try
            {
                messages = ReadMessages(request.Payload.Span);
            }
            catch (JsonException ex)
            {
                // Malformed input from a caller is expected rather than
                // exceptional, and refusing it with the reason is more use than
                // letting a parse error surface as a transport fault.
                return new InferenceResult
                {
                    Ok = false,
                    Finish = InferenceFinish.Error,
                    Message = "The request payload is not a valid chat completions body: " + ex.Message
                };
            }

            if (messages.Count == 0)
            {
                return new InferenceResult
                {
                    Ok = false,
                    Finish = InferenceFinish.Error,
                    Message = "The request payload carries no messages."
                };
            }

            var options = new ChatOptions { ModelId = request.Model };
            ApplyParameters(options, request.Parameters);

            using var timeout = request.Timeout > TimeSpan.Zero
                ? new CancellationTokenSource(request.Timeout)
                : null;
            using CancellationTokenSource? linked = timeout == null
                ? null
                : CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            ChatResponse response;
            try
            {
                response = await m_client
                    .GetResponseAsync(messages, options, linked?.Token ?? ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout is { IsCancellationRequested: true })
            {
                // The caller's own deadline elapsed. That is a timeout, not a
                // cancellation, and the two want different handling upstream.
                return new InferenceResult
                {
                    Ok = false,
                    Finish = InferenceFinish.Error,
                    Message = "The backend did not answer within the requested timeout."
                };
            }

            return Project(response, request.Model);
        }

        /// <inheritdoc/>
        public async ValueTask<BackendProbe> ProbeAsync(CancellationToken ct)
        {
            try
            {
                ChatResponse response = await m_client
                    .GetResponseAsync(
                        [new ChatMessage(ChatRole.User, "ping")],
                        new ChatOptions { MaxOutputTokens = 1 },
                        ct)
                    .ConfigureAwait(false);
                return new BackendProbe
                {
                    Reachable = true,
                    Detail = response.ModelId ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // A probe reports every failure rather than raising it.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return new BackendProbe
                {
                    Reachable = false,
                    Detail = ex.Message
                };
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_client.Dispose();
        }

        private static BackendModel[] BuildDefaultCatalogue(IChatClient client)
        {
            ChatClientMetadata? metadata = client.GetService(typeof(ChatClientMetadata)) as ChatClientMetadata;
            string? id = metadata?.DefaultModelId;
            if (string.IsNullOrEmpty(id))
            {
                return Array.Empty<BackendModel>();
            }
            return
            [
                new BackendModel
                {
                    Publisher = metadata?.ProviderName ?? string.Empty,
                    Name = id,
                    Version = string.Empty,
                    TaskKind = "chat",
                    Capabilities = ["chat"]
                }
            ];
        }

        private static List<ChatMessage> ReadMessages(ReadOnlySpan<byte> payload)
        {
            var messages = new List<ChatMessage>();
            if (payload.IsEmpty)
            {
                return messages;
            }

            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("messages", out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return messages;
            }

            foreach (JsonElement element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                string role = "user";
                if (element.TryGetProperty("role", out JsonElement r))
                {
                    // GetString throws on a non-string element, and that would
                    // escape the handler that turns a bad payload into a refusal.
                    if (r.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
                    {
                        throw new JsonException("A chat message role must be a string.");
                    }
                    role = r.GetString() ?? "user";
                }
                ChatRole chatRole = ToRole(role);
                if (!element.TryGetProperty("content", out JsonElement c) ||
                    c.ValueKind == JsonValueKind.String ||
                    c.ValueKind == JsonValueKind.Null)
                {
                    string text = c.ValueKind == JsonValueKind.String
                        ? c.GetString() ?? string.Empty
                        : string.Empty;
                    messages.Add(new ChatMessage(chatRole, text));
                    continue;
                }
                if (c.ValueKind == JsonValueKind.Array)
                {
                    messages.Add(new ChatMessage(chatRole, ReadContentParts(c)));
                    continue;
                }
                throw new JsonException("The chat message content must be a string or an array of content parts.");
            }
            return messages;
        }

        private static List<AIContent> ReadContentParts(JsonElement array)
        {
            var contents = new List<AIContent>();
            foreach (JsonElement part in array.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("A chat content part must be an object.");
                }
                if (!part.TryGetProperty("type", out JsonElement t) ||
                    t.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException("A chat content part must name its type.");
                }
                string type = t.GetString() ?? string.Empty;
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(ReadTextContent(part));
                    continue;
                }
                if (string.Equals(type, "image_url", StringComparison.OrdinalIgnoreCase))
                {
                    contents.Add(ReadImageContent(part));
                    continue;
                }

                // Refuse unsupported parts rather than skipping them: dropping the
                // only image would let a model answer confidently about content it
                // never received.
                throw new JsonException(
                    "The backend does not support chat content part type '" + type + "'.");
            }
            return contents;
        }

        private static TextContent ReadTextContent(JsonElement part)
        {
            if (!part.TryGetProperty("text", out JsonElement text) ||
                text.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("A text chat content part must carry string text.");
            }
            return new TextContent(text.GetString() ?? string.Empty);
        }

        private static AIContent ReadImageContent(JsonElement part)
        {
            if (!part.TryGetProperty("image_url", out JsonElement image) ||
                image.ValueKind != JsonValueKind.Object ||
                !image.TryGetProperty("url", out JsonElement urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("An image_url chat content part must carry a string url.");
            }

            string url = urlElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new JsonException("An image_url chat content part must carry a non-empty url.");
            }

            AIContent content;
            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                content = ReadDataImageContent(url);
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                content = new UriContent(uri);
            }
            else
            {
                throw new JsonException(
                    "An image_url chat content part must carry a data, http, or https url.");
            }

            ApplyImageDetail(image, content);
            return content;
        }

        private static DataContent ReadDataImageContent(string url)
        {
            int comma = url.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0)
            {
                throw new JsonException("The image_url data URI is missing its data separator.");
            }

            string metadata = url.Substring("data:".Length, comma - "data:".Length);
            string payload = url.Substring(comma + 1);
            string mediaType = ReadDataUriMediaType(metadata);
            if (!HasBase64Marker(metadata))
            {
                throw new JsonException("The image_url data URI must be base64 encoded.");
            }

            try
            {
                return new DataContent(Convert.FromBase64String(payload), mediaType);
            }
            catch (FormatException ex)
            {
                throw new JsonException("The image_url data URI base64 payload is malformed.", ex);
            }
            catch (ArgumentException ex)
            {
                // DataContent validates the media type itself and is stricter than
                // the shape check above. Letting that escape would unwind past the
                // handler that turns a bad payload into a structured refusal, so the
                // caller would get a bare Bad status carrying an internal exception
                // message instead of a result naming what was wrong.
                throw new JsonException(
                    "The image_url data URI does not name a valid media type.", ex);
            }
        }

        private static string ReadDataUriMediaType(string metadata)
        {
            int semicolon = metadata.IndexOf(';', StringComparison.Ordinal);
            string mediaType = semicolon < 0 ? metadata : metadata[..semicolon];
            if (string.IsNullOrWhiteSpace(mediaType))
            {
                throw new JsonException("The image_url data URI must name a media type.");
            }

            // A media type is type/subtype. Rejecting anything else here keeps the
            // refusal specific; DataContent would otherwise throw a less helpful
            // ArgumentException from inside the projection.
            int slash = mediaType.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0 ||
                slash == mediaType.Length - 1 ||
                mediaType.IndexOf('/', slash + 1) >= 0)
            {
                throw new JsonException(
                    "The image_url data URI media type must be of the form type/subtype.");
            }
            return mediaType;
        }

        private static bool HasBase64Marker(string metadata)
        {
            string[] tokens = metadata.Split(';');
            for (int ii = 1; ii < tokens.Length; ii++)
            {
                if (string.Equals(tokens[ii], "base64", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ApplyImageDetail(JsonElement image, AIContent content)
        {
            if (!image.TryGetProperty("detail", out JsonElement detailElement) ||
                detailElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            string? detail = detailElement.GetString();
            if (!string.Equals(detail, "low", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(detail, "high", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(detail, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            content.AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["detail"] = detail!
            };
        }

        private static ChatRole ToRole(string role)
        {
            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                return ChatRole.System;
            }
            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                return ChatRole.Assistant;
            }
            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                return ChatRole.Tool;
            }
            return ChatRole.User;
        }

        private static void ApplyParameters(
            ChatOptions options,
            IReadOnlyDictionary<string, string> parameters)
        {
            if (parameters == null)
            {
                return;
            }
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                if (string.Equals(parameter.Key, "temperature", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(
                        parameter.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float temperature))
                {
                    options.Temperature = temperature;
                }
                else if (string.Equals(parameter.Key, "max_tokens", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(
                        parameter.Value,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out int maxTokens))
                {
                    options.MaxOutputTokens = maxTokens;
                }
                else if (string.Equals(parameter.Key, "top_p", StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(
                        parameter.Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float topP))
                {
                    options.TopP = topP;
                }
                else
                {
                    // A parameter the backend cannot honour is refused rather than
                    // dropped: a caller whose parameter was silently ignored
                    // believes it took effect and never finds out otherwise.
                    throw new ArgumentException(
                        "The backend does not support the call parameter '" + parameter.Key + "'.",
                        nameof(parameters));
                }
            }
        }

        private static InferenceResult Project(ChatResponse response, string requested)
        {
            byte[] payload = Encoding.UTF8.GetBytes(response.Text ?? string.Empty);
            UsageDetails? usage = response.Usage;
            return new InferenceResult
            {
                Ok = true,
                Payload = payload,
                ContentType = "text/plain",
                // A client that named the model that answered is believed; one that
                // did not is not second-guessed, because the request named a model
                // and nothing observed contradicts it.
                ModelUsed = string.IsNullOrEmpty(response.ModelId) ? requested : response.ModelId!,
                UsageUnit = "tokens",
                InputUnits = ToUnits(usage?.InputTokenCount),
                OutputUnits = ToUnits(usage?.OutputTokenCount),
                TotalUnits = ToUnits(usage?.TotalTokenCount),
                Finish = ToFinish(response.FinishReason)
            };
        }

        private static ulong ToUnits(long? value)
        {
            return value is > 0 ? (ulong)value.Value : 0UL;
        }

        private static InferenceFinish ToFinish(ChatFinishReason? reason)
        {
            if (reason == null)
            {
                return InferenceFinish.Stop;
            }
            if (reason == ChatFinishReason.Length)
            {
                return InferenceFinish.Length;
            }
            if (reason == ChatFinishReason.ContentFilter)
            {
                return InferenceFinish.Filtered;
            }
            if (reason == ChatFinishReason.ToolCalls)
            {
                return InferenceFinish.ToolCall;
            }
            return InferenceFinish.Stop;
        }

        private readonly IChatClient m_client;
        private readonly IReadOnlyList<BackendModel> m_models;
    }
}
