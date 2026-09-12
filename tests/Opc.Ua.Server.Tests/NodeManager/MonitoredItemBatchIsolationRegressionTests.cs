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
    [TestFixture]
    [Category("NodeManager")]
    public sealed class MonitoredItemBatchIsolationRegressionTests
    {
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

        private sealed class BatchHarness : IDisposable
        {
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

            public ServiceResult[] Errors { get; } = new ServiceResult[4];
            public List<int> Calls { get; } = [];
            public List<int> EventCalls { get; } = [];

            public void UseEventItems(bool allEvents)
            {
                for (int i = 0; i < m_owners.Length; i++)
                {
                    int ownerIndex = i;
                    Mock<IAsyncNodeManager> owner = Mock.Get(m_owners[i]);
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

            public Mock<IEventMonitoredItem> FailFirstEventModeChange()
            {
                Mock.Get((IEventMonitoredItem)m_items[0]).Setup(item => item.SetMonitoringMode(It.IsAny<MonitoringMode>()))
                    .Throws<InvalidOperationException>();
                return Mock.Get((IEventMonitoredItem)m_items[1]);
            }

            public void Dispose()
            {
                m_master.Dispose();
                m_events.Dispose();
                m_queues.Dispose();
            }

            private ValueTask<ServiceResult> Unsubscribe(int owner, bool all)
            {
                EventCalls.Add(owner + (all ? 0 : 10));
                if (owner == 1)
                {
                    throw new InvalidOperationException("event unsubscribe failure");
                }
                return new ValueTask<ServiceResult>(ServiceResult.Good);
            }

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

            private readonly MasterNodeManager m_master;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly EventManager m_events;
            private readonly IMonitoredItem[] m_items = new IMonitoredItem[4];
            private readonly IAsyncNodeManager[] m_owners = new IAsyncNodeManager[3];
        }

        private static readonly int[] s_allOwners = [0, 1, 2];
        private static readonly int[] s_cancelledOwners = [0, 1];
        private static readonly int[] s_allEventOwners = [0, 1, 2, 12];
        private static readonly int[] s_singleEventOwners = [11, 12];
    }
}
