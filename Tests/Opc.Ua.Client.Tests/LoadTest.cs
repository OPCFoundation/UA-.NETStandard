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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
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
        public LoadTest(string uriScheme)
            : base(uriScheme)
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
            UseSamplingGroupsInReferenceNodeManager = false;
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
        public async Task ServerLoadTestAsync()
        {
            const int sessionCount = 50;
            const int subscriptionsPerSession = 15;
            const int publishingInterval = 100;
            const int writerInterval = 150;
            const int testDurationSeconds = 60;

            var sessions = new ConcurrentBag<ISession>();
            var subscriptions = new ConcurrentBag<Subscription>();
            var valueChanges = new ConcurrentDictionary<NodeId, int>();
            var monitoredItems = new ConcurrentDictionary<uint, MonitoredItem>();
            var clientHandles = new ConcurrentDictionary<uint, NodeId>();
            var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(testDurationSeconds));

            try
            {
                // Get nodes for subscription
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    NUnit.Framework.Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                TestContext.Out.WriteLine($"Subscribing to {nodeIds.Count} nodes.");

                // Create reader sessions and subscriptions in parallel
                var createSessionTasks = new List<Task>();
                for (int i = 0; i < sessionCount; i++)
                {
                    createSessionTasks.Add(Task.Run(async () =>
                    {
                        ISession session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);

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
                                    SamplingInterval = 0
                                };
                                valueChanges.TryAdd(nodeId, 0);
                                clientHandles.TryAdd(item.ClientHandle, nodeId);

                                monitoredItems.TryAdd(item.ClientHandle, item);
                                subscription.AddItem(item);
                            }

                            subscription.FastDataChangeCallback = (sub, item, value) =>
                            {
                                foreach (MonitoredItemNotification dv in item.MonitoredItems)
                                {
                                    if (!StatusCode.IsGood(dv.DiagnosticInfo.InnerStatusCode))
                                    {
                                        NUnit.Framework.Assert.Fail(
                                            "Monitored item reported bad status code: " +
                                            dv.DiagnosticInfo.InnerStatusCode +
                                            dv.DiagnosticInfo.LocalizedText);
                                    }

                                    valueChanges.AddOrUpdate(
                                        clientHandles[dv.ClientHandle],
                                        1,
                                        (key, count) => count + 1);
                                }
                            };

                            session.AddSubscription(subscription);
                            await subscription.CreateAsync().ConfigureAwait(false);
                            subscriptions.Add(subscription);
                        }
                    }, connectCts.Token));
                }
                await Task.WhenAll(createSessionTasks).ConfigureAwait(false);

                // Create writer session
                ISession writerSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                sessions.Add(writerSession);

                // Writer task
                short writeCount = 0;
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
                catch (OperationCanceledException)
                {
                    /* expected */
                }

                // Wait for server to process last write (testDurationSeconds / writeCount -> time for a single write)
                // + some publishing intervals for notifications to be processed
                await Task.Delay((testDurationSeconds / writeCount) + (publishingInterval * 10)).ConfigureAwait(false);

                // Verification
                TestContext.Out.WriteLine($"Writer task wrote {writeCount} times.");
                const int totalNotifications = sessionCount * subscriptionsPerSession;
                int expectedNotifications = (writeCount + 1) * totalNotifications * nodeIds.Count;
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
                double receiveRatio = (double)receivedNotifications / expectedNotifications;
                TestContext.Out.WriteLine($"Receive ratio: {receiveRatio:P2}");
                Assert.Greater(receiveRatio, 0.99, "The overall notification receive ratio is too low.");
            }
            finally
            {
                // Cleanup
                var closeTasks = sessions.Select(session => Task.Run(async () =>
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
                })).ToList();
                await Task.WhenAll(closeTasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Load test a server with multiple sessions reading values.
        /// </summary>
        [Test]
        [Explicit]
        [Order(110)]
        public async Task ServerReadLoadTestAsync()
        {
            const int sessionCount = 50;
            const int readInterval = 10;
            const int writerInterval = 15;
            const int testDurationSeconds = 60;

            var sessions = new ConcurrentBag<ISession>();
            var readErrors = new ConcurrentBag<string>();
            long totalReads = 0;
            long totalWrites = 0;

            using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(testDurationSeconds));
            try
            {
                // Get nodes for simulation
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    NUnit.Framework.Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                TestContext.Out.WriteLine($"Reading from {nodeIds.Count} nodes.");

                var nodesToRead = new ReadValueIdCollection();
                foreach (NodeId nodeId in nodeIds.Keys)
                {
                    nodesToRead.Add(new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value });
                }

                // Create reader sessions
                var readerTasks = new List<Task>();
                for (int i = 0; i < sessionCount; i++)
                {
                    readerTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            ISession session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                            sessions.Add(session);

                            while (!testCts.IsCancellationRequested)
                            {
                                try
                                {
                                    ReadResponse response = await session.ReadAsync(
                                        null,
                                        0,
                                        TimestampsToReturn.Both,
                                        nodesToRead,
                                        testCts.Token).ConfigureAwait(false);

                                    Interlocked.Add(ref totalReads, response.Results.Count);

                                    foreach (DataValue result in response.Results)
                                    {
                                        if (StatusCode.IsBad(result.StatusCode))
                                        {
                                            if (result.StatusCode == StatusCodes.BadRequestInterrupted)
                                            {
                                                continue;
                                            }

                                            readErrors.Add($"Bad status: {result.StatusCode}");
                                        }
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                }
                                catch (Exception ex)
                                {
                                    if (ex is ServiceResultException sre && sre.StatusCode == StatusCodes.BadRequestInterrupted)
                                    {
                                        continue;
                                    }

                                    readErrors.Add($"Read error: {ex.Message}");
                                }

                                try
                                {
                                    await Task.Delay(readInterval, testCts.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException)
                                {
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            TestContext.Out.WriteLine($"Session error: {ex.Message}");
                        }
                    }, testCts.Token));
                }

                // Create writer session
                ISession writerSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                sessions.Add(writerSession);

                // Writer task
                var writerTask = Task.Run(async () =>
                {
                    short writeCount = 0;
                    while (!testCts.IsCancellationRequested)
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
                            await writerSession.WriteAsync(null, nodesToWrite, testCts.Token).ConfigureAwait(false);
                            Interlocked.Increment(ref totalWrites);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (ServiceResultException sre)
                        {
                            if (sre.StatusCode != StatusCodes.BadRequestInterrupted)
                            {
                                TestContext.Out.WriteLine($"Writer session write error: {sre.Message}");
                            }
                        }

                        try
                        {
                            await Task.Delay(writerInterval, testCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }, testCts.Token);

                // Wait for test duration
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(testDurationSeconds + 2), testCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Test time is up
                }

                // Wait for tasks to complete
                try
                {
                    await Task.WhenAll(readerTasks.Concat([writerTask])).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }

                TestContext.Out.WriteLine($"Total reads: {totalReads}");
                TestContext.Out.WriteLine($"Total writes: {totalWrites}");
                TestContext.Out.WriteLine($"Total read errors: {readErrors.Count}");

                if (!readErrors.IsEmpty)
                {
                    foreach (string error in readErrors.Take(10))
                    {
                        TestContext.Out.WriteLine(error);
                    }
                }

                NUnit.Framework.Assert.That(readErrors.Count, Is.EqualTo(0), "There were read errors.");
                NUnit.Framework.Assert.That(totalReads, Is.GreaterThan(0), "No reads were performed.");
                NUnit.Framework.Assert.That(totalWrites, Is.GreaterThan(0), "No writes were performed.");
            }
            finally
            {
                // Cleanup
                var closeTasks = sessions.Select(session => Task.Run(async () =>
                {
                    try
                    {
                        if (session.Connected)
                        {
                            await session.CloseAsync().ConfigureAwait(false);
                        }
                        session.Dispose();
                    }
                    catch (Exception ex)
                    {
                        TestContext.Out.WriteLine($"Failed to close session: {ex.Message}");
                    }
                })).ToList();
                await Task.WhenAll(closeTasks).ConfigureAwait(false);
            }
        }
    }
}
