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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies per-owner failure isolation and request cancellation during monitored-item batch dispatch.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class MonitoredItemBatchIsolationRegressionTests
    {
        /// <summary>
        /// Verifies that one owner's failure preserves completed item results and still dispatches later owners.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FailingOwnerDoesNotDiscardCompletedResultsOrSkipOtherOwnersAsync(bool setMode, bool partial)
        {
            using var harness = new BatchHarness(partial);
            await harness.DispatchAsync(setMode, CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Errors[1].StatusCode,
                Is.EqualTo(partial ? StatusCodes.Good : StatusCodes.BadUnexpectedError));
            Assert.That(harness.Errors[2].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(harness.Errors[3].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Calls, Is.EqualTo(s_allOwners));
        }

        /// <summary>
        /// Verifies that request cancellation stops batch dispatch rather than being converted to an item-level
        /// failure.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RequestCancellationStillStopsDispatchInsteadOfBecomingAnItemFailure(bool setMode)
        {
            using var cancellation = new CancellationTokenSource();
            using var harness = new BatchHarness(partial: false, cancellation);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await harness.DispatchAsync(setMode, cancellation.Token).ConfigureAwait(false));
            Assert.That(harness.Calls, Is.EqualTo(s_cancelledOwners));
            Assert.That(harness.Errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that event unsubscribe failures do not skip other owners or independently monitored events.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EventUnsubscribeFailureDoesNotSkipOtherManagersOrItemsAsync(bool allEvents)
        {
            using var harness = new BatchHarness(partial: false);
            harness.UseEventItems(allEvents);
            await harness.DispatchAsync(setMode: false, CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Errors[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(harness.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.EventCalls, Is.EqualTo(allEvents ? s_allEventOwners : s_singleEventOwners));
        }

        /// <summary>
        /// Verifies that a failing event mode change leaves the next event item eligible for processing.
        /// </summary>
        [Test]
        public async Task EventModeFailureDoesNotSkipTheNextMonitoredItemAsync()
        {
            using var harness = new BatchHarness(partial: false);
            harness.UseEventItems(allEvents: false);
            Mock<IEventMonitoredItem> other = harness.FailFirstEventModeChange();
            await harness.DispatchAsync(setMode: true, CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Errors[0].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(harness.Errors[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            other.Verify(item => item.SetMonitoringMode(MonitoringMode.Disabled), Times.Once);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EventCreateFailureDoesNotLeakEventManagerItemsAsync(bool throws)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                const string uri = "urn:tests:event-startup-failure";
                ushort ns = server.Object.NamespaceUris.GetIndexOrAppend(uri);
                var source = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Source", ns),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                var owner = new Mock<IAsyncNodeManager>();
                owner.SetupGet(value => value.NamespaceUris).Returns([uri]);
                owner.Setup(value => value.GetManagerHandleAsync(
                        source.NodeId, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<object>(source));
                owner.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata>(new NodeMetadata(source, source.NodeId)
                    {
                        NodeClass = NodeClass.Object,
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }));
                owner.Setup(value => value.SubscribeToEventsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(), 1,
                        It.IsAny<IEventMonitoredItem>(), false, It.IsAny<CancellationToken>()))
                    .Returns(() => throws
                        ? throw new ServiceResultException(StatusCodes.BadServerNotConnected)
                        : new ValueTask<ServiceResult>(StatusCodes.BadServerNotConnected));
                owner.Setup(value => value.SubscribeToEventsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(), 1,
                        It.IsAny<IEventMonitoredItem>(), true, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var manager = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    [owner.Object]);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
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
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = source.NodeId, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        QueueSize = 1,
                        Filter = new ExtensionObject(filter)
                    }
                };
                var errors = new ServiceResult[1];
                var filterErrors = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];

                await manager.CreateMonitoredItemsAsync(
                    context, 1, 1000, TimestampsToReturn.Both,
                    [request], errors, filterErrors, items, false).ConfigureAwait(false);

                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
                Assert.That(items[0], Is.Null);
                Assert.That(events.GetMonitoredItems(), Is.Empty);
                owner.Verify(value => value.SubscribeToEventsAsync(
                    context, source, 1, It.IsAny<IEventMonitoredItem>(), true, CancellationToken.None), Times.Once);
            }
        }

        /// <summary>
        /// Supplies three monitored-item owners with controlled partial failures, cancellation, and event callbacks.
        /// </summary>
        private sealed class BatchHarness : IDisposable
        {
            /// <summary>
            /// Creates the master manager and owner mocks, configuring the middle owner to fail or cancel dispatch.
            /// </summary>
            public BatchHarness(bool partial, CancellationTokenSource cancellation = null)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                m_events = new EventManager(server.Object, 100, 100);
                server.SetupGet(value => value.EventManager).Returns(m_events);
                IAsyncNodeManager[] owners = m_owners;
                for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
                {
                    int ownerNumber = ownerIndex;
                    var owner = new Mock<IAsyncNodeManager>();
                    owner.SetupGet(value => value.NamespaceUris).Returns(["urn:batch-owner:" + ownerIndex]);
                    owner.Setup(value => value.DeleteMonitoredItemsAsync(
                            It.IsAny<OperationContext>(), It.IsAny<IList<IMonitoredItem>>(),
                            It.IsAny<IList<bool>>(), It.IsAny<IList<ServiceResult>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((OperationContext _, IList<IMonitoredItem> _, IList<bool> processed,
                            IList<ServiceResult> errors, CancellationToken ct) =>
                        {
                            Process(ownerNumber, processed, errors, partial, cancellation, ct);
                            return default;
                        });
                    owner.Setup(value => value.SetMonitoringModeAsync(
                            It.IsAny<OperationContext>(), It.IsAny<MonitoringMode>(),
                            It.IsAny<IList<IMonitoredItem>>(), It.IsAny<IList<bool>>(),
                            It.IsAny<IList<ServiceResult>>(), It.IsAny<CancellationToken>()))
                        .Returns((OperationContext _, MonitoringMode _, IList<IMonitoredItem> _, IList<bool> processed,
                            IList<ServiceResult> errors, CancellationToken ct) =>
                        {
                            Process(ownerNumber, processed, errors, partial, cancellation, ct);
                            return default;
                        });
                    owners[ownerIndex] = owner.Object;
                }
                m_master = new MasterNodeManager(server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() }, null, owners);
                int[] owningIndices = [0, 1, 1, 2];
                for (int i = 0; i < m_items.Length; i++)
                {
                    var item = new Mock<IMonitoredItem>();
                    item.SetupGet(value => value.Id).Returns((uint)i + 1);
                    item.SetupGet(value => value.NodeManager).Returns(owners[owningIndices[i]]);
                    m_items[i] = item.Object;
                }
            }

            /// <summary>
            /// Gets the result slots populated by monitored-item batch dispatch.
            /// </summary>
            public ServiceResult[] Errors { get; } = new ServiceResult[4];

            /// <summary>
            /// Gets owner indices in the order their data-change batch callbacks ran.
            /// </summary>
            public List<int> Calls { get; } = [];

            /// <summary>
            /// Gets recorded event unsubscribe owners, with an offset distinguishing single-source callbacks.
            /// </summary>
            public List<int> EventCalls { get; } = [];

            /// <summary>
            /// Replaces the data-change batch with two event items and configures their unsubscribe failures.
            /// </summary>
            public void UseEventItems(bool allEvents)
            {
                for (int i = 0; i < m_owners.Length; i++)
                {
                    int ownerIndex = i;
                    var owner = Mock.Get(m_owners[i]);
                    owner.Setup(value => value.SubscribeToAllEventsAsync(
                            It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(),
                            true, It.IsAny<CancellationToken>()))
                        .Returns(() => Unsubscribe(ownerIndex, all: true));
                    owner.Setup(value => value.SubscribeToEventsAsync(
                            It.IsAny<OperationContext>(), It.IsAny<object>(), 1, It.IsAny<IEventMonitoredItem>(),
                            true, It.IsAny<CancellationToken>()))
                        .Returns(() => Unsubscribe(ownerIndex, all: false));
                }
                for (int i = 0; i < 2; i++)
                {
                    var item = new Mock<IEventMonitoredItem>();
                    item.SetupGet(value => value.Id).Returns((uint)i + 1);
                    item.SetupGet(value => value.NodeManager).Returns(m_owners[i + 1]);
                    item.SetupGet(value => value.MonitoredItemType).Returns(
                        i == 0 && allEvents
                            ? MonitoredItemTypeMask.Events | MonitoredItemTypeMask.AllEvents
                            : MonitoredItemTypeMask.Events);
                    m_items[i] = item.Object;
                }
                m_items[2] = null;
                m_items[3] = null;
            }

            /// <summary>
            /// Dispatches either disabling or deletion of the configured items through the master node manager.
            /// </summary>
            public async ValueTask DispatchAsync(bool setMode, CancellationToken ct)
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None);
                if (setMode)
                {
                    await m_master.SetMonitoringModeAsync(context, MonitoringMode.Disabled, m_items, Errors, ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_master.DeleteMonitoredItemsAsync(context, 1, m_items, Errors, ct).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Makes the first event item's mode change throw and returns the second item for progress verification.
            /// </summary>
            public Mock<IEventMonitoredItem> FailFirstEventModeChange()
            {
                Mock.Get((IEventMonitoredItem)m_items[0]).Setup(item => item.SetMonitoringMode(It.IsAny<MonitoringMode>()))
                    .Throws<InvalidOperationException>();
                return Mock.Get((IEventMonitoredItem)m_items[1]);
            }

            /// <summary>
            /// Releases the master manager, event manager, and queue factory used by the batch.
            /// </summary>
            public void Dispose()
            {
                m_master.Dispose();
                m_events.Dispose();
                m_queues.Dispose();
            }

            /// <summary>
            /// Records an event unsubscribe and injects a failure for the middle owner.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            private ValueTask<ServiceResult> Unsubscribe(int owner, bool all)
            {
                EventCalls.Add(owner + (all ? 0 : 10));
                if (owner == 1)
                {
                    throw new InvalidOperationException("event unsubscribe failure");
                }
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

            /// <summary>
            /// Records owner dispatch and injects cancellation or failure before or after partial item processing.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            private void Process(
                int owner,
                IList<bool> processed,
                IList<ServiceResult> errors,
                bool partial,
                CancellationTokenSource cancellation,
                CancellationToken ct)
            {
                Calls.Add(owner);
                if (owner == 1 && cancellation != null)
                {
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                }
                if (owner == 1 && !partial)
                {
                    throw new InvalidOperationException("owner failure before work");
                }
                for (int i = 0; i < processed.Count; i++)
                {
                    if (processed[i])
                    {
                        continue;
                    }
                    processed[i] = true;
                    errors[i] = ServiceResult.Good;
                    if (owner == 1)
                    {
                        throw new InvalidOperationException("owner failure after partial work");
                    }
                }
            }

            /// <summary>
            /// Routes item batches to their registered node-manager owners.
            /// </summary>
            private readonly MasterNodeManager m_master;

            /// <summary>
            /// Owns the monitored-item queues supplied by the deterministic server.
            /// </summary>
            private readonly MonitoredItemQueueFactory m_queues;

            /// <summary>
            /// Supplies event-item bookkeeping for event deletion and mode-change scenarios.
            /// </summary>
            private readonly EventManager m_events;

            /// <summary>
            /// Stores the item batch, including null slots when testing only event items.
            /// </summary>
            private readonly IMonitoredItem[] m_items = new IMonitoredItem[4];

            /// <summary>
            /// Stores the ordered owners used to verify dispatch isolation.
            /// </summary>
            private readonly IAsyncNodeManager[] m_owners = new IAsyncNodeManager[3];
        }

        /// <summary>
        /// Defines the owner sequence expected when an item-level failure does not stop dispatch.
        /// </summary>
        private static readonly int[] s_allOwners = [0, 1, 2];

        /// <summary>
        /// Defines the owner sequence expected when cancellation stops dispatch at the middle owner.
        /// </summary>
        private static readonly int[] s_cancelledOwners = [0, 1];

        /// <summary>
        /// Defines all-events unsubscribe attempts followed by the independent single-source unsubscribe.
        /// </summary>
        private static readonly int[] s_allEventOwners = [0, 1, 2, 12];

        /// <summary>
        /// Defines the independent single-source unsubscribe attempts despite the first owner's failure.
        /// </summary>
        private static readonly int[] s_singleEventOwners = [11, 12];
    }
}
