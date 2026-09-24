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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Subscriptions.Classic.Tests
{
    /// <summary>
    /// Tests for subscription transfer scenarios.
    /// Each fixture combines one <see cref="TransferType"/> with acknowledged or retained
    /// origin notifications, initial values, and sequential publishing.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("TransferSubscription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(TransferTypeArgs))]
    [Parallelizable(ParallelScope.Fixtures)]
    public class TransferSubscriptionTest : ClientTestFramework
    {
        /// <summary>
        /// Fixture argument list: one fixture instance per <see cref="TransferType"/> value.
        /// </summary>
        public static readonly object[] TransferTypeArgs =
        [
            new object[] { TransferType.KeepOpen },
            new object[] { TransferType.CloseSession },
            new object[] { TransferType.DisconnectedAck },
            new object[] { TransferType.DisconnectedRepublish },
            new object[] { TransferType.DisconnectedRepublishDelayedAck }
        ];

        /// <summary>
        /// Describes how a subscription transfer scenario is set up.
        /// </summary>
        public enum TransferType
        {
            /// <summary>
            /// The origin session remains open and gives up the subscription.
            /// </summary>
            KeepOpen,

            /// <summary>
            /// The origin session is gracefully closed with
            /// DeleteSubscriptionsOnClose set to false.
            /// </summary>
            CloseSession,

            /// <summary>
            /// The origin session gets network disconnected; available sequence
            /// numbers are just acknowledged.
            /// </summary>
            DisconnectedAck,

            /// <summary>
            /// The origin session gets network disconnected; available sequence
            /// numbers are republished.
            /// </summary>
            DisconnectedRepublish,

            /// <summary>
            /// The origin session gets network disconnected; available sequence
            /// numbers are republished but the target delays the acknowledgement.
            /// </summary>
            DisconnectedRepublishDelayedAck
        }

        [Test]
        public void TransferredSequencesRemainAvailableForRepublish()
        {
            using var subscription = new Subscription(Session.DefaultSubscription) { RepublishAfterTransfer = true };
            ArrayOf<uint> available = new uint[] { 9, 10, 11 }.ToArrayOf();
            MethodInfo processTransfer = typeof(Subscription).GetMethod(
                "ProcessTransferredSequenceNumbers", BindingFlags.Instance | BindingFlags.NonPublic)!;

            processTransfer.Invoke(subscription, [available]);

            Assert.That(subscription.AvailableSequenceNumbers.ToArray(), Is.EquivalentTo(available.ToArray()));
        }

        /// <summary>
        /// Initializes a new fixture instance for the given <paramref name="transferType"/>.
        /// </summary>
        public TransferSubscriptionTest(TransferType transferType)
            : base(Utils.UriSchemeOpcTcp)
        {
            m_transferType = transferType;
        }

        /// <summary>
        /// Start one server per fixture instance and establish a single shared session.
        /// Tests create their own sessions independently.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            // Use a shared session for namespace URI lookups; tests create their own sessions.
            SingleSession = true;
            MaxChannelCount = 1000;
            ClientFixtureSubscriptionEngineFactory = ClassicSubscriptionEngineFactory.Instance;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        /// <inheritdoc/>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <inheritdoc/>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <inheritdoc/>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        /// <summary>
        /// Transfer subscriptions for all combinations of
        /// <paramref name="sendInitialValues"/>, <paramref name="sequentialPublishing"/>,
        /// and <paramref name="retainOriginNotifications"/>.
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(811)]
        [Parallelizable]
        public Task TransferSubscriptionAsync(
            [Values] bool sendInitialValues,
            [Values] bool sequentialPublishing,
            [Values] bool retainOriginNotifications)
        {
            return InternalTransferSubscriptionAsync(
                m_transferType,
                sendInitialValues,
                sequentialPublishing,
                retainOriginNotifications);
        }

        /// <summary>
        /// Debug variant that repeats the transfer test 30 times (explicit, not for normal CI runs).
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(812)]
        [Explicit]
        public async Task TransferSubscriptionDebugAsync(
            [Values] bool sendInitialValues,
            [Values] bool sequentialPublishing,
            [Values] bool retainOriginNotifications)
        {
            const int loopCount = 30;
            for (int i = 0; i < loopCount; i++)
            {
                await InternalTransferSubscriptionAsync(
                    m_transferType,
                    sendInitialValues,
                    sequentialPublishing,
                    retainOriginNotifications).ConfigureAwait(false);

                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine($"Completed iteration {i + 1} of {loopCount}.");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
            }
        }

        private async Task InternalTransferSubscriptionAsync(
            TransferType transferType,
            bool sendInitialValues,
            bool sequentialPublishing,
            bool retainOriginNotifications)
        {
            using ISession originSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            string filePath = Path.GetTempFileName();
            try
            {
                await InternalTransferSubscriptionAsync(
                    originSession,
                    transferType,
                    sendInitialValues,
                    sequentialPublishing,
                    retainOriginNotifications,
                    filePath).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(filePath);
            }
        }

        private async Task InternalTransferSubscriptionAsync(
            ISession originSession,
            TransferType transferType,
            bool sendInitialValues,
            bool sequentialPublishing,
            bool retainOriginNotifications,
            string filePath)
        {
            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            if (retainOriginNotifications)
            {
                originSession.PublishSequenceNumbersToAcknowledge += DeferSubscriptionAcknowledge;
            }

            bool originSessionOpen = transferType == TransferType.KeepOpen;

            // create subscriptions
            var originSubscriptions = new SubscriptionCollection(kTestSubscriptions);
            int[] originSubscriptionCounters = new int[kTestSubscriptions];
            int[] originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionFastDataCounters = new int[kTestSubscriptions];
            int[] originSubscriptionTransferred = new int[kTestSubscriptions];
            var staticTargetNotifications = new ConcurrentQueue<DataChangeNotification>();
            int targetKeepAliveCount = 0;
            bool expectRepublish = retainOriginNotifications &&
                transferType >= TransferType.DisconnectedRepublish;
            int expectedStaticBatchCount = (expectRepublish ? 1 : 0) + (sendInitialValues ? 1 : 0);
            using var subscriptionTemplate = new TestableSubscription(originSession.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                LifetimeCount = 30,
                KeepAliveCount = 1,
                PublishingEnabled = false,
                RepublishAfterTransfer = transferType >= TransferType.DisconnectedRepublish,
                SequentialPublishing = sequentialPublishing
            };

            await CreateSubscriptionsAsync(
                originSession,
                subscriptionTemplate,
                originSubscriptions,
                originSubscriptionCounters,
                originSubscriptionFastDataCounters,
                kTestSubscriptions,
                kQueueSize).ConfigureAwait(false);

            if (TransferType.KeepOpen == transferType)
            {
                foreach (Subscription subscription in originSubscriptions)
                {
                    subscription.PublishStatusChanged += (s, e) =>
                    {
                        TestContext.Out.WriteLine(
                            $"PublishStatusChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
                        if ((e.Status & PublishStateChangedMask.Transferred) != 0)
                        {
                            // subscription transferred
                            Interlocked.Increment(ref originSubscriptionTransferred[(int)s.Handle]);
                        }
                    };
                }
            }

            foreach (Subscription subscription in originSubscriptions)
            {
                await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            }
            await TestPolling.WaitUntilAsync(
                () => Enumerable.Range(0, kTestSubscriptions).All(index =>
                    Volatile.Read(ref originSubscriptionCounters[index]) >=
                        originSubscriptions[index].MonitoredItemCount &&
                    Volatile.Read(ref originSubscriptionFastDataCounters[index]) > 0),
                timeoutMessage: "The origin did not deliver every initial monitored-item value.")
                .ConfigureAwait(false);

            Subscription staticOrigin = originSubscriptions[0];
            if (!retainOriginNotifications)
            {
                await TestPolling.WaitUntilAsync(
                    () => staticOrigin.AvailableSequenceNumbers.IsEmpty,
                    timeoutMessage: "The origin's initial notification was not acknowledged by the server.")
                    .ConfigureAwait(false);
            }
            else
            {
                Assert.That(
                    staticOrigin.AvailableSequenceNumbers.ToArray(),
                    Is.EqualTo([staticOrigin.SequenceNumber]));
            }
            NotificationMessage originStaticMessage = staticOrigin.Notifications.Single();
            Assert.That(originStaticMessage.NotificationData.Count, Is.EqualTo(1));
            Assert.That(
                originStaticMessage.NotificationData[0].TryGetValue(out DataChangeNotification originStaticData),
                Is.True);
            Assert.That(originStaticData.MonitoredItems.Count, Is.EqualTo(staticOrigin.MonitoredItemCount));

            // close session, do not delete subscription
            if (transferType != TransferType.KeepOpen)
            {
                originSession.DeleteSubscriptionsOnClose = false;

                // save with custom Subscription subclass information
                originSession.Save(filePath);

                if (transferType == TransferType.CloseSession)
                {
                    // graceful close
                    StatusCode close = await originSession.CloseAsync().ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(close), Is.True);
                }
                else
                {
                    // force a socket dispose, to emulate network disconnect
                    // without closing session on server
                    originSession.TransportChannel.Dispose();
                }
            }

            // wait
            await Task.Delay(kDelay).ConfigureAwait(false);

            // close session, do not delete subscription
            if (transferType > TransferType.CloseSession)
            {
                StatusCode closeResult2 = await originSession
                    .CloseAsync()
                    .ConfigureAwait(false);
            }

            // create target session
            using ISession targetSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            if (transferType == TransferType.DisconnectedRepublishDelayedAck)
            {
                targetSession.PublishSequenceNumbersToAcknowledge += DeferSubscriptionAcknowledge;
            }

            // restore client state
            var transferSubscriptions = new SubscriptionCollection();
            if (transferType != TransferType.KeepOpen)
            {
                // load subscriptions for transfer
                transferSubscriptions.AddRange(targetSession.Load(filePath, true));
            }
            else
            {
                transferSubscriptions.AddRange((SubscriptionCollection)originSubscriptions.Clone());
            }

            for (int ii = 0; ii < transferSubscriptions.Count; ii++)
            {
                Subscription subscription = transferSubscriptions[ii];
                if (originSessionOpen)
                {
                    Assert.That(targetSession.AddSubscription(subscription), Is.True);
                }
                subscription.Handle = ii;
                subscription.FastDataChangeCallback = (s, notification, _) =>
                {
                    TestContext.Out.WriteLine(
                        "FastDataChangeHandlerTarget: {0}-{1}-{2}",
                        s.Id,
                        notification.SequenceNumber,
                        notification.MonitoredItems.Count);
                    int index = (int)s.Handle;
                    Interlocked.Increment(ref targetSubscriptionFastDataCounters[index]);
                    if (index == 0)
                    {
                        staticTargetNotifications.Enqueue(notification);
                    }
                };
                if (ii == 0)
                {
                    subscription.FastKeepAliveCallback = (_, _) => Interlocked.Increment(ref targetKeepAliveCount);
                }
                foreach (MonitoredItem monitoredItem in subscription.MonitoredItems)
                {
                    monitoredItem.Notification += (item, _) =>
                    {
                        Interlocked.Increment(ref targetSubscriptionCounters[(int)subscription.Handle]);
                        foreach (DataValue value in item.DequeueValues())
                        {
                            TestContext.Out.WriteLine(
                                "Tra:{0}: {1:20}, {2}, {3}, {4}",
                                subscription.Id,
                                item.DisplayName,
                                value.WrappedValue,
                                value.SourceTimestamp,
                                value.StatusCode);
                        }
                    };
                }
                subscription.StateChanged += (s, e) =>
                    TestContext.Out.WriteLine($"StateChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
                subscription.PublishStatusChanged += (s, e) =>
                    TestContext.Out.WriteLine($"PublishStatusChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
            }

            // transfer restored subscriptions
            bool result = await targetSession
                .TransferSubscriptionsAsync(transferSubscriptions, sendInitialValues)
                .ConfigureAwait(false);
            Assert.That(result, Is.True);

            // validate results
            for (int ii = 0; ii < transferSubscriptions.Count; ii++)
            {
                Assert.That(transferSubscriptions[ii].Created, Is.True);
            }

            TestContext.Out
                .WriteLine("TargetSession is now SessionId={0}", targetSession.SessionId);

            await TestPolling.WaitUntilAsync(
                () => staticTargetNotifications.Count >= expectedStaticBatchCount &&
                    Volatile.Read(ref targetKeepAliveCount) > 0 &&
                    Enumerable.Range(1, kTestSubscriptions - 1).All(index =>
                        Volatile.Read(ref targetSubscriptionCounters[index]) > 0 &&
                        Volatile.Read(ref targetSubscriptionFastDataCounters[index]) > 0),
                timeoutMessage: "The target did not deliver expected notifications and a keepalive.")
                .ConfigureAwait(false);

            if (TransferType.KeepOpen == transferType)
            {
                await TestPolling.WaitUntilAsync(
                    () => Enumerable.Range(0, kTestSubscriptions).All(index =>
                        Volatile.Read(ref originSubscriptionTransferred[index]) > 0),
                    timeoutMessage: "The origin did not receive every subscription-transferred notification.")
                    .ConfigureAwait(false);
                foreach (Subscription subscription in originSubscriptions)
                {
                    Assert.That(originSubscriptionTransferred[(int)subscription.Handle], Is.EqualTo(1));
                }
            }

            // stop publishing
            foreach (Subscription subscription in transferSubscriptions)
            {
                TestContext.Out.WriteLine(
                    "SetPublishingMode(false) for SessionId={0}, SubscriptionId={1}",
                    subscription.Session.SessionId,
                    subscription.Id);
                await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);
            }

            // validate expected counts
            for (int jj = 0; jj < kTestSubscriptions; jj++)
            {
                TestContext.Out.WriteLine(
                    "-- Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj,
                    originSubscriptionCounters[jj],
                    targetSubscriptionCounters[jj]);
                TestContext.Out.WriteLine(
                    "-- Subscription {0}: OriginFastDataCounts {1}, TargetFastDataCounts {2} ",
                    jj,
                    originSubscriptionFastDataCounters[jj],
                    targetSubscriptionFastDataCounters[jj]);
                uint monitoredItemCount = transferSubscriptions[jj].MonitoredItemCount;
                uint originExpectedCount = monitoredItemCount;
                uint targetExpectedCount = sendInitialValues ? monitoredItemCount : 0;
                if (jj == 0)
                {
                    targetExpectedCount = (uint)expectedStaticBatchCount * monitoredItemCount;
                    Assert.That(originSubscriptionCounters[jj], Is.EqualTo(originExpectedCount));
                    Assert.That(originSubscriptionFastDataCounters[jj], Is.EqualTo(1));
                    Assert.That(targetSubscriptionCounters[jj], Is.EqualTo(targetExpectedCount));
                    Assert.That(targetSubscriptionFastDataCounters[jj], Is.EqualTo(expectedStaticBatchCount));
                    AssertStaticNotifications(
                        staticTargetNotifications, originStaticData, expectRepublish, sendInitialValues);
                }
                else
                {
                    // dynamic nodes, expect only one set of changes, another one if send initial values was set
                    Assert.That(originSubscriptionCounters[jj], Is.GreaterThanOrEqualTo(originExpectedCount));
                    Assert.That(targetSubscriptionCounters[jj], Is.GreaterThanOrEqualTo(targetExpectedCount));
                }
            }

            int[] countsBeforeResume =
            [
                .. Enumerable.Range(0, kTestSubscriptions)
                    .Select(index => Volatile.Read(ref targetSubscriptionCounters[index]))
            ];
            int[] batchesBeforeResume =
            [
                .. Enumerable.Range(0, kTestSubscriptions)
                    .Select(index => Volatile.Read(ref targetSubscriptionFastDataCounters[index]))
            ];
            int keepAlivesBeforeResume = Volatile.Read(ref targetKeepAliveCount);

            // restart publishing
            foreach (Subscription subscription in transferSubscriptions)
            {
                TestContext.Out.WriteLine(
                    "SetPublishingMode(true) for SessionId={0}, SubscriptionId={1}",
                    subscription.Session.SessionId,
                    subscription.Id);
                await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            }

            await TestPolling.WaitUntilAsync(
                () => Volatile.Read(ref targetKeepAliveCount) > keepAlivesBeforeResume &&
                    Enumerable.Range(1, kTestSubscriptions - 1).All(index =>
                        Volatile.Read(ref targetSubscriptionCounters[index]) > countsBeforeResume[index] &&
                        Volatile.Read(ref targetSubscriptionFastDataCounters[index]) > batchesBeforeResume[index]),
                timeoutMessage: "Transferred subscriptions did not resume dynamic values and static keepalives.")
                .ConfigureAwait(false);

            // validate expected counts
            for (int jj = 0; jj < kTestSubscriptions; jj++)
            {
                TestContext.Out.WriteLine(
                    "-- Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj,
                    originSubscriptionCounters[jj],
                    targetSubscriptionCounters[jj]);
                TestContext.Out.WriteLine(
                    "-- Subscription {0}: OriginFastDataCounts {1}, TargetFastDataCounts {2} ",
                    jj,
                    originSubscriptionFastDataCounters[jj],
                    targetSubscriptionFastDataCounters[jj]);

                if (jj == 0)
                {
                    Assert.That(targetSubscriptionCounters[jj], Is.EqualTo(countsBeforeResume[jj]));
                    Assert.That(targetSubscriptionFastDataCounters[jj], Is.EqualTo(batchesBeforeResume[jj]));
                    AssertStaticNotifications(
                        staticTargetNotifications, originStaticData, expectRepublish, sendInitialValues);
                }
                else
                {
                    Assert.That(targetSubscriptionCounters[jj], Is.GreaterThan(countsBeforeResume[jj]));
                    Assert.That(targetSubscriptionFastDataCounters[jj], Is.GreaterThan(batchesBeforeResume[jj]));
                }
            }

            targetSession.DeleteSubscriptionsOnClose = true;

            // close sessions
            StatusCode closeResult = await targetSession.CloseAsync().ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closeResult), Is.True);

            if (originSessionOpen)
            {
                closeResult = await originSession.CloseAsync().ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(closeResult), Is.True);
            }
        }

        private static void AssertStaticNotifications(
            ConcurrentQueue<DataChangeNotification> received,
            DataChangeNotification origin,
            bool expectRepublish,
            bool sendInitialValues)
        {
            DataChangeNotification[] notifications = [.. received];
            Assert.That(
                notifications.Count(notification => notification.SequenceNumber == origin.SequenceNumber),
                Is.EqualTo(expectRepublish ? 1 : 0),
                "Only a retained origin message requested for republish should reuse its sequence number.");
            Assert.That(
                notifications.Count(notification => notification.SequenceNumber != origin.SequenceNumber),
                Is.EqualTo(sendInitialValues ? 1 : 0),
                "Requested initial values must arrive in one new notification message.");
            Assert.That(notifications.Select(notification => notification.SequenceNumber), Is.Unique);

            Dictionary<uint, MonitoredItemNotification> originalItems =
                origin.MonitoredItems.ToArray().ToDictionary(item => item.ClientHandle);
            foreach (DataChangeNotification notification in notifications)
            {
                Assert.That(notification.SequenceNumber, Is.GreaterThanOrEqualTo(origin.SequenceNumber));
                Assert.That(
                    notification.MonitoredItems.ToArray().Select(item => item.ClientHandle),
                    Is.EquivalentTo(originalItems.Keys));
                foreach (MonitoredItemNotification item in notification.MonitoredItems)
                {
                    Assert.That(
                        originalItems.TryGetValue(item.ClientHandle, out MonitoredItemNotification original),
                        Is.True);
                    Assert.That(item.Value.WrappedValue, Is.EqualTo(original.Value.WrappedValue));
                    Assert.That(item.Value.StatusCode, Is.EqualTo(original.Value.StatusCode));
                    Assert.That(item.Value.SourceTimestamp, Is.EqualTo(original.Value.SourceTimestamp));
                }
            }
        }

        /// <summary>
        /// Event handler to defer publish response sequence number acknowledge.
        /// </summary>
        private static void DeferSubscriptionAcknowledge(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            // for testing do not ack any sequence numbers
            e.DeferredAcknowledgementsToSend.Clear();
            e.AcknowledgementsToSend.Clear();
        }

        private readonly TransferType m_transferType;
    }
}
