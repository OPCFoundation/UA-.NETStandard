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
using Opc.Ua.Security.Certificates;
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
            var serverDiagnostics = new ServerDiagnosticsSummaryDataType();
            m_serverMock
                .Setup(s => s.UpdateServerDiagnostics(
                    It.IsAny<Action<ServerDiagnosticsSummaryDataType>>()))
                .Callback<Action<ServerDiagnosticsSummaryDataType>>(
                    update => update(serverDiagnostics));

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);

            // ServerSystemContext requires invoked server mock to have properties setup
            m_serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(m_serverMock.Object));

            var identity = new UserIdentity(new AnonymousIdentityToken());
            m_sessionMock.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            m_sessionMock.Setup(s => s.Identity).Returns(identity);
            m_sessionMock.Setup(s => s.IdentityToken).Returns(identity.TokenHandler);
            var sessionDiagnostics = new SessionDiagnosticsDataType
            {
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:localhost:opcfoundation.org:SubscriptionTests"
                }
            };
            m_sessionMock
                .Setup(s => s.UpdateDiagnostics(
                    It.IsAny<Action<SessionDiagnosticsDataType>>()))
                .Callback<Action<SessionDiagnosticsDataType>>(
                    update => update(sessionDiagnostics));
            m_sessionMock.Setup(s => s.ClientApplicationUri)
                .Returns(() => sessionDiagnostics.ClientDescription?.ApplicationUri);

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
        public void CreateMonitoredItemsDoesNotRejectClosingSession()
        {
            using ServerInternalData server = CreateServerInternalData();
            m_sessionMock.SetupGet(session => session.IsClosing).Returns(true);
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

            // The subscription no longer gates monitored item operations on the
            // owning session's closing state; ownership and deletion are the only
            // guards. A closing session that still owns the subscription is allowed.
            Assert.DoesNotThrowAsync(
                async () => await subscription
                    .CreateMonitoredItemsAsync(
                        context,
                        TimestampsToReturn.Both,
                        [],
                        CancellationToken.None)
                    .ConfigureAwait(false));
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
        public void TransferIsRejectedWhenTargetUsesADifferentTokenTypeWithTheSameIdentifier()
        {
            // A user name may be spelled exactly like a certificate subject. The
            // two are different principals, so the subscription must not move
            // between them (OPC 10000-4 transfer keeps the same ClientUserId).
            const string identifier = "CN=SubscriptionTransferTests";
            var userNameToken = new UserNameIdentityTokenHandler(identifier, [1, 2, 3]);
            SetSessionIdentity(m_sessionMock, userNameToken);
            using Subscription subscription = CreateSubscription();

            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateCertificate(identifier)
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
            var x509Token = new X509IdentityTokenHandler(new X509IdentityToken
            {
                CertificateData = certificate.RawData.ToByteString()
            });
            var targetSession = new Mock<ISession>();
            targetSession.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            SetSessionIdentity(targetSession, x509Token);

            Assert.That(subscription.IsTransferIdentityCompatible(targetSession.Object), Is.False);
        }

        [Test]
        public void TransferIsAllowedWhenTargetPresentsTheSameUserNameIdentity()
        {
            var ownerToken = new UserNameIdentityTokenHandler("alice", [1, 2, 3]);
            SetSessionIdentity(m_sessionMock, ownerToken);
            using Subscription subscription = CreateSubscription();

            var targetToken = new UserNameIdentityTokenHandler("alice", [4, 5, 6]);
            var targetSession = new Mock<ISession>();
            targetSession.Setup(s => s.Id).Returns(new NodeId(Guid.NewGuid()));
            SetSessionIdentity(targetSession, targetToken);

            Assert.That(subscription.IsTransferIdentityCompatible(targetSession.Object), Is.True);
        }

        private static SessionDiagnosticsDataType CreateSessionDiagnostics()
        {
            return new SessionDiagnosticsDataType
            {
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:localhost:opcfoundation.org:SubscriptionTests"
                }
            };
        }

        private static void SetSessionIdentity(
            Mock<ISession> session,
            IUserIdentityTokenHandler tokenHandler)
        {
            var identity = new UserIdentity(tokenHandler);
            session.Setup(s => s.Identity).Returns(identity);
            session.Setup(s => s.IdentityToken).Returns(tokenHandler);
            var diagnostics = new SessionDiagnosticsDataType
            {
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:localhost:opcfoundation.org:SubscriptionTests"
                }
            };
            session
                .Setup(s => s.UpdateDiagnostics(
                    It.IsAny<Action<SessionDiagnosticsDataType>>()))
                .Callback<Action<SessionDiagnosticsDataType>>(
                    update => update(diagnostics));
            session.Setup(s => s.ClientApplicationUri)
                .Returns(() => diagnostics.ClientDescription?.ApplicationUri);
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

        private static void SetPrivateField<T>(
            object instance,
            string fieldName,
            T value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"Field {fieldName} not found.");
            field.SetValue(instance, value);
        }

        private static void ExpireOnNextPublishTimer(Subscription subscription)
        {
            uint maxLifetimeCount = GetPrivateField<uint>(
                subscription,
                "m_maxLifetimeCount");
            SetPrivateField(
                subscription,
                "m_lifetimeCounter",
                maxLifetimeCount - 1);
            SetPrivateField(subscription, "m_waitingForPublish", true);
            SetExpiryTime(
                subscription,
                TimeProvider.System.GetTimestampMilliseconds() - 100);
        }

        private static SessionPublishQueue GetPublishQueue(
            SubscriptionManager manager,
            NodeId sessionId)
        {
            NodeIdDictionary<SessionPublishQueue> publishQueues =
                GetPrivateField<NodeIdDictionary<SessionPublishQueue>>(
                    manager,
                    "m_publishQueues");
            return publishQueues[sessionId];
        }

        private async Task<(
            SubscriptionManager Manager,
            Subscription Subscription,
            OperationContext SourceContext,
            OperationContext DestinationContext,
            Mock<ISession> DestinationSession)> CreateTransferSubscriptionAsync()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);
            m_serverMock.SetupGet(server => server.SubscriptionManager).Returns(manager);

            var identity = new UserIdentity("transfer-user", new byte[] { 1, 2, 3 });
            m_sessionMock.SetupGet(session => session.EffectiveIdentity).Returns(identity);
            m_sessionMock.SetupGet(session => session.Identity).Returns(identity);
            m_sessionMock.SetupGet(session => session.IdentityToken).Returns(identity.TokenHandler);
            m_sessionMock.SetupGet(session => session.IsClosing).Returns(false);
            var destinationSession = new Mock<ISession>();
            destinationSession.SetupGet(session => session.Id)
                .Returns(new NodeId(Guid.NewGuid()));
            destinationSession.SetupGet(session => session.EffectiveIdentity).Returns(identity);
            destinationSession.SetupGet(session => session.Identity).Returns(identity);
            destinationSession.SetupGet(session => session.IdentityToken)
                .Returns(identity.TokenHandler);
            SessionDiagnosticsDataType destinationDiagnostics = CreateSessionDiagnostics();
            destinationSession.SetupGet(session => session.ClientApplicationUri)
                .Returns(() => destinationDiagnostics.ClientDescription?.ApplicationUri);
            destinationSession
                .Setup(session => session.UpdateDiagnostics(
                    It.IsAny<Action<SessionDiagnosticsDataType>>()))
                .Callback<Action<SessionDiagnosticsDataType>>(
                    update => update(destinationDiagnostics));
            var sourceContext = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);
            var destinationContext = new OperationContext(
                destinationSession.Object,
                DiagnosticsMasks.None);

            CreateSubscriptionResponse created = await manager.CreateSubscriptionAsync(
                sourceContext,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: 0,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);
            if (!manager.TryGetSubscription(
                    created.SubscriptionId,
                    out ISubscription subscription))
            {
                manager.Dispose();
                throw new InvalidOperationException("Created subscription was not registered.");
            }

            return (
                manager,
                (Subscription)subscription,
                sourceContext,
                destinationContext,
                destinationSession);
        }

        private static async ValueTask DeleteSubscriptionAndSignalAsync(
            SubscriptionManager manager,
            uint subscriptionId,
            TaskCompletionSource<bool> deletionCompleted,
            CancellationToken cancellationToken)
        {
            await manager.DeleteSubscriptionAsync(
                    null!,
                    subscriptionId,
                    cancellationToken)
                .ConfigureAwait(false);
            deletionCompleted.TrySetResult(true);
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

        [Test]
        public void RestoreTransferClaimRemovesCurrentClaimWhenRestoreEntryAlreadyExists()
        {
            using Subscription subscription = CreateSubscription();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                maxPublishRequests: 10);
            queue.Add(subscription);

            Assert.That(
                queue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim claim),
                Is.True);
            Assert.That(claim, Is.Not.Null);

            var collidingSubscription = new Mock<ISubscriptionPublishPipeline>();
            collidingSubscription.SetupGet(sub => sub.Id).Returns(subscription.Id);
            queue.Add(collidingSubscription.Object);

            Assert.That(queue.RestoreTransferClaim(claim!), Is.False);

            subscription.AbortTransfer(m_sessionMock.Object);
            queue.Remove(collidingSubscription.Object, removeQueuedRequests: false);
            queue.Add(subscription);

            Assert.That(
                queue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim retryClaim),
                Is.True,
                "The failed restore must not leave a stale claim that blocks future transfers.");
            Assert.That(retryClaim, Is.Not.Null);
            queue.CompleteTransferClaim(retryClaim!);
            subscription.AbortTransfer(m_sessionMock.Object);
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

        [Test]
        public async Task TransferSerializesWithClosingSourceSessionAsync()
        {
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            using var manager = new SubscriptionManager(
                m_serverMock.Object,
                configuration);
            var identity = new UserIdentity("transfer-user", new byte[] { 1, 2, 3 });
            bool sourceClosing = false;
            m_sessionMock.SetupGet(session => session.EffectiveIdentity).Returns(identity);
            m_sessionMock.SetupGet(session => session.Identity).Returns(identity);
            m_sessionMock.SetupGet(session => session.IdentityToken).Returns(identity.TokenHandler);
            m_sessionMock.SetupGet(session => session.IsClosing).Returns(() => sourceClosing);
            var destinationSession = new Mock<ISession>();
            destinationSession.SetupGet(session => session.Id).Returns(new NodeId(Guid.NewGuid()));
            destinationSession.SetupGet(session => session.EffectiveIdentity).Returns(identity);
            destinationSession.SetupGet(session => session.Identity).Returns(identity);
            destinationSession.SetupGet(session => session.IdentityToken)
                .Returns(identity.TokenHandler);
            SessionDiagnosticsDataType destinationDiagnostics = CreateSessionDiagnostics();
            destinationSession.SetupGet(session => session.ClientApplicationUri)
                .Returns(() => destinationDiagnostics.ClientDescription?.ApplicationUri);
            destinationSession
                .Setup(session => session.UpdateDiagnostics(
                    It.IsAny<Action<SessionDiagnosticsDataType>>()))
                .Callback<Action<SessionDiagnosticsDataType>>(
                    update => update(destinationDiagnostics));
            var sourceContext = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);
            var destinationContext = new OperationContext(
                destinationSession.Object,
                DiagnosticsMasks.None);
            CreateSubscriptionResponse created = await manager.CreateSubscriptionAsync(
                sourceContext,
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: 0,
                publishingEnabled: true,
                priority: 0).ConfigureAwait(false);
            Assert.That(
                manager.TryGetSubscription(created.SubscriptionId, out ISubscription subscription),
                Is.True);

            var transferEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseTransfer = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_nodeManagerMock
                .Setup(manager => manager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => transferEntered.TrySetResult(true))
                .Returns(() => new ValueTask(releaseTransfer.Task));
            sourceClosing = true;

            Task<TransferSubscriptionsResponse> transferTask = manager
                .TransferSubscriptionsAsync(
                    destinationContext,
                    [created.SubscriptionId],
                    sendInitialValues: false)
                .AsTask();
            Task entered = await Task.WhenAny(
                transferEntered.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!ReferenceEquals(entered, transferEntered.Task))
            {
                releaseTransfer.TrySetResult(true);
                Assert.Fail("Transfer did not reach the monitored-item barrier.");
            }

            Task closeTask = manager
                .SessionClosingAsync(
                    sourceContext,
                    m_sessionMock.Object.Id,
                    deleteSubscriptions: false,
                    CancellationToken.None)
                .AsTask();
            bool closeWaitedForTransfer = !closeTask.IsCompleted;
            releaseTransfer.TrySetResult(true);
            TransferSubscriptionsResponse transferred = await transferTask.ConfigureAwait(false);
            await closeTask.ConfigureAwait(false);
            var abandonedSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                    manager,
                    "m_abandonedSubscriptions");

            Assert.Multiple(() =>
            {
                Assert.That(closeWaitedForTransfer, Is.True);
                Assert.That(transferred.Results, Has.Count.EqualTo(1));
                Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(subscription.Session, Is.SameAs(destinationSession.Object));
                Assert.That(abandonedSubscriptions, Is.Empty);
            });
            Assert.DoesNotThrow(() => subscription.ResendData(destinationContext));
        }

        [Test]
        public async Task TransferClaimsSourceBeforeCallbacksAndBlocksStalePublishAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            SetPrivateField(subscription, "m_waitingForPublish", false);
            SetPrivateField(subscription, "m_lifetimeCounter", 0u);
            SetExpiryTime(
                subscription,
                TimeProvider.System.GetTimestampMilliseconds() - 100);
            m_sessionMock
                .Setup(session => session.IsSecureChannelValid("source-channel"))
                .Returns(true);
            IReadOnlyList<SessionPublishQueue.QueuedSubscription> staleSnapshot =
                sourceQueue.CapturePublishTimerSnapshot();
            using var publishCancellation = new CancellationTokenSource();
            Task<ISubscriptionPublishPipeline> sourcePublish = sourceQueue.PublishAsync(
                "source-channel",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                publishCancellation.Token);
            var transferEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseTransfer = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => transferEntered.TrySetResult(true))
                .Returns(() => new ValueTask(releaseTransfer.Task));

            Task<TransferSubscriptionsResponse> transferTask = manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: true)
                .AsTask();
            Task entered = await Task.WhenAny(
                transferEntered.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!ReferenceEquals(entered, transferEntered.Task))
            {
                releaseTransfer.TrySetResult(true);
                Assert.Fail("Transfer did not reach the monitored-item callback barrier.");
            }

            try
            {
                sourceQueue.PublishTimerExpired(staleSnapshot);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        sourceQueue.CapturePublishTimerSnapshot(),
                        Is.Empty,
                        "The source entry must be claimed before callbacks run.");
                    Assert.That(
                        sourcePublish.IsCompleted,
                        Is.False,
                        "A stale source timer must not assign the claimed subscription.");
                    ServiceResultException error = Assert.Throws<ServiceResultException>(
                        () => subscription.Publish(
                            fixture.SourceContext,
                            out _,
                            out _));
                    Assert.That(
                        error.StatusCode,
                        Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                });
            }
            finally
            {
                releaseTransfer.TrySetResult(true);
            }

            TransferSubscriptionsResponse transferred = await transferTask.ConfigureAwait(false);
            publishCancellation.Cancel();
            try
            {
                await sourcePublish.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The old session's parked Publish is cancelled or completed by
                // the transferred-status notification, never with the subscription.
            }

            Assert.Multiple(() =>
            {
                Assert.That(transferred.Results, Has.Count.EqualTo(1));
                Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    subscription.Session,
                    Is.SameAs(fixture.DestinationSession.Object));
            });
            Assert.DoesNotThrow(
                () => subscription.ResendData(fixture.DestinationContext));
        }

        [Test]
        public async Task TransferInitialValueIsPublishedOnlyByDestinationAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            using var queueFactory = new MonitoredItemQueueFactory(m_telemetry);
            m_serverMock.Setup(server => server.MonitoredItemQueueFactory).Returns(queueFactory);
            var itemOwner = new Mock<IAsyncNodeManager>();
            using var monitoredItem = new MonitoredItem(
                m_serverMock.Object,
                itemOwner.Object,
                new object(),
                subscription.Id,
                id: 10,
                new ReadValueId
                {
                    NodeId = new NodeId("TransferValue", 2),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: 11,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
            await RegisterMonitoredItemsAsync(subscription, monitoredItem).ConfigureAwait(false);
            monitoredItem.QueueValue(new DataValue(new Variant(1234)), null);
            var initialNotifications = new Queue<MonitoredItemNotification>();
            monitoredItem.Publish(
                new OperationContext(monitoredItem),
                initialNotifications,
                new Queue<DiagnosticInfo>(),
                1,
                m_telemetry.CreateLogger<SubscriptionTests>());
            Assert.That(initialNotifications, Has.Count.EqualTo(1));

            var transferEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseTransfer = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            itemOwner
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<bool>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns<
                    OperationContext,
                    bool,
                    IList<IMonitoredItem>,
                    IList<bool>,
                    IList<ServiceResult>,
                    MonitoredItemTransferOptions,
                    CancellationToken>(async (_, sendInitialValues, monitoredItems, processedItems, errors, transferOptions, cancellationToken) =>
                    {
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            if (processedItems[ii])
                            {
                                continue;
                            }

                            processedItems[ii] = true;
                            errors[ii] = ServiceResult.Good;
                            if (sendInitialValues && !transferOptions.DeferInitialValues)
                            {
                                monitoredItems[ii].SetupResendDataTrigger();
                            }
                        }
                        transferEntered.TrySetResult(true);
                        await releaseTransfer.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    });
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();
            configurationNodeManager
                .SetupGet(nodeManager => nodeManager.NamespaceUris)
                .Returns(System.Array.Empty<string>());
            var coreNodeManager = new Mock<ICoreNodeManager>();
            coreNodeManager
                .SetupGet(nodeManager => nodeManager.NamespaceUris)
                .Returns(System.Array.Empty<string>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory
                .Setup(nodeManagerFactory => nodeManagerFactory.CreateConfigurationNodeManager())
                .Returns(configurationNodeManager.Object);
            factory
                .Setup(nodeManagerFactory => nodeManagerFactory.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(coreNodeManager.Object);
            m_serverMock.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            using var masterNodeManager = new MasterNodeManager(
                m_serverMock.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                null,
                itemOwner.Object);
            m_serverMock.Setup(server => server.NodeManager).Returns(masterNodeManager);

            Task<TransferSubscriptionsResponse> transferTask = manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: true)
                .AsTask();
            Task entered = await Task.WhenAny(
                transferEntered.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!ReferenceEquals(entered, transferEntered.Task))
            {
                releaseTransfer.TrySetResult(true);
                Assert.Fail("Transfer did not reach the monitored-item callback barrier.");
            }

            try
            {
                itemOwner.Verify(
                    nodeManager => nodeManager.TransferMonitoredItemsAsync(
                        It.IsAny<OperationContext>(),
                        true,
                        It.IsAny<IList<IMonitoredItem>>(),
                        It.IsAny<IList<bool>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.Is<MonitoredItemTransferOptions>(options => options.DeferInitialValues),
                        It.IsAny<CancellationToken>()),
                    Times.Once);
                Assert.That(monitoredItem.IsResendData, Is.False);
                ServiceResultException error = Assert.Throws<ServiceResultException>(
                    () => subscription.Publish(
                        fixture.SourceContext,
                        out _,
                        out _));
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            }
            finally
            {
                releaseTransfer.TrySetResult(true);
            }

            TransferSubscriptionsResponse transferred = await transferTask.ConfigureAwait(false);
            Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(monitoredItem.IsResendData, Is.True);

            SessionPublishQueue destinationQueue = GetPublishQueue(
                manager,
                fixture.DestinationContext.SessionId);
            SetPrivateField(subscription, "m_waitingForPublish", false);
            SetPrivateField(subscription, "m_lifetimeCounter", 0u);
            SetExpiryTime(
                subscription,
                TimeProvider.System.GetTimestampMilliseconds() - 100);
            destinationQueue.PublishTimerExpired(
                destinationQueue.CapturePublishTimerSnapshot());
            ISubscription readySubscription = await destinationQueue.PublishAsync(
                "destination-channel",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                CancellationToken.None).ConfigureAwait(false);
            NotificationMessage message = readySubscription.Publish(
                fixture.DestinationContext,
                out _,
                out _);

            Assert.Multiple(() =>
            {
                Assert.That(readySubscription, Is.SameAs(subscription));
                Assert.That(message, Is.Not.Null);
                Assert.That(message.NotificationData, Is.Not.Empty);
                Assert.That(monitoredItem.IsResendData, Is.False);
            });
        }

        [Test]
        public async Task TransferFallbackAppliesInitialValueExactlyOnceAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            using var queueFactory = new MonitoredItemQueueFactory(m_telemetry);
            m_serverMock.Setup(server => server.MonitoredItemQueueFactory).Returns(queueFactory);
            var itemOwner = new Mock<IAsyncNodeManager>();
            using var monitoredItem = new MonitoredItem(
                m_serverMock.Object,
                itemOwner.Object,
                new object(),
                subscription.Id,
                id: 11,
                new ReadValueId
                {
                    NodeId = new NodeId("FallbackTransferValue", 2),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: 12,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1000);
            await RegisterMonitoredItemsAsync(subscription, monitoredItem).ConfigureAwait(false);
            monitoredItem.QueueValue(new DataValue(new Variant(5678)), null);
            var initialNotifications = new Queue<MonitoredItemNotification>();
            monitoredItem.Publish(
                new OperationContext(monitoredItem),
                initialNotifications,
                new Queue<DiagnosticInfo>(),
                1,
                m_telemetry.CreateLogger<SubscriptionTests>());
            Assert.That(initialNotifications, Has.Count.EqualTo(1));
            int setupResendCalls = 0;
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    true,
                    It.Is<IList<IMonitoredItem>>(items => items.Contains(monitoredItem)),
                    It.IsAny<IList<ServiceResult>>(),
                    It.Is<MonitoredItemTransferOptions>(options => !options.DeferInitialValues),
                    It.IsAny<CancellationToken>()))
                .Callback<OperationContext, bool, IList<IMonitoredItem>, IList<ServiceResult>, MonitoredItemTransferOptions, CancellationToken>(
                    (_, sendInitialValues, monitoredItems, errors, transferOptions, _) =>
                    {
                        Assert.That(transferOptions.DeferInitialValues, Is.False);
                        for (int ii = 0; ii < monitoredItems.Count; ii++)
                        {
                            errors[ii] = ServiceResult.Good;
                            if (sendInitialValues)
                            {
                                setupResendCalls++;
                                monitoredItems[ii].SetupResendDataTrigger();
                            }
                        }
                    })
                .Returns(default(ValueTask));

            TransferSubscriptionsResponse transferred = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: true)
                .ConfigureAwait(false);

            Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(setupResendCalls, Is.EqualTo(1));
            SessionPublishQueue destinationQueue = GetPublishQueue(
                manager,
                fixture.DestinationContext.SessionId);
            SetPrivateField(subscription, "m_waitingForPublish", false);
            SetPrivateField(subscription, "m_lifetimeCounter", 0u);
            SetExpiryTime(
                subscription,
                TimeProvider.System.GetTimestampMilliseconds() - 100);
            destinationQueue.PublishTimerExpired(
                destinationQueue.CapturePublishTimerSnapshot());
            ISubscription readySubscription = await destinationQueue.PublishAsync(
                "destination-channel",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                CancellationToken.None).ConfigureAwait(false);
            NotificationMessage firstMessage = readySubscription.Publish(
                fixture.DestinationContext,
                out _,
                out _);
            NotificationMessage secondMessage = readySubscription.Publish(
                fixture.DestinationContext,
                out _,
                out _);

            Assert.Multiple(() =>
            {
                Assert.That(firstMessage.NotificationData, Is.Not.Empty);
                Assert.That(secondMessage?.NotificationData ?? [], Is.Empty);
                Assert.That(monitoredItem.IsResendData, Is.False);
            });
        }

        [Test]
        public async Task SourcePublishTimerSnapshotDoesNotExpireTransferredSubscriptionAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            ExpireOnNextPublishTimer(subscription);
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            var snapshotCaptured = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSnapshot = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task staleTimer = Task.Run(
                async () =>
                {
                    IReadOnlyList<SessionPublishQueue.QueuedSubscription> snapshot =
                        sourceQueue.CapturePublishTimerSnapshot();
                    snapshotCaptured.TrySetResult(true);
                    await releaseSnapshot.Task.ConfigureAwait(false);
                    sourceQueue.PublishTimerExpired(snapshot);
                });

            Task captured = await Task.WhenAny(
                snapshotCaptured.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!ReferenceEquals(captured, snapshotCaptured.Task))
            {
                releaseSnapshot.TrySetResult(true);
                Assert.Fail("Publish timer did not capture the source queue.");
            }

            TransferSubscriptionsResponse transferred;
            try
            {
                transferred = await manager.TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false).ConfigureAwait(false);
            }
            finally
            {
                releaseSnapshot.TrySetResult(true);
            }
            await staleTimer.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transferred.Results, Has.Count.EqualTo(1));
                Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    subscription.Session,
                    Is.SameAs(fixture.DestinationSession.Object));
                Assert.That(
                    manager.TryGetSubscription(subscription.Id, out _),
                    Is.True);
            });
            Assert.DoesNotThrow(
                () => subscription.ResendData(fixture.DestinationContext));
        }

        [Test]
        public async Task AbandonedTimerSnapshotDoesNotExpireTransferredSubscriptionAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            m_sessionMock.SetupGet(session => session.IsClosing).Returns(true);
            await manager.SessionClosingAsync(
                    fixture.SourceContext,
                    fixture.SourceContext.SessionId,
                    deleteSubscriptions: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
            ExpireOnNextPublishTimer(subscription);

            var snapshotCaptured = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseSnapshot = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Task staleTimer = Task.Run(
                async () =>
                {
                    IReadOnlyList<ISubscriptionPublishPipeline> snapshot =
                        manager.CaptureAbandonedPublishTimerSnapshot();
                    snapshotCaptured.TrySetResult(true);
                    await releaseSnapshot.Task.ConfigureAwait(false);
                    manager.ProcessAbandonedPublishTimers(snapshot);
                });

            Task captured = await Task.WhenAny(
                snapshotCaptured.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (!ReferenceEquals(captured, snapshotCaptured.Task))
            {
                releaseSnapshot.TrySetResult(true);
                Assert.Fail("Publish timer did not capture the abandoned subscription.");
            }

            TransferSubscriptionsResponse transferred;
            try
            {
                transferred = await manager.TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false).ConfigureAwait(false);
            }
            finally
            {
                releaseSnapshot.TrySetResult(true);
            }
            await staleTimer.ConfigureAwait(false);
            ConcurrentDictionary<uint, ISubscription> abandonedSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                    manager,
                    "m_abandonedSubscriptions");

            Assert.Multiple(() =>
            {
                Assert.That(transferred.Results, Has.Count.EqualTo(1));
                Assert.That(transferred.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    subscription.Session,
                    Is.SameAs(fixture.DestinationSession.Object));
                Assert.That(abandonedSubscriptions, Is.Empty);
                Assert.That(
                    manager.TryGetSubscription(subscription.Id, out _),
                    Is.True);
            });
            Assert.DoesNotThrow(
                () => subscription.ResendData(fixture.DestinationContext));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CurrentOwnerPublishTimerStillExpiresSubscriptionAsync(
            bool abandonBeforeExpiration)
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            if (abandonBeforeExpiration)
            {
                m_sessionMock.SetupGet(session => session.IsClosing).Returns(true);
                await manager.SessionClosingAsync(
                        fixture.SourceContext,
                        fixture.SourceContext.SessionId,
                        deleteSubscriptions: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            var deletionCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            m_serverMock
                .Setup(server => server.DeleteSubscriptionAsync(
                    subscription.Id,
                    It.IsAny<CancellationToken>()))
                .Returns((uint subscriptionId, CancellationToken cancellationToken) =>
                    DeleteSubscriptionAndSignalAsync(
                        manager,
                        subscriptionId,
                        deletionCompleted,
                        cancellationToken));
            ExpireOnNextPublishTimer(subscription);

            if (abandonBeforeExpiration)
            {
                manager.ProcessAbandonedPublishTimers(
                    manager.CaptureAbandonedPublishTimerSnapshot());
            }
            else
            {
                sourceQueue.PublishTimerExpired(
                    sourceQueue.CapturePublishTimerSnapshot());
            }

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => subscription.ResendData(fixture.SourceContext));
            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            Task deleted = await Task.WhenAny(
                deletionCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            Assert.That(
                deleted,
                Is.SameAs(deletionCompleted.Task),
                "Expired subscription cleanup did not complete.");
            Assert.That(
                manager.TryGetSubscription(subscription.Id, out _),
                Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedTransferRestoresClaimedExpirationSourceAsync(
            bool abandonBeforeTransfer)
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            if (abandonBeforeTransfer)
            {
                m_sessionMock.SetupGet(session => session.IsClosing).Returns(true);
                await manager.SessionClosingAsync(
                        fixture.SourceContext,
                        fixture.SourceContext.SessionId,
                        deleteSubscriptions: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            int transferCalls = 0;
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    () => Interlocked.Increment(ref transferCalls) == 1
                        ? new ValueTask(
                            Task.FromException(
                                new ServiceResultException(
                                    StatusCodes.BadUnexpectedError)))
                        : new ValueTask());

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(
                    ServiceResult.IsBad(failed.Results[0].StatusCode),
                    Is.True);
                if (abandonBeforeTransfer)
                {
                    Assert.That(subscription.Session, Is.Null);
                    ConcurrentDictionary<uint, ISubscription> abandonedSubscriptions =
                        GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                            manager,
                            "m_abandonedSubscriptions");
                    Assert.That(
                        abandonedSubscriptions.TryGetValue(
                            subscription.Id,
                            out ISubscription restoredSubscription),
                        Is.True);
                    Assert.That(restoredSubscription, Is.SameAs(subscription));
                }
                else
                {
                    IReadOnlyList<SessionPublishQueue.QueuedSubscription> restoredSource =
                        sourceQueue.CapturePublishTimerSnapshot();
                    Assert.That(subscription.Session, Is.SameAs(m_sessionMock.Object));
                    Assert.That(restoredSource, Has.Count.EqualTo(1));
                    Assert.That(
                        restoredSource[0].Subscription,
                        Is.SameAs(subscription));
                }
            });

            TransferSubscriptionsResponse retried = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.That(retried.Results, Has.Count.EqualTo(1));
            Assert.That(retried.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.DoesNotThrow(
                () => subscription.ResendData(fixture.DestinationContext));
        }

        [Test]
        public async Task FailedTransferPreservesDestinationQueuedPublishRequestsAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            fixture.DestinationSession
                .Setup(session => session.IsSecureChannelValid("destination-channel"))
                .Returns(true);
            CreateSubscriptionResponse destinationCreated = await manager
                .CreateSubscriptionAsync(
                    fixture.DestinationContext,
                    requestedPublishingInterval: 1000,
                    requestedLifetimeCount: 30,
                    requestedMaxKeepAliveCount: 10,
                    maxNotificationsPerPublish: 0,
                    publishingEnabled: true,
                    priority: 0)
                .ConfigureAwait(false);
            Assert.That(
                manager.TryGetSubscription(
                    destinationCreated.SubscriptionId,
                    out ISubscription destinationSubscription),
                Is.True);
            Assert.That(destinationSubscription, Is.Not.Null);
            SessionPublishQueue destinationQueue = GetPublishQueue(
                manager,
                fixture.DestinationContext.SessionId);
            Task<ISubscriptionPublishPipeline> destinationPublish = destinationQueue.PublishAsync(
                "destination-channel",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                CancellationToken.None);
            Assert.That(destinationPublish.IsCompleted, Is.False);
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask(Task.FromException(
                    new ServiceResultException(StatusCodes.BadUnexpectedError))));

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(ServiceResult.IsBad(failed.Results[0].StatusCode), Is.True);
                Assert.That(destinationPublish.IsCompleted, Is.False);
            });

            destinationQueue.PublishCompleted(destinationSubscription!, moreNotifications: true);

            ISubscription publishedSubscription = await destinationPublish.ConfigureAwait(false);
            Assert.That(publishedSubscription, Is.SameAs(destinationSubscription!));
        }

        [Test]
        public void TryClaimForTransferFailsWhenTheClaimingSessionIsNotTheOwner()
        {
            using Subscription subscription = CreateSubscription();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                maxPublishRequests: 10);
            queue.Add(subscription);
            var staleSession = new Mock<ISession>();
            staleSession.Setup(session => session.Id).Returns(new NodeId(Guid.NewGuid()));

            bool claimed = queue.TryClaimForTransfer(
                subscription,
                staleSession.Object,
                out SessionPublishQueue.SubscriptionTransferClaim claim);

            Assert.Multiple(() =>
            {
                Assert.That(claimed, Is.False);
                Assert.That(claim, Is.Null);
                Assert.That(
                    queue.ContainsSubscription(subscription),
                    Is.True,
                    "A refused claim must leave the subscription publishable by its owner.");
            });

            Assert.That(
                queue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim ownerClaim),
                Is.True,
                "The refused claim must not block the real owner from starting a transfer.");
            queue.CompleteTransferClaim(ownerClaim!);
            subscription.AbortTransfer(m_sessionMock.Object);
        }

        [Test]
        public void RestoreTransferClaimFailsWhenTheClaimWasAlreadyRestored()
        {
            using Subscription subscription = CreateSubscription();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                maxPublishRequests: 10);
            queue.Add(subscription);
            Assert.That(
                queue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim claim),
                Is.True);

            Assert.That(queue.RestoreTransferClaim(claim!), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(
                    queue.RestoreTransferClaim(claim!),
                    Is.False,
                    "A claim may only be restored once, otherwise a later queue entry is overwritten.");
                Assert.That(queue.ContainsSubscription(subscription), Is.True);
            });
            subscription.AbortTransfer(m_sessionMock.Object);
        }

        [Test]
        public void TryRemoveForTransferRemovesOnlyTheExactQueuedEntry()
        {
            using Subscription subscription = CreateSubscription();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                maxPublishRequests: 10);
            queue.Add(subscription);
            var impostor = new Mock<ISubscription>();
            impostor.Setup(candidate => candidate.Id).Returns(subscription.Id);

            Assert.Multiple(() =>
            {
                Assert.That(
                    queue.TryRemoveForTransfer(impostor.Object),
                    Is.False,
                    "A different subscription instance with the same id must not remove the entry.");
                Assert.That(queue.ContainsSubscription(subscription), Is.True);
            });

            Assert.That(queue.TryRemoveForTransfer(subscription), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(queue.ContainsSubscription(subscription), Is.False);
                Assert.That(
                    queue.TryRemoveForTransfer(subscription),
                    Is.False,
                    "A subscription that is no longer queued cannot be removed a second time.");
            });
        }

        [Test]
        public async Task PublishCompletedKeepsNotificationsOfAClaimedSubscriptionAsync()
        {
            using Subscription subscription = CreateSubscription();
            using var queue = new SessionPublishQueue(
                m_serverMock.Object,
                m_sessionMock.Object,
                maxPublishRequests: 10);
            queue.Add(subscription);
            Assert.That(
                queue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim claim),
                Is.True);

            queue.PublishCompleted(subscription, moreNotifications: true);

            Assert.Multiple(() =>
            {
                Assert.That(claim!.Entry.Publishing, Is.False);
                Assert.That(
                    claim.Entry.ReadyToPublish,
                    Is.True,
                    "A notification raised while the entry is claimed must survive an abandoned transfer.");
            });

            Assert.That(queue.RestoreTransferClaim(claim!), Is.True);
            Task<ISubscriptionPublishPipeline> publish = queue.PublishAsync(
                "channel1",
                DateTime.MaxValue,
                requeue: false,
                parkSink: null,
                CancellationToken.None);

            Assert.That(publish.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(await publish.ConfigureAwait(false), Is.SameAs(subscription));
            subscription.AbortTransfer(m_sessionMock.Object);
        }

        [Test]
        public void TryBeginTransferFailsWhileATransferIsAlreadyInProgress()
        {
            using Subscription subscription = CreateSubscription();

            Assert.That(subscription.TryBeginTransfer(m_sessionMock.Object), Is.True);

            Assert.That(
                subscription.TryBeginTransfer(m_sessionMock.Object),
                Is.False,
                "Only one transfer may reserve a subscription at a time.");

            subscription.AbortTransfer(m_sessionMock.Object);
            Assert.That(
                subscription.TryBeginTransfer(m_sessionMock.Object),
                Is.True,
                "An aborted transfer must release the reservation.");
            subscription.AbortTransfer(m_sessionMock.Object);
        }

        [Test]
        public void PublishTimerIsIdleWhileATransferIsInProgress()
        {
            using Subscription subscription = CreateSubscription();
            ExpireOnNextPublishTimer(subscription);
            Assert.That(subscription.TryBeginTransfer(m_sessionMock.Object), Is.True);

            PublishingState state = subscription.PublishTimerExpired();

            Assert.That(
                state,
                Is.EqualTo(PublishingState.Idle),
                "A reserved subscription must not expire on the source session timer.");
            subscription.AbortTransfer(m_sessionMock.Object);
        }

        [Test]
        public void PrepareSessionTransferAsyncRejectsAnUnreservedSubscription()
        {
            using Subscription subscription = CreateSubscription();
            var context = new OperationContext(
                m_sessionMock.Object,
                DiagnosticsMasks.None);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () =>
                {
                    await subscription
                        .PrepareSessionTransferAsync(
                            context,
                            m_sessionMock.Object,
                            sendInitialValues: false,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                });

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public void CompleteTransferRejectsAnUnexpectedOwner()
        {
            using Subscription subscription = CreateSubscription();

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => subscription.CompleteTransfer(m_sessionMock.Object));

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task TransferSessionAsyncMovesOwnershipToTheDestinationAsync()
        {
            using Subscription subscription = CreateSubscription();
            var destinationSession = new Mock<ISession>();
            destinationSession.Setup(session => session.Id).Returns(new NodeId(Guid.NewGuid()));
            SetSessionIdentity(
                destinationSession,
                new UserNameIdentityTokenHandler("transfer-user", [1, 2, 3]));
            var context = new OperationContext(
                destinationSession.Object,
                DiagnosticsMasks.None);

            await subscription
                .TransferSessionAsync(context, sendInitialValues: true, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(subscription.Session, Is.SameAs(destinationSession.Object));
            m_nodeManagerMock.Verify(
                nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    context,
                    true,
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task TransferFailsWhenTheSourceQueueEntryIsAlreadyClaimedAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            Assert.That(
                sourceQueue.TryClaimForTransfer(
                    subscription,
                    m_sessionMock.Object,
                    out SessionPublishQueue.SubscriptionTransferClaim claim),
                Is.True);
            var diagnosticsContext = new OperationContext(
                fixture.DestinationSession.Object,
                DiagnosticsMasks.OperationAll);

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    diagnosticsContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(
                    failed.Results[0].StatusCode,
                    Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                Assert.That(failed.DiagnosticInfos, Has.Count.EqualTo(1));
                Assert.That(
                    subscription.Session,
                    Is.SameAs(m_sessionMock.Object),
                    "A refused claim must leave ownership with the source session.");
            });

            Assert.That(sourceQueue.RestoreTransferClaim(claim!), Is.True);
            subscription.AbortTransfer(m_sessionMock.Object);
            TransferSubscriptionsResponse retried = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.That(retried.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task TransferFailsWhenTheAbandonedSubscriptionIsAlreadyReservedAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            m_sessionMock.SetupGet(session => session.IsClosing).Returns(true);
            await manager.SessionClosingAsync(
                    fixture.SourceContext,
                    fixture.SourceContext.SessionId,
                    deleteSubscriptions: false,
                    CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(subscription.Session, Is.Null);
            Assert.That(subscription.TryBeginTransfer(null), Is.True);
            var diagnosticsContext = new OperationContext(
                fixture.DestinationSession.Object,
                DiagnosticsMasks.OperationAll);

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    diagnosticsContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            ConcurrentDictionary<uint, ISubscription> abandonedSubscriptions =
                GetPrivateField<ConcurrentDictionary<uint, ISubscription>>(
                    manager,
                    "m_abandonedSubscriptions");
            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(
                    failed.Results[0].StatusCode,
                    Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                Assert.That(failed.DiagnosticInfos, Has.Count.EqualTo(1));
                Assert.That(
                    abandonedSubscriptions.TryGetValue(
                        subscription.Id,
                        out ISubscription retainedSubscription),
                    Is.True,
                    "A refused reservation must leave the subscription abandoned, not lost.");
                Assert.That(retainedSubscription, Is.SameAs(subscription));
            });

            subscription.AbortTransfer(null);
            TransferSubscriptionsResponse retried = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.That(retried.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task FailedOwnershipCommitRollsBackThePreparedTransferAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            int transferCalls = 0;
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    // Drop the reservation while the monitored items are handed over so the
                    // ownership commit that follows preparation has to fail and roll back.
                    if (Interlocked.Increment(ref transferCalls) == 1)
                    {
                        subscription.AbortTransfer(m_sessionMock.Object);
                    }
                })
                .Returns(default(ValueTask));

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            IReadOnlyList<SessionPublishQueue.QueuedSubscription> restoredSource =
                sourceQueue.CapturePublishTimerSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(ServiceResult.IsBad(failed.Results[0].StatusCode), Is.True);
                Assert.That(
                    subscription.Session,
                    Is.SameAs(m_sessionMock.Object),
                    "A failed ownership commit must leave the source session as the owner.");
                Assert.That(restoredSource, Has.Count.EqualTo(1));
                Assert.That(restoredSource[0].Subscription, Is.SameAs(subscription));
            });

            TransferSubscriptionsResponse retried = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(retried.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(
                    subscription.Session,
                    Is.SameAs(fixture.DestinationSession.Object));
            });
        }

        [Test]
        public async Task FailedSourceQueueRestoreAggregatesTheTransferErrorAsync()
        {
            var fixture = await CreateTransferSubscriptionAsync().ConfigureAwait(false);
            using SubscriptionManager manager = fixture.Manager;
            Subscription subscription = fixture.Subscription;
            SessionPublishQueue sourceQueue = GetPublishQueue(
                manager,
                fixture.SourceContext.SessionId);
            var collidingSubscription = new Mock<ISubscriptionPublishPipeline>();
            collidingSubscription.Setup(candidate => candidate.Id).Returns(subscription.Id);
            int transferCalls = 0;
            m_nodeManagerMock
                .Setup(nodeManager => nodeManager.TransferMonitoredItemsAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<bool>(),
                    It.IsAny<IList<IMonitoredItem>>(),
                    It.IsAny<IList<ServiceResult>>(),
                    It.IsAny<MonitoredItemTransferOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (Interlocked.Increment(ref transferCalls) != 1)
                    {
                        return default;
                    }

                    // Occupy the claimed slot so restoring the source queue entry has to
                    // fail, which is the only way the rollback itself can report an error.
                    sourceQueue.Add(collidingSubscription.Object);
                    return new ValueTask(
                        Task.FromException(
                            new ServiceResultException(StatusCodes.BadUnexpectedError)));
                });

            TransferSubscriptionsResponse failed = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(failed.Results, Has.Count.EqualTo(1));
                Assert.That(ServiceResult.IsBad(failed.Results[0].StatusCode), Is.True);
                Assert.That(
                    subscription.Session,
                    Is.SameAs(m_sessionMock.Object),
                    "A rollback that cannot restore the queue must still keep source ownership.");
                Assert.That(
                    sourceQueue.ContainsSubscription(subscription),
                    Is.False);
            });

            sourceQueue.Remove(collidingSubscription.Object, removeQueuedRequests: false);
            sourceQueue.Add(subscription);
            TransferSubscriptionsResponse retried = await manager
                .TransferSubscriptionsAsync(
                    fixture.DestinationContext,
                    [subscription.Id],
                    sendInitialValues: false)
                .ConfigureAwait(false);

            Assert.That(
                retried.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.Good),
                "A reported rollback failure must not leave a stale claim behind.");
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
