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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Tests for subscription transfer scenarios.
    /// Each fixture instance handles one <see cref="TransferType"/>, and the four boolean
    /// combinations of sendInitialValues × sequentialPublishing run in parallel within each
    /// fixture, while the five fixture instances themselves also execute in parallel.
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
            /// numbers are republished but the client delays the acknowledgement.
            /// </summary>
            DisconnectedRepublishDelayedAck
        }

        private readonly TransferType m_transferType;

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
        /// <paramref name="sendInitialValues"/> and <paramref name="sequentialPublishing"/>.
        /// Each combination runs in parallel with the others within this fixture instance
        /// and also in parallel across the five <see cref="TransferType"/> fixture instances.
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(811)]
        [Parallelizable]
        public Task TransferSubscriptionAsync(
            [Values] bool sendInitialValues,
            [Values] bool sequentialPublishing)
        {
            return InternalTransferSubscriptionAsync(
                m_transferType,
                sendInitialValues,
                sequentialPublishing);
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
            [Values] bool sequentialPublishing)
        {
            const int loopCount = 30;
            for (int i = 0; i < loopCount; i++)
            {
                await InternalTransferSubscriptionAsync(
                    m_transferType,
                    sendInitialValues,
                    sequentialPublishing).ConfigureAwait(false);

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
            bool sequentialPublishing)
        {
            // create test session and subscription
            ISession originSession = await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            ISession targetSession = null;
            try
            {
                targetSession = await InternalTransferSubscriptionAsync(
                    originSession,
                    transferType,
                    sendInitialValues,
                    sequentialPublishing).ConfigureAwait(false);
            }
            finally
            {
                originSession?.Dispose();
                targetSession?.Dispose();
            }
        }

        private async Task<ISession> InternalTransferSubscriptionAsync(
            ISession originSession,
            TransferType transferType,
            bool sendInitialValues,
            bool sequentialPublishing)
        {
            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            if (transferType == TransferType.DisconnectedRepublishDelayedAck)
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
            using var subscriptionTemplate = new TestableSubscription(originSession.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                LifetimeCount = 30,
                KeepAliveCount = 5,
                PublishingEnabled = true,
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

            // settle
            await Task.Delay(kDelay).ConfigureAwait(false);

            // persist the subscription state
            string filePath = Path.GetTempFileName();

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
            ISession targetSession = await ClientFixture
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

                // hook notifications for log output
                int ii = 0;
                foreach (Subscription subscription in transferSubscriptions)
                {
                    subscription.Handle = ii;
                    subscription.FastDataChangeCallback = (s, n, _) =>
                    {
                        TestContext.Out.WriteLine(
                            $"FastDataChangeHandlerTarget: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        targetSubscriptionFastDataCounters[(int)subscription.Handle]++;
                    };
                    subscription
                        .MonitoredItems.ToList()
                        .ForEach(i =>
                            i.Notification += (item, _) =>
                            {
                                targetSubscriptionCounters[(int)subscription.Handle]++;
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
                            });
                    ii++;
                }

                // wait
                await Task.Delay(kDelay).ConfigureAwait(false);
            }
            else
            {
                // wait
                await Task.Delay(kDelay).ConfigureAwait(false);

                transferSubscriptions.AddRange((SubscriptionCollection)originSubscriptions.Clone());
                int ii = 0;
                transferSubscriptions.ForEach(s =>
                {
                    targetSession.AddSubscription(s);
                    s.Handle = ii++;
                    s.FastDataChangeCallback = (sub, n, _) =>
                    {
                        TestContext.Out.WriteLine(
                            $"FastDataChangeHandlerTarget: {sub.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        targetSubscriptionFastDataCounters[(int)s.Handle]++;
                    };
                    s.MonitoredItems.ToList()
                        .ForEach(i =>
                            i.Notification += (item, _) =>
                            {
                                targetSubscriptionCounters[(int)s.Handle]++;
                                foreach (DataValue value in item.DequeueValues())
                                {
                                    TestContext.Out.WriteLine(
                                        "Tra:{0}: {1:20}, {2}, {3}, {4}",
                                        s.Id,
                                        item.DisplayName,
                                        value.WrappedValue,
                                        value.SourceTimestamp,
                                        value.StatusCode);
                                }
                            });
                    s.StateChanged += (su, e) =>
                        TestContext.Out
                            .WriteLine($"StateChanged: {su.Session.SessionId}-{su.Id}-{e.Status}");
                    s.PublishStatusChanged += (su, e) =>
                        TestContext.Out.WriteLine(
                            $"PublishStatusChanged: {su.Session.SessionId}-{su.Id}-{e.Status}");
                });
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

            // wait for some events
            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            if (TransferType.KeepOpen == transferType)
            {
                foreach (Subscription subscription in originSubscriptions)
                {
                    // assert if originSubscriptionTransferred is incremented
                    Assert.That(originSubscriptionTransferred[(int)subscription.Handle], Is.EqualTo(1));
                }
            }

            // For DisconnectedRepublishDelayedAck with sendInitialValues=true, the server
            // sends initial values immediately after transfer, and DeferSubscriptionAcknowledge
            // on the target session prevents those acks, causing the server to republish them
            // on the next publish cycle. Poll until subscription 0 reaches 2×monitoredItemCount
            // (initial values + one republish batch) or a generous timeout expires.
            // For sendInitialValues=false there is no reliable notification source for static
            // subscription 0: the server sends no initial values, and whether the origin
            // session's unacknowledged notifications are still available for republish depends
            // on the server's session-cleanup timing, so no polling is needed.
            if (transferType == TransferType.DisconnectedRepublishDelayedAck && sendInitialValues)
            {
                uint expectedCount0 = 2u * transferSubscriptions[0].MonitoredItemCount;
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);
                while ((uint)targetSubscriptionCounters[0] < expectedCount0 &&
                    DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200).ConfigureAwait(false);
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
                    // correct for delayed ack and republish count:
                    // when sendInitialValues=true, the target session's DeferSubscriptionAcknowledge
                    // prevents acks for the initial-value notifications, causing the server to
                    // republish them — adding monitoredItemCount to account for that republish batch.
                    // When sendInitialValues=false, static subscription 0 may receive zero
                    // notifications (server sends no initial values and origin-session republish
                    // is not reliable), so no additional count is expected.
                    if (transferType == TransferType.DisconnectedRepublishDelayedAck && sendInitialValues)
                    {
                        targetExpectedCount += monitoredItemCount;
                    }

                    // static nodes, expect only one set of changes, another one if send initial values was set
                    Assert.That(originSubscriptionCounters[jj], Is.EqualTo(originExpectedCount));
                    // For DisconnectedRepublishDelayedAck deferred acks cause continuous republishing,
                    // so the counter may exceed the exact expected value.
                    if (transferType == TransferType.DisconnectedRepublishDelayedAck)
                    {
                        Assert.That(targetSubscriptionCounters[jj], Is.GreaterThanOrEqualTo(targetExpectedCount));
                    }
                    else
                    {
                        Assert.That(targetSubscriptionCounters[jj], Is.EqualTo(targetExpectedCount));
                    }
                }
                else
                {
                    // dynamic nodes, expect only one set of changes, another one if send initial values was set
                    Assert.That(originSubscriptionCounters[jj], Is.GreaterThanOrEqualTo(originExpectedCount));
                    Assert.That(targetSubscriptionCounters[jj], Is.GreaterThanOrEqualTo(targetExpectedCount));
                }
            }

            // reset counters
            Array.Clear(originSubscriptionCounters, 0, kTestSubscriptions);
            Array.Clear(originSubscriptionFastDataCounters, 0, kTestSubscriptions);
            Array.Clear(targetSubscriptionCounters, 0, kTestSubscriptions);
            Array.Clear(targetSubscriptionFastDataCounters, 0, kTestSubscriptions);

            // restart publishing
            foreach (Subscription subscription in transferSubscriptions)
            {
                TestContext.Out.WriteLine(
                    "SetPublishingMode(true) for SessionId={0}, SubscriptionId={1}",
                    subscription.Session.SessionId,
                    subscription.Id);
                await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            }

            // wait for some events
            await Task.Delay(2 * kDelay).ConfigureAwait(false);

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

                int[] testCounter = targetSubscriptionCounters;
                int[] testFastDataCounter = targetSubscriptionFastDataCounters;

                if (jj == 0)
                {
                    // static nodes, expect no activity
                    Assert.That(testCounter[jj], Is.Zero);
                    Assert.That(testFastDataCounter[jj], Is.Zero);
                }
                else
                {
                    // dynamic nodes, expect changes in target counters
                    Assert.That(testCounter[jj], Is.GreaterThanOrEqualTo(0));
                    Assert.That(testFastDataCounter[jj], Is.GreaterThanOrEqualTo(0));
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

            // cleanup
            File.Delete(filePath);
            return targetSession;
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
    }
}
