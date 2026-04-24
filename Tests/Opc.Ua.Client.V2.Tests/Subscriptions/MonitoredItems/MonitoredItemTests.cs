// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using System;
    using System.Threading.Tasks;
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Moq;
    using Xunit;

    public sealed class MonitoredItemTests
    {
        public MonitoredItemTests()
        {
            _mockContext = new Mock<IMonitoredItemContext>();
            _options = OptionsFactory.Create<MonitoredItemOptions>();
            _mockLogger = new Mock<ILogger<MonitoredItem>>();
        }

        [Fact]
        public void ServerIdShouldGet()
        {
            // Arrange
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);
            const uint serverId = 123u;

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                MonitoredItemId = serverId,
                RevisedSamplingInterval = 10000,
                RevisedQueueSize = 10
            }, 0, [], new ResponseHeader());

            // Assert
            sut.ServerId.Should().Be(serverId);
            sut.Created.Should().BeTrue();
        }

        [Fact]
        public void CreatedShouldReturnFalseWhenServerIdIsNotSet()
        {
            // Act
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
              _options, _mockLogger.Object);
            var result = sut.Created;

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void SetMonitoringModeShouldNotifyItemChangeResultWhenStatusCodeIsBad()
        {
            // Arrange
            _options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Sampling,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
                _options, _mockLogger.Object);

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetMonitoringModeResult(MonitoringMode.Sampling, StatusCodes.Bad,
                0, [], new ResponseHeader());

            // Assert
            _mockContext.Verify(s => s.NotifyItemChangeResult(sut, 1,
                _options.CurrentValue,
                It.Is<ServiceResult>(s => s.StatusCode == StatusCodes.Bad),
                false, null),
                Times.Once);
        }

        [Fact]
        public void CreateShouldNotifyItemChangeResultWhenStatusCodeIsBad()
        {
            // Arrange
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Bad
            }, 0, [], new ResponseHeader());

            // Assert
            _mockContext.Verify(s => s.NotifyItemChangeResult(sut, 1,
                _options.CurrentValue,
                It.Is<ServiceResult>(s => s.StatusCode == StatusCodes.Bad),
                false, null),
                Times.Once);
        }

        [Fact]
        public void CreateShouldNotifyItemChangeResultWithFilterResult()
        {
            // Arrange
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);
            var filterResult = new EventFilterResult();

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good,
                FilterResult = new ExtensionObject(filterResult)
            }, 0, [], new ResponseHeader());

            // Assert
            _mockContext.Verify(s => s.NotifyItemChangeResult(sut, 0,
                _options.CurrentValue, ServiceResult.Good, true,
                It.Is<MonitoringFilterResult>(o => Utils.IsEqual(o, filterResult))),
                Times.Once);
        }

        [Fact]
        public void CurrentMonitoringModeShouldSetAndGet()
        {
            // Arrange
            _options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Reporting,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
                _options, _mockLogger.Object);

            sut.CurrentMonitoringMode.Should().NotBe(MonitoringMode.Sampling);

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            sut.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);
        }

        [Fact]
        public void CurrentMonitoringModeShouldUpdate()
        {
            // Arrange
            _options.Configure(o => o with
            {
                MonitoringMode = MonitoringMode.Sampling,
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
                _options, _mockLogger.Object);

            sut.CurrentMonitoringMode.Should().NotBe(MonitoringMode.Sampling);

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetMonitoringModeResult(MonitoringMode.Sampling, StatusCodes.Good,
                0, [], new ResponseHeader());

            // Assert
            sut.CurrentMonitoringMode.Should().Be(MonitoringMode.Sampling);
        }

        [Fact]
        public void CurrentSamplingIntervalShouldSetAndGet()
        {
            // Arrange
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);
            var currentSamplingInterval = TimeSpan.FromMilliseconds(500);

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                RevisedSamplingInterval = currentSamplingInterval.TotalMilliseconds,
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            sut.CurrentSamplingInterval.Should().Be(currentSamplingInterval);
        }

        [Fact]
        public void CurrentQueueSizeShouldSetAndGet()
        {
            // Arrange
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);
            const uint currentQueueSize = 5u;

            // Act
            sut.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change);
            change.SetCreateResult(new MonitoredItemCreateRequest
            {
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = sut.ClientHandle,
                    SamplingInterval = 1000,
                    QueueSize = 5,
                    DiscardOldest = true
                }
            }, new MonitoredItemCreateResult
            {
                RevisedQueueSize = currentQueueSize,
                StatusCode = StatusCodes.Good
            }, 0, [], new ResponseHeader());

            // Assert
            sut.CurrentQueueSize.Should().Be(currentQueueSize);
        }

        [Fact]
        public void ClientHandleShouldGet()
        {
            // Act
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
               _options, _mockLogger.Object);
            var result = sut.ClientHandle;

            // Assert
            result.Should().NotBe(0);
        }

        [Fact]
        public async Task DisposeShouldCallRemoveItemOnSubscriptionAsync()
        {
            // Act
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
              _options, _mockLogger.Object);
            await sut.DisposeAsync();

            // Assert
            _mockContext.Verify(s => s.NotifyItemChange(sut, true), Times.Once);
        }

        [Fact]
        public async Task DisposeCanBeCalledTwiceWithoutExceptionAsync()
        {
            // Act
            _options.Configure(o => o with
            {
                StartNodeId = new NodeId("test", 0)
            });
            var sut = new TestMonitoredItem(_mockContext.Object,
              _options, _mockLogger.Object);
            await sut.DisposeAsync();
            await sut.DisposeAsync();

            // Assert
            _mockContext.Verify(s => s.NotifyItemChange(sut, true), Times.Once);
        }

        [Fact]
        public void OnSubscriptionStateChangeShouldAdjustQueueSizeWhenAutoSetQueueSizeIsTrue()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            monitoredItem.TryGetPendingChange(out var change).Should().BeTrue();
            Assert.NotNull(change?.Create);
            change.Create.RequestedParameters.QueueSize.Should().Be(6); // (500 / 100) + 1 = 6
        }

        [Fact]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenAutoSetQueueSizeIsFalse()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = false,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            monitoredItem.TryGetPendingChange(out var change).Should().BeFalse();
        }

        [Fact]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenPublishingIntervalIsZero()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.Zero);

            // Assert
            monitoredItem.TryGetPendingChange(out var change).Should().BeFalse();
        }

        [Fact]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsZero()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.Zero
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            monitoredItem.TryGetPendingChange(out var change).Should().BeFalse();
        }

        [Fact]
        public void OnSubscriptionStateChangeShouldNotAdjustQueueSizeWhenSamplingIntervalIsNegative()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode"),
                AutoSetQueueSize = true,
                QueueSize = 1,
                SamplingInterval = TimeSpan.FromMilliseconds(-100)
            });

            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);
            while (monitoredItem.TryGetPendingChange(out var c)) { monitoredItem.CompleteChange(c); }

            // Act
            monitoredItem.OnSubscriptionStateChange(SubscriptionState.Created, TimeSpan.FromMilliseconds(500));

            // Assert
            monitoredItem.TryGetPendingChange(out var change).Should().BeFalse();
        }

        [Fact]
        public void ToStringShouldReturnExpectedString()
        {
            // Arrange
            var mockContext = new Mock<IMonitoredItemContext>();
            var mockLogger = new Mock<ILogger>();
            var options = OptionsFactory.Create<MonitoredItemOptions>();
            options.Configure(o => o with
            {
                StartNodeId = NodeId.Parse("ns=2;s=TestNode")
            });

            mockContext.Setup(c => c.ToString()).Returns("Test");
            var monitoredItem = new TestMonitoredItem(mockContext.Object, options, mockLogger.Object);

            // Act
            var result = monitoredItem.ToString();

            // Assert
            result.Should().Be($"Test#{monitoredItem.ClientHandle}|0 (TestItem)");
        }

        private sealed class TestMonitoredItem : MonitoredItem
        {
            public TestMonitoredItem(IMonitoredItemContext subscription,
                OptionsMonitor<MonitoredItemOptions> options, ILogger logger)
                : base(subscription, "TestItem", options, logger)
            {
                options.Configure(o => o with
                {
                    StartNodeId = new NodeId("test", 0)
                });
            }
        }

        private readonly Mock<IMonitoredItemContext> _mockContext;
        private readonly OptionsMonitor<MonitoredItemOptions> _options;
        private readonly Mock<ILogger<MonitoredItem>> _mockLogger;
    }
}
