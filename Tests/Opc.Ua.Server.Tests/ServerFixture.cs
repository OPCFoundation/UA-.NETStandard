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
using System.IO;
using System.Threading.Tasks;
using Opc.Ua.Configuration;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Server fixture for testing.
    /// </summary>
    /// <typeparam name="T">A server class T used for testing.</typeparam>
    public class ServerFixture<T> where T : ServerBase, new()
    {
        private NUnitTraceLogger m_traceLogger;
        public ApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public T Server { get; private set; }
        public bool LogConsole { get; set; }
        public bool AutoAccept { get; set; }
        public bool OperationLimits { get; set; }
        public int ReverseConnectTimeout { get; set; }
        public int TraceMasks { get; set; } = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
        public bool SecurityNone { get; set; } = false;
        public string UriScheme { get; set; } = Utils.UriSchemeOpcTcp;
        public int Port { get; private set; }

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
                .AsServer(
                    new string[] {
                    endpointUrl
                });

            if (SecurityNone)
            {
                serverConfig.AddUnsecurePolicyNone();
            }
            if (endpointUrl.StartsWith(Utils.UriSchemeHttps, StringComparison.InvariantCultureIgnoreCase))
            {
                serverConfig.AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256);
            }
            else if (endpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.InvariantCultureIgnoreCase))
            {
                // add deprecated policies for opc.tcp tests
                serverConfig.AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                    .AddSignPolicies()
                    .AddSignAndEncryptPolicies();
            }

            if (OperationLimits)
            {
                serverConfig.SetOperationLimits(new OperationLimits() {
                    MaxNodesPerBrowse = 2500,
                    MaxNodesPerRead = 250,
                    MaxNodesPerWrite = 250,
                    MaxNodesPerMethodCall = 500,
                    MaxMonitoredItemsPerCall = 1000,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = 1000
                });
            }

            if (ReverseConnectTimeout != 0)
            {
                serverConfig.SetReverseConnect(new ReverseConnectServerConfiguration() {
                    ConnectInterval = ReverseConnectTimeout / 4,
                    ConnectTimeout = ReverseConnectTimeout,
                    RejectTimeout = ReverseConnectTimeout / 4
                });
            }

            Config = await serverConfig.AddSecurityConfiguration(
                    "CN=" + typeof(T).Name + ", C=US, S=Arizona, O=OPC Foundation, DC=localhost",
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
            Random m_random = new Random();
            bool retryStartServer = false;
            int testPort = port;
            int serverStartRetries = 1;

            if (Application == null)
            {
                await LoadConfiguration(pkiRoot);
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
                    testPort = m_random.Next(ServerFixtureUtils.MinTestPort, ServerFixtureUtils.MaxTestPort);
                    retryStartServer = true;
                }
                await Task.Delay(m_random.Next(100, 1000)).ConfigureAwait(false);
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
                m_traceLogger = NUnitTraceLogger.Create(writer, Config, TraceMasks);
            }

            // check the application certificate.
            bool haveAppCertificate = await Application.CheckApplicationInstanceCertificate(
                true, CertificateFactory.DefaultKeySize, CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // start the server.
            T server = new T();
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
        /// Stop the server.
        /// </summary>
        public Task StopAsync()
        {
            Server?.Stop();
            Server?.Dispose();
            Server = null;
            return Task.Delay(100);
        }
    }
}
