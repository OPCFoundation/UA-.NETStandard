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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Identity;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;

#nullable enable

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
        // m_application.StopAsync.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private GdsHostedServer? m_server;

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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string appName = string.IsNullOrEmpty(m_options.ApplicationName)
                ? "GlobalDiscoveryServer"
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

            IApplicationConfigurationBuilderServerOptions optionsBuilder =
                serverBuilder.SetDiagnosticsEnabled(m_options.DiagnosticsEnabled);

            m_options.ConfigureBuilder?.Invoke(serverBuilder);

            IApplicationConfigurationBuilderSecurityOptions securityBuilder = optionsBuilder
                .AddSecurityConfiguration(certs, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates);

            await securityBuilder
                .AddExtension(
                    new XmlQualifiedName(
                        nameof(GlobalDiscoveryServerConfiguration),
                        Namespaces.OpcUaGds + "Configuration.xsd"),
                    BuildGdsConfiguration(pkiRoot))
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

            await m_application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);
            RegisterIdentityAuthenticators();
            RegisterIdentityAugmenters();

            foreach (string url in urls)
            {
                m_logger.LogInformation("GDS server listening at {Endpoint}.", url);
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

        private void RegisterIdentityAuthenticators()
        {
            if (m_server == null)
            {
                return;
            }

            ICertificateValidatorEx? certificateValidator =
                m_application?.ApplicationConfiguration?.CertificateManager;

            foreach (OpcUaServerIdentityAuthenticatorRegistration registration in m_identityRegistrations)
            {
                foreach (IUserTokenAuthenticator authenticator in registration.CreateAuthenticators(
                    m_services,
                    certificateValidator))
                {
                    // JWT issuer registrations expand to one authenticator per issuer because JwtAuthenticator
                    // validates one fixed IssuerUri through its resolver.
                    m_server.CurrentInstance.IdentityRegistry.Register(authenticator);
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

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);

            if (m_application != null)
            {
                m_logger.LogInformation("Stopping GDS server...");
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Error while stopping GDS server.");
                }
            }
        }

        public override void Dispose()
        {
            m_server?.Dispose();
            base.Dispose();
        }

        private GlobalDiscoveryServerConfiguration BuildGdsConfiguration(string pkiRoot)
        {
            string authoritiesStorePath = string.IsNullOrEmpty(m_options.AuthoritiesStorePath)
                ? Path.Combine(pkiRoot, "CA", "authorities")
                : m_options.AuthoritiesStorePath;

            string applicationCertificatesStorePath = string.IsNullOrEmpty(
                m_options.ApplicationCertificatesStorePath)
                ? Path.Combine(pkiRoot, "applications")
                : m_options.ApplicationCertificatesStorePath;

            string baseCertificateGroupStorePath = string.IsNullOrEmpty(
                m_options.BaseCertificateGroupStorePath)
                ? Path.Combine(pkiRoot, "CA")
                : m_options.BaseCertificateGroupStorePath;

            string defaultSubjectNameContext = string.IsNullOrEmpty(
                m_options.DefaultSubjectNameContext)
                ? ",O=OPC Foundation,DC=localhost"
                : m_options.DefaultSubjectNameContext;

            return new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath = authoritiesStorePath,
                ApplicationCertificatesStorePath = applicationCertificatesStorePath,
                BaseCertificateGroupStorePath = baseCertificateGroupStorePath,
                DefaultSubjectNameContext = defaultSubjectNameContext,
                CertificateGroups = [],
                KnownHostNames = []
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
}
