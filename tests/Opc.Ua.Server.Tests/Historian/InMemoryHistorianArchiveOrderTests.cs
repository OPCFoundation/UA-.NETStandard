/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// The in-memory historian keeps its raw values in sorted chunks it seeks into. Values
    /// inserted out of order, across chunk splits, and deleted again are read back in key
    /// order in both directions, and windowed reads return exactly the values in the window.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianArchiveOrderTests
    {
        private static readonly DateTime s_baseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [TestCase(7)]
        [TestCase(1234)]
        public async Task RandomInsertsAndDeletesReadBackInOrderAsync(int seed)
        {
            var random = new Random(seed);
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero },
                new FakeTimeProvider(s_baseTime));
            var nodeId = new NodeId("order", 1);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            // 3000 distinct seconds in random order, inserted in batches of up to 50
            int[] seconds = Enumerable.Range(0, 3000).OrderBy(_ => random.Next()).ToArray();
            var reference = new SortedSet<int>();
            for (int ii = 0; ii < seconds.Length;)
            {
                int count = Math.Min(seconds.Length - ii, 1 + random.Next(50));
                var batch = new List<DataValue>(count);
                for (int jj = 0; jj < count; jj++, ii++)
                {
                    batch.Add(Value(seconds[ii]));
                    reference.Add(seconds[ii]);
                }
                await provider.InsertAsync(context, nodeId, batch, CancellationToken.None);
            }

            // delete a few ranges, including one that empties whole chunks
            foreach ((int start, int end) in new[] { (100, 105), (1000, 1400), (2990, 3000), (0, 1) })
            {
                await provider.DeleteRawAsync(
                    context, nodeId, Time(start), Time(end), false, CancellationToken.None);
                reference.RemoveWhere(s => s >= start && s < end);
            }

            Assert.That(await ReadAllAsync(provider, context, nodeId, forward: true), Is.EqualTo(reference.ToList()));
            Assert.That(
                await ReadAllAsync(provider, context, nodeId, forward: false),
                Is.EqualTo(reference.Reverse().ToList()));

            for (int ii = 0; ii < 50; ii++)
            {
                int a = random.Next(3100);
                int b = a + random.Next(300);
                List<int> expected = reference.Where(s => s >= a && s < b).ToList();
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Time(a),
                        EndTime = Time(b),
                        IsForward = true
                    },
                    default,
                    CancellationToken.None);
                Assert.That(Seconds(page), Is.EqualTo(expected), $"[{a}, {b})");

                // the newest value before the window is its leading bound
                page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = Time(a),
                        EndTime = Time(b),
                        IsForward = true,
                        ReturnBounds = true
                    },
                    default,
                    CancellationToken.None);
                int? before = reference.Where(s => s < a).Select(s => (int?)s).LastOrDefault();
                if (!reference.Contains(a) && before.HasValue)
                {
                    Assert.That(Seconds(page)[0], Is.EqualTo(before.Value), $"leading bound of [{a}, {b})");
                }
            }
        }

        [Test]
        public async Task PrefixDeletesAndLaterAppendsKeepTheOrderAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero },
                new FakeTimeProvider(s_baseTime));
            var nodeId = new NodeId("prefix", 1);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            // 64 full chunks appended in order, then the oldest 40 chunks deleted in steps,
            // then 40 chunks more appended: the free slots in front of the chunk list are
            // reused for the new chunks
            await AppendAsync(provider, context, nodeId, 0, 128 * 64);
            for (int start = 0; start < 128 * 40; start += 128 * 8)
            {
                await provider.DeleteRawAsync(
                    context, nodeId, Time(start), Time(start + (128 * 8)), false, CancellationToken.None);
            }
            await AppendAsync(provider, context, nodeId, 128 * 64, 128 * 104);

            List<int> expected = Enumerable.Range(128 * 40, 128 * 64).ToList();
            Assert.That(await ReadAllAsync(provider, context, nodeId, forward: true, last: 128 * 104), Is.EqualTo(expected));
            expected.Reverse();
            Assert.That(await ReadAllAsync(provider, context, nodeId, forward: false, last: 128 * 104), Is.EqualTo(expected));
        }

        /// <summary>
        /// A reverse read from the OPC UA maximum time returns the values stored at that time.
        /// </summary>
        [Test]
        public async Task ReverseReadFromMaxValueReturnsValuesAtMaxValueAsync()
        {
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { RawDataRetentionPeriod = TimeSpan.Zero },
                new FakeTimeProvider(s_baseTime));
            var nodeId = new NodeId("max", 1);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            DateTime max = DateTimeUtc.MaxValue.ToDateTime();
            await provider.InsertAsync(
                context,
                nodeId,
                [Value(1), Value(2), new DataValue(new Variant(3.0), StatusCodes.Good, max, max)],
                CancellationToken.None);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = DateTimeUtc.MaxValue,
                    EndTime = Time(-1),
                    IsForward = false
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values.Count, Is.EqualTo(3));
            Assert.That(page.Values[0].Value.SourceTimestamp, Is.EqualTo(DateTimeUtc.MaxValue));
            Assert.That(page.Values[1].Value.SourceTimestamp, Is.EqualTo(Time(2)));
        }

        private static async Task AppendAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId,
            int from,
            int to)
        {
            for (int ii = from; ii < to; ii += 500)
            {
                var batch = new List<DataValue>();
                for (int jj = ii; jj < Math.Min(to, ii + 500); jj++)
                {
                    batch.Add(Value(jj));
                }
                await provider.InsertAsync(context, nodeId, batch, CancellationToken.None);
            }
        }

        private static async Task<List<int>> ReadAllAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId,
            bool forward,
            int last = 5000)
        {
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = forward ? Time(-1) : Time(last),
                EndTime = forward ? Time(last) : Time(-1),
                IsForward = forward,
                PageLimit = 97
            };
            var result = new List<int>();
            HistorianResumeToken token = default;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context, request, token, CancellationToken.None);
                result.AddRange(Seconds(page));
                if (page.IsFinal)
                {
                    return result;
                }
                token = page.NextToken;
            }
        }

        private static List<int> Seconds(HistorianPage<HistoricalDataValue> page)
        {
            var result = new List<int>(page.Values.Count);
            foreach (HistoricalDataValue value in page.Values)
            {
                if (StatusCode.IsBad(value.Value.StatusCode))
                {
                    continue;
                }
                result.Add((int)Math.Round((value.Value.SourceTimestamp.ToDateTime() - s_baseTime).TotalSeconds));
            }
            return result;
        }

        private static DataValue Value(int second)
        {
            DateTime timestamp = s_baseTime.AddSeconds(second);
            return new DataValue(new Variant((double)second), StatusCodes.Good, timestamp, timestamp);
        }

        private static DateTimeUtc Time(int second)
        {
            return new DateTimeUtc(s_baseTime.AddSeconds(second));
        }

        private static HistorianOperationContext CreateContext()
        {
            var namespaceUris = new NamespaceTable();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            var session = new Mock<ISession>();
            var operation = new OperationContext(
                new RequestHeader(), null!, RequestType.HistoryRead, RequestLifetime.None, session.Object);
            var system = new ServerSystemContext(server.Object, operation);
            return new HistorianOperationContext(system, operation, null, HistoryUpdateType.Insert);
        }
    }
}
