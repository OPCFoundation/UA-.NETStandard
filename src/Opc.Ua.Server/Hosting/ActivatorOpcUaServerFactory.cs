/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class ActivatorOpcUaServerFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TServer>
        : IOpcUaServerFactory
        where TServer : StandardServer
    {
        public ActivatorOpcUaServerFactory(IServiceProvider services)
        {
            m_services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public StandardServer CreateServer(ITelemetryContext telemetry, TimeProvider timeProvider)
        {
            ThrowIfConstructorHooksCannotApply();
            return ActivatorUtilities.CreateInstance<TServer>(m_services, telemetry, timeProvider);
        }

        private void ThrowIfConstructorHooksCannotApply()
        {
            if (typeof(DependencyInjectionStandardServer).IsAssignableFrom(typeof(TServer)))
            {
                return;
            }
            if (!HasConstructorHookRegistrations())
            {
                return;
            }
            throw new InvalidOperationException(
                "The configured custom OPC UA server type does not derive from DependencyInjectionStandardServer. " +
                "AddSessionManager, AddSubscriptionManager, AddDurableSubscriptions, ConfigureRoles, " +
                "AddRoleManager, ConfigureServerProperties, ConfigureResources and ConfigureAliasNames " +
                "require a DI-aware server type so their StandardServer hooks are not silently ignored.");
        }

        private bool HasConstructorHookRegistrations()
        {
            return m_services.GetServices<OpcUaServerSessionManagerRegistration>().Any() ||
                m_services.GetServices<OpcUaServerSubscriptionManagerRegistration>().Any() ||
                m_services.GetService<OpcUaServerRoleManagerRegistration>() != null ||
                m_services.GetService<ISubscriptionStore>() != null ||
                m_services.GetService<IMonitoredItemQueueFactory>() != null ||
                m_services.GetService<ServerProperties>() != null ||
                m_services.GetServices<IConfigureOptions<ServerProperties>>().Any() ||
                m_services.GetServices<IPostConfigureOptions<ServerProperties>>().Any() ||
                m_services.GetServices<OpcUaServerResourceRegistration>().Any() ||
                m_services.GetService<AliasNameServerOptions>() != null ||
                m_services.GetServices<IConfigureOptions<AliasNameServerOptions>>().Any() ||
                m_services.GetServices<IPostConfigureOptions<AliasNameServerOptions>>().Any() ||
                HasConfiguredCompositionOptions();
        }

        private bool HasConfiguredCompositionOptions()
        {
            ServerProperties? properties = m_services.GetService<IOptions<ServerProperties>>()?.Value;
            return (properties != null &&
                    (!string.IsNullOrEmpty(properties.ProductName) ||
                    !string.IsNullOrEmpty(properties.ProductUri) ||
                    !string.IsNullOrEmpty(properties.ManufacturerName) ||
                    !string.IsNullOrEmpty(properties.SoftwareVersion) ||
                    !string.IsNullOrEmpty(properties.BuildNumber) ||
                    properties.BuildDate != DateTime.MinValue)) ||
                m_services.GetService<IOptions<AliasNameServerOptions>>()?.Value.MaterializeAliasNodes == true;
        }

        private readonly IServiceProvider m_services;
    }
}
