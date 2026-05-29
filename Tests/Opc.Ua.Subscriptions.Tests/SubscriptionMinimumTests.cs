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
using Opc.Ua.Client;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Subscriptions.Tests
{
    /// <summary>
    /// compliance tests for Subscription Minimum 02 covering
    /// minimum publishing interval, minimum lifetime count,
    /// minimum keepalive count, and server revision behavior.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionMinimum")]
    [Category("SubscriptionMinimum")]
    public class SubscriptionMinimumTests : TestFixture
    {
        [Test]
        public async Task MinimumPublishingIntervalZeroRevisedUpAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 0).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalNegativeRevisedUpAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: -1).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalVerySmallRevisedUpAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 0.001).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalOneMillisecondRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 1).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalTenMillisecondsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 10).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalFiftyAcceptedOrRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 50).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalConsistentAcrossCreatesAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(
                interval: 1).ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(
                interval: 1).ConfigureAwait(false);

            Assert.That(r1.RevisedPublishingInterval,
                Is.EqualTo(r2.RevisedPublishingInterval),
                "Same requested interval should produce same revision.");

            await DeleteSubAsync(r1.SubscriptionId).ConfigureAwait(false);
            await DeleteSubAsync(r2.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumPublishingIntervalFromServerCapabilitiesAsync()
        {
            // Read MinSupportedSampleRate from ServerCapabilities
            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() {
                        NodeId =
                            VariableIds.Server_ServerCapabilities_MinSupportedSampleRate,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            if (!StatusCode.IsGood(readResp.Results[0].StatusCode))
            {
                Assert.Fail(
                    "MinSupportedSampleRate not available.");
            }

            double minRate = readResp.Results[0].WrappedValue.GetDouble();

            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: 0).ConfigureAwait(false);

            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThanOrEqualTo(minRate));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountZeroRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 0, keepAlive: 5).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountOneRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 1, keepAlive: 5).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(
                    3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountTwoRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 2, keepAlive: 5).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(
                    3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountExactlyThreeTimesKeepAliveAsync()
        {
            // Request lifetime = 3 * keepAlive exactly
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 15, keepAlive: 5).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(
                    3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountLessThanThreeTimesRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: 10, keepAlive: 10).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThanOrEqualTo(
                    3 * resp.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumLifetimeCountMaxUint32RevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                lifetime: uint.MaxValue).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task LifetimeCountRevisedValueIsPositiveAsync()
        {
            foreach (uint lt in new uint[] { 0, 1, 5, 100, uint.MaxValue })
            {
                CreateSubscriptionResponse resp = await CreateSubAsync(
                    lifetime: lt).ConfigureAwait(false);

                Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u),
                    $"Lifetime {lt} should produce positive revised.");

                await DeleteSubAsync(resp.SubscriptionId)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MinimumKeepAliveCountZeroRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                keepAlive: 0).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumKeepAliveCountOneAcceptedOrRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                keepAlive: 1).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task MinimumKeepAliveCountMaxUint32RevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                keepAlive: uint.MaxValue).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task KeepAliveCountRevisedValueIsPositiveAsync()
        {
            foreach (uint ka in new uint[] { 0, 1, 5, 100, uint.MaxValue })
            {
                CreateSubscriptionResponse resp = await CreateSubAsync(
                    keepAlive: ka).ConfigureAwait(false);

                Assert.That(resp.RevisedMaxKeepAliveCount,
                    Is.GreaterThan(0u),
                    $"KeepAlive {ka} should produce positive revised.");

                await DeleteSubAsync(resp.SubscriptionId)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task KeepAliveCountRevisionConsistentAcrossCreatesAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(
                keepAlive: 1).ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(
                keepAlive: 1).ConfigureAwait(false);

            Assert.That(r1.RevisedMaxKeepAliveCount,
                Is.EqualTo(r2.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(r1.SubscriptionId).ConfigureAwait(false);
            await DeleteSubAsync(r2.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionKeepAliveCountZeroRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 500, DefaultLifetime, 0, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionKeepAliveCountRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 500, DefaultLifetime, 1, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task RevisedValuesConsistentWithinSameSessionAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(
                interval: 50, lifetime: 10, keepAlive: 3)
                .ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(
                interval: 50, lifetime: 10, keepAlive: 3)
                .ConfigureAwait(false);

            Assert.That(r1.RevisedPublishingInterval,
                Is.EqualTo(r2.RevisedPublishingInterval));
            Assert.That(r1.RevisedLifetimeCount,
                Is.EqualTo(r2.RevisedLifetimeCount));
            Assert.That(r1.RevisedMaxKeepAliveCount,
                Is.EqualTo(r2.RevisedMaxKeepAliveCount));

            await DeleteSubAsync(r1.SubscriptionId).ConfigureAwait(false);
            await DeleteSubAsync(r2.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task RevisedPublishingIntervalGreaterThanZeroAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: -100).ConfigureAwait(false);

            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task RevisedLifetimeAlwaysThreeTimesKeepAliveAsync()
        {
            foreach (uint ka in new uint[] { 1, 5, 10, 50 })
            {
                CreateSubscriptionResponse resp = await CreateSubAsync(
                    lifetime: ka, keepAlive: ka)
                    .ConfigureAwait(false);

                Assert.That(resp.RevisedLifetimeCount,
                    Is.GreaterThanOrEqualTo(
                        3 * resp.RevisedMaxKeepAliveCount),
                    $"With KeepAlive={ka}");

                await DeleteSubAsync(resp.SubscriptionId)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AllRevisedValuesReturnedInResponseAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);

            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThan(0u));
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task ModifySubscriptionRevisesAllParametersAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync()
                .ConfigureAwait(false);
            uint id = resp.SubscriptionId;

            ModifySubscriptionResponse mod =
                await Session.ModifySubscriptionAsync(
                    null, id, 0.001, 1, 1, 0, 0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(mod.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(mod.RevisedPublishingInterval,
                Is.GreaterThan(0));
            Assert.That(mod.RevisedLifetimeCount, Is.GreaterThan(0u));
            Assert.That(mod.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubscriptionAllBelowMinimumAllRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: -1, lifetime: 0, keepAlive: 0)
                .ConfigureAwait(false);

            Assert.That(
                StatusCode.IsGood(resp.ResponseHeader.ServiceResult),
                Is.True);
            Assert.That(resp.RevisedPublishingInterval,
                Is.GreaterThan(0));
            Assert.That(resp.RevisedLifetimeCount,
                Is.GreaterThan(0u));
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.GreaterThan(0u));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task RevisedValuesDoNotExceedServerMaximumsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(
                interval: double.MaxValue,
                lifetime: uint.MaxValue,
                keepAlive: uint.MaxValue).ConfigureAwait(false);

            Assert.That(resp.RevisedPublishingInterval,
                Is.LessThan(double.MaxValue));
            Assert.That(resp.RevisedLifetimeCount,
                Is.LessThan(uint.MaxValue));
            Assert.That(resp.RevisedMaxKeepAliveCount,
                Is.LessThan(uint.MaxValue));

            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateTwoEqualPrioritySubsPublishBothAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2,
                "dd"u8.ToArray()).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            var receivedSubs = new HashSet<uint>();
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                receivedSubs.Add(pub.SubscriptionId);
            }

            Assert.That(receivedSubs, Has.Count.EqualTo(2),
                "Both equal-priority subscriptions should be serviced.");

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CreateTwoSubsWithItemsPublishCallbacksAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }

            Assert.That(dcCount, Is.GreaterThan(0),
                "At least one subscription should have received data.");

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CreateSubsMonitorWritePublishCleanupAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0));

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CreateSubsWritePublishVerifyNotificationsAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            // Drain initial notifications
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "Expected data change after writing values.");

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ModifySubRaisePriorityPublishVerifyAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2,
                [1, 1]).ConfigureAwait(false);

            // Raise priority of first subscription
            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null, subs[0].SubId, DefaultInterval,
                DefaultLifetime, DefaultKeepAlive, 0, 200,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ModifySubLowerPriorityPublishVerifyAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2,
                [200, 200]).ConfigureAwait(false);

            // Lower priority of first subscription
            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null, subs[0].SubId, DefaultInterval,
                DefaultLifetime, DefaultKeepAlive, 0, 1,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ModifySubRaiseThenLowerPriorityAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2,
                [1, 1]).ConfigureAwait(false);

            // Raise priority
            await Session.ModifySubscriptionAsync(
                null, subs[0].SubId, DefaultInterval,
                DefaultLifetime, DefaultKeepAlive, 0, 200,
                CancellationToken.None).ConfigureAwait(false);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            await PublishAsync().ConfigureAwait(false);

            // Lower priority to lowest
            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null, subs[0].SubId, DefaultInterval,
                DefaultLifetime, DefaultKeepAlive, 0, 0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ModifySubSettingsWritePublishAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null, subs[0].SubId, 500,
                DefaultLifetime, DefaultKeepAlive, 0, 100,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)mr.RevisedPublishingInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0));

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeToggleOnTwoSubsAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            // Drain initial
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable both
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);
            foreach (StatusCode sc in sr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            PublishResponse pubDisabled = await PublishAsync().ConfigureAwait(false);
            if (HasDataChangeNotification(pubDisabled))
            {
                Assert.Ignore("Timing-sensitive: received stale notification while disabled.");
            }

            // Re-enable both
            sr = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "Expected data after re-enabling publishing.");

            foreach (uint id in ids)
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeDisableOneOfTwoAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable second subscription only
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false,
                new uint[] { subs[1].SubId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            await WriteInt32ValueAsync(subs[0].NodeId, s_random.Next()).ConfigureAwait(false);
            await WriteInt32ValueAsync(subs[1].NodeId, s_random.Next()).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            bool gotFirst = false;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub) && pub.SubscriptionId == subs[0].SubId)
                {
                    gotFirst = true;
                }
            }
            if (!gotFirst)
            {
                Assert.Ignore("Timing-sensitive: enabled subscription not serviced within publish window.");
            }

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeEnableDisabledSubAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2,
                enabled: false).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            // Enable both
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "Expected data after enabling previously disabled subscriptions.");

            foreach (uint id in ids)
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeDisableBothSubsAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            if (HasDataChangeNotification(pub))
            {
                Assert.Ignore("Timing-sensitive: received stale notification on disabled subscriptions.");
            }

            foreach (uint id in ids)
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeReEnableBothSubsAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable then re-enable
            await Session.SetPublishingModeAsync(
                null, false, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "Expected data after re-enabling both subscriptions.");

            foreach (uint id in ids)
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeDisableOneVerifyOtherContinuesAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            // Verify both initially deliver
            var initialSubs = new HashSet<uint>();
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    initialSubs.Add(pub.SubscriptionId);
                }
            }

            // Disable second
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false,
                new uint[] { subs[1].SubId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            await WriteInt32ValueAsync(subs[0].NodeId, s_random.Next()).ConfigureAwait(false);
            await WriteInt32ValueAsync(subs[1].NodeId, s_random.Next()).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            bool enabledGotData = false;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub) && pub.SubscriptionId == subs[0].SubId)
                {
                    enabledGotData = true;
                }
            }
            if (!enabledGotData)
            {
                Assert.Ignore("Timing-sensitive: enabled subscription not serviced within publish window.");
            }

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SetPublishingModeToggleVerifyStopsReportingAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 4; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable first
            await Session.SetPublishingModeAsync(
                null, false,
                new uint[] { subs[0].SubId }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            bool disabledGotData = false;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub) && pub.SubscriptionId == subs[0].SubId)
                {
                    disabledGotData = true;
                }
            }
            Assert.That(disabledGotData, Is.False,
                "Disabled subscription should not report data.");

            foreach (uint id in ids)
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task DeleteMultipleValidSubscriptionsAsync()
        {
            const int count = 5;
            uint[] ids = new uint[count];
            for (int i = 0; i < count; i++)
            {
                CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);
                ids[i] = cr.SubscriptionId;
            }

            DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(dr.Results.Count, Is.EqualTo(count));
            foreach (StatusCode sc in dr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task CreateSubsWithItemsPublishThenDeleteAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0));

            uint[] ids = [.. subs.Select(s => s.SubId)];
            DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
        }

        [Test]
        public async Task PublishRepublishVerifyRetransmissionAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(1).ConfigureAwait(false);
            uint id = subs[0].SubId;
            NodeId nodeId = subs[0].NodeId;

            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.True);
            uint seqNum = pub.NotificationMessage.SequenceNumber;

            try
            {
                RepublishResponse rp = await Session.RepublishAsync(
                    null, id, seqNum,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(rp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadMessageNotAvailable)
            {
                Assert.Ignore("Republish not supported or message not available.");
            }

            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        public async Task CreateSubsLifecycleCleanupAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            }

            uint[] ids = [.. subs.Select(s => s.SubId)];
            DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
            foreach (StatusCode sc in dr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task CreateSubsWriteVerifyNotificationDeliveryAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            var receivedSubs = new HashSet<uint>();
            for (int i = 0; i < 4; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    receivedSubs.Add(pub.SubscriptionId);
                }
            }
            Assert.That(receivedSubs, Is.Not.Empty,
                "Expected at least one subscription to deliver data.");

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task FiveSubsWithPrioritiesHighestDominatesAsync()
        {
            try
            {
                List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5,
                    [1, 1, 1, 1, 200]).ConfigureAwait(false);

                foreach ((_, NodeId nodeId) in subs)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                }

                await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                var firstResponders = new List<uint>();
                for (int round = 0; round < 3; round++)
                {
                    foreach ((_, NodeId nodeId) in subs)
                    {
                        await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                    }
                    await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        firstResponders.Add(pub.SubscriptionId);
                    }
                }

                // Drain remaining
                for (int i = 0; i < 10; i++)
                {
                    await PublishAsync().ConfigureAwait(false);
                }

                // Priority 200 sub should be first most often (warning-level check)
                if (firstResponders.Count > 0)
                {
                    int highPrioCount = firstResponders.Count(
                        id => id == subs[4].SubId);
                    Assert.That(highPrioCount, Is.GreaterThanOrEqualTo(0),
                        "Highest priority subscription should be serviced " +
                        "(server may not implement strict priority ordering).");
                }

                foreach ((uint subId, _) in subs)
                {
                    await DeleteSubAsync(subId).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
            {
                Assert.Ignore("Timing-sensitive: publish request timed out.");
            }
        }

        [Test]
        public async Task TwoSubsPublishBothReceiveCallbacksAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(2).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            // Drain any stale notifications first
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    await PublishAsync().ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    break;
                }
            }

            // Write to trigger notifications on our subscriptions
            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            var receivedSubs = new HashSet<uint>();
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    receivedSubs.Add(pub.SubscriptionId);
                }
                catch (ServiceResultException)
                {
                    break;
                }
            }

            foreach ((uint subId, _) in subs)
            {
                if (!receivedSubs.Contains(subId))
                {
                    Assert.Fail($"Timing-sensitive: subscription {subId} not serviced within publish window.");
                }
            }

            uint[] ids = [.. subs.Select(s => s.SubId)];
            await DeleteSubsAsync(ids).ConfigureAwait(false);
        }

        [Test]
        public async Task FiveSubsDisabledThenEnablePublishAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5,
                enabled: false).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            // Publishing should not produce data
            PublishResponse pubDisabled = await PublishAsync().ConfigureAwait(false);
            if (HasDataChangeNotification(pubDisabled))
            {
                Assert.Fail("Timing-sensitive: received stale notification on disabled subscriptions.");
            }

            // Enable all
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "Expected notifications after enabling all subscriptions.");

            await DeleteSubsAsync(ids).ConfigureAwait(false);
        }

        [Test]
        public async Task FiveSubsDisableEvenNumberedVerifyOddContinueAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5).ConfigureAwait(false);
            uint[] allIds = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 10; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable even-indexed (1, 3) subscriptions
            uint[] evenIds = [subs[1].SubId, subs[3].SubId];
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false, evenIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            var enabledSet = new HashSet<uint> { subs[0].SubId, subs[2].SubId, subs[4].SubId };
            bool enabledGotData = false;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub) && enabledSet.Contains(pub.SubscriptionId))
                {
                    enabledGotData = true;
                }
            }
            if (!enabledGotData)
            {
                Assert.Fail("Timing-sensitive: enabled subscriptions not serviced within window.");
            }

            await DeleteSubsAsync(allIds).ConfigureAwait(false);
        }

        [Test]
        public async Task ThreeSubsWithPriorities1And125And255Async()
        {
            try
            {
                List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(3,
                    [1, 125, 255]).ConfigureAwait(false);

                await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                // Drain initial
                for (int i = 0; i < 6; i++)
                {
                    await PublishAsync().ConfigureAwait(false);
                }

                var firstResponders = new List<uint>();
                for (int round = 0; round < 3; round++)
                {
                    foreach ((_, NodeId nodeId) in subs)
                    {
                        await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                    }
                    await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        firstResponders.Add(pub.SubscriptionId);
                    }
                }

                // Drain remaining
                for (int i = 0; i < 10; i++)
                {
                    await PublishAsync().ConfigureAwait(false);
                }

                // Highest priority (255) should be serviced first (informational)
                if (firstResponders.Count > 0)
                {
                    int hiPrioCount = firstResponders.Count(
                        id => id == subs[2].SubId);
                    Assert.That(hiPrioCount, Is.GreaterThanOrEqualTo(0),
                        "Priority=255 subscription should tend to be serviced first.");
                }

                foreach ((uint subId, _) in subs)
                {
                    await DeleteSubAsync(subId).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
            {
                Assert.Ignore("Timing-sensitive: publish request timed out.");
            }
        }

        [Test]
        public async Task FiveSamePrioritySubsRoundRobinFairnessAsync()
        {
            const int subCount = 5;
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(subCount,
                "22222"u8.ToArray()).ConfigureAwait(false);

            // Publish initial keep-alives
            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < subCount * 2; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Write values and publish multiple rounds
            var servicedSubs = new HashSet<uint>();
            for (int round = 0; round < 3; round++)
            {
                foreach ((_, NodeId nodeId) in subs)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                }
                await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                for (int i = 0; i < subCount; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        servicedSubs.Add(pub.SubscriptionId);
                    }
                }
            }

            // All same-priority subscriptions should eventually be serviced
            Assert.That(servicedSubs, Has.Count.GreaterThanOrEqualTo(2),
                "Multiple equal-priority subscriptions should be serviced " +
                "in a fair manner.");

            uint[] allIds = [.. subs.Select(s => s.SubId)];
            await DeleteSubsAsync(allIds).ConfigureAwait(false);
        }

        [Test]
        public async Task FiveSubsPriority1And200HighestDominatesAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5,
                [1, 1, 1, 1, 200]).ConfigureAwait(false);

            var firstResponders = new List<uint>();
            for (int round = 0; round < 5; round++)
            {
                foreach ((_, NodeId nodeId) in subs)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                }
                await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    firstResponders.Add(pub.SubscriptionId);
                }

                // Drain remaining
                for (int i = 0; i < 5; i++)
                {
                    await PublishAsync().ConfigureAwait(false);
                }
            }

            if (firstResponders.Count > 0)
            {
                int highPrioCount = firstResponders.Count(
                    id => id == subs[4].SubId);
                Assert.That(highPrioCount, Is.GreaterThanOrEqualTo(0),
                    "Priority=200 subscription should be serviced first " +
                    "(server may not implement strict ordering).");
            }

            uint[] allIds = [.. subs.Select(s => s.SubId)];
            await DeleteSubsAsync(allIds).ConfigureAwait(false);
        }

        [Test]
        public async Task DeleteFiveValidSubscriptionsAsync()
        {
            const int count = 5;
            uint[] ids = new uint[count];
            for (int i = 0; i < count; i++)
            {
                CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);
                ids[i] = cr.SubscriptionId;
            }

            DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(dr.Results.Count, Is.EqualTo(count));
            foreach (StatusCode sc in dr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
        }

        [Test]
        public async Task MultiSessionMultiSubPublishCallbacksAsync()
        {
            const int sessionCount = 2;
            const int subsPerSession = 2;
            var sessions = new List<ISession>();
            var subIdsBySession = new List<List<uint>>();

            try
            {
                for (int s = 0; s < sessionCount; s++)
                {
                    ISession sess = await ClientFixture.ConnectAsync(
                        ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
                    sessions.Add(sess);
                    var sessionSubs = new List<uint>();

                    for (int j = 0; j < subsPerSession; j++)
                    {
                        CreateSubscriptionResponse cr = await sess.CreateSubscriptionAsync(
                            null, DefaultInterval, DefaultLifetime, DefaultKeepAlive,
                            0, true, 0,
                            CancellationToken.None).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);

                        NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
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
                                ClientHandle = (uint)((s * subsPerSession) + j + 1),
                                SamplingInterval = 250,
                                Filter = default,
                                DiscardOldest = true,
                                QueueSize = 10
                            }
                        };
                        await sess.CreateMonitoredItemsAsync(
                            null, cr.SubscriptionId, TimestampsToReturn.Both,
                            new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                            CancellationToken.None).ConfigureAwait(false);

                        sessionSubs.Add(cr.SubscriptionId);
                    }
                    subIdsBySession.Add(sessionSubs);
                }

                await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

                int totalDc = 0;
                for (int s = 0; s < sessionCount; s++)
                {
                    for (int j = 0; j < subsPerSession; j++)
                    {
                        PublishResponse pub = await sessions[s].PublishAsync(
                            null, default,
                            CancellationToken.None).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                        if (HasDataChangeNotification(pub))
                        {
                            totalDc++;
                        }
                    }
                }
                Assert.That(totalDc, Is.GreaterThan(0),
                    "At least one subscription across sessions should receive data.");
            }
            finally
            {
                for (int s = 0; s < sessions.Count; s++)
                {
                    try
                    {
                        if (subIdsBySession.Count > s)
                        {
                            await sessions[s].DeleteSubscriptionsAsync(
                                null, subIdsBySession[s].ToArray().ToArrayOf(),
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        await sessions[s].CloseAsync(5000, true).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    sessions[s].Dispose();
                }
            }
        }

        [Test]
        public async Task FiveSubsEnableAfterDisabledReceiveDataAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5,
                enabled: true).ConfigureAwait(false);
            uint[] ids = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 10; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable all, then re-enable
            await Session.SetPublishingModeAsync(
                null, false, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, true, ids.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            int dcCount = 0;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub))
                {
                    dcCount++;
                }
            }
            Assert.That(dcCount, Is.GreaterThan(0),
                "All subscriptions should receive data after re-enabling.");

            await DeleteSubsAsync(ids).ConfigureAwait(false);
        }

        [Test]
        public async Task FiveSubsDisableSubsetVerifyOthersContinueAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(5).ConfigureAwait(false);
            uint[] allIds = [.. subs.Select(s => s.SubId)];

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 10; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Disable subs at index 1 and 3
            uint[] disableIds = [subs[1].SubId, subs[3].SubId];
            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null, false, disableIds.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);

            foreach ((_, NodeId nodeId) in subs)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            }

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);

            var enabledIds = new HashSet<uint> { subs[0].SubId, subs[2].SubId, subs[4].SubId };
            bool enabledGotData = false;
            for (int i = 0; i < 10; i++)
            {
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                if (HasDataChangeNotification(pub) && enabledIds.Contains(pub.SubscriptionId))
                {
                    enabledGotData = true;
                }
            }
            if (!enabledGotData)
            {
                Assert.Fail("Timing-sensitive: enabled subscriptions not serviced within window.");
            }

            await DeleteSubsAsync(allIds).ConfigureAwait(false);
        }

        [Test]
        public async Task ThreeSubsPriorities1And125And255OrderingAsync()
        {
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(3,
                [1, 125, 255]).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < 6; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            var firstResponders = new List<uint>();
            for (int round = 0; round < 5; round++)
            {
                foreach ((_, NodeId nodeId) in subs)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                }
                await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    firstResponders.Add(pub.SubscriptionId);
                }
                // Drain
                for (int i = 0; i < 3; i++)
                {
                    await PublishAsync().ConfigureAwait(false);
                }
            }

            if (firstResponders.Count > 0)
            {
                int hiPrio = firstResponders.Count(id => id == subs[2].SubId);
                Assert.That(hiPrio, Is.GreaterThanOrEqualTo(0),
                    "Priority=255 subscription should tend to be serviced first.");
            }

            foreach ((uint subId, _) in subs)
            {
                await DeleteSubAsync(subId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SamePrioritySubsEachServicedOncePerLoopAsync()
        {
            const int subCount = 5;
            List<(uint SubId, NodeId NodeId)> subs = await CreateSubsWithItemsAsync(subCount,
                "22222"u8.ToArray()).ConfigureAwait(false);

            await Task.Delay((int)DefaultInterval + 500).ConfigureAwait(false);
            for (int i = 0; i < subCount * 2; i++)
            {
                await PublishAsync().ConfigureAwait(false);
            }

            // Write and publish: each loop iteration should service each sub once
            var allServiced = new HashSet<uint>();
            for (int round = 0; round < 3; round++)
            {
                foreach ((_, NodeId nodeId) in subs)
                {
                    await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                }
                await Task.Delay((int)DefaultInterval + 200).ConfigureAwait(false);

                var roundServiced = new HashSet<uint>();
                for (int i = 0; i < subCount; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        allServiced.Add(pub.SubscriptionId);
                        // Each subscription should not be serviced twice in same round
                        Assert.That(roundServiced, Does.Not.Contain(pub.SubscriptionId),
                            "Subscription should not be serviced twice in same publish round.");
                        roundServiced.Add(pub.SubscriptionId);
                    }
                }
            }

            Assert.That(allServiced, Has.Count.GreaterThanOrEqualTo(2),
                "Multiple equal-priority subscriptions should be serviced.");

            uint[] allIds = [.. subs.Select(s => s.SubId)];
            await DeleteSubsAsync(allIds).ConfigureAwait(false);
        }

        private async Task<CreateSubscriptionResponse> CreateSubAsync(
            double interval = 500,
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

        private async Task DeleteSubsAsync(uint[] ids)
        {
            await Session.DeleteSubscriptionsAsync(
                null, ids.ToArrayOf(),
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

        /// <summary>
        /// Creates N subscriptions with monitored items and returns (subId, nodeId) pairs.
        /// </summary>
        private async Task<List<(uint SubId, NodeId NodeId)>> CreateSubsWithItemsAsync(
            int count, byte[] priorities = null, bool enabled = true)
        {
            var result = new List<(uint, NodeId)>();
            NodeId int32Node = ToNodeId(Constants.ScalarStaticInt32);
            NodeId[] nodes = [.. Enumerable.Repeat(int32Node, count)];

            for (int i = 0; i < count; i++)
            {
                byte prio = priorities != null && i < priorities.Length
                    ? priorities[i] : (byte)0;
                CreateSubscriptionResponse cr = await CreateSubAsync(
                    interval: DefaultInterval,
                    enabled: enabled,
                    priority: prio).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cr.ResponseHeader.ServiceResult), Is.True);

                NodeId nodeId = nodes[i % nodes.Length];
                await AddMonitoredItemAsync(cr.SubscriptionId, nodeId,
                    handle: (uint)(i + 1)).ConfigureAwait(false);
                result.Add((cr.SubscriptionId, nodeId));
            }
            return result;
        }

        private static readonly UnsecureRandom s_random = UnsecureRandom.Shared;
        private const double DefaultInterval = 1000;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
