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

using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Shared fixture that starts a GDS server in-process and provides
    /// a connected <see cref="GlobalDiscoveryServerClient"/> for tests.
    /// Builds the server configuration programmatically to avoid
    /// config-file dependencies in the AOT test environment.
    /// </summary>
    public sealed class GdsTestFixture : IAsyncInitializer, IAsyncDisposable
    {
        public GlobalDiscoverySampleServer Server { get; private set; }
        public GlobalDiscoveryServerClient GdsClient { get; private set; }
        public ITelemetryContext Telemetry { get; private set; }
        public string EndpointUrl { get; private set; }
        public int BasePort { get; private set; }

        /// <summary>
        /// If non-null, the GDS fixture failed to initialize (e.g. under NativeAOT
        /// where DataContractSerializer is not available). Tests should skip.
        /// </summary>
        public string SkipReason { get; private set; }

        private ApplicationInstance m_serverApplication;
        private ApplicationConfiguration m_clientConfiguration;
        private string m_gdsRoot;
        private string m_pkiRoot;

        public async Task InitializeAsync()
        {
            try
            {
                await InitializeCoreAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SkipReason = $"GDS fixture initialization failed: {ex.Message}";
            }
        }

        private async Task InitializeCoreAsync()
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));

            m_gdsRoot = Path.Combine(Path.GetTempPath(), "OPC", "AotGDS");
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaAotGdsTests", "pki");

            // Clean any previous state
            CleanDirectory(m_gdsRoot);

            // Start GDS server with retry logic
            int testPort = AotServerFixture<ServerBase>.GetNextFreeIPPort();
            bool retryStartServer;
            int serverStartRetries = 25;
            do
            {
                retryStartServer = false;
                try
                {
                    await StartGdsServerAsync(testPort).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    serverStartRetries--;
                    if (Server != null)
                    {
                        await Server.StopAsync().ConfigureAwait(false);
                        Server.Dispose();
                        Server = null;
                    }
                    testPort = UnsecureRandom.Shared.Next(
                        AotServerFixture<ServerBase>.MinTestPort,
                        AotServerFixture<ServerBase>.MaxTestPort);
                    if (serverStartRetries == 0 ||
                        sre.StatusCode != StatusCodes.BadNoCommunication)
                    {
                        throw;
                    }
                    retryStartServer = true;
                }
                await Task.Delay(UnsecureRandom.Shared.Next(100, 1000))
                    .ConfigureAwait(false);
            } while (retryStartServer);

            BasePort = testPort;
            EndpointUrl =
                $"opc.tcp://localhost:{BasePort}/GlobalDiscoveryTestServer";

            // Build a client configuration programmatically
            m_clientConfiguration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "AotGdsTestClient",
                ApplicationUri =
                    "urn:localhost:OPCFoundation:AotGdsTestClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "own"),
                        SubjectName =
                            "CN=AotGdsTestClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas(),
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await m_clientConfiguration.ValidateAsync(ApplicationType.Client)
                .ConfigureAwait(false);

            m_clientConfiguration.CertificateValidator
                .CertificateValidation += (s, e) => e.Accept = true;

            // Create the GDS client with admin credentials
            GdsClient = new GlobalDiscoveryServerClient(
                m_clientConfiguration);

            // Select the None security endpoint for simplicity
            var endpointConfiguration =
                EndpointConfiguration.Create(m_clientConfiguration);
            using DiscoveryClient discoveryClient = await DiscoveryClient.CreateAsync(
                new Uri(EndpointUrl),
                endpointConfiguration,
                Telemetry).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints =
                await discoveryClient.GetEndpointsAsync(
                    default, CancellationToken.None)
                    .ConfigureAwait(false);
            await discoveryClient.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);

            EndpointDescription selectedEndpoint = null;
            foreach (EndpointDescription ep in endpoints)
            {
                if (ep.SecurityPolicyUri == SecurityPolicies.None)
                {
                    selectedEndpoint = ep;
                    break;
                }
            }
            selectedEndpoint ??= endpoints[0];

            GdsClient.Endpoint = new ConfiguredEndpoint(
                null, selectedEndpoint, endpointConfiguration);
            GdsClient.AdminCredentials = new UserIdentity(
                "appadmin", Encoding.UTF8.GetBytes("demo"));
            await GdsClient.ConnectAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (GdsClient != null)
            {
                try
                {
                    await GdsClient.DisconnectAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore disconnect errors during cleanup
                }
                GdsClient.Dispose();
                GdsClient = null;
            }

            if (Server != null)
            {
                using GlobalDiscoverySampleServer server = Server;
                Server = null;
                await server.StopAsync().ConfigureAwait(false);
            }

            if (m_serverApplication != null)
            {
                await m_serverApplication.DisposeAsync().ConfigureAwait(false);
                m_serverApplication = null;
            }

            CleanDirectory(m_pkiRoot);
            CleanDirectory(m_gdsRoot);
        }

        [UnconditionalSuppressMessage("AOT",
            "IL2026:RequiresUnreferencedCode",
            Justification = "Test-only code; GDS config serialization " +
                "is exercised at runtime.")]
        [UnconditionalSuppressMessage("AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "Test-only code; GDS config serialization " +
                "is exercised at runtime.")]
        private async Task StartGdsServerAsync(int port)
        {
            string[] baseAddresses =
                [$"opc.tcp://localhost:{port}/GlobalDiscoveryTestServer"];

            var gdsConfig = new GlobalDiscoveryServerConfiguration
            {
                AuthoritiesStorePath =
                    Path.Combine(m_gdsRoot, "authorities"),
                ApplicationCertificatesStorePath =
                    Path.Combine(m_gdsRoot, "applications"),
                DefaultSubjectNameContext = "O=OPC Foundation",
                CertificateGroups =
                [
                    new CertificateGroupConfiguration
                    {
                        Id = "Default",
                        CertificateTypes =
                        [
                            "RsaSha256ApplicationCertificateType"
                        ],
                        SubjectName =
                            "CN=GDS Test CA, O=OPC Foundation",
                        BaseStorePath =
                            Path.Combine(m_gdsRoot, "CA", "default"),
                        DefaultCertificateHashSize = 256,
                        DefaultCertificateKeySize = 2048,
                        DefaultCertificateLifetime = 12,
                        CACertificateHashSize = 512,
                        CACertificateKeySize = 4096,
                        CACertificateLifetime = 60
                    }
                ],
                DatabaseStorePath =
                    Path.Combine(m_gdsRoot, "gdsdb.json"),
                UsersDatabaseStorePath =
                    Path.Combine(m_gdsRoot, "gdsusersdb.json")
            };

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder
                    .CreateDefaultApplicationCertificates(
                        "CN=GDS AOT Test Server, O=OPC Foundation, " +
                        "DC=localhost",
                        CertificateStoreType.Directory,
                        m_gdsRoot);

            m_serverApplication = new ApplicationInstance(Telemetry)
            {
                ApplicationName = "GDS AOT Test Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GdsAotTestServer"
            };

            ApplicationConfiguration config = await m_serverApplication
                .Build(
                    "urn:localhost:opcfoundation.org:GdsAotTestServer",
                    "http://opcfoundation.org/UA/GdsAotTestServer")
                .AsServer(baseAddresses)
                .AddUnsecurePolicyNone()
                .AddSignAndEncryptPolicies()
                .AddUserTokenPolicy(UserTokenType.Anonymous)
                .AddUserTokenPolicy(UserTokenType.UserName)
                .SetDiagnosticsEnabled(true)
                .AddServerCapabilities("GDS")
                .AddServerProfile(
                    "http://opcfoundation.org/UA-Profile/Server/" +
                    "GlobalDiscoveryAndCertificateManagement2017")
                .SetShutdownDelay(0)
                .AddSecurityConfiguration(applicationCerts, m_gdsRoot)
                .SetAutoAcceptUntrustedCertificates(true)
                .SetRejectSHA1SignedCertificates(false)
                .SetRejectUnknownRevocationStatus(true)
                .SetMinimumCertificateKeySize(1024)
                .AddExtension<GlobalDiscoveryServerConfiguration>(
                    null, gdsConfig)
                .SetDeleteOnLoad(true)
                .CreateAsync()
                .ConfigureAwait(false);

            bool haveAppCertificate = await m_serverApplication
                .CheckApplicationInstanceCertificatesAsync(true)
                .ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid!");
            }

            // Load databases and register users
            var applicationsDatabase = JsonApplicationsDatabase.Load(
                gdsConfig.DatabaseStorePath);
            IUserDatabase userDatabase = JsonUserDatabase.Load(
                gdsConfig.UsersDatabaseStorePath, Telemetry);

            userDatabase.CreateUser("sysadmin", "demo"u8,
                [GdsRole.CertificateAuthorityAdmin,
                 GdsRole.DiscoveryAdmin,
                 Role.SecurityAdmin, Role.ConfigureAdmin]);
            userDatabase.CreateUser("appadmin", "demo"u8,
                [Role.AuthenticatedUser,
                 GdsRole.CertificateAuthorityAdmin,
                 GdsRole.DiscoveryAdmin]);
            userDatabase.CreateUser("appuser", "demo"u8,
                [Role.AuthenticatedUser]);

            Server = new GlobalDiscoverySampleServer(
                applicationsDatabase,
                applicationsDatabase,
                new CertificateGroup(Telemetry),
                userDatabase,
                Telemetry);
            await m_serverApplication.StartAsync(Server)
                .ConfigureAwait(false);

            if (Server.CurrentState != ServerState.Running)
            {
                throw new ServiceResultException(
                    "GDS server failed to start");
            }
        }

        private static void CleanDirectory(string path)
        {
            if (path != null && Directory.Exists(path))
            {
                try
                {
                    Directory.Delete(path, true);
                }
                catch
                {
                    // ignore cleanup errors
                }
            }
        }
    }
}
