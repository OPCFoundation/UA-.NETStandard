// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Creates stock <see cref="MachineryNodeManager"/> instances.
    /// </summary>
    public sealed class MachineryNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly MachineryServerOptions m_options;
        private readonly ArrayOf<IMachineryModelProvider> m_providers;
        private readonly IDiPostSetupRunner? m_postSetupRunner;

        /// <summary>
        /// Creates a factory with default options and the built-in provider.
        /// </summary>
        public MachineryNodeManagerFactory()
            : this(new MachineryServerOptions())
        {
        }

        /// <summary>
        /// Creates a factory with explicit options and the built-in provider
        /// for the configured parts.
        /// </summary>
        public MachineryNodeManagerFactory(MachineryServerOptions options)
            : this(
                new IMachineryModelProvider[]
                {
                    new MachineryModelProvider(
                        MachineryModelProviderUtilities.ValidateOptions(options).Parts)
                },
                options)
        {
        }

        /// <summary>
        /// Creates a factory with explicitly supplied providers and options.
        /// </summary>
        public MachineryNodeManagerFactory(
            ArrayOf<IMachineryModelProvider> providers,
            MachineryServerOptions options,
            IDiPostSetupRunner? postSetupRunner = null)
        {
            m_options = MachineryModelProviderUtilities.ValidateOptions(options);
            m_providers = MachineryModelProviderUtilities.Normalize(providers, m_options.Parts);
            m_options = MachineryModelProviderUtilities.ValidateOptions(m_options, m_providers);
            m_postSetupRunner = postSetupRunner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            MachineryModelProviderUtilities.GetFactoryNamespaceUris(m_providers, m_options);

        /// <inheritdoc/>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the node manager is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager nodeManager = new MachineryNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_postSetupRunner);
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }
}
