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

using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that every detach state transition of a <see cref="MonitoredItem"/> keeps
    /// <see cref="MonitoredItem.HasRecoverableItems"/> exact. Recovery of detached items is
    /// skipped while no item is counted, so a missed increment would suppress reattachment
    /// and a missed decrement would bring back the scans of every monitored item.
    /// </summary>
    /// <remarks>
    /// The count is process-wide and other tests run in parallel, so the tests assert the
    /// per-item flag, and only that the count is positive while an item is counted.
    /// </remarks>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class MonitoredItemRecoverableCountTests
    {
        [Test]
        public void NewItemIsNotCounted()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));

            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [Test]
        public void DeletedItemIsCountedUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            using MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.True);
            Assert.That(MonitoredItem.HasRecoverableItems, Is.True);

            // a second transition into the same state does not count the item twice.
            lifecycle.QueueNodeIdUnknown();
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [Test]
        public void DetachedItemIsCountedUntilRebound()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            Mock<IServerInternal> server = CreateServer(telemetry, queueFactory);
            server.Setup(s => s.CoreNodeManager).Returns(new Mock<ICoreNodeManager>().Object);
            using MonitoredItem item = CreateItem(server);
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.BeginDetach();
            Assert.That(item.IsCountedAsRecoverable, Is.True);
            Assert.That(MonitoredItem.HasRecoverableItems, Is.True);

            lifecycle.Detach(server.Object);
            Assert.That(lifecycle.IsDetached, Is.True);
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            lifecycle.Rebind(new Mock<IAsyncNodeManager>().Object, new object());
            Assert.That(lifecycle.IsDetached, Is.False);
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [Test]
        public void DisposedItemIsNotCounted()
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            MonitoredItem item = CreateItem(CreateServer(telemetry, queueFactory));
            var lifecycle = (IDetachableMonitoredItem)item;

            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.True);

            item.Dispose();
            Assert.That(item.IsCountedAsRecoverable, Is.False);

            // a disposed item cannot be attached again, so later transitions do not count it.
            lifecycle.MarkNodeDeleted();
            Assert.That(item.IsCountedAsRecoverable, Is.False);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void RestoredItemIsCountedWhenStoredDetachedOrDeleted(bool isDetached, bool isDeleted)
        {
            using MonitoredItemQueueFactory queueFactory = CreateQueueFactory(out ITelemetryContext telemetry);
            var stored = new StoredMonitoredItem
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
                LastValue = new DataValue(new Variant(0)),
                IsDetached = isDetached,
                IsDeleted = isDeleted
            };

            using var item = new MonitoredItem(
                CreateServer(telemetry, queueFactory).Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored,
                null);

            Assert.That(item.IsCountedAsRecoverable, Is.EqualTo(isDetached || isDeleted));
        }

        private static MonitoredItemQueueFactory CreateQueueFactory(out ITelemetryContext telemetry)
        {
            telemetry = NUnitTelemetryContext.Create();
            return new MonitoredItemQueueFactory(telemetry);
        }

        private static Mock<IServerInternal> CreateServer(
            ITelemetryContext telemetry,
            MonitoredItemQueueFactory queueFactory)
        {
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            server.Setup(s => s.SubscriptionStore).Returns(new Mock<ISubscriptionStore>().Object);
            server.Setup(s => s.DiagnosticsNodeManager).Returns(new Mock<IDiagnosticsNodeManager>().Object);
            return server;
        }

        private static MonitoredItem CreateItem(Mock<IServerInternal> server)
        {
            return new MonitoredItem(
                server.Object,
                new Mock<IAsyncNodeManager>().Object,
                null!,
                1,
                2,
                new ReadValueId { NodeId = new NodeId(1), AttributeId = Attributes.Value },
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                null,
                new MonitoringFilter(),
                null,
                1000,
                10,
                true,
                1000);
        }
    }
}
