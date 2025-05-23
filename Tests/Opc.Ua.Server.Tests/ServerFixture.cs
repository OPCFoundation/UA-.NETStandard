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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Server fixture for testing.
    /// </summary>
    /// <typeparam name="T">A server class T used for testing.</typeparam>
    public class ServerFixture<T> where T : ServerBase, new()
    {
        private NUnitTestLogger<T> m_traceLogger;
        public ApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public T Server { get; private set; }
        public bool LogConsole { get; set; }
        public bool AutoAccept { get; set; }
        public bool OperationLimits { get; set; }
        public int MaxChannelCount { get; set; } = 10;
        public int ReverseConnectTimeout { get; set; }
        public bool AllNodeManagers { get; set; }
        public int TraceMasks { get; set; } = Utils.TraceMasks.Error | Utils.TraceMasks.StackTrace | Utils.TraceMasks.Security | Utils.TraceMasks.Information;
        public bool SecurityNone { get; set; } = false;
        public string UriScheme { get; set; } = Utils.UriSchemeOpcTcp;
        public int Port { get; private set; }
        public bool UseTracing { get; set; }
        public bool DurableSubscriptionsEnabled { get; set; } = false;
        public ActivityListener ActivityListener { get; private set; }

        public ServerFixture(bool useTracing, bool disableActivityLogging)
        {
            UseTracing = useTracing;
            if (UseTracing)
            {
                StartActivityListenerInternal(disableActivityLogging);
            }
        }

        public ServerFixture()
        {

        }

        public async Task LoadConfiguration(string pkiRoot = null)
        {
            Application = new ApplicationInstance {
                ApplicationName = typeof(T).Name,
                ApplicationType = ApplicationType.Server
            };

            // create the application configuration. Use temp path for cert stores.
            pkiRoot = pkiRoot ?? Path.GetTempPath() + Path.GetRandomFileName();
            var endpointUrl = $"{UriScheme}://localhost:0/" + typeof(T).Name;
            var serverConfig = Application.Build(
                "urn:localhost:UA:" + typeof(T).Name,
                "uri:opcfoundation.org:" + typeof(T).Name)
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .SetChannelLifetime(30000)
                .AsServer(
                    new string[] {
                    endpointUrl
                });

            if (SecurityNone)
            {
                serverConfig.AddUnsecurePolicyNone();
            }
            if (Utils.IsUriHttpsScheme(endpointUrl))
            {
                serverConfig.AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256);
            }
            else if (endpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                // add deprecated policies for opc.tcp tests
                serverConfig.AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                    .AddSignPolicies()
                    .AddSignAndEncryptPolicies()
                    .AddEccSignPolicies()
                    .AddEccSignAndEncryptPolicies();
            }

            if (OperationLimits)
            {
                serverConfig.SetOperationLimits(new OperationLimits() {
                    MaxNodesPerBrowse = 2500,
                    MaxNodesPerRead = 1000,
                    MaxNodesPerWrite = 1000,
                    MaxNodesPerMethodCall = 1000,
                    MaxMonitoredItemsPerCall = 1000,
                    MaxNodesPerHistoryReadData = 1000,
                    MaxNodesPerHistoryReadEvents = 1000,
                    MaxNodesPerHistoryUpdateData = 1000,
                    MaxNodesPerHistoryUpdateEvents = 1000,
                    MaxNodesPerNodeManagement = 1000,
                    MaxNodesPerRegisterNodes = 1000,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = 1000
                });
            }

            serverConfig.SetMaxChannelCount(MaxChannelCount)
                .SetMaxMessageQueueSize(20)
                .SetDiagnosticsEnabled(true)
                .SetAuditingEnabled(true);

            if (ReverseConnectTimeout != 0)
            {
                serverConfig.SetReverseConnect(new ReverseConnectServerConfiguration() {
                    ConnectInterval = ReverseConnectTimeout / 4,
                    ConnectTimeout = ReverseConnectTimeout,
                    RejectTimeout = ReverseConnectTimeout / 4
                });
            }
            
            CertificateIdentifierCollection applicationCerts = ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                "CN=" + typeof(T).Name + ", C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                CertificateStoreType.Directory,
                pkiRoot);

            if (DurableSubscriptionsEnabled)
            {
                serverConfig.SetDurableSubscriptionsEnabled(true)
                    .SetMaxDurableEventQueueSize(10000)
                    .SetMaxDurableNotificationQueueSize(1000)
                    .SetMaxDurableSubscriptionLifetime(3600);
            }

            Config = await serverConfig.AddSecurityConfiguration(
                    applicationCerts,
                    pkiRoot)
                .SetAutoAcceptUntrustedCertificates(AutoAccept)
                .Create().ConfigureAwait(false);
        }

        /// <summary>
        /// Start server fixture on random or fixed port.
        /// </summary>
        public Task<T> StartAsync(TextWriter writer, int port = 0)
        {
            return StartAsync(writer, null, port);
        }

        /// <summary>
        /// Start server fixture on random or fixed port with dedicated PKI.
        /// </summary>
        public async Task<T> StartAsync(TextWriter writer, string pkiRoot, int port = 0)
        {
            Random random = new Random();
            bool retryStartServer = false;
            int testPort = port;
            int serverStartRetries = 1;

            if (Application == null)
            {
                await LoadConfiguration(pkiRoot).ConfigureAwait(false);
            }

            if (port <= 0)
            {
                testPort = ServerFixtureUtils.GetNextFreeIPPort();
                serverStartRetries = 25;
            }

            do
            {
                try
                {
                    await InternalStartServerAsync(writer, testPort).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    if (serverStartRetries <= 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    serverStartRetries--;
                    testPort = random.Next(ServerFixtureUtils.MinTestPort, ServerFixtureUtils.MaxTestPort);
                    retryStartServer = true;
                }
                await Task.Delay(random.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);

            return Server;
        }

        /// <summary>
        /// Create the configuration and start the server.
        /// </summary>
        private async Task InternalStartServerAsync(TextWriter writer, int port)
        {
            Config.ServerConfiguration.BaseAddresses = new StringCollection() {
                $"{UriScheme}://localhost:{port}/{typeof(T).Name}"
            };

            if (writer != null)
            {
                m_traceLogger = NUnitTestLogger<T>.Create(writer, Config, TraceMasks);
            }

            // check the application certificate.
            bool haveAppCertificate = await Application.CheckApplicationInstanceCertificates(
                true, CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // start the server.
            T server = new T();
            if (AllNodeManagers && server is StandardServer standardServer)
            {
                Quickstarts.Servers.Utils.AddDefaultNodeManagers(standardServer);
            }
            await Application.Start(server).ConfigureAwait(false);
            Server = server;
            Port = port;
        }

        /// <summary>
        /// Connect the nunit writer with the logger.
        /// </summary>
        public void SetTraceOutput(TextWriter writer)
        {
            m_traceLogger.SetWriter(writer);
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
        public void StartActivityListenerInternal(bool disableActivityLogging = false)
        {
            if (disableActivityLogging)
            {
                // Create an instance of ActivityListener without logging
                ActivityListener = new ActivityListener() {
                    ShouldListenTo = (source) => (source.Name == EndpointBase.ActivitySourceName),
                    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = _ => { },
                    ActivityStopped = _ => { }
                };
            }
            else
            {
                // Create an instance of ActivityListener and configure its properties with logging
                ActivityListener = new ActivityListener() {
                    ShouldListenTo = (source) => (source.Name == EndpointBase.ActivitySourceName),
                    Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = activity => Utils.LogInfo("Server Started: {0,-15} - TraceId: {1,-32} SpanId: {2,-16} ParentId: {3,-32}",
                        activity.OperationName, activity.TraceId, activity.SpanId, activity.ParentId),
                    ActivityStopped = activity => Utils.LogInfo("Server Stopped: {0,-15} - TraceId: {1,-32} SpanId: {2,-16} ParentId: {3,-32} Duration: {4}",
                        activity.OperationName, activity.TraceId, activity.SpanId, activity.ParentId, activity.Duration),
                };
            }
            ActivitySource.AddActivityListener(ActivityListener);
        }


        /// <summary>
        /// Stop the server.
        /// </summary>
        public Task StopAsync()
        {
            Server?.Stop();
            Server?.Dispose();
            Server = null;
            ActivityListener?.Dispose();
            ActivityListener = null;
            return Task.Delay(100);
        }
    }
}
