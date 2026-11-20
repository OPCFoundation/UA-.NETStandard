// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class DelegateServerStartupTask : IServerStartupTask
    {
        public DelegateServerStartupTask(
            IServiceProvider services,
            Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> callback)
        {
            m_services = services;
            m_callback = callback;
        }

        public ValueTask OnServerStartedAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            return m_callback(m_services, server, cancellationToken);
        }

        private readonly IServiceProvider m_services;
        private readonly Func<IServiceProvider, IServerContext, CancellationToken, ValueTask> m_callback;
    }

    internal sealed class OpcUaServerResourceRegistration
    {
        public OpcUaServerResourceRegistration(Action<IServiceProvider, ResourceManager> configure)
        {
            m_configure = configure;
        }

        public void Apply(IServiceProvider services, ResourceManager resources)
        {
            m_configure(services, resources);
        }

        private readonly Action<IServiceProvider, ResourceManager> m_configure;
    }

    internal sealed class OpcUaServerAliasNameStartupTask : IServerPreStartupTask
    {
        public OpcUaServerAliasNameStartupTask(IServiceProvider services)
        {
            m_services = services;
        }

        public ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default)
        {
            if (server is not IAliasNameStoreRegistryProvider provider)
            {
                throw new InvalidOperationException("The server does not provide an alias-name store registry.");
            }

            foreach (IAliasNameStoreRegistry registry in m_services.GetServices<IAliasNameStoreRegistry>())
            {
                foreach (IAliasNameStore store in registry.Stores)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    provider.AliasNameStoreRegistry.Register(store);
                }
            }

            foreach (OpcUaServerAliasNameStoreRegistration registration in
                m_services.GetServices<OpcUaServerAliasNameStoreRegistration>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                provider.AliasNameStoreRegistry.Register(registration.Store);
            }
            return default;
        }

        private readonly IServiceProvider m_services;
    }
}
