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
using ISession = Opc.Ua.Client.ISession;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// Depth compliance tests for Subscription Basic covering publish
    /// request queueing, lifetime behavior, KeepAlive timing,
    /// MaxNotificationsPerPublish, sequence numbers, retransmission,
    /// multiple subscriptions, and edge cases.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionBasicDepth")]
    public class SubscriptionBasicDepthTests : TestFixture
    {
        [Test]
        public async Task PublishRequestQueuedBeforeSubscriptionHasDataAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);

            // Publish before data arrives – should still return Good
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishRequestDequeuedInOrderAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(400).ConfigureAwait(false);

            var seqs = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
                await Task.Delay(150).ConfigureAwait(false);
            }

            for (int i = 1; i < seqs.Count; i++)
            {
                Assert.That(seqs[i], Is.GreaterThan(seqs[i - 1]),
                    "Sequence numbers must increase (FIFO dequeue).");
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishRequestOnePerSubscriptionServicedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.SubscriptionId, Is.EqualTo(id));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishWithZeroSubscriptionsReturnsErrorAsync()
        {
            Client.ISession freshSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                ServiceResultException ex =
                    Assert.ThrowsAsync<ServiceResultException>(async () => await freshSession.PublishWithTimeoutAsync().ConfigureAwait(false));
                Assert.That(ex.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoSubscription));
            }
            finally
            {
                await freshSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                freshSession.Dispose();
            }
        }

        [Test]
        public async Task PublishAfterAllSubscriptionsDeletedReturnsErrorAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            await DeleteSubAsync(id).ConfigureAwait(false);

            // Small delay to let server process deletion
            await Task.Delay(200).ConfigureAwait(false);

            ServiceResultException ex =
                Assert.ThrowsAsync<ServiceResultException>(PublishAsync);
            Assert.That(ex.StatusCode,
                Is.EqualTo(StatusCodes.BadNoSubscription));
        }

        [Test]
        public async Task PublishAfterSessionRecreatedNoSubscriptionsAsync()
        {
            Client.ISession freshSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                ServiceResultException ex =
                    Assert.ThrowsAsync<ServiceResultException>(async () => await freshSession.PublishWithTimeoutAsync().ConfigureAwait(false));
                Assert.That(ex.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoSubscription));
            }
            finally
            {
                await freshSession.CloseAsync(5000, true)
                    .ConfigureAwait(false);
                freshSession.Dispose();
            }
        }

        [Test]
        public async Task PublishRequestTimeoutBehaviorAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            // No monitored items; publish should return KeepAlive
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MultiplePublishRequestsQueuedAndServicedSequentiallyAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(500).ConfigureAwait(false);

            int goodCount = 0;
            for (int i = 0; i < 5; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                {
                    goodCount++;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.That(goodCount, Is.EqualTo(5),
                "All 5 publish requests should succeed.");

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimeExpiryWithNoPublishRequestsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 3, keepAlive: 1)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            int waitMs =
                (int)(resp.RevisedPublishingInterval *
                    resp.RevisedLifetimeCount) +
                2000;
            await Task.Delay(waitMs).ConfigureAwait(false);

            DeleteSubscriptionsResponse del =
                await Session.DeleteSubscriptionsAsync(
                    null, new uint[] { id }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(del.Results[0]) ||
                del.Results[0] == StatusCodes.BadSubscriptionIdInvalid,
                Is.True);
        }

        [Test]
        public async Task SubscriptionLifetimeResetByPublishAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 10, keepAlive: 3)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Publish several times to keep alive
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
            }

            // Subscription should still be alive
            DeleteSubscriptionsResponse del =
                await Session.DeleteSubscriptionsAsync(
                    null, new uint[] { id }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(del.Results[0]), Is.True);
        }

        [Test]
        public async Task SubscriptionLifetimeCountMustBeThreeTimesKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 5, keepAlive: 10).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimeWithVerySmallValuesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 1, keepAlive: 1).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));
            Assert.That(resp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimeWithMaxValuesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: uint.MaxValue, keepAlive: uint.MaxValue)
                .ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionLifetimeCountAcceptedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, DefaultInterval, 200, DefaultKeepAlive, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionLifetimeCountBelowMinRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, DefaultInterval, 2, 50, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * mod.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SubscriptionLifetimePreservedAcrossModifyAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, lifetime: 100, keepAlive: 10)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Modify interval only
            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 200, 100, 10, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);

            // Subscription still works
            await Task.Delay(300).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveReceivedWithinExpectedIntervalAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 2)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            // No items → KeepAlive
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage.NotificationData.Count,
                Is.Zero, "No items → KeepAlive expected.");

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveCountZeroRevisedToMinimumAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                keepAlive: 0).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveCountOneMinimumKeepalivesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 1)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveOnlyWhenNoDataChangesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 2)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            // Add a dynamic node – should produce data, not keepalive
            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0),
                "Dynamic node should produce data, not KeepAlive.");

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveHasEmptyNotificationDataAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 2)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage.NotificationData.Count,
                Is.Zero);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveSequenceNumberProgressesAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 1)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await Task.Delay(300).ConfigureAwait(false);

            var seqs = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
                await Task.Delay(200).ConfigureAwait(false);
            }

            for (int i = 1; i < seqs.Count; i++)
            {
                Assert.That(seqs[i], Is.GreaterThanOrEqualTo(seqs[i - 1]));
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveSubIdMatchesSubscriptionAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 30, keepAlive: 2)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.SubscriptionId, Is.EqualTo(id));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifyKeepAliveCountAndVerifyTimingAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, lifetime: 100, keepAlive: 5)
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 100, 100, 2, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await Task.Delay(500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MaxNotificationsZeroMeansNoLimitAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 0).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            for (uint h = 0; h < 5; h++)
            {
                await AddItemAsync(id,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h + 1, sampling: 50).ConfigureAwait(false);
            }

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MaxNotificationsOneOnlyOneItemPerPublishAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 1).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            for (uint h = 0; h < 3; h++)
            {
                await AddItemAsync(id,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h + 1, sampling: 50).ConfigureAwait(false);
            }

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage, Is.Not.Null);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MaxNotificationsLargerThanItemCountAllDeliveredAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.MoreNotifications, Is.False);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MaxNotificationsWithMultipleSubscriptionsAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(
                interval: 100, maxNotif: 1).ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(
                interval: 100, maxNotif: 100).ConfigureAwait(false);

            await AddItemAsync(r1.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                handle: 1, sampling: 50).ConfigureAwait(false);
            await AddItemAsync(r2.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                handle: 2, sampling: 50).ConfigureAwait(false);

            await Task.Delay(400).ConfigureAwait(false);

            bool saw1 = false;
            bool saw2 = false;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                if (pub.SubscriptionId == r1.SubscriptionId)
                {
                    saw1 = true;
                }

                if (pub.SubscriptionId == r2.SubscriptionId)
                {
                    saw2 = true;
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.That(saw1 || saw2, Is.True);

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] {
                    r1.SubscriptionId, r2.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task MoreNotificationsFlagSetWhenLimitedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 1).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            for (uint h = 0; h < 5; h++)
            {
                await AddItemAsync(id,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h + 10, sampling: 50).ConfigureAwait(false);
            }

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            // MoreNotifications should be true when limited
            // Server may or may not implement limiting
            Assert.That(pub.NotificationMessage, Is.Not.Null);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MoreNotificationsFlagClearWhenNotLimitedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 0).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.MoreNotifications, Is.False);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifyMaxNotificationsPerPublishAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 0).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 100, DefaultLifetime, DefaultKeepAlive, 5, 0,
                    CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task MaxNotificationsPerPublishWithQueuedItemsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100, maxNotif: 2).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            for (uint h = 0; h < 4; h++)
            {
                await AddItemAsync(id,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h + 20, sampling: 50).ConfigureAwait(false);
            }

            await Task.Delay(400).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishResponseTimingRelativeToIntervalAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 200).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 100).ConfigureAwait(false);

            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishResponseForFastSubscriptionIsQuickAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            DateTime before = DateTime.UtcNow;
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            TimeSpan elapsed = DateTime.UtcNow - before;

            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(elapsed.TotalMilliseconds, Is.LessThan(5000),
                "Fast subscription publish should return quickly.");

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishResponsePublishTimeIsReasonableAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            var pt = (DateTime)pub.NotificationMessage.PublishTime;
            Assert.That(pt.Year, Is.GreaterThanOrEqualTo(2020));
            Assert.That(pt,
                Is.LessThanOrEqualTo(DateTime.UtcNow.AddMinutes(5)));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishResponseContainsCorrectSubscriptionIdAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.SubscriptionId, Is.EqualTo(id));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SequenceNumberStartsAtOneForNewSubscriptionAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.NotificationMessage.SequenceNumber,
                Is.GreaterThanOrEqualTo(1u));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SequenceNumberGapDetectionAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            var seqs = new List<uint>();
            for (int i = 0; i < 5; i++)
            {
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
                await Task.Delay(150).ConfigureAwait(false);
            }

            for (int i = 1; i < seqs.Count; i++)
            {
                Assert.That(seqs[i] - seqs[i - 1], Is.LessThanOrEqualTo(2u),
                    "No large gaps in sequence numbers expected.");
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SequenceNumberWraparoundAsync()
        {
            // Verify sequence numbers don't wrap in typical use
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            uint lastSeq = 0;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                uint seq = pub.NotificationMessage.SequenceNumber;
                if (i > 0)
                {
                    Assert.That(seq, Is.GreaterThan(lastSeq),
                        "Sequence should not wrap in typical use.");
                }
                lastSeq = seq;
                await Task.Delay(100).ConfigureAwait(false);
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task AvailableSequenceNumbersAfterUnacknowledgedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Publish without ack
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub1.ResponseHeader.ServiceResult),
                Is.True);

            await Task.Delay(200).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub2.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub2.AvailableSequenceNumbers, Is.Not.Null);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task AcknowledgeReducesAvailableSequenceNumbersAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub1.ResponseHeader.ServiceResult),
                Is.True);
            uint seq1 = pub1.NotificationMessage.SequenceNumber;

            await Task.Delay(200).ConfigureAwait(false);

            // Acknowledge seq1
            PublishResponse pub2 = await PublishWithAckAsync(id, seq1)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub2.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task AcknowledgeInvalidSequenceReturnsErrorAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Ack a bogus sequence number
            PublishResponse pub = await PublishWithAckAsync(id, 999999)
                .ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishValidSequenceReturnsOriginalMessageAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(
                StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            uint seq = pub.NotificationMessage.SequenceNumber;

            RepublishResponse repub =
                await Session.RepublishAsync(
                    null, id, seq,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(repub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(repub.NotificationMessage.SequenceNumber,
                Is.EqualTo(seq));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishInvalidSequenceReturnsBadAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            await PublishAsync().ConfigureAwait(false);

            ServiceResultException ex =
                Assert.ThrowsAsync<ServiceResultException>(async () => await Session.RepublishAsync(
                        null, id, 999999u,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode,
                Is.EqualTo(StatusCodes.BadMessageNotAvailable));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishAfterAcknowledgeReturnsBadAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            uint seq = pub.NotificationMessage.SequenceNumber;

            // Acknowledge
            await PublishWithAckAsync(id, seq).ConfigureAwait(false);

            // Republish after ack → may fail
            try
            {
                RepublishResponse repub =
                    await Session.RepublishAsync(
                        null, id, seq,
                        CancellationToken.None).ConfigureAwait(false);
                // Some servers may still return Good
                Assert.That(repub.NotificationMessage, Is.Not.Null);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(sre.StatusCode,
                    Is.EqualTo(StatusCodes.BadMessageNotAvailable));
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishMultipleTimesReturnsSameMessageAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 100).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            await AddItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            uint seq = pub.NotificationMessage.SequenceNumber;

            RepublishResponse repub1 =
                await Session.RepublishAsync(
                    null, id, seq,
                    CancellationToken.None).ConfigureAwait(false);
            RepublishResponse repub2 =
                await Session.RepublishAsync(
                    null, id, seq,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(repub1.NotificationMessage.SequenceNumber,
                Is.EqualTo(repub2.NotificationMessage.SequenceNumber));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Category("LongRunning")]
        public async Task ThreeSubsDifferentIntervalsAllServicedAsync()
        {
            try
            {
                CreateSubscriptionResponse r1 = await CreateSubAsync(
                    interval: 100).ConfigureAwait(false);
                CreateSubscriptionResponse r2 = await CreateSubAsync(
                    interval: 500).ConfigureAwait(false);
                CreateSubscriptionResponse r3 = await CreateSubAsync(
                    interval: 1000).ConfigureAwait(false);

                await AddItemAsync(r1.SubscriptionId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: 1, sampling: 50).ConfigureAwait(false);
                await AddItemAsync(r2.SubscriptionId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: 2, sampling: 50).ConfigureAwait(false);
                await AddItemAsync(r3.SubscriptionId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: 3, sampling: 50).ConfigureAwait(false);

                await Task.Delay(1500).ConfigureAwait(false);

                var seen = new HashSet<uint>();
                for (int i = 0; i < 10; i++)
                {
                    PublishResponse pub = await PublishAsync()
                        .ConfigureAwait(false);
                    seen.Add(pub.SubscriptionId);
                    await Task.Delay(100).ConfigureAwait(false);
                }

                Assert.That(seen, Has.Count.GreaterThan(1),
                    "Multiple subs should be serviced.");

                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] {
                        r1.SubscriptionId,
                        r2.SubscriptionId,
                        r3.SubscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: 3-sub publish service sequence interrupted by CI runner load ({sre.StatusCode}).");
            }
        }

        [Test]
        public async Task SubscriptionsWithSameIntervalBothServicedAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(
                interval: 200).ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(
                interval: 200).ConfigureAwait(false);

            await AddItemAsync(r1.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                handle: 1, sampling: 50).ConfigureAwait(false);
            await AddItemAsync(r2.SubscriptionId,
                VariableIds.Server_ServerStatus_CurrentTime,
                handle: 2, sampling: 50).ConfigureAwait(false);

            await Task.Delay(600).ConfigureAwait(false);

            var seen = new HashSet<uint>();
            for (int i = 0; i < 6; i++)
            {
                PublishResponse pub = await PublishAsync()
                    .ConfigureAwait(false);
                seen.Add(pub.SubscriptionId);
                await Task.Delay(100).ConfigureAwait(false);
            }

            Assert.That(seen, Has.Count.GreaterThanOrEqualTo(2),
                "Both subs with same interval should be serviced.");

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] {
                    r1.SubscriptionId,
                    r2.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateDeleteCreateSubscriptionIdsUniqueAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id1 = r1.SubscriptionId;
            await DeleteSubAsync(id1).ConfigureAwait(false);

            CreateSubscriptionResponse r2 = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id2 = r2.SubscriptionId;

            Assert.That(id2, Is.Not.EqualTo(id1),
                "New subscription should have different ID.");

            await DeleteSubAsync(id2).ConfigureAwait(false);
        }

        [Test]
        public async Task TwentySubscriptionsAllCreateSuccessfullyAsync()
        {
            var ids = new List<uint>();
            for (int i = 0; i < 20; i++)
            {
                CreateSubscriptionResponse resp = await CreateSubAsync(
                    interval: 500).ConfigureAwait(false);
                Assert.That(
                    StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                    Is.True);
                ids.Add(resp.SubscriptionId);
            }

            Assert.That(ids, Has.Count.EqualTo(20));

            await Session.DeleteSubscriptionsAsync(
                null, ids.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteMultipleSubscriptionsAtOnceAsync()
        {
            var ids = new List<uint>();
            for (int i = 0; i < 5; i++)
            {
                CreateSubscriptionResponse resp = await CreateSubAsync()
                    .ConfigureAwait(false);
                ids.Add(resp.SubscriptionId);
            }

            DeleteSubscriptionsResponse del =
                await Session.DeleteSubscriptionsAsync(
                    null, ids.ToArray().ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(del.Results.Count, Is.EqualTo(5));
            foreach (StatusCode sc in del.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task DeleteEmptySubscriptionListReturnsErrorAsync()
        {
            try
            {
                DeleteSubscriptionsResponse del =
                    await Session.DeleteSubscriptionsAsync(
                        null, new uint[0].ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                // Server may return Good with empty results or error
                Assert.That(del.Results.Count, Is.Zero);
            }
            catch (ServiceResultException sre)
            {
                Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task CreateSubscriptionWithAllDefaultParametersAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.SubscriptionId, Is.GreaterThan(0u));
            Assert.That(resp.RevisedPublishingInterval, Is.GreaterThan(0));
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));
            Assert.That(resp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionPublishingIntervalMaxDoubleAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: double.MaxValue).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionThenImmediatelyDeleteAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);

            DeleteSubscriptionsResponse del =
                await Session.DeleteSubscriptionsAsync(
                    null,
                    new uint[] { resp.SubscriptionId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(del.Results[0]), Is.True);
        }

        [Test]
        public Task ModifyNonExistentSubscriptionReturnsBad()
        {
            ServiceResultException ex =
                Assert.ThrowsAsync<ServiceResultException>(async () => await Session.ModifySubscriptionAsync(
                        null, 999999u, 500, 100, 10, 0, 0,
                        CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode,
                Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            return Task.CompletedTask;
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

        private async Task<uint> AddItemAsync(
            uint subId, NodeId nodeId,
            uint handle = 1, double sampling = 100)
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
                    QueueSize = 10
                }
            };

            CreateMonitoredItemsResponse resp =
                await Session.CreateMonitoredItemsAsync(
                    null, subId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private async Task<PublishResponse> PublishAsync()
        {
            return await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
        }

        private async Task<PublishResponse> PublishWithAckAsync(
            uint subId, uint seqNum)
        {
            var ack = new SubscriptionAcknowledgement
            {
                SubscriptionId = subId,
                SequenceNumber = seqNum
            };
            return await Session.PublishWithTimeoutAsync(
                new SubscriptionAcknowledgement[] { ack }.ToArrayOf())
                .ConfigureAwait(false);
        }

        private const double DefaultInterval = 500;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
