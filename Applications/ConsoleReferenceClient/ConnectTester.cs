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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace Quickstarts
{
    /// <summary>
    /// Wraps connect testing functionality
    /// </summary>
    public sealed class ConnectTester : IAsyncDisposable
    {
        public ConnectTester(
            ITelemetryContext telemetry,
            ManualResetEvent? quitEvent = null)
        {
            m_quitEvent = quitEvent;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ConnectTester>();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_reconnectHandler?.Dispose();
            ISession? session = m_wrapper?.Session;
            if (session != null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Run the tests until cancelled or quit
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task RunAsync(CancellationToken ct)
        {
            m_reconnectHandler = new SessionReconnectHandler(
                m_telemetry,
                true,
                s_settings.ReconnectPeriodExponentialBackoff);

            m_logger.LogInformation("OPC UA Security Test Client");

            // The application name and config file names
            const string applicationName = "ConsoleReferenceClient";
            const string configSectionName = "Quickstarts.ReferenceClient";

            // Define the UA Client application
            var passwordProvider = new CertificatePasswordProvider([]);

            var application = new ApplicationInstance(m_telemetry)
            {
                ApplicationName = applicationName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = configSectionName,
                CertificatePasswordProvider = passwordProvider
            };

            try
            {
                application.ConfigureAwait(false);

                // load the application configuration.
                m_configuration = await application
                    .LoadApplicationConfigurationAsync(silent: false, ct: ct)
                    .ConfigureAwait(false);

                m_configuration.CertificateManager.AcceptError = AcceptCertificate;

                // check the application certificate.
                bool haveAppCertificate = await application
                    .CheckApplicationInstanceCertificatesAsync(false, ct: ct)
                    .ConfigureAwait(false);

                if (!haveAppCertificate)
                {
                    throw new InvalidOperationException("Application instance certificate invalid!");
                }

                m_logger.LogInformation("Connecting to... {ServerUrl}", s_settings.ServerUrl);

                ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync(
                    m_configuration,
                    s_settings.ServerUrl,
                    ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(s_settings.SecurityPolicyFilter))
                {
                    endpoints = endpoints
                        .Filter(x => x != null &&
                        (x.SecurityPolicyUri?.Contains(s_settings.SecurityPolicyFilter, StringComparison.OrdinalIgnoreCase) ?? false)
                    );
                }

                endpoints = endpoints
                    .Filter(x => x != null &&
                    (SecurityPolicies.GetInfo(x.SecurityPolicyUri!) != null)
                );

                if (endpoints.IsEmpty)
                {
                    throw new InvalidOperationException("No endpoints selected!");
                }

                var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                var sessionFactory = new DefaultSessionFactory(m_telemetry);
                var userNameProvider = await CreateUserNameProviderAsync(ct).ConfigureAwait(false);

                foreach (EndpointDescription ii in endpoints.ToArray()!)
                {
                    string securityPolicyUri = ii.SecurityPolicyUri!;

                    var identityProviders = new List<IClientIdentityProvider>
                    {
                        new AnonymousIdentityProvider()
                    };

                    if (!string.IsNullOrEmpty(s_settings.UserName))
                    {
                        identityProviders.Add(userNameProvider);
                    }

                    if (s_settings.SupportsX509)
                    {
                        string userCertificateFile = GetUserCertificateFile(securityPolicyUri, ii.UserIdentityTokens);

                        if (!string.IsNullOrEmpty(userCertificateFile))
                        {
                            X509Certificate2 x509 = X509CertificateLoader.LoadPkcs12FromFile(
                                Path.Combine(s_settings.UserCertificatePath, userCertificateFile),
                                s_settings.UserCertificatePassword);

                            string thumbprint = x509.Thumbprint!;

                            IClientIdentityProvider certificateProvider = await LoadUserCertificateProviderAsync(
                                thumbprint,
                                s_settings.UserCertificatePassword,
                                ct).ConfigureAwait(false);

                            identityProviders.Add(certificateProvider);
                        }
                    }

                    foreach (IClientIdentityProvider identityProvider in identityProviders)
                    {
                        IUserIdentity identity = await CreateUserIdentityAsync(
                            identityProvider,
                            ii,
                            ct).ConfigureAwait(false);

                        try
                        {
                            m_logger.LogWarning("{Line}", new string('=', 80));

                            m_logger.LogWarning(
                                "SECURITY-POLICY={SecurityPolicyUri} {SecurityMode}",
                                SecurityPolicies.GetDisplayName(securityPolicyUri),
                                ii.SecurityMode);

                            m_logger.LogWarning(
                                "IDENTITY={DisplayName} {TokenType}",
                                identity.DisplayName,
                                identity.TokenType);

                            SessionWrapper wrapper = m_wrapper = await RunTestAsync(
                                endpointConfiguration,
                                sessionFactory,
                                ii,
                                identity,
                                ct).ConfigureAwait(false);

                            m_logger.LogWarning("Waiting for SecureChannel renew");
                            await wrapper.Session.UpdateSessionAsync(identity, default, ct).ConfigureAwait(false);

                            for (int count = 0; count < 10; count++)
                            {
                                ReadResponse result = await wrapper.Session.ReadAsync(
                                    null,
                                    0,
                                    TimestampsToReturn.Neither,
                                    new List<ReadValueId>
                                    {
                                        new() {
                                            NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                                            AttributeId = Attributes.Value
                                        }
                                    },
                                    ct).ConfigureAwait(false);

                                m_logger.LogWarning(
                                    "CurrentTime: {CurrentTime}",
                                    result.Results[0].WrappedValue.GetDateTime());

                                await Task.Delay(5000, ct).ConfigureAwait(false);
                            }

                            await wrapper.Session.UpdateSessionAsync(identity, default, ct).ConfigureAwait(false);

                            await wrapper.Session.CloseAsync(true, ct: ct).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception: {0}", e.Message);
                            Console.WriteLine("StackTrace: {0}", e.StackTrace);

                            m_logger.LogWarning(
                                "SECURITY-POLICY={SecurityPolicyUri} {SecurityMode}",
                                SecurityPolicies.GetDisplayName(securityPolicyUri),
                                ii.SecurityMode);

                            m_logger.LogWarning(
                                "IDENTITY={DisplayName} {TokenType}",
                                identity.DisplayName,
                                identity.TokenType);

                            m_logger.LogWarning("{Line}", new string('=', 80));
                        }

                        m_logger.LogWarning(
                            "TEST COMPLETE: {SecurityPolicyUri} {SecurityMode}",
                            SecurityPolicies.GetDisplayName(securityPolicyUri),
                            ii.SecurityMode);

                        m_logger.LogWarning("{Line}", new string('=', 80));
                    }
                }

                Console.WriteLine("Ctrl-C to stop.");
                m_quitEvent!.WaitOne();
            }
            catch (Exception e)
            {
                m_logger.LogError("Exception: {Message}", e.Message);
                m_logger.LogTrace("StackTrace: {StackTrace}", e.StackTrace);
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal async Task<SessionWrapper> RunTestAsync(
            EndpointConfiguration endpointConfiguration,
            DefaultSessionFactory sessionFactory,
            EndpointDescription endpointDescription,
            IUserIdentity identity,
            CancellationToken ct)
        {
            var endpoint = new ConfiguredEndpoint(
                null,
                endpointDescription,
                endpointConfiguration);

            // Create the session
            ISession isession = await sessionFactory
                .CreateAsync(
                    m_configuration,
                    endpoint,
                    false,
                    false,
                    m_configuration.ApplicationName!,
                    600000,
                    endpointDescription.SecurityMode != MessageSecurityMode.None
                        ? identity
                        : await CreateUserIdentityAsync(
                            new AnonymousIdentityProvider(),
                            endpointDescription,
                            ct).ConfigureAwait(false),
                    default,
                    ct
                )
                .ConfigureAwait(false);
            bool ownsSession = true;
            try
            {
                SessionWrapper wrapper = m_wrapper = new SessionWrapper { Session = isession };
                ownsSession = false;

                // Assign the created session
                if (!wrapper.Session.Connected)
                {
                    throw new InvalidOperationException("Could not connect to server at " + s_settings.ServerUrl);
                }

                wrapper.Session.KeepAliveInterval = 10000;
                wrapper.Session.KeepAlive += Session_KeepAlive;

                var samples = new ClientSamples(m_telemetry, null, m_quitEvent);
                ArrayOf<ReferenceDescription> nodes = await samples.BrowseFullAddressSpaceAsync(
                    wrapper,
                    ObjectIds.ObjectsFolder,
                    null,
                    ct).ConfigureAwait(false);

                return wrapper;
            }
            finally
            {
                if (ownsSession)
                {
                    await isession.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask<UserNamePasswordIdentityProvider> CreateUserNameProviderAsync(
            CancellationToken ct)
        {
            var passwordStore = new InMemorySecretStore();
            var passwordId = new SecretIdentifier(
                "connect-tester-password",
                passwordStore.StoreType);
            await passwordStore
                .SetAsync(passwordId, new UTF8Encoding(false).GetBytes(s_settings.Password), ct)
                .ConfigureAwait(false);
            return new UserNamePasswordIdentityProvider(
                s_settings.UserName,
                new SecretRegistry(passwordStore),
                passwordId);
        }

        private ValueTask<IUserIdentity> CreateUserIdentityAsync(
            IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            CancellationToken ct)
        {
            return provider.AcquireIdentityAsync(
                endpointDescription,
                m_configuration.CreateMessageContext(),
                ct);
        }

        private async Task<IClientIdentityProvider>
            LoadUserCertificateProviderAsync(
            string thumbprint,
            string password,
            CancellationToken ct)
        {
            CertificateTrustList store = m_configuration.SecurityConfiguration.TrustedUserCertificates!;
#if NET8_0_OR_GREATER
            // get user certificate with matching thumbprint
            using CertificateCollection certificates =
                await store.GetCertificatesAsync(m_telemetry, ct).ConfigureAwait(false);
            using Certificate hit = certificates
                .Find(X509FindType.FindByThumbprint, thumbprint, false)
                .FirstOrDefault()!;

            // create Certificate Identifier
            var cid = new CertificateIdentifier
            {
                Thumbprint = hit.Thumbprint,
                SubjectName = hit.Subject,
                StorePath = store.StorePath,
                StoreType = store.StoreType
            };

            return new X509ClientIdentityProvider(
                cid,
                new CertificatePasswordProvider(new UTF8Encoding(false).GetBytes(password)),
                m_configuration.CertificateManager.CertificateProvider);
#else
            throw new NotSupportedException("User certificate identity requires net8.0 or greater.");
#endif
        }

        private static async ValueTask<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            ApplicationConfiguration application,
            string discoveryUrl,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(application);

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                application,
                new Uri(discoveryUrl),
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
        }

        private bool AcceptCertificate(Certificate certificate, ServiceResult error)
        {
            // ****
            // Implement a custom logic to decide if the certificate should be
            // accepted. Return true to accept, false to reject.
            // ***
            m_logger.LogInformation("{ServiceResult}", error);
            bool certificateAccepted = error.StatusCode == StatusCodes.BadCertificateUntrusted;

            if (certificateAccepted)
            {
                m_logger.LogInformation(
                    "Untrusted Certificate accepted. Subject = {Subject}",
                    certificate.Subject);
            }
            else
            {
                m_logger.LogInformation(
                    "Untrusted Certificate rejected. Subject = {Subject}",
                    certificate.Subject);
            }

            return certificateAccepted;
        }

        /// <summary>
        /// Handles a keep alive event from a session and triggers a reconnect if necessary.
        /// </summary>
        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                // check for events from discarded sessions.
                if (m_wrapper == null || !m_wrapper.Session.Equals(session))
                {
                    return;
                }

                // start reconnect sequence on communication error.
                if (ServiceResult.IsBad(e.Status))
                {
                    SessionReconnectHandler.ReconnectState state = m_reconnectHandler!
                        .BeginReconnect(
                            m_wrapper.Session,
                            null,
                            s_settings.ReconnectPeriod,
                            Client_ReconnectComplete
                            );
                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {StatusCode}, reconnect status {State}, reconnect period {ReconnectPeriod}ms.",
                            e.Status,
                            state,
                            s_settings.ReconnectPeriod
                        );
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {StatusCode}, reconnect status {State}.",
                            e.Status,
                            state);
                    }

                    // cancel sending a new keep alive request, because reconnect is triggered.
                    e.CancelKeepAlive = true;
                }
            }
            catch (Exception exception)
            {
                m_logger.LogError(exception, "Error in OnKeepAlive.");
            }
        }

        private void Client_ReconnectComplete(object? sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!ReferenceEquals(sender, m_reconnectHandler))
            {
                return;
            }

            lock (m_lock)
            {
                // if session recovered, Session property is null
                if (m_reconnectHandler!.Session != null)
                {
                    // ensure only a new instance is disposed
                    // after reactivate, the same session instance may be returned
                    if (!ReferenceEquals(m_wrapper!.Session, m_reconnectHandler.Session))
                    {
                        m_logger.LogInformation(
                            "--- RECONNECTED TO NEW SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = m_wrapper.Session;
                        m_wrapper = new SessionWrapper { Session = m_reconnectHandler.Session };
                        session?.Dispose();
                    }
                    else
                    {
                        m_logger.LogInformation(
                            "--- REACTIVATED SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId);
                    }
                }
                else
                {
                    m_logger.LogInformation("--- RECONNECT KeepAlive recovered ---");
                }
            }
        }

        private static string GetUserCertificateFile(string securityPolicyUri, ArrayOf<UserTokenPolicy> userTokenPolicies)
        {
            // GetInfo returns null only for null/empty URI; caller passes a non-empty value.
            SecurityPolicyInfo securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri)!;

            if (securityPolicy == null)
            {
                return string.Empty;
            }

            var policies = new List<UserTokenPolicy>(userTokenPolicies.ToArray()!);

            if (!policies.Any(x => x?.SecurityPolicyUri == securityPolicyUri))
            {
                foreach (var policy in policies)
                {
                    var tokenSecurityPolicy = SecurityPolicies.GetInfo(policy.SecurityPolicyUri!);

                    if (tokenSecurityPolicy != null &&
                        tokenSecurityPolicy.CertificateKeyFamily == securityPolicy.CertificateKeyFamily &&
                        tokenSecurityPolicy.SecureChannelEnhancements == securityPolicy.SecureChannelEnhancements)
                    {
                        securityPolicy = tokenSecurityPolicy;
                        break;
                    }
                }
            }

            switch (securityPolicy.CertificateKeyAlgorithm)
            {
                case CertificateKeyAlgorithm.BrainpoolP256r1:
                    return "iama.tester.brainpoolP256r1.pfx";
                case CertificateKeyAlgorithm.BrainpoolP384r1:
                    return "iama.tester.brainpoolP384r1.pfx";
                case CertificateKeyAlgorithm.NistP256:
                    return "iama.tester.nistP256.pfx";
                case CertificateKeyAlgorithm.NistP384:
                    return "iama.tester.nistP384.pfx";
                default:
                    return "iama.tester.rsa.pfx";
            }
        }

        internal sealed class SessionWrapper : IUAClient
        {
            public required ISession Session { get; init; }
        }

        private readonly Lock m_lock = new();
        private SessionReconnectHandler? m_reconnectHandler;
        private ApplicationConfiguration m_configuration = null!;
        private SessionWrapper? m_wrapper;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
        private readonly ManualResetEvent? m_quitEvent;

        // Defaults are baked in; an optional external "ConnectTester.Settings.json"
        // next to the executable can override any field. The path of that file can be
        // changed with the REFCLIENT_CONNECTTESTER_SETTINGS_FILE environment variable.
        private static readonly ConnectTesterSettings s_settings = ConnectTesterSettings.Load();
    }
}
