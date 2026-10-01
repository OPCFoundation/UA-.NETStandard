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
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;
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
    public class DurableSubscriptionTest : ClientTestFramework
    {
        public readonly uint MillisecondsPerHour = 3600 * 1000;

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

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
            {
                // start Ref server
                ServerFixture = new ServerFixture<ReferenceServer>(
                    enableTracing,
                    disableActivityLogging)
                {
                    UriScheme = UriScheme,
                    SecurityNone = securityNone,
                    AutoAccept = true,
                    AllNodeManagers = true,
                    OperationLimits = true,
                    DurableSubscriptionsEnabled = true
                };
            }

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = ServerFixture
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.ServerConfiguration.MinSessionTimeout = 1000;
            ServerFixture.Config.ServerConfiguration.MinSubscriptionLifetime = 1500;
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies
                .Add(new UserTokenPolicy(UserTokenType.UserName));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.Certificate));
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies.Add(
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                });

            ReferenceServer = await ServerFixture.StartAsync()
                .ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
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
            return MySetUpAsync();
        }

        public async Task MySetUpAsync()
        {
            if (!SingleSession)
            {
                try
                {
                    ClientFixture.SessionTimeout = 10000;
                    Session = await ClientFixture
                        .ConnectAsync(
                            ServerUrl,
                            SecurityPolicies.Basic256Sha256,
                            null,
                            new UserIdentity("sysadmin", "demo"u8))
                        .ConfigureAwait(false);
                    Session.DeleteSubscriptionsOnClose = false;
                }
                catch (Exception e)
                {
                    NUnit.Framework.Assert.Ignore(
                        $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
                }
            }
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
        [TestCase(900, 100u, 100u, 10000u, 3600u, 83442u, TestName = "Test Lifetime Over Maximum")]
        [TestCase(900, 100u, 100u, 0u, 3600u, 83442u, TestName = "Test Lifetime Zero")]
        [TestCase(1200, 100u, 100u, 1u, 1u, 3000u, TestName = "Test Lifetime One")]
        [TestCase(
            60000,
            183u,
            61u,
            1u,
            1u,
            60u,
            TestName = "Test Lifetime Reduce Count",
            Description = "Expected MaxLifetimeCount matches what the demo server does"
        )]
        public async Task TestLifetimeAsync(
            int publishingInterval,
            uint keepAliveCount,
            uint lifetimeCount,
            uint requestedHours,
            uint expectedHours,
            uint expectedLifetime)
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                PublishingInterval = publishingInterval
            };

            Assert.True(Session.AddSubscription(subscription));
            await subscription.CreateAsync().ConfigureAwait(false);

            Dictionary<string, NodeId> desiredNodeIds =
                await GetDesiredNodeIdsAsync(subscription.Id).ConfigureAwait(false);

            (bool success, uint revisedLifetimeInHours) =
                await subscription.SetSubscriptionDurableAsync(requestedHours).ConfigureAwait(false);
            Assert.True(success);
            Assert.AreEqual(expectedHours, revisedLifetimeInHours);

            Dictionary<string, object> modifiedValues =
                await GetValuesAsync(desiredNodeIds).ConfigureAwait(false);

            var maxLifetimeCountValue = modifiedValues["MaxLifetimeCount"] as DataValue;
            Assert.IsNotNull(maxLifetimeCountValue);
            Assert.IsNotNull(maxLifetimeCountValue.Value);
            Assert.AreEqual(
                expectedLifetime,
                Convert.ToUInt32(maxLifetimeCountValue.Value, CultureInfo.InvariantCulture));

            Assert.True(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false));
        }

        [Test]
        [Order(110)]
        [TestCase(0u, 1u, 1u, false, TestName = "QueueSize 0")]
        [TestCase(101u, 101u, 102u, false, TestName = "QueueSize over standard subscripion limit")]
        [TestCase(9999u, 1000u, 1000u, false, TestName = "QueueSize over durable limit")]
        [TestCase(0u, 1000u, 1u, true, TestName = "QueueSize 0 Event MI")]
        [TestCase(
            1001u,
            1001u,
            1002u,
            true,
            TestName = "QueueSize over standard subscripion limit Event MI")]
        [TestCase(
            99999u,
            10000u,
            10000u,
            true,
            TestName = "QueueSize over durable limit, Event MI")]
        public async Task TestRevisedQueueSizeAsync(
            uint queueSize,
            uint expectedRevisedQueueSize,
            uint expectedModifiedQueueSize,
            bool useEventMI)
        {
            TestableSubscription subscription = await CreateDurableSubscriptionAsync()
                .ConfigureAwait(false);

            MonitoredItem mi;
            if (useEventMI)
            {
                mi = CreateEventMonitoredItem(queueSize);
            }
            else
            {
                mi = new MonitoredItem(Session.MessageContext.Telemetry)
                {
                    AttributeId = Attributes.Value,
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    MonitoringMode = MonitoringMode.Reporting,
                    Handle = 1,
                    SamplingInterval = 500,
                    Filter = null,
                    DiscardOldest = true,
                    QueueSize = queueSize
                };
            }

            subscription.AddItem(mi);

            IList<MonitoredItem> result = await subscription.CreateItemsAsync().ConfigureAwait(false);
            NUnit.Framework.Assert.That(ServiceResult.IsGood(result[0].Status.Error), Is.True);
            NUnit.Framework.Assert
                .That(result[0].Status.QueueSize, Is.EqualTo(expectedRevisedQueueSize));

            mi.QueueSize = queueSize + 1;

            IList<MonitoredItem> resultModify = await subscription.ModifyItemsAsync()
                .ConfigureAwait(false);
            NUnit.Framework.Assert
                .That(ServiceResult.IsGood(resultModify[0].Status.Error), Is.True);
            NUnit.Framework.Assert
                .That(resultModify[0].Status.QueueSize, Is.EqualTo(expectedModifiedQueueSize));

            (bool success, _, _) = await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.True(success);

            Assert.True(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false));
        }

        [Test]
        [Order(160)]
        public async Task SetSubscriptionDurableFailsWhenMIExistsAsync()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.True(Session.AddSubscription(subscription));
            await subscription.CreateAsync().ConfigureAwait(false);

            uint id = subscription.Id;

            var mi = new MonitoredItem(Session.MessageContext.Telemetry)
            {
                AttributeId = Attributes.Value,
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                MonitoringMode = MonitoringMode.Reporting,
                Handle = 1,
                SamplingInterval = 500,
                Filter = null,
                DiscardOldest = true,
                QueueSize = 1
            };

            subscription.AddItem(mi);

            IList<MonitoredItem> result = await subscription.CreateItemsAsync().ConfigureAwait(false);
            NUnit.Framework.Assert.That(ServiceResult.IsGood(result[0].Status.Error), Is.True);

            NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                Session.CallAsync(ObjectIds.Server, MethodIds.Server_SetSubscriptionDurable, default, id, 1));

            Assert.True(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false));
        }

        [Test]
        [Order(180)]
        public async Task SetSubscriptionDurableFailsWhenSubscriptionDoesNotExistAsync()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.True(Session.AddSubscription(subscription));
            await subscription.CreateAsync().ConfigureAwait(false);

            uint id = subscription.Id;

            Assert.True(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false));

            NUnit.Framework.Assert.ThrowsAsync<ServiceResultException>(() =>
                Session.CallAsync(ObjectIds.Server, MethodIds.Server_SetSubscriptionDurable, default, id, 1));
        }

        [Test]
        [Order(200)]
        [TestCase(false, false, TestName = "Validate Session Close")]
        [TestCase(true, false, TestName = "Validate Transfer")]
        [TestCase(true, true, TestName = "Restart of Server")]
        public async Task TestSessionTransferAsync(bool setSubscriptionDurable, bool restartServer)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NUnit.Framework.Assert.Ignore("Timing on mac OS causes issues");
            }

            ISession transferSession = null;
            try
            {
                transferSession = await TestSessionTransferInternalAsync(
                    setSubscriptionDurable,
                    restartServer).ConfigureAwait(false);
            }
            finally
            {
                if (transferSession != null)
                {
                    transferSession.DeleteSubscriptionsOnClose = true;

                    TestContext.Out.WriteLine("------- Transfer session closing --------");
                    await transferSession.CloseAsync().ConfigureAwait(false);
                    transferSession.Dispose();
                }
            }
        }

        private async Task<ISession> TestSessionTransferInternalAsync(
            bool setSubscriptionDurable,
            bool restartServer)
        {
            const int publishingInterval = 100;
            const uint keepAliveCount = 5;
            const uint lifetimeCount = 15;
            const uint requestedHours = 1;
            const uint expectedHours = 1;
            const uint expectedLifetime = 36000;

            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                PublishingInterval = publishingInterval,
                MinLifetimeInterval = 1500
            };

            subscription.StateChanged += (s, e) =>
                TestContext.Out.WriteLine($"StateChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");

            Assert.True(Session.AddSubscription(subscription));
            await subscription.CreateAsync().ConfigureAwait(false);

            // Give some time to allow for the true browse of items
            await Task.Delay(500).ConfigureAwait(false);

            Dictionary<string, NodeId> desiredNodeIds =
                await GetDesiredNodeIdsAsync(subscription.Id).ConfigureAwait(false);
            Dictionary<string, object> initialValues =
                await GetValuesAsync(desiredNodeIds).ConfigureAwait(false);

            if (setSubscriptionDurable)
            {
                (bool success, uint revisedLifetimeInHours) =
                    await subscription.SetSubscriptionDurableAsync(requestedHours).ConfigureAwait(false);
                Assert.True(success);
                Assert.AreEqual(expectedHours, revisedLifetimeInHours);

                await ValidateDataValueAsync(desiredNodeIds, "MaxLifetimeCount", expectedLifetime)
                    .ConfigureAwait(false);
            }
            else
            {
                await ValidateDataValueAsync(desiredNodeIds, "MaxLifetimeCount", lifetimeCount)
                    .ConfigureAwait(false);
            }

            var testSet = new List<NodeId>();
            testSet.AddRange(GetTestSetFullSimulation(Session.NamespaceUris));
            var valueTimeStamps = new Dictionary<NodeId, List<DateTime>>();

            var monitoredItemsList = new List<MonitoredItem>();
            foreach (NodeId nodeId in testSet)
            {
                if (nodeId.IdType == IdType.String)
                {
                    valueTimeStamps.Add(nodeId, []);
                    var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = nodeId,
                        SamplingInterval = 1000,
                        QueueSize = 100
                    };
                    monitoredItem.Notification += (item, _) =>
                    {
                        List<DateTime> list = valueTimeStamps[nodeId];

                        foreach (DataValue value in item.DequeueValues())
                        {
                            list.Add(value.SourceTimestamp);
                        }
                    };

                    monitoredItemsList.Add(monitoredItem);
                }
            }

            //Add Event Monitored Item
            monitoredItemsList.Add(CreateEventMonitoredItem(100));

            DateTime startTime = DateTime.UtcNow;

            subscription.AddItems(monitoredItemsList);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            Dictionary<string, object> closeValues =
                await GetValuesAsync(desiredNodeIds).ConfigureAwait(false);

            var subscriptions = new SubscriptionCollection(Session.Subscriptions);
            DateTime closeTime = DateTime.UtcNow;
            TestContext.Out.WriteLine("Session Id {0} Closing at {1}",
                Session.SessionId, closeTime);
            await Session.CloseAsync(closeChannel: false).ConfigureAwait(false);
            TestContext.Out.WriteLine("Session closed. Initiated at {0}", closeTime);

            if (restartServer)
            {
                // if durable subscription the server will restore the subscription
                TestContext.Out.WriteLine("------- Server stopping --------");
                await ReferenceServer.StopAsync().ConfigureAwait(false);
                await ReferenceServer.StartAsync(ServerFixture.Config).ConfigureAwait(false);
                TestContext.Out.WriteLine("------- Server restarted --------");
            }
            else
            {
                // Subscription should time out with initial lifetime count
                await Task.Delay(3000).ConfigureAwait(false);
            }

            DateTime restartTime = DateTime.UtcNow;
#if !DEBUG_CONNECT_FAILED
            ISession transferSession = await ClientFixture
                .ConnectAsync(
                    ServerUrl,
                    SecurityPolicies.Basic256Sha256,
                    null,
                    new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
#else // TODO: Remove once failure is understood.
            ISession transferSession;
            for (int i = 0; ; i++)
            {
                try
                {
                    transferSession = await ClientFixture
                        .ConnectAsync(
                            ServerUrl,
                            SecurityPolicies.Basic256Sha256,
                            null,
                            new UserIdentity("sysadmin", "demo"u8))
                        .ConfigureAwait(false);
                    if (i != 0)
                    {
                        Debugger.Break();
                    }
                    break;
                }
                catch
                {
                    TestContext.Out.WriteLine("------- Transfer session failed to connect --------");
                    while (!Debugger.IsAttached)
                    {
                        System.Threading.Thread.Sleep(5000);
                    }

                    Debugger.Break();
                }
            }
#endif
            bool result = await transferSession.TransferSubscriptionsAsync(subscriptions, true)
                .ConfigureAwait(false);

            TestContext.Out.WriteLine("------- Dispose original session --------");
            Session.Dispose();
            Session = null;

            bool expected = setSubscriptionDurable; // Otherwise we close the session above and then transfer fails.
            Assert.AreEqual(
                expected,
                result,
                $"SetSubscriptionDurable = {setSubscriptionDurable} => Transfer Result: {result} != Expected {expected}");

            if (setSubscriptionDurable && !restartServer)
            {
                // New Session and Transfer - consume 4 seconds of messages then turn off.
                await Task.Delay(4000).ConfigureAwait(false);

                await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);

                DateTime completionTime = DateTime.UtcNow;

                await Task.Delay(1000).ConfigureAwait(false); // Let last notifications trickle through

                const double tolerance = 2500;

                TestContext.Out.WriteLine("Session StartTime at {0}", DateTimeMs(startTime));
                TestContext.Out.WriteLine("Session Closed at {0}", DateTimeMs(closeTime));
                TestContext.Out.WriteLine("Restart at {0}", DateTimeMs(restartTime));
                TestContext.Out.WriteLine("Completion at {0}", DateTimeMs(completionTime));

                // Validate
                foreach (KeyValuePair<NodeId, List<DateTime>> pair in valueTimeStamps)
                {
                    DateTime previous = startTime;

                    for (int index = 0; index < pair.Value.Count; index++)
                    {
                        DateTime timestamp = pair.Value[index];

                        TimeSpan timeSpan = timestamp - previous;
                        TestContext.Out.WriteLine(
                            $"Node: {pair.Key} Index: {index} Time: {DateTimeMs(timestamp)} " +
                            $"Previous: {DateTimeMs(previous)} " +
                            $"Timespan {timeSpan.TotalMilliseconds.ToString("000.", CultureInfo.InvariantCulture)}");

                        Assert.Less(
                            Math.Abs(timeSpan.TotalMilliseconds),
                            tolerance,
                            $"Node: {pair.Key} Index: {index} Timespan {timeSpan.TotalMilliseconds} ");

                        previous = timestamp;

                        if (index == pair.Value.Count - 1)
                        {
                            TimeSpan finalTimeSpan = completionTime - timestamp;
                            Assert.Less(
                                Math.Abs(finalTimeSpan.TotalMilliseconds),
                                tolerance * 2,
                                $"Last Value - Node: {pair.Key} Index: {index} Timespan {finalTimeSpan.TotalMilliseconds} ");
                        }
                    }
                }
            }

            return transferSession;
        }

        private async Task<Dictionary<string, object>> ValidateDataValueAsync(
            Dictionary<string, NodeId> nodeIds,
            string desiredValue,
            uint expectedValue)
        {
            Dictionary<string, object> modifiedValues =
                await GetValuesAsync(nodeIds).ConfigureAwait(false);

            var dataValue = modifiedValues[desiredValue] as DataValue;
            Assert.IsNotNull(dataValue);
            Assert.IsNotNull(dataValue.Value);
            Assert.AreEqual(
                expectedValue,
                Convert.ToUInt32(dataValue.Value, CultureInfo.InvariantCulture));

            return modifiedValues;
        }

        /// <summary>
        /// Queued samples must be sent on the first Publish after TransferSubscriptions(false).
        /// Raw services prevent the client's automatic Publish pipeline from consuming the response.
        /// </summary>
        [Test]
        public async Task TransferWithoutInitialValuesPublishesQueuedCurrentTimeAsync()
        {
            uint id = await CreateRawDurableSubscriptionAsync(0).ConfigureAwait(false);
            try
            {
                CreateMonitoredItemsResponse items = await CreateRawMonitoredItemsAsync(
                    id, [VariableIds.Server_ServerStatus_CurrentTime], 1000, 1000).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMilliseconds(items.Results[0].RevisedSamplingInterval * 1.5))
                    .ConfigureAwait(false);
                List<MonitoredItemNotification> initial = await DrainRawAsync(id,
                    await PublishRawAsync(id).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.IsNotEmpty(initial);
                DateTime lastTimestamp = initial.Last().Value.ServerTimestamp;

                await CloseRawSessionAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await MySetUpAsync().ConfigureAwait(false);
                await TransferRawAsync(id).ConfigureAwait(false);

                PublishResponse first = await PublishRawAsync(id).ConfigureAwait(false);
                Assert.IsNotEmpty(first.NotificationMessage.NotificationData,
                    "The first Publish after transfer must contain queued data, not a keep-alive.");
                List<MonitoredItemNotification> values = await DrainRawAsync(id, first).ConfigureAwait(false);
                Assert.Greater(values.Count, 1, "Samples must accumulate while disconnected.");
                Assert.GreaterOrEqual(values[0].Value.ServerTimestamp, lastTimestamp);
                Assert.IsTrue(values.All(value => StatusCode.IsGood(value.Value.StatusCode)));
                // Allow a server clock update (1 second) and scheduling jitter in addition to sampling.
                Assert.LessOrEqual((DateTime.UtcNow - values.Last().Value.ServerTimestamp).TotalMilliseconds,
                    items.Results[0].RevisedSamplingInterval + 2000,
                    "The final sample must be current relative to the sampling interval.");
            }
            finally
            {
                await DeleteRawSubscriptionAsync(id).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Draining queued writes must be followed by a keep-alive without extra initial values.
        /// Cover both one response and notification limits requiring MoreNotifications.
        /// </summary>
        [TestCase(0u)]
        [TestCase(7u)]
        public async Task TransferWithoutInitialValuesReturnsKeepAliveAfterDrainingAsync(uint maxNotifications)
        {
            const int loopCount = 10;
            NodeId[] nodes = GetTestSetStaticMassNumeric(Session.NamespaceUris)
                .Where(entry => entry.Value == typeof(int)).Take(3).Select(entry => entry.Key).ToArray();
            Assert.AreEqual(3, nodes.Length);
            var originals = new List<int>();
            foreach (NodeId node in nodes)
            {
                originals.Add((int)(await Session.ReadValueAsync(node).ConfigureAwait(false)).Value);
            }
            uint id = await CreateRawDurableSubscriptionAsync(maxNotifications).ConfigureAwait(false);
            try
            {
                await WriteRawValuesAsync(nodes, Enumerable.Repeat(-1, nodes.Length).ToArray()).ConfigureAwait(false);
                CreateMonitoredItemsResponse items = await CreateRawMonitoredItemsAsync(
                    id, nodes, loopCount, 100).ConfigureAwait(false);
                List<MonitoredItemNotification> initial = await DrainRawAsync(id,
                    await PublishRawAsync(id).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.AreEqual(nodes.Length, initial.Count);
                Assert.IsEmpty((await PublishRawAsync(id).ConfigureAwait(false)).NotificationMessage.NotificationData);

                await CloseRawSessionAsync().ConfigureAwait(false);
                await MySetUpAsync().ConfigureAwait(false);
                double samplingInterval = items.Results.Max(item => item.RevisedSamplingInterval);
                for (int value = 0; value < loopCount; value++)
                {
                    await WriteRawValuesAsync(nodes, Enumerable.Repeat(value, nodes.Length).ToArray()).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMilliseconds(samplingInterval * 1.5)).ConfigureAwait(false);
                }
                await CloseRawSessionAsync().ConfigureAwait(false);
                await MySetUpAsync().ConfigureAwait(false);
                await TransferRawAsync(id).ConfigureAwait(false);

                PublishResponse first = await PublishRawAsync(id).ConfigureAwait(false);
                Assert.IsNotEmpty(first.NotificationMessage.NotificationData,
                    "Queued writes must be delivered on the first Publish after transfer.");
                if (maxNotifications == 0)
                {
                    Assert.AreEqual(nodes.Length * loopCount,
                        ((DataChangeNotification)first.NotificationMessage.NotificationData.Single().Body).MonitoredItems.Count);
                }
                else
                {
                    Assert.IsTrue(first.MoreNotifications);
                }
                List<MonitoredItemNotification> values = await DrainRawAsync(id, first).ConfigureAwait(false);
                Assert.AreEqual(nodes.Length * loopCount, values.Count);
                for (uint handle = 1; handle <= nodes.Length; handle++)
                {
                    Assert.AreEqual(Enumerable.Range(0, loopCount), values
                        .Where(value => value.ClientHandle == handle).Select(value => value.Value.Value),
                        "Every queued write must be delivered once and in order for each item.");
                }
                PublishResponse keepAlive = await PublishRawAsync(id).ConfigureAwait(false);
                Assert.IsEmpty(keepAlive.NotificationMessage.NotificationData,
                    "The next Publish after draining must be a keep-alive.");
                Assert.IsFalse(keepAlive.MoreNotifications);
            }
            finally
            {
                await DeleteRawSubscriptionAsync(id).ConfigureAwait(false);
                await WriteRawValuesAsync(nodes, originals.ToArray()).ConfigureAwait(false);
            }
        }

        private async Task<uint> CreateRawDurableSubscriptionAsync(uint maxNotifications)
        {
            CreateSubscriptionResponse response = await Session.CreateSubscriptionAsync(
                null, 100, 1000, 5, maxNotifications, true, 0, CancellationToken.None).ConfigureAwait(false);
            IList<object> result = await Session.CallAsync(ObjectIds.Server,
                MethodIds.Server_SetSubscriptionDurable, CancellationToken.None, response.SubscriptionId, 1u)
                .ConfigureAwait(false);
            Assert.AreEqual(1u, result[0]);
            return response.SubscriptionId;
        }

        private async Task<CreateMonitoredItemsResponse> CreateRawMonitoredItemsAsync(
            uint id, NodeId[] nodes, uint queueSize, double samplingInterval)
        {
            var requests = new MonitoredItemCreateRequestCollection(nodes.Select((node, index) =>
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = node, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = (uint)index + 1,
                        SamplingInterval = samplingInterval,
                        QueueSize = queueSize,
                        DiscardOldest = true
                    }
                }));
            CreateMonitoredItemsResponse response = await Session.CreateMonitoredItemsAsync(
                null, id, TimestampsToReturn.Both, requests, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(nodes.Length, response.Results.Count);
            foreach (MonitoredItemCreateResult result in response.Results)
            {
                Assert.IsTrue(StatusCode.IsGood(result.StatusCode));
                Assert.AreEqual(queueSize, result.RevisedQueueSize);
            }
            return response;
        }

        private async Task CloseRawSessionAsync()
        {
            Session.DeleteSubscriptionsOnClose = false;
            await Session.CloseAsync().ConfigureAwait(false);
            Session.Dispose();
            Session = null;
        }

        private async Task TransferRawAsync(uint id)
        {
            TransferSubscriptionsResponse response = await Session.TransferSubscriptionsAsync(
                null, [id], false, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(1, response.Results.Count);
            Assert.AreEqual(StatusCodes.Good, response.Results[0].StatusCode.Code);
        }

        private async Task<PublishResponse> PublishRawAsync(uint id)
        {
            PublishResponse response = await Session.PublishAsync(
                new RequestHeader { TimeoutHint = 10000 }, [], CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(id, response.SubscriptionId);
            Assert.AreEqual(StatusCodes.Good, response.ResponseHeader.ServiceResult.Code);
            return response;
        }

        private async Task<List<MonitoredItemNotification>> DrainRawAsync(uint id, PublishResponse response)
        {
            var values = new List<MonitoredItemNotification>();
            for (int responses = 0; ; responses++)
            {
                Assert.Less(responses, 100, "MoreNotifications must eventually become false.");
                foreach (ExtensionObject notification in response.NotificationMessage.NotificationData)
                {
                    Assert.IsInstanceOf<DataChangeNotification>(notification.Body);
                    values.AddRange(((DataChangeNotification)notification.Body).MonitoredItems);
                }
                if (!response.MoreNotifications)
                {
                    return values;
                }
                response = await PublishRawAsync(id).ConfigureAwait(false);
            }
        }

        private async Task WriteRawValuesAsync(NodeId[] nodes, int[] values)
        {
            var writes = new WriteValueCollection(nodes.Select((node, index) => new WriteValue
            {
                NodeId = node,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(values[index]))
            }));
            WriteResponse response = await Session.WriteAsync(null, writes, CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(nodes.Length, response.Results.Count);
            Assert.IsTrue(response.Results.All(StatusCode.IsGood));
        }

        private async Task DeleteRawSubscriptionAsync(uint id)
        {
            if (Session == null)
            {
                await MySetUpAsync().ConfigureAwait(false);
            }
            // Reclaim the subscription if the test failed while it was abandoned.
            await Session.TransferSubscriptionsAsync(null, [id], false, CancellationToken.None).ConfigureAwait(false);
            DeleteSubscriptionsResponse response = await Session.DeleteSubscriptionsAsync(
                null, [id], CancellationToken.None).ConfigureAwait(false);
            Assert.AreEqual(StatusCodes.Good, response.Results[0].Code);
        }

        private async Task<TestableSubscription> CreateDurableSubscriptionAsync()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.True(Session.AddSubscription(subscription));
            await subscription.CreateAsync().ConfigureAwait(false);

            (bool success, _) = await subscription.SetSubscriptionDurableAsync(1)
                .ConfigureAwait(false);
            Assert.True(success);

            return subscription;
        }

        private async Task<Dictionary<string, NodeId>> GetDesiredNodeIdsAsync(
            uint subscriptionId)
        {
            var desiredNodeIds = new Dictionary<string, NodeId>();

            var serverDiags = new NodeId(
                Variables.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

            NodeId monitoredItemCountNodeId = default;
            NodeId maxLifetimeCountNodeId = default;
            NodeId maxKeepAliveCountNodeId = default;
            NodeId currentLifetimeCountNodeId = default;
            NodeId publishingIntervalNodeId = default;

            (_, _, ReferenceDescriptionCollection references) = await Session.BrowseAsync(
                null,
                null,
                serverDiags,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                0).ConfigureAwait(false);

            Assert.NotNull(references, "Initial Browse has no references");
            Assert.Greater(references.Count, 0, "Initial Browse has zero references");

            TestContext.Out.WriteLine(
                "Initial Browse for SubscriptionDiagnosticsArray has {0} references, Desired SubscriptionId {1}",
                references.Count,
                subscriptionId);

            foreach (ReferenceDescription reference in references)
            {
                TestContext.Out
                    .WriteLine("Initial Browse Reference {0}", reference.BrowseName.Name);

                if (reference.BrowseName.Name == subscriptionId.ToString(
                    CultureInfo.InvariantCulture))
                {
                    (
                        _,
                        byte[] anotherContinuationPoint,
                        ReferenceDescriptionCollection desiredReferences
                    ) = await Session.BrowseAsync(
                        null,
                        null,
                        (NodeId)reference.NodeId,
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HierarchicalReferences,
                        true,
                        0).ConfigureAwait(false);

                    Assert.NotNull(desiredReferences, "Secondary Browse has no references");
                    Assert.Greater(
                        desiredReferences.Count,
                        0,
                        "Secondary Browse has zero references");

                    TestContext.Out.WriteLine(
                        "Secondary Browse for SubscriptionId {0} has {1} references",
                        subscriptionId,
                        desiredReferences.Count);

                    foreach (ReferenceDescription referenceDescription in desiredReferences)
                    {
                        NodeId recreated = default;
                        if (referenceDescription.NodeId.IsNull)
                        {
                            TestContext.Out.WriteLine(
                                "Subscription Reference {0} ExpandedNodeId is Null",
                                referenceDescription.BrowseName.Name);
                            TestContext.Out.WriteLine(
                                "Full ReferenceDescription {0}",
                                referenceDescription.ToString());
                        }
                        else
                        {
                            recreated = new NodeId(
                                referenceDescription.NodeId.Identifier,
                                referenceDescription.NodeId.NamespaceIndex);

                            if (recreated.IsNullNodeId)
                            {
                                TestContext.Out.WriteLine(
                                    "Subscription Reference {0} Recreated Node is Null",
                                    referenceDescription.BrowseName.Name);
                                TestContext.Out.WriteLine(
                                    "Full ReferenceDescription {0}",
                                    referenceDescription.ToString());
                            }
                            else
                            {
                                TestContext.Out.WriteLine(
                                    "Subscription Reference {0} ExpandedNodeId {1} Recreated {2}",
                                    referenceDescription.BrowseName.Name,
                                    referenceDescription.NodeId.ToString(),
                                    recreated.ToString());
                            }
                        }

                        if (referenceDescription.BrowseName.Name.Equals(
                                "MonitoredItemCount",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            monitoredItemCountNodeId = recreated;
                        }
                        else if (referenceDescription.BrowseName.Name.Equals(
                                "MaxLifetimeCount",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            maxLifetimeCountNodeId = recreated;
                        }
                        else if (referenceDescription.BrowseName.Name.Equals(
                                "MaxKeepAliveCount",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            maxKeepAliveCountNodeId = recreated;
                        }
                        else if (referenceDescription.BrowseName.Name.Equals(
                                "CurrentLifetimeCount",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            currentLifetimeCountNodeId = recreated;
                        }
                        else if (referenceDescription.BrowseName.Name.Equals(
                                "PublishingInterval",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            publishingIntervalNodeId = recreated;
                        }
                    }
                    break;
                }
            }

            Assert.IsNotNull(monitoredItemCountNodeId, "Unable to find MonitoredItemCount");
            Assert.IsNotNull(maxLifetimeCountNodeId, "Unable to find MaxLifetimeCount");
            Assert.IsNotNull(maxKeepAliveCountNodeId, "Unable to find MaxKeepAliveCount");
            Assert.IsNotNull(currentLifetimeCountNodeId, "Unable to find CurrentLifetimeCount");
            Assert.IsNotNull(publishingIntervalNodeId, "Unable to find PublishingInterval");

            desiredNodeIds.Add("MonitoredItemCount", monitoredItemCountNodeId);
            desiredNodeIds.Add("MaxLifetimeCount", maxLifetimeCountNodeId);
            desiredNodeIds.Add("MaxKeepAliveCount", maxKeepAliveCountNodeId);
            desiredNodeIds.Add("CurrentLifetimeCount", currentLifetimeCountNodeId);
            desiredNodeIds.Add("PublishingInterval", publishingIntervalNodeId);

            return desiredNodeIds;
        }

        private async Task<Dictionary<string, object>> GetValuesAsync(Dictionary<string, NodeId> ids)
        {
            var values = new Dictionary<string, object>();

            foreach (KeyValuePair<string, NodeId> id in ids)
            {
                values.Add(id.Key,
                    await Session.ReadValueAsync(id.Value).ConfigureAwait(false));
                TestContext.Out.WriteLine($"{id.Key}: {values[id.Key]}");
            }

            return values;
        }

        private MonitoredItem CreateEventMonitoredItem(uint queueSize)
        {
            var whereClause = new ContentFilter();

            whereClause.Push(
                FilterOperator.Equals,
                [
                    new SimpleAttributeOperand
                    {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [.. new QualifiedName[] { "EventType" }]
                    },
                    new LiteralOperand {
                        Value = new Variant(ObjectTypeIds.BaseEventType) }
                ]);

            return new MonitoredItem(Session.MessageContext.Telemetry)
            {
                AttributeId = Attributes.EventNotifier,
                StartNodeId = ObjectIds.Server,
                MonitoringMode = MonitoringMode.Reporting,
                Handle = 1,
                SamplingInterval = -1,
                Filter = new EventFilter
                {
                    SelectClauses =
                    [
                        .. new SimpleAttributeOperand[]
                        {
                            new()
                            {
                                AttributeId = Attributes.Value,
                                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                BrowsePath = [.. new QualifiedName[] { BrowseNames.Message }]
                            }
                        }
                    ],
                    WhereClause = whereClause
                },
                DiscardOldest = true,
                QueueSize = queueSize
            };
        }

        private static string DateTimeMs(DateTime dateTime)
        {
            return dateTime.ToLongTimeString() +
                "." +
                dateTime.Millisecond.ToString("D3", CultureInfo.InvariantCulture);
        }
    }
}
