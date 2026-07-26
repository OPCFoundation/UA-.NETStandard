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
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Covers the immediate-retirement contract a <see cref="MonitoredItem"/>
    /// implements for the NodeManager lifecycle: a retired item stops accepting
    /// values and events, reports a terminal status, and only then releases its
    /// owner references.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable]
    public class MonitoredItemRetirementTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void RetireRejectsANullError()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => retirable.Retire(null!));

            Assert.That(exception.ParamName, Is.EqualTo("error"));
        }

        [Test]
        public void RetireMarksTheItemRetiredAndRecordsTheError()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            Assert.That(retirable.IsRetired, Is.False);
            Assert.That(retirable.RetirementError, Is.Null);

            retirable.Retire(error);

            Assert.That(retirable.IsRetired, Is.True);
            Assert.That(retirable.RetirementError, Is.SameAs(error));
        }

        [Test]
        public void RetireIsIdempotentAndKeepsTheFirstError()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;
            var first = new ServiceResult(StatusCodes.BadNodeIdUnknown);
            var second = new ServiceResult(StatusCodes.BadOutOfService);

            retirable.Retire(first);
            retirable.Retire(second);

            Assert.That(retirable.RetirementError, Is.SameAs(first));
        }

        [Test]
        public void RetireQueuesTheTerminalStatusOnADataChangeItem()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;

            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            Assert.That(item.IsReadyToPublish, Is.True);
        }

        [Test]
        public void RetireNotifiesTheSubscriptionThatTheItemIsReadyToPublish()
        {
            using MonitoredItem item = CreateDataChangeItem();
            var subscription = new Mock<ISubscription>();
            item.SubscriptionCallback = subscription.Object;
            IRetirableMonitoredItem retirable = item;

            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            subscription.Verify(s => s.ItemReadyToPublish(item), Times.Once);
        }

        [Test]
        public void RetireLeavesAnEventItemWithNothingToPublish()
        {
            using MonitoredItem item = CreateEventItem();
            IRetirableMonitoredItem retirable = item;

            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            Assert.That(retirable.IsRetired, Is.True);
            Assert.That(item.IsReadyToPublish, Is.False);
            Assert.That(item.IsReadyToTrigger, Is.False);
        }

        [Test]
        public void QueueValueIsIgnoredAfterRetirement()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;
            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            Assert.That(
                () => item.QueueValue(new DataValue(new Variant(42)), null, true),
                Throws.Nothing,
                "a retired item must silently drop late values instead of throwing");
        }

        [Test]
        public void QueueEventFieldsIsIgnoredAfterRetirement()
        {
            using MonitoredItem item = CreateEventItem();
            IRetirableMonitoredItem retirable = item;
            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            Assert.That(
                () => item.QueueEvent(new EventFieldList()),
                Throws.Nothing,
                "a retired item must silently drop late events instead of throwing");
            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void QueueEventInstanceIsIgnoredAfterRetirement()
        {
            using MonitoredItem item = CreateEventItem();
            IRetirableMonitoredItem retirable = item;
            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            Assert.That(
                () => item.QueueEvent(new Mock<IFilterTarget>().Object, true),
                Throws.Nothing);
            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void QueueEventRejectsANullInstance()
        {
            using MonitoredItem item = CreateEventItem();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => item.QueueEvent(null!, false));

            Assert.That(exception.ParamName, Is.EqualTo("instance"));
        }

        [Test]
        public void DetachOwnerRequiresRetirementFirst()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;

            Assert.That(
                () => retirable.DetachOwner(),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void DetachOwnerReleasesTheNodeManagerReferencesAfterRetirement()
        {
            var nodeManager = new Mock<IAsyncNodeManager>().Object;
            using MonitoredItem item = CreateDataChangeItem(nodeManager, new object());
            IRetirableMonitoredItem retirable = item;

            Assert.That(item.NodeManager, Is.SameAs(nodeManager));
            Assert.That(item.ManagerHandle, Is.Not.Null);

            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));
            retirable.DetachOwner();

            Assert.That(item.NodeManager, Is.Null);
            Assert.That(item.ManagerHandle, Is.Null);
        }

        [Test]
        public void DetachOwnerIsIdempotentAfterRetirement()
        {
            using MonitoredItem item = CreateDataChangeItem();
            IRetirableMonitoredItem retirable = item;
            retirable.Retire(new ServiceResult(StatusCodes.BadNodeIdUnknown));

            retirable.DetachOwner();

            Assert.That(() => retirable.DetachOwner(), Throws.Nothing);
        }

        private MonitoredItem CreateDataChangeItem(
            IAsyncNodeManager nodeManager = null,
            object managerHandle = null)
        {
            return CreateItem(
                new ReadValueId
                {
                    NodeId = new NodeId("Retirement", 2),
                    AttributeId = Attributes.Value
                },
                filter: null,
                nodeManager,
                managerHandle);
        }

        private MonitoredItem CreateEventItem()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                new NodeId(ObjectTypes.BaseEventType),
                new QualifiedName(BrowseNames.EventId));
            return CreateItem(
                new ReadValueId
                {
                    NodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier
                },
                filter,
                nodeManager: null,
                managerHandle: null);
        }

        private MonitoredItem CreateItem(
            ReadValueId itemToMonitor,
            MonitoringFilter filter,
            IAsyncNodeManager nodeManager,
            object managerHandle)
        {
            using var queueFactory = new MonitoredItemQueueFactory(m_telemetry);
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(m_telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            server.Setup(s => s.SubscriptionStore).Returns(Mock.Of<ISubscriptionStore>());

            return new MonitoredItem(
                server.Object,
                nodeManager ?? new Mock<IAsyncNodeManager>().Object,
                managerHandle!,
                subscriptionId: 1,
                id: 2,
                itemToMonitor: itemToMonitor,
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: filter,
                filterToUse: filter,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
        }

        private ITelemetryContext m_telemetry;
    }
}
