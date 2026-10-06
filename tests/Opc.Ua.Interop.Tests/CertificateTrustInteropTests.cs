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

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// Certificate trust between the stacks, with auto-accept switched off:
    /// untrusted peers are rejected and accepted once trusted, CA-issued
    /// certificates are accepted when only the CA is trusted, and a
    /// certificate on the CA's revocation list is rejected. Each test has
    /// its own PKI and server.
    /// </summary>
    [TestFixture]
    [Category("Interop")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateTrustInteropTests
    {
        private static readonly TimeSpan s_peerTimeout = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan s_storeTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// What a client may see when the server rejects its certificate. A
        /// server should hide the reason behind BadSecurityChecksFailed.
        /// </summary>
        private static readonly StatusCode[] s_clientRejectedCodes =
        [
            StatusCodes.BadSecurityChecksFailed,
            StatusCodes.BadCertificateUntrusted,
            StatusCodes.BadCertificateRevoked
        ];

        private static readonly string s_clientRejectedNames = string.Join(
            ",",
            s_clientRejectedCodes.Select(c => c.SymbolicId));

        private ITelemetryContext m_telemetry;
        private string m_pkiRoot;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = InteropPki.CreateRoot();
        }

        [TearDown]
        public void TearDown()
        {
            InteropPki.Delete(m_pkiRoot);
        }

        private string ServerPki => InteropPki.ServerPki(m_pkiRoot);
        private string ClientPki => InteropPki.ClientPki(m_pkiRoot);

        /// <summary>
        /// The 1.5 server rejects an untrusted 2.0 client, stores its
        /// certificate in the rejected store, and accepts it once trusted.
        /// </summary>
        [Test]
        public async Task LegacyServerRejectsUntrustedClientUntilTrustedAsync()
        {
            (LegacyPeerProcess server, string url) = await StartLegacyServerAsync(autoAccept: false)
                .ConfigureAwait(false);
            using (server)
            {
                await using ClientFixture client = await CreateClientAsync(autoAccept: true).ConfigureAwait(false);

#pragma warning disable CA2025 // Assert.ThrowsAsync completes the task before the client is disposed
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                    () => ConnectAsync(client, url));
#pragma warning restore CA2025
                Assert.That(sre.StatusCode, Is.AnyOf(s_clientRejectedCodes), sre.ToString());
                Assert.That(
                    await InteropPki.WaitForCertificatesAsync(ServerPki, InteropPki.Rejected, s_storeTimeout)
                        .ConfigureAwait(false),
                    Is.Not.Empty,
                    "the 1.5 server did not store the rejected certificate");

                InteropPki.Trust(ServerPki, ClientPki);
                await ConnectReadAndCloseAsync(client, url).ConfigureAwait(false);
                await server.StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The 2.0 client rejects an untrusted 1.5 server certificate and
        /// accepts it once trusted.
        /// </summary>
        [Test]
        public async Task ClientRejectsUntrustedLegacyServerUntilTrustedAsync()
        {
            (LegacyPeerProcess server, string url) = await StartLegacyServerAsync(autoAccept: true)
                .ConfigureAwait(false);
            using (server)
            {
                await using ClientFixture client = await CreateClientAsync(autoAccept: false).ConfigureAwait(false);

#pragma warning disable CA2025 // Assert.ThrowsAsync completes the task before the client is disposed
                ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                    () => ConnectAsync(client, url));
#pragma warning restore CA2025
                Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted), sre.ToString());

                InteropPki.Trust(ClientPki, ServerPki);
                await ConnectReadAndCloseAsync(client, url).ConfigureAwait(false);
                await server.StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The 2.0 server rejects an untrusted 1.5 client, stores its
        /// certificate in the rejected store, and accepts it once trusted.
        /// </summary>
        [Test]
        public async Task ServerRejectsUntrustedLegacyClientUntilTrustedAsync()
        {
            ServerFixture<ReferenceServer> server = await StartServerAsync(autoAccept: false).ConfigureAwait(false);
            try
            {
                string url = ServerUrl(server);
                PeerRun rejected = await RunLegacyClientAsync(url, true, "--expect-connect-error", s_clientRejectedNames)
                    .ConfigureAwait(false);
                rejected.AssertPassed("Connect");
                Assert.That(
                    await InteropPki.WaitForCertificatesAsync(ServerPki, InteropPki.Rejected, s_storeTimeout)
                        .ConfigureAwait(false),
                    Is.Not.Empty,
                    "the 2.0 server did not store the rejected certificate");

                InteropPki.Trust(ServerPki, ClientPki);
                PeerRun accepted = await RunLegacyClientAsync(url, true).ConfigureAwait(false);
                AssertConnected(accepted);
            }
            finally
            {
                await server.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The 1.5 client rejects an untrusted 2.0 server certificate and
        /// accepts it once trusted.
        /// </summary>
        [Test]
        public async Task LegacyClientRejectsUntrustedServerUntilTrustedAsync()
        {
            ServerFixture<ReferenceServer> server = await StartServerAsync(autoAccept: true).ConfigureAwait(false);
            try
            {
                string url = ServerUrl(server);
                PeerRun rejected = await RunLegacyClientAsync(
                    url,
                    false,
                    "--expect-connect-error",
                    "BadCertificateUntrusted").ConfigureAwait(false);
                rejected.AssertPassed("Connect");

                InteropPki.Trust(ClientPki, ServerPki);
                PeerRun accepted = await RunLegacyClientAsync(url, false).ConfigureAwait(false);
                AssertConnected(accepted);
            }
            finally
            {
                await server.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Both applications use certificates of one CA, and each trusts only
        /// the CA (with an empty CRL): the 2.0 client and the 1.5 server
        /// accept each other. With the client certificate on the CRL the
        /// server rejects it.
        /// </summary>
        [Test]
        public async Task CaIssuedCertificatesWithLegacyServerAsync([Values] bool revokeClient)
        {
            using Certificate ca = InteropPki.CreateCa("CN=Interop Test CA, O=OPC Foundation");

            // Let each side create its self-signed certificate, then replace
            // it with one of the CA, with the same subject and names.
            (LegacyPeerProcess init, int exitCode) = await LegacyPeerProcess.RunAsync(
                s_peerTimeout,
                "server", "--port", "4840", "--pki", ServerPki, "--init-only", "true").ConfigureAwait(false);
            using (init)
            {
                Assert.That(exitCode, Is.Zero, init.Output);
            }
            await (await CreateClientAsync(autoAccept: true).ConfigureAwait(false)).DisposeAsync()
                .ConfigureAwait(false);
            using Certificate serverCertificate = InteropPki.ReissueApplicationCertificate(ServerPki, ca);
            using Certificate clientCertificate = InteropPki.ReissueApplicationCertificate(ClientPki, ca);

            InteropPki.TrustCa(ServerPki, ca, revokeClient
                ? InteropPki.CreateCrl(ca, clientCertificate)
                : InteropPki.CreateCrl(ca));
            InteropPki.TrustCa(ClientPki, ca, InteropPki.CreateCrl(ca));

            (LegacyPeerProcess server, string url) = await StartLegacyServerAsync(autoAccept: false)
                .ConfigureAwait(false);
            using (server)
            {
                await using ClientFixture client = await CreateClientAsync(autoAccept: false).ConfigureAwait(false);
                if (revokeClient)
                {
#pragma warning disable CA2025 // Assert.ThrowsAsync completes the task before the client is disposed
                    ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                        () => ConnectAsync(client, url));
#pragma warning restore CA2025
                    Assert.That(sre.StatusCode, Is.AnyOf(s_clientRejectedCodes), sre.ToString());
                }
                else
                {
                    await ConnectReadAndCloseAsync(client, url).ConfigureAwait(false);
                }
                await server.StopAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Both applications use certificates of one CA, and each trusts only
        /// the CA (with an empty CRL): the 1.5 client and the 2.0 server
        /// accept each other. With the client certificate on the CRL the
        /// server rejects it.
        /// </summary>
        [Test]
        public async Task CaIssuedCertificatesWithLegacyClientAsync([Values] bool revokeClient)
        {
            using Certificate ca = InteropPki.CreateCa("CN=Interop Test CA, O=OPC Foundation");

            // Let each side create its self-signed certificate, then replace
            // it with one of the CA, with the same subject and names.
            ServerFixture<ReferenceServer> initServer = await StartServerAsync(autoAccept: true).ConfigureAwait(false);
            await initServer.StopAsync().ConfigureAwait(false);
            (LegacyPeerProcess init, int exitCode) = await LegacyPeerProcess.RunAsync(
                s_peerTimeout,
                "client", "--url", "opc.tcp://localhost:4840", "--pki", ClientPki, "--init-only", "true")
                .ConfigureAwait(false);
            using (init)
            {
                Assert.That(exitCode, Is.Zero, init.Output);
            }
            using Certificate serverCertificate = InteropPki.ReissueApplicationCertificate(ServerPki, ca);
            using Certificate clientCertificate = InteropPki.ReissueApplicationCertificate(ClientPki, ca);

            InteropPki.TrustCa(ServerPki, ca, revokeClient
                ? InteropPki.CreateCrl(ca, clientCertificate)
                : InteropPki.CreateCrl(ca));
            InteropPki.TrustCa(ClientPki, ca, InteropPki.CreateCrl(ca));

            ServerFixture<ReferenceServer> server = await StartServerAsync(autoAccept: false).ConfigureAwait(false);
            try
            {
                string url = ServerUrl(server);
                if (revokeClient)
                {
                    PeerRun rejected = await RunLegacyClientAsync(
                        url,
                        false,
                        "--expect-connect-error",
                        s_clientRejectedNames).ConfigureAwait(false);
                    rejected.AssertPassed("Connect");
                }
                else
                {
                    PeerRun accepted = await RunLegacyClientAsync(url, false).ConfigureAwait(false);
                    AssertConnected(accepted);
                }
            }
            finally
            {
                await server.StopAsync().ConfigureAwait(false);
            }
        }

        private static void AssertConnected(PeerRun run)
        {
            run.AssertPassed("Connect");
            run.AssertPassed("NamespaceArray");
            run.AssertPassed("CloseSession");
        }

        private Task<(LegacyPeerProcess Server, string Url)> StartLegacyServerAsync(bool autoAccept)
        {
            return LegacyPeerProcess.StartServerAsync(
                ServerPki,
                s_peerTimeout,
                "--autoaccept", autoAccept ? "true" : "false");
        }

        private async Task<ServerFixture<ReferenceServer>> StartServerAsync(bool autoAccept)
        {
            var server = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = autoAccept,
                SecurityNone = true
            };
            await server.LoadConfigurationAsync(ServerPki).ConfigureAwait(false);
            await server.StartAsync().ConfigureAwait(false);
            return server;
        }

        private static string ServerUrl(ServerFixture<ReferenceServer> server)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "opc.tcp://localhost:{0}/{1}",
                server.Port,
                nameof(ReferenceServer));
        }

        private Task<PeerRun> RunLegacyClientAsync(string url, bool autoAccept, params string[] extra)
        {
            return PeerRun.ExecuteAsync(
                s_peerTimeout,
                [
                    "client",
                    "--url", url,
                    "--pki", ClientPki,
                    "--policy", SecurityPolicies.Basic256Sha256,
                    "--mode", nameof(MessageSecurityMode.SignAndEncrypt),
                    "--autoaccept", autoAccept ? "true" : "false",
                    "--checks", "NamespaceArray",
                    .. extra
                ]);
        }

        private async Task<ClientFixture> CreateClientAsync(bool autoAccept)
        {
            var client = new ClientFixture(telemetry: m_telemetry);
            await client.LoadClientConfigurationAsync(ClientPki).ConfigureAwait(false);
            if (!autoAccept)
            {
                client.Config.SecurityConfiguration.AutoAcceptUntrustedCertificates = false;
                if (client.Config.CertificateManager is not CertificateManager manager)
                {
                    Assert.Fail("Unexpected certificate manager " + client.Config.CertificateManager?.GetType());
                    return client;
                }
                manager.AutoAcceptUntrustedCertificates = false;
            }
            return client;
        }

        private static async Task<ISession> ConnectAsync(ClientFixture client, string url)
        {
            ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(new Uri(url))
                .ConfigureAwait(false);
            EndpointDescription description = endpoints.ToArray().First(e =>
                e.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
                e.SecurityMode == MessageSecurityMode.SignAndEncrypt);
            var endpoint = new ConfiguredEndpoint(null, description, EndpointConfiguration.Create(client.Config));
            return await client.ConnectAsync(endpoint).ConfigureAwait(false);
        }

        private static async Task ConnectReadAndCloseAsync(ClientFixture client, string url)
        {
            ISession session = await ConnectAsync(client, url).ConfigureAwait(false);
            try
            {
                DataValue state = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(state.StatusCode), Is.True, state.StatusCode.ToString());
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }
    }
}
