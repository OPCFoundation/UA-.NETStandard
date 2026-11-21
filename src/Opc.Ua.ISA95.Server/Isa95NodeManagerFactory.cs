// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.ISA95.Server
{
    /// <summary>
    /// Creates ISA-95 node managers for the hosted server.
    /// </summary>
    public sealed class Isa95NodeManagerFactory : IAsyncNodeManagerFactory
    {
        public Isa95NodeManagerFactory(
            Isa95ServerOptions options,
            Isa95ServerProviders providers)
            : this(options, providers, [])
        {
        }

        public Isa95NodeManagerFactory(
            Isa95ServerOptions options,
            Isa95ServerProviders providers,
            IEnumerable<IIsa95ModelConfigurator> configurators)
        {
            m_options = options;
            m_providers = providers;
            m_configurators = [.. configurators];
        }

        public ArrayOf<string> NamespacesUris =>
        [
            m_options.InstanceNamespaceUri,
            Namespaces.ISA95,
            JobControl.V1.Namespaces.ISA95JobControlV1,
            JobControl.V2.Namespaces.ISA95JobControlV2
        ];

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager =
                new Isa95NodeManager(
                    server,
                    configuration,
                    m_options,
                    m_providers,
                    m_configurators);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly Isa95ServerOptions m_options;
        private readonly Isa95ServerProviders m_providers;
        private readonly IIsa95ModelConfigurator[] m_configurators;
    }
}
