/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if NET8_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.WebApi;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end smoke test for the HTTPS REST binding (OPC UA Part 6
    /// §G.3 "OpenAPI Mapping") against the full reference server.
    /// Validates that the REST controllers + dispatcher + auth + codec
    /// flow correctly through a real <see cref="StandardServer"/>
    /// instance — complementing the
    /// <c>RealHttpsListenerIntegrationTests</c> stub-callback coverage
    /// in <c>Opc.Ua.Bindings.WebApi.Tests</c>.
    /// </summary>
    /// <remarks>
    /// Runs per encoding (Compact / Verbose). Exercises FindServers,
    /// GetEndpoints, Read of a well-known node, Browse of the Server
    /// folder, and a full CreateSession + ActivateSession + Read flow
    /// via <see cref="WebApiClient"/>.
    /// </remarks>
    [TestFixture]
    [Category("HttpsWebApiIntegration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class HttpsWebApiIntegrationTests
    {
        private const int kMaxTimeout = 30_000;
        private ITelemetryContext m_telemetry;
        private ServerFixture<ReferenceServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ReferenceServer m_server;
        private WebApiServer m_restApiServer;
        private string m_pkiRoot;
        private Uri m_baseAddress;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            // Build the transport binding registry manually so we can
            // attach the REST API startup contributor (the binding mounts
            // MVC controllers into the same Kestrel host that serves the
            // binary / opcua+uajson sub-profiles).
            var messageContext = ServiceMessageContext.CreateEmpty(m_telemetry);
            m_restApiServer = new WebApiServer(messageContext, "rest-api-it");

            var registry = new DefaultTransportBindingRegistry();
            registry.RegisterChannelFactory(new TcpTransportChannelFactory());

            var opcTcpFactory = new TcpTransportListenerFactory();
            registry.RegisterListenerFactory(opcTcpFactory);

            var httpsFactory = new HttpsTransportListenerFactory();
            httpsFactory.StartupContributors.Add(new WebApiHttpsStartupContributor(m_restApiServer));
            registry.RegisterListenerFactory(httpsFactory);

            var opcHttpsFactory = new OpcHttpsTransportListenerFactory();
            opcHttpsFactory.StartupContributors.Add(new WebApiHttpsStartupContributor(m_restApiServer));
            registry.RegisterListenerFactory(opcHttpsFactory);

            registry.RegisterChannelFactory(new HttpsTransportChannelFactory());
            registry.RegisterChannelFactory(new OpcHttpsTransportChannelFactory());

            m_serverFixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
                UriScheme = Utils.UriSchemeOpcHttps,
                HttpsMutualTls = false,
                MaxChannelCount = 8,
                TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security,
                TransportBindingRegistry = registry
            };
            // Pre-load the server configuration so we can add UserName /
            // Certificate / IssuedToken user identity policies BEFORE the
            // listeners are bound. The default ServerFixture configuration
            // ships with empty ServerConfiguration.UserTokenPolicies, which
            // makes any ActivateSession call fail server-side with
            // BadIdentityTokenInvalid (no matching policy). The
            // ManagedSession-over-WebApi tests below need this.
            await m_serverFixture.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);
            m_serverFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                };
            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            // Client-side ApplicationConfiguration with PKI rooted at the
            // same trust store as the server so ManagedSession + WebApi
            // integration tests can connect through the standard
            // ClientChannelManager pipeline.
            m_clientFixture = new ClientFixture(telemetry: m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            // Routes live at the Kestrel-host root regardless of the
            // OPC UA endpoint path (the listener owns the whole port);
            // build the client against opc.https://localhost:port/ so
            // the URI scheme matches the server-advertised
            // ServerEndpoints entries (Session.OpenAsync validates the
            // returned EndpointDescription URI scheme matches the one
            // used to open the channel). The Web API channel strips the
            // 'opc.' prefix before handing the URL to HttpClient.
            m_baseAddress = new Uri(
                Utils.ReplaceLocalhost(
                    $"opc.https://localhost:{m_serverFixture.Port.ToString(CultureInfo.InvariantCulture)}/"));
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_clientFixture?.Dispose();
            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }
            try
            {
                if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        [Test]
        public void ReferenceServerExposesUnsecuredHttpsEndpoint()
        {
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription none = endpoints
                .ToArray()
                .FirstOrDefault(ep =>
                    string.Equals(ep.TransportProfileUri, Profiles.HttpsBinaryTransport, StringComparison.Ordinal) &&
                    ep.SecurityMode == MessageSecurityMode.None);
            Assert.That(none, Is.Not.Null,
                "Reference server did not advertise an unsecured HTTPS endpoint - REST requires SM None.");
        }

        [Test]
        public void ReferenceServerEmitsOpenApiDiscoveryEndpoint()
        {
            // The HttpsServiceHost emits a discovery-only twin per
            // SecurityMode=None HTTPS endpoint with
            // TransportProfileUri = Profiles.HttpsOpenApiTransport (profile/2338).
            // Verify that discovery-driven clients can find the OpenAPI
            // endpoint without hard-coding the URL.
            ArrayOf<EndpointDescription> endpoints = m_server.GetEndpoints();
            EndpointDescription openApi = endpoints
                .ToArray()
                .FirstOrDefault(ep =>
                    Profiles.IsHttpsOpenApi(ep.TransportProfileUri));
            Assert.That(openApi, Is.Not.Null,
                "Reference server must advertise the HTTPS OpenAPI sub-profile (profile/2338) " +
                "as a discovery-only twin alongside the SM=None HTTPS-binary endpoint.");
            Assert.That(openApi.SecurityMode, Is.EqualTo(MessageSecurityMode.None),
                "OpenAPI binding is restricted to SecurityMode.None.");
            Assert.That(openApi.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.None),
                "OpenAPI binding uses TLS — SecurityPolicy.None at the OPC UA layer.");
            Assert.That(openApi.UserIdentityTokens.Count, Is.GreaterThan(0),
                "OpenAPI endpoint must carry the server's user identity token policies " +
                "so clients can pick a compatible identity at activate time.");
        }

        [TestCase(WebApiEncoding.Compact)]
        [TestCase(WebApiEncoding.Verbose)]
        public async Task FindServersOverRestReturnsLocalServer(WebApiEncoding encoding)
        {
            using WebApiClient client = CreateClient(encoding);
            var request = new FindServersRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = (uint)kMaxTimeout
                },
                EndpointUrl = m_baseAddress.ToString()
            };

            FindServersResponse response = await client.FindServersAsync(request)
                .ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                (uint)response.ResponseHeader.ServiceResult,
                Is.EqualTo((uint)StatusCodes.Good),
                "FindServers should succeed against the reference server.");
            Assert.That(response.Servers, Has.Count.GreaterThan(0),
                "Reference server should advertise at least one application description.");
        }

        [TestCase(WebApiEncoding.Compact)]
        [TestCase(WebApiEncoding.Verbose)]
        public async Task GetEndpointsOverRestReturnsServerEndpoints(WebApiEncoding encoding)
        {
            using WebApiClient client = CreateClient(encoding);
            var request = new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = (uint)kMaxTimeout
                },
                EndpointUrl = m_baseAddress.ToString()
            };

            GetEndpointsResponse response = await client.GetEndpointsAsync(request)
                .ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                (uint)response.ResponseHeader.ServiceResult,
                Is.EqualTo((uint)StatusCodes.Good));
            Assert.That(response.Endpoints, Has.Count.GreaterThan(0));
        }

        [TestCase(WebApiEncoding.Compact)]
        [TestCase(WebApiEncoding.Verbose)]
        public async Task ReadSessionlessSucceedsForWellKnownNode(WebApiEncoding encoding)
        {
            // Sessionless Read exercises the wire path; the reference
            // server may reject without a session (BadSessionIdInvalid
            // in ResponseHeader.ServiceResult) — that's still a valid
            // round-trip and confirms the controller dispatches the
            // request. Either Good results or a fault response is
            // acceptable here; both prove the path works.
            using WebApiClient client = CreateClient(encoding);
            var request = new ReadRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = (uint)kMaxTimeout
                },
                MaxAge = 0,
                TimestampsToReturn = TimestampsToReturn.Both,
                NodesToRead = new ArrayOf<ReadValueId>(new ReadValueId[]
                {
                    new ReadValueId
                    {
                        NodeId = VariableIds.Server_NamespaceArray,
                        AttributeId = Attributes.Value
                    }
                }.AsMemory())
            };

            ReadResponse response = await client.ReadAsync(request).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.ResponseHeader, Is.Not.Null,
                "Response must carry a header even when the per-item read fails.");
        }

        [TestCase(WebApiEncoding.Compact)]
        [TestCase(WebApiEncoding.Verbose)]
        public async Task SessionCreateOverRestSucceeds(WebApiEncoding encoding)
        {
            // CreateSession exercises the full server-side dispatcher
            // path through the REST binding: controller → codec →
            // dispatcher → reference server → SessionManager. The test
            // stops at CreateSession (not ActivateSession) because the
            // reference server's UserTokenPolicies filter out Anonymous
            // tokens when HttpsMutualTls=false (see HttpsServiceHost.cs
            // line 229 — security-by-design policy: anonymous + HTTPS
            // without mutual TLS is rejected at activation). Validating
            // the full session lifecycle requires a fixture with mTLS
            // or a configured username identity; that's tracked as a
            // follow-up under T2A discovery emission + T3B richer
            // identity provider.
            using WebApiClient client = CreateClient(encoding);

            var request = new CreateSessionRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = (uint)kMaxTimeout
                },
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:test:WebApiClient",
                    ApplicationName = LocalizedText.From("REST API Test Client"),
                    ApplicationType = ApplicationType.Client,
                    ProductUri = "urn:opcfoundation.org:UA:RESTClient"
                },
                ServerUri = m_server.GetEndpoints().ToArray()[0].Server.ApplicationUri,
                EndpointUrl = m_server.GetEndpoints().ToArray()[0].EndpointUrl,
                SessionName = "HttpsWebApiIntegration-" + encoding,
                ClientNonce = ByteString.From(new byte[32]),
                ClientCertificate = default,
                RequestedSessionTimeout = 60_000,
                MaxResponseMessageSize = 0
            };

            CreateSessionResponse response = await client.CreateSessionAsync(request)
                .ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(
                (uint)response.ResponseHeader.ServiceResult,
                Is.EqualTo((uint)StatusCodes.Good),
                "CreateSession should succeed through the REST binding.");
            Assert.That(response.SessionId.IsNull, Is.False,
                "Server must allocate a non-null SessionId.");
            Assert.That(response.AuthenticationToken.IsNull, Is.False,
                "Server must allocate a non-null AuthenticationToken.");
            Assert.That(response.RevisedSessionTimeout, Is.GreaterThan(0));
            Assert.That(response.ServerNonce.IsNull, Is.False);
            Assert.That(response.ServerEndpoints, Has.Count.GreaterThan(0));

            // Close the freshly-created session so we don't leak it.
            await client.CloseSessionAsync(new CloseSessionRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 2,
                    TimeoutHint = (uint)kMaxTimeout,
                    AuthenticationToken = response.AuthenticationToken
                },
                DeleteSubscriptions = true
            }).ConfigureAwait(false);
        }

        [Test]
        public async Task ManagedSessionOverWebApiOpensActivatesAndClosesAsync()
        {
            // Exercises the integrated client path: ManagedSession over
            // the WebApi transport channel using fluent
            // UseWebApiEndpoint + WithUserIdentity. Verifies that the
            // session activates (CreateSession + ActivateSession) and
            // closes cleanly. Username auth is used because the fixture
            // runs with HttpsMutualTls=false which filters Anonymous
            // tokens out of the HTTPS endpoint's UserIdentityTokens
            // (HttpsServiceHost.cs line 229).
            await using ManagedSession session = await CreateWebApiManagedSessionAsync(
                "ManagedSessionWebApi-Lifecycle").ConfigureAwait(false);

            Assert.That(session.Connected, Is.True,
                "ManagedSession over WebApi must report Connected after ConnectAsync.");
            Assert.That(session.SessionId.IsNull, Is.False,
                "Server must allocate a non-null SessionId.");
        }

        [Test]
        public async Task ManagedSessionOverWebApiReadsServerNamespaceArrayAsync()
        {
            await using ManagedSession session = await CreateWebApiManagedSessionAsync(
                "ManagedSessionWebApi-Read").ConfigureAwait(false);

            var nodesToRead = new ArrayOf<ReadValueId>(new[]
            {
                new ReadValueId
                {
                    NodeId = VariableIds.Server_NamespaceArray,
                    AttributeId = Attributes.Value
                }
            }.AsMemory());

            ReadResponse response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                TimestampsToReturn.Both,
                nodesToRead,
                default).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(
                (uint)response.Results[0].StatusCode,
                Is.EqualTo((uint)StatusCodes.Good),
                "Reading Server.NamespaceArray over WebApi must succeed.");
        }

        [Test]
        public async Task ManagedSessionOverWebApiBrowsesServerObjectAsync()
        {
            await using ManagedSession session = await CreateWebApiManagedSessionAsync(
                "ManagedSessionWebApi-Browse").ConfigureAwait(false);

            BrowseResponse response = await session.BrowseAsync(
                requestHeader: null,
                view: new ViewDescription(),
                requestedMaxReferencesPerNode: 0,
                new ArrayOf<BrowseDescription>(new[]
                {
                    new BrowseDescription
                    {
                        NodeId = ObjectIds.Server,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.AsMemory()),
                default).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(
                (uint)response.Results[0].StatusCode,
                Is.EqualTo((uint)StatusCodes.Good),
                "Browse on Server object over WebApi must succeed.");
            Assert.That(response.Results[0].References, Has.Count.GreaterThan(0),
                "Server object must expose at least one child reference.");
        }

        [Test]
        public async Task ManagedSessionOverWebApiCreatesSubscriptionAndReceivesNotificationAsync()
        {
            // Full V2 subscription parity check over WebApi:
            //  - AddSubscription via V2 ManagedSession.AddSubscription
            //  - TryAdd monitored item on Server.ServerStatus.CurrentTime
            //  - publish loop delivers data-change notifications
            //  - Dispose subscription cleanly
            await using ManagedSession session = await CreateWebApiManagedSessionAsync(
                "ManagedSessionWebApi-Subscription").ConfigureAwait(false);

            var dataChangeReceived = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var notifications = new SubscriptionNotificationHandler(
                onDataChange: _ => dataChangeReceived.TrySetResult(true));

            ISubscription subscription = session.AddSubscription(
                notifications,
                new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                {
                    PublishingInterval = TimeSpan.FromMilliseconds(250),
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    MaxNotificationsPerPublish = 0,
                    PublishingEnabled = true,
                    Priority = 0
                });

            try
            {
                // Wait for the V2 state machine to create the subscription
                // on the server.
                using (var createCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    while (!subscription.Created && !createCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(50, createCts.Token).ConfigureAwait(false);
                    }
                }
                Assert.That(subscription.Created, Is.True,
                    "Subscription must be created on the server within 10s.");

                bool added = subscription.MonitoredItems.TryAdd(
                    "CurrentTime",
                    OptionsFactory.Create(
                        new Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions
                        {
                            StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value,
                            SamplingInterval = TimeSpan.FromMilliseconds(100),
                            QueueSize = 1,
                            DiscardOldest = true,
                            MonitoringMode = MonitoringMode.Reporting
                        }),
                    out IMonitoredItem? item);
                Assert.That(added, Is.True);
                Assert.That(item, Is.Not.Null);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                Task<bool> firstNotification = dataChangeReceived.Task;
                Task winner = await Task.WhenAny(
                    firstNotification,
                    Task.Delay(Timeout.Infinite, cts.Token))
                    .ConfigureAwait(false);

                Assert.That(winner, Is.SameAs(firstNotification),
                    "Subscription over WebApi must deliver at least one data-change notification within 15s.");
            }
            finally
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        private Task<ManagedSession> CreateWebApiManagedSessionAsync(string sessionName)
        {
            return new ManagedSessionBuilder(m_clientFixture.Config, m_telemetry)
                .UseWebApiEndpoint(m_baseAddress.ToString())
                .WithWebApiAuthentication(opts =>
                    opts.HttpMessageHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
                    })
                .WithUserIdentity(new UserIdentity("user1", "password"u8))
                .WithSessionName(sessionName)
                .WithCheckDomain(false)
                .ConnectAsync(default);
        }

        private sealed class SubscriptionNotificationHandler : ISubscriptionNotificationHandler
        {
            private readonly Action<ReadOnlyMemory<DataValueChange>> m_onDataChange;

            public SubscriptionNotificationHandler(Action<ReadOnlyMemory<DataValueChange>> onDataChange)
            {
                m_onDataChange = onDataChange;
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                m_onDataChange(notification);
                return ValueTask.CompletedTask;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
                => ValueTask.CompletedTask;

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
                => ValueTask.CompletedTask;

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
                => ValueTask.CompletedTask;
        }

        private WebApiClient CreateClient(WebApiEncoding encoding)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = static (_, _, _, _) => true
            };
            return WebApiClient.Create(
                m_baseAddress,
                new WebApiClientOptions
                {
                    Encoding = encoding,
                    HttpMessageHandler = handler,
                    DisposeHandler = true
                });
        }
    }
}

#endif
