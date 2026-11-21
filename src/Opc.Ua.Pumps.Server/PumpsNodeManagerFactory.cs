// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Produces <see cref="PumpsNodeManager"/> instances.
    /// </summary>
    /// <remarks>
    /// Constructed by the container through <c>AddPumpsServer</c>, the factory
    /// receives the Device Integration post-setup runner and forwards it to
    /// every manager it produces, which is what makes
    /// <c>ConfigureDevicesFor&lt;PumpsNodeManager&gt;</c> and
    /// <c>ConfigurePumps</c> work.
    /// </remarks>
    public sealed class PumpsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly IDiPostSetupRunner? m_runner;
        private readonly IOptions<PumpsServerOptions>? m_options;

        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public PumpsNodeManagerFactory()
            : this(null, null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner and options
        /// into every manager it produces.
        /// </summary>
        /// <param name="runner">The DI post-setup runner.</param>
        /// <param name="options">The manager options.</param>
        public PumpsNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<PumpsServerOptions>? options = null)
            : this(runner, options, default)
        {
        }

        /// <summary>
        /// Creates a factory that also loads neutral companion model providers.
        /// </summary>
        public PumpsNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<PumpsServerOptions>? options,
            ArrayOf<IDiModelProvider> providers)
        {
            m_runner = runner;
            m_options = options;
            m_providers = providers;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                PumpsServerOptions options = m_options?.Value ?? new PumpsServerOptions();
                var uris = new System.Collections.Generic.List<string>
                {
                    Namespaces.Pumps,
                    Opc.Ua.Machinery.Namespaces.Machinery,
                    Opc.Ua.Di.Namespaces.OpcUaDi
                };
                if (options.LoadIndustrialAutomationModel)
                {
                    uris.Add(Opc.Ua.IA.Namespaces.IA);
                }
                foreach (string uri in options.AdditionalNamespaceUris)
                {
                    uris.Add(uri);
                }
                for (int ii = 0; ii < m_providers.Count; ii++)
                {
                    foreach (string uri in m_providers[ii].NamespaceUris)
                    {
                        if (!uris.Contains(uri))
                        {
                            uris.Add(uri);
                        }
                    }
                }
                return uris.ToArray().ToArrayOf();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership transferred to the server
            IAsyncNodeManager manager = new PumpsNodeManager(
                server,
                configuration,
                m_runner,
                m_options,
                m_providers);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        /// <summary>Additional companion model providers.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;
    }
}
