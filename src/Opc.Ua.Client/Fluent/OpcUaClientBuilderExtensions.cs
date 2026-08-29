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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Client</c>: register OPC UA client services (telemetry,
    /// session factory, <see cref="ManagedSession"/> factory) for
    /// dependency-injected applications.
    /// </summary>
    /// <remarks>
    /// Mirrors the server-side <c>.AddServer(...)</c> pattern. The
    /// returned <see cref="IOpcUaClientBuilder"/> can be extended with
    /// further registrations.
    /// </remarks>
    public static class OpcUaClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddClient(IOpcUaBuilder, IConfiguration)"/> overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Client";

        /// <summary>
        /// Registers OPC UA client services and a lazy
        /// <see cref="Func{T, TResult}"/> factory for
        /// <see cref="ManagedSession"/>. The first call to the factory
        /// connects and caches the session; subsequent calls return the
        /// cached instance.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Configuration delegate for
        /// <see cref="OpcUaClientOptions"/>. Set an explicit
        /// <see cref="OpcUaClientOptions.Configuration"/>, or set
        /// <see cref="OpcUaClientOptions.ApplicationName"/> and the other
        /// application identity/security properties (mirroring
        /// <c>OpcUaServerOptions</c>). The latter compose with a root
        /// <c>ConfigureApplication(...)</c> call made before or after this
        /// <c>AddClient(...)</c> call: both contribute to one shared
        /// <see cref="OpcUaApplicationOptions"/>, with fields explicitly set
        /// via <c>ConfigureApplication(...)</c> winning over the client's
        /// <c>??=</c> defaults. An explicit <see cref="OpcUaClientOptions.Configuration"/>
        /// must not be combined with the application identity properties.
        /// Set <see cref="ManagedSessionOptions.Endpoint"/> when using the
        /// cached fixed-endpoint session delegate.</param>
        /// <returns>An <see cref="IOpcUaClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configure"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Client application
        /// identity options are combined with an explicit
        /// <see cref="OpcUaClientOptions.Configuration"/>.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            Action<OpcUaClientOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new OpcUaClientOptions();
            configure(options);
            EnsureClientConnectGate(options);
            ApplyClientApplicationOptions(builder, options);
            RegisterClientOptions(builder.Services, options);

            RegisterCoreServices(builder.Services);

            return new OpcUaClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers OPC UA client services whose
        /// <see cref="ApplicationConfiguration"/> is loaded from an existing
        /// OPC UA XML configuration file (e.g. <c>MyClient.Config.xml</c>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the migration path for applications that already own a
        /// configuration file: every setting in the file (security
        /// configuration, certificate stores, transport quotas, client
        /// configuration, ...) is applied as-is, exactly as when the file is
        /// loaded through
        /// <c>ApplicationInstance.LoadApplicationConfigurationAsync</c>. The
        /// document is loaded and the application-instance certificate is
        /// ensured on first use (first session connect or reverse-connect
        /// startup). Identity providers, reverse connect, and the other
        /// <see cref="IOpcUaClientBuilder"/> registrations compose with the
        /// loaded file the same way they compose with built configurations.
        /// </para>
        /// <para>
        /// Equivalent to
        /// <see cref="AddClient(IOpcUaBuilder, Action{OpcUaClientOptions})"/>
        /// with <see cref="OpcUaClientOptions.ConfigurationFile"/> set. Use
        /// <paramref name="configure"/> to set the session endpoint and the
        /// other client options, and
        /// <see cref="OpcUaClientOptions.ConfigureLoadedConfiguration"/>
        /// within it to override individual settings of the loaded file from
        /// code.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configurationFile">Path to the application
        /// configuration XML file. A relative path is resolved against the
        /// current working directory.</param>
        /// <param name="configure">Optional configuration delegate for the
        /// remaining <see cref="OpcUaClientOptions"/> (session, identity,
        /// reverse connect, ...). The application identity properties and an
        /// explicit <see cref="OpcUaClientOptions.Configuration"/> must not
        /// be combined with the configuration file.</param>
        /// <returns>An <see cref="IOpcUaClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configurationFile"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="configurationFile"/>
        /// is empty or white space.</exception>
        /// <exception cref="InvalidOperationException">The configuration file
        /// is combined with an explicit
        /// <see cref="OpcUaClientOptions.Configuration"/>, a
        /// <see cref="OpcUaClientOptions.ConfigurationStream"/>, or client
        /// application identity options.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            string configurationFile,
            Action<OpcUaClientOptions>? configure = null)
        {
            if (configurationFile is null)
            {
                throw new ArgumentNullException(nameof(configurationFile));
            }
            if (string.IsNullOrWhiteSpace(configurationFile))
            {
                throw new ArgumentException(
                    "The configuration file path must not be empty.",
                    nameof(configurationFile));
            }

            return builder.AddClient(options =>
            {
                options.ConfigurationFile = configurationFile;
                configure?.Invoke(options);
            });
        }

        /// <summary>
        /// Registers OPC UA client services whose
        /// <see cref="ApplicationConfiguration"/> is loaded from a stream
        /// containing an OPC UA XML configuration document, e.g. an embedded
        /// resource.
        /// </summary>
        /// <remarks>
        /// <para>
        /// See <see cref="AddClient(IOpcUaBuilder, string, Action{OpcUaClientOptions})"/>
        /// for how the loaded configuration is applied. Equivalent to
        /// <see cref="AddClient(IOpcUaBuilder, Action{OpcUaClientOptions})"/>
        /// with <see cref="OpcUaClientOptions.ConfigurationStream"/> set.
        /// </para>
        /// <para>
        /// The stream must remain open until the configuration is first
        /// used; it is read once and disposed after loading.
        /// </para>
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configurationStream">Stream containing the
        /// application configuration XML document.</param>
        /// <param name="configure">Optional configuration delegate for the
        /// remaining <see cref="OpcUaClientOptions"/>.</param>
        /// <returns>An <see cref="IOpcUaClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configurationStream"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">The configuration
        /// stream is combined with an explicit
        /// <see cref="OpcUaClientOptions.Configuration"/>, a
        /// <see cref="OpcUaClientOptions.ConfigurationFile"/>, or client
        /// application identity options.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            Stream configurationStream,
            Action<OpcUaClientOptions>? configure = null)
        {
            if (configurationStream is null)
            {
                throw new ArgumentNullException(nameof(configurationStream));
            }

            return builder.AddClient(options =>
            {
                options.ConfigurationStream = configurationStream;
                configure?.Invoke(options);
            });
        }

        /// <summary>
        /// Registers OPC UA client services with options bound from the
        /// supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/> (<c>OpcUa:Client</c>).
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing
        /// the <c>OpcUa:Client</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers OPC UA client services with options bound from the
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
        public static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            IConfigurationSection section)
        {
            return builder.AddClient(section, postConfigure: null);
        }

        /// <summary>
        /// Registers OPC UA client services with options bound from the
        /// supplied <paramref name="section"/>, applying an optional
        /// <paramref name="postConfigure"/> callback to the bound options
        /// before they are registered. Used by feature builders (e.g.
        /// <c>AddManagedClient</c>) to set option flags without depending
        /// on how the options are later resolved.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        internal static IOpcUaClientBuilder AddClient(
            this IOpcUaBuilder builder,
            IConfigurationSection section,
            Action<OpcUaClientOptions>? postConfigure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section is null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            var options = new OpcUaClientOptions();
            section.Bind(options);
            postConfigure?.Invoke(options);
            EnsureClientConnectGate(options);
            ApplyClientApplicationOptions(builder, options);
            RegisterClientOptions(builder.Services, options);

            RegisterCoreServices(builder.Services);

            return new OpcUaClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers injectable OPC UA discovery operations.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaBuilder AddDiscovery(this IOpcUaBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOpcUa();
            builder.Services.TryAddSingleton<IOpcUaDiscoveryService, OpcUaDiscoveryService>();
            return builder;
        }

        /// <summary>
        /// Registers injectable OPC UA discovery operations.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddDiscovery(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            new OpcUaBuilder(builder.Services).AddDiscovery();
            return builder;
        }

        /// <summary>
        /// Registers a client identity provider implementation.
        /// </summary>
        /// <typeparam name="TProvider">The concrete identity-provider type to register.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddIdentityProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaClientBuilder builder)
            where TProvider : class, IClientIdentityProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddSingleton<TProvider>();
            builder.Services.AddSingleton<IClientIdentityProvider>(
                sp => sp.GetRequiredService<TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers a composite client identity provider built from the
        /// supplied shortcut configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddIdentityProvider(
            this IOpcUaClientBuilder builder,
            Action<CompositeClientIdentityProviderBuilder> configure)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var compositeBuilder = new CompositeClientIdentityProviderBuilder();
            configure(compositeBuilder);
            builder.Services.AddSingleton<IClientIdentityProvider>(
                compositeBuilder.Build());
            return builder;
        }

        /// <summary>
        /// Registers client identity providers bound from configuration.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddIdentityProvider(
            this IOpcUaClientBuilder builder,
            IConfiguration section)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            OpcUaClientIdentityOptions options = BindIdentityOptions(section);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<IClientIdentityProvider>(
                sp => BuildConfiguredIdentityProvider(sp, options));
            return builder;
        }

        /// <summary>
        /// Registers an access-token provider implementation.
        /// </summary>
        /// <typeparam name="TProvider">The concrete access-token-provider type to register.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAccessTokenProvider<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(
            this IOpcUaClientBuilder builder)
            where TProvider : class, IAccessTokenProvider
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<TProvider>();
            builder.Services.AddSingleton<IAccessTokenProvider>(
                sp => sp.GetRequiredService<TProvider>());
            return builder;
        }

        /// <summary>
        /// Registers an access-token provider instance.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAccessTokenProvider(
            this IOpcUaClientBuilder builder,
            IAccessTokenProvider instance)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            builder.Services.AddSingleton(instance);
            return builder;
        }

        /// <summary>
        /// Registers an access-token provider factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaClientBuilder AddAccessTokenProvider(
            this IOpcUaClientBuilder builder,
            Func<IServiceProvider, IAccessTokenProvider> factory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddSingleton(sp => factory(sp) ??
                throw new InvalidOperationException(
                    "Access-token provider factory returned null."));
            return builder;
        }

        /// <summary>
        /// Registers container-default subscription and monitored-item options.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddSubscriptions(
            this IOpcUaClientBuilder builder,
            Action<Opc.Ua.Client.Subscriptions.SubscriptionOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            OptionsBuilder<Opc.Ua.Client.Subscriptions.SubscriptionOptions> subscriptionOptions =
                builder.Services.AddOptions<Opc.Ua.Client.Subscriptions.SubscriptionOptions>();
            if (configure != null)
            {
                subscriptionOptions.Configure(configure);
            }
            builder.Services.AddOptions<Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions>();
            return builder;
        }

        /// <summary>
        /// Registers a keyed managed-session pool backed by <see cref="IManagedSessionFactory"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddManagedClientPool(this IOpcUaClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<IManagedSessionPool, ManagedSessionPool>();
            return builder;
        }

        /// <summary>
        /// Registers a one-shot reverse-connect managed-client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddReverseConnectClient(
            this IOpcUaBuilder builder,
            Action<OpcUaClientOptions> configure,
            Uri serverUri)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (serverUri is null)
            {
                throw new ArgumentNullException(nameof(serverUri));
            }

            IOpcUaClientBuilder clientBuilder = builder.AddClient(configure);
            clientBuilder.Services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(sp =>
            {
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
                ReverseConnectManager manager = sp.GetRequiredService<ReverseConnectManager>();
                ConfiguredEndpoint endpoint = options.Session.Endpoint
                    ?? throw new InvalidOperationException("A session endpoint is required.");
                return ct => factory.ConnectReverseAsync(manager, serverUri, endpoint, ct);
            });
            return clientBuilder;
        }

        /// <summary>
        /// Registers a discovery-then-connect one-shot managed-client factory.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IOpcUaClientBuilder AddDiscoveryAndConnect(
            this IOpcUaClientBuilder builder,
            Action<DiscoveryConnectOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var options = new DiscoveryConnectOptions();
            configure(options);
            builder.AddDiscovery();
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(sp =>
            {
                IOpcUaDiscoveryService discovery = sp.GetRequiredService<IOpcUaDiscoveryService>();
                IManagedSessionFactory factory = sp.GetRequiredService<IManagedSessionFactory>();
                return async ct =>
                {
                    EndpointDescription endpoint = await SelectDiscoveredEndpointAsync(
                        discovery, options, ct).ConfigureAwait(false);
                    return await factory.ConnectAsync(
                        new ConfiguredEndpoint(null, endpoint, null), ct).ConfigureAwait(false);
                };
            });
            return builder;
        }

        private static async Task<EndpointDescription> SelectDiscoveredEndpointAsync(
            IOpcUaDiscoveryService discovery,
            DiscoveryConnectOptions options,
            CancellationToken ct)
        {
            ArrayOf<EndpointDescription> endpoints = await discovery
                .GetEndpointsAsync(options.DiscoveryUrl, ct: ct)
                .ConfigureAwait(false);
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (endpoint.SecurityMode == options.SecurityMode &&
                    string.Equals(endpoint.SecurityPolicyUri, options.SecurityPolicyUri, StringComparison.Ordinal) &&
                    (options.TransportProfileUri == null ||
                        string.Equals(
                            endpoint.TransportProfileUri,
                            options.TransportProfileUri,
                            StringComparison.Ordinal)))
                {
                    return endpoint;
                }
            }

            throw new InvalidOperationException(
                "No discovered endpoint matched the configured security policy and mode.");
        }

        private static OpcUaClientIdentityOptions BindIdentityOptions(
            IConfiguration section)
        {
            IConfiguration identitySection = section;
            IConfigurationSection nested = section.GetSection("Identity");
            if (nested.Exists())
            {
                identitySection = nested;
            }

            var options = new OpcUaClientIdentityOptions();
            identitySection.Bind(options);
            return options;
        }

        private static CompositeClientIdentityProvider BuildConfiguredIdentityProvider(
            IServiceProvider sp,
            OpcUaClientIdentityOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var providers = new List<IClientIdentityProvider>();
            if (options.EnableAnonymous)
            {
                providers.Add(new AnonymousIdentityProvider());
            }
            if (options.UserName != null)
            {
                providers.Add(CreateUserNameProvider(sp, options.UserName));
            }
            if (options.X509 != null)
            {
                providers.Add(CreateX509Provider(sp, options.X509));
            }
            if (options.IssuedToken != null)
            {
                providers.Add(CreateIssuedTokenProvider(sp, options.IssuedToken));
            }

            if (providers.Count == 0)
            {
                throw new InvalidOperationException(
                    "At least one client identity provider must be configured.");
            }

            ApplyConfiguredOrder(providers, options.Order);
            return new CompositeClientIdentityProvider(providers);
        }

        private static UserNamePasswordIdentityProvider CreateUserNameProvider(
            IServiceProvider sp,
            UserNameClientIdentityOptions options)
        {
            ValidateRequired(options.UserName, "UserName.UserName");
            ValidateRequired(options.SecretName, "UserName.SecretName");
            ValidateRequired(options.SecretStoreType, "UserName.SecretStoreType");

            var passwordId = new SecretIdentifier(
                options.SecretName,
                options.SecretStoreType,
                options.SecretStorePath);
            return new UserNamePasswordIdentityProvider(
                options.UserName,
                sp.GetRequiredService<ISecretRegistry>(),
                passwordId);
        }

        private static X509ClientIdentityProvider CreateX509Provider(
            IServiceProvider sp,
            X509ClientIdentityOptions options)
        {
            ValidateRequired(options.StoreType, "X509.StoreType");
            ValidateRequired(options.StorePath, "X509.StorePath");
            if (!string.IsNullOrWhiteSpace(options.SubjectName) &&
                !string.IsNullOrWhiteSpace(options.Thumbprint))
            {
                throw new InvalidOperationException(
                    "X509.SubjectName and X509.Thumbprint are mutually exclusive.");
            }
            if (string.IsNullOrWhiteSpace(options.SubjectName) &&
                string.IsNullOrWhiteSpace(options.Thumbprint))
            {
                throw new InvalidOperationException(
                    "Either X509.SubjectName or X509.Thumbprint must be configured.");
            }

            var certificateId = new CertificateIdentifier
            {
                StoreType = options.StoreType,
                StorePath = options.StorePath,
                SubjectName = options.SubjectName,
                Thumbprint = options.Thumbprint
            };
            return new X509ClientIdentityProvider(
                certificateId,
                sp.GetRequiredService<ICertificatePasswordProvider>(),
                sp.GetRequiredService<ICertificateProvider>());
        }

        private static IssuedTokenIdentityProvider CreateIssuedTokenProvider(
            IServiceProvider sp,
            IssuedTokenClientIdentityOptions options)
        {
            ValidateRequired(options.ProfileUri, "IssuedToken.ProfileUri");
            IAccessTokenProvider accessTokenProvider = ResolveAccessTokenProvider(
                sp,
                options.AuthorityUri);
            return new IssuedTokenIdentityProvider(accessTokenProvider, options.ProfileUri);
        }

        private static IAccessTokenProvider ResolveAccessTokenProvider(
            IServiceProvider sp,
            string? authorityUri)
        {
            var providers = new List<IAccessTokenProvider>();
            providers.AddRange(sp.GetServices<IAccessTokenProvider>());

            if (string.IsNullOrWhiteSpace(authorityUri))
            {
                if (providers.Count == 1)
                {
                    return providers[0];
                }
                if (providers.Count == 0)
                {
                    throw new InvalidOperationException(
                        "IssuedToken identity requires a registered IAccessTokenProvider.");
                }

                throw new InvalidOperationException(
                    "IssuedToken.AuthorityUri must be configured when multiple " +
                    "IAccessTokenProvider services are registered.");
            }

            foreach (IAccessTokenProvider provider in providers)
            {
                if (string.Equals(
                    provider.AuthorityUri,
                    authorityUri,
                    StringComparison.Ordinal))
                {
                    return provider;
                }
            }

            throw new InvalidOperationException(
                "No IAccessTokenProvider is registered for AuthorityUri '" +
                authorityUri +
                "'.");
        }

        private static void ApplyConfiguredOrder(
            List<IClientIdentityProvider> providers,
            IList<string> order)
        {
            if (order == null || order.Count == 0 || providers.Count < 2)
            {
                return;
            }

            var priorities = new Dictionary<UserTokenType, int>();
            for (int i = 0; i < order.Count; i++)
            {
                if (TryMapIdentityName(order[i], out UserTokenType tokenType) &&
                    !priorities.ContainsKey(tokenType))
                {
                    priorities.Add(tokenType, i);
                }
            }

            if (priorities.Count == 0)
            {
                return;
            }

            var ordered = new List<ProviderOrder>(providers.Count);
            for (int i = 0; i < providers.Count; i++)
            {
                ordered.Add(new ProviderOrder(
                    providers[i],
                    GetProviderPriority(providers[i], priorities),
                    i));
            }

            ordered.Sort(CompareProviderOrder);
            providers.Clear();
            foreach (ProviderOrder item in ordered)
            {
                providers.Add(item.Provider);
            }
        }

        private static int GetProviderPriority(
            IClientIdentityProvider provider,
            Dictionary<UserTokenType, int> priorities)
        {
            int priority = int.MaxValue;
            foreach (UserTokenType tokenType in provider.SupportedTokenTypes)
            {
                if (priorities.TryGetValue(tokenType, out int candidate) &&
                    candidate < priority)
                {
                    priority = candidate;
                }
            }
            return priority;
        }

        private static int CompareProviderOrder(ProviderOrder left, ProviderOrder right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0
                ? priority
                : left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private static bool TryMapIdentityName(string name, out UserTokenType tokenType)
        {
            if (string.Equals(name, "X509", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Certificate", StringComparison.OrdinalIgnoreCase))
            {
                tokenType = UserTokenType.Certificate;
                return true;
            }

            if (Enum.TryParse(name, true, out UserTokenType parsed))
            {
                tokenType = parsed;
                return true;
            }

            tokenType = default;
            return false;
        }

        private static void ValidateRequired(string value, string optionName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    optionName + " must be configured.");
            }
        }

        private static bool HasConfiguredIdentity(OpcUaClientIdentityOptions options)
        {
            return !options.EnableAnonymous ||
                options.UserName != null ||
                options.X509 != null ||
                options.IssuedToken != null ||
                options.Order.Count > 0;
        }

        private readonly struct ProviderOrder
        {
            public ProviderOrder(
                IClientIdentityProvider provider,
                int priority,
                int originalIndex)
            {
                Provider = provider;
                Priority = priority;
                OriginalIndex = originalIndex;
            }

            public IClientIdentityProvider Provider { get; }

            public int Priority { get; }

            public int OriginalIndex { get; }
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<ITelemetryContext>(
                sp => new ServiceProviderTelemetryContext(sp));

            services.AddHttpClient(OpcUaHttpClientDefaults.ClientName)
                .AddStandardResilienceHandler();
            services.TryAddSingleton<IOpcUaHttpClientFactory, DefaultOpcUaHttpClientFactory>();

            services.TryAddSingleton<IClientChannelManager>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                TimeProvider? timeProvider = sp.GetService<TimeProvider>();
                ApplicationConfiguration configuration = options.Configuration
                    ?? throw new InvalidOperationException(
                        "OpcUaClientOptions.Configuration is required to construct " +
                        "the IClientChannelManager.");
                IOpcUaHttpClientFactory? httpClientFactory = sp.GetService<IOpcUaHttpClientFactory>();
                // ClientChannelManager always reads from the host-scoped
                // ITransportBindingRegistry (it satisfies ITransportChannelBindings
                // via DefaultTransportBindingRegistry). When an HTTP client
                // factory is injected, wrap the registry in an HTTPS-aware
                // adapter so opc.https / https channels honour the supplied
                // IHttpClientFactory.
                ITransportChannelBindings channelBindings =
                    sp.GetRequiredService<ITransportBindingRegistry>() as ITransportChannelBindings
                    ?? throw new InvalidOperationException(
                        "The injected ITransportBindingRegistry must implement " +
                        "ITransportChannelBindings (DefaultTransportBindingRegistry " +
                        "satisfies this).");
                if (httpClientFactory != null)
                {
                    channelBindings = new HttpsTransportChannelBindings(channelBindings, httpClientFactory);
                }
                return new ClientChannelManager(
                    configuration,
                    telemetry,
                    channelFactory: channelBindings,
                    reconnectPolicy: null,
                    timeProvider: timeProvider,
                    securityPolicies: sp.GetService<ISecurityPolicyRegistry>());
            });

            services.TryAddSingleton<ISessionFactory>(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                TimeProvider? timeProvider = sp.GetService<TimeProvider>();
                return new DefaultSessionFactory(telemetry)
                {
                    SubscriptionEngineFactory =
                        options.Session.SubscriptionEngineFactory
                        ?? new DefaultSubscriptionEngineFactory(timeProvider),
                    TimeProvider = timeProvider,
                    SecurityPolicyRegistry = sp.GetService<ISecurityPolicyRegistry>()
                };
            });

            services.TryAddSingleton(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return new ManagedSessionFactory(telemetry);
            });

            services.TryAddSingleton<IManagedSessionFactory, DefaultManagedSessionFactory>();
            services.TryAddSingleton<IManagedSessionConnector, DefaultManagedSessionConnector>();

            services.TryAddSingleton<Func<CancellationToken, Task<ManagedSession>>>(
                sp => new ManagedSessionAccessor(sp).ConnectAsync);
            services.TryAddSingleton<IClientFailoverCoordinator, ClientFailoverCoordinator>();

            services.TryAddSingleton<IReverseConnectConfigurationProvider,
                DefaultReverseConnectConfigurationProvider>();

            services.TryAddSingleton(sp =>
            {
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
                IReverseConnectConfigurationProvider? provider =
                    sp.GetService<IReverseConnectConfigurationProvider>();
                ITransportBindingRegistry? transportBindings =
                    sp.GetService<ITransportBindingRegistry>();
                return ReverseConnectManagerActivator.Create(
                    options, telemetry, provider, transportBindings);
            });

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService,
                ReverseConnectManagerHostedService>());

            services.AddOpcUa();
        }

        internal static async Task<ManagedSession> ConnectManagedSessionAsync(
            this IServiceProvider sp,
            ManagedSessionOptions sessionOptions,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct)
        {
            OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();
            if (options.ConfigurationProvider != null)
            {
                // Completes validation and application-instance certificate
                // setup; a lazily loaded supplied configuration document
                // (ConfigurationFile / ConfigurationStream) also becomes
                // available here, so publish it on the resolved options.
                ApplicationConfiguration providedConfiguration = await options
                    .ConfigurationProvider.GetAsync(ct).ConfigureAwait(false);
                options.Configuration ??= providedConfiguration;
            }
            ValidateClientOptions(options, sessionOptions);
            ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
            var builder = new ManagedSessionBuilder(options.Configuration!, telemetry);
            ApplyManagedSessionOptions(sp, builder, sessionOptions);
            configure(builder);

            ManagedSession session = await builder.ConnectAsync(ct).ConfigureAwait(false);
            if (sessionOptions.LoadComplexTypes)
            {
                IComplexTypeSystemFactory complexTypeSystemFactory =
                    sp.GetService<IComplexTypeSystemFactory>() ??
                    new DefaultComplexTypeSystemFactory(telemetry);
                ComplexTypeSystem complexTypeSystem = complexTypeSystemFactory.Create(session);
                await complexTypeSystem.LoadAsync(ct: ct).ConfigureAwait(false);
            }

            return session;
        }

        private static void RegisterClientOptions(
            IServiceCollection services,
            OpcUaClientOptions options)
        {
            if (options.Configuration == null && !options.HasSuppliedConfigurationDocument)
            {
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<
                        IOpcUaApplicationConfigurationFeature,
                        OpcUaClientApplicationConfigurationFeature>());
            }

            if (options.HasSuppliedConfigurationDocument)
            {
                // Capture the document source eagerly: the provider factory
                // must not resolve OpcUaClientOptions, whose own factory
                // resolves this provider.
                string? configurationFile = options.ConfigurationFile;
                Stream? configurationStream = options.ConfigurationStream;
                Action<ApplicationConfiguration>? configureLoadedConfiguration =
                    options.ConfigureLoadedConfiguration;
                services.TryAddSingleton(sp => new ClientSuppliedConfigurationProvider(
                    configurationFile,
                    configurationStream,
                    configureLoadedConfiguration,
                    sp.GetRequiredService<IApplicationInstanceFactory>(),
                    sp.GetRequiredService<ITelemetryContext>(),
                    sp.GetService<ICertificateManager>(),
                    sp.GetService<ICertificatePasswordProvider>()));
            }

            services.TryAddSingleton<OpcUaClientOptions>(sp =>
            {
                var resolvedOptions = new OpcUaClientOptions();
                CopyClientOptions(options, resolvedOptions);
                if (resolvedOptions.Configuration == null)
                {
                    if (sp.GetService<ClientSuppliedConfigurationProvider>() is
                        ClientSuppliedConfigurationProvider suppliedProvider)
                    {
                        // An explicitly supplied configuration document is the
                        // most specific intent and therefore wins over a shared
                        // application registered via ConfigureApplication(...).
                        // It loads lazily: Configuration is filled in after the
                        // first GetAsync completes.
                        resolvedOptions.ConfigurationProvider = suppliedProvider;
                    }
                    else
                    {
                        resolvedOptions.ConfigurationProvider =
                            sp.GetService<IOpcUaApplicationConfigurationProvider>();
                        resolvedOptions.Configuration =
                            resolvedOptions.ConfigurationProvider?.Configuration;
                    }
                }
                return resolvedOptions;
            });
            RegisterOptionsValidation(services, options);
        }

        private static void RegisterOptionsValidation(
            IServiceCollection services,
            OpcUaClientOptions options)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<OpcUaClientOptions>, OpcUaClientOptionsValidator>());
            services.AddOptions<OpcUaClientOptions>()
                .Configure(configuredOptions => CopyClientOptions(options, configuredOptions))
                .ValidateOnStart();
        }

        /// <summary>
        /// Copies every publicly settable field of <paramref name="source"/>
        /// (application identity/security options, <see cref="OpcUaClientOptions.Configuration"/>,
        /// the supplied configuration document
        /// (<see cref="OpcUaClientOptions.ConfigurationFile"/> /
        /// <see cref="OpcUaClientOptions.ConfigurationStream"/> /
        /// <see cref="OpcUaClientOptions.ConfigureLoadedConfiguration"/>),
        /// <see cref="OpcUaClientOptions.Session"/>, <see cref="OpcUaClientOptions.Identity"/>
        /// and <see cref="OpcUaClientOptions.ReverseConnect"/>) into
        /// <paramref name="target"/>.
        /// </summary>
        private static void CopyClientOptions(OpcUaClientOptions source, OpcUaClientOptions target)
        {
            target.Configuration = source.Configuration;
            target.ConfigurationFile = source.ConfigurationFile;
            target.ConfigurationStream = source.ConfigurationStream;
            target.ConfigureLoadedConfiguration = source.ConfigureLoadedConfiguration;
            target.ApplicationName = source.ApplicationName;
            target.ApplicationUri = source.ApplicationUri;
            target.ProductUri = source.ProductUri;
            target.SubjectName = source.SubjectName;
            target.PkiRoot = source.PkiRoot;
            target.AutoAcceptUntrustedCertificates = source.AutoAcceptUntrustedCertificates;
            target.RejectSHA1SignedCertificates = source.RejectSHA1SignedCertificates;
            target.MinimumCertificateKeySize = source.MinimumCertificateKeySize;
            target.Session = source.Session;
            target.Identity = source.Identity;
            target.ReverseConnect = source.ReverseConnect;
        }

        /// <summary>
        /// Ensures the root <c>ConfigureApplication(...)</c> infrastructure
        /// (shared <see cref="OpcUaApplicationOptions"/>,
        /// <see cref="IApplicationInstanceFactory"/>, and
        /// <see cref="IOpcUaApplicationConfigurationProvider"/>) is
        /// registered when client application identity/security options are
        /// set directly on <see cref="OpcUaClientOptions"/> (mirroring
        /// <c>OpcUaServerOptions</c>). The call is idempotent: whether the
        /// root builder calls <c>ConfigureApplication(...)</c> before or
        /// after <c>AddClient(...)</c>, both contribute to the same shared
        /// <see cref="OpcUaApplicationOptions"/> instance, with explicitly
        /// set fields winning over the client's <c>??=</c> defaults (see
        /// <see cref="OpcUaClientApplicationConfigurationFeature.ApplyDefaults"/>).
        /// </summary>
        /// <exception cref="InvalidOperationException">The client
        /// application options are combined with an explicit
        /// <see cref="OpcUaClientOptions.Configuration"/>.</exception>
        private static void ApplyClientApplicationOptions(
            IOpcUaBuilder builder,
            OpcUaClientOptions options)
        {
            ValidateSuppliedConfigurationDocument(options);
            if (options.HasSuppliedConfigurationDocument)
            {
                // The supplied document is loaded through an
                // IApplicationInstance created by the shared factory; ensure
                // the factory is registered even when no ConfigureApplication
                // call is made.
                builder.AddApplicationInstance();
                return;
            }

            if (!options.HasApplicationOptions)
            {
                return;
            }
            if (options.Configuration != null)
            {
                throw new InvalidOperationException(
                    "OpcUaClientOptions.Configuration cannot be combined with client " +
                    "application identity options (ApplicationName, ApplicationUri, " +
                    "ProductUri, SubjectName, PkiRoot, AutoAcceptUntrustedCertificates, " +
                    "RejectSHA1SignedCertificates, MinimumCertificateKeySize). Set " +
                    "Configuration directly, or use the application identity options, " +
                    "not both.");
            }

            // Idempotent: reuses/mutates the shared OpcUaApplicationOptions
            // instance registered by a root ConfigureApplication(...) call
            // made before or after this AddClient(...) call.
            builder.ConfigureApplication(_ => { });
        }

        /// <summary>
        /// Rejects ambiguous combinations of an existing configuration XML
        /// document (<see cref="OpcUaClientOptions.ConfigurationFile"/> /
        /// <see cref="OpcUaClientOptions.ConfigurationStream"/>) with other
        /// configuration sources. Unlike the argument validation on the
        /// <c>AddClient(string, ...)</c> overload, this also covers values
        /// that arrive through configuration binding or the options
        /// callback.
        /// </summary>
        /// <exception cref="InvalidOperationException">An invalid
        /// combination was configured.</exception>
        private static void ValidateSuppliedConfigurationDocument(OpcUaClientOptions options)
        {
            bool hasConfigurationFile = !string.IsNullOrEmpty(options.ConfigurationFile);
            if (hasConfigurationFile && string.IsNullOrWhiteSpace(options.ConfigurationFile))
            {
                throw new InvalidOperationException(
                    "OpcUaClientOptions.ConfigurationFile must not be a white-space path.");
            }
            if (hasConfigurationFile && options.ConfigurationStream != null)
            {
                throw new InvalidOperationException(
                    "Set only one of OpcUaClientOptions.ConfigurationFile and " +
                    "OpcUaClientOptions.ConfigurationStream.");
            }
            if (!options.HasSuppliedConfigurationDocument)
            {
                return;
            }
            if (options.Configuration != null)
            {
                throw new InvalidOperationException(
                    "OpcUaClientOptions.ConfigurationFile / ConfigurationStream cannot " +
                    "be combined with an explicit OpcUaClientOptions.Configuration.");
            }
            if (options.HasApplicationOptions)
            {
                throw new InvalidOperationException(
                    "OpcUaClientOptions.ConfigurationFile / ConfigurationStream cannot " +
                    "be combined with client application identity options " +
                    "(ApplicationName, ApplicationUri, ProductUri, SubjectName, " +
                    "PkiRoot, AutoAcceptUntrustedCertificates, " +
                    "RejectSHA1SignedCertificates, MinimumCertificateKeySize). The " +
                    "supplied document is authoritative; use " +
                    "ConfigureLoadedConfiguration for programmatic overrides.");
            }
        }

        private static void ValidateClientOptions(
            OpcUaClientOptions options,
            ManagedSessionOptions sessionOptions)
        {
            ValidateOptionsResult result = OpcUaClientOptionsValidator.Validate(options);
            if (!result.Failed && sessionOptions.Endpoint == null)
            {
                result = ValidateOptionsResult.Fail("A session endpoint is required.");
            }
            if (result.Failed)
            {
                throw new OptionsValidationException(
                    string.Empty,
                    typeof(OpcUaClientOptions),
                    result.Failures);
            }
        }

        private static void ApplyManagedSessionOptions(
            IServiceProvider sp,
            ManagedSessionBuilder builder,
            ManagedSessionOptions sessionOptions)
        {
            if (sessionOptions.Endpoint != null)
            {
                builder.UseEndpoint(sessionOptions.Endpoint);
            }
            builder.WithSessionName(sessionOptions.SessionName)
                   .WithSessionTimeout(sessionOptions.SessionTimeout)
                   .WithCheckDomain(sessionOptions.CheckDomain)
                   .WithReconnectPolicy(_ => sessionOptions.ReconnectPolicy);

            IClientIdentityProvider? identityProvider =
                sessionOptions.IdentityProvider ?? ResolveIdentityProvider(sp);
            if (identityProvider != null)
            {
                builder.WithIdentityProvider(identityProvider);
            }
#pragma warning disable CS0618 // Legacy eager identity remains supported when no provider is configured.
            else if (sessionOptions.Identity != null)
            {
                builder.WithUserIdentity(sessionOptions.Identity);
            }
#pragma warning restore CS0618

            TimeProvider? timeProvider =
                sessionOptions.TimeProvider ?? sp.GetService<TimeProvider>();
            if (timeProvider != null)
            {
                builder.WithTimeProvider(timeProvider);
            }

            if (sessionOptions.PreferredLocales is { Count: > 0 } locales)
            {
                string[] arr = new string[locales.Count];
                for (int i = 0; i < locales.Count; i++)
                {
                    arr[i] = locales[i];
                }
                builder.WithPreferredLocales(arr);
            }
            if (sessionOptions.SubscriptionEngineFactory != null)
            {
                builder.UseSubscriptionEngine(sessionOptions.SubscriptionEngineFactory);
            }
            if (sessionOptions.EnableServerRedundancy)
            {
                builder.WithServerRedundancy();
            }
            if (!sessionOptions.NetworkRedundancy.AlternateEndpoints.IsEmpty)
            {
                builder.WithNetworkRedundancy(sessionOptions.NetworkRedundancy.AlternateEndpoints);
            }
            if (sessionOptions.EnableTokenReuseFailover)
            {
                builder.WithTokenReuseFailover();
            }
            if (sessionOptions.TransferSubscriptionsOnRecreate)
            {
                builder.WithTransferSubscriptionsOnRecreate();
            }
            if (sessionOptions.PoolNotifications)
            {
                builder.WithPoolNotifications();
            }
            if (sessionOptions.ModelChangeTracking)
            {
                builder.WithModelChangeTracking();
            }

            IClientChannelManager? mgr = sp.GetService<IClientChannelManager>();
            if (mgr != null)
            {
                builder.WithChannelManager(mgr);
            }

            // The application's policy set, so a policy contributed through
            // AddSecurityPolicy is resolvable by the session as well as by the
            // channel it opens.
            ISecurityPolicyRegistry? securityPolicies =
                sp.GetService<ISecurityPolicyRegistry>();
            if (securityPolicies != null)
            {
                builder.UseSecurityPolicies(securityPolicies);
            }

            IClientConnectGate? connectGate =
                sessionOptions.ConnectGate ?? sp.GetService<IClientConnectGate>();
            if (connectGate != null)
            {
                builder.WithConnectRateLimiter(connectGate);
            }
        }

        private static IClientIdentityProvider? ResolveIdentityProvider(IServiceProvider sp)
        {
            IEnumerable<IClientIdentityProvider> registered =
                sp.GetServices<IClientIdentityProvider>();
            var providers = new List<IClientIdentityProvider>();
            providers.AddRange(registered);

            OpcUaClientOptions clientOptions =
                sp.GetRequiredService<OpcUaClientOptions>();
            OpcUaClientIdentityOptions identityOptions =
                sp.GetService<OpcUaClientIdentityOptions>() ?? clientOptions.Identity;
            if (providers.Count == 0)
            {
                return HasConfiguredIdentity(identityOptions)
                    ? BuildConfiguredIdentityProvider(sp, identityOptions)
                    : null;
            }

            ApplyConfiguredOrder(providers, identityOptions.Order);
            if (providers.Count == 1)
            {
                return providers[0];
            }
            return new CompositeClientIdentityProvider(providers);
        }

        private static void EnsureClientConnectGate(OpcUaClientOptions options)
        {
            if (options.Session.ConnectGate != null ||
                options.Session.ConnectRateLimiterMaxConcurrency == null)
            {
                return;
            }

            options.Session = options.Session with
            {
                ConnectGate = new RateLimiterClientConnectGate(
                    options.Session.ConnectRateLimiterMaxConcurrency.Value)
            };
        }

        /// <summary>
        /// Configures a <see cref="ReverseConnectManager"/> on first
        /// resolution when client reverse-connect options are set. The
        /// factory only <em>configures</em> the initial startup; it never
        /// blocks on a start. Listener startup runs asynchronously either
        /// eagerly via the registered hosted service or lazily on first use
        /// (<see cref="ReverseConnectManager.EnsureStartedAsync"/>). The
        /// options are mirrored into
        /// <see cref="ClientConfiguration.ReverseConnect"/> so any other
        /// consumer reading the application configuration sees the same data.
        /// A missing <see cref="OpcUaClientOptions.Configuration"/> is
        /// surfaced during the async start rather than at resolution.
        /// </summary>
        private static class ReverseConnectManagerActivator
        {
            public static ReverseConnectManager Create(
                OpcUaClientOptions options,
                ITelemetryContext telemetry,
                IReverseConnectConfigurationProvider? provider,
                ITransportBindingRegistry? transportBindings)
            {
                var manager = new ReverseConnectManager(telemetry)
                {
                    ConfigurationProvider = provider,
                    // Wire the DI transport registry so transports registered
                    // via AddOpcTcpTransport()/AddHttpsTransport() etc. are
                    // visible to the reverse-connect listener. Null falls back
                    // to the manager's process-local default registry.
                    TransportBindings = transportBindings
                };

                ClientReverseConnectOptions? rcOptions = options.ReverseConnect;
                if (rcOptions == null)
                {
                    return manager;
                }

                // Capture the reverse-connect option values as immutable
                // snapshots (endpoint URL strings and the hold/wait timeouts) so
                // ApplyReverseConnectOverlay can rebuild a fresh, independent
                // ReverseConnectClientConfiguration on every invocation. The
                // overlay must never share a single mutable configuration
                // instance across invocations: it is applied to the initial
                // configuration, re-applied on every file-backed restart and
                // watcher reload, and passed through an injected provider that
                // may mutate the applied configuration in place. A shared
                // instance would let such a provider mutation (or an
                // accumulation/removal of endpoints) leak into a later
                // reload/restart. The captured strings/ints are value snapshots
                // that no later provider run can change.
                string?[] optionEndpointUrls = new string?[
                    rcOptions.ClientEndpointUrls.Count];
                for (int i = 0; i < optionEndpointUrls.Length; i++)
                {
                    optionEndpointUrls[i] = rcOptions.ClientEndpointUrls[i];
                }
                int optionHoldTimeMs = rcOptions.HoldTimeMs;
                int optionWaitTimeoutMs = rcOptions.WaitTimeoutMs;

                // The option endpoints are configured-candidate endpoints
                // carried in the application configuration, not persistent
                // manual entries, so an injected provider can replace or remove
                // them. Startup is configured even when the option list is
                // empty so a provider can supply the endpoints instead.
                //
                // A configuration originating from an
                // IOpcUaApplicationConfigurationProvider must be obtained via
                // the async GetAsync path: only that path runs validation and
                // application-instance certificate creation. The
                // OpcUaClientOptions.Configuration snapshot exposed for a
                // provider-origin configuration is not validated, so the
                // provider check wins over the direct snapshot. An explicit
                // user-supplied Configuration (no provider) is used directly.
                IOpcUaApplicationConfigurationProvider? configurationProvider =
                    options.ConfigurationProvider;
                ApplicationConfiguration? configuration = options.Configuration;
                ApplicationConfiguration ApplyReverseConnectOverlay(
                    ApplicationConfiguration cfg)
                {
                    // Build a fresh, independent ReverseConnectClientConfiguration
                    // (new endpoint objects, new ArrayOf, hold/wait timeouts)
                    // from the immutable captured option snapshots on every call.
                    // Never reuse a shared instance: a provider that mutates the
                    // applied configuration in place must not contaminate a later
                    // reload/restart overlay.
                    var clientEndpoints = new ReverseConnectClientEndpoint[
                        optionEndpointUrls.Length];
                    for (int i = 0; i < clientEndpoints.Length; i++)
                    {
                        clientEndpoints[i] = new ReverseConnectClientEndpoint
                        {
                            EndpointUrl = optionEndpointUrls[i]
                        };
                    }
                    cfg.ClientConfiguration ??= new ClientConfiguration();
                    cfg.ClientConfiguration.ReverseConnect =
                        new ReverseConnectClientConfiguration
                        {
                            ClientEndpoints =
                                new ArrayOf<ReverseConnectClientEndpoint>(clientEndpoints),
                            HoldTime = optionHoldTimeMs,
                            WaitTimeout = optionWaitTimeoutMs
                        };
                    return cfg;
                }
                if (configurationProvider != null)
                {
                    manager.ConfigureInitialStartup(async ct =>
                    {
                        ApplicationConfiguration provided = await configurationProvider
                            .GetAsync(ct).ConfigureAwait(false);
                        return ApplyReverseConnectOverlay(provided);
                    }, ApplyReverseConnectOverlay);
                }
                else if (configuration != null)
                {
                    // Reapply the reverse-connect option overlay both to the
                    // initial configuration AND on every file-backed restart, so
                    // a stop/restart that re-reads SourceFilePath keeps the DI
                    // in-memory reverse-connect endpoints instead of losing them
                    // to a plain file load.
                    manager.ConfigureInitialStartup(
                        ApplyReverseConnectOverlay(configuration),
                        ApplyReverseConnectOverlay);
                }
                else
                {
                    // Surface the missing configuration during async start,
                    // not at resolution.
                    manager.MarkInitialConfigurationMissing();
                }
                return manager;
            }
        }

        private sealed class OpcUaBuilder : IOpcUaBuilder
        {
            public OpcUaBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        private sealed class OpcUaClientBuilder : IOpcUaClientBuilder
        {
            public OpcUaClientBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        /// <summary>
        /// Lazily creates and caches the connected <see cref="ManagedSession"/>.
        /// Multiple awaiters of the factory delegate share the single
        /// connection task.
        /// </summary>
        private sealed class ManagedSessionAccessor
        {
            public ManagedSessionAccessor(IServiceProvider sp)
            {
                m_sp = sp;
            }

            public Task<ManagedSession> ConnectAsync(CancellationToken ct)
            {
                lock (m_gate)
                {
                    m_connectTask ??= ConnectCoreAsync(ct);
                    return m_connectTask;
                }
            }

            private Task<ManagedSession> ConnectCoreAsync(CancellationToken ct)
            {
                OpcUaClientOptions options =
                    m_sp.GetRequiredService<OpcUaClientOptions>();
                return m_sp.ConnectManagedSessionAsync(options.Session, _ => { }, ct);
            }

            private readonly IServiceProvider m_sp;
            private Task<ManagedSession>? m_connectTask;
            private readonly Lock m_gate = new();
        }

        private sealed class OpcUaClientOptionsValidator : IValidateOptions<OpcUaClientOptions>
        {
            public OpcUaClientOptionsValidator(
                IEnumerable<OpcUaApplicationOptions> applicationOptions,
                IServiceProviderIsService serviceProviderIsService)
            {
                m_hasConfigurationProvider = serviceProviderIsService.IsService(
                    typeof(IOpcUaApplicationConfigurationProvider));
                foreach (OpcUaApplicationOptions _ in applicationOptions)
                {
                    m_hasApplicationOptions = true;
                    break;
                }
            }

            public ValidateOptionsResult Validate(string? name, OpcUaClientOptions options)
            {
                return Validate(
                    options,
                    m_hasApplicationOptions || m_hasConfigurationProvider);
            }

            public static ValidateOptionsResult Validate(
                OpcUaClientOptions options,
                bool hasConfigurationProvider = false)
            {
                var failures = new List<string>();
                if (options.Configuration == null &&
                    !hasConfigurationProvider &&
                    !options.HasSuppliedConfigurationDocument)
                {
                    failures.Add("OpcUaClientOptions.Configuration is required.");
                }

                return failures.Count == 0
                    ? ValidateOptionsResult.Success
                    : ValidateOptionsResult.Fail(failures);
            }

            private readonly bool m_hasApplicationOptions;
            private readonly bool m_hasConfigurationProvider;
        }
    }
}
