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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class HistorianDispatcherBranchTests
    {
        [Test]
        public void ResolveProviderReturnsNodeManagerOverrideWhenProvided()
        {
            HarnessFixture h = CreateHarness();
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("resolve-override", 1),
                BrowseName = new QualifiedName("Var")
            };

            var sentinel = new InMemoryHistorianProvider();
            IHistorianProvider? resolved = HistorianDispatcher.ResolveProvider(
                h.MockServer.Object, node, sentinel);

            Assert.That(resolved, Is.SameAs(sentinel));
        }

        [Test]
        public void ResolveProviderFallsBackToRegistryWhenNoOverride()
        {
            var sentinel = new InMemoryHistorianProvider();
            var nodeId = new NodeId("resolve-registry", 1);

            var mockRegistry = new Mock<IHistorianProviderRegistry>();
            mockRegistry.Setup(r => r.Resolve(nodeId)).Returns(sentinel);

            var mockServer = new Mock<IServerInternal>(MockBehavior.Loose);
            mockServer.As<IHistorianRegistryProvider>()
                .Setup(p => p.HistorianRegistry).Returns(mockRegistry.Object);

            var node = new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("Var")
            };

            IHistorianProvider? resolved = HistorianDispatcher.ResolveProvider(
                mockServer.Object, node, null);

            Assert.That(resolved, Is.SameAs(sentinel));
        }

        [Test]
        public void ResolveProviderReturnsNullWhenNoRegistryAndNoOverride()
        {
            HarnessFixture h = CreateHarness();
            var node = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("resolve-null", 1),
                BrowseName = new QualifiedName("Var")
            };

            IHistorianProvider? resolved = HistorianDispatcher.ResolveProvider(
                h.MockServer.Object, node, null);

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public async Task DispatchDeleteRawWithNonDataProviderReturnsHistoryOperationUnsupportedAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId("del-raw-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddMinutes(1),
                IsDeleteModified = false
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, mockProvider.Object, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchDeleteRawCompletesAndPropagatesProviderStatusAsync()
        {
            HarnessFixture h = CreateHarness();
            NodeId nodeId = h.SeedSamples(5);
            BaseDataVariableState node = CreateVariable(nodeId);

            var details = new DeleteRawModifiedDetails
            {
                NodeId = nodeId,
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(10),
                IsDeleteModified = false
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchDeleteRawAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            // Re-read and verify the range is now empty.
            var readDetails = new ReadRawModifiedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(10),
                IsReadModified = false
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };
            var readResult = new HistoryReadResult();
            ServiceResult readError = await HistorianDispatcher.DispatchRawReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, readDetails,
                TimestampsToReturn.Source, readResult, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(readError), Is.True);
            if (readResult.HistoryData.TryGetValue(out HistoryData? hd))
            {
                Assert.That(hd.DataValues, Is.Empty);
            }
        }

        [Test]
        public async Task DispatchDeleteAtTimeWithNonDataProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId("del-at-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new DeleteAtTimeDetails
            {
                NodeId = nodeId,
                ReqTimes = new DateTimeUtc[] { HarnessFixture.BaseTime }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, mockProvider.Object, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchDeleteAtTimeMixedFoundNotFoundProducesPerTimestampStatusAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"del-at-mixed-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            DateTime t0 = HarnessFixture.BaseTime;
            DateTime t1 = HarnessFixture.BaseTime.AddSeconds(1);
            DateTime t2 = HarnessFixture.BaseTime.AddSeconds(2);

            var seedValues = new List<DataValue>
            {
                new(new Variant(10), StatusCodes.Good, sourceTimestamp: t0, serverTimestamp: t0),
                new(new Variant(20), StatusCodes.Good, sourceTimestamp: t1, serverTimestamp: t1),
                new(new Variant(30), StatusCodes.Good, sourceTimestamp: t2, serverTimestamp: t2)
            };
            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            await h.Provider.InsertAsync(ctx, nodeId, seedValues, CancellationToken.None).ConfigureAwait(false);

            DateTime unknown1 = HarnessFixture.BaseTime.AddSeconds(100);
            DateTime unknown2 = HarnessFixture.BaseTime.AddSeconds(200);

            var details = new DeleteAtTimeDetails
            {
                NodeId = nodeId,
                ReqTimes = new DateTimeUtc[] { t0, unknown1, t1, unknown2 }
            };

            BaseDataVariableState node = CreateVariable(nodeId);
            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchDeleteAtTimeAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.OperationResults, Has.Count.EqualTo(4));
            Assert.That(StatusCode.IsGood(result.OperationResults[0]), Is.True);
            Assert.That(result.OperationResults[1], Is.EqualTo(StatusCodes.BadNoEntryExists));
            Assert.That(StatusCode.IsGood(result.OperationResults[2]), Is.True);
            Assert.That(result.OperationResults[3], Is.EqualTo(StatusCodes.BadNoEntryExists));
        }

        [Test]
        public async Task DispatchProcessedReadAggregateNotSupportedReturnsBadAggregateNotSupportedAsync()
        {
            HarnessFixture h = CreateHarnessWithAggregateManager();
            NodeId nodeId = h.SeedSamples(5);
            BaseDataVariableState node = CreateVariable(nodeId);

            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddMinutes(1),
                ProcessingInterval = 10000
            };

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            var bogusAggregateId = new NodeId("not-an-aggregate", 1);

            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                bogusAggregateId, TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadAggregateNotSupported));
        }

        [Test]
        public async Task DispatchProcessedReadAnnotationCountCountsAnnotationsPerIntervalAsync()
        {
            HarnessFixture h = CreateHarnessWithAggregateManager();
            var nodeId = new NodeId($"anncount-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext context = HarnessFixture.CreateContext(h.SystemContext);

            // Raw samples that AnnotationCount must ignore (one per second for 30 s).
            var samples = new List<DataValue>();
            for (int i = 0; i < 30; i++)
            {
                DateTime ts = HarnessFixture.BaseTime.AddSeconds(i);
                samples.Add(new DataValue(new Variant((double)i), StatusCodes.Good, ts, ts));
            }
            await h.Provider.InsertAsync(context, nodeId, samples, CancellationToken.None).ConfigureAwait(false);

            // Annotations: 3 in [0,10) s, 1 in [10,20) s, 0 in [20,30) s.
            var annotations = new List<Annotation>
            {
                new() { Message = "a", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(2) },
                new() { Message = "b", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(5) },
                new() { Message = "c", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(8) },
                new() { Message = "d", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(15) }
            };
            await h.Provider.InsertAnnotationsAsync(context, nodeId, annotations, CancellationToken.None).ConfigureAwait(false);

            BaseDataVariableState node = CreateVariable(nodeId);
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(30),
                ProcessingInterval = 10000
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_AnnotationCount, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(3));

            int[] expected = [3, 1, 0];
            for (int i = 0; i < 3; i++)
            {
                Assert.That(values![i].WrappedValue.TryGetValue(out int count), Is.True);
                Assert.That(count, Is.EqualTo(expected[i]), $"annotation count for interval {i}");
                Assert.That(
                    values[i].SourceTimestamp.ToDateTime(),
                    Is.EqualTo(HarnessFixture.BaseTime.AddSeconds(i * 10)));
                Assert.That(StatusCode.IsGood(values[i].StatusCode), Is.True);
                Assert.That(values[i].StatusCode.AggregateBits, Is.EqualTo(AggregateBits.Calculated));
            }
        }

        [Test]
        public async Task DispatchProcessedReadAnnotationCountWithoutAnnotationProviderReturnsBadAggregateNotSupportedAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"anncount-noann-{Guid.NewGuid():N}", 1);
            BaseDataVariableState node = CreateVariable(nodeId);

            // A provider that implements neither IHistorianProcessedProvider nor
            // IHistorianAnnotationProvider must report AnnotationCount as unsupported.
            var bareProvider = new Mock<IHistorianProvider>();

            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(30),
                ProcessingInterval = 10000,
                AggregateConfiguration = new AggregateConfiguration
                {
                    PercentDataBad = 100,
                    PercentDataGood = 100
                }
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, bareProvider.Object, node, nodeToRead, details,
                ObjectIds.AggregateFunction_AnnotationCount, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadAggregateNotSupported));
        }

        [Test]
        public async Task DispatchProcessedReadAnnotationCountWithZeroIntervalReturnsSingleBucketAsync()
        {
            // §5.4.3.1: ProcessingInterval == 0 → one aggregate over the entire range.
            HarnessFixture h = CreateHarnessWithAggregateManager();
            var nodeId = new NodeId($"anncount-zero-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext context = HarnessFixture.CreateContext(h.SystemContext);
            var annotations = new List<Annotation>
            {
                new() { Message = "x", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(1) },
                new() { Message = "y", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(2) },
                new() { Message = "z", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(3) }
            };
            await h.Provider.InsertAnnotationsAsync(context, nodeId, annotations, CancellationToken.None).ConfigureAwait(false);

            BaseDataVariableState node = CreateVariable(nodeId);
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(30),
                ProcessingInterval = 0
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_AnnotationCount, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(values![0].WrappedValue.TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public async Task DispatchProcessedReadAnnotationCountReverseTimeReturnsBucketsAsync()
        {
            // §5.4.3.1: reverse time (start > end). Result is timestamped with each interval's
            // (later) start time and walks backward toward end.
            HarnessFixture h = CreateHarnessWithAggregateManager();
            var nodeId = new NodeId($"anncount-reverse-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            HistorianOperationContext context = HarnessFixture.CreateContext(h.SystemContext);
            var annotations = new List<Annotation>
            {
                new() { Message = "a", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(2) },
                new() { Message = "b", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(5) },
                new() { Message = "c", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(8) },
                new() { Message = "d", UserName = "t", AnnotationTime = HarnessFixture.BaseTime.AddSeconds(15) }
            };
            await h.Provider.InsertAnnotationsAsync(context, nodeId, annotations, CancellationToken.None).ConfigureAwait(false);

            BaseDataVariableState node = CreateVariable(nodeId);
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime.AddSeconds(30),
                EndTime = HarnessFixture.BaseTime,
                ProcessingInterval = 10000
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_AnnotationCount, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(3));

            int total = 0;
            foreach (DataValue v in values!)
            {
                Assert.That(v.WrappedValue.TryGetValue(out int count), Is.True);
                total += count;
            }
            Assert.That(total, Is.EqualTo(4),
                "Reverse-time AnnotationCount must total all four annotations across the buckets.");
        }

        [Test]
        public async Task DispatchProcessedReadAnnotationCountWithNoAnnotationsReturnsZerosAsync()
        {
            HarnessFixture h = CreateHarnessWithAggregateManager();
            var nodeId = new NodeId($"anncount-empty-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            BaseDataVariableState node = CreateVariable(nodeId);
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime.AddSeconds(20),
                ProcessingInterval = 10000
            };
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_AnnotationCount, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            // §5.4.3.20: empty interval count is 0 with Good/Calculated status (never Bad_NoData).
            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryData? hd), Is.True);
            DataValue[]? values = hd!.DataValues.ToArray();
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(2));
            foreach (DataValue v in values!)
            {
                Assert.That(v.WrappedValue.TryGetValue(out int count), Is.True);
                Assert.That(count, Is.Zero);
                Assert.That(StatusCode.IsGood(v.StatusCode), Is.True);
            }
        }

        [Test]
        public async Task DispatchProcessedReadWithEqualStartAndEndTimeReturnsBadInvalidArgumentAsync()
        {
            HarnessFixture h = CreateHarnessWithAggregateManager();
            NodeId nodeId = h.SeedSamples(5);
            BaseDataVariableState node = CreateVariable(nodeId);

            // Part 11 v1.05.07 §6.5.4.2: a zero-width time domain (StartTime == EndTime) has no
            // meaningful interpretation, so the Server shall return Bad_InvalidArgument. The guard
            // fires before aggregate/config resolution, so even a valid aggregate is rejected.
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime,
                EndTime = HarnessFixture.BaseTime,
                ProcessingInterval = 10000
            };

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_Average, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task DispatchProcessedReadWithReverseTimeRangeIsNotRejectedAsInvalidArgumentAsync()
        {
            HarnessFixture h = CreateHarnessWithAggregateManager();
            NodeId nodeId = h.SeedSamples(5);
            BaseDataVariableState node = CreateVariable(nodeId);

            // Part 11 v1.05.07 §6.5.4.2 explicitly permits a reverse time domain (EndTime less than
            // StartTime). The zero-width-domain guard must only trigger on equality, so a reverse
            // range must fall through to normal aggregate resolution (here Bad_AggregateNotSupported
            // because no factory is registered in this harness) rather than Bad_InvalidArgument.
            var details = new ReadProcessedDetails
            {
                StartTime = HarnessFixture.BaseTime.AddMinutes(1),
                EndTime = HarnessFixture.BaseTime,
                ProcessingInterval = 10000
            };

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchProcessedReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                ObjectIds.AggregateFunction_Average, TimestampsToReturn.Source,
                result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.Not.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadAggregateNotSupported));
        }

        [Test]
        public async Task DispatchAtTimeReadInterpolatesBetweenSamplesAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId($"at-time-interp-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);

            DateTime t10 = HarnessFixture.BaseTime.AddSeconds(10);
            DateTime t20 = HarnessFixture.BaseTime.AddSeconds(20);

            var seedValues = new List<DataValue>
            {
                new(new Variant(100.0), StatusCodes.Good, sourceTimestamp: t10, serverTimestamp: t10),
                new(new Variant(200.0), StatusCodes.Good, sourceTimestamp: t20, serverTimestamp: t20)
            };
            HistorianOperationContext ctx = HarnessFixture.CreateContext(h.SystemContext);
            await h.Provider.InsertAsync(ctx, nodeId, seedValues, CancellationToken.None).ConfigureAwait(false);

            BaseDataVariableState node = CreateVariable(nodeId);
            DateTime t15 = HarnessFixture.BaseTime.AddSeconds(15);

            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { t15 },
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
            Assert.That(values, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(values![0].SourceTimestamp.ToDateTime(), Is.EqualTo(t15));
            Assert.That(values[0].StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            double interpolated = Convert.ToDouble(values[0].WrappedValue.AsBoxedObject(), CultureInfo.InvariantCulture);
            Assert.That(interpolated, Is.EqualTo(150.0).Within(0.01));
        }

        [Test]
        public async Task DispatchAtTimeReadWithoutDataProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            HarnessFixture h = CreateHarness();
            var nodeId = new NodeId("at-time-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new ReadAtTimeDetails
            {
                ReqTimes = new DateTimeUtc[] { HarnessFixture.BaseTime },
                UseSimpleBounds = false
            };

            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.Empty
            };

            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchAtTimeReadAsync(
                h.SystemContext, mockProvider.Object, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public void ReleaseContinuationPointWithEmptyContinuationReturnsBadContinuationPointInvalid()
        {
            HarnessFixture h = CreateHarness();
            var nodeToRead = new HistoryReadValueId
            {
                ContinuationPoint = ByteString.Empty
            };

            ServiceResult error = HistorianDispatcher.ReleaseContinuationPoint(
                h.SystemContext, nodeToRead);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        [Test]
        public void ReleaseContinuationPointWithUnknownContinuationReturnsBadContinuationPointInvalid()
        {
            HarnessFixture h = CreateHarness();
            var randomCp = new ByteString(Guid.NewGuid().ToByteArray());
            var nodeToRead = new HistoryReadValueId
            {
                ContinuationPoint = randomCp
            };

            ServiceResult error = HistorianDispatcher.ReleaseContinuationPoint(
                h.SystemContext, nodeToRead);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
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

        private static HarnessFixture CreateHarness()
        {
            return new HarnessFixture();
        }

        private static HarnessFixture CreateHarnessWithAggregateManager()
        {
            return new HarnessFixture(withAggregateManager: true);
        }

        private sealed class HarnessFixture
        {
            public static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            public HarnessFixture(bool withAggregateManager = false)
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

                MockServer = new Mock<IServerInternal>();
                MockServer.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                MockServer.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                MockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

                if (withAggregateManager)
                {
                    var aggMgr = new AggregateManager(MockServer.Object);
                    MockServer.Setup(s => s.AggregateManager).Returns(aggMgr);
                }

                var opContext = new OperationContext(
                    new RequestHeader(),
                    null!,
                    RequestType.HistoryRead,
                    RequestLifetime.None,
                    mockSession.Object);
                SystemContext = new ServerSystemContext(MockServer.Object, opContext);
            }

            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }
            public Mock<IServerInternal> MockServer { get; }

            public NodeId SeedSamples(int count)
            {
                var nodeId = new NodeId($"branch-{Guid.NewGuid():N}", 1);
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
