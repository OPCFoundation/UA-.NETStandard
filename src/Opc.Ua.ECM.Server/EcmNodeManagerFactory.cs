// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// Creates ECM node managers for server hosting.
    /// </summary>
    public sealed class EcmNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory with optional DI post-setup and companion providers.
        /// </summary>
        public EcmNodeManagerFactory(
            IDiPostSetupRunner? runner = null,
            ArrayOf<IDiModelProvider> providers = default)
        {
            m_runner = runner;
            m_providers = providers;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
        {
            get
            {
                var uris = new List<string> { Namespaces.ECM, IA.Namespaces.IA };
                ArrayOf<string> contributed = new DiNodeManagerFactory(m_runner, m_providers).NamespacesUris;
                for (int ii = 0; ii < contributed.Count; ii++)
                {
                    if (!uris.Contains(contributed[ii]))
                    {
                        uris.Add(contributed[ii]);
                    }
                }
                return uris.ToArray();
            }
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EcmNodeManager? manager = null;
            ValueTask<IAsyncNodeManager> result;
            try
            {
                manager = new EcmNodeManager(server, configuration, m_runner, m_providers);
                result = new ValueTask<IAsyncNodeManager>(manager);
                manager = null;
            }
            finally
            {
                manager?.Dispose();
            }
            return result;
        }

        /// <summary>The post-setup runner.</summary>
        private readonly IDiPostSetupRunner? m_runner;

        /// <summary>The additional companion providers.</summary>
        private readonly ArrayOf<IDiModelProvider> m_providers;
    }
}
