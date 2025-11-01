/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Server load tests.
    /// </summary>
    [TestFixture]
    [Category("LoadTest")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    public class LoadTest : ClientTestFramework
    {
        public LoadTest(string uriScheme) : base(uriScheme)
        {
            SingleSession = false;
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            return base.OneTimeSetUpAsync();
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

        /// <summary>
        /// Load test a server with multiple sessions and subscriptions.
        /// </summary>
        [Test]
        [Explicit]
        [Order(100)]
        public async Task ServerLoadTest()
        {
            const int sessionCount = 20;
            const int subscriptionsPerSession = 2;
            const int publishingInterval = 100;
            const int writerInterval = 150;
            const int testDurationSeconds = 300;

            var sessions = new List<ISession>();
            var subscriptions = new List<Subscription>();
            var valueChanges = new ConcurrentDictionary<NodeId, int>();
            var monitoredItems = new List<MonitoredItem>();
            var clientHandles = new ConcurrentDictionary<uint, NodeId>();

            try
            {
                // Get nodes for subscription
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                TestContext.Out.WriteLine($"Subscribing to {nodeIds.Count} nodes.");

                // Create reader sessions and subscriptions
                for (int i = 0; i < sessionCount; i++)
                {
                    var session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                    sessions.Add(session);

                    for (int j = 0; j < subscriptionsPerSession; j++)
                    {
                        var subscription = new Subscription(session.DefaultSubscription)
                        {
                            PublishingInterval = publishingInterval
                        };

                        foreach (NodeId nodeId in nodeIds.Keys)
                        {

                            var item = new MonitoredItem(subscription.DefaultItem)
                            {
                                StartNodeId = nodeId,
                                AttributeId = Attributes.Value,
                                MonitoringMode = MonitoringMode.Reporting,
                                SamplingInterval = 0,
                            };
                            valueChanges.TryAdd(nodeId, 0);
                            clientHandles.TryAdd(item.ClientHandle, nodeId);


                            //item.Notification += (item, _) =>
                            //{
                            //    foreach (DataValue value in item.DequeueValues())
                            //    {
                            //        TestContext.Out.WriteLine(
                            //            "{0}: {1}, {2}, {3}",
                            //            item.DisplayName,
                            //            value.Value,
                            //            value.SourceTimestamp,
                            //            value.StatusCode);
                            //    }
                            //};

                            monitoredItems.Add(item);
                            subscription.AddItem(item);

                            subscription.FastDataChangeCallback = (sub, item, value) =>
                            {
                                foreach (var dv in item.MonitoredItems)
                                {
                                    if (!StatusCode.IsGood(dv.DiagnosticInfo.InnerStatusCode))
                                    {
                                        Assert.Fail("Monitored item reported bad status code: "
                                            + dv.DiagnosticInfo.InnerStatusCode
                                            + dv.DiagnosticInfo.LocalizedText);
                                    }

                                    valueChanges.AddOrUpdate(
                                        clientHandles[dv.ClientHandle],
                                        1,
                                        (key, count) => count + 1);
                                }
                            };
                        }

                        session.AddSubscription(subscription);
                        await subscription.CreateAsync().ConfigureAwait(false);
                        subscriptions.Add(subscription);
                    }
                }

                // Create writer session
                var writerSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                sessions.Add(writerSession);

                // Writer task
                int writeCount = 0;
                var writerCts = new CancellationTokenSource();
                var writerTask = Task.Run(async () =>
                {
                    while (!writerCts.IsCancellationRequested)
                    {
                        writeCount++;
                        var nodesToWrite = new WriteValueCollection();
                        foreach (KeyValuePair<NodeId, Type> node in nodeIds)
                        {
                            nodesToWrite.Add(new WriteValue
                            {
                                NodeId = node.Key,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(
                                    new Variant(
                                        Convert.ChangeType(writeCount, node.Value, CultureInfo.InvariantCulture)
                                    )
                                )
                            });
                        }
                        try
                        {
                            await writerSession.WriteAsync(null, nodesToWrite, writerCts.Token).ConfigureAwait(false);
                        }
                        catch (ServiceResultException sre)
                        {
                            TestContext.Out.WriteLine($"Writer session write error: {sre.Message}");
                        }
                        await Task.Delay(writerInterval, writerCts.Token).ConfigureAwait(false);
                    }
                }, writerCts.Token);

                // Run test for a certain duration
                await Task.Delay(TimeSpan.FromSeconds(testDurationSeconds)).ConfigureAwait(false);

                // Stop writer
                writerCts.Cancel();
                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* expected */ }

                // Wait for notifications to be processed
                await Task.Delay(publishingInterval * 10).ConfigureAwait(false);

                // Verification
                TestContext.Out.WriteLine($"Writer task wrote {writeCount} times.");
                int totalNotifications = sessionCount * subscriptionsPerSession;
                int expectedNotifications = (writeCount + 1) * totalNotifications;
                int receivedNotifications = valueChanges.Values.Sum();

                TestContext.Out.WriteLine($"Expected notifications multiplier per node: {totalNotifications}");
                TestContext.Out.WriteLine($"Total expected notifications: {expectedNotifications}");
                TestContext.Out.WriteLine($"Total received notifications: {receivedNotifications}");

                bool allNodesReceivedChanges = true;
                foreach (NodeId nodeId in nodeIds.Keys)
                {
                    if (valueChanges.TryGetValue(nodeId, out int changes))
                    {
                        TestContext.Out.WriteLine($"Node {nodeId} received {changes} changes.");
                        if (changes < writeCount)
                        {
                            allNodesReceivedChanges = false;
                            TestContext.Out.WriteLine($"!!! Node {nodeId} received only {changes} of {writeCount} changes.");
                        }
                    }
                    else
                    {
                        allNodesReceivedChanges = false;
                        TestContext.Out.WriteLine($"!!! Node {nodeId} received no changes.");
                    }
                }

                Assert.IsTrue(allNodesReceivedChanges, "Not all nodes received all value changes.");
                // Looser verification to allow for network delays
                double receiveRatio = (double)receivedNotifications / (expectedNotifications * nodeIds.Count);
                TestContext.Out.WriteLine($"Receive ratio: {receiveRatio:P2}");
                Assert.Greater(receiveRatio, 0.99, "The overall notification receive ratio is too low.");
            }
            finally
            {
                // Cleanup
                foreach (var session in sessions)
                {
                    try
                    {
                        await session.CloseAsync().ConfigureAwait(false);
                        session.Dispose();
                    }
                    catch (Exception ex)
                    {
                        TestContext.Out.WriteLine($"Failed to close session: {ex.Message}");
                    }
                }
            }
        }
    }
}
