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
                string role = element.TryGetProperty("role", out JsonElement r)
                    ? r.GetString() ?? "user"
                    : "user";
                string text = element.TryGetProperty("content", out JsonElement c)
                    ? c.ToString()
                    : string.Empty;
                messages.Add(new ChatMessage(ToRole(role), text));
            }
            return messages;
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
