/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
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
