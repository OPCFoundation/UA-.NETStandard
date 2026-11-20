/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.AI.Server.Hosting;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Hosting extensions for OPC UA AI Model Management servers.
    /// </summary>
    public static class OpcUaServerAIBuilderExtensions
    {
        /// <summary>
        /// Registers the AI node manager, its options and the configured inference
        /// backends.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddAI(
            this IOpcUaServerBuilder builder,
            Action<AIOptions>? configure = null,
            Action<InferenceBackendOptions>? configureBackend = null,
            Action<InferenceBackendOptions>? configureFallbackBackend = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<AIOptions>();
            builder.Services.AddOptions<InferenceBackendOptions>();
            builder.Services.AddOptions<InferenceBackendOptions>(AINodeManagerFactory.FallbackOptionsName);
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            if (configureBackend != null)
            {
                builder.Services.Configure(configureBackend);
            }
            if (configureFallbackBackend != null)
            {
                builder.Services.Configure(
                    AINodeManagerFactory.FallbackOptionsName,
                    configureFallbackBackend);
            }
            builder.Services.TryAddSingleton(CreateBackends);
            builder.AddNodeManager<AINodeManagerFactory>();
            return builder;
        }

        private static InferenceBackends CreateBackends(IServiceProvider services)
        {
            IOptionsMonitor<InferenceBackendOptions> monitor =
                services.GetRequiredService<IOptionsMonitor<InferenceBackendOptions>>();

            InferenceBackendOptions primaryOptions = monitor.CurrentValue;
            IInferenceBackend primary = CreateBackend(services, string.Empty, primaryOptions);

            InferenceBackendOptions fallbackOptions =
                monitor.Get(AINodeManagerFactory.FallbackOptionsName);
            if (!fallbackOptions.Enabled)
            {
                return new InferenceBackends(primary);
            }
            IInferenceBackend fallback = CreateBackend(
                services,
                AINodeManagerFactory.FallbackOptionsName,
                fallbackOptions);
            return new InferenceBackends(primary, fallback);
        }

        private static IInferenceBackend CreateBackend(
            IServiceProvider services,
            string backendName,
            InferenceBackendOptions options)
        {
            return options.Kind switch
            {
                InferenceBackendKind.ChatClient => new ChatClientInferenceBackend(
                    services.GetRequiredService<IChatClientFactory>()
                        .CreateChatClient(backendName, options),
                    options.Site,
                    [.. options.Models]),
                InferenceBackendKind.RestChatCompletions => new RestChatCompletionsBackend(
                    options,
                    CredentialResolverFor(options),
                    services.GetService<ILogger<RestChatCompletionsBackend>>() ??
                    NullLogger<RestChatCompletionsBackend>.Instance),
                _ => throw new InvalidOperationException(
                    "Unsupported inference backend kind '" + options.Kind + "'.")
            };
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
}
