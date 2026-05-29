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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
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
            RegisterCommonServices(builder.Services, enableConfiguredIdentityAuthenticators: false);

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
            return builder;
        }

        /// <summary>
        /// Registers a server-side identity authenticator and adds it to the server registry on startup.
        /// </summary>
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
                (sp, _) => new IUserTokenAuthenticator[] { sp.GetRequiredService<TAuth>() }));
            return builder;
        }

        /// <summary>
        /// Registers a server-side identity augmenter and adds it to the server registry on startup.
        /// </summary>
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
        /// Registers the built-in identity authenticators that can be resolved from DI and server state.
        /// </summary>
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
                ICertificateValidatorEx? certificateValidator = options.CertificateValidator ??
                    services.GetService<ICertificateValidatorEx>() ??
                    serverCertificateValidator;
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
            services.AddOptions<RoleConfigurationOptions>();
            if (enableConfiguredIdentityAuthenticators)
            {
                services.AddSingleton(new OpcUaServerIdentityAuthenticatorRegistration(
                    (sp, certificateValidator) =>
                    {
                        if (sp.GetService<OpcUaServerDefaultIdentityAuthenticatorsMarker>() != null)
                        {
                            return Array.Empty<IUserTokenAuthenticator>();
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

            options.ReverseConnect = reverseConnect;
        }

        private static void BindOperationLimits(OpcUaServerOptions options, IConfigurationSection section)
        {
            if (!section.Exists())
            {
                return;
            }

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
            options.OperationLimits = limits;
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
                    services.GetRequiredService<HttpClient>(),
                    services.GetRequiredService<TimeProvider>(),
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

            public async ValueTask<IReadOnlyList<IssuerVerificationKey>> GetKeysAsync(
                string? keyId,
                CancellationToken ct = default)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(CombinedIssuerKeyResolver));
                }

                var keys = new List<IssuerVerificationKey>();
                foreach (IIssuerKeyResolver resolver in m_resolvers)
                {
                    IReadOnlyList<IssuerVerificationKey> resolved = await resolver
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
