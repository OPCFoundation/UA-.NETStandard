/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using NUnit.Framework;
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

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Value, Is.EqualTo(200));
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

            Assert.That(result.Count, Is.EqualTo(expectedValues.Count));
            Assert.That(result.Select(x => x.Value), Is.EquivalentTo(expectedValues));

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

            Assert.That(result.Count, Is.EqualTo(kQueueSize));
            Assert.That(result.Select(x => x.Value), Is.EquivalentTo(values.Skip(2)));
        }

        [Test]
        public void SerializeDeserializeShouldHaveSameProperties()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var originalSession = SessionMock.Create();
            var originalSubscription = new TestableSubscription(telemetry);

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

            var loadedSession = SessionMock.Create();
            loadedSession.Load(stream);
            Assert.That(loadedSession.Subscriptions.Count(), Is.EqualTo(1));
            Assert.That(loadedSession.Subscriptions.First().MonitoredItems.Count(), Is.EqualTo(monitoredItems.Count));

            List<MonitoredItemState> originalStates = monitoredItems
                .ConvertAll(item =>
                {
                    item.Snapshot(out MonitoredItemState state);
                    return state;
                });

            var loadedItems = loadedSession.Subscriptions.First().MonitoredItems.ToList();
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
