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

// CA2000: test code; the created queues/monitored items are short-lived and disposed by the runtime.
#pragma warning disable CA2000
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that the monitored-item restore path consumes a pre-hydrated queue supplied by an asynchronous
    /// <see cref="ISubscriptionStore"/> and otherwise falls back to the synchronous restore.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemQueueRestoreTests
    {
        [Test]
        public void RestoreUsesPreHydratedDataChangeQueueAndSkipsSyncFallback()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var storeMock = new Mock<ISubscriptionStore>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory, storeMock.Object);

            IDataChangeMonitoredItemQueue preHydrated = queueFactory.CreateDataChangeQueue(false, 2);
            preHydrated.ResetQueue(10, false);
            preHydrated.Enqueue(new DataValue(new Variant(7)), ServiceResult.Good);

            StoredMonitoredItem stored = CreateStoredItem();
            stored.RestoredDataChangeQueue = preHydrated;

            using var item = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);

            Assert.That(item.Id, Is.EqualTo(stored.Id));
            storeMock.Verify(
                s => s.RestoreDataChangeMonitoredItemQueue(It.IsAny<uint>()),
                Times.Never);
        }

        [Test]
        public void RestoreFallsBackToSyncRestoreWhenNotPreHydrated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var storeMock = new Mock<ISubscriptionStore>();
            storeMock
                .Setup(s => s.RestoreDataChangeMonitoredItemQueue(It.IsAny<uint>()))
                .Returns((IDataChangeMonitoredItemQueue)null);
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory, storeMock.Object);

            StoredMonitoredItem stored = CreateStoredItem();

            using var item = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);

            storeMock.Verify(
                s => s.RestoreDataChangeMonitoredItemQueue(stored.Id),
                Times.Once);
        }

        private static StoredMonitoredItem CreateStoredItem()
        {
            return new StoredMonitoredItem
            {
                Id = 2,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = 10,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0))
            };
        }

        private static Mock<IServerInternal> CreateServerMock(
            ITelemetryContext telemetry,
            IMonitoredItemQueueFactory queueFactory,
            ISubscriptionStore subscriptionStore)
        {
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            serverMock.Setup(s => s.SubscriptionStore).Returns(subscriptionStore);
            return serverMock;
        }
    }
}
