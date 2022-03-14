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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;

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
            Session.KeepAlive += (Session sender, KeepAliveEventArgs e) => { keepAlive++; };

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
                }
            };
            list2.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list);
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
            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber));
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
            Session.KeepAlive += (Session sender, KeepAliveEventArgs e) => { keepAlive++; };

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
                }
            };
            list2.ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                foreach (var value in item.DequeueValues())
                {
                    TestContext.Out.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                }
            });
            subscription.AddItems(list);
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
        public void LoadSubscription()
        {
            if (!File.Exists(m_subscriptionTestXml)) Assert.Ignore("Save file {0} does not exist yet", m_subscriptionTestXml);

            // load
            var subscriptions = Session.Load(m_subscriptionTestXml);
            Assert.NotNull(subscriptions);
            Assert.IsNotEmpty(subscriptions);

            foreach (var subscription in subscriptions)
            {
                Session.AddSubscription(subscription);
                subscription.Create();
            }

            Thread.Sleep(5000);

            foreach (var subscription in subscriptions)
            {
                OutputSubscriptionInfo(TestContext.Out, subscription);
            }

            var result = Session.RemoveSubscriptions(subscriptions);
            Assert.True(result);

        }

        [Theory, Order(300)]
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

            // multiple Subscriptions to enforce multiple queued publish requests
            for (int i = 0; i < subscriptions; i++)
            {
                var s = new Subscription(Session.DefaultSubscription) {
                    SequentialPublishing = enabled,
                    PublishingInterval = 0,
                    DisableMonitoredItemCache = true, //Not needed
                    PublishingEnabled = false
                };
                subscriptionList.Add(s);
            }

            var subscription = subscriptionList[0];

            // Create monitored items on the server
            // and track the last reported source timestamp
            var list = Enumerable.Range(1, monitoredItemsPerSubscription).Select(_ => new MonitoredItem(subscription.DefaultItem) {
                StartNodeId = new NodeId("Scalar_Simulation_Int32", 2),
                SamplingInterval = 0,
            }).ToList();
            var dict = list.ToDictionary(item => item.ClientHandle, _ => DateTime.MinValue);
            subscription.AddItems(list);

            foreach (var s in subscriptionList)
            {
                var boolResult = Session.AddSubscription(s);
                Assert.True(boolResult);
                s.Create();
                var publishInterval = (int)s.CurrentPublishingInterval;
                TestContext.Out.WriteLine($"CurrentPublishingInterval: {publishInterval}");
                subscriptionIds.Add(s.Id);
            }

            //Need to realize test failed, assert needs to be brought to this thread
            subscription.FastDataChangeCallback = (_, notification, __) => {
                notification.MonitoredItems.ForEach(item => {
                    Interlocked.Increment(ref numOfNotifications);
                    if (dict[item.ClientHandle] > item.Value.SourceTimestamp)
                    {
                        TestContext.Out.WriteLine("Out of order encountered");
                        sequenceBroken.Set();
                        return;
                    }
                    dict[item.ClientHandle] = item.Value.SourceTimestamp;
                    Thread.Sleep(10);
                });
            };

            var stopwatch = Stopwatch.StartNew();

            // start
            Session.SetPublishingMode(null, true, subscriptionIds, out var results, out var diagnosticInfos);

            // Wait for out-of-order to occur
            var failed = sequenceBroken.WaitOne(testWaitTime);

            // stop
            Session.SetPublishingMode(null, false, subscriptionIds, out results, out diagnosticInfos);

            //Log information
            var elapsed = stopwatch.Elapsed.TotalMilliseconds / 1000.0;
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
                subscriptionList.Add(subscription);
                var simulatedNodes = GetTestSetSimulation(Session.NamespaceUris);
                var list = new List<MonitoredItem>();
                var nodeSet = simulatedNodes;
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
                TestContext.Out.WriteLine($"CurrentPublishingInterval: {publishInterval}");

                subscription.FastDataChangeCallback = (_, notification, __) => {
                    notification.MonitoredItems.ForEach(item => {
                        Interlocked.Increment(ref numOfNotifications);
                    });
                };
            }

            var stopwatch = Stopwatch.StartNew();

            await Task.Delay(1000).ConfigureAwait(false);

            // verify that number of active publishrequests is never exceeded
            while (stopwatch.ElapsedMilliseconds < testWaitTime)
            {
                // use the sample server default for max publish request count
                Assert.GreaterOrEqual(maxServerPublishRequest, Session.GoodPublishRequestCount);
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
            /// just ackoledged.
            /// </summary>
            DisconnectedAck,
            /// <summary>
            /// The origin session gets network disconnected,
            /// after transfer available sequence numbers are
            /// republished.
            /// </summary>
            DisconnectedRepublish
        }

        [Theory, Order(810)]
        public async Task TransferSubscription(TransferType transferType, bool sendInitialValues)
        {
            const int kTestSubscriptions = 2;
            const int kDelay = 2_000;

            // create test session and subscription
            var originSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
            bool originSessionOpen = transferType == TransferType.KeepOpen;

            // create subscriptions
            var originSubscriptions = new SubscriptionCollection();
            var originSubscriptionCounters = new int[kTestSubscriptions];
            var targetSubscriptionCounters = new int[kTestSubscriptions];

            for (int ii = 0; ii < kTestSubscriptions; ii++)
            {
                // create subscription with static monitored items
                var subscription = new Subscription(originSession.DefaultSubscription) {
                    PublishingInterval = 1_000,
                    PublishingEnabled = true,
                };

                originSubscriptions.Add(subscription);
                originSession.AddSubscription(subscription);
                subscription.RepublishAfterTransfer = transferType == TransferType.DisconnectedRepublish;
                subscription.Create();

                // set defaults
                subscription.DefaultItem.DiscardOldest = true;
                subscription.DefaultItem.QueueSize = (ii == 0) ? 0U : 5;
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

                subscription.Handle = ii;
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

            // restore client state
            var transferSubscriptions = new SubscriptionCollection();
            if (transferType != TransferType.KeepOpen)
            {
                // load
                transferSubscriptions.AddRange(targetSession.Load(filePath));

                // hook notifications for log output
                int ii = 0;
                foreach (var subscription in transferSubscriptions)
                {
                    subscription.Handle = ii;
                    subscription.MonitoredItems.ToList().ForEach(i => i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                        targetSubscriptionCounters[(int)subscription.Handle]++;
                        foreach (var value in item.DequeueValues())
                        {
                            TestContext.Out.WriteLine("Tra:{0}: {1:20}, {2}, {3}, {4}", subscription.Id, item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                        }
                    });
                    ii++;
                }
            }
            else
            {
                transferSubscriptions.AddRange(originSubscriptions);
            }

            // wait 
            await Task.Delay(kDelay).ConfigureAwait(false);

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
                TestContext.Out.WriteLine("Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj, originSubscriptionCounters[jj], targetSubscriptionCounters[jj]);
                var monitoredItemCount = transferSubscriptions[jj].MonitoredItemCount;
                var originExpectedCount = sendInitialValues && originSessionOpen ? monitoredItemCount * 2 : monitoredItemCount;
                var targetExpectedCount = sendInitialValues && !originSessionOpen ? monitoredItemCount : 0;
                if (jj == 0)
                {
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

            // close sessions
            targetSession.Close();
            if (originSessionOpen)
            {
                originSession.Close();
            }

            // cleanup
            File.Delete(filePath);
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
        #endregion
    }
}
