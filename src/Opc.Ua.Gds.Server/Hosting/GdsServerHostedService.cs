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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that hosts an OPC UA Global
    /// Discovery Server within a .NET Generic Host. Owns the
    /// <see cref="IApplicationInstance"/> lifetime, builds the
    /// <see cref="ApplicationConfiguration"/> (including the
    /// <see cref="GlobalDiscoveryServerConfiguration"/> extension) from
    /// <see cref="GdsServerOptions"/>, resolves the pluggable GDS
    /// services from DI, then starts a <see cref="GlobalDiscoverySampleServer"/>
    /// subclass that wires the optional services into the
    /// <see cref="ApplicationsNodeManager"/>.
    /// </summary>
    internal sealed class GdsServerHostedService : BackgroundService
    {
        private readonly GdsServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly IApplicationInstanceFactory m_applicationFactory;
        private readonly IApplicationsDatabase m_database;
        private readonly ICertificateRequest m_certificateRequest;
        private readonly ICertificateGroup m_certificateGroup;
        private readonly IUserDatabase m_userDatabase;
        private readonly IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> m_identityRegistrations;
        private readonly IEnumerable<OpcUaServerIdentityAugmenterRegistration> m_augmenterRegistrations;
        private readonly IServiceProvider m_services;
        private readonly IAccessTokenProvider? m_accessTokenProvider;
        private readonly AuthorizationServiceManager? m_authorizationServiceManager;
        private readonly IKeyCredentialRequestStore? m_keyCredentialStore;
        private readonly IConfigurationDataStore? m_configurationStore;
        private readonly bool m_enableBuiltInApplicationSelfAdminProvider;
        private readonly ILogger<GdsServerHostedService> m_logger;

        // CA2213: IApplicationInstance is IAsyncDisposable; the lifecycle here
        // is managed via the async StopAsync override which calls
        // m_application.DisposeAsync.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private GdsHostedServer? m_server;

        public GdsServerHostedService(
            IOptions<GdsServerOptions> options,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> identityRegistrations,
            IEnumerable<OpcUaServerIdentityAugmenterRegistration> augmenterRegistrations,
            IServiceProvider services,
            IOptions<GdsDefaultIdentityAuthenticatorOptions> defaultAuthenticatorOptions,
            ILogger<GdsServerHostedService> logger,
            IAccessTokenProvider? accessTokenProvider = null,
            AuthorizationServiceManager? authorizationServiceManager = null,
            IKeyCredentialRequestStore? keyCredentialStore = null,
            IConfigurationDataStore? configurationStore = null)
            : this(
                options,
                telemetry,
                applicationFactory,
                RequireStore<IApplicationsDatabase>(services, nameof(IApplicationsDatabase)),
                RequireStore<ICertificateRequest>(services, nameof(ICertificateRequest)),
                RequireStore<ICertificateGroup>(services, nameof(ICertificateGroup)),
                RequireStore<IUserDatabase>(services, nameof(IUserDatabase)),
                identityRegistrations,
                augmenterRegistrations,
                services,
                defaultAuthenticatorOptions,
                logger,
                accessTokenProvider,
                authorizationServiceManager,
                keyCredentialStore,
                configurationStore)
        {
        }

        public GdsServerHostedService(
            IOptions<GdsServerOptions> options,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            IApplicationsDatabase database,
            ICertificateRequest certificateRequest,
            ICertificateGroup certificateGroup,
            IUserDatabase userDatabase,
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> identityRegistrations,
            IEnumerable<OpcUaServerIdentityAugmenterRegistration> augmenterRegistrations,
            IServiceProvider services,
            IOptions<GdsDefaultIdentityAuthenticatorOptions> defaultAuthenticatorOptions,
            ILogger<GdsServerHostedService> logger,
            IAccessTokenProvider? accessTokenProvider = null,
            AuthorizationServiceManager? authorizationServiceManager = null,
            IKeyCredentialRequestStore? keyCredentialStore = null,
            IConfigurationDataStore? configurationStore = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_applicationFactory = applicationFactory
                ?? throw new ArgumentNullException(nameof(applicationFactory));
            m_database = database ?? throw new ArgumentNullException(nameof(database));
            m_certificateRequest = certificateRequest
                ?? throw new ArgumentNullException(nameof(certificateRequest));
            m_certificateGroup = certificateGroup
                ?? throw new ArgumentNullException(nameof(certificateGroup));
            m_userDatabase = userDatabase ?? throw new ArgumentNullException(nameof(userDatabase));
            m_identityRegistrations = identityRegistrations ??
                throw new ArgumentNullException(nameof(identityRegistrations));
            m_augmenterRegistrations = augmenterRegistrations ??
                throw new ArgumentNullException(nameof(augmenterRegistrations));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            if (defaultAuthenticatorOptions is null)
            {
                throw new ArgumentNullException(nameof(defaultAuthenticatorOptions));
            }
            m_enableBuiltInApplicationSelfAdminProvider =
                defaultAuthenticatorOptions.Value.EnableGdsApplicationSelfAdminProvider;
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_accessTokenProvider = accessTokenProvider;
            m_authorizationServiceManager = authorizationServiceManager;
            m_keyCredentialStore = keyCredentialStore;
            m_configurationStore = configurationStore;
        }

        private static T RequireStore<T>(IServiceProvider services, string serviceName)
            where T : class
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return services.GetService<T>() ??
                throw new InvalidOperationException(
                    $"AddGdsServer requires a {serviceName} registration. " +
                    "Call AddInMemoryStores() for the built-in in-memory stores or register a custom store with the " +
                    "matching AddApplicationsDatabase, AddCertificateRequest, AddCertificateGroup, or AddUserDatabase method.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string appName = ResolveApplicationName(m_options);

            string pkiRoot = string.IsNullOrEmpty(m_options.PkiRoot)
                ? DefaultPkiRoot.Get(appName, m_logger)
                : m_options.PkiRoot;

            string subject = string.IsNullOrEmpty(m_options.SubjectName)
                ? $"CN={appName}, O=OPC Foundation, DC=localhost"
                : m_options.SubjectName;

            m_application = m_applicationFactory.Create(m_telemetry);
            m_application.ApplicationName = appName;
            m_application.ApplicationType = ApplicationType.Server;

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subject, CertificateStoreType.Directory, pkiRoot);

            string[] urls = new string[m_options.EndpointUrls.Count];
            m_options.EndpointUrls.CopyTo(urls, 0);

            IApplicationConfigurationBuilderServerSelected serverBuilder = m_application
                .Build(m_options.ApplicationUri, m_options.ProductUri)
                .SetMaxByteStringLength((int)m_options.MaxByteStringLength)
                .SetMaxArrayLength((int)m_options.MaxArrayLength)
                .AsServer(urls);

            if (m_options.IncludeSignAndEncryptPolicies)
            {
                serverBuilder = serverBuilder.AddSignAndEncryptPolicies();
            }

            if (m_options.IncludeUnsecurePolicyNone)
            {
                serverBuilder = serverBuilder.AddUnsecurePolicyNone();
            }

            DefaultAuthenticatorOptions? defaultAuthenticatorOptions =
                GetDefaultAuthenticatorOptions(m_services);
            foreach (UserTokenType tokenType in GetUserTokenTypes(m_options, defaultAuthenticatorOptions))
            {
                serverBuilder = serverBuilder.AddUserTokenPolicy(tokenType);
            }

            IApplicationConfigurationBuilderServerOptions optionsBuilder =
                serverBuilder.SetDiagnosticsEnabled(m_options.DiagnosticsEnabled);

            if (m_options.ReverseConnect is ServerReverseConnectOptions reverseConnect)
            {
                optionsBuilder = optionsBuilder.SetReverseConnect(
                    ToReverseConnectConfiguration(reverseConnect));
            }

            m_options.ConfigureBuilder?.Invoke(serverBuilder);

            IApplicationConfigurationBuilderSecurityOptions securityBuilder = optionsBuilder
                .AddSecurityConfiguration(certs, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates);

            await securityBuilder
                .AddExtension(
                    new XmlQualifiedName(
                        nameof(GlobalDiscoveryServerConfiguration),
                        Namespaces.OpcUaGds + "Configuration.xsd"),
                    BuildGdsConfiguration(m_options, pkiRoot))
                .CreateAsync(stoppingToken)
                .ConfigureAwait(false);

            bool haveCert = await m_application
                .CheckApplicationInstanceCertificatesAsync(
                    silent: true, CertificateFactory.DefaultLifeTime, stoppingToken)
                .ConfigureAwait(false);
            if (!haveCert)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            m_authorizationServiceManager?.Initialize(m_application.ApplicationConfiguration!);

            if (m_options.AutoApprove)
            {
                m_logger.CertificateRequestAutoApprovalEnabled();
            }

            m_server = new GdsHostedServer(
                m_database,
                m_certificateRequest,
                m_certificateGroup,
                m_userDatabase,
                m_telemetry,
                m_accessTokenProvider,
                m_keyCredentialStore,
                m_configurationStore,
                m_options.AutoApprove,
                m_enableBuiltInApplicationSelfAdminProvider);

            if (m_services.GetService<ITransportBindingRegistry>() is { } transportBindings)
            {
                m_server.TransportBindings = transportBindings;
            }

            // Shared registrations (plain IServerStartupTask, IServerPreStartupTask
            // and IRoleManager) are meant for the regular server when one shares
            // the container; the GDS then only uses its own registrations.
            bool useSharedRegistrations = !HostsRegularServer(m_services);

            // Identity registrations are staged before start so they are in place
            // when the endpoints open; the server registers them after its
            // built-in authenticators, so they replace the built-ins.
            m_server.BuiltInAuthenticatorOptions = defaultAuthenticatorOptions;
            m_server.ConfiguredRoleManager = useSharedRegistrations
                ? m_services.GetService<IRoleManager>()
                : null;
            m_server.StageIdentityRegistrations(
                CreateIdentityAuthenticators(),
                CreateIdentityAugmenters());
            foreach (IServerPreStartupTask task in GetPreStartupTasks(useSharedRegistrations))
            {
                m_server.StagePreStartupTask(task);
            }

            await m_application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);

            foreach (IServerStartupTask startupTask in GetStartupTasks(useSharedRegistrations))
            {
                try
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await startupTask
                        .OnServerStartedAsync(m_server.CurrentInstance, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_logger.GdsServerStartupTaskFailed(ex, startupTask.GetType().FullName);
                    throw;
                }
            }

            foreach (string url in urls)
            {
                m_logger.GdsServerListening(url);
            }

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on host shutdown.
            }
        }

        private List<IUserTokenAuthenticator> CreateIdentityAuthenticators()
        {
            ICertificateValidatorEx? certificateValidator =
                m_application?.ApplicationConfiguration?.CertificateManager;
            var authenticators = new List<IUserTokenAuthenticator>();
            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in m_identityRegistrations)
            {
                foreach (IUserTokenAuthenticator authenticator in registration.CreateAuthenticators(
                    m_services,
                    certificateValidator))
                {
                    // The GDS's own UserName authenticator grants the roles the user
                    // database assigns, and its X.509 authenticator validates against
                    // the configured user trust list. The generic defaults, from the
                    // GDS builder or from a regular server sharing the container,
                    // would replace them with identities that carry no GDS role.
                    if (registration.ConfiguresDefaultAuthenticators &&
                        authenticator.TokenType is UserTokenType.UserName or UserTokenType.Certificate)
                    {
                        continue;
                    }

                    // JWT issuer registrations expand to one authenticator per issuer because JwtAuthenticator
                    // validates one fixed IssuerUri through its resolver.
                    authenticators.Add(authenticator);
                }
            }
            return authenticators;
        }

        private List<IIdentityAugmenter> CreateIdentityAugmenters()
        {
            var augmenters = new List<IIdentityAugmenter>();
            foreach (OpcUaServerIdentityAugmenterRegistration registration in m_augmenterRegistrations)
            {
                augmenters.Add(registration.CreateAugmenter(m_services));
            }
            return augmenters;
        }

        private IEnumerable<IServerPreStartupTask> GetPreStartupTasks(bool useSharedRegistrations)
        {
            if (useSharedRegistrations)
            {
                foreach (IServerPreStartupTask task in m_services.GetServices<IServerPreStartupTask>())
                {
                    yield return task;
                }
            }
            foreach (GdsServerPreStartupTaskRegistration registration in
                m_services.GetServices<GdsServerPreStartupTaskRegistration>())
            {
                yield return registration.Factory(m_services);
            }
        }

        private IEnumerable<IServerStartupTask> GetStartupTasks(bool useSharedRegistrations)
        {
            if (useSharedRegistrations)
            {
                foreach (IServerStartupTask task in m_services.GetServices<IServerStartupTask>())
                {
                    yield return task;
                }
            }
            foreach (GdsServerStartupTaskRegistration registration in
                m_services.GetServices<GdsServerStartupTaskRegistration>())
            {
                yield return registration.Factory(m_services);
            }
        }

        /// <summary>
        /// Whether a regular hosted server (<c>AddServer</c>) shares the container.
        /// </summary>
        private static bool HostsRegularServer(IServiceProvider services)
        {
            return services.GetService<IServiceProviderIsService>() is { } isService &&
                isService.IsService(typeof(IOpcUaServerFactory));
        }

        /// <summary>
        /// The options of the last <c>AddDefaultIdentityAuthenticators</c> call on the
        /// GDS builder, or <c>null</c> when it was not called.
        /// </summary>
        internal static GdsDefaultIdentityAuthenticatorOptions? GetDefaultAuthenticatorOptions(
            IServiceProvider services)
        {
            return services.GetServices<GdsDefaultIdentityAuthenticatorsRegistration>()
                .LastOrDefault()?.Options;
        }

        /// <summary>
        /// The user token types the GDS endpoints advertise: the configured
        /// <see cref="GdsServerOptions.UserTokenPolicies"/>, or Anonymous and
        /// UserName, leaving out a type the default authenticator options disable
        /// (Certificate when both are disabled).
        /// </summary>
        /// <exception cref="InvalidOperationException">No policy is configured and
        /// the default authenticator options disable every token type the GDS
        /// could advertise by default.</exception>
        /// <remarks>
        /// Anonymous serves registered applications, which pull their certificates
        /// with the ApplicationSelfAdmin Privilege (OPC 10000-12 §7.6); registering
        /// an application needs DiscoveryAdmin or the ApplicationAdmin Privilege
        /// (§6.5.6), which only an authenticated user holds.
        /// </remarks>
        internal static List<UserTokenType> GetUserTokenTypes(
            GdsServerOptions options,
            DefaultAuthenticatorOptions? defaultAuthenticatorOptions)
        {
            var tokenTypes = new List<UserTokenType>();
            if (options.UserTokenPolicies.Count > 0)
            {
                foreach (OpcUaUserTokenPolicy policy in options.UserTokenPolicies)
                {
                    if (!tokenTypes.Contains(policy.TokenType))
                    {
                        tokenTypes.Add(policy.TokenType);
                    }
                }
                return tokenTypes;
            }

            if (defaultAuthenticatorOptions?.EnableAnonymous != false)
            {
                tokenTypes.Add(UserTokenType.Anonymous);
            }
            if (defaultAuthenticatorOptions?.EnableUserNamePassword != false)
            {
                tokenTypes.Add(UserTokenType.UserName);
            }
            if (tokenTypes.Count == 0 && defaultAuthenticatorOptions?.EnableX509 != false)
            {
                // without Anonymous and UserName the configuration builder would fall
                // back to an Anonymous policy the GDS rejects.
                tokenTypes.Add(UserTokenType.Certificate);
            }
            if (tokenTypes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The GDS default identity authenticators disable Anonymous, UserName and X.509 " +
                    "access, so no user token policy can be advertised by default. Enable one of " +
                    "them or set GdsServerOptions.UserTokenPolicies explicitly.");
            }
            return tokenTypes;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);

            if (m_application != null)
            {
                m_logger.StoppingGdsServer();
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.ErrorWhileStoppingGdsServer(ex);
                }
                finally
                {
                    await m_application.DisposeAsync().ConfigureAwait(false);
                    m_application = null;
                }
            }
        }

        public override void Dispose()
        {
            m_server?.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// The application name the GDS runs as: <see cref="GdsServerOptions.ApplicationName"/>
        /// or <c>GlobalDiscoveryServer</c> when it is empty.
        /// </summary>
        internal static string ResolveApplicationName(GdsServerOptions options)
        {
            return string.IsNullOrEmpty(options.ApplicationName)
                ? "GlobalDiscoveryServer"
                : options.ApplicationName;
        }

        internal static GlobalDiscoveryServerConfiguration BuildGdsConfiguration(
            GdsServerOptions options,
            string pkiRoot)
        {
            string authoritiesStorePath = string.IsNullOrEmpty(options.AuthoritiesStorePath)
                ? Path.Combine(pkiRoot, "CA", "authorities")
                : options.AuthoritiesStorePath;

            string applicationCertificatesStorePath = string.IsNullOrEmpty(
                options.ApplicationCertificatesStorePath)
                ? Path.Combine(pkiRoot, "applications")
                : options.ApplicationCertificatesStorePath;

            string baseCertificateGroupStorePath = string.IsNullOrEmpty(
                options.BaseCertificateGroupStorePath)
                ? Path.Combine(pkiRoot, "CA")
                : options.BaseCertificateGroupStorePath;

            string defaultSubjectNameContext = string.IsNullOrEmpty(
                options.DefaultSubjectNameContext)
                ? ",O=OPC Foundation,DC=localhost"
                : options.DefaultSubjectNameContext;

            // OPC 10000-12 §7.8.3.3: the DefaultApplicationGroup is mandatory,
            // without a certificate group the GDS cannot issue certificates.
            // Configured groups that do not include it get it appended, so the
            // mandatory group is always backed by a CA.
            var groups = options.CertificateGroups
                .Select(group => ToCertificateGroupConfiguration(group, baseCertificateGroupStorePath))
                .ToList();
            if (!groups.Any(group => IsDefaultApplicationGroupId(group.Id)))
            {
                groups.Add(CreateDefaultApplicationGroup(
                    ResolveApplicationName(options),
                    baseCertificateGroupStorePath));
            }
            ArrayOf<CertificateGroupConfiguration> certificateGroups = groups.ToArrayOf();

            return new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath = authoritiesStorePath,
                ApplicationCertificatesStorePath = applicationCertificatesStorePath,
                BaseCertificateGroupStorePath = baseCertificateGroupStorePath,
                DefaultSubjectNameContext = defaultSubjectNameContext,
                CertificateGroups = certificateGroups,
                KnownHostNames = []
            };
        }

        /// <summary>
        /// Whether <paramref name="groupId"/> selects the DefaultApplicationGroup
        /// (the ids ApplicationsNodeManager maps onto that standard node).
        /// </summary>
        private static bool IsDefaultApplicationGroupId(string? groupId)
        {
            return string.Equals(groupId, "Default", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(groupId, "DefaultApplicationGroup", StringComparison.OrdinalIgnoreCase);
        }

        private static CertificateGroupConfiguration CreateDefaultApplicationGroup(
            string applicationName,
            string baseCertificateGroupStorePath)
        {
            return new CertificateGroupConfiguration
            {
                Id = "Default",
                CertificateTypes = [nameof(Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType)],
                SubjectName = $"CN={applicationName} CA, O=OPC Foundation",
                BaseStorePath = Path.Combine(baseCertificateGroupStorePath, "default")
            };
        }

        private static CertificateGroupConfiguration ToCertificateGroupConfiguration(
            GdsCertificateGroupOptions group,
            string baseCertificateGroupStorePath)
        {
            return new CertificateGroupConfiguration
            {
                Id = group.Id,
                CertificateTypes = group.CertificateTypes.ToArrayOf(),
                SubjectName = group.SubjectName,
                BaseStorePath = string.IsNullOrEmpty(group.BaseStorePath)
                    ? Path.Combine(baseCertificateGroupStorePath, group.Id)
                    : group.BaseStorePath,
                DefaultCertificateLifetime = group.DefaultCertificateLifetime,
                DefaultCertificateKeySize = group.DefaultCertificateKeySize,
                DefaultCertificateHashSize = group.DefaultCertificateHashSize,
                CACertificateLifetime = group.CACertificateLifetime,
                CACertificateKeySize = group.CACertificateKeySize,
                CACertificateHashSize = group.CACertificateHashSize
            };
        }

        private static ReverseConnectServerConfiguration ToReverseConnectConfiguration(
            ServerReverseConnectOptions options)
        {
            var clients = new ReverseConnectClient[options.Clients.Count];
            for (int i = 0; i < options.Clients.Count; i++)
            {
                ServerReverseConnectClientOptions c = options.Clients[i];
                clients[i] = new ReverseConnectClient
                {
                    EndpointUrl = c.EndpointUrl,
                    Timeout = c.Timeout,
                    MaxSessionCount = c.MaxSessionCount,
                    Enabled = c.Enabled
                };
            }

            return new ReverseConnectServerConfiguration
            {
                Clients = new ArrayOf<ReverseConnectClient>(clients),
                ConnectInterval = options.ConnectIntervalMs,
                ConnectTimeout = options.ConnectTimeoutMs,
                RejectTimeout = options.RejectTimeoutMs
            };
        }

        /// <summary>
        /// <see cref="GlobalDiscoverySampleServer"/> subclass that wires
        /// the optional DI-supplied services (<see cref="IAccessTokenProvider"/>,
        /// <see cref="IKeyCredentialRequestStore"/>, <see cref="IConfigurationDataStore"/>)
        /// into the <see cref="ApplicationsNodeManager"/> and adds a
        /// <see cref="DefaultManagedApplicationsNodeManager"/> when a
        /// configuration store is supplied.
        /// </summary>
        private sealed class GdsHostedServer : GlobalDiscoverySampleServer
        {
            private readonly IApplicationsDatabase m_database;
            private readonly ICertificateRequest m_request;
            private readonly ICertificateGroup m_certificateGroup;
            private readonly IAccessTokenProvider? m_accessTokenProvider;
            private readonly IKeyCredentialRequestStore? m_keyCredentialStore;
            private readonly IConfigurationDataStore? m_configurationStore;
            private readonly bool m_autoApprove;
            private IReadOnlyList<IUserTokenAuthenticator> m_authenticators = [];
            private IReadOnlyList<IIdentityAugmenter> m_augmenters = [];

            public GdsHostedServer(
                IApplicationsDatabase database,
                ICertificateRequest request,
                ICertificateGroup certificateGroup,
                IUserDatabase userDatabase,
                ITelemetryContext telemetry,
                IAccessTokenProvider? accessTokenProvider,
                IKeyCredentialRequestStore? keyCredentialStore,
                IConfigurationDataStore? configurationStore,
                bool autoApprove,
                bool enableApplicationSelfAdminProvider)
                : base(
                    database,
                    request,
                    certificateGroup,
                    userDatabase,
                    telemetry,
                    autoApprove,
                    enableApplicationSelfAdminProvider)
            {
                m_database = database;
                m_request = request;
                m_certificateGroup = certificateGroup;
                m_accessTokenProvider = accessTokenProvider;
                m_keyCredentialStore = keyCredentialStore;
                m_configurationStore = configurationStore;
                m_autoApprove = autoApprove;
            }

            /// <summary>
            /// The role manager registered with <c>ConfigureRoles</c>, or <c>null</c>
            /// for the server's default.
            /// </summary>
            public IRoleManager? ConfiguredRoleManager { get; set; }

            /// <summary>
            /// Stages the dependency-injected identity registrations, which
            /// <see cref="OnServerStarted"/> registers after the built-ins.
            /// </summary>
            public void StageIdentityRegistrations(
                IReadOnlyList<IUserTokenAuthenticator> authenticators,
                IReadOnlyList<IIdentityAugmenter> augmenters)
            {
                m_authenticators = authenticators;
                m_augmenters = augmenters;
            }

            /// <summary>
            /// Stages a pre-startup task; call before the server is started.
            /// </summary>
            public void StagePreStartupTask(IServerPreStartupTask task)
            {
                AddPreStartupTask(task);
            }

            protected override void OnServerStarted(IServerInternal server)
            {
                base.OnServerStarted(server);

                // OnServerStarted runs before the endpoints open, so no session
                // sees the built-ins these replace.
                foreach (IUserTokenAuthenticator authenticator in m_authenticators)
                {
                    server.IdentityRegistry.Register(authenticator);
                }
                foreach (IIdentityAugmenter augmenter in m_augmenters)
                {
                    server.IdentityRegistry.RegisterAugmenter(augmenter);
                }
            }

            protected override IRoleManager CreateRoleManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return ConfiguredRoleManager ?? base.CreateRoleManager(server, configuration);
            }

            protected override ValueTask<IMasterNodeManager> CreateMasterNodeManagerAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                var applications = new ApplicationsNodeManager(
                    server,
                    configuration,
                    m_database,
                    m_request,
                    m_certificateGroup,
                    m_autoApprove);

                if (m_accessTokenProvider != null)
                {
                    applications.AccessTokenProvider = m_accessTokenProvider;
                }

                if (m_keyCredentialStore != null)
                {
                    applications.KeyCredentialRequestStore = m_keyCredentialStore;
                }

                var nodeManagers = new List<IAsyncNodeManager> { applications };

                if (m_configurationStore != null)
                {
                    nodeManagers.Add(new DefaultManagedApplicationsNodeManager(
                        server,
                        configuration,
                        m_configurationStore));
                }

#pragma warning disable CA2000 // ownership of MasterNodeManager transfers to the caller via the returned ValueTask<IMasterNodeManager>
                return new ValueTask<IMasterNodeManager>(
                    new MasterNodeManager(server, configuration, null, nodeManagers, null));
#pragma warning restore CA2000
            }
        }
    }

    internal static partial class GdsServerHostedServiceLog
    {
        [LoggerMessage(EventId = GdsServerCommonEventIds.GdsServerHostedService + 0, Level = LogLevel.Information,
            Message = "GDS server listening at {Endpoint}.")]
        public static partial void GdsServerListening(this ILogger logger, string endpoint);

        [LoggerMessage(EventId = GdsServerCommonEventIds.GdsServerHostedService + 1, Level = LogLevel.Information,
            Message = "Stopping GDS server...")]
        public static partial void StoppingGdsServer(this ILogger logger);

        [LoggerMessage(EventId = GdsServerCommonEventIds.GdsServerHostedService + 2, Level = LogLevel.Warning,
            Message = "Error while stopping GDS server.")]
        public static partial void ErrorWhileStoppingGdsServer(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = GdsServerCommonEventIds.GdsServerHostedService + 3, Level = LogLevel.Warning,
            Message = "GDS certificate requests are approved automatically (AutoApprove); do not enable in production.")]
        public static partial void CertificateRequestAutoApprovalEnabled(this ILogger logger);

        [LoggerMessage(EventId = GdsServerCommonEventIds.GdsServerHostedService + 4, Level = LogLevel.Error,
            Message = "GDS server startup task {StartupTask} failed after server start.")]
        public static partial void GdsServerStartupTaskFailed(
            this ILogger logger,
            Exception ex,
            string? startupTask);
    }
}
