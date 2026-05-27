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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;

#nullable enable

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
        private readonly RoleConfigurationOptions m_roleOptions;
        private readonly ITelemetryContext m_telemetry;
        private readonly IApplicationInstanceFactory m_applicationFactory;
        private readonly IEnumerable<OpcUaServerNodeManagerRegistration> m_registrations;
        private readonly IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> m_identityRegistrations;
        private readonly IEnumerable<OpcUaServerIdentityAugmenterRegistration> m_augmenterRegistrations;
        private readonly IEnumerable<KeyCredentialPushSubject> m_keyCredentialPushSubjects;
        private readonly IServiceProvider m_services;
        private readonly ILogger<OpcUaServerHostedService> m_logger;
        // CA2213: ApplicationInstance is IAsyncDisposable; the lifecycle here is
        // managed via the async StopAsync override which calls m_application.StopAsync.
#pragma warning disable CA2213
        private IApplicationInstance? m_application;
#pragma warning restore CA2213
        private StandardServer? m_server;

        public OpcUaServerHostedService(
            IOptions<OpcUaServerOptions> options,
            IOptions<RoleConfigurationOptions> roleOptions,
            ITelemetryContext telemetry,
            IApplicationInstanceFactory applicationFactory,
            IEnumerable<OpcUaServerNodeManagerRegistration> registrations,
            IEnumerable<OpcUaServerIdentityAuthenticatorRegistration> identityRegistrations,
            IEnumerable<OpcUaServerIdentityAugmenterRegistration> augmenterRegistrations,
            IEnumerable<KeyCredentialPushSubject> keyCredentialPushSubjects,
            IServiceProvider services,
            ILogger<OpcUaServerHostedService> logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (roleOptions is null)
            {
                throw new ArgumentNullException(nameof(roleOptions));
            }
            m_options = options.Value ?? throw new ArgumentNullException(nameof(options));
            m_roleOptions = roleOptions.Value ?? throw new ArgumentNullException(nameof(roleOptions));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_applicationFactory = applicationFactory ?? throw new ArgumentNullException(nameof(applicationFactory));
            m_registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
            m_identityRegistrations = identityRegistrations ??
                throw new ArgumentNullException(nameof(identityRegistrations));
            m_augmenterRegistrations = augmenterRegistrations ??
                throw new ArgumentNullException(nameof(augmenterRegistrations));
            m_keyCredentialPushSubjects = keyCredentialPushSubjects ??
                throw new ArgumentNullException(nameof(keyCredentialPushSubjects));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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

            ArrayOf<CertificateIdentifier> certs =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subject, CertificateStoreType.Directory, pkiRoot);

            string[] urls = new string[m_options.EndpointUrls.Count];
            m_options.EndpointUrls.CopyTo(urls, 0);

            IApplicationConfigurationBuilderTransportQuotas quotasBuilder = m_application
                .Build(m_options.ApplicationUri, m_options.ProductUri)
                .SetMaxByteStringLength((int)m_options.MaxByteStringLength)
                .SetMaxArrayLength((int)m_options.MaxArrayLength);

            if (m_options.MaxMessageSize is int maxMessageSize)
            {
                quotasBuilder = quotasBuilder.SetMaxMessageSize(maxMessageSize);
            }
            if (m_options.OperationTimeoutMs is int operationTimeout)
            {
                quotasBuilder = quotasBuilder.SetOperationTimeout(operationTimeout);
            }

            IApplicationConfigurationBuilderServerSelected serverBuilder =
                quotasBuilder.AsServer(urls);

            if (m_options.IncludeSignAndEncryptPolicies)
            {
                serverBuilder = serverBuilder.AddSignAndEncryptPolicies();
            }

            if (m_options.IncludeEccPolicies)
            {
                serverBuilder = serverBuilder.AddEccSignAndEncryptPolicies();
            }

            if (m_options.IncludeUnsecurePolicyNone)
            {
                serverBuilder = serverBuilder.AddUnsecurePolicyNone();
            }

            if (m_options.UserTokenPolicies.Count == 0)
            {
                serverBuilder = serverBuilder.AddUserTokenPolicy(UserTokenType.Anonymous);
            }
            else
            {
                foreach (OpcUaUserTokenPolicy policy in m_options.UserTokenPolicies)
                {
                    serverBuilder = serverBuilder.AddUserTokenPolicy(policy.TokenType);
                }
            }

            IApplicationConfigurationBuilderServerOptions optionsBuilder =
                serverBuilder.SetDiagnosticsEnabled(m_options.DiagnosticsEnabled);

            if (m_options.OperationLimits is OperationLimitsOptions operationLimits)
            {
                optionsBuilder = optionsBuilder.SetOperationLimits(
                    operationLimits.ToOperationLimits());
            }

            if (m_options.ReverseConnect is ServerReverseConnectOptions reverseConnect)
            {
                optionsBuilder = optionsBuilder.SetReverseConnect(
                    ToReverseConnectConfiguration(reverseConnect));
            }

            if (!string.IsNullOrEmpty(m_options.RegistrationEndpointUrl))
            {
                optionsBuilder = optionsBuilder.SetRegistrationEndpoint(
                    new EndpointDescription { EndpointUrl = m_options.RegistrationEndpointUrl });
            }

            m_options.ConfigureBuilder?.Invoke(serverBuilder);

            IApplicationConfigurationBuilderSecurityOptions securityOptions = optionsBuilder
                .AddSecurityConfiguration(certs, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(m_options.AutoAcceptUntrustedCertificates)
                .SetRejectSHA1SignedCertificates(m_options.RejectSHA1Certificates);

            if (m_options.MinCertificateKeySize > 0)
            {
                securityOptions = securityOptions.SetMinimumCertificateKeySize(
                    m_options.MinCertificateKeySize);
            }

            await securityOptions
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

            m_server = new ConfiguredRoleStandardServer(m_telemetry, m_roleOptions);
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

            await m_application.StartAsync(m_server, stoppingToken).ConfigureAwait(false);
            await BindKeyCredentialPushAsync(stoppingToken).ConfigureAwait(false);
            RegisterIdentityAuthenticators();
            RegisterIdentityAugmenters();

            foreach (string url in urls)
            {
                m_logger.LogInformation("OPC UA server listening at {Endpoint}.", url);
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
                m_application?.ApplicationConfiguration?.CertificateManager as ICertificateValidatorEx;

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
                m_logger.LogInformation("Stopping OPC UA server...");
                try
                {
                    await m_application.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(ex, "Error while stopping OPC UA server.");
                }
            }
        }

        public override void Dispose()
        {
            m_server?.Dispose();
            base.Dispose();
        }

        private sealed class ConfiguredRoleStandardServer : StandardServer
        {
            private readonly RoleConfigurationOptions m_roleOptions;
            private bool m_roleManagerConfigured;

            public ConfiguredRoleStandardServer(
                ITelemetryContext telemetry,
                RoleConfigurationOptions roleOptions)
                : base(telemetry)
            {
                m_roleOptions = roleOptions ?? throw new ArgumentNullException(nameof(roleOptions));
            }

            protected override ResourceManager CreateResourceManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                if (!m_roleManagerConfigured)
                {
                    IRoleManager previous = server.RoleManager;
                    RoleManager? roleManager = new(m_roleOptions);
                    try
                    {
                        server.SetRoleManager(roleManager);
                        roleManager = null;
                    }
                    finally
                    {
                        roleManager?.Dispose();
                    }
                    (previous as IDisposable)?.Dispose();
                    m_roleManagerConfigured = true;
                }

                return base.CreateResourceManager(server, configuration);
            }
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
    }
}
