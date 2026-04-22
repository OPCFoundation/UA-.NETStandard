/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="IServiceCollection"/> extensions to host an OPC UA server in
    /// a .NET Generic Host so the host owns the application lifetime, logging,
    /// and Ctrl+C handling.
    /// </summary>
    public static class OpcUaServerServiceCollectionExtensions
    {
        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hosted service builds an <see cref="ApplicationConfiguration"/>
        /// from the supplied <see cref="OpcUaServerOptions"/>, ensures the
        /// application instance certificate is present, registers every
        /// <see cref="IAsyncNodeManagerFactory"/> and
        /// <see cref="INodeManagerFactory"/> resolved from DI, then starts a
        /// <see cref="StandardServer"/>. Stop is signalled by the host
        /// (Ctrl+C, SIGTERM, <see cref="IHostApplicationLifetime.StopApplication"/>).
        /// </para>
        /// <para>
        /// An <see cref="ITelemetryContext"/> is registered automatically that
        /// adapts the host's <see cref="ILoggerFactory"/>; user code does not
        /// need to wire telemetry separately.
        /// </para>
        /// </remarks>
        public static IOpcUaServerBuilder AddOpcUaServer(
            this IServiceCollection services,
            Action<OpcUaServerOptions> configure)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaServerOptions();
            configure(options);
            services.TryAddSingleton(options);

            services.TryAddSingleton<ITelemetryContext>(
                sp => new HostTelemetryContext(sp.GetRequiredService<ILoggerFactory>()));

            services.AddHostedService<OpcUaServerHostedService>();

            return new OpcUaServerBuilder(services);
        }

        private sealed class OpcUaServerBuilder : IOpcUaServerBuilder
        {
            public OpcUaServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IOpcUaServerBuilder AddNodeManager<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
                where TFactory : class, IAsyncNodeManagerFactory
            {
                Services.AddSingleton<IAsyncNodeManagerFactory, TFactory>();
                return this;
            }

            public IOpcUaServerBuilder AddSyncNodeManager<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
                where TFactory : class, INodeManagerFactory
            {
                Services.AddSingleton<INodeManagerFactory, TFactory>();
                return this;
            }
        }
    }
}
