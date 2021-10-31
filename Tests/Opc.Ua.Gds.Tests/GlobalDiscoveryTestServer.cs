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
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;

namespace Opc.Ua.Gds.Tests
{
    public class GlobalDiscoveryTestServer
    {
        public GlobalDiscoverySampleServer Server => m_server;
        public ApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public int BasePort { get; private set; }

        public GlobalDiscoveryTestServer(bool _autoAccept)
        {
            m_autoAccept = _autoAccept;
        }

        public async Task StartServer(bool clean, int basePort = -1)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            Application = new ApplicationInstance {
                ApplicationName = "Global Discovery Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GlobalDiscoveryTestServer"
            };

            BasePort = basePort;
            Config = await Load(Application, basePort).ConfigureAwait(false);

            if (clean)
            {
                string thumbprint = Config.SecurityConfiguration.ApplicationCertificate.Thumbprint;
                if (thumbprint != null)
                {
                    using (var store = Config.SecurityConfiguration.ApplicationCertificate.OpenStore())
                    {
                        await store.Delete(thumbprint).ConfigureAwait(false);
                    }
                }

                // always start with clean cert store
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.ApplicationCertificate.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.TrustedIssuerCertificates.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.TrustedPeerCertificates.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.RejectedCertificateStore.OpenStore());

                Config = await Load(Application, basePort).ConfigureAwait(false);
            }

            // check the application certificate.
            bool haveAppCertificate = await Application.CheckApplicationInstanceCertificate(true, 0).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!Config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                Config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // get the DatabaseStorePath configuration parameter.
            GlobalDiscoveryServerConfiguration gdsConfiguration = Config.ParseExtension<GlobalDiscoveryServerConfiguration>();
            string databaseStorePath = Utils.ReplaceSpecialFolderNames(gdsConfiguration.DatabaseStorePath);

            if (clean)
            {
                // clean up database
                if (File.Exists(databaseStorePath))
                {
                    File.Delete(databaseStorePath);
                }

                // clean up GDS stores
                TestUtils.DeleteDirectory(gdsConfiguration.AuthoritiesStorePath);
                TestUtils.DeleteDirectory(gdsConfiguration.ApplicationCertificatesStorePath);
                foreach (var group in gdsConfiguration.CertificateGroups)
                {
                    TestUtils.DeleteDirectory(group.BaseStorePath);
                }
            }

            var database = JsonApplicationsDatabase.Load(databaseStorePath);

            // start the server.
            m_server = new GlobalDiscoverySampleServer(
                database,
                database,
                new CertificateGroup());
            await Application.Start(m_server).ConfigureAwait(false);

            ServerState serverState = Server.GetStatus().State;
            if (serverState != ServerState.Running)
            {
                throw new ServiceResultException("Server failed to start");
            }
        }

        public void StopServer()
        {
            if (m_server != null)
            {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (GlobalDiscoverySampleServer server = m_server)
                {
                    m_server = null;
                    // Stop server and dispose
                    server.Stop();
                }
            }
        }

        public string ReadLogFile()
        {
            return File.ReadAllText(Utils.ReplaceSpecialFolderNames(Config.TraceConfiguration.OutputFilePath));
        }

        public bool ResetLogFile()
        {
            try
            {
                File.Delete(Utils.ReplaceSpecialFolderNames(Config.TraceConfiguration.OutputFilePath));
                return true;
            }
            catch { }
            return false;
        }

        public string GetLogFilePath()
        {
            return Utils.ReplaceSpecialFolderNames(Config.TraceConfiguration.OutputFilePath);
        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = m_autoAccept;
                if (m_autoAccept)
                {
                    Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
                }
                else
                {
                    Console.WriteLine("Rejected Certificate: {0}", e.Certificate.Subject);
                }
            }
        }

        private static async Task<ApplicationConfiguration> Load(ApplicationInstance application, int basePort)
        {
#if USE_FILE_CONFIG
            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(true).ConfigureAwait(false);
#else
            string root = Path.Combine("%LocalApplicationData%", "OPC");
            string gdsRoot = Path.Combine(root, "GDS");
            var gdsConfig = new GlobalDiscoveryServerConfiguration() {
                AuthoritiesStorePath = Path.Combine(gdsRoot, "authorities"),
                ApplicationCertificatesStorePath = Path.Combine(gdsRoot, "applications"),
                DefaultSubjectNameContext = "O=OPC Foundation",
                CertificateGroups = new CertificateGroupConfigurationCollection()
                {
                    new CertificateGroupConfiguration() {
                        Id = "Default",
                        CertificateType = "RsaSha256ApplicationCertificateType",
                        SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                        BaseStorePath = Path.Combine(gdsRoot, "CA", "default"),
                        DefaultCertificateHashSize = 256,
                        DefaultCertificateKeySize = 2048,
                        DefaultCertificateLifetime = 12,
                        CACertificateHashSize = 512,
                        CACertificateKeySize = 4096,
                        CACertificateLifetime = 60
                    }
                },
                DatabaseStorePath = Path.Combine(gdsRoot, "gdsdb.json")
            };

            var transportQuotas = new TransportQuotas() {
                OperationTimeout = 120000,
                MaxStringLength = 1048576,
                MaxByteStringLength = 1048576,
                MaxArrayLength = 65535,
                MaxMessageSize = 4194304,
                MaxBufferSize = 65535,
                ChannelLifetime = 300000,
                SecurityTokenLifetime = 3600000,
            };

            // build the application configuration.
            ApplicationConfiguration config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:GlobalDiscoveryTestServer",
                    "http://opcfoundation.org/UA/GlobalDiscoveryTestServer")
                .SetTransportQuotas(transportQuotas)
                .AsServer(new string[] { "opc.tcp://localhost:58810/GlobalDiscoveryTestServer" })
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .SetDiagnosticsEnabled(true)
                .AddServerCapabilities("GDS")
                .AddServerProfile("http://opcfoundation.org/UA-Profile/Server/GlobalDiscoveryAndCertificateManagement2017")
                .SetShutdownDelay(0)
                .AddSecurityConfiguration(
                    "CN=Global Discovery Test Server, O=OPC Foundation, DC=localhost",
                    gdsRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(true)
                .SetMinimumCertificateKeySize(1024)
                .AddExtension<GlobalDiscoveryServerConfiguration>(null, gdsConfig)
                .SetDeleteOnLoad(true)
                .SetOutputFilePath(Path.Combine(root, "Logs", "Opc.Ua.Gds.Tests.log.txt"))
                .SetTraceMasks(519)
                .Create().ConfigureAwait(false);
#endif
            TestUtils.PatchBaseAddressesPorts(config, basePort);
            return config;
        }

        private GlobalDiscoverySampleServer m_server;
        private static bool m_autoAccept = false;
    }
}
