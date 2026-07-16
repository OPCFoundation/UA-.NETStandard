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
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;
using MonitoredItem = Opc.Ua.Client.MonitoredItem;
using Subscription = Opc.Ua.Client.Subscription;

#if NET10_0_OR_GREATER
namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Explicit subscription-driven ArrayPool diagnostics for issue #3988.
    /// </summary>
    [TestFixture]
    [Explicit]
    [NonParallelizable]
    [Category("LoadTest")]
    [Category("ArrayPool")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class SubscriptionArrayPoolDiagnosticsLoadTests : ClientTestFramework
    {
        private const int kParallelPublishThreshold = 256;
        private const int kPublishingIntervalMs = 50;
        private const int kWriterIntervalMs = 10;
        private const int kScenarioDurationMs = 6000;
        private const int kTrackedNodeCount = 1;
        private const int kTcpMinBufferSize = 8192;
        private const int kBufferCookieLength = 1;
        private const int kOutstandingToleranceCount = 8;
        private const int kPeakOutstandingMultiplier = 8;

        private int m_transportMaxBufferSize = 65535;

        public SubscriptionArrayPoolDiagnosticsLoadTests()
            : base(Utils.UriSchemeOpcTcp, NUnitTelemetryContext.Create(logLevel: LogLevel.Warning))
        {
            SingleSession = false;
            UseSamplingGroupsInReferenceNodeManager = false;
        }

        [Test]
        [TestCase(65535, TestName = "SubscriptionArrayPoolDiagnostics_MaxBufferSize65535")]
        [TestCase(65536, TestName = "SubscriptionArrayPoolDiagnostics_MaxBufferSize65536")]
        public async Task SubscriptionPublishesKeepArrayPoolBalancedAsync(int maxBufferSize)
        {
            ConfigureFixture(maxBufferSize);

            try
            {
                await OneTimeSetUpAsync().ConfigureAwait(false);

                ArrayPoolDiagnosticScenario[] scenarios =
                [
                    new(
                        "serial-threshold-minus-one",
                        kParallelPublishThreshold - 1,
                        kScenarioDurationMs,
                        kWriterIntervalMs,
                        kPublishingIntervalMs,
                        SlowConsumerDelayMs: 0,
                        SlowConsumerEveryNthSession: 0),
                    new(
                        "parallel-threshold-plus-one",
                        kParallelPublishThreshold + 1,
                        kScenarioDurationMs,
                        kWriterIntervalMs,
                        kPublishingIntervalMs,
                        SlowConsumerDelayMs: 0,
                        SlowConsumerEveryNthSession: 0),
                    new(
                        "parallel-threshold-plus-one-slow-consumer",
                        kParallelPublishThreshold + 1,
                        kScenarioDurationMs,
                        kWriterIntervalMs,
                        kPublishingIntervalMs,
                        SlowConsumerDelayMs: 5,
                        SlowConsumerEveryNthSession: 8)
                ];

                foreach (ArrayPoolDiagnosticScenario scenario in scenarios)
                {
                    await RunScenarioAsync(scenario).ConfigureAwait(false);
                }
            }
            finally
            {
                await OneTimeTearDownAsync().ConfigureAwait(false);
            }
        }

        public override async Task OneTimeSetUpAsync()
        {
            await OneTimeSetUpCoreAsync(securityNone: true).ConfigureAwait(false);
            ClientFixture.Config.TransportQuotas.MaxBufferSize = m_transportMaxBufferSize;
            ClientFixture.SessionTimeout = 300_000;
            ClientFixture.OperationTimeout = 90_000;
        }

        public override async Task CreateReferenceServerFixtureAsync(
            bool enableTracing,
            bool disableActivityLogging,
            bool securityNone)
        {
            ServerFixture = new ServerFixture<ReferenceServer>(
                t => new ReferenceServer(t),
                enableTracing,
                disableActivityLogging)
            {
                UriScheme = UriScheme,
                SecurityNone = securityNone,
                AutoAccept = true,
                AllNodeManagers = AllNodeManagers,
                OperationLimits = true,
                UseSamplingGroupsInReferenceNodeManager = UseSamplingGroupsInReferenceNodeManager,
                TransportBindingRegistry = TransportBindingRegistry
            };

            await ServerFixture.LoadConfigurationAsync(PkiRoot).ConfigureAwait(false);
            ServerFixture.Config.TransportQuotas.MaxBufferSize = m_transportMaxBufferSize;
            ServerFixture.Config.TransportQuotas.MaxMessageSize = TransportQuotaMaxMessageSize;
            ServerFixture.Config.TransportQuotas.MaxByteStringLength = ServerFixture
                .Config
                .TransportQuotas
                .MaxStringLength = TransportQuotaMaxStringLength;
            ServerFixture.Config.TransportQuotas.SecurityTokenLifetime = SecurityTokenLifetime;

            void AddExplicitUserTokenPolicies(string securityPolicyUri)
            {
                if (SecurityPolicies.GetInfo(securityPolicyUri) == null)
                {
                    return;
                }

                ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                    new UserTokenPolicy(UserTokenType.UserName)
                    {
                        SecurityPolicyUri = securityPolicyUri
                    };

                ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                    new UserTokenPolicy(UserTokenType.Certificate)
                    {
                        SecurityPolicyUri = securityPolicyUri
                    };

                ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                    new UserTokenPolicy(UserTokenType.IssuedToken)
                    {
                        IssuedTokenType = Profiles.JwtUserToken,
                        PolicyId = Profiles.JwtUserToken,
                        SecurityPolicyUri = securityPolicyUri
                    };
            }

            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.Certificate);
            ServerFixture.Config.ServerConfiguration.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.IssuedToken)
                {
                    IssuedTokenType = Profiles.JwtUserToken
                };

            AddExplicitUserTokenPolicies(SecurityPolicies.Basic128Rsa15);

            foreach (string securityPolicyUri in GetSupportedEccPolicyUris())
            {
                AddExplicitUserTokenPolicies(securityPolicyUri);
            }

            ServerFixture.Config.ServerConfiguration.MaxChannelCount = MaxChannelCount;
            ServerFixture.Config.ServerConfiguration.MaxSessionCount = MaxSessionCount;
            ServerFixture.Config.ServerConfiguration.MaxFailedAuthenticationAttempts =
                MaxFailedAuthenticationAttempts;
            ServerFixture.Config.ServerConfiguration.MaxRequestThreadCount =
                MaxRequestThreadCount;
            ServerFixture.Config.ServerConfiguration.MinRequestThreadCount =
                MinRequestThreadCount;
            ServerFixture.Config.ServerConfiguration.MaxSubscriptionCount =
                MaxSubscriptionCount;
            ServerFixture.Config.ServerConfiguration.MaxQueuedRequestCount = 100000;
            ReferenceServer = await ServerFixture.StartAsync().ConfigureAwait(false);
            ReferenceServer.TokenValidator = TokenValidator;
            ServerFixturePort = ServerFixture.Port;
        }

        private void ConfigureFixture(int maxBufferSize)
        {
            m_transportMaxBufferSize = maxBufferSize;
            MaxChannelCount = 320;
            MaxSessionCount = 320;
            MaxSubscriptionCount = 320;
            MaxFailedAuthenticationAttempts = 0;
            MaxRequestThreadCount = 200;
            MinRequestThreadCount = 50;
        }

        private async Task RunScenarioAsync(ArrayPoolDiagnosticScenario scenario)
        {
            var readers = new ConcurrentBag<ReaderSessionBundle>();
            var writerErrors = new ConcurrentQueue<string>();
            long notificationCount = 0;
            long callbackCount = 0;
            int writeCount = 0;
            bool readersClosed = false;
            CancellationTokenSource? writerCts = null;
            Task? writerTask = null;
            ISession? writerSession = null;

            try
            {
                writerSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);

                IDictionary<NodeId, Type> nodes = GetTestSetStaticMassNumeric(
                    writerSession.NamespaceUris);
                KeyValuePair<NodeId, Type>[] monitoredNodes = [.. nodes
                    .Where(static value => value.Value == typeof(uint))
                    .Take(kTrackedNodeCount)];

                if (monitoredNodes.Length < kTrackedNodeCount)
                {
                    Assert.Ignore("No writable UInt32 nodes were found for the subscription load harness.");
                }

                await CreateReaderSessionsAsync(
                        scenario,
                        monitoredNodes,
                        readers,
                        () => Interlocked.Increment(ref callbackCount),
                        count => Interlocked.Add(ref notificationCount, count))
                    .ConfigureAwait(false);

                await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);

                int trackedMinimumBufferSize =
                    GetTrackedMinimumBufferSize(m_transportMaxBufferSize);

                using var listener = new ArrayPoolEventListener(trackedMinimumBufferSize);

                long allocatedBytesBefore = GetTotalAllocatedBytes();
                int gen2Before = GC.CollectionCount(2);
                writerCts = new CancellationTokenSource();

                // CA2025 false positive: writerTask is awaited in the timed run and
                // again in finally before writerSession is closed/disposed.
                // TODO: Refactor the harness shape once CA2025 recognizes this pattern.
#pragma warning disable CA2025
                writerTask = RunWriterAsync(
                    writerSession,
                    monitoredNodes,
                    scenario,
                    writerErrors,
                    () => Interlocked.Increment(ref writeCount),
                    writerCts.Token);
#pragma warning restore CA2025

                await Task.Delay(
                        TimeSpan.FromMilliseconds(scenario.DurationMs))
                    .ConfigureAwait(false);

                writerCts.Cancel();

                try
                {
                    await writerTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the timed run ends.
                }

                await Task.Delay(
                        TimeSpan.FromMilliseconds(
                            (scenario.PublishingIntervalMs * 4) +
                            (scenario.SlowConsumerDelayMs * 8)))
                    .ConfigureAwait(false);

                await CloseSessionsAsync(readers.Select(static value => value.Session))
                    .ConfigureAwait(false);
                readersClosed = true;

                await CloseSessionAsync(writerSession).ConfigureAwait(false);
                writerSession = null;

                bool quiesced = await WaitForQuiescenceAsync(
                        listener,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(20))
                    .ConfigureAwait(false);

                ArrayPoolMetrics metrics = listener.CreateMetrics();
                long allocatedBytesAfter = GetTotalAllocatedBytes();
                int gen2After = GC.CollectionCount(2);

                WriteScenarioResults(
                    scenario,
                    writeCount,
                    callbackCount,
                    notificationCount,
                    allocatedBytesAfter - allocatedBytesBefore,
                    gen2After - gen2Before,
                    quiesced,
                    writerErrors,
                    metrics);

                Assert.That(writeCount, Is.GreaterThan(0), "The writer produced no writes.");
                Assert.That(callbackCount, Is.GreaterThan(0), "No data change callbacks were observed.");
                Assert.That(notificationCount, Is.GreaterThan(0), "No monitored item notifications were observed.");
                Assert.That(quiesced, Is.True, "ArrayPool activity did not quiesce after the scenario.");
                Assert.That(metrics.RentedCount, Is.GreaterThan(0), "No tracked ArrayPool rents were observed.");
                Assert.That(metrics.ReturnedCount, Is.GreaterThan(0), "No tracked ArrayPool returns were observed.");
                Assert.That(
                    Math.Abs(metrics.OutstandingCount),
                    Is.LessThanOrEqualTo(kOutstandingToleranceCount),
                    "Tracked ArrayPool rent/return balance did not settle after quiescence.");
                Assert.That(
                    Math.Abs(metrics.OutstandingBytes),
                    Is.LessThanOrEqualTo(
                        (long)kOutstandingToleranceCount * metrics.MaxObservedBufferSize),
                    "Tracked ArrayPool outstanding bytes did not settle after quiescence.");
                Assert.That(
                    metrics.PeakOutstandingCount,
                    Is.LessThanOrEqualTo(
                        scenario.PublishQueueCount * kPeakOutstandingMultiplier),
                    "Tracked ArrayPool outstanding buffers grew beyond the session-scaled envelope.");
            }
            finally
            {
                writerCts?.Cancel();

                if (writerTask != null)
                {
                    try
                    {
                        await writerTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during cleanup.
                    }
                }

                writerCts?.Dispose();

                if (!readersClosed)
                {
                    await CloseSessionsAsync(readers.Select(static value => value.Session))
                        .ConfigureAwait(false);
                }

                if (writerSession != null)
                {
                    await CloseSessionAsync(writerSession).ConfigureAwait(false);
                }
            }
        }

        private async Task CreateReaderSessionsAsync(
            ArrayPoolDiagnosticScenario scenario,
            KeyValuePair<NodeId, Type>[] monitoredNodes,
            ConcurrentBag<ReaderSessionBundle> readers,
            Action onCallback,
            Action<int> onNotification)
        {
            const int maxConcurrentConnects = 24;
            using var gate = new SemaphoreSlim(maxConcurrentConnects, maxConcurrentConnects);
            var tasks = new List<Task>(scenario.PublishQueueCount);

            for (int sessionIndex = 0; sessionIndex < scenario.PublishQueueCount; sessionIndex++)
            {
                int localSessionIndex = sessionIndex;
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        ISession session = await ClientFixture
                            .ConnectAsync(ServerUrl, SecurityPolicies.None)
                            .ConfigureAwait(false);

                        session.MinPublishRequestCount = 1;
                        session.MaxPublishRequestCount = 1;

                        var subscription = new Subscription(session.DefaultSubscription)
                        {
                            PublishingEnabled = true,
                            PublishingInterval = scenario.PublishingIntervalMs,
                            SequentialPublishing = true,
                            DisableMonitoredItemCache = true
                        };

                        foreach (KeyValuePair<NodeId, Type> monitoredNode in monitoredNodes)
                        {
                            var item = new MonitoredItem(subscription.DefaultItem)
                            {
                                StartNodeId = monitoredNode.Key,
                                AttributeId = Attributes.Value,
                                MonitoringMode = MonitoringMode.Reporting,
                                SamplingInterval = 0,
                                QueueSize = 1
                            };

                            subscription.AddItem(item);
                        }

                        bool isSlowConsumer = scenario.SlowConsumerDelayMs > 0 &&
                            scenario.SlowConsumerEveryNthSession > 0 &&
                            localSessionIndex % scenario.SlowConsumerEveryNthSession == 0;

                        subscription.FastDataChangeCallback = (_, notification, _) =>
                        {
                            onCallback();
                            onNotification(notification.MonitoredItems.Count);

                            if (isSlowConsumer)
                            {
                                Thread.Sleep(scenario.SlowConsumerDelayMs);
                            }
                        };

                        session.AddSubscription(subscription);
                        await subscription.CreateAsync().ConfigureAwait(false);

                        readers.Add(new ReaderSessionBundle(session, subscription));
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task RunWriterAsync(
            ISession writerSession,
            KeyValuePair<NodeId, Type>[] monitoredNodes,
            ArrayPoolDiagnosticScenario scenario,
            ConcurrentQueue<string> writerErrors,
            Action onWrite,
            CancellationToken ct)
        {
            WriteValue[] nodesToWrite = [.. monitoredNodes
                .Select(static value => new WriteValue
                {
                    NodeId = value.Key,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant((uint)0))
                })];

            uint nextValue = 0;

            while (!ct.IsCancellationRequested)
            {
                nextValue++;

                for (int i = 0; i < nodesToWrite.Length; i++)
                {
                    nodesToWrite[i].Value = new DataValue(new Variant(nextValue));
                }

                try
                {
                    await writerSession.WriteAsync(null, nodesToWrite, ct).ConfigureAwait(false);
                    onWrite();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadRequestInterrupted ||
                        sre.StatusCode == StatusCodes.BadSecureChannelClosed ||
                        sre.StatusCode == StatusCodes.BadSessionIdInvalid)
                {
                    writerErrors.Enqueue(sre.StatusCode.ToString());
                }
                catch (Exception ex)
                {
                    writerErrors.Enqueue(ex.GetType().Name + ":" + ex.Message);
                }

                await Task.Delay(
                        TimeSpan.FromMilliseconds(scenario.WriterIntervalMs),
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private static async Task CloseSessionsAsync(IEnumerable<ISession> sessions)
        {
            ISession[] sessionArray = [.. sessions
                .Where(static value => value != null)
                .Distinct()];

            if (sessionArray.Length == 0)
            {
                return;
            }

            await Task.WhenAll(sessionArray.Select(CloseSessionAsync)).ConfigureAwait(false);
        }

        private static async Task CloseSessionAsync(ISession session)
        {
            try
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup for an explicit diagnostics harness.
            }
            finally
            {
                session.Dispose();
            }
        }

        private static async Task<bool> WaitForQuiescenceAsync(
            ArrayPoolEventListener listener,
            TimeSpan stablePeriod,
            TimeSpan timeout)
        {
            long deadline = Stopwatch.GetTimestamp() +
                (long)(timeout.TotalSeconds * Stopwatch.Frequency);

            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (listener.GetIdleDuration() >= stablePeriod)
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            return listener.GetIdleDuration() >= stablePeriod;
        }

        private void WriteScenarioResults(
            ArrayPoolDiagnosticScenario scenario,
            int writeCount,
            long callbackCount,
            long notificationCount,
            long allocatedBytesDelta,
            int gen2CollectionsDelta,
            bool quiesced,
            ConcurrentQueue<string> writerErrors,
            ArrayPoolMetrics metrics)
        {
            TestContext.Out.WriteLine(
                $"ARRAYPOOL_DIAGNOSTIC scenario={scenario.Name}; " +
                $"maxBufferSize={m_transportMaxBufferSize}; " +
                $"publishQueues={scenario.PublishQueueCount}; " +
                $"slowConsumerDelayMs={scenario.SlowConsumerDelayMs}; " +
                $"slowConsumerEveryNthSession={scenario.SlowConsumerEveryNthSession}; " +
                $"writes={writeCount}; " +
                $"callbacks={callbackCount}; " +
                $"notifications={notificationCount}; " +
                $"allocatedBytesDelta={allocatedBytesDelta}; " +
                $"gen2CollectionsDelta={gen2CollectionsDelta}; " +
                $"quiesced={quiesced}; " +
                $"rented={metrics.RentedCount}; " +
                $"returned={metrics.ReturnedCount}; " +
                $"allocated={metrics.AllocatedCount}; " +
                $"dropped={metrics.DroppedCount}; " +
                $"trimmed={metrics.TrimmedCount}; " +
                $"outstanding={metrics.OutstandingCount}; " +
                $"outstandingBytes={metrics.OutstandingBytes}; " +
                $"peakOutstanding={metrics.PeakOutstandingCount}; " +
                $"peakOutstandingBytes={metrics.PeakOutstandingBytes}; " +
                $"maxObservedBufferSize={metrics.MaxObservedBufferSize}; " +
                $"writerErrors={writerErrors.Count}");

            foreach ((int bufferSize, long count) in metrics.BufferSizeHistogram)
            {
                TestContext.Out.WriteLine(
                    $"ARRAYPOOL_DIAGNOSTIC_BUFFER scenario={scenario.Name}; " +
                    $"bufferSize={bufferSize}; " +
                    $"count={count}");
            }

            if (!writerErrors.IsEmpty)
            {
                string[] errors = [.. writerErrors
                    .Distinct()
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .Take(5)];

                TestContext.Out.WriteLine(
                    "ARRAYPOOL_DIAGNOSTIC_ERRORS scenario=" +
                    scenario.Name +
                    "; samples=" +
                    string.Join(", ", errors));
            }
        }

        private static long GetTotalAllocatedBytes()
        {
            return GC.GetTotalAllocatedBytes(true);
        }

        private static int RoundUpToPowerOfTwo(int value)
        {
            int result = 1;

            while (result < value && result != 0)
            {
                result <<= 1;
            }

            return result;
        }

        private static int GetTrackedMinimumBufferSize(int configuredMaxBufferSize)
        {
            int normalizedDataSize = Math.Max(
                kTcpMinBufferSize,
                GetSuggestedBufferSize(configuredMaxBufferSize));

            return RoundUpToPowerOfTwo(normalizedDataSize + kBufferCookieLength);
        }

        private static int GetSuggestedBufferSize(int size)
        {
            int currentBucket = RoundUpToPowerOfTwo(size);
            int rentBucket = RoundUpToPowerOfTwo(size + kBufferCookieLength);

            if (currentBucket != rentBucket)
            {
                return currentBucket - kBufferCookieLength;
            }

            return size;
        }

        private sealed record ArrayPoolDiagnosticScenario(
            string Name,
            int PublishQueueCount,
            int DurationMs,
            int WriterIntervalMs,
            int PublishingIntervalMs,
            int SlowConsumerDelayMs,
            int SlowConsumerEveryNthSession);

        private sealed record ReaderSessionBundle(
            ISession Session,
            Subscription Subscription);

        private sealed record ArrayPoolMetrics(
            long RentedCount,
            long ReturnedCount,
            long AllocatedCount,
            long DroppedCount,
            long TrimmedCount,
            long OutstandingCount,
            long OutstandingBytes,
            long PeakOutstandingCount,
            long PeakOutstandingBytes,
            int MaxObservedBufferSize,
            IReadOnlyList<KeyValuePair<int, long>> BufferSizeHistogram);

        private sealed class ArrayPoolEventListener : EventListener
        {
            private const string kArrayPoolEventSourceName = "System.Buffers.ArrayPoolEventSource";

            private readonly int m_minTrackedBufferSize;
            private readonly ConcurrentDictionary<int, long> m_bufferSizes = new();
            private readonly ConcurrentDictionary<BufferKey, int> m_outstandingBuffers = new();
            private long m_allocatedCount;
            private long m_droppedCount;
            private long m_maxObservedBufferSize;
            private long m_outstandingBytes;
            private long m_outstandingCount;
            private long m_peakOutstandingBytes;
            private long m_peakOutstandingCount;
            private long m_rentedCount;
            private long m_returnedCount;
            private long m_trimmedCount;
            private long m_lastActivityTimestamp = Stopwatch.GetTimestamp();
            private EventSource? m_source;

            public ArrayPoolEventListener(int minTrackedBufferSize)
            {
                m_minTrackedBufferSize = minTrackedBufferSize;
            }

            public ArrayPoolMetrics CreateMetrics()
            {
                IReadOnlyList<KeyValuePair<int, long>> histogram =
                [
                    .. m_bufferSizes
                        .OrderBy(static value => value.Key)
                        .Select(static value => new KeyValuePair<int, long>(value.Key, value.Value))
                ];

                return new ArrayPoolMetrics(
                    RentedCount: Interlocked.Read(ref m_rentedCount),
                    ReturnedCount: Interlocked.Read(ref m_returnedCount),
                    AllocatedCount: Interlocked.Read(ref m_allocatedCount),
                    DroppedCount: Interlocked.Read(ref m_droppedCount),
                    TrimmedCount: Interlocked.Read(ref m_trimmedCount),
                    OutstandingCount: Interlocked.Read(ref m_outstandingCount),
                    OutstandingBytes: Interlocked.Read(ref m_outstandingBytes),
                    PeakOutstandingCount: Interlocked.Read(ref m_peakOutstandingCount),
                    PeakOutstandingBytes: Interlocked.Read(ref m_peakOutstandingBytes),
                    MaxObservedBufferSize: (int)Interlocked.Read(ref m_maxObservedBufferSize),
                    BufferSizeHistogram: histogram);
            }

            public TimeSpan GetIdleDuration()
            {
                long elapsedTicks = Stopwatch.GetTimestamp() -
                    Interlocked.Read(ref m_lastActivityTimestamp);

                return TimeSpan.FromSeconds(
                    (double)elapsedTicks / Stopwatch.Frequency);
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                if (eventSource.Name == kArrayPoolEventSourceName)
                {
                    m_source = eventSource;
                    EnableEvents(eventSource, EventLevel.Verbose);
                }
            }

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                string eventName = eventData.EventName ??
                    eventData.EventId.ToString(CultureInfo.InvariantCulture);

                if (!TryGetPayloadInt32(eventData, "bufferSize", fallbackIndex: 1, out int bufferSize) ||
                    bufferSize < m_minTrackedBufferSize)
                {
                    return;
                }

                UpdateMaxObservedBufferSize(bufferSize);
                Interlocked.Exchange(ref m_lastActivityTimestamp, Stopwatch.GetTimestamp());

                switch (eventName)
                {
                    case "BufferRented":
                        Interlocked.Increment(ref m_rentedCount);
                        m_bufferSizes.AddOrUpdate(bufferSize, 1, static (_, count) => count + 1);
                        if (TryGetBufferKey(eventData, out BufferKey rentedKey) &&
                            m_outstandingBuffers.TryAdd(rentedKey, bufferSize))
                        {
                            AddOutstanding(bufferSize);
                        }
                        break;
                    case "BufferReturned":
                        if (TryGetBufferKey(eventData, out BufferKey returnedKey) &&
                            m_outstandingBuffers.TryRemove(returnedKey, out int rentedBufferSize))
                        {
                            Interlocked.Increment(ref m_returnedCount);
                            RemoveOutstanding(rentedBufferSize);
                        }
                        break;
                    case "BufferAllocated":
                        Interlocked.Increment(ref m_allocatedCount);
                        break;
                    case "BufferDropped":
                        Interlocked.Increment(ref m_droppedCount);
                        break;
                    case "BufferTrimmed":
                        Interlocked.Increment(ref m_trimmedCount);
                        break;
                }
            }

            public override void Dispose()
            {
                if (m_source != null)
                {
                    DisableEvents(m_source);
                }

                base.Dispose();
            }

            private static bool TryGetBufferKey(
                EventWrittenEventArgs eventData,
                out BufferKey key)
            {
                if (TryGetPayloadInt32(eventData, "bufferId", fallbackIndex: 0, out int bufferId) &&
                    TryGetPayloadInt32(eventData, "poolId", fallbackIndex: 2, out int poolId))
                {
                    key = new BufferKey(poolId, bufferId);
                    return true;
                }

                key = default;
                return false;
            }

            private static bool TryGetPayloadInt32(
                EventWrittenEventArgs eventData,
                string payloadName,
                int fallbackIndex,
                out int result)
            {
                result = 0;
                IList<string>? payloadNames = eventData.PayloadNames;
                IList<object?>? payload = eventData.Payload;

                if (payload == null)
                {
                    return false;
                }

                if (payloadNames != null)
                {
                    for (int i = 0; i < payloadNames.Count; i++)
                    {
                        if (string.Equals(
                                payloadNames[i],
                                payloadName,
                                StringComparison.OrdinalIgnoreCase) &&
                            TryConvertToInt32(payload[i], out result))
                        {
                            return true;
                        }
                    }
                }

                return fallbackIndex < payload.Count &&
                    TryConvertToInt32(payload[fallbackIndex], out result);
            }

            private static bool TryConvertToInt32(object? value, out int result)
            {
                switch (value)
                {
                    case int int32:
                        result = int32;
                        return true;
                    case long int64 when int64 is <= int.MaxValue and >= int.MinValue:
                        result = (int)int64;
                        return true;
                    default:
                        result = 0;
                        return false;
                }
            }

            private void AddOutstanding(int bufferSize)
            {
                long outstandingCount = Interlocked.Increment(ref m_outstandingCount);
                long outstandingBytes = Interlocked.Add(ref m_outstandingBytes, bufferSize);
                UpdatePeak(ref m_peakOutstandingCount, outstandingCount);
                UpdatePeak(ref m_peakOutstandingBytes, outstandingBytes);
            }

            private void RemoveOutstanding(int bufferSize)
            {
                Interlocked.Decrement(ref m_outstandingCount);
                Interlocked.Add(ref m_outstandingBytes, -bufferSize);
            }

            private void UpdateMaxObservedBufferSize(int bufferSize)
            {
                long current;
                do
                {
                    current = Interlocked.Read(ref m_maxObservedBufferSize);
                    if (bufferSize <= current)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(
                    ref m_maxObservedBufferSize,
                    bufferSize,
                    current) != current);
            }

            private static void UpdatePeak(ref long peak, long candidate)
            {
                long current;
                do
                {
                    current = Interlocked.Read(ref peak);
                    if (candidate <= current)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref peak, candidate, current) != current);
            }

            private readonly record struct BufferKey(int PoolId, int BufferId);
        }
    }
}
#endif
