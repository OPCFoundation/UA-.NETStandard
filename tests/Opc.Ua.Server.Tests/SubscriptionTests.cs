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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public class SubscriptionTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<ISession> m_sessionMock;
        private Mock<IDiagnosticsNodeManager> m_diagnosticsNodeManagerMock;
        private Mock<IMasterNodeManager> m_nodeManagerMock;
        private Mock<IMonitoredItemQueueFactory> m_queueFactoryMock;
        private ITelemetryContext m_telemetry;

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

            // ServerSystemContext requires invoked server mock to have properties setup
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

        private Subscription CreateSubscription(
            double publishingInterval = 1000,
            uint maxNotificationsPerPublish = 0,
            TimeProvider timeProvider = null)
        {
            return new Subscription(
                m_serverMock.Object,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: publishingInterval,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: maxNotificationsPerPublish,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10,
                timeProvider: timeProvider);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void HasMonitoredItemsThrowsForNullNodeManager()
        {
            using Subscription subscription = CreateSubscription();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => subscription.HasMonitoredItems(null!));

            Assert.That(exception.ParamName, Is.EqualTo("nodeManager"));
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void CreateMonitoredItemsRejectsClosingSession()
        {
            using ServerInternalData server = CreateServerInternalData();
            ConcurrentDictionary<NodeId, int> closingSessions =
                GetPrivateField<ConcurrentDictionary<NodeId, int>>(
                    server,
                    "m_closingSessions");
            closingSessions.TryAdd(m_sessionMock.Object.Id, 1);
            using var subscription = new Subscription(
                server,
                m_sessionMock.Object,
                subscriptionId: 1,
                publishingInterval: 1000,
                maxLifetimeCount: 10,
                maxKeepAliveCount: 5,
                maxNotificationsPerPublish: 0,
                priority: 0,
                publishingEnabled: true,
                maxMessageCount: 10);
            var context = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await subscription
                        .CreateMonitoredItemsAsync(
                            context,
                            TimestampsToReturn.Both,
                            [],
                            CancellationToken.None)
                        .ConfigureAwait(false));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSessionClosed));
        }

        private ServerInternalData CreateServerInternalData()
        {
            var configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:opcfoundation.org:Tests:Subscription",
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = []
                }
            };
            var server = new ServerInternalData(
                new ServerProperties(),
                configuration,
                ServiceMessageContext.Create(m_telemetry));
            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager
                .SetupGet(manager => manager.DiagnosticsNodeManager)
                .Returns(m_diagnosticsNodeManagerMock.Object);
            masterNodeManager
                .SetupGet(manager => manager.ConfigurationNodeManager)
                .Returns((IConfigurationNodeManager)null);
            masterNodeManager
                .SetupGet(manager => manager.CoreNodeManager)
                .Returns((ICoreNodeManager)null);
            server.SetNodeManager(masterNodeManager.Object);
            server.SetMonitoredItemQueueFactory(m_queueFactoryMock.Object);
            return server;
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void ClosePublishQueueRetainsClaimedDeletingSubscription()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);
            var sessionId = new NodeId(Guid.NewGuid());
            NodeId currentSessionId = sessionId;
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Id).Returns(1);
            subscription
                .SetupGet(value => value.SessionId)
                .Returns(() => currentSessionId);
            subscription
                .Setup(value => value.SessionClosed())
                .Callback(() => currentSessionId = NodeId.Null);

            ConcurrentDictionary<uint, ISubscription> activeSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                    manager,
                    "m_subscriptions");
            ConcurrentDictionary<uint, byte> deletingSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, byte>>(
                    manager,
                    "m_deletingSubscriptions");
            ConcurrentDictionary<uint, ISubscription> abandonedSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                    manager,
                    "m_abandonedSubscriptions");
            ConcurrentDictionary<NodeId, IList<ISubscription>>
                closedSessionSubscriptions =
                    GetPrivateField<
                        ConcurrentDictionary<NodeId, IList<ISubscription>>>(
                            manager,
                            "m_closedSessionSubscriptions");
            activeSubscriptions.TryAdd(subscription.Object.Id, subscription.Object);
            deletingSubscriptions.TryAdd(subscription.Object.Id, 0);

            MethodInfo closePublishQueue = typeof(SubscriptionManager).GetMethod(
                "ClosePublishQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "ClosePublishQueue method not found.");
            object closeWork = closePublishQueue.Invoke(
                manager,
                [sessionId, true]);

            Assert.That(closeWork, Is.Not.Null);
            Assert.That(
                closedSessionSubscriptions.TryGetValue(
                    sessionId,
                    out IList<ISubscription> subscriptions),
                Is.True);
            Assert.That(subscriptions, Has.Count.EqualTo(1));
            Assert.That(subscriptions![0], Is.SameAs(subscription.Object));
            Assert.That(currentSessionId.IsNull, Is.True);
            subscription.Verify(value => value.SessionClosed(), Times.Once);

            Type claimType = typeof(SubscriptionManager).GetNestedType(
                "SubscriptionDeletionClaim",
                BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "SubscriptionDeletionClaim type not found.");
            ConstructorInfo claimConstructor = claimType.GetConstructors(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)[0];
            object claim = claimConstructor.Invoke(
                [subscription.Object, sessionId, null, false]);
            MethodInfo restoreSubscriptionDeletion =
                typeof(SubscriptionManager).GetMethod(
                    "RestoreSubscriptionDeletion",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "RestoreSubscriptionDeletion method not found.");

            Assert.That(
                restoreSubscriptionDeletion.Invoke(manager, [claim]),
                Is.True);
            Assert.That(
                abandonedSubscriptions.TryGetValue(
                    subscription.Object.Id,
                    out ISubscription restoredSubscription),
                Is.True);
            Assert.That(
                restoredSubscription,
                Is.SameAs(subscription.Object));
            Assert.That(
                deletingSubscriptions.ContainsKey(subscription.Object.Id),
                Is.False);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task HasMonitoredItemsReturnsTrueForDifferentAdaptersOverSameSynchronousManagerAsync()
        {
            var synchronousNodeManager = new Mock<INodeManager>();
            var adapterA = new AsyncNodeManagerAdapter(synchronousNodeManager.Object);
            var adapterB = new AsyncNodeManagerAdapter(synchronousNodeManager.Object);
            var differentAdapter = new AsyncNodeManagerAdapter(new Mock<INodeManager>().Object);
            using var queueFactory = new MonitoredItemQueueFactory(m_telemetry);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            using Subscription subscription = CreateSubscription();
            var itemToMonitor = new ReadValueId
            {
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value
            };
            var monitoredItem = new MonitoredItem(
                m_serverMock.Object,
                adapterA,
                new object(),
                subscription.Id,
                id: 1,
                itemToMonitor,
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: 1,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 0,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 0);
            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager
                .Setup(n => n.CreateMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<uint>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<IList<MonitoringFilterResult>>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    OperationContext,
                    uint,
                    double,
                    TimestampsToReturn,
                    ArrayOf<MonitoredItemCreateRequest>,
                    IList<ServiceResult>,
                    IList<MonitoringFilterResult>,
                    IList<IMonitoredItem>,
                    bool,
                    CancellationToken>((
                    _,
                    _,
                    _,
                    _,
                    _,
                    errors,
                    filterResults,
                    monitoredItems,
                    _,
                    _) =>
                {
                    errors[0] = ServiceResult.Good;
                    filterResults[0] = null;
                    monitoredItems[0] = monitoredItem;
                })
                .Returns(default(ValueTask));
            m_serverMock.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);

            var request = new MonitoredItemCreateRequest
            {
                ItemToMonitor = itemToMonitor,
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 0,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };
            var context = new OperationContext(m_sessionMock.Object, DiagnosticsMasks.None);

            CreateMonitoredItemsResponse response = await subscription.CreateMonitoredItemsAsync(
                context,
                TimestampsToReturn.Both,
                [request],
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(adapterA, Is.Not.SameAs(adapterB));
            Assert.That(adapterA.SyncNodeManager, Is.SameAs(synchronousNodeManager.Object));
            Assert.That(adapterB.SyncNodeManager, Is.SameAs(synchronousNodeManager.Object));
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(subscription.HasMonitoredItems(adapterA), Is.True);
            Assert.That(subscription.HasMonitoredItems(adapterB), Is.True);
            Assert.That(subscription.HasMonitoredItems(differentAdapter), Is.False);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task CreateMonitoredItemsWhileDeletionInProgressRejectsRequestAsync()
        {
            var deletionStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDeletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var nodeManager = new Mock<IMasterNodeManager>();
            m_serverMock.Setup(server => server.NodeManager).Returns(nodeManager.Object);

            var serverDiagnostics = new ServerDiagnosticsSummaryDataType();
            var sessionDiagnostics = new SessionDiagnosticsDataType();
            m_serverMock
                .Setup(server => server.DiagnosticsWriteLock)
                .Returns(new object());
            m_serverMock
                .Setup(server => server.ServerDiagnostics)
                .Returns(serverDiagnostics);
            m_sessionMock
                .Setup(session => session.DiagnosticsLock)
                .Returns(new object());
            m_sessionMock
                .Setup(session => session.SessionDiagnostics)
                .Returns(sessionDiagnostics);
            m_diagnosticsNodeManagerMock
                .Setup(diagnostics => diagnostics.DeleteSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    deletionStarted.TrySetResult(true);
                    return new ValueTask(releaseDeletion.Task);
                });

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinPublishingInterval = 10,
                    MaxPublishingInterval = 3600000,
                    PublishingResolution = 10,
                    MinSubscriptionLifetime = 1000,
                    MaxSubscriptionLifetime = 3600000,
                    MaxMessageQueueSize = 10,
                    MaxNotificationsPerPublish = 1000,
                    MaxPublishRequestCount = 10,
                    MaxSubscriptionCount = 10
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);
            m_serverMock
                .Setup(server => server.SubscriptionManager)
                .Returns(manager);
            var context = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);
            CreateSubscriptionResponse createSubscription = await manager
                .CreateSubscriptionAsync(
                    context,
                    requestedPublishingInterval: 100,
                    requestedLifetimeCount: 100,
                    requestedMaxKeepAliveCount: 10,
                    maxNotificationsPerPublish: 0,
                    publishingEnabled: true,
                    priority: 0,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            using var subscription =
                (Subscription)manager.GetSubscriptions()[0];
            ArrayOf<MonitoredItemCreateRequest> requests =
            [
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = new NodeId(1000),
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 100,
                        QueueSize = 1,
                        DiscardOldest = true
                    }
                }
            ];

            Task<StatusCode> deleteTask = manager
                .DeleteSubscriptionAsync(
                    context,
                    createSubscription.SubscriptionId,
                    CancellationToken.None)
                .AsTask();
            Task deletionStart = await Task.WhenAny(
                deletionStarted.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            Assert.That(deletionStart, Is.SameAs(deletionStarted.Task));
            await deletionStarted.Task.ConfigureAwait(false);

            try
            {
                ServiceResultException exception =
                    Assert.ThrowsAsync<ServiceResultException>(
                        async () => await subscription
                            .CreateMonitoredItemsAsync(
                                context,
                                TimestampsToReturn.Both,
                                requests,
                                CancellationToken.None)
                            .ConfigureAwait(false));

                Assert.That(
                    exception.StatusCode,
                    Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                Assert.That(subscription.MonitoredItemCount, Is.Zero);
                nodeManager.Verify(
                    candidate => candidate.CreateMonitoredItemsAsync(
                        It.IsAny<OperationContext>(),
                        createSubscription.SubscriptionId,
                        It.IsAny<double>(),
                        It.IsAny<TimestampsToReturn>(),
                        It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
            finally
            {
                releaseDeletion.TrySetResult(true);
            }

            StatusCode deleteResult = await deleteTask.ConfigureAwait(false);
            Assert.That(deleteResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(manager.GetSubscriptions(), Is.Empty);
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public async Task SessionClosingWithoutDeleteAbandonsThenDeleteRemovesSubscriptionAsync()
        {
            var serverDiagnostics = new ServerDiagnosticsSummaryDataType();
            var sessionDiagnostics = new SessionDiagnosticsDataType();
            var ownerIdentity = new Mock<IUserIdentity>();
            object serverDiagnosticsLock = new();
            object sessionDiagnosticsLock = new();
            m_serverMock
                .Setup(server => server.DiagnosticsWriteLock)
                .Returns(serverDiagnosticsLock);
            m_serverMock
                .Setup(server => server.ServerDiagnostics)
                .Returns(serverDiagnostics);
            m_serverMock
                .Setup(server => server.NodeManager)
                .Returns(new Mock<IMasterNodeManager>().Object);
            m_sessionMock
                .Setup(session => session.DiagnosticsLock)
                .Returns(sessionDiagnosticsLock);
            m_sessionMock
                .Setup(session => session.SessionDiagnostics)
                .Returns(sessionDiagnostics);
            m_sessionMock
                .Setup(session => session.EffectiveIdentity)
                .Returns(ownerIdentity.Object);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinPublishingInterval = 10,
                    MaxPublishingInterval = 3600000,
                    PublishingResolution = 10,
                    MinSubscriptionLifetime = 1000,
                    MaxSubscriptionLifetime = 3600000,
                    MaxMessageQueueSize = 10,
                    MaxNotificationsPerPublish = 1000,
                    MaxPublishRequestCount = 10,
                    MaxSubscriptionCount = 10
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);
            int createdEvents = 0;
            int deletedEvents = 0;
            manager.SubscriptionCreated += (_, deleted) =>
            {
                Assert.That(deleted, Is.False);
                createdEvents++;
            };
            manager.SubscriptionDeleted += (_, deleted) =>
            {
                Assert.That(deleted, Is.True);
                deletedEvents++;
            };
            var context = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);

            CreateSubscriptionResponse response = await manager
                .CreateSubscriptionAsync(
                    context,
                    requestedPublishingInterval: 100,
                    requestedLifetimeCount: 100,
                    requestedMaxKeepAliveCount: 10,
                    maxNotificationsPerPublish: 0,
                    publishingEnabled: true,
                    priority: 0,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(manager.GetSubscriptions(), Has.Count.EqualTo(1));
            Assert.That(serverDiagnostics.CurrentSubscriptionCount, Is.EqualTo(1));
            Assert.That(createdEvents, Is.EqualTo(1));

            await manager.SessionClosingAsync(
                context,
                m_sessionMock.Object.Id,
                deleteSubscriptions: false,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            IList<ISubscription> abandonedSubscriptions = manager.GetSubscriptions();
            Assert.That(abandonedSubscriptions, Has.Count.EqualTo(1));
            var abandonedSubscription = (Subscription)abandonedSubscriptions[0];
            Assert.That(abandonedSubscription.Id, Is.EqualTo(response.SubscriptionId));
            Assert.That(abandonedSubscription.SessionId.IsNull, Is.True);
            Assert.That(
                abandonedSubscription.EffectiveIdentity,
                Is.SameAs(ownerIdentity.Object));
            Assert.That(serverDiagnostics.CurrentSubscriptionCount, Is.EqualTo(1));
            Assert.That(deletedEvents, Is.Zero);

            await manager.SessionClosingAsync(
                context,
                m_sessionMock.Object.Id,
                deleteSubscriptions: true,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);

            Assert.That(manager.GetSubscriptions(), Is.Empty);
            Assert.That(serverDiagnostics.CurrentSubscriptionCount, Is.Zero);
            Assert.That(deletedEvents, Is.EqualTo(1));
            m_diagnosticsNodeManagerMock.Verify(
                diagnostics => diagnostics.DeleteSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<NodeId>(),
                    CancellationToken.None),
                Times.Once);
        }

        private static void SetExpiryTime(Subscription subscription, long expiryTime)
        {
            FieldInfo field = typeof(Subscription).GetField("m_publishTimerExpiry", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_publishTimerExpiry not found");
            field.SetValue(subscription, expiryTime);
        }

        private static T GetPrivateField<T>(
            object instance,
            string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"Field {fieldName} not found.");
            return (T)(field.GetValue(instance) ??
                throw new InvalidOperationException(
                    $"Field {fieldName} is null."));
        }

        private static void ResetKeepAlive(Subscription subscription)
        {
            FieldInfo field = typeof(Subscription).GetField("m_keepAliveCounter", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_keepAliveCounter not found");
            field.SetValue(subscription, (uint)0);
        }

        private static void AddMonitoredItem(Subscription subscription, IMonitoredItem item)
        {
            // Subscription has:
            // private readonly Dictionary<uint, LinkedListNode<IMonitoredItem>> m_monitoredItems;
            // private readonly LinkedList<IMonitoredItem> m_itemsToCheck;

            FieldInfo monitoredItemsField = typeof(Subscription).GetField("m_monitoredItems", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_monitoredItems not found");
            FieldInfo itemsToCheckField = typeof(Subscription).GetField("m_itemsToCheck", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToCheck not found");

            var monitoredItems = (System.Collections.IDictionary)monitoredItemsField.GetValue(subscription);
            var itemsToCheck = (LinkedList<IMonitoredItem>)itemsToCheckField.GetValue(subscription);

            // Add to itemsToCheck first to get the node
            LinkedListNode<IMonitoredItem> node = itemsToCheck.AddLast(item);
            // Add to dictionary
            monitoredItems.Add(item.Id, node);
        }

        private static void AddTriggerLink(Subscription subscription, uint triggeringId, ITriggeredMonitoredItem triggeredItem)
        {
            // private readonly Dictionary<uint, List<ITriggeredMonitoredItem>> m_itemsToTrigger;
            FieldInfo itemsToTriggerField = typeof(Subscription).GetField("m_itemsToTrigger", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToTrigger not found");
            var itemsToTrigger = (System.Collections.IDictionary)itemsToTriggerField.GetValue(subscription);

            if (!itemsToTrigger.Contains(triggeringId))
            {
                itemsToTrigger.Add(triggeringId, new List<ITriggeredMonitoredItem>());
            }
            var list = (List<ITriggeredMonitoredItem>)itemsToTrigger[triggeringId];
            list.Add(triggeredItem);
        }

        private static int GetItemsToPublishCount(Subscription subscription)
        {
            FieldInfo itemsToPublishField = typeof(Subscription).GetField("m_itemsToPublish", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToPublish not found");
            var itemsToPublish = (LinkedList<IMonitoredItem>)itemsToPublishField.GetValue(subscription);
            return itemsToPublish.Count;
        }

        [Test]
        public void PublishTimerExpired_NotExpired_ReturnsIdle()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);

            // Set expiry in far future
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() + 100000);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.Idle));
        }

        [Test]
        public void PublishTimerExpired_Expired_ReturnsNotificationsAvailable_ForKeepAlive()
        {
            using Subscription subscription = CreateSubscription(1000);
            // Don't reset keepalive, it should be maxKeepAliveCount initially.

            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));
        }

        [Test]
        public void PublishTimerExpired_Expired_ItemsReady_ReturnsNotificationsAvailable()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            // Mock Monitored Item
            var itemMock = new Mock<IMonitoredItem>();
            itemMock.Setup(i => i.Id).Returns(1);
            itemMock.Setup(i => i.IsReadyToPublish).Returns(true);

            AddMonitoredItem(subscription, itemMock.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));
            Assert.That(GetItemsToPublishCount(subscription), Is.EqualTo(1));
        }

        [Test]
        public void PublishTimerExpired_Expired_ItemsNotReady_ReturnsIdle()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            // Mock Monitored Item
            var itemMock = new Mock<IMonitoredItem>();
            itemMock.Setup(i => i.Id).Returns(1);
            itemMock.Setup(i => i.IsReadyToPublish).Returns(false);

            AddMonitoredItem(subscription, itemMock.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.Idle));
            Assert.That(GetItemsToPublishCount(subscription), Is.Zero);
        }

        [Test]
        public void PublishTimerExpired_Expired_IncrementsKeepAlive()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.Zero);

            subscription.PublishTimerExpired();

            Assert.That(subscription.Diagnostics.CurrentKeepAliveCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishTimerExpired_Triggering_CorrectlyTriggersAndPublishes()
        {
            using Subscription subscription = CreateSubscription(1000);
            ResetKeepAlive(subscription);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);

            // Item A: Triggering item. Ready to publish, Ready to trigger.
            var itemAMock = new Mock<IMonitoredItem>();
            itemAMock.Setup(i => i.Id).Returns(1);
            itemAMock.Setup(i => i.IsReadyToPublish).Returns(true);
            itemAMock.SetupProperty(i => i.IsReadyToTrigger, true); // Use property behavior so it can be set to false by Subscription

            // Item B: Triggered item. Initially NOT ready to publish.
            // B must implement ITriggeredMonitoredItem as well.
            var itemBMock = new Mock<IMonitoredItem>();
            itemBMock.As<ITriggeredMonitoredItem>();
            Mock<ITriggeredMonitoredItem> triggeredItemB = itemBMock.As<ITriggeredMonitoredItem>();

            itemBMock.Setup(i => i.Id).Returns(2);
            triggeredItemB.Setup(i => i.Id).Returns(2);

            // "State" of ready
            bool bIsReady = false;
            itemBMock.Setup(i => i.IsReadyToPublish).Returns(() => bIsReady);

            // SetTriggered updates state
            triggeredItemB.Setup(i => i.SetTriggered()).Returns(() =>
            {
                bIsReady = true;
                return true; // True indicates it has something to publish
            });

            // Add both items
            AddMonitoredItem(subscription, itemAMock.Object);
            AddMonitoredItem(subscription, itemBMock.Object);

            // Add trigger link A -> B
            AddTriggerLink(subscription, 1, triggeredItemB.Object);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(state, Is.EqualTo(PublishingState.NotificationsAvailable));

            // Both items should be in publish queue
            Assert.That(GetItemsToPublishCount(subscription), Is.EqualTo(2), "Both items should be ready to publish");

            // Verify trigger was called
            triggeredItemB.Verify(i => i.SetTriggered(), Times.Once);

            // Verify IsReadyToTrigger on A was reset to false
            Assert.That(itemAMock.Object.IsReadyToTrigger, Is.False, "IsReadyToTrigger should be reset");
        }

        private static void AddMonitoredItemToPublish(Subscription subscription, IMonitoredItem item)
        {
            FieldInfo itemsToPublishField = typeof(Subscription).GetField("m_itemsToPublish", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Field m_itemsToPublish not found");
            var itemsToPublish = (LinkedList<IMonitoredItem>)itemsToPublishField.GetValue(subscription);
            itemsToPublish.AddLast(item);
        }

        [Test]
        public void Publish_MultipleTimes_WithMaxMessageCount()
        {
            using var subscription = new Subscription(m_serverMock.Object, m_sessionMock.Object, 1, 100, 1000, 10, 1, 0, true, 2);
            var itemMock = new Mock<IDataChangeMonitoredItem2>();

            var values = new List<MonitoredItemNotification>
            {
                new() { Value = new DataValue(1) },
                new() { Value = new DataValue(2) },
                new() { Value = new DataValue(3) }
            };

            int counter = 0;
            itemMock.Setup(i => i.Publish(
                It.IsAny<OperationContext>(),
                It.IsAny<Queue<MonitoredItemNotification>>(),
                It.IsAny<Queue<DiagnosticInfo>>(),
                It.IsAny<uint>(),
                It.IsAny<Microsoft.Extensions.Logging.ILogger>()))
                .Returns<OperationContext, Queue<MonitoredItemNotification>, Queue<DiagnosticInfo>, uint, Microsoft.Extensions.Logging.ILogger>(
                (ctx, nq, dq, max, logger) =>
                {
                    if (counter < values.Count)
                    {
                        nq.Enqueue(values[counter++]);
                        dq.Enqueue(new DiagnosticInfo());
                        itemMock.SetupGet(x => x.IsReadyToPublish).Returns(counter < values.Count);
                        return counter < values.Count;
                    }
                    return false;
                });
            itemMock.SetupGet(i => i.Id).Returns(1);
            itemMock.SetupGet(i => i.IsReadyToPublish).Returns(true);
            itemMock.SetupGet(i => i.AttributeId).Returns(Attributes.Value);
            itemMock.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);

            AddMonitoredItem(subscription, itemMock.Object);
            SetExpiryTime(subscription, TimeProvider.System.GetTimestampMilliseconds() - 100);
            PublishingState state = subscription.PublishTimerExpired();

            AddMonitoredItemToPublish(subscription, itemMock.Object);

            var messages = new List<NotificationMessage>();

            // First publish
            var ctx1 = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage message = subscription.Publish(ctx1, out ArrayOf<uint> availableSequenceNumbers, out bool moreNotifications1);
            messages.Add(message);

            // Should be more because we generated multiple notifications and limit the max per publish to 1 for tests.
            Assert.That(moreNotifications1, Is.True);

            // Second publish
            NotificationMessage message2 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications2);

            // third publish
            NotificationMessage message3 = subscription.Publish(ctx1, out availableSequenceNumbers, out bool moreNotifications3);

            Assert.That(message2, Is.Not.Null);
            Assert.That(message3, Is.Not.Null);
            Assert.That(moreNotifications2, Is.True);
            Assert.That(moreNotifications3, Is.False);
        }

        [Test]
        public async Task PublishWithZeroLimitDrainsEventAndDataChangeQueuesAsync()
        {
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using Subscription subscription = CreateSubscription(
                publishingInterval: 100,
                maxNotificationsPerPublish: 0,
                timeProvider);
            var publishLimits = new List<uint>();
            Mock<IEventMonitoredItem> eventItem = CreateEventMonitoredItem(
                id: 1,
                notificationCount: 2,
                publishLimits);
            Mock<IDataChangeMonitoredItem> dataChangeItem = CreateDataChangeMonitoredItem(
                id: 2,
                notificationCount: 2,
                publishLimits);
            await RegisterMonitoredItemsAsync(
                subscription,
                eventItem.Object,
                dataChangeItem.Object).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(
                subscription.PublishTimerExpired(),
                Is.EqualTo(PublishingState.NotificationsAvailable));

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage message = subscription.Publish(
                context,
                out _,
                out bool moreNotifications);

            Assert.That(message.NotificationData, Has.Count.EqualTo(2));
            var eventNotification = (EventNotificationList)ExtensionObject.ToEncodeable(
                message.NotificationData[0]);
            var dataChangeNotification = (DataChangeNotification)ExtensionObject.ToEncodeable(
                message.NotificationData[1]);
            Assert.Multiple(() =>
            {
                Assert.That(moreNotifications, Is.False);
                Assert.That(eventNotification.Events, Has.Count.EqualTo(2));
                Assert.That(dataChangeNotification.MonitoredItems, Has.Count.EqualTo(2));
                Assert.That(dataChangeNotification.DiagnosticInfos, Has.Count.EqualTo(2));
                Assert.That(
                    publishLimits,
                    Is.EqualTo(new[] { uint.MaxValue, uint.MaxValue }));
            });
        }

        [Test]
        public async Task PublishWithFiniteLimitBuildsAndDrainsQueuedMessagesAsync()
        {
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using Subscription subscription = CreateSubscription(
                publishingInterval: 100,
                maxNotificationsPerPublish: 2,
                timeProvider);
            var publishLimits = new List<uint>();
            Mock<IEventMonitoredItem> firstEventItem = CreateEventMonitoredItem(
                id: 1,
                notificationCount: 3,
                publishLimits);
            Mock<IEventMonitoredItem> secondEventItem = CreateEventMonitoredItem(
                id: 2,
                notificationCount: 1,
                publishLimits);
            Mock<IDataChangeMonitoredItem> dataChangeItem = CreateDataChangeMonitoredItem(
                id: 3,
                notificationCount: 2,
                publishLimits);
            await RegisterMonitoredItemsAsync(
                subscription,
                firstEventItem.Object,
                secondEventItem.Object,
                dataChangeItem.Object).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMilliseconds(101));
            Assert.That(
                subscription.PublishTimerExpired(),
                Is.EqualTo(PublishingState.NotificationsAvailable));

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            NotificationMessage firstMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterFirst);
            NotificationMessage secondMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterSecond);
            NotificationMessage thirdMessage = subscription.Publish(
                context,
                out _,
                out bool moreAfterThird);

            Assert.Multiple(() =>
            {
                Assert.That(firstMessage.NotificationData, Has.Count.EqualTo(1));
                Assert.That(secondMessage.NotificationData, Has.Count.EqualTo(1));
                Assert.That(thirdMessage.NotificationData, Has.Count.EqualTo(1));
            });
            var firstEvents = (EventNotificationList)ExtensionObject.ToEncodeable(
                firstMessage.NotificationData[0]);
            var secondEvents = (EventNotificationList)ExtensionObject.ToEncodeable(
                secondMessage.NotificationData[0]);
            var dataChanges = (DataChangeNotification)ExtensionObject.ToEncodeable(
                thirdMessage.NotificationData[0]);
            Assert.Multiple(() =>
            {
                Assert.That(moreAfterFirst, Is.True);
                Assert.That(moreAfterSecond, Is.True);
                Assert.That(moreAfterThird, Is.False);
                Assert.That(firstEvents.Events, Has.Count.EqualTo(2));
                Assert.That(secondEvents.Events, Has.Count.EqualTo(2));
                Assert.That(dataChanges.MonitoredItems, Has.Count.EqualTo(2));
                Assert.That(publishLimits, Is.EqualTo(new uint[] { 6, 6, 6 }));
            });
        }

        [TestCase(0, 0L, 0L)]
        [TestCase(0, 25L, 25L)]
        [TestCase(10, 0L, 10L)]
        [TestCase(10, 10L, 10L)]
        [TestCase(10, 25L, 10L)]
        [TestCase(0, uint.MaxValue, uint.MaxValue)]
        [TestCase(10, uint.MaxValue, 10L)]
        [TestCase(int.MaxValue, uint.MaxValue, int.MaxValue)]
        public async Task CreateSubscriptionWithNotificationLimitsUsesEffectiveLimitAsync(
            int serverLimit,
            long requestedLimit,
            long expectedLimit)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationsPerPublish = serverLimit
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateSubscriptionResponse response = await manager.CreateSubscriptionAsync(
                context,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: (uint)requestedLimit,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);

            Assert.That(
                manager.TryGetSubscription(response.SubscriptionId, out ISubscription subscription),
                Is.True);
            Assert.That(
                subscription.Diagnostics.MaxNotificationsPerPublish,
                Is.EqualTo((uint)expectedLimit));
        }

        [TestCase(0, 0L, 0L)]
        [TestCase(0, 25L, 25L)]
        [TestCase(10, 0L, 10L)]
        [TestCase(10, 10L, 10L)]
        [TestCase(10, 25L, 10L)]
        [TestCase(0, uint.MaxValue, uint.MaxValue)]
        [TestCase(10, uint.MaxValue, 10L)]
        [TestCase(int.MaxValue, uint.MaxValue, int.MaxValue)]
        public async Task ModifySubscriptionWithNotificationLimitsUsesEffectiveLimitAsync(
            int serverLimit,
            long requestedLimit,
            long expectedLimit)
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationsPerPublish = serverLimit
                }
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateSubscriptionResponse response = await manager.CreateSubscriptionAsync(
                context,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: 1,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);

            manager.ModifySubscription(
                context,
                response.SubscriptionId,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: (uint)requestedLimit,
                priority: 0,
                revisedPublishingInterval: out _,
                revisedLifetimeCount: out _,
                revisedMaxKeepAliveCount: out _);

            Assert.That(
                manager.TryGetSubscription(response.SubscriptionId, out ISubscription subscription),
                Is.True);
            Assert.That(
                subscription.Diagnostics.MaxNotificationsPerPublish,
                Is.EqualTo((uint)expectedLimit));
        }

        private async Task RegisterMonitoredItemsAsync(
            Subscription subscription,
            params IMonitoredItem[] monitoredItems)
        {
            m_nodeManagerMock
                .Setup(n => n.CreateMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    subscription.Id,
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<IList<MonitoringFilterResult>>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Callback<
                    OperationContext,
                    uint,
                    double,
                    TimestampsToReturn,
                    ArrayOf<MonitoredItemCreateRequest>,
                    IList<ServiceResult>,
                    IList<MonitoringFilterResult>,
                    IList<IMonitoredItem>,
                    bool,
                    CancellationToken>(
                    (_, _, _, _, _, _, _, createdItems, _, _) =>
                    {
                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            createdItems[ii] = monitoredItems[ii];
                        }
                    })
                .Returns(default(ValueTask));

            var requests = new MonitoredItemCreateRequest[monitoredItems.Length];
            for (int ii = 0; ii < monitoredItems.Length; ii++)
            {
                requests[ii] = new MonitoredItemCreateRequest
                {
                    MonitoringMode = MonitoringMode.Reporting
                };
            }
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate = [.. requests];

            var context = new OperationContext(m_sessionMock.Object, new DiagnosticsMasks());
            CreateMonitoredItemsResponse response = await subscription.CreateMonitoredItemsAsync(
                context,
                TimestampsToReturn.Both,
                itemsToCreate).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(monitoredItems.Length));
        }

        private static Mock<IEventMonitoredItem> CreateEventMonitoredItem(
            uint id,
            int notificationCount,
            List<uint> publishLimits)
        {
            var item = new Mock<IEventMonitoredItem>();
            var createResult = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                RevisedSamplingInterval = 0,
                RevisedQueueSize = (uint)notificationCount
            };
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(true);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.Events);
            item.Setup(i => i.GetCreateResult(out createResult)).Returns(ServiceResult.Good);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<EventFieldList>>(),
                    It.IsAny<uint>()))
                .Returns<OperationContext, Queue<EventFieldList>, uint>(
                    (_, notifications, maxNotificationsPerPublish) =>
                    {
                        publishLimits.Add(maxNotificationsPerPublish);
                        for (int ii = 0; ii < notificationCount; ii++)
                        {
                            notifications.Enqueue(new EventFieldList());
                        }
                        return false;
                    });
            return item;
        }

        private static Mock<IDataChangeMonitoredItem> CreateDataChangeMonitoredItem(
            uint id,
            int notificationCount,
            List<uint> publishLimits)
        {
            var item = new Mock<IDataChangeMonitoredItem>();
            var createResult = new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                RevisedSamplingInterval = 0,
                RevisedQueueSize = (uint)notificationCount
            };
            item.SetupGet(i => i.Id).Returns(id);
            item.SetupGet(i => i.IsReadyToPublish).Returns(true);
            item.SetupGet(i => i.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            item.Setup(i => i.GetCreateResult(out createResult)).Returns(ServiceResult.Good);
            item.Setup(i => i.Publish(
                    It.IsAny<OperationContext>(),
                    It.IsAny<Queue<MonitoredItemNotification>>(),
                    It.IsAny<Queue<DiagnosticInfo>>(),
                    It.IsAny<uint>(),
                    It.IsAny<Microsoft.Extensions.Logging.ILogger>()))
                .Returns<
                    OperationContext,
                    Queue<MonitoredItemNotification>,
                    Queue<DiagnosticInfo>,
                    uint,
                    Microsoft.Extensions.Logging.ILogger>(
                    (_, notifications, diagnostics, maxNotificationsPerPublish, _) =>
                    {
                        publishLimits.Add(maxNotificationsPerPublish);
                        for (int ii = 0; ii < notificationCount; ii++)
                        {
                            notifications.Enqueue(
                                new MonitoredItemNotification { Value = new DataValue(ii) });
                            diagnostics.Enqueue(new DiagnosticInfo());
                        }
                        return false;
                    });
            return item;
        }
    }
}
