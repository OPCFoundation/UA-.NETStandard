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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies independent client quotas, server page limits, and annotation identities.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianReviewPagingTests
    {
        [TestCase(2u, 5u, false, false, 11)]
        [TestCase(2u, 5u, true, false, 11)]
        [TestCase(5u, 2u, false, true, 5)]
        [TestCase(5u, 2u, true, true, 5)]
        [TestCase(4u, 2u, false, true, 4)]
        [TestCase(4u, 2u, true, true, 4)]
        [TestCase(1u, 5u, false, true, 1)]
        [TestCase(1u, 5u, true, true, 1)]
        [TestCase(0u, 2u, false, false, 11)]
        [TestCase(0u, 2u, true, false, 11)]
        [TestCase(3u, 0u, false, true, 3)]
        [TestCase(3u, 0u, true, true, 3)]
        [TestCase(20u, 2u, false, true, 11)]
        [TestCase(20u, 2u, true, true, 11)]
        public async Task RawPagingHonorsClientAndServerLimitsAsync(
            uint maxValues,
            uint pageLimit,
            bool reverse,
            bool openEnded,
            int expectedCount)
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero
            });
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.raw.pages", 1);
            provider.Register(nodeId);
            var input = new List<DataValue>();
            for (int i = 0; i < 11; i++)
            {
                input.Add(new DataValue(i, StatusCodes.Good, s_start.AddSeconds(i)));
            }
            await provider.InsertBatchAsync(
                context,
                [new HistorianDataBatch(nodeId, input.ToArrayOf())],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = openEnded && reverse ? DateTimeUtc.MinValue : s_start.AddTicks(-1),
                EndTime = openEnded && !reverse ? DateTimeUtc.MaxValue : s_start.AddSeconds(11),
                IsForward = !reverse,
                MaxValues = maxValues,
                PageLimit = pageLimit
            };

            var actual = new List<int>();
            HistorianResumeToken token = default;
            do
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    request,
                    token,
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(page.Values, Has.Count.LessThanOrEqualTo(1000));
                if (maxValues > 0)
                {
                    Assert.That(page.Values, Has.Count.LessThanOrEqualTo(maxValues));
                }
                if (pageLimit > 0)
                {
                    Assert.That(page.Values, Has.Count.LessThanOrEqualTo(pageLimit));
                }
                foreach (HistoricalDataValue value in page.Values)
                {
                    Assert.That(value.Value.WrappedValue.TryGetValue(out int number), Is.True);
                    actual.Add(number);
                }
                Assert.That(actual, Has.Count.LessThanOrEqualTo(expectedCount));
                Assert.That(page.IsFinal || page.Values.Count > 0, Is.True, "A cursor must make progress.");
                token = new HistorianResumeToken(ByteString.From(page.NextToken.State.Span));
            }
            while (!token.IsEmpty);

            IEnumerable<int> expected = reverse ? Enumerable.Range(0, 11).Reverse() : Enumerable.Range(0, 11);
            Assert.That(actual, Is.EqualTo(expected.Take(expectedCount)));
        }

        private static HistorianOperationContext CreateContext()
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.SetupGet(s => s.ServerUris).Returns(new StringTable());
            server.SetupGet(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.Telemetry).Returns(Mock.Of<ITelemetryContext>());
            var operationContext = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            var systemContext = new ServerSystemContext(server.Object, operationContext);
            return new HistorianOperationContext(systemContext, operationContext, null, HistoryUpdateType.Insert);
        }

        private static readonly DateTime s_start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
