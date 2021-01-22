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
            Config = await Load(Application, basePort);

            if (clean)
            {
                string thumbprint = Config.SecurityConfiguration.ApplicationCertificate.Thumbprint;
                if (thumbprint != null)
                {
                    using (var store = Config.SecurityConfiguration.ApplicationCertificate.OpenStore())
                    {
                        await store.Delete(thumbprint);
                    }
                }

                // always start with clean cert store
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.ApplicationCertificate.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.TrustedIssuerCertificates.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.TrustedPeerCertificates.OpenStore());
                TestUtils.CleanupTrustList(Config.SecurityConfiguration.RejectedCertificateStore.OpenStore());

                Config = await Load(Application, basePort);
            }

            // check the application certificate.
            bool haveAppCertificate = await Application.CheckApplicationInstanceCertificate(true, 0);
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
            await Application.Start(m_server);

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

        private async Task<ApplicationConfiguration> Load(ApplicationInstance application, int basePort)
        {
            // load the application configuration.
            ApplicationConfiguration config = await application.LoadApplicationConfiguration(true);
            TestUtils.PatchBaseAddressesPorts(config, basePort);
            return config;
        }

        private GlobalDiscoverySampleServer m_server;
        private static bool m_autoAccept = false;
    }
}
