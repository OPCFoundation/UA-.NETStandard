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

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT-compatible server fixture for testing. Based on
    /// <c>Opc.Ua.Server.Tests.ServerFixture{T}</c> but avoids the
    /// project reference to the NUnit test infrastructure (which
    /// transitively pulls BenchmarkDotNet, Moq, and other
    /// NativeAOT-incompatible packages).
    /// </summary>
    public sealed class AotServerFixture<T>
        where T : ServerBase
    {
        public IApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public T Server { get; private set; }
        public bool AutoAccept { get; set; }
        public bool SecurityNone { get; set; }
        public bool AllNodeManagers { get; set; }
        public int Port { get; private set; }
        public string UriScheme { get; set; } = Utils.UriSchemeOpcTcp;

        public AotServerFixture(
            Func<ITelemetryContext, T> factory,
            ITelemetryContext telemetry)
        {
            m_factory = factory;
            m_telemetry = telemetry;
        }

        public async Task LoadConfigurationAsync(string pkiRoot = null)
        {
            Application = new ApplicationInstance(m_telemetry)
            {
                ApplicationName = typeof(T).Name,
                ApplicationType = ApplicationType.Server
            };

            pkiRoot ??= Path.GetTempPath() + Path.GetRandomFileName();
            string endpointUrl = $"{UriScheme}://localhost:0/" + typeof(T).Name;
            IApplicationConfigurationBuilderServerSelected serverConfig = Application
                .Build(
                    "urn:localhost:UA:" + typeof(T).Name,
                    "uri:opcfoundation.org:" + typeof(T).Name)
                .SetMaxByteStringLength(4 * 1024 * 1024)
                .SetMaxArrayLength(1024 * 1024)
                .SetChannelLifetime(30000)
                .AsServer([endpointUrl]);

            if (SecurityNone)
            {
                serverConfig.AddUnsecurePolicyNone();
            }
            if (endpointUrl.StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                serverConfig
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15)
                    .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                    .AddSignPolicies()
                    .AddSignAndEncryptPolicies()
                    .AddEccSignPolicies()
                    .AddEccSignAndEncryptPolicies();
            }

            serverConfig
                .SetMaxChannelCount(10)
                .SetMaxMessageQueueSize(20)
                .SetDiagnosticsEnabled(true)
                .SetAuditingEnabled(true);

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=" + typeof(T).Name + ", C=US, S=Arizona, O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    pkiRoot);

            Config = await serverConfig
                .AddSecurityConfiguration(applicationCerts, pkiRoot)
                .SetAutoAcceptUntrustedCertificates(AutoAccept)
                .CreateAsync()
                .ConfigureAwait(false);
        }

        public async Task<T> StartAsync(int port = 0)
        {
            bool retryStartServer = false;
            int testPort = port;
            int serverStartRetries = 1;

            if (Application == null)
            {
                await LoadConfigurationAsync().ConfigureAwait(false);
            }

            if (port <= 0)
            {
                testPort = GetNextFreeIPPort();
                serverStartRetries = 25;
            }

            do
            {
                try
                {
                    await InternalStartServerAsync(testPort).ConfigureAwait(false);
                    retryStartServer = false;
                }
                catch (ServiceResultException sre)
                    when (serverStartRetries > 0 &&
                        sre.StatusCode == StatusCodes.BadNoCommunication)
                {
                    serverStartRetries--;
                    testPort = UnsecureRandom.Shared.Next(
                        MinTestPort, MaxTestPort);
                    retryStartServer = true;
                }
                await Task.Delay(UnsecureRandom.Shared.Next(100, 1000))
                    .ConfigureAwait(false);
            } while (retryStartServer);

            return Server;
        }

        public async Task StopAsync()
        {
            if (Server != null)
            {
                await Server.StopAsync().ConfigureAwait(false);
                Server.Dispose();
                Server = null;
            }
            await Task.Delay(100).ConfigureAwait(false);
        }

        private async Task InternalStartServerAsync(int port)
        {
            Config.ServerConfiguration.BaseAddresses
                = [$"{UriScheme}://localhost:{port}/{typeof(T).Name}"];

            bool haveAppCertificate = await Application
                .CheckApplicationInstanceCertificatesAsync(
                    true, CertificateFactory.DefaultLifeTime)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid!");
            }

            T server = m_factory(m_telemetry);
            if (AllNodeManagers && server is StandardServer standardServer)
            {
                Quickstarts.Servers.Utils.AddDefaultNodeManagers(standardServer);
            }
            await Application.StartAsync(server).ConfigureAwait(false);
            Server = server;
            Port = port;
        }

        internal const int MinTestPort = 50000;
        internal const int MaxTestPort = 65000;

        internal static int GetNextFreeIPPort()
        {
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
            using var socket = new Socket(
                endpoint.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp);
            socket.Bind(endpoint);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                return ep.Port;
            }
            return 0;
        }

        private readonly Func<ITelemetryContext, T> m_factory;
        private readonly ITelemetryContext m_telemetry;
    }
}
