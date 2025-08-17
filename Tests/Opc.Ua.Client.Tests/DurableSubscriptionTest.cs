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
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
            return OneTimeSetUpAsync(writer: null, securityNone: true);
        }

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone,
            TextWriter writer)
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

            if (writer != null)
            {
                ServerFixture.TraceMasks = Utils.TraceMasks.Error | Utils.TraceMasks.Security;
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

            ReferenceServer = await ServerFixture.StartAsync(writer ?? TestContext.Out)
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
                            new UserIdentity("sysadmin", "demo"))
                        .ConfigureAwait(false);
                    Session.DeleteSubscriptionsOnClose = false;
                }
                catch (Exception e)
                {
                    NUnit.Framework.Assert.Ignore(
                        $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
                }
            }
            if (ServerFixture == null)
            {
                ClientFixture.SetTraceOutput(TestContext.Out);
            }
            else
            {
                ServerFixture.SetTraceOutput(TestContext.Out);
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
        public void TestLifetime(
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
            subscription.Create();

            Dictionary<string, NodeId> desiredNodeIds = GetDesiredNodeIds(subscription.Id);

            Assert.True(
                subscription.SetSubscriptionDurable(
                    requestedHours,
                    out uint revisedLifetimeInHours));

            Assert.AreEqual(expectedHours, revisedLifetimeInHours);

            Dictionary<string, object> modifiedValues = GetValues(desiredNodeIds);

            var maxLifetimeCountValue = modifiedValues["MaxLifetimeCount"] as DataValue;
            Assert.IsNotNull(maxLifetimeCountValue);
            Assert.IsNotNull(maxLifetimeCountValue.Value);
            Assert.AreEqual(
                expectedLifetime,
                Convert.ToUInt32(maxLifetimeCountValue.Value, CultureInfo.InvariantCulture));

            Assert.True(Session.RemoveSubscription(subscription));
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
                mi = new MonitoredItem
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

            IList<MonitoredItem> result = await subscription.CreateItemsAsync()
                .ConfigureAwait(false);
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

            Assert.True(subscription.GetMonitoredItems(out _, out _));

            Assert.True(await Session.RemoveSubscriptionAsync(subscription).ConfigureAwait(false));
        }

        [Test]
        [Order(160)]
        public void SetSubscriptionDurableFailsWhenMIExists()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.True(Session.AddSubscription(subscription));
            subscription.Create();

            uint id = subscription.Id;

            var mi = new MonitoredItem
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

            IList<MonitoredItem> result = subscription.CreateItems();
            NUnit.Framework.Assert.That(ServiceResult.IsGood(result[0].Status.Error), Is.True);

            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                Session.Call(ObjectIds.Server, MethodIds.Server_SetSubscriptionDurable, id, 1));

            Assert.True(Session.RemoveSubscription(subscription));
        }

        [Test]
        [Order(180)]
        public void SetSubscriptionDurableFailsWhenSubscriptionDoesNotExist()
        {
            var subscription = new TestableSubscription(Session.DefaultSubscription)
            {
                KeepAliveCount = 100u,
                LifetimeCount = 100u,
                PublishingInterval = 900
            };

            Assert.True(Session.AddSubscription(subscription));
            subscription.Create();

            uint id = subscription.Id;

            Assert.True(Session.RemoveSubscription(subscription));

            NUnit.Framework.Assert.Throws<ServiceResultException>(() =>
                Session.Call(ObjectIds.Server, MethodIds.Server_SetSubscriptionDurable, id, 1));
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
            subscription.Create();

            // Give some time to allow for the true browse of items
            await Task.Delay(500).ConfigureAwait(false);

            Dictionary<string, NodeId> desiredNodeIds = GetDesiredNodeIds(subscription.Id);
            Dictionary<string, object> initialValues = GetValues(desiredNodeIds);

            if (setSubscriptionDurable)
            {
                Assert.True(subscription.SetSubscriptionDurable(
                    requestedHours,
                    out uint revisedLifetimeInHours));

                Assert.AreEqual(expectedHours, revisedLifetimeInHours);

                ValidateDataValue(desiredNodeIds, "MaxLifetimeCount", expectedLifetime);
            }
            else
            {
                ValidateDataValue(desiredNodeIds, "MaxLifetimeCount", lifetimeCount);
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
            subscription.ApplyChanges();
            await Task.Delay(2000).ConfigureAwait(false);

            Dictionary<string, object> closeValues = GetValues(desiredNodeIds);

            var subscriptions = new SubscriptionCollection(Session.Subscriptions);
            DateTime closeTime = DateTime.UtcNow;
            TestContext.Out.WriteLine("Session Id {0} Closed at {1}", Session.SessionId, closeTime);

            Session.Close(closeChannel: false);

            if (restartServer)
            {
                // if durable subscription the server will restore the subscription
                ReferenceServer.Stop();
                ReferenceServer.Start(ServerFixture.Config);
            }
            else
            {
                // Subscription should time out with initial lifetime count
                await Task.Delay(3000).ConfigureAwait(false);
            }

            DateTime restartTime = DateTime.UtcNow;
            ISession transferSession = await ClientFixture
                .ConnectAsync(
                    ServerUrl,
                    SecurityPolicies.Basic256Sha256,
                    null,
                    new UserIdentity("sysadmin", "demo"))
                .ConfigureAwait(false);

            bool result = await transferSession.TransferSubscriptionsAsync(subscriptions, true)
                .ConfigureAwait(false);

            Assert.AreEqual(
                setSubscriptionDurable,
                result,
                "SetSubscriptionDurable = " +
                setSubscriptionDurable.ToString() +
                " Transfer Result " +
                result.ToString() +
                " Expected " +
                setSubscriptionDurable);

            if (setSubscriptionDurable && !restartServer)
            {
                // New Session and Transfer
                await Task.Delay(4000).ConfigureAwait(false);

                DateTime completionTime = DateTime.UtcNow;

                subscription.SetPublishingMode(false);

                const double tolerance = 1500;

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
                            $"Node: {pair.Key} Index: {index} Time: {DateTimeMs(timestamp)} Previous: {DateTimeMs(previous)} Timespan {timeSpan.TotalMilliseconds.ToString("000.", CultureInfo.InvariantCulture)}");

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
            else if (setSubscriptionDurable)
            {
                Assert.True(await transferSession.RemoveSubscriptionAsync(subscription)
                    .ConfigureAwait(false));
            }
        }

        private Dictionary<string, object> ValidateDataValue(
            Dictionary<string, NodeId> nodeIds,
            string desiredValue,
            uint expectedValue)
        {
            Dictionary<string, object> modifiedValues = GetValues(nodeIds);

            var dataValue = modifiedValues[desiredValue] as DataValue;
            Assert.IsNotNull(dataValue);
            Assert.IsNotNull(dataValue.Value);
            Assert.AreEqual(
                expectedValue,
                Convert.ToUInt32(dataValue.Value, CultureInfo.InvariantCulture));

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

            Assert.True(Session.AddSubscription(subscription));
            subscription.Create();

            (bool success, _) = await subscription.SetSubscriptionDurableAsync(1)
                .ConfigureAwait(false);
            Assert.True(success);

            return subscription;
        }

        private Dictionary<string, NodeId> GetDesiredNodeIds(uint subscriptionId)
        {
            var desiredNodeIds = new Dictionary<string, NodeId>();

            var serverDiags = new NodeId(
                Variables.Server_ServerDiagnostics_SubscriptionDiagnosticsArray);

            NodeId monitoredItemCountNodeId = null;
            NodeId maxLifetimeCountNodeId = null;
            NodeId maxKeepAliveCountNodeId = null;
            NodeId currentLifetimeCountNodeId = null;
            NodeId publishingIntervalNodeId = null;

            Session.Browse(
                null,
                null,
                serverDiags,
                0u,
                BrowseDirection.Forward,
                ReferenceTypeIds.HierarchicalReferences,
                true,
                0,
                out _,
                out ReferenceDescriptionCollection references);

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
                    Session.Browse(
                        null,
                        null,
                        (NodeId)reference.NodeId,
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HierarchicalReferences,
                        true,
                        0,
                        out byte[] anotherContinuationPoint,
                        out ReferenceDescriptionCollection desiredReferences);

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
                        NodeId recreated = null;
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

        private Dictionary<string, object> GetValues(Dictionary<string, NodeId> ids)
        {
            var values = new Dictionary<string, object>();

            foreach (KeyValuePair<string, NodeId> id in ids)
            {
                values.Add(id.Key, Session.ReadValue(id.Value));
                TestContext.Out.WriteLine($"{id.Key}: {values[id.Key]}");
            }

            return values;
        }

        private static MonitoredItem CreateEventMonitoredItem(uint queueSize)
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
                        Value = new Variant(new NodeId(ObjectTypeIds.BaseEventType)) }
                ]);

            return new MonitoredItem
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
