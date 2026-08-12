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
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.AI.Inference;

namespace Opc.Ua.AI.Server.Hosting
{
    /// <summary>
    /// Service registration helpers for AI chat-client factories.
    /// </summary>
    public static class AIChatClientServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a chat-client factory over the OpenAI-compatible REST
        /// chat-completions contract.
        /// </summary>
        /// <param name="services">The service collection to update.</param>
        public static IServiceCollection AddRestChatCompletionsAIChatClientFactory(
            this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            services.TryAddSingleton<IChatClientFactory, RestChatCompletionsChatClientFactory>();
            return services;
        }
    }

    internal sealed class RestChatCompletionsChatClientFactory : IChatClientFactory
    {
        public IChatClient CreateChatClient(string backendName, InferenceBackendOptions options)
        {
            return new RestChatCompletionsChatClient(options, CredentialResolverFor(options));
        }

        private static ICredentialResolver CredentialResolverFor(InferenceBackendOptions options)
        {
            return options.Authentication switch
            {
                BackendAuthentication.Anonymous => NullCredentialResolver.Instance,
                BackendAuthentication.WorkloadIdentity =>
                    new WorkloadIdentityCredentialResolver(options.TokenAudience),
                _ => new FileCredentialResolver(options.CredentialDirectory)
            };
        }
    }

    internal sealed class RestChatCompletionsChatClient : IChatClient
    {
        public RestChatCompletionsChatClient(
            InferenceBackendOptions options,
            ICredentialResolver credentials)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            if (!string.IsNullOrEmpty(options.EndpointUri))
            {
                m_http.BaseAddress = new Uri(options.EndpointUri, UriKind.Absolute);
            }
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                m_options.ChatCompletionsPath);
            message.Content = JsonContentFor(messages, options);
            await AuthenticateAsync(message, cancellationToken).ConfigureAwait(false);

            using HttpResponseMessage response = await m_http
                .SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using Stream body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = await JsonDocument
                .ParseAsync(body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return ProjectResponse(document.RootElement, options?.ModelId ?? string.Empty);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
            m_http.Dispose();
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
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                    break;
                default:
                    break;
            }
        }

        private static ByteArrayContent JsonContentFor(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options)
        {
            var request = new ChatCompletionRequest
            {
                Model = options?.ModelId,
                Messages = ToMessages(messages),
                Temperature = options?.Temperature,
                MaxTokens = options?.MaxOutputTokens,
                TopP = options?.TopP
            };
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                request,
                RestChatCompletionsChatClientJsonContext.Default.ChatCompletionRequest);
            var content = new ByteArrayContent(json);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return content;
        }

        private static List<ChatCompletionMessage> ToMessages(IEnumerable<ChatMessage> messages)
        {
            var list = new List<ChatCompletionMessage>();
            foreach (ChatMessage message in messages)
            {
                list.Add(new ChatCompletionMessage
                {
                    Role = ToRole(message.Role),
                    Content = message.Text ?? string.Empty
                });
            }
            return list;
        }

        private static string ToRole(ChatRole role)
        {
            if (role == ChatRole.System)
            {
                return "system";
            }
            if (role == ChatRole.Assistant)
            {
                return "assistant";
            }
            if (role == ChatRole.Tool)
            {
                return "tool";
            }
            return "user";
        }

        private static ChatResponse ProjectResponse(JsonElement root, string requestedModel)
        {
            string model = root.TryGetProperty("model", out JsonElement m)
                ? m.GetString() ?? requestedModel
                : requestedModel;
            JsonElement choice = root.GetProperty("choices")[0];
            string content = choice
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, content))
            {
                ModelId = model,
                FinishReason = FinishReasonOf(choice)
            };
            if (root.TryGetProperty("usage", out JsonElement usage))
            {
                response.Usage = new UsageDetails
                {
                    InputTokenCount = LongProperty(usage, "prompt_tokens"),
                    OutputTokenCount = LongProperty(usage, "completion_tokens"),
                    TotalTokenCount = LongProperty(usage, "total_tokens")
                };
            }
            return response;
        }

        private static ChatFinishReason FinishReasonOf(JsonElement choice)
        {
            if (!choice.TryGetProperty("finish_reason", out JsonElement value))
            {
                return ChatFinishReason.Stop;
            }
            string? reason = value.GetString();
            if (string.Equals(reason, "length", StringComparison.OrdinalIgnoreCase))
            {
                return ChatFinishReason.Length;
            }
            if (string.Equals(reason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                return ChatFinishReason.ContentFilter;
            }
            if (string.Equals(reason, "tool_calls", StringComparison.OrdinalIgnoreCase))
            {
                return ChatFinishReason.ToolCalls;
            }
            return ChatFinishReason.Stop;
        }

        private static long LongProperty(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                value.TryGetInt64(out long result)
                ? result
                : 0L;
        }

        private readonly HttpClient m_http = new();
        private readonly InferenceBackendOptions m_options;
        private readonly ICredentialResolver m_credentials;
    }

    internal sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("messages")]
        public List<ChatCompletionMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("top_p")]
        public float? TopP { get; set; }
    }

    internal sealed class ChatCompletionMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    [JsonSerializable(typeof(ChatCompletionRequest))]
    internal sealed partial class RestChatCompletionsChatClientJsonContext : JsonSerializerContext
    {
    }
}
