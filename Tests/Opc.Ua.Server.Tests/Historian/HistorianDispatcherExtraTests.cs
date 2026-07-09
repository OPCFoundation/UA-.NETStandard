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

// CA2000: test code; disposables are short-lived or ownership-transferred.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext.
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
    /// Extra gap-coverage tests for <see cref="HistorianDispatcher"/> targeting
    /// null-argument guards, modified-data reads, and interpolation edge-cases
    /// not covered by existing tests or <see cref="HistorianDispatcherGapsTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianDispatcherExtraTests
    {
        private static readonly DateTime BaseTime = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // ─── DispatchRawReadAsync null-arg guards ────────────────────────────

        [Test]
        public void DispatchRawReadAsyncThrowsWhenProviderIsNull()
        {
            HarnessFixture h = CreateHarness();
            var node = CreateVariable(new NodeId("rrn-prov", 1));

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext, null!, node,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DispatchRawReadAsyncThrowsWhenNodeIsNull()
        {
            HarnessFixture h = CreateHarness();

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext, h.Provider, null!,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DispatchRawReadAsyncThrowsWhenNodeToReadIsNull()
        {
            HarnessFixture h = CreateHarness();
            var node = CreateVariable(new NodeId("rrn-ntr", 1));

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext, h.Provider, node, null!,
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DispatchRawReadAsyncThrowsWhenDetailsIsNull()
        {
            HarnessFixture h = CreateHarness();
            var node = CreateVariable(new NodeId("rrn-det", 1));

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext, h.Provider, node,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty },
                    null!,
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void DispatchRawReadAsyncThrowsWhenResultIsNull()
        {
            HarnessFixture h = CreateHarness();
            var node = CreateVariable(new NodeId("rrn-res", 1));

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext, h.Provider, node,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    null!,
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        // ─── Modified data read (IsReadModified = true) ──────────────────────

        [Test]
        public async Task DispatchRawReadAsyncWithIsReadModifiedReturnsModifiedDataAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"modified-read-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            DateTime t1 = BaseTime.AddSeconds(1);
            // Insert then delete to create a modified-log entry.
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(10.0), StatusCodes.Good, t1, t1)],
                CancellationToken.None).ConfigureAwait(false);
            await h.Provider.DeleteRawAsync(ctx, nodeId,
                (DateTimeUtc)BaseTime,
                (DateTimeUtc)BaseTime.AddMinutes(1),
                isDeleteModified: false,
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            var details = new ReadRawModifiedDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                IsReadModified = true
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchRawReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryModifiedData? md), Is.True);
            Assert.That(md!.DataValues, Is.Not.Empty);
        }

        // ─── AtTime read – interpolation branches ────────────────────────────

        [Test]
        public async Task DispatchAtTimeReadAsyncWithExactMatchReturnsExactValueAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"at-exact-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            DateTime t = BaseTime.AddSeconds(10);
            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(77.0), StatusCodes.Good, t, t)],
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { (DateTimeUtc)t },
                UseSimpleBounds = false
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(StatusCode.IsGood(values[0].StatusCode), Is.True);
        }

        [Test]
        public async Task DispatchAtTimeReadAsyncWithNoDataReturnsNoDataStatusAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"at-nodata-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            var node = CreateVariable(nodeId);
            // No values inserted — both before and after are null.
            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { (DateTimeUtc)BaseTime.AddSeconds(5) },
                UseSimpleBounds = false
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.BadNoData));
        }

        [Test]
        public async Task DispatchAtTimeReadAsyncWithSimpleBoundsAndOnlyAfterValueReturnsLastUsableValueAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"at-simpbound-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            DateTime tAfter = BaseTime.AddSeconds(20);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(50.0), StatusCodes.Good, tAfter, tAfter)],
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            // Requested time is before the only data point; "before" will be null.
            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { (DateTimeUtc)BaseTime.AddSeconds(5) },
                UseSimpleBounds = true
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0].StatusCode,
                Is.EqualTo(StatusCodes.UncertainNoCommunicationLastUsableValue));
        }

        [Test]
        public async Task DispatchAtTimeReadAsyncWithNonNumericValueFallsBackToLastUsableValueAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"at-nonnum-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            DateTime t1 = BaseTime.AddSeconds(5);
            DateTime t2 = BaseTime.AddSeconds(15);
            // Insert string values — non-numeric → Convert.ToDouble throws InvalidCastException.
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant("hello"), StatusCodes.Good, t1, t1)],
                CancellationToken.None).ConfigureAwait(false);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant("world"), StatusCodes.Good, t2, t2)],
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { (DateTimeUtc)BaseTime.AddSeconds(10) },
                UseSimpleBounds = false
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Has.Length.EqualTo(1));
            Assert.That(values[0].StatusCode,
                Is.EqualTo(StatusCodes.UncertainNoCommunicationLastUsableValue));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static HarnessFixture CreateHarness()
        {
            return new();
        }

        // ─── DispatchRawReadAsync null systemContext guard ───────────────────

        [Test]
        public void DispatchRawReadAsyncThrowsWhenSystemContextIsNull()
        {
            HarnessFixture h = CreateHarness();
            var node = CreateVariable(new NodeId("rrn-sc", 1));

            Assert.That(() =>
                HistorianDispatcher.DispatchRawReadAsync(
                    null!, h.Provider, node,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None).AsTask(), Throws.TypeOf<ArgumentNullException>());
        }

        // ─── ReleaseContinuationPoint null-arg guards ────────────────────────

        [Test]
        public void ReleaseContinuationPointThrowsWhenSystemContextIsNull()
        {
            Assert.That(() =>
                HistorianDispatcher.ReleaseContinuationPoint(
                    null!,
                    new HistoryReadValueId { ContinuationPoint = ByteString.Empty }),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ReleaseContinuationPointThrowsWhenNodeToReadIsNull()
        {
            HarnessFixture h = CreateHarness();

            Assert.That(() =>
                HistorianDispatcher.ReleaseContinuationPoint(
                    h.SystemContext,
                    null!), Throws.TypeOf<ArgumentNullException>());
        }

        // ─── TimestampsToReturn.Neither clears sourceTimestamp ───────────────

        [Test]
        public async Task DispatchRawReadAsyncWithTimestampsNeitherClearsTimestampsAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"ts-neither-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            DateTime t1 = BaseTime.AddSeconds(1);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(42.0), StatusCodes.Good, t1, t1)],
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            var details = new ReadRawModifiedDetails
            {
                StartTime = (DateTimeUtc)BaseTime,
                EndTime = (DateTimeUtc)BaseTime.AddMinutes(1),
                NumValuesPerNode = 100,
                IsReadModified = false
            };
            var result = new HistoryReadResult();

            ServiceResult err = await HistorianDispatcher.DispatchRawReadAsync(
                h.SystemContext, h.Provider, node,
                new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = ByteString.Empty },
                details,
                TimestampsToReturn.Neither,
                result,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(err), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Has.Length.GreaterThan(0));
            // Both timestamps should be cleared (set to MinValue) for TimestampsToReturn.Neither
            Assert.That(values![0].SourceTimestamp, Is.EqualTo(DateTime.MinValue));
        }

        // ─── DataEncoding unsupported path ───────────────────────────────────

        [Test]
        public async Task DispatchRawReadAsyncWithDataEncodingReturnsBadEncodingAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"enc-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            DateTime t1 = BaseTime.AddSeconds(1);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(99.0), StatusCodes.Good, t1, t1)],
                CancellationToken.None).ConfigureAwait(false);

            var node = CreateVariable(nodeId);
            var details = new ReadRawModifiedDetails
            {
                StartTime = (DateTimeUtc)BaseTime,
                EndTime = (DateTimeUtc)BaseTime.AddMinutes(1),
                NumValuesPerNode = 100,
                IsReadModified = false
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty,
                DataEncoding = new QualifiedName("DefaultBinary")
            };
            var result = new HistoryReadResult();

            ServiceResult err = await HistorianDispatcher.DispatchRawReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead,
                details,
                TimestampsToReturn.Source,
                result,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(err), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            // The value should come back with BadDataEncodingUnsupported status.
            Assert.That(values, Has.Length.GreaterThan(0));
            Assert.That(StatusCode.IsBad(values![0].StatusCode), Is.True);
        }

        private static BaseDataVariableState CreateVariable(NodeId nodeId)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("TestVar"),
                AccessLevel = AccessLevels.HistoryReadOrWrite,
                Historizing = true
            };
        }

        private sealed class HarnessFixture
        {
            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }

            public HarnessFixture()
            {
                Provider = new InMemoryHistorianProvider();

                var mockTelemetry = new Mock<ITelemetryContext>();
                var continuationStore = new Dictionary<Guid, object>();

                var mockSession = new Mock<ISession>();
                mockSession
                    .Setup(s => s.SaveHistoryContinuationPoint(It.IsAny<Guid>(), It.IsAny<object>()))
                    .Callback<Guid, object>((id, cp) => continuationStore[id] = cp);
                mockSession
                    .Setup(s => s.RestoreHistoryContinuationPoint(It.IsAny<ByteString>()))
                    .Returns<ByteString>(bs =>
                    {
                        if (bs.Length != 16)
                        {
                            return null;
                        }
                        var id = new Guid(bs.ToArray());
                        if (continuationStore.TryGetValue(id, out object? state))
                        {
                            continuationStore.Remove(id);
                            return state;
                        }
                        return null;
                    });

                var mockServer = new Mock<IServerInternal>();
                mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                var opContext = new OperationContext(
                    new RequestHeader(), null!, RequestType.HistoryRead,
                    RequestLifetime.None, mockSession.Object);
                SystemContext = new ServerSystemContext(mockServer.Object, opContext);
            }

            public static HistorianOperationContext CreateContext(ServerSystemContext systemContext)
            {
                return new HistorianOperationContext(
                    systemContext,
                    systemContext.OperationContext!,
                    null,
                    HistoryUpdateType.Insert);
            }
        }
    }
}
