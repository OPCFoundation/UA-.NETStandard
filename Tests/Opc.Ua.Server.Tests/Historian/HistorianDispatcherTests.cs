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
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianDispatcherTests
    {
        [Test]
        public async Task PagedRawReadKeepsStableCursorGuidAsync()
        {
            HarnessFixture h = CreateHarness();
            NodeId nodeId = h.SeedSamples(100);

            var variable = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("PagedVar"),
                AccessLevel = AccessLevels.HistoryRead,
                Historizing = true,
            };

            var details = new ReadRawModifiedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddMinutes(5),
                NumValuesPerNode = 10,
                IsReadModified = false,
            };

            ByteString cp = ByteString.Empty;
            Guid? firstCursor = null;
            int totalValues = 0;
            int pages = 0;

            while (true)
            {
                var nodeToRead = new HistoryReadValueId
                {
                    NodeId = nodeId,
                    ContinuationPoint = cp,
                };
                var result = new HistoryReadResult();

                ServiceResult error = await HistorianDispatcher.DispatchRawReadAsync(
                    h.SystemContext,
                    h.Provider,
                    variable,
                    nodeToRead,
                    details,
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None);

                Assert.That(ServiceResult.IsGood(error), Is.True);

                if (result.HistoryData.TryGetValue(out HistoryData? hd))
                {
                    DataValue[]? values = hd.DataValues.ToArray();
                    if (values != null)
                    {
                        totalValues += values.Length;
                    }
                }

                pages++;

                if (result.ContinuationPoint.IsEmpty)
                {
                    break;
                }

                Guid currentCursor = new(result.ContinuationPoint.ToArray());
                firstCursor ??= currentCursor;
                Assert.That(currentCursor, Is.EqualTo(firstCursor),
                    "Cursor Guid must remain stable across paged reads.");

                cp = result.ContinuationPoint;
                Assert.That(pages, Is.LessThan(50), "Pagination did not terminate.");
            }

            Assert.That(totalValues, Is.EqualTo(100));
            Assert.That(pages, Is.GreaterThan(1), "Test should exercise multiple pages.");
        }

        [Test]
        public async Task AnnotationUpdateDispatchInsertsAndDeletesAsync()
        {
            HarnessFixture h = CreateHarness();
            var parentNodeId = new NodeId("parent.var", 1);
            h.Provider.Register(parentNodeId);

            var parent = new BaseDataVariableState(null)
            {
                NodeId = parentNodeId,
                BrowseName = new QualifiedName("ParentVar"),
                AccessLevel = AccessLevels.HistoryReadOrWrite,
                Historizing = true,
            };

            DateTime when = HarnessFixture.BaseTime.AddSeconds(5);
            var annotation = new Annotation
            {
                Message = "comment",
                UserName = "tester",
                AnnotationTime = when,
            };

            var insertDetails = new UpdateStructureDataDetails
            {
                NodeId = new NodeId("parent.var.Annotations", 1),
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(new ExtensionObject(annotation)), StatusCodes.Good, sourceTimestamp: when, serverTimestamp: DateTimeUtc.MinValue),
                },
            };

            var insertResult = new HistoryUpdateResult();
            ServiceResult insertError = await HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext,
                h.Provider,
                parent,
                insertDetails,
                insertResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(insertError), Is.True);
            Assert.That(insertResult.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertResult.OperationResults[0]), Is.True);

            // Now delete via PerformUpdateType.Remove
            var removeDetails = new UpdateStructureDataDetails
            {
                NodeId = insertDetails.NodeId,
                PerformInsertReplace = PerformUpdateType.Remove,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(new ExtensionObject(annotation)), StatusCodes.Good, sourceTimestamp: when, serverTimestamp: DateTimeUtc.MinValue),
                },
            };

            var removeResult = new HistoryUpdateResult();
            ServiceResult removeError = await HistorianDispatcher.DispatchAnnotationUpdateAsync(
                h.SystemContext,
                h.Provider,
                parent,
                removeDetails,
                removeResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(removeError), Is.True);
            Assert.That(removeResult.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(removeResult.OperationResults[0]), Is.True);
        }

        [Test]
        public async Task AnnotationReadDispatchReturnsAnnotationsAsync()
        {
            HarnessFixture h = CreateHarness();
            var parentNodeId = new NodeId("parent.var", 1);
            h.Provider.Register(parentNodeId);

            HistorianOperationContext insertContext = HarnessFixture.CreateContext(h.SystemContext);
            DateTime when = HarnessFixture.BaseTime.AddSeconds(5);
            var annotation = new Annotation
            {
                Message = "via-direct-insert",
                UserName = "tester",
                AnnotationTime = when,
            };
            await h.Provider.InsertAnnotationsAsync(
                insertContext, parentNodeId, [annotation], CancellationToken.None);

            var parent = new BaseDataVariableState(null)
            {
                NodeId = parentNodeId,
                BrowseName = new QualifiedName("ParentVar"),
                AccessLevel = AccessLevels.HistoryRead,
                Historizing = true,
            };

            var details = new ReadRawModifiedDetails
            {
                IsReadModified = false,
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddMinutes(1),
            };

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = new NodeId("parent.var.Annotations", 1),
                ContinuationPoint = ByteString.Empty,
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchAnnotationReadAsync(
                h.SystemContext,
                h.Provider,
                parent,
                nodeToRead,
                details,
                TimestampsToReturn.Source,
                result,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(
                result.HistoryData.TryGetValue(out HistoryData? hd),
                Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(1));

            Assert.That(values![0].WrappedValue.TryGetValue(out ExtensionObject ext), Is.True);
            Assert.That(ext.TryGetValue(out Annotation? readBack), Is.True);
            Assert.That(readBack!.Message, Is.EqualTo("via-direct-insert"));
        }

        private static HarnessFixture CreateHarness()
        {
            return new HarnessFixture();
        }

        private sealed class HarnessFixture
        {
            public static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public HarnessFixture()
            {
                Provider = new InMemoryHistorianProvider();

                var mockTelemetry = new Mock<ITelemetryContext>();
                m_continuationStore = [];

                var mockSession = new Mock<ISession>();
                mockSession
                    .Setup(s => s.SaveHistoryContinuationPoint(It.IsAny<Guid>(), It.IsAny<object>()))
                    .Callback<Guid, object>((id, cp) => m_continuationStore[id] = cp);
                mockSession
                    .Setup(s => s.RestoreHistoryContinuationPoint(It.IsAny<ByteString>()))
                    .Returns<ByteString>(bs =>
                    {
                        if (bs.Length != 16)
                        {
                            return null;
                        }
                        var id = new Guid(bs.ToArray());
                        if (m_continuationStore.TryGetValue(id, out object? state))
                        {
                            m_continuationStore.Remove(id);
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
                    new RequestHeader(),
                    null!,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    mockSession.Object);
                SystemContext = new ServerSystemContext(mockServer.Object, opContext);
            }

            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }

            public NodeId SeedSamples(int count)
            {
                var nodeId = new NodeId($"paged-{Guid.NewGuid():N}", 1);
                Provider.Register(nodeId);

                var values = new List<DataValue>(count);
                for (int i = 0; i < count; i++)
                {
                    values.Add(new DataValue(
                        new Variant(i),
                        StatusCodes.Good,
                        sourceTimestamp: BaseTime.AddSeconds(i),
                        serverTimestamp: BaseTime.AddSeconds(i)));
                }
                HistorianOperationContext context = CreateContext(SystemContext);
                _ = Provider.InsertAsync(context, nodeId, values, CancellationToken.None)
                    .AsTask().GetAwaiter().GetResult();
                return nodeId;
            }

            public static HistorianOperationContext CreateContext(ServerSystemContext systemContext)
            {
                return new HistorianOperationContext(
                    systemContext,
                    systemContext.OperationContext!,
                    null,
                    HistoryUpdateType.Insert);
            }

            private readonly Dictionary<Guid, object> m_continuationStore;
        }
    }
}
