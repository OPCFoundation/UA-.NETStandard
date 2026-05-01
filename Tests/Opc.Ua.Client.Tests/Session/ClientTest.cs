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
    [Parallelizable(ParallelScope.Fixtures)]
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

        public static readonly string[] SupportedEccPolicies =
        [
            .. GetSupportedEccPolicyUris(includeCurvePolicies: false)
        ];

        public static readonly string[] SupportedEccX509Policies =
        [
            .. SupportedEccPolicies.Where(policyUri =>
            {
                CertificateKeyAlgorithm certificateKeyAlgorithm =
                    SecurityPolicies.GetInfo(policyUri).CertificateKeyAlgorithm;
                return certificateKeyAlgorithm is not CertificateKeyAlgorithm.Curve25519 and
                    not CertificateKeyAlgorithm.Curve448;
            })
        ];

        public static IEnumerable<TestCaseData> ReconnectSessionOnAlternateChannelWithSavedSessionSecretsEccTestCases()
        {
            foreach (string securityPolicy in SupportedEccPolicies)
            {
                yield return new TestCaseData(securityPolicy, true);
                yield return new TestCaseData(securityPolicy, false);
            }
        }

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

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                telemetry).ConfigureAwait(false);
            Endpoints = await client.GetEndpointsAsync(default, CancellationToken.None)
                .ConfigureAwait(false);
            StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

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

                if (!endpoint.ServerCertificate.IsEmpty)
                {
                    using X509Certificate2 cert = CertificateFactory.Create(
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

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                telemetry).ConfigureAwait(false);
            ArrayOf<ApplicationDescription> servers = await client.FindServersAsync(default)
                .ConfigureAwait(false);
            StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

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

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                telemetry).ConfigureAwait(false);
            try
            {
                FindServersOnNetworkResponse response = await client
                    .FindServersOnNetworkAsync(null, 0, 100, default, CancellationToken.None)
                    .ConfigureAwait(false);
                StatusCode statusCode = await client.CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(statusCode, Is.EqualTo(StatusCodes.Good));

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
                Assert.Ignore("FindServersOnNetwork not supported on server.");
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

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                telemetry).ConfigureAwait(false);
            ArrayOf<EndpointDescription> endpoints =
                await client.GetEndpointsAsync(default).ConfigureAwait(false);
            Assert.That(endpoints.IsNull, Is.False);

            ITransportChannel channel = client.TransportChannel;
            var sessionClient = new SessionClient(channel, telemetry)
            {
                ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText
            };

            var request = new ReadRequest { RequestHeader = null };

            var readValueId = new ReadValueId
            {
                NodeId = new NodeId(Guid.NewGuid()),
                AttributeId = Attributes.Value
            };

            var readValues = new List<ReadValueId>();
            for (int i = 0; i < readCount; i++)
            {
                readValues.Add(readValueId);
            }

            // try to read nodes using discovery channel
            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sessionClient.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    readValues,
                    default).ConfigureAwait(false));
            StatusCode statusCode = StatusCodes.BadSecurityPolicyRejected;
            // race condition, if socket closed is detected before the error was returned,
            // client may report channel closed instead of security policy rejected
            if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
            {
                Assert.Inconclusive($"Unexpected Status: {sre}");
            }
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityPolicyRejected),
                $"Unexpected Status: {sre}");
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

            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                telemetry).ConfigureAwait(false);
            var profileUris = new List<string>();
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
                ServiceResultException sre = Assert
                    .ThrowsAsync<ServiceResultException>(() =>
                        client.GetEndpointsAsync(profileUris));
                // race condition, if socket closed is detected before the error was returned,
                // client may report channel closed instead of security policy rejected
                if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
                {
                    Assert.Inconclusive($"Unexpected Status: {sre}");
                }
                Assert.That(
                    sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadSecurityPolicyRejected),
                    $"Unexpected Status: {sre}");
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
            Assert.That(applicationInstance, Is.Not.Null);

            ArrayOf<CertificateIdentifier> applicationCerts =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    ClientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName);

            _ = await applicationInstance
                .Build(ClientFixture.Config.ApplicationUri, ClientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(applicationCerts)
                .CreateAsync()
                .ConfigureAwait(false);
        }

        [Theory]
        [Order(200)]
        public async Task ConnectAndCloseSyncAsync(string securityPolicy)
        {
            bool closeChannel = securityPolicy != SecurityPolicies.Aes128_Sha256_RsaOaep;
            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(session, Is.Not.Null);
            Session.SessionClosing += SessionClosing;
            StatusCode result =
                await session.CloseAsync(5_000, closeChannel).ConfigureAwait(false);
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
            Assert.That(session, Is.Not.Null);
            Session.SessionClosing += SessionClosing;
            StatusCode result = await session
                .CloseAsync(5_000, closeChannel, CancellationToken.None)
                .ConfigureAwait(false);
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
            Assert.That(session, Is.Not.Null);
            Session.SessionClosing += SessionClosing;

            NodeId nodeId = VariableIds.ServerStatusType_BuildInfo;
            Node node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);

            // keep channel open/inactive
            StatusCode result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));

            await Task.Delay(5_000).ConfigureAwait(false);

            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await session.ReadNodeAsync(nodeId, CancellationToken.None)
                        .ConfigureAwait(false));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadSessionIdInvalid));
        }

        [Test]
        [Order(204)]
        public async Task ConnectAndCloseAsyncReadAfterCloseSessionReconnectAsync()
        {
            const string securityPolicy = SecurityPolicies.Basic256Sha256;
            using ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(session, Is.Not.Null);
            Session.SessionClosing += SessionClosing;

            IUserIdentity userIdentity = session.Identity;
            string sessionName = session.SessionName;

            NodeId nodeId = VariableIds.ServerStatusType_BuildInfo;
            Node node = await session.ReadNodeAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(nodeId, CancellationToken.None)
                .ConfigureAwait(false);

            // keep channel open/inactive
            StatusCode result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));

            await Task.Delay(5_000).ConfigureAwait(false);

            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await session.ReadNodeAsync(nodeId, CancellationToken.None)
                        .ConfigureAwait(false));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadSessionIdInvalid));

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
            Assert.That(session, Is.Not.Null);
            Session.SessionClosing += SessionClosing;

            IUserIdentity userIdentity = session.Identity;
            string sessionName = session.SessionName;

            NodeId nodeId = VariableIds.ServerStatusType_BuildInfo;
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
                Assert.That(tcp.Socket, Is.Null);
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
            Assert.That(session, Is.Not.Null);

            int sessionConfigChanged = 0;
            session.SessionConfigurationChanged += (sender, e) => sessionConfigChanged++;

            int sessionClosing = 0;
            session.SessionClosing += (sender, e) => sessionClosing++;

            var quitEvent = new ManualResetEvent(false);
#pragma warning disable CS0618 // Type or member is obsolete
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
                        Assert.Fail("Unexpected sender");
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
            Assert.That(timeout, Is.True);

            if (reconnectAbort)
            {
                Assert.That(reconnectHandler.Session, Is.Null);
            }
            else
            {
                Assert.That(reconnectHandler.Session, Is.EqualTo(session));
            }

            Assert.That(sessionConfigChanged, Is.EqualTo(reconnectAbort ? 0 : 1));
            Assert.That(sessionClosing, Is.Zero);

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            reconnectHandler.Dispose();
#pragma warning restore CS0618 // Type or member is obsolete
            session.Dispose();

            Assert.That(sessionClosing, Is.GreaterThanOrEqualTo(0));
        }

        [Theory]
        [Order(220)]
        public async Task ConnectJWTAsync(string securityPolicy)
        {
            byte[] identityToken = "fakeTokenString"u8.ToArray();
            using var issuedToken = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, identityToken);
            using var userIdentity = new UserIdentity(issuedToken);

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity)
                .ConfigureAwait(false);
            Assert.That(session, Is.Not.Null);
            Assert.That(TokenValidator.LastIssuedToken, Is.Not.Null);

            byte[] receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.That(receivedToken, Is.EqualTo(identityToken));

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            session.Dispose();
        }

        [Theory]
        [Order(230)]
        public async Task ReconnectJWTAsync(string securityPolicy)
        {
            static UserIdentity CreateUserIdentity(byte[] tokenData)
            {
                var issuedToken = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, tokenData);
                return new UserIdentity(issuedToken);
            }

            byte[] identityToken = "fakeTokenString"u8.ToArray();
            using UserIdentity userIdentity = CreateUserIdentity(identityToken);

            ISession session = await ClientFixture
                .ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity)
                .ConfigureAwait(false);
            Assert.That(session, Is.Not.Null);
            Assert.That(TokenValidator.LastIssuedToken, Is.Not.Null);

            byte[] receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.That(receivedToken, Is.EqualTo(identityToken));
            Array.Clear(receivedToken, 0, receivedToken.Length);

            byte[] newIdentityToken = "fakeTokenStringNew"u8.ToArray();
            session.RenewUserIdentity += (_, _) => CreateUserIdentity(newIdentityToken);

            await session.ReconnectAsync().ConfigureAwait(false);
            receivedToken = TokenValidator.LastIssuedToken.DecryptedTokenData;
            Assert.That(receivedToken, Is.EqualTo(newIdentityToken));
            Array.Clear(receivedToken, 0, receivedToken.Length);

            StatusCode result = await session.CloseAsync().ConfigureAwait(false);
            session.Dispose();
        }

        [Test]
        [Order(240)]
        public async Task ConnectMultipleSessionsAsync()
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);

            ITransportChannel channel = await ClientFixture.CreateChannelAsync(endpoint, false)
                .ConfigureAwait(false);
            Assert.That(channel, Is.Not.Null);

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
            using ISession session3 = await ClientFixture
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
            Assert.That(endpoint, Is.Not.Null);

            // the active channel
            ISession session1 = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.That(session1, Is.Not.Null);

            // test by reading a value
            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value1, Is.Not.Null);

            // save the channel to close it later
            ITransportChannel channel1 = session1.TransportChannel;

            // test case: close the channel before reconnecting
            if (closeChannel)
            {
                session1.DetachChannel();
                channel1.Dispose();

                // cannot read using a detached channel
                ServiceResultException exception = Assert
                    .ThrowsAsync<ServiceResultException>(async () =>
                        await session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus).ConfigureAwait(false));
                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadSecureChannelClosed));
            }

            // the inactive channel
            ITransportChannel channel2 =
                await ClientFixture.CreateChannelAsync(endpoint, false).ConfigureAwait(false);
            Assert.That(channel2, Is.Not.Null);

            // activate the session on the new channel
            await session1.ReconnectAsync(channel2, CancellationToken.None)
                .ConfigureAwait(false);

            // test by reading a value
            ServerStatusDataType value2 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value2, Is.Not.Null);
            Assert.That(value2.State, Is.EqualTo(value1.State));

            if (!closeChannel)
            {
                // Closing channel should throw because it was disposed during reconnect
                Assert.ThrowsAsync<ObjectDisposedException>(
                    () => channel1.CloseAsync(default).AsTask());
                // Calling dispose twice will not throw.
                Assert.DoesNotThrow(channel1.Dispose);
            }

            // test by reading a value
            ServerStatusDataType value3 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value3, Is.Not.Null);
            Assert.That(value3.State, Is.EqualTo(value1.State));

            // close the session, keep the channel open
            await session1.CloseAsync(closeChannel: false, CancellationToken.None)
                .ConfigureAwait(false);

            // cannot read using a closed session, validate the status code
            sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadSessionIdInvalid),
                sre.Message);

            // close the channel
            await channel2.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            channel2.Dispose();

            // cannot read using a closed channel, validate the status code
            sre = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));

            if (StatusCodes.BadSecureChannelClosed != sre.StatusCode)
            {
                Assert.That(
                    sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadNotConnected),
                    sre.Message);
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
        [TestCase(SecurityPolicies.RSA_DH_AesGcm, true)]
        [TestCase(SecurityPolicies.RSA_DH_AesGcm, false)]
        [TestCase(SecurityPolicies.RSA_DH_ChaChaPoly, true)]
        [TestCase(SecurityPolicies.RSA_DH_ChaChaPoly, false)]
        [TestCaseSource(nameof(ReconnectSessionOnAlternateChannelWithSavedSessionSecretsEccTestCases))]
        public async Task ReconnectSessionOnAlternateChannelWithSavedSessionSecretsAsync(
            string securityPolicy,
            bool anonymous)
        {
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);

            ServiceResultException sre;

            using UserIdentity userIdentity = anonymous
                ? new UserIdentity()
                : new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                    $" / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            Assert.That(session1, Is.Not.Null);

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value1, Is.Not.Null);

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
            Assert.That(channel2, Is.Not.Null);

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

            Assert.That(session2.SessionId, Is.EqualTo(session1.SessionId));

            ServerStatusDataType value2 = await session2.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value2, Is.Not.Null);

            await Task.Delay(500).ConfigureAwait(false);

            // cannot read using a closed channel, validate the status code
            if (endpoint.EndpointUrl.ToString()
                .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                sre = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await session1.ReadValueAsync<ServerStatusDataType>(
                        VariableIds.Server_ServerStatus).ConfigureAwait(false));
                Assert.That(
                    sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid),
                    sre.Message);
            }
            else
            {
                object result = await session1.ReadValueAsync<ServerStatusDataType>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false);
                Assert.That(result, Is.Not.Null);
            }

            session1.DeleteSubscriptionsOnClose = true;
            await session1.CloseAsync(1000).ConfigureAwait(false);
            session1?.Dispose();

            session2.DeleteSubscriptionsOnClose = true;
            await session2.CloseAsync(1000).ConfigureAwait(false);
            session2?.Dispose();
        }

        /// <summary>
        /// Open a session on a channel using a usertokenpolicy from another endpoint, then reconnect (activate)
        /// </summary>
        [Test]
        [Order(260)]
        [TestCase(SecurityPolicies.Basic256Sha256, SecurityPolicies.Basic128Rsa15)]
        public async Task ReconnectSession_ReuseUsertokenPolicyAsync(
            string securityPolicy, string userTokenPolicy)
        {
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);
            await IgnoreIfPolicyNotAdvertisedAsync(userTokenPolicy).ConfigureAwait(false);

            using var userIdentity = new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);
            endpoint = new ConfiguredEndpoint(
                null,
                (EndpointDescription)endpoint.Description.MemberwiseClone(),
                endpoint.Configuration);
            ConfiguredEndpoint tokenPolicyEndpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, userTokenPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.That(tokenPolicyEndpoint, Is.Not.Null);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                tokenPolicyEndpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                    $" / {userIdentity.IssuedTokenType}");
            }
            if (identityPolicy.SecurityPolicyUri != userTokenPolicy)
            {
                Assert.Fail(
                    $"UserTokenPolicy SecurityPolicyUri {identityPolicy.SecurityPolicyUri} does not match test expected SecurityPolicyUri {userTokenPolicy}. " +
                    "Please fix test parameters or the test server configuration.");
            }
            userIdentity.PolicyId = identityPolicy.PolicyId;

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            Assert.That(session1, Is.Not.Null);
            try
            {
                await session1.ReconnectAsync(null, null).ConfigureAwait(false);
                Assert.That(session1.Identity.PolicyId, Is.EqualTo(identityPolicy.PolicyId),
                    "User Token PolicyId needs to be preserved after reconnect.");
            }
            finally
            {
                session1.DeleteSubscriptionsOnClose = true;
                await session1.CloseAsync(1000).ConfigureAwait(false);
                session1?.Dispose();
            }
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
            using var userIdentityAnonymous = new UserIdentity();
            using var userIdentityPW = new UserIdentity("user1", "password"u8);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            Assert.That(endpoint, Is.Not.Null);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentityAnonymous.TokenType,
                userIdentityAnonymous.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentityAnonymous.TokenType}" +
                    $" / {userIdentityAnonymous.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentityAnonymous)
                .ConfigureAwait(false);
            Assert.That(session1, Is.Not.Null);

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value1, Is.Not.Null);

            // hook callback to renew the user identity
            session1.RenewUserIdentity += (_, _) => userIdentityPW;

            ISession session2 = await session1.SessionFactory
                .RecreateAsync(session1)
                .ConfigureAwait(false);

            // create new channel
            ITransportChannel channel2 = await ClientFixture
                .CreateChannelAsync(session1.ConfiguredEndpoint, false)
                .ConfigureAwait(false);
            Assert.That(channel2, Is.Not.Null);

            ISession session3 = await session1.SessionFactory
                .RecreateAsync(session1, channel2)
                .ConfigureAwait(false);

            // validate new Session Ids are used and also UserName PW identity token is
            // injected as renewed token
            Assert.That(session2.SessionId, Is.Not.EqualTo(session1.SessionId));
            Assert.That(session2.Identity.TokenType, Is.EqualTo(userIdentityPW.TokenType));
            Assert.That(session3.SessionId, Is.Not.EqualTo(session1.SessionId));
            Assert.That(session3.Identity.TokenType, Is.EqualTo(userIdentityPW.TokenType));

            ServerStatusDataType value2 = await session2.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.That(value2, Is.Not.Null);

            session1.DeleteSubscriptionsOnClose = true;
            await session1.CloseAsync(1000).ConfigureAwait(false);
            session1?.Dispose();

            session2.DeleteSubscriptionsOnClose = true;
            await session2.CloseAsync(1000).ConfigureAwait(false);
            session2?.Dispose();

            session3.DeleteSubscriptionsOnClose = true;
            await session3.CloseAsync(1000).ConfigureAwait(false);
            session3?.Dispose();
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
            TestContext.Out.WriteLine("NamespaceUris    : {0}", string.Join(", ", Session.NamespaceUris));
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
            ArrayOf<string> localeCollection = ["de-de", "en-us"];
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

            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.ReadValueAsync<ServiceHost>(
                    VariableIds.Server_ServerStatus).ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
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
                Assert.That(dataValue, Is.Not.Null);
                Assert.That(dataValue.WrappedValue.IsNull, Is.False);
                Assert.That(dataValue.SourceTimestamp, Is.Not.EqualTo(DateTime.MinValue));
                Assert.That(dataValue.ServerTimestamp, Is.Not.EqualTo(DateTime.MinValue));
            }
        }

        [Test]
        public async Task ReadValuesAsync()
        {
            NamespaceTable namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors) =
                await Session.ReadValuesAsync([.. testSet]).ConfigureAwait(false);
            Assert.That(values.Count, Is.EqualTo(testSet.Count));
            Assert.That(errors.Count, Is.EqualTo(testSet.Count));
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
            (ArrayOf<Node> nodes, ArrayOf<ServiceResult> errors) = await Session
                .ReadNodesAsync([DataTypeIds.ProgramDiagnosticDataType], NodeClass.DataType, false)
                .ConfigureAwait(false);
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));
            ValidateDataTypeDefinition(nodes[0]);
        }

        private static void ValidateDataTypeDefinition(INode node)
        {
            Assert.That(node, Is.Not.Null);
            var dataTypeNode = (DataTypeNode)node;
            Assert.That(dataTypeNode, Is.Not.Null);
            ExtensionObject dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.That(dataTypeDefinition.IsNull, Is.False);
            Assert.That(dataTypeDefinition.TryGetEncodeable(out StructureDefinition structureDefinition), Is.True);
            Assert.That(
                structureDefinition.DefaultEncodingId,
                Is.EqualTo(ObjectIds.ProgramDiagnosticDataType_Encoding_DefaultBinary));
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
            ReferenceDescriptions = await CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(
                clientTestServices,
                requestHeader,
                operationLimits ? OperationLimits : null,
                outputResult: true).ConfigureAwait(false);

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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }
            ArrayOf<NodeId> nodeIds = ReferenceDescriptions
                .ConvertAll(n => ExpandedNodeId.ToNodeId(n.NodeId, Session.NamespaceUris));
            if (OperationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > OperationLimits.MaxNodesPerRead)
            {
                // force error
                try
                {
                    Session.OperationLimits.MaxNodesPerRead = 0;
                    ServiceResultException sre = Assert
                        .ThrowsAsync<ServiceResultException>(async () =>
                            await Session.ReadDisplayNameAsync(
                                nodeIds).ConfigureAwait(false));
                    Assert.That(
                        sre.StatusCode,
                        Is.EqualTo(StatusCodes.BadTooManyOperations));
                    while (nodeIds.Count > 0)
                    {
                        ArrayOf<NodeId> slice = nodeIds.Count <= OperationLimits.MaxNodesPerRead ?
                            nodeIds :
                            nodeIds[..(int)OperationLimits.MaxNodesPerRead];
                        (ArrayOf<string> displayNames, ArrayOf<ServiceResult> errors) =
                            await Session.ReadDisplayNameAsync(slice).ConfigureAwait(false);
                        foreach (string name in displayNames)
                        {
                            TestContext.Out.WriteLine("{0}", name);
                        }
                        nodeIds = nodeIds.Count <= OperationLimits.MaxNodesPerRead ?
                            default :
                            nodeIds[(int)OperationLimits.MaxNodesPerRead..];
                    }
                }
                finally
                {
                    Session.OperationLimits.MaxNodesPerRead = OperationLimits.MaxNodesPerRead;
                }
            }
            else
            {
                (ArrayOf<string> displayNames, ArrayOf<ServiceResult> errors) =
                    await Session.ReadDisplayNameAsync(nodeIds).ConfigureAwait(false);
                foreach (string name in displayNames)
                {
                    TestContext.Out.WriteLine("{0}", name);
                }
            }
        }

        [Test]
        [Order(480)]
        public async Task SubscriptionAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var requestHeader = new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                TimeoutHint = MaxTimeout
            };

            var clientTestServices = new ClientTestServices(Session, telemetry);
            await CommonTestWorkers.SubscriptionTestAsync(clientTestServices, requestHeader).ConfigureAwait(false);
        }

        [Test]
        [Order(550)]
        public async Task ReadNodeSyncAsync()
        {
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions[..MaxReferences].ToList())
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                Node node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.That(node, Is.Not.Null);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        DataValue value = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
                        Assert.That(value, Is.Not.Null);
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            foreach (ReferenceDescription reference in ReferenceDescriptions[..MaxReferences].ToList())
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.That(node, Is.Not.Null);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        DataValue value = await Session.ReadValueAsync(nodeId)
                            .ConfigureAwait(false);
                        Assert.That(value, Is.Not.Null);
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
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            ArrayOf<NodeId> nodes =
                ReferenceDescriptions
[..nodeCount]
                    .ConvertAll(reference => ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        Session.NamespaceUris));

            (ArrayOf<Node> nodeCollection, ArrayOf<ServiceResult> errors) =
                await Session.ReadNodesAsync(nodes).ConfigureAwait(false);
            Assert.That(nodeCollection.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(nodeCollection.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            (nodeCollection, errors) = await Session.ReadNodesAsync(
                nodes, NodeClass.Unspecified).ConfigureAwait(false);
            Assert.That(nodeCollection.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(nodeCollection.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            int ii = 0;
            var variableNodes = new List<NodeId>();
            foreach (Node node in nodeCollection.ToList())
            {
                Assert.That(node, Is.Not.Null);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                Assert.That(errors[ii], Is.EqualTo(ServiceResult.Good));
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        DataValue value =
                            await Session.ReadValueAsync(node.NodeId).ConfigureAwait(false);
                        Assert.That(value, Is.Not.Null);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            (ArrayOf<DataValue> values, errors) = await Session.ReadValuesAsync(nodes)
                .ConfigureAwait(false);

            Assert.That(values.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            (values, errors) = await Session.ReadValuesAsync(variableNodes)
                .ConfigureAwait(false);

            Assert.That(values.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(variableNodes.Count));
            Assert.That(errors.Count, Is.EqualTo(variableNodes.Count));
        }

        [Test]
        [Order(570)]
        [TestCase(0)]
        [TestCase(MaxReferences)]
        public async Task ReadNodesAsync(int nodeCount)
        {
            if (ReferenceDescriptions.IsNull)
            {
                await BrowseFullAddressSpaceAsync(null).ConfigureAwait(false);
            }

            ArrayOf<NodeId> nodes =
                ReferenceDescriptions
                    .Filter(reference => reference.NodeClass == NodeClass.Variable)
[..nodeCount]
                    .ConvertAll(reference => ExpandedNodeId.ToNodeId(
                        reference.NodeId,
                        Session.NamespaceUris));

            (ArrayOf<Node> nodeCollection, ArrayOf<ServiceResult> errors) = await Session
                .ReadNodesAsync(nodes, true)
                .ConfigureAwait(false);
            Assert.That(nodeCollection.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(nodeCollection.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            (nodeCollection, errors) = await Session
                .ReadNodesAsync(nodes, NodeClass.Unspecified, true)
                .ConfigureAwait(false);
            Assert.That(nodeCollection.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(nodeCollection.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            int ii = 0;
            var variableNodes = new List<NodeId>();
            foreach (Node node in nodeCollection.ToList())
            {
                Assert.That(node, Is.Not.Null);
                Assert.That(errors[ii], Is.EqualTo(ServiceResult.Good));
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", node.NodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        variableNodes.Add(node.NodeId);
                        DataValue value = await Session.ReadValueAsync(node.NodeId)
                            .ConfigureAwait(false);
                        Assert.That(value, Is.Not.Null);
                        TestContext.Out.WriteLine("-- Value {0} ", value);
                    }
                    catch (ServiceResultException sre)
                    {
                        TestContext.Out.WriteLine("-- Read Value {0} ", sre.Message);
                    }
                }
                ii++;
            }

            (ArrayOf<DataValue> values, errors) = await Session.ReadValuesAsync(nodes).ConfigureAwait(false);

            Assert.That(values.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            (values, errors) = await Session.ReadValuesAsync(variableNodes).ConfigureAwait(false);

            Assert.That(values.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(variableNodes.Count));
            Assert.That(errors.Count, Is.EqualTo(variableNodes.Count));
        }

        [Test]
        [Order(620)]
        public async Task ReadAvailableEncodingsAsync()
        {
            ServiceResultException sre = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.ReadAvailableEncodingsAsync(DataTypeIds.BaseDataType)
                    .ConfigureAwait(false));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            ArrayOf<ReferenceDescription> encoding = await Session.ReadAvailableEncodingsAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);
            Assert.That(encoding.IsNull, Is.False);
            Assert.That(encoding.Count, Is.Zero);
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
                ArrayOf<uint> subscriptionIds = await CommonTestWorkers.CreateSubscriptionForTransferAsync(
                    clientTestServices,
                    requestHeader,
                    testSet,
                    0,
                    -1).ConfigureAwait(false);

                TestContext.Out.WriteLine("Transfer SubscriptionIds: {0}", subscriptionIds[0]);

                transferSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                    .ConfigureAwait(false);
                Assert.That(transferSession.SessionId, Is.Not.EqualTo(Session.SessionId));

                requestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                var transferTestServices = new ClientTestServices(transferSession, telemetry);
                await CommonTestWorkers.TransferSubscriptionTestAsync(
                    transferTestServices,
                    requestHeader,
                    subscriptionIds,
                    sendInitialData,
                    false).ConfigureAwait(false);

                // verify the notification of message transfer
                requestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                await CommonTestWorkers.VerifySubscriptionTransferredAsync(
                    clientTestServices,
                    requestHeader,
                    subscriptionIds,
                    true).ConfigureAwait(false);

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
                ITransportChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint)
                : base(channel, configuration, endpoint, null)
            {
                ActivityTraceFlags = ClientTraceFlags.Traces;
            }

            /// <summary>
            /// Expose the protected method for testing
            /// </summary>
            public void TestableUpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                base.UpdateRequestHeader(request, useDefaults);
            }
        }

        private static ActivityContext TestExtractActivityContextFromParameters(
            AdditionalParametersType parameters)
        {
            if (parameters == null)
            {
                return default;
            }

            foreach (KeyValuePair item in parameters.Parameters)
            {
                if (item.Key != "SpanContext")
                {
                    continue;
                }
                if (item.Value.TryGetStructure(out SpanContextDataType spanContext))
                {
#if NET8_0_OR_GREATER
                    Span<byte> spanIdBytes = stackalloc byte[8];
                    Span<byte> traceIdBytes = stackalloc byte[16];
                    ((Guid)spanContext.TraceId).TryWriteBytes(traceIdBytes);
                    BitConverter.TryWriteBytes(spanIdBytes, spanContext.SpanId);
#else
                    byte[] spanIdBytes = BitConverter.GetBytes(spanContext.SpanId);
                    byte[] traceIdBytes = spanContext.TraceId.ToByteArray();
#endif
                    var traceId = ActivityTraceId.CreateFromBytes(traceIdBytes);
                    var spanId = ActivitySpanId.CreateFromBytes(spanIdBytes);
                    return new ActivityContext(traceId, spanId, ActivityTraceFlags.None);
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
                Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded
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
                    Assert.That(endpoint, Is.Not.Null);

                    // Mock the channel and session
                    var channelMock = new Mock<ITransportChannel>();
                    var messageContext = ServiceMessageContext.Create(telemetry);
                    channelMock.Setup(mock => mock.MessageContext).Returns(messageContext);

                    var testableTraceableRequestHeaderClientSession
                        = new TestableTraceableRequestHeaderClientSession(
                            channelMock.Object,
                            ClientFixture.Config,
                            endpoint);
                    var request = new CreateSessionRequest { RequestHeader = new RequestHeader() };

                    // Mock call TestableUpdateRequestHeader() to simulate the header update
                    testableTraceableRequestHeaderClientSession.TestableUpdateRequestHeader(
                        request,
                        true);

                    // Get the AdditionalHeader from the request
                    ExtensionObject additionalHeader = request.RequestHeader.AdditionalHeader;
                    Assert.That(additionalHeader.IsNull, Is.False);
                    Assert.That(additionalHeader.TryGetEncodeable(out AdditionalParametersType additionalParams), Is.True);

                    // Simulate extraction
                    ActivityContext extractedContext = TestExtractActivityContextFromParameters(additionalParams);
                    // Verify that the trace context is propagated.
                    Assert.That(extractedContext.TraceId, Is.EqualTo(activity.TraceId));
                    Assert.That(extractedContext.SpanId, Is.EqualTo(activity.SpanId));

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
            ArrayOf<NodeId> nodes =
            [
                VariableIds.Server_ServerStatus_BuildInfo,
                VariableIds.Server_ServerStatus_BuildInfo_ProductName,
                VariableIds.Server_ServerStatus_BuildInfo_ProductUri,
                VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName,
                VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion,
                VariableIds.Server_ServerStatus_BuildInfo_BuildNumber,
                VariableIds.Server_ServerStatus_BuildInfo_BuildDate
            ];

            (ArrayOf<Node> nodeCollection, ArrayOf<ServiceResult> errors) =
                await Session.ReadNodesAsync(nodes).ConfigureAwait(false);
            Assert.That(nodeCollection.IsNull, Is.False);
            Assert.That(errors.IsNull, Is.False);
            Assert.That(nodeCollection.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors.Count, Is.EqualTo(nodes.Count));

            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> errors2) =
                await Session.ReadValuesAsync(nodes).ConfigureAwait(false);
            Assert.That(values.IsNull, Is.False);
            Assert.That(errors2.IsNull, Is.False);
            Assert.That(values.Count, Is.EqualTo(nodes.Count));
            Assert.That(errors2.Count, Is.EqualTo(nodes.Count));

            ArrayOf<VariableNode> variableNodes = nodeCollection.ConvertAll(n => (VariableNode)n);
            Assert.That(variableNodes.IsNull, Is.False);

            // test build info contains the equal values as the properties
            (values[0].WrappedValue.TryGet(out ExtensionObject eo) ? eo : default)
                .TryGetEncodeable(out BuildInfo buildInfo);
            Assert.That(buildInfo, Is.Not.Null);
            Assert.That((string)values[1].WrappedValue, Is.EqualTo(buildInfo.ProductName));
            Assert.That((string)values[2].WrappedValue, Is.EqualTo(buildInfo.ProductUri));
            Assert.That((string)values[3].WrappedValue, Is.EqualTo(buildInfo.ManufacturerName));
            Assert.That((string)values[4].WrappedValue, Is.EqualTo(buildInfo.SoftwareVersion));
            Assert.That((string)values[5].WrappedValue, Is.EqualTo(buildInfo.BuildNumber));
            Assert.That((DateTimeUtc)values[6].WrappedValue, Is.EqualTo(buildInfo.BuildDate));
        }

        /// <summary>
        /// Open a session on a channel using ECC encrypted UserIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10100)]
        public async Task OpenSessionECCUserNamePwdIdentityTokenAsync(
            [ValueSource(nameof(SupportedEccPolicies))] string securityPolicy)
        {
            IgnoreUnsupportedBrainpoolOnMacOs(securityPolicy);
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);

            using var userIdentity = new UserIdentity("user1", "password"u8);

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
                Assert.Ignore(
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

        /// <summary>
        /// Open a session on a channel using ECC encrypted IssuedIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10200)]
        public async Task OpenSessionECCIssuedIdentityTokenAsync(
            [ValueSource(nameof(SupportedEccPolicies))] string securityPolicy)
        {
            IgnoreUnsupportedBrainpoolOnMacOs(securityPolicy);
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);

            const string identityToken = "fakeTokenString";

            using var issuedToken = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                Encoding.UTF8.GetBytes(identityToken));
            using var userIdentity = new UserIdentity(issuedToken);

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
                Assert.Ignore(
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

        /// <summary>
        /// Open a session on a channel using ECC encrypted UserCertificateIdentityToken
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(10300)]
        public async Task OpenSessionECCUserCertIdentityTokenAsync(
            [ValueSource(nameof(SupportedEccX509Policies))] string securityPolicy)
        {
            IgnoreUnsupportedBrainpoolOnMacOs(securityPolicy);
            await IgnoreIfPolicyNotAdvertisedAsync(securityPolicy).ConfigureAwait(false);

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

                    var userIdentity = new UserIdentity(cert);

                    // the first channel determines the endpoint
                    ConfiguredEndpoint endpoint = await ClientFixture
                        .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                        .ConfigureAwait(false);
                    Assert.That(endpoint, Is.Not.Null);

                    UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                        userIdentity.TokenType,
                        userIdentity.IssuedTokenType,
                        endpoint.Description.SecurityPolicyUri);
                    if (identityPolicy == null)
                    {
                        Assert.Ignore(
                            $"No UserTokenPolicy found for {userIdentity.TokenType}" +
                            $" / {userIdentity.IssuedTokenType}");
                    }

                    // the active channel
                    ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                        .ConfigureAwait(false);
                    Assert.That(session1, Is.Not.Null);

                    ServerStatusDataType value1 =
                        await session1.ReadValueAsync<ServerStatusDataType>(
                        VariableIds.Server_ServerStatus).ConfigureAwait(false);
                    Assert.That(value1, Is.Not.Null);
                }
            }
        }

        /// <summary>
        /// Happy SetSubscriptionDurable
        /// </summary>
        [Test]
        [Order(11000)]
        public async Task SetSubscriptionDurableSuccessAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            const uint expectedRevised = 5;

            ArrayOf<Variant> outputParameters = [expectedRevised];

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint), typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            if (result)
            {
                Assert.That(revised, Is.EqualTo(expectedRevised));
            }
            else
            {
                Assert.Fail("Unexpected Error in SetSubscriptionDurable");
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
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint), typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.That(result, Is.False);
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

            ArrayOf<Variant> outputParameters = [];

            var sessionMock = new Mock<ISession>();

            sessionMock
                 .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint), typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.That(result, Is.False);
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

            ArrayOf<Variant> outputParameters = [];

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint), typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.That(result, Is.False);
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

            ArrayOf<Variant> outputParameters = [expectedRevised, expectedRevised];

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint), typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool result, uint revised) =
                await subscription.SetSubscriptionDurableAsync(1).ConfigureAwait(false);
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// GetMonitoredItems Success Case
        /// </summary>
        [Test]
        [Order(11100)]
        public async Task GetMonitoredItemsSuccessAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            ArrayOf<Variant> outputParameters =
            [
                Variant.From(new uint[] { 1, 2, 3, 4, 5 }),
                Variant.From(new uint[] { 6, 7, 8, 9, 10 })
            ];

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool success, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.That(success, Is.True);
            Assert.That(serverHandles.Count, Is.EqualTo(5));
            Assert.That(clientHandles.Count, Is.EqualTo(5));
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
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid));

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool success, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.That(success, Is.False);
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

            ArrayOf<Variant> outputParameters = [];

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool success, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.That(success, Is.False);
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

            ArrayOf<Variant> outputParameters = default;

            var sessionMock = new Mock<ISession>();

            sessionMock
                .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool success, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.That(success, Is.False);
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

            ArrayOf<Variant> outputParameters =
            [
                Variant.From(new uint[] { 1, 2, 3, 4, 5 }),
                Variant.From(new uint[] { 6, 7, 8, 9, 10 }),
                Variant.From(new uint[] { 11, 12, 13, 14, 15 })
            ];

            var sessionMock = new Mock<ISession>();

            sessionMock
                 .Setup(mock => mock.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(c => c.HasArgsOfType(typeof(uint))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(outputParameters.ToResponse());

            using var subscription = new Subscription(telemetry) { Session = sessionMock.Object };

            (bool success, ArrayOf<uint> serverHandles, ArrayOf<uint> clientHandles) =
                await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);

            Assert.That(success, Is.False);
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
                Assert.That(serverLimit, Is.GreaterThanOrEqualTo(clientLimit));
                Assert.That(clientLimit, Is.Not.Zero);
            }
        }

        private static void IgnoreUnsupportedBrainpoolOnMacOs(string securityPolicyUri)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                (securityPolicyUri.Contains("ECC_brainpoolP256r1", StringComparison.Ordinal) ||
                    securityPolicyUri.Contains("ECC_brainpoolP384r1", StringComparison.Ordinal)))
            {
                Assert.Ignore("Brainpool curve is not supported on Mac OS.");
            }
        }
    }
}
