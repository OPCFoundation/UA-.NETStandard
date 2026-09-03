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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianRetentionTests
    {
        private const ushort NamespaceIndex = 1;

        private static readonly DateTime BaseTime =
            new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void DefaultOptionsRetainOneHourOfRawData()
        {
            var options = new InMemoryHistorianOptions();

            Assert.That(options.RawDataRetentionPeriod, Is.EqualTo(TimeSpan.FromHours(1)));
        }

        [Test]
        public void NegativeRawDataRetentionPeriodIsRejected()
        {
            var options = new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromTicks(-1)
            };

            Assert.That(
                () => new InMemoryHistorianProvider(options),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public async Task DefaultRetentionEvictsSamplesOlderThanOneHourAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("retention.default", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime, 1),
                    MakeValue(BaseTime.AddMinutes(30), 2),
                    MakeValue(BaseTime.AddHours(1), 3),
                    MakeValue(BaseTime.AddHours(1).AddTicks(1), 4)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(3));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(30)));
        }

        [Test]
        public async Task ZeroRetentionPeriodPreservesUnboundedRawDataAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero
            });
            var nodeId = new NodeId("retention.unbounded", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(BaseTime, 1), MakeValue(BaseTime.AddDays(1), 2)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task RetentionPeriodAndSampleCapBothApplyAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromHours(1),
                MaxSamplesPerNode = 2
            });
            var nodeId = new NodeId("retention.combined", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime, 1),
                    MakeValue(BaseTime.AddMinutes(30), 2),
                    MakeValue(BaseTime.AddMinutes(60), 3),
                    MakeValue(BaseTime.AddMinutes(90), 4)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(60)));
        }

        [Test]
        public async Task BulkInsertUsesNewestTimestampForRetentionAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromMinutes(10)
            });
            var nodeId = new NodeId("retention.bulk", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            ArrayOf<HistorianDataBatch> batch =
            [
                new(
                    nodeId,
                [
                    MakeValue(BaseTime.AddMinutes(20), 3),
                    MakeValue(BaseTime, 1),
                    MakeValue(BaseTime.AddMinutes(10), 2)
                ])
            ];

            await provider.InsertBatchAsync(
                context,
                batch,
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(10)));
        }

        [Test]
        public async Task AtomicInsertEnforcesRetentionAfterCommitAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromSeconds(2)
            });
            var nodeId = new NodeId("retention.atomic", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAtomicAsync(
                context,
                nodeId,
                [
                    MakeValue(BaseTime.AddSeconds(1), 1),
                    MakeValue(BaseTime.AddSeconds(2), 2),
                    MakeValue(BaseTime.AddSeconds(4), 4)
                ],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddSeconds(2)));
        }

        [Test]
        public async Task OutOfOrderInsertOlderThanWindowIsImmediatelyEvictedAsync()
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromMinutes(10)
            });
            var nodeId = new NodeId("retention.outoforder", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();

            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddMinutes(20), 2)],
                CancellationToken.None).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> outcome = await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddMinutes(5), 1)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(outcome.OperationResults[0], Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(20)));
        }

        [Test]
        public async Task DeleteAtTimeRefreshesLatestTimestampForRetentionAsync()
        {
            using InMemoryHistorianProvider provider = CreateTenMinuteProvider();
            var nodeId = new NodeId("retention.deleteattime", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            DateTime earlier = BaseTime.AddMinutes(10);
            DateTime latest = BaseTime.AddMinutes(20);

            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(earlier, 1), MakeValue(latest, 2)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.DeleteAtTimeAsync(
                context,
                nodeId,
                [(DateTimeUtc)latest],
                CancellationToken.None).ConfigureAwait(false);
            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddMinutes(5), 0)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(5)));
        }

        [Test]
        public async Task DeleteRawRefreshesLatestTimestampForRetentionAsync()
        {
            using InMemoryHistorianProvider provider = CreateTenMinuteProvider();
            var nodeId = new NodeId("retention.deleteraw", NamespaceIndex);
            provider.Register(nodeId);
            HistorianOperationContext context = CreateContext();
            DateTime earlier = BaseTime.AddMinutes(10);
            DateTime latest = BaseTime.AddMinutes(20);

            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(earlier, 1), MakeValue(latest, 2)],
                CancellationToken.None).ConfigureAwait(false);
            await provider.DeleteRawAsync(
                context,
                nodeId,
                (DateTimeUtc)latest,
                (DateTimeUtc)latest.AddTicks(1),
                isDeleteModified: false,
                CancellationToken.None).ConfigureAwait(false);
            await provider.InsertAsync(
                context,
                nodeId,
                [MakeValue(BaseTime.AddMinutes(5), 0)],
                CancellationToken.None).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> page =
                await ReadAllAsync(provider, context, nodeId).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(
                page.Values[0].Value.SourceTimestamp.ToDateTime(),
                Is.EqualTo(BaseTime.AddMinutes(5)));
        }

        private static InMemoryHistorianProvider CreateTenMinuteProvider()
        {
            return new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.FromMinutes(10)
            });
        }

        private static async Task<HistorianPage<HistoricalDataValue>> ReadAllAsync(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId)
        {
            return await provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime.AddDays(-1),
                    EndTime = BaseTime.AddDays(2),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
        }

        private static DataValue MakeValue(DateTime timestamp, double value)
        {
            return new DataValue(
                new Variant(value),
                StatusCodes.Good,
                sourceTimestamp: timestamp,
                serverTimestamp: timestamp);
        }

        private static HistorianOperationContext CreateContext()
        {
            var telemetry = new Mock<ITelemetryContext>();
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry.Object);

            var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryUpdate,
                RequestLifetime.None);
            var systemContext = new ServerSystemContext(server.Object, operationContext);
            return new HistorianOperationContext(
                systemContext,
                operationContext,
                null,
                HistoryUpdateType.Insert);
        }
    }
}
