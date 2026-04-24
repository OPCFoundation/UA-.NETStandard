// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Moq;
    using Nito.AsyncEx;
    using Opc.Ua.Client.Services;
    using Opc.Ua.Client.Subscriptions.MonitoredItems;
    using Xunit;

    public sealed class SubscriptionTests
    {
        public SubscriptionTests()
        {
            _mockSubscriptionServices = new Mock<ISubscriptionServiceSet>();
            _mockMonitoredItemServices = new Mock<IMonitoredItemServiceSet>();
            _mockMethodServices = new Mock<IMethodServiceSet>();
            _mockSubscriptionServices = new Mock<ISubscriptionServiceSet>();
            _mockSession = new Mock<ISubscriptionContext>();
            _mockSession
                .Setup(_mockSession => _mockSession.SubscriptionServiceSet)
                .Returns(_mockSubscriptionServices.Object);
            _mockSession
                .Setup(_mockSession => _mockSession.MethodServiceSet)
                .Returns(_mockMethodServices.Object);
            _mockSession
                .Setup(_mockSession => _mockSession.MonitoredItemServiceSet)
                .Returns(_mockMonitoredItemServices.Object);
            _mockCompletion = new Mock<IMessageAckQueue>();
            _options = OptionsFactory.Create<SubscriptionOptions>();

            _mockTimeProvider = new Mock<TimeProvider>();
            _mockTimer = new Mock<ITimer>();
            _mockTimeProvider
                .Setup(t => t.CreateTimer(
                    It.IsAny<TimerCallback>(),
                    It.IsAny<object>(),
                    It.IsAny<TimeSpan>(),
                    It.IsAny<TimeSpan>()))
                .Returns(_mockTimer.Object);
            _mockObservability = new Mock<ITelemetryContext>();
            _mockObservability
                .Setup(o => o.TimeProvider).Returns(_mockTimeProvider.Object);
            _mockLogger = new Mock<ILogger<Subscription>>();
            _mockObservability
                .Setup(o => o.LoggerFactory.CreateLogger(It.IsAny<string>()))
                .Returns(_mockLogger.Object);
            _mockNotificationDataHandler = new Mock<ISubscriptionNotificationHandler>();
        }

        [Fact]
        public void AddMonitoredItemShouldAddItemToMonitoredItems()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);

            // Act
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);

            // Assert
            success.Should().BeTrue();
            monitoredItem.Should().NotBeNull();
            Assert.NotNull(monitoredItem);
            sut.MonitoredItems.Items.Should().Contain(monitoredItem);
        }

        [Fact]
        public async Task ChangeSubscriptionOptionsShouldCallCreateAsyncIfNotCreatedAsync()
        {
            // Arrange
            var publishingInterval = TimeSpan.FromSeconds(100);
            _mockSubscriptionServices
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
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);
            sut.Created.Should().BeFalse();
            sut.CurrentPublishingInterval.Should().Be(TimeSpan.Zero);
            sut.CurrentKeepAliveCount.Should().Be(0);
            sut.CurrentLifetimeCount.Should().Be(0);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(0);
            sut.CurrentPriority.Should().Be(0);

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
            sut.Created.Should().BeTrue();
            sut.CurrentPublishingInterval.Should().Be(TimeSpan.FromSeconds(10));
            sut.CurrentKeepAliveCount.Should().Be(5);
            sut.CurrentLifetimeCount.Should().Be(10);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(10);
            sut.CurrentPriority.Should().Be(3);
            sut.Id.Should().Be(22);
            _mockSession.Verify();
        }

        [Fact]
        public async Task ChangeSubscriptionOptionsShouldCallModifyAsyncIfCreatedAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            _mockSubscriptionServices
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
            _mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, new UInt32Collection { 22 }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);

            sut.Created.Should().BeTrue();
            sut.CurrentPublishingEnabled.Should().BeFalse();
            sut.CurrentPublishingInterval.Should().Be(publishingInterval);
            sut.CurrentKeepAliveCount.Should().Be(7);
            sut.CurrentLifetimeCount.Should().Be(21);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(10);
            sut.CurrentPriority.Should().Be(3);

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
            sut.Created.Should().BeTrue();
            sut.CurrentPublishingInterval.Should().Be(TimeSpan.FromSeconds(10));
            sut.CurrentKeepAliveCount.Should().Be(5);
            sut.CurrentLifetimeCount.Should().Be(10);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(0);
            sut.CurrentPriority.Should().Be(4);
            sut.CurrentPublishingEnabled.Should().BeTrue();
            _mockSession.Verify();
        }

        [Fact]
        public async Task ChangeSubscriptionOptionsShouldCallSetPublishingModeIfItIsTheOnlyChangeAsync()
        {
            // Arrange

            var publishingInterval = TimeSpan.FromSeconds(100);

            _mockSubscriptionServices
                .Setup(s => s.SetPublishingModeAsync(It.IsAny<RequestHeader>(),
                    true, new UInt32Collection { 22 }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SetPublishingModeResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);

            sut.Created.Should().BeTrue();
            sut.CurrentPublishingEnabled.Should().BeFalse();

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
            sut.CurrentPublishingEnabled.Should().BeTrue();
            _mockSession.Verify();
        }

        [Fact]
        public async Task ChangeMonitoredItemOptionsShouldAddCreatedItemsAsync()
        {
            // Arrange
            _mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<MonitoredItemCreateRequestCollection>(), It.IsAny<CancellationToken>()))
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

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
              _mockCompletion.Object, _options, _mockObservability.Object, 2);

            sut.SubscriptionStateChanged.Reset();
            var success = sut.MonitoredItems.TryAdd("Test", OptionsFactory.Create(new MonitoredItemOptions
            {
                StartNodeId = NodeId.Parse("ns=2;s=Demo")
            }), out var monitoredItem);
            success.Should().BeTrue();

            // Act
            await sut.SubscriptionStateChanged.WaitAsync();

            Assert.NotNull(monitoredItem);
            monitoredItem.ServerId.Should().Be(100);
            monitoredItem.CurrentSamplingInterval.Should().Be(TimeSpan.FromSeconds(10));
            monitoredItem.CurrentQueueSize.Should().Be(10);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Reporting);
            _mockSession.Verify();
        }

        [Fact]
        public async Task ChangeMonitoredItemOptionsShouldChangeSubscriptionAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
              _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            Assert.NotNull(monitoredItem);
            monitoredItem.ServerId.Should().Be(monitoredItem.ClientHandle);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);

            _mockMonitoredItemServices
                .Setup(s => s.ModifyMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<MonitoredItemModifyRequestCollection>(),
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

            monitoredItem.CurrentSamplingInterval.Should().Be(TimeSpan.FromSeconds(100));
            monitoredItem.CurrentQueueSize.Should().Be(1000);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);
            _mockSession.Verify();
        }

        [Fact]
        public async Task ChangeMonitoredItemOptionsNameShouldDeleteAndRecreateMonitoredItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
              _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            Assert.NotNull(monitoredItem);
            monitoredItem.ServerId.Should().Be(monitoredItem.ClientHandle);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);

            _mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    new UInt32Collection { monitoredItem.ServerId }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            _mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.Is<MonitoredItemCreateRequestCollection>(r => r.Count == 1
                        && r[0].ItemToMonitor.NodeId.Identifier.Equals("NewDemo")),
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

            monitoredItem.CurrentSamplingInterval.Should().Be(TimeSpan.FromSeconds(10));
            monitoredItem.CurrentQueueSize.Should().Be(10);
            monitoredItem.ServerId.Should().Be(400);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Reporting);
            _mockSession.Verify();
        }

        [Fact]
        public async Task UpdatingMonitoringModeOnlyShouldCallSetMonitoringModeAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            Assert.NotNull(monitoredItem);
            monitoredItem.ServerId.Should().Be(monitoredItem.ClientHandle);
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);
            // Now we have a monitored item in sampling mode

            // Only set monitoring mode should be called for the item
            _mockMonitoredItemServices
                .Setup(s => s.SetMonitoringModeAsync(It.IsAny<RequestHeader>(),
                    sut.Id, MonitoringMode.Reporting,
                    new UInt32Collection { monitoredItem.ServerId },
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
            monitoredItem.CurrentMonitoringMode.Should().Be(MonitoringMode.Reporting);
            _mockSession.Verify();
        }

        [Fact]
        public async Task RemovingMonitoredItemShouldRemoveRemovedItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
              _mockCompletion.Object, _options, _mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();
            sut.MonitoredItems.Count.Should().Be(1);
            Assert.NotNull(monitoredItem);
            // Now we got an item that is created

            // Only delete monitored item should be called
            _mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    new UInt32Collection { monitoredItem.ServerId },
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
            success.Should().BeTrue();
            sut.MonitoredItems.Count.Should().Be(0);
            _mockSession.Verify();
        }

        [Fact]
        public async Task RemovingMonitoredItemShouldTryAgainIfDeleteFailsAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
              _mockCompletion.Object, _options, _mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();
            sut.MonitoredItems.Count.Should().Be(1);
            // Now we got an item that is created

            // Only delete monitored item should be called
            _mockMonitoredItemServices
                .SetupSequence(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(),
                    2, new UInt32Collection { monitoredItem.ServerId },
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
            success.Should().BeTrue();
            sut.MonitoredItems.Count.Should().Be(0);
            _mockSession.Verify();
        }

        [Fact]
        public async Task ConditionRefreshAsyncShouldCallSessionCallAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            // Assert
            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<CallMethodRequestCollection>(), It.IsAny<CancellationToken>()))
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
            _mockSession.Verify();
        }

        [Fact]
        public async Task ConditionRefreshAsyncThrowsIfNotYetCreatedAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);

            // Act
            Func<Task> act = async () => await sut.ConditionRefreshAsync(CancellationToken.None);

            // Assert
            (await act.Should().ThrowAsync<ServiceResultException>())
                .Which.StatusCode.Should().Be(StatusCodes.BadSubscriptionIdInvalid);
            _mockSession.Verify();
        }

        [Fact]
        public async Task DeleteAsyncShouldCallSessionDeleteSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);

            _mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), new UInt32Collection { 22 }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DeleteAsync(default);

            // Assert
            _mockSession.Verify();
        }

        [Fact]
        public async Task DisposeAsyncShouldCallSessionDeleteSubscriptionsAndCleanupMonitoredItemsAsync()
        {
            // Arrange

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);

            _mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), new UInt32Collection { 22 }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Good]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DisposeAsync();

            // Assert
            _mockSession.Verify();
            sut.MonitoredItems.Items.Should().BeEmpty();
        }

        [Fact]
        public async Task DisableShouldCallSessionDeleteSubscriptionsButNotMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            _mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), new UInt32Collection { 22 },
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
            _mockSession.Verify();
            sut.MonitoredItems.Items.Should().NotBeEmpty();
        }

        [Fact]
        public async Task DeleteAsyncShouldCatchAllExceptionsAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 22);

            _mockSubscriptionServices
                .Setup(s => s.DeleteSubscriptionsAsync(
                    It.IsAny<RequestHeader>(), new UInt32Collection { 22 },
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteSubscriptionsResponse
                {
                    Results = [StatusCodes.Bad]
                }).
                Verifiable(Times.Once);

            // Act
            await sut.DeleteAsync(default);

            // Assert
            _mockSession.Verify();
        }

        [Fact]
        public async Task DisposeAsyncShouldDisposePublishTimerAsync()
        {
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);
            // Act
            await sut.DisposeAsync();

            // Assert
            _mockTimer.Verify(t => t.Dispose(), Times.Once);
        }

        [Fact]
        public void FindItemByClientHandleShouldReturnMonitoredItem()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();

            // Act
            success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(
                monitoredItem.ClientHandle, out var result);

            // Assert
            success.Should().BeTrue();
            result.Should().Be(monitoredItem);
        }

        [Fact]
        public void FindItemByClientHandleShouldReturnNullIfNotFound()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out _);
            success.Should().BeTrue();

            // Act
            success = sut.MonitoredItems.TryGetMonitoredItemByClientHandle(55,
                out var result);

            // Assert
            success.Should().BeFalse();
            result.Should().BeNull();
        }

        [Fact]
        public async Task OnPublishReceivedAsyncShouldResetKeepAliveTimerAsync()
        {
            // Arrange
            var message = new NotificationMessage();

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);

            // Act
            await sut.OnPublishReceivedAsync(message, null, null!);

            // Assert
            _mockTimer.Verify(t => t.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task RecreateAsyncShouldReCreateSubscriptionAndMonitoredItemsAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 10);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();

            // We have a running subscription with one monitored item.

            _mockSubscriptionServices
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
            _mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                    TimestampsToReturn.Both,
                    It.IsAny<MonitoredItemCreateRequestCollection>(), It.IsAny<CancellationToken>()))
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

            sut.Created.Should().BeTrue();

            // Act
            await sut.RecreateAsync(default);

            // Assert
            sut.Created.Should().BeTrue();
            sut.CurrentPublishingInterval.Should().Be(TimeSpan.FromSeconds(10));
            sut.CurrentKeepAliveCount.Should().Be(5);
            sut.CurrentLifetimeCount.Should().Be(10);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(10);
            sut.CurrentPriority.Should().Be(3);
            sut.Id.Should().Be(22);
            monitoredItem.ServerId.Should().Be(200);
            _mockSession.Verify();
        }

        [Fact]
        public async Task RecreateAsyncShouldReCreateSubscriptionsAsync()
        {
            // Arrange

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 10);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();

            _mockSubscriptionServices
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

            _mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 22,
                    TimestampsToReturn.Both,
                    It.IsAny<MonitoredItemCreateRequestCollection>(), It.IsAny<CancellationToken>()))
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
            sut.Created.Should().BeTrue();

            // Act
            await sut.RecreateAsync(default);

            // Assert
            sut.Created.Should().BeTrue();
            sut.CurrentPublishingInterval.Should().Be(TimeSpan.FromSeconds(10));
            sut.CurrentKeepAliveCount.Should().Be(5);
            sut.CurrentLifetimeCount.Should().Be(10);
            sut.CurrentMaxNotificationsPerPublish.Should().Be(10);
            sut.CurrentPriority.Should().Be(3);
            sut.Id.Should().Be(22);
            sut.MonitoredItems.Count.Should().Be(1);
            monitoredItem.ServerId.Should().Be(200);
            _mockSession.Verify();
        }

        [Fact]
        public void RemoveItemShouldRemoveItemFromMonitoredItems()
        {
            // Arrange
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object);
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();

            // Act
            success = sut.MonitoredItems.TryRemove(monitoredItem.ClientHandle);

            // Assert
            success.Should().BeTrue();
            sut.MonitoredItems.Items.Should().NotContain(monitoredItem);
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong1Async()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            success.Should().BeFalse();
            _mockSession.Verify();
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldReturnFalseWhenResponseWrong2Async()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            success.Should().BeTrue();

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            success.Should().BeFalse();
            _mockSession.Verify();
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndCreateServerItemAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            _mockSession.Verify();
            success.Should().BeTrue();
            monitoredItem.ServerId.Should().Be(19900);
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDoNothingIfStateIsCorrectAsync()
        {
            // Arrange

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();
            var serverId = monitoredItem.ServerId;

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            _mockSession.Verify();
            monitoredItem.ServerId.Should().Be(serverId);
            success.Should().BeTrue();
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndDeleteItemIfNotInSubscriptionAsync()
        {
            // Arrange

            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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

            _mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    new UInt32Collection { 19900 }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);

            // Act
            var success = await sut.TryCompleteTransferAsync([], default);

            // Assert
            _mockSession.Verify();
            success.Should().BeTrue();
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldCallGetMonitoredItemsAsyncAndReturnFalseIfFailingAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();
            monitoredItem.Created.Should().BeTrue();

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            _mockSession.Verify();
            success.Should().BeFalse();
            monitoredItem.Created.Should().BeFalse();
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldAssignServerIdToMonitoredItemWithClientIdAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();
            monitoredItem.Created.Should().BeTrue();
            var clientId = monitoredItem.ClientHandle;
            var serverId = monitoredItem.ServerId;

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            _mockSession.Verify();
            success.Should().BeTrue();
            monitoredItem.ClientHandle.Should().Be(19900);
            monitoredItem.ServerId.Should().Be(serverId);
        }

        [Fact]
        public async Task TryCompleteTransferAsyncShouldCreateWhatIsMissingOnServerAsync()
        {
            // Arrange
            var sut = new TestSubscription(_mockSession.Object, _mockNotificationDataHandler.Object,
                _mockCompletion.Object, _options, _mockObservability.Object, 2);
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            var success = sut.MonitoredItems.TryAdd("Test", options, out var monitoredItem);
            Assert.NotNull(monitoredItem);
            success.Should().BeTrue();
            monitoredItem.Created.Should().BeTrue();
            var clientId = monitoredItem.ClientHandle;
            var serverId = monitoredItem.ServerId;

            _mockMethodServices
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<CallMethodRequestCollection>(r =>
                           r.Count == 1
                        && r[0].InputArguments.Count == 1
                        && r[0].InputArguments[0].Value.Equals(2u)
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
            _mockMonitoredItemServices
                .Setup(s => s.DeleteMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    new UInt32Collection { 30000u }, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteMonitoredItemsResponse
                {
                    Results = [StatusCodes.Good]
                })
                .Verifiable(Times.Once);
            _mockMonitoredItemServices
                .Setup(s => s.CreateMonitoredItemsAsync(It.IsAny<RequestHeader>(), 2,
                    TimestampsToReturn.Both,
                    It.IsAny<MonitoredItemCreateRequestCollection>(), It.IsAny<CancellationToken>()))
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
            success.Should().BeTrue();
            monitoredItem.Created.Should().BeTrue();
            monitoredItem.ClientHandle.Should().Be(clientId);
            monitoredItem.ServerId.Should().Be(44444);

            _mockSession.Verify();
            _mockMonitoredItemServices.Verify();
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
                if (NodeId.IsNull(options.CurrentValue.StartNodeId))
                {
                    // Auto create
                    options.Configure(o => CreatedOptions);
                    TryGetPendingChange(out var change).Should().BeTrue();
                    Assert.NotNull(change);
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

        private readonly Mock<IMessageAckQueue> _mockCompletion;
        private readonly Opc.Ua.OptionsMonitor<SubscriptionOptions> _options;
        private readonly Mock<ITelemetryContext> _mockObservability;
        private readonly Mock<TimeProvider> _mockTimeProvider;
        private readonly Mock<ISubscriptionServiceSet> _mockSubscriptionServices;
        private readonly Mock<IMonitoredItemServiceSet> _mockMonitoredItemServices;
        private readonly Mock<IMethodServiceSet> _mockMethodServices;
        private readonly Mock<ISubscriptionContext> _mockSession;
        private readonly Mock<ISubscriptionNotificationHandler> _mockNotificationDataHandler;
        private readonly Mock<ITimer> _mockTimer;
        private readonly Mock<ILogger<Subscription>> _mockLogger;
    }
}
