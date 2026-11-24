// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading.Tasks;
using MemoryBuffer;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Exercises the MemoryBuffer sample node manager on a running reference
    /// server: virtual tags resolve, read and write, and value subscriptions
    /// are fed by the buffer scan.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class MemoryBufferNodeManagerTests
    {
        private const int kTagCount = 16;

        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_secureChannelContext;
        private ServerTestServices m_services;
        private ushort m_instanceNamespaceIndex;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AllNodeManagers = true,
                DurableSubscriptionsEnabled = false,
                AutoAccept = true
            };
            await m_fixture.LoadConfigurationAsync().ConfigureAwait(false);
            m_fixture.Config.UpdateExtension<MemoryBufferConfiguration>(
                null,
                new MemoryBufferConfiguration
                {
                    Buffers =
                    [
                        new MemoryBufferInstance
                        {
                            Name = "UInt32",
                            TagCount = kTagCount,
                            DataType = "UInt32"
                        }
                    ]
                });
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_instanceNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                global::MemoryBuffer.Namespaces.MemoryBuffer + "/Instance");
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [SetUp]
        public async Task SetUpAsync()
        {
            (m_requestHeader, m_secureChannelContext) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            m_requestHeader.TimeoutHint = 10000;
            m_services = new ServerTestServices(m_server, m_secureChannelContext);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            await m_server.CloseSessionAsync(m_secureChannelContext, m_requestHeader, true, RequestLifetime.None)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// The sample keeps the namespace order it had as a synchronous factory:
        /// its namespaces precede the Boiler namespaces.
        /// </summary>
        [Test]
        public void MemoryBufferNamespacesPrecedeBoilerNamespaces()
        {
            NamespaceTable namespaces = m_server.CurrentInstance.NamespaceUris;
            int model = namespaces.GetIndex(global::MemoryBuffer.Namespaces.MemoryBuffer);
            int instance = namespaces.GetIndex(global::MemoryBuffer.Namespaces.MemoryBuffer + "/Instance");
            int boiler = namespaces.GetIndex(Boiler.Namespaces.Boiler);

            Assert.That(model, Is.GreaterThan(0));
            Assert.That(instance, Is.EqualTo(model + 1));
            Assert.That(boiler, Is.GreaterThan(instance));
        }

        /// <summary>
        /// A tag id resolves to a virtual tag that can be read and written, and
        /// an id that is out of range is unknown.
        /// </summary>
        [Test]
        public async Task VirtualTagCanBeReadAndWrittenAsync()
        {
            NodeId tagId = TagId(8);
            const uint written = 0x12345600;

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            WriteResponse write = await m_server.WriteAsync(
                m_secureChannelContext,
                m_requestHeader,
                [
                    new WriteValue
                    {
                        NodeId = tagId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(written))
                    }
                ],
                RequestLifetime.None).ConfigureAwait(false);
            Assert.That(write.Results[0], Is.EqualTo(StatusCodes.Good));

            m_requestHeader.Timestamp = DateTimeUtc.Now;
            ReadResponse read = await m_server.ReadAsync(
                m_secureChannelContext,
                m_requestHeader,
                0,
                TimestampsToReturn.Both,
                [
                    new ReadValueId { NodeId = tagId, AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = tagId, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = TagId(kTagCount * 4), AttributeId = Attributes.Value }
                ],
                RequestLifetime.None).ConfigureAwait(false);

            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out uint value), Is.True);
            // a running scan only bumps the low byte of each element.
            Assert.That(value & 0xFFFFFF00, Is.EqualTo(written));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(DataTypeIds.UInt32));
            Assert.That(read.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Browsing the buffer returns its virtual tags.
        /// </summary>
        [Test]
        public async Task BrowseBufferReturnsVirtualTagsAsync()
        {
            m_requestHeader.Timestamp = DateTimeUtc.Now;
            BrowseResponse response = await m_services.BrowseAsync(
                m_requestHeader,
                new ViewDescription(),
                0,
                [
                    new BrowseDescription
                    {
                        NodeId = new NodeId("UInt32", m_instanceNamespaceIndex),
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Variable,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ]).ConfigureAwait(false);

            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            var targets = new List<ExpandedNodeId>();
            foreach (ReferenceDescription reference in response.Results[0].References)
            {
                targets.Add(reference.NodeId);
            }
            Assert.That(targets, Does.Contain(new ExpandedNodeId(TagId(0))));
            Assert.That(targets, Does.Contain(new ExpandedNodeId(TagId(4))));
        }

        /// <summary>
        /// A value subscription on a tag gets its initial value and then the
        /// changes the buffer scan pushes, and keeps receiving them after a
        /// second item on the same tag is deleted. Each scan changes the value,
        /// so the values collected are distinct.
        /// </summary>
        [Test]
        public async Task ValueSubscriptionReceivesScanUpdatesAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse created = await CreateItemsAsync(
                    subscriptionId,
                    ValueItem(TagId(0), 1),
                    ValueItem(TagId(0), 2)).ConfigureAwait(false);
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(created.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));

                await DeleteItemsAsync(subscriptionId, created.Results[1].MonitoredItemId)
                    .ConfigureAwait(false);

                List<uint> values = await CollectValuesAsync(clientHandle: 1, count: 3)
                    .ConfigureAwait(false);

                Assert.That(values, Has.Count.GreaterThanOrEqualTo(3));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Filters are not supported on tags; non-value attributes use the default item.
        /// </summary>
        [Test]
        public async Task TagItemsRejectFiltersAndAcceptNonValueAttributesAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                MonitoredItemCreateRequest filtered = ValueItem(TagId(4), 1);
                filtered.RequestedParameters.Filter = new ExtensionObject(new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue,
                    DeadbandType = (uint)DeadbandType.None
                });
                MonitoredItemCreateRequest displayName = ValueItem(TagId(4), 2);
                displayName.ItemToMonitor.AttributeId = Attributes.DisplayName;

                CreateMonitoredItemsResponse created = await CreateItemsAsync(
                    subscriptionId, filtered, displayName).ConfigureAwait(false);

                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
                Assert.That(created.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Modifying a tag item cannot add the filter that creating it refuses.
        /// </summary>
        [Test]
        public async Task ModifyTagItemRejectsFilterAsync()
        {
            uint subscriptionId = await CreateSubscriptionAsync().ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse created = await CreateItemsAsync(
                    subscriptionId, ValueItem(TagId(5), 1)).ConfigureAwait(false);
                Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

                var requestHeader = (RequestHeader)m_requestHeader.Clone();
                requestHeader.Timestamp = DateTimeUtc.Now;
                ModifyMonitoredItemsResponse modified = await m_services.ModifyMonitoredItemsAsync(
                    requestHeader,
                    subscriptionId,
                    TimestampsToReturn.Both,
                    new[]
                    {
                        new MonitoredItemModifyRequest
                        {
                            MonitoredItemId = created.Results[0].MonitoredItemId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 100,
                                QueueSize = 10,
                                DiscardOldest = true,
                                Filter = new ExtensionObject(new DataChangeFilter
                                {
                                    Trigger = DataChangeTrigger.StatusValue,
                                    DeadbandType = (uint)DeadbandType.None
                                })
                            }
                        }
                    }.ToArrayOf()).ConfigureAwait(false);

                Assert.That(modified.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadFilterNotAllowed));
            }
            finally
            {
                await DeleteSubscriptionAsync(subscriptionId).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disposing a manager directly, without deleting its address space
        /// first, stops the buffer scan so it no longer feeds or retains
        /// monitored items. Uses a second manager so the registered one is
        /// left alone.
        /// </summary>
        [Test]
        public async Task DisposeStopsBufferScanAsync()
        {
            var manager = new MemoryBufferNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                [
                    global::MemoryBuffer.Namespaces.MemoryBuffer,
                    global::MemoryBuffer.Namespaces.MemoryBuffer + "/Instance"
                ]);
            await manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            // the scan feeds items through QueueValue(in DataValue, ...); the
            // mock records every call, so count its invocations.
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            manager.Buffers["UInt32"].AddMonitoredItem(item.Object, 0);

            for (int ii = 0; ii < 50 && item.Invocations.Count == 0; ii++)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
            Assert.That(item.Invocations, Is.Not.Empty, "the scan did not run");

            await manager.DisposeAsync().ConfigureAwait(false);
            // a scan already running when the timer stopped may still finish.
            await Task.Delay(150).ConfigureAwait(false);
            int queuedAtDispose = item.Invocations.Count;
            await Task.Delay(500).ConfigureAwait(false);

            Assert.That(item.Invocations, Has.Count.EqualTo(queuedAtDispose));
        }

        private NodeId TagId(int offset)
        {
            return new NodeId(Utils.Format("UInt32[{0}]", offset), m_instanceNamespaceIndex);
        }

        private static MonitoredItemCreateRequest ValueItem(NodeId nodeId, uint clientHandle)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = 100,
                    QueueSize = 10,
                    DiscardOldest = true
                }
            };
        }

        private async Task<uint> CreateSubscriptionAsync()
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            CreateSubscriptionResponse response = await m_services.CreateSubscriptionAsync(
                requestHeader, 100, 100, 10, 0, true, 0).ConfigureAwait(false);
            return response.SubscriptionId;
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            await m_services.DeleteSubscriptionsAsync(requestHeader, [subscriptionId]).ConfigureAwait(false);
        }

        private async Task<CreateMonitoredItemsResponse> CreateItemsAsync(
            uint subscriptionId,
            params MonitoredItemCreateRequest[] items)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            return await m_services.CreateMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                TimestampsToReturn.Both,
                items.ToArrayOf()).ConfigureAwait(false);
        }

        private async Task DeleteItemsAsync(uint subscriptionId, params uint[] monitoredItemIds)
        {
            var requestHeader = (RequestHeader)m_requestHeader.Clone();
            requestHeader.Timestamp = DateTimeUtc.Now;
            await m_services.DeleteMonitoredItemsAsync(
                requestHeader,
                subscriptionId,
                monitoredItemIds.ToArrayOf()).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes until <paramref name="count"/> distinct values for the item
        /// arrive or the attempts are exhausted.
        /// </summary>
        private async Task<List<uint>> CollectValuesAsync(uint clientHandle, int count)
        {
            var values = new List<uint>();
            var acknowledgements = new List<SubscriptionAcknowledgement>();
            for (int attempt = 0; attempt < 50 && values.Count < count; attempt++)
            {
                var requestHeader = (RequestHeader)m_requestHeader.Clone();
                requestHeader.Timestamp = DateTimeUtc.Now;
                PublishResponse response = await m_services.PublishAsync(
                    requestHeader,
                    acknowledgements.ToArrayOf()).ConfigureAwait(false);
                acknowledgements.Clear();
                NotificationMessage message = response.NotificationMessage;
                if (message.NotificationData.Count > 0)
                {
                    acknowledgements.Add(new SubscriptionAcknowledgement
                    {
                        SubscriptionId = response.SubscriptionId,
                        SequenceNumber = message.SequenceNumber
                    });
                }

                foreach (ExtensionObject data in message.NotificationData)
                {
                    if (!data.TryGetValue(out DataChangeNotification? notification))
                    {
                        continue;
                    }

                    foreach (MonitoredItemNotification item in notification.MonitoredItems)
                    {
                        if (item.ClientHandle == clientHandle &&
                            item.Value.WrappedValue.TryGetValue(out uint value) && !values.Contains(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }

            return values;
        }
    }
}
