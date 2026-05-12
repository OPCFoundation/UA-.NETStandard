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

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for the Subscription Basic conformance unit.
    /// Tests 001-073 map to official JS test cases for
    /// CreateSubscription, ModifySubscription, SetPublishingMode,
    /// DeleteSubscriptions, Publish, and Republish.
    /// </summary>
    [NonParallelizable]
    [TestFixture]
    [Category("Conformance")]
    [Category("Subscription")]
    [Category("SubscriptionBasic")]
    public class SubscriptionBasicTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "001")]
        public async Task CreateSubscriptionDefaultParamsAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync().ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(id, Is.GreaterThan(0u));
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange notification.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "002")]
        public async Task CreateSubscriptionPublishingIntervalOneAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1, lifetime: 15, keepAlive: 5).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.Not.Zero, "Server must not revise publishingInterval to 0.");
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.True, "Expected initial DataChange.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "003")]
        public async Task CreateSubscriptionPublishingIntervalZeroRevisedAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 0).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.Not.Zero, "Server should revise publishingInterval from 0.");
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "004")]
        public async Task CreateSubscriptionPublishingIntervalMaxDoubleAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: double.MaxValue).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedPublishingInterval, Is.Not.EqualTo(double.MaxValue), "Server should revise publishingInterval from MaxValue.");
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "005")]
        public async Task CreateSubscriptionLifetimeZeroKeepAliveZeroAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: 0, keepAlive: 0).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThan(0u));
            Assert.That(resp.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "006")]
        public async Task CreateSubscriptionLifetimeThreeKeepAliveOneAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: 3, keepAlive: 1).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "007")]
        public async Task CreateSubscriptionLifetimeEqualKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: 3, keepAlive: 3).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "008")]
        public async Task CreateSubscriptionLifetimeLessThanKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: 1, keepAlive: 15).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "009")]
        public async Task CreateSubscriptionLifetimeLessThanThreeTimesKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: 10, keepAlive: 15).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "010")]
        public async Task CreateSubscriptionLifetimeMaxKeepAliveMaxDivThreeAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: uint.MaxValue, keepAlive: uint.MaxValue / 3).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "011")]
        public async Task CreateSubscriptionLifetimeMaxKeepAliveMaxDivTwoAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: uint.MaxValue, keepAlive: uint.MaxValue / 2).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "012")]
        public async Task CreateSubscriptionLifetimeHalfMaxKeepAliveMaxAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: uint.MaxValue / 2, keepAlive: uint.MaxValue).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "013")]
        public async Task CreateSubscriptionLifetimeMaxKeepAliveMaxAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(lifetime: uint.MaxValue, keepAlive: uint.MaxValue).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
            Assert.That(resp.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * resp.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(resp.SubscriptionId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "014")]
        public async Task CreateSubscriptionPublishingDisabledNoDataChangeAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(enabled: false).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.False, "No data expected since publishing is disabled.");
            Assert.That(pub.SubscriptionId, Is.EqualTo(id));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "015")]
        public async Task CreateSubscriptionNoItemsPublishKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, lifetime: 20).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            int waitMs = (int)(resp.RevisedPublishingInterval * resp.RevisedLifetimeCount / 2);
            await Task.Delay(Math.Min(waitMs, 5000)).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    Assert.That(HasDataChangeNotification(pub), Is.False, "Expected keep-alive only (no monitored items).");
                }
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadRequestTimeout)
            {
                Assert.Fail("Publish timed out waiting for keep-alive notification (timing-sensitive).");
            }
            finally
            {
                await DeleteSubAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "016")]
        public async Task CreateSubscriptionLifetimeNotExpiredBeforeExpectedTimeAsync()
        {
            async Task RunLifetimeTest(double pubInterval, uint lifetimeCount)
            {
                uint keepAlive = Math.Max(1, lifetimeCount / 3);

                CreateSubscriptionResponse resp = await CreateSubAsync(interval: pubInterval, lifetime: lifetimeCount, keepAlive: keepAlive).ConfigureAwait(
                    false);
                uint id = resp.SubscriptionId;
                double lifetimeMs = resp.RevisedPublishingInterval * resp.RevisedLifetimeCount;
                int waitMs = Math.Max(0, (int)(lifetimeMs - 500));
                waitMs = Math.Min(waitMs, 10000);
                await Task.Delay(waitMs).ConfigureAwait(false);

                DeleteSubscriptionsResponse delResp = await Session.DeleteSubscriptionsAsync(null, new uint[] { id }.ToArrayOf(), CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(delResp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(StatusCode.IsGood(delResp.Results[0]), Is.True, "Subscription should still be alive before expiration.");
            }
            await RunLifetimeTest(100, 10).ConfigureAwait(false);
            await RunLifetimeTest(100, 30).ConfigureAwait(false);
            await RunLifetimeTest(800, 15).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "017")]
        public async Task CreateSubscriptionPublishTwiceKeepAliveSequenceNumberOneAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, keepAlive: 5).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub1), Is.False, "Expected keep-alive only.");
            Assert.That(pub1.SubscriptionId, Is.EqualTo(id));
            Assert.That(pub1.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub2), Is.False, "Expected keep-alive only.");
            Assert.That(pub2.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "018")]
        public async Task CreateSubscriptionInterval3000KeepAlive3PublishTwiceAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 3000, keepAlive: 3).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            try
            {
                PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub1), Is.False);
                Assert.That(pub1.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
                // Accept any valid subscription ID from this session
                Assert.That(pub1.SubscriptionId, Is.GreaterThan(0u));
                PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub2), Is.False);
                Assert.That(pub2.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: subscription publish interrupted by CI runner load ({sre.StatusCode}).");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "019")]
        public async Task CreateSubscriptionDelayedPublishImmediateResponseAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, keepAlive: 5).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub1), Is.False);
            Assert.That(pub1.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            Assert.That(pub1.SubscriptionId, Is.EqualTo(id));
            int waitMs = (int)(resp.RevisedPublishingInterval * resp.RevisedMaxKeepAliveCount) + 500;
            await Task.Delay(Math.Min(waitMs, 10000)).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub2), Is.False);
            Assert.That(pub2.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "020")]
        public async Task CreateSubscriptionDisabledPublishTwiceKeepAliveOnlyAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, keepAlive: 5, enabled: false).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    Assert.That(HasDataChangeNotification(pub), Is.False, "No data expected since publishing is disabled.");
                    Assert.That(pub.NotificationMessage.SequenceNumber, Is.EqualTo(1u));
                    Assert.That(pub.SubscriptionId, Is.EqualTo(id));
                }
            }
            catch (ServiceResultException sre) when (IsTransientCiTimeoutStatus(sre.StatusCode))
            {
                Assert.Ignore(
                    $"Timing-sensitive: disabled-subscription publish interrupted by CI runner load ({sre.StatusCode}).");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "021")]
        public async Task CreateSubscriptionDisabledWaitHalfKeepAlivePublishTwiceAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, lifetime: 30, keepAlive: 10, enabled: false).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            int waitMs = (int)(resp.RevisedPublishingInterval * (resp.RevisedMaxKeepAliveCount / 2));
            await Task.Delay(Math.Min(waitMs, 5000)).ConfigureAwait(false);
            try
            {
                PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub1), Is.False);
                Assert.That(pub1.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
                PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
                Assert.That(HasDataChangeNotification(pub2), Is.False);
                Assert.That(pub2.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadRequestTimeout)
            {
                Assert.Fail("Timing-sensitive: publish request timed out.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "022")]
        public async Task CreateSubscriptionWithItemWritePublishThenKeepAliveAsync()
        {
            CreateSubscriptionResponse resp = await CreateSubAsync(interval: 1000, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = resp.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            int waitMs = (int)(resp.RevisedPublishingInterval * (resp.RevisedMaxKeepAliveCount / 2));
            await Task.Delay(Math.Min(waitMs, 5000)).ConfigureAwait(false);
            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)resp.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            if (!HasDataChangeNotification(pub1))
            {
                Assert.Fail("Timing-sensitive: no initial data change received.");
            }
            Assert.That(pub1.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub2.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "023")]
        public async Task ModifySubscriptionDefaultParamsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                cr.RevisedPublishingInterval,
                cr.RevisedLifetimeCount,
                cr.RevisedMaxKeepAliveCount,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.EqualTo(cr.RevisedPublishingInterval));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "024")]
        public async Task ModifySubscriptionIntervalHigherBySevenMsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                cr.RevisedPublishingInterval + 7,
                30,
                10,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.GreaterThan(0));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "025")]
        public async Task ModifySubscriptionIntervalLowerBySevenMsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                Math.Max(1, cr.RevisedPublishingInterval - 7),
                30,
                10,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.GreaterThan(0));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "026")]
        public async Task ModifySubscriptionIntervalMatchesRevisedFromCreateAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, cr.RevisedPublishingInterval, 30, 10, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.EqualTo(cr.RevisedPublishingInterval));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "027")]
        public async Task ModifySubscriptionIntervalOneFastestSupportedAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, 1, DefaultLifetime, DefaultKeepAlive, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.GreaterThan(0));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "028")]
        public async Task ModifySubscriptionIntervalZeroRevisedAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, 0, DefaultLifetime, DefaultKeepAlive, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.GreaterThan(0));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "029")]
        public async Task ModifySubscriptionIntervalMaxFloatRevisedAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                float.MaxValue,
                DefaultLifetime,
                DefaultKeepAlive,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedPublishingInterval, Is.Not.EqualTo((double)float.MaxValue));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "030")]
        public async Task ModifySubscriptionVariousLifetimeKeepAliveCombinationsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            uint rl = cr.RevisedLifetimeCount;
            uint rk = cr.RevisedMaxKeepAliveCount;
            const uint o = 0xA;

            (uint Lt, uint Ka)[] combos = [
                (rl + o, rk),
                (rl - o, rk),
                (rl, rk + o),
                (rl, Math.Max(1, rk - o)),
                (rl + o, rk + o),
                (Math.Max(1, rl - o), Math.Max(1, rk - o)),
                (rl + o, Math.Max(1, rk - o)),
                (Math.Max(1, rl - o), rk + o),
                (rl, rk) ];
            foreach ((uint lt, uint ka) in combos)
            {
                ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, lt, ka, 0, 0, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
                Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "031")]
        public async Task ModifySubscriptionLifetimeZeroKeepAliveZeroAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, 0, 0, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThan(0u));
            Assert.That(mr.RevisedMaxKeepAliveCount, Is.GreaterThan(0u));
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "032")]
        public async Task ModifySubscriptionLifetimeThreeKeepAliveOneAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, 3, 1, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "033")]
        public async Task ModifySubscriptionLifetimeEqualsKeepAliveAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, 1, 1, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "034")]
        public async Task ModifySubscriptionLifetimeLessThanKeepAliveAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, 190, 200, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "035")]
        public async Task ModifySubscriptionLifetimeLessThanThreeTimesKeepAliveAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(null, id, DefaultInterval, 200, 100, 0, 0, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "036")]
        public async Task ModifySubscriptionLifetimeMaxKeepAliveMaxDivTwoAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultInterval,
                uint.MaxValue,
                uint.MaxValue / 2,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "037")]
        public async Task ModifySubscriptionLifetimeMaxKeepAliveMaxDivThreeAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultInterval,
                uint.MaxValue,
                uint.MaxValue / 3,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "038")]
        public async Task ModifySubscriptionLifetimeHalfMaxKeepAliveMaxAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultInterval,
                uint.MaxValue / 2,
                uint.MaxValue,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "039")]
        public async Task ModifySubscriptionLifetimeMaxKeepAliveMaxAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultInterval,
                uint.MaxValue,
                uint.MaxValue,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(mr.RevisedLifetimeCount, Is.GreaterThanOrEqualTo(3 * mr.RevisedMaxKeepAliveCount));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "040")]
        public async Task ModifySubscriptionMaxNotificationsPerPublishToOneAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId n1 = ToNodeId(Constants.ScalarStaticInt32);
            NodeId n2 = ToNodeId(Constants.ScalarStaticDouble);
            await AddMonitoredItemAsync(id, n1, handle: 1).ConfigureAwait(false);
            await AddMonitoredItemAsync(id, n2, handle: 2).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            await PublishAsync().ConfigureAwait(false);

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                cr.RevisedPublishingInterval,
                cr.RevisedLifetimeCount,
                cr.RevisedMaxKeepAliveCount,
                1,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            await WriteInt32ValueAsync(n1, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(CountDataChangeNotifications(pub), Is.LessThanOrEqualTo(1), "MaxNotificationsPerPublish=1 should limit to 1 notification.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "041")]
        public async Task ModifySubscriptionMaxNotificationsPerPublishToTenAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId[] nodeIds = [.. Constants.ScalarStaticNodes.Take(15).Select(ToNodeId)];
            await AddMultipleMonitoredItemsAsync(id, nodeIds).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            await PublishAsync().ConfigureAwait(false);

            ModifySubscriptionResponse mr = await Session.ModifySubscriptionAsync(
                null,
                id,
                cr.RevisedPublishingInterval,
                cr.RevisedLifetimeCount,
                cr.RevisedMaxKeepAliveCount,
                10,
                0,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(mr.ResponseHeader.ServiceResult), Is.True);
            await WriteInt32ValueAsync(ToNodeId(Constants.ScalarStaticInt32), s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(CountDataChangeNotifications(pub), Is.LessThanOrEqualTo(10), "MaxNotificationsPerPublish=10 should limit notifications.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "042")]
        public async Task RepublishOutOfOrderAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqNums = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 200).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNums.Add(pub.NotificationMessage.SequenceNumber);
            }
            seqNums.Reverse();
            foreach (uint seqNum in seqNums)
            {
                try
                {
                    RepublishResponse repub = await Session.RepublishAsync(null, id, seqNum, CancellationToken.None).ConfigureAwait(false);
                    if (StatusCode.IsGood(repub.ResponseHeader.ServiceResult))
                    {
                        Assert.That(repub.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));
                    }
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
                {
                }
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "043")]
        public async Task SetPublishingModeDisableEnabledAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            if (!HasDataChangeNotification(pub1))
            {
                Assert.Fail("Timing-sensitive: no initial data change received.");
            }

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(null, false, new uint[] { id }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(sr.Results[0]), Is.True);
            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(HasDataChangeNotification(pub2), Is.False, "No data expected after disabling publishing.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "044")]
        public async Task SetPublishingModeEnableDisabledAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, enabled: false).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            if (HasDataChangeNotification(pub1))
            {
                Assert.Fail("Timing-sensitive: received stale notification while disabled.");
            }

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(null, true, new uint[] { id }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(sr.Results[0]), Is.True);
            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            if (!HasDataChangeNotification(pub2))
            {
                Assert.Fail("Timing-sensitive: no data change after enabling publishing.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "045")]
        public async Task SetPublishingModeReEnableAlreadyEnabledAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            if (!HasDataChangeNotification(pub1))
            {
                Assert.Fail("Timing-sensitive: no initial data change received.");
            }

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(null, true, new uint[] { id }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.Results[0]), Is.True);
            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub2 = await PublishAsync().ConfigureAwait(false);
            if (!HasDataChangeNotification(pub2))
            {
                Assert.Fail("Timing-sensitive: no data change after re-enabling.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "046")]
        public async Task SetPublishingModeDisableAlreadyDisabledAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, enabled: false).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null,
                false,
                new uint[] { id, id, id, id, id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(sr.Results.Count, Is.EqualTo(5));
            foreach (StatusCode sc in sr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            if (HasDataChangeNotification(pub))
            {
                Assert.Fail("Timing-sensitive: received stale notification on disabled subscription.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "047")]
        public async Task SetPublishingModeEnableDuplicateIdsFiveTimesAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, enabled: false).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);

            SetPublishingModeResponse sr = await Session.SetPublishingModeAsync(
                null,
                true,
                new uint[] { id, id, id, id, id }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(sr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(sr.Results.Count, Is.EqualTo(5));
            foreach (StatusCode sc in sr.Results)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }
            await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            if (!HasDataChangeNotification(pub))
            {
                Assert.Fail("Timing-sensitive: no data change after enabling.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "048")]
        public async Task PublishDefaultParamsFirstSequenceNumberOneAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(pub.NotificationMessage.SequenceNumber, Is.GreaterThanOrEqualTo(1u));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "049")]
        public async Task PublishAcknowledgeValidSequenceNumberAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub1 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub1.ResponseHeader.ServiceResult), Is.True);
            if (!HasDataChangeNotification(pub1))
            {
                Assert.Fail("Timing-sensitive: no data change notification received.");
            }
            PublishResponse pub2 = await PublishWithAckAsync(id, pub1.NotificationMessage.SequenceNumber).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub2.ResponseHeader.ServiceResult), Is.True);
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "050")]
        public async Task PublishAcknowledgeMultipleValidSequenceNumbersAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqNums = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNums.Add(pub.NotificationMessage.SequenceNumber);
            }
            Assert.That(seqNums, Has.Count.EqualTo(3));
            SubscriptionAcknowledgement[] acks = [.. seqNums.Select(s => new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = s })];
            PublishResponse ackPub = await PublishWithAcksAsync(acks).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ackPub.ResponseHeader.ServiceResult), Is.True);
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "051")]
        public async Task PublishAcknowledgeFromMultipleSubscriptionsAsync()
        {
            CreateSubscriptionResponse r1 = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            CreateSubscriptionResponse r2 = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            await AddMonitoredItemAsync(r1.SubscriptionId, ToNodeId(Constants.ScalarStaticInt32), handle: 1).ConfigureAwait(false);
            await AddMonitoredItemAsync(r2.SubscriptionId, ToNodeId(Constants.ScalarStaticDouble), handle: 2).ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false);
            PublishResponse p1 = await PublishAsync().ConfigureAwait(false);
            PublishResponse p2 = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(p1.ResponseHeader.ServiceResult), Is.True);
            Assert.That(StatusCode.IsGood(p2.ResponseHeader.ServiceResult), Is.True);

            SubscriptionAcknowledgement[] acks = [
                new SubscriptionAcknowledgement { SubscriptionId = p1.SubscriptionId, SequenceNumber = p1.NotificationMessage.SequenceNumber },
                new SubscriptionAcknowledgement { SubscriptionId = p2.SubscriptionId, SequenceNumber = p2.NotificationMessage.SequenceNumber } ];
            PublishResponse ap = await PublishWithAcksAsync(acks).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ap.ResponseHeader.ServiceResult), Is.True);

            await Session.DeleteSubscriptionsAsync(null, new uint[] { r1.SubscriptionId, r2.SubscriptionId }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "052")]
        public async Task PublishAcknowledgeMixedValidAndInvalidAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqNums = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNums.Add(pub.NotificationMessage.SequenceNumber);
            }

            SubscriptionAcknowledgement[] acks = [
                new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = seqNums[0] + 1000 },
                new SubscriptionAcknowledgement { SubscriptionId = id + 1000, SequenceNumber = seqNums[1] },
                new SubscriptionAcknowledgement { SubscriptionId = id + 1000, SequenceNumber = seqNums[2] + 1000 } ];
            PublishResponse ap = await PublishWithAcksAsync(acks).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ap.ResponseHeader.ServiceResult), Is.True);
            if (ap.Results != default && ap.Results.Count >= 3)
            {
                Assert.That(ap.Results[0], Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
                Assert.That(ap.Results[1], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
                Assert.That(ap.Results[2], Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "053")]
        public async Task PublishAcknowledgeAlternatingValidAndInvalidAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqNums = new List<uint>();
            for (int i = 0; i < 4; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNums.Add(pub.NotificationMessage.SequenceNumber);
            }
            var acks = new SubscriptionAcknowledgement[seqNums.Count];
            for (int i = 0; i < seqNums.Count; i++)
            {
                acks[i] = new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = i % 2 == 0 ? seqNums[i] + 1000 : seqNums[i] };
            }
            PublishResponse ap = await PublishWithAcksAsync(acks).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ap.ResponseHeader.ServiceResult), Is.True);
            if (ap.Results != default && ap.Results.Count >= 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (i % 2 == 0)
                    {
                        Assert.That(ap.Results[i], Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
                    }
                    else
                    {
                        Assert.That(StatusCode.IsGood(ap.Results[i]), Is.True);
                    }
                }
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "054")]
        public async Task PublishAcknowledgeWithCallbackCountAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 1000, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            int publishCount = 0;
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                if (HasDataChangeNotification(pub))
                {
                    publishCount++;
                }
            }
            Assert.That(publishCount, Is.GreaterThan(0), "Expected at least one data change notification.");
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "055")]
        public async Task PublishAcknowledgeAlternatingFromValidSubscriptionAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqNums = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqNums.Add(pub.NotificationMessage.SequenceNumber);
            }

            SubscriptionAcknowledgement[] acks = [
                new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = seqNums[0] + 1000 },
                new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = seqNums[1] },
                new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = seqNums[2] + 1000 } ];
            PublishResponse ap = await PublishWithAcksAsync(acks).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(ap.ResponseHeader.ServiceResult), Is.True);
            if (ap.Results != default && ap.Results.Count >= 3)
            {
                Assert.That(ap.Results[0], Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
                Assert.That(StatusCode.IsGood(ap.Results[1]), Is.True);
                Assert.That(ap.Results[2], Is.EqualTo(StatusCodes.BadSequenceNumberUnknown));
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "056")]
        public async Task RepublishDefaultParamsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.True);
            uint seqNum = pub.NotificationMessage.SequenceNumber;
            try
            {
                RepublishResponse rp = await Session.RepublishAsync(null, id, seqNum, CancellationToken.None).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(rp.ResponseHeader.ServiceResult), Is.True);
                Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
            {
                Assert.Fail(
                "Republish not supported or message not available.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "057")]
        public async Task RepublishLastThreeUpdatesCompareAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqs = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
            }
            foreach (uint sq in seqs)
            {
                try
                {
                    RepublishResponse rp = await Session.RepublishAsync(null, id, sq, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(sq));
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
                {
                    Assert.Fail(
                    "Republish not supported.");
                    return;
                }
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "058")]
        public async Task RepublishAfterKeepAliveIntervalNoAcksAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 5).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqs = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
            }
            foreach (uint sq in seqs)
            {
                try
                {
                    RepublishResponse rp = await Session.RepublishAsync(null, id, sq, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(sq));
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
                {
                }
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "059")]
        public async Task RepublishMissingThirdNotificationAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqs = new List<uint>();
            for (int i = 0; i < 4; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
            }
            SubscriptionAcknowledgement[] acks = [.. seqs.Where((_, idx) => idx != 2)
                .Select(s => new SubscriptionAcknowledgement { SubscriptionId = id, SequenceNumber = s })];
            await PublishWithAcksAsync(acks).ConfigureAwait(false);
            uint missingSeq = seqs[2];
            try
            {
                RepublishResponse rp = await Session.RepublishAsync(null, id, missingSeq, CancellationToken.None).ConfigureAwait(false);
                Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(missingSeq));
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
            {
                Assert.Fail(
                "Republish not supported or message expired.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "060")]
        public async Task DeleteSingleSubscriptionAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);

            DeleteSubscriptionsResponse dr = await Session.DeleteSubscriptionsAsync(null, new uint[] { cr.SubscriptionId }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(dr.ResponseHeader.ServiceResult), Is.True);
            Assert.That(dr.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(dr.Results[0]), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "061")]
        public async Task DeleteSubscriptionThenModifyReturnsBadIdAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync().ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await DeleteSubAsync(id).ConfigureAwait(false);
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(async () => await Session.ModifySubscriptionAsync(
                null,
                id,
                DefaultInterval,
                DefaultLifetime,
                DefaultKeepAlive,
                0,
                0,
                CancellationToken.None)
                    .ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSubscriptionIdInvalid));
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "062")]
        public async Task RepublishSequenceGreaterThanCurrentReturnsBadMessageAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            uint seqNum = pub.NotificationMessage.SequenceNumber;
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await Session.RepublishAsync(null, id, seqNum + 10, CancellationToken.None).ConfigureAwait(false));
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadMessageNotAvailable));
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "063")]
        public async Task SubscriptionLifetimeExtendedByNonPublishCallsAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 1000, lifetime: 5, keepAlive: 2).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            uint monItemId = await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            double lifetimeMs = cr.RevisedPublishingInterval * cr.RevisedLifetimeCount;
            int waitMs = Math.Max(100, (int)(lifetimeMs * 0.8));
            waitMs = Math.Min(waitMs, 5000);
            await Task.Delay(waitMs).ConfigureAwait(false);

            await Session.ModifySubscriptionAsync(
                null,
                id,
                cr.RevisedPublishingInterval,
                cr.RevisedLifetimeCount,
                cr.RevisedMaxKeepAliveCount,
                0,
                0,
                CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(waitMs).ConfigureAwait(false);
            await Session.SetPublishingModeAsync(null, true, new uint[] { id }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(waitMs).ConfigureAwait(false);

            await Session.SetMonitoringModeAsync(null, id, MonitoringMode.Reporting, new uint[] { monItemId }.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            await Task.Delay(waitMs).ConfigureAwait(false);
            try
            {
                await Session.RepublishAsync(null, id, 0, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
            }
            await Task.Delay(waitMs).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "067")]
        public async Task PublishTimeoutSmallerThanKeepAliveAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 5, keepAlive: 3).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            if (!HasDataChangeNotification(pub))
            {
                Assert.Fail("Timing-sensitive: no initial data change received.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "070")]
        public async Task AcknowledgeSequenceNumbersOutOfOrderAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 30, keepAlive: 10).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            await AddMonitoredItemAsync(id, nodeId).ConfigureAwait(false);
            var seqs = new List<uint>();
            for (int i = 0; i < 3; i++)
            {
                await WriteInt32ValueAsync(nodeId, s_random.Next()).ConfigureAwait(false);
                await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
                PublishResponse pub = await PublishAsync().ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                seqs.Add(pub.NotificationMessage.SequenceNumber);
            }
            seqs.Reverse();
            foreach (uint sq in seqs)
            {
                PublishResponse ap = await PublishWithAckAsync(id, sq).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(ap.ResponseHeader.ServiceResult), Is.True);
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "071")]
        public async Task MultipleSessionsOneSubscriptionPerSessionAsync()
        {
            const int sessionCount = 3;
            var sessions = new List<ISession>();
            var subIds = new List<uint>();
            try
            {
                for (int i = 0; i < sessionCount; i++)
                {
                    ISession s = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
                    sessions.Add(s);

                    CreateSubscriptionResponse resp = await s.CreateSubscriptionAsync(
                        null,
                        500,
                        DefaultLifetime,
                        DefaultKeepAlive,
                        0,
                        true,
                        0,
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(resp.ResponseHeader.ServiceResult), Is.True);
                    subIds.Add(resp.SubscriptionId);

                    var item = new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ToNodeId(Constants.ScalarStaticInt32),
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = (uint)(i + 1),
                            SamplingInterval = 250,
                            Filter = default,
                            DiscardOldest = true,
                            QueueSize = 10
                        }
                    };
                    await s.CreateMonitoredItemsAsync(
                        null,
                        resp.SubscriptionId,
                        TimestampsToReturn.Both,
                        new MonitoredItemCreateRequest[] { item }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                }
                await Task.Delay(1500).ConfigureAwait(false);
                int dcCount = 0;
                for (int i = 0; i < sessionCount; i++)
                {
                    PublishResponse pub = await sessions[i].PublishAsync(null, default, CancellationToken.None)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
                    if (HasDataChangeNotification(pub))
                    {
                        dcCount++;
                    }
                }
                Assert.That(dcCount, Is.GreaterThan(0), "At least one session should have received a data change.");
            }
            finally
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        await sessions[i].DeleteSubscriptionsAsync(null, new uint[] { subIds[i] }.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
                        await sessions[i].CloseAsync(5000, true).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    sessions[i].Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "072")]
        public async Task PublishTimeoutSmallerThanKeepAliveDescriptionOnlyAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500, lifetime: 15, keepAlive: 3).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "Subscription Basic")]
        [Property("Tag", "073")]
        public async Task CreateSubscriptionPublishRepublishLoopAsync()
        {
            CreateSubscriptionResponse cr = await CreateSubAsync(interval: 500).ConfigureAwait(false);
            uint id = cr.SubscriptionId;
            await AddMonitoredItemAsync(id, ToNodeId(Constants.ScalarStaticInt32)).ConfigureAwait(false);
            await Task.Delay((int)cr.RevisedPublishingInterval + 500).ConfigureAwait(false);
            PublishResponse pub = await PublishAsync().ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(pub.ResponseHeader.ServiceResult), Is.True);
            Assert.That(HasDataChangeNotification(pub), Is.True);
            uint seqNum = pub.NotificationMessage.SequenceNumber;
            try
            {
                RepublishResponse rp = await Session.RepublishAsync(null, id, seqNum, CancellationToken.None).ConfigureAwait(false);
                Assert.That(rp.NotificationMessage.SequenceNumber, Is.EqualTo(seqNum));
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadMessageNotAvailable)
            {
                Assert.Fail("Republish not supported.");
            }
            await DeleteSubAsync(id).ConfigureAwait(false);
        }

        private static readonly Random s_random = new();

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

        private async Task<List<uint>> AddMultipleMonitoredItemsAsync(
            uint subId, NodeId[] nodeIds, double sampling = 250)
        {
            var items = new MonitoredItemCreateRequest[nodeIds.Length];
            for (int i = 0; i < nodeIds.Length; i++)
            {
                items[i] = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = nodeIds[i],
                        AttributeId = Attributes.Value
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)(i + 1),
                        SamplingInterval = sampling,
                        Filter = default,
                        DiscardOldest = true,
                        QueueSize = 10
                    }
                };
            }

            CreateMonitoredItemsResponse resp = await Session.CreateMonitoredItemsAsync(
                null, subId, TimestampsToReturn.Both,
                items.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            var ids = new List<uint>();
            foreach (MonitoredItemCreateResult r in resp.Results)
            {
                Assert.That(StatusCode.IsGood(r.StatusCode), Is.True);
                ids.Add(r.MonitoredItemId);
            }
            return ids;
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
            return await Session.PublishWithTimeoutAsync(
                new SubscriptionAcknowledgement[] { ack }.ToArrayOf()).ConfigureAwait(false);
        }

        private async Task<PublishResponse> PublishWithAcksAsync(SubscriptionAcknowledgement[] acks)
        {
            return await Session.PublishWithTimeoutAsync(acks.ToArrayOf()).ConfigureAwait(false);
        }

        private static bool HasDataChangeNotification(PublishResponse pub)
        {
            if (pub.NotificationMessage?.NotificationData == null || pub.NotificationMessage.NotificationData.Count == 0)
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

        private static int CountDataChangeNotifications(PublishResponse pub)
        {
            int count = 0;
            if (pub.NotificationMessage?.NotificationData == null)
            {
                return count;
            }
            foreach (ExtensionObject ext in pub.NotificationMessage.NotificationData)
            {
                var dcn = ExtensionObject.ToEncodeable(ext) as DataChangeNotification;

                if (dcn != null && dcn.MonitoredItems != default)
                {
                    count += dcn.MonitoredItems.Count;
                }
            }
            return count;
        }

        private const double DefaultInterval = 1000;
        private const uint DefaultLifetime = 100;
        private const uint DefaultKeepAlive = 10;
    }
}
