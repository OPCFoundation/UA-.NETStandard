// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Retains the dependency-injected factory used to create a server's session manager.
    /// </summary>
    internal sealed class OpcUaServerSessionManagerRegistration
    {
        /// <summary>
        /// Initializes the registration with the session manager factory.
        /// </summary>
        public OpcUaServerSessionManagerRegistration(
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISessionManager> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Creates the session manager using the service provider, server, and application configuration.
        /// </summary>
        public ISessionManager CreateManager(
            IServiceProvider services,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_factory(services, server, configuration);
        }

        private readonly Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISessionManager> m_factory;
    }

    /// <summary>
    /// Retains the dependency-injected factory used to create a server's subscription manager.
    /// </summary>
    internal sealed class OpcUaServerSubscriptionManagerRegistration
    {
        /// <summary>
        /// Initializes the registration with the subscription manager factory.
        /// </summary>
        public OpcUaServerSubscriptionManagerRegistration(
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISubscriptionManager> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        /// <summary>
        /// Creates the subscription manager using the service provider, server, and application configuration.
        /// </summary>
        public ISubscriptionManager CreateManager(
            IServiceProvider services,
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_factory(services, server, configuration);
        }

        private readonly Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISubscriptionManager> m_factory;
    }

    /// <summary>
    /// Retains a historian provider factory and its server-lifetime ownership setting.
    /// </summary>
    internal sealed class OpcUaServerHistorianRegistration
    {
        /// <summary>
        /// Creates a registration for an existing historian provider whose lifetime the server owns.
        /// </summary>
        public OpcUaServerHistorianRegistration(IHistorianProvider provider)
            : this(_ => provider, ownsProvider: true)
        {
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
        }

        /// <summary>
        /// Creates a registration with a provider factory and an explicit lifetime ownership setting.
        /// </summary>
        public OpcUaServerHistorianRegistration(
            Func<IServiceProvider, IHistorianProvider> factory,
            bool ownsProvider)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            OwnsProvider = ownsProvider;
        }

        /// <summary>
        /// Whether the server is responsible for disposing the resolved historian provider.
        /// </summary>
        public bool OwnsProvider { get; }

        /// <summary>
        /// Resolves the historian provider and rejects a null factory result.
        /// </summary>
        public IHistorianProvider Resolve(IServiceProvider services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return m_factory(services) ??
                throw new InvalidOperationException(
                    "The historian provider factory returned null.");
        }

        private readonly Func<IServiceProvider, IHistorianProvider> m_factory;
    }

    /// <summary>
    /// Applies dependency-injected historian and pre-startup task registrations to a server.
    /// </summary>
    internal static class OpcUaServerRegistrationStaging
    {
        /// <summary>
        /// Stages registered historian providers and pre-startup tasks before server startup.
        /// </summary>
        public static void Apply(
            StandardServer server,
            IServiceProvider services)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            foreach (OpcUaServerHistorianRegistration registration in
                services.GetServices<OpcUaServerHistorianRegistration>())
            {
                server.AddHistorianProvider(
                    registration.Resolve(services),
                    registration.OwnsProvider);
            }
            foreach (IServerPreStartupTask task in
                services.GetServices<IServerPreStartupTask>())
            {
                server.AddPreStartupTask(task);
            }
            // Application tasks may populate source registries used by address-space materialization.
            server.AddPreStartupTask(
                services.GetService<OpcUaServerAliasNameStartupTask>() ??
                    new OpcUaServerAliasNameStartupTask(services));
        }
    }

    /// <summary>
    /// Retains the alias-name store registered for a server.
    /// </summary>
    internal sealed class OpcUaServerAliasNameStoreRegistration
    {
        /// <summary>
        /// Initializes the registration with the alias-name store.
        /// </summary>
        public OpcUaServerAliasNameStoreRegistration(IAliasNameStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Alias-name store supplied by this registration.
        /// </summary>
        public IAliasNameStore Store { get; }
    }
}
