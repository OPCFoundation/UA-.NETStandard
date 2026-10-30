/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.DataChannels;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.DataChannels;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Server.Tests.DataChannels
{
    /// <summary>
    /// Live TCP Browse/Read/Call/Event tests of the optional model.
    /// The common data runtime uses the bounded test carrier; socket data-carrier tests are separate.
    /// </summary>
    [TestFixture]
    [Category("DataChannelsControl")]
    [NonParallelizable]
    public sealed partial class DataChannelModelTests
    {
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var sources = new DataChannelSourceRegistry();
            sources.Register(new DataChannelSource(
                DataChannelControlTestContext.ResourceUri, new DataChannelControlTestContext.TestSourcePolicy())
            {
                SourceNodeId = new ExpandedNodeId(400u, kSourceNamespace),
                SupportedDirections = [DataChannelDirection.Duplex, DataChannelDirection.AcceptorToOpener,
                    DataChannelDirection.OpenerToAcceptor],
                SupportedProtocols = [DataChannelProfiles.UadpPayload],
                ContentType = "application/opcua+uadp",
                MaxMessageSize = 2048,
                MaxReceiveBytes = 8192
            });
            m_options = new DataChannelOptions
            {
                MaxFlows = 8,
                MaxMessageSize = 4096,
                MaxReceiveBytes = 16384,
                MaxContextBytes = 65536
            };
            m_provider = new ServerDataChannelProvider(
                sources, m_options, new DataChannelApplicationBudget(), TimeProvider.System, m_telemetry);
            m_fixture = new ServerFixture<StandardServer>(telemetry =>
                new StandardServer(telemetry).UseDataChannels(m_provider, enableModel: true))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), "uadc-model-" + Guid.NewGuid().ToString("N"));
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_client = new ClientFixture(m_telemetry);
            await m_client.LoadClientConfigurationAsync(
                Path.Combine(m_pkiRoot, "client"), "DataChannelModelClient").ConfigureAwait(false);
            m_endpoint = m_server.GetEndpoints().ToList().First(endpoint =>
                endpoint.TransportProfileUri == Profiles.UaTcpTransport &&
                endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256).EndpointUrl!;
        }

        [SetUp]
        public async Task ConnectControlSessionAsync()
        {
            m_session = await m_client.ConnectAsync(new Uri(m_endpoint), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task CloseControlSessionAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(5000, true).ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
        }

        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            if (m_client != null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, true);
            }
        }

        [Test]
        public async Task LiveBrowseReadExposeExactManagerMethodsArgumentsAndMetadataNamespace()
        {
            BrowseResponse browse = await m_session.BrowseAsync(null, null, 100,
            [
                new BrowseDescription
                {
                    NodeId = ObjectIds.DataChannelManager,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(browse.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(browse.Results[0].References.ToList().Select(reference =>
                ExpandedNodeId.ToNodeId(reference.NodeId, m_session.NamespaceUris)),
                Does.Contain(MethodIds.DataChannelManager_OpenDataChannel)
                    .And.Contain(MethodIds.DataChannelManager_ModifyDataChannel)
                    .And.Contain(MethodIds.DataChannelManager_CloseDataChannel)
                    .And.Contain(VariableIds.DataChannelManager_Channels));
            DataValue arguments = await ReadAsync(
                m_session, VariableIds.DataChannelManager_OpenDataChannel_InputArguments).ConfigureAwait(false);
            Assert.That(arguments.WrappedValue.TryGetStructure(out ArrayOf<Argument> fields), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(fields.ToList().Select(field => field.Name), Is.EqualTo(s_openArgumentNames));
                Assert.That(fields.ToList().Select(field => field.DataType), Is.EqualTo(
                [
                    DataTypeIds.UInt32, DataTypeIds.NodeId, DataTypeIds.DataChannelBindingDataType,
                    DataTypeIds.DataChannelParametersDataType
                ]));
                Assert.That(fields.ToList().All(field => field.ValueRank == ValueRanks.Scalar), Is.True);
            });
            ushort metadataIndex = checked((ushort)m_session.NamespaceUris.GetIndex(
                "http://opcfoundation.org/UA/DataChannel/"));
            Assert.That(metadataIndex, Is.GreaterThan(1), "Namespace metadata must not assume ns=1.");
            ReadValueId[] metadata = [.. Enumerable.Range(100067, 8).Select(id => new ReadValueId
            {
                NodeId = new NodeId((uint)id, metadataIndex),
                AttributeId = id == 100067 ? Attributes.BrowseName : Attributes.Value
            })];
            ReadResponse read = await m_session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, metadata, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(read.Results.ToList().All(value => StatusCode.IsGood(value.StatusCode)), Is.True);
            Assert.That(read.Results[2].WrappedValue.TryGetValue(out string? version), Is.True);
            Assert.That(version, Is.EqualTo("0.3.0"));
            Assert.That(read.Results[3].WrappedValue.TryGetValue(out DateTimeUtc publication), Is.True);
            Assert.That(publication.ToDateTime(), Is.EqualTo(new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)));
#if NET8_0_OR_GREATER
            string[] requestTypes = Enum.GetNames<RequestType>();
#else
            string[] requestTypes = Enum.GetNames(typeof(RequestType));
#endif
            Assert.That(requestTypes, Does.Not.Contain("OpenDataChannel")
                .And.Not.Contain("ModifyDataChannel").And.Not.Contain("CloseDataChannel"));
        }

        [Test]
        public async Task GeneratedCallRejectsUnknownSourceAndCountsAsOrdinaryCall()
        {
            await using DataChannelControlTestPair pair = await CreatePairAsync().ConfigureAwait(false);
            uint id = pair.Client.ReserveChannelId();
            uint count = ServerSession.ReadDiagnostics(diagnostics => diagnostics.CallCount.TotalCount);
            var proxy = new DataChannelManagerTypeClient(
                m_session, ObjectIds.DataChannelManager, m_telemetry);
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await proxy.OpenDataChannelAsync(
                    id, new NodeId("unknown-source", SourceId.NamespaceIndex), Binding(pair),
                    DataChannelControlTestContext.Parameters()).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(ServerSession.ReadDiagnostics(diagnostics => diagnostics.CallCount.TotalCount),
                    Is.GreaterThan(count));
                Assert.That(pair.Server.Channels, Is.Empty);
            });
        }

        [Test]
        public async Task GeneratedSessionControlAndLiveReadsExposeOnlyOwnersActualFlowCounters()
        {
            (object? handle, IAsyncNodeManager? owner) = await m_server.CurrentInstance.NodeManager
                .GetManagerHandleAsync(SourceId).ConfigureAwait(false);
            Assert.That(owner, Is.Not.Null, "The registered source must have a live node manager.");
            Assert.That(handle, Is.Not.Null, "The registered source identity must resolve without guessing.");
            using var operation = new OperationContext(ServerSession, DiagnosticsMasks.None);
            NodeMetadata metadata = await owner!.GetNodeMetadataAsync(
                operation, handle!, BrowseResultMask.All).ConfigureAwait(false);
            Assert.That(metadata.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(MasterNodeManager.ValidateRolePermissions(operation, metadata, PermissionType.Read).StatusCode,
                Is.EqualTo(StatusCodes.Good), "The fixture must explicitly admit the reading user.");
            Assert.That(MasterNodeManager.ValidateRolePermissions(operation, metadata, PermissionType.Write).StatusCode,
                Is.EqualTo(StatusCodes.Good), "The fixture must explicitly admit the writing user.");
            await using var authorizer = new SessionDataChannelAuthorizer(
                m_session, new DataChannelControlTestPair.LocalPolicy(), TimeProvider.System, m_telemetry);
            await using DataChannelControlTestPair pair = await CreatePairAsync(authorizer).ConfigureAwait(false);
            var control = new DataChannelSessionControl(m_session, authorizer, TimeProvider.System);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            DataChannel opened = await control.OpenAsync(
                pair.Client, SourceId, DataChannelControlTestContext.Parameters(), deadline.Token)
                .ConfigureAwait(false);
            DataChannel accepted = (await m_provider.AcceptAsync(
                DataChannelControlTestContext.ResourceUri, DataChannelProfiles.UadpPayload,
                DataChannelCarrierKind.SharedUasc, deadline.Token).ConfigureAwait(false))!;
            var payload = ByteString.From([1, 3, 5, 7]);
            Assert.That(await opened.SendAsync(payload, cancellationToken: deadline.Token).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await accepted.ReadAsync(deadline.Token).ConfigureAwait(false))!;
            Assert.That(received.Payload, Is.EqualTo(payload));
            DataValue ownValue = await ReadAsync(m_session, VariableIds.DataChannelManager_Channels)
                .ConfigureAwait(false);
            Assert.That(ownValue.WrappedValue.TryGetStructure(out ArrayOf<DataChannelStatusDataType> own), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(own.Count, Is.EqualTo(1));
                Assert.That(own[0].ChannelId, Is.EqualTo(opened.ChannelId));
                Assert.That(own[0].SessionId, Is.EqualTo(m_session.SessionId));
                Assert.That(own[0].State, Is.EqualTo(DataChannelState.Open));
            });
            ArrayOf<DataChannelDiagnosticsDataType> counters = await m_provider.GetDiagnosticsAsync(ServerSession)
                .ConfigureAwait(false);
            Assert.That(counters[0].MessagesReceived, Is.EqualTo(1));
            Assert.That(
                counters[0].BytesReceived,
                Is.EqualTo((ulong)payload.Length),
                "Baseline FLO diagnostics count the UADP NetworkMessage body, excluding UASC overhead.");
            ClientSession other = await m_client.ConnectAsync(new Uri(m_endpoint), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                DataValue otherValue = await ReadAsync(other, VariableIds.DataChannelManager_Channels)
                    .ConfigureAwait(false);
                Assert.That(otherValue.WrappedValue.TryGetStructure(out ArrayOf<DataChannelStatusDataType> hidden),
                    Is.True);
                Assert.That(hidden, Is.Empty);
                var otherProxy = new DataChannelManagerTypeClient(other, ObjectIds.DataChannelManager, m_telemetry);
                ServiceResultException forbidden = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await otherProxy.CloseDataChannelAsync(opened.ChannelId, true).ConfigureAwait(false))
                        .ConfigureAwait(false))!;
                Assert.That(forbidden.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            finally
            {
                await other.CloseAsync(5000, true).ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task LiveOfferedEventIsRecipientScopedAndDoesNotAuthorizeOrOpen()
        {
            BrowseResponse schema = await m_session.BrowseAsync(null, null, 100,
            [
                new BrowseDescription
                {
                    NodeId = ObjectTypeIds.DataChannelOfferedEventType,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.Aggregates,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(schema.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(schema.Results[0].References.ToList().Select(reference => reference.BrowseName.Name),
                Is.EquivalentTo(
                [
                    BrowseNames.SourceNodeId, BrowseNames.ResourceUri, BrowseNames.Parameters,
                    BrowseNames.RecipientSessionId, BrowseNames.ExpiresAt
                ]), "An offer must not expose a GrantId, binding challenge, or direct token.");
            ClientSession other = await m_client.ConnectAsync(new Uri(m_endpoint), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                uint ownerSubscription = await CreateEventSubscriptionAsync(m_session).ConfigureAwait(false);
                uint otherSubscription = await CreateEventSubscriptionAsync(other).ConfigureAwait(false);
                await m_provider.OfferAsync(ServerSession, DataChannelControlTestContext.ResourceUri,
                    DataChannelControlTestContext.Parameters(), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                EventNotificationList? received = null;
                for (int attempt = 0; attempt < 20 && received == null; attempt++)
                {
                    PublishResponse response = await m_session.PublishAsync(null, [], deadline.Token)
                        .ConfigureAwait(false);
                    foreach (ExtensionObject body in response.NotificationMessage.NotificationData)
                    {
                        if (body.TryGetValue(out EventNotificationList? events) && events != null)
                        {
                            received = events;
                        }
                    }
                }
                Assert.That(received, Is.Not.Null);
                Assert.That(received!.Events.Count, Is.EqualTo(1));
                Assert.That(received.Events[0].EventFields[0].TryGetValue(out NodeId type), Is.True);
                Assert.That(type, Is.EqualTo(ObjectTypeIds.DataChannelOfferedEventType));
                Assert.That(received.Events[0].EventFields[1].TryGetValue(out string? resource), Is.True);
                Assert.That(resource, Is.EqualTo(DataChannelControlTestContext.ResourceUri));
                PublishResponse hidden = await other.PublishAsync(null, [], deadline.Token).ConfigureAwait(false);
                Assert.That(hidden.NotificationMessage.NotificationData, Is.Empty);
                Assert.That(await m_provider.GetChannelsAsync(ServerSession).ConfigureAwait(false), Is.Empty);
                await m_session.DeleteSubscriptionsAsync(null, [ownerSubscription], deadline.Token)
                    .ConfigureAwait(false);
                await other.DeleteSubscriptionsAsync(null, [otherSubscription], deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                await other.CloseAsync(5000, true).ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task LiveMonitoredChannelValueIsProjectedForEachSession()
        {
            ClientSession other = await m_client.ConnectAsync(new Uri(m_endpoint), SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            try
            {
                uint ownSubscription = await CreateDataSubscriptionAsync(m_session).ConfigureAwait(false);
                uint otherSubscription = await CreateDataSubscriptionAsync(other).ConfigureAwait(false);
                await using DataChannelControlTestPair pair = await CreatePairAsync().ConfigureAwait(false);
                var proxy = new DataChannelManagerTypeClient(m_session, ObjectIds.DataChannelManager, m_telemetry);
                uint id = pair.Client.ReserveChannelId();
                DataChannelGrantDataType grant = await proxy.OpenDataChannelAsync(
                    id, SourceId, Binding(pair), DataChannelControlTestContext.Parameters()).ConfigureAwait(false);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await pair.Client.OpenAsync(DataChannelControlTestContext.Parameters(),
                    DataChannelAuthorizationMode.Session, grant.GrantId, id, deadline.Token).ConfigureAwait(false);
                bool sawOwned = false;
                for (int attempt = 0; attempt < 20 && !sawOwned; attempt++)
                {
                    PublishResponse response = await m_session.PublishAsync(null, [], deadline.Token)
                        .ConfigureAwait(false);
                    foreach (ExtensionObject body in response.NotificationMessage.NotificationData)
                    {
                        if (body.TryGetValue(out DataChangeNotification? change) && change != null)
                        {
                            foreach (MonitoredItemNotification value in change.MonitoredItems)
                            {
                                Assert.That(value.Value.WrappedValue.TryGetStructure(
                                    out ArrayOf<DataChannelStatusDataType> statuses), Is.True);
                                if (statuses.Count == 1)
                                {
                                    Assert.That(statuses[0].SessionId, Is.EqualTo(m_session.SessionId));
                                    sawOwned = true;
                                }
                            }
                        }
                    }
                }
                Assert.That(sawOwned, Is.True);
                PublishResponse responseOther = await other.PublishAsync(null, [], deadline.Token)
                    .ConfigureAwait(false);
                foreach (ExtensionObject body in responseOther.NotificationMessage.NotificationData)
                {
                    if (body.TryGetValue(out DataChangeNotification? change) && change != null)
                    {
                        foreach (MonitoredItemNotification value in change.MonitoredItems)
                        {
                            Assert.That(value.Value.WrappedValue.TryGetStructure(
                                out ArrayOf<DataChannelStatusDataType> statuses), Is.True);
                            Assert.That(statuses, Is.Empty);
                        }
                    }
                }
                await m_session.DeleteSubscriptionsAsync(null, [ownSubscription], deadline.Token).ConfigureAwait(false);
                await other.DeleteSubscriptionsAsync(null, [otherSubscription], deadline.Token).ConfigureAwait(false);
            }
            finally
            {
                await other.CloseAsync(5000, true).ConfigureAwait(false);
                await other.DisposeAsync().ConfigureAwait(false);
            }
        }

        private Task<DataChannelControlTestPair> CreatePairAsync(IDataChannelAuthorizer? authorizer = null)
        {
            return DataChannelControlTestPair.CreateAsync(
                m_provider, m_options, TimeProvider.System, m_telemetry,
                m_fixture.Config.ApplicationUri!, m_client.Config.ApplicationUri!,
                DataChannelCertificateIdentity.GetKeyThumbprint(ServerSession.ServerCertificate),
                DataChannelCertificateIdentity.GetKeyThumbprint(ServerSession.ClientCertificate), authorizer);
        }

        private static DataChannelBindingDataType Binding(DataChannelControlTestPair pair)
        {
            DataChannelContextIdentity identity = pair.Server.Identity;
            return new DataChannelBindingDataType
            {
                ContextId = identity.ContextId,
                Generation = identity.Generation,
                PeerApplicationUri = identity.Peer.PeerApplicationUri,
                PeerKeyThumbprint = identity.Peer.PeerKeyThumbprint,
                Challenge = identity.LocalChallenge,
                ExpiresAt = DateTime.MaxValue
            };
        }

        private static async Task<DataValue> ReadAsync(ClientSession session, NodeId nodeId)
        {
            ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }], CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return response.Results[0];
        }

        private static Task<uint> CreateEventSubscriptionAsync(ClientSession session)
        {
            return CreateEventSubscriptionAsync(
                session, ObjectTypeIds.DataChannelOfferedEventType, BrowseNames.ResourceUri);
        }

        private static Task<uint> CreateEventSubscriptionAsync(
            ClientSession session,
            NodeId selectedType,
            params string[] selectedFields)
        {
            return CreateEventSubscriptionAsync(session, selectedType, false, selectedFields);
        }

        private static async Task<uint> CreateEventSubscriptionAsync(
            ClientSession session,
            NodeId selectedType,
            bool restrictType,
            params string[] selectedFields)
        {
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [QualifiedName.From(BrowseNames.EventType)],
                        AttributeId = Attributes.Value
                    },
                    .. selectedFields.Select(selectedField => new SimpleAttributeOperand
                    {
                        TypeDefinitionId = selectedType,
                        BrowsePath = [QualifiedName.From(selectedField)],
                        AttributeId = Attributes.Value
                    })
                ]
            };
            if (restrictType)
            {
                filter.WhereClause = new ContentFilter
                {
                    Elements =
                    [
                        new ContentFilterElement
                        {
                            FilterOperator = FilterOperator.OfType,
                            FilterOperands = [new ExtensionObject(new LiteralOperand
                            {
                                Value = new Variant(selectedType)
                            })]
                        }
                    ]
                };
            }
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both,
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 10,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter)
                    }
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscription.SubscriptionId;
        }

        private static async Task<uint> CreateDataSubscriptionAsync(ClientSession session)
        {
            CreateSubscriptionResponse subscription = await session.CreateSubscriptionAsync(
                null, 100, 100, 1, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            CreateMonitoredItemsResponse items = await session.CreateMonitoredItemsAsync(
                null, subscription.SubscriptionId, TimestampsToReturn.Both,
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = VariableIds.DataChannelManager_Channels,
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 100,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                }
            ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(items.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            return subscription.SubscriptionId;
        }

        private NodeId SourceId => new(400u, checked((ushort)m_session.NamespaceUris.GetIndex(kSourceNamespace)));

        private ISession ServerSession => m_server.CurrentInstance.SessionManager.GetSessions()
            .Single(session => session.Id == m_session.SessionId);

        private const string kSourceNamespace = "urn:control-tests:source";
        private static readonly string[] s_openArgumentNames = ["channelId", "sourceNodeId", "binding", "parameters"];
        private ITelemetryContext m_telemetry = null!;
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;
        private ServerDataChannelProvider m_provider = null!;
        private DataChannelOptions m_options = null!;
        private ClientFixture m_client = null!;
        private ClientSession m_session = null!;
        private string m_endpoint = null!;
        private string m_pkiRoot = null!;
    }
}
