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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Security.Certificates.Tests;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest : ClientTestFramework
    {
        public ClientTest()
            : base(Utils.UriSchemeOpcTcp)
        {
        }

        public ClientTest(string uriScheme = Utils.UriSchemeOpcTcp)
            : base(uriScheme)
        {
        }

        public static readonly NodeId[] TypeSystems =
        [
            ObjectIds.OPCBinarySchema_TypeSystem,
            ObjectIds.XmlSchema_TypeSystem
        ];

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }

        [Test]
        [Order(100)]
        public async Task GetEndpointsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration, telemetry);
            Endpoints = await client.GetEndpointsAsync(null, CancellationToken.None)
                .ConfigureAwait(false);
            StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

            TestContext.Out.WriteLine("Endpoints:");
            foreach (EndpointDescription endpoint in Endpoints)
            {
                TestContext.Out.WriteLine("{0}", endpoint.Server.ApplicationName);
                TestContext.Out.WriteLine("  {0}", endpoint.Server.ApplicationUri);
                TestContext.Out.WriteLine(" {0}", endpoint.EndpointUrl);
                TestContext.Out.WriteLine("  {0}", endpoint.EncodingSupport);
                TestContext.Out.WriteLine(
                    "  {0}/{1}/{2}",
                    endpoint.SecurityLevel,
                    endpoint.SecurityMode,
                    endpoint.SecurityPolicyUri);

                if (endpoint.ServerCertificate != null)
                {
                    using X509Certificate2 cert = X509CertificateLoader.LoadCertificate(
                        endpoint.ServerCertificate);
                    TestContext.Out.WriteLine("  [{0}]", cert.Thumbprint);
                }
                else
                {
                    TestContext.Out.WriteLine("  [no certificate]");
                }

                foreach (UserTokenPolicy userIdentity in endpoint.UserIdentityTokens)
                {
                    TestContext.Out.WriteLine("  {0}", userIdentity.TokenType);
                    TestContext.Out.WriteLine("  {0}", userIdentity.PolicyId);
                    TestContext.Out.WriteLine("  {0}", userIdentity.SecurityPolicyUri);
                }
            }
        }

        [Test]
        [Order(100)]
        public async Task FindServersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration, telemetry);
            ApplicationDescriptionCollection servers = await client.FindServersAsync(null)
                .ConfigureAwait(false);
            StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

            foreach (ApplicationDescription server in servers)
            {
                TestContext.Out.WriteLine("{0}", server.ApplicationName);
                TestContext.Out.WriteLine("  {0}", server.ApplicationUri);
                TestContext.Out.WriteLine("  {0}", server.ApplicationType);
                TestContext.Out.WriteLine("  {0}", server.ProductUri);
                foreach (string discoveryUrl in server.DiscoveryUrls)
                {
                    TestContext.Out.WriteLine("  {0}", discoveryUrl);
                }
            }
        }

        [Test]
        [Order(100)]
        public async Task FindServersOnNetworkAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration, telemetry);
            try
            {
                FindServersOnNetworkResponse response = await client
                    .FindServersOnNetworkAsync(null, 0, 100, null, CancellationToken.None)
                    .ConfigureAwait(false);
                StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

                foreach (ServerOnNetwork server in response.Servers)
                {
                    TestContext.Out.WriteLine("{0}", server.ServerName);
                    TestContext.Out.WriteLine("  {0}", server.RecordId);
                    TestContext.Out.WriteLine("  {0}", server.ServerCapabilities);
                    TestContext.Out.WriteLine("  {0}", server.DiscoveryUrl);
                }
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                .BadServiceUnsupported)
            {
                NUnit.Framework.Assert.Ignore("FindServersOnNetwork not supported on server.");
            }
        }

        /// <summary>
        /// Try to use the discovery channel to read a node.
        /// </summary>
        [Test]
        [Order(105)]
        [TestCase(1000)]
        [TestCase(10000)]
        public async Task ReadOnDiscoveryChannelAsync(int readCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration, telemetry);
            EndpointDescriptionCollection endpoints =
                await client.GetEndpointsAsync(null).ConfigureAwait(false);
            Assert.NotNull(endpoints);

            // cast Innerchannel to ISessionChannel
            ITransportChannel channel = client.TransportChannel;

            var sessionClient = new SessionClient(channel, telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.All
            };

            var request = new ReadRequest { RequestHeader = null };

            var readMessage = new ReadMessage { ReadRequest = request };

            var readValueId = new ReadValueId
            {
                NodeId = new NodeId(Guid.NewGuid()),
                AttributeId = Attributes.Value
            };

            var readValues = new ReadValueIdCollection();
            for (int i = 0; i < readCount; i++)
            {
                readValues.Add(readValueId);
            }

            // try to read nodes using discovery channel
            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                sessionClient.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    readValues,
                    default));
            StatusCode statusCode = StatusCodes.BadSecurityPolicyRejected;
            // race condition, if socket closed is detected before the error was returned,
            // client may report channel closed instead of security policy rejected
            if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
            {
                NUnit.Framework.Assert.Inconclusive($"Unexpected Status: {sre}");
            }
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadSecurityPolicyRejected,
                (StatusCode)sre.StatusCode,
                "Unexpected Status: {0}",
                sre);
        }

        /// <summary>
        /// GetEndpoints on the discovery channel,
        /// but an oversized message should return an error.
        /// </summary>
        [Test]
        [Order(105)]
        [TestCase(false)]
        public async Task GetEndpointsOnDiscoveryChannelAsync(bool securityNoneEnabled)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration, telemetry);
            var profileUris = new StringCollection();
            for (int i = 0; i < 10000; i++)
            {
                // dummy uri to create a bigger message
                profileUris.Add($"https://opcfoundation.org/ProfileUri={i}");
            }

            if (securityNoneEnabled)
            {
                // test can pass, there is no limit for discovery messages
                // because the server supports security None
                await client.GetEndpointsAsync(profileUris).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException sre = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(() =>
                        client.GetEndpointsAsync(profileUris));
                // race condition, if socket closed is detected before the error was returned,
                // client may report channel closed instead of security policy rejected
                if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
                {
                    NUnit.Framework.Assert.Inconclusive($"Unexpected Status: {sre}");
                }
                Assert.AreEqual(
                    (StatusCode)StatusCodes.BadSecurityPolicyRejected,
                    (StatusCode)sre.StatusCode,
                    "Unexpected Status: {0}",
                    sre);
            }
        }

        [Test]
        [Order(110)]
        public async Task InvalidConfigurationAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var applicationInstance = new ApplicationInstance(telemetry)
            {
                ApplicationName = ClientFixture.Config.ApplicationName
            };
            Assert.NotNull(applicationInstance);

            CertificateIdentifierCollection applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    ClientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName);

            _ = await applicationInstance
                .Build(ClientFixture.Config.ApplicationUri, ClientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(applicationCerts)
                .CreateAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Test that connecting with an unsupported security policy throws BadSecurityPolicyRejected
        /// instead of BadUserAccessDenied.
        /// </summary>
        [Test]
        [Order(115)]
        public async Task ConnectWithUnsupportedSecurityPolicyAsync()
        {
            // Get endpoints from server
            var endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            Assert.NotNull(endpoints);
            Assert.Greater(endpoints.Count, 0);

            // Find a security policy that is NOT supported by the server
            // by checking all available policies and finding one not in the endpoints
            string unsupportedPolicy = null;
            var supportedPolicies = new HashSet<string>(
                endpoints.Select(e => e.SecurityPolicyUri));

            // Try to find an unsupported policy from the standard set
            foreach (var policy in new[] {
                SecurityPolicies.Aes256_Sha256_RsaPss,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP384r1,
                SecurityPolicies.Basic128Rsa15
            })
            {
                if (!supportedPolicies.Contains(policy))
                {
                    unsupportedPolicy = policy;
                    break;
                }
            }

            // If we can't find an unsupported policy, skip the test
            if (unsupportedPolicy == null)
            {
                NUnit.Framework.Assert.Ignore("Could not find an unsupported security policy for testing.");
                return;
            }

            TestContext.Out.WriteLine(
                $"Testing connection with unsupported security policy: {unsupportedPolicy}");

            // Create a configured endpoint with the unsupported security policy
            // Use the first available endpoint but override the security policy
            var baseEndpoint = endpoints[0];
            var endpointDescription = new EndpointDescription
            {
                EndpointUrl = baseEndpoint.EndpointUrl,
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = unsupportedPolicy,
                SecurityLevel = baseEndpoint.SecurityLevel,
                Server = baseEndpoint.Server,
                ServerCertificate = baseEndpoint.ServerCertificate,
                TransportProfileUri = baseEndpoint.TransportProfileUri,
                UserIdentityTokens = baseEndpoint.UserIdentityTokens
            };

            var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
            endpointConfiguration.OperationTimeout = ClientFixture.OperationTimeout;
            var configuredEndpoint = new ConfiguredEndpoint(
                null,
                endpointDescription,
                endpointConfiguration);

            // Try to connect with the unsupported security policy
            var ex = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                await ClientFixture.ConnectAsync(configuredEndpoint).ConfigureAwait(false);
            });

            // Verify we get BadSecurityPolicyRejected or BadSecurityModeRejected, not BadUserAccessDenied
            Assert.That(
                (StatusCode)ex.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadSecurityPolicyRejected)
                    .Or.EqualTo((StatusCode)StatusCodes.BadSecurityModeRejected),
                $"Expected BadSecurityPolicyRejected or BadSecurityModeRejected, but got {ex.StatusCode}: {ex.Message}");

            TestContext.Out.WriteLine($"Correctly received status code: {ex.StatusCode}");
        }

        [Theory]
        [Order(200)]
        public async Task ConnectAndCloseSyncAsync(string securityPolicy)
        {
            bool closeChannel = securityPolicy != SecurityPolicies.Aes128_Sha256_RsaOaep;
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += SessionClosing;
            StatusCode result =
                await session.CloseAsync(5_000, closeChannel).ConfigureAwait(false);
            Assert.NotNull(result);
            session.Dispose();
        }

        [Theory]
        [Order(201)]
        public async Task ConnectAndCloseAsync(string securityPolicy)
        {
            bool closeChannel = securityPolicy != SecurityPolicies.Basic128Rsa15;
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += SessionClosing;
            StatusCode result = await session
                .CloseAsync(5_000, closeChannel, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test]
        [Order(202)]
        public async Task ConnectAndCloseAsyncReadAfterCloseAsync()
        {
            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            using ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += SessionClosing;

            var nodeId = new NodeId(VariableIds.ServerStatusType_BuildInfo);
            Node node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);

            // keep channel open/inactive
            StatusCode result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.Good, result);

            await Task.Delay(5_000).ConfigureAwait(false);

            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await session.ReadNodeAsync(nodeId, CancellationToken.None)
                        .ConfigureAwait(false));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadSessionIdInvalid,
                (StatusCode)sre.StatusCode);
        }

        [Test]
        [Order(204)]
        public async Task ConnectAndCloseAsyncReadAfterCloseSessionReconnectAsync()
        {
            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            using ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += SessionClosing;

            IUserIdentity userIdentity = session.Identity;
            string sessionName = session.SessionName;

            var nodeId = new NodeId(VariableIds.ServerStatusType_BuildInfo);
            Node node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);

            // keep channel open/inactive
            StatusCode result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
            Assert.AreEqual((StatusCode)StatusCodes.Good, result);

            await Task.Delay(5_000).ConfigureAwait(false);

            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await session.ReadNodeAsync(nodeId, CancellationToken.None)
                        .ConfigureAwait(false));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadSessionIdInvalid,
                (StatusCode)sre.StatusCode);

            // reconnect/reactivate
            await session.OpenAsync(sessionName, userIdentity, CancellationToken.None)
                .ConfigureAwait(false);

            node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        [Order(206)]
        public async Task ConnectCloseSessionCloseChannelAsync()
        {
            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            using ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += SessionClosing;

            IUserIdentity userIdentity = session.Identity;
            string sessionName = session.SessionName;

            var nodeId = new NodeId(VariableIds.ServerStatusType_BuildInfo);
            Node node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);

            // keep channel opened but detach so no comm goes through
            ITransportChannel channel = session.TransportChannel;
            session.DetachChannel();

            int waitTime =
                ServerFixture.Application.ApplicationConfiguration.TransportQuotas.ChannelLifetime +
                (ServerFixture.Application.ApplicationConfiguration.TransportQuotas
                    .ChannelLifetime /
                    2) +
                5_000;
            await Task.Delay(waitTime).ConfigureAwait(false);

            // Channel handling checked for TcpTransportChannel only
            if (channel is TcpTransportChannel tcp)
            {
                Assert.IsNull(tcp.Socket);
            }
        }

        [Theory]
        [Order(210)]
        public async Task ConnectAndReconnectAsync(bool reconnectAbort, bool useMaxReconnectPeriod)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const int connectTimeout = MaxTimeout;
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session);

            int sessionConfigChanged = 0;
            session.SessionConfigurationChanged += (sender, e) => sessionConfigChanged++;

            int sessionClosing = 0;
            session.SessionClosing += (sender, e) => sessionClosing++;

            var quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler(
                telemetry,
                reconnectAbort,
                useMaxReconnectPeriod ? MaxTimeout : -1);
            reconnectHandler.BeginReconnect(
                session,
                connectTimeout / 5,
                (sender, e) =>
                {
                    // ignore callbacks from discarded objects.
                    if (!ReferenceEquals(sender, reconnectHandler))
                    {
                        NUnit.Framework.Assert.Fail("Unexpected sender");
                    }

                    if (reconnectHandler.Session != null)
                    {
                        if (!ReferenceEquals(reconnectHandler.Session, session))
                        {
                            session.Dispose();
                            session = reconnectHandler.Session;
                            TestContext.Out.WriteLine("Reconnected with new session.");
                        }
                        else
                        {
                            TestContext.Out.WriteLine("Reconnected with the same session.");
                        }
                    }
                    else
                    {
                        TestContext.Out.WriteLine("Reconnect aborted reusing secure channel.");
                    }

                    quitEvent.Set();
                });

            bool timeout = quitEvent.WaitOne(connectTimeout);
            Assert.True(timeout);

            if (reconnectAbort)
            {
                Assert.IsNull(reconnectHandler.Session);
            }
            else
            {
                Assert.AreEqual(session, reconnectHandler.Session);
            }

            Assert.AreEqual(reconnectAbort ? 0 : 1, sessionConfigChanged);
            Assert.AreEqual(0, sessionClosing);

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            Assert.NotNull(result);
            reconnectHandler.Dispose();
            session.Dispose();

            Assert.Less(0, sessionClosing);
        }

        [Theory]
        [Order(220)]
        public async Task ConnectJWTAsync(string securityPolicy)
        {
            byte[] identityToken = "fakeTokenString"u8.ToArray();

            var issuedToken = new IssuedIdentityToken
            {
                IssuedTokenType = IssuedTokenType.JWT,
                PolicyId = Profiles.JwtUserToken,
                DecryptedTokenData = identityToken
            };

            var userIdentity = new UserIdentity(issuedToken);

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            byte[] receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.AreEqual(identityToken, receivedToken);

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            Assert.NotNull(result);

            session.Dispose();
        }

        [Theory]
        [Order(230)]
        public async Task ReconnectJWTAsync(string securityPolicy)
        {
            static UserIdentity CreateUserIdentity(byte[] tokenData)
            {
                var issuedToken = new IssuedIdentityToken
                {
                    IssuedTokenType = IssuedTokenType.JWT,
                    PolicyId = Profiles.JwtUserToken,
                    DecryptedTokenData = tokenData
                };

                return new UserIdentity(issuedToken);
            }

            byte[] identityToken = "fakeTokenString"u8.ToArray();
            UserIdentity userIdentity = CreateUserIdentity(identityToken);

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity)
                .ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            byte[] receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.AreEqual(identityToken, receivedToken);
            Array.Clear(receivedToken, 0, receivedToken.Length);

            byte[] newIdentityToken = "fakeTokenStringNew"u8.ToArray();
            session.RenewUserIdentity += (_, _) => CreateUserIdentity(newIdentityToken);

            await session.ReconnectAsync().ConfigureAwait(false);
            receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.AreEqual(newIdentityToken, receivedToken);
            Array.Clear(receivedToken, 0, receivedToken.Length);

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            Assert.NotNull(result);

            session.Dispose();
        }

        [Test]
        [Order(240)]
        public async Task ConnectMultipleSessionsAsync()
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(endpoint);

            ITransportChannel channel = await ClientFixture.CreateChannelAsync(endpoint, false)
                .ConfigureAwait(false);
            Assert.NotNull(channel);

            ISession session1 = ClientFixture.CreateSession(channel, endpoint);
            await session1.OpenAsync("Session1", null, CancellationToken.None).ConfigureAwait(false);

            ISession session2 = ClientFixture.CreateSession(channel, endpoint);
            await session2.OpenAsync("Session2", null, CancellationToken.None).ConfigureAwait(false);

            await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);

            await session1.CloseAsync(closeChannel: false).ConfigureAwait(false);
            session1.DetachChannel();
            session1.Dispose();

            await session2.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);

            await session2.CloseAsync(closeChannel: false).ConfigureAwait(false);
            session2.DetachChannel();
            session2.Dispose();

            //Recreate session using same channel
            ISession session3 = await ClientFixture
                .SessionFactory.RecreateAsync(session1, channel)
                .ConfigureAwait(false);

            await session3.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);

            channel.Dispose();
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate) the
        /// same session on a new channel.
        /// Close the first channel before or after the new channel is activated.
        /// </summary>
        [Theory]
        [Order(250)]
        public async Task ReconnectSessionOnAlternateChannelAsync(bool closeChannel)
        {
            ServiceResultException sre;

            // the endpoint to use
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(endpoint);

            // the active channel
            ISession session1 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(session1);

            // test by reading a value
            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value1);

            // save the channel to close it later
            ITransportChannel channel1 = session1.TransportChannel;

            // test case: close the channel before reconnecting
            if (closeChannel)
            {
                session1.DetachChannel();
                channel1.Dispose();

                // cannot read using a detached channel
                ServiceResultException exception = NUnit.Framework.Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus).ConfigureAwait(false));
                Assert.AreEqual(
                    (StatusCode)StatusCodes.BadSecureChannelClosed,
                    (StatusCode)exception.StatusCode);
            }

            // the inactive channel
            ITransportChannel channel2 =
                await ClientFixture.CreateChannelAsync(endpoint, false).ConfigureAwait(false);
            Assert.NotNull(channel2);

            // activate the session on the new channel
            await session1.ReconnectAsync(channel2, CancellationToken.None)
                .ConfigureAwait(false);

            // test by reading a value
            ServerStatusDataType value2 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value2);
            Assert.AreEqual(value1.State, value2.State);

            // test case: close the first channel after the session is activated on the new channel
            if (!closeChannel)
            {
                channel1.Close();
                channel1.Dispose();
            }

            // test by reading a value
            ServerStatusDataType value3 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value3);
            Assert.AreEqual(value1.State, value3.State);

            // close the session, keep the channel open
            await session1.CloseAsync(closeChannel: false, CancellationToken.None)
                .ConfigureAwait(false);

            // cannot read using a closed session, validate the status code
            sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadSessionIdInvalid,
                (StatusCode)sre.StatusCode,
                sre.Message);

            // close the channel
            await channel2.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            channel2.Dispose();

            // cannot read using a closed channel, validate the status code
            sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));

            // TODO: Both channel should return BadNotConnected
            if (StatusCodes.BadSecureChannelClosed != sre.StatusCode)
            {
                if (endpoint.EndpointUrl.ToString()
                    .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadNotConnected,
                        (StatusCode)sre.StatusCode,
                        sre.Message);
                }
                else
                {
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadUnknownResponse,
                        (StatusCode)sre.StatusCode,
                        sre.Message);
                }
            }
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets
        /// </summary>
        [Test]
        [Order(260)]
        [TestCase(SecurityPolicies.None, true)]
        [TestCase(SecurityPolicies.None, false)]
        [TestCase(SecurityPolicies.Basic256Sha256, true)]
        [TestCase(SecurityPolicies.Basic256Sha256, false)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1, true)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1, false)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1, true)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1, false)]
        [TestCase(SecurityPolicies.ECC_nistP256, true)]
        [TestCase(SecurityPolicies.ECC_nistP256, false)]
        [TestCase(SecurityPolicies.ECC_nistP384, true)]
        [TestCase(SecurityPolicies.ECC_nistP384, false)]
        public async Task ReconnectSessionOnAlternateChannelWithSavedSessionSecretsAsync(
            string securityPolicy,
            bool anonymous)
        {
            ServiceResultException sre;

            UserIdentity userIdentity = anonymous
                ? new UserIdentity()
                : new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(endpoint);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                NUnit.Framework.Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                    $" / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            Assert.NotNull(session1);

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value1);

            // save the session configuration
            var stream = new MemoryStream();

            session1.SaveSessionConfiguration(stream);

            byte[] streamArray = stream.ToArray();
            TestContext.Out.WriteLine($"SessionSecrets: {stream.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(streamArray));

            // read the session configuration
            var loadStream = new MemoryStream(streamArray);
            var sessionConfiguration = SessionConfiguration.Create(loadStream, Telemetry);

            // create the inactive channel
            ITransportChannel channel2 = await ClientFixture
                .CreateChannelAsync(sessionConfiguration.ConfiguredEndpoint, false)
                .ConfigureAwait(false);
            Assert.NotNull(channel2);

            // prepare the inactive session with the new channel
            ISession session2 = ClientFixture.CreateSession(
                channel2,
                sessionConfiguration.ConfiguredEndpoint);

            // apply the saved session configuration
            bool success = session2.ApplySessionConfiguration(sessionConfiguration);

            // hook callback to renew the user identity
            session2.RenewUserIdentity += (_, _) => userIdentity;

            // activate the session from saved session secrets on the new channel
            await session2.ReconnectAsync(channel2, CancellationToken.None)
                .ConfigureAwait(false);
            Thread.Sleep(500);

            Assert.AreEqual(session1.SessionId, session2.SessionId);

            ServerStatusDataType value2 = await session2.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value2);

            await Task.Delay(500).ConfigureAwait(false);

            // cannot read using a closed channel, validate the status code
            if (endpoint.EndpointUrl.ToString()
                .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                    async () => await session1.ReadValueAsync<ServerStatusDataType>(
                        VariableIds.Server_ServerStatus).ConfigureAwait(false));
                Assert.AreEqual(
                    (StatusCode)StatusCodes.BadSecureChannelIdInvalid,
                    (StatusCode)sre.StatusCode,
                    sre.Message);
            }
            else
            {
                object result = await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false);
                Assert.NotNull(result);
            }

            session1.DeleteSubscriptionsOnClose = true;
            await session1.CloseAsync(1000).ConfigureAwait(false);
            Utils.SilentDispose(session1);

            session2.DeleteSubscriptionsOnClose = true;
            await session2.CloseAsync(1000).ConfigureAwait(false);
            Utils.SilentDispose(session2);
        }

        /// <summary>
        /// Open a session on a channel, then recreate using the session as a template,
        /// verify the renewUserIdentityHandler is brought to the new session and called
        /// before Session.Open
        /// /// </summary>
        [Test]
        [Order(270)]
        public async Task RecreateSessionWithRenewUserIdentityAsync()
        {
            var userIdentityAnonymous = new UserIdentity();
            var userIdentityPW = new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(endpoint);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentityAnonymous.TokenType,
                userIdentityAnonymous.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                NUnit.Framework.Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentityAnonymous.TokenType}" +
                    $" / {userIdentityAnonymous.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentityAnonymous)
                .ConfigureAwait(false);
            Assert.NotNull(session1);

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value1);

            // hook callback to renew the user identity
            session1.RenewUserIdentity += (_, _) => userIdentityPW;

            Session session2 = await Client
                .Session.RecreateAsync((Session)((TraceableSession)session1).Session)
                .ConfigureAwait(false);

            // create new channel
            ITransportChannel channel2 = await ClientFixture
                .CreateChannelAsync(session1.ConfiguredEndpoint, false)
                .ConfigureAwait(false);
            Assert.NotNull(channel2);

            Session session3 = await Client
                .Session.RecreateAsync((Session)((TraceableSession)session1).Session, channel2)
                .ConfigureAwait(false);

            // validate new Session Ids are used and also UserName PW identity token is
            // injected as renewed token
            Assert.AreNotEqual(session1.SessionId, session2.SessionId);
            Assert.AreEqual(userIdentityPW.TokenType, session2.Identity.TokenType);
            Assert.AreNotEqual(session1.SessionId, session3.SessionId);
            Assert.AreEqual(userIdentityPW.TokenType, session3.Identity.TokenType);

            ServerStatusDataType value2 = await session2.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value2);

            session1.DeleteSubscriptionsOnClose = true;
            await session1.CloseAsync(1000).ConfigureAwait(false);
            Utils.SilentDispose(session1);

            session2.DeleteSubscriptionsOnClose = true;
            await session2.CloseAsync(1000).ConfigureAwait(false);
            Utils.SilentDispose(session2);

            session3.DeleteSubscriptionsOnClose = true;
            await session3.CloseAsync(1000).ConfigureAwait(false);
            Utils.SilentDispose(session3);
        }

        [Test]
        [Order(300)]
        public async Task GetOperationLimitsTestAsync()
        {
            await GetOperationLimitsAsync().ConfigureAwait(false);

            ValidateOperationLimit(
                OperationLimits.MaxNodesPerRead,
                Session.OperationLimits.MaxNodesPerRead);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerHistoryReadData,
                Session.OperationLimits.MaxNodesPerHistoryReadData);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerHistoryReadEvents,
                Session.OperationLimits.MaxNodesPerHistoryReadEvents);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerBrowse,
                Session.OperationLimits.MaxNodesPerBrowse);
            ValidateOperationLimit(
                OperationLimits.MaxMonitoredItemsPerCall,
                Session.OperationLimits.MaxMonitoredItemsPerCall);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerHistoryUpdateData,
                Session.OperationLimits.MaxNodesPerHistoryUpdateData);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerHistoryUpdateEvents,
                Session.OperationLimits.MaxNodesPerHistoryUpdateEvents);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerMethodCall,
                Session.OperationLimits.MaxNodesPerMethodCall);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerNodeManagement,
                Session.OperationLimits.MaxNodesPerNodeManagement);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerRegisterNodes,
                Session.OperationLimits.MaxNodesPerRegisterNodes);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds,
                Session.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);
            ValidateOperationLimit(
                OperationLimits.MaxNodesPerWrite,
                Session.OperationLimits.MaxNodesPerWrite);
        }

        [Test]
        public void ReadPublicProperties()
        {
            TestContext.Out.WriteLine("Identity         : {0}", Session.Identity);
            TestContext.Out.WriteLine("IdentityHistory  : {0}", Session.IdentityHistory);
            TestContext.Out.WriteLine("NamespaceUris    : {0}", Session.NamespaceUris.ToString());
            TestContext.Out.WriteLine("ServerUris       : {0}", Session.ServerUris);
            TestContext.Out.WriteLine("SystemContext    : {0}", Session.SystemContext);
            TestContext.Out.WriteLine("Factory          : {0}", Session.Factory);
            TestContext.Out.WriteLine("FilterContext    : {0}", Session.FilterContext);
            TestContext.Out.WriteLine("PreferredLocales : {0}", Session.PreferredLocales);
            TestContext.Out.WriteLine("Subscriptions    : {0}", Session.Subscriptions);
            TestContext.Out.WriteLine("SubscriptionCount: {0}", Session.SubscriptionCount);
            TestContext.Out.WriteLine("DefaultSubscription: {0}", Session.DefaultSubscription);
            TestContext.Out.WriteLine("LastKeepAliveTime: {0}", Session.LastKeepAliveTime);
            TestContext.Out
                .WriteLine("LastKeepAliveTickCount: {0}", Session.LastKeepAliveTickCount);
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval += 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval -= 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            TestContext.Out.WriteLine("KeepAliveStopped : {0}", Session.KeepAliveStopped);
            TestContext.Out
                .WriteLine("OutstandingRequestCount : {0}", Session.OutstandingRequestCount);
            TestContext.Out.WriteLine("DefunctRequestCount     : {0}", Session.DefunctRequestCount);
            TestContext.Out
                .WriteLine("GoodPublishRequestCount : {0}", Session.GoodPublishRequestCount);
        }

        [Test]
        public async Task ChangePreferredLocalesAsync()
        {
            // change locale
            var localeCollection = new StringCollection { "de-de", "en-us" };
            await Session.ChangePreferredLocalesAsync(localeCollection).ConfigureAwait(false);
        }

        [Test]
        public Task ReadValueAsync()
        {
            // Test ReadValue
            Task task1 = Session.ReadValueAsync(
                VariableIds.Server_ServerRedundancy_RedundancySupport);
            Task task2 = Session.ReadValueAsync(VariableIds.Server_ServerStatus);
            Task task3 = Session.ReadValueAsync(VariableIds.Server_ServerStatus_BuildInfo);
            return Task.WhenAll(task1, task2, task3);
        }

        [Test]
        public async Task ReadValueTypedAsync()
        {
            // Test ReadValue
            await Session.ReadValueAsync<int>(
                VariableIds.Server_ServerRedundancy_RedundancySupport).ConfigureAwait(false);
            await Session.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);

            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.ReadValueAsync<ServiceHost>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));
            Assert.AreEqual((StatusCode)StatusCodes.BadTypeMismatch, (StatusCode)sre.StatusCode);
        }

        [Test]
        public async Task ReadValueFromTestSimulationAsync()
        {
            NamespaceTable namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetSimulation(namespaceUris));
            foreach (NodeId nodeId in testSet)
            {
                DataValue dataValue = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
                Assert.NotNull(dataValue);
                Assert.NotNull(dataValue.Value);
                Assert.AreNotEqual(DateTime.MinValue, dataValue.SourceTimestamp);
                Assert.AreNotEqual(DateTime.MinValue, dataValue.ServerTimestamp);
            }
        }

        [Test]
        public async Task ReadValuesAsync()
        {
            NamespaceTable namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            DataValueCollection values;
            IList<ServiceResult> errors;
            (values, errors) = await Session.ReadValuesAsync([.. testSet]).ConfigureAwait(false);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }

        [Test]
        public async Task ReadDataTypeDefinitionAsync()
        {
            // Test Read a DataType Node
            INode node = await Session.ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType)
                .ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinition2Async()
        {
            // Test Read a DataType Node, the nodeclass is known
            INode node = await Session
                .ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType, NodeClass.DataType, false)
                .ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinitionNodesAsync()
        {
            // Test Read a DataType Node, the nodeclass is known
            (IList<Node> nodes, IList<ServiceResult> errors) = await Session
                .ReadNodesAsync([DataTypeIds.ProgramDiagnosticDataType], NodeClass.DataType, false)
                .ConfigureAwait(false);
            Assert.AreEqual(nodes.Count, errors.Count);
            ValidateDataTypeDefinition(nodes[0]);
        }

        private static void ValidateDataTypeDefinition(INode node)
        {
            Assert.NotNull(node);
            var dataTypeNode = (DataTypeNode)node;
            Assert.NotNull(dataTypeNode);
            ExtensionObject dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.NotNull(dataTypeDefinition);
            Assert.NotNull(dataTypeDefinition.Body);
            Assert.True(dataTypeDefinition.Body is StructureDefinition);
            var structureDefinition = dataTypeDefinition.Body as StructureDefinition;
            Assert.AreEqual(
                ObjectIds.ProgramDiagnosticDataType_Encoding_DefaultBinary,
                structureDefinition.DefaultEncodingId);
        }

        [Theory]
        [Order(400)]
        public async Task BrowseFullAddressSpaceAsync(
            string securityPolicy,
            bool operationLimits = false)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            if (OperationLimits == null)
            {
                await GetOperationLimitsAsync().ConfigureAwait(false);
            }

            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            // Session
            ISession session;
            if (securityPolicy != null)
            {
                session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                    .ConfigureAwait(false);
                if (operationLimits)
                {
                    // disable the operation limit handler in SessionClientOperationLimits
                    session.OperationLimits.MaxNodesPerBrowse = 0;
                }
            }
            else
            {
                session = Session;
            }

            var clientTestServices = new ClientTestServices(session, telemetry);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(
                clientTestServices,
                requestHeader,
                operationLimits ? OperationLimits : null,
                outputResult: true);

            if (securityPolicy != null)
            {
                await session.CloseAsync().ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Test]
        [Order(410)]
        [NonParallelizable]
        public async Task ReadDisplayNamesAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }
            var nodeIds = ReferenceDescriptions
                .Select(n => ExpandedNodeId.ToNodeId(n.NodeId, Session.NamespaceUris))
                .ToList();
            if (OperationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > OperationLimits.MaxNodesPerRead)
            {
                // force error
                try
                {
                    Session.OperationLimits.MaxNodesPerRead = 0;
                    ServiceResultException sre = NUnit.Framework.Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await Session.ReadDisplayNameAsync(
                                nodeIds).ConfigureAwait(false));
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadTooManyOperations,
                        (StatusCode)sre.StatusCode);
                    while (nodeIds.Count > 0)
                    {
                        IList<string> displayNames;
                        IList<ServiceResult> errors;
                        (displayNames, errors) = await Session.ReadDisplayNameAsync(
                            [.. nodeIds.Take((int)OperationLimits.MaxNodesPerRead)]).ConfigureAwait(false);
                        foreach (string name in displayNames)
                        {
                            TestContext.Out.WriteLine("{0}", name);
                        }
                        nodeIds = [.. nodeIds.Skip((int)OperationLimits.MaxNodesPerRead)];
                    }
                }
                finally
                {
                    Session.OperationLimits.MaxNodesPerRead = OperationLimits.MaxNodesPerRead;
                }
            }
            else
            {
                IList<string> displayNames;
                IList<ServiceResult> errors;
                (displayNames, errors) =
                    await Session.ReadDisplayNameAsync(nodeIds).ConfigureAwait(false);
                foreach (string name in displayNames)
                {
                    TestContext.Out.WriteLine("{0}", name);
                }
            }
        }

        [Test]
        [Order(480)]
        public void Subscription()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            var clientTestServices = new ClientTestServices(Session, telemetry);
            CommonTestWorkers.SubscriptionTest(clientTestServices, requestHeader);
        }

        [Test]
        [Order(550)]
        public async Task ReadNodeSyncAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                Node node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        DataValue value = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
            }
        }

        [Test]
        [Order(550)]
        public async Task ReadNodeAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        DataValue value = await Session.ReadValueAsync(nodeId)
                            .ConfigureAwait(false);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
            }
        }

        [Test]
        [Order(560)]
        [TestCase(0)]
        [TestCase(MaxReferences)]
        public async Task ReadNodesSyncAsync(int nodeCount)
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            var nodes = new NodeIdCollection(
                ReferenceDescriptions
                    .Take(nodeCount)
                    .Select(reference => ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        Session.NamespaceUris)));

            IList<Node> nodeCollection;
            IList<ServiceResult> errors;
            (nodeCollection, errors) = await Session.ReadNodesAsync(nodes)
                .ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (nodeCollection, errors) = await Session.ReadNodesAsync(
                nodes, NodeClass.Unspecified).ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            int ii = 0;
            var variableNodes = new NodeIdCollection();
            foreach (Node node in nodeCollection)
            {
                Assert.NotNull(node);
                Assert.AreEqual(ServiceResult.Good, errors[ii]);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        DataValue value =
                            await Session.ReadValueAsync(node.NodeId).ConfigureAwait(false);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            (DataValueCollection values, errors) = await Session.ReadValuesAsync(nodes)
                .ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (values, errors) = await Session.ReadValuesAsync(variableNodes)
                .ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.AreEqual(variableNodes.Count, values.Count);
            Assert.AreEqual(variableNodes.Count, errors.Count);
        }

        [Test]
        [Order(570)]
        [TestCase(0)]
        [TestCase(MaxReferences)]
        public async Task ReadNodesAsync(int nodeCount)
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            var nodes = new NodeIdCollection(
                ReferenceDescriptions
                    .Where(reference => reference.NodeClass == NodeClass.Variable)
                    .Take(nodeCount)
                    .Select(reference => ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        Session.NamespaceUris)));

            (IList<Node> nodeCollection, IList<ServiceResult> errors) = await Session
                .ReadNodesAsync(nodes, true)
                .ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (nodeCollection, errors) = await Session
                .ReadNodesAsync(nodes, NodeClass.Unspecified, true)
                .ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            int ii = 0;
            var variableNodes = new NodeIdCollection();
            foreach (Node node in nodeCollection)
            {
                Assert.NotNull(node);
                Assert.AreEqual(ServiceResult.Good, errors[ii]);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        DataValue value = await Session.ReadValueAsync(node.NodeId)
                            .ConfigureAwait(false);
                        Assert.NotNull(value);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            DataValueCollection values;
            (values, errors) = await Session.ReadValuesAsync(nodes).ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (values, errors) = await Session.ReadValuesAsync(variableNodes).ConfigureAwait(false);

            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(variableNodes.Count, values.Count);
            Assert.AreEqual(variableNodes.Count, errors.Count);
        }

        [Test]
        [Order(620)]
        public async Task ReadAvailableEncodingsAsync()
        {
            ServiceResultException sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.ReadAvailableEncodingsAsync(DataTypeIds.BaseDataType)
                    .ConfigureAwait(false));
            Assert.AreEqual((StatusCode)StatusCodes.BadNodeIdInvalid, (StatusCode)sre.StatusCode);
            ReferenceDescriptionCollection encoding = await Session.ReadAvailableEncodingsAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.NotNull(encoding);
            Assert.AreEqual(0, encoding.Count);
        }

        /// <summary>
        /// Transfer the subscription using the native service calls, not the client SDK layer.
        /// </summary>
        /// <remarks>
        /// Create a subscription with a monitored item using the native service calls.
        /// Create a secondary Session.
        /// </remarks>
        [Theory]
        [Order(800)]
        [NonParallelizable]
        public async Task TransferSubscriptionNativeAsync(bool sendInitialData)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ISession transferSession = null;
            try
            {
                var requestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };

                // to validate the behavior of the sendInitialValue flag,
                // use a static variable to avoid sampled notifications in publish requests
                NamespaceTable namespaceUris = Session.NamespaceUris;
                NodeId[] testSet =
                [
                    .. CommonTestWorkers.NodeIdTestSetStatic
                        .Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris))
                ];
                var clientTestServices = new ClientTestServices(Session, telemetry);
                UInt32Collection subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(
                    clientTestServices,
                    requestHeader,
                    testSet,
                    0,
                    -1);

                TestContext.Out.WriteLine("Transfer SubscriptionIds: {0}", subscriptionIds[0]);

                transferSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                    .ConfigureAwait(false);
                Assert.AreNotEqual(Session.SessionId, transferSession.SessionId);

                requestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                var transferTestServices = new ClientTestServices(transferSession, telemetry);
                CommonTestWorkers.TransferSubscriptionTest(
                    transferTestServices,
                    requestHeader,
                    subscriptionIds,
                    sendInitialData,
                    false);

                // verify the notification of message transfer
                requestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                CommonTestWorkers.VerifySubscriptionTransferred(
                    clientTestServices,
                    requestHeader,
                    subscriptionIds,
                    true);

                await transferSession.CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                transferSession?.Dispose();
            }
        }

        /// <summary>
        /// Test class for testing protected methods in TraceableRequestHeaderClientSession
        /// </summary>
        public class TestableTraceableRequestHeaderClientSession : TraceableRequestHeaderClientSession
        {
            public TestableTraceableRequestHeaderClientSession(
                ISessionChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint,
                ITelemetryContext telemetry)
                : base(channel, configuration, endpoint)
            {
            }

            /// <summary>
            /// Expose the protected method for testing
            /// </summary>
            public void TestableUpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                base.UpdateRequestHeader(request, useDefaults);
            }
        }

        public static ActivityContext TestExtractActivityContextFromParameters(
            AdditionalParametersType parameters)
        {
            if (parameters == null)
            {
                return default;
            }

            foreach (KeyValuePair item in parameters.Parameters)
            {
                if (item.Key == "traceparent")
                {
                    string traceparent = item.Value.ToString();
                    int firstDash = traceparent.IndexOf('-', StringComparison.Ordinal);
                    int secondDash = traceparent.IndexOf('-', firstDash + 1);
                    int thirdDash = traceparent.IndexOf('-', secondDash + 1);

                    if (firstDash != -1 && secondDash != -1)
                    {
                        ReadOnlySpan<char> traceIdSpan = traceparent.AsSpan(
                            firstDash + 1,
                            secondDash - firstDash - 1);
                        ReadOnlySpan<char> spanIdSpan = traceparent.AsSpan(
                            secondDash + 1,
                            thirdDash - secondDash - 1);
                        ReadOnlySpan<char> traceFlagsSpan = traceparent.AsSpan(thirdDash + 1);

                        var traceId = ActivityTraceId.CreateFromString(traceIdSpan);
                        var spanId = ActivitySpanId.CreateFromString(spanIdSpan);
                        ActivityTraceFlags traceFlags = traceFlagsSpan.SequenceEqual("01".AsSpan())
                            ? ActivityTraceFlags.Recorded
                            : ActivityTraceFlags.None;
                        return new ActivityContext(traceId, spanId, traceFlags);
                    }
                    return default;
                }
            }

            // no traceparent header found
            return default;
        }

        [Test]
        [Order(900)]
        public async Task ClientTestRequestHeaderUpdateAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Activity rootActivity = new Activity("Test_Activity_Root")
            {
                ActivityTraceFlags = ActivityTraceFlags.Recorded
            }.Start();

            var activityListener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };

            ActivitySource.AddActivityListener(activityListener);

            using (Activity activity = new ActivitySource("TestActivitySource").StartActivity(
                "Test_Activity"))
            {
                if (activity != null && activity.Id != null)
                {
                    ConfiguredEndpoint endpoint = await ClientFixture
                        .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                        .ConfigureAwait(false);
                    Assert.NotNull(endpoint);

                    // Mock the channel and session
                    var channelMock = new Mock<ITransportChannel>();
                    Mock<ISessionChannel> sessionChannelMock = channelMock.As<ISessionChannel>();

                    var testableTraceableRequestHeaderClientSession
                        = new TestableTraceableRequestHeaderClientSession(
                        sessionChannelMock.Object,
                        ClientFixture.Config,
                        endpoint,
                        telemetry);
                    var request = new CreateSessionRequest { RequestHeader = new RequestHeader() };

                    // Mock call TestableUpdateRequestHeader() to simulate the header update
                    testableTraceableRequestHeaderClientSession.TestableUpdateRequestHeader(
                        request,
                        true);

                    // Get the AdditionalHeader from the request
                    ExtensionObject additionalHeader = request.RequestHeader.AdditionalHeader;
                    Assert.NotNull(additionalHeader);

                    // Simulate extraction
                    ActivityContext extractedContext = TestExtractActivityContextFromParameters(
                        additionalHeader.Body as AdditionalParametersType);

                    // Verify that the trace context is propagated.
                    Assert.AreEqual(activity.TraceId, extractedContext.TraceId);
                    Assert.AreEqual(activity.SpanId, extractedContext.SpanId);

                    TestContext.Out.WriteLine(
                        $"Activity TraceId: {activity.TraceId}, Activity SpanId: {activity.SpanId}");
                    TestContext.Out.WriteLine(
                        $"Extracted TraceId: {extractedContext.TraceId}, Extracted SpanId: {extractedContext.SpanId}");
                }
            }

            rootActivity.Stop();
        }

        /// <summary>
        /// Read BuildInfo and ensure the values in the structure are the same as in the properties.
        /// </summary>
        [Test]
        [Order(10000)]
        public async Task ReadBuildInfoAsync()
        {
            var nodes = new NodeIdCollection
            {
                VariableIds.Server_ServerStatus_BuildInfo,
                VariableIds.Server_ServerStatus_BuildInfo_ProductName,
                VariableIds.Server_ServerStatus_BuildInfo_ProductUri,
                VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName,
                VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion,
                VariableIds.Server_ServerStatus_BuildInfo_BuildNumber,
                VariableIds.Server_ServerStatus_BuildInfo_BuildDate
            };

            IList<Node> nodeCollection;
            IList<ServiceResult> errors;
            (nodeCollection, errors) =
                await Session.ReadNodesAsync(nodes).ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            DataValueCollection values;
            IList<ServiceResult> errors2;
            (values, errors2) =
                await Session.ReadValuesAsync(nodes).ConfigureAwait(false);
            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors2.Count);

            IList<VariableNode> variableNodes = [.. nodeCollection.Cast<VariableNode>()];
            Assert.NotNull(variableNodes);

            // test build info contains the equal values as the properties
            var buildInfo = (values[0].Value as ExtensionObject)?.Body as BuildInfo;
            Assert.NotNull(buildInfo);
            Assert.AreEqual(buildInfo.ProductName, values[1].Value);
            Assert.AreEqual(buildInfo.ProductUri, values[2].Value);
            Assert.AreEqual(buildInfo.ManufacturerName, values[3].Value);
            Assert.AreEqual(buildInfo.SoftwareVersion, values[4].Value);
            Assert.AreEqual(buildInfo.BuildNumber, values[5].Value);
            Assert.AreEqual(buildInfo.BuildDate, values[6].Value);
        }

        /// <summary>
        /// Open a session on a channel using ECC encrypted UserIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10100)]
        public async Task OpenSessionECCUserNamePwdIdentityTokenAsync(
            [Values(
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP256r1,
                SecurityPolicies.ECC_brainpoolP384r1
            )] string securityPolicy)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                (securityPolicy != SecurityPolicies.ECC_brainpoolP256r1) ||
                (securityPolicy != SecurityPolicies.ECC_brainpoolP384r1))
            {
                var userIdentity = new UserIdentity("user1", "password"u8);

                // the first channel determines the endpoint
                ConfiguredEndpoint endpoint = await ClientFixture
                    .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                    .ConfigureAwait(false);
                Assert.NotNull(endpoint);

                UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                    userIdentity.TokenType,
                    userIdentity.IssuedTokenType,
                    endpoint.Description.SecurityPolicyUri);
                if (identityPolicy == null)
                {
                    NUnit.Framework.Assert.Ignore(
                        $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                        $" / {userIdentity.IssuedTokenType}");
                }

                // the active channel
                ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                    .ConfigureAwait(false);
                Assert.NotNull(session1);

                ServerStatusDataType value1 =
                    await session1.ReadValueAsync<ServerStatusDataType>(
                        VariableIds.Server_ServerStatus).ConfigureAwait(false);
                Assert.NotNull(value1);
            }
        }

        /// <summary>
        /// Open a session on a channel using ECC encrypted IssuedIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10200)]
        public async Task OpenSessionECCIssuedIdentityTokenAsync(
            [Values(
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP256r1,
                SecurityPolicies.ECC_brainpoolP384r1
            )] string securityPolicy)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                (securityPolicy != SecurityPolicies.ECC_brainpoolP256r1) ||
                (securityPolicy != SecurityPolicies.ECC_brainpoolP384r1))
            {
                const string identityToken = "fakeTokenString";

                var issuedToken = new IssuedIdentityToken
                {
                    IssuedTokenType = IssuedTokenType.JWT,
                    PolicyId = Profiles.JwtUserToken,
                    DecryptedTokenData = Encoding.UTF8.GetBytes(identityToken)
                };

                var userIdentity = new UserIdentity(issuedToken);

                // the first channel determines the endpoint
                ConfiguredEndpoint endpoint = await ClientFixture
                    .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                    .ConfigureAwait(false);
                Assert.NotNull(endpoint);

                UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                    userIdentity.TokenType,
                    userIdentity.IssuedTokenType,
                    securityPolicy);

                if (identityPolicy == null)
                {
                    NUnit.Framework.Assert.Ignore(
                        $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                        $" / {userIdentity.IssuedTokenType}");
                }

                // the active channel
                ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                    .ConfigureAwait(false);
                Assert.NotNull(session1);

                ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false);
                Assert.NotNull(value1);
            }
        }

#if ECC_SUPPORT
        /// <summary>
        /// Open a session on a channel using ECC encrypted UserCertificateIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10300)]
        public async Task OpenSessionECCUserCertIdentityTokenAsync(
            [Values(
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP256r1,
                SecurityPolicies.ECC_brainpoolP384r1
            )]
                string securityPolicy)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var eccCurveHashPairs = new ECCurveHashPairCollection
            {
                { ECCurve.NamedCurves.nistP256, HashAlgorithmName.SHA256 },
                { ECCurve.NamedCurves.nistP384, HashAlgorithmName.SHA384 }
            };
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                eccCurveHashPairs.AddRange(
                    new ECCurveHashPairCollection
                    {
                        { ECCurve.NamedCurves.brainpoolP256r1, HashAlgorithmName.SHA256 },
                        { ECCurve.NamedCurves.brainpoolP384r1, HashAlgorithmName.SHA384 }
                    });
            }

            foreach (ECCurveHashPair eccurveHashPair in eccCurveHashPairs)
            {
                string extractedFriendlyNamae = null;
                string[] friendlyNameContext = securityPolicy.Split('_');
                if (friendlyNameContext.Length > 1)
                {
                    extractedFriendlyNamae = friendlyNameContext[1];
                }
                if (eccurveHashPair.Curve.Oid.FriendlyName
                    .Contains(extractedFriendlyNamae, StringComparison.Ordinal))
                {
                    X509Certificate2 cert = CertificateBuilder
                        .Create("CN=Client Test ECC Subject, O=OPC Foundation")
                        .SetECCurve(eccurveHashPair.Curve)
                        .CreateForECDsa();

                    var userIdentity = new UserIdentity(cert, telemetry);

                    // the first channel determines the endpoint
                    ConfiguredEndpoint endpoint = await ClientFixture
                        .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                        .ConfigureAwait(false);
                    Assert.NotNull(endpoint);

                    UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                        userIdentity.TokenType,
                        userIdentity.IssuedTokenType,
                        endpoint.Description.SecurityPolicyUri);
                    if (identityPolicy == null)
                    {
                        NUnit.Framework.Assert.Ignore(
                            $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                            $" / {userIdentity.IssuedTokenType}");
                    }

                    // the active channel
                    ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                        .ConfigureAwait(false);
                    Assert.NotNull(session1);

                    ServerStatusDataType value1 =
                        await session1.ReadValueAsync<ServerStatusDataType>(
                        VariableIds.Server_ServerStatus).ConfigureAwait(false);
                    Assert.NotNull(value1);
                }
            }
        }
#endif

        /// <summary>
        /// Happy SetSubscriptionDurable
        /// </summary>
        [Test]
        [Order(11000)]
        public async Task SetSubscriptionDurableSuccessAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const uint expectedRevised = 5;

            var outputParameters = new List<object> { expectedRevised };

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            if (result)
            {
                Assert.AreEqual(expectedRevised, revised);
            }
            else
            {
                NUnit.Framework.Assert.Fail("Unexpected Error in SetSubscriptionDurable");
            }
        }

        /// <summary>
        /// SetSubscriptionDurable Typical Failure
        /// </summary>
        [Test]
        [Order(11010)]
        public async Task SetSubscriptionDurableExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.IsFalse(result);
        }

        /// <summary>
        /// SetSubscriptionDurable No Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11020)]
        public async Task SetSubscriptionDurableNoOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var outputParameters = new List<object>();

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.IsFalse(result);
        }

        /// <summary>
        /// SetSubscriptionDurable No Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11030)]
        public async Task SetSubscriptionDurableNullOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            List<object> outputParameters = null;

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.IsFalse(result);
        }

        /// <summary>
        /// SetSubscriptionDurable with in invalid number of Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11040)]
        public async Task SetSubscriptionDurableTooManyOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const uint expectedRevised = 5;

            var outputParameters = new List<object> { expectedRevised, expectedRevised };

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.IsFalse(result);
        }

        /// <summary>
        /// GetMonitoredItems Success Case
        /// </summary>
        [Test]
        [Order(11100)]
        public async Task GetMonitoredItemsSuccessAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var outputParameters = new List<object> {
                new uint[] { 1, 2, 3, 4, 5 },
                new uint[] { 6, 7, 8, 9, 10 } };

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            UInt32Collection serverHandles;
            UInt32Collection clientHandles;
            (bool success, serverHandles, clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.IsTrue(success);
            Assert.AreEqual(5, serverHandles.Count);
            Assert.AreEqual(5, clientHandles.Count);
        }

        /// <summary>
        /// GetMonitoredItems Error Case
        /// </summary>
        [Test]
        [Order(11110)]
        public async Task GetMonitoredItemsExceptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            UInt32Collection serverHandles;
            UInt32Collection clientHandles;
            (bool success, serverHandles, clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.IsFalse(success);
        }

        /// <summary>
        /// GetMonitoredItems No Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11120)]
        public async Task GetMonitoredItemsNoOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var outputParameters = new List<object>();

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            UInt32Collection serverHandles;
            UInt32Collection clientHandles;
            (bool success, serverHandles, clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.IsFalse(success);
        }

        /// <summary>
        /// GetMonitoredItems Null Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11130)]
        public async Task GetMonitoredItemsNullOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            List<object> outputParameters = null;

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            UInt32Collection serverHandles;
            UInt32Collection clientHandles;
            (bool success, serverHandles, clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.IsFalse(success);
        }

        /// <summary>
        /// GetMonitoredItems invalid number of Output Parameters
        /// Not an expected case
        /// </summary>
        [Test]
        [Order(11140)]
        public async Task GetMonitoredItemsTooManyOutputParametersAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var outputParameters = new List<object>
            {
                new uint[] { 1, 2, 3, 4, 5 },
                new uint[] { 6, 7, 8, 9, 10 },
                new uint[] { 11, 12, 13, 14, 15 }
            };

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<uint>()))
                .ReturnsAsync(outputParameters);

            var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            UInt32Collection serverHandles;
            UInt32Collection clientHandles;
            (bool success, serverHandles, clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);

            Assert.IsFalse(success);
        }

        /// <summary>
        /// Benchmark wrapper for browse tests.
        /// </summary>
        [Benchmark]
        public async Task BrowseFullAddressSpaceBenchmarkAsync()
        {
            await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
        }

        private static void ValidateOperationLimit(uint serverLimit, uint clientLimit)
        {
            if (serverLimit != 0)
            {
                Assert.GreaterOrEqual(serverLimit, clientLimit);
                Assert.NotZero(clientLimit);
            }
        }
    }
}
