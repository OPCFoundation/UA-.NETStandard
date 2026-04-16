using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Quickstarts;

namespace Quickstarts
{
    public partial class ClientSamples
    {
        private readonly Lock m_lock = new();
        private SessionReconnectHandler m_reconnectHandler;
        private ApplicationConfiguration m_configuration;
        private SessionWrapper m_wrapper;
        //private bool m_verbose = true;

        const string ServerUrl = "opc.tcp://localhost:62541";
        const string UserName = "sysadmin";
        const string Password = "demo";
        const bool supportsX509 = true;

        //const string ServerUrl = "opc.tcp://10.103.19.71:52520/OPCUA/SampleConsoleServer";
        //const string UserName = "opcua";
        //const string Password = "opcua";
        //static bool supportsX509 = false;

        //const string ServerUrl = "opc.tcp://10.103.141.179:4840";
        //const string UserName = "iop";
        //const string Password = "test";
        //static bool supportsX509 = false;

        //const string ServerUrl = "opc.tcp://78.46.151.116:4840";
        //const string UserName = "user1";
        //const string Password = "passsword";
        //static bool supportsX509 = false;

        //const string ServerUrl = "opc.tcp://10.103.119.62:4888/Softing/OpcUa/TestServer";
        //const string UserName = "usr";
        //const string Password = "pwd";
        //const bool supportsX509 = true;

        //static string TargetPolicy = SecurityPolicies.ECC_nistP256_AesGcm;

        //const int kMaxSearchDepth = 128;
        const int ReconnectPeriod = 1000;
        const int ReconnectPeriodExponentialBackoff = 15000;

        //public RunConnectAll(ApplicationConfiguration configuration, ITelemetryContext context)
        //{
        //    CryptoTrace.Enabled = true;

        //    m_telemetry = context;
        //    m_configuration = configuration;
        //    m_logger = context.CreateLogger("Test");

        //    m_reconnectHandler = new SessionReconnectHandler(
        //        context,
        //        true,
        //        ReconnectPeriodExponentialBackoff);
        //}


        public class SessionWrapper : IUAClient
        {
            public ISession Session { get; init; }
        }

        private string GetUserCertificateFile(string securityPolicyUri)
        {
            var securityPolicy = SecurityPolicies.GetInfo(securityPolicyUri);

            switch (securityPolicy.CertificateKeyAlgorithm)
            {
                default:
                case CertificateKeyAlgorithm.RSA:
                case CertificateKeyAlgorithm.RSADH:
                    return $"iama.tester.rsa.pfx";
                case CertificateKeyAlgorithm.BrainpoolP256r1:
                    return $"iama.tester.brainpoolP256r1.pfx";
                case CertificateKeyAlgorithm.BrainpoolP384r1:
                    return $"iama.tester.brainpoolP384r1.pfx";
                case CertificateKeyAlgorithm.NistP256:
                    return $"iama.tester.nistP256.pfx";
                case CertificateKeyAlgorithm.NistP384:
                    return $"iama.tester.nistP384.pfx";
            }
        }

        public async Task<bool> RunAsync(ManualResetEvent quitEvent, CancellationToken ct)
        {
            try
            {
                m_reconnectHandler = new SessionReconnectHandler(
                    m_telemetry,
                    true,
                    ReconnectPeriodExponentialBackoff);

                m_logger.LogInformation("OPC UA Security Test Client");
                CryptoTrace.Enabled = false;

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

                // load the application configuration.
                var configuration = m_configuration = await application
                    .LoadApplicationConfigurationAsync(silent: false, ct: ct)
                    .ConfigureAwait(false);

                m_configuration.CertificateValidator.CertificateValidation += CertificateValidation;

                // check the application certificate.
                bool haveAppCertificate = await application
                    .CheckApplicationInstanceCertificatesAsync(false, ct: ct)
                    .ConfigureAwait(false);

                if (!haveAppCertificate)
                {
                    throw new InvalidOperationException("Application instance certificate invalid!");
                }

                m_logger.LogInformation("Connecting to... {ServerUrl}", ServerUrl);

                var endpoints = await GetEndpoints(
                    m_configuration,
                    ServerUrl,
                    ct).ConfigureAwait(false);

                //endpoints = endpoints.Where(x => x.SecurityPolicyUri == TargetPolicy).ToList();

                var endpointConfiguration = EndpointConfiguration.Create(m_configuration);
                var sessionFactory = new DefaultSessionFactory(m_telemetry);
                var userNameidentity = new UserIdentity(UserName, new UTF8Encoding(false).GetBytes(Password));

                foreach (var ii in endpoints.ToArray())
                {
                    var userCertificateFile = GetUserCertificateFile(ii.SecurityPolicyUri);

                    var x509 = X509CertificateLoader.LoadPkcs12FromFile(
                        Path.Combine("..\\..\\pki\\trustedUser\\private",
                        userCertificateFile),
                        "password");

                    var thumbprint = x509.Thumbprint;

                    var certificateIdentity = await LoadUserCertificateAsync(thumbprint, "password", ct).ConfigureAwait(false);

                    var identities = new List<UserIdentity>
                    {
                       new UserIdentity()
                    };

                    if (!String.IsNullOrEmpty(UserName))
                    { identities.Add(userNameidentity); }
                    if (supportsX509)
                    { identities.Add(certificateIdentity); }

                    foreach (var identity in identities)
                    {
                        try
                        {
                            m_logger.LogWarning("{Line}", new string('=', 80));

                            m_logger.LogWarning(
                                "SECURITY-POLICY={SecurityPolicyUri} {SecurityMode}",
                                SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri),
                                ii.SecurityMode);

                            m_logger.LogWarning(
                                "IDENTITY={DisplayName} {TokenType}",
                                identity.DisplayName,
                                identity.TokenType);

                            var wrapper = m_wrapper = await RunTestAsync(
                                endpointConfiguration,
                                sessionFactory,
                                ii,
                                identity,
                                ct).ConfigureAwait(false);

                            m_logger.LogWarning("Waiting for SecureChannel renew");
                            await wrapper.Session.UpdateSessionAsync(identity, default, ct).ConfigureAwait(false);

                            for (int count = 0; count < 1; count++)
                            {
                                var result = await wrapper.Session.ReadAsync(
                                    null,
                                    0,
                                    TimestampsToReturn.Neither,
                                    new List<ReadValueId>()
                                    {
                                        new ReadValueId()
                                        {
                                            NodeId = Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime,
                                            AttributeId = Attributes.Value
                                        }
                                    },
                                    ct).ConfigureAwait(false);

                                m_logger.LogWarning(
                                    "CurrentTime: {CurrentTime}",
                                    result.Results[0].WrappedValue.ToString());

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
                                SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri),
                                ii.SecurityMode);

                            m_logger.LogWarning(
                                "IDENTITY={DisplayName} {TokenType}",
                                identity.DisplayName,
                                identity.TokenType);

                            m_logger.LogWarning("{Line}", new string('=', 80));
                        }

                        m_logger.LogWarning(
                            "TEST COMPLETE: {SecurityPolicyUri} {SecurityMode}",
                            SecurityPolicies.GetDisplayName(ii.SecurityPolicyUri),
                            ii.SecurityMode);

                        m_logger.LogWarning("{Line}", new string('=', 80));
                        //break;
                    }

                    //break;
                }

                Console.WriteLine("Ctrl-C to stop.");
                quitEvent.WaitOne();
            }
            catch (Exception e)
            {
                m_logger.LogError("Exception: {Message}", e.Message);
                m_logger.LogTrace("StackTrace: {StackTrace}", e.StackTrace);
            }

            return true;
        }

        private async Task<UserIdentity> LoadUserCertificateAsync(string thumbprint, string password, CancellationToken ct)
        {
#if NET8_0_OR_GREATER
            var store = m_configuration.SecurityConfiguration.TrustedUserCertificates;

            // get user certificate with matching thumbprint
            var hit = (
                await store.GetCertificatesAsync(m_telemetry, ct).ConfigureAwait(false)
            ).Find(X509FindType.FindByThumbprint, thumbprint, false).FirstOrDefault();

            // create Certificate Identifier
            var cid = new CertificateIdentifier(hit)
            {
                StorePath = store.StorePath,
                StoreType = store.StoreType
            };

            return await UserIdentity.CreateAsync(
                cid,
                new CertificatePasswordProvider(new UTF8Encoding(false).GetBytes(password)),
                m_telemetry,
                ct
            ).ConfigureAwait(false);
#else
            await Task.Delay(1, ct).ConfigureAwait(false);
            throw new NotSupportedException("User certificate identity is only supported on .NET 8 or greater.");
#endif
        }

        public async Task<SessionWrapper> RunTestAsync(
            EndpointConfiguration endpointConfiguration,
            DefaultSessionFactory sessionFactory,
            EndpointDescription endpointDescription,
            UserIdentity identity,
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
                    m_configuration.ApplicationName,
                    600000,
                    //new UserIdentity(),
                    (endpointDescription.SecurityMode != MessageSecurityMode.None) ? identity : new UserIdentity(),
                    default,
                    ct
                )
                .ConfigureAwait(false);

            var wrapper = m_wrapper = new SessionWrapper() { Session = isession };

            // Assign the created session
            if (!wrapper.Session.Connected)
            {
                throw new InvalidOperationException("Could not connect to server at " + ServerUrl);
            }

            wrapper.Session.KeepAliveInterval = 10000;
            wrapper.Session.KeepAlive += Session_KeepAlive;

            var nodes = await BrowseFullAddressSpaceAsync(
                wrapper,
                ObjectIds.ObjectsFolder,
                null,
                ct).ConfigureAwait(false);

            return wrapper;
        }

        private async ValueTask<ArrayOf<EndpointDescription>> GetEndpoints(
            ApplicationConfiguration application,
            string discoveryUrl,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(application);

            var client = await DiscoveryClient.CreateAsync(
                application,
                new Uri(discoveryUrl),
                endpointConfiguration,
                ct: ct).ConfigureAwait(false);

            return await client.GetEndpointsAsync(default, ct).ConfigureAwait(false);
        }

        private void CertificateValidation(
            CertificateValidator sender,
            CertificateValidationEventArgs e)
        {
            bool certificateAccepted = false;

            // ****
            // Implement a custom logic to decide if the certificate should be
            // accepted or not and set certificateAccepted flag accordingly.
            // The certificate can be retrieved from the e.Certificate field
            // ***

            ServiceResult error = e.Error;
            m_logger.LogInformation("{ServiceResult}", error);
            if (error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                certificateAccepted = true;
            }

            if (certificateAccepted)
            {
                m_logger.LogInformation(
                    "Untrusted Certificate accepted. Subject = {Subject}",
                    e.Certificate.Subject);
                e.Accept = true;
            }
            else
            {
                m_logger.LogInformation(
                    "Untrusted Certificate rejected. Subject = {Subject}",
                    e.Certificate.Subject);
            }
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
                    SessionReconnectHandler.ReconnectState state = m_reconnectHandler
                        .BeginReconnect(
                            m_wrapper.Session,
                            null,
                            ReconnectPeriod,
                            Client_ReconnectComplete
                            );
                    if (state == SessionReconnectHandler.ReconnectState.Triggered)
                    {
                        m_logger.LogInformation(
                            "KeepAlive status {StatusCode}, reconnect status {State}, reconnect period {ReconnectPeriod}ms.",
                            e.Status,
                            state,
                            ReconnectPeriod
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

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            // ignore callbacks from discarded objects.
            if (!ReferenceEquals(sender, m_reconnectHandler))
            {
                return;
            }

            lock (m_lock)
            {
                // if session recovered, Session property is null
                if (m_reconnectHandler.Session != null)
                {
                    // ensure only a new instance is disposed
                    // after reactivate, the same session instance may be returned
                    if (!ReferenceEquals(m_wrapper.Session, m_reconnectHandler.Session))
                    {
                        m_logger.LogInformation(
                            "--- RECONNECTED TO NEW SESSION --- {SessionId}",
                            m_reconnectHandler.Session.SessionId
                        );
                        ISession session = m_wrapper.Session;
                        m_wrapper = new SessionWrapper() { Session = m_reconnectHandler.Session };
                        Utils.SilentDispose(session);
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
    }
}
