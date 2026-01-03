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
    /// Long-running connection stability test.
    /// </summary>
    [TestFixture]
    [Category("ConnectionStability")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ConnectionStabilityTest : ClientTestFramework
    {
        private const int SecurityTokenLifetimeCIMs = 5 * 60 * 1000; // 5 minutes for CI
        private const int SecurityTokenLifetimeLocalMs = 10 * 1000; // 10 seconds for local testing
        private const int StatusReportIntervalSeconds = 60; // Report status every 60 seconds
        private const double NotificationToleranceRatio = 0.95; // Accept 95% of expected notifications (5% tolerance)

        public ConnectionStabilityTest()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        /// <summary>
        /// Set up a Server and a Client instance.
        /// </summary>
        [OneTimeSetUp]
        public override async Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;

            // Check if running in CI environment
            bool isCI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

            // Configure security token lifetime based on environment
            // CI: 5 minutes to force 18 renewals in 90 minute test
            // Local: 10 seconds to force 6 renewals in 1 minute test
            int tokenLifetime = isCI ? SecurityTokenLifetimeCIMs : SecurityTokenLifetimeLocalMs;

            SecurityTokenLifetime = tokenLifetime;

            await base.OneTimeSetUpAsync().ConfigureAwait(false);
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
        /// Long-running test that verifies connection stability over a configurable duration.
        /// Tests that:
        /// - Connection remains stable over extended period
        /// - Subscriptions deliver all expected values (no message loss)
        /// - Security token renewals happen correctly (every 5 minutes in CI, every 10 seconds locally)
        /// Duration can be configured via TEST_DURATION_MINUTES environment variable (default: 90 minutes CI, 1 minute local)
        /// </summary>
        [Test]
        [Order(100)]
        public async Task LongRunningStabilityTestAsync()
        {
            // Get test duration from environment variable or use default
            int testDurationMinutes = GetTestDurationMinutes();
            int testDurationSeconds = testDurationMinutes * 60;

            // Determine token lifetime based on environment
            bool isCI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
            int tokenLifetimeMs = isCI ? SecurityTokenLifetimeCIMs : SecurityTokenLifetimeLocalMs;

            TestContext.Out.WriteLine($"Starting connection stability test for {testDurationMinutes} minutes ({testDurationSeconds} seconds)");
            TestContext.Out.WriteLine($"Security token lifetime: {tokenLifetimeMs / 1000} seconds ({tokenLifetimeMs / 60000.0:F1} minutes)");

            const int publishingInterval = 1000; // 1 second
            const int writerInterval = 2000;     // 2 seconds
            const int samplingInterval = 500;    // 500 ms

            var valueChanges = new ConcurrentDictionary<NodeId, int>();
            var clientHandles = new ConcurrentDictionary<uint, NodeId>();
            var errors = new ConcurrentBag<string>();

            ISession session = null;
            Subscription subscription = null;

            try
            {
                // Get nodes for subscription
                IDictionary<NodeId, Type> nodeIds = GetTestSetStaticMassNumeric(Session.NamespaceUris);
                if (nodeIds.Count == 0)
                {
                    NUnit.Framework.Assert.Ignore("No nodes for simulation found, ignoring test.");
                }

                TestContext.Out.WriteLine($"Subscribing to {nodeIds.Count} nodes.");

                // Create session
                session = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.NotNull(session, "Failed to create session");

                // Create subscription
                subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = publishingInterval,
                    PublishingEnabled = true,
                    KeepAliveCount = 10,
                    LifetimeCount = 100,
                    MaxNotificationsPerPublish = 1000,
                    Priority = 100
                };

                // Add monitored items
                foreach (NodeId nodeId in nodeIds.Keys)
                {
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = nodeId,
                        AttributeId = Attributes.Value,
                        MonitoringMode = MonitoringMode.Reporting,
                        SamplingInterval = samplingInterval,
                        QueueSize = 10,
                        DiscardOldest = true
                    };

                    valueChanges.TryAdd(nodeId, 0);
                    clientHandles.TryAdd(item.ClientHandle, nodeId);
                    subscription.AddItem(item);
                }

                // Set up notification callback
                subscription.FastDataChangeCallback = (sub, item, value) =>
                {
                    try
                    {
                        foreach (MonitoredItemNotification notification in item.MonitoredItems)
                        {
                            if (!StatusCode.IsGood(notification.Value.StatusCode))
                            {
                                string error = $"Bad status code received: {notification.Value.StatusCode} for client handle {notification.ClientHandle}";
                                errors.Add(error);
                                TestContext.Out.WriteLine($"ERROR: {error}");
                            }
                            else if (clientHandles.TryGetValue(notification.ClientHandle, out NodeId nodeId))
                            {
                                valueChanges.AddOrUpdate(nodeId, 1, (key, count) => count + 1);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        string error = $"Exception in data change callback: {ex.Message}";
                        errors.Add(error);
                        TestContext.Out.WriteLine($"ERROR: {error}");
                    }
                };

                // Create subscription on server
                session.AddSubscription(subscription);
                await subscription.CreateAsync().ConfigureAwait(false);

                TestContext.Out.WriteLine($"Subscription created with {subscription.MonitoredItemCount} monitored items");

                // Create writer session
                ISession writerSession = await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                Assert.NotNull(writerSession, "Failed to create writer session");

                // Writer task - continuously write values
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
                        catch (Exception ex)
                        {
                            string error = $"Writer session error: {ex.Message}";
                            errors.Add(error);
                            TestContext.Out.WriteLine($"ERROR: {error}");
                        }

                        try
                        {
                            await Task.Delay(writerInterval, writerCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }

                    TestContext.Out.WriteLine($"Writer task completed. Total writes: {writeCount}");
                }, writerCts.Token);

                // Status reporting task
                var statusReportingCts = new CancellationTokenSource();
                var statusTask = Task.Run(async () =>
                {
                    int reportCount = 0;

                    while (!statusReportingCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(StatusReportIntervalSeconds), statusReportingCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        reportCount++;
                        int totalNotifications = valueChanges.Values.Sum();
                        int elapsedMinutes = reportCount * StatusReportIntervalSeconds / 60;

                        TestContext.Out.WriteLine(
                            $"[Status Report {reportCount}] Elapsed: {elapsedMinutes} minutes, " +
                            $"Total notifications: {totalNotifications}, Write count: {writeCount}, Errors: {errors.Count}");

#if DEBUG
                        // Report per-node statistics
                        if (reportCount % 5 == 0) // Every 5 minutes
                        {
                            TestContext.Out.WriteLine("Per-node notification counts:");
                            foreach (var kvp in valueChanges.OrderBy(x => x.Key.ToString()))
                            {
                                TestContext.Out.WriteLine($"  {kvp.Key}: {kvp.Value} notifications");
                            }
                        }
#endif
                    }
                }, statusReportingCts.Token);

                // Run test for the specified duration
                TestContext.Out.WriteLine($"Test running... will complete at {DateTime.UtcNow.AddSeconds(testDurationSeconds):yyyy-MM-dd HH:mm:ss} UTC");
                await Task.Delay(TimeSpan.FromSeconds(testDurationSeconds)).ConfigureAwait(false);

                // Stop tasks
                TestContext.Out.WriteLine("Test duration elapsed. Stopping writer and status tasks...");
                writerCts.Cancel();
                statusReportingCts.Cancel();

                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    /* expected */
                }

                try
                {
                    await statusTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    /* expected */
                }

                // Wait for final notifications to be processed
                TestContext.Out.WriteLine("Waiting for final notifications to be processed...");
                await Task.Delay(publishingInterval * 5).ConfigureAwait(false);

                // Verification
                TestContext.Out.WriteLine("=== Final Results ===");
                TestContext.Out.WriteLine($"Test duration: {testDurationMinutes} minutes");
                TestContext.Out.WriteLine($"Security token lifetime: {tokenLifetimeMs / 1000} seconds ({tokenLifetimeMs / 60000.0:F1} minutes)");
                TestContext.Out.WriteLine($"Expected token renewals: ~{(testDurationMinutes * 60000) / tokenLifetimeMs} times");
                TestContext.Out.WriteLine($"Total write operations: {writeCount}");
                TestContext.Out.WriteLine($"Total errors: {errors.Count}");

                // Calculate expected and received notifications
                int totalNotifications = valueChanges.Values.Sum();
                int expectedMinNotifications = (writeCount - 1) * nodeIds.Count; // Allow for some timing variance

                TestContext.Out.WriteLine($"Total notifications received: {totalNotifications}");
                TestContext.Out.WriteLine($"Expected minimum notifications: {expectedMinNotifications}");

                // Per-node verification
                TestContext.Out.WriteLine("Per-node results:");
                bool allNodesReceivedData = true;
                foreach (NodeId nodeId in nodeIds.Keys)
                {
                    if (valueChanges.TryGetValue(nodeId, out int changes))
                    {
#if DEBUG
                        TestContext.Out.WriteLine($"  {nodeId}: {changes} notifications");
#endif
                        if (changes < (writeCount * NotificationToleranceRatio))
                        {
                            allNodesReceivedData = false;
                            TestContext.Out.WriteLine($"    WARNING: Expected at least {writeCount * NotificationToleranceRatio:F0} notifications");
                        }
                    }
                    else
                    {
                        allNodesReceivedData = false;
                        TestContext.Out.WriteLine($"  {nodeId}: 0 notifications (ERROR)");
                    }
                }

                // List all errors
                if (!errors.IsEmpty)
                {
                    TestContext.Out.WriteLine($"Errors encountered ({errors.Count}):");
                    foreach (string error in errors.Take(20)) // Show first 20 errors
                    {
                        TestContext.Out.WriteLine($"  - {error}");
                    }
                    if (errors.Count > 20)
                    {
                        TestContext.Out.WriteLine($"  ... and {errors.Count - 20} more errors");
                    }
                }

                // Cleanup writer session
                try
                {
                    await writerSession.CloseAsync().ConfigureAwait(false);
                    writerSession.Dispose();
                }
                catch (Exception ex)
                {
                    TestContext.Out.WriteLine($"Failed to close writer session: {ex.Message}");
                }

                // Assertions
                Assert.IsTrue(allNodesReceivedData, "Not all nodes received expected data");
                Assert.AreEqual(0, errors.Count, $"Test encountered {errors.Count} errors");
                Assert.GreaterOrEqual(totalNotifications, expectedMinNotifications, "Total notifications received is less than expected minimum");

                TestContext.Out.WriteLine("Connection stability test PASSED");
            }
            finally
            {
                // Cleanup
                if (subscription != null)
                {
                    try
                    {
                        await subscription.DeleteAsync(true).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TestContext.Out.WriteLine($"Failed to delete subscription: {ex.Message}");
                    }
                }

                if (session != null)
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

        /// <summary>
        /// Gets the test duration in minutes from environment variable or returns default.
        /// </summary>
        private int GetTestDurationMinutes()
        {
            string envValue = Environment.GetEnvironmentVariable("TEST_DURATION_MINUTES");

            if (!string.IsNullOrEmpty(envValue) && int.TryParse(envValue, out int minutes) && minutes > 0)
            {
                return minutes;
            }

            // Default to 90 minutes for nightly runs, but use 1 minute for manual/local testing
            // CI: 90 minutes with 5-minute token lifetime = 18 renewals
            // Local: 1 minute with 10-second token lifetime = 6 renewals
            // Check if running in CI environment
            bool isCI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

            return isCI ? 90 : 1; // 90 minutes for CI (18 renewals), 1 minute for local (6 renewals)
        }
    }
}
