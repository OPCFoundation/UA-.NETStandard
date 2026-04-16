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
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class SubscriptionTest : ClientTestFramework
    {
        private readonly string m_subscriptionTestXml = Path.Combine(
            Path.GetTempPath(),
            "SubscriptionTest.xml");

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            // the tests can be run against server specified in .runsettings
            SupportsExternalServerUrl = true;
            // create a new session for every test
            SingleSession = false;
            MaxChannelCount = 1000;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        /// <summary>
        /// Tear down the Server and the Client.
        /// </summary>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// Test setup.
        /// </summary>
        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        /// <summary>
        /// Test teardown.
        /// </summary>
        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        public async Task AddSubscriptionAsync()
        {
            using var defaultItemSource = new TestableSubscription(Telemetry);

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (_, _) => keepAlive++;

            int sessionConfigChanged = 0;
            Session.SessionConfigurationChanged += (sender, e) => sessionConfigChanged++;

            // add current time
            var list = new List<MonitoredItem>
            {
                new TestableMonitoredItem(defaultItemSource.DefaultItem)
                {
                    DisplayName = "ServerStatusCurrentTime",
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime
                }
            };
            list.ForEach(i =>
                i.Notification += (item, _) =>
                {
                    foreach (DataValue value in item.DequeueValues())
                    {
                        TestContext.Out.WriteLine(
                            "{0}: {1}, {2}, {3}",
                            item.DisplayName,
                            value.WrappedValue,
                            value.SourceTimestamp,
                            value.StatusCode);
                    }
                });

            using var subscription = new TestableSubscription(Session.DefaultSubscription);

            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine(
                "MaxNotificationsPerPublish: {0}",
                subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.StateChanged += (_, e) =>
                TestContext.Out.WriteLine(
                    "SubscriptionStateChangedEventArgs: Id: {0} Status: {1}",
                    subscription.Id,
                    e.Status);

            subscription.AddItem(list[0]);
            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(1));
            Assert.That(subscription.ChangesPending, Is.True);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await subscription.CreateAsync().ConfigureAwait(false));
            bool result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.That(result, Is.False);
            result = await Session.RemoveSubscriptionsAsync([subscription]).ConfigureAwait(false);
            Assert.That(result, Is.False);
            result = Session.AddSubscription(subscription);
            Assert.That(result, Is.True);
            result = Session.AddSubscription(subscription);
            Assert.That(result, Is.False);
            result = await Session.RemoveSubscriptionsAsync([subscription]).ConfigureAwait(false);
            Assert.That(result, Is.True);
            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.That(result, Is.False);
            result = Session.AddSubscription(subscription);
            Assert.That(result, Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);

            // add state
            var list2 = new List<MonitoredItem>
            {
                new TestableMonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState",
                    StartNodeId = VariableIds.Server_ServerStatus_State
                }
            };

            IList<NodeId> simulatedNodes = GetTestSetSimulation(Session.NamespaceUris);
            list2.AddRange(CreateMonitoredItemTestSet(subscription, simulatedNodes));
            list2.ForEach(i =>
                i.Notification += (item, _) =>
                {
                    foreach (DataValue value in item.DequeueValues())
                    {
                        TestContext.Out.WriteLine(
                            "{0}: {1}, {2}, {3}",
                            item.DisplayName,
                            value.WrappedValue,
                            value.SourceTimestamp,
                            value.StatusCode);
                    }
                });
            subscription.AddItems(list2);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);
            Assert.That(subscription.PublishingEnabled, Is.False);
            await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            Assert.That(subscription.PublishingEnabled, Is.True);
            Assert.That(subscription.PublishingStopped, Is.False);

            subscription.Priority = 200;
            await subscription.ModifyAsync().ConfigureAwait(false);

            // save with custom Subscription state subclass information
            Session.Save(m_subscriptionTestXml);

            await Task.Delay(5000).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            ServiceResultException sre = Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await subscription.RepublishAsync(subscription.SequenceNumber + 100)
                    .ConfigureAwait(false));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadMessageNotAvailable),
                $"Expected BadMessageNotAvailable, but received {sre.Message}");

            // verify that reconnect created subclassed version of subscription and monitored item
            foreach (Subscription s in Session.Subscriptions)
            {
                Assert.That(s.GetType(), Is.EqualTo(typeof(TestableSubscription)));
                foreach (MonitoredItem m in s.MonitoredItems)
                {
                    Assert.That(m.GetType(), Is.EqualTo(typeof(TestableMonitoredItem)));
                }
            }

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            subscription.RemoveItem(list2[0]);

            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.That(result, Is.True);
        }

        [Test]
        [Order(200)]
        public async Task LoadSubscriptionAsync()
        {
            if (!File.Exists(m_subscriptionTestXml))
            {
                Assert
                    .Ignore($"Save file {m_subscriptionTestXml} does not exist yet");
            }

            // load
            IEnumerable<Subscription> subscriptions = Session.Load(
                m_subscriptionTestXml,
                false);
            Assert.That(subscriptions, Is.Not.Null);
            Assert.IsNotEmpty(subscriptions);

            int valueChanges = 0;

            foreach (Subscription subscription in subscriptions)
            {
                var list = subscription.MonitoredItems.ToList();
                list.ForEach(i =>
                    i.Notification += (item, _) =>
                    {
                        foreach (DataValue value in item.DequeueValues())
                        {
                            valueChanges++;
                            TestContext.Out.WriteLine(
                                "{0}: {1}, {2}, {3}",
                                item.DisplayName,
                                value.WrappedValue,
                                value.SourceTimestamp,
                                value.StatusCode);
                        }
                    });

                Session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
            }

            await Task.Delay(5000).ConfigureAwait(false);

            TestContext.Out.WriteLine("{0} value changes.", valueChanges);

            Assert.That(valueChanges, Is.GreaterThanOrEqualTo(10));

            foreach (Subscription subscription in Session.Subscriptions)
            {
                OutputSubscriptionInfo(TestContext.Out, subscription);
            }

            bool result = await Session.RemoveSubscriptionsAsync(subscriptions)
                .ConfigureAwait(false);
            Assert.That(result, Is.True);
        }

        [Theory]
        [Order(300)]
        [CancelAfter(30_000)]
        /// <summary>
        /// This test doesn't deterministically prove sequential publishing,
        /// but rather relies on a subscription not being able to handle the message load.
        /// This test should be re-implemented with a Session that deterministically
        /// provides the wrong order of messages to Subscription.
        ///</summary>
        public async Task SequentialPublishingSubscriptionAsync(bool enabled)
        {
            var subscriptionList = new List<Subscription>();
            var subscriptionIds = new List<uint>();
            using var sequenceBroken = new AutoResetEvent(false);
            long numOfNotifications = 0L;
            const int testWaitTime = 10000;
            const int monitoredItemsPerSubscription = 500;
            const int subscriptions = 10;

            Session.MinPublishRequestCount = 3;

            // multiple Subscriptions to enforce multiple queued publish requests
            for (int i = 0; i < subscriptions; i++)
            {
                var s = new TestableSubscription(Session.DefaultSubscription)
                {
                    SequentialPublishing = enabled,
                    KeepAliveCount = 10,
                    PublishingInterval = 100,
                    DisableMonitoredItemCache = true,
                    PublishingEnabled = false,
                    MaxMessageCount = 1
                };
                subscriptionList.Add(s);
            }

            Subscription subscription = subscriptionList[0];

            // Create monitored items on the server
            // and track the last reported sequence number
            var testSet = new List<NodeId>();
            while (testSet.Count < monitoredItemsPerSubscription)
            {
                testSet.AddRange(GetTestSetFullSimulation(Session.NamespaceUris));
            }
            List<MonitoredItem> monitoredItemsList = testSet.ConvertAll(nodeId => new MonitoredItem(
                subscription.DefaultItem)
            {
                StartNodeId = nodeId,
                SamplingInterval = 0
            });

            subscription.AddItems(monitoredItemsList);

            foreach (Subscription s in subscriptionList)
            {
                bool boolResult = Session.AddSubscription(s);
                Assert.That(boolResult, Is.True);
                await s.CreateAsync().ConfigureAwait(false);
                int publishInterval = (int)s.CurrentPublishingInterval;
                TestContext.Out.WriteLine($"CurrentPublishingInterval: {publishInterval}");
                subscriptionIds.Add(s.Id);
            }

            // populate sequence number dictionary
            var dictionary = new ConcurrentDictionary<uint, uint>();
            foreach (Subscription item in subscriptionList)
            {
                dictionary.TryAdd(item.Id, 0);
            }

            // track the last reported sequence number
            subscription.FastDataChangeCallback = (s, notification, __) =>
            {
                Interlocked.Increment(ref numOfNotifications);
                if (dictionary[s.Id] > notification.SequenceNumber)
                {
                    TestContext.Out.WriteLine(
                        "Out of order encountered Id: {0}, {1} > {2}",
                        s.Id,
                        dictionary[s.Id],
                        notification.SequenceNumber);
                    sequenceBroken.Set();
                    return;
                }
                dictionary[s.Id] = notification.SequenceNumber;
            };

            var stopwatch = Stopwatch.StartNew();

            // start
            await Session.SetPublishingModeAsync(
                null,
                true,
                subscriptionIds,
                default).ConfigureAwait(false);

            // Wait for out-of-order to occur
            bool failed = sequenceBroken.WaitOne(testWaitTime);
            if (failed)
            {
                TestContext.Out.WriteLine("Error detected, terminating after 1 second");
                Thread.Sleep(1000);
            }

            // stop
            await Session.SetPublishingModeAsync(
                null,
                false,
                subscriptionIds,
                default).ConfigureAwait(false);

            //Log information
            double elapsed = stopwatch.Elapsed.TotalMilliseconds / 1000.0;
            TestContext.Out.WriteLine($"Ran for: {elapsed:N} seconds");
            long totalNotifications = Interlocked.Read(ref numOfNotifications);
            double notificationRate = totalNotifications / elapsed;
            int outstandingMessageWorkers = subscription.OutstandingMessageWorkers;
            TestContext.Out.WriteLine(
                $"Id: {subscription.Id} Outstanding workers: {outstandingMessageWorkers}");

            // clean up before validating conditions
            foreach (Subscription s in subscriptionList)
            {
                bool result = await Session.RemoveSubscriptionAsync(s).ConfigureAwait(false);
                Assert.That(result, Is.True);
            }

            TestContext.Out.WriteLine($"Number of notifications: {totalNotifications:N0}");
            //How fast it processed notifications.
            TestContext.Out.WriteLine($"Notifications rate: {notificationRate:N} per second");
            //No notifications means nothing worked
            Assert.NotZero(totalNotifications);

            // The issue more unlikely seem to appear on .NET 6 in the given timeframe
            if (!enabled && !failed)
            {
                Assert
                    .Inconclusive("The test couldn't validate the issue on this platform");
            }

            // catch if expected/unexpected Out-of-sequence occurred
            Assert.That(!failed, Is.EqualTo(enabled));
        }

        [Test]
        [Order(400)]
        [CancelAfter(30_000)]
        public async Task PublishRequestCountAsync()
        {
            var subscriptionList = new List<Subscription>();
            long numOfNotifications = 0L;
            const int testWaitTime = 10000;
            const int monitoredItemsPerSubscription = 50;
            const int subscriptions = 50;
            const int maxServerPublishRequest = 20;

            for (int i = 0; i < subscriptions; i++)
            {
                var subscription = new TestableSubscription(Session.DefaultSubscription)
                {
                    PublishingInterval = 0,
                    DisableMonitoredItemCache = true,
                    PublishingEnabled = true,
                    FastDataChangeCallback = (_, notification, __) =>
                        Interlocked.Add(ref numOfNotifications, notification.MonitoredItems.Count)
                };

                subscriptionList.Add(subscription);
                var list = new List<MonitoredItem>();
                IList<NodeId> nodeSet = GetTestSetFullSimulation(Session.NamespaceUris);

                for (int ii = 0; ii < monitoredItemsPerSubscription; ii++)
                {
                    NodeId nextNode = nodeSet[ii % nodeSet.Count];
                    list.Add(
                        new TestableMonitoredItem(subscription.DefaultItem)
                        {
                            StartNodeId = nextNode,
                            SamplingInterval = 0
                        });
                }
                var dict = list.ToDictionary(item => item.ClientHandle, _ => DateTime.MinValue);

                subscription.AddItems(list);
                Assert.ThrowsAsync<ServiceResultException>(
                    () => subscription.CreateAsync());
                bool result = Session.AddSubscription(subscription);
                Assert.That(result, Is.True);
                await subscription.CreateAsync().ConfigureAwait(false);
                int publishInterval = (int)subscription.CurrentPublishingInterval;

                TestContext.Out.WriteLine(
                    $"Id: {subscription.Id} CurrentPublishingInterval: {publishInterval}");
            }

            var stopwatch = Stopwatch.StartNew();

            await Task.Delay(1000).ConfigureAwait(false);

            // verify that number of active publishrequests is never exceeded
            while (stopwatch.ElapsedMilliseconds < testWaitTime)
            {
                // use the sample server default for max publish request count
                Assert.That(
                    Math.Max(maxServerPublishRequest, subscriptions),
                    Is.GreaterThanOrEqualTo(Session.GoodPublishRequestCount),
                    "No. of Good Publish Requests shall be at max count of subscriptions");
                await Task.Delay(100).ConfigureAwait(false);
            }

            foreach (Subscription subscription in subscriptionList)
            {
                bool result = await Session.RemoveSubscriptionAsync(subscription)
                    .ConfigureAwait(false);
                Assert.That(result, Is.True);
            }
        }

        [Test]
        [Order(1000)]
        public async Task FastKeepAliveCallbackAsync()
        {
            // add current time
            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 1,
                PublishingInterval = 250
            };
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine(
                "MaxNotificationsPerPublish: {0}",
                subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            // add static nodes
            var list = new List<MonitoredItem>
            {
                new TestableMonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = "ServerStatusState",
                    StartNodeId = VariableIds.Server_ServerStatus_State
                }
            };

            IList<NodeId> staticNodes = GetTestSetStatic(Session.NamespaceUris);
            list.AddRange(CreateMonitoredItemTestSet(subscription, staticNodes));
            list.ForEach(i =>
                i.Notification += (item, _) =>
                {
                    foreach (DataValue value in item.DequeueValues())
                    {
                        TestContext.Out.WriteLine(
                            "{0}: {1}, {2}, {3}",
                            item.DisplayName,
                            value.WrappedValue,
                            value.SourceTimestamp,
                            value.StatusCode);
                    }
                });
            subscription.AddItems(list);

            int numOfKeepAliveNotifications = 0;
            subscription.FastKeepAliveCallback = (_, notification) =>
            {
                int n = Interlocked.Increment(ref numOfKeepAliveNotifications);
                TestContext.Out.WriteLine(
                    "KeepAliveCallback {0} next sequenceNumber {1} PublishTime {2}",
                    n,
                    notification.SequenceNumber,
                    notification.PublishTime);
            };

            int numOfDataChangeNotifications = 0;
            subscription.FastDataChangeCallback = (_, notification, __) =>
            {
                int n = Interlocked.Increment(ref numOfDataChangeNotifications);
                TestContext.Out.WriteLine(
                    "DataChangeCallback {0} sequenceNumber {1} PublishTime {2} Count {3}",
                    n,
                    notification.SequenceNumber,
                    notification.PublishTime,
                    notification.MonitoredItems.Count);
            };

            bool result = Session.AddSubscription(subscription);
            Assert.That(result, Is.True);

            await subscription.CreateAsync().ConfigureAwait(false);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);
            Assert.That(subscription.PublishingEnabled, Is.False);
            await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            Assert.That(subscription.PublishingEnabled, Is.True);
            Assert.That(subscription.PublishingStopped, Is.False);

            subscription.Priority = 55;
            await subscription.ModifyAsync().ConfigureAwait(false);

            TestContext.Out.WriteLine("Waiting for keep alive");

            const int delay = 2000;
            await Task.Delay(delay).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            // expect at least half number of keep alive notifications
            Assert.That(
                numOfKeepAliveNotifications,
                Is.GreaterThan(delay / subscription.PublishingInterval / 2));
            Assert.That(numOfDataChangeNotifications, Is.EqualTo(1));

            TestContext.Out.WriteLine("Call ResendData.");
            bool resendData = await subscription.ResendDataAsync().ConfigureAwait(false);
            Assert.That(resendData, Is.True);

            await Task.Delay(delay).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            Assert.That(numOfDataChangeNotifications, Is.EqualTo(2));

            TestContext.Out.WriteLine("Call ConditionRefresh.");
            bool conditionRefresh =
                await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            Assert.That(conditionRefresh, Is.True);

            ServiceResultException sre =
                Assert.ThrowsAsync<ServiceResultException>(() =>
                    subscription.RepublishAsync(subscription.SequenceNumber + 100));
            Assert.That(
                sre.StatusCode,
                Is.EqualTo(StatusCodes.BadMessageNotAvailable));

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            result = await Session.RemoveSubscriptionAsync(
                subscription).ConfigureAwait(false);
            Assert.That(result, Is.True);
        }

        [Test]
        [Order(900)]
        public async Task SetTriggeringTrackingAsync()
        {
            // Create a subscription
            using var subscription = new Subscription(Session.DefaultSubscription)
            {
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                Priority = 100
            };

            Session.AddSubscription(subscription);
            await subscription.CreateAsync(CancellationToken.None).ConfigureAwait(false);
            Assert.That(subscription.Created, Is.True);

            // Create monitored items
            var triggeringItem = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 0,
                QueueSize = 0,
                DiscardOldest = true
            };

            var triggeredItem1 = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_State,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = 0,
                QueueSize = 0,
                DiscardOldest = true
            };

            var triggeredItem2 = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_BuildInfo,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Sampling,
                SamplingInterval = 0,
                QueueSize = 0,
                DiscardOldest = true
            };

            subscription.AddItem(triggeringItem);
            subscription.AddItem(triggeredItem1);
            subscription.AddItem(triggeredItem2);

            // Create the items
            await subscription.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(triggeringItem.Created, Is.True);
            Assert.That(triggeredItem1.Created, Is.True);
            Assert.That(triggeredItem2.Created, Is.True);

            // Set up triggering relationship using the new method
            var linksToAdd = new List<MonitoredItem> { triggeredItem1, triggeredItem2 };
            SetTriggeringResponse response = await subscription.SetTriggeringAsync(
                triggeringItem,
                linksToAdd,
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);

            // Verify the triggering relationships are tracked
            Assert.That(triggeringItem.TriggeredItems.IsNull, Is.False);
            Assert.That(triggeringItem.TriggeredItems.Count, Is.EqualTo(2));
            Assert.That(triggeringItem.TriggeredItems.ToList(), Does.Contain(triggeredItem1.ClientHandle));
            Assert.That(triggeringItem.TriggeredItems.ToList(), Does.Contain(triggeredItem2.ClientHandle));

            Assert.That(triggeredItem1.TriggeringItemId, Is.EqualTo(triggeringItem.Status.Id));
            Assert.That(triggeredItem2.TriggeringItemId, Is.EqualTo(triggeringItem.Status.Id));

            // Snapshot the subscription state
            subscription.Snapshot(out SubscriptionState state);

            // Verify that the triggering relationships are persisted
            MonitoredItemState triggeringItemState = state.MonitoredItems
                .FirstOrDefault(m => m.ClientId == triggeringItem.ClientHandle);
            Assert.That(triggeringItemState, Is.Not.Null);
            Assert.That(triggeringItemState.TriggeredItems.IsNull, Is.False);
            Assert.That(triggeringItemState.TriggeredItems.Count, Is.EqualTo(2));

            // Clean up
            await subscription.DeleteAsync(true, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Test that concurrent calls to CreateItemsAsync do not create duplicate monitored items.
        /// This test verifies the fix for the race condition where multiple threads calling
        /// CreateItemsAsync could include the same items in their create requests.
        /// </summary>
        [Test]
        [Order(1100)]
        public async Task ConcurrentCreateItemsNoDuplicatesAsync()
        {
            using var subscription = new TestableSubscription(Session.DefaultSubscription);
            Session.AddSubscription(subscription);
            await subscription.CreateAsync().ConfigureAwait(false);

            // Create multiple monitored items
            var items = new List<MonitoredItem>();
            for (int i = 0; i < 10; i++)
            {
                items.Add(new TestableMonitoredItem(subscription.DefaultItem)
                {
                    DisplayName = $"Item{i}",
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value
                });
            }

            subscription.AddItems(items);
            Assert.That(subscription.MonitoredItemCount, Is.EqualTo(10));

            // Simulate concurrent CreateItemsAsync calls
            // Use 3 concurrent tasks to ensure at least 2 will race with each other
            const int concurrentTasks = 3;
            var tasks = new List<Task<ArrayOf<MonitoredItem>>>();
            for (int i = 0; i < concurrentTasks; i++)
            {
                tasks.Add(Task.Run(() =>
                    subscription.CreateItemsAsync(CancellationToken.None)));
            }

            ArrayOf<MonitoredItem>[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Verify that all items were created exactly once
            int totalCreated = 0;
            foreach (MonitoredItem item in items)
            {
                if (item.Status.Created)
                {
                    totalCreated++;
                    Assert.That(item.Status.Id, Is.GreaterThan(0u),
                        $"Item {item.DisplayName} should have a server-assigned ID");
                }
            }

            Assert.That(totalCreated, Is.EqualTo(10),
                "All 10 items should be created exactly once");

            // Verify that each result list contains only the items that were actually created
            // by that specific call (should be empty for concurrent calls after the first)
            int nonEmptyResults = 0;
            foreach (ArrayOf<MonitoredItem> result in results)
            {
                if (result.Count > 0)
                {
                    nonEmptyResults++;
                }
            }

            Assert.That(nonEmptyResults, Is.LessThanOrEqualTo(1),
                "Only one CreateItemsAsync call should have created items");

            // Clean up
            await subscription.DeleteAsync(true, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
