// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Server;

namespace Opc.Ua.Vision.Server.Hosting
{
    internal sealed class VisionHostedNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public VisionHostedNodeManagerFactory(
            ArrayOf<IVisionModelProvider> providers,
            VisionServerOptions options,
            IVisionPostSetupRunner? runner,
            IServiceProvider services)
        {
            m_providers = VisionNodeManager.NormalizeProviders(providers);
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_options.Validate();
            m_runner = runner;
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public ArrayOf<string> NamespacesUris =>
            VisionNodeManager.GetNamespaceUris(m_providers, m_options).ToArrayOf();

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "Ownership is transferred to the server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            DiAddressSpaceOwnership? ownership = m_services.GetService<DiAddressSpaceOwnership>();
            if ((m_options.MachineVisionParts & MachineVisionParts.Assets) != 0 &&
                ownership is { OwnerName: not "AddVision" })
            {
                throw new InvalidOperationException(
                    $"Device Integration is owned by '{ownership.OwnerName}'. Use AddVisionToMachinery " +
                    "instead of registering a second Vision manager with asset support.");
            }
            IAsyncNodeManager manager = new VisionNodeManager(
                server,
                configuration,
                m_providers,
                m_options,
                m_runner,
                m_services);
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly ArrayOf<IVisionModelProvider> m_providers;
        private readonly VisionServerOptions m_options;
        private readonly IVisionPostSetupRunner? m_runner;
        private readonly IServiceProvider m_services;
    }
}
