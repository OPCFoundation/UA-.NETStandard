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

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

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
    public class HistorianDispatcherNullGuardTests
    {
        [Test]
        public async Task DispatchUpdateDataAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateDataAsync(
                null!, h.Provider, h.Node, h.UpdateDataDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, null!, h.Node, h.UpdateDataDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, null!, h.UpdateDataDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, h.Node, h.UpdateDataDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchDeleteRawAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteRawAsync(
                null!, h.Provider, h.Node, h.DeleteRawDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, null!, h.Node, h.DeleteRawDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, h.Provider, null!, h.DeleteRawDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, h.Provider, h.Node, h.DeleteRawDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchDeleteAtTimeAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteAtTimeAsync(
                null!, h.Provider, h.Node, h.DeleteAtTimeDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, null!, h.Node, h.DeleteAtTimeDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, h.Provider, null!, h.DeleteAtTimeDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, h.Provider, h.Node, h.DeleteAtTimeDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchProcessedReadAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                null!, h.Provider, h.Node, h.NodeToRead, h.ProcessedDetails,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, null!, h.Node, h.NodeToRead, h.ProcessedDetails,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, null!, h.NodeToRead, h.ProcessedDetails,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.ProcessedDetails,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, null!,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, h.ProcessedDetails,
                ObjectIds.AggregateFunction_Count, TimestampsToReturn.Source, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchAtTimeReadAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                null!, h.Provider, h.Node, h.NodeToRead, h.AtTimeDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, null!, h.Node, h.NodeToRead, h.AtTimeDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, null!, h.NodeToRead, h.AtTimeDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.AtTimeDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, null!,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, h.AtTimeDetails,
                TimestampsToReturn.Source, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchAnnotationReadAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                null!, h.Provider, h.Node, h.NodeToRead, h.RawReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext, null!, h.Node, h.NodeToRead, h.RawReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext, h.Provider, null!, h.NodeToRead, h.RawReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.RawReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, null!,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, h.RawReadDetails,
                TimestampsToReturn.Source, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchAnnotationUpdateAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationUpdateAsync(
                null!, h.Provider, h.Node, h.StructureUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext, null!, h.Node, h.StructureUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext, h.Provider, null!, h.StructureUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext, h.Provider, h.Node, h.StructureUpdateDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchEventReadAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                null!, h.Provider, h.Node, h.NodeToRead, h.EventReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, null!, h.Node, h.NodeToRead, h.EventReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, h.Provider, null!, h.NodeToRead, h.EventReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.EventReadDetails,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, null!,
                TimestampsToReturn.Source, h.ReadResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, h.Provider, h.Node, h.NodeToRead, h.EventReadDetails,
                TimestampsToReturn.Source, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchUpdateEventAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateEventAsync(
                null!, h.Provider, h.Node, h.EventUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, null!, h.Node, h.EventUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, h.Provider, null!, h.EventUpdateDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, h.Provider, h.Node, h.EventUpdateDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchDeleteEventsAsyncWithNullArgumentsThrowsAsync()
        {
            Fixture h = CreateFixture();

            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteEventsAsync(
                null!, h.Provider, h.Node, h.DeleteEventDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, null!, h.Node, h.DeleteEventDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, h.Provider, null!, h.DeleteEventDetails, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, h.Provider, h.Node, null!, h.UpdateResult, CancellationToken.None).AsTask()).ConfigureAwait(false);
            await AssertArgumentNullAsync(() => HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, h.Provider, h.Node, h.DeleteEventDetails, null!, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        private static Task AssertArgumentNullAsync(Func<Task> action)
        {
            return Assert.ThatAsync(action, Throws.TypeOf<ArgumentNullException>());
        }

        private static Fixture CreateFixture()
        {
            return new Fixture();
        }

        private sealed class Fixture
        {
            private static readonly DateTime BaseTime = new(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);

            public Fixture()
            {
                var mockServer = new Mock<IServerInternal>();
                mockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                mockServer.Setup(s => s.Telemetry).Returns(new Mock<ITelemetryContext>().Object);

                var opContext = new OperationContext(
                    new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
                SystemContext = new ServerSystemContext(mockServer.Object, opContext);
                Provider = new InMemoryHistorianProvider();
                Node = new BaseDataVariableState(null)
                {
                    NodeId = NodeId,
                    BrowseName = new QualifiedName("HistorianDispatcherNullGuard"),
                    AccessLevel = AccessLevels.HistoryReadOrWrite,
                    Historizing = true
                };
                NodeToRead = new HistoryReadValueId
                {
                    NodeId = NodeId,
                    ContinuationPoint = ByteString.Empty
                };
            }

            public ServerSystemContext SystemContext { get; }

            public InMemoryHistorianProvider Provider { get; }

            public BaseDataVariableState Node { get; }

            public HistoryReadValueId NodeToRead { get; }

            public ReadRawModifiedDetails RawReadDetails { get; } = new()
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1)
            };

            public UpdateDataDetails UpdateDataDetails { get; } = new()
            {
                NodeId = NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = []
            };

            public DeleteRawModifiedDetails DeleteRawDetails { get; } = new()
            {
                NodeId = NodeId,
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1)
            };

            public DeleteAtTimeDetails DeleteAtTimeDetails { get; } = new()
            {
                NodeId = NodeId,
                ReqTimes = []
            };

            public ReadProcessedDetails ProcessedDetails { get; } = new()
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                ProcessingInterval = 1000
            };

            public ReadAtTimeDetails AtTimeDetails { get; } = new()
            {
                ReqTimes = []
            };

            public UpdateStructureDataDetails StructureUpdateDetails { get; } = new()
            {
                NodeId = NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = []
            };

            public ReadEventDetails EventReadDetails { get; } = new()
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                Filter = new EventFilter()
            };

            public UpdateEventDetails EventUpdateDetails { get; } = new()
            {
                NodeId = NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = new EventFilter(),
                EventData = []
            };

            public DeleteEventDetails DeleteEventDetails { get; } = new()
            {
                NodeId = NodeId,
                EventIds = []
            };

            public HistoryReadResult ReadResult { get; } = new();

            public HistoryUpdateResult UpdateResult { get; } = new();

            private static NodeId NodeId { get; } = new("dispatcher-null-guards", 1);
        }
    }
}
