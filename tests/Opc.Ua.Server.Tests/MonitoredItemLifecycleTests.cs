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
using System.Linq;
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
            var lifecycle = (IDetachableMonitoredItem)item;

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
        public void QueueSizeOnePublishesRequiredBadInsteadOfThePreDeletionValue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(telemetry, queueSize: 1);
            var lifecycle = (IDetachableMonitoredItem)item;
            var beforeDeletion = new DataValue(new Variant(7), StatusCodes.Good);
            var recovered = new DataValue(new Variant(42), StatusCodes.Good);

            item.QueueValue(beforeDeletion, ServiceResult.Good);
            lifecycle.MarkNodeDeleted();

            // Part 4 5.13.1.5 makes a queue of size one a buffer holding the newest Notification,
            // so the marker takes the single slot and the value sampled before the deletion goes.
            Queue<MonitoredItemNotification> first = Publish(item, telemetry, 1, out bool more);
            item.QueueValue(recovered, ServiceResult.Good);
            Queue<MonitoredItemNotification> second = Publish(item, telemetry, 1, out bool moreAfter);

            Assert.Multiple(() =>
            {
                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(first.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(more, Is.False);
                Assert.That(second, Has.Count.EqualTo(1));
                Assert.That(second.Peek().Value, Is.EqualTo(recovered));
                Assert.That(moreAfter, Is.False);
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LifecycleValuesObeyQueueDiscardPolicyWithoutDiscardingBad(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 2,
                discardOldest: discardOldest);

            handler.QueueRequiredValue(
                new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));
            handler.QueueValue(new DataValue(new Variant(1), StatusCodes.Good), ServiceResult.Good);
            handler.QueueValue(new DataValue(new Variant(2), StatusCodes.Good), ServiceResult.Good);
            handler.QueueValue(new DataValue(new Variant(3), StatusCodes.Good), ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                // The marker occupies an ordinary slot but is never the value that gets
                // discarded, so the incoming value is dropped instead once the queue is full.
                Assert.That(published, Has.Count.EqualTo(2));
                Assert.That(published[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(
                    published[1].WrappedValue,
                    Is.EqualTo(new Variant(discardOldest ? 1 : 3)));
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
                filter: filter,
                samplingInterval: 0);
            var lifecycle = (IDetachableMonitoredItem)item;

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
                // The status-only filter lets the value after the marker through and filters
                // the one that follows it, because its status is unchanged.
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
            var lifecycle = (IDetachableMonitoredItem)item;

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
            var lifecycle = (IDetachableMonitoredItem)item;

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
        public void StoringAndRestoringADeletedItemCarriesTheDeletedAndDetachedFlags()
        {
            // The flags are persisted explicitly rather than inferred from the last value, so a
            // restored item cannot silently disagree with the item it was stored from.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem source = CreateMonitoredItem(telemetry, queueSize: 2);
            var sourceLifecycle = (IDetachableMonitoredItem)source;
            sourceLifecycle.MarkNodeDeleted();
            sourceLifecycle.BeginDetach();

            IStoredMonitoredItem stored = source.ToStorableMonitoredItem();

            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(telemetry, queueFactory);
            using var restored = new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);
            var restoredLifecycle = (IDetachableMonitoredItem)restored;

            Assert.Multiple(() =>
            {
                Assert.That(stored.IsDeleted, Is.True);
                Assert.That(stored.IsDetached, Is.True);
                Assert.That(restoredLifecycle.IsDeleted, Is.True);
                Assert.That(restoredLifecycle.IsDetached, Is.True);
            });
        }

        [Test]
        public void StoringAndRestoringALiveItemLeavesTheFlagsClear()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem source = CreateMonitoredItem(telemetry, queueSize: 2);
            source.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);

            IStoredMonitoredItem stored = source.ToStorableMonitoredItem();

            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(telemetry, queueFactory);
            using var restored = new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);
            var restoredLifecycle = (IDetachableMonitoredItem)restored;

            Assert.Multiple(() =>
            {
                Assert.That(stored.IsDeleted, Is.False);
                Assert.That(stored.IsDetached, Is.False);
                Assert.That(restoredLifecycle.IsDeleted, Is.False);
                Assert.That(restoredLifecycle.IsDetached, Is.False);
            });
        }
        [Test]
        public void MultiplePendingDeletionEpochsCollapseIntoOneMarker()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var reboundManager = new Mock<IAsyncNodeManager>();
            using MonitoredItem item = CreateMonitoredItem(
                telemetry,
                queueSize: 4,
                samplingInterval: 0);
            var lifecycle = (IDetachableMonitoredItem)item;

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
                // A remove, re-add, remove sequence before a Publish says the same thing once,
                // so the second marker is collapsed into the one that is still pending.
                Assert.That(notifications, Has.Count.EqualTo(3));
                Assert.That(
                    notifications.Dequeue().Value.StatusCode,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(1)));
                Assert.That(
                    notifications.Dequeue().Value.WrappedValue,
                    Is.EqualTo(new Variant(2)));
                Assert.That(more, Is.False);
            });
        }

        [Test]
        public void DiscardRetriesTransientDurableDequeue()
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
                discardOldest: true,
                samplingInterval: 0,
                telemetry,
                discardedValueHandler: null);
            queue.FailuresRemaining = 1;

            handler.QueueRequiredValue(
                new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));

            var values = new List<DataValue>();
            while (handler.PublishSingleValue(out DataValue value, out _))
            {
                values.Add(value);
            }

            Assert.Multiple(() =>
            {
                // The queue was full, so the marker displaced the oldest value even though the
                // durable queue transiently reported no value to hand back.
                Assert.That(values, Has.Count.EqualTo(2));
                Assert.That(values[0].WrappedValue, Is.EqualTo(new Variant(2)));
                Assert.That(values[0].StatusCode.Overflow, Is.True);
                Assert.That(values[1].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public void OrdinaryBadNodeIdUnknownSampleDoesNotBlockTheQueue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem item = CreateMonitoredItem(telemetry, queueSize: 1);
            var recovered = new DataValue(new Variant(42), StatusCodes.Good);

            item.QueueValue(default, new ServiceResult(StatusCodes.BadNodeIdUnknown));
            item.QueueValue(recovered, ServiceResult.Good);

            Queue<MonitoredItemNotification> notifications =
                Publish(item, telemetry, 10, out bool more);

            Assert.Multiple(() =>
            {
                Assert.That(((IDetachableMonitoredItem)item).IsDeleted, Is.False);
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications.Peek().Value.WrappedValue, Is.EqualTo(new Variant(42)));
                Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(more, Is.False);
            });
        }

        [Test]
        public void OrdinaryBadNodeIdUnknownValuesObeyTheConfiguredQueueSize()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 3,
                discardOldest: true);

            for (int ii = 0; ii < 6; ii++)
            {
                if (ii % 2 == 0)
                {
                    handler.QueueValue(
                        CreateBadNodeIdUnknownValue(),
                        new ServiceResult(StatusCodes.BadNodeIdUnknown));
                }
                else
                {
                    handler.QueueValue(
                        new DataValue(new Variant(ii), StatusCodes.Good),
                        ServiceResult.Good);
                }

                Assert.That(handler.ItemsInQueue, Is.LessThanOrEqualTo(3));
            }

            List<DataValue> published = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                Assert.That(published, Has.Count.EqualTo(3));
                Assert.That(published[0].WrappedValue, Is.EqualTo(new Variant(3)));
                Assert.That(published[0].StatusCode.Overflow, Is.True);
                Assert.That(
                    published[1].StatusCode.Code,
                    Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(published[2].WrappedValue, Is.EqualTo(new Variant(5)));
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SecondMarkerCollapsesIntoThePendingOneAndTheMarkerReportsOverflow(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 2,
                discardOldest: discardOldest);
            var marker = new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown);
            var markerError = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            handler.QueueRequiredValue(marker, markerError);
            handler.QueueValue(new DataValue(new Variant(1), StatusCodes.Good), ServiceResult.Good);
            handler.QueueRequiredValue(marker, markerError);
            handler.QueueValue(new DataValue(new Variant(2), StatusCodes.Good), ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                // The second marker says the same thing as the pending one, so it is collapsed
                // into it rather than displacing a real value.
                Assert.That(published, Has.Count.EqualTo(2));
                Assert.That(published[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                if (discardOldest)
                {
                    // The incoming value was dropped to keep the marker, which reports the loss.
                    Assert.That(published[0].StatusCode.Overflow, Is.True);
                    Assert.That(published[1].WrappedValue, Is.EqualTo(new Variant(1)));
                }
                else
                {
                    Assert.That(published[1].WrappedValue, Is.EqualTo(new Variant(2)));
                    Assert.That(published[1].StatusCode.Overflow, Is.True);
                }
            });
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ReplacementRequiredMarkerSupersedesPendingMarker(
            bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 2,
                discardOldest: discardOldest);
            DateTime timestamp = DateTime.UtcNow;
            var historyError = new DataValue(
                Variant.Null,
                StatusCodes.BadCommunicationError,
                timestamp,
                timestamp);
            var nodeDeleted = new DataValue(
                Variant.Null,
                StatusCodes.BadNodeIdUnknown,
                timestamp.AddMilliseconds(1),
                timestamp.AddMilliseconds(1));

            handler.QueueRequiredValue(
                historyError,
                new ServiceResult(StatusCodes.BadCommunicationError));
            handler.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);
            handler.QueueValue(
                new DataValue(new Variant(2), StatusCodes.Good),
                ServiceResult.Good);
            handler.QueueRequiredValue(
                nodeDeleted,
                new ServiceResult(StatusCodes.BadNodeIdUnknown),
                replaceExisting: true);
            handler.QueueValue(
                new DataValue(new Variant(3), StatusCodes.Good),
                ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);
            DataValue deletionNotification = published.Single(value =>
                value.StatusCode.Code == StatusCodes.BadNodeIdUnknown);

            Assert.That(
                published.Count(value =>
                    value.StatusCode.Code == StatusCodes.BadNodeIdUnknown),
                Is.EqualTo(1));
            Assert.That(deletionNotification.StatusCode.Overflow, Is.True);
            Assert.That(
                published.Any(value =>
                    value.StatusCode == StatusCodes.BadCommunicationError),
                Is.False);
            Assert.That(handler.HasRequiredValues, Is.False);
        }

        [Test]
        public void RequiredQueueRebuildsRetryTransientDurableDequeues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queue = new TransientDequeueQueue(
                new DataChangeMonitoredItemQueue(
                    createDurable: false,
                    monitoredItemId: 1,
                    telemetry));
            queue.ResetQueue(2, queueErrors: true);
            using var handler = new DataChangeQueueHandler(
                queue,
                discardOldest: true,
                samplingInterval: 0,
                telemetry,
                discardedValueHandler: null);
            DateTime timestamp = DateTime.UtcNow;
            handler.QueueRequiredValue(
                new DataValue(
                    Variant.Null,
                    StatusCodes.BadCommunicationError,
                    timestamp,
                    timestamp),
                new ServiceResult(StatusCodes.BadCommunicationError));
            handler.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);

            queue.FailuresRemaining = 1;
            handler.SetQueueSize(2, true, DiagnosticsMasks.OperationAll);
            queue.FailuresRemaining = 1;
            handler.QueueRequiredValue(
                new DataValue(
                    Variant.Null,
                    StatusCodes.BadNodeIdUnknown,
                    timestamp.AddMilliseconds(1),
                    timestamp.AddMilliseconds(1)),
                new ServiceResult(StatusCodes.BadNodeIdUnknown),
                replaceExisting: true);

            List<DataValue> published = DrainHandler(handler);

            Assert.That(
                published.Any(value =>
                    value.StatusCode.Code == StatusCodes.BadNodeIdUnknown),
                Is.True);
            Assert.That(
                published.Any(value =>
                    value.StatusCode.Code == StatusCodes.BadCommunicationError),
                Is.False);
        }

        [Test]
        public void QueueSizeOneDropsIncomingValuesWhileTheMarkerIsPending()
        {
            // The marker takes the single slot and is never discarded, so values sampled after
            // the deletion are dropped until it has been published.
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 1,
                discardOldest: true);

            handler.QueueValue(
                new DataValue(new Variant(7), StatusCodes.Good),
                ServiceResult.Good);
            handler.QueueRequiredValue(
                new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));
            handler.QueueValue(
                new DataValue(new Variant(42), StatusCodes.Good),
                ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);

            // Once the marker has been published the queue accepts values again.
            handler.QueueValue(
                new DataValue(new Variant(42), StatusCodes.Good),
                ServiceResult.Good);
            List<DataValue> afterPublication = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                Assert.That(published, Has.Count.EqualTo(1));
                Assert.That(published[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(afterPublication, Has.Count.EqualTo(1));
                Assert.That(afterPublication[0].WrappedValue, Is.EqualTo(new Variant(42)));
            });
        }

        [Test]
        public void ResizingAQueueKeepsMarkersProtectedAndOrdinaryValuesDiscardable()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 2,
                discardOldest: true);

            handler.QueueRequiredValue(
                new DataValue(Variant.Null, StatusCodes.BadNodeIdUnknown),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));
            handler.QueueValue(new DataValue(new Variant(1), StatusCodes.Good), ServiceResult.Good);
            handler.QueueValue(new DataValue(new Variant(2), StatusCodes.Good), ServiceResult.Good);

            handler.SetQueueSize(2, true, DiagnosticsMasks.None);
            handler.QueueValue(new DataValue(new Variant(3), StatusCodes.Good), ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                Assert.That(handler.HasRequiredValues, Is.False);
                Assert.That(published, Has.Count.EqualTo(2));
                Assert.That(published[0].StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(published[0].StatusCode.Overflow, Is.True);
                Assert.That(published[1].WrappedValue, Is.EqualTo(new Variant(1)));
            });
        }

        [Test]
        public void OrdinaryValueReplacesBadNodeIdUnknownAtQueueSizeOne()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            using DataChangeQueueHandler handler = CreateQueueHandler(
                telemetry,
                queueFactory,
                queueSize: 1,
                discardOldest: true);

            handler.QueueValue(
                CreateBadNodeIdUnknownValue(),
                new ServiceResult(StatusCodes.BadNodeIdUnknown));
            bool markerAfterBad = handler.HasRequiredValues;
            handler.QueueValue(
                new DataValue(new Variant(42), StatusCodes.Good),
                ServiceResult.Good);

            List<DataValue> published = DrainHandler(handler);

            Assert.Multiple(() =>
            {
                Assert.That(
                    markerAfterBad,
                    Is.False,
                    "an ordinary bad sample must not become a protected marker");
                Assert.That(published, Has.Count.EqualTo(1));
                Assert.That(published[0].WrappedValue, Is.EqualTo(new Variant(42)));
            });
        }
        private static DataChangeQueueHandler CreateQueueHandler(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queueFactory,
            uint queueSize,
            bool discardOldest)
        {
            IDataChangeMonitoredItemQueue queue = queueFactory.CreateDataChangeQueue(false, 1);
            queue.ResetQueue(queueSize, false);
            return new DataChangeQueueHandler(
                queue,
                discardOldest,
                samplingInterval: 0,
                telemetry,
                discardedValueHandler: null);
        }

        private static DataValue CreateBadNodeIdUnknownValue()
        {
            DateTime utcNow = DateTime.UtcNow;
            return new DataValue(
                Variant.Null,
                StatusCodes.BadNodeIdUnknown,
                utcNow,
                utcNow);
        }

        private static List<DataValue> DrainHandler(DataChangeQueueHandler handler)
        {
            var values = new List<DataValue>();
            while (handler.PublishSingleValue(out DataValue value, out ServiceResult _))
            {
                values.Add(value);
            }
            return values;
        }
        private static MonitoredItem CreateMonitoredItem(
            ITelemetryContext telemetry,
            uint queueSize = 1,
            bool discardOldest = true,
            MonitoringMode monitoringMode = MonitoringMode.Reporting,
            IAsyncNodeManager nodeManager = null,
            object managerHandle = null,
            MonitoringFilter filter = null,
            double samplingInterval = 1000)
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
                samplingInterval,
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
