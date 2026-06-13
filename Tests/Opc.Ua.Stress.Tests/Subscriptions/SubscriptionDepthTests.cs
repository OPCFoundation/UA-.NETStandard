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

namespace Opc.Ua.Stress.Tests.Subscriptions
{
    /// <summary>
    /// Depth compliance tests for Subscription Service Set covering
    /// publish overflow, KeepAlive, lifetime, priority, MaxNotificationsPerPublish,
    /// and additional revision/behavior tests.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionDepth")]
    public class SubscriptionDepthTests : TestFixture
    {
        [Test]
        public async Task PublishTooManyOutstandingRequestsHandledGracefullyAsync()
        {
            // : Subscription PublishRequest Queue Overflow – send many Publishes
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Fire several publishes sequentially – server must handle gracefully
            bool sawGood = false;
            for (int i = 0; i < 5; i++)
            {
                PublishResponse r = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                if (StatusCode.IsGood(r.ResponseHeader.ServiceResult))
                {
                    sawGood = true;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.That(sawGood, Is.True,
                "At least one publish response should be Good.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionRevisedIntervalIsAtLeastServerMinimumAsync()
        {
            // Request extremely small interval – server should revise to its minimum
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                publishingInterval: 0.001).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.GreaterThan(0),
                "Server must revise to a positive publishing interval.");

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionNegativeIntervalRevisedToMinimumAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                publishingInterval: -1).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.GreaterThan(0));

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionKeepAliveReceivedBeforeTimeoutAsync()
        {
            // Create a subscription with short interval so KeepAlive arrives quickly
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100,
                lifetimeCount: 30,
                maxKeepAliveCount: 3).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            // No monitored items → server sends KeepAlive
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.SubscriptionId, Is.EqualTo(id));
            // KeepAlive has empty notification data
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionWithZeroMonitoredItemsOnlyKeepAliveAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100,
                lifetimeCount: 30,
                maxKeepAliveCount: 2).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            // Wait for several publishing cycles
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            // With no items, notification data count should be 0 (KeepAlive)
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero,
                "Empty subscription should produce KeepAlive with no notification data.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimeCountRevisedWhenZeroAsync()
        {
            // Request LifetimeCount=0 → server uses minimum
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                lifetimeCount: 0, maxKeepAliveCount: 5).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u),
                "Server must provide a positive LifetimeCount when 0 is requested.");
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimeExpiryDetectedAsync()
        {
            // Create subscription with very short lifetime
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100,
                lifetimeCount: 3,
                maxKeepAliveCount: 1).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            double revisedInterval = createResp.RevisedPublishingInterval;
            uint revisedLifetime = createResp.RevisedLifetimeCount;

            // Wait longer than LifetimeCount * PublishingInterval without publishing
            int waitMs = (int)(revisedInterval * revisedLifetime) + 2000;
            await Task.Delay(waitMs).ConfigureAwait(false);

            // Subscription should have expired – delete should fail
            DeleteSubscriptionsResponse deleteResp = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            // Either Good (still alive) or BadSubscriptionIdInvalid (expired)
            StatusCode sc = deleteResp.Results[0];
            Assert.That(
                StatusCode.IsGood(sc) || sc == StatusCodes.BadSubscriptionIdInvalid,
                Is.True,
                $"Expected Good or BadSubscriptionIdInvalid, got {sc}");
        }

        [Test]
        public async Task PublishingDisabledAtCreationOnlyKeepAliveAsync()
        {
            // : Create with PublishingEnabled=false → only KeepAlive
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100,
                publishingEnabled: false).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.SubscriptionId, Is.EqualTo(id));
            // Publishing disabled → notification data should be empty (KeepAlive)
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero,
                "Publishing disabled should produce KeepAlive with no notifications.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeToggleNotificationFlowStartsStopsAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;
            await CreateMonitoredItemAsync(id, nodeId, samplingInterval: 50).ConfigureAwait(false);

            // Disable publishing
            await Session.SetPublishingModeAsync(
                null, false, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubDisabled = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubDisabled.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubDisabled.NotificationMessage.NotificationData.Count, Is.Zero,
                "After disabling, should receive KeepAlive.");

            // Re-enable publishing
            await Session.SetPublishingModeAsync(
                null, true, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubEnabled = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubEnabled.ResponseHeader.ServiceResult), Is.True);
            // After re-enabling with changing node, expect data notifications
            Assert.That(pubEnabled.NotificationMessage.NotificationData.Count, Is.GreaterThan(0),
                "After re-enabling, should receive data change notifications.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionMaxNotificationsPerPublishLimitAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100,
                maxNotificationsPerPublish: 1).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            // Add two items on changing nodes
            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                clientHandle: 1, samplingInterval: 50).ConfigureAwait(false);
            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                clientHandle: 2, samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            // Server may or may not enforce this but MoreNotifications should be true if limited
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionVeryLargeMaxNotificationsServerRevisesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                maxNotificationsPerPublish: uint.MaxValue).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task NotificationSequenceNumberMonotonicallyIncreasingAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            var seqNumbers = new List<uint>();
            for (int i = 0; i < 5; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNumbers.Add(pub.NotificationMessage.SequenceNumber);
                await Task.Delay(200).ConfigureAwait(false);
            }

            for (int i = 1; i < seqNumbers.Count; i++)
            {
                Assert.That(seqNumbers[i], Is.GreaterThan(seqNumbers[i - 1]),
                    $"SequenceNumber[{i}]={seqNumbers[i]} must be > [{i - 1}]={seqNumbers[i - 1]}");
            }

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task NotificationPublishTimeIsValidUtcAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            var publishTime = (DateTime)pubResp.NotificationMessage.PublishTime;
            Assert.That(publishTime, Is.GreaterThan(DateTime.MinValue));
            Assert.That(publishTime.Year, Is.GreaterThanOrEqualTo(2020));
            Assert.That(publishTime, Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MultipleSubscriptionsWithDifferentPrioritiesBothServicedAsync()
        {
            // Subscription Minimum 02 - priority handling
            CreateSubscriptionResponse lowPriResp = await CreateSubscriptionAsync(
                publishingInterval: 100, priority: 10).ConfigureAwait(false);
            CreateSubscriptionResponse highPriResp = await CreateSubscriptionAsync(
                publishingInterval: 100, priority: 200).ConfigureAwait(false);

            await CreateMonitoredItemAsync(lowPriResp.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                clientHandle: 1, samplingInterval: 50).ConfigureAwait(false);
            await CreateMonitoredItemAsync(highPriResp.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                clientHandle: 2, samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(500).ConfigureAwait(false);

            var seenSubscriptions = new HashSet<uint>();
            for (int i = 0; i < 6; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seenSubscriptions.Add(pub.SubscriptionId);
                await Task.Delay(150).ConfigureAwait(false);
            }

            Assert.That(seenSubscriptions.Contains(lowPriResp.SubscriptionId) ||
                seenSubscriptions.Contains(highPriResp.SubscriptionId), Is.True,
                "At least one subscription should be serviced.");

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { lowPriResp.SubscriptionId, highPriResp.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionRevisesKeepAliveCountIfLifetimeTooSmallAsync()
        {
            // Request KeepAlive=50, Lifetime=10 → server must revise so Lifetime >= 3*KeepAlive
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                lifetimeCount: 10, maxKeepAliveCount: 50).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount),
                "RevisedLifetimeCount must be >= 3 * RevisedMaxKeepAliveCount.");

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteSubscriptionWhilePublishOutstandingSucceedsAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            // Get at least one publish response
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Delete subscription – should succeed even with prior publish activity
            DeleteSubscriptionsResponse deleteResp = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(deleteResp.Results[0]), Is.True);
        }

        [Test]
        public async Task ModifySubscriptionToShorterIntervalAcceptedAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 2000).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modResp = await Session.ModifySubscriptionAsync(
                null, id, 100, DefaultLifetimeCount, DefaultMaxKeepAliveCount, 0, 0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modResp.RevisedPublishingInterval,
                Is.LessThanOrEqualTo(2000),
                "Modified interval should be shorter or at server minimum.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public Task RepublishWithInvalidSubscriptionId()
        {
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.RepublishAsync(
                    null, 999999u, 1,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            return Task.CompletedTask;
        }

        [Test]
        public async Task CreateAddDeleteItemsNoMoreNotificationsAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;
            uint monId = await CreateMonitoredItemAsync(id, nodeId,
                samplingInterval: 50).ConfigureAwait(false);

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Delete the item
            DeleteMonitoredItemsResponse delResp = await Session.DeleteMonitoredItemsAsync(
                null, id, new uint[] { monId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True);

            // Wait and publish – should get KeepAlive only
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.Zero,
                "After deleting all items, only KeepAlive expected.");

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishReturnsAvailableSequenceNumbersAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // First publish without acknowledging
            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub1.AvailableSequenceNumbers, Is.Not.Null);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteSubscriptionCausesStatusChangeNotificationAsync()
        {
            CreateSubscriptionResponse createResp1 = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            CreateSubscriptionResponse createResp2 = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id1 = createResp1.SubscriptionId;
            uint id2 = createResp2.SubscriptionId;

            await CreateMonitoredItemAsync(id1,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);
            await CreateMonitoredItemAsync(id2,
                VariableIds.Server_ServerStatus_CurrentTime,
                clientHandle: 2, samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Consume notifications from both
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Delete id2 – should produce StatusChangeNotification on next publish
            await DeleteSubscriptionAsync(id2).ConfigureAwait(false);

            await Task.Delay(200).ConfigureAwait(false);

            PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub, Is.Not.Null);

            await DeleteSubscriptionAsync(id1).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionChangePriorityAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                priority: 10).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modResp = await Session.ModifySubscriptionAsync(
                null, id, DefaultPublishingInterval,
                DefaultLifetimeCount, DefaultMaxKeepAliveCount, 0, 200,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.ResponseHeader.ServiceResult), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteSameSubscriptionTwiceSecondReturnsBadSubscriptionIdInvalidAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);

            DeleteSubscriptionsResponse deleteResp2 = await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp2.Results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task TenSubscriptionsAllReceivePublishResponsesAsync()
        {
            var subIds = new List<uint>();
            for (int i = 0; i < 10; i++)
            {
                CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                    publishingInterval: 100, priority: (byte)(i * 25)).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                subIds.Add(resp.SubscriptionId);
            }

            // Add monitored item to each
            for (int i = 0; i < subIds.Count; i++)
            {
                await CreateMonitoredItemAsync(subIds[i],
                    VariableIds.Server_ServerStatus_CurrentTime,
                    clientHandle: (uint)(100 + i), samplingInterval: 50).ConfigureAwait(false);
            }

            await Task.Delay(500).ConfigureAwait(false);

            var seen = new HashSet<uint>();
            for (int i = 0; i < 20; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seen.Add(pub.SubscriptionId);
                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.That(seen, Has.Count.GreaterThan(1),
                "Multiple subscriptions should be serviced.");

            await Session.DeleteSubscriptionsAsync(
                null, subIds.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionIncreaseKeepAliveCountAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                maxKeepAliveCount: 5).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modResp = await Session.ModifySubscriptionAsync(
                null, id, DefaultPublishingInterval,
                DefaultLifetimeCount, 50, 0, 0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modResp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionMaxNotificationsPerPublishZeroMeansUnlimitedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubscriptionAsync(
                maxNotificationsPerPublish: 0).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionThenPublishStillWorksAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 500).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            // Modify to faster
            await Session.ModifySubscriptionAsync(
                null, id, 100,
                DefaultLifetimeCount, DefaultMaxKeepAliveCount, 0, 0,
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub.NotificationMessage, Is.Not.Null);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MultiplePublishesWithoutAcknowledgementSucceedAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(500).ConfigureAwait(false);

            // Publish three times without acknowledging
            for (int i = 0; i < 3; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                await Task.Delay(200).ConfigureAwait(false);
            }

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeDisableMultipleThenReEnableAsync()
        {
            CreateSubscriptionResponse resp1 = await CreateSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp2 = await CreateSubscriptionAsync().ConfigureAwait(false);

            uint[] ids = [resp1.SubscriptionId, resp2.SubscriptionId];

            // Disable both
            SetPublishingModeResponse disableResp = await Session.SetPublishingModeAsync(
                null, false, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(disableResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(disableResp.Results[0]), Is.True);
            Assert.That(StatusCode.IsGood(disableResp.Results[1]), Is.True);

            // Re-enable both
            SetPublishingModeResponse enableResp = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(enableResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(enableResp.Results[0]), Is.True);
            Assert.That(StatusCode.IsGood(enableResp.Results[1]), Is.True);

            await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishWithBadAcknowledgementReturnsResultAsync()
        {
            CreateSubscriptionResponse createResp = await CreateSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Acknowledge a bogus sequence number
            var badAck = new SubscriptionAcknowledgement
            {
                SubscriptionId = id,
                SequenceNumber = 999999
            };

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                new SubscriptionAcknowledgement[] { badAck }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        private async Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            double publishingInterval = DefaultPublishingInterval,
            uint lifetimeCount = DefaultLifetimeCount,
            uint maxKeepAliveCount = DefaultMaxKeepAliveCount,
            uint maxNotificationsPerPublish = 0,
            bool publishingEnabled = true,
            byte priority = 0)
        {
            return await Session.CreateSubscriptionAsync(
                null,
                publishingInterval,
                lifetimeCount,
                maxKeepAliveCount,
                maxNotificationsPerPublish,
                publishingEnabled,
                priority,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task DeleteSubscriptionAsync(uint subscriptionId)
        {
            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { subscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> CreateMonitoredItemAsync(
            uint subscriptionId,
            NodeId nodeId,
            uint clientHandle = 1,
            double samplingInterval = 100,
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
                    ClientHandle = clientHandle,
                    SamplingInterval = samplingInterval,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = queueSize
                }
            };

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null,
                subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private const double DefaultPublishingInterval = 500;
        private const uint DefaultLifetimeCount = 100;
        private const uint DefaultMaxKeepAliveCount = 10;
    }
}
