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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Server.Tests;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Features.Tests
{
    [TestFixture]
    [Category("Integration")]
    [Category("NodeManager")]
    public sealed class NodeContractIntegrationTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            m_providerDirectory = Path.Combine(m_directory, "files");
            Directory.CreateDirectory(m_providerDirectory);
            await WritePayloadAsync(10240).ConfigureAwait(false);
            m_fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry)
            {
                EnableFileSystemNodeManager = true,
                FileSystemProvider = new PhysicalFileSystemProvider(m_providerDirectory, "Metadata")
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                AllNodeManagers = false,
                OperationLimits = true
            };
            string pki = Path.Combine(m_directory, "pki");
            await m_fixture.LoadConfigurationAsync(pki).ConfigureAwait(false);
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_client = new ClientFixture(false, false, NUnitTelemetryContext.Create());
            await m_client.LoadClientConfigurationAsync(pki).ConfigureAwait(false);
            m_session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_fixture.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            try
            {
                if (m_session != null)
                {
                    try
                    {
                        await m_session.CloseAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_session.Dispose();
                    }
                }
            }
            finally
            {
                try
                {
                    if (m_fixture != null)
                    {
                        await m_fixture.StopAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_client?.Dispose();
                    if (m_directory != null && Directory.Exists(m_directory))
                    {
                        Directory.Delete(m_directory, recursive: true);
                    }
                }
            }
        }

        [Test]
        [Category("FileSystem")]
        public async Task FirstFileSizeNotificationsAreRealAcrossCreateReattachAndReenableAsync()
        {
            NodeManagerRegistration registration = m_server.NodeManagerLifecycle.Registrations.ToArray()
                .Single(value => value.NodeManager is FileSystemNodeManager);
            var manager = (FileSystemNodeManager)registration.NodeManager;
            NodeId sizeId = await ResolveSizeAsync(manager.NamespaceIndex).ConfigureAwait(false);
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                MonitoredItemCreateResult created = await CreateItemAsync(
                    subscriptionId, sizeId, Attributes.Value).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(created.MonitoredItemId, Is.Not.Zero);
                AssertSize(await PublishDataAsync(subscriptionId).ConfigureAwait(false), 10240);
                Assert.That(manager.TrackedFileCount, Is.Zero);

                await m_server.NodeManagerLifecycle.RemoveAsync(registration, null).ConfigureAwait(false);
                DataValue removed = await PublishDataAsync(subscriptionId).ConfigureAwait(false);
                Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                await WritePayloadAsync(20480).ConfigureAwait(false);
                IAsyncNodeManagerFactory replacement = new FileSystemNodeManagerFactory(
                    new PhysicalFileSystemProvider(m_providerDirectory, "Metadata"));
                NodeManagerRegistration restored = await m_server.NodeManagerLifecycle
                    .AddAsync(replacement, null).ConfigureAwait(false);
                var restoredManager = (FileSystemNodeManager)restored.NodeManager;
                Assert.That(restoredManager.NamespaceIndex, Is.EqualTo(manager.NamespaceIndex));
                AssertSize(await PublishDataAsync(subscriptionId).ConfigureAwait(false), 20480);
                Assert.That(restoredManager.TrackedFileCount, Is.Zero);

                SetMonitoringModeResponse disabled = await m_session.SetMonitoringModeAsync(
                    null, subscriptionId, MonitoringMode.Disabled, [created.MonitoredItemId], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(disabled.Results, Has.Count.EqualTo(1));
                Assert.That(disabled.Results[0], Is.EqualTo(StatusCodes.Good));
                await WritePayloadAsync(30720).ConfigureAwait(false);
                SetMonitoringModeResponse enabled = await m_session.SetMonitoringModeAsync(
                    null, subscriptionId, MonitoringMode.Reporting, [created.MonitoredItemId], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(enabled.Results, Has.Count.EqualTo(1));
                Assert.That(enabled.Results[0], Is.EqualTo(StatusCodes.Good));
                AssertSize(await PublishDataAsync(subscriptionId).ConfigureAwait(false), 30720);
                Assert.That(restoredManager.TrackedFileCount, Is.Zero);

                await DeleteItemAsync(subscriptionId, created.MonitoredItemId).ConfigureAwait(false);
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReloadPreservesItemAndPublishesAsyncProviderValueWithoutPlaceholderAsync()
        {
            var provider = new PhysicalFileSystemProvider(m_providerDirectory, "Reload");
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(new ReloadableValueFactory(provider), null).ConfigureAwait(false);
            var manager = (ReloadableValueManager)registration.NodeManager;
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                MonitoredItemCreateResult created = await CreateItemAsync(
                    subscriptionId, manager.SizeId, Attributes.Value).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
                AssertSize(await PublishDataAsync(subscriptionId).ConfigureAwait(false), 10240);
                await WritePayloadAsync(40960).ConfigureAwait(false);

                NodeManagerRegistration reloaded = await m_server.NodeManagerLifecycle
                    .ReloadAsync(registration, new ReloadableValueFactory(provider), null).ConfigureAwait(false);

                Assert.That(reloaded.Id, Is.EqualTo(registration.Id));
                Assert.That(reloaded.Generation, Is.GreaterThan(registration.Generation));
                AssertSize(await PublishDataAsync(subscriptionId).ConfigureAwait(false), 40960);
                await DeleteItemAsync(subscriptionId, created.MonitoredItemId).ConfigureAwait(false);
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ServerEventsSurviveUnsupportedRootsAndRollBackRealStartupFailureAsync()
        {
            NodeManagerRegistration registration = await m_server.NodeManagerLifecycle
                .AddAsync(new EventNodeManagerFactory(), null).ConfigureAwait(false);
            var manager = (TestableAsyncCustomNodeManager)registration.NodeManager;
            BaseObjectState supported = await AddRootAsync(manager, "Supported", true).ConfigureAwait(false);
            BaseObjectState unsupported = await AddRootAsync(manager, "Unsupported", false).ConfigureAwait(false);
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                EventFilter filter = CreateEventFilter(supported.NodeId);
                MonitoredItemCreateResult created = await CreateItemAsync(
                    subscriptionId, ObjectIds.Server, Attributes.EventNotifier, filter).ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(created.MonitoredItemId, Is.Not.Zero);
                Assert.That(unsupported.EventNotifier, Is.EqualTo(EventNotifiers.None));
                await ReportAndAssertEventAsync(manager, supported, subscriptionId, "before-delete")
                    .ConfigureAwait(false);
                await DeleteItemAsync(subscriptionId, created.MonitoredItemId).ConfigureAwait(false);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(supported.AreEventsMonitored, Is.False);

                manager.EventSubscriptionCallback = (unsubscribe, _) => unsubscribe
                    ? default
                    : throw new ServiceResultException(StatusCodes.BadServerNotConnected);
                MonitoredItemCreateResult rejected = await CreateItemAsync(
                    subscriptionId, ObjectIds.Server, Attributes.EventNotifier, filter).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
                Assert.That(rejected.MonitoredItemId, Is.Zero);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(manager.MonitoredNodes, Has.Count.Zero);
                Assert.That(supported.AreEventsMonitored, Is.False);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Is.Empty);

                manager.EventSubscriptionCallback = null!;
                MonitoredItemCreateResult recovered = await CreateItemAsync(
                    subscriptionId, ObjectIds.Server, Attributes.EventNotifier, filter).ConfigureAwait(false);
                Assert.That(recovered.StatusCode, Is.EqualTo(StatusCodes.Good));
                await ReportAndAssertEventAsync(manager, supported, subscriptionId, "after-rollback")
                    .ConfigureAwait(false);
                await DeleteItemAsync(subscriptionId, recovered.MonitoredItemId).ConfigureAwait(false);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Is.Empty);
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        private async Task WritePayloadAsync(int length)
        {
            using var writer = new StreamWriter(Path.Combine(m_providerDirectory, "payload.bin"));
            await writer.WriteAsync(new string('x', length)).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveSizeAsync(ushort namespaceIndex)
        {
            ArrayOf<QualifiedName> names =
            [
                new QualifiedName("Metadata", namespaceIndex),
                new QualifiedName("payload.bin", namespaceIndex),
                new QualifiedName(BrowseNames.Size)
            ];
            TranslateBrowsePathsToNodeIdsResponse response = await m_session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = ObjectIds.FileSystem,
                        RelativePath = new RelativePath
                        {
                            Elements = names.ToArrayOf(name => new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = name
                            })
                        }
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].Targets, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, m_session.NamespaceUris);
        }

        private async Task<uint> CreateSubscriptionAsync()
        {
            CreateSubscriptionResponse response = await m_session.CreateSubscriptionAsync(
                null, 50, 100, 10, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.SubscriptionId, Is.Not.Zero);
            return response.SubscriptionId;
        }

        private async Task<MonitoredItemCreateResult> CreateItemAsync(
            uint subscriptionId,
            NodeId nodeId,
            uint attributeId,
            EventFilter? filter = null)
        {
            CreateMonitoredItemsResponse response = await m_session.CreateMonitoredItemsAsync(
                null, subscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = nodeId, AttributeId = attributeId },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1,
                            SamplingInterval = 0,
                            QueueSize = filter == null ? 1u : 10u,
                            DiscardOldest = true,
                            Filter = filter == null ? ExtensionObject.Null : new ExtensionObject(filter)
                        }
                    }
                ],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            return response.Results[0];
        }

        private async Task<DataValue> PublishDataAsync(uint subscriptionId)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                PublishResponse response = await m_session.PublishAsync(null, default, cancellation.Token)
                    .ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (notification.TryGetValue(out DataChangeNotification change))
                    {
                        Assert.That(change.MonitoredItems, Has.Count.EqualTo(1));
                        Assert.That(change.MonitoredItems[0].ClientHandle, Is.EqualTo(1));
                        return change.MonitoredItems[0].Value;
                    }
                }
            }
        }

        private static void AssertSize(in DataValue value, ulong expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ulong size), Is.True);
            Assert.That(size, Is.EqualTo(expected));
        }

        private async Task DeleteItemAsync(uint subscriptionId, uint monitoredItemId)
        {
            DeleteMonitoredItemsResponse response = await m_session.DeleteMonitoredItemsAsync(
                null, subscriptionId, [monitoredItemId], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            DeleteSubscriptionsResponse response = await m_session.DeleteSubscriptionsAsync(
                null, [subscriptionId], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good));
        }

        private static async Task<BaseObjectState> AddRootAsync(
            TestableAsyncCustomNodeManager manager, string name, bool supported)
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, manager.NamespaceIndex),
                BrowseName = new QualifiedName(name, manager.NamespaceIndex),
                EventNotifier = supported ? EventNotifiers.SubscribeToEvents : EventNotifiers.None
            };
            root.CreateAsPredefinedNode(manager.SystemContext);
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, root).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
            return root;
        }

        private static EventFilter CreateEventFilter(NodeId source)
        {
            var sourceOperand = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                AttributeId = Attributes.Value,
                BrowsePath = [new QualifiedName(BrowseNames.SourceNode)]
            };
            return new EventFilter
            {
                SelectClauses =
                [
                    sourceOperand,
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        AttributeId = Attributes.Value,
                        BrowsePath = [new QualifiedName(BrowseNames.Message)]
                    }
                ],
                WhereClause = new ContentFilter
                {
                    Elements =
                    [
                        new ContentFilterElement
                        {
                            FilterOperator = FilterOperator.Equals,
                            FilterOperands =
                            [
                                new ExtensionObject(sourceOperand),
                                new ExtensionObject(new LiteralOperand { Value = new Variant(source) })
                            ]
                        }
                    ]
                }
            };
        }

        private async Task ReportAndAssertEventAsync(
            TestableAsyncCustomNodeManager manager,
            BaseObjectState source,
            uint subscriptionId,
            string message)
        {
            var occurrence = new BaseEventState(null);
            occurrence.Initialize(manager.SystemContext, source, EventSeverity.Low, new LocalizedText(message));
            await source.ReportEventAsync(manager.SystemContext, occurrence).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                PublishResponse response = await m_session.PublishAsync(null, default, cancellation.Token)
                    .ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    if (notification.TryGetValue(out EventNotificationList events))
                    {
                        Assert.That(events.Events, Has.Count.EqualTo(1));
                        EventFieldList fields = events.Events[0];
                        Assert.That(fields.ClientHandle, Is.EqualTo(1));
                        Assert.That(fields.EventFields, Has.Count.EqualTo(2));
                        Assert.That(fields.EventFields[0].TryGetValue(out NodeId sourceId), Is.True);
                        Assert.That(sourceId, Is.EqualTo(source.NodeId));
                        Assert.That(fields.EventFields[1].TryGetValue(out LocalizedText text), Is.True);
                        Assert.That(text.Text, Is.EqualTo(message));
                        return;
                    }
                }
            }
        }

        private sealed class EventNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => ["urn:tests:live-node-contracts"];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new TestableAsyncCustomNodeManager(
                    server, configuration, server.Telemetry.CreateLogger<TestableAsyncCustomNodeManager>(),
                    "urn:tests:live-node-contracts"));
            }
        }

        private sealed class ReloadableValueFactory(IFileSystemProvider provider) : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => ["urn:tests:reloadable-async-metadata"];

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new ReloadableValueManager(server, configuration, provider));
            }
        }

        private sealed class ReloadableValueManager : AsyncCustomNodeManager, INodeManagerReloadParticipant
        {
            public ReloadableValueManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                IFileSystemProvider provider)
                : base(server, configuration, "urn:tests:reloadable-async-metadata")
            {
                m_provider = provider;
            }

            public NodeId SizeId => new("Size", NamespaceIndex);

            public override ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                var size = new BaseDataVariableState(null)
                {
                    NodeId = SizeId,
                    BrowseName = new QualifiedName(BrowseNames.Size, NamespaceIndex),
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    OnReadValueAsync = ReadSizeAsync
                };
                size.CreateAsPredefinedNode(SystemContext, cancellationToken);
                return AddPredefinedNodeAsync(SystemContext, size, cancellationToken);
            }

            public ValueTask<ArrayOf<LocalReference>> PrepareReloadAsync(
                IAsyncNodeManager replacement,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<ArrayOf<LocalReference>>([]);
            }

            private async ValueTask<AttributeReadResult> ReadSizeAsync(
                ISystemContext context,
                NodeState node,
                NumericRange indexRange,
                QualifiedName dataEncoding,
                CancellationToken cancellationToken)
            {
                FileSystemEntry? entry = await m_provider.GetEntryAsync("payload.bin", cancellationToken)
                    .ConfigureAwait(false);
                if (entry is not { } file)
                {
                    return new AttributeReadResult(
                        StatusCodes.BadNodeIdUnknown, Variant.Null, StatusCodes.BadNodeIdUnknown, DateTimeUtc.Now);
                }
                return new AttributeReadResult(
                    ServiceResult.Good, new Variant((ulong)file.Length), StatusCodes.Good, DateTimeUtc.Now);
            }

            private readonly IFileSystemProvider m_provider;
        }

        private string m_directory = null!;
        private string m_providerDirectory = null!;
        private ServerFixture<ReferenceServer> m_fixture = null!;
        private ReferenceServer m_server = null!;
        private ClientFixture m_client = null!;
        private ClientSession m_session = null!;
    }
}
