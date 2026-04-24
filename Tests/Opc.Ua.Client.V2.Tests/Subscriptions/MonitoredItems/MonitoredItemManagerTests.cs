// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using FluentAssertions;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Moq;
    using Opc.Ua.Client;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Xunit;

    public sealed class MonitoredItemManagerTests
    {
        public MonitoredItemManagerTests()
        {
            _observabilityMock = new Mock<ITelemetryContext>();
            _mockLogger = new Mock<ILogger<MonitoredItemManager>>();
            _mockLoggerFactory = new Mock<ILoggerFactory>();
            _mockLoggerFactory
                  .Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
            _observabilityMock.Setup(o => o.LoggerFactory)
                .Returns(_mockLoggerFactory.Object);
            _contextMock = new Mock<IMonitoredItemManagerContext>();
            _contextMock
                .Setup(m => m.CreateMonitoredItem(
                    It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(),
                    It.IsAny<IMonitoredItemContext>()))
                .Returns((string name, IOptionsMonitor<MonitoredItemOptions> options, IMonitoredItemContext context) =>
                    new TestMonitoredItem(context, name, (Opc.Ua.OptionsMonitor<MonitoredItemOptions>)options, _mockLogger.Object));
        }

        [Fact]
        public async Task TryAddItemSucceedsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3Again).Should().BeFalse();

            // Assert
            existingItem3Again.Should().BeSameAs(existingItem3);
            _contextMock.Verify();
        }

        [Fact]
        public async Task TryRemoveItemSucceedsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4).Should().BeTrue();

            Assert.NotNull(existingItem4);
            sut.TryRemove(existingItem4.ClientHandle).Should().BeTrue();

            // Assert
            sut.Items.Should().ContainSingle().Which.Name.Should().Be("Item3");
            _contextMock.Verify();
        }

        [Fact]
        public async Task TryRemoveItemSucceedsRemoveAgainAndItFailsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4).Should().BeTrue();

            Assert.NotNull(existingItem4);
            sut.TryRemove(existingItem4.ClientHandle).Should().BeTrue();
            sut.TryRemove(existingItem4.ClientHandle).Should().BeFalse();

            // Assert
            sut.Items.Should().ContainSingle().Which.Name.Should().Be("Item3");
            _contextMock.Verify();
        }

        [Fact]
        public async Task PauseAndUnpauseMonitoredItemsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4);

            sut.NotifySubscriptionManagerPaused(true);
            sut.Items.Should().AllSatisfy(i => ((TestMonitoredItem)i).Paused.Should().BeTrue());
            sut.NotifySubscriptionManagerPaused(false);
            sut.Items.Should().AllSatisfy(i => ((TestMonitoredItem)i).Paused.Should().BeFalse());
            sut.NotifySubscriptionManagerPaused(false);
            sut.Items.Should().AllSatisfy(i => ((TestMonitoredItem)i).Paused.Should().BeFalse());
            sut.NotifySubscriptionManagerPaused(true);
            sut.Items.Should().AllSatisfy(i => ((TestMonitoredItem)i).Paused.Should().BeTrue());

            // Assert
            _contextMock.Verify();
        }

        [Fact]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem);
            Assert.NotNull(monitoredItem);
            var dataChangeNotification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem.ClientHandle,
                        Value = new DataValue("test"),
                        DiagnosticInfo = new DiagnosticInfo()
                    }
                ]
            };
            // Act
            var result = sut.CreateNotification(dataChangeNotification);

            // Assert
            result.ToArray().Should().ContainSingle()
                .Which.Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test") && dvc.MonitoredItem == monitoredItem);
        }

        [Fact]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsInOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 1 }), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 2 }), out var monitoredItem2);
            Assert.NotNull(monitoredItem1);
            monitoredItem1.Order.Should().Be(1);
            Assert.NotNull(monitoredItem2);
            monitoredItem2.Order.Should().Be(2);
            var dataChangeNotification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test1", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test2", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        Value = new DataValue("test3", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    }
                ]
            };
            // Act
            var result = sut.CreateNotification(dataChangeNotification);

            // Assert
            result.Length.Should().Be(3);
            // TODO: result.Span[0].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test3") && dvc.MonitoredItem == monitoredItem1);
            // TODO: result.Span[1].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test1") && dvc.MonitoredItem == monitoredItem2);
            // TODO: result.Span[2].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test2") && dvc.MonitoredItem == monitoredItem2);
        }

        [Fact]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsInDefaultOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem2);
            Assert.NotNull(monitoredItem1);
            monitoredItem1.Order.Should().Be(0);
            Assert.NotNull(monitoredItem2);
            monitoredItem2.Order.Should().Be(0);
            var dataChangeNotification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test1", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test2", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        Value = new DataValue("test3", StatusCodes.Good, DateTime.UtcNow),
                        DiagnosticInfo = new DiagnosticInfo()
                    }
                ]
            };
            // Act
            var result = sut.CreateNotification(dataChangeNotification);

            // Assert
            result.Length.Should().Be(3);
            result.Span[0].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test1") && dvc.MonitoredItem == monitoredItem2);
            result.Span[1].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test2") && dvc.MonitoredItem == monitoredItem2);
            result.Span[2].Should().Match<DataValueChange>(dvc => dvc.Value.Value.Equals("test3") && dvc.MonitoredItem == monitoredItem1);
        }

        [Fact]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem);
            Assert.NotNull(monitoredItem);

            var eventNotificationList = new EventNotificationList
            {
                Events =
                [
                    new EventFieldList
                    {
                        ClientHandle = monitoredItem.ClientHandle,
                        EventFields = [new Variant("Event1")]
                    }
                ]
            };

            // Act
            var result = sut.CreateNotification(eventNotificationList);

            // Assert
            result.ToArray().Should().ContainSingle()
                .Which.Should().Match<EventNotification>(en => en.Fields[0].Value.Equals("Event1") && en.MonitoredItem == monitoredItem);
        }

        [Fact]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsInDefaultOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem2);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem3);
            Assert.NotNull(monitoredItem1);
            Assert.NotNull(monitoredItem3);

            var eventNotificationList = new EventNotificationList
            {
                Events =
                [
                    new EventFieldList
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        EventFields = [new Variant("Event1")]
                    },
                    new EventFieldList
                    {
                        ClientHandle = monitoredItem3.ClientHandle,
                        EventFields = [new Variant("Event2")]
                    }
                ]
            };

            // Act
            var result = sut.CreateNotification(eventNotificationList);

            // Assert
            result.Length.Should().Be(2);
            result.Span[0].Should().Match<EventNotification>(en => en.Fields[0].Value.Equals("Event1") && en.MonitoredItem == monitoredItem1);
            result.Span[1].Should().Match<EventNotification>(en => en.Fields[0].Value.Equals("Event2") && en.MonitoredItem == monitoredItem3);
        }

        [Fact]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsInOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 5 }), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 3 }), out var monitoredItem2);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 1 }), out var monitoredItem3);
            Assert.NotNull(monitoredItem1);
            monitoredItem1.Order.Should().Be(5);
            Assert.NotNull(monitoredItem3);
            monitoredItem3.Order.Should().Be(1);

            var eventNotificationList = new EventNotificationList
            {
                Events =
                [
                    new EventFieldList
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        EventFields = [new Variant("Event1")]
                    },
                    new EventFieldList
                    {
                        ClientHandle = monitoredItem3.ClientHandle,
                        EventFields = [new Variant("Event2")]
                    }
                ]
            };

            // Act
            var result = sut.CreateNotification(eventNotificationList);

            // Assert
            result.Length.Should().Be(2);
            // TODO: result.Span[1].Should().Match<EventNotification>(en => en.Fields[0].Value.Equals("Event1") && en.MonitoredItem == monitoredItem1);
            // TODO: result.Span[0].Should().Match<EventNotification>(en => en.Fields[0].Value.Equals("Event2") && en.MonitoredItem == monitoredItem3);
        }

        [Fact]
        public async Task UpdateAddsNewItemsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item1", OptionsFactory.Create<MonitoredItemOptions>()),
                ("Item2", OptionsFactory.Create<MonitoredItemOptions>())
            };

            // Act
            var result = sut.Update(state);

            // Assert
            result.Count.Should().Be(2);
            result.Should().Contain(item => item.Name == "Item1").And.Contain(item => item.Name == "Item2");
            _contextMock.Verify();
        }

        [Fact]
        public async Task UpdateUpdatesExistingItemsAndRemovesRemainingAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item1", OptionsFactory.Create<MonitoredItemOptions>())
            };

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(), out _);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out _);

            // Act
            var result = sut.Update(state);

            // Assert
            result.Should().ContainSingle().Which.Should().BeOfType<TestMonitoredItem>()
                .Which.Should().Be(existingItem);
            _contextMock.Verify();
        }

        [Fact]
        public async Task UpdateUpdatesRemovesExistingItemAndAddsNewItemAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item2", OptionsFactory.Create<MonitoredItemOptions>())
            };

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem);

            // Act
            var result = sut.Update(state);

            // Assert
            result.Should().ContainSingle().Which.Should().BeOfType<TestMonitoredItem>()
                .Which.Should().NotBe(existingItem);
            result.Should().ContainSingle().Which.Name.Should().Be("Item2");
            _contextMock.Verify();
        }

        [Fact]
        public async Task UpdateRemovesItemsNotInStateAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item1", OptionsFactory.Create<MonitoredItemOptions>())
            };

            var item1 = OptionsFactory.Create<MonitoredItemOptions>();
            var item2 = OptionsFactory.Create<MonitoredItemOptions>();

            sut.TryAdd("Item1", item1, out var existingItem1);
            sut.TryAdd("Item2", item2, out _);

            // Act
            var result = sut.Update(state);

            // Assert
            result.Should().ContainSingle().Which.Should().BeOfType<TestMonitoredItem>()
                .Which.Should().Be(existingItem1);
            sut.TryGetMonitoredItemByName("Item2", out _).Should().BeFalse();
            _contextMock.Verify();
        }

        [Fact]
        public async Task UpdateUpdatesItemOptionsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(_contextMock.Object, _observabilityMock.Object);

            var success = sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem);
            existingItem.Should().BeOfType<TestMonitoredItem>()
                .Which.Options.CurrentValue.SamplingInterval.Should().NotBe(TimeSpan.FromSeconds(100));
            var options = OptionsFactory.Create<MonitoredItemOptions>(o => o with
            {
                SamplingInterval = TimeSpan.FromSeconds(100)
            });
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item1", options)
            };

            // Act
            var result = sut.Update(state);

            // Assert
            result.Should().ContainSingle().Which.Should().BeOfType<TestMonitoredItem>()
                .Which.Options.CurrentValue.SamplingInterval.Should().Be(TimeSpan.FromSeconds(100));
            _contextMock.Verify();
        }

        private sealed class TestMonitoredItem : MonitoredItem
        {
            public bool Paused { get; private set; }

            public TestMonitoredItem(IMonitoredItemContext subscription, string name,
                Opc.Ua.OptionsMonitor<MonitoredItemOptions> options, ILogger logger)
                : base(subscription, name, options, logger)
            {
                options.Configure(o => o with
                {
                    StartNodeId = new NodeId(name, 0)
                });
            }

            protected internal override void NotifySubscriptionManagerPaused(bool paused)
            {
                Paused = paused;
                base.NotifySubscriptionManagerPaused(paused);
            }
        }

        private readonly Mock<IMonitoredItemManagerContext> _contextMock;
        private readonly Mock<ITelemetryContext> _observabilityMock;
        private readonly Mock<ILogger<MonitoredItemManager>> _mockLogger;
        private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    }
}
