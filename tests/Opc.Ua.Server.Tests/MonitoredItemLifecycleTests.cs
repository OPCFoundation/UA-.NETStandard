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
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the deleted, detached, and recovery state of a monitored item.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public sealed class MonitoredItemLifecycleTests
    {
        [Test]
        public void RepeatedDeletionMarksPublishBadNodeIdUnknownOnce()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(telemetry);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            lifecycle.MarkNodeDeleted();
            lifecycle.QueueNodeIdUnknown();

            Queue<MonitoredItemNotification> first = Publish(item, telemetry, 10, out bool more);
            Queue<MonitoredItemNotification> second = Publish(item, telemetry, 10, out bool moreAfter);

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(first.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(more, Is.False);
                Assert.That(second, Is.Empty);
                Assert.That(moreAfter, Is.False);
            });
        }

        [Test]
        public void QueueSizeOnePublishesPreDeletionValueAndRequiredBad()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(telemetry, queueSize: 1);
            var lifecycle = (IMonitoredItemLifecycle)item;
            var beforeDeletion = new DataValue(new Variant(7), StatusCodes.Good);
            var recovered = new DataValue(new Variant(42), StatusCodes.Good);

            item.QueueValue(beforeDeletion, ServiceResult.Good);
            lifecycle.MarkNodeDeleted();
            item.QueueValue(recovered, ServiceResult.Good);

            Queue<MonitoredItemNotification> first = Publish(item, telemetry, 1, out bool more);
            Queue<MonitoredItemNotification> second = Publish(item, telemetry, 1, out bool moreAfter);
            item.QueueValue(recovered, ServiceResult.Good);
            Queue<MonitoredItemNotification> third = Publish(item, telemetry, 1, out bool moreFinally);

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(first.Peek().Value, Is.EqualTo(beforeDeletion));
                Assert.That(more, Is.True);
                Assert.That(second, Has.Count.EqualTo(1));
                Assert.That(second.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(moreAfter, Is.False);
                Assert.That(third, Has.Count.EqualTo(1));
                Assert.That(third.Peek().Value, Is.EqualTo(recovered));
                Assert.That(moreFinally, Is.False);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LifecycleValuesObeyQueueDiscardPolicyWithoutDiscardingBad(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(
                telemetry,
                queueSize: 3,
                discardOldest: discardOldest);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            item.QueueValue(new DataValue(new Variant(1), StatusCodes.Good), ServiceResult.Good);
            item.QueueValue(new DataValue(new Variant(2), StatusCodes.Good), ServiceResult.Good);
            item.QueueValue(new DataValue(new Variant(3), StatusCodes.Good), ServiceResult.Good);

            Queue<MonitoredItemNotification> first = Publish(item, telemetry, 1, out bool more);
            Queue<MonitoredItemNotification> second = Publish(item, telemetry, 10, out bool moreAfter);

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(first.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(more, Is.True);
                Assert.That(second, Has.Count.EqualTo(2));
                Assert.That(
                    second.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(discardOldest ? 2 : 1)));
                Assert.That(second.Dequeue().Value.WrappedValue, Is.EqualTo(new Variant(3)));
                Assert.That(moreAfter, Is.False);
            });
        }

        [Test]
        public void RecoveryValuesPassTheConfiguredDataChangeFilter()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.Status,
                DeadbandType = (uint)DeadbandType.None
            };
            using MonitoredItem item = CreateMonitoredItem(
                telemetry,
                queueSize: 4,
                filter: filter);
            var lifecycle = (IMonitoredItemLifecycle)item;

            item.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);
            lifecycle.MarkNodeDeleted();
            item.QueueValue(
                new DataValue(new Variant(2), StatusCodes.Good),
                ServiceResult.Good);
            item.QueueValue(
                new DataValue(new Variant(3), StatusCodes.Good),
                ServiceResult.Good);

            Queue<MonitoredItemNotification> notifications =
                Publish(item, telemetry, 10, out bool more);

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Has.Count.EqualTo(3));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(1)));
                Assert.That(
                    notifications.Dequeue().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(2)));
                Assert.That(more, Is.False);
            });
        }

        [Test]
        public void RebindUpdatesOwnershipWithoutErasingPendingBad()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var originalManager = new Mock<IAsyncNodeManager>();
            var reboundManager = new Mock<IAsyncNodeManager>();
            var originalHandle = new object();
            var reboundHandle = new object();
            using MonitoredItem item = CreateMonitoredItem(
                telemetry,
                nodeManager: originalManager.Object,
                managerHandle: originalHandle);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            lifecycle.Rebind(reboundManager.Object, reboundHandle);
            Queue<MonitoredItemNotification> notifications = Publish(item, telemetry, 1, out _);

            Assert.Multiple(() =>
            {
                Assert.That(lifecycle.IsDetached, Is.False);
                Assert.That(lifecycle.IsDeleted, Is.False);
                Assert.That(item.NodeManager, Is.SameAs(reboundManager.Object));
                Assert.That(item.ManagerHandle, Is.SameAs(reboundHandle));
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(
                    notifications.Peek().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public void NewDeletionEpochAfterRecoveryPublishesBadAgain()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var reboundManager = new Mock<IAsyncNodeManager>();
            using MonitoredItem item = CreateMonitoredItem(telemetry, queueSize: 2);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            item.QueueValue(new DataValue(new Variant(1), StatusCodes.Good), ServiceResult.Good);
            Queue<MonitoredItemNotification> firstEpoch = Publish(item, telemetry, 2, out _);

            lifecycle.Rebind(reboundManager.Object, new object());
            lifecycle.MarkNodeDeleted();
            Queue<MonitoredItemNotification> secondEpoch = Publish(item, telemetry, 1, out _);

            Assert.Multiple(() =>
            {
                Assert.That(firstEpoch, Has.Count.EqualTo(2));
                Assert.That(firstEpoch.Dequeue().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(firstEpoch.Dequeue().Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(secondEpoch, Has.Count.EqualTo(1));
                Assert.That(
                    secondEpoch.Peek().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public void MultiplePendingDeletionEpochsPreserveChronologicalOrder()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var reboundManager = new Mock<IAsyncNodeManager>();
            using MonitoredItem item = CreateMonitoredItem(telemetry, queueSize: 4);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            item.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);
            lifecycle.Rebind(reboundManager.Object, new object());
            lifecycle.MarkNodeDeleted();
            item.QueueValue(
                new DataValue(new Variant(2), StatusCodes.Good),
                ServiceResult.Good);

            Queue<MonitoredItemNotification> notifications =
                Publish(item, telemetry, 10, out bool more);

            Assert.Multiple(() =>
            {
                Assert.That(notifications, Has.Count.EqualTo(4));
                Assert.That(
                    notifications.Dequeue().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(1)));
                Assert.That(
                    notifications.Dequeue().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(2)));
                Assert.That(more, Is.False);
            });
        }

        [Test]
        public void RestoredBadNodeIdUnknownIsProtectedUntilPublication()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem source = CreateMonitoredItem(telemetry, queueSize: 2);
            ((IMonitoredItemLifecycle)source).MarkNodeDeleted();
            IStoredMonitoredItem stored = source.ToStorableMonitoredItem();
            DateTimeUtc timestamp = stored.LastValue.SourceTimestamp;
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(telemetry, queueFactory);
            using var item = new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);
            var recovered = new DataValue(new Variant(99), StatusCodes.Good);

            item.QueueValue(recovered, ServiceResult.Good);
            Queue<MonitoredItemNotification> first = Publish(item, telemetry, 1, out bool more);
            Queue<MonitoredItemNotification> second = Publish(item, telemetry, 10, out _);

            Assert.Multiple(() =>
            {
                Assert.That(stored.LastValue.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(stored.LastError.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(first.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(first.Peek().Value.SourceTimestamp, Is.EqualTo(timestamp));
                Assert.That(more, Is.True);
                Assert.That(second, Has.Count.EqualTo(1));
                Assert.That(second.Peek().Value, Is.EqualTo(recovered));
            });
        }

        [Test]
        public void DisabledItemPublishesRememberedDeletionAfterReportingIsEnabled()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(
                telemetry,
                monitoringMode: MonitoringMode.Disabled);
            var lifecycle = (IMonitoredItemLifecycle)item;

            lifecycle.MarkNodeDeleted();
            Queue<MonitoredItemNotification> disabled = Publish(item, telemetry, 1, out _);
            item.SetMonitoringMode(MonitoringMode.Reporting);
            Queue<MonitoredItemNotification> enabled = Publish(item, telemetry, 1, out _);

            Assert.Multiple(() =>
            {
                Assert.That(disabled, Is.Empty);
                Assert.That(enabled, Has.Count.EqualTo(1));
                Assert.That(enabled.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public void RequiredQueueResizeRetriesTransientDurableDequeue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queue = new TransientDequeueQueue(
                new DataChangeMonitoredItemQueue(
                    createDurable: false,
                    monitoredItemId: 1,
                    telemetry));
            queue.ResetQueue(2, queueErrors: true);
            queue.Enqueue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);
            queue.Enqueue(
                new DataValue(new Variant(2), StatusCodes.Good),
                ServiceResult.Good);
            using var handler = new DataChangeQueueHandler(
                queue,
                queueSize: 2,
                discardOldest: true,
                samplingInterval: 0,
                DiagnosticsMasks.OperationAll,
                telemetry,
                discardedValueHandler: null);
            queue.FailuresRemaining = 1;

            handler.QueueRequiredValue(
                new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));

            var values = new List<DataValue>();
            while (handler.PublishSingleValue(
                out DataValue value,
                out _,
                out _))
            {
                values.Add(value);
            }

            Assert.Multiple(() =>
            {
                Assert.That(values, Has.Count.EqualTo(3));
                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(1)));
                Assert.That(values[1].WrappedValue, Is.EqualTo(new Variant(2)));
                Assert.That(values[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        private static MonitoredItem CreateMonitoredItem(
            ITelemetryContext telemetry,
            uint queueSize = 1,
            bool discardOldest = true,
            MonitoringMode monitoringMode = MonitoringMode.Reporting,
            IAsyncNodeManager nodeManager = null,
            object managerHandle = null,
            MonitoringFilter filter = null)
        {
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(telemetry, queueFactory);

            return new MonitoredItem(
                server.Object,
                nodeManager ?? new Mock<IAsyncNodeManager>().Object,
                managerHandle!,
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId
                {
                    NodeId = new NodeId("Lifecycle", 2),
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode,
                clientHandle: 3,
                originalFilter: filter,
                filterToUse: filter,
                range: null,
                samplingInterval: 1000,
                queueSize,
                discardOldest,
                sourceSamplingInterval: 1000);
        }

        private static Mock<IServerInternal> CreateServerMock(
            ITelemetryContext telemetry,
            IMonitoredItemQueueFactory queueFactory)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            server.Setup(s => s.SubscriptionStore).Returns(Mock.Of<ISubscriptionStore>());
            return server;
        }

        private static Queue<MonitoredItemNotification> Publish(
            MonitoredItem item,
            ITelemetryContext telemetry,
            uint maxNotificationsPerPublish,
            out bool more)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            ILogger logger = telemetry.CreateLogger<MonitoredItemLifecycleTests>();
            more = item.Publish(
                new OperationContext(item),
                notifications,
                diagnostics,
                maxNotificationsPerPublish,
                logger);
            return notifications;
        }

        private sealed class TransientDequeueQueue : IDataChangeMonitoredItemQueue
        {
            public TransientDequeueQueue(IDataChangeMonitoredItemQueue inner)
            {
                m_inner = inner;
            }

            public int FailuresRemaining { get; set; }

            public uint MonitoredItemId => m_inner.MonitoredItemId;

            public bool IsDurable => true;

            public uint QueueSize => m_inner.QueueSize;

            public int ItemsInQueue => m_inner.ItemsInQueue;

            public void ResetQueue(uint queueSize, bool queueErrors)
            {
                m_inner.ResetQueue(queueSize, queueErrors);
            }

            public void Enqueue(DataValue value, ServiceResult error)
            {
                m_inner.Enqueue(value, error);
            }

            public bool Dequeue(out DataValue value, out ServiceResult error)
            {
                if (ItemsInQueue > 0 && FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    value = default;
                    error = null!;
                    return false;
                }

                return m_inner.Dequeue(out value, out error);
            }

            public bool TryPeekOldestValue(out DataValue value)
            {
                return m_inner.TryPeekOldestValue(out value);
            }

            public void OverwriteLastValue(DataValue value, ServiceResult error)
            {
                m_inner.OverwriteLastValue(value, error);
            }

            public bool TryPeekLastValue(out DataValue value)
            {
                return m_inner.TryPeekLastValue(out value);
            }

            public void Dispose()
            {
                m_inner.Dispose();
            }

            private readonly IDataChangeMonitoredItemQueue m_inner;
        }
    }
}
