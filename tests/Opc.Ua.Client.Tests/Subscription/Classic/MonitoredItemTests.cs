// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class MonitoredItemTests
    {
        [Test]
        public void SaveValueInCacheShouldOverwriteWithQueueSizeOne()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var monitoredItem = new MonitoredItem(telemetry) { CacheQueueSize = 1 };

            var notification1 = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue(new Variant(100), StatusCodes.Good, DateTime.UtcNow)
            };
            monitoredItem.SaveValueInCache(notification1);

            var notification2 = new MonitoredItemNotification
            {
                ClientHandle = monitoredItem.ClientHandle,
                Value = new DataValue(new Variant(200), StatusCodes.Good, DateTime.UtcNow)
            };
            monitoredItem.SaveValueInCache(notification2);

            IList<DataValue> result = monitoredItem.DequeueValues();

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That((int)result[0].WrappedValue, Is.EqualTo(200));
        }

        [Test]
        public void DequeueValuesShouldReturnAllQueuedValues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var monitoredItem = new MonitoredItem(telemetry) { CacheQueueSize = 5 };

            List<int> expectedValues = [1, 2, 3, 4, 5];
            List<MonitoredItemNotification> notifications = expectedValues
                .ConvertAll(value => new MonitoredItemNotification
                {
                    ClientHandle = monitoredItem.ClientHandle,
                    Value = new DataValue(new Variant(value), StatusCodes.Good, DateTime.UtcNow)
                });

            foreach (MonitoredItemNotification notification in notifications)
            {
                monitoredItem.SaveValueInCache(notification);
            }

            IList<DataValue> result = monitoredItem.DequeueValues();

            Assert.That(result, Has.Count.EqualTo(expectedValues.Count));
            Assert.That(result.Select(x => (int)x.WrappedValue), Is.EquivalentTo(expectedValues));

            // Ensure the cache is empty after dequeue
            IList<DataValue> emptyResult = monitoredItem.DequeueValues();
            Assert.That(emptyResult, Is.Empty);
        }

        [Test]
        public void SaveValueInCacheShouldOverwriteOldestValues()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            const int kQueueSize = 5;
            var monitoredItem = new MonitoredItem(telemetry) { CacheQueueSize = kQueueSize };

            List<int> values = [1, 2, 3, 4, 5, 6, 7];
            List<MonitoredItemNotification> notifications = values
                .ConvertAll(value => new MonitoredItemNotification
                {
                    ClientHandle = monitoredItem.ClientHandle,
                    Value = new DataValue(new Variant(value), StatusCodes.Good, DateTime.UtcNow)
                });

            foreach (MonitoredItemNotification notification in notifications)
            {
                monitoredItem.SaveValueInCache(notification);
            }

            IList<DataValue> result = monitoredItem.DequeueValues();

            Assert.That(result, Has.Count.EqualTo(kQueueSize));
            Assert.That(result.Select(x => (int)x.WrappedValue), Is.EquivalentTo(values.Skip(2)));
        }

        [Test]
        public void SerializeDeserializeShouldHaveSameProperties()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using var originalSession = SessionMock.Create();
            using var originalSubscription = new TestableSubscription(telemetry);

            var monitoredItems = new List<MonitoredItem>
            {
                new(telemetry)
                {
                    DisplayName = "MonitoredItem1", QueueSize = 10, CacheQueueSize = 1, SamplingInterval = 500
                },
                new(telemetry)
                {
                    DisplayName = "MonitoredItem2", QueueSize = 25, CacheQueueSize = 50, SamplingInterval = 500
                },
                new(telemetry)
                {
                    DisplayName = "MonitoredItem3", QueueSize = 0, CacheQueueSize = 0, SamplingInterval = 500
                }
            };

            // CacheQueueSize of 0 is invalid and should be set to 1 internally
            Assert.That(monitoredItems[^1].CacheQueueSize, Is.EqualTo(1));

            originalSubscription.AddItems(monitoredItems);
            originalSession.AddSubscription(originalSubscription);

            using var stream = new MemoryStream();
            originalSession.Save(stream, [originalSubscription]);
            stream.Position = 0;

            using var loadedSession = SessionMock.Create();
            loadedSession.Load(stream);
            Assert.That(loadedSession.Subscriptions.Count(), Is.EqualTo(1));
            Assert.That(loadedSession.Subscriptions.First().MonitoredItems.Count(), Is.EqualTo(monitoredItems.Count));

            List<MonitoredItemState> originalStates = monitoredItems
                .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
                .ToList()
                .ConvertAll(item =>
                {
                    item.Snapshot(out MonitoredItemState state);
                    return state;
                });

            var loadedItems = loadedSession.Subscriptions.First().MonitoredItems
                .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
                .ToList();
            List<MonitoredItemState> loadedStates = loadedItems
                .ConvertAll(item =>
                {
                    item.Snapshot(out MonitoredItemState state);
                    return state;
                });

            for (int i = 0; i < monitoredItems.Count; i++)
            {
                Assert.That(loadedStates[i] with { Timestamp = default },
                    Is.EqualTo(originalStates[i] with { Timestamp = default }));
            }
        }
    }
}
