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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

#nullable enable

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Extra gap-coverage tests for <see cref="InMemoryHistorianProvider"/>
    /// targeting branches not yet exercised by
    /// <see cref="InMemoryHistorianProviderTests"/> or
    /// <see cref="InMemoryHistorianProviderGapsTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianExtraTests
    {
        private const ushort Ns = 1;

        private static readonly DateTime BaseTime =
            new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // ─── InsertBatchAsync – null values entry ────────────────────────────

        [Test]
        public async Task InsertBatchAsyncWithNullValuesEntryReturnsEmptyStatusListAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"batch-null-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            var batch = new Dictionary<NodeId, IList<DataValue>>
            {
                [nodeId] = null!       // null values → empty StatusCode array
            };

            IReadOnlyDictionary<NodeId, IList<StatusCode>> result =
                await provider.InsertBatchAsync(ctx, batch, CancellationToken.None);

            Assert.That(result, Contains.Key(nodeId));
            Assert.That(result[nodeId], Is.Empty);
        }

        // ─── DeleteRawAsync – no archive ────────────────────────────────────

        [Test]
        public async Task DeleteRawAsyncReturnsGoodNoDataWhenNodeNotRegisteredAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-raw-noarch-{Guid.NewGuid():N}", Ns);

            HistorianOperationContext ctx = CreateContext();
            StatusCode status = await provider.DeleteRawAsync(
                ctx, nodeId,
                (DateTimeUtc)BaseTime,
                (DateTimeUtc)BaseTime.AddMinutes(1),
                isDeleteModified: false,
                CancellationToken.None);

            Assert.That(status, Is.EqualTo(StatusCodes.GoodNoData));
        }

        // ─── DeleteRawAsync – isDeleteModified=true ──────────────────────────

        [Test]
        public async Task DeleteRawAsyncWithIsDeleteModifiedTrueRemovesFromModifiedLogAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-mod-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            DateTime t1 = BaseTime.AddSeconds(1);

            // Insert a value, then delete it to create a modified-log entry.
            await provider.InsertAsync(ctx, nodeId,
                [MakeValue(t1, 1.0)], CancellationToken.None);
            await provider.DeleteRawAsync(ctx, nodeId,
                (DateTimeUtc)BaseTime, (DateTimeUtc)BaseTime.AddMinutes(1),
                isDeleteModified: false, CancellationToken.None);

            // Now delete from the modified log.
            StatusCode status = await provider.DeleteRawAsync(ctx, nodeId,
                (DateTimeUtc)BaseTime, (DateTimeUtc)BaseTime.AddMinutes(1),
                isDeleteModified: true, CancellationToken.None);

            Assert.That(StatusCode.IsGood(status), Is.True);
        }

        [Test]
        public async Task DeleteRawAsyncWithIsDeleteModifiedTrueReturnsGoodNoDataWhenModifiedLogIsEmptyAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-mod-empty-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            // Seed a raw value but never delete it (no modified-log entries).
            await provider.InsertAsync(ctx, nodeId, [MakeValue(BaseTime.AddSeconds(1), 1.0)], CancellationToken.None);

            StatusCode status = await provider.DeleteRawAsync(ctx, nodeId,
                (DateTimeUtc)BaseTime, (DateTimeUtc)BaseTime.AddMinutes(1),
                isDeleteModified: true, CancellationToken.None);

            Assert.That(status, Is.EqualTo(StatusCodes.GoodNoData));
        }

        // ─── DeleteRawAsync – start > end swap ──────────────────────────────

        [Test]
        public async Task DeleteRawAsyncWithStartGreaterThanEndSwapsAndDeletesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-swap-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            DateTime ts = BaseTime.AddSeconds(5);
            await provider.InsertAsync(ctx, nodeId, [MakeValue(ts, 99.0)], CancellationToken.None);

            // Pass end < start; the dispatcher should swap them internally.
            StatusCode status = await provider.DeleteRawAsync(ctx, nodeId,
                startTime: (DateTimeUtc)BaseTime.AddMinutes(1),
                endTime: (DateTimeUtc)BaseTime,
                isDeleteModified: false,
                CancellationToken.None);

            Assert.That(StatusCode.IsGood(status), Is.True);
        }

        // ─── DeleteAtTimeAsync – no archive ─────────────────────────────────

        [Test]
        public async Task DeleteAtTimeAsyncReturnsAllBadNoEntryExistsWhenNodeNotRegisteredAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-at-noarch-{Guid.NewGuid():N}", Ns);

            HistorianOperationContext ctx = CreateContext();
            IList<StatusCode> statuses = await provider.DeleteAtTimeAsync(
                ctx, nodeId,
                new DateTimeUtc[] { (DateTimeUtc)BaseTime, (DateTimeUtc)BaseTime.AddSeconds(1) },
                CancellationToken.None);

            Assert.That(statuses, Has.Count.EqualTo(2));
            Assert.That(statuses[0], Is.EqualTo(StatusCodes.BadNoEntryExists));
            Assert.That(statuses[1], Is.EqualTo(StatusCodes.BadNoEntryExists));
        }

        // ─── DeleteAnnotationsAsync – no archive ─────────────────────────────

        [Test]
        public async Task DeleteAnnotationsAsyncReturnsAllBadNoEntryExistsWhenNodeNotRegisteredAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"del-ann-noarch-{Guid.NewGuid():N}", Ns);

            HistorianOperationContext ctx = CreateContext();
            IList<StatusCode> statuses = await provider.DeleteAnnotationsAsync(
                ctx, nodeId,
                new DateTimeUtc[] { (DateTimeUtc)BaseTime },
                CancellationToken.None);

            Assert.That(statuses, Has.Count.EqualTo(1));
            Assert.That(statuses[0], Is.EqualTo(StatusCodes.BadNoEntryExists));
        }

        // ─── Annotation paths ────────────────────────────────────────────────

        [Test]
        public async Task InsertAnnotationsAsyncDuplicateKeyReturnsBadEntryExistsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"ann-dup-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            DateTime ts = BaseTime.AddSeconds(1);
            var annotation = new Annotation { AnnotationTime = (DateTimeUtc)ts, Message = "first" };
            await provider.InsertAnnotationsAsync(ctx, nodeId, [annotation], CancellationToken.None);

            // Second insert at same timestamp → BadEntryExists
            IList<StatusCode> statuses =
                await provider.InsertAnnotationsAsync(ctx, nodeId, [annotation], CancellationToken.None);

            Assert.That(statuses[0], Is.EqualTo(StatusCodes.BadEntryExists));
        }

        [Test]
        public async Task ReplaceAnnotationsAsyncReturnsBadNoEntryExistsForNonExistingEntryAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"ann-rep-ne-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            var annotation = new Annotation
            {
                AnnotationTime = (DateTimeUtc)BaseTime.AddSeconds(1),
                Message = "not-there"
            };

            IList<StatusCode> statuses =
                await provider.ReplaceAnnotationsAsync(ctx, nodeId, [annotation], CancellationToken.None);

            Assert.That(statuses[0], Is.EqualTo(StatusCodes.BadNoEntryExists));
        }

        [Test]
        public async Task ReplaceAnnotationsAsyncReplacesExistingEntryAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"ann-rep-ex-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            DateTime ts = BaseTime.AddSeconds(1);
            var original = new Annotation { AnnotationTime = (DateTimeUtc)ts, Message = "original" };
            await provider.InsertAnnotationsAsync(ctx, nodeId, [original], CancellationToken.None);

            var replacement = new Annotation { AnnotationTime = (DateTimeUtc)ts, Message = "replaced" };
            IList<StatusCode> statuses =
                await provider.ReplaceAnnotationsAsync(ctx, nodeId, [replacement], CancellationToken.None);

            Assert.That(statuses[0], Is.EqualTo(StatusCodes.GoodEntryReplaced));
        }

        [Test]
        public async Task UpdateAnnotationsAsyncInsertsOrReplacesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"ann-upd-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            DateTime ts = BaseTime.AddSeconds(1);
            var annotation = new Annotation { AnnotationTime = (DateTimeUtc)ts, Message = "v1" };

            // Update on non-existing → GoodEntryInserted
            IList<StatusCode> statuses1 =
                await provider.UpdateAnnotationsAsync(ctx, nodeId, [annotation], CancellationToken.None);
            Assert.That(statuses1[0], Is.EqualTo(StatusCodes.GoodEntryInserted));

            // Update on existing → GoodEntryReplaced
            var updated = new Annotation { AnnotationTime = (DateTimeUtc)ts, Message = "v2" };
            IList<StatusCode> statuses2 =
                await provider.UpdateAnnotationsAsync(ctx, nodeId, [updated], CancellationToken.None);
            Assert.That(statuses2[0], Is.EqualTo(StatusCodes.GoodEntryReplaced));
        }

        // ─── Backward raw read (exercises lo/hi swap and ReturnBounds) ────────

        [Test]
        public async Task ReadRawBackwardWithReturnBoundsIncludesBoundValuesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId($"raw-bwd-{Guid.NewGuid():N}", Ns);
            provider.Register(nodeId);

            HistorianOperationContext ctx = CreateContext();
            for (int i = 0; i < 5; i++)
            {
                await provider.InsertAsync(ctx, nodeId,
                    [MakeValue(BaseTime.AddSeconds(i), i)], CancellationToken.None);
            }

            // Backward read: start > end + ReturnBounds
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                ctx,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = (DateTimeUtc)BaseTime.AddSeconds(4),
                    EndTime = (DateTimeUtc)BaseTime,
                    MaxValues = 0,
                    IsForward = false,
                    ReturnBounds = true
                },
                default,
                CancellationToken.None);

            // We get values in the window plus potential bounds.
            Assert.That(page.Values, Is.Not.Empty);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static DataValue MakeValue(DateTime timestamp, double value)
        {
            return new DataValue(
                new Variant(value), StatusCodes.Good,
                sourceTimestamp: timestamp,
                serverTimestamp: timestamp);
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
