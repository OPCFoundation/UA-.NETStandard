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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for batch MonitoredItem operations including
    /// batch create, modify, and delete of monitored items.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("MonitoredItem")]
    [Category("MonitorItemsBatch")]
    public class MonitorItemsBatchTests : TestFixture
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
        public async Task BatchCreateTenItemsOnDifferentNodesAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 10; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(100 + i)));
            }

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(10));
            int goodCount = resp.Results.ToArray()
                .Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.EqualTo(10),
                "All 10 items on different nodes should be created.");
        }

        [Test]
        public async Task BatchCreateTenItemsOnSameNodeAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 10; i++)
            {
                items.Add(CreateItemRequest(nodeId, (uint)(200 + i)));
            }

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(10));
            int goodCount = resp.Results.ToArray()
                .Count(r => StatusCode.IsGood(r.StatusCode));
            Assert.That(goodCount, Is.GreaterThan(0),
                "At least some items on the same node should succeed.");
        }

        [Test]
        public async Task BatchCreateMixOfValidAndInvalidNodesAsync()
        {
            var items = new List<MonitoredItemCreateRequest>
            {
                CreateItemRequest(
                    ToNodeId(Constants.ScalarStaticInt32), 300),
                CreateItemRequest(
                    Constants.InvalidNodeId, 301),
                CreateItemRequest(
                    ToNodeId(Constants.ScalarStaticDouble), 302),
                CreateItemRequest(
                    Constants.InvalidNodeId, 303),
                CreateItemRequest(
                    ToNodeId(Constants.ScalarStaticString), 304)
            };

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(5));

            // Valid nodes should succeed
            Assert.That(
                StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(resp.Results[2].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(resp.Results[4].StatusCode), Is.True);

            // Invalid nodes should fail
            Assert.That(
                StatusCode.IsGood(resp.Results[1].StatusCode), Is.False);
            Assert.That(
                StatusCode.IsGood(resp.Results[3].StatusCode), Is.False);
        }

        [Test]
        public async Task BatchCreateWithVaryingSamplingIntervalsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            double[] intervals =
                [0, 50, 100, 250, 500, 1000, 2000, 5000, 10000, -1];
            for (int i = 0; i < 10; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(
                    ToNodeId(eni), (uint)(400 + i),
                    samplingInterval: intervals[i]));
            }

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(resp.Results.Count, Is.EqualTo(10));
            foreach (MonitoredItemCreateResult r in resp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                Assert.That(r.RevisedSamplingInterval,
                    Is.GreaterThanOrEqualTo(0.0));
            }
        }

        [Test]
        public async Task BatchModifyTenItemsSamplingIntervalAsync()
        {
            // Create 10 items first
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 10; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(
                    ToNodeId(eni), (uint)(500 + i), samplingInterval: 1000));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(10));

            // Build modify requests
            var modItems = new List<MonitoredItemModifyRequest>();
            for (int i = 0; i < 10; i++)
            {
                modItems.Add(new MonitoredItemModifyRequest
                {
                    MonitoredItemId = createResp.Results[i].MonitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(500 + i),
                        SamplingInterval = 250,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                });
            }

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    modItems.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(10));
            foreach (MonitoredItemModifyResult r in modResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                Assert.That(r.RevisedSamplingInterval,
                    Is.GreaterThanOrEqualTo(0.0));
            }
        }

        [Test]
        public async Task BatchModifyTenItemsQueueSizeAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 10; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(
                    ToNodeId(eni), (uint)(600 + i), queueSize: 5));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(10));

            var modItems = new List<MonitoredItemModifyRequest>();
            for (int i = 0; i < 10; i++)
            {
                modItems.Add(new MonitoredItemModifyRequest
                {
                    MonitoredItemId = createResp.Results[i].MonitoredItemId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(600 + i),
                        SamplingInterval = 100,
                        QueueSize = 20,
                        DiscardOldest = true
                    }
                });
            }

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    modItems.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(10));
            foreach (MonitoredItemModifyResult r in modResp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                Assert.That(r.RevisedQueueSize, Is.GreaterThan(0u));
            }
        }

        [Test]
        public async Task BatchModifyMixOfValidAndInvalidIdsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[]
                    {
                        CreateItemRequest(nodeId, 700)
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            uint validId = createResp.Results[0].MonitoredItemId;

            var modItems = new MonitoredItemModifyRequest[]
            {
                new() {
                    MonitoredItemId = validId,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 700,
                        SamplingInterval = 500,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                },
                new() {
                    MonitoredItemId = 999999,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 701,
                        SamplingInterval = 500,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                }
            };

            ModifyMonitoredItemsResponse modResp =
                await Session.ModifyMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    modItems.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(modResp.Results.Count, Is.EqualTo(2));
            Assert.That(
                StatusCode.IsGood(modResp.Results[0].StatusCode), Is.True);
            Assert.That(
                StatusCode.IsGood(modResp.Results[1].StatusCode), Is.False,
                "Invalid monitored item ID should fail.");
        }

        [Test]
        public async Task BatchDeleteTenItemsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 10; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(ToNodeId(eni), (uint)(800 + i)));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(createResp.Results.Count, Is.EqualTo(10));

            uint[] monIds = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];

            DeleteMonitoredItemsResponse delResp =
                await Session.DeleteMonitoredItemsAsync(
                    null, m_subscriptionId, monIds.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(delResp.Results.Count, Is.EqualTo(10));
            foreach (StatusCode sc in delResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task BatchDeleteMixOfValidAndInvalidIdsAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[]
                    {
                        CreateItemRequest(nodeId, 900)
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            uint validId = createResp.Results[0].MonitoredItemId;

            uint[] ids = [validId, 999998, 999999];

            DeleteMonitoredItemsResponse delResp =
                await Session.DeleteMonitoredItemsAsync(
                    null, m_subscriptionId, ids.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(delResp.Results.Count, Is.EqualTo(3));
            Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);
            Assert.That(StatusCode.IsGood(delResp.Results[1]), Is.False);
            Assert.That(StatusCode.IsGood(delResp.Results[2]), Is.False);
        }

        [Test]
        public async Task BatchDeleteThenVerifyNoMoreNotificationsAsync()
        {
            var items = new List<MonitoredItemCreateRequest>();
            for (int i = 0; i < 5; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(CreateItemRequest(
                    ToNodeId(eni), (uint)(1000 + i), samplingInterval: 50));
            }

            CreateMonitoredItemsResponse createResp =
                await Session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    items.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            // Consume initial notifications
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Delete all items
            uint[] monIds = [.. createResp.Results.ToArray().Select(r => r.MonitoredItemId)];
            await Session.DeleteMonitoredItemsAsync(
                null, m_subscriptionId, monIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // Write to trigger potential notifications
            NodeId writeNode = ToNodeId(Constants.ScalarStaticInt32);
            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = writeNode,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(42))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pubResp.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "After deleting all items, publish should return keep-alive.");
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

        private uint m_subscriptionId;
    }
}
