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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;

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
            RegisterCommonServices(builder.Services, enableConfiguredIdentityAuthenticators: true);
            RegisterConfiguredJwtIssuers(builder.Services);

            return new OpcUaServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>
        /// using a custom <see cref="StandardServer"/> subclass.
        /// </summary>
        /// <typeparam name="TServer">The server type created by the hosted service.</typeparam>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Required callback used to populate
        /// <see cref="OpcUaServerOptions"/>.</param>
        /// <returns>An <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
        public static IOpcUaServerBuilder AddServer<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TServer>(
                this IOpcUaBuilder builder,
                Action<OpcUaServerOptions> configure)
            where TServer : StandardServer
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
            RegisterCommonServices(builder.Services, enableConfiguredIdentityAuthenticators: true);
            RegisterConfiguredJwtIssuers(builder.Services);
            builder.Services.Replace(ServiceDescriptor.Singleton<IOpcUaServerFactory,
                ActivatorOpcUaServerFactory<TServer>>());

            return new OpcUaServerBuilder(builder.Services);
        }

        /// <summary>
        /// Registers an OPC UA server hosted as an <see cref="IHostedService"/>
        /// with options bound from the supplied <paramref name="configuration"/>
        /// section <see cref="DefaultConfigurationSection"/> (<c>OpcUa:Server</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Server</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
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
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">An OPC UA server
        /// is already registered.</exception>
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

            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options => BindOpcUaServerOptions(options, section));

            IConfigurationSection rolesSection = section.GetSection("Roles");
            if (rolesSection.Exists())
            {
                builder.Services.AddOptions<RoleConfigurationOptions>().Bind(rolesSection);
                builder.Services.TryAddSingleton<OpcUaServerRoleManagerRegistration>();
                builder.Services.TryAddSingleton(CreateConfiguredRoleManager);
            }

            IConfigurationSection identitySection = section.GetSection("Identity");
            if (identitySection.Exists())
            {
                foreach (IConfigurationSection issuerSection in identitySection.GetSection("Issuers").GetChildren())
                {
                    RegisterJwtIssuer(builder.Services, BindJwtIssuerOptions(issuerSection));
                }
            }

            RegisterCommonServices(builder.Services, identitySection.Exists());

            return new OpcUaServerBuilder(builder.Services);
        }

        /// <summary>
        /// Configures the default role manager used by the hosted OPC UA server.
        /// </summary>
        /// <param name="builder">The server builder returned by
        /// <see cref="AddServer(IOpcUaBuilder, Action{OpcUaServerOptions})"/>.</param>
        /// <param name="configure">Callback used to populate
        /// <see cref="RoleConfigurationOptions"/>.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureRoles(
            this IOpcUaServerBuilder builder,
            Action<RoleConfigurationOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.AddOptions<RoleConfigurationOptions>().Configure(configure);
            builder.Services.TryAddSingleton<OpcUaServerRoleManagerRegistration>();
            builder.Services.TryAddSingleton(CreateConfiguredRoleManager);
            return builder;
        }

        /// <summary>
        /// Configures the default role manager from a configuration section.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="section">Configuration section bound to <see cref="RoleConfigurationOptions"/>.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder ConfigureRoles(
            this IOpcUaServerBuilder builder,
            IConfiguration section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            builder.Services.AddOptions<RoleConfigurationOptions>().Bind(section);
            builder.Services.TryAddSingleton<OpcUaServerRoleManagerRegistration>();
            builder.Services.TryAddSingleton(CreateConfiguredRoleManager);
            return builder;
        }

        /// <summary>
        /// Registers a role manager that is installed on the hosted server at startup.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="roleManager">Role manager instance to use.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> or
        /// <paramref name="roleManager"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddRoleManager(
            this IOpcUaServerBuilder builder,
            IRoleManager roleManager)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (roleManager is null)
            {
                throw new ArgumentNullException(nameof(roleManager));
            }

            builder.Services.Replace(ServiceDescriptor.Singleton(roleManager));
            builder.Services.TryAddSingleton<OpcUaServerRoleManagerRegistration>();
            return builder;
        }

        /// <summary>
        /// Registers a role manager type that is installed on the hosted server at startup.
        /// </summary>
        /// <typeparam name="TManager">The role manager implementation type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddRoleManager<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager>(
                this IOpcUaServerBuilder builder)
            where TManager : class, IRoleManager
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.Replace(ServiceDescriptor.Singleton<IRoleManager, TManager>());
            builder.Services.TryAddSingleton<OpcUaServerRoleManagerRegistration>();
            return builder;
        }

        /// <summary>
        /// Registers a server-side identity authenticator and adds it to the server registry on startup.
        /// </summary>
        /// <typeparam name="TAuth">The concrete authenticator type to register.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddIdentityAuthenticator<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAuth>(
                this IOpcUaServerBuilder builder)
            where TAuth : class, IUserTokenAuthenticator
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<TAuth>();
            builder.Services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (sp, _) => [sp.GetRequiredService<TAuth>()]));
            return builder;
        }

        /// <summary>
        /// Registers a server-side identity augmenter and adds it to the server registry on startup.
        /// </summary>
        /// <typeparam name="TAugmenter">The concrete augmenter type to register.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddIdentityAugmenter<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAugmenter>(
                this IOpcUaServerBuilder builder)
            where TAugmenter : class, IIdentityAugmenter
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<TAugmenter>();
            builder.Services.AddSingleton(new OpcUaServerIdentityAugmenterRegistration(
                sp => sp.GetRequiredService<TAugmenter>()));
            return builder;
        }

        /// <summary>
        /// Registers a server-side identity augmenter factory and adds it to the server registry on startup.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddIdentityAugmenter(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IIdentityAugmenter> factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddSingleton(new OpcUaServerIdentityAugmenterRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Enables the OPC 10000-12 §8 resource-server KeyCredential push model.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder WithKeyCredentialPush(
            this IOpcUaServerBuilder builder,
            Action<KeyCredentialPushOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configure != null)
            {
                builder.Services.AddOptions<KeyCredentialPushOptions>().Configure(configure);
            }
            else
            {
                builder.Services.AddOptions<KeyCredentialPushOptions>();
            }

            builder.Services.TryAddSingleton<IKeyCredentialStore>(sp =>
                new InMemoryKeyCredentialStore(
                    sp.GetService<ISecretStore>() ?? new InMemorySecretStore("KeyCredentialPush")));
            builder.Services.AddSingleton(sp => new KeyCredentialPushSubject(
                sp.GetRequiredService<IKeyCredentialStore>(),
                sp.GetRequiredService<IOptions<KeyCredentialPushOptions>>().Value));
            return builder;
        }

        /// <summary>
        /// Configures the Optional OPC 10000-12 §7.10.3
        /// <c>ServerConfigurationType</c> surface: the <c>HasSecureElement</c>
        /// and <c>InApplicationSetup</c> Properties, plus the timing of
        /// <c>ResetToServerDefaults</c> and the <c>ConfigurationFile</c>. Each
        /// member is only exposed when configured.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureServerConfiguration(
            this IOpcUaServerBuilder builder,
            Action<ServerConfigurationOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.AddOptions<ServerConfigurationOptions>().Configure(configure);
            return builder;
        }

        /// <summary>
        /// Exposes the Optional OPC 10000-12 §7.10.13
        /// <c>ResetToServerDefaults</c> Method on the <c>ServerConfiguration</c>
        /// Object and delegates the reset to <typeparamref name="TProvider"/>.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The reset provider implementation to resolve from DI.
        /// </typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder WithServerConfigurationReset<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaServerBuilder builder)
            where TProvider : class, IServerConfigurationResetProvider
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<IServerConfigurationResetProvider, TProvider>();
            return builder;
        }

        /// <summary>
        /// Exposes the Optional OPC 10000-12 §7.10.13
        /// <c>ResetToServerDefaults</c> Method on the <c>ServerConfiguration</c>
        /// Object and delegates the reset to <paramref name="provider"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder WithServerConfigurationReset(
            this IOpcUaServerBuilder builder,
            IServerConfigurationResetProvider provider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            builder.Services.TryAddSingleton(provider);
            return builder;
        }

        /// <summary>
        /// Exposes the Optional OPC 10000-12 §7.10.20 <c>ConfigurationFile</c>
        /// Object on the <c>ServerConfiguration</c> Object and backs its
        /// read/update flow with <typeparamref name="TProvider"/>.
        /// </summary>
        /// <typeparam name="TProvider">
        /// The configuration-file provider implementation to resolve from DI.
        /// </typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder WithApplicationConfigurationFile<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaServerBuilder builder)
            where TProvider : class, IApplicationConfigurationFileProvider
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<IApplicationConfigurationFileProvider, TProvider>();
            return builder;
        }

        /// <summary>
        /// Exposes the Optional OPC 10000-12 §7.10.20 <c>ConfigurationFile</c>
        /// Object on the <c>ServerConfiguration</c> Object and backs its
        /// read/update flow with <paramref name="provider"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder WithApplicationConfigurationFile(
            this IOpcUaServerBuilder builder,
            IApplicationConfigurationFileProvider provider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            builder.Services.TryAddSingleton(provider);
            return builder;
        }

        /// <summary>
        /// Registers the built-in identity authenticators that can be resolved from DI and server state.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddDefaultIdentityAuthenticators(
            this IOpcUaServerBuilder builder,
            Action<DefaultAuthenticatorOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new DefaultAuthenticatorOptions();
            configure(options);
            RegisterDefaultIdentityAuthenticators(builder.Services, options);
            return builder;
        }

        /// <summary>
        /// Registers the built-in identity authenticators from a configuration section.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="section">Configuration section bound to <see cref="DefaultAuthenticatorOptions"/>.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder AddDefaultIdentityAuthenticators(
            this IOpcUaServerBuilder builder,
            IConfiguration section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            DefaultAuthenticatorOptions options = BindDefaultAuthenticatorOptions(section);
            RegisterDefaultIdentityAuthenticators(builder.Services, options);
            return builder;
        }

        /// <summary>
        /// Registers a trusted JWT issuer from code.
        /// </summary>
        /// <remarks>
        /// When both <see cref="JwtIssuerOptions.JwksUri"/> and static keys are supplied,
        /// the registered resolver queries JWKS first and static keys second so online
        /// key rotation wins while inline keys remain available as a fallback.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddJwtIssuer(
            this IOpcUaServerBuilder builder,
            Action<JwtIssuerOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new JwtIssuerOptions();
            configure(options);
            RegisterJwtIssuer(builder.Services, options);
            return builder;
        }

        /// <summary>
        /// Registers a trusted JWT issuer from a configuration section.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddJwtIssuer(
            this IOpcUaServerBuilder builder,
            IConfiguration section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            RegisterJwtIssuer(builder.Services, BindJwtIssuerOptions(section));
            return builder;
        }

        /// <summary>
        /// Registers durable subscription persistence services used by
        /// <see cref="StandardServer"/> during startup.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddDurableSubscriptions(
            this IOpcUaServerBuilder builder,
            ISubscriptionStore subscriptionStore,
            IMonitoredItemQueueFactory monitoredItemQueueFactory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (subscriptionStore is null)
            {
                throw new ArgumentNullException(nameof(subscriptionStore));
            }
            if (monitoredItemQueueFactory is null)
            {
                throw new ArgumentNullException(nameof(monitoredItemQueueFactory));
            }

            builder.Services.AddSingleton(subscriptionStore);
            builder.Services.AddSingleton(monitoredItemQueueFactory);
            return builder;
        }

        /// <summary>
        /// Registers a session manager factory used by the hosted server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSessionManager(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISessionManager> factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddSingleton(new OpcUaServerSessionManagerRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Registers a session manager type created with dependency injection.
        /// </summary>
        /// <typeparam name="TManager">The session manager type.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSessionManager<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager>(
                this IOpcUaServerBuilder builder)
            where TManager : class, ISessionManager
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransient<TManager>();
            builder.Services.AddSingleton(new OpcUaServerSessionManagerRegistration(
                (sp, server, configuration) => ActivatorUtilities.CreateInstance<TManager>(
                    sp,
                    server,
                    configuration)));
            return builder;
        }

        /// <summary>
        /// Registers a subscription manager factory used by the hosted server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSubscriptionManager(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IServerInternal, ApplicationConfiguration, ISubscriptionManager> factory)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddSingleton(new OpcUaServerSubscriptionManagerRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Registers a subscription manager type created with dependency injection.
        /// </summary>
        /// <typeparam name="TManager">The subscription manager type.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSubscriptionManager<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager>(
                this IOpcUaServerBuilder builder)
            where TManager : class, ISubscriptionManager
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddTransient<TManager>();
            builder.Services.AddSingleton(new OpcUaServerSubscriptionManagerRegistration(
                (sp, server, configuration) => ActivatorUtilities.CreateInstance<TManager>(
                    sp,
                    server,
                    configuration)));
            return builder;
        }

        /// <summary>
        /// Registers a server-wide historian provider as the default provider.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddHistorian(
            this IOpcUaServerBuilder builder,
            IHistorianProvider provider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            builder.Services.AddSingleton(provider);
            builder.Services.AddSingleton(new OpcUaServerHistorianRegistration(provider));
            return builder;
        }

        /// <summary>
        /// Registers the Part 20 file system provider and node manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddFileSystem(
            this IOpcUaServerBuilder builder,
            IFileSystemProvider provider)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            builder.Services.AddSingleton(provider);
            builder.Services.AddSingleton<FileSystemNodeManagerFactory>();
            builder.Services.AddSingleton(sp => new OpcUaServerNodeManagerRegistration(
                (IAsyncNodeManagerFactory)sp.GetRequiredService<FileSystemNodeManagerFactory>()));
            return builder;
        }

        /// <summary>
        /// Registers a physical-directory Part 20 file system provider and node manager.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddFileSystem(
            this IOpcUaServerBuilder builder,
            string rootDirectory,
            string? mountName = null,
            bool isWritable = true)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.AddFileSystem(new PhysicalFileSystemProvider(rootDirectory, mountName, isWritable));
        }

        /// <summary>
        /// Registers a fluent node manager built from a namespace URI and configuration callback.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="namespaceUri">Namespace URI owned by the fluent node manager.</param>
        /// <param name="build">Callback that wires fluent nodes, alarms, state machines and simulations.</param>
        /// <returns>The same <see cref="IOpcUaServerBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddNodeManager(
            this IOpcUaServerBuilder builder,
            string namespaceUri,
            Action<INodeManagerBuilder> build)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            var factory = new FluentNodeManagerFactory(namespaceUri, build);
            builder.Services.AddSingleton<IAsyncNodeManagerFactory>(factory);
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
            return builder;
        }

        /// <summary>
        /// Registers the server secret store used by server features that persist secrets.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSecretStore(
            this IOpcUaServerBuilder builder,
            ISecretStore secretStore)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (secretStore is null)
            {
                throw new ArgumentNullException(nameof(secretStore));
            }

            builder.Services.AddSingleton(secretStore);
            return builder;
        }

        /// <summary>
        /// Registers a certificate manager for the hosted server configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddCertificateManager(
            this IOpcUaServerBuilder builder,
            ICertificateManager certificateManager)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (certificateManager is null)
            {
                throw new ArgumentNullException(nameof(certificateManager));
            }

            builder.Services.AddSingleton(certificateManager);
            return builder;
        }

        /// <summary>
        /// Registers a Part 17 alias-name store with the hosted server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddAliasNameStore(
            this IOpcUaServerBuilder builder,
            IAliasNameStore store)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (store is null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton(new OpcUaServerAliasNameStoreRegistration(store));
            return builder;
        }

        /// <summary>
        /// Registers a Part 17 alias-name store registry whose stores are copied into the server registry.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddAliasNameStoreRegistry(
            this IOpcUaServerBuilder builder,
            IAliasNameStoreRegistry registry)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            builder.Services.AddSingleton(registry);
            return builder;
        }

        /// <summary>
        /// Configures server-side reverse connect on the hosted server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddReverseConnect(
            this IOpcUaServerBuilder builder,
            Action<ServerReverseConnectOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options =>
                {
                    options.ReverseConnect ??= new ServerReverseConnectOptions();
                    configure(options.ReverseConnect);
                });
            return builder;
        }

        /// <summary>
        /// Configures server-side reverse connect from a configuration section.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddReverseConnect(
            this IOpcUaServerBuilder builder,
            IConfiguration section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options => options.ReverseConnect = BindServerReverseConnectOptions(section));
            return builder;
        }

        /// <summary>
        /// Configures operation limits advertised by the hosted server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureOperationLimits(
            this IOpcUaServerBuilder builder,
            Action<OperationLimitsOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options =>
                {
                    options.OperationLimits ??= new OperationLimitsOptions();
                    configure(options.OperationLimits);
                });
            return builder;
        }

        /// <summary>
        /// Configures operation limits from a configuration section.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder ConfigureOperationLimits(
            this IOpcUaServerBuilder builder,
            IConfiguration section)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            builder.Services.AddOptions<OpcUaServerOptions>()
                .Configure(options => options.OperationLimits = BindOperationLimitsOptions(section));
            return builder;
        }

        /// <summary>
        /// Registers a batteries-included reference-style demo server.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddReferenceServer(
            this IOpcUaBuilder builder,
            Action<OpcUaServerOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            IOpcUaServerBuilder serverBuilder = builder.AddServer(options =>
            {
                options.ApplicationName = "OpcUaReferenceServer";
                options.ApplicationUri = "urn:localhost:OpcUaReferenceServer";
                options.ProductUri = "http://opcfoundation.org/UA/ReferenceServer";
                options.IncludeSignAndEncryptPolicies = true;
                options.IncludeUnsecurePolicyNone = false;
                configure?.Invoke(options);
            });
            return serverBuilder
                .AddRoleManager<RoleManager>()
                .AddNodeManager(
                    "http://opcfoundation.org/UA/ReferenceServer",
                    nodeManager => nodeManager.Node("ReferenceServer"));
        }

        /// <summary>
        /// Registers a hardened hosted server preset with secure policies, identity authenticators and roles.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddSecureServer(
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

            IOpcUaServerBuilder serverBuilder = builder.AddServer(options =>
            {
                options.IncludeSignAndEncryptPolicies = true;
                options.IncludeUnsecurePolicyNone = false;
                options.UserTokenPolicies.Clear();
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.Certificate });
                configure(options);
            });
            serverBuilder.Services.TryAddSingleton<IUserDatabase, LinqUserDatabase>();
            serverBuilder.Services.TryAddSingleton<IUserManagement, UserManagement>();
            return serverBuilder
                .AddRoleManager<RoleManager>()
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = true;
                    options.EnableJwt = false;
                });
        }

        /// <summary>
        /// Registers a historian provider together with a Part 20 file-system mount.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaServerBuilder AddHistorianFileStore(
            this IOpcUaServerBuilder builder,
            IHistorianProvider historian,
            string rootDirectory,
            string? mountName = null,
            bool isWritable = true)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (historian is null)
            {
                throw new ArgumentNullException(nameof(historian));
            }

            return builder
                .AddHistorian(historian)
                .AddFileSystem(rootDirectory, mountName, isWritable);
        }

        private static void RegisterDefaultIdentityAuthenticators(
            IServiceCollection services,
            DefaultAuthenticatorOptions options)
        {
            services.AddSingleton<OpcUaServerDefaultIdentityAuthenticatorsMarker>();
            services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (sp, certificateValidator) => CreateDefaultIdentityAuthenticators(
                    sp,
                    certificateValidator,
                    options)));
        }

        private static IEnumerable<IUserTokenAuthenticator> CreateDefaultIdentityAuthenticators(
            IServiceProvider services,
            ICertificateValidatorEx? serverCertificateValidator,
            DefaultAuthenticatorOptions options)
        {
            if (options.EnableAnonymous)
            {
                yield return new AnonymousAuthenticator();
            }

            if (options.EnableUserNamePassword)
            {
                IUserDatabase? userDatabase = options.UserDatabase ?? services.GetService<IUserDatabase>();
                IUserManagement? userManagement = options.UserManagement ?? services.GetService<IUserManagement>();
                if (userDatabase != null && userManagement != null)
                {
                    yield return new UserNamePasswordAuthenticator(
                        userDatabase,
                        userManagement,
                        services.GetRequiredService<ITelemetryContext>());
                }
            }

            if (options.EnableX509)
            {
                ICertificateValidatorEx? certificateValidator =
                    services.GetService<ICertificateValidatorEx>() ?? serverCertificateValidator;
                if (certificateValidator != null)
                {
                    yield return new X509Authenticator(
                        certificateValidator,
                        options.UserCertificateTrustList);
                }
            }

            if (options.EnableJwt)
            {
                if (options.IssuerKeyResolver != null)
                {
                    if (!string.IsNullOrEmpty(options.ExpectedAudience))
                    {
                        yield return new JwtAuthenticator(
                            options.IssuerKeyResolver,
                            options.ExpectedAudience,
                            options.ClockSkewTolerance);
                    }
                    yield break;
                }

                bool haveIssuerRegistrations = false;
                foreach (JwtIssuerRegistration registration in services.GetServices<JwtIssuerRegistration>())
                {
                    haveIssuerRegistrations = true;
                    string? audience = registration.Audience ?? options.ExpectedAudience;
                    if (!string.IsNullOrEmpty(audience))
                    {
                        yield return new JwtAuthenticator(
                            registration.KeyResolver,
                            audience,
                            options.ClockSkewTolerance);
                    }
                }

                if (!haveIssuerRegistrations && !string.IsNullOrEmpty(options.ExpectedAudience))
                {
                    foreach (IIssuerKeyResolver keyResolver in services.GetServices<IIssuerKeyResolver>())
                    {
                        yield return new JwtAuthenticator(
                            keyResolver,
                            options.ExpectedAudience,
                            options.ClockSkewTolerance);
                    }
                }
            }
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

        private static void RegisterCommonServices(
            IServiceCollection services,
            bool enableConfiguredIdentityAuthenticators)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            // The PushManagement transaction coordinator holds mutable
            // per-server transaction state (OPC 10000-12 §§7.10.2-7.10.11) and
            // must therefore NOT be a shared singleton: two servers created
            // from the same container (e.g. via IOpcUaServerFactory) would
            // otherwise corrupt each other's transactions. It is deliberately
            // left unregistered so each server's ConfigurationNodeManager owns
            // its own instance (see ConfigurationNodeManager's coordinator
            // fallback). A host that wants to observe or replace it can still
            // register a custom IPushConfigurationTransactionCoordinator, which
            // DependencyInjectionStandardServer honors as-is.
            services.TryAddSingleton<IPendingCertificateKeyStore, DirectoryPendingCertificateKeyStore>();
            services
                .TryAddSingleton<IPushCertificateKeyGenerator, AdditionalEntropyCertificateKeyGenerator>();
            services
                .TryAddSingleton<IPushConfigurationTrustListEffectHandler, PushConfigurationTrustListEffectHandler>();
            services.AddOptions<RoleConfigurationOptions>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IOpcUaApplicationConfigurationFeature,
                    OpcUaServerApplicationConfigurationFeature>());
            if (enableConfiguredIdentityAuthenticators)
            {
                services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                    (sp, certificateValidator) =>
                    {
                        if (sp.GetService<OpcUaServerDefaultIdentityAuthenticatorsMarker>() != null)
                        {
                            return [];
                        }

                        OpcUaServerOptions options = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
                        return CreateDefaultIdentityAuthenticators(
                            sp,
                            certificateValidator,
                            options.Identity.Defaults);
                    }));
            }
            services.AddHostedService<OpcUaServerHostedService>();
            services.AddOpcUa().AddApplicationInstance();
            services.TryAddSingleton<IOpcUaServerFactory, DefaultOpcUaServerFactory>();
            RegisterFallbackAnonymousAuthenticator(services);
        }

        private static IRoleManager CreateConfiguredRoleManager(IServiceProvider services)
        {
            var roleManager = new RoleManager();
            RoleConfigurationOptions options = services.GetRequiredService<IOptions<RoleConfigurationOptions>>().Value;
            ApplyRoleConfiguration(roleManager, options);
            return roleManager;
        }

        private static void ApplyRoleConfiguration(RoleManager roleManager, RoleConfigurationOptions options)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(Opc.Ua.Namespaces.OpcUa);
            foreach (RoleDefinitionOptions role in options.Roles)
            {
                if (string.IsNullOrWhiteSpace(role.Name))
                {
                    continue;
                }

                NodeId roleId = ResolveOrCreateRole(roleManager, namespaces, role);
                foreach (RoleIdentityMappingOptions identity in role.Identities)
                {
                    roleManager.AddIdentity(roleId, new IdentityMappingRuleType
                    {
                        CriteriaType = identity.CriteriaType,
                        Criteria = identity.Criteria
                    });
                }
                foreach (string application in role.Applications)
                {
                    roleManager.AddApplication(roleId, application);
                }
                foreach (EndpointType endpoint in role.Endpoints)
                {
                    roleManager.AddEndpoint(roleId, endpoint);
                }
                roleManager.SetApplicationsExclude(roleId, role.ApplicationsExclude);
                roleManager.SetEndpointsExclude(roleId, role.EndpointsExclude);
                roleManager.SetCustomConfiguration(roleId, role.CustomConfiguration);
            }
        }

        private static NodeId ResolveOrCreateRole(
            RoleManager roleManager,
            NamespaceTable namespaces,
            RoleDefinitionOptions role)
        {
            foreach (NodeId roleId in roleManager.RoleIds)
            {
                RoleEntry? entry = roleManager.GetRole(roleId);
                if (string.Equals(entry?.BrowseName, role.Name, StringComparison.Ordinal))
                {
                    return roleId;
                }
            }

            string? namespaceUri = role.NamespaceUri;
            ushort namespaceIndex = 1;
            if (!string.IsNullOrEmpty(namespaceUri))
            {
                namespaceIndex = namespaces.GetIndexOrAppend(namespaceUri);
            }

            ServiceResult result = roleManager.AddRole(
                role.Name,
                namespaceUri,
                namespaces,
                namespaceIndex,
                out NodeId newRoleId);
            if (ServiceResult.IsBad(result))
            {
                throw new InvalidOperationException(
                    $"Role '{role.Name}' could not be configured: {result}.");
            }
            return newRoleId;
        }

        private static void RegisterFallbackAnonymousAuthenticator(IServiceCollection services)
        {
            services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (sp, _) =>
                {
                    if (sp.GetService<OpcUaServerDefaultIdentityAuthenticatorsMarker>() != null)
                    {
                        return [];
                    }

                    foreach (OpcUaServerIdentityAuthenticatorRegistration registration in
                        sp.GetServices<OpcUaServerIdentityAuthenticatorRegistration>())
                    {
                        if (!registration.IsFallback)
                        {
                            return [];
                        }
                    }

                    return [new AnonymousAuthenticator()];
                },
                isFallback: true));
        }

        /// <summary>
        /// Registers a deferred authenticator source that materializes JWT
        /// authenticators from <see cref="OpcUaServerIdentityOptions.Issuers"/>
        /// configured on the bound <see cref="OpcUaServerOptions"/>. Used by the
        /// <see cref="AddServer(IOpcUaBuilder, Action{OpcUaServerOptions})"/>
        /// overload so issuers set in code (or bound into the options object)
        /// are honored the same way the configuration-section overload honors
        /// <c>OpcUa:Server:Identity:Issuers</c>.
        /// </summary>
        private static void RegisterConfiguredJwtIssuers(IServiceCollection services)
        {
            services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                (sp, _) =>
                {
                    if (sp.GetService<OpcUaServerDefaultIdentityAuthenticatorsMarker>() != null)
                    {
                        return [];
                    }

                    OpcUaServerOptions options = sp.GetRequiredService<IOptions<OpcUaServerOptions>>().Value;
                    return CreateConfiguredJwtIssuerAuthenticators(sp, options);
                }));
        }

        private static IEnumerable<IUserTokenAuthenticator> CreateConfiguredJwtIssuerAuthenticators(
            IServiceProvider services,
            OpcUaServerOptions options)
        {
            DefaultAuthenticatorOptions defaults = options.Identity.Defaults;
            if (!defaults.EnableJwt)
            {
                yield break;
            }

            foreach (JwtIssuerOptions issuer in options.Identity.Issuers)
            {
                issuer.Validate();
                ValidateAlgorithms(issuer);
                string? audience = issuer.Audience ?? defaults.ExpectedAudience;
                if (!string.IsNullOrEmpty(audience))
                {
                    yield return new JwtAuthenticator(
                        CreateIssuerKeyResolver(services, issuer),
                        audience,
                        defaults.ClockSkewTolerance);
                }
            }
        }

        private static void BindOpcUaServerOptions(OpcUaServerOptions options, IConfiguration section)
        {
            BindString(section, nameof(OpcUaServerOptions.ApplicationName), value => options.ApplicationName = value);
            BindString(section, nameof(OpcUaServerOptions.ApplicationUri), value => options.ApplicationUri = value);
            BindString(section, nameof(OpcUaServerOptions.ProductUri), value => options.ProductUri = value);
            BindString(section, nameof(OpcUaServerOptions.SubjectName), value => options.SubjectName = value);
            BindString(section, nameof(OpcUaServerOptions.PkiRoot), value => options.PkiRoot = value);
            BindString(section, nameof(OpcUaServerOptions.RegistrationEndpointUrl),
                value => options.RegistrationEndpointUrl = value);
            BindBoolean(section, nameof(OpcUaServerOptions.AutoAcceptUntrustedCertificates),
                value => options.AutoAcceptUntrustedCertificates = value);
            BindBoolean(section, nameof(OpcUaServerOptions.DiagnosticsEnabled),
                value => options.DiagnosticsEnabled = value);
            BindBoolean(section, nameof(OpcUaServerOptions.IncludeSignAndEncryptPolicies),
                value => options.IncludeSignAndEncryptPolicies = value);
            BindBoolean(section, nameof(OpcUaServerOptions.IncludeUnsecurePolicyNone),
                value => options.IncludeUnsecurePolicyNone = value);
            BindBoolean(section, nameof(OpcUaServerOptions.IncludeEccPolicies),
                value => options.IncludeEccPolicies = value);
            BindBoolean(section, nameof(OpcUaServerOptions.RejectSHA1Certificates),
                value => options.RejectSHA1Certificates = value);
            BindUInt32(section, nameof(OpcUaServerOptions.MaxByteStringLength),
                value => options.MaxByteStringLength = value);
            BindUInt32(section, nameof(OpcUaServerOptions.MaxArrayLength),
                value => options.MaxArrayLength = value);
            BindNullableInt32(section, nameof(OpcUaServerOptions.MaxMessageSize),
                value => options.MaxMessageSize = value);
            BindNullableInt32(section, nameof(OpcUaServerOptions.OperationTimeoutMs),
                value => options.OperationTimeoutMs = value);
            BindUInt16(section, nameof(OpcUaServerOptions.MinCertificateKeySize),
                value => options.MinCertificateKeySize = value);

            BindEndpointUrls(options, section.GetSection(nameof(OpcUaServerOptions.EndpointUrls)));
            BindUserTokenPolicies(options, section.GetSection(nameof(OpcUaServerOptions.UserTokenPolicies)));
            BindReverseConnect(options, section.GetSection(nameof(OpcUaServerOptions.ReverseConnect)));
            BindOperationLimits(options, section.GetSection(nameof(OpcUaServerOptions.OperationLimits)));
            BindIdentity(options, section.GetSection(nameof(OpcUaServerOptions.Identity)));
        }

        private static void BindEndpointUrls(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

            options.EndpointUrls.Clear();
            foreach (IConfigurationSection child in section.GetChildren())
            {
                if (!string.IsNullOrEmpty(child.Value))
                {
                    options.EndpointUrls.Add(child.Value);
                }
            }
        }

        private static void BindUserTokenPolicies(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

            options.UserTokenPolicies.Clear();
            foreach (IConfigurationSection child in section.GetChildren())
            {
                var policy = new OpcUaUserTokenPolicy();
                string? tokenType = child[nameof(OpcUaUserTokenPolicy.TokenType)];
                if (!string.IsNullOrEmpty(tokenType) &&
                    Enum.TryParse(tokenType, ignoreCase: true, out UserTokenType parsedTokenType))
                {
                    policy.TokenType = parsedTokenType;
                }
                options.UserTokenPolicies.Add(policy);
            }
        }

        private static void BindReverseConnect(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

            options.ReverseConnect = BindServerReverseConnectOptions(section);
        }

        private static ServerReverseConnectOptions BindServerReverseConnectOptions(IConfiguration section)
        {
            var reverseConnect = new ServerReverseConnectOptions();
            BindInt32(section, nameof(ServerReverseConnectOptions.ConnectIntervalMs),
                value => reverseConnect.ConnectIntervalMs = value);
            BindInt32(section, nameof(ServerReverseConnectOptions.ConnectTimeoutMs),
                value => reverseConnect.ConnectTimeoutMs = value);
            BindInt32(section, nameof(ServerReverseConnectOptions.RejectTimeoutMs),
                value => reverseConnect.RejectTimeoutMs = value);

            foreach (IConfigurationSection child in section.GetSection(nameof(ServerReverseConnectOptions.Clients)).GetChildren())
            {
                var client = new ServerReverseConnectClientOptions();
                BindString(child, nameof(ServerReverseConnectClientOptions.EndpointUrl),
                    value => client.EndpointUrl = value);
                BindInt32(child, nameof(ServerReverseConnectClientOptions.Timeout), value => client.Timeout = value);
                BindInt32(child, nameof(ServerReverseConnectClientOptions.MaxSessionCount),
                    value => client.MaxSessionCount = value);
                BindBoolean(child, nameof(ServerReverseConnectClientOptions.Enabled), value => client.Enabled = value);
                reverseConnect.Clients.Add(client);
            }

            return reverseConnect;
        }

        private static void BindOperationLimits(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

            options.OperationLimits = BindOperationLimitsOptions(section);
        }

        private static OperationLimitsOptions BindOperationLimitsOptions(IConfiguration section)
        {
            var limits = new OperationLimitsOptions();
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerRead), value => limits.MaxNodesPerRead = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerHistoryReadData),
                value => limits.MaxNodesPerHistoryReadData = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerHistoryReadEvents),
                value => limits.MaxNodesPerHistoryReadEvents = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerWrite), value => limits.MaxNodesPerWrite = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerHistoryUpdateData),
                value => limits.MaxNodesPerHistoryUpdateData = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerHistoryUpdateEvents),
                value => limits.MaxNodesPerHistoryUpdateEvents = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerMethodCall),
                value => limits.MaxNodesPerMethodCall = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerBrowse), value => limits.MaxNodesPerBrowse = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerRegisterNodes),
                value => limits.MaxNodesPerRegisterNodes = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerTranslateBrowsePathsToNodeIds),
                value => limits.MaxNodesPerTranslateBrowsePathsToNodeIds = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxNodesPerNodeManagement),
                value => limits.MaxNodesPerNodeManagement = value);
            BindUInt32(section, nameof(OperationLimitsOptions.MaxMonitoredItemsPerCall),
                value => limits.MaxMonitoredItemsPerCall = value);
            return limits;
        }

        private static void BindIdentity(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

            var identity = new OpcUaServerIdentityOptions
            {
                Defaults = BindDefaultAuthenticatorOptions(section.GetSection(nameof(OpcUaServerIdentityOptions.Defaults)))
            };
            foreach (IConfigurationSection issuerSection in section.GetSection(nameof(OpcUaServerIdentityOptions.Issuers)).GetChildren())
            {
                identity.Issuers.Add(BindJwtIssuerOptions(issuerSection));
            }
            options.Identity = identity;
        }

        private static DefaultAuthenticatorOptions BindDefaultAuthenticatorOptions(IConfiguration section)
        {
            var options = new DefaultAuthenticatorOptions();
            BindBoolean(section, nameof(DefaultAuthenticatorOptions.EnableAnonymous),
                value => options.EnableAnonymous = value);
            BindBoolean(section, nameof(DefaultAuthenticatorOptions.EnableUserNamePassword),
                value => options.EnableUserNamePassword = value);
            BindBoolean(section, nameof(DefaultAuthenticatorOptions.EnableX509),
                value => options.EnableX509 = value);
            BindBoolean(section, nameof(DefaultAuthenticatorOptions.EnableJwt),
                value => options.EnableJwt = value);

            string? expectedAudience = section[nameof(DefaultAuthenticatorOptions.ExpectedAudience)];
            if (!string.IsNullOrEmpty(expectedAudience))
            {
                options.ExpectedAudience = expectedAudience;
            }

            string? clockSkew = section[nameof(DefaultAuthenticatorOptions.ClockSkewTolerance)];
            if (!string.IsNullOrEmpty(clockSkew) &&
                TimeSpan.TryParse(clockSkew, CultureInfo.InvariantCulture, out TimeSpan parsedClockSkew))
            {
                options.ClockSkewTolerance = parsedClockSkew;
            }

            string? trustList = section[nameof(DefaultAuthenticatorOptions.UserCertificateTrustList)] ??
                section.GetSection(nameof(DefaultAuthenticatorOptions.UserCertificateTrustList))["Name"];
            if (!string.IsNullOrEmpty(trustList))
            {
                options.UserCertificateTrustList = new TrustListIdentifier(trustList);
            }

            return options;
        }

        private static void BindString(
            IConfiguration section,
            string key,
            Action<string> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value))
            {
                assign(value);
            }
        }

        private static void BindBoolean(
            IConfiguration section,
            string key,
            Action<bool> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value) && bool.TryParse(value, out bool parsed))
            {
                assign(parsed);
            }
        }

        private static void BindInt32(
            IConfiguration section,
            string key,
            Action<int> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value) &&
                int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed))
            {
                assign(parsed);
            }
        }

        private static void BindNullableInt32(
            IConfiguration section,
            string key,
            Action<int?> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value) &&
                int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsed))
            {
                assign(parsed);
            }
        }

        private static void BindUInt32(
            IConfiguration section,
            string key,
            Action<uint> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value) &&
                uint.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint parsed))
            {
                assign(parsed);
            }
        }

        private static void BindUInt16(
            IConfiguration section,
            string key,
            Action<ushort> assign)
        {
            string? value = section[key];
            if (!string.IsNullOrEmpty(value) &&
                ushort.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort parsed))
            {
                assign(parsed);
            }
        }

        private static JwtIssuerOptions BindJwtIssuerOptions(IConfiguration section)
        {
            var options = new JwtIssuerOptions();
            IConfigurationSection algorithmsSection = section.GetSection("Algorithms");
            if (algorithmsSection.Exists())
            {
                options.Algorithms.Clear();
            }

            section.Bind(options);
            if (options.Algorithms.Count == 0)
            {
                options.Algorithms.Add("RS256");
            }
            return options;
        }

        private static void RegisterJwtIssuer(IServiceCollection services, JwtIssuerOptions options)
        {
            options.Validate();
            ValidateAlgorithms(options);

            if (!string.IsNullOrEmpty(options.JwksUri))
            {
                services.TryAddSingleton(TimeProvider.System);
                services.TryAddSingleton<HttpClient>();
            }

            string registrationId = Guid.NewGuid().ToString("N");
            services.AddSingleton(sp => new JwtIssuerRegistration(
                registrationId,
                options.Audience,
                CreateIssuerKeyResolver(sp, options)));
            services.AddSingleton(sp =>
                GetJwtIssuerRegistration(sp, registrationId).KeyResolver);
        }

        private static JwtIssuerRegistration GetJwtIssuerRegistration(
            IServiceProvider services,
            string registrationId)
        {
            foreach (JwtIssuerRegistration registration in services.GetServices<JwtIssuerRegistration>())
            {
                if (string.Equals(registration.Id, registrationId, StringComparison.Ordinal))
                {
                    return registration;
                }
            }

            throw new InvalidOperationException("JWT issuer registration was not found.");
        }

        private static IIssuerKeyResolver CreateIssuerKeyResolver(
            IServiceProvider services,
            JwtIssuerOptions options)
        {
            var resolvers = new List<IIssuerKeyResolver>();
            if (!string.IsNullOrEmpty(options.JwksUri))
            {
                resolvers.Add(new JwksIssuerKeyResolver(
                    options.IssuerUri,
                    options.JwksUri,
                    services.GetService<HttpClient>() ?? s_jwtIssuerHttpClient,
                    services.GetService<TimeProvider>() ?? TimeProvider.System,
                    TimeSpan.FromMinutes(5),
                    options.GetEffectiveAlgorithms()));
            }

            if (options.StaticKeys.Count != 0)
            {
                resolvers.Add(new StaticIssuerKeyResolver(
                    options.IssuerUri,
                    CreateStaticVerificationKeys(options)));
            }

            return resolvers.Count == 1
                ? resolvers[0]
                : new CombinedIssuerKeyResolver(options.IssuerUri, resolvers);
        }

        private static ReadOnlyCollection<IssuerVerificationKey> CreateStaticVerificationKeys(
            JwtIssuerOptions options)
        {
            IReadOnlyList<string> allowedAlgorithms = options.GetEffectiveAlgorithms();
            var keys = new List<IssuerVerificationKey>();
            foreach (JwtStaticKeyOptions keyOptions in options.StaticKeys)
            {
                if (!ContainsOrdinal(allowedAlgorithms, keyOptions.Algorithm))
                {
                    throw new InvalidOperationException(
                        $"Static JWT key algorithm '{keyOptions.Algorithm}' is not allowed for issuer '{options.IssuerUri}'.");
                }
                keys.Add(keyOptions.CreateVerificationKey());
            }
            return keys.AsReadOnly();
        }

        private static void ValidateAlgorithms(JwtIssuerOptions options)
        {
            foreach (string algorithm in options.GetEffectiveAlgorithms())
            {
                if (string.IsNullOrWhiteSpace(algorithm))
                {
                    throw new InvalidOperationException(
                        $"JWT issuer '{options.IssuerUri}' contains an empty algorithm entry.");
                }
            }
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> values, string value)
        {
            foreach (string item in values)
            {
                if (string.Equals(item, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shared <see cref="HttpClient"/> used to build JWKS issuer key resolvers on the
        /// <see cref="AddServer(IOpcUaBuilder, Action{OpcUaServerOptions})"/> path when the
        /// container has not registered one. Reused for the process lifetime per
        /// <see cref="HttpClient"/> guidance; <see cref="JwksIssuerKeyResolver"/> does not dispose it.
        /// </summary>
        private static readonly HttpClient s_jwtIssuerHttpClient = new();

        private sealed class JwtIssuerRegistration : IDisposable
        {
            public JwtIssuerRegistration(
                string id,
                string? audience,
                IIssuerKeyResolver keyResolver)
            {
                Id = id;
                Audience = audience;
                KeyResolver = keyResolver;
            }

            public string Id { get; }

            public string? Audience { get; }

            public IIssuerKeyResolver KeyResolver { get; }

            public void Dispose()
            {
                (KeyResolver as IDisposable)?.Dispose();
            }
        }

        private sealed class CombinedIssuerKeyResolver : IIssuerKeyResolver, IDisposable
        {
            private readonly IReadOnlyList<IIssuerKeyResolver> m_resolvers;
            private bool m_disposed;

            public CombinedIssuerKeyResolver(string issuerUri, IReadOnlyList<IIssuerKeyResolver> resolvers)
            {
                IssuerUri = issuerUri;
                m_resolvers = resolvers;
            }

            public string IssuerUri { get; }

            public async ValueTask<IReadOnlyList<IIssuerVerificationKey>> GetKeysAsync(
                string? keyId,
                CancellationToken ct = default)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(CombinedIssuerKeyResolver));
                }

                var keys = new List<IIssuerVerificationKey>();
                foreach (IIssuerKeyResolver resolver in m_resolvers)
                {
                    IReadOnlyList<IIssuerVerificationKey> resolved = await resolver
                        .GetKeysAsync(keyId, ct)
                        .ConfigureAwait(false);
                    for (int i = 0; i < resolved.Count; i++)
                    {
                        keys.Add(resolved[i]);
                    }
                }
                return keys.AsReadOnly();
            }

            public void Dispose()
            {
                if (m_disposed)
                {
                    return;
                }

                m_disposed = true;
                foreach (IIssuerKeyResolver resolver in m_resolvers)
                {
                    (resolver as IDisposable)?.Dispose();
                }
            }
        }

        private sealed class OpcUaServerDefaultIdentityAuthenticatorsMarker;

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

            public IOpcUaServerBuilder AddNodeManager(string namespaceUri, Action<INodeManagerBuilder> build)
            {
                var factory = new FluentNodeManagerFactory(namespaceUri, build);
                Services.AddSingleton<IAsyncNodeManagerFactory>(factory);
                Services.AddSingleton(new OpcUaServerNodeManagerRegistration(factory));
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

        private sealed class OpcUaServerRegistrationMarker;
    }
}
