/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy,
 * modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
 * BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
 * ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
 * CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies retry-safe historian continuations, buffered payload ownership, and durable annotation identity.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianContinuationSafetyTests
    {
        /// <summary>
        /// Verifies that a failed raw-history resume can retry the same continuation.
        /// </summary>
        [Test]
        public async Task RawResumeProviderFailureCanRetrySameContinuationAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var nodeId = new NodeId("raw-retry", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianDataProvider> dataProvider =
                provider.As<IHistorianDataProvider>();
            var token = new HistorianResumeToken(ByteString.From([1]));
            dataProvider
                .SetupSequence(p => p.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                        [CreateHistoricalValue(1)],
                        token)))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError))
                .Returns(new ValueTask<HistorianPage<HistoricalDataValue>>(
                    new HistorianPage<HistoricalDataValue>(
                        [CreateHistoricalValue(2)])));
            var details = new ReadRawModifiedDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMinutes(1),
                NumValuesPerNode = 1
            };
            var firstResult = new HistoryReadResult();

            ServiceResult firstError = await HistorianDispatcher.DispatchRawReadAsync(
                systemContext,
                provider.Object,
                node,
                new HistoryReadValueId { NodeId = nodeId },
                details,
                TimestampsToReturn.Source,
                firstResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(firstResult.ContinuationPoint.IsEmpty, Is.False);
            ByteString continuationPoint = firstResult.ContinuationPoint;
            var continuedNode = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = continuationPoint
            };

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await HistorianDispatcher.DispatchRawReadAsync(
                        systemContext,
                        provider.Object,
                        node,
                        continuedNode,
                        details,
                        TimestampsToReturn.Source,
                        new HistoryReadResult(),
                        CancellationToken.None))!;
            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError = await HistorianDispatcher.DispatchRawReadAsync(
                systemContext,
                provider.Object,
                node,
                continuedNode,
                details,
                TimestampsToReturn.Source,
                retryResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that a failed modified-history resume can retry the same continuation.
        /// </summary>
        [Test]
        public async Task ModifiedResumeProviderFailureCanRetrySameContinuationAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var nodeId = new NodeId("modified-retry", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianModifiedProvider> modifiedProvider =
                provider.As<IHistorianModifiedProvider>();
            var token = new HistorianResumeToken(ByteString.From([1]));
            modifiedProvider
                .SetupSequence(p => p.ReadModifiedAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianModifiedReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<ModifiedDataValue>>(
                    new HistorianPage<ModifiedDataValue>(
                        [CreateModifiedValue(1)],
                        token)))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError))
                .Returns(new ValueTask<HistorianPage<ModifiedDataValue>>(
                    new HistorianPage<ModifiedDataValue>(
                        [CreateModifiedValue(2)])));
            var details = new ReadRawModifiedDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMinutes(1),
                NumValuesPerNode = 1,
                IsReadModified = true
            };
            var firstResult = new HistoryReadResult();

            ServiceResult firstError = await HistorianDispatcher.DispatchRawReadAsync(
                systemContext,
                provider.Object,
                node,
                new HistoryReadValueId { NodeId = nodeId },
                details,
                TimestampsToReturn.Source,
                firstResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(firstResult.ContinuationPoint.IsEmpty, Is.False);
            var continuedNode = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = firstResult.ContinuationPoint
            };

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await HistorianDispatcher.DispatchRawReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    continuedNode,
                    details,
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError = await HistorianDispatcher.DispatchRawReadAsync(
                systemContext,
                provider.Object,
                node,
                continuedNode,
                details,
                TimestampsToReturn.Source,
                retryResult,
                CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that a failed annotation-history resume can retry the same continuation.
        /// </summary>
        [Test]
        public async Task AnnotationResumeProviderFailureCanRetrySameContinuationAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var parentNodeId = new NodeId("annotation-parent", 1);
            var propertyNodeId = new NodeId("annotation-property", 1);
            BaseDataVariableState parent = CreateVariable(parentNodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    parentNodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianAnnotationProvider> annotationProvider =
                provider.As<IHistorianAnnotationProvider>();
            var token = new HistorianResumeToken(ByteString.From([1]));
            annotationProvider
                .SetupSequence(p => p.ReadAnnotationsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianAnnotationReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<Annotation>>(
                    new HistorianPage<Annotation>(
                        [CreateAnnotation(1)],
                        token)))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError))
                .Returns(new ValueTask<HistorianPage<Annotation>>(
                    new HistorianPage<Annotation>(
                        [CreateAnnotation(2)])));
            var details = new ReadRawModifiedDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMinutes(1),
                NumValuesPerNode = 1
            };
            var firstResult = new HistoryReadResult();

            ServiceResult firstError =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    parent,
                    new HistoryReadValueId { NodeId = propertyNodeId },
                    details,
                    TimestampsToReturn.Source,
                    firstResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(firstResult.ContinuationPoint.IsEmpty, Is.False);
            var continuedNode = new HistoryReadValueId
            {
                NodeId = propertyNodeId,
                ContinuationPoint = firstResult.ContinuationPoint
            };

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    parent,
                    continuedNode,
                    details,
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    parent,
                    continuedNode,
                    details,
                    TimestampsToReturn.Source,
                    retryResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that retrying an event-history resume uses the persisted event filter.
        /// </summary>
        [Test]
        public async Task EventResumeFailureRetriesWithPersistedFilterAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var nodeId = new NodeId("event-retry", 1);
            NodeState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.EventReadWrite));
            Mock<IHistorianEventProvider> eventProvider =
                provider.As<IHistorianEventProvider>();
            var token = new HistorianResumeToken(ByteString.From([1]));
            HistorianEventRecord eventRecord = CreateEventRecord(1);
            eventProvider
                .SetupSequence(p => p.ReadEventsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianEventReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianPage<HistorianEventRecord>>(
                    new HistorianPage<HistorianEventRecord>(
                        [eventRecord],
                        token)))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError))
                .Returns(new ValueTask<HistorianPage<HistorianEventRecord>>(
                    new HistorianPage<HistorianEventRecord>(
                        [eventRecord])));
            var persistedFilter = new EventFilter();
            persistedFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            var incomingFilter = new EventFilter();
            incomingFilter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            incomingFilter.WhereClause.Push(
                FilterOperator.Equals,
                [
                    Variant.FromStructure(new LiteralOperand
                    {
                        Value = Variant.From(1)
                    }),
                    Variant.FromStructure(new LiteralOperand
                    {
                        Value = Variant.From(2)
                    })
                ]);
            var initialDetails = new ReadEventDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMinutes(1),
                NumValuesPerNode = 1,
                Filter = persistedFilter
            };
            var firstResult = new HistoryReadResult();

            ServiceResult firstError =
                await HistorianDispatcher.DispatchEventReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    new HistoryReadValueId { NodeId = nodeId },
                    initialDetails,
                    TimestampsToReturn.Source,
                    firstResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(firstResult.ContinuationPoint.IsEmpty, Is.False);
            var continuedNode = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = firstResult.ContinuationPoint
            };
            var incomingDetails = new ReadEventDetails
            {
                StartTime = s_baseTime.AddYears(-1),
                EndTime = s_baseTime.AddYears(1),
                NumValuesPerNode = 100,
                Filter = incomingFilter
            };

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await HistorianDispatcher.DispatchEventReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    continuedNode,
                    incomingDetails,
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError =
                await HistorianDispatcher.DispatchEventReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    continuedNode,
                    incomingDetails,
                    TimestampsToReturn.Source,
                    retryResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.True);
            Assert.That(
                retryResult.HistoryData.TryGetValue(out HistoryEvent history),
                Is.True);
            Assert.That(history!.Events, Has.Count.EqualTo(1));
            Assert.That(history.Events[0].EventFields, Has.Count.EqualTo(1));
            Assert.That(
                history.Events[0].EventFields[0].TryGetValue(
                    out ByteString projectedEventId),
                Is.True);
            Assert.That(projectedEventId, Is.EqualTo(eventRecord.EventId));
        }

        /// <summary>
        /// Verifies that a failed native processed-history resume can retry the same continuation.
        /// </summary>
        [Test]
        public async Task NativeProcessedResumeProviderFailureCanRetrySameContinuationAsync()
        {
            var continuationPoints = new SessionContinuationPoints(
                () => NodeId.Null,
                maxBrowse: 10,
                maxHistory: 10,
                store: null);
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints,
                out AggregateManager aggregateManager);
            var nodeId = new NodeId("native-processed-retry", 1);
            var aggregateId = new NodeId("native-processed-aggregate", 1);
            await RegisterAggregateAsync(aggregateManager, aggregateId);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            Mock<IHistorianProcessedProvider> processedProvider =
                provider.As<IHistorianProcessedProvider>();
            processedProvider
                .SetupSequence(p => p.ReadProcessedAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianProcessedReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError))
                .Returns(new ValueTask<HistorianPage<DataValue>>(
                    new HistorianPage<DataValue>(
                        [CreateDataValue(2)])));
            var state = CreateProcessedState(
                Guid.NewGuid(),
                provider.Object,
                nodeId,
                aggregateId);
            continuationPoints.SaveHistory(state);
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.From(state.Id.ToByteArray())
            };
            ReadProcessedDetails details = CreateProcessedDetails();

            Assert.ThrowsAsync<ServiceResultException>(
                async () => await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    TimestampsToReturn.Source,
                    new HistoryReadResult(),
                    CancellationToken.None));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError =
                await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    details,
                    aggregateId,
                    TimestampsToReturn.Source,
                    retryResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.True);
        }

        /// <summary>
        /// Verifies that a failed buffered processed-history successor preserves its identifier and offset.
        /// </summary>
        [Test]
        public async Task BufferedProcessedSuccessorFailurePreservesIdAndOffsetAsync()
        {
            var continuationPoints = new TestContinuationPoints();
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints,
                out AggregateManager aggregateManager);
            var nodeId = new NodeId("buffered-processed-retry", 1);
            var aggregateId = new NodeId("buffered-processed-aggregate", 1);
            await RegisterAggregateAsync(aggregateManager, aggregateId);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            _ = provider.As<IHistorianDataProvider>();
            Guid oldId = Guid.NewGuid();
            HistorianContinuationState state = CreateProcessedState(
                oldId,
                provider.Object,
                nodeId,
                aggregateId);
            state.BufferedProcessedOutputs =
                new HistorianBufferedProcessedPayload(
                [
                    CreateDataValue(1),
                    CreateDataValue(2),
                    CreateDataValue(3)
                ]);
            object bufferedPayload = state.BufferedProcessedOutputs;
            state.BufferedProcessedOffset = 0;
            continuationPoints.SaveHistory(state);
            continuationPoints.AsyncSaveFailuresRemaining = 1;
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.From(oldId.ToByteArray())
            };

            var failedResult = new HistoryReadResult();
            ServiceResult failedError =
                await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    CreateProcessedDetails(),
                    aggregateId,
                    TimestampsToReturn.Source,
                    failedResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(failedError), Is.True);
            Assert.That(
                failedResult.StatusCode,
                Is.EqualTo(StatusCodes.BadNoContinuationPoints));
            Assert.That(continuationPoints.Contains(oldId), Is.True);
            Assert.That(
                continuationPoints.Peek(oldId).BufferedProcessedOffset,
                Is.Zero);
            Assert.That(continuationPoints.AsyncSavedPoints, Has.Count.EqualTo(2));
            var failedSuccessor =
                (HistorianContinuationState)continuationPoints.AsyncSavedPoints[0];
            var restoredClone =
                (HistorianContinuationState)continuationPoints.AsyncSavedPoints[1];
            Assert.That(failedSuccessor.Id, Is.Not.EqualTo(oldId));
            Assert.That(failedSuccessor.BufferedProcessedOffset, Is.EqualTo(1));
            Assert.That(restoredClone.Id, Is.EqualTo(oldId));
            Assert.That(restoredClone.BufferedProcessedOffset, Is.Zero);
            Assert.That(restoredClone, Is.Not.SameAs(state));
            Assert.That(
                restoredClone.BufferedProcessedOutputs,
                Is.SameAs(bufferedPayload));

            var retryResult = new HistoryReadResult();
            ServiceResult retryError =
                await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    CreateProcessedDetails(),
                    aggregateId,
                    TimestampsToReturn.Source,
                    retryResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(retryResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(retryResult.ContinuationPoint.IsEmpty, Is.False);
            Assert.That(
                retryResult.ContinuationPoint,
                Is.Not.EqualTo(nodeToRead.ContinuationPoint));
            Assert.That(continuationPoints.Contains(oldId), Is.False);
            Guid successorId = new(retryResult.ContinuationPoint.ToArray());
            Assert.That(continuationPoints.Contains(successorId), Is.True);
            HistorianContinuationState successor =
                continuationPoints.Peek(successorId);
            Assert.That(
                successor.BufferedProcessedOffset,
                Is.EqualTo(1));
            Assert.That(
                successor.BufferedProcessedOutputs,
                Is.SameAs(bufferedPayload));
            Assert.That(
                retryResult.HistoryData.TryGetValue(out HistoryData history),
                Is.True);
            Assert.That(history!.DataValues, Has.Count.EqualTo(1));
            Assert.That(
                history.DataValues[0].WrappedValue.TryGetValue(out int value),
                Is.True);
            Assert.That(value, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that buffered continuation successors and restored state safely share their payload.
        /// </summary>
        [Test]
        public void BufferedSuccessorAndRestorationSharePayloadSafely()
        {
            var provider = new Mock<IHistorianProvider>();
            HistorianContinuationState state = CreateProcessedState(
                Guid.NewGuid(),
                provider.Object,
                new NodeId("shared-buffer", 1),
                new NodeId("shared-buffer-aggregate", 1));
            state.BufferedProcessedOutputs =
                new HistorianBufferedProcessedPayload(
                [
                    CreateDataValue(1),
                    CreateDataValue(2),
                    CreateDataValue(3)
                ]);
            object bufferedPayload = state.BufferedProcessedOutputs;
            using HistorianContinuationState successor =
                state.CreateSuccessor(
                    new HistorianResumeToken(ByteString.From([2])),
                    bufferedProcessedOffset: 1);
            using HistorianContinuationState restoration =
                state.CreateRestorationCopy();

            Assert.That(
                successor.BufferedProcessedOutputs,
                Is.SameAs(bufferedPayload));
            Assert.That(
                restoration.BufferedProcessedOutputs,
                Is.SameAs(bufferedPayload));

            state.Dispose();

            Assert.That(successor.BufferedProcessedOutputs, Has.Count.EqualTo(3));
            successor.Dispose();
            Assert.That(restoration.BufferedProcessedOutputs, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Verifies that terminal processed-history incompatibility retires the continuation.
        /// </summary>
        [Test]
        public async Task ProcessedTerminalIncompatibilityRetiresContinuationAsync()
        {
            var continuationPoints = new TestContinuationPoints();
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints,
                out _);
            var nodeId = new NodeId("processed-terminal", 1);
            var aggregateId = new NodeId("unsupported-after-save", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            _ = provider.As<IHistorianProcessedProvider>();
            Guid oldId = Guid.NewGuid();
            continuationPoints.SaveHistory(CreateProcessedState(
                oldId,
                provider.Object,
                nodeId,
                aggregateId));
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.From(oldId.ToByteArray())
            };
            var firstResult = new HistoryReadResult();

            ServiceResult firstError =
                await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    CreateProcessedDetails(),
                    aggregateId,
                    TimestampsToReturn.Source,
                    firstResult,
                    CancellationToken.None);

            Assert.That(
                firstError.StatusCode,
                Is.EqualTo(StatusCodes.BadAggregateNotSupported));
            Assert.That(continuationPoints.Contains(oldId), Is.False);

            var retryResult = new HistoryReadResult();
            ServiceResult retryError =
                await HistorianDispatcher.DispatchProcessedReadAsync(
                    systemContext,
                    provider.Object,
                    node,
                    nodeToRead,
                    CreateProcessedDetails(),
                    aggregateId,
                    TimestampsToReturn.Source,
                    retryResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(retryError), Is.True);
            Assert.That(
                retryResult.StatusCode,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        /// <summary>
        /// Verifies that failed durable restoration falls back to the local continuation state.
        /// </summary>
        [Test]
        public async Task DurableRestorationFailureFallsBackToLocalStateAsync()
        {
            var continuationPoints = new TestContinuationPoints();
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var nodeId = new NodeId("raw-local-fallback", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var provider = new Mock<IHistorianProvider>();
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    nodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    HistorianNodeCapabilities.ReadOnly));
            provider
                .As<IHistorianDataProvider>()
                .Setup(p => p.ReadRawAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianRawReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(
                    StatusCodes.BadCommunicationError));
            Guid oldId = Guid.NewGuid();
            var originalState = new HistorianContinuationState
            {
                Id = oldId,
                Provider = provider.Object,
                NodeId = nodeId,
                Kind = HistorianReadKind.Raw,
                ResumeToken = new HistorianResumeToken(
                    ByteString.From([1])),
                RawRequest = new HistorianRawReadRequest
                {
                    NodeId = nodeId,
                    StartTime = s_baseTime,
                    EndTime = s_baseTime.AddMinutes(1),
                    MaxValues = 1,
                    IsForward = true
                }
            };
            continuationPoints.SaveHistory(originalState);
            continuationPoints.AsyncSaveFailuresRemaining = 1;
            var nodeToRead = new HistoryReadValueId
            {
                NodeId = nodeId,
                ContinuationPoint = ByteString.From(oldId.ToByteArray())
            };

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await HistorianDispatcher.DispatchRawReadAsync(
                        systemContext,
                        provider.Object,
                        node,
                        nodeToRead,
                        new ReadRawModifiedDetails(),
                        TimestampsToReturn.Source,
                        new HistoryReadResult(),
                        CancellationToken.None))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(continuationPoints.AsyncSaveAttempts, Is.EqualTo(1));
            Assert.That(continuationPoints.LocalSaveCount, Is.EqualTo(2));
            Assert.That(continuationPoints.Contains(oldId), Is.True);
            Assert.That(continuationPoints.AsyncSavedPoints, Has.Count.EqualTo(1));
            Assert.That(
                continuationPoints.AsyncSavedPoints[0].Id,
                Is.EqualTo(oldId));
            Assert.That(
                continuationPoints.AsyncSavedPoints[0],
                Is.Not.SameAs(originalState));
            Assert.That(
                continuationPoints.Peek(oldId),
                Is.SameAs(originalState));
        }

        /// <summary>
        /// Verifies that durable annotation resume addresses the annotation property's parent node.
        /// </summary>
        [Test]
        public async Task DurableAnnotationResumeUsesParentNodeIdAsync()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:durable-annotation");
            var serverUris = new StringTable();
            var telemetry = new Mock<ITelemetryContext>();
            var messageContext = new ServiceMessageContext(
                telemetry.Object,
                EncodeableFactory.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.MessageContext).Returns(messageContext);
            server.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(s => s.ServerUris).Returns(serverUris);
            server.SetupGet(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.Telemetry).Returns(telemetry.Object);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(s => s.HistorianRegistry)
                .Returns(registry);

            var propertyNodeId = new NodeId("Annotations", 1);
            var parentNodeId = new NodeId("HistorizedVariable", 1);
            var provider = new Mock<IHistorianProvider>();
            provider.As<IHistorianProviderIdentity>()
                .SetupGet(p => p.ProviderId)
                .Returns("durable-annotation-provider");
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    It.IsAny<NodeId>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    new HistorianNodeCapabilities
                    {
                        PortableResumeTokens = true
                    }));
            NodeId providerRequestNodeId = NodeId.Null;
            provider.As<IHistorianAnnotationProvider>()
                .Setup(p => p.ReadAnnotationsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianAnnotationReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Callback<HistorianOperationContext,
                    HistorianAnnotationReadRequest,
                    HistorianResumeToken,
                    CancellationToken>(
                    (_, request, _, _) =>
                        providerRequestNodeId = request.NodeId)
                .Returns(new ValueTask<HistorianPage<Annotation>>(
                    HistorianPage<Annotation>.Empty));
            registry.RegisterForNode(parentNodeId, provider.Object);

            var store = new DurableHistoryStore();
            var codec = new HistorianContinuationPointCodec(server.Object);
            NodeId sourceSessionId = new(Guid.NewGuid());
            var sourcePoints = new SessionContinuationPoints(
                () => sourceSessionId,
                maxBrowse: 4,
                maxHistory: 4,
                store: null,
                historyStore: store,
                historyCodec: codec,
                namespaceUris: namespaceUris);
            Guid continuationId = Guid.NewGuid();
            await sourcePoints.SaveHistoryAsync(
                new HistorianContinuationState
                {
                    Id = continuationId,
                    Provider = provider.Object,
                    Kind = HistorianReadKind.Annotations,
                    NodeId = propertyNodeId,
                    ResumeToken = new HistorianResumeToken(
                        ByteString.From([1])),
                    TimestampsToReturn = TimestampsToReturn.Source,
                    AnnotationRequest = new HistorianAnnotationReadRequest
                    {
                        NodeId = parentNodeId,
                        StartTime = s_baseTime,
                        EndTime = s_baseTime.AddMinutes(1),
                        MaxValues = 1,
                        IsForward = true
                    }
                },
                CancellationToken.None);

            NodeId targetSessionId = new(Guid.NewGuid());
            var targetPoints = new SessionContinuationPoints(
                () => targetSessionId,
                maxBrowse: 4,
                maxHistory: 4,
                store: null,
                historyStore: store,
                historyCodec: codec,
                namespaceUris: namespaceUris);
            await targetPoints.LoadMirroredAsync(
                sourceSessionId,
                CancellationToken.None);
            ServerSystemContext systemContext = CreateSystemContext(
                targetPoints,
                server.Object);
            var result = new HistoryReadResult();

            ServiceResult error =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    CreateVariable(parentNodeId),
                    new HistoryReadValueId
                    {
                        NodeId = propertyNodeId,
                        ContinuationPoint = ByteString.From(
                            continuationId.ToByteArray())
                    },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(providerRequestNodeId, Is.EqualTo(parentNodeId));
        }

        /// <summary>
        /// Verifies that durable legacy annotation resume normalizes its node identity.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task DurableLegacyAnnotationResumeNormalizesIdentityAsync(
            int formatVersion)
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:test:durable-legacy-annotation");
            var serverUris = new StringTable();
            var telemetry = new Mock<ITelemetryContext>();
            var messageContext = new ServiceMessageContext(
                telemetry.Object,
                EncodeableFactory.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };
            var registry = new HistorianProviderRegistry(namespaceUris);
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.MessageContext).Returns(messageContext);
            server.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            server.SetupGet(s => s.ServerUris).Returns(serverUris);
            server.SetupGet(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.Telemetry).Returns(telemetry.Object);
            server.As<IHistorianRegistryProvider>()
                .SetupGet(s => s.HistorianRegistry)
                .Returns(registry);

            var propertyNodeId = new NodeId("Annotations", 1);
            var parentNodeId = new NodeId("HistorizedVariable", 1);
            var provider = new Mock<IHistorianProvider>();
            provider.As<IHistorianProviderIdentity>()
                .SetupGet(p => p.ProviderId)
                .Returns("durable-legacy-annotation-provider");
            provider
                .Setup(p => p.GetCapabilitiesAsync(
                    parentNodeId,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<HistorianNodeCapabilities>(
                    new HistorianNodeCapabilities
                    {
                        PortableResumeTokens = true
                    }));
            var providerRequestNodeIds = new List<NodeId>();
            int readCount = 0;
            provider.As<IHistorianAnnotationProvider>()
                .Setup(p => p.ReadAnnotationsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianAnnotationReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()))
                .Returns<HistorianOperationContext,
                    HistorianAnnotationReadRequest,
                    HistorianResumeToken,
                    CancellationToken>(
                    (_, request, _, _) =>
                    {
                        providerRequestNodeIds.Add(request.NodeId);
                        readCount++;
                        return new ValueTask<HistorianPage<Annotation>>(
                            readCount == 1
                                ? new HistorianPage<Annotation>(
                                    [CreateAnnotation(1)],
                                    new HistorianResumeToken(
                                        ByteString.From([2])))
                                : HistorianPage<Annotation>.Empty);
                    });
            registry.RegisterForNode(parentNodeId, provider.Object);

            var store = new DurableHistoryStore();
            var codec = new HistorianContinuationPointCodec(server.Object);
            NodeId sourceSessionId = new(Guid.NewGuid());
            Guid legacyId = Guid.NewGuid();
            await store.StoreAsync(
                CreateLegacyAnnotationEnvelope(
                    messageContext,
                    namespaceUris,
                    serverUris,
                    provider.As<IHistorianProviderIdentity>().Object.ProviderId,
                    sourceSessionId,
                    legacyId,
                    parentNodeId,
                    formatVersion),
                CancellationToken.None);

            NodeId targetSessionId = new(Guid.NewGuid());
            var targetPoints = new SessionContinuationPoints(
                () => targetSessionId,
                maxBrowse: 4,
                maxHistory: 4,
                store: null,
                historyStore: store,
                historyCodec: codec,
                namespaceUris: namespaceUris);
            await targetPoints.LoadMirroredAsync(
                sourceSessionId,
                CancellationToken.None);
            ServerSystemContext systemContext = CreateSystemContext(
                targetPoints,
                server.Object);
            var firstResult = new HistoryReadResult();

            ServiceResult firstError =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    CreateVariable(parentNodeId),
                    new HistoryReadValueId
                    {
                        NodeId = propertyNodeId,
                        ContinuationPoint = ByteString.From(
                            legacyId.ToByteArray())
                    },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    firstResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(firstError), Is.True);
            Assert.That(firstResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(firstResult.ContinuationPoint.IsEmpty, Is.False);
            ArrayOf<HistoryContinuationPointEnvelope> successors =
                await store.LoadAsync(
                    targetSessionId,
                    CancellationToken.None);
            Assert.That(successors, Has.Count.EqualTo(1));
            Assert.That(successors[0].CodecVersion, Is.EqualTo(3));
            var decodedSuccessor =
                (HistorianContinuationState)await codec.DecodeAsync(
                    successors[0],
                    CancellationToken.None);
            Assert.That(decodedSuccessor, Is.Not.Null);
            Assert.That(decodedSuccessor!.NodeId, Is.EqualTo(propertyNodeId));
            Assert.That(
                decodedSuccessor.UsesLegacyAnnotationNodeId,
                Is.False);
            Assert.That(decodedSuccessor.AnnotationRequest, Is.Not.Null);
            Assert.That(
                decodedSuccessor.AnnotationRequest!.NodeId,
                Is.EqualTo(parentNodeId));

            var secondResult = new HistoryReadResult();
            ServiceResult secondError =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    CreateVariable(parentNodeId),
                    new HistoryReadValueId
                    {
                        NodeId = propertyNodeId,
                        ContinuationPoint = firstResult.ContinuationPoint
                    },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    secondResult,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(secondError), Is.True);
            Assert.That(secondResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(secondResult.ContinuationPoint.IsEmpty, Is.True);
            Assert.That(
                providerRequestNodeIds,
                Is.EqualTo(new[] { parentNodeId, parentNodeId }));
        }

        /// <summary>
        /// Verifies that annotation resume rejects a different parent node.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AnnotationResumeRejectsDifferentParentAsync(
            bool legacy)
        {
            var continuationPoints = new TestContinuationPoints();
            ServerSystemContext systemContext = CreateSystemContext(
                continuationPoints);
            var savedParentNodeId = new NodeId("SavedParent", 1);
            var currentParentNodeId = new NodeId("CurrentParent", 1);
            var propertyNodeId = new NodeId("Annotations", 1);
            var provider = new Mock<IHistorianProvider>();
            _ = provider.As<IHistorianAnnotationProvider>();
            Guid continuationId = Guid.NewGuid();
            continuationPoints.SaveHistory(
                new HistorianContinuationState
                {
                    Id = continuationId,
                    Provider = provider.Object,
                    Kind = HistorianReadKind.Annotations,
                    NodeId = legacy
                        ? savedParentNodeId
                        : propertyNodeId,
                    UsesLegacyAnnotationNodeId = legacy,
                    ResumeToken = new HistorianResumeToken(
                        ByteString.From([1])),
                    AnnotationRequest = new HistorianAnnotationReadRequest
                    {
                        NodeId = savedParentNodeId,
                        StartTime = s_baseTime,
                        EndTime = s_baseTime.AddMinutes(1),
                        MaxValues = 1,
                        IsForward = true
                    }
                });
            var result = new HistoryReadResult();

            ServiceResult error =
                await HistorianDispatcher.DispatchAnnotationReadAsync(
                    systemContext,
                    provider.Object,
                    CreateVariable(currentParentNodeId),
                    new HistoryReadValueId
                    {
                        NodeId = propertyNodeId,
                        ContinuationPoint = ByteString.From(
                            continuationId.ToByteArray())
                    },
                    new ReadRawModifiedDetails(),
                    TimestampsToReturn.Source,
                    result,
                    CancellationToken.None);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
            Assert.That(continuationPoints.Contains(continuationId), Is.False);
            provider.As<IHistorianAnnotationProvider>().Verify(
                p => p.ReadAnnotationsAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<HistorianAnnotationReadRequest>(),
                    It.IsAny<HistorianResumeToken>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static readonly DateTime s_baseTime =
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static HistoricalDataValue CreateHistoricalValue(int value)
        {
            DateTime timestamp = s_baseTime.AddSeconds(value);
            return new HistoricalDataValue(
                new DataValue(
                    new Variant(value),
                    StatusCodes.Good,
                    timestamp,
                    timestamp));
        }

        private static ModifiedDataValue CreateModifiedValue(int value)
        {
            return new ModifiedDataValue(
                CreateHistoricalValue(value).Value,
                new ModificationInfo
                {
                    ModificationTime = s_baseTime.AddMinutes(value),
                    UpdateType = HistoryUpdateType.Replace,
                    UserName = "tester"
                });
        }

        private static Annotation CreateAnnotation(int value)
        {
            return new Annotation
            {
                AnnotationTime = s_baseTime.AddSeconds(value),
                Message = $"annotation-{value}",
                UserName = "tester"
            };
        }

        private static HistorianEventRecord CreateEventRecord(int value)
        {
            var eventId = ByteString.From([(byte)value]);
            return new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                s_baseTime.AddSeconds(value),
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(eventId),
                    [BrowseNames.Message] = new Variant(
                        new LocalizedText($"event-{value}"))
                }.ToArrayOf());
        }

        private static DataValue CreateDataValue(int value)
        {
            DateTime timestamp = s_baseTime.AddSeconds(value);
            return new DataValue(
                new Variant(value),
                StatusCodes.Good,
                timestamp,
                timestamp);
        }

        private static ReadProcessedDetails CreateProcessedDetails()
        {
            return new ReadProcessedDetails
            {
                StartTime = s_baseTime,
                EndTime = s_baseTime.AddMinutes(1),
                ProcessingInterval = 1000
            };
        }

        private static HistorianContinuationState CreateProcessedState(
            Guid id,
            IHistorianProvider provider,
            NodeId nodeId,
            NodeId aggregateId)
        {
            return new HistorianContinuationState
            {
                Id = id,
                Provider = provider,
                NodeId = nodeId,
                Kind = HistorianReadKind.Processed,
                ResumeToken = new HistorianResumeToken(
                    ByteString.From([1])),
                ProcessedRequest = new HistorianProcessedReadRequest
                {
                    NodeId = nodeId,
                    AggregateId = aggregateId,
                    StartTime = s_baseTime,
                    EndTime = s_baseTime.AddMinutes(1),
                    ProcessingInterval = 1000,
                    MaxValues = 1,
                    Configuration = new AggregateConfiguration
                    {
                        PercentDataBad = 100,
                        PercentDataGood = 100,
                        TreatUncertainAsBad = true
                    }
                },
                TimestampsToReturn = TimestampsToReturn.Source
            };
        }

        private static HistoryContinuationPointEnvelope
            CreateLegacyAnnotationEnvelope(
            ServiceMessageContext messageContext,
            NamespaceTable namespaceUris,
            StringTable serverUris,
            string providerId,
            NodeId ownerSessionId,
            Guid id,
            NodeId nodeId,
            int formatVersion)
        {
            using var encoder = new BinaryEncoder(messageContext);
            encoder.WriteInt32(null, formatVersion);
            if (formatVersion >= 2)
            {
                encoder.WriteStringArray(null, namespaceUris.ToArrayOf());
                encoder.WriteStringArray(null, serverUris.ToArrayOf());
                encoder.SetMappingTables(namespaceUris, serverUris);
            }
            encoder.WriteString(null, providerId);
            encoder.WriteEnumerated(null, HistorianReadKind.Annotations);
            encoder.WriteNodeId(null, nodeId);
            encoder.WriteByteString(null, ByteString.From([1]));
            encoder.WriteEnumerated(null, TimestampsToReturn.Source);
            encoder.WriteString(null, string.Empty);
            encoder.WriteQualifiedName(null, QualifiedName.Null);
            encoder.WriteDateTime(null, s_baseTime);
            encoder.WriteDateTime(null, s_baseTime.AddMinutes(1));
            encoder.WriteUInt32(null, 1);
            encoder.WriteBoolean(null, true);
            byte[] payload = encoder.CloseAndReturnBuffer() ??
                throw new InvalidOperationException(
                    "The legacy continuation payload was not encoded.");
            return new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = ownerSessionId,
                CodecId = "opcua-historian",
                CodecVersion = (uint)formatVersion,
                Payload = ByteString.From(payload)
            };
        }

        private static BaseDataVariableState CreateVariable(NodeId nodeId)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("TestVariable"),
                AccessLevel = AccessLevels.HistoryRead,
                Historizing = true
            };
        }

        private static ServerSystemContext CreateSystemContext(
            ISessionContinuationPoints continuationPoints)
        {
            var telemetry = new Mock<ITelemetryContext>();
            var session = new Mock<ISession>();
            session
                .Setup(s => s.ContinuationPoints)
                .Returns(continuationPoints);
            var server = new Mock<IServerInternal>();
            var namespaceUris = new NamespaceTable();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry.Object);
            var operationContext = new OperationContext(
                new RequestHeader(),
                null!,
                RequestType.HistoryRead,
                RequestLifetime.None,
                session.Object);
            return new ServerSystemContext(server.Object, operationContext);
        }

        private static ServerSystemContext CreateSystemContext(
            ISessionContinuationPoints continuationPoints,
            IServerInternal server)
        {
            var session = new Mock<ISession>();
            session
                .Setup(s => s.ContinuationPoints)
                .Returns(continuationPoints);
            var operationContext = new OperationContext(
                new RequestHeader(),
                null!,
                RequestType.HistoryRead,
                RequestLifetime.None,
                session.Object);
            return new ServerSystemContext(server, operationContext);
        }

        private static ServerSystemContext CreateSystemContext(
            ISessionContinuationPoints continuationPoints,
            out AggregateManager aggregateManager)
        {
            var telemetry = new Mock<ITelemetryContext>();
            var session = new Mock<ISession>();
            session
                .Setup(s => s.ContinuationPoints)
                .Returns(continuationPoints);
            var server = new Mock<IServerInternal>();
            var namespaceUris = new NamespaceTable();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry.Object);
            server
                .Setup(s => s.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            aggregateManager = new AggregateManager(server.Object);
            server.Setup(s => s.AggregateManager).Returns(aggregateManager);
            var operationContext = new OperationContext(
                new RequestHeader(),
                null!,
                RequestType.HistoryRead,
                RequestLifetime.None,
                session.Object);
            return new ServerSystemContext(server.Object, operationContext);
        }

        private static ValueTask RegisterAggregateAsync(
            AggregateManager aggregateManager,
            NodeId aggregateId)
        {
            return aggregateManager.RegisterFactoryAsync(
                aggregateId,
                aggregateId.ToString(),
                static (_, _, _, _, _, _, _) => null);
        }

        private sealed class TestContinuationPoints :
            ISessionContinuationPoints
        {
            public int MaxBrowse => 0;

            public int AsyncSaveFailuresRemaining { get; set; }

            public int AsyncSaveAttempts { get; private set; }

            public int LocalSaveCount { get; private set; }

            public List<IHistoryContinuationPoint> AsyncSavedPoints { get; } = [];

            public bool Contains(Guid id)
            {
                return m_history.ContainsKey(id);
            }

            public HistorianContinuationState Peek(Guid id)
            {
                return (HistorianContinuationState)m_history[id];
            }

            public void SaveBrowse(ContinuationPoint continuationPoint)
            {
                throw new NotSupportedException();
            }

            public ContinuationPoint RestoreBrowse(
                ByteString continuationPoint)
            {
                throw new NotSupportedException();
            }

            public void SaveHistory(
                IHistoryContinuationPoint continuationPoint)
            {
                LocalSaveCount++;
                m_history.Add(continuationPoint.Id, continuationPoint);
            }

            public ValueTask SaveHistoryAsync(
                IHistoryContinuationPoint continuationPoint,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AsyncSaveAttempts++;
                AsyncSavedPoints.Add(continuationPoint);
                if (AsyncSaveFailuresRemaining > 0)
                {
                    AsyncSaveFailuresRemaining--;
                    continuationPoint.Dispose();
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError);
                }
                SaveHistory(continuationPoint);
                return default;
            }

            public IHistoryContinuationPoint RestoreHistory(
                ByteString continuationPoint)
            {
                Guid id = new(continuationPoint.ToArray());
                if (!m_history.TryGetValue(
                        id,
                        out IHistoryContinuationPoint value))
                {
                    return null;
                }
                m_history.Remove(id);
                return value;
            }

            public bool ReleaseHistory(ByteString continuationPoint)
            {
                IHistoryContinuationPoint value =
                    RestoreHistory(continuationPoint);
                value?.Dispose();
                return value != null;
            }

            public ValueTask<IHistoryContinuationPoint> RestoreHistoryAsync(
                ByteString continuationPoint,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IHistoryContinuationPoint>(
                    RestoreHistory(continuationPoint));
            }

            public void RemoveForManager(IAsyncNodeManager nodeManager)
            {
            }

            private readonly Dictionary<Guid, IHistoryContinuationPoint>
                m_history = [];
        }

        private sealed class DurableHistoryStore :
            IHistoryContinuationPointStore
        {
            public ValueTask StoreAsync(
                HistoryContinuationPointEnvelope envelope,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int existing = m_envelopes.FindIndex(
                    item => item.OwnerSessionId == envelope.OwnerSessionId &&
                        item.Id == envelope.Id);
                if (existing >= 0)
                {
                    m_envelopes[existing] = envelope;
                }
                else
                {
                    m_envelopes.Add(envelope);
                }
                return default;
            }

            public ValueTask<bool> TryTakeAsync(
                NodeId ownerSessionId,
                Guid id,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int index = m_envelopes.FindIndex(
                    item => item.OwnerSessionId == ownerSessionId &&
                        item.Id == id);
                if (index < 0)
                {
                    return new ValueTask<bool>(false);
                }
                m_envelopes.RemoveAt(index);
                return new ValueTask<bool>(true);
            }

            public void ScheduleRemove(NodeId ownerSessionId, Guid id)
            {
                int index = m_envelopes.FindIndex(
                    item => item.OwnerSessionId == ownerSessionId &&
                        item.Id == id);
                if (index >= 0)
                {
                    m_envelopes.RemoveAt(index);
                }
            }

            public ValueTask<ArrayOf<HistoryContinuationPointEnvelope>>
                LoadAsync(
                NodeId ownerSessionId,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<
                    ArrayOf<HistoryContinuationPointEnvelope>>(
                        m_envelopes.FindAll(
                            item => item.OwnerSessionId == ownerSessionId)
                        .ToArrayOf());
            }

            private readonly List<HistoryContinuationPointEnvelope>
                m_envelopes = [];
        }
    }
}
