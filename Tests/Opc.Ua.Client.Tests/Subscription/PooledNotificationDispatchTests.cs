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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions.Fakes;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Integration tests for V2 publish-dispatch pooled notification
    /// recycling. Validates that <see cref="IPooledEncodeable.Reuse"/>
    /// on a source-generated publish-payload type behaves correctly
    /// end-to-end: it resets fields, returns the instance to its
    /// activator's static pool, and a subsequent
    /// <c>CreateInstance()</c> hands the same recycled instance back
    /// with the sentinel cleared.
    /// </summary>
    [TestFixture]
    [Category("PooledNotificationDispatch")]
    [NonParallelizable]
    public sealed class PooledNotificationDispatchTests
    {
        [SetUp]
        public void SetUp()
        {
            m_completion = new FakeMessageAckQueue { PoolNotifications = true };
            m_telemetry = NUnitTelemetryContext.Create();
            m_mockServices = new Mock<ISubscriptionServiceSetClientMethods>();
            // Drain process-global pools so per-test instance identity
            // checks are stable. The pools are static on the closed
            // generic activator types, so prior tests in the run can
            // leave instances around; we evict everything we can see.
            DrainPool(MonitoredItemNotificationActivator.Instance);
            DrainPool(DataChangeNotificationActivator.Instance);
            DrainPool(EventFieldListActivator.Instance);
            DrainPool(EventNotificationListActivator.Instance);
        }

        [Test]
        public void GeneratedMonitoredItemNotificationImplementsPooledEncodeable()
        {
            var instance = new MonitoredItemNotification();
            Assert.That(instance, Is.InstanceOf<IPooledEncodeable>());
        }

        [Test]
        public void ReuseOnMonitoredItemNotificationResetsFieldsAndReturnsToPool()
        {
            // Rent via the activator so the sentinel starts at 0.
            var rented = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            rented.ClientHandle = 42;
            rented.Value = new DataValue(Variant.Null, StatusCodes.Good, DateTimeUtc.Now);

            rented.Reuse();

            // Fields reset to default.
            Assert.That(rented.ClientHandle, Is.EqualTo(0u),
                "Reuse should reset ClientHandle to default(uint)");
            Assert.That(rented.Value.IsNull, Is.True,
                "Reuse should reset Value to default (null)");

            // Pool should now hand the same reference back.
            var reRented = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            Assert.That(reRented, Is.SameAs(rented),
                "Activator should re-rent the instance returned by Reuse");
        }

        [Test]
        public void DoubleReuseOnGeneratedTypeIsIdempotent()
        {
            var rented = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            rented.Reuse();
            // Second Reuse must be a no-op: it must not re-add the
            // instance to the pool. We verify by renting twice — the
            // first rent gets the original; the second must be a
            // fresh new instance because the pool only ever held one
            // entry.
            rented.Reuse();
            var first = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            var second = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            Assert.That(first, Is.SameAs(rented));
            Assert.That(second, Is.Not.SameAs(rented));
        }

        [Test]
        public void RentAfterReuseClearsSentinelSoSubsequentReuseStillWorks()
        {
            var rented = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            rented.ClientHandle = 7;
            rented.Reuse();
            var rerented = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            Assert.That(rerented, Is.SameAs(rented));
            // After rent, sentinel should be clear; touching and reusing
            // again should reset the fields and re-pool.
            rerented.ClientHandle = 99;
            rerented.Reuse();
            Assert.That(rerented.ClientHandle, Is.EqualTo(0u));
            var third = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            Assert.That(third, Is.SameAs(rented),
                "Sentinel should clear on rent so subsequent Reuse can " +
                "re-pool the same instance");
        }

        [Test]
        public void ReuseOnDataChangeNotificationResetsMonitoredItemsAndPools()
        {
            var rented = (DataChangeNotification)
                DataChangeNotificationActivator.Instance.CreateInstance();
            rented.MonitoredItems = new MonitoredItemNotification[]
            {
                new() { ClientHandle = 1 },
                new() { ClientHandle = 2 }
            };
            Assert.That(rented.MonitoredItems.Count, Is.EqualTo(2));
            rented.Reuse();
            Assert.That(rented.MonitoredItems.Count, Is.EqualTo(0),
                "Reuse should drop the MonitoredItems backing reference");
            var reRented = (DataChangeNotification)
                DataChangeNotificationActivator.Instance.CreateInstance();
            Assert.That(reRented, Is.SameAs(rented));
        }

        [Test]
        public void ReuseOnEventFieldListResetsAndPools()
        {
            var rented = (EventFieldList)
                EventFieldListActivator.Instance.CreateInstance();
            rented.ClientHandle = 12;
            rented.EventFields = new Variant[] { new(42), new("text") };
            Assert.That(rented.EventFields.Count, Is.EqualTo(2));
            rented.Reuse();
            Assert.That(rented.ClientHandle, Is.EqualTo(0u));
            Assert.That(rented.EventFields.Count, Is.EqualTo(0));
            var reRented = (EventFieldList)
                EventFieldListActivator.Instance.CreateInstance();
            Assert.That(reRented, Is.SameAs(rented));
        }

        [Test]
        public void ReuseOnEventNotificationListResetsAndPools()
        {
            var rented = (EventNotificationList)
                EventNotificationListActivator.Instance.CreateInstance();
            rented.Events = new EventFieldList[]
            {
                new() { ClientHandle = 5 }
            };
            Assert.That(rented.Events.Count, Is.EqualTo(1));
            rented.Reuse();
            Assert.That(rented.Events.Count, Is.EqualTo(0));
            var reRented = (EventNotificationList)
                EventNotificationListActivator.Instance.CreateInstance();
            Assert.That(reRented, Is.SameAs(rented));
        }

        [Test]
        public async Task DispatcherCallsReuseOnPayloadWhenPoolNotificationsEnabledAsync()
        {
            // Wire a TestMessageProcessor that mirrors the V2
            // Subscription dispatch contract: on data change, walk the
            // notification and call Reuse() on each pooled item
            // (matching the production code in Subscription.cs).
            DrainPool(MonitoredItemNotificationActivator.Instance);
            DrainPool(DataChangeNotificationActivator.Instance);

            // Rent payload items from the activators so we have known
            // instance identities to assert pool re-entry against.
            var monitoredItem = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            monitoredItem.ClientHandle = 17;
            var dataChange = (DataChangeNotification)
                DataChangeNotificationActivator.Instance.CreateInstance();
            dataChange.MonitoredItems = new[] { monitoredItem };

            var stringTable = new List<string>();
            var availableSeq = new List<uint>();
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1,
                    NotificationData =
                    [
                        new ExtensionObject(dataChange)
                    ]
                }, availableSeq, stringTable).ConfigureAwait(false);

                await sut.DataChangeNotificationReceived
                    .WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            // After the dispatcher finally runs the reuse walk, the
            // activator's pool should contain our items. Rent and check
            // identity to verify pooling actually happened.
            var rentedItem = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            var rentedContainer = (DataChangeNotification)
                DataChangeNotificationActivator.Instance.CreateInstance();
            Assert.That(rentedItem, Is.SameAs(monitoredItem),
                "Pool should hand back the MonitoredItemNotification that " +
                "was reused after dispatch");
            Assert.That(rentedContainer, Is.SameAs(dataChange),
                "Pool should hand back the DataChangeNotification that was " +
                "reused after dispatch");
            Assert.That(rentedItem.ClientHandle, Is.EqualTo(0u),
                "Reused MonitoredItemNotification should have been reset");
        }

        [Test]
        public async Task DispatcherSkipsReuseWalkWhenPoolNotificationsDisabledAsync()
        {
            DrainPool(MonitoredItemNotificationActivator.Instance);
            m_completion.PoolNotifications = false;

            var monitoredItem = new MonitoredItemNotification { ClientHandle = 99 };
            var dataChange = new DataChangeNotification
            {
                MonitoredItems = new[] { monitoredItem }
            };

            var stringTable = new List<string>();
            var availableSeq = new List<uint>();
            var sut = new TestMessageProcessor(m_mockServices.Object,
                m_completion, m_telemetry)
            {
                Id = 1
            };
            await using (sut.ConfigureAwait(false))
            {
                await sut.OnPublishReceivedAsync(new NotificationMessage
                {
                    SequenceNumber = 1,
                    NotificationData =
                    [
                        new ExtensionObject(dataChange)
                    ]
                }, availableSeq, stringTable).ConfigureAwait(false);

                await sut.DataChangeNotificationReceived
                    .WaitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            // Pool is empty: nothing was returned because the dispatcher
            // skipped the reuse walk. CreateInstance allocates a fresh.
            var fresh = (MonitoredItemNotification)
                MonitoredItemNotificationActivator.Instance.CreateInstance();
            Assert.That(fresh, Is.Not.SameAs(monitoredItem),
                "PoolNotifications=false must not return instances to pool");
            // The original item's fields must NOT have been reset by the
            // dispatcher (since the reuse walk was skipped).
            Assert.That(monitoredItem.ClientHandle, Is.EqualTo(99u),
                "PoolNotifications=false must leave the payload untouched");
        }

        /// <summary>
        /// Rent and discard up to <paramref name="limit"/> instances from
        /// the activator to drain its pool to empty (or near-empty) before
        /// a test runs. The static pools are process-global so prior
        /// tests in the same run can leave entries that confuse identity
        /// assertions.
        /// </summary>
        private static void DrainPool<T>(PooledEncodeableType<T> activator,
            int limit = 64)
            where T : class, IPooledEncodeable, new()
        {
            // CreateInstance always returns an instance; we can't tell
            // pool hit from miss from outside. Rent up to `limit` items;
            // dropped references will be collected eventually.
            for (int i = 0; i < limit; i++)
            {
                _ = activator.CreateInstance();
            }
        }

        private sealed class TestMessageProcessor : MessageProcessor
        {
            public TestMessageProcessor(ISubscriptionServiceSetClientMethods session,
                IMessageAckQueue completion, ITelemetryContext telemetry)
                : base(session, completion, telemetry)
            {
            }

            public AsyncManualResetEvent DataChangeNotificationReceived { get; } = new();
            public AsyncManualResetEvent EventNotificationReceived { get; } = new();
            public AsyncManualResetEvent KeepAliveNotificationReceived { get; } = new();
            public AsyncManualResetEvent StatusChangeNotificationReceived { get; } = new();

            protected override ValueTask OnDataChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, DataChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                DataChangeNotificationReceived.Set();
                if (PoolNotifications)
                {
                    // Mirror the production reuse walk from
                    // Subscription.cs. Tested here in isolation so we
                    // don't drag in the full Subscription stack.
                    ReadOnlySpan<MonitoredItemNotification> items =
                        notification.MonitoredItems.Span;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (items[i] is IPooledEncodeable p)
                        {
                            p.Reuse();
                        }
                    }
                    if (notification is IPooledEncodeable container)
                    {
                        container.Reuse();
                    }
                }
                return default;
            }

            protected override ValueTask OnEventDataNotificationAsync(uint sequenceNumber,
                DateTime publishTime, EventNotificationList notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                EventNotificationReceived.Set();
                return default;
            }

            protected override ValueTask OnKeepAliveNotificationAsync(uint sequenceNumber,
                DateTime publishTime, PublishState publishStateMask)
            {
                KeepAliveNotificationReceived.Set();
                return default;
            }

            protected override ValueTask OnStatusChangeNotificationAsync(uint sequenceNumber,
                DateTime publishTime, StatusChangeNotification notification,
                PublishState publishStateMask, IReadOnlyList<string> stringTable)
            {
                StatusChangeNotificationReceived.Set();
                return default;
            }
        }

        private FakeMessageAckQueue m_completion = null!;
        private ITelemetryContext m_telemetry = null!;
        private Mock<ISubscriptionServiceSetClientMethods> m_mockServices = null!;
    }
}
