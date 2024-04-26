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
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Configuration;
using Opc.Ua.Server.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Client tests.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class ClientTest : ClientTestFramework
    {
        public ClientTest() : base(Utils.UriSchemeOpcTcp)
        {
        }

        public ClientTest(string uriScheme = Utils.UriSchemeOpcTcp) :
            base(uriScheme)
        {
        }

        #region DataPointSources
        public static readonly NodeId[] TypeSystems = {
            ObjectIds.OPCBinarySchema_TypeSystem,
            ObjectIds.XmlSchema_TypeSystem
        };
        #endregion

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUp();
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new Task SetUp()
        {
            return base.SetUp();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Global Setup for benchmarks.
        /// </summary>
        [GlobalSetup]
        public new void GlobalSetup()
        {
            base.GlobalSetup();
        }

        /// <summary>
        /// Global cleanup for benchmarks.
        /// </summary>
        [GlobalCleanup]
        public new void GlobalCleanup()
        {
            base.GlobalCleanup();
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public async Task GetEndpointsAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                Endpoints = await client.GetEndpointsAsync(null, CancellationToken.None).ConfigureAwait(false);
                var statusCode = await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

                TestContext.Out.WriteLine("Endpoints:");
                foreach (var endpoint in Endpoints)
                {
                    TestContext.Out.WriteLine("{0}", endpoint.Server.ApplicationName);
                    TestContext.Out.WriteLine("  {0}", endpoint.Server.ApplicationUri);
                    TestContext.Out.WriteLine(" {0}", endpoint.EndpointUrl);
                    TestContext.Out.WriteLine("  {0}", endpoint.EncodingSupport);
                    TestContext.Out.WriteLine("  {0}/{1}/{2}", endpoint.SecurityLevel, endpoint.SecurityMode, endpoint.SecurityPolicyUri);

                    if (endpoint.ServerCertificate != null)
                    {
                        using (var cert = new X509Certificate2(endpoint.ServerCertificate))
                        {
                            TestContext.Out.WriteLine("  [{0}]", cert.Thumbprint);
                        }
                    }
                    else
                    {
                        TestContext.Out.WriteLine("  [no certificate]");
                    }

                    foreach (var userIdentity in endpoint.UserIdentityTokens)
                    {
                        TestContext.Out.WriteLine("  {0}", userIdentity.TokenType);
                        TestContext.Out.WriteLine("  {0}", userIdentity.PolicyId);
                        TestContext.Out.WriteLine("  {0}", userIdentity.SecurityPolicyUri);
                    }
                }
            }
        }

        [Test, Order(100)]
        public async Task FindServersAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                var servers = await client.FindServersAsync(null).ConfigureAwait(false);
                var statusCode = await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

                foreach (var server in servers)
                {
                    TestContext.Out.WriteLine("{0}", server.ApplicationName);
                    TestContext.Out.WriteLine("  {0}", server.ApplicationUri);
                    TestContext.Out.WriteLine("  {0}", server.ApplicationType);
                    TestContext.Out.WriteLine("  {0}", server.ProductUri);
                    foreach (var discoveryUrl in server.DiscoveryUrls)
                    {
                        TestContext.Out.WriteLine("  {0}", discoveryUrl);
                    }
                }
            }
        }

        [Test, Order(100)]
        public async Task FindServersOnNetworkAsync()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                try
                {
                    var response = await client.FindServersOnNetworkAsync(null, 0, 100, null, CancellationToken.None).ConfigureAwait(false);
                    var statusCode = await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    Assert.AreEqual((StatusCode)StatusCodes.Good, statusCode);

                    foreach (ServerOnNetwork server in response.Servers)
                    {
                        TestContext.Out.WriteLine("{0}", server.ServerName);
                        TestContext.Out.WriteLine("  {0}", server.RecordId);
                        TestContext.Out.WriteLine("  {0}", server.ServerCapabilities);
                        TestContext.Out.WriteLine("  {0}", server.DiscoveryUrl);
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore("FindServersOnNetwork not supported on server.");
                }
            }
        }

        /// <summary>
        /// Try to use the discovery channel to read a node.
        /// </summary>
        [Test, Order(105)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void ReadOnDiscoveryChannel(int readCount)
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                var endpoints = client.GetEndpoints(null);
                Assert.NotNull(endpoints);

                // cast Innerchannel to ISessionChannel
                ITransportChannel channel = client.TransportChannel;

                var sessionClient = new SessionClient(channel) {
                    ReturnDiagnostics = DiagnosticsMasks.All
                };

                var request = new ReadRequest {
                    RequestHeader = null
                };

                var readMessage = new ReadMessage() {
                    ReadRequest = request,
                };

                var readValueId = new ReadValueId() {
                    NodeId = new NodeId(Guid.NewGuid()),
                    AttributeId = Attributes.Value
                };

                var readValues = new ReadValueIdCollection();
                for (int i = 0; i < readCount; i++)
                {
                    readValues.Add(readValueId);
                }

                // try to read nodes using discovery channel
                var sre = Assert.Throws<ServiceResultException>(() =>
                    sessionClient.Read(null, 0, TimestampsToReturn.Neither,
                        readValues, out var results, out var diagnosticInfos));
                StatusCode statusCode = StatusCodes.BadSecurityPolicyRejected;
                // race condition, if socket closed is detected before the error was returned,
                // client may report channel closed instead of security policy rejected
                if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
                {
                    Assert.Inconclusive($"Unexpected Status: {sre}" );
                }
                Assert.AreEqual((StatusCode)StatusCodes.BadSecurityPolicyRejected, (StatusCode)sre.StatusCode, "Unexpected Status: {0}", sre);
            }
        }

        /// <summary>
        /// GetEndpoints on the discovery channel,
        /// but an oversized message should return an error.
        /// </summary>
        [Test, Order(105)]
        public void GetEndpointsOnDiscoveryChannel()
        {
            var endpointConfiguration = EndpointConfiguration.Create();
            endpointConfiguration.OperationTimeout = 10000;

            using (var client = DiscoveryClient.Create(ServerUrl, endpointConfiguration))
            {
                var profileUris = new StringCollection();
                for (int i = 0; i < 10000; i++)
                {
                    // dummy uri to create a bigger message
                    profileUris.Add($"https://opcfoundation.org/ProfileUri={i}");
                }
                var sre = Assert.Throws<ServiceResultException>(() => client.GetEndpoints(profileUris));
                // race condition, if socket closed is detected before the error was returned,
                // client may report channel closed instead of security policy rejected
                if (StatusCodes.BadSecureChannelClosed == sre.StatusCode)
                {
                    Assert.Inconclusive($"Unexpected Status: {sre}" );
                }
                Assert.AreEqual((StatusCode)StatusCodes.BadSecurityPolicyRejected, (StatusCode)sre.StatusCode, "Unexpected Status: {0}", sre);
            }
        }

        [Test, Order(110)]
        public async Task InvalidConfiguration()
        {
            var applicationInstance = new ApplicationInstance() {
                ApplicationName = ClientFixture.Config.ApplicationName
            };
            Assert.NotNull(applicationInstance);
            ApplicationConfiguration config = await applicationInstance.Build(ClientFixture.Config.ApplicationUri, ClientFixture.Config.ProductUri)
                .AsClient()
                .AddSecurityConfiguration(ClientFixture.Config.SecurityConfiguration.ApplicationCertificate.SubjectName)
                .Create().ConfigureAwait(false);
        }

        [Theory, Order(200)]
        public async Task ConnectAndClose(string securityPolicy)
        {
            bool closeChannel = securityPolicy != SecurityPolicies.Aes128_Sha256_RsaOaep;
            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += Session_Closing;
            var result = session.Close(5_000, closeChannel);
            Assert.NotNull(result);
            session.Dispose();
        }

        [Theory, Order(201)]
        public async Task ConnectAndCloseAsync(string securityPolicy)
        {
            bool closeChannel = securityPolicy != SecurityPolicies.Basic128Rsa15;
            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session);
            Session.SessionClosing += Session_Closing;
            var result = await session.CloseAsync(5_000, closeChannel, CancellationToken.None).ConfigureAwait(false);
            Assert.NotNull(result);
            session.Dispose();
        }

        [Test, Order(202)]
        public async Task ConnectAndCloseAsyncReadAfterClose()
        {
            var securityPolicy = SecurityPolicies.Basic256Sha256;
            using (var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false))
            {
                Assert.NotNull(session);
                Session.SessionClosing += Session_Closing;

                var nodeId = new NodeId(Opc.Ua.VariableIds.ServerStatusType_BuildInfo);
                var node = await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
                var value = await session.ReadValueAsync(nodeId, CancellationToken.None).ConfigureAwait(false);

                // keep channel open/inactive
                var result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, result);

                await Task.Delay(5_000).ConfigureAwait(false);

                var sre = Assert.ThrowsAsync<ServiceResultException>(async () => await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual((StatusCode)StatusCodes.BadSessionIdInvalid, (StatusCode)sre.StatusCode);
            }
        }

        [Test, Order(204)]
        public async Task ConnectAndCloseAsyncReadAfterCloseSessionReconnect()
        {
            var securityPolicy = SecurityPolicies.Basic256Sha256;
            using (var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false))
            {
                Assert.NotNull(session);
                Session.SessionClosing += Session_Closing;

                var userIdentity = session.Identity;
                var sessionName = session.SessionName;

                var nodeId = new NodeId(Opc.Ua.VariableIds.ServerStatusType_BuildInfo);
                var node = await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
                var value = await session.ReadValueAsync(nodeId, CancellationToken.None).ConfigureAwait(false);

                // keep channel open/inactive
                var result = await session.CloseAsync(1_000, false).ConfigureAwait(false);
                Assert.AreEqual((StatusCode)StatusCodes.Good, result);

                await Task.Delay(5_000).ConfigureAwait(false);

                var sre = Assert.ThrowsAsync<ServiceResultException>(async () => await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false));
                Assert.AreEqual((StatusCode)StatusCodes.BadSessionIdInvalid, (StatusCode)sre.StatusCode);

                // reconect/reactivate
                await session.OpenAsync(sessionName, userIdentity, CancellationToken.None).ConfigureAwait(false);

                node = await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
                value = await session.ReadValueAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Test, Order(206)]
        public async Task ConnectCloseSessionCloseChannel()
        {
            var securityPolicy = SecurityPolicies.Basic256Sha256;
            using (var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false))
            {

                Assert.NotNull(session);
                Session.SessionClosing += Session_Closing;

                var userIdentity = session.Identity;
                var sessionName = session.SessionName;

                var nodeId = new NodeId(Opc.Ua.VariableIds.ServerStatusType_BuildInfo);
                var node = await session.ReadNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
                var value = await session.ReadValueAsync(nodeId, CancellationToken.None).ConfigureAwait(false);

                // keep channel opened but detach so no comm goes through
                var channel = session.TransportChannel;
                session.DetachChannel();

                int waitTime = ServerFixture.Application.ApplicationConfiguration.TransportQuotas.ChannelLifetime +
                    (ServerFixture.Application.ApplicationConfiguration.TransportQuotas.ChannelLifetime / 2) + 5_000;
                await Task.Delay(waitTime).ConfigureAwait(false);

                // Channel handling checked for TcpTransportChannel only
                if (channel is TcpTransportChannel tcp)
                {
                    Assert.IsNull(tcp.Socket);
                }
            }
        }

        [Theory, Order(210)]
        public async Task ConnectAndReconnectAsync(bool reconnectAbort, bool useMaxReconnectPeriod)
        {
            const int connectTimeout = MaxTimeout;
            var session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session);

            int sessionConfigChanged = 0;
            session.SessionConfigurationChanged += (object sender, EventArgs e) => { sessionConfigChanged++; };

            int sessionClosing = 0;
            session.SessionClosing += (object sender, EventArgs e) => { sessionClosing++; };

            ManualResetEvent quitEvent = new ManualResetEvent(false);
            var reconnectHandler = new SessionReconnectHandler(reconnectAbort, useMaxReconnectPeriod ? MaxTimeout : -1);
            reconnectHandler.BeginReconnect(session, connectTimeout / 5,
                (object sender, EventArgs e) => {
                    // ignore callbacks from discarded objects.
                    if (!Object.ReferenceEquals(sender, reconnectHandler))
                    {
                        Assert.Fail("Unexpected sender");
                    }

                    if (reconnectHandler.Session != null)
                    {
                        if (!Object.ReferenceEquals(reconnectHandler.Session, session))
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

            var timeout = quitEvent.WaitOne(connectTimeout);
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

            var result = session.Close();
            Assert.NotNull(result);
            reconnectHandler.Dispose();
            session.Dispose();

            Assert.Less(0, sessionClosing);
        }

        [Theory, Order(220)]
        public async Task ConnectJWT(string securityPolicy)
        {
            var identityToken = "fakeTokenString";

            var issuedToken = new IssuedIdentityToken() {
                IssuedTokenType = IssuedTokenType.JWT,
                PolicyId = Profiles.JwtUserToken,
                DecryptedTokenData = Encoding.UTF8.GetBytes(identityToken)
            };

            var userIdentity = new UserIdentity(issuedToken);

            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            var receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(identityToken, receivedToken);

            var result = session.Close();
            Assert.NotNull(result);

            session.Dispose();
        }

        [Theory, Order(230)]
        public async Task ReconnectJWT(string securityPolicy)
        {
            UserIdentity CreateUserIdentity(string tokenData)
            {
                var issuedToken = new IssuedIdentityToken() {
                    IssuedTokenType = IssuedTokenType.JWT,
                    PolicyId = Profiles.JwtUserToken,
                    DecryptedTokenData = Encoding.UTF8.GetBytes(tokenData)
                };

                return new UserIdentity(issuedToken);
            }

            var identityToken = "fakeTokenString";
            var userIdentity = CreateUserIdentity(identityToken);

            var session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session);
            Assert.NotNull(TokenValidator.LastIssuedToken);

            var receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(identityToken, receivedToken);

            var newIdentityToken = "fakeTokenStringNew";
            session.RenewUserIdentity += (s, i) => {
                return CreateUserIdentity(newIdentityToken);
            };

            session.Reconnect();
            receivedToken = Encoding.UTF8.GetString(TokenValidator.LastIssuedToken.DecryptedTokenData);
            Assert.AreEqual(newIdentityToken, receivedToken);

            var result = session.Close();
            Assert.NotNull(result);

            session.Dispose();
        }

        [Test, Order(240)]
        public async Task ConnectMultipleSessionsAsync()
        {
            var endpoint = await ClientFixture.GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
            Assert.NotNull(endpoint);

            var channel = await ClientFixture.CreateChannelAsync(endpoint, false).ConfigureAwait(false);
            Assert.NotNull(channel);

            var session1 = ClientFixture.CreateSession(channel, endpoint);
            session1.Open("Session1", null);

            var session2 = ClientFixture.CreateSession(channel, endpoint);
            session2.Open("Session2", null);

            _ = session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));

            session1.Close(closeChannel: false);
            session1.DetachChannel();
            session1.Dispose();

            _ = session2.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));

            session2.Close(closeChannel: false);
            session2.DetachChannel();
            session2.Dispose();

            channel.Dispose();
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate) the same session on a new channel.
        /// Close the first channel before or after the new channel is activated.
        /// </summary>
        [Theory, Order(250)]
        public async Task ReconnectSessionOnAlternateChannel(bool closeChannel)
        {
            ServiceResultException sre;

            // the endpoint to use
            var endpoint = await ClientFixture.GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
            Assert.NotNull(endpoint);

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
            Assert.NotNull(session1);

            // test by reading a value
            ServerStatusDataType value1 = (ServerStatusDataType)session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value1);

            // save the channel to close it later
            var channel1 = session1.TransportChannel;

            // test case: close the channel before reconnecting
            if (closeChannel)
            {
                session1.DetachChannel();
                channel1.Dispose();

                // cannot read using a detached channel
                var exception = Assert.Throws<ServiceResultException>(() => session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType)));
                Assert.AreEqual((StatusCode)StatusCodes.BadSecureChannelClosed, (StatusCode)exception.StatusCode);
            }

            // the inactive channel
            ITransportChannel channel2 = await ClientFixture.CreateChannelAsync(endpoint, false).ConfigureAwait(false);
            Assert.NotNull(channel2);

            // activate the session on the new channel
            session1.Reconnect(channel2);

            // test by reading a value
            ServerStatusDataType value2 = (ServerStatusDataType)session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value2);
            Assert.AreEqual(value1.State, value2.State);

            // test case: close the first channel after the session is activated on the new channel
            if (!closeChannel)
            {
                channel1.Close();
                channel1.Dispose();
            }

            // test by reading a value
            ServerStatusDataType value3 = (ServerStatusDataType)session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value3);
            Assert.AreEqual(value1.State, value3.State);

            // close the session, keep the channel open
            session1.Close(closeChannel: false);

            // cannot read using a closed session, validate the status code
            sre = Assert.Throws<ServiceResultException>(() => session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType)));
            Assert.AreEqual((StatusCode)StatusCodes.BadSessionIdInvalid, (StatusCode)sre.StatusCode, sre.Message);

            // close the channel
            channel2.Close();
            channel2.Dispose();

            // cannot read using a closed channel, validate the status code
            sre = Assert.Throws<ServiceResultException>(() => session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType)));

            // TODO: Both channel should return BadSecureChannelClosed
            if (!(StatusCodes.BadSecureChannelClosed == sre.StatusCode))
            {
                if (endpoint.EndpointUrl.ToString().StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    Assert.AreEqual((StatusCode)StatusCodes.BadSessionIdInvalid, (StatusCode)sre.StatusCode, sre.Message);
                }
                else
                {
                    Assert.AreEqual((StatusCode)StatusCodes.BadUnknownResponse, (StatusCode)sre.StatusCode, sre.Message);
                }
            }
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets
        /// </summary>
        [Test, Order(260)]
        [TestCase(SecurityPolicies.None, true)]
        [TestCase(SecurityPolicies.None, false)]
        [TestCase(SecurityPolicies.Basic256Sha256, true)]
        [TestCase(SecurityPolicies.Basic256Sha256, false)]
        public async Task ReconnectSessionOnAlternateChannelWithSavedSessionSecrets(string securityPolicy, bool anonymous)
        {
            ServiceResultException sre;

            IUserIdentity userIdentity = anonymous ? new UserIdentity() : new UserIdentity("user1", "password");

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
            Assert.NotNull(endpoint);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(userIdentity.TokenType, userIdentity.IssuedTokenType);
            if (identityPolicy == null)
            {
                Assert.Ignore($"No UserTokenPolicy found for {userIdentity.TokenType} / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session1);

            ServerStatusDataType value1 = (ServerStatusDataType)session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value1);

            // save the session configuration
            var stream = new MemoryStream();
            session1.SaveSessionConfiguration(stream);

            var streamArray = stream.ToArray();
            TestContext.Out.WriteLine($"SessionSecrets: {stream.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(streamArray));

            // read the session configuration
            var loadStream = new MemoryStream(streamArray);
            var sessionConfiguration = SessionConfiguration.Create(loadStream);

            // create the inactive channel
            ITransportChannel channel2 = await ClientFixture.CreateChannelAsync(sessionConfiguration.ConfiguredEndpoint, false).ConfigureAwait(false);
            Assert.NotNull(channel2);

            // prepare the inactive session with the new channel
            ISession session2 = ClientFixture.CreateSession(channel2, sessionConfiguration.ConfiguredEndpoint);

            // apply the saved session configuration
            bool success = session2.ApplySessionConfiguration(sessionConfiguration);

            // hook callback to renew the user identity
            session2.RenewUserIdentity += (session, identity) => {
                return userIdentity;
            };

            // activate the session from saved session secrets on the new channel
            session2.Reconnect(channel2);

            Thread.Sleep(500);

            Assert.AreEqual(session1.SessionId, session2.SessionId);

            ServerStatusDataType value2 = (ServerStatusDataType)session2.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value2);

            await Task.Delay(500).ConfigureAwait(false);

            // cannot read using a closed channel, validate the status code
            if (endpoint.EndpointUrl.ToString().StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
            {
                sre = Assert.Throws<ServiceResultException>(() => session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType)));
                Assert.AreEqual((StatusCode)StatusCodes.BadSecureChannelIdInvalid, (StatusCode)sre.StatusCode, sre.Message);
            }
            else
            {
                var result = session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
                Assert.NotNull(result);
            }

            session1.DeleteSubscriptionsOnClose = true;
            session1.Close(1000);
            Utils.SilentDispose(session1);

            session2.DeleteSubscriptionsOnClose = true;
            session2.Close(1000);
            Utils.SilentDispose(session2);
        }

        [Test, Order(300)]
        public new void GetOperationLimits()
        {
            base.GetOperationLimits();

            ValidateOperationLimit(OperationLimits.MaxNodesPerRead, Session.OperationLimits.MaxNodesPerRead);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryReadData, Session.OperationLimits.MaxNodesPerHistoryReadData);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryReadEvents, Session.OperationLimits.MaxNodesPerHistoryReadEvents);
            ValidateOperationLimit(OperationLimits.MaxNodesPerBrowse, Session.OperationLimits.MaxNodesPerBrowse);
            ValidateOperationLimit(OperationLimits.MaxMonitoredItemsPerCall, Session.OperationLimits.MaxMonitoredItemsPerCall);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryUpdateData, Session.OperationLimits.MaxNodesPerHistoryUpdateData);
            ValidateOperationLimit(OperationLimits.MaxNodesPerHistoryUpdateEvents, Session.OperationLimits.MaxNodesPerHistoryUpdateEvents);
            ValidateOperationLimit(OperationLimits.MaxNodesPerMethodCall, Session.OperationLimits.MaxNodesPerMethodCall);
            ValidateOperationLimit(OperationLimits.MaxNodesPerNodeManagement, Session.OperationLimits.MaxNodesPerNodeManagement);
            ValidateOperationLimit(OperationLimits.MaxNodesPerRegisterNodes, Session.OperationLimits.MaxNodesPerRegisterNodes);
            ValidateOperationLimit(OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds, Session.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds);
            ValidateOperationLimit(OperationLimits.MaxNodesPerWrite, Session.OperationLimits.MaxNodesPerWrite);
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
            TestContext.Out.WriteLine("TypeTree         : {0}", Session.TypeTree);
            TestContext.Out.WriteLine("FilterContext    : {0}", Session.FilterContext);
            TestContext.Out.WriteLine("PreferredLocales : {0}", Session.PreferredLocales);
            TestContext.Out.WriteLine("DataTypeSystem   : {0}", Session.DataTypeSystem);
            TestContext.Out.WriteLine("Subscriptions    : {0}", Session.Subscriptions);
            TestContext.Out.WriteLine("SubscriptionCount: {0}", Session.SubscriptionCount);
            TestContext.Out.WriteLine("DefaultSubscription: {0}", Session.DefaultSubscription);
            TestContext.Out.WriteLine("LastKeepAliveTime: {0}", Session.LastKeepAliveTime);
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval += 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            Session.KeepAliveInterval -= 1000;
            TestContext.Out.WriteLine("KeepAliveInterval: {0}", Session.KeepAliveInterval);
            TestContext.Out.WriteLine("KeepAliveStopped : {0}", Session.KeepAliveStopped);
            TestContext.Out.WriteLine("OutstandingRequestCount : {0}", Session.OutstandingRequestCount);
            TestContext.Out.WriteLine("DefunctRequestCount     : {0}", Session.DefunctRequestCount);
            TestContext.Out.WriteLine("GoodPublishRequestCount : {0}", Session.GoodPublishRequestCount);
        }

        [Test]
        public void ChangePreferredLocales()
        {
            // change locale
            var localeCollection = new StringCollection() { "de-de", "en-us" };
            Session.ChangePreferredLocales(localeCollection);
        }

        [Test]
        public void ReadValueAsync()
        {
            // Test ReadValue
            Task task1 = Session.ReadValueAsync(VariableIds.Server_ServerRedundancy_RedundancySupport);
            Task task2 = Session.ReadValueAsync(VariableIds.Server_ServerStatus);
            Task task3 = Session.ReadValueAsync(VariableIds.Server_ServerStatus_BuildInfo);
            Task.WaitAll(task1, task2, task3);
        }

        [Test]
        public void ReadValueTyped()
        {
            // Test ReadValue
            _ = Session.ReadValue(VariableIds.Server_ServerRedundancy_RedundancySupport, typeof(Int32));
            _ = Session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            var sre = Assert.Throws<ServiceResultException>(() => Session.ReadValue(VariableIds.Server_ServerStatus, typeof(ServiceHost)));
            Assert.AreEqual((StatusCode)StatusCodes.BadTypeMismatch, (StatusCode)sre.StatusCode);
        }

        [Test]
        public void ReadValue()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetSimulation(namespaceUris));
            foreach (var nodeId in testSet)
            {
                var dataValue = Session.ReadValue(nodeId);
                Assert.NotNull(dataValue);
                Assert.NotNull(dataValue.Value);
                Assert.AreNotEqual(DateTime.MinValue, dataValue.SourceTimestamp);
                Assert.AreNotEqual(DateTime.MinValue, dataValue.ServerTimestamp);
            }
        }

        [Test]
        public void ReadValues()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = new NodeIdCollection(GetTestSetStatic(namespaceUris));
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            Session.ReadValues(testSet, out DataValueCollection values, out IList<ServiceResult> errors);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }

        [Test]
        public async Task ReadValuesAsync()
        {
            var namespaceUris = Session.NamespaceUris;
            var testSet = GetTestSetStatic(namespaceUris).ToList();
            testSet.AddRange(GetTestSetFullSimulation(namespaceUris));
            DataValueCollection values;
            IList<ServiceResult> errors;
            (values, errors) = await Session.ReadValuesAsync(new NodeIdCollection(testSet)).ConfigureAwait(false);
            Assert.AreEqual(testSet.Count, values.Count);
            Assert.AreEqual(testSet.Count, errors.Count);
        }

        [Test]
        public void ReadDataTypeDefinition()
        {
            // Test Read a DataType Node
            INode node = Session.ReadNode(DataTypeIds.ProgramDiagnosticDataType);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinitionAsync()
        {
            // Test Read a DataType Node
            INode node = await Session.ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType).ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public void ReadDataTypeDefinition2()
        {
            // Test Read a DataType Node, the nodeclass is known
            INode node = Session.ReadNode(DataTypeIds.ProgramDiagnosticDataType, NodeClass.DataType, false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public async Task ReadDataTypeDefinition2Async()
        {
            // Test Read a DataType Node, the nodeclass is known
            INode node = await Session.ReadNodeAsync(DataTypeIds.ProgramDiagnosticDataType, NodeClass.DataType, false).ConfigureAwait(false);
            ValidateDataTypeDefinition(node);
        }

        [Test]
        public void ReadDataTypeDefinitionNodes()
        {
            // Test Read a DataType Node, the nodeclass is known
            Session.ReadNodes(new NodeIdCollection() { DataTypeIds.ProgramDiagnosticDataType }, NodeClass.DataType, out IList<Node> nodes, out IList<ServiceResult> errors, false);
            ValidateDataTypeDefinition(nodes[0]);
        }

        [Test]
        public async Task ReadDataTypeDefinitionNodesAsync()
        {
            // Test Read a DataType Node, the nodeclass is known
            (var nodes, var errors) = await Session.ReadNodesAsync(new NodeIdCollection() { DataTypeIds.ProgramDiagnosticDataType }, NodeClass.DataType, false).ConfigureAwait(false);
            Assert.AreEqual(nodes.Count, errors.Count);
            ValidateDataTypeDefinition(nodes[0]);
        }


        private void ValidateDataTypeDefinition(INode node)
        {
            Assert.NotNull(node);
            var dataTypeNode = (DataTypeNode)node;
            Assert.NotNull(dataTypeNode);
            var dataTypeDefinition = dataTypeNode.DataTypeDefinition;
            Assert.NotNull(dataTypeDefinition);
            Assert.True(dataTypeDefinition is ExtensionObject);
            Assert.NotNull(dataTypeDefinition.Body);
            Assert.True(dataTypeDefinition.Body is StructureDefinition);
            StructureDefinition structureDefinition = dataTypeDefinition.Body as StructureDefinition;
            Assert.AreEqual(ObjectIds.ProgramDiagnosticDataType_Encoding_DefaultBinary, structureDefinition.DefaultEncodingId);
        }

        [Theory, Order(400)]
        public async Task BrowseFullAddressSpace(string securityPolicy, bool operationLimits = false)
        {
            if (OperationLimits == null) { GetOperationLimits(); }

            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            // Session
            ISession session;
            if (securityPolicy != null)
            {
                session = await ClientFixture.ConnectAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
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

            var clientTestServices = new ClientTestServices(session);
            ReferenceDescriptions = CommonTestWorkers.BrowseFullAddressSpaceWorker(clientTestServices, requestHeader, operationLimits ? OperationLimits : null);

            if (securityPolicy != null)
            {
                session.Close();
                session.Dispose();
            }
        }

        [Test, Order(410)]
        [NonParallelizable]
        public async Task ReadDisplayNames()
        {
            if (ReferenceDescriptions == null) { await BrowseFullAddressSpace(null).ConfigureAwait(false); }
            var nodeIds = ReferenceDescriptions.Select(n => ExpandedNodeId.ToNodeId(n.NodeId, Session.NamespaceUris)).ToList();
            if (OperationLimits.MaxNodesPerRead > 0 &&
                nodeIds.Count > OperationLimits.MaxNodesPerRead)
            {
                // force error
                try
                {
                    Session.OperationLimits.MaxNodesPerRead = 0;
                    var sre = Assert.Throws<ServiceResultException>(() => Session.ReadDisplayName(nodeIds, out var displayNames, out var errors));
                    Assert.AreEqual((StatusCode)StatusCodes.BadTooManyOperations, (StatusCode)sre.StatusCode);
                    while (nodeIds.Count > 0)
                    {
                        Session.ReadDisplayName(nodeIds.Take((int)OperationLimits.MaxNodesPerRead).ToArray(), out var displayNames, out var errors);
                        foreach (var name in displayNames)
                        {
                            TestContext.Out.WriteLine("{0}", name);
                        }
                        nodeIds = nodeIds.Skip((int)OperationLimits.MaxNodesPerRead).ToList();
                    }
                }
                finally
                {
                    Session.OperationLimits.MaxNodesPerRead = OperationLimits.MaxNodesPerRead;
                }
            }
            else
            {
                Session.ReadDisplayName(nodeIds, out var displayNames, out var errors);
                foreach (var name in displayNames)
                {
                    TestContext.Out.WriteLine("{0}", name);
                }
            }
        }

        [Test, Order(480)]
        public void Subscription()
        {
            var requestHeader = new RequestHeader();
            requestHeader.Timestamp = DateTime.UtcNow;
            requestHeader.TimeoutHint = MaxTimeout;

            var clientTestServices = new ClientTestServices(Session);
            CommonTestWorkers.SubscriptionTest(clientTestServices, requestHeader);
        }


        [Test, Order(550)]
        public async Task ReadNode()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                var node = Session.ReadNode(nodeId);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = Session.ReadValue(nodeId);
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

        [Test, Order(550)]
        public async Task ReadNodeAsync()
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            foreach (var reference in ReferenceDescriptions.Take(MaxReferences))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                INode node = await Session.ReadNodeAsync(nodeId).ConfigureAwait(false);
                Assert.NotNull(node);
                TestContext.Out.WriteLine("NodeId: {0} Node: {1}", nodeId, node);
                if (node is VariableNode)
                {
                    try
                    {
                        var value = await Session.ReadValueAsync(nodeId).ConfigureAwait(false);
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

        [Test, Order(560)]
        [TestCase(0)]
        [TestCase(MaxReferences)]
        public async Task ReadNodes(int nodeCount)
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            NodeIdCollection nodes = new NodeIdCollection(
                ReferenceDescriptions
                .Take(nodeCount)
                .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris)));

            Session.ReadNodes(nodes, out IList<Node> nodeCollection, out IList<ServiceResult> errors);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            Session.ReadNodes(nodes, NodeClass.Unspecified, out nodeCollection, out errors);
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
                        var value = Session.ReadValue(node.NodeId);
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

            Session.ReadValues(nodes, out DataValueCollection values, out errors);

            Assert.NotNull(values);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            Session.ReadValues(variableNodes, out values, out errors);

            Assert.NotNull(values);
            Assert.AreEqual(variableNodes.Count, values.Count);
            Assert.AreEqual(variableNodes.Count, errors.Count);
        }

        [Test, Order(570)]
        [TestCase(0)]
        [TestCase(MaxReferences)]
        public async Task ReadNodesAsync(int nodeCount)
        {
            if (ReferenceDescriptions == null)
            {
                await BrowseFullAddressSpace(null).ConfigureAwait(false);
            }

            NodeIdCollection nodes = new NodeIdCollection(
                ReferenceDescriptions
                    .Where(reference => reference.NodeClass == NodeClass.Variable)
                    .Take(nodeCount)
                    .Select(reference => ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris))
                );

            (IList<Node> nodeCollection, IList<ServiceResult> errors) = await Session.ReadNodesAsync(nodes, true).ConfigureAwait(false);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            (nodeCollection, errors) = await Session.ReadNodesAsync(nodes, NodeClass.Unspecified, true).ConfigureAwait(false);
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
                        var value = await Session.ReadValueAsync(node.NodeId).ConfigureAwait(false);
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

        [Test, Order(620)]
        public void ReadAvailableEncodings()
        {
            var sre = Assert.Throws<ServiceResultException>(() => Session.ReadAvailableEncodings(DataTypeIds.BaseDataType));
            Assert.AreEqual((StatusCode)StatusCodes.BadNodeIdInvalid, (StatusCode)sre.StatusCode);
            var encoding = Session.ReadAvailableEncodings(VariableIds.Server_ServerStatus_CurrentTime);
            Assert.NotNull(encoding);
            Assert.AreEqual(0, encoding.Count);
        }

        [Test, Order(700)]
        public async Task LoadStandardDataTypeSystem()
        {
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => {
                var t = await Session.LoadDataTypeSystem(ObjectIds.ObjectAttributes_Encoding_DefaultJson).ConfigureAwait(false);
            });
            Assert.AreEqual((StatusCode)StatusCodes.BadNodeIdInvalid, (StatusCode)sre.StatusCode);
            var typeSystem = await Session.LoadDataTypeSystem().ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await Session.LoadDataTypeSystem(ObjectIds.OPCBinarySchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
            typeSystem = await Session.LoadDataTypeSystem(ObjectIds.XmlSchema_TypeSystem).ConfigureAwait(false);
            Assert.NotNull(typeSystem);
        }

        [Test, Order(710)]
        [TestCaseSource(nameof(TypeSystems))]
        public void LoadAllServerDataTypeSystems(NodeId dataTypeSystem)
        {
            // find the dictionary for the description.
            Browser browser = new Browser(Session) {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            ReferenceDescriptionCollection references = browser.Browse(dataTypeSystem);
            Assert.NotNull(references);

            TestContext.Out.WriteLine("  Found {0} references", references.Count);

            // read all type dictionaries in the type system
            foreach (var r in references)
            {
                NodeId dictionaryId = ExpandedNodeId.ToNodeId(r.NodeId, Session.NamespaceUris);
                TestContext.Out.WriteLine("  ReadDictionary {0} {1}", r.BrowseName.Name, dictionaryId);
                var dictionaryToLoad = new DataDictionary(Session);
                dictionaryToLoad.Load(dictionaryId, r.BrowseName.Name);

                // internal API for testing only
                var dictionary = dictionaryToLoad.ReadDictionary(dictionaryId);
                // TODO: workaround known issues in the Xml type system.
                // https://mantis.opcfoundation.org/view.php?id=7393
                if (dataTypeSystem.Equals(ObjectIds.XmlSchema_TypeSystem))
                {
                    try
                    {
                        dictionaryToLoad.Validate(dictionary, true);
                    }
                    catch (Exception ex)
                    {
                        Assert.Inconclusive(ex.Message);
                    }
                }
                else
                {
                    dictionaryToLoad.Validate(dictionary, true);
                }
            }
        }

        /// <summary>
        /// Transfer the subscription using the native service calls, not the client SDK layer.
        /// </summary>
        /// <remarks>
        /// Create a subscription with a monitored item using the native service calls.
        /// Create a secondary Session.
        /// </remarks>
        [Theory, Order(800)]
        [NonParallelizable]
        public async Task TransferSubscriptionNative(bool sendInitialData)
        {
            ISession transferSession = null;
            try
            {
                var requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };

                // to validate the behavior of the sendInitialValue flag,
                // use a static variable to avoid sampled notifications in publish requests
                var namespaceUris = Session.NamespaceUris;
                NodeId[] testSet = CommonTestWorkers.NodeIdTestSetStatic.Select(n => ExpandedNodeId.ToNodeId(n, namespaceUris)).ToArray();
                var clientTestServices = new ClientTestServices(Session);
                var subscriptionIds = CommonTestWorkers.CreateSubscriptionForTransfer(clientTestServices, requestHeader, testSet, 0, -1);

                TestContext.Out.WriteLine("Transfer SubscriptionIds: {0}", subscriptionIds[0]);

                transferSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
                Assert.AreNotEqual(Session.SessionId, transferSession.SessionId);

                requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                var transferTestServices = new ClientTestServices(transferSession);
                CommonTestWorkers.TransferSubscriptionTest(transferTestServices, requestHeader, subscriptionIds, sendInitialData, false);

                // verify the notification of message transfer
                requestHeader = new RequestHeader {
                    Timestamp = DateTime.UtcNow,
                    TimeoutHint = MaxTimeout
                };
                CommonTestWorkers.VerifySubscriptionTransferred(clientTestServices, requestHeader, subscriptionIds, true);

                transferSession.Close();
            }
            finally
            {
                transferSession?.Dispose();
            }
        }

        // Test class for testing protected methods in TraceableRequestHeaderClientSession
        public class TestableTraceableRequestHeaderClientSession : TraceableRequestHeaderClientSession
        {
            public TestableTraceableRequestHeaderClientSession(
                ISessionChannel channel,
                ApplicationConfiguration configuration,
                ConfiguredEndpoint endpoint)
                : base(channel, configuration, endpoint)
            {
            }

            // Expose the protected method for testing
            public void TestableUpdateRequestHeader(IServiceRequest request, bool useDefaults)
            {
                base.UpdateRequestHeader(request, useDefaults);
            }
        }

        public static ActivityContext TestExtractActivityContextFromParameters(AdditionalParametersType parameters)
        {
            if (parameters == null)
            {
                return default;
            }

            ActivityTraceId traceId = default;
            ActivitySpanId spanId = default;
            ActivityTraceFlags traceFlags = ActivityTraceFlags.None;

            foreach (var item in parameters.Parameters)
            {
                if (item.Key == "traceparent")
                {
                    var traceparent = item.Value.ToString();
                    int firstDash = traceparent.IndexOf('-');
                    int secondDash = traceparent.IndexOf('-', firstDash + 1);
                    int thirdDash = traceparent.IndexOf('-', secondDash + 1);

                    if (firstDash != -1 && secondDash != -1)
                    {
                        ReadOnlySpan<char> traceIdSpan = traceparent.AsSpan(firstDash + 1, secondDash - firstDash - 1);
                        ReadOnlySpan<char> spanIdSpan = traceparent.AsSpan(secondDash + 1, thirdDash - secondDash - 1);
                        ReadOnlySpan<char> traceFlagsSpan = traceparent.AsSpan(thirdDash + 1);

                        traceId = ActivityTraceId.CreateFromString(traceIdSpan);
                        spanId = ActivitySpanId.CreateFromString(spanIdSpan);
                        traceFlags = traceFlagsSpan.SequenceEqual("01".AsSpan()) ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

                        return new ActivityContext(traceId, spanId, traceFlags);
                    }
                    return default;
                }
            }

            // no traceparent header found
            return default;
        }

        [Test, Order(900)]
        public async Task ClientTestRequestHeaderUpdate()
        {
            var rootActivity = new Activity("Test_Activity_Root") {
                ActivityTraceFlags = ActivityTraceFlags.Recorded,
            }.Start();

            var activityListener = new ActivityListener {
                ShouldListenTo = s => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            };

            ActivitySource.AddActivityListener(activityListener);

            using (var activity = new ActivitySource("TestActivitySource").StartActivity("Test_Activity"))
            {
                if (activity != null && activity.Id != null)
                {
                    var endpoint = await ClientFixture.GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints).ConfigureAwait(false);
                    Assert.NotNull(endpoint);

                    // Mock the channel and session
                    var channelMock = new Mock<ITransportChannel>();
                    var sessionChannelMock = channelMock.As<ISessionChannel>();

                    TestableTraceableRequestHeaderClientSession testableTraceableRequestHeaderClientSession = new TestableTraceableRequestHeaderClientSession(sessionChannelMock.Object, ClientFixture.Config, endpoint);
                    CreateSessionRequest request = new CreateSessionRequest();
                    request.RequestHeader = new RequestHeader();

                    // Mock call TestableUpdateRequestHeader() to simulate the header update
                    testableTraceableRequestHeaderClientSession.TestableUpdateRequestHeader(request, true);

                    // Get the AdditionalHeader from the request
                    var additionalHeader = request.RequestHeader.AdditionalHeader as ExtensionObject;
                    Assert.NotNull(additionalHeader);

                    // Simulate extraction
                    var extractedContext = TestExtractActivityContextFromParameters(additionalHeader.Body as AdditionalParametersType);

                    // Verify that the trace context is propagated.
                    Assert.AreEqual(activity.TraceId, extractedContext.TraceId);
                    Assert.AreEqual(activity.SpanId, extractedContext.SpanId);

                    TestContext.Out.WriteLine($"Activity TraceId: {activity.TraceId}, Activity SpanId: {activity.SpanId}");
                    TestContext.Out.WriteLine($"Extracted TraceId: {extractedContext.TraceId}, Extracted SpanId: {extractedContext.SpanId}");
                }
            }

            rootActivity.Stop();
        }

        /// <summary>
        /// Read BuildInfo and ensure the values in the structure are the same as in the properties.
        /// </summary>
        [Test, Order(10000)]
        public void ReadBuildInfo()
        {
            NodeIdCollection nodes = new NodeIdCollection()
            {
                VariableIds.Server_ServerStatus_BuildInfo,
                VariableIds.Server_ServerStatus_BuildInfo_ProductName,
                VariableIds.Server_ServerStatus_BuildInfo_ProductUri,
                VariableIds.Server_ServerStatus_BuildInfo_ManufacturerName,
                VariableIds.Server_ServerStatus_BuildInfo_SoftwareVersion,
                VariableIds.Server_ServerStatus_BuildInfo_BuildNumber,
                VariableIds.Server_ServerStatus_BuildInfo_BuildDate
            };

            Session.ReadNodes(nodes, out IList<Node> nodeCollection, out IList<ServiceResult> errors);
            Assert.NotNull(nodeCollection);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, nodeCollection.Count);
            Assert.AreEqual(nodes.Count, errors.Count);

            Session.ReadValues(nodes, out DataValueCollection values, out IList<ServiceResult> errors2);
            Assert.NotNull(values);
            Assert.NotNull(errors);
            Assert.AreEqual(nodes.Count, values.Count);
            Assert.AreEqual(nodes.Count, errors2.Count);

            IList<VariableNode> variableNodes = nodeCollection.Cast<VariableNode>().ToList();

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
        #endregion

        #region Benchmarks
        /// <summary>
        /// Benchmark wrapper for browse tests.
        /// </summary>
        [Benchmark]
        public async Task BrowseFullAddressSpaceBenchmark()
        {
            await BrowseFullAddressSpace(null).ConfigureAwait(false);
        }
        #endregion

        #region Private Methods
        void ValidateOperationLimit(uint serverLimit, uint clientLimit)
        {
            if (serverLimit != 0)
            {
                Assert.GreaterOrEqual(serverLimit, clientLimit);
                Assert.NotZero(clientLimit);
            }
        }
        #endregion
    }
}
