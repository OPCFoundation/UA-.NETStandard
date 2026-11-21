// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// <see cref="IAsyncNodeManagerFactory"/> that produces
    /// <see cref="DiNodeManager"/> instances. When constructed through
    /// the unified <c>AddOpcUaDi()</c> hosting pipeline, the factory
    /// injects an <see cref="IDiPostSetupRunner"/> into every manager
    /// it produces so post-setup configurators are dispatched
    /// automatically.
    /// </summary>
    public sealed class DiNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public DiNodeManagerFactory()
            : this(null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the supplied runner into every
        /// manager it produces. Typically resolved from the DI container
        /// by the <c>AddOpcUaDi()</c> registration.
        /// </summary>
        public DiNodeManagerFactory(IDiPostSetupRunner? runner)
            : this(runner, default)
        {
        }

        /// <summary>
        /// Creates a factory that also loads the supplied model providers
        /// after DI. The <c>AddOpcUaDi()</c> registration passes the
        /// providers registered through <c>AddDiModel</c>.
        /// </summary>
        public DiNodeManagerFactory(
            IDiPostSetupRunner? runner,
            ArrayOf<IDiModelProvider> providers)
        {
            m_runner = runner;
            m_providers = providers;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                var uris = new List<string>();
                foreach (string uri in DiModelProviderUtilities.GetNamespaceUris(m_providers))
                {
                    if (!uris.Contains(uri))
                    {
                        uris.Add(uri);
                    }
                }
                if (!uris.Contains(DiNodeManager.DiNamespaceUri))
                {
                    uris.Add(DiNodeManager.DiNamespaceUri);
                }
                return uris.ToArray();
            }
        }

        /// <inheritdoc/>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            // Ownership of the node manager is transferred to the server.
            IAsyncNodeManager nodeManager = new DiNodeManager(server, configuration, m_runner, m_providers);
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        /// <summary>The optional hosted configuration runner.</summary>
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>The additional models loaded by each manager.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;
    }
}
