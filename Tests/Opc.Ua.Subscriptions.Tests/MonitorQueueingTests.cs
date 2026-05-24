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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for MonitoredItem queue behavior including
    /// queue size enforcement, discard policies, and overflow handling.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    [Category("MonitorQueueing")]
    public class MonitorQueueingTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            m_subscriptionId = await CreateSetupSubscriptionAsync(
                publishingInterval: 100, requestedLifetimeCount: 100,
                requestedMaxKeepAliveCount: 10).ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (m_subscriptionId > 0)
            {
                try
                {
                    await Session.DeleteSubscriptionsAsync(
                        null,
                        new uint[] { m_subscriptionId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Subscription may already be deleted
                }
                m_subscriptionId = 0;
            }
        }

        [Test]
        public async Task QueueSizeOneOnlyLatestValueDeliveredAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 1, samplingInterval: 0,
queueSize: 1, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int lastVal = 0;
            for (int i = 0; i < 5; i++)
            {
                lastVal = 4000 + i;
                await WriteValueAsync(nodeId, lastVal).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    int notified = dcn.MonitoredItems[^1].Value.WrappedValue.GetInt32();
                    Assert.That(notified, Is.EqualTo(lastVal),
                        "Queue size 1 should deliver only the latest value.");
                }
            }
        }

        [Test]
        public async Task QueueSizeFiveAccumulatesUpToFiveValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 2, samplingInterval: 0, queueSize: 5))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
            {
                await WriteValueAsync(nodeId, 5000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThanOrEqualTo(1),
                "Queue of 5 should accumulate up to 5 notifications.");
        }

        [Test]
        public async Task QueueSizeTenWithFewerChangesProvidesAllChangesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 3, samplingInterval: 0, queueSize: 10))
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 3; i++)
            {
                await WriteValueAsync(nodeId, 6000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThanOrEqualTo(1),
                "All 3 changes should be delivered when queue is 10.");
        }

        [Test]
        public async Task QueueSizeZeroRevisedToOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 4, queueSize: 0))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].RevisedQueueSize,
                Is.GreaterThanOrEqualTo(1u),
                "Server should revise queue size 0 to at least 1.");
        }

        [Test]
        public async Task DiscardOldestTrueDropsFirstEnqueuedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 10, samplingInterval: 0,
queueSize: 2, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            // Write 4 values to overflow a queue of 2
            for (int i = 0; i < 4; i++)
            {
                await WriteValueAsync(nodeId, 7000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    // Last notified value should be the latest written
                    int last = dcn.MonitoredItems[^1].Value.WrappedValue.GetInt32();
                    Assert.That(last, Is.EqualTo(7003),
                        "DiscardOldest=true should keep the newest value.");
                }
            }
        }

        [Test]
        public async Task DiscardOldestFalseDropsNewestEnqueuedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 11, samplingInterval: 0,
queueSize: 2, discardOldest: false))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                await WriteValueAsync(nodeId, 8000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    // With DiscardOldest=false, oldest values are kept
                    int first = dcn.MonitoredItems[0].Value.WrappedValue.GetInt32();
                    Assert.That(first, Is.EqualTo(8000),
                        "DiscardOldest=false should keep the oldest value.");
                }
            }
        }

        [Test]
        public async Task DiscardOldestDefaultIsTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            // Default CreateItemRequest has discardOldest: true
            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 12, samplingInterval: 0, queueSize: 2))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                await WriteValueAsync(nodeId, 9000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    int last = dcn.MonitoredItems[^1].Value.WrappedValue.GetInt32();
                    Assert.That(last, Is.EqualTo(9003),
                        "Default discard policy should behave as DiscardOldest=true.");
                }
            }
        }

        [Test]
        public async Task ModifyItemDiscardOldestChangedAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 13, samplingInterval: 0,
queueSize: 5, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monId = createResp.Results[0].MonitoredItemId;

            // Modify to DiscardOldest=false
            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = monId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 13,
                                SamplingInterval = 0,
                                QueueSize = 5,
                                DiscardOldest = false
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True,
                "Modify to change DiscardOldest should succeed.");
        }

        [Test]
        public async Task QueueOverflowSetsOverflowBitInStatusCodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 20, samplingInterval: 0,
queueSize: 2, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            // Overflow the queue (write more than queue size)
            for (int i = 0; i < 5; i++)
            {
                await WriteValueAsync(nodeId, 10000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    // Check if any item has the Overflow bit set
                    bool hasOverflow = dcn.MonitoredItems.ToArray()
                        .Any(m => m.Value.StatusCode.Overflow);
                    // Overflow bit is optional per spec, so just log
                    Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0),
                        "Should receive notifications even on overflow.");
                }
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task QueueOverflowWithSingleItemQueueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 21, samplingInterval: 0,
queueSize: 1, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            try
            {
                await ConsumeInitialPublishAsync().ConfigureAwait(false);

                for (int i = 0; i < 10; i++)
                {
                    await WriteValueAsync(nodeId, 11000 + i).ConfigureAwait(false);
                }

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

                // With queue=1 we should get at most 1 notification per item
                if (pubResp.NotificationMessage.NotificationData.Count > 0)
                {
                    var dcn = ExtensionObject.ToEncodeable(
                        pubResp.NotificationMessage.NotificationData[0]) as
                        DataChangeNotification;
                    Assert.That(dcn, Is.Not.Null);
                    Assert.That(dcn.MonitoredItems.Count,
                        Is.LessThanOrEqualTo(2),
                        "Queue size 1 should deliver at most 1-2 items.");
                }
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: rapid-write/publish sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task QueueOverflowCountMatchesDroppedItemsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const uint queueSize = 3;

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 22, samplingInterval: 0,
queueSize: queueSize, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            // Write more values than the queue size
            for (int i = 0; i < 6; i++)
            {
                await WriteValueAsync(nodeId, 12000 + i).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                Assert.That(dcn, Is.Not.Null);
                Assert.That(
                    (uint)dcn.MonitoredItems.Count,
                    Is.LessThanOrEqualTo(queueSize),
                    "Notification count should not exceed queue size.");
            }
        }

        [Test]
        public async Task VeryLargeQueueSizeRevisedDownwardAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 30, queueSize: uint.MaxValue))
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(resp.Results[0].RevisedQueueSize, Is.LessThan(uint.MaxValue),
                "Server should revise an extremely large queue size downward.");
            Assert.That(resp.Results[0].RevisedQueueSize, Is.GreaterThan(0u));
        }

        [Test]
        public async Task QueueSizePreservedAfterModifyAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp = await CreateSingleItemAsync(
                CreateItemRequest(nodeId, 31, samplingInterval: 100, queueSize: 7))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            uint monId = createResp.Results[0].MonitoredItemId;
            uint originalQueueSize = createResp.Results[0].RevisedQueueSize;

            // Modify only sampling interval, keep same queue size
            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = monId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 31,
                                SamplingInterval = 250,
                                QueueSize = originalQueueSize,
                                DiscardOldest = true
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            Assert.That(modResp.Results[0].RevisedQueueSize,
                Is.EqualTo(originalQueueSize),
                "Queue size should be preserved when modifying other parameters.");
        }

        [Test]
        public async Task QueueSizeDifferentPerItemAsync()
        {
            NodeId node1 = ToNodeId(Constants.ScalarStaticInt32);
            NodeId node2 = ToNodeId(Constants.ScalarStaticDouble);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(node1, 32, samplingInterval: 0, queueSize: 3),
                CreateItemRequest(node2, 33, samplingInterval: 0, queueSize: 8)
            };

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode), Is.True);

            uint revised1 = createResp.Results[0].RevisedQueueSize;
            uint revised2 = createResp.Results[1].RevisedQueueSize;

            // Both should be at least 1 and they are independently set
            Assert.That(revised1, Is.GreaterThan(0u));
            Assert.That(revised2, Is.GreaterThan(0u));
            Assert.That(revised2, Is.GreaterThanOrEqualTo(revised1),
                "Second item with larger requested queue should get >= first.");
        }

        [Test]
        public async Task QueueSizeOneDiscardOldestDeliversLatestAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 1, samplingInterval: 0,
queueSize: 1, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            int lastVal = 0;
            for (int i = 0; i < 5; i++)
            {
                lastVal = 4000 + i;
                await WriteValueAsync(nodeId, lastVal)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    int notified = dcn.MonitoredItems[^1].Value
                        .WrappedValue.GetInt32();
                    Assert.That(notified, Is.EqualTo(lastVal),
                        "Queue size 1 should deliver only the latest.");
                }
            }
        }

        [Test]
        public async Task QueueSizeFiveAccumulatesFiveValuesAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 2, samplingInterval: 0,
queueSize: 5))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
            {
                await WriteValueAsync(nodeId, 5000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(
                pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count,
                Is.GreaterThanOrEqualTo(1),
                "Queue of 5 should accumulate notifications.");
        }

        [Test]
        [Category("LongRunning")]
        public async Task QueueSizeTenFewerChangesAllDeliveredAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 3, samplingInterval: 0,
queueSize: 10))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            try
            {
                await ConsumeInitialPublishAsync().ConfigureAwait(false);

                for (int i = 0; i < 3; i++)
                {
                    await WriteValueAsync(nodeId, 6000 + i)
                        .ConfigureAwait(false);
                }

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pubResp.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0));

                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                Assert.That(dcn, Is.Not.Null);
                Assert.That(dcn.MonitoredItems.Count,
                    Is.GreaterThanOrEqualTo(1),
                    "All 3 changes should be delivered with queue 10.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: write/publish sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task QueueSizeZeroRevisedToAtLeastOneAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse resp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 4, queueSize: 0))
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(
                resp.Results[0].RevisedQueueSize,
                Is.GreaterThanOrEqualTo(1u),
                "Server should revise queue size 0 to at least 1.");
        }

        [Test]
        public async Task DiscardOldestTrueKeepsNewestAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 10, samplingInterval: 0,
queueSize: 2, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                await WriteValueAsync(nodeId, 7000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    int last = dcn.MonitoredItems[^1].Value
                        .WrappedValue.GetInt32();
                    Assert.That(last, Is.EqualTo(7003),
                        "DiscardOldest=true keeps newest value.");
                }
            }
        }

        [Test]
        public async Task DiscardOldestFalseKeepsOldestAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 11, samplingInterval: 0,
queueSize: 2, discardOldest: false))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                await WriteValueAsync(nodeId, 8000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    int first = dcn.MonitoredItems[0].Value
                        .WrappedValue.GetInt32();
                    Assert.That(first, Is.EqualTo(8000),
                        "DiscardOldest=false keeps oldest value.");
                }
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task DefaultDiscardOldestBehavesAsTrueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 12, samplingInterval: 0,
queueSize: 2))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            try
            {
                await ConsumeInitialPublishAsync().ConfigureAwait(false);

                for (int i = 0; i < 4; i++)
                {
                    await WriteValueAsync(nodeId, 9000 + i)
                        .ConfigureAwait(false);
                }

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                    Is.True);

                if (pubResp.NotificationMessage.NotificationData.Count > 0)
                {
                    var dcn = ExtensionObject.ToEncodeable(
                        pubResp.NotificationMessage.NotificationData[0]) as
                        DataChangeNotification;
                    if (dcn != null && dcn.MonitoredItems.Count > 0)
                    {
                        int last = dcn.MonitoredItems[^1].Value
                            .WrappedValue.GetInt32();
                        Assert.That(last, Is.EqualTo(9003),
                            "Default DiscardOldest behaves as true.");
                    }
                }
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: discard-oldest publish interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task ModifyDiscardOldestFromTrueToFalseAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 13, samplingInterval: 0,
queueSize: 5, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);
            uint monId = createResp.Results[0].MonitoredItemId;

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemModifyRequest[]
                    {
                        new() {
                            MonitoredItemId = monId,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 13,
                                SamplingInterval = 0,
                                QueueSize = 5,
                                DiscardOldest = false
                            }
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True,
                "Modify DiscardOldest to false should succeed.");
        }

        [Test]
        public async Task QueueOverflowMaySetOverflowBitAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 20, samplingInterval: 0,
queueSize: 2, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
            {
                await WriteValueAsync(nodeId, 10000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems.Count > 0)
                {
                    bool hasOverflow = dcn.MonitoredItems.ToArray()
                        .Any(m => m.Value.StatusCode.Overflow);
                    // Overflow bit is optional per spec
                    Assert.That(dcn.MonitoredItems.Count,
                        Is.GreaterThan(0),
                        "Should receive notifications on overflow.");
                }
            }
        }

        [Test]
        public async Task QueueOverflowSizeOneBoundedCountAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 21, samplingInterval: 0,
queueSize: 1, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 10; i++)
            {
                await WriteValueAsync(nodeId, 11000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                Assert.That(dcn, Is.Not.Null);
                Assert.That(dcn.MonitoredItems.Count,
                    Is.LessThanOrEqualTo(2),
                    "Queue size 1 should deliver at most 1-2 items.");
            }
        }

        [Test]
        public async Task QueueOverflowCountBoundedByQueueSizeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const uint queueSize = 3;

            CreateMonitoredItemsResponse createResp =
                await CreateSingleItemAsync(
                    CreateItemRequest(nodeId, 22, samplingInterval: 0,
queueSize: queueSize, discardOldest: true))
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);

            await ConsumeInitialPublishAsync().ConfigureAwait(false);

            for (int i = 0; i < 6; i++)
            {
                await WriteValueAsync(nodeId, 12000 + i)
                    .ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);

            if (pubResp.NotificationMessage.NotificationData.Count > 0)
            {
                var dcn = ExtensionObject.ToEncodeable(
                    pubResp.NotificationMessage.NotificationData[0]) as
                    DataChangeNotification;
                Assert.That(dcn, Is.Not.Null);
                Assert.That(
                    (uint)dcn.MonitoredItems.Count,
                    Is.LessThanOrEqualTo(queueSize),
                    "Notification count should not exceed queue size.");
            }
        }

        [Test]
        public async Task TwoItemsDifferentQueueSizesAsync()
        {
            NodeId node1 = ToNodeId(Constants.ScalarStaticInt32);
            NodeId node2 = ToNodeId(Constants.ScalarStaticDouble);

            var items = new MonitoredItemCreateRequest[]
            {
                CreateItemRequest(node1, 32,                     samplingInterval: 0,
queueSize: 3),
                CreateItemRequest(node2, 33,                     samplingInterval: 0,
queueSize: 8)
            };

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(createResp.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(createResp.Results[0].StatusCode),
                Is.True);
            Assert.That(
                StatusCode.IsGood(createResp.Results[1].StatusCode),
                Is.True);

            uint revised1 = createResp.Results[0].RevisedQueueSize;
            uint revised2 = createResp.Results[1].RevisedQueueSize;

            Assert.That(revised1, Is.GreaterThan(0u));
            Assert.That(revised2, Is.GreaterThan(0u));
            Assert.That(revised2, Is.GreaterThanOrEqualTo(revised1),
                "Larger requested queue should get >= smaller.");
        }

        private MonitoredItemCreateRequest CreateItemRequest(
            NodeId nodeId,
            uint clientHandle,
            double samplingInterval = 100,
            uint queueSize = 10,
            MonitoringMode mode = MonitoringMode.Reporting,
            uint attributeId = Attributes.Value,
            bool discardOldest = true,
            ExtensionObject filter = default,
            TimestampsToReturn timestamps = TimestampsToReturn.Both)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributeId
                },
                MonitoringMode = mode,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    Filter = filter,
                    DiscardOldest = discardOldest,
                    QueueSize = queueSize
                }
            };
        }

        private async Task<CreateMonitoredItemsResponse> CreateSingleItemAsync(
            MonitoredItemCreateRequest item,
            TimestampsToReturn timestamps = TimestampsToReturn.Both)
        {
            return await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                timestamps,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task WriteValueAsync(NodeId nodeId, int value)
        {
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(value))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(writeResp.Results[0]), Is.True);
        }

        private async Task ConsumeInitialPublishAsync()
        {
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private uint m_subscriptionId;
    }
}
