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
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Concurrency tests for <see cref="InMemoryHistorianProvider"/> exercising
    /// parallel readers, writers, and mixed update/read patterns to confirm
    /// that the per-method locking strategy preserves correctness under
    /// realistic multi-session load.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianConcurrencyTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public async Task ParallelInsertsDoNotLoseDataAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("concurrent.insert", NamespaceIndex);
            provider.Register(nodeId);

            const int writers = 8;
            const int perWriter = 250;
            HistorianOperationContext context = CreateContext();

            var tasks = new Task[writers];
            for (int w = 0; w < writers; w++)
            {
                int writerIndex = w;
                tasks[w] = Task.Run(async () =>
                {
                    for (int i = 0; i < perWriter; i++)
                    {
                        DateTime ts = BaseTime.AddTicks(writerIndex * 10_000_000L + i);
                        IList<StatusCode> statuses = await provider.InsertAsync(
                            context, nodeId, [MakeValue(ts, writerIndex * perWriter + i)], CancellationToken.None)
                            .ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(statuses[0]), Is.True);
                    }
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            int count = await CountAllAsync(provider, context, nodeId).ConfigureAwait(false);
            Assert.That(count, Is.EqualTo(writers * perWriter));
        }

        [Test]
        public async Task ConcurrentReadersSeeMonotonicSnapshotAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("concurrent.read", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            const int totalInserts = 1000;

            using var cts = new CancellationTokenSource();
            var writer = Task.Run(async () =>
            {
                for (int i = 0; i < totalInserts; i++)
                {
                    DateTime ts = BaseTime.AddMilliseconds(i);
                    await provider.InsertAsync(
                        context, nodeId, [MakeValue(ts, i)], CancellationToken.None).ConfigureAwait(false);
                }
            });

            var readers = new Task<int>[6];
            for (int r = 0; r < readers.Length; r++)
            {
                readers[r] = Task.Run(async () =>
                {
                    int observed = 0;
                    while (!writer.IsCompleted)
                    {
                        int snapshot = await CountAllAsync(provider, context, nodeId).ConfigureAwait(false);
                        Assert.That(snapshot, Is.GreaterThanOrEqualTo(observed));
                        observed = snapshot;
                        await Task.Yield();
                    }
                    int final = await CountAllAsync(provider, context, nodeId).ConfigureAwait(false);
                    Assert.That(final, Is.GreaterThanOrEqualTo(observed));
                    return final;
                });
            }
            await writer.ConfigureAwait(false);
            int[] results = await Task.WhenAll(readers).ConfigureAwait(false);
            cts.Cancel();
            foreach (int finalCount in results)
            {
                Assert.That(finalCount, Is.EqualTo(totalInserts));
            }
        }

        [Test]
        public async Task ParallelInsertReplaceDeleteAreSerialisedAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("concurrent.mixed", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            const int slots = 200;

            // Seed
            var seed = new List<DataValue>(slots);
            for (int i = 0; i < slots; i++)
            {
                seed.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, seed, CancellationToken.None).ConfigureAwait(false);

            var replacer = Task.Run(async () =>
            {
                for (int i = 0; i < slots; i++)
                {
                    await provider.ReplaceAsync(
                        context, nodeId, [MakeValue(BaseTime.AddSeconds(i), i + 10_000)], CancellationToken.None)
                        .ConfigureAwait(false);
                }
            });
            var reader = Task.Run(async () =>
            {
                for (int iter = 0; iter < 50; iter++)
                {
                    int observed = await CountAllAsync(provider, context, nodeId).ConfigureAwait(false);
                    Assert.That(observed, Is.EqualTo(slots), "Replace must not change cardinality");
                }
            });
            await Task.WhenAll(replacer, reader).ConfigureAwait(false);

            // Verify final values are all replaced.
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddSeconds(slots + 1),
                    MaxValues = 0,
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(slots));
            foreach (HistoricalDataValue v in page.Values)
            {
                int actual = Convert.ToInt32(v.Value.WrappedValue.AsBoxedObject(), System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(actual, Is.GreaterThanOrEqualTo(10_000));
            }
        }

        [Test]
        public async Task ConcurrentAnnotationUpdatesPreserveAllEntriesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("concurrent.annotations", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            const int writers = 4;
            const int perWriter = 100;

            var tasks = new Task[writers];
            for (int w = 0; w < writers; w++)
            {
                int writerIndex = w;
                tasks[w] = Task.Run(async () =>
                {
                    for (int i = 0; i < perWriter; i++)
                    {
                        DateTime when = BaseTime.AddTicks(writerIndex * 10_000_000L + i);
                        var annotation = new Annotation
                        {
                            Message = $"w{writerIndex}-{i}",
                            UserName = $"u{writerIndex}",
                            AnnotationTime = when
                        };
                        IList<StatusCode> statuses = await provider.InsertAnnotationsAsync(
                            context, nodeId, [annotation], CancellationToken.None).ConfigureAwait(false);
                        Assert.That(StatusCode.IsGood(statuses[0]), Is.True);
                    }
                });
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);

            HistorianPage<Annotation> page = await provider.ReadAnnotationsAsync(
                context,
                new HistorianAnnotationReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddDays(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(writers * perWriter));
        }

        [Test]
        public async Task RepeatedRegisterIsIdempotentAndSafeUnderRaceAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            const int iterations = 500;

            // Two threads call Register on the same 8 NodeIds repeatedly.
            // Register must be idempotent: data inserted between calls
            // must survive.
            var ids = new NodeId[8];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = new NodeId($"reg.race.{i}", NamespaceIndex);
                provider.Register(ids[i]);
            }

            var registrarA = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    provider.Register(ids[i % ids.Length]);
                }
            });
            var registrarB = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    provider.Register(ids[i % ids.Length]);
                }
            });
            var inserter = Task.Run(async () =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    NodeId id = ids[i % ids.Length];
                    DateTime ts = BaseTime.AddTicks(i);
                    IList<StatusCode> statuses = await provider.InsertAsync(
                        context, id, [MakeValue(ts, i)], CancellationToken.None).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(statuses[0]), Is.True);
                }
            });
            await Task.WhenAll(registrarA, registrarB, inserter).ConfigureAwait(false);

            int total = 0;
            foreach (NodeId id in ids)
            {
                total += await CountAllAsync(provider, context, id).ConfigureAwait(false);
            }
            Assert.That(total, Is.EqualTo(iterations));
        }

        private static async Task<int> CountAllAsync(
            InMemoryHistorianProvider provider, HistorianOperationContext context, NodeId nodeId)
        {
            int count = 0;
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = BaseTime.AddYears(-1),
                        EndTime = BaseTime.AddYears(1),
                        MaxValues = 0,
                        IsForward = true
                    },
                    token,
                    CancellationToken.None).ConfigureAwait(false);
                count += page.Values.Count;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
            }
            return count;
        }

        private static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DataValue MakeValue(DateTime sourceTimestamp, double value)
        {
            return new DataValue(new Variant(value), StatusCodes.Good, sourceTimestamp: sourceTimestamp, serverTimestamp: sourceTimestamp);
        }

        private static HistorianOperationContext CreateContext()
        {
            var mockTelemetry = new Mock<ITelemetryContext>();
            var mockServer = new Mock<IServerInternal>();
            mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var opContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryUpdate, RequestLifetime.None);
            var systemContext = new ServerSystemContext(mockServer.Object, opContext);
            return new HistorianOperationContext(
                systemContext, opContext, null, HistoryUpdateType.Insert);
        }
    }
}
