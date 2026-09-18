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

        [TestCase(false, 1u)]
        [TestCase(true, 1u)]
        [TestCase(false, 2u)]
        [TestCase(true, 2u)]
        [TestCase(false, 0u)]
        [TestCase(true, 0u)]
        public async Task TimestampedAnnotationsRemainOrderedAcrossPagesAsync(bool reverse, uint maxValues)
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.annotation.pages", 1);
            await provider.InsertAnnotationsWithTimestampsAsync(
                context,
                nodeId,
                [
                    MakeAnnotation(2, 30, "C"),
                    MakeAnnotation(1, 40, "B"),
                    MakeAnnotation(1, 10, "A"),
                    MakeAnnotation(3, 20, "D")
                ],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianAnnotationReadRequest
            {
                NodeId = nodeId,
                StartTime = s_start,
                EndTime = s_start.AddSeconds(4),
                IsForward = !reverse,
                MaxValues = maxValues
            };
            var actual = new List<string>();
            HistorianResumeToken token = default;
            do
            {
                HistorianPage<HistorianAnnotation> page = await provider.ReadAnnotationsWithTimestampsAsync(
                    context, request, token, CancellationToken.None).ConfigureAwait(false);
                actual.AddRange(page.Values.ToArray().Select(a => a.Annotation.Message));
                Assert.That(actual, Has.Count.LessThanOrEqualTo(4), "Continuation must not repeat annotations.");
                Assert.That(page.IsFinal || page.Values.Count > 0, Is.True);
                token = page.NextToken;
            }
            while (!token.IsEmpty);

            string[] expected = reverse ? ["D", "C", "B", "A"] : ["A", "B", "C", "D"];
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public async Task TimestampedAnnotationMutationsUseBothTimestampsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.annotation.identity", 1);
            HistorianUpdateOutcome<HistorianAnnotation> inserted = await provider.InsertAnnotationsWithTimestampsAsync(
                context,
                nodeId,
                [MakeAnnotation(1, 10, "first"), MakeAnnotation(2, 10, "second")],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(inserted.OperationResults, Is.EqualTo(
                new[] { StatusCodes.GoodEntryInserted, StatusCodes.GoodEntryInserted }));
            HistorianUpdateOutcome<HistorianAnnotation> missing = await provider.ReplaceAnnotationsWithTimestampsAsync(
                context, nodeId, [MakeAnnotation(3, 10, "missing")], CancellationToken.None).ConfigureAwait(false);
            Assert.That(missing.OperationResults[0], Is.EqualTo(StatusCodes.BadNoEntryExists));
            HistorianUpdateOutcome<HistorianAnnotation> updated = await provider.UpdateAnnotationsWithTimestampsAsync(
                context, nodeId, [MakeAnnotation(3, 10, "third")], CancellationToken.None).ConfigureAwait(false);
            Assert.That(updated.OperationResults[0], Is.EqualTo(StatusCodes.GoodEntryInserted));
            HistorianUpdateOutcome<HistorianAnnotation> replaced = await provider.ReplaceAnnotationsWithTimestampsAsync(
                context, nodeId, [MakeAnnotation(2, 10, "replacement")], CancellationToken.None).ConfigureAwait(false);
            Assert.That(replaced.OldValues[0].Annotation.Message, Is.EqualTo("second"));
            HistorianUpdateOutcome<HistorianAnnotation> deleted = await provider.DeleteAnnotationsWithTimestampsAsync(
                context, nodeId, [MakeAnnotation(1, 10, "ignored")], CancellationToken.None).ConfigureAwait(false);
            Assert.That(deleted.OldValues[0].Annotation.Message, Is.EqualTo("first"));

            HistorianPage<HistorianAnnotation> page = await provider.ReadAnnotationsWithTimestampsAsync(
                context,
                new HistorianAnnotationReadRequest
                {
                    NodeId = nodeId,
                    StartTime = s_start,
                    EndTime = s_start.AddSeconds(4),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);
            string[] expectedMessages = ["replacement", "third"];
            Assert.That(page.Values.ToArray().Select(a => a.Annotation.Message), Is.EqualTo(expectedMessages));
            Assert.That(page.Values.ToArray().Select(a => a.SourceTimestamp),
                Is.EqualTo(new[] { (DateTimeUtc)s_start.AddSeconds(2), (DateTimeUtc)s_start.AddSeconds(3) }));
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public async Task TimestampedAnnotationsRespectHardPageLimitAsync(uint maxValues)
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.annotation.cap", 1);
            var input = new List<HistorianAnnotation>();
            for (int i = 0; i < 1001; i++)
            {
                input.Add(MakeAnnotation(i, i, "annotation"));
            }
            await provider.InsertAnnotationsWithTimestampsAsync(
                context, nodeId, input.ToArrayOf(), CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianAnnotationReadRequest
            {
                NodeId = nodeId,
                StartTime = s_start,
                EndTime = s_start.AddHours(1),
                IsForward = true,
                MaxValues = maxValues
            };
            HistorianPage<HistorianAnnotation> first = await provider.ReadAnnotationsWithTimestampsAsync(
                context, request, default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(first.Values, Has.Count.EqualTo(1000));
            Assert.That(first.IsFinal, Is.False);
            HistorianPage<HistorianAnnotation> last = await provider.ReadAnnotationsWithTimestampsAsync(
                context, request, first.NextToken, CancellationToken.None).ConfigureAwait(false);
            Assert.That(last.Values, Has.Count.EqualTo(1));
            Assert.That(last.Values[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start.AddSeconds(1000)));
            Assert.That(last.IsFinal, Is.True);
        }

        [TestCase(false, "bounded", "ABC")]
        [TestCase(true, "bounded", "DC")]
        [TestCase(false, "exact", "AB")]
        [TestCase(true, "exact", "BA")]
        [TestCase(false, "open", "ABC")]
        [TestCase(true, "open", "DCB")]
        public async Task TimestampedAnnotationWindowsAndQuotasResumeAsync(
            bool reverse,
            string window,
            string expected)
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.annotation.window", 1);
            await provider.InsertAnnotationsWithTimestampsAsync(
                context,
                nodeId,
                [
                    MakeAnnotation(0, 100, "Z"),
                    MakeAnnotation(1, 10, "A"),
                    MakeAnnotation(1, 20, "B"),
                    MakeAnnotation(2, 30, "C"),
                    MakeAnnotation(3, 40, "D")
                ],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianAnnotationReadRequest
            {
                NodeId = nodeId,
                StartTime = window == "open" && reverse ? DateTimeUtc.MinValue : s_start.AddSeconds(1),
                EndTime = window == "open" && !reverse
                    ? DateTimeUtc.MaxValue
                    : s_start.AddSeconds(window == "exact" ? 1 : 3),
                IsForward = !reverse,
                MaxValues = 3,
                PageLimit = 1
            };
            string actual = string.Empty;
            HistorianResumeToken token = default;
            do
            {
                HistorianPage<HistorianAnnotation> page = await provider.ReadAnnotationsWithTimestampsAsync(
                    context, request, token, CancellationToken.None).ConfigureAwait(false);
                Assert.That(page.Values, Has.Count.EqualTo(1));
                actual += page.Values[0].Annotation.Message;
                Assert.That(actual, Has.Length.LessThanOrEqualTo(expected.Length));
                token = page.NextToken;
            }
            while (!token.IsEmpty);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(999)]
        [TestCase(1000)]
        [TestCase(1001)]
        public async Task RawPagesRespectHardLimitAtBoundaryAsync(int count)
        {
            using var provider = new InMemoryHistorianProvider(new InMemoryHistorianOptions
            {
                RawDataRetentionPeriod = TimeSpan.Zero
            });
            HistorianOperationContext context = CreateContext();
            var nodeId = new NodeId("review.raw.hardlimit", 1);
            var input = new List<DataValue>();
            for (int i = 0; i < count; i++)
            {
                input.Add(new DataValue(i, StatusCodes.Good, s_start.AddSeconds(i)));
            }
            await provider.InsertBatchAsync(
                context, [new HistorianDataBatch(nodeId, input.ToArrayOf())],
                CancellationToken.None).ConfigureAwait(false);
            var request = new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = s_start,
                EndTime = s_start.AddHours(1),
                MaxValues = uint.MaxValue,
                IsForward = true
            };
            HistorianPage<HistoricalDataValue> first = await provider.ReadRawAsync(
                context, request, default, CancellationToken.None).ConfigureAwait(false);
            Assert.That(first.Values, Has.Count.EqualTo(count > 1000 ? 1000 : count));
            Assert.That(first.IsFinal, Is.EqualTo(count <= 1000));
            if (!first.IsFinal)
            {
                HistorianPage<HistoricalDataValue> last = await provider.ReadRawAsync(
                    context, request, first.NextToken, CancellationToken.None).ConfigureAwait(false);
                Assert.That(last.Values, Has.Count.EqualTo(1));
                Assert.That(last.Values[0].Value.SourceTimestamp, Is.EqualTo((DateTimeUtc)s_start.AddSeconds(1000)));
                Assert.That(last.IsFinal, Is.True);
            }
        }

        [TestCase(PerformUpdateType.Insert)]
        [TestCase(PerformUpdateType.Replace)]
        [TestCase(PerformUpdateType.Update)]
        [TestCase(PerformUpdateType.Remove)]
        public async Task TimestampedAnnotationUpdatePreservesInvalidEntriesAsync(PerformUpdateType updateType)
        {
            using var provider = new InMemoryHistorianProvider();
            HistorianOperationContext context = CreateContext();
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("review.annotation.update", 1) };
            provider.Register(variable.NodeId, new HistorianNodeCapabilities { InsertAnnotation = true });
            HistorianAnnotation first = MakeAnnotation(1, 10, "first");
            HistorianAnnotation second = MakeAnnotation(2, 20, "second");
            if (updateType != PerformUpdateType.Insert)
            {
                await provider.InsertAnnotationsWithTimestampsAsync(
                    context, variable.NodeId, [first, second], CancellationToken.None).ConfigureAwait(false);
            }
            var result = new HistoryUpdateResult();
            await HistorianDispatcher.DispatchAnnotationUpdateAsync(
                context.SystemContext,
                provider,
                variable,
                new UpdateStructureDataDetails
                {
                    NodeId = variable.NodeId,
                    PerformInsertReplace = updateType,
                    UpdateValues =
                    [
                        DataValue.Null,
                        new DataValue(new Variant(new ExtensionObject(first.Annotation)), StatusCodes.Good,
                            first.SourceTimestamp),
                        new DataValue(42),
                        new DataValue(new Variant(new ExtensionObject(second.Annotation)), StatusCodes.Good,
                            second.SourceTimestamp),
                        DataValue.Null
                    ]
                },
                result,
                CancellationToken.None).ConfigureAwait(false);
            StatusCode expectedGood = updateType switch
            {
                PerformUpdateType.Insert => StatusCodes.GoodEntryInserted,
                PerformUpdateType.Remove => StatusCodes.Good,
                _ => StatusCodes.GoodEntryReplaced
            };
            Assert.That(result.OperationResults, Is.EqualTo(new[]
            {
                StatusCodes.BadInvalidArgument,
                expectedGood,
                StatusCodes.BadInvalidArgument,
                expectedGood,
                StatusCodes.BadInvalidArgument
            }));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public async Task TimestampedAnnotationUpdateRejectsMismatchedProviderResultsAsync(int resultCount)
        {
            HistorianOperationContext context = CreateContext();
            var provider = new Mock<IHistorianProvider>();
            provider.As<IHistorianAnnotationProvider>();
            Mock<IHistorianTimestampedAnnotationProvider> timestamped =
                provider.As<IHistorianTimestampedAnnotationProvider>();
            provider.Setup(p => p.GetCapabilitiesAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistorianNodeCapabilities { InsertAnnotation = true });
            timestamped.Setup(p => p.InsertAnnotationsWithTimestampsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<HistorianAnnotation>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HistorianUpdateOutcome<HistorianAnnotation>(
                    Enumerable.Repeat(StatusCodes.Good, resultCount).ToArray().ToArrayOf()));
            var variable = new BaseDataVariableState(null) { NodeId = new NodeId("review.annotation.count", 1) };
            var result = new HistoryUpdateResult();
            await HistorianDispatcher.DispatchAnnotationUpdateAsync(
                context.SystemContext,
                provider.Object,
                variable,
                new UpdateStructureDataDetails
                {
                    NodeId = variable.NodeId,
                    PerformInsertReplace = PerformUpdateType.Insert,
                    UpdateValues = [DataValue.Null, DataValue.Null]
                },
                result,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.OperationResults,
                Is.EqualTo(new[] { StatusCodes.BadUnexpectedError, StatusCodes.BadUnexpectedError }));
        }

        private static HistorianAnnotation MakeAnnotation(int sourceSeconds, int annotationSeconds, string message)
        {
            return new HistorianAnnotation(s_start.AddSeconds(sourceSeconds), new Annotation
            {
                AnnotationTime = s_start.AddSeconds(annotationSeconds),
                Message = message,
                UserName = "review"
            });
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
