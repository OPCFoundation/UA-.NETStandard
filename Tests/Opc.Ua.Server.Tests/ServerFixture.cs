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
    public class ServerFixture<T> where T : ServerBase, new()
    {
        private NUnitTraceLogger m_traceLogger;
        public T Server { get; private set; }
        public bool LogConsole { get; set; }
        public bool AutoAccept { get; set; }
        public bool OperationLimits { get; set; }
        public int ReverseConnectTimeout { get; set; }
        public int TraceMasks { get; set; } = Utils.TraceMasks.All;
        public int Port { get; private set; }

        private void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                if (AutoAccept)
                {
                    if (!LogConsole)
                    {
                        Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                    }
                    Utils.Trace(Utils.TraceMasks.Security, "Accepted Certificate: {0}", e.Certificate.Subject);
                    e.Accept = true;
                    return;
                }
            }
            if (!LogConsole)
            {
                Console.WriteLine("Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
            }
            Utils.Trace(Utils.TraceMasks.Security, "Rejected Certificate: {0} {1}", e.Error, e.Certificate.Subject);
        }

        /// <summary>
        /// Start server fixture on random port.
        /// </summary>
        public async Task<T> StartAsync(TextWriter writer, bool clean)
        {
            Random m_random = new Random();
            int testPort;
            bool retryStartServer = false;
            int serverStartRetries = 25;
            do
            {
                try
                {
                    testPort = m_random.Next(50000, 60000);
                    await InternalStartServerAsync(writer, clean, testPort).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (serverStartRetries == 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    retryStartServer = true;
                }
                await Task.Delay(m_random.Next(100, 1000)).ConfigureAwait(false);
            } while (retryStartServer);

            return Server;
        }

        /// <summary>
        /// Create the configuration and start the server.
        /// </summary>
        private async Task InternalStartServerAsync(TextWriter writer, bool clean, int port)
        {
            // TODO: support password
            // TODO: support clean start
            //CertificatePasswordProvider PasswordProvider = new CertificatePasswordProvider(Password);
            ApplicationInstance application = new ApplicationInstance {
                ApplicationName = typeof(T).Name,
                ApplicationType = ApplicationType.Server
            };

            // create the application configuration. Use temp path for cert stores.
            var pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            var endpointUrl = $"opc.tcp://localhost:{port}/" + typeof(T).Name;
            var serverConfig = application.Build(
                "urn:localhost:UA:" + typeof(T).Name,
                "uri:opcfoundation.org:" + typeof(T).Name)
                .AsServer(
                    new string[] {
                    endpointUrl
                })
                .AddUnsecurePolicyNone()
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                .AddSignPolicies()
                .AddSignAndEncryptPolicies();
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

            ApplicationConfiguration config = await serverConfig.AddSecurityConfiguration(
                    "CN=" + typeof(T).Name + ", C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                    pkiRoot)
                .SetAutoAcceptUntrustedCertificates(AutoAccept)
                .Create().ConfigureAwait(false);

            if (writer != null)
            {
                m_traceLogger = NUnitTraceLogger.Create(writer, config, TraceMasks);
            }

            // check the application certificate.
            bool haveAppCertificate = await application.CheckApplicationInstanceCertificate(
                true, CertificateFactory.DefaultKeySize, CertificateFactory.DefaultLifeTime).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // cert validator
            // config.CertificateValidator.CertificateValidation += CertificateValidator_CertificateValidation;

            // start the server.
            T server = new T();
            await application.Start(server).ConfigureAwait(false);
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
        /// Stop the server
        /// </summary>
        public Task StopAsync()
        {
            Server.Stop();
            Server.Dispose();
            Server = null;
            return Task.Delay(100);
        }
    }
}
