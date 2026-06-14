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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Client.TestFramework
{
    /// <summary>
    /// Client fixture for tests.
    /// </summary>
    public class ClientFixture : IDisposable
    {
        private const uint kDefaultOperationLimits = 5000;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private ApplicationInstance m_application;

        public ApplicationConfiguration Config { get; private set; }
        public ConfiguredEndpoint Endpoint { get; private set; }
        public string EndpointUrl { get; private set; }
        public string ReverseConnectUri { get; private set; }
        public ReverseConnectManager ReverseConnectManager { get; private set; }
        public uint SessionTimeout { get; set; } = 10000;
        public int OperationTimeout { get; set; } = 10000;

        public int TraceMasks { get; set; } =
            Utils.TraceMasks.Error |
            Utils.TraceMasks.StackTrace |
            Utils.TraceMasks.Security |
            Utils.TraceMasks.Information;

        public ISessionFactory SessionFactory { get; set; }
        public ActivityListener ActivityListener { get; private set; }

        /// <summary>
        /// Optional <see cref="Opc.Ua.Bindings.ITransportBindingRegistry"/>
        /// assigned to <see cref="ReverseConnectManager"/> immediately
        /// after construction so the listeners
        /// <see cref="StartReverseConnectHostAsync()"/> creates pick up
        /// the right factory for the URI scheme (e.g. Kestrel-TCP
        /// instead of the raw-socket TCP listener).
        /// </summary>
        public Opc.Ua.Bindings.ITransportBindingRegistry TransportBindingRegistry { get; set; }

        /// <summary>
        /// Subscription engine factory to inject into every session
        /// created via <see cref="SessionFactory"/>. <c>null</c> means
        /// the session uses the <see cref="DefaultSessionFactory"/>
        /// default (which is the V2
        /// <see cref="DefaultSubscriptionEngineFactory"/> after the
        /// flip in <c>Session.cs</c>). Test projects that rely on the
        /// classic subscription engine (e.g. classic
        /// <c>Session.AddSubscription</c> with classic
        /// <c>Subscription</c>) should set this to
        /// <c>ClassicSubscriptionEngineFactory.Instance</c> in their
        /// fixture setup.
        /// </summary>
        /// <remarks>
        /// Setting this property after <see cref="SessionFactory"/> is
        /// constructed has no effect on already-created sessions. To
        /// switch engines, set this property and then call
        /// <see cref="UseSubscriptionEngineFactory"/> to rebuild the
        /// factory.
        /// </remarks>
        public ISubscriptionEngineFactory SubscriptionEngineFactory { get; private set; }

        /// <summary>
        /// Configure the engine factory used by this fixture's
        /// <see cref="SessionFactory"/>. Replaces the current
        /// <see cref="SessionFactory"/> with a fresh
        /// <see cref="DefaultSessionFactory"/> that uses the supplied
        /// engine.
        /// </summary>
        public void UseSubscriptionEngineFactory(ISubscriptionEngineFactory engineFactory)
        {
            SubscriptionEngineFactory = engineFactory
                ?? throw new ArgumentNullException(nameof(engineFactory));
            SessionFactory = new DefaultSessionFactory(m_telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText,
                SubscriptionEngineFactory = engineFactory
            };
        }

        public ClientFixture(bool useTracing, bool disableActivityLogging, ITelemetryContext telemetry)
            : this(telemetry)
        {
            if (useTracing)
            {
                SessionFactory = new TraceableRequestHeaderClientSessionFactory(telemetry)
                {
                    SubscriptionEngineFactory = SubscriptionEngineFactory
                };
                StartActivityListenerInternal(disableActivityLogging);
            }
            else
            {
                SessionFactory = new DefaultSessionFactory(telemetry)
                {
                    ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText,
                    SubscriptionEngineFactory = SubscriptionEngineFactory
                };
            }
        }

        public ClientFixture(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ClientFixture>();
            SessionFactory = new DefaultSessionFactory(telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText,
                SubscriptionEngineFactory = SubscriptionEngineFactory
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopActivityListener();
                m_application?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                m_application = null;
            }
        }

        /// <summary>
        /// Load the default client configuration.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async Task LoadClientConfigurationAsync(
            string pkiRoot = null,
            string clientName = "TestClient")
        {
            m_application?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            m_application = new ApplicationInstance(m_telemetry) { ApplicationName = clientName };
            ApplicationInstance application = m_application;

            pkiRoot ??= Path.Combine("%LocalApplicationData%", "OPC", "pki");

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=" + clientName + ", O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    pkiRoot);

            // build the application configuration.
            Config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:" + clientName,
                    "http://opcfoundation.org/UA/" + clientName)
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .AsClient()
                .SetClientOperationLimits(
                    new OperationLimits
                    {
                        MaxNodesPerBrowse = kDefaultOperationLimits,
                        MaxNodesPerRead = kDefaultOperationLimits,
                        MaxMonitoredItemsPerCall = kDefaultOperationLimits,
                        MaxNodesPerWrite = kDefaultOperationLimits
                    })
                .AddSecurityConfiguration(applicationCerts, pkiRoot)
                // .SetApplicationCertificates(applicationCerts)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetOutputFilePath(Path.Combine(pkiRoot, "Logs", "Opc.Ua.Client.Tests.log.txt"))
                .CreateAsync()
                .ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await application
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new InvalidOperationException("Application instance certificate invalid!");
            }

            ReverseConnectManager = new ReverseConnectManager(m_telemetry)
            {
                TransportBindings = TransportBindingRegistry
            };
        }

        /// <summary>
        /// Start a host for reverse connections on random port using the
        /// default <c>opc.tcp://</c> scheme.
        /// </summary>
        public Task StartReverseConnectHostAsync()
            => StartReverseConnectHostAsync(Utils.UriSchemeOpcTcp);

        /// <summary>
        /// Start a host for reverse connections on random port using
        /// the specified URL scheme. Used by the WSS reverse-connect
        /// integration tests; the existing parameterless overload
        /// preserves the opc.tcp default.
        /// </summary>
        /// <param name="uriScheme">The URL scheme for the reverse-connect listener.</param>
        public async Task StartReverseConnectHostAsync(string uriScheme)
        {
            int testPort = ServerFixtureUtils.GetNextFreeIPPort();
            int serverStartRetries = 25;
            bool retryStartServer;
            do
            {
                retryStartServer = false;
                try
                {
                    var reverseConnectUri = new Uri($"{uriScheme}://localhost:{testPort}");
                    ReverseConnectManager.AddEndpoint(reverseConnectUri, Config);
                    ReverseConnectManager.StartService(Config);
                    ReverseConnectUri = reverseConnectUri.ToString();
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (serverStartRetries == 0 || sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }

                    testPort = UnsecureRandom.Shared.Next(
                        ServerFixtureUtils.MinTestPort,
                        ServerFixtureUtils.MaxTestPort);
                    retryStartServer = true;
                }

                await Task.Delay(UnsecureRandom.Shared.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ISession> ConnectAsync(string endpointUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(endpointUrl))
            {
                throw new ArgumentNullException(nameof(endpointUrl));
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException(
                    endpointUrl + " is not a valid URL.",
                    nameof(endpointUrl));
            }

            const int maxAttempts = 25;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    EndpointDescription endpointDescription =
                        await CoreClientUtils.SelectEndpointAsync(
                            Config,
                            endpointUrl,
                            true,
                            m_telemetry,
                            ct).ConfigureAwait(false);
                    var endpointConfiguration = EndpointConfiguration.Create(Config);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    return await ConnectAsync(endpoint).ConfigureAwait(false);
                }
                catch (ServiceResultException e) when (
                (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication
                ) &&
                attempt < maxAttempts)
                {
                    attempt++;
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying in 1 second...", attempt + 1);
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            }

            throw new ServiceResultException(StatusCodes.BadNoCommunication);
        }

        /// <summary>
        /// Connects the url endpoint with specified security profile.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ISession> ConnectAsync(
            Uri url,
            string securityProfile,
            ArrayOf<EndpointDescription> endpoints = default,
            IUserIdentity userIdentity = null)
        {
            string uri = url.AbsoluteUri;
            Uri getEndpointsUrl = url;
            if (uri.StartsWith(Utils.UriSchemeHttp, StringComparison.Ordinal) ||
                Utils.IsUriHttpsScheme(uri))
            {
                getEndpointsUrl = CoreClientUtils.GetDiscoveryUrl(uri);
            }

            const int maxAttempts = 25;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    ConfiguredEndpoint endpoint = await GetEndpointAsync(
                        getEndpointsUrl,
                        securityProfile,
                        endpoints
                    ).ConfigureAwait(false);
                    return await ConnectAsync(endpoint, userIdentity).ConfigureAwait(false);
                }
                catch (ServiceResultException e) when (IsPermanentConnectFailure(e.StatusCode.Code))
                {
                    // Permanent failure (bad credentials, bad cert, rejected
                    // security policy etc.). Retrying just floods the server
                    // and can lock out the account.
                    throw;
                }
                catch (ServiceResultException e) when (
                (
                    e.StatusCode == StatusCodes.BadServerHalted ||
                    e.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.StatusCode == StatusCodes.BadNoCommunication
                ) &&
                attempt < maxAttempts)
                {
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying in 1 second...", attempt + 1);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                catch (Exception e) when (
                    e is not IgnoreException &&
                    attempt < maxAttempts)
                {
                    // Retry on transient errors (e.g. HttpRequestException when HTTPS server is not yet ready)
                    m_logger.LogError(e, "Failed to connect {Attempt}. Retrying in 1 second...", attempt + 1);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            }

            throw new ServiceResultException(StatusCodes.BadNoCommunication);
        }

        /// <summary>
        /// True for service-result status codes that indicate a permanent
        /// connect failure that won't be resolved by retrying — namely
        /// authentication and certificate-validation errors. Retrying these
        /// would cause the test to spend 25 s flooding the server with bad
        /// auth attempts, which can trip the failed-auth lockout and
        /// poison later tests.
        /// </summary>
        private static bool IsPermanentConnectFailure(uint statusCode)
        {
            return statusCode == StatusCodes.BadIdentityTokenInvalid ||
                statusCode == StatusCodes.BadIdentityTokenRejected ||
                statusCode == StatusCodes.BadUserAccessDenied ||
                statusCode == StatusCodes.BadCertificateInvalid ||
                statusCode == StatusCodes.BadCertificateUntrusted ||
                statusCode == StatusCodes.BadCertificateTimeInvalid ||
                statusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                statusCode == StatusCodes.BadCertificateHostNameInvalid ||
                statusCode == StatusCodes.BadCertificateUriInvalid ||
                statusCode == StatusCodes.BadCertificateUseNotAllowed ||
                statusCode == StatusCodes.BadCertificateIssuerUseNotAllowed ||
                statusCode == StatusCodes.BadCertificateRevoked ||
                statusCode == StatusCodes.BadCertificateIssuerRevoked ||
                statusCode == StatusCodes.BadCertificateRevocationUnknown ||
                statusCode == StatusCodes.BadCertificateIssuerRevocationUnknown ||
                statusCode == StatusCodes.BadCertificatePolicyCheckFailed ||
                statusCode == StatusCodes.BadSecurityChecksFailed ||
                statusCode == StatusCodes.BadSecurityPolicyRejected ||
                statusCode == StatusCodes.BadSecurityModeRejected;
        }

        /// <summary>
        /// Connects the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The configured endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public async Task<ISession> ConnectAsync(
            ConfiguredEndpoint endpoint,
            IUserIdentity userIdentity = null)
        {
            endpoint ??= Endpoint ?? throw new ArgumentNullException(nameof(endpoint));

            ISession session = await SessionFactory
                .CreateAsync(
                    Config,
                    endpoint,
                    false,
                    false,
                    Config.ApplicationName,
                    SessionTimeout,
                    userIdentity,
                    default)
                .ConfigureAwait(false);

            Endpoint = session.ConfiguredEndpoint;

            session.KeepAlive += Session_KeepAlive;

            EndpointUrl = session.ConfiguredEndpoint.EndpointUrl.ToString();

            return session;
        }

        /// <summary>
        /// Create a channel using the specified endpoint.
        /// </summary>
        /// <param name="endpoint">The configured endpoint</param>
        public Task<ITransportChannel> CreateChannelAsync(
            ConfiguredEndpoint endpoint,
            bool updateBeforeConnect = true)
        {
            return SessionFactory.CreateChannelAsync(
                Config,
                null,
                endpoint,
                updateBeforeConnect,
                checkDomain: false);
        }

        /// <summary>
        /// Create a session using the specified channel.
        /// </summary>
        /// <param name="channel">The channel to use</param>
        /// <param name="endpoint">The configured endpoint</param>
        public ISession CreateSession(ITransportChannel channel, ConfiguredEndpoint endpoint)
        {
            return SessionFactory.Create(channel, Config, endpoint, null);
        }

        /// <summary>
        /// Get configured endpoint from url with security profile.
        /// </summary>
        public async Task<ConfiguredEndpoint> GetEndpointAsync(
            Uri url,
            string securityPolicy,
            ArrayOf<EndpointDescription> endpoints = default)
        {
            if (endpoints.IsNull)
            {
                endpoints = await GetEndpointsAsync(url).ConfigureAwait(false);
            }
            EndpointDescription endpointDescription = SelectEndpoint(
                Config,
                endpoints,
                url,
                securityPolicy);
            if (endpointDescription == null)
            {
                Assert.Ignore("The endpoint is not supported by the server.");
            }
            var endpointConfiguration = EndpointConfiguration.Create(Config);
            endpointConfiguration.OperationTimeout = OperationTimeout;
            return new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        }

        /// <summary>
        /// Select a security endpoint from description.
        /// </summary>
        public static EndpointDescription SelectEndpoint(
            ApplicationConfiguration configuration,
            ArrayOf<EndpointDescription> endpoints,
            Uri url,
            string securityPolicy)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox.
            foreach (EndpointDescription endpoint in endpoints)
            {
                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme, StringComparison.Ordinal))
                {
                    // skip unsupported security policies
                    if (!configuration.SecurityConfiguration.SupportedSecurityPolicies.Contains(
                            endpoint.SecurityPolicyUri))
                    {
                        continue;
                    }

                    // pick the first available endpoint by default.
                    if (selectedEndpoint == null &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri, StringComparison.Ordinal))
                    {
                        selectedEndpoint = endpoint;
                        continue;
                    }

                    if (selectedEndpoint?.SecurityMode < endpoint.SecurityMode &&
                        securityPolicy.Equals(endpoint.SecurityPolicyUri, StringComparison.Ordinal))
                    {
                        selectedEndpoint = endpoint;
                    }
                }
            }
            // return the selected endpoint.
            return selectedEndpoint;
        }

        /// <summary>
        /// Get endpoints from discovery endpoint.
        /// </summary>
        public async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync(
            Uri url,
            CancellationToken ct = default)
        {
            var endpointConfiguration = EndpointConfiguration.Create(Config);
            endpointConfiguration.OperationTimeout = OperationTimeout;

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                url,
                endpointConfiguration,
                m_telemetry,
                ct: ct).ConfigureAwait(false);
            client.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            ArrayOf<EndpointDescription> result = await client.GetEndpointsAsync(default, ct)
                .ConfigureAwait(false);
            await client.CloseAsync(ct).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Configures Activity Listener and registers with Activity Source.
        /// </summary>
        public void StartActivityListenerInternal(bool disableActivityLogging)
        {
            ActivitySource activitySource = m_telemetry.GetActivitySource();
            string expectedName = activitySource.Name;

            if (disableActivityLogging)
            {
                // Create an instance of ActivityListener without logging
                ActivityListener = new ActivityListener
                {
                    ShouldListenTo = (source) => source.Name == expectedName,

                    // Sample all data and recorded activities
                    Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                    // Do not log during benchmarks
                    ActivityStarted = _ => { },
                    ActivityStopped = _ => { }
                };
            }
            else
            {
                // Create an instance of ActivityListener and configure its properties with logging
                ActivityListener = new ActivityListener
                {
                    ShouldListenTo = (source) => source.Name == expectedName,

                    // Sample all data and recorded activities
                    Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity =>
                        m_logger.LogInformation(
                            "Client Started: {OperationName,-15} - TraceId: {TraceId,-32} SpanId: {SpanId,-16}",
                            activity.OperationName,
                            activity.TraceId,
                            activity.SpanId
                        ),
                    ActivityStopped = activity =>
                        m_logger.LogInformation(
                            "Client Stopped: {OperationName,-15} - TraceId: {TraceId,-32} SpanId: {SpanId,-16} Duration: {Duration}",
                            activity.OperationName,
                            activity.TraceId,
                            activity.SpanId,
                            activity.Duration)
                };
            }
            ActivitySource.AddActivityListener(ActivityListener);
        }

        /// <summary>
        /// Disposes Activity Listener and unregisters from Activity Source.
        /// </summary>
        public void StopActivityListener()
        {
            ActivityListener?.Dispose();
            ActivityListener = null;
        }

        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                // Ignore expected errors during test shutdown to reduce noise in CI logs
                if (e.Status?.StatusCode == StatusCodes.BadServerHalted ||
                    e.Status?.StatusCode == StatusCodes.BadConnectionClosed ||
                    e.Status?.StatusCode == StatusCodes.BadNoCommunication ||
                    e.Status?.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    e.Status?.StatusCode == StatusCodes.BadRequestInterrupted)
                {
                    return;
                }

                m_logger.LogError(
                    "Session '{SessionName}' keep alive error: {StatusCode}",
                    session.SessionName,
                    e.Status?.ToLongString());
            }
        }
    }
}
