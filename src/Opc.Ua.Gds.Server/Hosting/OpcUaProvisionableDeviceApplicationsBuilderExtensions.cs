/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Configures the application components exposed by a provisionable device.
    /// </summary>
    public static class OpcUaProvisionableDeviceApplicationsBuilderExtensions
    {
        /// <summary>
        /// Registers application push targets to publish beneath a non-singleton ProvisionableDevice.
        /// The provider is separate from the GDS ManagedApplications provider.
        /// </summary>
        public static IOpcUaServerBuilder AddProvisionableDeviceApplications(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IProvisionableDeviceApplicationsProvider> factory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddProvisionableDeviceApplications(factory);
            return builder;
        }

        /// <summary>
        /// Registers the provider used to publish application components on a provisionable device.
        /// </summary>
        public static IServiceCollection AddProvisionableDeviceApplications(
            this IServiceCollection services,
            Func<IServiceProvider, IProvisionableDeviceApplicationsProvider> factory)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (services.Any(static descriptor =>
                !descriptor.IsKeyedService &&
                descriptor.ServiceType == typeof(IProvisionableDeviceApplicationsProvider)))
            {
                throw new InvalidOperationException("Select only one provisionable-device application provider.");
            }
            services.AddSingleton(factory);
            return services;
        }
    }
}
