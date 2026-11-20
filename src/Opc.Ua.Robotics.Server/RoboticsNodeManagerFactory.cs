/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Creates stock <see cref="RoboticsNodeManager"/> instances.
    /// </summary>
    public sealed class RoboticsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly RoboticsServerOptions m_options;
        private readonly ArrayOf<IRoboticsModelProvider> m_providers;
        private readonly IDiPostSetupRunner? m_postSetupRunner;

        /// <summary>
        /// Creates a factory with default options and the built-in provider.
        /// </summary>
        public RoboticsNodeManagerFactory()
            : this(
                new IRoboticsModelProvider[] { new RoboticsModelProvider() },
                new RoboticsServerOptions())
        {
        }

        /// <summary>
        /// Creates a factory with explicitly supplied providers and options.
        /// </summary>
        public RoboticsNodeManagerFactory(
            ArrayOf<IRoboticsModelProvider> providers,
            RoboticsServerOptions options,
            IDiPostSetupRunner? postSetupRunner = null)
        {
            m_providers = RoboticsModelProviderUtilities.Normalize(providers);
            m_options = RoboticsModelProviderUtilities.ValidateOptions(options, m_providers);
            m_postSetupRunner = postSetupRunner;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            RoboticsModelProviderUtilities.GetFactoryNamespaceUris(m_providers, m_options);

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
            IAsyncNodeManager nodeManager = new RoboticsNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_postSetupRunner);
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }
}
