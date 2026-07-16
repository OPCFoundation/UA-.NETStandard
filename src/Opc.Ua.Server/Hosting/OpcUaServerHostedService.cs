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
using Opc.Ua.Server.AliasNames;
using Opc.Ua.Server.Historian;

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
        private readonly IEnumerable<OpcUaServerNodeManagerRegistration> m_registrations;
        private readonly IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> m_identityRegistrations;
        private readonly IEnumerable<OpcUaServerIdentityAugmenterRegistration> m_augmenterRegistrations;
        private readonly IEnumerable<KeyCredentialPushSubject> m_keyCredentialPushSubjects;
        private readonly IServiceProvider m_services;
        private readonly IOpcUaServerFactory m_serverFactory;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger<OpcUaServerHostedService> m_logger;
        // CA2213: ApplicationInstance is IAsyncDisposable; it is owned either
        // by this service and disposed in StopAsync or by the shared provider.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private StandardServer? m_server;
        private bool m_ownsApplication;

        public OpcUaServerHostedService(
            IOptions<OpcUaServerOptions> options,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            IEnumerable<IOpcUaApplicationConfigurationProvider> configurationProviders,
            IEnumerable<OpcUaServerNodeManagerRegistration> registrations,
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> identityRegistrations,
            IEnumerable<OpcUaServerIdentityAugmenterRegistration> augmenterRegistrations,
            IEnumerable<KeyCredentialPushSubject> keyCredentialPushSubjects,
            IServiceProvider services,
            IOpcUaServerFactory serverFactory,
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
            m_registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            m_identityRegistrations = identityRegistrations ??
                throw new ArgumentNullException(nameof(identityRegistrations));
            m_augmenterRegistrations = augmenterRegistrations ??
                throw new ArgumentNullException(nameof(augmenterRegistrations));
            m_keyCredentialPushSubjects = keyCredentialPushSubjects ??
                throw new ArgumentNullException(nameof(keyCredentialPushSubjects));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_serverFactory = serverFactory ?? throw new ArgumentNullException(nameof(serverFactory));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string[] urls = new string[m_options.EndpointUrls.Count];
            m_options.EndpointUrls.CopyTo(urls, 0);
            ICertificateManager? certificateManager =
                m_services.GetService<ICertificateManager>();
            if (m_configurationProvider != null)
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
            m_server.MonitoredItemQueueFactory = m_services.GetService<IMonitoredItemQueueFactory>();
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

            foreach (OpcUaServerNodeManagerRegistration reg in m_registrations)
            {
                if (reg.AsyncFactory is not null)
                {
                    m_server.AddNodeManager(reg.AsyncFactory);
                }
                if (reg.SyncFactory is not null)
                {
                    m_server.AddNodeManager(reg.SyncFactory);
                }
            }

            await application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);
            RegisterPostStartRegistries();
            await BindKeyCredentialPushAsync(stoppingToken).ConfigureAwait(false);
            RegisterIdentityAuthenticators();
            RegisterIdentityAugmenters();

            // Run post-start tasks (e.g. distributed address-space wiring)
            // now that the server is fully initialized and CurrentInstance is
            // available. Features register these without subclassing the server.
            foreach (IServerStartupTask startupTask in m_services.GetServices<IServerStartupTask>())
            {
                try
                {
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
                }
            }

            foreach (string url in urls)
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
                ? Path.Combine(Path.GetTempPath(), "OPC Foundation", appName, "pki")
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

            var authenticators = new List<IUserTokenAuthenticator>();
            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in m_identityRegistrations)
            {
                authenticators.AddRange(registration.CreateAuthenticators(
                    m_services,
                    certificateValidator));
            }

            if (authenticators.Count == 0)
            {
                authenticators.Add(new AnonymousAuthenticator());
            }

            WarnForUnmatchedUserTokenPolicies(authenticators);

            foreach (IUserTokenAuthenticator authenticator in authenticators)
            {
                // JWT issuer registrations expand to one authenticator per issuer because JwtAuthenticator
                // validates one fixed IssuerUri through its resolver.
                m_server.CurrentInstance.IdentityRegistry.Register(authenticator);
            }
        }

        private void RegisterPostStartRegistries()
        {
            if (m_server is null or DependencyInjectionStandardServer)
            {
                return;
            }

            IServerInternal server = m_server.CurrentInstance;
            if (server is IHistorianRegistryProvider historianRegistryProvider)
            {
                foreach (OpcUaServerHistorianRegistration registration in
                    m_services.GetServices<OpcUaServerHistorianRegistration>())
                {
                    historianRegistryProvider.HistorianRegistry.RegisterDefault(registration.Provider);
                }
            }

            if (server is IAliasNameStoreRegistryProvider aliasNameStoreRegistryProvider)
            {
                foreach (IAliasNameStoreRegistry registry in m_services.GetServices<IAliasNameStoreRegistry>())
                {
                    foreach (IAliasNameStore store in registry.Stores)
                    {
                        aliasNameStoreRegistryProvider.AliasNameStoreRegistry.Register(store);
                    }
                }

                foreach (OpcUaServerAliasNameStoreRegistration registration in
                    m_services.GetServices<OpcUaServerAliasNameStoreRegistration>())
                {
                    aliasNameStoreRegistryProvider.AliasNameStoreRegistry.Register(registration.Store);
                }
            }
        }

        private void RegisterIdentityAugmenters()
        {
            if (m_server == null)
            {
                return;
            }

            foreach (OpcUaServerIdentityAugmenterRegistration registration in m_augmenterRegistrations)
            {
                m_server.CurrentInstance.IdentityRegistry.RegisterAugmenter(
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
            using var certificates =
                certificateManager.SnapshotApplicationCertificates();
            return certificates.Count > 0;
        }

        private void WarnForUnmatchedUserTokenPolicies(IReadOnlyList<IUserTokenAuthenticator> authenticators)
        {
            IEnumerable<OpcUaUserTokenPolicy> policies = m_options.UserTokenPolicies.Count == 0
                ? [new OpcUaUserTokenPolicy { TokenType = UserTokenType.Anonymous }]
                : m_options.UserTokenPolicies;

            foreach (OpcUaUserTokenPolicy policy in policies)
            {
                if (policy.TokenType == UserTokenType.Anonymous)
                {
                    continue;
                }

                if (!HasMatchingAuthenticator(policy.TokenType, authenticators))
                {
                    m_logger.UserTokenPolicyTokenTypeIsConfiguredWithout(policy.TokenType);
                }
            }
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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);

            if (m_application != null)
            {
                m_logger.StoppingOPCUAServer();
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.ErrorWhileStoppingOPCUAServer(ex);
                }
                finally
                {
                    if (m_ownsApplication)
                    {
                        await m_application.DisposeAsync().ConfigureAwait(false);
                    }
                    m_application = null;
                }
            }
        }

        public override void Dispose()
        {
            m_server?.Dispose();
            base.Dispose();
        }

    }

    /// <summary>
    /// Source-generated log messages for OpcUaServerHostedService.
    /// </summary>
    internal static partial class OpcUaServerHostedServiceLog
    {
        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 0, Level = LogLevel.Error,
            Message = "Server startup task {StartupTask} failed after server start.")]
        public static partial void ServerStartupTaskStartupTaskFailedAfterServer(
            this ILogger logger,
            Exception ex,
            string? startupTask);

        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 1, Level = LogLevel.Information,
            Message = "OPC UA server listening at {Endpoint}.")]
        public static partial void OPCUAServerListeningAtEndpoint(this ILogger logger, string endpoint);

        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 2, Level = LogLevel.Warning,
            Message = "User token policy {TokenType} is configured without a matching identity authenticator.")]
        public static partial void UserTokenPolicyTokenTypeIsConfiguredWithout(
            this ILogger logger,
            UserTokenType tokenType);

        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 3, Level = LogLevel.Information,
            Message = "Stopping OPC UA server...")]
        public static partial void StoppingOPCUAServer(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.OpcUaServerHostedService + 4, Level = LogLevel.Warning,
            Message = "Error while stopping OPC UA server.")]
        public static partial void ErrorWhileStoppingOPCUAServer(this ILogger logger, Exception ex);
    }

}
