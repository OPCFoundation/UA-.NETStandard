// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.AI.Inference;
using Opc.Ua.Server;

namespace Opc.Ua.AI.Server
{
    /// <summary>
    /// Produces the AI node manager for the hosting pipeline.
    /// </summary>
    /// <remarks>
    /// The factory is what the DI container resolves, so it is where the backends,
    /// options and logging arrive. The node manager itself is constructed per Server
    /// and owned by it.
    /// </remarks>
    public sealed class AINodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>Names the fallback backend's configuration section.</summary>
        public const string FallbackOptionsName = "fallback";

        private readonly InferenceBackends m_backends;
        private readonly IOptions<AIOptions>? m_options;
        private readonly IOptions<InferenceBackendOptions>? m_backendOptions;
        private readonly InferenceBackendOptions? m_fallbackBackendOptions;
        private readonly ILogger<AINodeManager>? m_logger;

        /// <summary>
        /// Creates the factory.
        /// </summary>
        public AINodeManagerFactory(
            InferenceBackends backends,
            IOptions<AIOptions>? options = null,
            IOptions<InferenceBackendOptions>? backendOptions = null,
            IOptionsMonitor<InferenceBackendOptions>? namedBackendOptions = null,
            ILogger<AINodeManager>? logger = null)
        {
            m_backends = backends;
            m_options = options;
            m_backendOptions = backendOptions;
            // The fallback's configuration is a NAMED option, so it has to be asked
            // for by name. Without it the node manager would describe the fallback
            // deployment using the primary's site, jurisdiction and egress.
            m_fallbackBackendOptions = namedBackendOptions?.Get(FallbackOptionsName);
            m_logger = logger;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            Opc.Ua.AI.Namespaces.AI,
            Opc.Ua.AI.Namespaces.xRegistry
        };

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to the server
            IAsyncNodeManager manager = new AINodeManager(
                server,
                configuration,
                m_backends,
                m_options,
                m_backendOptions,
                m_fallbackBackendOptions,
                m_logger);
#pragma warning restore CA2000

            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }
}
