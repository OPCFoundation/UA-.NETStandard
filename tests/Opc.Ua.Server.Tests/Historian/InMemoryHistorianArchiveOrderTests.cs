/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

        private static async Task<List<int>> ReadAllAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId,
            bool forward)
        {
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = forward ? Time(-1) : Time(5000),
                EndTime = forward ? Time(5000) : Time(-1),
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
