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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Tests;
using Quickstarts.ReferenceServer;

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
                    t => new ReferenceServer(t),
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
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                };

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

        private async Task MySetUpAsync()
        {
            if (!SingleSession)
            {
                try
                {
                    ClientFixture.SessionTimeout = 10000;
                    using var userIdentity = new UserIdentity("sysadmin", "demo"u8);
                    Session = await ClientFixture
                        .ConnectAsync(
                            ServerUrl,
                            SecurityPolicies.Basic256Sha256,
                            default,
                            userIdentity)
                        .ConfigureAwait(false);
                    Session.DeleteSubscriptionsOnClose = false;
                }
                catch (Exception e)
                {
                    Assert.Ignore(
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
            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                PublishingInterval = publishingInterval
            };

            Assert.That(Session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);

            Dictionary<string, NodeId> desiredNodeIds =
                await GetDesiredNodeIdsAsync(subscription.Id).ConfigureAwait(false);

            (bool success, uint revisedLifetimeInHours) =
                await subscription.SetSubscriptionDurableAsync(requestedHours).ConfigureAwait(false);
            Assert.That(success, Is.True);
            Assert.That(revisedLifetimeInHours, Is.EqualTo(expectedHours));

            Dictionary<string, object> modifiedValues =
                await GetValuesAsync(desiredNodeIds).ConfigureAwait(false);

            var maxLifetimeCountValue = modifiedValues["MaxLifetimeCount"] as DataValue;
            Assert.That(maxLifetimeCountValue, Is.Not.Null);
            Assert.That(maxLifetimeCountValue.WrappedValue.IsNull, Is.False);
            Assert.That(
                maxLifetimeCountValue.WrappedValue.ConvertToUInt32(),
                Is.EqualTo(expectedLifetime));

            Assert.That(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false), Is.True);
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
            using TestableSubscription subscription = await CreateDurableSubscriptionAsync()
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

            ArrayOf<MonitoredItem> result = await subscription.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result[0].Status.Error), Is.True);
            Assert
                .That(result[0].Status.QueueSize, Is.EqualTo(expectedRevisedQueueSize));

            mi.QueueSize = queueSize + 1;

            ArrayOf<MonitoredItem> resultModify = await subscription.ModifyItemsAsync()
                .ConfigureAwait(false);
            Assert
                .That(ServiceResult.IsGood(resultModify[0].Status.Error), Is.True);
            Assert
                .That(resultModify[0].Status.QueueSize, Is.EqualTo(expectedModifiedQueueSize));

            (bool success, _, _) = await subscription.GetMonitoredItemsAsync().ConfigureAwait(false);
            Assert.That(success, Is.True);

            Assert.That(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false), Is.True);
        }

        [Test]
        [Order(160)]
        public async Task SetSubscriptionDurableFailsWhenMIExistsAsync()
        {
            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.That(Session.AddSubscription(subscription), Is.True);
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

            ArrayOf<MonitoredItem> result = await subscription.CreateItemsAsync().ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result[0].Status.Error), Is.True);

            Assert.ThrowsAsync<ServiceResultException>(() =>
                Session.CallAsync(ObjectIds.Server, MethodIds.Server_SetSubscriptionDurable, default, id, 1));

            Assert.That(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false), Is.True);
        }

        [Test]
        [Order(180)]
        public async Task SetSubscriptionDurableFailsWhenSubscriptionDoesNotExistAsync()
        {
            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.That(Session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);

            uint id = subscription.Id;

            Assert.That(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false), Is.True);

            Assert.ThrowsAsync<ServiceResultException>(() =>
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
                Assert.Ignore("Timing on mac OS causes issues");
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

            using var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = keepAliveCount,
                LifetimeCount = lifetimeCount,
                PublishingInterval = publishingInterval,
                MinLifetimeInterval = 1500
            };

            subscription.StateChanged += (s, e) =>
                TestContext.Out.WriteLine($"StateChanged: {s.Session.SessionId}-{s.Id}-{e.Status}");

            Assert.That(Session.AddSubscription(subscription), Is.True);
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
                Assert.That(success, Is.True);
                Assert.That(revisedLifetimeInHours, Is.EqualTo(expectedHours));

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
            var valueTimeStamps = new Dictionary<NodeId, List<DateTimeUtc>>();

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
                        List<DateTimeUtc> list = valueTimeStamps[nodeId];

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

            DateTimeUtc startTime = DateTimeUtc.Now;

            subscription.AddItems(monitoredItemsList);
            await subscription.ApplyChangesAsync().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            Dictionary<string, object> closeValues =
                await GetValuesAsync(desiredNodeIds).ConfigureAwait(false);

            var subscriptions = new SubscriptionCollection(Session.Subscriptions);
            DateTimeUtc closeTime = DateTimeUtc.Now;
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

            DateTimeUtc restartTime = DateTimeUtc.Now;
#if !DEBUG_CONNECT_FAILED
            using var transferUserIdentity = new UserIdentity("sysadmin", "demo"u8);
            ISession transferSession = await ClientFixture
                .ConnectAsync(
                    ServerUrl,
                    SecurityPolicies.Basic256Sha256,
                    default,
                    transferUserIdentity)
                .ConfigureAwait(false);
#else // TODO: Remove once failure is understood.
            ISession transferSession;
            for (int i = 0; ; i++)
            {
                try
                {
                    using var retryUserIdentity = new UserIdentity("sysadmin", "demo"u8);
                    transferSession = await ClientFixture
                        .ConnectAsync(
                            ServerUrl,
                            SecurityPolicies.Basic256Sha256,
                            null,
                            retryUserIdentity)
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
            Assert.That(
                result,
                Is.EqualTo(expected),
                $"SetSubscriptionDurable = {setSubscriptionDurable} => Transfer Result: {result} != Expected {expected}");

            if (setSubscriptionDurable && !restartServer)
            {
                // New Session and Transfer - consume 4 seconds of messages then turn off.
                await Task.Delay(4000).ConfigureAwait(false);

                await subscription.SetPublishingModeAsync(false).ConfigureAwait(false);

                DateTimeUtc completionTime = DateTimeUtc.Now;

                await Task.Delay(1000).ConfigureAwait(false); // Let last notifications trickle through

                const double tolerance = 2500;

                TestContext.Out.WriteLine("Session StartTime at {0}", DateTimeMs(startTime));
                TestContext.Out.WriteLine("Session Closed at {0}", DateTimeMs(closeTime));
                TestContext.Out.WriteLine("Restart at {0}", DateTimeMs(restartTime));
                TestContext.Out.WriteLine("Completion at {0}", DateTimeMs(completionTime));

                // Validate
                foreach (KeyValuePair<NodeId, List<DateTimeUtc>> pair in valueTimeStamps)
                {
                    DateTimeUtc previous = startTime;

                    for (int index = 0; index < pair.Value.Count; index++)
                    {
                        DateTimeUtc timestamp = pair.Value[index];

                        TimeSpan timeSpan = timestamp - previous;
                        TestContext.Out.WriteLine(
                            $"Node: {pair.Key} Index: {index} Time: {DateTimeMs(timestamp)} " +
                            $"Previous: {DateTimeMs(previous)} " +
                            $"Timespan {timeSpan.TotalMilliseconds.ToString("000.", CultureInfo.InvariantCulture)}");

                        Assert.That(
                            Math.Abs(timeSpan.TotalMilliseconds),
                            Is.LessThan(tolerance),
                            $"Node: {pair.Key} Index: {index} Timespan {timeSpan.TotalMilliseconds} ");

                        previous = timestamp;

                        if (index == pair.Value.Count - 1)
                        {
                            TimeSpan finalTimeSpan = completionTime - timestamp;
                            Assert.That(
                                Math.Abs(finalTimeSpan.TotalMilliseconds),
                                Is.LessThan(tolerance * 2),
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
            Assert.That(dataValue, Is.Not.Null);
            Assert.That(dataValue.WrappedValue.IsNull, Is.False);
            Assert.That(
                dataValue.WrappedValue.ConvertToUInt32(),
                Is.EqualTo(expectedValue));

            return modifiedValues;
        }

        private async Task<TestableSubscription> CreateDurableSubscriptionAsync()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.That(Session.AddSubscription(subscription), Is.True);
            await subscription.CreateAsync().ConfigureAwait(false);

            (bool success, _) = await subscription.SetSubscriptionDurableAsync(1)
                .ConfigureAwait(false);
            Assert.That(success, Is.True);

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

            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                null,
                null,
                serverDiags,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                0).ConfigureAwait(false);

            Assert.That(references.IsNull, Is.False, "Initial Browse has no references");
            Assert.That(references.Count, Is.GreaterThan(0), "Initial Browse has zero references");

            TestContext.Out.WriteLine(
                "Initial Browse for SubscriptionDiagnosticsArray has {0} references, Desired SubscriptionId {1}",
                references.Count,
                subscriptionId);

            foreach (ReferenceDescription reference in references.ToList())
            {
                TestContext.Out
                    .WriteLine("Initial Browse Reference {0}", reference.BrowseName.Name);

                if (reference.BrowseName.Name == subscriptionId.ToString(
                    CultureInfo.InvariantCulture))
                {
                    (
                        _,
                        ByteString anotherContinuationPoint,
                        ArrayOf<ReferenceDescription> desiredReferences
                    ) = await Session.BrowseAsync(
                        null,
                        null,
                        (NodeId)reference.NodeId,
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HierarchicalReferences,
                        true,
                        0).ConfigureAwait(false);

                    Assert.That(desiredReferences.IsNull, Is.False, "Secondary Browse has no references");
                    Assert.That(
                        desiredReferences.Count,
                        Is.GreaterThan(0),
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
                            recreated = referenceDescription.NodeId.InnerNodeId.WithNamespaceIndex(
                                referenceDescription.NodeId.NamespaceIndex);

                            if (recreated.IsNull)
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

            Assert.That(monitoredItemCountNodeId.IsNull, Is.False, "Unable to find MonitoredItemCount");
            Assert.That(maxLifetimeCountNodeId.IsNull, Is.False, "Unable to find MaxLifetimeCount");
            Assert.That(maxKeepAliveCountNodeId.IsNull, Is.False, "Unable to find MaxKeepAliveCount");
            Assert.That(currentLifetimeCountNodeId.IsNull, Is.False, "Unable to find CurrentLifetimeCount");
            Assert.That(publishingIntervalNodeId.IsNull, Is.False, "Unable to find PublishingInterval");

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
                    Variant.FromStructure(new SimpleAttributeOperand
                    {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [.. new QualifiedName[] { QualifiedName.From("EventType") }]
                    }),
                    Variant.FromStructure(new LiteralOperand
                    {
                        Value = Variant.From(ObjectTypeIds.BaseEventType)
                    })
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
                                BrowsePath = [.. new QualifiedName[] { QualifiedName.From(BrowseNames.Message) }]
                            }
                        }
                    ],
                    WhereClause = whereClause
                },
                DiscardOldest = true,
                QueueSize = queueSize
            };
        }

        private static string DateTimeMs(DateTimeUtc dateTime)
        {
            var dt = dateTime.ToDateTime();
            return dt.ToLongTimeString() +
                "." +
                dt.Millisecond.ToString("D3", CultureInfo.InvariantCulture);
        }
    }
}
