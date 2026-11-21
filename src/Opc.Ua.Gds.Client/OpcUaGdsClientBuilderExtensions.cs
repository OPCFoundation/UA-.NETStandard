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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using DeviceRegistrarTypeClient = Opc.Ua.Onboarding.DeviceRegistrarTypeClient;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions provided by
    /// <c>Opc.Ua.Gds.Client</c>: register the GDS / push-config
    /// clients for dependency-injected applications.
    /// </summary>
    public static class OpcUaGdsClientBuilderExtensions
    {
        /// <summary>
        /// Default <see cref="IConfiguration"/> section name used by the
        /// <see cref="AddGdsClient(IOpcUaBuilder, IConfiguration)"/>
        /// overload.
        /// </summary>
        public const string DefaultConfigurationSection = "OpcUa:Gds:Client";

        /// <summary>
        /// Registers the OPC UA GDS client services. The
        /// <see cref="ApplicationConfiguration"/> must be pre-registered.
        /// An optional <see cref="ISessionFactory"/> may be registered to
        /// override the default factory.
        /// </summary>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configure">Optional callback used to populate
        /// <see cref="GdsClientOptions"/>.</param>
        /// <returns>An <see cref="IGdsClientBuilder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaBuilder builder,
            Action<GdsClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.AddOptions();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            RegisterCoreServices(builder.Services);

            return new GdsClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers the OPC UA GDS client services with options bound
        /// from the supplied <paramref name="configuration"/> section
        /// <see cref="DefaultConfigurationSection"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="configuration">Configuration root containing the
        /// <c>OpcUa:Gds:Client</c> section.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="configuration"/> is <c>null</c>.</exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaBuilder builder,
            IConfiguration configuration)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            return builder.AddGdsClient(configuration.GetSection(DefaultConfigurationSection));
        }

        /// <summary>
        /// Registers the OPC UA GDS client services with options bound
        /// from the supplied <paramref name="section"/>.
        /// </summary>
        /// <remarks>
        /// AOT-safe: bound by the .NET 8+ configuration binding source
        /// generator (<c>EnableConfigurationBindingGenerator</c>).
        /// </remarks>
        /// <param name="builder">The OPC UA builder.</param>
        /// <param name="section">Configuration section to bind.</param>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>
        /// or <paramref name="section"/> is <c>null</c>.</exception>
        public static IGdsClientBuilder AddGdsClient(
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

            builder.Services.AddOptions<GdsClientOptions>().Bind(section);

            RegisterCoreServices(builder.Services);

            return new GdsClientBuilder(builder.Services);
        }

        /// <summary>
        /// Registers GDS client services on an existing OPC UA client builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaClientBuilder builder,
            Action<GdsClientOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return new BuilderAdapter(builder.Services).AddGdsClient(configure);
        }

        /// <summary>
        /// Registers GDS client services on an existing OPC UA client builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaClientBuilder builder,
            IConfiguration configuration)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            return new BuilderAdapter(builder.Services).AddGdsClient(configuration);
        }

        /// <summary>
        /// Registers GDS client services on an existing OPC UA client builder.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddGdsClient(
            this IOpcUaClientBuilder builder,
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

            return new BuilderAdapter(builder.Services).AddGdsClient(section);
        }

        /// <summary>
        /// Registers key-credential service client factories.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddKeyCredentialServiceClient(this IGdsClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<KeyCredentialServiceClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = GetManagedSessionAccessor(sp);
                return async (nodeId, ct) => new KeyCredentialServiceClient(
                    await accessor(ct).ConfigureAwait(false), nodeId);
            });
            return builder;
        }

        /// <summary>
        /// Registers authorization service client factories.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddAuthorizationServiceClient(this IGdsClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<AuthorizationServiceClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = GetManagedSessionAccessor(sp);
                return async (nodeId, ct) => new AuthorizationServiceClient(
                    await accessor(ct).ConfigureAwait(false), nodeId);
            });
            return builder;
        }

        /// <summary>
        /// Registers local discovery server clients.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddLocalDiscoveryServerClient(this IGdsClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp.GetRequiredService<ApplicationConfiguration>();
                ISessionFactory? sessionFactory = sp.GetService<ISessionFactory>();
                return sessionFactory == null
                    ? new LocalDiscoveryServerClient(configuration)
                    : new LocalDiscoveryServerClient(configuration, sessionFactory);
            });
            builder.Services.TryAddSingleton<ILocalDiscoveryServerClient>(sp =>
                sp.GetRequiredService<LocalDiscoveryServerClient>());
            return builder;
        }

        /// <summary>
        /// Registers onboarding client factories.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddOnboardingClient(this IGdsClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<Func<NodeId, CancellationToken, ValueTask<OnboardingClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = GetManagedSessionAccessor(sp);
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return async (nodeId, ct) => new OnboardingClient(
                    await accessor(ct).ConfigureAwait(false), nodeId, telemetry);
            });
            return builder;
        }

        /// <summary>
        /// Registers typed device-facing registrar clients that borrow the container-owned managed session.
        /// </summary>
        public static IGdsClientBuilder AddDeviceRegistrarClient(this IGdsClientBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.Services.TryAddSingleton<
                Func<NodeId, CancellationToken, ValueTask<DeviceRegistrarTypeClient>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = GetManagedSessionAccessor(sp);
                ITelemetryContext telemetry = sp.GetRequiredService<ITelemetryContext>();
                return async (nodeId, ct) => new DeviceRegistrarTypeClient(
                    await accessor(ct).ConfigureAwait(false), nodeId, telemetry);
            });
            return builder;
        }

        /// <summary>
        /// Registers the bounded pull-DCA workflow. Hosts register <see cref="IPullDcaIdentityProvider"/> and
        /// <see cref="IPullDcaCredentialProvider"/> for real certificate selection and installation.
        /// A separate <see cref="IPullDcaNewKeyPairCredentialProvider"/> or the credential provider handles
        /// requested private-key output. Discovery, software-update and signing-journal providers are optional.
        /// The existing managed-session accessor owns every session.
        /// A multi-endpoint host may register a registrar-aware session accessor before resolving the workflow.
        /// </summary>
        public static IGdsClientBuilder AddPullDcaWorkflow(
            this IGdsClientBuilder builder,
            Action<PullDcaOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddOptions<PullDcaOptions>();
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            builder.AddDeviceRegistrarClient();
            builder.Services.TryAddSingleton<
                Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(sp =>
            {
                Func<CancellationToken, Task<ManagedSession>> accessor = GetManagedSessionAccessor(sp);
                return (_, ct) => new ValueTask<ManagedSession>(accessor(ct));
            });
            builder.Services.TryAddSingleton(sp => new PullDcaWorkflow(
                sp.GetRequiredService<IOptions<PullDcaOptions>>().Value,
                sp.GetRequiredService<Func<PullDcaRegistrar, CancellationToken, ValueTask<ManagedSession>>>(),
                sp.GetRequiredService<IPullDcaIdentityProvider>(),
                sp.GetRequiredService<IPullDcaCredentialProvider>(),
                sp.GetRequiredService<ITelemetryContext>(),
                sp.GetService<IPullDcaSoftwareUpdateProvider>(),
                sp.GetService<IPullDcaRegistrarDiscovery>(),
                sp.GetService<IPullDcaCertificateReceiptStore>(),
                sp.GetService<IPullDcaSigningRecoveryProvider>(),
                sp.GetService<IPullDcaSigningJournal>(),
                sp.GetService<IPullDcaNewKeyPairCredentialProvider>()));
            return builder;
        }

        /// <summary>
        /// Selects received-certificate persistence using a host-registered secret store.
        /// One adapter instance exclusively owns scope. The namespace table must match the registrar's session;
        /// hosts restore original keys and channel identity before resuming. Store durability is host-selected.
        /// This cannot recover a server-accepted response lost before local receipt publication.
        /// </summary>
        public static IGdsClientBuilder AddPullDcaCertificateReceipts(
            this IGdsClientBuilder builder,
            NamespaceTable namespaceUris,
            string scope)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive device receipt scope is required.", nameof(scope));
            }
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(IPullDcaCertificateReceiptStore) ||
                    descriptor.ServiceType == typeof(SecretStorePullDcaCertificateReceiptStore)))
                {
                    throw new InvalidOperationException("Select only one pull certificate receipt store.");
                }
            }
            builder.Services.AddSingleton(provider => new SecretStorePullDcaCertificateReceiptStore(
                provider.GetRequiredService<ISecretStore>(), namespaceUris, scope,
                provider.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<IPullDcaCertificateReceiptStore>(provider =>
                provider.GetRequiredService<SecretStorePullDcaCertificateReceiptStore>());
            return builder;
        }

        /// <summary>
        /// Registers the GDS certificate-management convenience surface.
        /// </summary>
        public static IGdsClientBuilder AddCertificateManagement(
            this IGdsClientBuilder builder,
            string authorityUri = "opc.gds")
        {
            return builder.AddCertificateManagement(ObjectIds.AuthorizationServices, authorityUri);
        }

        /// <summary>
        /// Registers the GDS certificate-management convenience surface.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static IGdsClientBuilder AddCertificateManagement(
            this IGdsClientBuilder builder,
            NodeId authorizationServiceNodeId,
            string authorityUri = "opc.gds")
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddKeyCredentialServiceClient()
                .AddAuthorizationServiceClient()
                .AddOnboardingClient();

            builder.Services.TryAddSingleton<IAccessTokenProvider>(sp =>
            {
                Func<NodeId, CancellationToken, ValueTask<AuthorizationServiceClient>> factory =
                    sp.GetRequiredService<Func<NodeId, CancellationToken, ValueTask<AuthorizationServiceClient>>>();
                return new GdsAccessTokenProvider(
                    ct => factory(authorizationServiceNodeId, ct),
                    authorityUri);
            });
            return builder;
        }

        private static Func<CancellationToken, Task<ManagedSession>> GetManagedSessionAccessor(IServiceProvider sp)
        {
            return sp.GetService<Func<CancellationToken, Task<ManagedSession>>>()
                ?? throw new InvalidOperationException(
                    "AddGdsClient() requires AddClient() before resolving per-session GDS clients.");
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp
                    .GetRequiredService<ApplicationConfiguration>();
                ISessionFactory? sessionFactory = sp.GetService<ISessionFactory>();
                GdsClientOptions options = sp
                    .GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return new GlobalDiscoveryServerClient(
                    configuration,
                    options,
                    adminUserIdentity: null,
                    sessionFactory: sessionFactory);
            });
            services.TryAddSingleton<IGlobalDiscoveryServerClient>(sp =>
                sp.GetRequiredService<GlobalDiscoveryServerClient>());

            services.TryAddSingleton(sp =>
            {
                ApplicationConfiguration configuration = sp
                    .GetRequiredService<ApplicationConfiguration>();
                ISessionFactory? sessionFactory = sp.GetService<ISessionFactory>();
                GdsClientOptions options = sp
                    .GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return new ServerPushConfigurationClient(
                    configuration,
                    options,
                    sessionFactory: sessionFactory);
            });
            services.TryAddSingleton<IServerPushConfigurationClient>(sp =>
                sp.GetRequiredService<ServerPushConfigurationClient>());
            services.TryAddSingleton<Func<ISession, CancellationToken, ValueTask<ServerPushConfigurationClient>>>(sp =>
            {
                ApplicationConfiguration configuration = sp.GetRequiredService<ApplicationConfiguration>();
                GdsClientOptions options = sp.GetRequiredService<IOptions<GdsClientOptions>>().Value;
                return (session, ct) => ServerPushConfigurationClient.CaptureAsync(configuration, session, options, ct);
            });

            services.AddOpcUa();
        }

        private sealed class BuilderAdapter : IOpcUaBuilder
        {
            public BuilderAdapter(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }

        private sealed class GdsClientBuilder : IGdsClientBuilder
        {
            public GdsClientBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
