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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


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
            MaxChannelCount = 1000;
            return base.OneTimeSetUpAsync(writer: null, securityNone: true);
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
            var subscription = new TestableSubscription();

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (ISession sender, KeepAliveEventArgs e) => { keepAlive++; };

            // add current time
            var list = new List<MonitoredItem> {
                new TestableMonitoredItem(subscription.DefaultItem)
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

            subscription = new TestableSubscription(Session.DefaultSubscription);
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.StateChanged += (Subscription sender, SubscriptionStateChangedEventArgs e) => {
                TestContext.Out.WriteLine("SubscriptionStateChangedEventArgs: Id: {0} Status: {1}", subscription.Id, e.Status);
            };

            subscription.AddItem(list.First());
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            Assert.Throws<ServiceResultException>(() => subscription.Create());
            bool result = Session.AddSubscription(subscription);
            Assert.True(result);
            subscription.Create();

            // add state
            var list2 = new List<MonitoredItem> {
                new TestableMonitoredItem(subscription.DefaultItem)
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

            // save with custom Subscription subclass information
            Session.Save(m_subscriptionTestXml, new[] { typeof(TestableSubscription) });

            Thread.Sleep(5000);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            subscription.ConditionRefresh();
            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber + 100));
            Assert.AreEqual((StatusCode)StatusCodes.BadMessageNotAvailable, (StatusCode)sre.StatusCode);

            // verify that reconnect created subclassed version of subscription and monitored item
            foreach (var s in Session.Subscriptions)
            {
                Assert.AreEqual(typeof(TestableSubscription), s.GetType());
                foreach (var m in s.MonitoredItems)
                {
                    Assert.AreEqual(typeof(TestableMonitoredItem), m.GetType());
                }
            }

            subscription.RemoveItems(list);
            subscription.ApplyChanges();

            subscription.RemoveItem(list2.First());

            result = Session.RemoveSubscription(subscription);
            Assert.True(result);
        }

        [Test, Order(110)]
        public async Task AddSubscriptionAsync()
        {
            var subscription = new TestableSubscription();

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (ISession sender, KeepAliveEventArgs e) => { keepAlive++; };

            int sessionConfigChanged = 0;
            Session.SessionConfigurationChanged += (object sender, EventArgs e) => { sessionConfigChanged++; };

            // add current time
            var list = new List<MonitoredItem> {
                new TestableMonitoredItem(subscription.DefaultItem)
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

            subscription = new TestableSubscription(Session.DefaultSubscription);

            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            subscription.StateChanged += (Subscription sender, SubscriptionStateChangedEventArgs e) => {
                TestContext.Out.WriteLine("SubscriptionStateChangedEventArgs: Id: {0} Status: {1}", subscription.Id, e.Status);
            };

            subscription.AddItem(list.First());
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            Assert.ThrowsAsync<ServiceResultException>(async () => await subscription.CreateAsync().ConfigureAwait(false));
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
                new TestableMonitoredItem(subscription.DefaultItem)
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

            // save with custom Subscription subclass information
            Session.Save(m_subscriptionTestXml, new[] { typeof(TestableSubscription) });

            await Task.Delay(5000).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            var sre = Assert.ThrowsAsync<ServiceResultException>(async () => await subscription.RepublishAsync(subscription.SequenceNumber + 100).ConfigureAwait(false));
            Assert.AreEqual((StatusCode)StatusCodes.BadMessageNotAvailable, (StatusCode)sre.StatusCode, $"Expected BadMessageNotAvailable, but received {sre.Message}");

            // verify that reconnect created subclassed version of subscription and monitored item
            foreach (var s in Session.Subscriptions)
            {
                Assert.AreEqual(typeof(TestableSubscription), s.GetType());
                foreach (var m in s.MonitoredItems)
                {
                    Assert.AreEqual(typeof(TestableMonitoredItem), m.GetType());
                }
            }

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            subscription.RemoveItem(list2.First());

            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.True(result);
        }

        [Test, Order(200)]
        public async Task LoadSubscriptionAsync()
        {
            if (!File.Exists(m_subscriptionTestXml)) Assert.Ignore($"Save file {m_subscriptionTestXml} does not exist yet");

            // load
            var subscriptions = Session.Load(m_subscriptionTestXml, false, new[] { typeof(TestableSubscription) });
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
        [CancelAfter(30_000)]
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
                var s = new TestableSubscription(Session.DefaultSubscription) {
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

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets.
        /// Use only synchronous methods.
        /// </summary>
        [Test, Combinatorial, Order(350), Explicit]
        public Task ReconnectWithSavedSessionSecretsSync(
            [Values(SecurityPolicies.None, SecurityPolicies.Basic256Sha256)] string securityPolicy,
            [Values(true, false)] bool anonymous,
            [Values(true, false)] bool sequentialPublishing,
            [Values(true, false)] bool sendInitialValues)
            => ReconnectWithSavedSessionSecretsAsync(securityPolicy, anonymous, sequentialPublishing, sendInitialValues, false);

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets.
        /// Use only asnc methods.
        /// </summary>
        [Test, Combinatorial, Order(351)]
        public Task ReconnectWithSavedSessionSecretsOnlyAsync(
            [Values(SecurityPolicies.None, SecurityPolicies.Basic256Sha256)] string securityPolicy,
            [Values(true, false)] bool anonymous,
            [Values(true, false)] bool sequentialPublishing,
            [Values(true, false)] bool sendInitialValues)
            => ReconnectWithSavedSessionSecretsAsync(securityPolicy, anonymous, sequentialPublishing, sendInitialValues, true);

        public async Task ReconnectWithSavedSessionSecretsAsync(string securityPolicy, bool anonymous, bool sequentialPublishing, bool sendInitialValues, bool asyncTest)
        {
            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            ServiceResultException sre;

            IUserIdentity userIdentity = anonymous ? new UserIdentity() : new UserIdentity("user1", "password");

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture.GetEndpointAsync(ServerUrl, securityPolicy, Endpoints).ConfigureAwait(false);
            Assert.NotNull(endpoint);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(userIdentity.TokenType, userIdentity.IssuedTokenType);
            if (identityPolicy == null)
            {
                Assert.Ignore($"No UserTokenPolicy found for {userIdentity.TokenType} / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity).ConfigureAwait(false);
            Assert.NotNull(session1);
            var sessionId1 = session1.SessionId;

            int session1ConfigChanged = 0;
            session1.SessionConfigurationChanged += (object sender, EventArgs e) => { session1ConfigChanged++; };

            ServerStatusDataType value1 = (ServerStatusDataType)session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
            Assert.NotNull(value1);

            var originSubscriptions = new SubscriptionCollection(kTestSubscriptions);
            var originSubscriptionCounters = new int[kTestSubscriptions];
            var originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var targetSubscriptionCounters = new int[kTestSubscriptions];
            var targetSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var subscriptionTemplate = new TestableSubscription(session1.DefaultSubscription) {
                PublishingInterval = 1_000,
                KeepAliveCount = 5,
                PublishingEnabled = true,
                RepublishAfterTransfer = true,
                SequentialPublishing = sequentialPublishing,
            };

            CreateSubscriptions(session1, subscriptionTemplate,
                originSubscriptions, originSubscriptionCounters, originSubscriptionFastDataCounters,
                kTestSubscriptions, kQueueSize);

            // wait 
            await Task.Delay(kDelay).ConfigureAwait(false);

            // save the session configuration
            var configStream = new MemoryStream();
            session1.SaveSessionConfiguration(configStream);

            var configStreamArray = configStream.ToArray();
            TestContext.Out.WriteLine($"SessionSecrets: {configStream.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(configStreamArray));

            var subscriptionStream = new MemoryStream();
            session1.Save(subscriptionStream, session1.Subscriptions, new[] { typeof(TestableSubscription) });

            var subscriptionStreamArray = subscriptionStream.ToArray();
            TestContext.Out.WriteLine($"Subscriptions: {subscriptionStreamArray.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(subscriptionStreamArray));

            // read the session configuration
            var loadConfigurationStream = new MemoryStream(configStreamArray);
            var sessionConfiguration = SessionConfiguration.Create(loadConfigurationStream);

            // create the inactive channel
            ITransportChannel channel2 = await ClientFixture.CreateChannelAsync(sessionConfiguration.ConfiguredEndpoint, false).ConfigureAwait(false);
            Assert.NotNull(channel2);

            // prepare the inactive session with the new channel
            ISession session2 = ClientFixture.CreateSession(channel2, sessionConfiguration.ConfiguredEndpoint);

            int session2ConfigChanged = 0;
            session2.SessionConfigurationChanged += (object sender, EventArgs e) => { session2ConfigChanged++; };

            // apply the saved session configuration
            bool success = session2.ApplySessionConfiguration(sessionConfiguration);

            // restore the subscriptions
            var loadSubscriptionStream = new MemoryStream(subscriptionStreamArray);
            var restoredSubscriptions = new SubscriptionCollection(session2.Load(loadSubscriptionStream, true, new[] { typeof(TestableSubscription) }));

            // hook notifications for log output
            int ii = 0;
            foreach (var subscription in restoredSubscriptions)
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

            // hook callback to renew the user identity
            session2.RenewUserIdentity += (session, identity) => {
                return userIdentity;
            };

            // activate the session from saved session secrets on the new channel
            if (asyncTest)
            {
                await session2.ReconnectAsync(channel2).ConfigureAwait(false);
            }
            else
            {
                session2.Reconnect(channel2);
            }

            // reactivate restored subscriptions
            if (asyncTest)
            {
                bool reactivateResult = await session2.ReactivateSubscriptionsAsync(restoredSubscriptions, sendInitialValues).ConfigureAwait(false);
                Assert.IsTrue(reactivateResult);
            }
            else
            {
                bool reactivateResult = session2.ReactivateSubscriptions(restoredSubscriptions, sendInitialValues);
                Assert.IsTrue(reactivateResult);
            }

            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            try
            {
                Assert.AreEqual(sessionId1, session2.SessionId);

                if (asyncTest)
                {
                    DataValue value2 = await session2.ReadValueAsync(VariableIds.Server_ServerStatus).ConfigureAwait(false);
                    Assert.NotNull(value2);
                }
                else
                {
                    ServerStatusDataType value2 = (ServerStatusDataType)session2.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
                    Assert.NotNull(value2);
                }

                for (ii = 0; ii < kTestSubscriptions; ii++)
                {
                    var monitoredItemCount = restoredSubscriptions[ii].MonitoredItemCount;
                    string errorText = $"Error in test subscription {ii}";

                    // the static subscription doesn't resend data until there is a data change
                    if (ii == 0 && !sendInitialValues)
                    {
                        Assert.AreEqual(0, targetSubscriptionCounters[ii], errorText);
                        Assert.AreEqual(0, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                    else if (ii == 0)
                    {
                        Assert.AreEqual(monitoredItemCount, targetSubscriptionCounters[ii], errorText);
                        Assert.AreEqual(1, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                    else
                    {
                        Assert.LessOrEqual(monitoredItemCount, targetSubscriptionCounters[ii], errorText);
                        Assert.LessOrEqual(1, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                }

                await Task.Delay(kDelay).ConfigureAwait(false);

                // verify that reconnect created subclassed version of subscription and monitored item
                foreach (var s in session2.Subscriptions)
                {
                    Assert.AreEqual(typeof(TestableSubscription), s.GetType());
                    foreach (var m in s.MonitoredItems)
                    {
                        Assert.AreEqual(typeof(TestableMonitoredItem), m.GetType());
                    }
                }

                // cannot read using a closed channel, validate the status code
                if (endpoint.EndpointUrl.ToString().StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    sre = Assert.Throws<ServiceResultException>(() => session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType)));
                    Assert.AreEqual((StatusCode)StatusCodes.BadSecureChannelIdInvalid, (StatusCode)sre.StatusCode, sre.Message);
                }
                else
                {
                    var result = session1.ReadValue(VariableIds.Server_ServerStatus, typeof(ServerStatusDataType));
                    Assert.NotNull(result);
                }
            }
            finally
            {
                session1.DeleteSubscriptionsOnClose = true;
                session2.DeleteSubscriptionsOnClose = true;
                if (asyncTest)
                {
                    await session1.CloseAsync(1000, true).ConfigureAwait(false);
                    await session2.CloseAsync(1000, true).ConfigureAwait(false);
                }
                else
                {
                    session1.Close(1000, true);
                    session2.Close(1000, true);
                }
                Utils.SilentDispose(session1);
                Utils.SilentDispose(session2);
            }

            Assert.AreEqual(0, session1ConfigChanged);
            Assert.Less(0, session2ConfigChanged);
        }

        [Test, Order(400)]
        [CancelAfter(30_000)]
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
                var subscription = new TestableSubscription(Session.DefaultSubscription) {
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
                    list.Add(new TestableMonitoredItem(subscription.DefaultItem) {
                        StartNodeId = nextNode,
                        SamplingInterval = 0
                    });
                }
                var dict = list.ToDictionary(item => item.ClientHandle, _ => DateTime.MinValue);

                subscription.AddItems(list);
                Assert.Throws<ServiceResultException>(() => subscription.Create());
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
            /// just acknowledged.
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
        [Explicit]
        public Task TransferSubscriptionSync(TransferType transferType, bool sendInitialValues, bool sequentialPublishing)
            => InternalTransferSubscriptionAsync(transferType, sendInitialValues, sequentialPublishing, false);

        [Theory, Order(811)]
        public Task TransferSubscriptionOnlyAsync(TransferType transferType, bool sendInitialValues, bool sequentialPublishing)
            => InternalTransferSubscriptionAsync(transferType, sendInitialValues, sequentialPublishing, true);

        public async Task InternalTransferSubscriptionAsync(TransferType transferType, bool sendInitialValues, bool sequentialPublishing, bool asyncTransfer)
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
            var originSubscriptions = new SubscriptionCollection(kTestSubscriptions);
            var originSubscriptionCounters = new int[kTestSubscriptions];
            var originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var targetSubscriptionCounters = new int[kTestSubscriptions];
            var targetSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var originSubscriptionTransferred = new int[kTestSubscriptions];
            var subscriptionTemplate = new TestableSubscription(originSession.DefaultSubscription) {
                PublishingInterval = 1_000,
                LifetimeCount = 30,
                KeepAliveCount = 5,
                PublishingEnabled = true,
                RepublishAfterTransfer = transferType >= TransferType.DisconnectedRepublish,
                SequentialPublishing = sequentialPublishing,
            };

            CreateSubscriptions(originSession, subscriptionTemplate,
                originSubscriptions, originSubscriptionCounters, originSubscriptionFastDataCounters,
                kTestSubscriptions, kQueueSize);

            if (TransferType.KeepOpen == transferType)
            {
                foreach (var subscription in originSubscriptions)
                {
                    subscription.PublishStatusChanged += (s, e) => {
                        TestContext.Out.WriteLine($"PublishStatusChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
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
            var filePath = Path.GetTempFileName();

            // close session, do not delete subscription
            if (transferType != TransferType.KeepOpen)
            {
                originSession.DeleteSubscriptionsOnClose = false;

                // save with custom Subscription subclass information
                originSession.Save(filePath, new[] { typeof(TestableSubscription) });

                if (transferType == TransferType.CloseSession)
                {
                    // graceful close
                    var result = originSession.Close();
                    Assert.True(ServiceResult.IsGood(result));
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
                if (asyncTransfer)
                {
                    var result = await originSession.CloseAsync().ConfigureAwait(false);
                }
                else
                {
                    var result = originSession.Close();
                }
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
                transferSubscriptions.AddRange(targetSession.Load(filePath, true, new[] { typeof(TestableSubscription) }));

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

                transferSubscriptions.AddRange((SubscriptionCollection)originSubscriptions.Clone());
                int ii = 0;
                transferSubscriptions.ForEach(s => {
                    targetSession.AddSubscription(s);
                    s.Handle = ii++;
                    s.FastDataChangeCallback = (sub, n, _) => {
                        TestContext.Out.WriteLine($"FastDataChangeHandlerTarget: {sub.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        targetSubscriptionFastDataCounters[(int)s.Handle]++;
                    };
                    s.MonitoredItems.ToList().ForEach(i =>
                        i.Notification += (MonitoredItem item, MonitoredItemNotificationEventArgs e) => {
                            targetSubscriptionCounters[(int)s.Handle]++;
                            foreach (var value in item.DequeueValues())
                            {
                                TestContext.Out.WriteLine("Tra:{0}: {1:20}, {2}, {3}, {4}", s.Id, item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                            }
                        }
                    );
                    s.StateChanged += (su, e) => {
                        TestContext.Out.WriteLine($"StateChanged: {su.Session.SessionId}-{su.Id}-{e.Status}");
                    };
                    s.PublishStatusChanged += (su, e) => {
                        TestContext.Out.WriteLine($"PublishStatusChanged: {su.Session.SessionId}-{su.Id}-{e.Status}");
                    };
                });
            }

            // transfer restored subscriptions
            if (asyncTransfer)
            {
                var result = await targetSession.TransferSubscriptionsAsync(transferSubscriptions, sendInitialValues).ConfigureAwait(false);
                Assert.IsTrue(result);
            }
            else
            {
                var result = targetSession.TransferSubscriptions(transferSubscriptions, sendInitialValues);
                Assert.IsTrue(result);
            }

            // validate results
            for (int ii = 0; ii < transferSubscriptions.Count; ii++)
            {
                Assert.IsTrue(transferSubscriptions[ii].Created);
            }

            TestContext.Out.WriteLine("TargetSession is now SessionId={0}", targetSession.SessionId);

            // wait for some events
            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            if (TransferType.KeepOpen == transferType)
            {
                foreach (var subscription in originSubscriptions)
                {
                    // assert if originSubscriptionTransferred is incremented
                    Assert.AreEqual(1, originSubscriptionTransferred[(int)subscription.Handle]);
                }
            }

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
                var originExpectedCount = monitoredItemCount;
                var targetExpectedCount = sendInitialValues ? monitoredItemCount : 0;
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
            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            // validate expected counts
            for (int jj = 0; jj < kTestSubscriptions; jj++)
            {
                TestContext.Out.WriteLine("-- Subscription {0}: OriginCounts {1}, TargetCounts {2} ",
                    jj, originSubscriptionCounters[jj], targetSubscriptionCounters[jj]);
                TestContext.Out.WriteLine("-- Subscription {0}: OriginFastDataCounts {1}, TargetFastDataCounts {2} ",
                    jj, originSubscriptionFastDataCounters[jj], targetSubscriptionFastDataCounters[jj]);

                int[] testCounter = targetSubscriptionCounters;
                int[] testFastDataCounter = targetSubscriptionFastDataCounters;

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

            targetSession.DeleteSubscriptionsOnClose = true;

            if (asyncTransfer)
            {
                // close sessions
                var result = await targetSession.CloseAsync().ConfigureAwait(false);
                Assert.True(ServiceResult.IsGood(result));

                if (originSessionOpen)
                {
                    result = await originSession.CloseAsync().ConfigureAwait(false);
                    Assert.True(ServiceResult.IsGood(result));
                }
            }
            else
            {
                // close sessions
                var result = targetSession.Close();
                Assert.True(ServiceResult.IsGood(result));

                if (originSessionOpen)
                {
                    result = originSession.Close();
                    Assert.True(ServiceResult.IsGood(result));
                }
            }

            // cleanup
            File.Delete(filePath);
        }

        [Test, Order(1000)]
        public void FastKeepAliveCallback()
        {
            // add current time
            var subscription = new TestableSubscription(Session.DefaultSubscription) {
                KeepAliveCount = 1,
                PublishingInterval = 250,
            };
            TestContext.Out.WriteLine("MaxMessageCount: {0}", subscription.MaxMessageCount);
            TestContext.Out.WriteLine("MaxNotificationsPerPublish: {0}", subscription.MaxNotificationsPerPublish);
            TestContext.Out.WriteLine("MinLifetimeInterval: {0}", subscription.MinLifetimeInterval);

            // add static nodes
            var list = new List<MonitoredItem> {
                new TestableMonitoredItem(subscription.DefaultItem)
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

            var sre = Assert.Throws<ServiceResultException>(() => subscription.Republish(subscription.SequenceNumber + 100));
            Assert.AreEqual((StatusCode)StatusCodes.BadMessageNotAvailable, (StatusCode)sre.StatusCode);

            subscription.RemoveItems(list);
            subscription.ApplyChanges();

            result = Session.RemoveSubscription(subscription);
            Assert.True(result);
        }
        #endregion

        #region Private Methods
        private void CreateSubscriptions(
            ISession session,
            Subscription template,
            SubscriptionCollection originSubscriptions,
            int[] notificationCounters,
            int[] fastDataCounters,
            int subscriptionCount,
            uint queueSize)
        {
            for (int ii = 0; ii < subscriptionCount; ii++)
            {
                // create subscription with static monitored items
                var subscription = new TestableSubscription(template) {
                    PublishingEnabled = true,
                    Handle = ii,
                    FastDataChangeCallback = (s, n, _) => {
                        TestContext.Out.WriteLine($"FastDataChangeHandlerOrigin: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        fastDataCounters[(int)s.Handle]++;
                    },
                };

                subscription.StateChanged += (s, e) => {
                    TestContext.Out.WriteLine($"StateChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
                };

                subscription.PublishStatusChanged += (s, e) => {
                    TestContext.Out.WriteLine($"PublishStatusChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");
                };

                originSubscriptions.Add(subscription);
                session.AddSubscription(subscription);
                subscription.Create();

                // set defaults
                subscription.DefaultItem.DiscardOldest = true;
                subscription.DefaultItem.QueueSize = (ii == 0) ? 0U : queueSize;
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
                    notificationCounters[(int)subscription.Handle]++;
                    foreach (var value in item.DequeueValues())
                    {
                        TestContext.Out.WriteLine("Org:{0}: {1:20}, {2}, {3}, {4}", subscription.Id, item.DisplayName, value.Value, value.SourceTimestamp, value.StatusCode);
                    }
                });
                subscription.AddItems(list);
                subscription.ApplyChanges();
            }
        }

        private List<MonitoredItem> CreateMonitoredItemTestSet(Subscription subscription, IList<NodeId> nodeIds)
        {
            var list = new List<MonitoredItem>();
            foreach (NodeId nodeId in nodeIds)
            {
                var item = new TestableMonitoredItem(subscription.DefaultItem) {
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
            // for testing do not ack any sequence numbers
            e.DeferredAcknowledgementsToSend.Clear();
            e.AcknowledgementsToSend.Clear();
        }
        #endregion
    }
}
