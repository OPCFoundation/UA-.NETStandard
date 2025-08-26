/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client fixture for tests.
    /// </summary>
    public class ClientFixture : IDisposable
    {
        private const uint kDefaultOperationLimits = 5000;
        private NUnitTestLogger<ClientFixture> m_traceLogger;
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

        public ISessionFactory SessionFactory { get; set; } = DefaultSessionFactory.Instance;
        public ActivityListener ActivityListener { get; private set; }

        public ClientFixture(bool useTracing, bool disableActivityLogging)
        {
            if (useTracing)
            {
                SessionFactory = TraceableRequestHeaderClientSessionFactory.Instance;
                StartActivityListenerInternal(disableActivityLogging);
            }
            else
            {
                SessionFactory = TraceableSessionFactory.Instance;
            }
        }

        public ClientFixture()
        {
            SessionFactory = DefaultSessionFactory.Instance;
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
            }
        }

        /// <summary>
        /// Load the default client configuration.
        /// </summary>
        public async Task LoadClientConfigurationAsync(
            string pkiRoot = null,
            string clientName = "TestClient")
        {
            var application = new ApplicationInstance { ApplicationName = clientName };

            pkiRoot ??= Path.Combine("%LocalApplicationData%", "OPC", "pki");

            CertificateIdentifierCollection applicationCerts =
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
                .SetTraceMasks(TraceMasks)
                .Create()
                .ConfigureAwait(false);

            // check the application certificate.
            bool haveAppCertificate = await application
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            ReverseConnectManager = new ReverseConnectManager();
        }

        /// <summary>
        /// Start a host for reverse connections on random port.
        /// </summary>
        public async Task StartReverseConnectHostAsync()
        {
            var random = new Random();
            int testPort = ServerFixtureUtils.GetNextFreeIPPort();
            bool retryStartServer = false;
            int serverStartRetries = 25;
            do
            {
                try
                {
                    var reverseConnectUri = new Uri("opc.tcp://localhost:" + testPort);
                    ReverseConnectManager.AddEndpoint(reverseConnectUri);
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
                    testPort = random.Next(
                        ServerFixtureUtils.MinTestPort,
                        ServerFixtureUtils.MaxTestPort);
                    retryStartServer = true;
                }
                await Task.Delay(random.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <param name="ct"">Cancellation token to cancel operation with</param>
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

            bool serverHalted;
            do
            {
                try
                {
                    EndpointDescription endpointDescription =
                        await CoreClientUtils.SelectEndpointAsync(
                            Config,
                            endpointUrl,
                            true,
                            ct).ConfigureAwait(false);
                    var endpointConfiguration = EndpointConfiguration.Create(Config);
                    var endpoint = new ConfiguredEndpoint(
                        null,
                        endpointDescription,
                        endpointConfiguration);

                    return await ConnectAsync(endpoint).ConfigureAwait(false);
                }
                catch (ServiceResultException e) when (e.StatusCode == StatusCodes.BadServerHalted)
                {
                    serverHalted = true;
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
            } while (serverHalted);

            throw new ServiceResultException(StatusCodes.BadNoCommunication);
        }

        /// <summary>
        /// Connects the url endpoint with specified security profile.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async Task<ISession> ConnectAsync(
            Uri url,
            string securityProfile,
            EndpointDescriptionCollection endpoints = null,
            IUserIdentity userIdentity = null)
        {
            string uri = url.AbsoluteUri;
            Uri getEndpointsUrl = url;
            if (uri.StartsWith(Utils.UriSchemeHttp, StringComparison.Ordinal) ||
                Utils.IsUriHttpsScheme(uri))
            {
                getEndpointsUrl = CoreClientUtils.GetDiscoveryUrl(uri);
            }

            bool serverHalted;
            do
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
                catch (ServiceResultException e) when (e.StatusCode == StatusCodes.BadServerHalted)
                {
                    serverHalted = true;
                    await Task.Delay(1000).ConfigureAwait(false);
                }
            } while (serverHalted);

            throw new ServiceResultException(StatusCodes.BadNoCommunication);
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
            if (endpoint == null)
            {
                endpoint = Endpoint;
                if (endpoint == null)
                {
                    throw new ArgumentNullException(nameof(endpoint));
                }
            }

            ISession session = await SessionFactory
                .CreateAsync(
                    Config,
                    endpoint,
                    false,
                    false,
                    Config.ApplicationName,
                    SessionTimeout,
                    userIdentity,
                    null)
                .ConfigureAwait(false);

            Endpoint = session.ConfiguredEndpoint;

            session.KeepAlive += Session_KeepAlive;

            session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
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
            return SessionFactory.Create(Config, channel, endpoint, null);
        }

        /// <summary>
        /// Get configured endpoint from url with security profile.
        /// </summary>
        public async Task<ConfiguredEndpoint> GetEndpointAsync(
            Uri url,
            string securityPolicy,
            EndpointDescriptionCollection endpoints = null)
        {
            endpoints ??= await GetEndpointsAsync(url).ConfigureAwait(false);
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
            EndpointDescriptionCollection endpoints,
            Uri url,
            string securityPolicy)
        {
            EndpointDescription selectedEndpoint = null;

            // select the best endpoint to use based on the selected URL and the UseSecurity checkbox.
            foreach (EndpointDescription endpoint in endpoints)
            {
                // check for a match on the URL scheme.
                if (endpoint.EndpointUrl.StartsWith(url.Scheme))
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
        /// <param name="url">The url of the discovery endpoint.</param>
        public async Task<EndpointDescriptionCollection> GetEndpointsAsync(Uri url)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = OperationTimeout;

            using var client = DiscoveryClient.Create(url, endpointConfiguration);
            EndpointDescriptionCollection result = await client.GetEndpointsAsync(null)
                .ConfigureAwait(false);
            await client.CloseAsync().ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Connect the nunit writer with the logger.
        /// </summary>
        public void SetTraceOutput(TextWriter writer)
        {
            if (m_traceLogger == null)
            {
                m_traceLogger = NUnitTestLogger<ClientFixture>.Create(writer);
            }
            else
            {
                m_traceLogger.SetWriter(writer);
            }
        }

        /// <summary>
        /// Adjust the Log level for the tracer
        /// </summary>
        public void SetTraceOutputLevel(LogLevel logLevel = LogLevel.Debug)
        {
            if (m_traceLogger != null)
            {
                m_traceLogger.MinimumLogLevel = logLevel;
            }
        }

        /// <summary>
        /// Configures Activity Listener and registers with Activity Source.
        /// </summary>
        public void StartActivityListenerInternal(bool disableActivityLogging)
        {
            if (disableActivityLogging)
            {
                // Create an instance of ActivityListener without logging
                ActivityListener = new ActivityListener
                {
                    ShouldListenTo = (source) => source.Name == TraceableSession.ActivitySourceName,

                    // Sample all data and recorded activities
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                        ActivitySamplingResult.AllDataAndRecorded,
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
                    ShouldListenTo = (source) => source.Name == TraceableSession.ActivitySourceName,

                    // Sample all data and recorded activities
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                        ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity =>
                        Utils.LogInfo(
                            "Client Started: {0,-15} - TraceId: {1,-32} SpanId: {2,-16}",
                            activity.OperationName,
                            activity.TraceId,
                            activity.SpanId
                        ),
                    ActivityStopped = activity =>
                        Utils.LogInfo(
                            "Client Stopped: {0,-15} - TraceId: {1,-32} SpanId: {2,-16} Duration: {3}",
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
                Utils.LogError(
                    "Session '{0}' keep alive error: {1}",
                    session.SessionName,
                    e.Status);
            }
        }
    }
}
