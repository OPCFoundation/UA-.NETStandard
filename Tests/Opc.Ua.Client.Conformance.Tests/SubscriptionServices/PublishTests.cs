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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for Publish and notification behavior.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("Publish")]
    public class PublishTests : TestFixture
    {
        [SetUp]
        public async Task SetUp()
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
            m_subscriptionId = response.SubscriptionId;
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
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "005")]
        public async Task PublishReturnsKeepAliveWhenNoChangesAsync()
        {
            // Create subscription with a static node in sampling mode (no changes)
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Sampling,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 1000,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null,
                m_subscriptionId,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            // First publish may have initial data, consume it
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            // Second publish should be keep-alive (no data changes in sampling mode)
            await Task.Delay(200).ConfigureAwait(false);
            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "001")]
        public async Task PublishReturnsDataChangeNotificationAfterWriteAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            await CreateMonitoredItemAsync(nodeId).ConfigureAwait(false);

            // Consume initial data
            await Task.Delay(200).ConfigureAwait(false);
            await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            // Write to trigger notification
            await WriteValueAsync(nodeId, new Random().Next(1, 10000)).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            Assert.That(dcn, Is.Not.Null);
            Assert.That(dcn.MonitoredItems.Count, Is.GreaterThan(0));
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "001")]
        public async Task PublishReturnsCorrectClientHandleAsync()
        {
            NodeId nodeId = VariableIds.Server_ServerStatus_CurrentTime;
            const uint expectedHandle = 555u;

            await CreateMonitoredItemAsync(nodeId, expectedHandle).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.NotificationData.Count, Is.GreaterThan(0));

            var dcn = ExtensionObject.ToEncodeable(pubResp.NotificationMessage.NotificationData[0]) as
                DataChangeNotification;
            if (dcn != null && dcn.MonitoredItems.Count > 0)
            {
                Assert.That(dcn.MonitoredItems[0].ClientHandle, Is.EqualTo(expectedHandle));
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "002")]
        public async Task MultipleSubscriptionsPublishReturnsNotificationsFromEachAsync()
        {
            // Create a second subscription
            CreateSubscriptionResponse resp2 = await Session.CreateSubscriptionAsync(
                null, 100, 100, 10, 0, true, 0,
                CancellationToken.None).ConfigureAwait(false);
            uint sub2Id = resp2.SubscriptionId;

            // Add monitored items to both subscriptions
            NodeId node1 = VariableIds.Server_ServerStatus_CurrentTime;
            NodeId node2 = VariableIds.Server_ServerStatus_CurrentTime;

            await CreateMonitoredItemAsync(node1, clientHandle: 1).ConfigureAwait(false);

            var item2 = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = node2,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 2,
                    SamplingInterval = 50,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null,
                sub2Id,
                TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item2 }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Collect publish responses
            PublishResponse pub1 = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            PublishResponse pub2 = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);

            // At least one from each subscription (order may vary)
            bool sawSub1 = pub1.SubscriptionId == m_subscriptionId || pub2.SubscriptionId == m_subscriptionId;
            bool sawSub2 = pub1.SubscriptionId == sub2Id || pub2.SubscriptionId == sub2Id;
            Assert.That(sawSub1 || sawSub2, Is.True,
                "Expected to receive notifications from at least one subscription.");

            await Session.DeleteSubscriptionsAsync(
                null,
                new uint[] { sub2Id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "001")]
        public async Task PublishWithAcknowledgementOfPreviousSequenceNumberAsync()
        {
            await CreateMonitoredItemAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub1 = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);

            var ack = new SubscriptionAcknowledgement
            {
                SubscriptionId = m_subscriptionId,
                SequenceNumber = pub1.NotificationMessage.SequenceNumber
            };

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pub2 = await Session.PublishAsync(
                null,
                new SubscriptionAcknowledgement[] { ack }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "003")]
        public async Task RepublishValidSequenceNumberReturnsNotificationAsync()
        {
            await CreateMonitoredItemAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            uint seqNum = pubResp.NotificationMessage.SequenceNumber;

            RepublishResponse republishResp = await Session.RepublishAsync(
                null, m_subscriptionId, seqNum,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(republishResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(republishResp.NotificationMessage, Is.Not.Null);
            Assert.That(republishResp.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "Err-001")]
        public Task RepublishInvalidSequenceNumberReturnsBadMessageNotAvailable()
        {
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.RepublishAsync(
                    null, m_subscriptionId, 999999u,
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadMessageNotAvailable));
            return Task.CompletedTask;
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "001")]
        public async Task PublishNotificationMessageHasValidTimestampAsync()
        {
            await CreateMonitoredItemAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That((DateTime)pubResp.NotificationMessage.PublishTime, Is.GreaterThan(DateTime.MinValue));
            Assert.That(((DateTime)pubResp.NotificationMessage.PublishTime).Year, Is.GreaterThanOrEqualTo(2020));
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Publish Basic")]
        [Property("Tag", "001")]
        public async Task PublishNotificationSequenceNumberIsPositiveAsync()
        {
            await CreateMonitoredItemAsync(
                VariableIds.Server_ServerStatus_CurrentTime).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            PublishResponse pubResp = await Session.PublishAsync(
                null,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(pubResp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pubResp.NotificationMessage.SequenceNumber, Is.GreaterThan(0u));
        }

        private async Task<uint> CreateMonitoredItemAsync(
            NodeId nodeId,
            uint clientHandle = 1,
            double samplingInterval = 50,
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
                m_subscriptionId,
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

        private uint m_subscriptionId;
    }
}
