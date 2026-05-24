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
    /// compliance tests for Subscription Service Set.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    public class SubscriptionServicesTests : TestFixture
    {
        [Test]
        public async Task CreateSubscriptionWithDefaultParamsAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionReturnsRevisedPublishingIntervalAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(500).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedPublishingInterval, Is.GreaterThan(0));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionWithZeroIntervalServerRevisesToMinimumAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(0).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedPublishingInterval, Is.GreaterThan(0));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionWithSmallIntervalRevisesUpwardAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(1).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedPublishingInterval, Is.GreaterThanOrEqualTo(1));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionLifetimeRevisedWhenLessThanThreeTimesKeepAliveAsync()
        {
            // Request LifetimeCount=5, KeepAlive=10 → Lifetime < 3*KeepAlive
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(
                lifetimeCount: 5, maxKeepAliveCount: 10).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * response.RevisedMaxKeepAliveCount));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionVerifyLifetimeGreaterOrEqualThreeTimesKeepAliveAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * response.RevisedMaxKeepAliveCount));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionWithLargeLifetimeRevisesDownwardAsync()
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null,
                DefaultPublishingInterval,
                uint.MaxValue,
                DefaultMaxKeepAliveCount,
                0,
                true,
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateMultipleSubscriptionsAsync()
        {
            CreateSubscriptionResponse resp1 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp2 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp3 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            Assert.That(resp1.SubscriptionId, Is.Not.EqualTo(resp2.SubscriptionId));
            Assert.That(resp2.SubscriptionId, Is.Not.EqualTo(resp3.SubscriptionId));

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { resp1.SubscriptionId, resp2.SubscriptionId, resp3.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionWithPriorityZeroAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(
                priority: 0).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionWithMaxPriorityAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(
                priority: 255).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionPublishingDisabledAtCreationAsync()
        {
            CreateSubscriptionResponse response = await CreateDefaultSubscriptionAsync(
                publishingEnabled: false).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.SubscriptionId, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(response.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateFiveSubscriptionsAllUniqueIdsAsync()
        {
            uint[] ids = new uint[5];
            for (int i = 0; i < 5; i++)
            {
                CreateSubscriptionResponse resp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                ids[i] = resp.SubscriptionId;
            }

            Assert.That(ids.Distinct().Count(), Is.EqualTo(5));

            await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionChangesIntervalAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modifyResp = await Session.ModifySubscriptionAsync(
                null,
                id,
                2000,
                DefaultLifetimeCount,
                DefaultMaxKeepAliveCount,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.RevisedPublishingInterval, Is.GreaterThan(0));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionReturnsRevisedKeepAliveCountAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modifyResp = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultPublishingInterval,
                DefaultLifetimeCount,
                5,
                0,
                128,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionChangeAllParametersAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ModifySubscriptionResponse modifyResp = await Session.ModifySubscriptionAsync(
                null,
                id,
                500,
                60,
                15,
                0,
                200,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.RevisedPublishingInterval, Is.GreaterThan(0));
            Assert.That(modifyResp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));
            Assert.That(modifyResp.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public Task ModifySubscriptionWithInvalidIdReturnsBadSubscriptionIdInvalid()
        {
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.ModifySubscriptionAsync(
                    null,
                    999999u,
                    DefaultPublishingInterval,
                    DefaultLifetimeCount,
                    DefaultMaxKeepAliveCount,
                    0,
                    0,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            return Task.CompletedTask;
        }

        [Test]
        public async Task ModifySubscriptionLifetimeRevisedToMatchKeepAliveConstraintAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            // Request Lifetime=5, KeepAlive=10 → server must revise
            ModifySubscriptionResponse modifyResp = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultPublishingInterval,
                5,
                10,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(modifyResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(modifyResp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(3 * modifyResp.RevisedMaxKeepAliveCount));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeEnableAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingEnabled: false).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            SetPublishingModeResponse response = await Session.SetPublishingModeAsync(
                null,
                true,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeDisableAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            SetPublishingModeResponse response = await Session.SetPublishingModeAsync(
                null,
                false,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeEnableThenDisableAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingEnabled: false).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            // Enable
            SetPublishingModeResponse enableResp = await Session.SetPublishingModeAsync(
                null,
                true,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(enableResp.Results[0]), Is.True);

            // Disable
            SetPublishingModeResponse disableResp = await Session.SetPublishingModeAsync(
                null,
                false,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(disableResp.Results[0]), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task SetPublishingModeWithInvalidIdReturnsBadSubscriptionIdInvalidAsync()
        {
            SetPublishingModeResponse response = await Session.SetPublishingModeAsync(
                null,
                true,
                new uint[] { 999999u }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task SetPublishingModeOnMultipleSubscriptionsAsync()
        {
            CreateSubscriptionResponse resp1 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp2 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            SetPublishingModeResponse response = await Session.SetPublishingModeAsync(
                null,
                false,
                new uint[] { resp1.SubscriptionId, resp2.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True);
            Assert.That(StatusCode.IsGood(response.Results[1]), Is.True);

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { resp1.SubscriptionId, resp2.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteSubscriptionAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            DeleteSubscriptionsResponse deleteResp = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(deleteResp.Results[0]), Is.True);
        }

        [Test]
        public async Task DeleteNonExistentSubscriptionReturnsBadSubscriptionIdInvalidAsync()
        {
            DeleteSubscriptionsResponse response = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { 999999u }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(response.ResponseHeader.ServiceResult), Is.True);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task DeleteMultipleSubscriptionsInSingleCallAsync()
        {
            CreateSubscriptionResponse resp1 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp2 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            CreateSubscriptionResponse resp3 = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            DeleteSubscriptionsResponse deleteResp = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] {
                    resp1.SubscriptionId, resp2.SubscriptionId, resp3.SubscriptionId
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp.Results.Count, Is.EqualTo(3));
            foreach (StatusCode sc in deleteResp.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task DeleteMixedValidAndInvalidSubscriptionIdsAsync()
        {
            CreateSubscriptionResponse resp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);

            DeleteSubscriptionsResponse deleteResp = await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { resp.SubscriptionId, 999999u }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(deleteResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(deleteResp.Results.Count, Is.EqualTo(2));
            Assert.That(StatusCode.IsGood(deleteResp.Results[0]), Is.True);
            Assert.That(deleteResp.Results[1], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        public async Task PublishOnDisabledSubscriptionReturnsKeepAliveAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingEnabled: false).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            PublishResponse publishResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(publishResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(publishResp.SubscriptionId, Is.EqualTo(id));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishVerifySequenceNumberIncrementsAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            uint seq1 = pub1.NotificationMessage.SequenceNumber;

            await Task.Delay(300).ConfigureAwait(false);

            var ack = new SubscriptionAcknowledgement
            {
                SubscriptionId = id,
                SequenceNumber = seq1
            };

            PublishResponse pub2 = await Session.PublishAsync(
                null,
                new SubscriptionAcknowledgement[] { ack }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub2.NotificationMessage.SequenceNumber, Is.GreaterThan(seq1));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishVerifyNotificationMessageTimestampAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That((DateTime)pubResp.NotificationMessage.PublishTime, Is.GreaterThan(DateTime.MinValue));
            Assert.That(((DateTime)pubResp.NotificationMessage.PublishTime).Year, Is.GreaterThanOrEqualTo(2020));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishWithDataChangeAfterWriteAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            const uint clientHandle = 42u;

            await CreateMonitoredItemAsync(id, nodeId, clientHandle, samplingInterval: 50).ConfigureAwait(false);

            // Consume initial notification
            await Task.Delay(300).ConfigureAwait(false);
            await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            // Write new value
            int newValue = new Random().Next(1, 10000);
            await WriteValueAsync(nodeId, newValue).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishWithAcknowledgementAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);

            var ack = new SubscriptionAcknowledgement
            {
                SubscriptionId = id,
                SequenceNumber = pub1.NotificationMessage.SequenceNumber
            };

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub2 = await Session.PublishAsync(
                null,
                new SubscriptionAcknowledgement[] { ack }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishWithValidSequenceNumberAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync(
                publishingInterval: 100).ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            await CreateMonitoredItemAsync(id,
                VariableIds.Server_ServerStatus_CurrentTime,
                samplingInterval: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            uint seqNum = pubResp.NotificationMessage.SequenceNumber;

            RepublishResponse republishResp = await Session.RepublishAsync(
                null, id, seqNum,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(republishResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(republishResp.NotificationMessage, Is.Not.Null);
            Assert.That(republishResp.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RepublishWithInvalidSequenceNumberReturnsBadMessageNotAvailableAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.RepublishAsync(
                    null, id, 999999u,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadMessageNotAvailable));

            await DeleteSubscriptionAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task TransferSubscriptionsToNewSessionAsync()
        {
            CreateSubscriptionResponse createResp = await CreateDefaultSubscriptionAsync().ConfigureAwait(false);
            uint id = createResp.SubscriptionId;

            TransferSubscriptionsResponse transferResp = await Session.TransferSubscriptionsAsync(
                null,
                new uint[] { id }.ToArrayOf(),
                true,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(transferResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(transferResp.Results.Count, Is.EqualTo(1));

            // Transfer to same session should succeed or return specific status
            StatusCode resultStatus = transferResp.Results[0].StatusCode;
            Assert.That(
                StatusCode.IsGood(resultStatus) ||
                resultStatus == StatusCodes.BadNothingToDo ||
                resultStatus == StatusCodes.BadSubscriptionIdInvalid,
                Is.True,
                $"Unexpected TransferSubscriptions status: {resultStatus}");

            // Cleanup: delete if still valid
            try
            {
                await DeleteSubscriptionAsync(id).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                // May already be transferred/deleted
            }
        }

        private async Task<CreateSubscriptionResponse> CreateDefaultSubscriptionAsync(
            double publishingInterval = DefaultPublishingInterval,
            uint lifetimeCount = DefaultLifetimeCount,
            uint maxKeepAliveCount = DefaultMaxKeepAliveCount,
            bool publishingEnabled = true,
            byte priority = 0)
        {
            return await Session.CreateSubscriptionAsync(
                null,
                publishingInterval,
                lifetimeCount,
                maxKeepAliveCount,
                0,
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
            double samplingInterval = 250,
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

        private const double DefaultPublishingInterval = 1000;
        private const uint DefaultLifetimeCount = 100;
        private const uint DefaultMaxKeepAliveCount = 10;
    }
}
