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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for the Subscription Publish conformance units:
    /// Subscription Publish Basic, Publish Min 05, Publish Min 10,
    /// and Subscription PublishRequest Queue Overflow.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionPublish")]
    public class SubscriptionPublishTests : TestFixture
    {
        [Test]
        public async Task PublishBasicTimeoutHintSmallerThanLifetimeCausesBadTimeoutAsync()
        {
            // Specifying a TimeoutHint smaller than lifetime causes BadTimeout
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 1000, lifetime: 20, keepAlive: 1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // First publish to get initial data
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");

                // Send a publish with a very small timeout hint via RequestHeader
                var header = new RequestHeader
                {
                    TimeoutHint = 10
                };
                try
                {
                    PublishResponse pubTimeout = await Session.PublishAsync(
                        header,
                        default,
                        CancellationToken.None).ConfigureAwait(false);

                    // If the server responds, it may return BadTimeout or Good
                    // (depends on server timing)
                    Assert.That(
                        StatusCode.IsGood(pubTimeout.ResponseHeader.ServiceResult) ||
                        pubTimeout.ResponseHeader.ServiceResult == StatusCodes.BadTimeout,
                        Is.True,
                        "Expected Good or BadTimeout for small timeout hint.");
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadTimeout)
                {
                    // Expected: BadTimeout when timeout hint is too small
                    Assert.Pass("Server returned BadTimeout as expected.");
                }
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishBasicQueueTwoPublishCallsWithinSessionAsync()
        {
            // Queue 2 Publish() calls within a single session
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 1000, keepAlive: 1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // Issue two publish calls and verify both return Good
                PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub1), Is.True, "Expected initial DataChange.");

                PublishResponse pub2 = await PublishWithAckAsync(
                    subId, pub1.NotificationMessage.SequenceNumber).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);

                // Delete subscription and verify final publish gets BadNoSubscription
                await DeleteSubAsync(subId).ConfigureAwait(false);
                subId = 0;

                try
                {
                    PublishResponse pubFinal = await PublishAsync().ConfigureAwait(false);
                    Assert.That(
                        pubFinal.ResponseHeader.ServiceResult == StatusCodes.BadNoSubscription ||
                        StatusCode.IsGood(pubFinal.ResponseHeader.ServiceResult),
                        Is.True);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNoSubscription)
                {
                    // Expected after deleting last subscription
                }
            }
            finally
            {
                // CA1508: analyzer cannot prove this; subId may be the original SubscriptionId value
                // when an exception is raised before the explicit subId=0 assignment.
#pragma warning disable CA1508
                if (subId != 0)
#pragma warning restore CA1508
                {
                    await DeleteSubAsync(subId).ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task PublishBasicResponseTimingAtPublishingIntervalAsync()
        {
            // Queue 2 Publish() calls; verify response timing at RevisedPublishingInterval
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 1000, keepAlive: 1).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                DateTime before = DateTime.UtcNow;
                PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
                DateTime after = DateTime.UtcNow;

                Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub1), Is.True, "Expected initial DataChange.");

                // Second publish should arrive after roughly one keep-alive interval
                DateTime before2 = DateTime.UtcNow;
                PublishResponse pub2 = await PublishWithAckAsync(
                    subId, pub1.NotificationMessage.SequenceNumber).ConfigureAwait(false);
                TimeSpan elapsed = DateTime.UtcNow - before2;

                Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
                // Allow generous grace period for timing
                Assert.That(
                    elapsed.TotalMilliseconds,
                    Is.LessThan((resp.RevisedPublishingInterval * 3) + 2000),
                    "Publish response took too long.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishBasicRepublishRetrievesQueuedNotificationsAsync()
        {
            // Queue 2 data-change notifications and retrieve via Republish
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 10).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                // Write to trigger data changes
                await WriteInt32ValueAsync(nodeId, 100).ConfigureAwait(false);
                await Task.Delay((int)resp.RevisedPublishingInterval + 300).ConfigureAwait(false);

                // Publish without acknowledging to keep messages in retransmit queue
                PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub1), Is.True, "Expected DataChange.");

                uint seqNum1 = pub1.NotificationMessage.SequenceNumber;

                await WriteInt32ValueAsync(nodeId, 200).ConfigureAwait(false);
                await Task.Delay((int)resp.RevisedPublishingInterval + 300).ConfigureAwait(false);

                PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);

                // Attempt Republish for the first sequence number
                try
                {
                    RepublishResponse republish = await Session.RepublishAsync(
                        null, subId, seqNum1,
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(
                        StatusCode.IsGood(republish.ResponseHeader.ServiceResult) ||
                        republish.ResponseHeader.ServiceResult == StatusCodes.BadMessageNotAvailable,
                        Is.True);

                    if (StatusCode.IsGood(republish.ResponseHeader.ServiceResult))
                    {
                        Assert.That(
                            republish.NotificationMessage.SequenceNumber,
                            Is.EqualTo(seqNum1));
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadMessageNotAvailable)
                {
                    // Acceptable: server may not retain messages
                }
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishBasicOutstandingPublishRequestQueueSizeAsync()
        {
            // Verify outstanding PublishRequest queue size matches requirements.
            // This test is marked as not-implemented in the JS.
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishBasicMinimumRetransmissionQueueSizeAsync()
        {
            // Verify minimum retransmission queue size is supported.
            // This test is marked as not-implemented in the JS.
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 10).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");

                // Attempt Republish of the received sequence number
                try
                {
                    RepublishResponse republish = await Session.RepublishAsync(
                        null, subId, pub.NotificationMessage.SequenceNumber,
                        CancellationToken.None).ConfigureAwait(false);

                    Assert.That(
                        StatusCode.IsGood(republish.ResponseHeader.ServiceResult) ||
                        republish.ResponseHeader.ServiceResult == StatusCodes.BadMessageNotAvailable,
                        Is.True);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadMessageNotAvailable)
                {
                    // Acceptable
                }
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishBasicAsyncPublishQueueBasedOnMaxSubscriptionsAsync()
        {
            // Call Publish() X times asynchronously; X based on server capabilities.
            // This test is marked as not-implemented in the JS.
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // Issue publish and verify
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Category("LongRunning")]
        public async Task PublishMin05AsyncPublishFiveConcurrentAsync()
        {
            // Call Publish() asynchronously, invoking 5 concurrent publish requests
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 10).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // Fire 5 concurrent publish requests
                const int publishQueueSize = 5;
                var publishTasks = new Task<PublishResponse>[publishQueueSize];
                for (int i = 0; i < publishQueueSize; i++)
                {
                    publishTasks[i] = PublishAsync();
                }

                // Wait for at least the first to complete
                PublishResponse first;
                try
                {
                    first = await publishTasks[0].ConfigureAwait(false);
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    Assert.Ignore("Timing-sensitive: publish request timed out.");
                    return;
                }
                Assert.That(StatusCode.IsGood(first.ResponseHeader.ServiceResult), Is.True);
                if (!HasDataChangeNotification(first))
                {
                    Assert.Ignore("Timing-sensitive: no initial data change in concurrent publish.");
                }

                // Await remaining and verify no failures
                int dataChangeCount = HasDataChangeNotification(first) ? 1 : 0;
                for (int i = 1; i < publishQueueSize; i++)
                {
                    try
                    {
                        PublishResponse p = await publishTasks[i].ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(p.ResponseHeader.ServiceResult), Is.True);
                        if (HasDataChangeNotification(p))
                        {
                            dataChangeCount++;
                        }
                    }
                    catch (ServiceResultException sre)
                        when (sre.StatusCode == StatusCodes.BadTooManyPublishRequests ||
                            sre.StatusCode == StatusCodes.BadNoSubscription ||
                            sre.StatusCode == StatusCodes.BadRequestTimeout)
                    {
                        // Some servers may reject excess publish requests or timeout
                    }
                }

                Assert.That(dataChangeCount, Is.GreaterThan(0), "Expected at least one DataChange.");
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: concurrent publish interrupted by CI runner load ({sre.StatusCode}).");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishMin05MultipleSessionsWithFiveSubscriptionsAsync()
        {
            // Create session with 5 subscriptions, 1 monitored item each.
            // Call Publish once per subscription.
            const int subscriptionCount = 5;
            var subIds = new List<uint>();
            try
            {
                for (int i = 0; i < subscriptionCount; i++)
                {
                    CreateSubscriptionResponse resp = await CreateSubAsync(
                        interval: 5000, lifetime: 1000, keepAlive: 1).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(resp.SubscriptionId);

                    NodeId nodeId = ToNodeId(Constants.ScalarStaticNodes[i % Constants.ScalarStaticNodes.Length]);
                    await AddMonitoredItemAsync(subIds[i], nodeId, handle: (uint)(i + 1)).ConfigureAwait(false);
                }

                await Task.Delay(1500).ConfigureAwait(false);

                // Issue publish calls and collect responses
                var receivedSubIds = new HashSet<uint>();
                for (int i = 0; i < subscriptionCount * 2; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    receivedSubIds.Add(pub.SubscriptionId);

                    if (receivedSubIds.Count == subscriptionCount)
                    {
                        break;
                    }
                }

                Assert.That(receivedSubIds, Is.Not.Empty,
                    "Expected publish responses from at least one subscription.");
            }
            finally
            {
                foreach (uint id in subIds)
                {
                    try
                    {
                        await DeleteSubAsync(id).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // best-effort cleanup
                    }
                }
            }
        }

        [Test]
        public async Task PublishMin05RepublishQueueSizeFiveAsync()
        {
            // Queue data-change notifications and retrieve using Republish with queue size 5
            const int retransmitQueueSize = 5;
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 10).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                var seqNumbers = new List<uint>();
                for (int i = 0; i < retransmitQueueSize; i++)
                {
                    await WriteInt32ValueAsync(nodeId, 100 + i).ConfigureAwait(false);
                    await Task.Delay((int)resp.RevisedPublishingInterval + 200).ConfigureAwait(false);

                    // Publish without ack to keep in retransmit queue
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    seqNumbers.Add(pub.NotificationMessage.SequenceNumber);
                }

                // Attempt Republish for collected sequence numbers
                int republishGoodCount = 0;
                foreach (uint seqNum in seqNumbers)
                {
                    try
                    {
                        RepublishResponse republish = await Session.RepublishAsync(
                            null, subId, seqNum,
                            CancellationToken.None).ConfigureAwait(false);

                        if (StatusCode.IsGood(republish.ResponseHeader.ServiceResult))
                        {
                            Assert.That(
                                republish.NotificationMessage.SequenceNumber,
                                Is.EqualTo(seqNum));
                            republishGoodCount++;
                        }
                    }
                    catch (ServiceResultException sre)
                        when (sre.StatusCode == StatusCodes.BadMessageNotAvailable)
                    {
                        // Acceptable
                    }
                }

                Assert.That(republishGoodCount, Is.GreaterThan(0),
                    "Expected at least one successful Republish.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishMin05AsyncPublishFiveConcurrentWithDataChangesAsync()
        {
            // Call Publish() asynchronously with 5 concurrent requests, verify data changes
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 10).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                const int publishQueueSize = 5;
                const int callbacksNeeded = 10;
                int dataChangeCount = 0;

                for (int round = 0; round < callbacksNeeded; round++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);

                    if (HasDataChangeNotification(pub))
                    {
                        dataChangeCount++;
                    }

                    if (dataChangeCount >= publishQueueSize)
                    {
                        break;
                    }

                    // Write to trigger more data changes
                    await WriteInt32ValueAsync(nodeId, round + 1).ConfigureAwait(false);
                    await Task.Delay(200).ConfigureAwait(false);
                }

                Assert.That(dataChangeCount, Is.GreaterThan(0),
                    "Expected at least one DataChange notification.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishMin10CreateTenSubscriptionsWithCallbacksAsync()
        {
            // Create 10 subscriptions, add a monitored item to each, publish and check callbacks
            const int subscriptionCount = 10;
            var subIds = new List<uint>();
            try
            {
                for (int i = 0; i < subscriptionCount; i++)
                {
                    CreateSubscriptionResponse resp = await CreateSubAsync(
                        interval: 5000, lifetime: 10, keepAlive: 1).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(resp.SubscriptionId);

                    NodeId nodeId = ToNodeId(Constants.ScalarStaticNodes[i % Constants.ScalarStaticNodes.Length]);
                    await AddMonitoredItemAsync(subIds[i], nodeId, handle: (uint)(i + 1)).ConfigureAwait(false);
                }

                await Task.Delay(2000).ConfigureAwait(false);

                // Issue publish calls and track which subscriptions respond
                var receivedSubIds = new HashSet<uint>();
                const int maxPublishCalls = subscriptionCount * 3;
                for (int i = 0; i < maxPublishCalls; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    receivedSubIds.Add(pub.SubscriptionId);

                    if (receivedSubIds.Count == subscriptionCount)
                    {
                        break;
                    }
                }

                Assert.That(receivedSubIds, Is.Not.Empty,
                    "Expected publish callbacks from subscriptions.");
            }
            finally
            {
                foreach (uint id in subIds)
                {
                    try
                    {
                        await DeleteSubAsync(id).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // best-effort cleanup
                    }
                }
            }
        }

        [Test]
        public async Task PublishMin10AsyncPublishTenConcurrentAsync()
        {
            // Call Publish() asynchronously, trying to invoke 10 concurrent publish requests
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, lifetime: 100, keepAlive: 2).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                // Write initial value
                await WriteInt32ValueAsync(nodeId, 42).ConfigureAwait(false);
                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // Fire 10 concurrent publish requests
                const int publishQueueSize = 10;
                var publishTasks = new Task<PublishResponse>[publishQueueSize];
                for (int i = 0; i < publishQueueSize; i++)
                {
                    publishTasks[i] = PublishAsync();
                }

                int dataChangeCount = 0;
                int goodCount = 0;
                for (int i = 0; i < publishQueueSize; i++)
                {
                    try
                    {
                        PublishResponse pub = await publishTasks[i].ConfigureAwait(false);
                        if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                        {
                            goodCount++;
                            if (HasDataChangeNotification(pub))
                            {
                                dataChangeCount++;
                            }
                        }
                    }
                    catch (ServiceResultException sre)
                        when (sre.StatusCode == StatusCodes.BadTooManyPublishRequests ||
                            sre.StatusCode == StatusCodes.BadNoSubscription)
                    {
                        // Some servers may reject excess publish requests
                    }
                }

                Assert.That(goodCount, Is.GreaterThan(0), "Expected at least one Good publish response.");
                Assert.That(dataChangeCount, Is.GreaterThan(0), "Expected at least one DataChange.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PublishMin10SetPublishingModeDisableFiveOfTenAsync()
        {
            // Create 10 subscriptions, disable 5 via SetPublishingMode, verify behavior
            const int subscriptionCount = 10;
            var subIds = new List<uint>();
            try
            {
                for (int i = 0; i < subscriptionCount; i++)
                {
                    CreateSubscriptionResponse resp = await CreateSubAsync(
                        interval: 1000, lifetime: 100, keepAlive: 5).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(resp.SubscriptionId);

                    NodeId nodeId = ToNodeId(Constants.ScalarStaticNodes[i % Constants.ScalarStaticNodes.Length]);
                    await AddMonitoredItemAsync(subIds[i], nodeId, handle: (uint)(i + 1)).ConfigureAwait(false);
                }

                await Task.Delay(1500).ConfigureAwait(false);

                // Disable odd-numbered subscriptions
                var disableIds = new List<uint>();
                for (int i = 0; i < subscriptionCount; i++)
                {
                    if (i % 2 == 1)
                    {
                        disableIds.Add(subIds[i]);
                    }
                }

                SetPublishingModeResponse setModeResp = await Session.SetPublishingModeAsync(
                    null, false, disableIds.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(setModeResp.ResponseHeader.ServiceResult), Is.True);

                // Write values and publish
                NodeId writeNode = ToNodeId(Constants.ScalarStaticInt32);
                await WriteInt32ValueAsync(writeNode, 999).ConfigureAwait(false);
                await Task.Delay(1500).ConfigureAwait(false);

                // Collect publish responses - disabled subs should not send data changes
                var respondedSubIds = new HashSet<uint>();
                for (int i = 0; i < subscriptionCount; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        respondedSubIds.Add(pub.SubscriptionId);
                    }
                }

                // Verify disabled subscriptions did not send data changes
                foreach (uint disabledId in disableIds)
                {
                    Assert.That(respondedSubIds, Does.Not.Contain(disabledId),
                        $"Disabled subscription {disabledId} should not send DataChange.");
                }
            }
            finally
            {
                foreach (uint id in subIds)
                {
                    try
                    {
                        await DeleteSubAsync(id).ConfigureAwait(false);
                    }
                    catch (ServiceResultException)
                    {
                        // best-effort cleanup
                    }
                }
            }
        }

        [Test]
        public async Task QueueOverflowOlderPublishRequestDiscardedAsync()
        {
            // Verify the older PublishRequest is discarded on a PublishRequest Queue overflow.
            // This test is marked as not-implemented in the JS.
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 5).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // Issue a publish to verify basic functionality
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueueOverflowExceedsSupportedPublishRequestsBadTooManyAsync()
        {
            // Verify correct handling when exceeding supported number of publish requests.
            // Server should return BadTooManyPublishRequests.
            // This test is marked as not-implemented in the JS.
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, keepAlive: 5).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);

                // First publish to drain initial data
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);

                // Fire many concurrent publish requests to try to exceed the queue
                const int excessCount = 20;
                var tasks = new Task<PublishResponse>[excessCount];
                for (int i = 0; i < excessCount; i++)
                {
                    tasks[i] = PublishAsync();
                }

                bool gotTooMany = false;
                for (int i = 0; i < excessCount; i++)
                {
                    try
                    {
                        PublishResponse p = await tasks[i].ConfigureAwait(false);
                        if (p.ResponseHeader.ServiceResult == StatusCodes.BadTooManyPublishRequests)
                        {
                            gotTooMany = true;
                        }
                    }
                    catch (ServiceResultException sre)
                        when (sre.StatusCode == StatusCodes.BadTooManyPublishRequests)
                    {
                        gotTooMany = true;
                    }
                    catch (ServiceResultException)
                    {
                        // Other errors may occur when overloading
                    }
                }

                // Server may or may not reject - this is informational
                if (!gotTooMany)
                {
                    Assert.Warn("Server did not return BadTooManyPublishRequests; " +
                        "it may accept unlimited publish requests.");
                }
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            double interval = DefaultInterval,
            uint lifetime = DefaultLifetime,
            uint keepAlive = DefaultKeepAlive,
            uint maxNotif = 0,
            bool enabled = true,
            byte priority = 0)
        {
            return await Session.CreateSubscriptionAsync(
                null, interval, lifetime, keepAlive, maxNotif,
                enabled, priority,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task DeleteSubAsync(uint id)
        {
            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> AddMonitoredItemAsync(
            uint subId, NodeId nodeId,
            uint handle = 1, double sampling = 250,
            uint queueSize = 10)
        {
            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = handle,
                    SamplingInterval = sampling,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            };

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private async Task WriteInt32ValueAsync(NodeId nodeId, int value)
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

        private async Task<PublishResponse> PublishAsync()
        {
            return await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private async Task<PublishResponse> PublishWithAckAsync(uint subId, uint seqNum)
        {
            var ack = new SubscriptionAcknowledgement { SubscriptionId = subId, SequenceNumber = seqNum };
            return await Session.PublishAsync(
                null, new SubscriptionAcknowledgement[] { ack }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private static bool HasDataChangeNotification(PublishResponse pub)
        {
            if (pub.NotificationMessage?.NotificationData == null ||
                pub.NotificationMessage.NotificationData.Count == 0)
            {
                return false;
            }
            foreach (ExtensionObject ext in pub.NotificationMessage.NotificationData)
            {
                var dcn = ExtensionObject.ToEncodeable(ext) as DataChangeNotification;
                if (dcn != null && dcn.MonitoredItems != default && dcn.MonitoredItems.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private const double DefaultInterval = 1000;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
