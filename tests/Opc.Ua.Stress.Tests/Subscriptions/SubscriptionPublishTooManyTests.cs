// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Stress.Tests.Subscriptions
{
    /// <summary>
    /// compliance tests for Subscription Publish Too Many covering
    /// overflow behavior when too many publish requests are outstanding.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionPublishTooMany")]
    public class SubscriptionPublishTooManyTests : TestFixture
    {
        [Test]
        public async Task TooManyPublishRequestsHandledGracefullyAsync()
        {
            uint id = await CreateSubWithItemAsync().ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            int goodCount = 0;
            for (int i = 0; i < 20; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                {
                    goodCount++;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.That(goodCount, Is.GreaterThan(0),
                "Server should handle many publishes gracefully.");

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishQueueOverflowReturnsGoodOrErrorAsync()
        {
            uint id = await CreateSubWithItemAsync().ConfigureAwait(false);

            await Task.Delay(200).ConfigureAwait(false);

            bool sawGood = false;
            for (int i = 0; i < 15; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                {
                    sawGood = true;
                }
                await Task.Delay(30).ConfigureAwait(false);
            }

            Assert.That(sawGood, Is.True);

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishCountExceedsSubscriptionCountAsync()
        {
            uint id = await CreateSubWithItemAsync().ConfigureAwait(false);

            await Task.Delay(200).ConfigureAwait(false);

            // More publishes than subscriptions
            int goodCount = 0;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                {
                    goodCount++;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.That(goodCount, Is.GreaterThan(0));

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task PublishOverflowDoesNotAffectExistingSubscriptionsAsync()
        {
            uint id = await CreateSubWithItemAsync().ConfigureAwait(false);

            // Flood publishes
            for (int i = 0; i < 10; i++)
            {
                await Session.PublishWithTimeoutAsync().ConfigureAwait(false);
                await Task.Delay(30).ConfigureAwait(false);
            }

            // Wait and verify subscription still works
            await Task.Delay(500).ConfigureAwait(false);

            PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(pub.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(pub.SubscriptionId, Is.EqualTo(id));

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        [Test]
        public async Task RapidPublishRequestsAllReturnValidResponsesAsync()
        {
            uint id = await CreateSubWithItemAsync().ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            int validCount = 0;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await Session.PublishWithTimeoutAsync().ConfigureAwait(false);

                Assert.That(pub.ResponseHeader, Is.Not.Null);
                Assert.That(pub.NotificationMessage, Is.Not.Null);

                if (StatusCode.IsGood(pub.ResponseHeader.ServiceResult))
                {
                    validCount++;
                }
            }

            Assert.That(validCount, Is.EqualTo(10),
                "All rapid publishes should return valid responses.");

            await Session.DeleteSubscriptionsAsync(
                null, new uint[] { id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<uint> CreateSubWithItemAsync(double interval = 100)
        {
            CreateSubscriptionResponse resp =
                await Session.CreateSubscriptionAsync(
                    null, interval, 100, 10, 0, true, 0,
                    CancellationToken.None).ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            var item = new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 50,
                    Filter = default,
                    DiscardOldest = true,
                    QueueSize = 10
                }
            };

            await Session.CreateMonitoredItemsAsync(
                null, id, TimestampsToReturn.Both,
                new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            return id;
        }
    }
}
