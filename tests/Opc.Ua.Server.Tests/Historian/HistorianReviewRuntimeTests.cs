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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Verifies capture failure isolation and historical condition identity.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class HistorianReviewRuntimeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureSurvivesProviderFailuresWithExactDropsAndBoundedLogsAsync(bool canceledByProvider)
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            ServerSystemContext context = CreateSystemContext(logger.Object);
            var clock = new FakeTimeProvider(s_start);
            var provider = new Mock<IHistorianProvider>();
            int calls = 0;
            provider.As<IHistorianBulkInsertProvider>().Setup(p => p.InsertBatchAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<ArrayOf<HistorianDataBatch>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext _, ArrayOf<HistorianDataBatch> batch, CancellationToken _) =>
                {
                    if (++calls <= 64)
                    {
                        if (canceledByProvider)
                        {
                            throw new OperationCanceledException("Provider timeout, not sink shutdown.");
                        }
                        throw new IOException("Provider unavailable.");
                    }
                    ArrayOf<HistorianUpdateOutcome<DataValue>> result =
                        [new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])];
                    return new ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>>(result);
                });
            var sink = new HistorianCaptureSink(
                provider.Object,
                context,
                new HistorianCaptureOptions { BatchTarget = 1, MaxQueuedSamples = 128 },
                clock);
            for (int i = 0; i < 65; i++)
            {
                sink.Enqueue(new NodeId("capture", 1), new DataValue(i, StatusCodes.Good, s_start.AddSeconds(i)));
            }
            await sink.DisposeAsync().ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(65), "The consumer must process the successful batch after every failure.");
            Assert.That(sink.DroppedSampleCount, Is.EqualTo(64));
            Assert.That(sink.RejectedSampleCount, Is.Zero);
            Assert.That(logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log)),
                Is.EqualTo(1), "A sustained outage must not generate one warning per sample.");
        }

        [Test]
        public async Task CaptureCountsOnlyFailedNodesInPartiallySuccessfulBatchAsync()
        {
            var provider = new Mock<IHistorianProvider>();
            var failedNode = new NodeId("failed", 1);
            var successfulNodes = new List<NodeId>();
            provider.As<IHistorianDataProvider>().Setup(p => p.InsertAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<ArrayOf<DataValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext _, NodeId nodeId, ArrayOf<DataValue> values, CancellationToken _) =>
                {
                    if (nodeId == failedNode)
                    {
                        throw new IOException("One node is unavailable.");
                    }
                    successfulNodes.Add(nodeId);
                    return new ValueTask<HistorianUpdateOutcome<DataValue>>(
                        new HistorianUpdateOutcome<DataValue>([StatusCodes.Good]));
                });
            var firstNode = new NodeId("first", 1);
            var lastNode = new NodeId("last", 1);
            var sink = new HistorianCaptureSink(
                provider.Object,
                CreateSystemContext(),
                new HistorianCaptureOptions { BatchTarget = 3, BatchWindow = TimeSpan.FromMinutes(1) });
            sink.Enqueue(firstNode, new DataValue(1));
            sink.Enqueue(failedNode, new DataValue(2));
            sink.Enqueue(lastNode, new DataValue(3));
            await sink.DisposeAsync().ConfigureAwait(false);

            Assert.That(successfulNodes, Is.EquivalentTo(new[] { firstNode, lastNode }));
            Assert.That(sink.DroppedSampleCount, Is.EqualTo(1));
            Assert.That(sink.RejectedSampleCount, Is.Zero);
        }

        [Test]
        public async Task CaptureFailureDiagnosticsRespectThirtySecondBoundaryAsync()
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            var clock = new FakeTimeProvider(s_start);
            var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IHistorianProvider>();
            provider.As<IHistorianBulkInsertProvider>().Setup(p => p.InsertBatchAsync(
                    It.IsAny<HistorianOperationContext>(),
                    It.IsAny<ArrayOf<HistorianDataBatch>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((HistorianOperationContext _, ArrayOf<HistorianDataBatch> batch, CancellationToken _) =>
                {
                    if (batch[0].Values[0].StatusCode == StatusCodes.Bad)
                    {
                        throw new IOException("Provider unavailable.");
                    }
                    barrier.TrySetResult(true);
                    ArrayOf<HistorianUpdateOutcome<DataValue>> result =
                        [new HistorianUpdateOutcome<DataValue>([StatusCodes.Good])];
                    return new ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>>(result);
                });
            var sink = new HistorianCaptureSink(
                provider.Object, CreateSystemContext(logger.Object),
                new HistorianCaptureOptions { BatchTarget = 1 }, clock);
            for (int phase = 0; phase < 3; phase++)
            {
                barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (phase > 0)
                {
                    clock.Advance(TimeSpan.FromSeconds(phase == 1 ? 29 : 1));
                }
                sink.Enqueue(new NodeId("failed", 1), new DataValue(0, StatusCodes.Bad));
                sink.Enqueue(new NodeId("barrier", 1), new DataValue(1));
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(logger.Invocations.Count(i => i.Method.Name == nameof(ILogger.Log)),
                    Is.EqualTo(phase == 2 ? 2 : 1));
            }
            await sink.DisposeAsync().ConfigureAwait(false);
            Assert.That(sink.DroppedSampleCount, Is.EqualTo(3));
        }

        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        public async Task CaptureShutdownRetainsTokenResourcesUntilProviderCompletesAsync(
            bool events,
            bool bulk,
            bool failAfterRelease)
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            ServerSystemContext context = CreateSystemContext(logger.Object);
            var clock = new FakeTimeProvider(s_start);
            var entered = new TaskCompletionSource<CancellationToken>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IHistorianProvider>();
            int calls = 0;
            IAsyncDisposable capture;
            var options = new HistorianCaptureOptions { BatchTarget = bulk ? 1 : 2 };
            if (events)
            {
                provider.As<IHistorianEventProvider>().Setup(p => p.UpdateEventsAsync(
                        It.IsAny<HistorianOperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<ArrayOf<HistorianEventRecord>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((HistorianOperationContext _, NodeId _, ArrayOf<HistorianEventRecord> _,
                        CancellationToken ct) => FinishProviderAsync<HistorianEventRecord>(ct));
                var pump = new HistorianEventCapture(
                    context.Server, provider.Object, HistorianNodeCapabilities.EventReadWrite, options, clock);
                capture = pump;
                var notifier = new BaseObjectState(null) { NodeId = new NodeId("shutdown", 1) };
                var reportedEvent = new BaseEventState(null);
                reportedEvent.Initialize(context, notifier, EventSeverity.Medium, new LocalizedText("shutdown"));
                pump.Enqueue(context, notifier, reportedEvent);
                pump.Enqueue(context, new BaseObjectState(null) { NodeId = new NodeId("later", 1) }, reportedEvent);
            }
            else
            {
                if (bulk)
                {
                    provider.As<IHistorianBulkInsertProvider>().Setup(p => p.InsertBatchAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<ArrayOf<HistorianDataBatch>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((HistorianOperationContext _, ArrayOf<HistorianDataBatch> _, CancellationToken ct) =>
                            FinishBatchAsync(ct));
                }
                else
                {
                    provider.As<IHistorianDataProvider>().Setup(p => p.InsertAsync(
                            It.IsAny<HistorianOperationContext>(),
                            It.IsAny<NodeId>(),
                            It.IsAny<ArrayOf<DataValue>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((HistorianOperationContext _, NodeId _, ArrayOf<DataValue> _, CancellationToken ct) =>
                            FinishProviderAsync<DataValue>(ct));
                }
                var pump = new HistorianCaptureSink(provider.Object, context, options, clock);
                capture = pump;
                pump.Enqueue(new NodeId("shutdown", 1), new DataValue(1));
                if (!bulk)
                {
                    pump.Enqueue(new NodeId("later", 1), new DataValue(2));
                }
            }

            WaitHandle handle = null;
            try
            {
                CancellationToken token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                handle = token.WaitHandle;
                Task disposing = capture.DisposeAsync().AsTask();
                clock.Advance(TimeSpan.FromSeconds(5));
                Assert.ThrowsAsync<TimeoutException>(async () => await disposing.ConfigureAwait(false));
                Assert.That(token.IsCancellationRequested, Is.True);
                Assert.That(handle.SafeWaitHandle.IsClosed, Is.False,
                    "An in-flight provider still owns access to the cancellation token's resources.");
            }
            finally
            {
                release.TrySetResult(true);
                await capture.DisposeAsync().ConfigureAwait(false);
            }

            for (int attempt = 0; attempt < 500 && !handle.SafeWaitHandle.IsClosed; attempt++)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.That(handle.SafeWaitHandle.IsClosed, Is.True, "Completion must release the token resources.");
            Assert.That(calls, Is.EqualTo(1), "A timed-out pump must not start another provider call.");
            Assert.That(logger.Invocations.Any(call =>
                call.Method.Name == nameof(ILogger.Log) &&
                call.Arguments[3] is IOException { Message: "late provider failure" }), Is.EqualTo(failAfterRelease));

            async ValueTask<HistorianUpdateOutcome<T>> FinishProviderAsync<T>(CancellationToken ct)
            {
                calls++;
                entered.TrySetResult(ct);
                await release.Task.ConfigureAwait(false);
                if (failAfterRelease)
                {
                    throw new IOException("late provider failure");
                }
                return new HistorianUpdateOutcome<T>([StatusCodes.Good]);
            }

            async ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> FinishBatchAsync(CancellationToken ct)
            {
                HistorianUpdateOutcome<DataValue> outcome =
                    await FinishProviderAsync<DataValue>(ct).ConfigureAwait(false);
                return [outcome];
            }
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(2, true)]
        [TestCase(3, false)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        [TestCase(4, true)]
        public async Task HistoricalConditionIdSelectionAndFilteringUseStoredIdentityAsync(int fieldKind, bool where)
        {
            using var provider = new InMemoryHistorianProvider();
            ServerSystemContext systemContext = CreateSystemContext();
            var notifier = new BaseObjectState(null) { NodeId = new NodeId("conditions", 1) };
            provider.Register(notifier.NodeId, HistorianNodeCapabilities.EventReadWrite);
            var conditionId = new NodeId("actual-condition", 1);
            var operand = new SimpleAttributeOperand
            {
                TypeDefinitionId = ObjectTypeIds.ConditionType,
                BrowsePath = [],
                AttributeId = Attributes.NodeId
            };
            NodeId storedType = fieldKind switch
            {
                1 => ObjectTypeIds.AlarmConditionType,
                2 => ObjectTypeIds.AuditEventType,
                _ => ObjectTypeIds.ConditionType
            };
            ArrayOf<KeyValuePair<HistorianEventFieldKey, Variant>> fields = fieldKind == 3
                ? []
                : [new KeyValuePair<HistorianEventFieldKey, Variant>(
                    new HistorianEventFieldKey(storedType, [], Attributes.NodeId, null),
                    new Variant(conditionId))];
            if (fieldKind == 4)
            {
                fields =
                [
                    new KeyValuePair<HistorianEventFieldKey, Variant>(
                        new HistorianEventFieldKey(ObjectTypeIds.AlarmConditionType, [], Attributes.NodeId, null),
                        new Variant(new NodeId("different-condition", 1))),
                    fields[0]
                ];
            }
            var record = new HistorianEventRecord(
                ByteString.From([1]), ObjectTypeIds.AlarmConditionType, s_start, [])
            {
                QualifiedFields = fields
            };
            var filter = new EventFilter { SelectClauses = [operand] };
            if (where)
            {
                filter.WhereClause.Push(FilterOperator.Equals,
                [
                    Variant.FromStructure(operand),
                    Variant.FromStructure(new LiteralOperand { Value = new Variant(conditionId) })
                ]);
            }
            var context = new HistorianOperationContext(
                systemContext, systemContext.OperationContext!, notifier, HistoryUpdateType.Insert);
            await provider.InsertEventsAsync(
                context, notifier.NodeId, [record], CancellationToken.None).ConfigureAwait(false);
            var result = new HistoryReadResult();
            ServiceResult error = await HistorianDispatcher.DispatchEventReadAsync(
                systemContext,
                provider,
                notifier,
                new HistoryReadValueId { NodeId = notifier.NodeId },
                new ReadEventDetails
                {
                    StartTime = s_start,
                    EndTime = s_start.AddSeconds(1),
                    Filter = filter
                },
                TimestampsToReturn.Source,
                result,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryEvent history), Is.True);
            bool hasIdentity = fieldKind < 2 || fieldKind == 4;
            Assert.That(history.Events, Has.Count.EqualTo(where && !hasIdentity ? 0 : 1));
            if (!where || hasIdentity)
            {
                Variant actual = history.Events[0].EventFields[0];
                Assert.That(actual, Is.EqualTo(hasIdentity ? new Variant(conditionId) : Variant.Null));
            }
            if (fieldKind is 0 or 3 or 4)
            {
                HistoryEventFieldList projected = HistorianDispatcher.ProjectEventFields(record, filter);
                Assert.That(projected.EventFields[0],
                    Is.EqualTo(hasIdentity ? new Variant(conditionId) : Variant.Null));
            }
        }

        private static ServerSystemContext CreateSystemContext(ILogger logger = null)
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:historian-review");
            var types = new TypeTable(namespaces);
            types.AddSubtype(ObjectTypeIds.BaseEventType, NodeId.Null);
            types.AddSubtype(ObjectTypeIds.ConditionType, ObjectTypeIds.BaseEventType);
            types.AddSubtype(ObjectTypeIds.AlarmConditionType, ObjectTypeIds.ConditionType);
            types.AddSubtype(ObjectTypeIds.AuditEventType, ObjectTypeIds.BaseEventType);
            var telemetry = new Mock<ITelemetryContext>();
            if (logger != null)
            {
                var factory = new Mock<ILoggerFactory>();
                factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger);
                telemetry.SetupGet(t => t.LoggerFactory).Returns(factory.Object);
            }
            var server = new Mock<IServerInternal>();
            server.SetupGet(s => s.NamespaceUris).Returns(namespaces);
            server.SetupGet(s => s.ServerUris).Returns(new StringTable());
            server.SetupGet(s => s.TypeTree).Returns(types);
            server.SetupGet(s => s.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(s => s.Telemetry).Returns(telemetry.Object);
            var operation = new OperationContext(
                new RequestHeader(), null, RequestType.HistoryRead, RequestLifetime.None);
            return new ServerSystemContext(server.Object, operation);
        }

        private static readonly DateTime s_start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
