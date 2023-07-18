/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Test Client Services.
    /// </summary>
    [TestFixture, Category("Client")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public class SubscriptionTest : ClientTestFramework
    {
        private readonly string m_subscriptionTestXml = Path.Combine(Path.GetTempPath(), "SubscriptionTest.xml");

        #region Test Setup
        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public new Task OneTimeSetUp()
        {
            // the tests can be run against server specified in .runsettings
            SupportsExternalServerUrl = true;
            // create a new session for every test
            SingleSession = false;
            return base.OneTimeSetUpAsync(null);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public new Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public new Task SetUp()
        {
            return base.SetUp();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public new Task TearDown()
        {
            return base.TearDown();
        }
        #endregion

        #region Test Methods
        [Test, Order(100)]
        public void AddSubscription()
        {
            var subscription = new Subscription();

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (ISession sender, KeepAliveEventArgs e) => { keepAlive++; };

            // add current time
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            };
            list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });

            subscription = new Subscription(Session.DefaultSubscription);
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.AddItem(list.First());
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            bool result = Session.AddSubscription(subscription);
            Assert.True(result);
            subscription.Create();

            // add state
            var list2 = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState", StartNodeId = VariableIds.Server_ServerStatus_State
                },
            };

            var simulatedNodes = GetTestSetSimulation(Session.NamespaceUris);
            list2.AddRange(CreateMonitoredItemTestSet(subscription, simulatedNodes));
            list2.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list2);
            subscription.ApplyChanges();
            subscription.SetPublishingMode(false);
            Assert.False(subscription.PublishingEnabled);
            subscription.SetPublishingMode(true);
            Assert.True(subscription.PublishingEnabled);
            Assert.False(subscription.PublishingStopped);

            subscription.Priority = 200;
            subscription.Modify();

            // save
            Session.Save(m_subscriptionTestXml);

            Thread.Sleep(5000);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            subscription.ConditionRefresh();
            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber + 100));
            Assert.AreEqual(StatusCodes.BadMessageNotAvailable, sre.StatusCode);

            subscription.RemoveItems(list);
            subscription.ApplyChanges();

            subscription.RemoveItem(list2.First());

            result = Session.RemoveSubscription(subscription);
            Assert.True(result);
        }

        [Test, Order(110)]
        public async Task AddSubscriptionAsync()
        {
            var subscription = new Subscription();

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (ISession sender, KeepAliveEventArgs e) => { keepAlive++; };

            // add current time
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime", StartNodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            };
            list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });

            subscription = new Subscription(Session.DefaultSubscription);
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.AddItem(list.First());
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            bool result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.False(result);
            result = await Session.RemoveSubscriptionsAsync(new List<Subscription>() { subscription }).ConfigureAwait(false);
            Assert.False(result);
            result = Session.AddSubscription(subscription);
            Assert.True(result);
            result = Session.AddSubscription(subscription);
            Assert.False(result);
            result = await Session.RemoveSubscriptionsAsync(new List<Subscription>() { subscription }).ConfigureAwait(false);
            Assert.True(result);
            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.False(result);
            result = Session.AddSubscription(subscription);
            Assert.True(result);
            await subscription.CreateAsync().ConfigureAwait(false);

            // add state
            var list2 = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState", StartNodeId = VariableIds.Server_ServerStatus_State
                },
            };

            var simulatedNodes = GetTestSetSimulation(Session.NamespaceUris);
            list2.AddRange(CreateMonitoredItemTestSet(subscription, simulatedNodes));
            list2.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list2);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);
            Assert.False(subscription.PublishingEnabled);
            await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            Assert.True(subscription.PublishingEnabled);
            Assert.False(subscription.PublishingStopped);

            subscription.Priority = 200;
            await subscription.ModifyAsync().ConfigureAwait(false);

            // save
            Session.Save(m_subscriptionTestXml);

            await Task.Delay(5000).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber));
            Assert.AreEqual(StatusCodes.BadMessageNotAvailable, sre.StatusCode);

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            subscription.RemoveItem(list2.First());

            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.True(result);
        }

        [Test, Order(200)]
        public async Task LoadSubscriptionAsync()
        {
            if (!File.Exists(m_subscriptionTestXml)) Assert.Ignore("Save file {0} does not exist yet", m_subscriptionTestXml);

            // load
            var subscriptions = Session.Load(m_subscriptionTestXml);
            Assert.NotNull(subscriptions);
            Assert.IsNotEmpty(subscriptions);

            int valueChanges = 0;

            foreach (var subscription in subscriptions)
            {
                var list = subscription.MonitoredItems.ToList();
                list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                    foreach (var value in item.DequeueValues())
                    {
                        valueChanges++;
                        TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                    }
                });

                Session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
            }

            await Task.Delay(5000).ConfigureAwait(false);

            TestContext.Out.WriteLine("{0} value changes.", valueChanges);

            Assert.GreaterOrEqual(valueChanges, 10);

            foreach (var subscription in Session.Subscriptions)
            {
                OutputSubscriptionInfo(TestContext.Out, subscription);
            }

            var result = await Session.RemoveSubscriptionsAsync(subscriptions).ConfigureAwait(false);
            Assert.True(result);
        }

        [Theory, Order(300)]
        [Timeout(30_000)]
        /// <remarks>
        /// This test doesn't deterministically prove sequential publishing,
        /// but rather relies on a subscription not being able to handle the message load.
        /// This test should be re-implemented with a Session that deterministically
        /// provides the wrong order of messages to Subscription.
        ///</remarks>
        public void SequentialPublishingSubscription(bool enabled)
        {
            var subscriptionList = new List<Subscription>();
            var subscriptionIds = new UInt32Collection();
            var sequenceBroken = new AutoResetEvent(false);
            var numOfNotifications = 0L;
            const int testWaitTime = 10000;
            const int monitoredItemsPerSubscription = 500;
            const int subscriptions = 10;

            Session.MinPublishRequestCount = 3;

            // multiple Subscriptions to enforce multiple queued publish requests
            for (int i = 0; i < subscriptions; i++)
            {
                var s = new Subscription(Session.DefaultSubscription) {
                    SequentialPublishing = enabled,
                    KeepAliveCount = 10,
                    PublishingInterval = 100,
                    DisableMonitoredItemCache = true,
                    PublishingEnabled = false,
                    MaxMessageCount = 1
                };
                subscriptionList.Add(s);
            }

            var subscription = subscriptionList[0];

            // Create monitored items on the server
            // and track the last reported sequence number
            var testSet = new List<NodeId>();
            while (testSet.Count < monitoredItemsPerSubscription)
            {
                testSet.AddRange(GetTestSetFullSimulation(Session.NamespaceUris));
            }
            var monitoredItemsList = testSet.Select(nodeId => new MonitoredItem(subscription.DefaultItem) {
                StartNodeId = nodeId,
                SamplingInterval = 0,
            }).ToList();

            subscription.AddItems(monitoredItemsList);

            foreach (var s in subscriptionList)
            {
                var boolResult = Session.AddSubscription(s);
                Assert.True(boolResult);
                s.Create();
                var publishInterval = (int)s.CurrentPublishingInterval;
                TestContext.Out.WriteLine($"CurrentPublishingInterval: {publishInterval}");
                subscriptionIds.Add(s.Id);
            }

            // populate sequence number dictionary
            var dictionary = new ConcurrentDictionary<uint, uint>();
            foreach (var item in subscriptionList)
            {
                dictionary.TryAdd(item.Id, 0);
            }

            // track the last reported sequence number
            subscription.FastDataChangeCallback = (s, notification, __) => {
                Interlocked.Increment(ref numOfNotifications);
                if (dictionary[s.Id] > notification.SequenceNumber)
                {
                    TestContext.Out.WriteLine("Out of order encountered Id: {0}, {1} > {2}", s.Id, dictionary[s.Id], notification.SequenceNumber);
                    sequenceBroken.Set();
                    return;
                };
                dictionary[s.Id] = notification.SequenceNumber;
            };

            var stopwatch = Stopwatch.StartNew();

            // start
            Session.SetPublishingMode(null, true, subscriptionIds, out var results, out var diagnosticInfos);

            // Wait for out-of-order to occur
            var failed = sequenceBroken.WaitOne(testWaitTime);
            if (failed)
            {
                TestContext.Out.WriteLine("Error detected, terminating after 1 second");
                Thread.Sleep(1000);
            }

            // stop
            Session.SetPublishingMode(null, false, subscriptionIds, out results, out diagnosticInfos);

            //Log information
            double elapsed = stopwatch.Elapsed.TotalMilliseconds / 1000.0;
            TestContext.Out.WriteLine($"Ran for: {elapsed:N} seconds");
            long totalNotifications = Interlocked.Read(ref numOfNotifications);
            double notificationRate = totalNotifications / elapsed;
            int outstandingMessageWorkers = subscription.OutstandingMessageWorkers;
            TestContext.Out.WriteLine($"Id: {subscription.Id} Outstanding workers: {outstandingMessageWorkers}");

            // clean up before validating conditions
            foreach (var s in subscriptionList)
            {
                var result = Session.RemoveSubscription(s);
                Assert.True(result);
            }

            TestContext.Out.WriteLine($"Number of notifications: {totalNotifications:N0}");
            //How fast it processed notifications.
            TestContext.Out.WriteLine($"Notifications rate: {notificationRate:N} per second");
            //No notifications means nothing worked
            Assert.NotZero(totalNotifications);

            // The issue more unlikely seem to appear on .NET 6 in the given timeframe
            if (!enabled && !failed)
            {
                Assert.Inconclusive("The test couldn't validate the issue on this platform");
            }

            // catch if expected/unexpected Out-of-sequence occurred
            Assert.AreEqual(enabled, !failed);
        }

        [Test, Order(400)]
        [Timeout(30_000)]
        public async Task PublishRequestCount()
        {
            var subscriptionList = new List<Subscription>();
            var numOfNotifications = 0L;
            const int testWaitTime = 10000;
            const int monitoredItemsPerSubscription = 50;
            const int subscriptions = 50;
            const int maxServerPublishRequest = 20;

            for (int i = 0; i < subscriptions; i++)
            {
                var subscription = new Subscription(Session.DefaultSubscription) {
                    PublishingInterval = 0,
                    DisableMonitoredItemCache = true,
                    PublishingEnabled = true
                };

                subscription.FastDataChangeCallback = (_, notification, __) => {
                    notification.MonitoredItems.ForEach(item => {
                        Interlocked.Increment(ref numOfNotifications);
                    });
                };

                subscriptionList.Add(subscription);
                var list = new List<MonitoredItem>();
                var nodeSet = GetTestSetFullSimulation(Session.NamespaceUris);

                for (int ii = 0; ii < monitoredItemsPerSubscription; ii++)
                {
                    var nextNode = nodeSet[ii % nodeSet.Count];
                    list.Add(new MonitoredItem(subscription.DefaultItem) {
                        StartNodeId = nextNode,
                        SamplingInterval = 0
                    });
                }
                var dict = list.ToDictionary(item => item.ClientHandle, _ => DateTime.MinValue);

                subscription.AddItems(list);
                var result = Session.AddSubscription(subscription);
                Assert.True(result);
                await subscription.CreateAsync().ConfigureAwait(false);
                var publishInterval = (int)subscription.CurrentPublishingInterval;

                TestContext.Out.WriteLine($"Id: {subscription.Id} CurrentPublishingInterval: {publishInterval}");

            }

            var stopwatch = Stopwatch.StartNew();

            await Task.Delay(1000).ConfigureAwait(false);

            // verify that number of active publishrequests is never exceeded
            while (stopwatch.ElapsedMilliseconds < testWaitTime)
            {
                // use the sample server default for max publish request count
                Assert.GreaterOrEqual(Math.Max(maxServerPublishRequest, subscriptions), Session.GoodPublishRequestCount);
                await Task.Delay(100).ConfigureAwait(false);
            }

            foreach (var subscription in subscriptionList)
            {
                var result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
                Assert.True(result);
            }
        }

        public enum TransferType
        {
            /// <summary>
            /// The origin session remains open and
            /// gives up the subscription.
            /// </summary>
            KeepOpen,
            /// <summary>
            /// The origin session is gracefully closed with
            /// DeleteSubscriptionsOnClose set to false.
            /// </summary>
            CloseSession,
            /// <summary>
            /// The origin session gets network disconnected,
            /// after transfer available sequence numbers are
            /// just acknoledged.
            /// </summary>
            DisconnectedAck,
            /// <summary>
            /// The origin session gets network disconnected,
            /// after transfer available sequence numbers are
            /// republished.
            /// </summary>
            DisconnectedRepublish,
            /// <summary>
            /// The origin session gets network disconnected,
            /// after transfer available sequence numbers are
            /// republished. The ack was delayed by the client.
            /// </summary>
            DisconnectedRepublishDelayedAck
        }

        [Theory, Order(810)]
        public async Task TransferSubscription(TransferType transferType, bool sendInitialValues, bool sequentialPublishing)
        {
            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            // create test session and subscription
            var originSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            if (transferType == TransferType.DisconnectedRepublishDelayedAck)
            {
                originSession.PublishSequenceNumbersToAcknowledge += DeferSubscriptionAcknowledge;
            }

            bool originSessionOpen = transferType == TransferType.KeepOpen;

            // create subscriptions
            var originSubscriptions = new SubscriptionCollection();
            var originSubscriptionCounters = new int[kTestSubscriptions];
            var originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var targetSubscriptionCounters = new int[kTestSubscriptions];
            var targetSubscriptionFastDataCounters = new int[kTestSubscriptions];

            for (int ii = 0; ii < kTestSubscriptions; ii++)
            {
                // create subscription with static monitored items
                var subscription = new Subscription(originSession.DefaultSubscription) {
                    PublishingInterval = 1_000,
                    KeepAliveCount = 2,
                    PublishingEnabled = true,
                    RepublishAfterTransfer = transferType >= TransferType.DisconnectedRepublish,
                    SequentialPublishing = sequentialPublishing,
                    Handle = ii,
                    FastDataChangeCallback = (s, n, _) => {
                        TestContext.Out.WriteLine($"FastDataChangeHandlerOrigin: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        originSubscriptionFastDataCounters[(int)s.Handle]++;
                    }
                };

                originSubscriptions.Add(subscription);
                originSession.AddSubscription(subscription);
                subscription.Create();

                // set defaults
                subscription.DefaultItem.DiscardOldest = true;
                subscription.DefaultItem.QueueSize = (ii == 0) ? 0U : kQueueSize;
                subscription.DefaultItem.MonitoringMode = MonitoringMode.Reporting;

                // create test set
                var namespaceUris = Session.NamespaceUris;
                var testSet = new List<NodeId>();
                if (ii == 0)
                {
                    testSet.AddRange(GetTestSetStatic(namespaceUris));
                }
                else
                {
                    testSet.AddRange(GetTestSetSimulation(namespaceUris));
                }

                var list = CreateMonitoredItemTestSet(subscription, testSet).ToList();
                list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                    originSubscriptionCounters[(int)subscription.Handle]++;
                    foreach (var value in item.DequeueValues())
                    {
                        TestContext.Out.WriteLine("Org:{0}: {1:20}, {2}, {3}, {4}", subscription.Id, item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                    }
                });
                subscription.AddItems(list);
                subscription.ApplyChanges();
            }

            // settle
            await Task.Delay(kDelay).ConfigureAwait(false);

            // persist the subscription state
            var filePath = Path.GetTempFileName();

            // close session, do not delete subscription
            if (transferType != TransferType.KeepOpen)
            {
                originSession.DeleteSubscriptionsOnClose = false;
                originSession.Save(filePath);
                if (transferType == TransferType.CloseSession)
                {
                    // graceful close
                    originSession.Close();
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
                originSession.Close();
            }

            // create target session
            var targetSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
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
                foreach (var subscription in transferSubscriptions)
                {
                    subscription.Handle = ii;
                    subscription.FastDataChangeCallback = (s, n, _) => {
                        TestContext.Out.WriteLine($"FastDataChangeHandlerTarget: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        targetSubscriptionFastDataCounters[(int)subscription.Handle]++;
                    };
                    subscription.MonitoredItems.ToList().ForEach(i =>
                        i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                            targetSubscriptionCounters[(int)subscription.Handle]++;
                            foreach (var value in item.DequeueValues())
                            {
                                TestContext.Out.WriteLine("Tra:{0}: {1:20}, {2}, {3}, {4}", subscription.Id, item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                            }
                        }
                    );
                    ii++;
                }

                // wait
                await Task.Delay(kDelay).ConfigureAwait(false);

            }
            else
            {
                // wait
                await Task.Delay(kDelay).ConfigureAwait(false);

                transferSubscriptions.AddRange(originSubscriptions);
                int ii = 0;
                transferSubscriptions.ForEach(s => {
                    s.Handle = ii++;
                    s.FastDataChangeCallback = (sub, n, _) => {
                        TestContext.Out.WriteLine($"FastDataChangeHandlerTarget: {sub.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        targetSubscriptionFastDataCounters[(int)s.Handle]++;
                    };
                });
            }

            // transfer restored subscriptions
            var result = targetSession.TransferSubscriptions(transferSubscriptions, sendInitialValues);
            Assert.IsTrue(result);

            // validate results
            for (int ii = 0; ii < transferSubscriptions.Count; ii++)
            {
                Assert.IsTrue(transferSubscriptions[ii].Created);
            }

            TestContext.Out.WriteLine("TargetSession is now SessionId={0}", targetSession.SessionId);

            // wait for some events
            await Task.Delay(kDelay).ConfigureAwait(false);

            // stop publishing
            foreach (var subscription in transferSubscriptions)
            {
                TestContext.Out.WriteLine("SetPublishingMode(false) for SessionId={0}, SubscriptionId={1}",
                    subscription.Session.SessionId, subscription.Id);
                subscription.SetPublishingMode(false);
            }

            // validate expected counts
            for (int jj = 0; jj < kTestSubscriptions; jj++)
            {
                TestContext.Out.WriteLine("-- Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj, originSubscriptionCounters[jj], targetSubscriptionCounters[jj]);
                TestContext.Out.WriteLine("-- Subscription {0}: OriginFastDataCounts {1}, TargetFastDataCounts {2} ",
                    jj, originSubscriptionFastDataCounters[jj], targetSubscriptionFastDataCounters[jj]);
                var monitoredItemCount = transferSubscriptions[jj].MonitoredItemCount;
                var originExpectedCount = sendInitialValues && originSessionOpen ? monitoredItemCount * 2 : monitoredItemCount;
                var targetExpectedCount = sendInitialValues && !originSessionOpen ? monitoredItemCount : 0;
                if (jj == 0)
                {
                    // correct for delayed ack and republish count
                    if (transferType == TransferType.DisconnectedRepublishDelayedAck)
                    {
                        targetExpectedCount += monitoredItemCount;
                    }

                    // static nodes, expect only one set of changes, another one if send initial values was set
                    Assert.AreEqual(originExpectedCount, originSubscriptionCounters[jj]);
                    Assert.AreEqual(targetExpectedCount, targetSubscriptionCounters[jj]);
                }
                else
                {
                    // dynamic nodes, expect only one set of changes, another one if send initial values was set
                    Assert.LessOrEqual(originExpectedCount, originSubscriptionCounters[jj]);
                    Assert.LessOrEqual(targetExpectedCount, targetSubscriptionCounters[jj]);
                }
            }

            // reset counters
            Array.Clear(originSubscriptionCounters, 0, kTestSubscriptions);
            Array.Clear(originSubscriptionFastDataCounters, 0, kTestSubscriptions);
            Array.Clear(targetSubscriptionCounters, 0, kTestSubscriptions);
            Array.Clear(targetSubscriptionFastDataCounters, 0, kTestSubscriptions);

            // restart publishing
            foreach (var subscription in transferSubscriptions)
            {
                TestContext.Out.WriteLine("SetPublishingMode(true) for SessionId={0}, SubscriptionId={1}",
                    subscription.Session.SessionId, subscription.Id);
                subscription.SetPublishingMode(true);
            }

            // wait for some events
            await Task.Delay(kDelay).ConfigureAwait(false);

            // validate expected counts
            for (int jj = 0; jj < kTestSubscriptions; jj++)
            {
                TestContext.Out.WriteLine("-- Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj, originSubscriptionCounters[jj], targetSubscriptionCounters[jj]);
                TestContext.Out.WriteLine("-- Subscription {0}: OriginFastDataCounts {1}, TargetFastDataCounts {2} ",
                    jj, originSubscriptionFastDataCounters[jj], targetSubscriptionFastDataCounters[jj]);

                int[] testCounter = targetSubscriptionCounters;
                int[] testFastDataCounter = targetSubscriptionFastDataCounters;
                if (transferType == TransferType.KeepOpen)
                {
                    testCounter = originSubscriptionCounters;
                }

                if (jj == 0)
                {
                    // static nodes, expect no activity
                    Assert.AreEqual(0, testCounter[jj]);
                    Assert.AreEqual(0, testFastDataCounter[jj]);
                }
                else
                {
                    // dynamic nodes, expect changes in target counters
                    Assert.Less(0, testCounter[jj]);
                    Assert.Less(0, testFastDataCounter[jj]);
                }
            }

            // close sessions
            targetSession.Close();
            if (originSessionOpen)
            {
                originSession.Close();
            }

            // cleanup
            File.Delete(filePath);
        }

        [Test, Order(1000)]
        public void FastKeepAliveCallback()
        {
            var subscription = new Subscription();

            // add current time
            subscription = new Subscription(Session.DefaultSubscription) {
                KeepAliveCount = 1,
                PublishingInterval = 250,
            };
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            // add static nodes
            var list = new List<MonitoredItem> {
                new MonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState", StartNodeId = VariableIds.Server_ServerStatus_State
                },
            };

            var staticNodes = GetTestSetStatic(Session.NamespaceUris);
            list.AddRange(CreateMonitoredItemTestSet(subscription, staticNodes));
            list.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list);

            int numOfKeepAliveNotifications = 0;
            subscription.FastKeepAliveCallback = (_, notification) => {
                var n = Interlocked.Increment(ref numOfKeepAliveNotifications);
                TestContext.Out.WriteLine("KeepAliveCallback {0} next sequenceNumber {1} PublishTime {2}", n, notification.SequenceNumber, notification.PublishTime);
            };

            int numOfDataChangeNotifications = 0;
            subscription.FastDataChangeCallback = (_, notification, __) => {
                var n = Interlocked.Increment(ref numOfDataChangeNotifications);
                TestContext.Out.WriteLine("DataChangeCallback {0} sequenceNumber {1} PublishTime {2} Count {3}",
                    n, notification.SequenceNumber, notification.PublishTime, notification.MonitoredItems.Count);
            };

            bool result = Session.AddSubscription(subscription);
            Assert.True(result);

            subscription.Create();
            subscription.ApplyChanges();
            subscription.SetPublishingMode(false);
            Assert.False(subscription.PublishingEnabled);
            subscription.SetPublishingMode(true);
            Assert.True(subscription.PublishingEnabled);
            Assert.False(subscription.PublishingStopped);

            subscription.Priority = 55;
            subscription.Modify();

            TestContext.Out.WriteLine("Waiting for keep alive");

            int delay = 2000;
            Thread.Sleep(delay);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            // expect at least half number of keep alive notifications
            Assert.LessOrEqual((delay / subscription.PublishingInterval) / 2, numOfKeepAliveNotifications);
            Assert.AreEqual(1, numOfDataChangeNotifications);

            TestContext.Out.WriteLine("Call ResendData.");
            bool resendData = subscription.ResendData();
            Assert.True(resendData);

            Thread.Sleep(delay);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            Assert.AreEqual(2, numOfDataChangeNotifications);

            TestContext.Out.WriteLine("Call ConditionRefresh.");
            bool conditionRefresh = subscription.ConditionRefresh();
            Assert.True(conditionRefresh);

            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber));
            Assert.AreEqual(StatusCodes.BadMessageNotAvailable, sre.StatusCode);

            subscription.RemoveItems(list);
            subscription.ApplyChanges();

            result = Session.RemoveSubscription(subscription);
            Assert.True(result);
        }
        #endregion

        #region Private Methods
        private IList<MonitoredItem> CreateMonitoredItemTestSet(Subscription subscription, IList<NodeId> nodeIds)
        {
            var list = new List<MonitoredItem>();
            foreach (NodeId nodeId in nodeIds)
            {
                var item = new MonitoredItem(subscription.DefaultItem) {
                    StartNodeId = nodeId
                };
                list.Add(item);
            };
            return list;
        }

        /// <summary>
        /// Event handler to defer publish response sequence number acknowledge.
        /// </summary>
        private void DeferSubscriptionAcknowledge(ISession session, PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            // for testing keep the latest sequence numbers for a while
            const int AckDelay = 4;
            if (e.AcknowledgementsToSend.Count > 0)
            {
                // defer latest sequence numbers
                var deferredItems = e.AcknowledgementsToSend.OrderByDescending(s => s.SequenceNumber).Take(AckDelay).ToList();
                e.DeferredAcknowledgementsToSend.AddRange(deferredItems);
                foreach (var deferredItem in deferredItems)
                {
                    e.AcknowledgementsToSend.Remove(deferredItem);
                }
            }
        }
        #endregion
    }
}
