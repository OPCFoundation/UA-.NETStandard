/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="StandardServer"/> variant that resolves supported server hooks from dependency injection.
    /// </summary>
    public class DependencyInjectionStandardServer : StandardServer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyInjectionStandardServer"/> class.
        /// </summary>
        /// <param name="services">Service provider that supplies DI-backed hooks.</param>
        /// <param name="telemetry">Telemetry context used by the server.</param>
        /// <param name="timeProvider">Time provider used by the server.</param>
        public DependencyInjectionStandardServer(
            IServiceProvider services,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
            : base(telemetry, timeProvider)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <inheritdoc/>
        protected override ISessionManager CreateSessionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            foreach (OpcUaServerSessionManagerRegistration registration in
                m_services.GetServices<OpcUaServerSessionManagerRegistration>())
            {
                return registration.CreateManager(m_services, server, configuration);
            }

            if (m_services.GetService<ISessionManager>() is { } sessionManager)
            {
                return sessionManager;
            }

            return base.CreateSessionManager(server, configuration);
        }

        /// <inheritdoc/>
        protected override ISubscriptionManager CreateSubscriptionManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            foreach (OpcUaServerSubscriptionManagerRegistration registration in
                m_services.GetServices<OpcUaServerSubscriptionManagerRegistration>())
            {
                return registration.CreateManager(m_services, server, configuration);
            }

            if (m_services.GetService<ISubscriptionManager>() is { } subscriptionManager)
            {
                return subscriptionManager;
            }

            return base.CreateSubscriptionManager(server, configuration);
        }

        /// <inheritdoc/>
        protected override IMonitoredItemQueueFactory? CreateMonitoredItemQueueFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_services.GetService<IMonitoredItemQueueFactory>() ??
                base.CreateMonitoredItemQueueFactory(server, configuration);
        }

        /// <inheritdoc/>
        protected override ISubscriptionStore? CreateSubscriptionStore(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_services.GetService<ISubscriptionStore>() ??
                base.CreateSubscriptionStore(server, configuration);
        }

        /// <inheritdoc/>
        protected override IRoleManager CreateRoleManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            return m_services.GetService<IRoleManager>() ??
                base.CreateRoleManager(server, configuration);
        }

        /// <inheritdoc/>
        protected override IMainNodeManagerFactory CreateMainNodeManagerFactory(
            IServerInternal server,
            ApplicationConfiguration configuration)
        {
            IPushConfigurationTransactionCoordinator? coordinator =
                m_services.GetService<IPushConfigurationTransactionCoordinator>();
            IPendingCertificateKeyStore? pendingKeyStore =
                m_services.GetService<IPendingCertificateKeyStore>();

            return coordinator != null || pendingKeyStore != null
                ? new MainNodeManagerFactory(configuration, server, coordinator, pendingKeyStore)
                : base.CreateMainNodeManagerFactory(server, configuration);
        }

        /// <inheritdoc/>
        protected override void OnNodeManagerStarted(IServerInternal server)
        {
            RegisterHistorians(server);
            RegisterAliasNames(server);
            base.OnNodeManagerStarted(server);
        }

        private void RegisterHistorians(IServerInternal server)
        {
            if (server is not IHistorianRegistryProvider registryProvider)
            {
                return;
            }

            foreach (OpcUaServerHistorianRegistration registration in
                m_services.GetServices<OpcUaServerHistorianRegistration>())
            {
                registryProvider.HistorianRegistry.RegisterDefault(registration.Provider);
            }
        }

        private void RegisterAliasNames(IServerInternal server)
        {
            if (server is not IAliasNameStoreRegistryProvider registryProvider)
            {
                return;
            }

            foreach (IAliasNameStoreRegistry registry in m_services.GetServices<IAliasNameStoreRegistry>())
            {
                foreach (IAliasNameStore store in registry.Stores)
                {
                    registryProvider.AliasNameStoreRegistry.Register(store);
                }
            }

            foreach (OpcUaServerAliasNameStoreRegistration registration in
                m_services.GetServices<OpcUaServerAliasNameStoreRegistration>())
            {
                registryProvider.AliasNameStoreRegistry.Register(registration.Store);
            }
        }

        private readonly IServiceProvider m_services;
    }
}
