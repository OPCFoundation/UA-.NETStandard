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

        public async Task InitializeAsync()
        {
            bool initialized = false;
            try
            {
                await InitializeCoreAsync().ConfigureAwait(false);
                initialized = true;
            }
            finally
            {
                if (!initialized)
                {
                    await DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await DisconnectClientAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await StopServerAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        CleanDirectory(m_pkiRoot);
                        CleanDirectory(m_gdsRoot);
                    }
                    finally
                    {
                        if (Telemetry is IDisposable telemetry)
                        {
                            telemetry.Dispose();
                        }
                        Telemetry = null;
                        GC.SuppressFinalize(this);
                    }
                }
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
            int testPort = AotServerFixtureSupport.GetNextFreeIPPort();
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
                    await StopServerAsync().ConfigureAwait(false);
                    testPort = UnsecureRandom.Shared.Next(
                        AotServerFixtureSupport.MinTestPort,
                        AotServerFixtureSupport.MaxTestPort);
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
                    AutoAcceptUntrustedCertificates = true,
                    RejectSHA1SignedCertificates = false,
                    RejectUnknownRevocationStatus = true,
                    MinimumCertificateKeySize = 1024
                },
                TransportQuotas = new TransportQuotas(),
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            m_clientApplication = new ApplicationInstance(Telemetry)
            {
                ApplicationName = m_clientConfiguration.ApplicationName,
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = m_clientConfiguration
            };
            await m_clientConfiguration.ValidateAsync(ApplicationType.Client)
                .ConfigureAwait(false);

            m_clientConfiguration.CertificateManager ??= CertificateManagerFactory.Create(
                m_clientConfiguration.SecurityConfiguration, Telemetry);
            m_clientConfiguration.CertificateManager.AcceptError = static (cert, err) => true;

            bool haveAppCertificate = await m_clientApplication
                .CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            if (!haveAppCertificate)
            {
                throw new InvalidOperationException("Client application certificate invalid!");
            }

            // Create the GDS client with admin credentials
            GdsClient = new GlobalDiscoveryServerClient(
                m_clientConfiguration);

            // Prefer a secured endpoint for the administrative identity.
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
                if (ep.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                    ep.SecurityPolicyUri == SecurityPolicies.Aes256_Sha256_RsaPss)
                {
                    selectedEndpoint = ep;
                    break;
                }
            }
            if (selectedEndpoint == null)
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                        (ep.SecurityPolicyUri == SecurityPolicies.Aes128_Sha256_RsaOaep ||
                            ep.SecurityPolicyUri == SecurityPolicies.Basic256Sha256))
                    {
                        selectedEndpoint = ep;
                        break;
                    }
                }
            }
            if (selectedEndpoint == null)
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode == MessageSecurityMode.SignAndEncrypt)
                    {
                        selectedEndpoint = ep;
                        break;
                    }
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

        private async ValueTask DisconnectClientAsync()
        {
            try
            {
                if (GdsClient != null)
                {
                    GlobalDiscoveryServerClient client = GdsClient;
                    GdsClient = null;
                    await using (client.ConfigureAwait(false))
                    {
                        await client.DisconnectAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ApplicationInstance application = m_clientApplication;
                m_clientApplication = null;
                m_clientConfiguration = null;
                if (application != null)
                {
                    await application.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async ValueTask StopServerAsync()
        {
            try
            {
                ApplicationInstance application = m_serverApplication;
                m_serverApplication = null;
                if (application != null)
                {
                    await using (application.ConfigureAwait(false))
                    {
                        await application.StopAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Server = null;
                m_certificateGroup?.Dispose();
                m_certificateGroup = null;
            }
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
            [
                new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = m_gdsRoot,
                    SubjectName =
                        "CN=GDS AOT Test Server, O=OPC Foundation, DC=localhost",
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }
            ];

            m_serverApplication = new ApplicationInstance(Telemetry)
            {
                ApplicationName = "GDS AOT Test Server",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Opc.Ua.GdsAotTestServer"
            };

            _ = await m_serverApplication
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
                .AddExtension(
                    null, gdsConfig)
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

            m_certificateGroup = new CertificateGroup(Telemetry);
            Server = new GlobalDiscoverySampleServer(
                applicationsDatabase,
                applicationsDatabase,
                m_certificateGroup,
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
                Directory.Delete(path, true);
            }
        }

        private ApplicationInstance m_serverApplication;
        private ApplicationInstance m_clientApplication;
        private ApplicationConfiguration m_clientConfiguration;
        private string m_gdsRoot;
        private string m_pkiRoot;
        private CertificateGroup m_certificateGroup;
    }
}
