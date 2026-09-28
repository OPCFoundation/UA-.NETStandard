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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class ServerEventFanoutRegressionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task EventModifyFailuresRemainPerItemAndDoNotSkipOtherOwnersAsync(bool throws)
        {
            using var harness = new FanoutHarness(false);

            ArrayOf<ServiceResult> results = await harness.ModifyEventPairWithFailureAsync(throws)
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.VerifySubscriptions(unsubscribe: false, count: 2);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataModifyFailurePreservesCompletedItemsAndContinuesOtherOwnersAsync(bool completeFirst)
        {
            using var harness = new FanoutHarness(false);

            ArrayOf<ServiceResult> results = await harness.ModifyDataBatchWithFailureAsync(completeFirst)
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode,
                Is.EqualTo(completeFirst ? StatusCodes.Good : StatusCodes.BadUnexpectedError));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task DeletedRootNotifiersReleaseAllEventLinksBeforeReRegistrationAsync(
            bool serviceDelete,
            bool childRoot,
            bool retainOtherRoot)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                IEventMonitoredItem item = events.CreateMonitoredItem(
                    context, manager, null!, 1, new MonitoredItemIdFactory(), TimestampsToReturn.Both, 1000,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                    },
                    new EventFilter(), false);
                var parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId("parent", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("parent", manager.NamespaceIndex)
                };
                var root = new BaseObjectState(childRoot ? parent : null)
                {
                    NodeId = new NodeId("root", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                if (childRoot)
                {
                    parent.AddChild(root);
                }
                await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, childRoot ? parent : root)
                    .ConfigureAwait(false);
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                if (retainOtherRoot)
                {
                    await AddRootAsync(manager, "other", true).ConfigureAwait(false);
                }
                MonitoredNode2 previous = manager.MonitoredNodes[root.NodeId];
                NodeId deletedId = childRoot ? parent.NodeId : root.NodeId;
                if (serviceDelete)
                {
                    ServiceResult deleted = await manager.DeleteNodeAsync(
                        context, new DeleteNodesItem { NodeId = deletedId }).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(deleted), Is.True);
                }
                else
                {
                    Assert.That(await manager.DeleteNodeAsync(manager.SystemContext, deletedId)
                        .ConfigureAwait(false), Is.True);
                }

                Assert.That(manager.MonitoredNodes.ContainsKey(root.NodeId), Is.False);
                Assert.That(manager.MonitoredItems.ContainsKey(item.Id), Is.EqualTo(retainOtherRoot));
                Assert.That(root.OnReportEventAsync, Is.Null);
                Assert.That(events.GetMonitoredItems(), Has.Count.EqualTo(1));

                BaseObjectState replacement = await AddRootAsync(manager, "root", true).ConfigureAwait(false);
                Assert.That(manager.MonitoredNodes[replacement.NodeId], Is.Not.SameAs(previous));
                Assert.That(manager.MonitoredNodes[replacement.NodeId].Node, Is.SameAs(replacement));
                Assert.That(
                    manager.MonitoredNodes[replacement.NodeId].EventMonitoredItems.ContainsKey(item.Id), Is.True);
                Assert.That(replacement.AreEventsMonitored, Is.True);

                await manager.SubscribeToAllEventsAsync(context, 1, item, true).ConfigureAwait(false);
                events.DeleteMonitoredItem(item.Id);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedManagerDoesNotPreventServerEventCreationOrDeletionAsync(bool unsupportedFirst)
        {
            using var harness = new FanoutHarness(unsupportedFirst);

            await harness.CreateAsync().ConfigureAwait(false);

            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Item, Is.Not.Null);
            Assert.That(harness.Events.GetMonitoredItems(), Has.Count.EqualTo(1));
            harness.VerifySubscriptions(unsubscribe: false);

            ServiceResult deleted = await harness.DeleteAsync().ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GenuineManagerFailureRollsBackEveryAttemptedOwnerAsync(bool throws)
        {
            using var harness = new FanoutHarness(true, StatusCodes.BadServerNotConnected, throws);

            await harness.CreateAsync().ConfigureAwait(false);

            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
            Assert.That(harness.Item, Is.Null);
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: false);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [Test]
        public async Task GenuineUnsubscribeFailureIsReportedAfterOtherOwnersAreCleanedAsync()
        {
            using var harness = new FanoutHarness(true, cleanupFailure: StatusCodes.BadTimeout);
            await harness.CreateAsync().ConfigureAwait(false);
            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.Good));

            ServiceResult deleted = await harness.DeleteAsync().ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task NullSuccessRootCallbacksPreserveSubscriptionAndCleanupResultsAsync(
            bool samplingGroups,
            bool failSecondSubscription)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            await using (var manager = new NullSuccessRootNodeManager(
                server.Object, samplingGroups, failSecondSubscription))
            {
                ArrayOf<BaseObjectState> roots =
                [
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("First", manager.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    },
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("Second", manager.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }
                ];
                for (int ii = 0; ii < roots.Count; ii++)
                {
                    BaseObjectState root = roots[ii];
                    root.CreateAsPredefinedNode(manager.SystemContext);
                    await manager.RegisterRootAsync(root).ConfigureAwait(false);
                }

                var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.NodeId).Returns(ObjectIds.Server);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                IFilterTarget notification = Mock.Of<IFilterTarget>();
                item.Setup(value => value.QueueEvent(notification)).Callback(() => delivered.TrySetResult(true));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                try
                {
                    ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, false)
                        .ConfigureAwait(false);

                    Assert.That(result.StatusCode,
                        Is.EqualTo(failSecondSubscription ? StatusCodes.BadServerNotConnected : StatusCodes.Good));
                    Assert.That(manager.SubscriptionCalls, Is.EqualTo(2));
                    if (!failSecondSubscription)
                    {
                        await roots[0].ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        item.Verify(value => value.QueueEvent(notification), Times.Once);
                    }
                }
                finally
                {
                    ServiceResult cleanup = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, true)
                        .ConfigureAwait(false);
                    Assert.That(cleanup.StatusCode, Is.EqualTo(StatusCodes.Good));
                }

                Assert.That(manager.UnsubscriptionCalls, Is.EqualTo(2));
                Assert.That(await ((INodeManagerMonitoredItemLifecycle)manager)
                    .GetMonitoredItemsSnapshotAsync().ConfigureAwait(false), Is.Empty);
                foreach (BaseObjectState root in roots)
                {
                    Assert.That(root.AreEventsMonitored, Is.False);
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task MixedRootsDeliverSupportedEventsAndUnsubscribeWithoutChangingCapabilitiesAsync(
            bool samplingGroups,
            bool cleanupFailure)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            await using (var manager = new TestableAsyncCustomNodeManager(
                server.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 100,
                        AvailableSamplingRates = []
                    }
                },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                BaseObjectState supported = await AddRootAsync(manager, "Supported", true).ConfigureAwait(false);
                BaseObjectState unsupported = await AddRootAsync(manager, "Unsupported", false).ConfigureAwait(false);
                BaseObjectState other = await AddRootAsync(manager, "Other", true).ConfigureAwait(false);
                var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.NodeId).Returns(ObjectIds.Server);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                IFilterTarget notification = Mock.Of<IFilterTarget>();
                item.Setup(value => value.QueueEvent(notification)).Callback(() => delivered.TrySetResult(true));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);

                ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, false)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(unsupported.EventNotifier, Is.EqualTo(EventNotifiers.None));
                Assert.That(manager.MonitoredNodes.ContainsKey(unsupported.NodeId), Is.False);
                Assert.That(manager.MonitoredNodes[supported.NodeId].EventMonitoredItems.ContainsKey(42), Is.True);
                Assert.That(manager.MonitoredNodes[other.NodeId].EventMonitoredItems.ContainsKey(42), Is.True);
                await supported.ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                item.Verify(value => value.QueueEvent(notification), Times.Once);

                int unsubscribed = 0;
                manager.EventSubscriptionCallback = (unsubscribe, _) =>
                {
                    if (unsubscribe && Interlocked.Increment(ref unsubscribed) == 1 && cleanupFailure)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerNotConnected);
                    }
                    return default;
                };
                ServiceResult deleted = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, true)
                    .ConfigureAwait(false);
                Assert.That(deleted.StatusCode,
                    Is.EqualTo(cleanupFailure ? StatusCodes.BadServerNotConnected : StatusCodes.Good));
                Assert.That(unsubscribed, Is.EqualTo(2));
                Assert.That(manager.MonitoredNodes, Has.Count.Zero);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(supported.AreEventsMonitored, Is.False);
                Assert.That(other.AreEventsMonitored, Is.False);

                object handle = await manager.GetManagerHandleAsync(unsupported.NodeId).ConfigureAwait(false);
                ServiceResult direct = await manager.SubscribeToEventsAsync(
                    context, handle, 1, item.Object, false).ConfigureAwait(false);
                Assert.That(direct.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
        }

        private static async Task<BaseObjectState> AddRootAsync(
            TestableAsyncCustomNodeManager manager,
            string name,
            bool supported)
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

        private sealed class NullSuccessRootNodeManager : AsyncCustomNodeManager
        {
            public NullSuccessRootNodeManager(IServerInternal server, bool samplingGroups, bool failSecondSubscription)
                : base(
                    server,
                    new ApplicationConfiguration
                    {
                        ServerConfiguration = new ServerConfiguration
                        {
                            MaxNotificationQueueSize = 100,
                            MaxDurableNotificationQueueSize = 100,
                            AvailableSamplingRates = []
                        }
                    },
                    samplingGroups,
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri)
            {
                m_failSecondSubscription = failSecondSubscription;
            }

            public int SubscriptionCalls { get; private set; }

            public int UnsubscriptionCalls { get; private set; }

            public async ValueTask RegisterRootAsync(BaseObjectState root)
            {
                await AddNodeAsync(SystemContext, NodeId.Null, root).ConfigureAwait(false);
                await AddRootNotifierAsync(root).ConfigureAwait(false);
            }

            protected override async ValueTask<ServiceResult> SubscribeToEventsAsync(
                ServerSystemContext context,
                NodeState source,
                IEventMonitoredItem monitoredItem,
                bool unsubscribe,
                CancellationToken cancellationToken = default)
            {
                ServiceResult result = await base.SubscribeToEventsAsync(
                    context, source, monitoredItem, unsubscribe, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                if (unsubscribe)
                {
                    UnsubscriptionCalls++;
                }
                else if (++SubscriptionCalls == 2 && m_failSecondSubscription)
                {
                    return StatusCodes.BadServerNotConnected;
                }
                return null!;
            }

            private readonly bool m_failSecondSubscription;
        }

        private sealed class FanoutHarness : IDisposable
        {
            public FanoutHarness(
                bool unsupportedFirst,
                StatusCode failure = default,
                bool throws = false,
                StatusCode cleanupFailure = default)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                Events = new EventManager(server.Object, 100, 100);
                server.SetupGet(value => value.EventManager).Returns(Events);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                m_supported = CreateOwner(server.Object.NamespaceUris.GetString(0), StatusCodes.Good, cleanupFailure);
                m_unsupported = CreateOwner("urn:tests:unsupported-events",
                    StatusCodes.BadNotSupported, StatusCodes.BadNotSupported);
                m_terminal = CreateOwner("urn:tests:terminal-events", failure, StatusCodes.Good);
                if (throws)
                {
                    m_terminal.Setup(value => value.SubscribeToAllEventsAsync(
                            It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), false,
                            It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new ServiceResultException(failure));
                }

                var source = new BaseObjectState(null)
                {
                    NodeId = ObjectIds.Server,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                m_supported.Setup(value => value.GetManagerHandleAsync(
                        ObjectIds.Server, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<object>(source));
                m_supported.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata>(new NodeMetadata(source, source.NodeId)
                    {
                        NodeClass = NodeClass.Object,
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }));
                IAsyncNodeManager[] owners = unsupportedFirst
                    ? [m_unsupported.Object, m_supported.Object, m_terminal.Object]
                    : [m_supported.Object, m_terminal.Object, m_unsupported.Object];
                m_master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    owners);
            }

            public EventManager Events { get; }

            public IMonitoredItem Item { get; private set; }

            public ServiceResult Result { get; private set; }

            public async Task CreateAsync()
            {
                var request = new MonitoredItemCreateRequest
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
                        QueueSize = 10,
                        Filter = new ExtensionObject(new EventFilter
                        {
                            SelectClauses =
                            [
                                new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                    AttributeId = Attributes.Value,
                                    BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                                }
                            ]
                        })
                    }
                };
                var errors = new ServiceResult[1];
                var items = new IMonitoredItem[1];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                await m_master.CreateMonitoredItemsAsync(
                    context, 1, 1000, TimestampsToReturn.Both, [request], errors,
                    new MonitoringFilterResult[1], items, false).ConfigureAwait(false);
                Item = items[0];
                Result = errors[0];
            }

            public async Task<ServiceResult> DeleteAsync()
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None);
                var errors = new ServiceResult[1];
                await m_master.DeleteMonitoredItemsAsync(context, 1, [Item], errors).ConfigureAwait(false);
                return errors[0];
            }

            public async Task<ArrayOf<ServiceResult>> ModifyEventPairWithFailureAsync(bool throws)
            {
                await CreateAsync().ConfigureAwait(false);
                IMonitoredItem first = Item;
                await CreateAsync().ConfigureAwait(false);
                IMonitoredItem second = Item;
                Moq.Language.Flow.ISetup<IAsyncNodeManager, ValueTask<ServiceResult>> failing = m_terminal
                    .Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), 1,
                    It.Is<IEventMonitoredItem>(item => item.Id == first.Id), false,
                    It.IsAny<CancellationToken>()));
                if (throws)
                {
                    failing.ThrowsAsync(new ServiceResultException(StatusCodes.BadServerNotConnected));
                }
                else
                {
                    failing.Returns(new ValueTask<ServiceResult>(StatusCodes.BadServerNotConnected));
                }
                foreach (Mock<IAsyncNodeManager> owner in new[] { m_supported, m_unsupported, m_terminal })
                {
                    owner.Invocations.Clear();
                }
                var filter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            AttributeId = Attributes.Value,
                            BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                        }
                    ]
                };
                var errors = new ServiceResult[2];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.ModifyMonitoredItems, RequestLifetime.None);
                await m_master.ModifyMonitoredItemsAsync(
                    context, TimestampsToReturn.Both, [first, second],
                    [CreateModify(first.Id, filter), CreateModify(second.Id, filter)],
                    errors, new MonitoringFilterResult[2]).ConfigureAwait(false);
                return errors;
            }

            public async Task<ArrayOf<ServiceResult>> ModifyDataBatchWithFailureAsync(bool completeFirst)
            {
                var first = new Mock<IMonitoredItem>();
                var second = new Mock<IMonitoredItem>();
                var third = new Mock<IMonitoredItem>();
                first.SetupGet(value => value.NodeManager).Returns(m_supported.Object);
                second.SetupGet(value => value.NodeManager).Returns(m_supported.Object);
                third.SetupGet(value => value.NodeManager).Returns(m_terminal.Object);
                foreach (Mock<IMonitoredItem> item in new[] { first, second, third })
                {
                    item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                }
                m_supported.Setup(value => value.ModifyMonitoredItemsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<TimestampsToReturn>(),
                        It.IsAny<IList<IMonitoredItem>>(), It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, TimestampsToReturn _, IList<IMonitoredItem> _,
                        ArrayOf<MonitoredItemModifyRequest> requests, IList<ServiceResult> errors,
                        IList<MonitoringFilterResult> _, CancellationToken _) =>
                    {
                        Assert.That(requests[2].Processed, Is.True);
                        if (completeFirst)
                        {
                            requests[0].Processed = true;
                            errors[0] = ServiceResult.Good;
                        }
                        throw new InvalidOperationException("Simulated owner failure.");
                    });
                m_terminal.Setup(value => value.ModifyMonitoredItemsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<TimestampsToReturn>(),
                        It.IsAny<IList<IMonitoredItem>>(), It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, TimestampsToReturn _, IList<IMonitoredItem> _,
                        ArrayOf<MonitoredItemModifyRequest> requests, IList<ServiceResult> errors,
                        IList<MonitoringFilterResult> _, CancellationToken _) =>
                    {
                        Assert.That(requests[2].Processed, Is.False);
                        requests[2].Processed = true;
                        errors[2] = ServiceResult.Good;
                        return default;
                    });
                var results = new ServiceResult[3];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.ModifyMonitoredItems, RequestLifetime.None);
                await m_master.ModifyMonitoredItemsAsync(
                    context, TimestampsToReturn.Both, [first.Object, second.Object, third.Object],
                    [CreateModify(1), CreateModify(2), CreateModify(3)], results, new MonitoringFilterResult[3])
                    .ConfigureAwait(false);
                return results;
            }

            public void VerifySubscriptions(bool unsubscribe, int count = 1)
            {
                foreach (Mock<IAsyncNodeManager> owner in new[] { m_supported, m_unsupported, m_terminal })
                {
                    owner.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), unsubscribe,
                        CancellationToken.None), Times.Exactly(count));
                }
            }

            public void Dispose()
            {
                m_master.Dispose();
                Events.Dispose();
                m_queues.Dispose();
            }

            private static Mock<IAsyncNodeManager> CreateOwner(string uri, StatusCode subscribe, StatusCode unsubscribe)
            {
                var owner = new Mock<IAsyncNodeManager>();
                owner.SetupGet(value => value.NamespaceUris).Returns([uri]);
                owner.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), false,
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(new ServiceResult(subscribe)));
                owner.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), true,
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(new ServiceResult(unsubscribe)));
                return owner;
            }

            private static MonitoredItemModifyRequest CreateModify(uint id, EventFilter filter = null)
            {
                return new MonitoredItemModifyRequest
                {
                    MonitoredItemId = id,
                    RequestedParameters = new MonitoringParameters
                    {
                        QueueSize = 10,
                        SamplingInterval = 500,
                        Filter = filter == null ? ExtensionObject.Null : new ExtensionObject(filter)
                    }
                };
            }

            private readonly MasterNodeManager m_master;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly Mock<IAsyncNodeManager> m_supported;
            private readonly Mock<IAsyncNodeManager> m_unsupported;
            private readonly Mock<IAsyncNodeManager> m_terminal;
        }
    }
}
