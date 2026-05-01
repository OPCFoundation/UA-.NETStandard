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
using Opc.Ua.Server;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Server load tests.
    /// </summary>
    [TestFixture]
    [Explicit]
    [Category("LoadTest")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [TestFixtureSource(nameof(FixtureArgs))]
    [Parallelizable(ParallelScope.Fixtures)]
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
        public async Task ServerSubscribeLoadTestAsync()
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
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(testDurationSeconds));

            try
            {
                // Get nodes for subscription
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    Assert.Ignore("No nodes for simulation found, ignoring test.");
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

                            try
                            {
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
                                            Assert.Fail(
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
                            catch
                            {
                                subscription.Dispose();
                                throw;
                            }
                        }
                    }, connectCts.Token));
                }
                await Task.WhenAll(createSessionTasks).ConfigureAwait(false);

                // Create writer session
                ISession writerSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                sessions.Add(writerSession);

                // Writer task
                short writeCount = 0;
                using var writerCts = new CancellationTokenSource();
                var writerTask = Task.Run(async () =>
                {
                    while (!writerCts.IsCancellationRequested)
                    {
                        writeCount++;
                        var nodesToWrite = new List<WriteValue>();
                        foreach (KeyValuePair<NodeId, Type> node in nodeIds)
                        {
#pragma warning disable CS0618 // Type or member is obsolete
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
#pragma warning restore CS0618 // Type or member is obsolete
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

                Assert.That(allNodesReceivedChanges, Is.True, "Not all nodes received all value changes.");
                // Allow for network delays
                double receiveRatio = (double)receivedNotifications / expectedNotifications;
                TestContext.Out.WriteLine($"Receive ratio: {receiveRatio:P2}");
                Assert.That(receiveRatio, Is.GreaterThan(0.99), "The overall notification receive ratio is too low.");
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
                    Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                TestContext.Out.WriteLine($"Reading from {nodeIds.Count} nodes.");

                var nodesToRead = new List<ReadValueId>();
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
                        var nodesToWrite = new List<WriteValue>();
                        foreach (KeyValuePair<NodeId, Type> node in nodeIds)
                        {
#pragma warning disable CS0618 // Type or member is obsolete
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
#pragma warning restore CS0618 // Type or member is obsolete
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

                Assert.That(readErrors, Is.Empty, "There were read errors.");
                Assert.That(totalReads, Is.GreaterThan(0), "No reads were performed.");
                Assert.That(totalWrites, Is.GreaterThan(0), "No writes were performed.");
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

        /// <summary>
        /// Load test a server with multiple sessions subscribing to events.
        /// Verifies that event handling remains responsive under load with many concurrent
        /// event subscriptions, addressing the performance issue where server becomes
        /// unresponsive as the number of monitored items increases.
        /// </summary>
        [Test]
        [Explicit]
        [Order(120)]
        public async Task ServerEventSubscribeLoadTestAsync()
        {
            const int sessionCount = 50;
            const int subscriptionsPerSession = 15;
            const int publishingInterval = 100;
            const int testDurationSeconds = 30;

            var sessions = new ConcurrentBag<ISession>();
            long eventsReceived = 0;
            long totalDelayTicks = 0;

            try
            {
                TestContext.Out.WriteLine($"Creating {sessionCount} sessions with {subscriptionsPerSession} event subscriptions each.");

                // Create sessions with event subscriptions in parallel.
                var createSessionTasks = new List<Task>();
                for (int i = 0; i < sessionCount; i++)
                {
                    createSessionTasks.Add(Task.Run(async () =>
                    {
                        ISession session = await ClientFixture.ConnectAsync(
                            ServerUrl,
                            SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                        sessions.Add(session);

                        for (int j = 0; j < subscriptionsPerSession; j++)
                        {
                            var subscription = new Subscription(session.DefaultSubscription)
                            {
                                PublishingInterval = publishingInterval
                            };

                            try
                            {
                                // Build an event filter for the base event type.
                                var eventFilter = new EventFilter();
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.EventId));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.EventType));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.SourceNode));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.SourceName));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.Time));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.Message));
                                eventFilter.AddSelectClause(
                                    ObjectTypeIds.BaseEventType,
                                    QualifiedName.From(BrowseNames.Severity));

                                // Monitor the Server node for events.
                                var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                                {
                                    StartNodeId = ObjectIds.Server,
                                    AttributeId = Attributes.EventNotifier,
                                    MonitoringMode = MonitoringMode.Reporting,
                                    Filter = eventFilter,
                                    QueueSize = 10000
                                };

                                subscription.FastEventCallback = (sub, notification, _) =>
                                {
                                    Interlocked.Add(ref eventsReceived, notification.Events.Count);
                                    foreach (EventFieldList fieldList in notification.Events)
                                    {
                                        if (fieldList.EventFields.Count > 4 && fieldList.EventFields[4].TryGetValue(out DateTimeUtc eventTime))
                                        {
                                            TimeSpan delay = DateTime.UtcNow - ((DateTime)eventTime).ToUniversalTime();
                                            Interlocked.Add(ref totalDelayTicks, delay.Ticks);
                                        }
                                    }
                                };

                                subscription.AddItem(monitoredItem);
                                session.AddSubscription(subscription);
                                await subscription.CreateAsync().ConfigureAwait(false);
                            }
                            catch
                            {
                                subscription.Dispose();
                                throw;
                            }
                        }
                    }));
                }

                await Task.WhenAll(createSessionTasks).ConfigureAwait(false);

                const int totalSubscriptions = sessionCount * subscriptionsPerSession;
                TestContext.Out.WriteLine(
                    $"Created {totalSubscriptions} event subscriptions across {sessionCount} sessions.");
                TestContext.Out.WriteLine($"Generating events on the server for {testDurationSeconds} seconds...");

                // Generate events directly on the server to stress-test event delivery.
                IServerInternal serverInternal = ReferenceServer.CurrentInstance;
                ISystemContext serverContext = serverInternal.DefaultSystemContext;
                int eventCount = 0;

                using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(testDurationSeconds));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!testCts.IsCancellationRequested)
                {
                    using var e = new BaseEventState(null);
                    e.Initialize(
                        serverContext,
                        serverInternal.ServerObject,
                        EventSeverity.Medium,
                        new LocalizedText($"LoadTest event {eventCount}"));
                    serverInternal.ReportEvent(serverContext, e);
                    eventCount++;
                }

                sw.Stop();
                TestContext.Out.WriteLine($"Generated {eventCount} events in {sw.ElapsedMilliseconds} ms " +
                    $"({eventCount / sw.Elapsed.TotalSeconds:F0} events/sec).");

                // Wait for subscriptions to deliver the events.
                // Allow enough publishing intervals for all notifications to be sent and acknowledged.
                const int publishingIntervalsToWait = 20;
                await Task.Delay(publishingInterval * publishingIntervalsToWait).ConfigureAwait(false);

                long expectedTotal = (long)eventCount * totalSubscriptions;
                long received = Interlocked.Read(ref eventsReceived);

                TestContext.Out.WriteLine($"Expected event notifications : {expectedTotal}");
                TestContext.Out.WriteLine($"Received event notifications : {received}");

                double receiveRatio = expectedTotal > 0 ? (double)received / expectedTotal : 0;
                TestContext.Out.WriteLine($"Receive ratio: {receiveRatio:P2}");

                if (received > 0)
                {
                    long averageDelayTicks = Interlocked.Read(ref totalDelayTicks) / received;
                    var averageDelay = TimeSpan.FromTicks(averageDelayTicks);
                    TestContext.Out.WriteLine($"Average event delivery delay: {averageDelay.TotalMilliseconds:F2} ms");
                }

                NUnit.Framework.Assert.That(
                    received,
                    Is.GreaterThan(0),
                    "No event notifications were received.");

                NUnit.Framework.Assert.That(
                    receiveRatio,
                    Is.GreaterThan(0.99),
                    "The event notification receive ratio is too low.");
            }
            finally
            {
                // Cleanup all sessions.
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
