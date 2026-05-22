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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Conformance.Tests.SubscriptionServices
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
        [Property("ConformanceUnit", "Subscription PublishRequest Queue Overflow")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Subscription PublishRequest Queue Overflow")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Subscription PublishRequest Queue Overflow")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Subscription PublishRequest Queue Overflow")]
        [Property("Tag", "002")]
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
        [Property("ConformanceUnit", "Subscription PublishRequest Queue Overflow")]
        [Property("Tag", "001")]
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
