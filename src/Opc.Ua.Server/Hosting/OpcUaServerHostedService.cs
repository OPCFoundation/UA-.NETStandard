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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Schema;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// <see cref="BackgroundService"/> that hosts an OPC UA
    /// <see cref="StandardServer"/> within a .NET Generic Host. Owns the
    /// <see cref="ApplicationInstance"/> lifetime, builds the configuration
    /// from <see cref="OpcUaServerOptions"/>, and attaches every
    /// <see cref="OpcUaServerNodeManagerRegistration"/> resolved from DI
    /// before starting the server.
    /// </summary>
    internal sealed class OpcUaServerHostedService : BackgroundService
    {
        private readonly OpcUaServerOptions m_options;
        private readonly ITelemetryContext m_telemetry;
        private readonly IApplicationInstanceFactory m_applicationFactory;
        private readonly IOpcUaApplicationConfigurationProvider? m_configurationProvider;
        private readonly IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> m_identityRegistrations;
        private readonly IEnumerable<OpcUaServerIdentityAugmenterRegistration> m_augmenterRegistrations;
        private readonly IEnumerable<KeyCredentialPushSubject> m_keyCredentialPushSubjects;
        private readonly IServiceProvider m_services;
        private readonly IOpcUaServerFactory m_serverFactory;
        private readonly HostedNodeManagerLifecycle m_nodeManagerLifecycle;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<OpcUaServerHostedService> m_logger;
        // CA2213: ApplicationInstance is IAsyncDisposable; it is owned either
        // by this service and disposed during execution cleanup or by the shared provider.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private StandardServer? m_server;
        private bool m_ownsApplication;

        /// <summary>
        /// Initializes the hosted server with its options, injected registrations, factories, and lifecycle services.
        /// </summary>
        public OpcUaServerHostedService(
            IOptions<OpcUaServerOptions> options,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            IEnumerable<IOpcUaApplicationConfigurationProvider> configurationProviders,
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> identityRegistrations,
            IEnumerable<OpcUaServerIdentityAugmenterRegistration> augmenterRegistrations,
            IEnumerable<KeyCredentialPushSubject> keyCredentialPushSubjects,
            IServiceProvider services,
            IOpcUaServerFactory serverFactory,
            HostedNodeManagerLifecycle nodeManagerLifecycle,
            ILogger<OpcUaServerHostedService> logger,
            TimeProvider? timeProvider = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_applicationFactory = applicationFactory ?? throw new ArgumentNullException(nameof(applicationFactory));
            if (configurationProviders is null)
            {
                throw new ArgumentNullException(nameof(configurationProviders));
            }
            foreach (IOpcUaApplicationConfigurationProvider provider in configurationProviders)
            {
                m_configurationProvider = provider;
            }
            m_identityRegistrations = identityRegistrations ??
                throw new ArgumentNullException(nameof(identityRegistrations));
            m_augmenterRegistrations = augmenterRegistrations ??
                throw new ArgumentNullException(nameof(augmenterRegistrations));
            m_keyCredentialPushSubjects = keyCredentialPushSubjects ??
                throw new ArgumentNullException(nameof(keyCredentialPushSubjects));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_serverFactory = serverFactory ?? throw new ArgumentNullException(nameof(serverFactory));
            m_nodeManagerLifecycle = nodeManagerLifecycle ??
                throw new ArgumentNullException(nameof(nodeManagerLifecycle));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Waits for server cleanup before the host disposes its injected services.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await base.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (ExecuteTask is { } execution)
                {
                    // Keep dependencies alive through cleanup; execution failures remain on ExecuteTask,
                    // matching BackgroundService.StopAsync rather than rethrowing them during shutdown.
                    await Task.WhenAny(execution).ConfigureAwait(false);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await RunServerAsync(stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                await StopApplicationAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies DI isolation overrides before startup, leaving externally supplied providers host-owned.
        /// </summary>
        internal static void ApplyResourceIsolation(
            StandardServer server,
            IServiceProvider services,
            OpcUaServerOptions options)
        {
            ServerResourceIsolationOptions? isolation = services.GetService<ServerResourceIsolationOptions>();
            if (isolation == null &&
                (services.GetServices<IConfigureOptions<ServerResourceIsolationOptions>>().Any() ||
                 services.GetServices<IPostConfigureOptions<ServerResourceIsolationOptions>>().Any()))
            {
                isolation = services.GetRequiredService<IOptions<ServerResourceIsolationOptions>>().Value;
            }
            server.ResourceIsolationOptions = isolation ?? options.ResourceIsolation;
            server.ResourceIsolationClassifier = services.GetService<IResourceIsolationClassifier>();
            if (services.GetService<IServerResourceIsolationProvider>() is { } provider)
            {
                server.ResourceIsolationProvider = provider;
            }
        }

        /// <summary>
        /// Applies an optional DI policy while preserving a policy supplied by the server factory when absent.
        /// </summary>
        internal static void ApplyRequestParking(ServerBase server, IServiceProvider services)
        {
            if (services.GetService<IRequestParkingPolicy>() is { } policy)
            {
                server.RequestParkingPolicy = policy;
            }
        }

        /// <summary>
        /// Applies host configuration and injected features before startup, then waits for host shutdown.
        /// </summary>
        private async Task RunServerAsync(CancellationToken stoppingToken)
        {
            ICertificateManager? certificateManager =
                m_services.GetService<ICertificateManager>();
            if (HasSuppliedConfiguration)
            {
                bool hasConfigurationFile = !string.IsNullOrEmpty(m_options.ConfigurationFile);
                if (hasConfigurationFile && m_options.ConfigurationStream != null)
                {
                    throw new InvalidOperationException(
                        "Set only one of OpcUaServerOptions.ConfigurationFile " +
                        "and OpcUaServerOptions.ConfigurationStream.");
                }
                if (hasConfigurationFile &&
                    string.IsNullOrWhiteSpace(m_options.ConfigurationFile))
                {
                    throw new InvalidOperationException(
                        "OpcUaServerOptions.ConfigurationFile must not be a " +
                        "white-space path.");
                }

                // An explicitly supplied configuration document is the most
                // specific intent and therefore wins over a shared
                // application registered via ConfigureApplication(...).
                m_ownsApplication = true;
                await LoadSuppliedApplicationConfigurationAsync(
                    certificateManager,
                    stoppingToken).ConfigureAwait(false);
            }
            else if (m_configurationProvider != null)
            {
                m_application = m_configurationProvider.Application;
                ApplyDependencyInjectedCertificateManager(certificateManager);
                await m_configurationProvider.GetAsync(stoppingToken).ConfigureAwait(false);
            }
            else
            {
                m_ownsApplication = true;
                await CreateApplicationConfigurationAsync(
                    certificateManager,
                    stoppingToken).ConfigureAwait(false);
            }

            IApplicationInstance application = m_application ??
                throw new InvalidOperationException(
                    "The application configuration was not created.");
            ApplicationConfiguration configuration =
                application.ApplicationConfiguration ??
                throw new InvalidOperationException(
                    "The application configuration was not assigned.");
            bool haveCert = certificateManager is null or CertificateManager
                ? await application
                    .CheckApplicationInstanceCertificatesAsync(
                        silent: true,
                        CertificateFactory.DefaultLifeTime,
                        stoppingToken)
                    .ConfigureAwait(false)
                : await HasApplicationCertificateAsync(
                    certificateManager,
                    configuration,
                    stoppingToken).ConfigureAwait(false);
            if (!haveCert)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            ServerComplexTypeOptions? complexTypeOptions =
                m_services.GetService<ServerComplexTypeOptions>();

            m_server = m_serverFactory.CreateServer(m_telemetry, m_timeProvider);
            m_nodeManagerLifecycle.Attach(m_server.NodeManagerLifecycle);
            if (m_server is not DependencyInjectionStandardServer)
            {
                OpcUaServerRegistrationStaging.Apply(m_server, m_services);
            }

            // Complex-type loading is on by default (StandardServer.LoadComplexTypes);
            // build and register stand-in encodeables for runtime-loaded custom
            // DataTypes once the address space is available, and expose the primed
            // factory as the schema resolver. An explicitly registered
            // ServerComplexTypeOptions can tune or opt out (Enabled = false).
            m_server.ComplexTypeOptions = complexTypeOptions;
            m_server.ComplexTypeRegistry = m_services.GetService<DataTypeDefinitionRegistry>();
            m_server.ComplexTypeResolverHolder =
                m_services.GetService<ServerDataTypeDefinitionResolver>();
            if (complexTypeOptions != null)
            {
                m_server.LoadComplexTypes = complexTypeOptions.Enabled;
            }

            m_server.SessionManagerFactory = m_services.GetService<ISessionManagerFactory>();
            m_server.RedundantServerSetProvider = m_services.GetService<IRedundantServerSetProvider>();
            m_server.GetEndpointsDirector = m_services.GetService<IGetEndpointsDirector>();
            m_server.SubscriptionStore = m_services.GetService<ISubscriptionStore>();
            m_server.HistoryContinuationPointStore =
                m_services.GetService<IHistoryContinuationPointStore>();
            m_server.MonitoredItemQueueFactory = m_services.GetService<IMonitoredItemQueueFactory>();
            ApplyRequestParking(m_server, m_services);
            if (m_services.GetService<ITransportBindingRegistry>() is { } transportBindings)
            {
                m_server.TransportBindings = transportBindings;
            }

            // Apply admission-control (rate limiting) configuration: a DI-registered
            // provider wins; otherwise apply the options callback (rate limiting is
            // on by default with conservative limits when neither is supplied).
            IServerRateLimiterProvider? rateLimiterProvider =
                m_services.GetService<IServerRateLimiterProvider>();
            if (rateLimiterProvider != null)
            {
                m_server.RateLimiterProvider = rateLimiterProvider;
            }
            else if (m_options.ConfigureRateLimits != null)
            {
                var rateLimitOptions = new ServerRateLimitOptions();
                m_options.ConfigureRateLimits(rateLimitOptions);
                m_server.RateLimitOptions = rateLimitOptions;
            }

            // A registered budget bounds what incomplete messages may hold across
            // all the listeners; without one the server sizes it from the
            // maximum message size.
            if (m_services.GetService<ChunkReassemblyBudget>() is { } chunkReassemblyBudget)
            {
                m_server.ChunkReassemblyBudget = chunkReassemblyBudget;
            }
            if (m_services.GetService<ISessionBindingProvider>() is { } sessionBindingProvider)
            {
                m_server.SessionBindingProvider = sessionBindingProvider;
            }
            ApplyResourceIsolation(m_server, m_services, m_options);

            foreach (OpcUaServerNodeManagerRegistration reg in
                m_services.GetServices<OpcUaServerNodeManagerRegistration>())
            {
                stoppingToken.ThrowIfCancellationRequested();
                foreach (IAsyncNodeManagerFactory factory in reg.ResolveAsyncFactories(m_services, configuration))
                {
                    m_server.AddNodeManager(factory ??
                        throw new InvalidOperationException(
                            "The node-manager factories callback returned a null factory."));
                }

                if (reg.SyncFactory is not null)
                {
                    m_server.AddNodeManager(reg.SyncFactory);
                }
            }

            RegisterIdentityAuthenticators();
            RegisterIdentityAugmenters();
            await application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);
            await BindKeyCredentialPushAsync(stoppingToken).ConfigureAwait(false);

            // Run post-start tasks (e.g. distributed address-space wiring)
            // now that the server is fully initialized and CurrentInstance is
            // available. Features register these without subclassing the server.
            foreach (IServerStartupTask startupTask in m_services.GetServices<IServerStartupTask>())
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
                    if (m_logger.IsEnabled(LogLevel.Error))
                    {
                        m_logger.ServerStartupTaskStartupTaskFailedAfterServer(ex, startupTask.GetType().FullName);
                    }
                    throw;
                }
            }

            foreach (string url in GetListenEndpointUrls(configuration))
            {
                m_logger.OPCUAServerListeningAtEndpoint(url);
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

        private async Task CreateApplicationConfigurationAsync(
            ICertificateManager? certificateManager,
            CancellationToken ct)
        {
            string appName = string.IsNullOrEmpty(m_options.ApplicationName)
                ? "OpcUaServer"
                : m_options.ApplicationName;
            string pkiRoot = string.IsNullOrEmpty(m_options.PkiRoot)
                ? DefaultPkiRoot.Get(appName, m_logger)
                : m_options.PkiRoot;
            string subject = string.IsNullOrEmpty(m_options.SubjectName)
                ? $"CN={appName}, O=OPC Foundation, DC=localhost"
                : m_options.SubjectName;

            m_application = m_applicationFactory.Create(m_telemetry);
            m_application.ApplicationName = appName;
            m_application.ApplicationType = ApplicationType.Server;

            IApplicationConfigurationBuilderSecurity securityBuilder =
                OpcUaServerApplicationConfigurationFeature.Configure(
                    m_application.Build(m_options.ApplicationUri, m_options.ProductUri),
                    m_options);
            ArrayOf<CertificateIdentifier> certificates =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subject,
                    CertificateStoreType.Directory,
                    pkiRoot);
            IApplicationConfigurationBuilderSecurityOptions securityOptions = securityBuilder
                .AddSecurityConfiguration(certificates, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates)
                .SetRejectSHA1SignedCertificates(m_options.RejectSHA1Certificates);
            if (m_options.MinCertificateKeySize > 0)
            {
                securityOptions = securityOptions.SetMinimumCertificateKeySize(
                    m_options.MinCertificateKeySize);
            }

            ApplyDependencyInjectedCertificateManager(certificateManager);
            await securityOptions.CreateAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Whether an existing configuration XML document was supplied via
        /// <see cref="OpcUaServerOptions.ConfigurationFile"/> or
        /// <see cref="OpcUaServerOptions.ConfigurationStream"/>.
        /// </summary>
        private bool HasSuppliedConfiguration =>
            !string.IsNullOrEmpty(m_options.ConfigurationFile) ||
            m_options.ConfigurationStream != null;

        /// <summary>
        /// Loads the application configuration from the XML configuration
        /// document supplied via <see cref="OpcUaServerOptions.ConfigurationFile"/>
        /// or <see cref="OpcUaServerOptions.ConfigurationStream"/> so every
        /// setting from the document is applied as-is, then applies the
        /// optional <see cref="OpcUaServerOptions.ConfigureLoadedConfiguration"/>
        /// override callback. A supplied stream is read once and disposed.
        /// </summary>
        private async Task LoadSuppliedApplicationConfigurationAsync(
            ICertificateManager? certificateManager,
            CancellationToken ct)
        {
            IApplicationInstance application = m_applicationFactory.Create(m_telemetry);
            m_application = application;
            application.ApplicationType = ApplicationType.Server;
            application.CertificatePasswordProvider =
                m_services.GetService<ICertificatePasswordProvider>();

            ApplicationConfiguration configuration;
            if (m_options.ConfigurationStream is { } configurationStream)
            {
                m_logger.LoadingOPCUAServerConfigurationFromStream();
                using (configurationStream)
                {
                    configuration = await application
                        .LoadApplicationConfigurationAsync(configurationStream, silent: false, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                string configurationFile = m_options.ConfigurationFile!;
                m_logger.LoadingOPCUAServerConfigurationFromFile(configurationFile);
                configuration = await application
                    .LoadApplicationConfigurationAsync(configurationFile, silent: false, ct)
                    .ConfigureAwait(false);
            }

            application.ApplicationName = configuration.ApplicationName;
            m_options.ConfigureLoadedConfiguration?.Invoke(configuration);
            ApplyDependencyInjectedCertificateManager(certificateManager);
        }

        /// <summary>
        /// The endpoint URLs to report as listening: the configured
        /// <see cref="OpcUaServerOptions.EndpointUrls"/> when present,
        /// otherwise the base addresses of the effective configuration
        /// (e.g. when it was loaded from a configuration file).
        /// </summary>
        private string[] GetListenEndpointUrls(ApplicationConfiguration configuration)
        {
            if (m_options.EndpointUrls.Count > 0)
            {
                string[] urls = new string[m_options.EndpointUrls.Count];
                m_options.EndpointUrls.CopyTo(urls, 0);
                return urls;
            }

            if (configuration.ServerConfiguration is { } serverConfiguration)
            {
                string[] urls = new string[serverConfiguration.BaseAddresses.Count];
                for (int i = 0; i < urls.Length; i++)
                {
                    urls[i] = serverConfiguration.BaseAddresses[i];
                }
                return urls;
            }

            return [];
        }

        private async Task BindKeyCredentialPushAsync(CancellationToken ct)
        {
            if (m_server == null)
            {
                return;
            }

            foreach (KeyCredentialPushSubject subject in m_keyCredentialPushSubjects)
            {
                await m_server.CurrentInstance.ConfigurationNodeManager
                    .BindKeyCredentialPushAsync(subject, ct)
                    .ConfigureAwait(false);
            }
        }

        private void RegisterIdentityAuthenticators()
        {
            if (m_server == null)
            {
                return;
            }

            ICertificateValidatorEx? certificateValidator =
                m_application?.ApplicationConfiguration?.CertificateManager;

            List<IUserTokenAuthenticator> authenticators = CreateIdentityAuthenticators(
                m_identityRegistrations,
                m_services,
                certificateValidator);
            ServerConfiguration? serverConfiguration =
                m_application?.ApplicationConfiguration?.ServerConfiguration;
            if (authenticators.Exists(a => a is AnonymousRejectingAuthenticator) &&
                m_options.UserTokenPolicies.Count == 0 &&
                !HasSuppliedConfiguration &&
                serverConfiguration != null)
            {
                // The Anonymous policy is only the implicit default: an endpoint lists the
                // user identity tokens the Server accepts (Part 4 7.14, 7.41), so advertise
                // the token types of the registered authenticators instead.
                serverConfiguration.UserTokenPolicies = ReplaceDefaultAnonymousUserTokenPolicy(
                    serverConfiguration.UserTokenPolicies,
                    authenticators);
                if (serverConfiguration.UserTokenPolicies.IsEmpty)
                {
                    // no authenticator accepts a token: the server falls back to the
                    // Anonymous policy, which is always rejected.
                    m_logger.UserTokenPolicyTokenTypeIsConfiguredWithout(UserTokenType.Anonymous);
                }
            }
            else if (authenticators.Exists(a => a is AnonymousRejectingAuthenticator))
            {
                foreach (UserTokenType tokenType in
                    GetAdvertisedUserTokenTypes(m_application?.ApplicationConfiguration))
                {
                    if (tokenType == UserTokenType.Anonymous)
                    {
                        // advertised but always rejected: clients will fail to connect.
                        m_logger.UserTokenPolicyTokenTypeIsConfiguredWithout(tokenType);
                        break;
                    }
                }
            }

            WarnForUnmatchedUserTokenPolicies(
                authenticators,
                m_application?.ApplicationConfiguration);

            foreach (IUserTokenAuthenticator authenticator in authenticators)
            {
                // JWT issuer registrations expand to one authenticator per issuer because JwtAuthenticator
                // validates one fixed IssuerUri through its resolver.
                m_server.RegisterIdentityAuthenticator(authenticator);
            }
        }

        /// <summary>
        /// Materializes the registered identity authenticators. Anonymous tokens are
        /// rejected explicitly only when the default authenticator options disabled
        /// anonymous access; otherwise an unhandled anonymous token falls through to
        /// the session manager, which accepts it whenever the endpoint advertises it.
        /// Custom authenticators alone do not disable anonymous access.
        /// </summary>
        internal static List<IUserTokenAuthenticator> CreateIdentityAuthenticators(
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> registrations,
            IServiceProvider services,
            ICertificateValidatorEx? certificateValidator)
        {
            var authenticators = new List<IUserTokenAuthenticator>();
            bool configuresDefaultAuthenticators = false;
            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in registrations)
            {
                configuresDefaultAuthenticators |= registration.ConfiguresDefaultAuthenticators;
                authenticators.AddRange(registration.CreateAuthenticators(
                    services,
                    certificateValidator));
            }

            if (configuresDefaultAuthenticators &&
                !authenticators.Exists(a => a.TokenType == UserTokenType.Anonymous))
            {
                authenticators.Add(new AnonymousRejectingAuthenticator());
            }
            else if (authenticators.Count == 0)
            {
                // no identity configuration at all: keep the anonymous default.
                authenticators.Add(new AnonymousAuthenticator());
            }

            return authenticators;
        }

        /// <summary>
        /// Removes the Anonymous policies and adds one policy for each non-anonymous token
        /// type the authenticators accept that is not advertised yet.
        /// </summary>
        internal static ArrayOf<UserTokenPolicy> ReplaceDefaultAnonymousUserTokenPolicy(
            ArrayOf<UserTokenPolicy> policies,
            IReadOnlyList<IUserTokenAuthenticator> authenticators)
        {
            var result = new List<UserTokenPolicy>();
            for (int i = 0; i < policies.Count; i++)
            {
                if (policies[i].TokenType != UserTokenType.Anonymous)
                {
                    result.Add(policies[i]);
                }
            }

            foreach (IUserTokenAuthenticator authenticator in authenticators)
            {
                if (authenticator.TokenType == UserTokenType.Anonymous ||
                    result.Exists(p =>
                        p.TokenType == authenticator.TokenType &&
                        (authenticator.TokenType != UserTokenType.IssuedToken ||
                            p.IssuedTokenType == authenticator.IssuedTokenProfileUri)))
                {
                    continue;
                }

                var policy = new UserTokenPolicy(authenticator.TokenType);
                if (authenticator.TokenType == UserTokenType.IssuedToken)
                {
                    policy.IssuedTokenType = authenticator.IssuedTokenProfileUri;
                }
                result.Add(policy);
            }

            return new ArrayOf<UserTokenPolicy>(result.ToArray());
        }

        private void RegisterIdentityAugmenters()
        {
            if (m_server == null)
            {
                return;
            }

            foreach (OpcUaServerIdentityAugmenterRegistration registration in m_augmenterRegistrations)
            {
                m_server.RegisterIdentityAugmenter(
                    registration.CreateAugmenter(m_services));
            }
        }

        private void ApplyDependencyInjectedCertificateManager(
            ICertificateManager? certificateManager)
        {
            if (certificateManager == null ||
                m_application?.ApplicationConfiguration == null)
            {
                return;
            }

            m_application.ApplicationConfiguration.CertificateManager = certificateManager;
        }

        private static async Task<bool> HasApplicationCertificateAsync(
            ICertificateManager certificateManager,
            ApplicationConfiguration configuration,
            CancellationToken ct)
        {
            await certificateManager.UpdateAsync(
                configuration.SecurityConfiguration,
                configuration.ApplicationUri,
                ct).ConfigureAwait(false);
            using CertificateEntryCollection certificates =
                certificateManager.SnapshotApplicationCertificates();
            return certificates.Count > 0;
        }

        private void WarnForUnmatchedUserTokenPolicies(
            IReadOnlyList<IUserTokenAuthenticator> authenticators,
            ApplicationConfiguration? configuration)
        {
            foreach (UserTokenType tokenType in GetAdvertisedUserTokenTypes(configuration))
            {
                if (tokenType == UserTokenType.Anonymous)
                {
                    continue;
                }

                if (!HasMatchingAuthenticator(tokenType, authenticators))
                {
                    m_logger.UserTokenPolicyTokenTypeIsConfiguredWithout(tokenType);
                }
            }
        }

        /// <summary>
        /// The user token types the server advertises: the option-configured
        /// policies when present, the policies of the loaded configuration
        /// document on the <see cref="OpcUaServerOptions.ConfigurationFile"/> /
        /// <see cref="OpcUaServerOptions.ConfigurationStream"/> path, and the
        /// anonymous fallback otherwise.
        /// </summary>
        private IEnumerable<UserTokenType> GetAdvertisedUserTokenTypes(
            ApplicationConfiguration? configuration)
        {
            if (m_options.UserTokenPolicies.Count > 0)
            {
                foreach (OpcUaUserTokenPolicy policy in m_options.UserTokenPolicies)
                {
                    yield return policy.TokenType;
                }
                yield break;
            }

            if (HasSuppliedConfiguration &&
                configuration?.ServerConfiguration != null)
            {
                ArrayOf<UserTokenPolicy> policies =
                    configuration.ServerConfiguration.UserTokenPolicies;
                for (int i = 0; i < policies.Count; i++)
                {
                    yield return policies[i].TokenType;
                }
                yield break;
            }

            yield return UserTokenType.Anonymous;
        }

        private static bool HasMatchingAuthenticator(
            UserTokenType tokenType,
            IReadOnlyList<IUserTokenAuthenticator> authenticators)
        {
            foreach (IUserTokenAuthenticator authenticator in authenticators)
            {
                if (tokenType == UserTokenType.UserName && authenticator is UserNamePasswordAuthenticator)
                {
                    return true;
                }
                if (tokenType == UserTokenType.Certificate && authenticator is X509Authenticator)
                {
                    return true;
                }
                if (tokenType == UserTokenType.IssuedToken && authenticator is JwtAuthenticator)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rejects anonymous identity tokens when the identity configuration
        /// does not enable anonymous access.
        /// </summary>
        internal sealed class AnonymousRejectingAuthenticator : IUserTokenAuthenticator
        {
            /// <inheritdoc/>
            public UserTokenType TokenType => UserTokenType.Anonymous;

            /// <inheritdoc/>
            public string? IssuedTokenProfileUri => null;

            /// <inheritdoc/>
            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(
                    AuthenticationResult.Reject(new ServiceResult(
                        StatusCodes.BadIdentityTokenRejected,
                        new LocalizedText(
                            "Anonymous access is disabled by the server identity configuration."))));
            }
        }

        private async ValueTask StopApplicationAsync(CancellationToken cancellationToken)
        {
            if (m_server is not null)
            {
                m_nodeManagerLifecycle.Detach(m_server.NodeManagerLifecycle);
            }

            IApplicationInstance? application = Interlocked.Exchange(ref m_application, null);
            if (application != null)
            {
                m_logger.StoppingOPCUAServer();
                try
                {
                    await application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.ErrorWhileStoppingOPCUAServer(ex);
                }
                finally
                {
                    try
                    {
                        if (m_ownsApplication)
                        {
                            await application.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        m_server?.Dispose();
                        m_server = null;
                    }
                }
            }
        }

        /// <summary>
        /// Detaches the node-manager lifecycle and disposes the server and background service.
        /// </summary>
        public override void Dispose()
        {
            if (m_server is not null)
            {
                m_nodeManagerLifecycle.Detach(m_server.NodeManagerLifecycle);
            }
            m_server?.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Source-generated log messages for OpcUaServerHostedService.
    /// </summary>
    internal static partial class OpcUaServerHostedServiceLog
    {
        /// <summary>
        /// Logs a startup task failure after the OPC UA server has started.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 0, Level = LogLevel.Error,
            Message = "Server startup task {StartupTask} failed after server start.")]
        public static partial void ServerStartupTaskStartupTaskFailedAfterServer(
            this ILogger logger,
            Exception ex,
            string? startupTask);

        /// <summary>
        /// Logs an endpoint on which the OPC UA server is listening.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 1, Level = LogLevel.Information,
            Message = "OPC UA server listening at {Endpoint}.")]
        public static partial void OPCUAServerListeningAtEndpoint(this ILogger logger, string endpoint);

        /// <summary>
        /// Logs a configured user token policy that lacks a matching identity authenticator.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 2, Level = LogLevel.Warning,
            Message = "User token policy {TokenType} is configured without a matching identity authenticator.")]
        public static partial void UserTokenPolicyTokenTypeIsConfiguredWithout(
            this ILogger logger,
            UserTokenType tokenType);

        /// <summary>
        /// Logs the start of hosted OPC UA server shutdown.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 3, Level = LogLevel.Information,
            Message = "Stopping OPC UA server...")]
        public static partial void StoppingOPCUAServer(this ILogger logger);

        /// <summary>
        /// Logs an exception while stopping the hosted OPC UA server.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 4, Level = LogLevel.Warning,
            Message = "Error while stopping OPC UA server.")]
        public static partial void ErrorWhileStoppingOPCUAServer(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs the file used to load the OPC UA server configuration.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 5, Level = LogLevel.Information,
            Message = "Loading OPC UA server configuration from file {ConfigurationFile}.")]
        public static partial void LoadingOPCUAServerConfigurationFromFile(
            this ILogger logger,
            string configurationFile);

        /// <summary>
        /// Logs loading of the OPC UA server configuration from a stream.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 6, Level = LogLevel.Information,
            Message = "Loading OPC UA server configuration from a stream.")]
        public static partial void LoadingOPCUAServerConfigurationFromStream(this ILogger logger);
    }
}
