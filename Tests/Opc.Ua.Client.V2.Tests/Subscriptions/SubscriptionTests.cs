// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Nito.AsyncEx;
using Opc.Ua.Client.Services;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions
{
    [TestFixture]
    public sealed class SubscriptionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_mockSubscriptionServices = new Mock<ISubscriptionServiceSet>();
            m_mockMonitoredItemServices = new Mock<IMonitoredItemServiceSet>();
            m_mockMethodServices = new Mock<IMethodServiceSet>();
            m_mockSubscriptionServices = new Mock<ISubscriptionServiceSet>();
            m_mockSession = new Mock<ISubscriptionContext>();
            m_mockSession
                .Setup(m_mockSession => m_mockSession.SubscriptionServiceSet)
                .Returns(m_mockSubscriptionServices.Object);
            m_mockSession
                .Setup(m_mockSession => m_mockSession.MethodServiceSet)
                .Returns(m_mockMethodServices.Object);
            m_mockSession
                .Setup(m_mockSession => m_mockSession.MonitoredItemServiceSet)
                .Returns(m_mockMonitoredItemServices.Object);
            m_mockCompletion = new Mock<IMessageAckQueue>();
            _options = OptionsFactory.Create<SubscriptionOptions>();

            m_mockTimeProvider = new Mock<TimeProvider>();
            m_mockTimer = new Mock<ITimer>();
            m_mockTimeProvider
                .Setup(t => t.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Returns(m_mockTimer.Object);
            m_mockObservability = new Mock<ITelemetryContext>();
            m_mockObservability
                .Setup(o => o.TimeProvider).Returns(m_mockTimeProvider.Object);
            m_mockLogger = new Mock<ILogger<Subscription>>();
            m_mockObservability
                .Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(m_mockLogger.Object);
            m_mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
        }

        [Test]
        public void AddMonitoredItemShouldAddItemToMonitoredItems()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);

            // Act
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(sut.MonitoredItems.Items, Does.Contain(monitoredItem));
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallCreateAsyncIfNotCreatedAsync()
        {
            // Arrange
            var publishingInterval = TimeSpan.FromSeconds(100);
            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    publishingInterval.TotalMilliseconds, 21, 7, 10, true,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);

            _options.Configure(o => o with { Disabled = true });
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);
            Assert.That(sut.Created, Is.False);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.Zero));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(0));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(0));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(0));
            Assert.That(sut.CurrentPriority, Is.EqualTo(0));

            // Act
            sut.SubscriptionStateChanged.Reset();
            _options.Configure(o => o with
            {
                Disabled = false,
                PublishingEnabled = true,
                PublishingInterval = publishingInterval,
                KeepAliveCount = 7,
                LifetimeCount = 15,
                Priority = 3,
                MaxNotificationsPerPublish = 10
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
            Assert.That(sut.CurrentPriority, Is.EqualTo(3));
            Assert.That(sut.Id, Is.EqualTo(22));
            m_mockSession.Verify();
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallModifyAsyncIfCreatedAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            m_mockSubscriptionServices
                .Setup(s => s.ModifySubscriptionAsync(It.IsAny<RequestHeader>(),
                    22, publishingInterval.TotalMilliseconds, 30, 10, 0,
                    4, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifySubscriptionResponse
                {
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);
            m_mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);

            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingEnabled, Is.False);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(publishingInterval));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(7));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(21));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
            Assert.That(sut.CurrentPriority, Is.EqualTo(3));

            // Act
            sut.SubscriptionStateChanged.Reset();
            _options.Configure(o => o with
            {
                Disabled = false,
                PublishingInterval = publishingInterval,
                PublishingEnabled = true,
                Priority = 4
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(0));
            Assert.That(sut.CurrentPriority, Is.EqualTo(4));
            Assert.That(sut.CurrentPublishingEnabled, Is.True);
            m_mockSession.Verify();
        }

        [Test]
        public async Task ChangeSubscriptionOptionsShouldCallSetPublishingModeIfItIsTheOnlyChangeAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            m_mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);

            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingEnabled, Is.False);

            // Act
            sut.SubscriptionStateChanged.Reset();
            _options.Configure(o => o with
            {
                Disabled = false,
                PublishingEnabled = true,
                PublishingInterval = publishingInterval,
                KeepAliveCount = 7,
                LifetimeCount = 15,
                Priority = 3,
                MaxNotificationsPerPublish = 10
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(sut.CurrentPublishingEnabled, Is.True);
            m_mockSession.Verify();
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsShouldAddCreatedItemsAsync()
        {
            // Arrange
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 100,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
              m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            sut.SubscriptionStateChanged.Reset();
            var success = sut.MonitoredItems.TryAdd("Test", OptionsFactory.Create(new MonitoredItemOptions
            {
                StartNodeId = NodeId.Parse("ns=2;s=Demo")
            }), out var monitoredItem);
            Assert.That(success, Is.True);

            // Act
            await sut.SubscriptionStateChanged.WaitAsync();

            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ServerId, Is.EqualTo(100));
            Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(10));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            m_mockSession.Verify();
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsShouldChangeSubscriptionAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
              m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));

            m_mockMonitoredItemServices
                .Setup(s => s.ModifyMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModifyMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            RevisedSamplingInterval = 100000,
                            RevisedQueueSize = 1000
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            sut.SubscriptionStateChanged.Reset();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=Demo"),
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = TimeSpan.FromSeconds(555),
                QueueSize = 3333,
                DiscardOldest = true
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(100)));
            Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(1000));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
            m_mockSession.Verify();
        }

        [Test]
        public async Task ChangeMonitoredItemOptionsNameShouldDeleteAndRecreateMonitoredItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
              m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));

            m_mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.Is<ArrayOf<MonitoredItemCreateRequest>>(r => r.Count == 1
                        && r[0].ItemToMonitor.NodeId.ToString().Contains("NewDemo")),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 400,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);
            // Act
            sut.SubscriptionStateChanged.Reset();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=3;s=NewDemo"), // Changed
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = TimeSpan.FromSeconds(555),
                QueueSize = 3333,
                DiscardOldest = true
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            Assert.That(monitoredItem.CurrentSamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(monitoredItem.CurrentQueueSize, Is.EqualTo(10));
            Assert.That(monitoredItem.ServerId, Is.EqualTo(400));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            m_mockSession.Verify();
        }

        [Test]
        public async Task UpdatingMonitoringModeOnlyShouldCallSetMonitoringModeAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ServerId, Is.EqualTo(monitoredItem.ClientHandle));
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Sampling));
            // Now we have a monitored item in sampling mode

            // Only set monitoring mode should be called for the item
            m_mockMonitoredItemServices
                .Setup(s => s.SetMonitoringModeAsync(It.IsAny<RequestHeader>(),
                    sut.Id, MonitoringMode.Reporting,
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetMonitoringModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            // Act
            sut.SubscriptionStateChanged.Reset();
            options.Configure(o => TestMonitoredItem.CreatedOptions with
            {
                MonitoringMode = MonitoringMode.Reporting
            });
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(monitoredItem.CurrentMonitoringMode, Is.EqualTo(MonitoringMode.Reporting));
            m_mockSession.Verify();
        }

        [Test]
        public async Task RemovingMonitoredItemShouldRemoveRemovedItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
              m_mockCompletion.Object, _options, m_mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);
            Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
            Assert.That(monitoredItem, Is.Not.Null);
            // Now we got an item that is created

            // Only delete monitored item should be called
            m_mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            // Act
            sut.SubscriptionStateChanged.Reset();

            success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(success, Is.True);
            Assert.That(sut.MonitoredItems.Count, Is.EqualTo(0));
            m_mockSession.Verify();
        }

        [Test]
        public async Task RemovingMonitoredItemShouldTryAgainIfDeleteFailsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
              m_mockCompletion.Object, _options, m_mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);
            Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
            // Now we got an item that is created

            // Only delete monitored item should be called
            m_mockMonitoredItemServices
                .SetupSequence(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(),
                    2, It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == monitoredItem.ServerId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    ResponseHeader = new ResponseHeader
                    {
                        ServiceResult = StatusCodes.Bad
                    },
                    Results = []
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Bad]
                })
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                ;

            // Act
            sut.SubscriptionStateChanged.Reset();
            success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            Assert.That(success, Is.True);
            Assert.That(sut.MonitoredItems.Count, Is.EqualTo(0));
            m_mockSession.Verify();
        }

        [Test]
        public async Task ConditionRefreshAsyncShouldCallSessionCallAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            // Assert
            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            await sut.ConditionRefreshAsync(default);

            // Assert
            m_mockSession.Verify();
        }

        [Test]
        public async Task ConditionRefreshAsyncThrowsIfNotYetCreatedAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);

            // Act
            Func<Task> act = async () => await sut.ConditionRefreshAsync(CancellationToken.None);

            // Assert
            var ex = Assert.ThrowsAsync<ServiceResultException>(async () => await act());
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            m_mockSession.Verify();
        }

        [Test]
        public async Task DeleteAsyncShouldCallSessionDeleteSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);

            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DeleteAsync(default);

            // Assert
            m_mockSession.Verify();
        }

        [Test]
        public async Task DisposeAsyncShouldCallSessionDeleteSubscriptionsAndCleanupMonitoredItemsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);

            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync();

            // Assert
            m_mockSession.Verify();
            Assert.That(sut.MonitoredItems.Items, Is.Empty);
        }

        [Test]
        public async Task DisableShouldCallSessionDeleteSubscriptionsButNotMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            sut.SubscriptionStateChanged.Reset();
            _options.Configure(o => o with { Disabled = true });
            await sut.SubscriptionStateChanged.WaitAsync();

            // Assert
            m_mockSession.Verify();
            Assert.That(sut.MonitoredItems.Items, Is.Not.Empty);
        }

        [Test]
        public async Task DeleteAsyncShouldCatchAllExceptionsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 22);

            m_mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 22u),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Bad]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DeleteAsync(default);

            // Assert
            m_mockSession.Verify();
        }

        [Test]
        public async Task DisposeAsyncShouldDisposePublishTimerAsync()
        {
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);
            // Act
            await sut.DisposeAsync();

            // Assert
            m_mockTimer.Verify(t => t.Dispose(), Times.Once);
        }

        [Test]
        public void FindItemByClientHandleShouldReturnMonitoredItem()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);

            // Act
            success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(
                monitoredItem.ClientHandle, out var result);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(result, Is.EqualTo(monitoredItem));
        }

        [Test]
        public void FindItemByClientHandleShouldReturnNullIfNotFound()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out _);
            Assert.That(success, Is.True);

            // Act
            success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(55,
                out var result);

            // Assert
            Assert.That(success, Is.False);
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task OnPublishReceivedAsyncShouldResetKeepAliveTimerAsync()
        {
            // Arrange
            var message = new NotificationMessage();

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);

            // Act
            await sut.OnPublishReceivedAsync(message, null, null!);

            // Assert
            m_mockTimer.Verify(t => t.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Test]
        public async Task RecreateAsyncShouldReCreateSubscriptionAndMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 10);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);

            // We have a running subscription with one monitored item.

            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 200,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);

            Assert.That(sut.Created, Is.True);

            // Act
            await sut.RecreateAsync(default);

            // Assert
            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
            Assert.That(sut.CurrentPriority, Is.EqualTo(3));
            Assert.That(sut.Id, Is.EqualTo(22));
            Assert.That(monitoredItem.ServerId, Is.EqualTo(200));
            m_mockSession.Verify();
        }

        [Test]
        public async Task RecreateAsyncShouldReCreateSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 10);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);

            m_mockSubscriptionServices
                .Setup(s => s.CreateSubscriptionAsync(It.IsAny<RequestHeader>(),
                    TimeSpan.FromSeconds(100).TotalMilliseconds, 21, 7, 10, false,
                    3, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateSubscriptionResponse
                {
                    SubscriptionId = 22,
                    RevisedLifetimeCount = 10,
                    RevisedMaxKeepAliveCount = 5,
                    RevisedPublishingInterval = 10000
                })
                .Verifiable(Times.Once);

            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 200,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);
            Assert.That(sut.Created, Is.True);

            // Act
            await sut.RecreateAsync(default);

            // Assert
            Assert.That(sut.Created, Is.True);
            Assert.That(sut.CurrentPublishingInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(sut.CurrentKeepAliveCount, Is.EqualTo(5));
            Assert.That(sut.CurrentLifetimeCount, Is.EqualTo(10));
            Assert.That(sut.CurrentMaxNotificationsPerPublish, Is.EqualTo(10));
            Assert.That(sut.CurrentPriority, Is.EqualTo(3));
            Assert.That(sut.Id, Is.EqualTo(22));
            Assert.That(sut.MonitoredItems.Count, Is.EqualTo(1));
            Assert.That(monitoredItem.ServerId, Is.EqualTo(200));
            m_mockSession.Verify();
        }

        [Test]
        public void RemoveItemShouldRemoveItemFromMonitoredItems()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);

            // Act
            success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(sut.MonitoredItems.Items, Does.Not.Contain(monitoredItem));
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong1Async()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([1990u, 2200u, 3300u]), // serverHandles
                                new Variant([22222u])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], CancellationToken.None);

            // Assert
            Assert.That(success, Is.False);
            m_mockSession.Verify();
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong2Async()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(success, Is.True);

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant(["string"]), // serverHandles
                                new Variant([22u])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], CancellationToken.None);

            // Assert
            Assert.That(success, Is.False);
            m_mockSession.Verify();
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndCreateServerItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([19900u]), // serverHandles
                                new Variant([monitoredItem.ClientHandle])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            m_mockSession.Verify();
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.ServerId, Is.EqualTo(19900));
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDoNothingIfStateIsCorrectAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);
            var serverId = monitoredItem.ServerId;

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([monitoredItem.ServerId]), // serverHandles
                                new Variant([monitoredItem.ClientHandle])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            m_mockSession.Verify();
            Assert.That(monitoredItem.ServerId, Is.EqualTo(serverId));
            Assert.That(success, Is.True);
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDeleteItemIfNotInSubscriptionAsync()
        {
            // Arrange

            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([19900u]), // serverHandles
                                new Variant([300u])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            m_mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 19900u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            // Act
            var success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            m_mockSession.Verify();
            Assert.That(success, Is.True);
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndReturnFalseIfFailingAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.Created, Is.True);

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Bad,
                            OutputArguments = []
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            m_mockSession.Verify();
            Assert.That(success, Is.False);
            Assert.That(monitoredItem.Created, Is.False);
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldAssignServerIdToMonitoredItemWithClientIdAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.Created, Is.True);
            var clientId = monitoredItem.ClientHandle;
            var serverId = monitoredItem.ServerId;

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([serverId]), // serverHandles
                                new Variant([19900u])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            m_mockSession.Verify();
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.ClientHandle, Is.EqualTo(19900));
            Assert.That(monitoredItem.ServerId, Is.EqualTo(serverId));
        }

        [Test]
        public async Task TryCompleteTransferAsyncShouldCreateWhatIsMissingOnServerAsync()
        {
            // Arrange
            var sut = new TestSubscription(m_mockSession.Object, m_mockNotificationDataHandler.Object,
                m_mockCompletion.Object, _options, m_mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.Created, Is.True);
            var clientId = monitoredItem.ClientHandle;
            var serverId = monitoredItem.ServerId;

            m_mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].AsBoxedObject().Equals(2u)
                        && r[0].ObjectId == ObjectIds.Server
                        && r[0].MethodId == MethodIds.Server_GetMonitoredItems), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            OutputArguments =
                            [
                                new Variant([30000u]), // serverHandles
                                new Variant([19900u])  // clientHandles
                            ]
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Delete monitored item should be called
            m_mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    It.Is<ArrayOf<uint>>(a => a.Count == 1 && a[0] == 30000u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            m_mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<MonitoredItemCreateRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CreateMonitoredItemsResponse
                {
                    Results =
                    [
                        new ()
                        {
                            StatusCode = StatusCodes.Good,
                            MonitoredItemId = 44444,
                            RevisedSamplingInterval = 10000,
                            RevisedQueueSize = 10
                        }
                    ]
                })
                .Verifiable(Times.Once);

            // Act
            success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            Assert.That(success, Is.True);
            Assert.That(monitoredItem.Created, Is.True);
            Assert.That(monitoredItem.ClientHandle, Is.EqualTo(clientId));
            Assert.That(monitoredItem.ServerId, Is.EqualTo(44444));

            m_mockSession.Verify();
            m_mockMonitoredItemServices.Verify();
        }

        private sealed class TestMonitoredItem : MonitoredItem
        {
            public static MonitoredItemOptions CreatedOptions => new()
            {
                StartNodeId = NodeId.Parse("ns=2;s=Demo"),
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = TimeSpan.FromSeconds(1),
                QueueSize = 5,
                TimestampsToReturn = TimestampsToReturn.Both,
                AttributeId = Attributes.Value,
                DiscardOldest = true
            };

            public TestMonitoredItem(IMonitoredItemContext subscription, string name,
                Opc.Ua.OptionsMonitor<MonitoredItemOptions> options, ILogger logger)
                : base(subscription, name, options, logger)
            {
                if (options.CurrentValue.StartNodeId.IsNull)
                {
                    // Auto create
                    options.Configure(o => CreatedOptions);
                    Assert.That(TryGetPendingChange(out var change), Is.True);
                    Assert.That(change, Is.Not.Null);
                    change.SetCreateResult(new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            AttributeId = Options.CurrentValue.AttributeId,
                            NodeId = Options.CurrentValue.StartNodeId
                        },
                        MonitoringMode = MonitoringMode.Sampling,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = ClientHandle,
                            SamplingInterval = Options.CurrentValue.SamplingInterval.TotalMilliseconds,
                            QueueSize = Options.CurrentValue.QueueSize,
                            DiscardOldest = Options.CurrentValue.DiscardOldest
                        }
                    },
                    new MonitoredItemCreateResult
                    {
                        StatusCode = StatusCodes.Good,
                        MonitoredItemId = ClientHandle,
                        RevisedSamplingInterval = 10000,
                        RevisedQueueSize = 10
                    }, 0, [], new ResponseHeader());
                }
            }
        }

        private sealed class TestSubscription : Subscription
        {
            public static SubscriptionOptions SubscriptionOptions => new()
            {
                Disabled = false,
                PublishingEnabled = false,
                PublishingInterval = TimeSpan.FromSeconds(100),
                KeepAliveCount = 7,
                LifetimeCount = 15,
                Priority = 3,
                MaxNotificationsPerPublish = 10
            };

            public TestSubscription(ISubscriptionContext session, ISubscriptionNotificationHandler handler,
                IMessageAckQueue completion, Opc.Ua.OptionsMonitor<SubscriptionOptions> options,
                ITelemetryContext telemetry, uint? subscriptionIdForAlreadyCreatedState = null)
                : base(session, handler, completion, !subscriptionIdForAlreadyCreatedState.HasValue ?
                      options : options.Configure(o => o with { Disabled = true }), telemetry)
            {
                // Let the subscription create itself
                if (subscriptionIdForAlreadyCreatedState.HasValue)
                {
                    Options = SubscriptionOptions;
                    OnSubscriptionUpdateComplete(true, subscriptionIdForAlreadyCreatedState.Value,
                        TimeSpan.FromSeconds(100), 7, 21, 3, 10, false);
                    // Now we have a created subscription
                }
            }

            public SemaphoreSlim Block { get; } = new(0, 1);
            public AsyncManualResetEvent SubscriptionStateChanged { get; } = new();

            public async ValueTask WaitAsync()
            {
                await Block.WaitAsync();
                Block.Release();
            }

            protected override void OnSubscriptionStateChanged(SubscriptionState state)
            {
                if (state != SubscriptionState.Opened)
                {
                    SubscriptionStateChanged.Set();
                }
                base.OnSubscriptionStateChanged(state);
            }

            protected override MonitoredItem CreateMonitoredItem(string name,
                IOptionsMonitor<MonitoredItemOptions> options, IMonitoredItemContext context,
                ITelemetryContext telemetry)
            {
                return new TestMonitoredItem(context, name,
                    (Opc.Ua.OptionsMonitor<MonitoredItemOptions>)options, new Mock<ILogger>().Object);
            }

            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                return WaitAsync();
            }
        }

        private Mock<IMessageAckQueue> m_mockCompletion;
        private Opc.Ua.OptionsMonitor<SubscriptionOptions> _options;
        private Mock<ITelemetryContext> m_mockObservability;
        private Mock<TimeProvider> m_mockTimeProvider;
        private Mock<ISubscriptionServiceSet> m_mockSubscriptionServices;
        private Mock<IMonitoredItemServiceSet> m_mockMonitoredItemServices;
        private Mock<IMethodServiceSet> m_mockMethodServices;
        private Mock<ISubscriptionContext> m_mockSession;
        private Mock<ISubscriptionNotificationHandler> m_mockNotificationDataHandler;
        private Mock<ITimer> m_mockTimer;
        private Mock<ILogger<Subscription>> m_mockLogger;
    }
}
