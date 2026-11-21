// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Robotics.Server.Hosting
{
    internal sealed class RobotIntentHostedNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public RobotIntentHostedNodeManagerFactory(
            ArrayOf<IRobotIntentModelProvider> providers,
            RobotIntentServerOptions options,
            IRobotIntentPostSetupRunner? runner,
            IServiceProvider services)
        {
            m_providers = RobotIntentNodeManager.NormalizeProviders(providers);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_runner = runner;
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public ArrayOf<string> NamespacesUris =>
            RobotIntentNodeManager.GetNamespaceUris(m_providers, m_options).ToArrayOf();

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager manager = new RobotIntentNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_runner,
                m_services);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly ArrayOf<IRobotIntentModelProvider> m_providers;
        private readonly RobotIntentServerOptions m_options;
        private readonly IRobotIntentPostSetupRunner? m_runner;
        private readonly IServiceProvider m_services;
    }
}
