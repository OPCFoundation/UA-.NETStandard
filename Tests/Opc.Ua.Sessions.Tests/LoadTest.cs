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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using ISession = Opc.Ua.Client.ISession;
using MonitoredItem = Opc.Ua.Client.MonitoredItem;
using Subscription = Opc.Ua.Client.Subscription;

namespace Opc.Ua.Sessions.Tests
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
            : base(uriScheme, NUnitTelemetryContext.Create(logLevel: LogLevel.Warning))
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

            // The many-sessions load test runs a selectable session count (500 by
            // default, up to 10000 for the largest stress case). Size the shared
            // fixture for the largest case: one secure channel and one subscription
            // per session plus head-room for the extra writer session. These are
            // caps, not pre-allocations, so a small case pays nothing for the head-room.
            MaxChannelCount = 10100;
            MaxSessionCount = 10100;
            MaxSubscriptionCount = 10100;

            // Each session keeps one long-polled Publish request outstanding. With
            // ServerConfiguration.DecoupleHeldPublishRequests on (the default), a held
            // Publish releases its request-processing worker at the park point instead of
            // occupying it for the whole wait, so the worker pool no longer has to scale
            // with the session count - size it to the active (non-parked) establishment
            // concurrency instead. Measurement on a 6-core box showed a 200-worker pool
            // cleanly established and served ~4000 concurrent sessions, whereas a pool
            // sized to the session count (e.g. 10500) was materially slower to establish
            // and reached a lower ceiling (thread oversubscription). Workers grow on
            // demand from the warm minimum up to the maximum.
            MaxRequestThreadCount = 200;
            MinRequestThreadCount = 50;

            // Disable the brute-force authentication lockout for this fixture. All
            // sessions share a single client certificate, so once a handful of
            // handshakes fail transiently under load the whole certificate would be
            // locked out and every remaining session would be rejected with
            // BadUserAccessDenied. This is a scaling test, not an auth test.
            MaxFailedAuthenticationAttempts = 0;
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
        /// When <paramref name="useManagedSession"/> is <c>true</c>, each reader session
        /// is created as a <see cref="ManagedSession"/> with the V2 (ChannelBased)
        /// <see cref="DefaultSubscriptionEngineFactory"/>.
        /// </summary>
        [Test]
        [Explicit]
        [Order(100)]
        [TestCase(false, TestName = "ServerSubscribeLoadTest_Classic")]
        [TestCase(true, TestName = "ServerSubscribeLoadTest_ManagedSessionV2")]
        public async Task ServerSubscribeLoadTestAsync(bool useManagedSession)
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

            // Resolve the endpoint once so ManagedSession creation does not
            // need to do endpoint discovery on every parallel task.
            ConfiguredEndpoint configuredEndpoint = useManagedSession
                ? await ClientFixture.GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false)
                : null;

            TestContext.Out.WriteLine(
                useManagedSession
                    ? "Using ManagedSession with SubscriptionEngine V2 (ChannelBased)."
                    : "Using classic Session with default SubscriptionEngine.");

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
                        ISession session;
                        if (useManagedSession)
                        {
                            ManagedSession managedSession = await ManagedSession.CreateAsync(
                                ClientFixture.Config,
                                configuredEndpoint,
                                new DefaultSessionFactory(Telemetry),
                                engineFactory: DefaultSubscriptionEngineFactory.Instance,
                                ct: connectCts.Token).ConfigureAwait(false);
                            session = managedSession;
                            sessions.Add(session);

                            var handler = new LoadTestDataChangeHandler(valueChanges, clientHandles);

                            for (int j = 0; j < subscriptionsPerSession; j++)
                            {
                                Client.Subscriptions.ISubscription subscription = managedSession.AddSubscription(
                                    handler,
                                    o => o with
                                    {
                                        PublishingInterval = TimeSpan.FromMilliseconds(publishingInterval),
                                        PublishingEnabled = true
                                    });

                                foreach (NodeId nodeId in nodeIds.Keys)
                                {
                                    valueChanges.TryAdd(nodeId, 0);
                                    subscription.TryAddMonitoredItem(
                                        nodeId.ToString(),
                                        nodeId,
                                        o => o with
                                        {
                                            AttributeId = Attributes.Value,
                                            MonitoringMode = MonitoringMode.Reporting,
                                            SamplingInterval = TimeSpan.Zero,
                                            QueueSize = 1
                                        },
                                        out Client.Subscriptions.MonitoredItems.IMonitoredItem? item);

                                    if (item != null)
                                    {
                                        clientHandles.TryAdd(item.ClientHandle, nodeId);
                                    }
                                }
                            }
                        }
                        else
                        {
                            session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
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
                        }
                    }, connectCts.Token));
                }
                await Task.WhenAll(createSessionTasks).ConfigureAwait(false);

                // Create writer session (always uses classic session)
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
                await writerCts.CancelAsync().ConfigureAwait(false);
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
                await Task.Delay((testDurationSeconds / writeCount) + (publishingInterval * 50)).ConfigureAwait(false);

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
                    var e = new BaseEventState(null);
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

                Assert.That(
                    received,
                    Is.GreaterThan(0),
                    "No event notifications were received.");

                Assert.That(
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

        /// <summary>
        /// Load test the server with a selectable number of concurrent sessions,
        /// each holding a single slow-publishing subscription that monitors a value.
        /// This exercises the per-session and per-subscription scaling limits and is
        /// the basis for the session-scalability notes in <c>Docs/Benchmarks.md</c>.
        /// 500 is the supported baseline; the higher cases (up to 10000) are stress
        /// cases that push steady-state publish delivery until it starts dropping
        /// sessions. Because the test is <c>[Explicit]</c>, the case to run is
        /// selected by name (e.g.
        /// <c>ServerManySessionsLoadTestAsync(2000)</c>).
        /// Connecting the sessions uses its own deadline that is independent of the
        /// steady-state duration.
        /// </summary>
        /// <param name="sessionCount">The number of concurrent sessions to establish.</param>
        [Test]
        [Explicit]
        [Order(130)]
        [TestCase(500)]
        [TestCase(1000)]
        [TestCase(1500)]
        [TestCase(2000)]
        [TestCase(2500)]
        [TestCase(4000)]
        [TestCase(5000)]
        [TestCase(8000)]
        [TestCase(10000)]
        public async Task ServerManySessionsLoadTestAsync(int sessionCount)
        {
            const int testDurationSeconds = 60;
            const int publishingInterval = 1000; // slow publishing subscription.
            const int writerInterval = 500;
            const int maxConnectAttempts = 5;

            // Each secure-channel + ActivateSession handshake is CPU bound (RSA)
            // and the sessions share a single client certificate. Keep the connect
            // concurrency low: a large simultaneous-connect burst both oversubscribes
            // the CPU and, because every handshake signs with the same shared
            // certificate, can race the certificate's private-key handle under load
            // (surfacing as BadTcpInternalError / "invalid handle"). A gentle connect
            // avoids both.
            int maxConnectConcurrency = Math.Max(2, Environment.ProcessorCount / 4);

            // Establishing the sessions has its own generous deadline that is
            // independent of the steady-state duration, so a slow connect phase can
            // never eat into - or cancel - the measurement window the way a single
            // shared deadline did. Scale it with the session count so the larger
            // stress case has enough head-room to establish every session.
            int connectTimeoutSeconds = Math.Max(500, sessionCount * 3);
            using var connectCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(connectTimeoutSeconds));

            var sessions = new ConcurrentBag<ISession>();
            var notificationsPerSession = new ConcurrentDictionary<int, int>();
            var createErrors = new ConcurrentBag<string>();

            try
            {
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                // Use a single shared node so every one of the sessions observes the same
                // value changes; this keeps the test focused on session/subscription scaling.
                KeyValuePair<NodeId, Type> monitoredNode = nodeIds.First();
                NodeId monitoredNodeId = monitoredNode.Key;
                Type monitoredNodeType = monitoredNode.Value;

                TestContext.Out.WriteLine(
                    $"Creating {sessionCount} sessions, each with one slow ({publishingInterval} ms) subscription.");

                // Resolve the endpoint once so each session does not repeat endpoint
                // discovery (GetEndpoints) on connect; doing discovery per session is a
                // measurable connect-time bottleneck at this scale.
                ConfiguredEndpoint configuredEndpoint = await ClientFixture
                    .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);

                var sessionFactory = new DefaultSessionFactory(Telemetry);

                // Throttle the concurrent secure-channel handshakes so the RSA
                // handshakes do not oversubscribe the CPU and self-inflict a
                // connect storm.
                using var connectThrottle = new SemaphoreSlim(maxConnectConcurrency);
                var createSessionTasks = new List<Task>();
                var swConnect = System.Diagnostics.Stopwatch.StartNew();
                CancellationToken connectToken = connectCts.Token;
                for (int i = 0; i < sessionCount; i++)
                {
                    int sessionIndex = i;
                    createSessionTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await connectThrottle.WaitAsync(connectToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Connect deadline reached before this session started.
                            return;
                        }
                        try
                        {
                            // Secure-channel handshakes can transiently time out under
                            // load; retry a bounded number of times so the full session
                            // count can be established on capable hardware. Cancellation
                            // is handled locally so it can never fault Task.WhenAll.
                            for (int attempt = 1; attempt <= maxConnectAttempts; attempt++)
                            {
                                if (connectToken.IsCancellationRequested)
                                {
                                    return;
                                }

                                ManagedSession session = null;
                                try
                                {
                                    session = await ManagedSession.CreateAsync(
                                        ClientFixture.Config,
                                        configuredEndpoint,
                                        sessionFactory,
                                        engineFactory: DefaultSubscriptionEngineFactory.Instance,
                                        ct: connectToken)
                                        .ConfigureAwait(false);

                                    var handler = new ManySessionsNotificationHandler(
                                        sessionIndex,
                                        notificationsPerSession);

                                    Client.Subscriptions.ISubscription subscription = session.AddSubscription(
                                        handler,
                                        o => o with
                                        {
                                            PublishingInterval = TimeSpan.FromMilliseconds(publishingInterval),
                                            PublishingEnabled = true
                                        });

                                    subscription.TryAddMonitoredItem(
                                        monitoredNodeId.ToString(),
                                        monitoredNodeId,
                                        o => o with
                                        {
                                            AttributeId = Attributes.Value,
                                            MonitoringMode = MonitoringMode.Reporting,
                                            SamplingInterval = TimeSpan.Zero,
                                            QueueSize = 1
                                        },
                                        out _);

                                    sessions.Add(session);
                                    break;
                                }
                                catch (OperationCanceledException)
                                {
                                    // Connect deadline reached; stop retrying.
                                    session?.Dispose();
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    session?.Dispose();
                                    if (attempt >= maxConnectAttempts)
                                    {
                                        createErrors.Add(ex.Message);
                                        return;
                                    }

                                    // Bounded backoff before the next attempt. Swallow
                                    // cancellation so it never escapes to Task.WhenAll.
                                    try
                                    {
                                        await Task.Delay(attempt * 250, connectToken)
                                            .ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            connectThrottle.Release();
                        }
                    }));
                }
                await Task.WhenAll(createSessionTasks).ConfigureAwait(false);
                swConnect.Stop();

                TestContext.Out.WriteLine(
                    $"Established {sessions.Count} sessions in {swConnect.ElapsedMilliseconds} ms " +
                    $"({sessions.Count / Math.Max(swConnect.Elapsed.TotalSeconds, 0.001):F0} sessions/sec).");

                // Opt-in machine-readable capture of the establishment metric. Written the
                // moment it is measured so a flaky teardown cannot swallow the result.
                AppendBenchmarkResult(
                    $"ESTABLISH sessionCount={sessionCount} established={sessions.Count} " +
                    $"perSec={sessions.Count / Math.Max(swConnect.Elapsed.TotalSeconds, 0.001):F0} " +
                    $"ms={swConnect.ElapsedMilliseconds} errors={createErrors.Count} " +
                    $"firstError=[{(createErrors.IsEmpty ? "none" : createErrors.First().Replace('\r', ' ').Replace('\n', ' '))}]");

                if (!createErrors.IsEmpty)
                {
                    foreach (string error in createErrors.Take(10))
                    {
                        TestContext.Out.WriteLine($"Session create error: {error}");
                    }
                }

                Assert.That(
                    sessions,
                    Has.Count.EqualTo(sessionCount),
                    $"Not all {sessionCount} sessions could be created (errors: {createErrors.Count}).");

                // Writer session changes the monitored value periodically.
                ISession writerSession = await ClientFixture
                    .ConnectAsync(configuredEndpoint)
                    .ConfigureAwait(false);
                sessions.Add(writerSession);

                short writeCount = 0;
                using var writerCts = new CancellationTokenSource();
                var writerTask = Task.Run(async () =>
                {
                    while (!writerCts.IsCancellationRequested)
                    {
                        writeCount++;
                        var nodesToWrite = new List<WriteValue>
                        {
#pragma warning disable CS0618 // Type or member is obsolete
                            new WriteValue
                            {
                                NodeId = monitoredNodeId,
                                AttributeId = Attributes.Value,
                                Value = new DataValue(
                                    new Variant(
                                        Convert.ChangeType(
                                            writeCount,
                                            monitoredNodeType,
                                            CultureInfo.InvariantCulture)))
                            }
#pragma warning restore CS0618 // Type or member is obsolete
                        };
                        try
                        {
                            await writerSession.WriteAsync(null, nodesToWrite, writerCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (ServiceResultException sre)
                        {
                            TestContext.Out.WriteLine($"Writer session write error: {sre.Message}");
                        }

                        try
                        {
                            await Task.Delay(writerInterval, writerCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }, writerCts.Token);

                // Run the steady-state load for the configured duration. Because
                // connecting the sessions used its own separate deadline, the
                // measurement window is always the full duration regardless of how
                // long establishing the sessions took.
                await Task.Delay(TimeSpan.FromSeconds(testDurationSeconds)).ConfigureAwait(false);

                // Stop the writer.
                await writerCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    /* expected */
                }

                // Allow the slow subscriptions enough publishing cycles to deliver the last writes.
                await Task.Delay(publishingInterval * 5).ConfigureAwait(false);

                int sessionsWithNotifications = notificationsPerSession.Count;
                long totalNotifications = notificationsPerSession.Values.Sum(v => (long)v);

                TestContext.Out.WriteLine($"Writer performed {writeCount} writes.");
                TestContext.Out.WriteLine(
                    $"{sessionsWithNotifications}/{sessionCount} sessions received notifications " +
                    $"(total {totalNotifications}).");

                AppendBenchmarkResult(
                    $"NOTIFY sessionCount={sessionCount} withNotifications={sessionsWithNotifications} " +
                    $"total={totalNotifications} writes={writeCount}");

                Assert.That(
                    sessionsWithNotifications,
                    Is.EqualTo(sessionCount),
                    "Some sessions did not receive any notifications under load.");
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

        /// <summary>
        /// Appends a machine-readable benchmark result line to the file named by the
        /// <c>BENCH_RESULT_FILE</c> environment variable, if set. This is a no-op for
        /// normal test runs (the variable is unset) and lets a load-test harness capture
        /// the establishment / notification metrics even when a flaky teardown would
        /// otherwise prevent the run from completing.
        /// </summary>
        private static void AppendBenchmarkResult(string line)
        {
            string path = Environment.GetEnvironmentVariable("BENCH_RESULT_FILE");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                System.IO.File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (System.IO.IOException)
            {
                // Best-effort capture only; never fail the test on a logging error.
            }
        }

        /// <summary>
        /// Notification handler for the V2 SubscriptionManager path in load tests.
        /// Routes each <see cref="DataValueChange"/> back to the shared
        /// <paramref name="valueChanges"/> counter dict via <paramref name="clientHandles"/>.
        /// </summary>
        private sealed class LoadTestDataChangeHandler : Client.Subscriptions.ISubscriptionNotificationHandler
        {
            private readonly ConcurrentDictionary<NodeId, int> m_valueChanges;
            private readonly ConcurrentDictionary<uint, NodeId> m_clientHandles;

            public LoadTestDataChangeHandler(
                ConcurrentDictionary<NodeId, int> valueChanges,
                ConcurrentDictionary<uint, NodeId> clientHandles)
            {
                m_valueChanges = valueChanges;
                m_clientHandles = clientHandles;
            }

            public ValueTask OnDataChangeNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Client.Subscriptions.DataValueChange> notification,
                Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<Client.Subscriptions.DataValueChange> changes = notification.Span;
                for (int i = 0; i < changes.Length; i++)
                {
                    Client.Subscriptions.DataValueChange change = changes[i];
                    if (change.DiagnosticInfo != null &&
                        !StatusCode.IsGood(change.DiagnosticInfo.InnerStatusCode))
                    {
                        Assert.Fail(
                            "Monitored item reported bad status code: " +
                            change.DiagnosticInfo.InnerStatusCode +
                            change.DiagnosticInfo.LocalizedText);
                    }

                    if (change.MonitoredItem != null &&
                        m_clientHandles.TryGetValue(change.MonitoredItem.ClientHandle, out NodeId nodeId))
                    {
                        m_valueChanges.AddOrUpdate(nodeId, 1, (_, count) => count + 1);
                    }
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Client.Subscriptions.EventNotification> notification,
                Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                Client.Subscriptions.ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                Client.Subscriptions.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }
        }

        /// <summary>
        /// Per-session notification handler for the many-sessions load test.
        /// Counts the data-value changes delivered to a single session so the
        /// test can assert every session receives notifications under load.
        /// </summary>
        private sealed class ManySessionsNotificationHandler : Client.Subscriptions.ISubscriptionNotificationHandler
        {
            private readonly int m_sessionIndex;
            private readonly ConcurrentDictionary<int, int> m_notificationsPerSession;

            public ManySessionsNotificationHandler(
                int sessionIndex,
                ConcurrentDictionary<int, int> notificationsPerSession)
            {
                m_sessionIndex = sessionIndex;
                m_notificationsPerSession = notificationsPerSession;
            }

            public ValueTask OnDataChangeNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Client.Subscriptions.DataValueChange> notification,
                Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                int count = notification.Length;
                if (count > 0)
                {
                    m_notificationsPerSession.AddOrUpdate(
                        m_sessionIndex,
                        count,
                        (_, existing) => existing + count);
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Client.Subscriptions.EventNotification> notification,
                Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                Client.Subscriptions.ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                Client.Subscriptions.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
