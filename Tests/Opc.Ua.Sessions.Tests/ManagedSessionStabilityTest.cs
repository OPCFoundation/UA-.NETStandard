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

#nullable enable

#pragma warning disable CA2016

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Long-running stability/haul tests for the V2
    /// <see cref="ManagedSession"/> + <see cref="ISubscriptionManager"/>
    /// stack — addresses
    /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/issues/3744">
    /// issue #3744</see>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pattern: a single writer increments a monotonic counter and
    /// writes it into a scalar variable on the reference server every
    /// <c>writerInterval</c>. A V2 subscription monitors the same
    /// variable. The handler captures every received value and asserts
    /// the per-subscription monotonic ordering V2 promises in
    /// <see cref="ISubscriptionNotificationHandler"/>'s doc — each
    /// subsequent sample must be <em>strictly greater than</em> the
    /// previous one. Values may be skipped (sampling rate &lt; write
    /// rate) but they must never be re-ordered or duplicated.
    /// </para>
    /// <para>
    /// The fault-injection long-haul variant additionally tears down
    /// the inner transport channel at a configurable cadence to verify
    /// the V2 subscription manager survives repeated reconnects and
    /// continues to deliver values in monotonic order across the
    /// breaks.
    /// </para>
    /// <para>
    /// All three tests are <see cref="ExplicitAttribute">[Explicit]</see>
    /// so they only run when explicitly requested (e.g.
    /// <c>dotnet test --filter "Category=ManagedSessionHaul"</c>). Long
    /// variants honour <c>TEST_DURATION_MINUTES</c> /
    /// <c>FAULT_INJECTION_INTERVAL_SECONDS</c> env vars.
    /// </para>
    /// </remarks>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Category("Client")]
    [Category("ManagedSessionHaul")]
    public class ManagedSessionStabilityTest : ClientTestFramework
    {
        private const int kSecurityTokenLifetimeLocalMs = 10 * 1000;
        private const int kSecurityTokenLifetimeCIMs = 5 * 60 * 1000;
        private const int kStatusReportIntervalSeconds = 60;

        public ManagedSessionStabilityTest()
            : base(Utils.UriSchemeOpcTcp)
        {
            SupportsExternalServerUrl = true;
        }

        /// <summary>
        /// Short-haul (2 minute) sanity test for the V2 managed
        /// session + subscription engine counter-monotonicity contract.
        /// </summary>
        [Test]
        [Order(100)]
        [Explicit]
        public async Task ShortHaulManagedSessionV2Async()
        {
            try
            {
                SecurityTokenLifetime = kSecurityTokenLifetimeLocalMs;
                await OneTimeSetUpAsync().ConfigureAwait(false);

                await RunCounterMonotonicityTestAsync(
                    testDurationMinutes: 2,
                    faultInjectionIntervalSeconds: null)
                    .ConfigureAwait(false);
            }
            finally
            {
                await OneTimeTearDownAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Long-haul (default 90 minute, configurable via
        /// <c>TEST_DURATION_MINUTES</c> env var) stability test for
        /// the V2 managed session + subscription engine. No fault
        /// injection — pure long-running monotonicity verification.
        /// </summary>
        [Test]
        [Order(200)]
        [Explicit]
        public async Task LongHaulManagedSessionV2Async()
        {
            try
            {
                SecurityTokenLifetime = kSecurityTokenLifetimeCIMs;
                await OneTimeSetUpAsync().ConfigureAwait(false);

                int minutes = GetEnvIntOrDefault("TEST_DURATION_MINUTES", 90);
                await RunCounterMonotonicityTestAsync(
                    testDurationMinutes: minutes,
                    faultInjectionIntervalSeconds: null)
                    .ConfigureAwait(false);
            }
            finally
            {
                await OneTimeTearDownAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Long-haul (default 90 minute, configurable via
        /// <c>TEST_DURATION_MINUTES</c>) stability test for the V2
        /// managed session + subscription engine WITH periodic fault
        /// injection — every
        /// <c>FAULT_INJECTION_INTERVAL_SECONDS</c> (default 60s) the
        /// underlying transport channel is force-closed to verify the
        /// V2 subscription manager recovers and continues to deliver
        /// monotonically-increasing values across the break. This is
        /// the bonus feature called out in
        /// <see href="https://github.com/OPCFoundation/UA-.NETStandard/issues/3744">
        /// issue #3744</see>.
        /// </summary>
        [Test]
        [Order(300)]
        [Explicit]
        public async Task LongHaulManagedSessionWithFaultInjectionV2Async()
        {
            try
            {
                SecurityTokenLifetime = kSecurityTokenLifetimeCIMs;
                await OneTimeSetUpAsync().ConfigureAwait(false);

                int minutes = GetEnvIntOrDefault("TEST_DURATION_MINUTES", 90);
                int faultEverySeconds = GetEnvIntOrDefault(
                    "FAULT_INJECTION_INTERVAL_SECONDS", 60);

                await RunCounterMonotonicityTestAsync(
                    testDurationMinutes: minutes,
                    faultInjectionIntervalSeconds: faultEverySeconds)
                    .ConfigureAwait(false);
            }
            finally
            {
                await OneTimeTearDownAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Core haul test: connect a V2 <see cref="ManagedSession"/>,
        /// subscribe to a single writable scalar variable, run a
        /// monotonic counter writer in parallel, and verify the
        /// per-subscription ordering contract over the requested
        /// duration. When
        /// <paramref name="faultInjectionIntervalSeconds"/> is
        /// non-null the inner transport channel is force-closed on
        /// that cadence to exercise the V2 reconnect path.
        /// </summary>
        private async Task RunCounterMonotonicityTestAsync(
            int testDurationMinutes,
            int? faultInjectionIntervalSeconds)
        {
            int testDurationSeconds = testDurationMinutes * 60;
            int writerIntervalMs = 250;
            int publishingIntervalMs = 500;

            TestContext.Out.WriteLine(
                $"V2 ManagedSession stability test: duration={testDurationMinutes}min, " +
                $"writer={writerIntervalMs}ms, publish={publishingIntervalMs}ms, " +
                $"faultInjection={(faultInjectionIntervalSeconds.HasValue ? faultInjectionIntervalSeconds + "s" : "off")}");

            // Pick a single writable scalar from the reference server's
            // mass test set. Scalar_Static_Mass_UInt32_UInt32_00 is exposed
            // by the reference node manager and accepts client writes.
            var counterNode = ExpandedNodeId.ToNodeId(
                new ExpandedNodeId(
                    "Scalar_Static_Mass_UInt32_UInt32_00",
                    Quickstarts.ReferenceServer.Namespaces.ReferenceServer),
                Session.NamespaceUris);
            Assert.That(counterNode, Is.Not.Null);
            Assert.That(counterNode.IsNull, Is.False,
                "Reference server must expose Scalar_Static_UInt32 for haul writes.");

            // Run two ManagedSessions: a subscriber (V2 engine) and a
            // writer (also V2; writer just uses raw Write service
            // calls, no subscription).
            using var globalCts = new CancellationTokenSource(
                TimeSpan.FromMinutes(testDurationMinutes + 5));
            CancellationToken ct = globalCts.Token;

            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);

            ManagedSessionBuilder subscriberBuilder =
                new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                    .UseEndpoint(endpoint)
                    .WithSessionName($"V2HaulSubscriber-{testDurationMinutes}m")
                    .WithSessionTimeout(TimeSpan.FromSeconds(120))
                    .WithReconnectPolicy(p => p with
                    {
                        Strategy = BackoffStrategy.Exponential,
                        InitialDelay = TimeSpan.FromMilliseconds(200),
                        MaxDelay = TimeSpan.FromSeconds(5),
                        MaxRetries = 0
                    });
            if (faultInjectionIntervalSeconds.HasValue)
            {
                // Survive faults: ask the V2 manager to transfer the
                // subscription after each forced reconnect; on transfer
                // failure it falls back to recreate.
                subscriberBuilder = subscriberBuilder
                    .WithTransferSubscriptionsOnRecreate();
            }

            ManagedSession subscriber = await subscriberBuilder
                .ConnectAsync(ct).ConfigureAwait(false);
            ManagedSession? writer = null;
            try
            {
                writer = await new ManagedSessionBuilder(
                        ClientFixture.Config, Telemetry)
                    .UseEndpoint(endpoint)
                    .WithSessionName($"V2HaulWriter-{testDurationMinutes}m")
                    .WithSessionTimeout(TimeSpan.FromSeconds(120))
                    .ConnectAsync(ct).ConfigureAwait(false);

                // Seed the counter to 0 so the first received sample is
                // a known baseline.
                await WriteCounterAsync(writer, counterNode, 0, ct)
                    .ConfigureAwait(false);

                var monotonicityHandler = new MonotonicCounterHandler();
                ISubscription subscription = subscriber.AddSubscription(
                    monotonicityHandler,
                    new Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromMilliseconds(publishingIntervalMs),
                        KeepAliveCount = 10,
                        LifetimeCount = 100,
                        MaxNotificationsPerPublish = 1000,
                        PublishingEnabled = true
                    });

                bool created = await WaitForAsync(() => subscription.Created,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(created, Is.True);

                Assert.That(subscription.TryAddMonitoredItem(
                    "Counter",
                    counterNode,
                    o => o with
                    {
                        SamplingInterval = TimeSpan.FromMilliseconds(publishingIntervalMs / 2),
                        QueueSize = 100,
                        DiscardOldest = false,
                        MonitoringMode = MonitoringMode.Reporting
                    },
                    out Client.Subscriptions.MonitoredItems.IMonitoredItem? item), Is.True);
                Assert.That(item, Is.Not.Null);

                bool itemCreated = await WaitForAsync(() => item!.Created,
                    TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                Assert.That(itemCreated, Is.True);

                // Wait for the initial sample to arrive before starting
                // the writer, so the baseline is captured.
                bool baseline = await WaitForAsync(
                    () => monotonicityHandler.ReceivedCount > 0,
                    TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                Assert.That(baseline, Is.True,
                    "Subscription must deliver initial sample before haul begins.");

                long writeCount = 0;
                using var writerCts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                var writerTask = Task.Run(async () =>
                {
                    while (!writerCts.IsCancellationRequested)
                    {
                        long next = Interlocked.Increment(ref writeCount);
                        try
                        {
                            await WriteCounterAsync(writer, counterNode,
                                (uint)next, writerCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (ServiceResultException sre)
                            when (sre.StatusCode == StatusCodes.BadRequestInterrupted ||
                                  sre.StatusCode == StatusCodes.BadNotConnected ||
                                  sre.StatusCode == StatusCodes.BadSecureChannelClosed)
                        {
                            // Expected when fault injection breaks the
                            // channel mid-write; the next iteration will
                            // pick up after the writer's session
                            // reconnects.
                            TestContext.Out.WriteLine(
                                $"INFO: Writer transient failure (expected during fault): {sre.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            monotonicityHandler.RecordError(
                                $"Writer error: {ex.Message}");
                        }
                        try
                        {
                            await Task.Delay(writerIntervalMs, writerCts.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, writerCts.Token);

                using var faultCts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                Task? faultTask = null;
                int faultCount = 0;
                if (faultInjectionIntervalSeconds.HasValue)
                {
                    int faultEverySeconds = faultInjectionIntervalSeconds.Value;
                    faultTask = Task.Run(async () =>
                    {
                        while (!faultCts.IsCancellationRequested)
                        {
                            try
                            {
                                await Task.Delay(
                                    TimeSpan.FromSeconds(faultEverySeconds),
                                    faultCts.Token).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                break;
                            }
                            ITransportChannel? channel =
                                subscriber.InnerSession?.TransportChannel;
                            if (channel == null)
                            {
                                continue;
                            }
                            Interlocked.Increment(ref faultCount);
                            TestContext.Out.WriteLine(
                                $"FAULT INJECTION #{faultCount}: closing subscriber transport channel");
                            try
                            { channel.Dispose(); }
                            catch (Exception ex)
                            {
                                TestContext.Out.WriteLine(
                                    $"INFO: Channel dispose threw (ok): {ex.Message}");
                            }
                        }
                    }, faultCts.Token);
                }

                // Status reporting
                using var statusCts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct);
                var statusTask = Task.Run(async () =>
                {
                    int reportNum = 0;
                    while (!statusCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(
                                TimeSpan.FromSeconds(kStatusReportIntervalSeconds),
                                statusCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        reportNum++;
                        TestContext.Out.WriteLine(
                            $"[Status #{reportNum}] elapsedMin={reportNum * kStatusReportIntervalSeconds / 60} " +
                            $"writes={Interlocked.Read(ref writeCount)} " +
                            $"received={monotonicityHandler.ReceivedCount} " +
                            $"lastValue={monotonicityHandler.LastValue} " +
                            $"faults={faultCount} " +
                            $"errors={monotonicityHandler.ErrorCount} " +
                            $"connected={subscriber.Connected}");
                    }
                }, statusCts.Token);

                // Run for the requested duration.
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(testDurationSeconds), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* timeout fired */ }

                // Stop background tasks.
                await writerCts.CancelAsync().ConfigureAwait(false);
                await faultCts.CancelAsync().ConfigureAwait(false);
                await statusCts.CancelAsync().ConfigureAwait(false);
                try
                { await writerTask.ConfigureAwait(false); }
                catch { /* ok */ }
                if (faultTask != null)
                {
                    try
                    { await faultTask.ConfigureAwait(false); }
                    catch { /* ok */ }
                }
                try
                { await statusTask.ConfigureAwait(false); }
                catch { /* ok */ }

                // Drain the last few publishes.
                await Task.Delay(publishingIntervalMs * 4).ConfigureAwait(false);

                long totalWrites = Interlocked.Read(ref writeCount);
                long totalReceived = monotonicityHandler.ReceivedCount;
                IReadOnlyList<string> monoErrors =
                    monotonicityHandler.MonotonicityErrors;
                IReadOnlyList<string> otherErrors =
                    monotonicityHandler.Errors;

                TestContext.Out.WriteLine("=== Final Results ===");
                TestContext.Out.WriteLine($"Writes issued: {totalWrites}");
                TestContext.Out.WriteLine($"Samples received: {totalReceived}");
                TestContext.Out.WriteLine($"Faults injected: {faultCount}");
                TestContext.Out.WriteLine($"Monotonicity violations: {monoErrors.Count}");
                TestContext.Out.WriteLine($"Other errors: {otherErrors.Count}");
                TestContext.Out.WriteLine($"Final received value: {monotonicityHandler.LastValue}");

                if (monoErrors.Count > 0)
                {
                    foreach (string e in monoErrors.Take(10))
                    {
                        TestContext.Out.WriteLine("  MONOTONICITY: " + e);
                    }
                }
                if (otherErrors.Count > 0)
                {
                    foreach (string e in otherErrors.Take(10))
                    {
                        TestContext.Out.WriteLine("  ERROR: " + e);
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(monoErrors, Is.Empty,
                        "All samples must arrive in strictly increasing order " +
                        "(per-subscription V2 ordering guarantee).");
                    Assert.That(otherErrors, Is.Empty,
                        "Writer / handler must not record any unexpected errors.");
                    Assert.That(totalReceived, Is.GreaterThan(0),
                        "Subscription must deliver at least one sample.");
                    if (faultInjectionIntervalSeconds.HasValue)
                    {
                        Assert.That(faultCount, Is.GreaterThan(0),
                            "Fault-injection variant must trigger at least one fault.");
                    }
                });

                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                { await subscriber.CloseAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                try
                { await subscriber.DisposeAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
                if (writer != null)
                {
                    try
                    { await writer.CloseAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                    try
                    { await writer.DisposeAsync().ConfigureAwait(false); }
                    catch { /* best effort */ }
                }
            }
        }

        private static async Task WriteCounterAsync(
            ManagedSession writer, NodeId counterNode, uint value,
            CancellationToken ct)
        {
            var write = new WriteValue
            {
                NodeId = counterNode,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value))
            };
            ArrayOf<WriteValue> nodesToWrite =
                new WriteValue[] { write }.ToArrayOf();
            WriteResponse response = await writer.InnerSession!.WriteAsync(
                null, nodesToWrite, ct).ConfigureAwait(false);
            ArrayOf<StatusCode> results = response.Results;
            if (results.Count > 0 && StatusCode.IsBad(results[0]))
            {
                throw new ServiceResultException(results[0],
                    "Counter write returned bad status");
            }
        }

        private static int GetEnvIntOrDefault(string name, int defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }
            return int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed
                : defaultValue;
        }

        private static async Task<bool> WaitForAsync(
            Func<bool> predicate, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (predicate())
                {
                    return true;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            return predicate();
        }

        /// <summary>
        /// V2 <see cref="ISubscriptionNotificationHandler"/> that
        /// accumulates received <c>UInt32</c> counter samples and
        /// records a monotonicity violation whenever a value is less
        /// than the previously-observed maximum. Skips (sampling rate
        /// &lt; write rate) are allowed; duplicates and reorderings
        /// are not.
        /// </summary>
        private sealed class MonotonicCounterHandler : ISubscriptionNotificationHandler
        {
            private long m_receivedCount;
            private long m_errorCount;
            // Stored as long so Interlocked.Exchange / Volatile.Read have
            // overloads on net4x (the typed uint overload is .NET 5+).
            // Value range stays within uint at runtime.
            private long m_lastValue;
            private readonly ConcurrentQueue<string> m_monotonicityErrors = new();
            private readonly ConcurrentQueue<string> m_errors = new();

            public long ReceivedCount => Volatile.Read(ref m_receivedCount);
            public long ErrorCount => Volatile.Read(ref m_errorCount);
            public uint LastValue => (uint)Volatile.Read(ref m_lastValue);
            public IReadOnlyList<string> MonotonicityErrors
                => [.. m_monotonicityErrors];
            public IReadOnlyList<string> Errors => [.. m_errors];

            public void RecordError(string error)
            {
                m_errors.Enqueue(error);
                Interlocked.Increment(ref m_errorCount);
            }

            public ValueTask OnDataChangeNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<DataValueChange> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<DataValueChange> span = notification.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    DataValueChange change = span[i];
                    if (change.DiagnosticInfo != null &&
                        StatusCode.IsBad(change.DiagnosticInfo.InnerStatusCode))
                    {
                        RecordError(
                            $"DiagInfo bad status: {change.DiagnosticInfo.InnerStatusCode}");
                        continue;
                    }
                    if (StatusCode.IsBad(change.Value.StatusCode))
                    {
                        RecordError(
                            $"Sample bad status: {change.Value.StatusCode}");
                        continue;
                    }
                    if (!change.Value.WrappedValue.TryGetValue(out uint sample))
                    {
                        RecordError(
                            $"Sample wrong type: {change.Value.WrappedValue.TypeInfo}");
                        continue;
                    }
                    uint previous = (uint)Interlocked.Exchange(ref m_lastValue, sample);
                    Interlocked.Increment(ref m_receivedCount);
                    // Strict monotonic non-decreasing. We allow == only
                    // on the very first observed sample (previous=0,
                    // sample=0). After that, the writer is monotonic
                    // strictly increasing — any duplicate or smaller
                    // value is a per-subscription ordering violation.
                    long received = Volatile.Read(ref m_receivedCount);
                    if (received > 1 && sample <= previous)
                    {
                        string err = string.Format(
                            CultureInfo.InvariantCulture,
                            "received sample #{0} = {1} not > previous {2}",
                            received, sample, previous);
                        m_monotonicityErrors.Enqueue(err);
                    }
                }
                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<EventNotification> notification,
                PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                ISubscription subscription,
                Client.Subscriptions.SubscriptionState state,
                PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
            }
        }
    }
}
