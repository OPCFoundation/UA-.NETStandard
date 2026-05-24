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
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Server.UserDatabase;

#nullable enable

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Gds.Server.Common</c>: register an OPC UA Global
    /// Discovery Server hosted as an <see cref="IHostedService"/> so
    /// the .NET Generic Host owns its lifetime, logging pipeline and
    /// Ctrl+C / SIGTERM handling.
    /// </summary>
    public static class OpcUaGdsServerBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddGdsServer(IOpcUaBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Gds:Server";

        /// <summary>
        /// Registers an OPC UA Global Discovery Server hosted as an
        /// <see cref="IHostedService"/> and returns an
        /// <see cref="IGdsServerBuilder"/> for chaining the pluggable
        /// service registrations (applications database, certificate
        /// stores, user database, optional token / key-credential /
        /// managed application stores).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The hosted service builds an <see cref="ApplicationConfiguration"/>
        /// from the supplied <see cref="GdsServerOptions"/>, attaches a
        /// <see cref="GlobalDiscoveryServerConfiguration"/> extension,
        /// ensures the application instance certificate is present, then
        /// starts a <see cref="GlobalDiscoverySampleServer"/> subclass
        /// that wires the resolved services into the
        /// <see cref="ApplicationsNodeManager"/>.
        /// </para>
        /// <para>
        /// Calling this method twice on the same
        /// <see cref="IServiceCollection"/> throws
        /// <see cref="InvalidOperationException"/>: at most one GDS
        /// server may be registered per service collection. A GDS
        /// server can coexist with a regular server registered via
        /// <c>.AddServer(...)</c>.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Required callback used to populate
        /// <see cref="GdsServerOptions"/>.</param>
        /// <returns>An <see cref="IGdsServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A GDS server is
        /// already registered.</exception>
        public static IGdsServerBuilder AddGdsServer(
            this IOpcUaBuilder builder,
            Action<GdsServerOptions> configure)
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

            builder.Services.AddOptions<GdsServerOptions>().Configure(configure);
            RegisterCommonServices(builder.Services);

            return new GdsServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an OPC UA Global Discovery Server hosted as an
        /// <see cref="IHostedService"/> with options bound from the
        /// supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/>
        /// (<c>OpcUa:Gds:Server</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Gds:Server</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A GDS server is
        /// already registered.</exception>
        public static IGdsServerBuilder AddGdsServer(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddGdsServer(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers an OPC UA Global Discovery Server hosted as an
        /// <see cref="IHostedService"/> with options bound from the
        /// supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">A GDS server is
        /// already registered.</exception>
        public static IGdsServerBuilder AddGdsServer(
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

            builder.Services.AddOptions<GdsServerOptions>().Bind(section);
            RegisterCommonServices(builder.Services);

            return new GdsServerBuilder(builder.Services);
        }

        private static void EnsureFirstRegistration(IServiceCollection services)
        {
            foreach (ServiceDescriptor d in services)
            {
                if (d.ServiceType == typeof(GdsServerRegistrationMarker))
                {
                    throw new InvalidOperationException(
                        "AddGdsServer has already been called. At most one GDS server may be registered per service collection.");
                }
            }
            services.AddSingleton<GdsServerRegistrationMarker>();
        }

        private static void RegisterCommonServices(IServiceCollection services)
        {
            // Ensure the shared ITelemetryContext and the default
            // IApplicationInstanceFactory are registered (both
            // idempotent: TryAddSingleton). Mirrors the
            // OpcUaServerBuilderExtensions pattern so the GDS feature
            // can be added independently of AddServer.
            OpcUaServiceCollectionExtensions.AddOpcUa(services).AddApplicationInstance();

            services.AddHostedService<GdsServerHostedService>();
        }

        private sealed class GdsServerBuilder : IGdsServerBuilder
        {
            public GdsServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }

            public IGdsServerBuilder AddApplicationsDatabase<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IApplicationsDatabase
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<IApplicationsDatabase>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }

            public IGdsServerBuilder AddApplicationsDatabase(
                Func<IServiceProvider, IApplicationsDatabase> factory)
            {
                if (factory is null)
                {
                    throw new ArgumentNullException(nameof(factory));
                }
                Services.AddSingleton(factory);
                return this;
            }

            public IGdsServerBuilder AddUserDatabase<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IUserDatabase
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<IUserDatabase>(
                    sp => sp.GetRequiredService<T>());

                // If T also implements IGdsUserDatabase, expose it via
                // that interface too so consumers can resolve the
                // GDS-specific membership API directly.
                if (typeof(IGdsUserDatabase).IsAssignableFrom(typeof(T)))
                {
                    Services.AddSingleton<IGdsUserDatabase>(
                        sp => (IGdsUserDatabase)sp.GetRequiredService<T>());
                }
                return this;
            }

            public IGdsServerBuilder AddCertificateGroup<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, ICertificateGroup
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<ICertificateGroup>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }

            public IGdsServerBuilder AddCertificateRequest<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, ICertificateRequest
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<ICertificateRequest>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }

            public IGdsServerBuilder AddAccessTokenProvider<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IAccessTokenProvider
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<IAccessTokenProvider>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }

            public IGdsServerBuilder AddKeyCredentialRequestStore<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IKeyCredentialRequestStore
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<IKeyCredentialRequestStore>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }

            public IGdsServerBuilder AddConfigurationDataStore<
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
                where T : class, IConfigurationDataStore
            {
                Services.AddSingleton<T>();
                Services.AddSingleton<IConfigurationDataStore>(
                    sp => sp.GetRequiredService<T>());
                return this;
            }
        }

        private sealed class GdsServerRegistrationMarker
        {
        }
    }
}
