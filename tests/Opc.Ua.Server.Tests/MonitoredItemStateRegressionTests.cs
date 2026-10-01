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

// CA2000: test code; the monitored items and queue factories are disposed by the tests.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the state a <see cref="MonitoredItem"/> keeps between queueing,
    /// monitoring mode changes, modification and publishing.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemStateRegressionTests
    {
        /// <summary>
        /// Clearing the sampling error with null must leave a Good result, not a null one.
        /// </summary>
        [Test]
        public void SetSamplingErrorNullResetsToGood()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1);

            item.SetSamplingError(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            item.SetSamplingError(null);

            ServiceResult createResult = item.GetCreateResult(out MonitoredItemCreateResult created);
            ServiceResult modifyResult = item.GetModifyResult(out MonitoredItemModifyResult modified);

            Assert.That(createResult, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(createResult), Is.True);
            Assert.That(created.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(modifyResult, Is.Not.Null);
            Assert.That(ServiceResult.IsGood(modifyResult), Is.True);
            Assert.That(modified.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Growing the queue from one slot must not re-queue a value that was already published.
        /// </summary>
        [Test]
        public void GrowingQueueDoesNotRequeuePublishedValue()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            ModifyQueueSize(item, 10);
            Assert.That(item.ItemsInQueue, Is.Zero);
            Assert.That(item.IsReadyToPublish, Is.False);

            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(published[0].Value.WrappedValue, Is.EqualTo(Variant.From(2)));
        }

        /// <summary>
        /// Growing the queue from one slot keeps a value that has not been published yet.
        /// </summary>
        [Test]
        public void GrowingQueueKeepsUnpublishedValue()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            ModifyQueueSize(item, 10);
            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(published[0].Value.WrappedValue, Is.EqualTo(Variant.From(1)));
            Assert.That(published[1].Value.WrappedValue, Is.EqualTo(Variant.From(2)));
        }

        /// <summary>
        /// The first value queued after a triggering item leaves DISABLED must trigger its
        /// linked items, while the value queued at creation still does not.
        /// </summary>
        [Test]
        public void FirstValueAfterReenableIsReadyToTrigger()
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(item.IsReadyToTrigger, Is.False, "creation value must not trigger");
            _ = PublishData(harness, item);

            item.SetMonitoringMode(MonitoringMode.Disabled);
            item.SetMonitoringMode(MonitoringMode.Reporting);
            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);

            Assert.That(item.IsReadyToTrigger, Is.True);
        }

        /// <summary>
        /// A resend requested before the item leaves REPORTING must not publish the last value.
        /// </summary>
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        public void ResendDataIsDroppedWhenItemStopsReporting(MonitoringMode monitoringMode)
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize: 1, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            item.SetupResendDataTrigger();
            Assert.That(item.IsResendData, Is.True);

            item.SetMonitoringMode(monitoringMode);

            Assert.That(item.IsResendData, Is.False);
            Assert.That(PublishData(harness, item), Is.Empty);
        }

        /// <summary>
        /// A node deleted while the item is disabled must be reported as Bad_NodeIdUnknown
        /// once the item samples again, never as an empty Good value.
        /// </summary>
        [TestCase(1u, MonitoringMode.Sampling)]
        [TestCase(10u, MonitoringMode.Sampling)]
        [TestCase(1u, MonitoringMode.Reporting)]
        [TestCase(10u, MonitoringMode.Reporting)]
        public void NodeDeletedWhileDisabledReportsNodeIdUnknownAfterReenable(
            uint queueSize,
            MonitoringMode monitoringMode)
        {
            using var harness = new Harness();
            using MonitoredItem item = harness.CreateDataItem(queueSize, samplingInterval: 0);

            item.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            Assert.That(PublishData(harness, item), Has.Count.EqualTo(1));

            item.SetMonitoringMode(MonitoringMode.Disabled);
            ((IDetachableMonitoredItem)item).MarkNodeDeleted();
            Assert.That(item.ItemsInQueue, Is.Zero);
            Assert.That(((IDetachableMonitoredItem)item).IsDeleted, Is.True);

            item.SetMonitoringMode(monitoringMode);
            if (monitoringMode == MonitoringMode.Sampling)
            {
                Assert.That(item.SetTriggered(), Is.True);
            }

            List<MonitoredItemNotification> published = PublishData(harness, item);

            Assert.That(published, Has.Count.EqualTo(1));
            Assert.That(
                published[0].Value.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        private static void ModifyQueueSize(MonitoredItem item, uint queueSize)
        {
            ServiceResult result = item.ModifyAttributes(
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                item.ClientHandle,
                item.Filter,
                item.Filter,
                null,
                item.SamplingInterval,
                queueSize,
                discardOldest: true);
            Assert.That(ServiceResult.IsBad(result), Is.False);
        }

        private static List<MonitoredItemNotification> PublishData(
            Harness harness,
            MonitoredItem item)
        {
            var notifications = new Queue<MonitoredItemNotification>();
            _ = item.Publish(
                new OperationContext(item),
                notifications,
                new Queue<DiagnosticInfo>(),
                100,
                harness.Logger);
            return [.. notifications];
        }

        private static List<EventFieldList> PublishEvents(MonitoredItem item)
        {
            var notifications = new Queue<EventFieldList>();
            _ = item.Publish(new OperationContext(item), notifications, 100);
            return [.. notifications];
        }

        private sealed class Harness : IDisposable
        {
            public Harness()
            {
                Telemetry = NUnitTelemetryContext.Create();
                Logger = Telemetry.CreateLogger<MonitoredItemStateRegressionTests>();
                QueueFactory = new MonitoredItemQueueFactory(Telemetry);
                StoreMock = new Mock<ISubscriptionStore>();
                ServerMock = new Mock<IServerInternal>();
                ServerMock.Setup(s => s.Telemetry).Returns(Telemetry);
                ServerMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                ServerMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                ServerMock.Setup(s => s.MonitoredItemQueueFactory).Returns(QueueFactory);
                ServerMock.Setup(s => s.SubscriptionStore).Returns(StoreMock.Object);
                TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            }

            public ITelemetryContext Telemetry { get; }

            public ILogger Logger { get; }

            public MonitoredItemQueueFactory QueueFactory { get; }

            public Mock<ISubscriptionStore> StoreMock { get; }

            public Mock<IServerInternal> ServerMock { get; }

            public FakeTimeProvider TimeProvider { get; }

            public MonitoredItem CreateDataItem(
                uint queueSize,
                MonitoringMode monitoringMode = MonitoringMode.Reporting,
                double samplingInterval = 1000)
            {
                var filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValue
                };
                return new MonitoredItem(
                    ServerMock.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null,
                    1,
                    2,
                    new ReadValueId
                    {
                        NodeId = new NodeId("V", 1),
                        AttributeId = Attributes.Value
                    },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    monitoringMode,
                    3,
                    filter,
                    filter,
                    null,
                    samplingInterval,
                    queueSize,
                    discardOldest: true,
                    sourceSamplingInterval: 0,
                    createDurable: false,
                    TimeProvider);
            }

            public MonitoredItem CreateEventItem(
                EventFilter filter,
                double samplingInterval = 0,
                uint queueSize = 10)
            {
                return new MonitoredItem(
                    ServerMock.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null,
                    1,
                    4,
                    new ReadValueId
                    {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    MonitoringMode.Reporting,
                    5,
                    filter,
                    filter,
                    null,
                    samplingInterval,
                    queueSize,
                    discardOldest: true,
                    sourceSamplingInterval: 0,
                    createDurable: false,
                    TimeProvider);
            }

            public void Dispose()
            {
                QueueFactory.Dispose();
            }
        }
    }
}
