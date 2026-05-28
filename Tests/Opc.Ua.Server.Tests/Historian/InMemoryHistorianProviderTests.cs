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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianProviderTests
    {
        private const ushort NamespaceIndex = 1;

        [Test]
        public async Task InsertAsyncStoresValuesAndRawReadReturnsThemAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("test.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>
            {
                MakeValue(BaseTime.AddSeconds(10), 1.0),
                MakeValue(BaseTime.AddSeconds(20), 2.0),
                MakeValue(BaseTime.AddSeconds(30), 3.0),
            };

            IList<StatusCode> insertStatuses = await provider.InsertAsync(
                context, nodeId, values, CancellationToken.None);
            Assert.That(insertStatuses, Has.Count.EqualTo(3));
            foreach (StatusCode sc in insertStatuses)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = false,
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(3));
            Assert.That(page.IsFinal, Is.True);
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(1.0));
            Assert.That(
                Convert.ToDouble(page.Values[2].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(3.0));
        }

        [Test]
        public async Task InsertRejectsDuplicateSourceTimestampAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("dup.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);
            IList<StatusCode> first = await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.0)], CancellationToken.None);
            Assert.That(StatusCode.IsGood(first[0]), Is.True);

            IList<StatusCode> second = await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.5)], CancellationToken.None);
            Assert.That((uint)second[0].Code, Is.EqualTo(StatusCodes.BadEntryExists.Code));
        }

        [Test]
        public async Task ReplaceFailsWhenNoEntryExistsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("rep.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            IList<StatusCode> statuses = await provider.ReplaceAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddSeconds(15), 42.0)],
                CancellationToken.None);

            Assert.That((uint)statuses[0].Code, Is.EqualTo(StatusCodes.BadNoEntryExists.Code));
        }

        [Test]
        public async Task UpdateUpsertsAndLogsModificationAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("up.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);

            await provider.InsertAsync(
                context, nodeId, [MakeValue(when, 1.0)], CancellationToken.None);
            await provider.UpdateAsync(
                context, nodeId, [MakeValue(when, 2.0)], CancellationToken.None);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                },
                default,
                CancellationToken.None);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(2.0));

            HistorianPage<ModifiedDataValue> mod = await provider.ReadModifiedAsync(
                context,
                new HistorianModifiedReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                },
                default,
                CancellationToken.None);
            Assert.That(mod.Values, Has.Count.EqualTo(1));
            Assert.That(mod.Values[0].Info.UpdateType, Is.EqualTo(HistoryUpdateType.Update));
        }

        [Test]
        public async Task DeleteAtTimeRemovesEntriesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("del.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            await provider.InsertAsync(context, nodeId,
                [MakeValue(BaseTime.AddSeconds(10), 1.0), MakeValue(BaseTime.AddSeconds(20), 2.0)],
                CancellationToken.None);

            IList<StatusCode> result = await provider.DeleteAtTimeAsync(
                context, nodeId, [(DateTimeUtc)BaseTime.AddSeconds(10)], CancellationToken.None);
            Assert.That(StatusCode.IsGood(result[0]), Is.True);

            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                },
                default,
                CancellationToken.None);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                Convert.ToDouble(page.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(2.0));
        }

        [Test]
        public async Task PaginationFollowsResumeTokensAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("page.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            var values = new List<DataValue>(50);
            for (int i = 0; i < 50; i++)
            {
                values.Add(MakeValue(BaseTime.AddSeconds(i), i));
            }
            await provider.InsertAsync(context, nodeId, values, CancellationToken.None);

            uint pageSize = 10;
            var allReturned = new List<DataValue>();
            HistorianResumeToken token = default;
            int pages = 0;
            while (true)
            {
                HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                    context,
                    new HistorianRawReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = BaseTime,
                        EndTime = BaseTime.AddMinutes(2),
                        MaxValues = pageSize,
                        IsForward = true,
                    },
                    token,
                    CancellationToken.None);
                foreach (HistoricalDataValue v in page.Values)
                {
                    allReturned.Add(v.Value);
                }
                pages++;
                if (page.IsFinal)
                {
                    break;
                }
                token = page.NextToken;
                Assert.That(pages, Is.LessThan(20), "Pagination did not terminate.");
            }
            Assert.That(allReturned, Has.Count.EqualTo(50));
            Assert.That(
                Convert.ToInt32(allReturned[0].WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.Zero);
            Assert.That(
                Convert.ToInt32(allReturned[^1].WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(49));
        }

        [Test]
        public async Task AnnotationLifecycleAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("ann.var", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();
            DateTime when = BaseTime.AddSeconds(10);
            var annotation = new Annotation { Message = "test", UserName = "alice", AnnotationTime = when };

            IList<StatusCode> insert = await provider.InsertAnnotationsAsync(
                context, nodeId, [annotation], CancellationToken.None);
            Assert.That(StatusCode.IsGood(insert[0]), Is.True);

            HistorianPage<Annotation> page = await provider.ReadAnnotationsAsync(
                context,
                new HistorianAnnotationReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                },
                default,
                CancellationToken.None);
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Message, Is.EqualTo("test"));

            IList<StatusCode> del = await provider.DeleteAnnotationsAsync(
                context, nodeId, [(DateTimeUtc)when], CancellationToken.None);
            Assert.That(StatusCode.IsGood(del[0]), Is.True);
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
