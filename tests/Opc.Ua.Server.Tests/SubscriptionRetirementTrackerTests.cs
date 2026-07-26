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
using System.Reflection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Covers the monitored-item retirement and lifecycle seams a
    /// <see cref="Subscription"/> exposes to the NodeManager lifecycle when a
    /// generation is retired immediately.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Category("NodeManagerLifecycle")]
    [Parallelizable]
    public class SubscriptionRetirementTrackerTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_sessionMock = new Mock<ISession>();
            m_diagnosticsNodeManagerMock = new Mock<IDiagnosticsNodeManager>();
            m_nodeManagerMock = new Mock<IMasterNodeManager>();
            m_queueFactoryMock = new Mock<IMonitoredItemQueueFactory>();

            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(m_diagnosticsNodeManagerMock.Object);
            m_serverMock.Setup(s => s.NodeManager).Returns(m_nodeManagerMock.Object);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactoryMock.Object);
            m_serverMock.Setup(s => s.DiagnosticsWriteLock).Returns(new object());
            m_serverMock.Setup(s => s.ServerDiagnostics).Returns(new ServerDiagnosticsSummaryDataType());

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            m_sessionMock.Setup(s => s.DiagnosticsLock).Returns(new object());
            m_sessionMock.Setup(s => s.SessionDiagnostics).Returns(new SessionDiagnosticsDataType());

            m_diagnosticsNodeManagerMock
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));
        }

        [Test]
        public void CanRetireMonitoredItemsThrowsForNullNodeManager()
        {
            using Subscription subscription = CreateSubscription();
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => tracker.CanRetireMonitoredItems(null!));

            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RetireMonitoredItemsThrowsForNullNodeManager()
        {
            using Subscription subscription = CreateSubscription();
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => tracker.RetireMonitoredItems(null!, StatusCodes.BadNodeIdUnknown));

            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void RetireMonitoredItemsThrowsForNullError()
        {
            using Subscription subscription = CreateSubscription();
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => tracker.RetireMonitoredItems(CreateNodeManager(), null!));

            Assert.That(exception.ParamName, Is.EqualTo("error"));
        }

        [Test]
        public void DetachRetiredMonitoredItemsThrowsForNullNodeManager()
        {
            using Subscription subscription = CreateSubscription();
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => tracker.DetachRetiredMonitoredItems(null!));

            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        public void CanRetireMonitoredItemsIsTrueForAnEmptySubscription()
        {
            using Subscription subscription = CreateSubscription();
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(CreateNodeManager()), Is.True);
        }

        [Test]
        public void CanRetireMonitoredItemsIsFalseForADurableSubscription()
        {
            m_queueFactoryMock.Setup(f => f.SupportsDurableQueues).Returns(true);
            using Subscription subscription = CreateSubscription();
            Assert.That(
                ServiceResult.IsGood(subscription.SetSubscriptionDurable(1000)),
                Is.True);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(CreateNodeManager()), Is.False);
        }

        [Test]
        public void RetireMonitoredItemsRejectsADurableSubscription()
        {
            m_queueFactoryMock.Setup(f => f.SupportsDurableQueues).Returns(true);
            using Subscription subscription = CreateSubscription();
            Assert.That(
                ServiceResult.IsGood(subscription.SetSubscriptionDurable(1000)),
                Is.True);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(
                () => tracker.RetireMonitoredItems(
                    CreateNodeManager(),
                    StatusCodes.BadNodeIdUnknown),
                Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void CanRetireMonitoredItemsIsTrueWhenEveryOwnedItemIsRetirable()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            AddMonitoredItem(subscription, new RetirableMonitoredItemStub(1, nodeManager));
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(nodeManager), Is.True);
        }

        [Test]
        public void CanRetireMonitoredItemsIsFalseWhenAnOwnedItemIsNotRetirable()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            AddMonitoredItem(subscription, new PlainMonitoredItemStub(2, nodeManager));
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(nodeManager), Is.False);
        }

        [Test]
        public void CanRetireMonitoredItemsIsFalseWhenAnOwnedItemIsDurable()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            AddMonitoredItem(
                subscription,
                new RetirableMonitoredItemStub(3, nodeManager) { IsDurable = true });
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(nodeManager), Is.False);
        }

        [Test]
        public void CanRetireMonitoredItemsIgnoresItemsOwnedByAnotherNodeManager()
        {
            IAsyncNodeManager owner = CreateNodeManager();
            IAsyncNodeManager other = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            AddMonitoredItem(subscription, new PlainMonitoredItemStub(4, other));
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(tracker.CanRetireMonitoredItems(owner), Is.True);
        }

        [Test]
        public void RetireMonitoredItemsRetiresEveryOwnedItem()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            var item = new RetirableMonitoredItemStub(5, nodeManager);
            AddMonitoredItem(subscription, item);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;
            var error = new ServiceResult(StatusCodes.BadNodeIdUnknown);

            tracker.RetireMonitoredItems(nodeManager, error);

            Assert.That(item.RetireCount, Is.EqualTo(1));
            Assert.That(item.IsRetired, Is.True);
            Assert.That(item.RetirementError, Is.SameAs(error));
        }

        [Test]
        public void RetireMonitoredItemsLeavesItemsOfAnotherNodeManagerUntouched()
        {
            IAsyncNodeManager owner = CreateNodeManager();
            IAsyncNodeManager other = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            var foreign = new RetirableMonitoredItemStub(6, other);
            AddMonitoredItem(subscription, foreign);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            tracker.RetireMonitoredItems(owner, StatusCodes.BadNodeIdUnknown);

            Assert.That(foreign.RetireCount, Is.Zero);
        }

        [Test]
        public void RetireMonitoredItemsRejectsANonRetirableOwnedItem()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            AddMonitoredItem(subscription, new PlainMonitoredItemStub(7, nodeManager));
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(
                () => tracker.RetireMonitoredItems(
                    nodeManager,
                    StatusCodes.BadNodeIdUnknown),
                Throws.InstanceOf<NotSupportedException>());
        }

        [Test]
        public void RetireMonitoredItemsRejectsADurableOwnedItem()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            var durable = new RetirableMonitoredItemStub(12, nodeManager) { IsDurable = true };
            AddMonitoredItem(subscription, durable);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            Assert.That(
                () => tracker.RetireMonitoredItems(
                    nodeManager,
                    StatusCodes.BadNodeIdUnknown),
                Throws.InstanceOf<NotSupportedException>());
            Assert.That(durable.RetireCount, Is.Zero);
        }

        [Test]
        public void DetachRetiredMonitoredItemsDetachesOnlyRetiredOwnedItems()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();

            var retired = new RetirableMonitoredItemStub(8, nodeManager);
            retired.Retire(StatusCodes.BadNodeIdUnknown);
            var live = new RetirableMonitoredItemStub(9, nodeManager);
            AddMonitoredItem(subscription, retired);
            AddMonitoredItem(subscription, live);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            tracker.DetachRetiredMonitoredItems(nodeManager);

            Assert.That(retired.DetachCount, Is.EqualTo(1));
            Assert.That(live.DetachCount, Is.Zero);
        }

        [Test]
        public void DetachRetiredMonitoredItemsIgnoresRetiredItemsOfAnotherNodeManager()
        {
            IAsyncNodeManager owner = CreateNodeManager();
            IAsyncNodeManager other = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            var foreign = new RetirableMonitoredItemStub(10, other);
            foreign.Retire(StatusCodes.BadNodeIdUnknown);
            AddMonitoredItem(subscription, foreign);
            INodeManagerMonitoredItemRetirementTracker tracker = subscription;

            tracker.DetachRetiredMonitoredItems(owner);

            Assert.That(foreign.DetachCount, Is.Zero);
        }

        [Test]
        public void ContainsMonitoredItemDistinguishesRegisteredInstances()
        {
            IAsyncNodeManager nodeManager = CreateNodeManager();
            using Subscription subscription = CreateSubscription();
            var registered = new RetirableMonitoredItemStub(11, nodeManager);
            AddMonitoredItem(subscription, registered);
            ISubscriptionMonitoredItemLifecycle lifecycle = subscription;

            Assert.That(lifecycle.ContainsMonitoredItem(registered), Is.True);

            // Same id, different instance: the subscription must not claim it.
            var impostor = new RetirableMonitoredItemStub(11, nodeManager);
            Assert.That(lifecycle.ContainsMonitoredItem(impostor), Is.False);
        }

        private static IAsyncNodeManager CreateNodeManager()
        {
            return new Mock<IAsyncNodeManager>().Object;
        }

        private Subscription CreateSubscription()
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 1000,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: 0,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10);
        }

        private static void AddMonitoredItem(Subscription subscription, IMonitoredItem monitoredItem)
        {
            var items = GetPrivateField<Dictionary<uint, LinkedListNode<IMonitoredItem>>>(
                subscription,
                "m_monitoredItems");
            var checkList = GetPrivateField<LinkedList<IMonitoredItem>>(
                subscription,
                "m_itemsToCheck");
            LinkedListNode<IMonitoredItem> node = checkList.AddLast(monitoredItem);
            items[monitoredItem.Id] = node;
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field {fieldName} not found.");
            return (T)(field.GetValue(instance)
                ?? throw new InvalidOperationException($"Field {fieldName} is null."));
        }

        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<IDiagnosticsNodeManager> m_diagnosticsNodeManagerMock;
        private Mock<IMasterNodeManager> m_nodeManagerMock;
        private Mock<IMonitoredItemQueueFactory> m_queueFactoryMock;
        private ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// A minimal monitored item that does not support immediate retirement.
    /// Hand written because <c>IRetirableMonitoredItem</c> is internal and
    /// therefore cannot be proxied by Moq.
    /// </summary>
    internal class PlainMonitoredItemStub : IMonitoredItem
    {
        public PlainMonitoredItemStub(uint id, IAsyncNodeManager nodeManager)
        {
            Id = id;
            NodeManager = nodeManager;
        }
        public IAsyncNodeManager NodeManager { get; }

        public ISession Session => null;

        public IUserIdentity EffectiveIdentity => null;

        public uint Id { get; }

        public uint SubscriptionId => 1;

        public bool IsDurable { get; set; }

        public uint ClientHandle => Id;

        public ISubscription SubscriptionCallback { get; set; }

        public object ManagerHandle => null;

        public int MonitoredItemType => 1;

        public bool IsReadyToPublish => false;

        public bool IsReadyToTrigger { get; set; }

        public bool IsResendData => false;

        public NodeId NodeId => new(Id);

        public MonitoringMode MonitoringMode => MonitoringMode.Reporting;

        public double SamplingInterval => 1000;

        public void SetupResendDataTrigger()
        {
        }

        public ServiceResult GetCreateResult(out MonitoredItemCreateResult result)
        {
            result = new MonitoredItemCreateResult();
            return ServiceResult.Good;
        }

        public ServiceResult GetModifyResult(out MonitoredItemModifyResult result)
        {
            result = new MonitoredItemModifyResult();
            return ServiceResult.Good;
        }

        public IStoredMonitoredItem ToStorableMonitoredItem()
        {
            return null;
        }

        public MonitoringMode SetMonitoringMode(MonitoringMode monitoringMode)
        {
            return MonitoringMode.Reporting;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// A monitored item that supports immediate retirement and records how it
    /// was driven by the subscription.
    /// </summary>
    internal sealed class RetirableMonitoredItemStub : PlainMonitoredItemStub, IRetirableMonitoredItem
    {
        public RetirableMonitoredItemStub(uint id, IAsyncNodeManager nodeManager)
            : base(id, nodeManager)
        {
        }

        public bool IsRetired { get; private set; }

        public ServiceResult RetirementError { get; private set; }

        public int RetireCount { get; private set; }

        public int DetachCount { get; private set; }

        public void Retire(ServiceResult error)
        {
            IsRetired = true;
            RetirementError = error;
            RetireCount++;
        }

        public void DetachOwner()
        {
            DetachCount++;
        }
    }
}
