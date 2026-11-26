// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// Produces AutoID owners with the DI post-setup runner and additional companion models.
    /// </summary>
    public sealed class AutoIdNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory without DI-hosting integration.
        /// </summary>
        public AutoIdNodeManagerFactory()
            : this(null, null)
        {
        }

        /// <summary>
        /// Creates a factory that injects the post-setup runner and manager options.
        /// </summary>
        public AutoIdNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<AutoIdServerOptions>? options = null)
            : this(runner, options, default)
        {
        }

        /// <summary>
        /// Creates a factory that also loads the supplied neutral model providers.
        /// </summary>
        public AutoIdNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<AutoIdServerOptions>? options,
            ArrayOf<IDiModelProvider> providers)
            : this(runner, options, providers, null)
        {
        }

        /// <summary>
        /// Creates a factory with an injectable image FileType binder.
        /// </summary>
        public AutoIdNodeManagerFactory(
            IDiPostSetupRunner? runner,
            IOptions<AutoIdServerOptions>? options,
            ArrayOf<IDiModelProvider> providers,
            Opc.Ua.Server.FileSystem.IFileTypeBinder? fileBinder)
        {
            m_runner = runner;
            m_options = options;
            m_providers = providers;
            m_fileBinder = fileBinder;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                var uris = new List<string>(AutoIdNodeManager.ManagerNamespaceUris(m_options?.Value))
                {
                    Di.Namespaces.OpcUaDi
                };
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
            IAsyncNodeManager manager = new AutoIdNodeManager(
                server, configuration, m_runner, m_options, m_providers, m_fileBinder);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        /// <summary>The post-setup runner supplied by hosting.</summary>
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>The configured AutoID options.</summary>
        private readonly IOptions<AutoIdServerOptions>? m_options;

        /// <summary>Additional companion providers loaded by the owner.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;

        /// <summary>The image file binder supplied by hosting.</summary>
        private readonly Opc.Ua.Server.FileSystem.IFileTypeBinder? m_fileBinder;
    }
}
