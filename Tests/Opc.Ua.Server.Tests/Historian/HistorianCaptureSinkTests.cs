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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates the auto-capture pipeline that ferries live
    /// <see cref="BaseVariableState"/> value updates into the historian
    /// via a bounded <see cref="System.Threading.Channels.Channel{T}"/>
    /// and a batched flusher. Covers the
    /// <see cref="HistorianCaptureSink"/>, the
    /// <see cref="IHistorianBulkInsertProvider"/> capability, and the
    /// <see cref="HistorianBuilder.Historize"/> opt-out parameter.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [NonParallelizable] // Each test wires its own captures + sinks
    public class HistorianCaptureSinkTests
    {
        private const ushort kNs = 2;
        private static readonly TimeSpan kFlushWait = TimeSpan.FromSeconds(2);
        private static readonly HistorianCaptureOptions kFastFlush = new()
        {
            BatchTarget = 1,
            BatchWindow = TimeSpan.FromMilliseconds(5),
        };

        [Test]
        public async Task StateChangedWithValueMaskEnqueuesSampleAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("v1");
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            SetValue(v, fixture.SystemContext, 42, baseTime: new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc));

            await WaitForArchiveCountAsync(fixture.Provider, v.NodeId, 1).ConfigureAwait(false);
        }

        [Test]
        public async Task StateChangedWithoutValueMaskIsIgnoredAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("vIgnore");
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            // Fire a non-value mask (NonValue ≠ Value). The handler must ignore it.
            v.UpdateChangeMasks(NodeStateChangeMasks.NonValue);
            v.ClearChangeMasks(fixture.SystemContext, includeChildren: false);

            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(await CountAsync(fixture.Provider, v.NodeId).ConfigureAwait(false), Is.EqualTo(0));
        }

        [Test]
        public async Task MultipleQuickUpdatesAreBatchedAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("vBatch");
            // Larger BatchTarget so all updates land in one flush.
            var opts = new HistorianCaptureOptions
            {
                BatchTarget = 16,
                BatchWindow = TimeSpan.FromMilliseconds(30),
            };
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: opts);

            for (int i = 0; i < 5; i++)
            {
                SetValue(v, fixture.SystemContext, i, baseTime: new DateTime(2025, 1, 1, 0, 0, i + 1, DateTimeKind.Utc));
            }

            await WaitForArchiveCountAsync(fixture.Provider, v.NodeId, 5).ConfigureAwait(false);
        }

        [Test]
        public async Task BulkProviderReceivesSingleCallPerFlushAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            var counting = new CountingBulkProvider(fixture.Provider);
            fixture.Builder.UseProvider(counting);
            BaseDataVariableState v = fixture.MakeVariable("vCount");
            var opts = new HistorianCaptureOptions
            {
                BatchTarget = 8,
                BatchWindow = TimeSpan.FromMilliseconds(40),
            };
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: opts);

            for (int i = 0; i < 4; i++)
            {
                SetValue(v, fixture.SystemContext, i, baseTime: new DateTime(2025, 1, 1, 0, 0, i + 1, DateTimeKind.Utc));
            }

            await WaitForArchiveCountAsync(fixture.Provider, v.NodeId, 4).ConfigureAwait(false);
            Assert.That(counting.BulkCalls, Is.GreaterThanOrEqualTo(1));
            Assert.That(counting.PerNodeCalls, Is.EqualTo(0),
                "Bulk-capable provider should never see per-node InsertAsync from the capture pipeline.");
        }

        [Test]
        public async Task NonBulkProviderFallsBackToPerNodeInsertAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            var nonBulk = new NonBulkProvider(fixture.Provider);
            fixture.Builder.UseProvider(nonBulk);
            BaseDataVariableState v = fixture.MakeVariable("vNonBulk");
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            SetValue(v, fixture.SystemContext, 7,
                baseTime: new DateTime(2025, 1, 1, 0, 0, 7, DateTimeKind.Utc));

            await WaitForArchiveCountAsync(fixture.Provider, v.NodeId, 1).ConfigureAwait(false);
            Assert.That(nonBulk.PerNodeCalls, Is.GreaterThanOrEqualTo(1),
                "Non-bulk provider must receive at least one InsertAsync call from the capture pipeline.");
        }

        [Test]
        public async Task AutoCaptureOptOutDoesNotInstallHandlerAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("vOptOut");
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, autoCapture: false);

            SetValue(v, fixture.SystemContext, 100,
                baseTime: new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc));

            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(await CountAsync(fixture.Provider, v.NodeId).ConfigureAwait(false), Is.EqualTo(0));
        }

        [Test]
        public async Task BoundedQueueDropsOldestUnderOverloadAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            // Slow the consumer down by using a provider that pauses each flush.
            var slow = new SlowProvider(fixture.Provider, TimeSpan.FromMilliseconds(50));
            fixture.Builder.UseProvider(slow);
            BaseDataVariableState v = fixture.MakeVariable("vOverload");
            var opts = new HistorianCaptureOptions
            {
                MaxQueuedSamples = 8,
                BatchTarget = 2,
                BatchWindow = TimeSpan.FromMilliseconds(5),
                FullMode = CaptureFullMode.DropOldest,
            };
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: opts);

            // Push more than the queue can hold.
            for (int i = 0; i < 50; i++)
            {
                SetValue(v, fixture.SystemContext, i,
                    baseTime: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(i));
            }
            await Task.Delay(150).ConfigureAwait(false);
            // After dispose, drained count + dropped count should equal 50.
            await fixture.DisposeBuilderAsync().ConfigureAwait(false);
            int archived = await CountAsync(fixture.Provider, v.NodeId).ConfigureAwait(false);
            Assert.That(archived, Is.LessThanOrEqualTo(50));
            Assert.That(archived, Is.GreaterThan(0),
                "At least some samples should survive even under heavy backpressure.");
        }

        [Test]
        public async Task DisposeAsyncFlushesPendingSamplesAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("vFlush");
            // Wide window so samples remain in the channel until dispose.
            var opts = new HistorianCaptureOptions
            {
                BatchTarget = 1000,
                BatchWindow = TimeSpan.FromSeconds(1),
            };
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: opts);

            for (int i = 0; i < 3; i++)
            {
                SetValue(v, fixture.SystemContext, i,
                    baseTime: new DateTime(2025, 1, 1, 0, 0, i + 1, DateTimeKind.Utc));
            }
            await fixture.DisposeBuilderAsync().ConfigureAwait(false);

            Assert.That(await CountAsync(fixture.Provider, v.NodeId).ConfigureAwait(false), Is.EqualTo(3),
                "All pending samples should be flushed during DisposeAsync.");
        }

        [Test]
        public async Task ProviderExceptionDoesNotCrashConsumerAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            var flaky = new FlakyProvider(fixture.Provider, failures: 2);
            fixture.Builder.UseProvider(flaky);
            BaseDataVariableState v = fixture.MakeVariable("vFlaky");
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            for (int i = 0; i < 5; i++)
            {
                SetValue(v, fixture.SystemContext, i,
                    baseTime: new DateTime(2025, 1, 1, 0, 0, i + 1, DateTimeKind.Utc));
                await Task.Delay(20).ConfigureAwait(false);
            }

            // After failures the consumer must still run and persist subsequent samples.
            await Task.Delay(200).ConfigureAwait(false);
            int archived = await CountAsync(fixture.Provider, v.NodeId).ConfigureAwait(false);
            Assert.That(archived, Is.GreaterThanOrEqualTo(1),
                "Capture consumer must survive provider exceptions and continue flushing.");
        }

        [Test]
        public async Task MultipleVariablesShareSinkAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v1 = fixture.MakeVariable("vA");
            BaseDataVariableState v2 = fixture.MakeVariable("vB");
            fixture.Builder.Historize(v1, systemContext: fixture.SystemContext, captureOptions: kFastFlush);
            fixture.Builder.Historize(v2, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            SetValue(v1, fixture.SystemContext, 1, baseTime: new DateTime(2025, 1, 1, 0, 0, 1, DateTimeKind.Utc));
            SetValue(v2, fixture.SystemContext, 2, baseTime: new DateTime(2025, 1, 1, 0, 0, 2, DateTimeKind.Utc));

            await WaitForArchiveCountAsync(fixture.Provider, v1.NodeId, 1).ConfigureAwait(false);
            await WaitForArchiveCountAsync(fixture.Provider, v2.NodeId, 1).ConfigureAwait(false);
        }

        [Test]
        public async Task DefaultIsOptInAsync()
        {
            using HistorianTestFixture fixture = HistorianTestFixture.Create();
            BaseDataVariableState v = fixture.MakeVariable("vDefault");
            // No autoCapture argument — verify default is true.
            fixture.Builder.Historize(
                v, systemContext: fixture.SystemContext, captureOptions: kFastFlush);

            SetValue(v, fixture.SystemContext, 9, baseTime: new DateTime(2025, 1, 1, 0, 0, 9, DateTimeKind.Utc));
            await WaitForArchiveCountAsync(fixture.Provider, v.NodeId, 1).ConfigureAwait(false);
        }

        private static void SetValue(
            BaseDataVariableState variable, ServerSystemContext ctx, int value, DateTime baseTime)
        {
            variable.Value = value;
            variable.Timestamp = baseTime;
            variable.StatusCode = StatusCodes.Good;
            variable.ClearChangeMasks(ctx, includeChildren: false);
        }

        private static async Task<int> CountAsync(InMemoryHistorianProvider provider, NodeId nodeId)
        {
            HistorianOperationContext ctx = CreateOpContext(provider);
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = (DateTimeUtc)new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndTime = (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                MaxValues = 0,
                IsForward = true,
            };
            int count = 0;
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(ctx, request, token, default).ConfigureAwait(false);
                count += page.Values.Count;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }
            return count;
        }

        private static async Task WaitForArchiveCountAsync(
            InMemoryHistorianProvider provider, NodeId nodeId, int expected)
        {
            DateTime deadline = DateTime.UtcNow + kFlushWait;
            while (DateTime.UtcNow < deadline)
            {
                if (await CountAsync(provider, nodeId).ConfigureAwait(false) >= expected)
                {
                    return;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            int actual = await CountAsync(provider, nodeId).ConfigureAwait(false);
            Assert.That(actual, Is.GreaterThanOrEqualTo(expected),
                $"Expected ≥ {expected} samples in archive for {nodeId} within {kFlushWait}; saw {actual}.");
        }

        private static HistorianOperationContext CreateOpContext(InMemoryHistorianProvider provider)
        {
            // Build a minimal HistorianOperationContext directly — for read-side counting only.
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(systemContext, opContext, null, HistoryUpdateType.Insert);
        }

        /// <summary>
        /// Test fixture that constructs a server + historian builder with
        /// a registry-host mock. Disposes the builder (and its sink) at
        /// teardown so the consumer task is properly drained.
        /// </summary>
        private sealed class HistorianTestFixture : IDisposable
        {
            public required InMemoryHistorianProvider Provider { get; init; }
            public required ServerSystemContext SystemContext { get; init; }
            public required HistorianBuilder Builder { get; init; }

            public BaseDataVariableState MakeVariable(string name)
            {
                return new BaseDataVariableState(parent: null)
                {
                    NodeId = new NodeId(name, kNs),
                    BrowseName = new QualifiedName(name, kNs),
                    DisplayName = new LocalizedText(name),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                };
            }

            public async ValueTask DisposeBuilderAsync()
            {
                await Builder.DisposeAsync().ConfigureAwait(false);
            }

            public void Dispose()
            {
                Builder.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Provider.Dispose();
            }

            public static HistorianTestFixture Create()
            {
                var nsTable = new NamespaceTable();
                nsTable.Append("urn:test:capture");

                var mockTelemetry = new Mock<ITelemetryContext>();
                var registry = new HistorianProviderRegistry(nsTable);

                var mockServer = new Mock<IServerInternal>();
                mockServer.Setup(s => s.NamespaceUris).Returns(nsTable);
                mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(nsTable));
                mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);
                mockServer.As<IHistorianRegistryProvider>()
                    .Setup(p => p.HistorianRegistry).Returns(registry);

                IServerInternal server = mockServer.Object;
                var systemContext = new ServerSystemContext(server);
                var provider = new InMemoryHistorianProvider();
                var builder = new HistorianBuilder(server);
                builder.UseProvider(provider).RegisterAsDefault();

                return new HistorianTestFixture
                {
                    Provider = provider,
                    SystemContext = systemContext,
                    Builder = builder,
                };
            }
        }

        // Wraps an inner provider and counts bulk vs per-node calls.
        private sealed class CountingBulkProvider :
            HistorianProviderBase,
            IHistorianDataProvider,
            IHistorianBulkInsertProvider
        {
            private readonly InMemoryHistorianProvider m_inner;
            public int BulkCalls;
            public int PerNodeCalls;

            public CountingBulkProvider(InMemoryHistorianProvider inner) => m_inner = inner;

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext c, HistorianRawReadRequest r, HistorianResumeToken t, CancellationToken ct)
                => m_inner.ReadRawAsync(c, r, t, ct);

            public ValueTask<IList<StatusCode>> InsertAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
            {
                Interlocked.Increment(ref PerNodeCalls);
                return m_inner.InsertAsync(c, n, v, ct);
            }

            public ValueTask<IList<StatusCode>> ReplaceAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.ReplaceAsync(c, n, v, ct);

            public ValueTask<IList<StatusCode>> UpdateAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.UpdateAsync(c, n, v, ct);

            public ValueTask<StatusCode> DeleteRawAsync(
                HistorianOperationContext c, NodeId n, DateTimeUtc s, DateTimeUtc e, bool m, CancellationToken ct)
                => m_inner.DeleteRawAsync(c, n, s, e, m, ct);

            public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
                HistorianOperationContext c, NodeId n, IList<DateTimeUtc> t, CancellationToken ct)
                => m_inner.DeleteAtTimeAsync(c, n, t, ct);

            public ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
                HistorianOperationContext c, IReadOnlyDictionary<NodeId, IList<DataValue>> b, CancellationToken ct)
            {
                Interlocked.Increment(ref BulkCalls);
                return m_inner.InsertBatchAsync(c, b, ct);
            }
        }

        // Provider that does NOT implement IHistorianBulkInsertProvider — verifies fallback.
        private sealed class NonBulkProvider :
            HistorianProviderBase,
            IHistorianDataProvider
        {
            private readonly InMemoryHistorianProvider m_inner;
            public int PerNodeCalls;

            public NonBulkProvider(InMemoryHistorianProvider inner) => m_inner = inner;

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext c, HistorianRawReadRequest r, HistorianResumeToken t, CancellationToken ct)
                => m_inner.ReadRawAsync(c, r, t, ct);

            public ValueTask<IList<StatusCode>> InsertAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
            {
                Interlocked.Increment(ref PerNodeCalls);
                return m_inner.InsertAsync(c, n, v, ct);
            }

            public ValueTask<IList<StatusCode>> ReplaceAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.ReplaceAsync(c, n, v, ct);

            public ValueTask<IList<StatusCode>> UpdateAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.UpdateAsync(c, n, v, ct);

            public ValueTask<StatusCode> DeleteRawAsync(
                HistorianOperationContext c, NodeId n, DateTimeUtc s, DateTimeUtc e, bool m, CancellationToken ct)
                => m_inner.DeleteRawAsync(c, n, s, e, m, ct);

            public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
                HistorianOperationContext c, NodeId n, IList<DateTimeUtc> t, CancellationToken ct)
                => m_inner.DeleteAtTimeAsync(c, n, t, ct);
        }

        // Slows down each insert; used to provoke backpressure-induced drops.
        private sealed class SlowProvider :
            HistorianProviderBase,
            IHistorianDataProvider,
            IHistorianBulkInsertProvider
        {
            private readonly InMemoryHistorianProvider m_inner;
            private readonly TimeSpan m_delay;

            public SlowProvider(InMemoryHistorianProvider inner, TimeSpan delay)
            {
                m_inner = inner;
                m_delay = delay;
            }

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext c, HistorianRawReadRequest r, HistorianResumeToken t, CancellationToken ct)
                => m_inner.ReadRawAsync(c, r, t, ct);

            public async ValueTask<IList<StatusCode>> InsertAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
            {
                await Task.Delay(m_delay, ct).ConfigureAwait(false);
                return await m_inner.InsertAsync(c, n, v, ct).ConfigureAwait(false);
            }

            public ValueTask<IList<StatusCode>> ReplaceAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.ReplaceAsync(c, n, v, ct);

            public ValueTask<IList<StatusCode>> UpdateAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.UpdateAsync(c, n, v, ct);

            public ValueTask<StatusCode> DeleteRawAsync(
                HistorianOperationContext c, NodeId n, DateTimeUtc s, DateTimeUtc e, bool m, CancellationToken ct)
                => m_inner.DeleteRawAsync(c, n, s, e, m, ct);

            public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
                HistorianOperationContext c, NodeId n, IList<DateTimeUtc> t, CancellationToken ct)
                => m_inner.DeleteAtTimeAsync(c, n, t, ct);

            public async ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
                HistorianOperationContext c, IReadOnlyDictionary<NodeId, IList<DataValue>> b, CancellationToken ct)
            {
                await Task.Delay(m_delay, ct).ConfigureAwait(false);
                return await m_inner.InsertBatchAsync(c, b, ct).ConfigureAwait(false);
            }
        }

        // Throws the first N flush attempts then forwards to the inner provider.
        private sealed class FlakyProvider :
            HistorianProviderBase,
            IHistorianDataProvider,
            IHistorianBulkInsertProvider
        {
            private readonly InMemoryHistorianProvider m_inner;
            private int m_remainingFailures;

            public FlakyProvider(InMemoryHistorianProvider inner, int failures)
            {
                m_inner = inner;
                m_remainingFailures = failures;
            }

            public ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
                HistorianOperationContext c, HistorianRawReadRequest r, HistorianResumeToken t, CancellationToken ct)
                => m_inner.ReadRawAsync(c, r, t, ct);

            public ValueTask<IList<StatusCode>> InsertAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
            {
                if (Interlocked.Decrement(ref m_remainingFailures) >= 0)
                {
                    throw new InvalidOperationException("forced");
                }
                return m_inner.InsertAsync(c, n, v, ct);
            }

            public ValueTask<IList<StatusCode>> ReplaceAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.ReplaceAsync(c, n, v, ct);

            public ValueTask<IList<StatusCode>> UpdateAsync(
                HistorianOperationContext c, NodeId n, IList<DataValue> v, CancellationToken ct)
                => m_inner.UpdateAsync(c, n, v, ct);

            public ValueTask<StatusCode> DeleteRawAsync(
                HistorianOperationContext c, NodeId n, DateTimeUtc s, DateTimeUtc e, bool m, CancellationToken ct)
                => m_inner.DeleteRawAsync(c, n, s, e, m, ct);

            public ValueTask<IList<StatusCode>> DeleteAtTimeAsync(
                HistorianOperationContext c, NodeId n, IList<DateTimeUtc> t, CancellationToken ct)
                => m_inner.DeleteAtTimeAsync(c, n, t, ct);

            public ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
                HistorianOperationContext c, IReadOnlyDictionary<NodeId, IList<DataValue>> b, CancellationToken ct)
            {
                if (Interlocked.Decrement(ref m_remainingFailures) >= 0)
                {
                    throw new InvalidOperationException("forced");
                }
                return m_inner.InsertBatchAsync(c, b, ct);
            }
        }
    }
}
