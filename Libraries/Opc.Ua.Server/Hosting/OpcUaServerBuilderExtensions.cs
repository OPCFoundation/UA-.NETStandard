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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Server</c>: register an OPC UA server hosted as an
    /// <see cref="IHostedService"/> so the .NET Generic Host owns its
    /// lifetime, logging pipeline and Ctrl+C / SIGTERM handling.
    /// </summary>
    public static class OpcUaServerBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddServer(IOpcUaBuilder, IConfiguration)"/> overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Server";

        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>
        /// and returns an <see cref="IOpcUaServerBuilder"/> for chaining
        /// node-manager registrations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hosted service builds an <see cref="ApplicationConfiguration"/>
        /// from the supplied <see cref="OpcUaServerOptions"/>, ensures the
        /// application instance certificate is present, attaches every
        /// <see cref="OpcUaServerNodeManagerRegistration"/> resolved from
        /// DI, then starts a <see cref="StandardServer"/>. Stop is signalled
        /// by the host.
        /// </para>
        /// <para>
        /// Calling this method twice on the same
        /// <see cref="IServiceCollection"/> throws
        /// <see cref="InvalidOperationException"/>: at most one regular
        /// server may be registered per service collection. A normal
        /// server can coexist with a GDS server / LDS server / WoT
        /// Connectivity server registered via their own
        /// <c>.AddGdsServer()</c> / <c>.AddLdsServer()</c> /
        /// <c>.AddWotConServer()</c> methods.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Required callback used to populate
        /// <see cref="OpcUaServerOptions"/>.</param>
        /// <returns>An <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
        public static IOpcUaServerBuilder AddServer(
            this IOpcUaBuilder builder,
            Action<OpcUaServerOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            EnsureFirstRegistration(builder.Services);

            builder.Services.AddOptions<OpcUaServerOptions>().Configure(configure);
            RegisterCommonServices(builder.Services);

            return new OpcUaServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>
        /// with options bound from the supplied <paramref name="configuration"/>
        /// section <see cref="DefaultConfigurationSection"/> (<c>OpcUa:Server</c>).
        /// </summary>
        /// <remarks>
        /// Uses reflection-based configuration binding. AOT consumers
        /// should prefer
        /// <see cref="AddServer(IOpcUaBuilder, Action{OpcUaServerOptions})"/>.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Server</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
        [RequiresUnreferencedCode(
            "Binds OpcUaServerOptions using reflection-based configuration binding. " +
            "Use the Action<OpcUaServerOptions> overload for trim/AOT consumers.")]
        [RequiresDynamicCode(
            "Binds OpcUaServerOptions using reflection-based configuration binding. " +
            "Use the Action<OpcUaServerOptions> overload for AOT consumers.")]
        public static IOpcUaServerBuilder AddServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddServer(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>
        /// with options bound from the supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// Uses reflection-based configuration binding. AOT consumers
        /// should prefer
        /// <see cref="AddServer(IOpcUaBuilder, Action{OpcUaServerOptions})"/>.
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
        [RequiresUnreferencedCode(
            "Binds OpcUaServerOptions using reflection-based configuration binding. " +
            "Use the Action<OpcUaServerOptions> overload for trim/AOT consumers.")]
        [RequiresDynamicCode(
            "Binds OpcUaServerOptions using reflection-based configuration binding. " +
            "Use the Action<OpcUaServerOptions> overload for AOT consumers.")]
        public static IOpcUaServerBuilder AddServer(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            EnsureFirstRegistration(builder.Services);

            builder.Services.AddOptions<OpcUaServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);

            return new OpcUaServerBuilder(builder.Services);
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(OpcUaServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddServer has already been called. At most one regular OPC UA server may be registered per service collection.");
                }
            }
            services.AddSingleton<OpcUaServerRegistrationMarker>();
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));
            services.AddHostedService<OpcUaServerHostedService>();
            OpcUaServiceCollectionExtensions.AddOpcUa(services).AddApplicationInstance();
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
                Services.AddSingleton<TFactory>();
                Services.AddSingleton(sp => new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<TFactory>()));
                return this;
            }

            public IOpcUaServerBuilder AddSyncNodeManager<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
                where TFactory : class, INodeManagerFactory
            {
                Services.AddSingleton<TFactory>();
                Services.AddSingleton(sp => new OpcUaServerNodeManagerRegistration(
                    sp.GetRequiredService<TFactory>()));
                return this;
            }
        }

        private sealed class OpcUaServerRegistrationMarker
        {
        }
    }
}
