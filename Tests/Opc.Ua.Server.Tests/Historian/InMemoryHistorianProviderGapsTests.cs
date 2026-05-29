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

// CA1861: inline constant-array expected-values used in test assertions are a clarity win
// for the test author and aren't on a hot path. Disabled file-level for the suite.
#pragma warning disable CA1861
// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    public class InMemoryHistorianProviderGapsTests
    {
        private const ushort NamespaceIndex = 1;

        private static readonly DateTime BaseTime =
            new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void DisposeIsIdempotent()
        {
            var provider = new InMemoryHistorianProvider();

            Assert.DoesNotThrow(() =>
            {
                provider.Dispose();
                provider.Dispose();
            });
        }

        [Test]
        public async Task RegisterTwiceLeavesCapabilitiesUnchangedAsync()
        {
            // Production code: Register always overwrites capabilities
            // (m_capabilities[nodeId] = capabilities ?? default).
            // So a second Register with capsB DOES overwrite capsA.
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("reg-twice", NamespaceIndex);

            var capsA = new HistorianNodeCapabilities { InsertData = true };
            var capsB = new HistorianNodeCapabilities { InsertData = false, DeleteRaw = true };

            provider.Register(nodeId, capsA);
            provider.Register(nodeId, capsB);

            HistorianNodeCapabilities result =
                await provider.GetCapabilitiesAsync(nodeId, CancellationToken.None);

            // Register overwrites: second call wins.
            Assert.That(result.InsertData, Is.EqualTo(capsB.InsertData));
            Assert.That(result.DeleteRaw, Is.EqualTo(capsB.DeleteRaw));
        }

        [Test]
        public void ForgetUnknownNodeReturnsFalse()
        {
            using var provider = new InMemoryHistorianProvider();

            // Forget a node that was never registered.
            bool result = provider.Forget(new NodeId("never-registered", 42));
            Assert.That(result, Is.False);

            // Register, forget once (true), forget again (false).
            var nodeId = new NodeId("forget-me", NamespaceIndex);
            provider.Register(nodeId);
            Assert.That(provider.Forget(nodeId), Is.True);
            Assert.That(provider.Forget(nodeId), Is.False);
        }

        [Test]
        public async Task SetCapabilitiesOverridesPreviousCapabilitiesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("set-caps", NamespaceIndex);

            var capsA = new HistorianNodeCapabilities { InsertData = true };
            var capsB = new HistorianNodeCapabilities { InsertData = false, DeleteRaw = true };

            provider.Register(nodeId, capsA);
            provider.SetCapabilities(nodeId, capsB);

            HistorianNodeCapabilities result =
                await provider.GetCapabilitiesAsync(nodeId, CancellationToken.None);

            Assert.That(result.InsertData, Is.EqualTo(capsB.InsertData));
            Assert.That(result.DeleteRaw, Is.EqualTo(capsB.DeleteRaw));
        }

        [Test]
        public async Task InsertBatchAsyncFanOutsAcrossNodesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeA = new NodeId("batch.a", NamespaceIndex);
            var nodeB = new NodeId("batch.b", NamespaceIndex);
            provider.Register(nodeA);
            provider.Register(nodeB);

            HistorianOperationContext context = CreateContext();

            var batch = new Dictionary<NodeId, IList<DataValue>>
            {
                [nodeA] =
                [
                    MakeValue(BaseTime.AddSeconds(1), 10.0),
                    MakeValue(BaseTime.AddSeconds(2), 20.0),
                    MakeValue(BaseTime.AddSeconds(3), 30.0),
                ],
                [nodeB] =
                [
                    MakeValue(BaseTime.AddSeconds(4), 40.0),
                    MakeValue(BaseTime.AddSeconds(5), 50.0),
                    MakeValue(BaseTime.AddSeconds(6), 60.0),
                ],
            };

            IReadOnlyDictionary<NodeId, IList<StatusCode>> result =
                await provider.InsertBatchAsync(context, batch, CancellationToken.None);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[nodeA], Has.Count.EqualTo(3));
            Assert.That(result[nodeB], Has.Count.EqualTo(3));

            foreach (StatusCode sc in result[nodeA].Concat(result[nodeB]))
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            // Verify values are readable.
            HistorianPage<HistoricalDataValue> pageA = await ReadAll(provider, context, nodeA);
            Assert.That(pageA.Values, Has.Count.EqualTo(3));
            Assert.That(
                Convert.ToDouble(pageA.Values[0].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(10.0));

            HistorianPage<HistoricalDataValue> pageB = await ReadAll(provider, context, nodeB);
            Assert.That(pageB.Values, Has.Count.EqualTo(3));
            Assert.That(
                Convert.ToDouble(pageB.Values[2].Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture),
                Is.EqualTo(60.0));
        }

        [Test]
        public async Task DeleteRawAsyncRemovesValuesInTimeRangeAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("del-raw", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();

            // Insert 5 values at t0..t4 (1s apart).
            DateTime t0 = BaseTime;
            for (int i = 0; i < 5; i++)
            {
                await provider.InsertAsync(
                    context, nodeId, [MakeValue(t0.AddSeconds(i), i)],
                    CancellationToken.None);
            }

            DateTime t1 = t0.AddSeconds(1);
            DateTime t3 = t0.AddSeconds(3);

            // DeleteRawAsync uses [start, end) — start-inclusive, end-exclusive.
            StatusCode status = await provider.DeleteRawAsync(
                context, nodeId, (DateTimeUtc)t1, (DateTimeUtc)t3,
                isDeleteModified: false, CancellationToken.None);

            Assert.That(StatusCode.IsGood(status), Is.True);

            // Read remaining values.
            HistorianPage<HistoricalDataValue> page = await ReadAll(provider, context, nodeId);

            // t1 and t2 deleted ([1,3) range); t0, t3, t4 remain.
            Assert.That(page.Values, Has.Count.EqualTo(3));
            double[] remaining = [.. page.Values.Select(v => Convert.ToDouble(v.Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture))];
            Assert.That(remaining, Is.EqualTo([0.0, 3.0, 4.0]));
        }

        [Test]
        public async Task InsertEventReadEventRoundTripAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("evt-node", NamespaceIndex);

            HistorianOperationContext context = CreateContext();

            var evtId1 = (ByteString)new byte[] { 0x01 };
            var evtId2 = (ByteString)new byte[] { 0x02 };

            var events = new List<HistorianEventRecord>
            {
                new(evtId1, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(10),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("hello") }),
                new(evtId2, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(20),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("world") }),
            };

            IList<StatusCode> insertResult =
                await provider.InsertEventsAsync(context, nodeId, events, CancellationToken.None);
            Assert.That(insertResult, Has.Count.EqualTo(2));
            Assert.That(StatusCode.IsGood(insertResult[0]), Is.True);
            Assert.That(StatusCode.IsGood(insertResult[1]), Is.True);

            HistorianPage<HistorianEventRecord> page = await provider.ReadEventsAsync(
                context,
                new HistorianEventReadRequest
                {
                    NodeId = nodeId,
                    StartTime = (DateTimeUtc)BaseTime,
                    EndTime = (DateTimeUtc)BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = new EventFilter(),
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(2));
            Assert.That(page.Values[0].EventId, Is.EqualTo(evtId1));
            Assert.That(page.Values[1].EventId, Is.EqualTo(evtId2));
        }

        [Test]
        public async Task ReplaceEventReplacesExistingEventAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("evt-replace", NamespaceIndex);

            HistorianOperationContext context = CreateContext();

            var evtId = (ByteString)new byte[] { 0xAA };
            var original = new HistorianEventRecord(
                evtId, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime.AddSeconds(10),
                new Dictionary<string, Variant> { ["Message"] = new Variant("original") });

            await provider.InsertEventsAsync(context, nodeId, [original], CancellationToken.None);

            var replacement = new HistorianEventRecord(
                evtId, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime.AddSeconds(10),
                new Dictionary<string, Variant> { ["Message"] = new Variant("replaced") });

            IList<StatusCode> replaceResult =
                await provider.ReplaceEventsAsync(context, nodeId, [replacement], CancellationToken.None);
            Assert.That(StatusCode.IsGood(replaceResult[0]), Is.True);

            HistorianPage<HistorianEventRecord> page = await provider.ReadEventsAsync(
                context,
                new HistorianEventReadRequest
                {
                    NodeId = nodeId,
                    StartTime = (DateTimeUtc)BaseTime,
                    EndTime = (DateTimeUtc)BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = new EventFilter(),
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(
                page.Values[0].Fields["Message"].ToString(),
                Is.EqualTo("replaced"));
        }

        [Test]
        public async Task DeleteEventsAsyncRemovesByEventIdAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("evt-del", NamespaceIndex);

            HistorianOperationContext context = CreateContext();

            var id1 = (ByteString)new byte[] { 0x01 };
            var id2 = (ByteString)new byte[] { 0x02 };
            var id3 = (ByteString)new byte[] { 0x03 };

            var events = new List<HistorianEventRecord>
            {
                new(id1, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(10),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("a") }),
                new(id2, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(20),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("b") }),
                new(id3, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(30),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("c") }),
            };

            await provider.InsertEventsAsync(context, nodeId, events, CancellationToken.None);

            // Delete the middle event.
            IList<StatusCode> delResult = await provider.DeleteEventsAsync(
                context, nodeId, [id2], CancellationToken.None);
            Assert.That(StatusCode.IsGood(delResult[0]), Is.True);

            HistorianPage<HistorianEventRecord> page = await provider.ReadEventsAsync(
                context,
                new HistorianEventReadRequest
                {
                    NodeId = nodeId,
                    StartTime = (DateTimeUtc)BaseTime,
                    EndTime = (DateTimeUtc)BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = new EventFilter(),
                },
                default,
                CancellationToken.None);

            Assert.That(page.Values, Has.Count.EqualTo(2));

            ByteString[] remainingIds = [.. page.Values.Select(v => v.EventId)];
            Assert.That(remainingIds, Does.Contain(id1));
            Assert.That(remainingIds, Does.Contain(id3));
            Assert.That(remainingIds, Does.Not.Contain(id2));
        }

        [Test]
        public async Task ReplaceAtomicAsyncCommitsAllValuesAsync()
        {
            // UpdateAtomicAsync with HistoryUpdateType.Update never fails
            // for valid (non-null) DataValues — the preflight always returns
            // Good. So we test the commit-all path via ReplaceAtomicAsync
            // instead.
            using var provider = new InMemoryHistorianProvider();
            var nodeId = new NodeId("atomic-replace", NamespaceIndex);
            provider.Register(nodeId);

            HistorianOperationContext context = CreateContext();

            DateTime t1 = BaseTime.AddSeconds(1);
            DateTime t2 = BaseTime.AddSeconds(2);
            DateTime t3 = BaseTime.AddSeconds(3);

            // Pre-insert 3 values.
            await provider.InsertAsync(context, nodeId,
                [MakeValue(t1, 1.0), MakeValue(t2, 2.0), MakeValue(t3, 3.0)],
                CancellationToken.None);

            // Atomically replace all 3 with new values.
            IList<StatusCode> statuses = await provider.ReplaceAtomicAsync(
                context, nodeId,
                [MakeValue(t1, 100.0), MakeValue(t2, 200.0), MakeValue(t3, 300.0)],
                CancellationToken.None);

            Assert.That(statuses, Has.Count.EqualTo(3));
            foreach (StatusCode sc in statuses)
            {
                Assert.That(StatusCode.IsGood(sc), Is.True);
            }

            // Verify values were replaced.
            HistorianPage<HistoricalDataValue> page = await ReadAll(provider, context, nodeId);
            Assert.That(page.Values, Has.Count.EqualTo(3));

            double[] vals = [.. page.Values.Select(v => Convert.ToDouble(v.Value.WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture))];
            Assert.That(vals, Is.EqualTo([100.0, 200.0, 300.0]));
        }

        private static DataValue MakeValue(DateTime sourceTimestamp, double value)
        {
            return new DataValue(
                new Variant(value), StatusCodes.Good,
                sourceTimestamp: sourceTimestamp,
                serverTimestamp: sourceTimestamp);
        }

        private static ValueTask<HistorianPage<HistoricalDataValue>> ReadAll(
            InMemoryHistorianProvider provider,
            HistorianOperationContext context,
            NodeId nodeId)
        {
            return provider.ReadRawAsync(
                context,
                new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddHours(1),
                    MaxValues = 0,
                    IsForward = true,
                    ReturnBounds = false,
                },
                default,
                CancellationToken.None);
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
