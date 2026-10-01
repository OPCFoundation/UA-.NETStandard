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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for the subscription manager: expiry cleanup, session
    /// closing races, diagnostics bookkeeping and revised subscription limits.
    /// </summary>
    [TestFixture]
    [Category("Subscription")]
    [Parallelizable]
    public sealed class SubscriptionManagerRegressionTests
    {
        private Mock<IServerInternal> m_serverMock;
        private Mock<IMasterNodeManager> m_nodeManagerMock;
        private ServerDiagnosticsSummaryDataType m_serverDiagnostics;
        private ITelemetryContext m_telemetry;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_serverMock = new Mock<IServerInternal>();
            m_nodeManagerMock = new Mock<IMasterNodeManager>();
            var diagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            diagnosticsNodeManager
                .Setup(d => d.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                .ReturnsAsync(new NodeId(1));

            m_serverMock.Setup(s => s.Telemetry).Returns(m_telemetry);
            m_serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(diagnosticsNodeManager.Object);
            m_serverMock.Setup(s => s.NodeManager).Returns(m_nodeManagerMock.Object);
            m_serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new Mock<IMonitoredItemQueueFactory>().Object);
            m_serverDiagnostics = new ServerDiagnosticsSummaryDataType();
            m_serverMock
                .Setup(s => s.UpdateServerDiagnostics(
                    It.IsAny<Action<ServerDiagnosticsSummaryDataType>>()))
                .Callback<Action<ServerDiagnosticsSummaryDataType>>(
                    update =>
                    {
                        lock (m_serverDiagnostics)
                        {
                            update(m_serverDiagnostics);
                        }
                    });

            var namespaceUris = new NamespaceTable();
            m_serverMock.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            m_serverMock.Setup(s => s.ServerUris).Returns(new StringTable());
            m_serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            m_serverMock.Setup(s => s.Factory).Returns(new Mock<IEncodeableFactory>().Object);
            m_serverMock.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(m_serverMock.Object));
        }

        /// <summary>
        /// A session closing right after the publish sweep claimed an expiry used to
        /// cancel the cleanup (it ran on the closing session's queue scope) and leak
        /// the subscription forever (M2-1).
        /// </summary>
        [Test]
        public async Task ExpiredSubscriptionIsDeletedWhenItsSessionClosesDuringCleanupAsync()
        {
            using SubscriptionManager manager = CreateManager(new ServerConfiguration());
            TestSession session = CreateSession();
            Subscription subscription = await CreateSubscriptionAsync(manager, session)
                .ConfigureAwait(false);

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deleted = new TaskCompletionSource<StatusCode>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_serverMock
                .Setup(s => s.DeleteSubscriptionAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns(async (uint id, CancellationToken ct) =>
                {
                    await gate.Task.ConfigureAwait(false);
                    try
                    {
                        deleted.TrySetResult(
                            await manager.DeleteSubscriptionAsync(null!, id, ct).ConfigureAwait(false));
                    }
                    catch (Exception e)
                    {
                        deleted.TrySetException(e);
                        throw;
                    }
                });

            SessionPublishQueue queue = GetPublishQueue(manager, session.Id);
            ExpireOnNextPublishTimer(subscription);
            queue.PublishTimerExpired(queue.CapturePublishTimerSnapshot());

            await manager.SessionClosingAsync(
                    new OperationContext(session.Mock.Object, DiagnosticsMasks.None),
                    session.Id,
                    deleteSubscriptions: true,
                    CancellationToken.None)
                .ConfigureAwait(false);
            gate.TrySetResult(true);

            StatusCode result = await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(StatusCodes.Good));
            Assert.That(manager.TryGetSubscription(subscription.Id, out _), Is.False);
            Assert.That(subscription.IsDeleted, Is.True);
        }

        /// <summary>
        /// One failing deletion must not drop the other expired subscriptions of the
        /// same cleanup batch (M2-1).
        /// </summary>
        [Test]
        public async Task ExpiryCleanupContinuesAfterAFailedDeletionAsync()
        {
            using SubscriptionManager manager = CreateManager(new ServerConfiguration());
            TestSession session = CreateSession();
            Subscription first = await CreateSubscriptionAsync(manager, session).ConfigureAwait(false);
            Subscription second = await CreateSubscriptionAsync(manager, session).ConfigureAwait(false);

            int calls = 0;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_serverMock
                .Setup(s => s.DeleteSubscriptionAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                .Returns(async (uint id, CancellationToken ct) =>
                {
                    if (Interlocked.Increment(ref calls) == 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                    }

                    await manager.DeleteSubscriptionAsync(null!, id, ct).ConfigureAwait(false);
                    done.TrySetResult(true);
                });

            SessionPublishQueue queue = GetPublishQueue(manager, session.Id);
            ExpireOnNextPublishTimer(first);
            ExpireOnNextPublishTimer(second);
            queue.PublishTimerExpired(queue.CapturePublishTimerSnapshot());

            await done.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(manager.GetSubscriptions(), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A restarted manager must still delete expired abandoned subscriptions (M2-8).
        /// </summary>
        [Test]
        public async Task RestartedManagerStillDeletesExpiredAbandonedSubscriptionsAsync()
        {
            using SubscriptionManager manager = CreateManager(new ServerConfiguration());
            await manager.StartupAsync().ConfigureAwait(false);
            await manager.ShutdownAsync().ConfigureAwait(false);
            await manager.StartupAsync().ConfigureAwait(false);
            try
            {
                TestSession session = CreateSession();
                Subscription subscription = await CreateSubscriptionAsync(manager, session)
                    .ConfigureAwait(false);
                var deleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_serverMock
                    .Setup(s => s.DeleteSubscriptionAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                    .Returns(async (uint id, CancellationToken ct) =>
                    {
                        await manager.DeleteSubscriptionAsync(null!, id, ct).ConfigureAwait(false);
                        deleted.TrySetResult(true);
                    });

                session.Closing = true;
                await manager.SessionClosingAsync(
                        new OperationContext(session.Mock.Object, DiagnosticsMasks.None),
                        session.Id,
                        deleteSubscriptions: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                ExpireOnNextPublishTimer(subscription);
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());

                await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(manager.TryGetSubscription(subscription.Id, out _), Is.False);
            }
            finally
            {
                await manager.ShutdownAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A durable subscription whose expiry was claimed but not deleted before
        /// shutdown must not be stored and resurrected on restart (M2-6).
        /// </summary>
        [Test]
        public async Task ExpiredDurableSubscriptionIsNotStoredAtShutdownAsync()
        {
            var clock = new FakeTimeProvider();
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory, clock);
            using (queueFactory)
            {
                server.SetupGet(value => value.IsRunning).Returns(false);
                server.SetupGet(value => value.DiagnosticsNodeManager)
                    .Returns(Mock.Of<IDiagnosticsNodeManager>());
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(Mock.Of<IConfigurationNodeManager>());
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(Mock.Of<ICoreNodeManager>());
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                var stored = new StoredSubscription
                {
                    Id = 41,
                    IsDurable = true,
                    PublishingInterval = 1_000,
                    MaxKeepaliveCount = 1,
                    MaxLifetimeCount = 3,
                    LifetimeCounter = 2,
                    MaxMessageCount = 10,
                    SequenceNumber = 1,
                    UserIdentityToken = new AnonymousIdentityToken(),
                    SentMessages = [],
                    MonitoredItems = []
                };
                var store = new Mock<ISubscriptionStore>();
                store.Setup(value => value.RestoreSubscriptionsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new RestoreSubscriptionResult(true, [stored]));
                server.SetupGet(value => value.SubscriptionStore).Returns(store.Object);
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        DurableSubscriptionsEnabled = true,
                        MaxSubscriptionCount = 10,
                        MinSubscriptionLifetime = 0,
                        MaxDurableSubscriptionLifetimeInHours = 24
                    }
                };
                using var master = new MasterNodeManager(server.Object, configuration, null, [], null);
                server.SetupGet(value => value.NodeManager).Returns(master);
                using var manager = new SubscriptionManager(server.Object, configuration, clock);

                // the deletion is lost (for instance the cleanup could not be scheduled)
                var cleanupRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                server.Setup(value => value.DeleteSubscriptionAsync(It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        cleanupRan.TrySetResult(true);
                        return default;
                    });

                await manager.RestoreSubscriptionsAsync().ConfigureAwait(false);
                ISubscription restored = manager.GetSubscriptions()[0];
                Assert.That(restored.IsDurable, Is.True);
                clock.Advance(TimeSpan.FromMilliseconds(1_001));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                clock.Advance(TimeSpan.FromMilliseconds(1_000));
                manager.ProcessAbandonedPublishTimers(manager.CaptureAbandonedPublishTimerSnapshot());
                await cleanupRan.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(manager.GetSubscriptions(), Has.Count.EqualTo(1));

                await manager.ShutdownAsync().ConfigureAwait(false);

                store.Verify(
                    value => value.StoreSubscriptionsAsync(
                        It.IsAny<IEnumerable<IStoredSubscription>>(),
                        It.IsAny<CancellationToken>()),
                    Times.Never);
            }
        }

        /// <summary>
        /// A CreateSubscription that passed the closing check before CloseSession
        /// marked the session closing must not re-create the publish and status
        /// queues of the closed session (M2-2).
        /// </summary>
        [Test]
        public async Task CreateSubscriptionRacingCloseSessionIsRejectedAsync()
        {
            using SubscriptionManager manager = CreateManager(new ServerConfiguration());
            TestSession session = CreateSession();
            SemaphoreSlim semaphore = GetPrivateField<SemaphoreSlim>(manager, "m_semaphoreSlim");

            await semaphore.WaitAsync().ConfigureAwait(false);
            Task<CreateSubscriptionResponse> creating;
            try
            {
                creating = StartCreateSubscription(manager, session);
                Assert.That(creating.IsCompleted, Is.False);
                session.Closing = true;
            }
            finally
            {
                semaphore.Release();
            }

            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await creating.ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(manager.GetSubscriptions(), Is.Empty);
            Assert.That(HasPublishQueue(manager, session.Id), Is.False);
            Assert.That(HasStatusQueue(manager, session.Id), Is.False);
            Assert.That(m_serverDiagnostics.CurrentSubscriptionCount, Is.Zero);
            Assert.That(session.Diagnostics.CurrentSubscriptionsCount, Is.Zero);
        }

        /// <summary>
        /// A TransferSubscriptions to a destination session that starts closing while
        /// the transfer waits for the manager must not attach the subscription to the
        /// closed session (M2-2).
        /// </summary>
        [Test]
        public async Task TransferToSessionClosedWhileWaitingIsRejectedAsync()
        {
            using SubscriptionManager manager = CreateManager(new ServerConfiguration());
            TestSession source = CreateSession();
            TestSession destination = CreateSession();
            Subscription subscription = await CreateSubscriptionAsync(manager, source).ConfigureAwait(false);
            SemaphoreSlim semaphore = GetPrivateField<SemaphoreSlim>(manager, "m_semaphoreSlim");

            await semaphore.WaitAsync().ConfigureAwait(false);
            Task<TransferSubscriptionsResponse> transferring;
            try
            {
                transferring = manager.TransferSubscriptionsAsync(
                    new OperationContext(destination.Mock.Object, DiagnosticsMasks.None),
                    [subscription.Id],
                    sendInitialValues: false).AsTask();
                Assert.That(transferring.IsCompleted, Is.False);
                destination.Closing = true;
            }
            finally
            {
                semaphore.Release();
            }

            TransferSubscriptionsResponse response = await transferring.ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(subscription.Session, Is.SameAs(source.Mock.Object));
            Assert.That(HasPublishQueue(manager, destination.Id), Is.False);
            Assert.That(HasStatusQueue(manager, destination.Id), Is.False);
        }

        /// <summary>
        /// Concurrent CreateSubscription requests must not exceed MaxSubscriptionCount (M2-7).
        /// </summary>
        [Test]
        public async Task ConcurrentCreateSubscriptionHonoursMaxSubscriptionCountAsync()
        {
            using SubscriptionManager manager = CreateManager(
                new ServerConfiguration { MaxSubscriptionCount = 1 });
            TestSession session = CreateSession();
            SemaphoreSlim semaphore = GetPrivateField<SemaphoreSlim>(manager, "m_semaphoreSlim");

            await semaphore.WaitAsync().ConfigureAwait(false);
            var creating = new List<Task<CreateSubscriptionResponse>>();
            try
            {
                for (int ii = 0; ii < 3; ii++)
                {
                    creating.Add(StartCreateSubscription(manager, session));
                }
            }
            finally
            {
                semaphore.Release();
            }

            int succeeded = 0;
            int rejected = 0;
            foreach (Task<CreateSubscriptionResponse> task in creating)
            {
                try
                {
                    await task.ConfigureAwait(false);
                    succeeded++;
                }
                catch (ServiceResultException e) when (e.StatusCode == StatusCodes.BadTooManySubscriptions)
                {
                    rejected++;
                }
            }

            Assert.That(succeeded, Is.EqualTo(1));
            Assert.That(rejected, Is.EqualTo(2));
            Assert.That(manager.GetSubscriptions(), Has.Count.EqualTo(1));
            Assert.That(m_serverDiagnostics.CurrentSubscriptionCount, Is.EqualTo(1));
        }

        private static bool HasPublishQueue(SubscriptionManager manager, NodeId sessionId)
        {
            return GetPrivateField<System.Collections.IDictionary>(manager, "m_publishQueues").Contains(sessionId);
        }

        private static bool HasStatusQueue(SubscriptionManager manager, NodeId sessionId)
        {
            return GetPrivateField<System.Collections.IDictionary>(manager, "m_statusMessages").Contains(sessionId);
        }

        private SubscriptionManager CreateManager(ServerConfiguration serverConfiguration)
        {
            var manager = new SubscriptionManager(
                m_serverMock.Object,
                new ApplicationConfiguration { ServerConfiguration = serverConfiguration });
            m_serverMock.SetupGet(server => server.SubscriptionManager).Returns(manager);
            return manager;
        }

        private static TestSession CreateSession(
            string applicationUri = "urn:localhost:opcfoundation.org:SubscriptionManagerRegressionTests",
            UserIdentity identity = null)
        {
            identity ??= new UserIdentity("manager-user", [1, 2, 3]);
            var result = new TestSession
            {
                Id = new NodeId(Guid.NewGuid()),
                Diagnostics = new SessionDiagnosticsDataType
                {
                    ClientDescription = new ApplicationDescription { ApplicationUri = applicationUri }
                },
                Mock = new Mock<ISession>()
            };
            result.Mock.SetupGet(s => s.Id).Returns(result.Id);
            result.Mock.SetupGet(s => s.Identity).Returns(identity);
            result.Mock.SetupGet(s => s.EffectiveIdentity).Returns(identity);
            result.Mock.SetupGet(s => s.IdentityToken).Returns(identity.TokenHandler);
            result.Mock.SetupGet(s => s.IsClosing).Returns(() => result.Closing);
            result.Mock.SetupGet(s => s.ClientApplicationUri)
                .Returns(() => result.Diagnostics.ClientDescription?.ApplicationUri);
            result.Mock
                .Setup(s => s.UpdateDiagnostics(It.IsAny<Action<SessionDiagnosticsDataType>>()))
                .Callback<Action<SessionDiagnosticsDataType>>(update =>
                {
                    lock (result.Diagnostics)
                    {
                        update(result.Diagnostics);
                    }
                });
            return result;
        }

        private static async Task<Subscription> CreateSubscriptionAsync(
            SubscriptionManager manager,
            TestSession session)
        {
            CreateSubscriptionResponse created = await StartCreateSubscription(manager, session)
                .ConfigureAwait(false);
            Assert.That(manager.TryGetSubscription(created.SubscriptionId, out ISubscription subscription), Is.True);
            return (Subscription)subscription;
        }

        private static Task<CreateSubscriptionResponse> StartCreateSubscription(
            SubscriptionManager manager,
            TestSession session)
        {
            return manager.CreateSubscriptionAsync(
                new OperationContext(session.Mock.Object, DiagnosticsMasks.None),
                requestedPublishingInterval: 1000,
                requestedLifetimeCount: 30,
                requestedMaxKeepAliveCount: 10,
                maxNotificationsPerPublish: 0,
                publishingEnabled: true,
                priority: 0).AsTask();
        }

        private static SessionPublishQueue GetPublishQueue(SubscriptionManager manager, NodeId sessionId)
        {
            return GetPrivateField<NodeIdDictionary<SessionPublishQueue>>(manager, "m_publishQueues")[sessionId];
        }

        private static void ExpireOnNextPublishTimer(Subscription subscription)
        {
            uint maxLifetimeCount = GetPrivateField<uint>(subscription, "m_maxLifetimeCount");
            SetPrivateField(subscription, "m_lifetimeCounter", maxLifetimeCount - 1);
            SetPrivateField(subscription, "m_waitingForPublish", true);
            SetPrivateField(
                subscription,
                "m_publishTimerExpiry",
                TimeProvider.System.GetTimestampMilliseconds() - 100);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field {fieldName} not found.");
            return (T)field.GetValue(instance);
        }

        private static void SetPrivateField<T>(object instance, string fieldName, T value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Field {fieldName} not found.");
            field.SetValue(instance, value);
        }

        private sealed class TestSession
        {
            public NodeId Id { get; set; }
            public Mock<ISession> Mock { get; set; }
            public SessionDiagnosticsDataType Diagnostics { get; set; }
            public bool Closing { get; set; }
        }
    }
}
