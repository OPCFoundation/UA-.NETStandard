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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Tests
{
    public class GlobalDiscoveryTestServer
    {
        public GlobalDiscoverySampleServer Server { get; private set; }
        public ApplicationInstance Application { get; private set; }
        public ApplicationConfiguration Config { get; private set; }
        public int BasePort { get; private set; }

        public GlobalDiscoveryTestServer(bool autoAccept, ITelemetryContext telemetry)
        {
            s_autoAccept = autoAccept;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<GlobalDiscoveryTestServer>();
        }

        public async Task StartServerAsync(
            bool clean,
            int basePort = -1,
            string storeType = CertificateStoreType.Directory)
        {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg(m_logger);

            string configSectionName = "Opc.Ua.GlobalDiscoveryTestServer";
            if (storeType == CertificateStoreType.X509Store)
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    throw new PlatformNotSupportedException(
                        "X509 Store with crls is only supported on Windows");
                }
                configSectionName = "Opc.Ua.GlobalDiscoveryTestServerX509Stores";
            }
            Application = new ApplicationInstance(m_telemetry)
            {
                ApplicationName = "Global Discovery Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = configSectionName
            };

            BasePort = basePort;
            Config = await LoadAsync(Application, basePort).ConfigureAwait(false);

            if (clean)
            {
                string thumbprint = Config.SecurityConfiguration.ApplicationCertificate.Thumbprint;
                if (thumbprint != null)
                {
                    using ICertificateStore store = Config.SecurityConfiguration
                        .ApplicationCertificate
                        .OpenStore(m_telemetry);
                    await store.DeleteAsync(thumbprint).ConfigureAwait(false);
                }

                // always start with clean cert store
                await TestUtils
                    .CleanupTrustListAsync(
                        Config.SecurityConfiguration.ApplicationCertificate, m_telemetry)
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Config.SecurityConfiguration.TrustedIssuerCertificates, m_telemetry)
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Config.SecurityConfiguration.TrustedPeerCertificates, m_telemetry)
                    .ConfigureAwait(false);
                await TestUtils
                    .CleanupTrustListAsync(
                        Config.SecurityConfiguration.RejectedCertificateStore, m_telemetry)
                    .ConfigureAwait(false);

                Config = await LoadAsync(Application, basePort).ConfigureAwait(false);
            }

            // check the application certificate.
            bool haveAppCertificate = await Application
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!Config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                Config.CertificateValidator.CertificateValidation
                    += new CertificateValidationEventHandler(
                    CertificateValidator_CertificateValidation);
            }

            // get the DatabaseStorePath configuration parameter.
            GlobalDiscoveryServerConfiguration gdsConfiguration =
                Config.ParseExtension<GlobalDiscoveryServerConfiguration>();
            string databaseStorePath = Utils.ReplaceSpecialFolderNames(
                gdsConfiguration.DatabaseStorePath);
            string usersDatabaseStorePath = Utils.ReplaceSpecialFolderNames(
                gdsConfiguration.UsersDatabaseStorePath);

            if (clean)
            {
                // clean up database
                if (File.Exists(databaseStorePath))
                {
                    File.Delete(databaseStorePath);
                }
                if (File.Exists(usersDatabaseStorePath))
                {
                    File.Delete(usersDatabaseStorePath);
                }

                // clean up GDS stores
                TestUtils.DeleteDirectory(gdsConfiguration.AuthoritiesStorePath);
                TestUtils.DeleteDirectory(gdsConfiguration.ApplicationCertificatesStorePath);
                foreach (CertificateGroupConfiguration group in gdsConfiguration.CertificateGroups)
                {
                    TestUtils.DeleteDirectory(group.BaseStorePath);
                }
            }

            var applicationsDatabase = JsonApplicationsDatabase.Load(databaseStorePath);
            IUserDatabase userDatabase = JsonUserDatabase.Load(usersDatabaseStorePath, m_telemetry);

            RegisterDefaultUsers(userDatabase);

            // start the server.
            Server = new GlobalDiscoverySampleServer(
                applicationsDatabase,
                applicationsDatabase,
                new CertificateGroup(m_telemetry),
                userDatabase);
            await Application.StartAsync(Server).ConfigureAwait(false);

            ServerState serverState = Server.CurrentState;
            if (serverState != ServerState.Running)
            {
                throw new ServiceResultException("Server failed to start");
            }
        }

        public void StopServer()
        {
            if (Server != null)
            {
                m_logger.LogInformation("Server stopped. Waiting for exit...");

                using GlobalDiscoverySampleServer server = Server;
                Server = null;
                // Stop server and dispose
                server.Stop();
            }
        }

        private void CertificateValidator_CertificateValidation(
            CertificateValidator validator,
            CertificateValidationEventArgs e)
        {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
            {
                e.Accept = s_autoAccept;
                if (s_autoAccept)
                {
                    m_logger.LogInformation("Accepted Certificate: {Subject}", e.Certificate.Subject);
                }
                else
                {
                    m_logger.LogInformation("Rejected Certificate: {Subject}", e.Certificate.Subject);
                }
            }
        }

        /// <summary>
        /// Creates the default GDS users.
        /// </summary>
        private static void RegisterDefaultUsers(IUserDatabase userDatabase)
        {
            userDatabase.CreateUser(
                "sysadmin",
                "demo"u8,
                [GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin, Role.SecurityAdmin, Role.ConfigureAdmin]);
            userDatabase.CreateUser(
                "appadmin", "demo"u8,
                [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin, GdsRole.DiscoveryAdmin]);
            userDatabase.CreateUser(
                "appuser",
                "demo"u8,
                [Role.AuthenticatedUser]);

            userDatabase.CreateUser(
                "DiscoveryAdmin",
                "demo"u8,
                [Role.AuthenticatedUser, GdsRole.DiscoveryAdmin]);
            userDatabase.CreateUser(
                "CertificateAuthorityAdmin", "demo"u8,
                [Role.AuthenticatedUser, GdsRole.CertificateAuthorityAdmin]);
        }

        private static async Task<ApplicationConfiguration> LoadAsync(
            ApplicationInstance application,
            int basePort)
        {
#if !USE_FILE_CONFIG
            // load the application configuration.
            ApplicationConfiguration config = await application
                .LoadApplicationConfigurationAsync(true)
                .ConfigureAwait(false);
#else
            string[] baseAddresses = ["opc.tcp://localhost:58810/GlobalDiscoveryTestServer"];
            string root = Path.Combine("%LocalApplicationData%", "OPC");
            string gdsRoot = Path.Combine(root, "GDS");
            var gdsConfig = new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath = Path.Combine(gdsRoot, "authorities"),
                ApplicationCertificatesStorePath = Path.Combine(gdsRoot, "applications"),
                DefaultSubjectNameContext = "O=OPC Foundation",
                CertificateGroups =
                [
                    new CertificateGroupConfiguration
                    {
                        Id = "Default",
                        CertificateTypes =
                        [
                            "RsaSha256ApplicationCertificateType",
                            "EccNistP256ApplicationCertificateType",
                            "EccNistP384ApplicationCertificateType",
                            "EccBrainpoolP256r1ApplicationCertificateType",
                            "EccBrainpoolP384r1ApplicationCertificateType"
                        ],
                        SubjectName = "CN=GDS Test CA, O=OPC Foundation",
                        BaseStorePath = Path.Combine(gdsRoot, "CA", "default"),
                        DefaultCertificateHashSize = 256,
                        DefaultCertificateKeySize = 2048,
                        DefaultCertificateLifetime = 12,
                        CACertificateHashSize = 512,
                        CACertificateKeySize = 4096,
                        CACertificateLifetime = 60
                    }
                ],
                DatabaseStorePath = Path.Combine(gdsRoot, "gdsdb.json"),
                UsersDatabaseStorePath = Path.Combine(gdsRoot, "gdsusersdb.json")
            };

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    "CN=Global Discovery Test Client, O=OPC Foundation, DC=localhost",
                    CertificateStoreType.Directory,
                    gdsRoot);

            // build the application configuration.
            ApplicationConfiguration config = await application
                .Build(
                    "urn:localhost:opcfoundation.org:GlobalDiscoveryTestServer",
                    "http://opcfoundation.org/UA/GlobalDiscoveryTestServer")
                .AsServer(baseAddresses)
                .AddEccSignAndEncryptPolicies()
                .AddSignAndEncryptPolicies()
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .SetDiagnosticsEnabled(true)
                .AddServerCapabilities("GDS")
                .AddServerProfile(
                    "http://opcfoundation.org/UA-Profile/Server/GlobalDiscoveryAndCertificateManagement2017")
                .SetShutdownDelay(0)
                .AddSecurityConfiguration(applicationCerts, gdsRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(true)
                .SetMinimumCertificateKeySize(1024)
                .AddExtension<GlobalDiscoveryServerConfiguration>(null, gdsConfig)
                .SetDeleteOnLoad(true)
                .SetOutputFilePath(Path.Combine(root, "Logs", "Opc.Ua.Gds.Tests.log.txt"))
                .SetTraceMasks(519)
                .CreateAsync()
                .ConfigureAwait(false);
#endif
            TestUtils.PatchBaseAddressesPorts(config, basePort);
            return config;
        }

        private static bool s_autoAccept;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
    }
}
