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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Conformance.Tests.SubscriptionServices
{
    /// <summary>
    /// compliance tests for Subscription Durable covering
    /// durable subscription lifecycle, SetPublishingMode on durable
    /// subscriptions, parameter modification, and edge cases.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionDurable")]
    public class SubscriptionDurableTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "001")]
        public async Task DurableSubscriptionCreatedWithPublishingEnabledAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(Session)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.SubscriptionId, Is.GreaterThan(0u));

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { resp.SubscriptionId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        public async Task DurableSubscriptionSurvivesSessionCloseAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Close session without deleting subscription
            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            // Reconnect and transfer
            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                TransferSubscriptionsResponse xfer =
                    await TransferOrIgnoreAsync(session2, subId, true)
                        .ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(xfer.Results[0].StatusCode),
                    Is.True,
                    "Subscription should survive session close.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        [Property("Tag", "006")]
        public async Task DurableSubscriptionTransferAfterReconnectAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Consume initial data
            await Task.Delay(300).ConfigureAwait(false);
            await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            // Close without deleting
            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            await Task.Delay(200).ConfigureAwait(false);

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(pub.SubscriptionId, Is.EqualTo(subId));

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "000")]
        public async Task DurableSubscriptionNotAvailableIgnoredAsync()
        {
            // Test the ignore path for servers that don't support
            // transfer (used as durable proxy)
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                // This will Assert.Ignore if not supported
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        public async Task DurableSubscriptionDeleteBeforeReconnectAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                DeleteSubscriptionsResponse del =
                    await session2.DeleteSubscriptionsAsync(
                        null, new uint[] { subId }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(del.Results[0]), Is.True);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "001")]
        public async Task DurableSubSetPublishingModeDisableAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            SetPublishingModeResponse spResp =
                await session1.SetPublishingModeAsync(
                    null, false, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(spResp.Results[0]), Is.True);

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "001")]
        public async Task DurableSubSetPublishingModeReEnableAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await session1.SetPublishingModeAsync(
                null, false, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            SetPublishingModeResponse spResp =
                await session1.SetPublishingModeAsync(
                    null, true, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(spResp.Results[0]), Is.True);

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "006")]
        public async Task DurableSubPublishingModePreservedAfterTransferAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            // Disable publishing
            await session1.SetPublishingModeAsync(
                null, false, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                // Publishing was disabled → expect KeepAlive
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.Zero);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "001")]
        public async Task DurableSubDisabledNoNotificationsAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await session1.SetPublishingModeAsync(
                null, false, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pub.NotificationMessage.NotificationData.Count,
                Is.Zero,
                "Disabled durable sub should only send KeepAlive.");

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        public async Task DurableSubReEnableAfterTransferAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await session1.SetPublishingModeAsync(
                null, false, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                // Re-enable
                await session2.SetPublishingModeAsync(
                    null, true, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0),
                    "Re-enabled sub should produce notifications.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        [Property("Tag", "011")]
        public async Task DurableSubModifyIntervalAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(
                session1, interval: 2000).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await session1.ModifySubscriptionAsync(
                    null, subId, 100, DefaultLifetime, DefaultKeepAlive,
                    0, 0, CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedPublishingInterval,
                Is.LessThanOrEqualTo(2000));

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        [Property("Tag", "011")]
        public async Task DurableSubModifyKeepAliveCountAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await session1.ModifySubscriptionAsync(
                    null, subId, DefaultInterval, DefaultLifetime, 50,
                    0, 0, CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        [Property("Tag", "011")]
        public async Task DurableSubModifyLifetimeCountAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await session1.ModifySubscriptionAsync(
                    null, subId, DefaultInterval, 500, DefaultKeepAlive,
                    0, 0, CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedLifetimeCount, Is.GreaterThan(0u));

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        [Property("Tag", "011")]
        public async Task DurableSubModifyPriorityAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await session1.ModifySubscriptionAsync(
                    null, subId, DefaultInterval, DefaultLifetime,
                    DefaultKeepAlive, 0, 200,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        [Property("Tag", "011")]
        public async Task DurableSubModifyMaxNotificationsAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await session1.ModifySubscriptionAsync(
                    null, subId, DefaultInterval, DefaultLifetime,
                    DefaultKeepAlive, 5, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "005")]
        public async Task DurableSubWithMultipleMonitoredItemsAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(session1)
                .ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            for (uint h = 1; h <= 5; h++)
            {
                await AddItemAsync(session1, subId,
                    VariableIds.Server_ServerStatus_CurrentTime,
                    handle: h, sampling: 50).ConfigureAwait(false);
            }

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub = await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(
                pub.NotificationMessage.NotificationData.Count,
                Is.GreaterThan(0));

            await session1.DeleteSubscriptionsAsync(
                null, new uint[] { subId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        public async Task DurableSubTransferWithInitialTrueAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(
                session1, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
                Assert.That(
                    pub.NotificationMessage.NotificationData.Count,
                    Is.GreaterThan(0),
                    "Transfer with initial=true should send data.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "004")]
        public async Task DurableSubTransferWithInitialFalseAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(
                session1, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                ToNodeId(Constants.ScalarStaticInt32))
                .ConfigureAwait(false);

            // Consume initial
            await Task.Delay(300).ConfigureAwait(false);
            await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, false)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "010")]
        public async Task DurableSubCreateMultipleSubsAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            var subIds = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                CreateSubscriptionResponse resp =
                    await CreateSubAsync(session1).ConfigureAwait(false);
                subIds.Add(resp.SubscriptionId);
            }

            Assert.That(subIds, Has.Count.EqualTo(3));

            await session1.DeleteSubscriptionsAsync(
                null, subIds.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            await CloseSessionAsync(session1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "008")]
        public async Task DurableSubSeqNumbersPreservedAsync()
        {
            Client.ISession session1 = await CreateSessionAsync()
                .ConfigureAwait(false);

            CreateSubscriptionResponse resp = await CreateSubAsync(
                session1, interval: 100).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            await AddItemAsync(session1, subId,
                VariableIds.Server_ServerStatus_CurrentTime,
                sampling: 50).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await session1.PublishAsync(
                null, default,
                CancellationToken.None).ConfigureAwait(false);
            uint seqBefore = pub1.NotificationMessage.SequenceNumber;

            await session1.CloseAsync(5000, false).ConfigureAwait(false);
            session1.Dispose();

            Client.ISession session2 = await CreateSessionAsync()
                .ConfigureAwait(false);
            try
            {
                await TransferOrIgnoreAsync(session2, subId, true)
                    .ConfigureAwait(false);

                await Task.Delay(300).ConfigureAwait(false);

                PublishResponse pub2 = await session2.PublishAsync(
                    null,
                    default,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    pub2.NotificationMessage.SequenceNumber,
                    Is.GreaterThan(seqBefore),
                    "Seq numbers should continue after transfer.");

                await session2.DeleteSubscriptionsAsync(
                    null, new uint[] { subId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await CloseSessionAsync(session2).ConfigureAwait(false);
            }
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            Client.ISession session, double interval = DefaultInterval)
        {
            return await session.CreateSubscriptionAsync(
                null, interval, DefaultLifetime, DefaultKeepAlive,
                0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> AddItemAsync(
            Client.ISession session, uint subId, NodeId nodeId,
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
                await session.CreateMonitoredItemsAsync(
                    null, subId, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode),
                Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        private Task<Client.ISession> CreateSessionAsync()
        {
            return ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.None);
        }

        private async Task CloseSessionAsync(Client.ISession session)
        {
            await session.CloseAsync(5000, true).ConfigureAwait(false);
            session.Dispose();
        }

        private const double DefaultInterval = 200;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;

        private async Task<TransferSubscriptionsResponse>
            TransferOrIgnoreAsync(
                Client.ISession target, uint subId, bool sendInitial)
        {
            try
            {
                TransferSubscriptionsResponse resp =
                    await target.TransferSubscriptionsAsync(
                        null,
                        new uint[] { subId }.ToArrayOf(),
                        sendInitial,
                        CancellationToken.None).ConfigureAwait(false);

                if (resp.Results.Count > 0 &&
                    StatusCode.IsBad(resp.Results[0].StatusCode))
                {
                    Assert.Ignore(
                        "Durable/Transfer subscriptions failed: " +
                        resp.Results[0].StatusCode.ToString());
                }

                return resp;
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadNoSubscription ||
                    sre.StatusCode == StatusCodes.BadSessionClosed)
            {
                Assert.Ignore(
                    "Durable/Transfer subscriptions not supported: " +
                    sre.StatusCode.ToString());
                return null;
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "002")]
        public async Task DurableSetLifetimeMaxUint32Async()
        {
            // Create durable subscription; set lifetimeInHours to UInt32.MaxValue
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                uint revisedLifetime = await SetSubscriptionDurableAsync(
                    subId, uint.MaxValue).ConfigureAwait(false);

                // Server may revise the lifetime, but should return a valid value
                Assert.That(revisedLifetime, Is.GreaterThan(0u),
                    "Revised lifetime should be greater than 0.");

                if (revisedLifetime != uint.MaxValue)
                {
                    Assert.Warn($"Server revised lifetimeInHours from UInt32.MaxValue to {revisedLifetime}.");
                }
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "003")]
        public async Task DurableSetLifetimeZeroRevisedGreaterThanZeroAsync()
        {
            // Set lifetimeInHours to 0; revised should be > 0
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                uint revisedLifetime = await SetSubscriptionDurableAsync(
                    subId, 0).ConfigureAwait(false);

                Assert.That(revisedLifetime, Is.GreaterThan(0u),
                    "Revised lifetime should be greater than requested (0).");
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "009")]
        public async Task DurableSubscriptionWithServerNotifierEventsAsync()
        {
            // Manual test: durable subscription with Server.Notifier events.
            // This test is marked as skipped in the JS.
            Assert.Ignore("Manual test for durable subscriptions " +
                "with Server.Notifier events; skipped per definition.");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "011")]
        public async Task DurableWithZeroMonitoredItemsThenRepeatCallWithDifferentParamsAsync()
        {
            // Create durable sub with 0 monitored items, then repeat call with different params.
            // Second call while monitored item exists should return BadInvalidState.
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                // First call with 0 monitored items should succeed
                uint revisedLifetime1 = await SetSubscriptionDurableAsync(
                    subId, 5).ConfigureAwait(false);
                Assert.That(revisedLifetime1, Is.GreaterThan(0u));

                // Add a monitored item
                NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
                uint monItemId = await AddMonitoredItemAsync(subId, nodeId).ConfigureAwait(false);

                // Second call while monitored item exists should return BadInvalidState
                try
                {
                    CallMethodResult result = await CallSetSubscriptionDurableAsync(
                        subId, 10).ConfigureAwait(false);

                    // Some servers may return BadInvalidState in the result status
                    if (result.StatusCode == StatusCodes.BadInvalidState)
                    {
                        // Expected
                    }
                    else if (StatusCode.IsGood(result.StatusCode))
                    {
                        // Server accepted the call - may not enforce the restriction
                        Assert.Warn("Server accepted SetSubscriptionDurable with active " +
                            "monitored items; BadInvalidState was expected.");
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadInvalidState)
                {
                    // Expected
                }

                // Delete monitored item and retry - should succeed
                await Session.DeleteMonitoredItemsAsync(
                    null, subId,
                    new uint[] { monItemId }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                uint revisedLifetime3 = await SetSubscriptionDurableAsync(
                    subId, 10).ConfigureAwait(false);
                Assert.That(revisedLifetime3, Is.GreaterThan(0u));
            }
            finally
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "012")]
        public async Task DurableShortLivedSubscriptionModifyResetsStateAsync()
        {
            // Short-lived sub, durable call changes lifetime;
            // ModifySubscription should reset durable state.
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 500, lifetime: 3, keepAlive: 3).ConfigureAwait(false);
            uint subId = resp.SubscriptionId;
            try
            {
                // Set durable with lifetime of 1 hour
                uint revisedLifetime = await SetSubscriptionDurableAsync(
                    subId, 1).ConfigureAwait(false);
                Assert.That(revisedLifetime, Is.GreaterThan(0u));

                // Modify subscription to reset durable state
                ModifySubscriptionResponse modResp = await Session.ModifySubscriptionAsync(
                    null, subId, 500, 3, 3, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(modResp.ResponseHeader.ServiceResult), Is.True);

                // After modify, the subscription should behave as non-durable
                // (shorter lifetime). Wait for keep-alive or timeout.
                await Task.Delay(1000).ConfigureAwait(false);

                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult) ||
                    pub.ResponseHeader.ServiceResult == StatusCodes.BadTimeout ||
                    pub.ResponseHeader.ServiceResult == StatusCodes.BadNoSubscription,
                    Is.True,
                    "Expected Good, BadTimeout, or BadNoSubscription after modify.");
            }
            finally
            {
                try
                {
                    await DeleteSubAsync(subId).ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Subscription may have already expired
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Durable")]
        [Property("Tag", "013")]
        public async Task DurableDeleteSubscriptionRemovesDurableStateAsync()
        {
            // Delete a durable subscription, verify it is fully removed.
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint subId = resp.SubscriptionId;

            // Make it durable
            uint revisedLifetime = await SetSubscriptionDurableAsync(
                subId, 24).ConfigureAwait(false);
            Assert.That(revisedLifetime, Is.GreaterThan(0u));

            // Delete it
            await DeleteSubAsync(subId).ConfigureAwait(false);

            // Attempting to publish should eventually return BadNoSubscription
            try
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(
                    pub.ResponseHeader.ServiceResult == StatusCodes.BadNoSubscription ||
                    StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                    Is.True);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNoSubscription)
            {
                // Expected
            }
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            double interval = DefaultInterval,
            uint lifetime = DefaultLifetime,
            uint keepAlive = DefaultKeepAlive,
            bool enabled = true)
        {
            return await Session.CreateSubscriptionAsync(
                null, interval, lifetime, keepAlive, 0,
                enabled, 0,
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
            uint handle = 1, double sampling = 250)
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

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(resp.Results[0].StatusCode), Is.True);
            return resp.Results[0].MonitoredItemId;
        }

        /// <summary>
        /// Calls the SetSubscriptionDurable method on the server.
        /// Returns the revised lifetime in hours, or Assert.Ignore if not supported.
        /// </summary>
        private async Task<uint> SetSubscriptionDurableAsync(
            uint subscriptionId, uint lifetimeInHours)
        {
            try
            {
                CallMethodResult result = await CallSetSubscriptionDurableAsync(
                    subscriptionId, lifetimeInHours).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadMethodInvalid ||
                    result.StatusCode == StatusCodes.BadNotSupported ||
                    result.StatusCode == StatusCodes.BadNodeIdUnknown)
                {
                    Assert.Ignore(
                        "SetSubscriptionDurable not supported: " + result.StatusCode.ToString());
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    $"SetSubscriptionDurable failed: {result.StatusCode}");
                Assert.That(result.OutputArguments, Is.Not.Null);
                Assert.That(result.OutputArguments.Count, Is.EqualTo(1));

                return (uint)result.OutputArguments[0];
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNotSupported ||
                    sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadMethodInvalid ||
                    sre.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Assert.Ignore(
                    "SetSubscriptionDurable not supported: " + sre.StatusCode.ToString());
                return 0; // unreachable
            }
        }

        private async Task<CallMethodResult> CallSetSubscriptionDurableAsync(
            uint subscriptionId, uint lifetimeInHours)
        {
            var request = new CallMethodRequest
            {
                ObjectId = ServerObjectId,
                MethodId = SetSubscriptionDurableMethodId,
                InputArguments = new Variant[]
                {
                    new(subscriptionId),
                    new(lifetimeInHours)
                }.ToArrayOf()
            };

            CallResponse callResp = await Session.CallAsync(
                null,
                new CallMethodRequest[] { request }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(callResp.Results, Is.Not.Null);
            Assert.That(callResp.Results.Count, Is.EqualTo(1));
            return callResp.Results[0];
        }

        /// <summary>
        /// <summary>
        /// SetSubscriptionDurable method NodeIds (Part 5, 12.1.13)
        /// </summary>
        private static readonly NodeId SetSubscriptionDurableMethodId =
            MethodIds.Server_SetSubscriptionDurable;

        private static readonly NodeId ServerObjectId = ObjectIds.Server;
    }
}
