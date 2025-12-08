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
            var subscription = new TestableSubscription(Telemetry);

            // check keepAlive
            int keepAlive = 0;
            Session.KeepAlive += (_, _) => keepAlive++;

            int sessionConfigChanged = 0;
            Session.SessionConfigurationChanged += (sender, e) => sessionConfigChanged++;

            // add current time
            var list = new List<MonitoredItem>
            {
                new TestableMonitoredItem(subscription.DefaultItem)
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
                            value.Value,
                            value.SourceTimestamp,
                            value.StatusCode);
                    }
                });

            subscription = new TestableSubscription(Session.DefaultSubscription);

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
            Assert.AreEqual(1, subscription.MonitoredItemCount);
            Assert.True(subscription.ChangesPending);
            NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(async () =>
                await subscription.CreateAsync().ConfigureAwait(false));
            bool result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.False(result);
            result = await Session.RemoveSubscriptionsAsync([subscription]).ConfigureAwait(false);
            Assert.False(result);
            result = Session.AddSubscription(subscription);
            Assert.True(result);
            result = Session.AddSubscription(subscription);
            Assert.False(result);
            result = await Session.RemoveSubscriptionsAsync([subscription]).ConfigureAwait(false);
            Assert.True(result);
            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.False(result);
            result = Session.AddSubscription(subscription);
            Assert.True(result);
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
                            value.Value,
                            value.SourceTimestamp,
                            value.StatusCode);
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

            // save with custom Subscription state subclass information
            Session.Save(m_subscriptionTestXml);

            await Task.Delay(5000).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            ServiceResultException sre = NUnit.Framework.Assert
                .ThrowsAsync<ServiceResultException>(async () =>
                    await subscription.RepublishAsync(subscription.SequenceNumber + 100)
                    .ConfigureAwait(false));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadMessageNotAvailable,
                (StatusCode)sre.StatusCode,
                $"Expected BadMessageNotAvailable, but received {sre.Message}");

            // verify that reconnect created subclassed version of subscription and monitored item
            foreach (Subscription s in Session.Subscriptions)
            {
                Assert.AreEqual(typeof(TestableSubscription), s.GetType());
                foreach (MonitoredItem m in s.MonitoredItems)
                {
                    Assert.AreEqual(typeof(TestableMonitoredItem), m.GetType());
                }
            }

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            subscription.RemoveItem(list2[0]);

            result = await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false);
            Assert.True(result);
        }

        [Test]
        [Order(200)]
        public async Task LoadSubscriptionAsync()
        {
            if (!File.Exists(m_subscriptionTestXml))
            {
                NUnit.Framework.Assert
                    .Ignore($"Save file {m_subscriptionTestXml} does not exist yet");
            }

            // load
            IEnumerable<Subscription> subscriptions = Session.Load(
                m_subscriptionTestXml,
                false);
            Assert.NotNull(subscriptions);
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
                                value.Value,
                                value.SourceTimestamp,
                                value.StatusCode);
                        }
                    });

                Session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);
            }

            await Task.Delay(5000).ConfigureAwait(false);

            TestContext.Out.WriteLine("{0} value changes.", valueChanges);

            Assert.GreaterOrEqual(valueChanges, 10);

            foreach (Subscription subscription in Session.Subscriptions)
            {
                OutputSubscriptionInfo(TestContext.Out, subscription);
            }

            bool result = await Session.RemoveSubscriptionsAsync(subscriptions)
                .ConfigureAwait(false);
            Assert.True(result);
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
            var subscriptionIds = new UInt32Collection();
            var sequenceBroken = new AutoResetEvent(false);
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
                Assert.True(boolResult);
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
                NUnit.Framework.Assert
                    .Inconclusive("The test couldn't validate the issue on this platform");
            }

            // catch if expected/unexpected Out-of-sequence occurred
            Assert.AreEqual(enabled, !failed);
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets.
        /// Use only asnc methods. Test the ECC profiles.
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(351)]
        [Explicit]
        public Task ReconnectWithSavedSessionSecretsOnlyECCAsync(
            [Values(
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.ECC_nistP384,
                SecurityPolicies.ECC_brainpoolP256r1,
                SecurityPolicies.ECC_brainpoolP384r1
            )]
                string securityPolicy,
            [Values(true, false)] bool anonymous,
            [Values(true, false)] bool sequentialPublishing,
            [Values(true, false)] bool sendInitialValues)
        {
            return ReconnectWithSavedSessionSecretsAsync(
                securityPolicy,
                anonymous,
                sequentialPublishing,
                sendInitialValues);
        }

        /// <summary>
        /// Open a session on a channel, then reconnect (activate)
        /// the same session on a new channel with saved session secrets.
        /// Use only asnc methods.
        /// </summary>
        [Test]
        [Combinatorial]
        [Order(352)]
        public Task ReconnectWithSavedSessionSecretsOnlyAsync(
            [Values(SecurityPolicies.None,
                SecurityPolicies.ECC_nistP256,
                SecurityPolicies.Basic256Sha256)]
                string securityPolicy,
            [Values(true, false)] bool anonymous,
            [Values(true, false)] bool sequentialPublishing,
            [Values(true, false)] bool sendInitialValues)
        {
            return ReconnectWithSavedSessionSecretsAsync(
                securityPolicy,
                anonymous,
                sequentialPublishing,
                sendInitialValues);
        }

        public async Task ReconnectWithSavedSessionSecretsAsync(
            string securityPolicy,
            bool anonymous,
            bool sequentialPublishing,
            bool sendInitialValues)
        {
            const int kTestSubscriptions = 5;
            const int kDelay = 2_000;
            const int kQueueSize = 10;

            ServiceResultException sre;
            UserIdentity userIdentity = anonymous
                ? new UserIdentity()
                : new UserIdentity("user1", "password"u8);

            ClientFixture.SessionFactory = new TestableSessionFactory(Telemetry);

            // the first channel determines the endpoint
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, securityPolicy, Endpoints)
                .ConfigureAwait(false);
            Assert.NotNull(endpoint);

            UserTokenPolicy identityPolicy = endpoint.Description.FindUserTokenPolicy(
                userIdentity.TokenType,
                userIdentity.IssuedTokenType,
                endpoint.Description.SecurityPolicyUri);
            if (identityPolicy == null)
            {
                NUnit.Framework.Assert.Ignore(
                    $"No UserTokenPolicy found for {userIdentity.TokenType} / {userIdentity.IssuedTokenType}");
            }

            // the active channel
            ISession session1 = await ClientFixture.ConnectAsync(endpoint, userIdentity)
                .ConfigureAwait(false);
            Assert.NotNull(session1);
            NodeId sessionId1 = session1.SessionId;

            int session1ConfigChanged = 0;
            session1.SessionConfigurationChanged += (sender, e) => session1ConfigChanged++;

            ServerStatusDataType value1 = await session1.ReadValueAsync<ServerStatusDataType>(
                VariableIds.Server_ServerStatus).ConfigureAwait(false);
            Assert.NotNull(value1);

            var originSubscriptions = new SubscriptionCollection(kTestSubscriptions);
            int[] originSubscriptionCounters = new int[kTestSubscriptions];
            int[] originSubscriptionFastDataCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionCounters = new int[kTestSubscriptions];
            int[] targetSubscriptionFastDataCounters = new int[kTestSubscriptions];
            var subscriptionTemplate = new TestableSubscription(session1.DefaultSubscription)
            {
                PublishingInterval = 1_000,
                KeepAliveCount = 5,
                PublishingEnabled = true,
                RepublishAfterTransfer = true,
                SequentialPublishing = sequentialPublishing
            };

            await CreateSubscriptionsAsync(
                session1,
                subscriptionTemplate,
                originSubscriptions,
                originSubscriptionCounters,
                originSubscriptionFastDataCounters,
                kTestSubscriptions,
                kQueueSize).ConfigureAwait(false);

            // wait
            await Task.Delay(kDelay).ConfigureAwait(false);

            // save the session configuration
            var configStream = new MemoryStream();
            session1.SaveSessionConfiguration(configStream);

            byte[] configStreamArray = configStream.ToArray();
            TestContext.Out.WriteLine($"SessionSecrets: {configStream.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(configStreamArray));

            var subscriptionStream = new MemoryStream();
            session1.Save(
                subscriptionStream,
                session1.Subscriptions);

            byte[] subscriptionStreamArray = subscriptionStream.ToArray();
            TestContext.Out.WriteLine($"Subscriptions: {subscriptionStreamArray.Length} bytes");
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(subscriptionStreamArray));

            // read the session configuration
            var loadConfigurationStream = new MemoryStream(configStreamArray);
            var sessionConfiguration = SessionConfiguration.Create(
                loadConfigurationStream,
                Telemetry);

            // create the inactive channel
            ITransportChannel channel2 = await ClientFixture
                .CreateChannelAsync(sessionConfiguration.ConfiguredEndpoint, false)
                .ConfigureAwait(false);
            Assert.NotNull(channel2);

            // prepare the inactive session with the new channel
            ISession session2 = ClientFixture.CreateSession(
                channel2,
                sessionConfiguration.ConfiguredEndpoint);

            int session2ConfigChanged = 0;
            session2.SessionConfigurationChanged += (sender, e) => session2ConfigChanged++;

            // apply the saved session configuration
            bool success = session2.ApplySessionConfiguration(sessionConfiguration);

            // restore the subscriptions
            var loadSubscriptionStream = new MemoryStream(subscriptionStreamArray);
            var restoredSubscriptions = new SubscriptionCollection(
                session2.Load(loadSubscriptionStream, true, [typeof(SubscriptionState)]));

            // hook notifications for log output
            int ii = 0;
            foreach (Subscription subscription in restoredSubscriptions)
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
                                    value.Value,
                                    value.SourceTimestamp,
                                    value.StatusCode);
                            }
                        });
                ii++;
            }

            // hook callback to renew the user identity
            session2.RenewUserIdentity += (_, _) => userIdentity;

            // activate the session from saved session secrets on the new channel
            await session2.ReconnectAsync(channel2).ConfigureAwait(false);

            // reactivate restored subscriptions
            bool reactivateResult = await session2
                .ReactivateSubscriptionsAsync(restoredSubscriptions, sendInitialValues)
                .ConfigureAwait(false);
            Assert.IsTrue(reactivateResult);

            await Task.Delay(2 * kDelay).ConfigureAwait(false);

            try
            {
                Assert.AreEqual(sessionId1, session2.SessionId);

                DataValue value2 = await session2
                    .ReadValueAsync(VariableIds.Server_ServerStatus)
                    .ConfigureAwait(false);
                Assert.NotNull(value2);

                for (ii = 0; ii < kTestSubscriptions; ii++)
                {
                    uint monitoredItemCount = restoredSubscriptions[ii].MonitoredItemCount;
                    string errorText = $"Error in test subscription {ii}";

                    // the static subscription doesn't resend data until there is a data change
                    if (ii == 0 && !sendInitialValues)
                    {
                        Assert.AreEqual(0, targetSubscriptionCounters[ii], errorText);
                        Assert.AreEqual(0, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                    else if (ii == 0)
                    {
                        Assert.AreEqual(
                            monitoredItemCount,
                            targetSubscriptionCounters[ii],
                            errorText);
                        Assert.AreEqual(1, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                    else
                    {
                        Assert.LessOrEqual(
                            monitoredItemCount,
                            targetSubscriptionCounters[ii],
                            errorText);
                        Assert.LessOrEqual(1, targetSubscriptionFastDataCounters[ii], errorText);
                    }
                }

                await Task.Delay(kDelay).ConfigureAwait(false);

                // verify that reconnect created subclassed version of subscription and monitored item
                foreach (Subscription s in session2.Subscriptions)
                {
                    Assert.AreEqual(typeof(TestableSubscription), s.GetType());
                    foreach (MonitoredItem m in s.MonitoredItems)
                    {
                        Assert.AreEqual(typeof(TestableMonitoredItem), m.GetType());
                    }
                }

                // cannot read using a closed channel, validate the status code
                if (endpoint.EndpointUrl.ToString()
                    .StartsWith(Utils.UriSchemeOpcTcp, StringComparison.Ordinal))
                {
                    sre = NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                        session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus));
                    Assert.AreEqual(
                        (StatusCode)StatusCodes.BadSecureChannelIdInvalid,
                        (StatusCode)sre.StatusCode,
                        sre.Message);
                }
                else
                {
                    ServerStatusDataType result =
                        await session1.ReadValueAsync<ServerStatusDataType>(
                            VariableIds.Server_ServerStatus).ConfigureAwait(false);
                    Assert.NotNull(result);
                }
            }
            finally
            {
                session1.DeleteSubscriptionsOnClose = true;
                session2.DeleteSubscriptionsOnClose = true;
                await session1.CloseAsync(1000, true).ConfigureAwait(false);
                await session2.CloseAsync(1000, true).ConfigureAwait(false);
                Utils.SilentDispose(session1);
                Utils.SilentDispose(session2);
            }

            Assert.AreEqual(0, session1ConfigChanged);
            Assert.Less(0, session2ConfigChanged);
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
                NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(
                    () => subscription.CreateAsync());
                bool result = Session.AddSubscription(subscription);
                Assert.True(result);
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
                Assert.GreaterOrEqual(
                    Math.Max(maxServerPublishRequest, subscriptions),
                    Session.GoodPublishRequestCount,
                    "No. of Good Publish Requests shall be at max count of subscriptions");
                await Task.Delay(100).ConfigureAwait(false);
            }

            foreach (Subscription subscription in subscriptionList)
            {
                bool result = await Session.RemoveSubscriptionAsync(subscription)
                    .ConfigureAwait(false);
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

        [Theory]
        [Order(811)]
        public Task TransferSubscriptionOnlyAsync(
            TransferType transferType,
            bool sendInitialValues,
            bool sequentialPublishing)
        {
            return InternalTransferSubscriptionAsync(
                transferType,
                sendInitialValues,
                sequentialPublishing);
        }

        [Theory]
        [Order(812)]
        [Explicit]
        public async Task TransferSubscriptionOnlyDebugAsync(
            TransferType transferType,
            bool sendInitialValues,
            bool sequentialPublishing)
        {
            const int loopCount = 30;
            for (int i = 0; i < loopCount; i++)
            {
                await InternalTransferSubscriptionAsync(
                    transferType,
                    sendInitialValues,
                    sequentialPublishing).ConfigureAwait(false);

                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine($"Completed {i}th iteration.");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
            }
        }

        public async Task InternalTransferSubscriptionAsync(
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
                Utils.SilentDispose(originSession);
                Utils.SilentDispose(targetSession);
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
            var subscriptionTemplate = new TestableSubscription(originSession.DefaultSubscription)
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
                    Assert.True(ServiceResult.IsGood(close));
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
                                        value.Value,
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
                                        value.Value,
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
            Assert.IsTrue(result);

            // validate results
            for (int ii = 0; ii < transferSubscriptions.Count; ii++)
            {
                Assert.IsTrue(transferSubscriptions[ii].Created);
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
                    Assert.AreEqual(1, originSubscriptionTransferred[(int)subscription.Handle]);
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

            // close sessions
            StatusCode closeResult = await targetSession.CloseAsync().ConfigureAwait(false);
            Assert.True(ServiceResult.IsGood(closeResult));

            if (originSessionOpen)
            {
                closeResult = await originSession.CloseAsync().ConfigureAwait(false);
                Assert.True(ServiceResult.IsGood(closeResult));
            }

            // cleanup
            File.Delete(filePath);
            return targetSession;
        }

        [Test]
        [Order(1000)]
        public async Task FastKeepAliveCallbackAsync()
        {
            // add current time
            var subscription = new TestableSubscription(Session.DefaultSubscription)
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
                            value.Value,
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
            Assert.True(result);

            await subscription.CreateAsync().ConfigureAwait(false);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);
            Assert.False(subscription.PublishingEnabled);
            await subscription.SetPublishingModeAsync(true).ConfigureAwait(false);
            Assert.True(subscription.PublishingEnabled);
            Assert.False(subscription.PublishingStopped);

            subscription.Priority = 55;
            await subscription.ModifyAsync().ConfigureAwait(false);

            TestContext.Out.WriteLine("Waiting for keep alive");

            const int delay = 2000;
            await Task.Delay(delay).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            // expect at least half number of keep alive notifications
            Assert.Greater(
                numOfKeepAliveNotifications,
                delay / subscription.PublishingInterval / 2);
            Assert.AreEqual(1, numOfDataChangeNotifications);

            TestContext.Out.WriteLine("Call ResendData.");
            bool resendData = await subscription.ResendDataAsync().ConfigureAwait(false);
            Assert.True(resendData);

            await Task.Delay(delay).ConfigureAwait(false);
            OutputSubscriptionInfo(TestContext.Out, subscription);

            Assert.AreEqual(2, numOfDataChangeNotifications);

            TestContext.Out.WriteLine("Call ConditionRefresh.");
            bool conditionRefresh =
                await subscription.ConditionRefreshAsync().ConfigureAwait(false);
            Assert.True(conditionRefresh);

            ServiceResultException sre =
                NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                    subscription.RepublishAsync(subscription.SequenceNumber + 100));
            Assert.AreEqual(
                (StatusCode)StatusCodes.BadMessageNotAvailable,
                (StatusCode)sre.StatusCode);

            subscription.RemoveItems(list);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);

            result = await Session.RemoveSubscriptionAsync(
                subscription).ConfigureAwait(false);
            Assert.True(result);
        }

        private async Task CreateSubscriptionsAsync(
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
                var subscription = new TestableSubscription(template)
                {
                    PublishingEnabled = true,
                    Handle = ii,
                    FastDataChangeCallback = (s, n, _) =>
                    {
                        TestContext.Out.WriteLine(
                            $"FastDataChangeHandlerOrigin: {s.Id}-{n.SequenceNumber}-{n.MonitoredItems.Count}");
                        fastDataCounters[(int)s.Handle]++;
                    }
                };

                subscription.StateChanged += (s, e) =>
                    TestContext.Out
                        .WriteLine($"StateChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");

                subscription.PublishStatusChanged += (s, e) =>
                    TestContext.Out.WriteLine(
                        $"PublishStatusChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");

                originSubscriptions.Add(subscription);
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);

                // set defaults
                subscription.DefaultItem.DiscardOldest = true;
                subscription.DefaultItem.QueueSize = ii == 0 ? 0U : queueSize;
                subscription.DefaultItem.MonitoringMode = MonitoringMode.Reporting;

                // create test set
                NamespaceTable namespaceUris = Session.NamespaceUris;
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
                list.ForEach(i =>
                    i.Notification += (item, _) =>
                    {
                        notificationCounters[(int)subscription.Handle]++;
                        foreach (DataValue value in item.DequeueValues())
                        {
                            TestContext.Out.WriteLine(
                                "Org:{0}: {1:20}, {2}, {3}, {4}",
                                subscription.Id,
                                item.DisplayName,
                                value.Value,
                                value.SourceTimestamp,
                                value.StatusCode);
                        }
                    });
                subscription.AddItems(list);
                await subscription.ApplyChangesAsync().ConfigureAwait(false);
            }
        }

        private static List<MonitoredItem> CreateMonitoredItemTestSet(
            Subscription subscription,
            IList<NodeId> nodeIds)
        {
            var list = new List<MonitoredItem>();
            foreach (NodeId nodeId in nodeIds)
            {
                var item = new TestableMonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = nodeId
                };
                list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// Event handler to defer publish response sequence number acknowledge.
        /// </summary>
        private void DeferSubscriptionAcknowledge(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            // for testing do not ack any sequence numbers
            e.DeferredAcknowledgementsToSend.Clear();
            e.AcknowledgementsToSend.Clear();
        }

        [Test]
        [Order(900)]
        public async Task SetTriggeringTrackingAsync()
        {
            // Create a subscription
            var subscription = new Subscription(Session.DefaultSubscription)
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
                null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response, Is.Not.Null);

            // Verify the triggering relationships are tracked
            Assert.That(triggeringItem.TriggeredItems, Is.Not.Null);
            Assert.That(triggeringItem.TriggeredItems.Count, Is.EqualTo(2));
            Assert.That(triggeringItem.TriggeredItems, Does.Contain(triggeredItem1.ClientHandle));
            Assert.That(triggeringItem.TriggeredItems, Does.Contain(triggeredItem2.ClientHandle));

            Assert.That(triggeredItem1.TriggeringItemId, Is.EqualTo(triggeringItem.Status.Id));
            Assert.That(triggeredItem2.TriggeringItemId, Is.EqualTo(triggeringItem.Status.Id));

            // Snapshot the subscription state
            subscription.Snapshot(out SubscriptionState state);

            // Verify that the triggering relationships are persisted
            MonitoredItemState triggeringItemState = state.MonitoredItems
                .FirstOrDefault(m => m.ClientId == triggeringItem.ClientHandle);
            Assert.That(triggeringItemState, Is.Not.Null);
            Assert.That(triggeringItemState.TriggeredItems, Is.Not.Null);
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
        public async Task ConcurrentCreateItemsNoDuplicates()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription);
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
            var tasks = new List<Task<IList<MonitoredItem>>>();
            for (int i = 0; i < 3; i++)
            {
                tasks.Add(Task.Run(() =>
                    subscription.CreateItemsAsync(CancellationToken.None)));
            }

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            // Verify that all items were created exactly once
            int totalCreated = 0;
            foreach (var item in items)
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
            foreach (var result in results)
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
