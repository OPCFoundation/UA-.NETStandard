#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    [TestFixture]
    public sealed class MonitoredItemManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            m_observabilityMock = new Mock<ITelemetryContext>();
            m_mockLogger = new Mock<ILogger<MonitoredItemManager>>();
            m_mockLoggerFactory = new Mock<ILoggerFactory>();
            m_mockLoggerFactory
                  .Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(m_mockLogger.Object);
            m_observabilityMock.Setup(o => o.LoggerFactory)
                .Returns(m_mockLoggerFactory.Object);
            m_contextMock = new Mock<IMonitoredItemManagerContext>();
            m_contextMock
                .Setup(m => m.CreateMonitoredItem(
                    It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(),
                    It.IsAny<IMonitoredItemContext>()))
                .Returns((string name, IOptionsMonitor<MonitoredItemOptions> options, IMonitoredItemContext context) =>
                    new TestMonitoredItem(context, name, (Opc.Ua.OptionsMonitor<MonitoredItemOptions>)options, m_mockLogger.Object));
        }

        [Test]
        public async Task TryAddItemSucceedsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            Assert.That(sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3Again), Is.False);

            // Assert
            Assert.That(existingItem3Again, Is.SameAs(existingItem3));
            m_contextMock.Verify();
        }

        [Test]
        public async Task TryRemoveItemSucceedsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            Assert.That(sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4), Is.True);

            Assert.That(existingItem4, Is.Not.Null);
            Assert.That(sut.TryRemove(existingItem4.ClientHandle), Is.True);

            // Assert
            Assert.That(sut.Items, Has.Exactly(1).Items);
            Assert.That(sut.Items.First().Name, Is.EqualTo("Item3"));
            m_contextMock.Verify();
        }

        [Test]
        public async Task TryRemoveItemSucceedsRemoveAgainAndItFailsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            Assert.That(sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4), Is.True);

            Assert.That(existingItem4, Is.Not.Null);
            Assert.That(sut.TryRemove(existingItem4.ClientHandle), Is.True);
            Assert.That(sut.TryRemove(existingItem4.ClientHandle), Is.False);

            // Assert
            Assert.That(sut.Items, Has.Exactly(1).Items);
            Assert.That(sut.Items.First().Name, Is.EqualTo("Item3"));
            m_contextMock.Verify();
        }

        [Test]
        public async Task PauseAndUnpauseMonitoredItemsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            // Act
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem3);
            sut.TryAdd("Item4", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem4);

            sut.NotifySubscriptionManagerPaused(true);
            Assert.That(sut.Items, Has.All.Matches<IMonitoredItem>(i => ((TestMonitoredItem)i).Paused == true));
            sut.NotifySubscriptionManagerPaused(false);
            Assert.That(sut.Items, Has.All.Matches<IMonitoredItem>(i => ((TestMonitoredItem)i).Paused == false));
            sut.NotifySubscriptionManagerPaused(false);
            Assert.That(sut.Items, Has.All.Matches<IMonitoredItem>(i => ((TestMonitoredItem)i).Paused == false));
            sut.NotifySubscriptionManagerPaused(true);
            Assert.That(sut.Items, Has.All.Matches<IMonitoredItem>(i => ((TestMonitoredItem)i).Paused == true));

            // Assert
            m_contextMock.Verify();
        }

        [Test]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);
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
            Assert.That(result.ToArray(), Has.Exactly(1).Items);
            Assert.That(result.ToArray().Single(), Is.TypeOf<DataValueChange>());
            var single = (DataValueChange)result.ToArray().Single();
            Assert.That(single, Is.Not.Null); // verify match: dvc => dvc.Value.Value.Equals("test") && dvc.MonitoredItem == monitoredItem
        }

        [Test]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsInOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 1 }), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 2 }), out var monitoredItem2);
            Assert.That(monitoredItem1, Is.Not.Null);
            Assert.That(monitoredItem1.Order, Is.EqualTo(1));
            Assert.That(monitoredItem2, Is.Not.Null);
            Assert.That(monitoredItem2.Order, Is.EqualTo(2));
            var dataChangeNotification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test1", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test2", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        Value = new DataValue("test3", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    }
                ]
            };
            // Act
            var result = sut.CreateNotification(dataChangeNotification);

            // Assert
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result.Span[0], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[0]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test3"));
            Assert.That(((DataValueChange)result.Span[0]).MonitoredItem , Is.SameAs(monitoredItem1));
            Assert.That(result.Span[1], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[1]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test1"));
            Assert.That(((DataValueChange)result.Span[1]).MonitoredItem , Is.SameAs(monitoredItem2));
            Assert.That(result.Span[2], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[2]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test2"));
            Assert.That(((DataValueChange)result.Span[2]).MonitoredItem , Is.SameAs(monitoredItem2));
        }

        [Test]
        public async Task CreateNotificationDataChangeNotificationCreatesCorrectNotificationsInDefaultOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem2);
            Assert.That(monitoredItem1, Is.Not.Null);
            Assert.That(monitoredItem1.Order, Is.EqualTo(0));
            Assert.That(monitoredItem2, Is.Not.Null);
            Assert.That(monitoredItem2.Order, Is.EqualTo(0));
            var dataChangeNotification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test1", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem2.ClientHandle,
                        Value = new DataValue("test2", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    },
                    new MonitoredItemNotification
                    {
                        ClientHandle = monitoredItem1.ClientHandle,
                        Value = new DataValue("test3", StatusCodes.Good, DateTimeUtc.Now),
                        DiagnosticInfo = new DiagnosticInfo()
                    }
                ]
            };
            // Act
            var result = sut.CreateNotification(dataChangeNotification);

            // Assert
            Assert.That(result.Length, Is.EqualTo(3));
            Assert.That(result.Span[0], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[0]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test1"));
            Assert.That(((DataValueChange)result.Span[0]).MonitoredItem , Is.SameAs(monitoredItem2));
            Assert.That(result.Span[1], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[1]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test2"));
            Assert.That(((DataValueChange)result.Span[1]).MonitoredItem , Is.SameAs(monitoredItem2));
            Assert.That(result.Span[2], Is.TypeOf<DataValueChange>());
            Assert.That(((DataValueChange)result.Span[2]).Value.WrappedValue.AsBoxedObject(), Is.EqualTo("test3"));
            Assert.That(((DataValueChange)result.Span[2]).MonitoredItem , Is.SameAs(monitoredItem1));
        }

        [Test]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem);
            Assert.That(monitoredItem, Is.Not.Null);

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
            Assert.That(result.ToArray(), Has.Exactly(1).Items);
            Assert.That(result.ToArray().Single(), Is.TypeOf<EventNotification>());
            var single = (EventNotification)result.ToArray().Single();
            Assert.That(single, Is.Not.Null); // verify match: en => en.Fields[0].Value.Equals("Event1") && en.MonitoredItem == monitoredItem
        }

        [Test]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsInDefaultOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem2);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(), out var monitoredItem3);
            Assert.That(monitoredItem1, Is.Not.Null);
            Assert.That(monitoredItem3, Is.Not.Null);

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
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result.Span[0], Is.TypeOf<EventNotification>());
            Assert.That(((EventNotification)result.Span[0]).Fields[0].AsBoxedObject(), Is.EqualTo("Event1"));
            Assert.That(((EventNotification)result.Span[0]).MonitoredItem , Is.SameAs(monitoredItem1));
            Assert.That(result.Span[1], Is.TypeOf<EventNotification>());
            Assert.That(((EventNotification)result.Span[1]).Fields[0].AsBoxedObject(), Is.EqualTo("Event2"));
            Assert.That(((EventNotification)result.Span[1]).MonitoredItem , Is.SameAs(monitoredItem3));
        }

        [Test]
        public async Task CreateNotificationEventNotificationListCreatesCorrectNotificationsInOrderAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            var monitoredItemMock = new Mock<IMonitoredItem>();
            monitoredItemMock.SetupGet(m => m.ClientHandle).Returns(1);

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 5 }), out var monitoredItem1);
            sut.TryAdd("Item2", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 3 }), out var monitoredItem2);
            sut.TryAdd("Item3", OptionsFactory.Create<MonitoredItemOptions>(o => o with { Order = 1 }), out var monitoredItem3);
            Assert.That(monitoredItem1, Is.Not.Null);
            Assert.That(monitoredItem1.Order, Is.EqualTo(5));
            Assert.That(monitoredItem3, Is.Not.Null);
            Assert.That(monitoredItem3.Order, Is.EqualTo(1));

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
            Assert.That(result.Length, Is.EqualTo(2));
            Assert.That(result.Span[1], Is.TypeOf<EventNotification>());
            Assert.That(((EventNotification)result.Span[1]).Fields[0].AsBoxedObject(), Is.EqualTo("Event1"));
            Assert.That(((EventNotification)result.Span[1]).MonitoredItem , Is.SameAs(monitoredItem1));
            Assert.That(result.Span[0], Is.TypeOf<EventNotification>());
            Assert.That(((EventNotification)result.Span[0]).Fields[0].AsBoxedObject(), Is.EqualTo("Event2"));
            Assert.That(((EventNotification)result.Span[0]).MonitoredItem , Is.SameAs(monitoredItem3));
        }

        [Test]
        public async Task UpdateAddsNewItemsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item1", OptionsFactory.Create<MonitoredItemOptions>()),
                ("Item2", OptionsFactory.Create<MonitoredItemOptions>())
            };

            // Act
            var result = sut.Update(state);

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Select(i => i.Name), Does.Contain("Item1").And.Contain("Item2"));
            m_contextMock.Verify();
        }

        [Test]
        public async Task UpdateUpdatesExistingItemsAndRemovesRemainingAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
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
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result[0], Is.TypeOf<TestMonitoredItem>());
            Assert.That(result[0], Is.EqualTo(existingItem));
            m_contextMock.Verify();
        }

        [Test]
        public async Task UpdateUpdatesRemovesExistingItemAndAddsNewItemAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
            var state = new List<(string Name, IOptionsMonitor<MonitoredItemOptions> Options)>
            {
                ("Item2", OptionsFactory.Create<MonitoredItemOptions>())
            };

            sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem);

            // Act
            var result = sut.Update(state);

            // Assert
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result[0], Is.TypeOf<TestMonitoredItem>());
            Assert.That(result[0], Is.Not.EqualTo(existingItem));
            Assert.That(result[0].Name, Is.EqualTo("Item2"));
            m_contextMock.Verify();
        }

        [Test]
        public async Task UpdateRemovesItemsNotInStateAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);
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
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result[0], Is.TypeOf<TestMonitoredItem>());
            Assert.That(result[0], Is.EqualTo(existingItem1));
            Assert.That(sut.TryGetMonitoredItemByName("Item2", out _), Is.False);
            m_contextMock.Verify();
        }

        [Test]
        public async Task UpdateUpdatesItemOptionsAsync()
        {
            // Arrange
            await using var sut = new MonitoredItemManager(m_contextMock.Object, m_observabilityMock.Object);

            var success = sut.TryAdd("Item1", OptionsFactory.Create<MonitoredItemOptions>(), out var existingItem);
            Assert.That(existingItem, Is.TypeOf<TestMonitoredItem>());
            Assert.That(((TestMonitoredItem)existingItem!).Options.CurrentValue.SamplingInterval, Is.Not.EqualTo(TimeSpan.FromSeconds(100)));
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
            Assert.That(result, Has.Exactly(1).Items);
            Assert.That(result[0], Is.TypeOf<TestMonitoredItem>());
            Assert.That(((TestMonitoredItem)result[0]).Options.CurrentValue.SamplingInterval, Is.EqualTo(TimeSpan.FromSeconds(100)));
            m_contextMock.Verify();
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

        private Mock<IMonitoredItemManagerContext> m_contextMock;
        private Mock<ITelemetryContext> m_observabilityMock;
        private Mock<ILogger<MonitoredItemManager>> m_mockLogger;
        private Mock<ILoggerFactory> m_mockLoggerFactory;
    }
}
#endif
