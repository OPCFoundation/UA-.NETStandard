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

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("Historian")]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class AsyncCustomNodeManagerHistoryRoutingTests
    {
        private static readonly DateTime BaseTime
            = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private const string TestNamespaceUri = "http://test.org/UA/HistoryRouting/";

        [Test]
        public async Task HistoryReadWithReleaseContinuationPointsCallsReleaseAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryReadVariable(h, "RelCp");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var nodesToRead = new List<HistoryReadValueId>
            {
                new()
                {
                    NodeId = variable.NodeId,
                    ContinuationPoint = new ByteString(new byte[] { 0xAB, 0xCD })
                }
            };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext,
                new ReadRawModifiedDetails { StartTime = BaseTime, EndTime = BaseTime.AddMinutes(1) },
                TimestampsToReturn.Source,
                releaseContinuationPoints: true,
                nodesToRead,
                results,
                errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadContinuationPointInvalid));
        }

        [Test]
        public async Task HistoryReadRawOnHistorizedVariableRoutesToProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryReadVariable(h, "RawRead");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            DateTime t1 = BaseTime;
            DateTime t2 = BaseTime.AddSeconds(1);
            DateTime t3 = BaseTime.AddSeconds(2);
            await provider.InsertAsync(
                h.CreateHistorianOpContext(),
                variable.NodeId,
                [
                    new DataValue(new Variant(10.0), StatusCodes.Good, t1, t1),
                    new DataValue(new Variant(20.0), StatusCodes.Good, t2, t2),
                    new DataValue(new Variant(30.0), StatusCodes.Good, t3, t3)
                ],
                CancellationToken.None).ConfigureAwait(false);

            var details = new ReadRawModifiedDetails
            {
                StartTime = BaseTime.AddSeconds(-1),
                EndTime = BaseTime.AddSeconds(10)
            };
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = variable.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext, details, TimestampsToReturn.Source,
                false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0], Is.Not.Null);

            var historyData = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;
            Assert.That(historyData, Is.Not.Null);
            Assert.That(historyData!.DataValues, Has.Count.EqualTo(3));
            Assert.That(historyData.DataValues[0].GetValue<double>(0), Is.EqualTo(10.0));
            Assert.That(historyData.DataValues[1].GetValue<double>(0), Is.EqualTo(20.0));
            Assert.That(historyData.DataValues[2].GetValue<double>(0), Is.EqualTo(30.0));
        }

        [Test]
        public async Task HistoryReadAnnotationsPropertyRoutesToParentProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            ushort nsIdx = h.Manager.NamespaceIndexes[0];
            var parent = new BaseDataVariableState(null);
            parent.CreateAsPredefinedNode(h.Context);
            parent.NodeId = new NodeId("AnnotParent", nsIdx);
            parent.BrowseName = new QualifiedName("AnnotParent", nsIdx);
            parent.DataType = DataTypeIds.Int32;
            parent.ValueRank = ValueRanks.Scalar;
            parent.AccessLevel = AccessLevels.HistoryRead;
            parent.Historizing = true;

            var annotProp = new PropertyState(parent);
            annotProp.CreateAsPredefinedNode(h.Context);
            annotProp.NodeId = new NodeId("AnnotParent_Annotations", nsIdx);
            annotProp.BrowseName = new QualifiedName(BrowseNames.Annotations, 0);
            annotProp.DataType = DataTypeIds.BaseDataType;
            annotProp.ValueRank = ValueRanks.Scalar;
            annotProp.AccessLevel = AccessLevels.HistoryRead;

            await h.Manager.AddNodeAsync(h.Context, default, parent).ConfigureAwait(false);
            await h.Manager.AddNodeAsync(h.Context, default, annotProp).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(parent.NodeId, provider);
            provider.Register(parent.NodeId);

            DateTime annotTime = BaseTime.AddSeconds(5);
            await provider.InsertAnnotationsAsync(
                h.CreateHistorianOpContext(),
                parent.NodeId,
                [new Annotation { AnnotationTime = annotTime, UserName = "tester", Message = "note1" }],
                CancellationToken.None).ConfigureAwait(false);

            var details = new ReadRawModifiedDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1)
            };
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = annotProp.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext, details, TimestampsToReturn.Source,
                false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0], Is.Not.Null);

            var historyData = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryData;
            Assert.That(historyData, Is.Not.Null);
            Assert.That(historyData!.DataValues, Has.Count.EqualTo(1));

            ExtensionObject annotExt = historyData.DataValues[0].GetValue<ExtensionObject>(default);
            var annotation = ExtensionObject.ToEncodeable(annotExt) as Annotation;
            Assert.That(annotation, Is.Not.Null);
            Assert.That(annotation!.Message, Is.EqualTo("note1"));
        }

        [Test]
        public async Task HistoryReadOnUnownedNodeLeavesDefaultErrorAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            var unknownNodeId = new NodeId("DoesNotExist", h.Manager.NamespaceIndexes[0]);
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = unknownNodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext,
                new ReadRawModifiedDetails { StartTime = BaseTime, EndTime = BaseTime.AddMinutes(1) },
                TimestampsToReturn.Source,
                false, nodesToRead, results, errors).ConfigureAwait(false);

            // GetManagerHandleAsync returns null for unknown nodes — the loop
            // continues without modifying errors[0], so it stays null.
            Assert.That(errors[0], Is.Null);
            Assert.That(results[0], Is.Null);
        }

        [Test]
        public async Task HistoryReadEventsOnNotifierObjectRoutesToProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);
            ushort nsIdx = h.Manager.NamespaceIndexes[0];

            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(h.Context);
            notifier.NodeId = new NodeId("EventNotifier", nsIdx);
            notifier.BrowseName = new QualifiedName("EventNotifier", nsIdx);
            notifier.EventNotifier = EventNotifiers.HistoryRead | EventNotifiers.SubscribeToEvents;

            await h.Manager.AddNodeAsync(h.Context, default, notifier).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(notifier.NodeId, provider);

            var eventId = new ByteString(Encoding.UTF8.GetBytes("evt-1"));
            var record = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                BaseTime.AddSeconds(10),
                new Dictionary<string, Variant>(StringComparer.Ordinal)
                {
                    [BrowseNames.EventId] = new Variant(eventId),
                    [BrowseNames.EventType] = new Variant(ObjectTypeIds.BaseEventType),
                    [BrowseNames.Time] = new Variant((DateTimeUtc)BaseTime.AddSeconds(10)),
                    [BrowseNames.Message] = new Variant(new LocalizedText("test event"))
                });

            await provider.InsertEventsAsync(
                h.CreateHistorianOpContext(),
                notifier.NodeId,
                [record],
                CancellationToken.None).ConfigureAwait(false);

            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message, Attributes.Value);

            var details = new ReadEventDetails
            {
                NumValuesPerNode = 100,
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                Filter = filter
            };
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = notifier.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext, details, TimestampsToReturn.Source,
                false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0], Is.Not.Null);

            var historyEvent = ExtensionObject.ToEncodeable(results[0].HistoryData) as HistoryEvent;
            Assert.That(historyEvent, Is.Not.Null);
            Assert.That(historyEvent!.Events, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task HistoryReadWithInvalidTimestampsToReturnThrowsAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryReadVariable(h, "TsInvalid");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            var details = new ReadRawModifiedDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1)
            };
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = variable.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await h.Manager.HistoryReadAsync(
                    h.OperationContext, details,
                    (TimestampsToReturn)99,
                    false, nodesToRead, results, errors).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadTimestampsToReturnInvalid));
        }

        [Test]
        public async Task HistoryReadRawWithMissingTimestampsAndZeroMaxThrowsAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryReadVariable(h, "NoTs");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            var details = new ReadRawModifiedDetails
            {
                StartTime = DateTime.MinValue,
                EndTime = DateTime.MinValue,
                NumValuesPerNode = 0
            };
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = variable.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await h.Manager.HistoryReadAsync(
                    h.OperationContext, details,
                    TimestampsToReturn.Source,
                    false, nodesToRead, results, errors).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationInvalid));
        }

        [Test]
        public async Task HistoryReadProcessedAggregateMismatchThrowsAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryReadVariable(h, "AggMismatch");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            var details = new ReadProcessedDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                ProcessingInterval = 1000,
                AggregateType = [ObjectIds.AggregateFunction_Average, ObjectIds.AggregateFunction_Count]
            };
            // Only 1 node but 2 aggregate types.
            var nodesToRead = new List<HistoryReadValueId> { new() { NodeId = variable.NodeId } };
            var results = new List<HistoryReadResult> { null! };
            var errors = new List<ServiceResult> { null! };

            ServiceResultException? ex = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await h.Manager.HistoryReadAsync(
                    h.OperationContext, details,
                    TimestampsToReturn.Source,
                    false, nodesToRead, results, errors).ConfigureAwait(false));

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadAggregateListMismatch));
        }

        [Test]
        public async Task HistoryReadProcessedWithEqualStartAndEndTimeReturnsBadInvalidArgumentPerNodeAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState node1 = CreateHistoryReadVariable(h, "EqualTimesA");
            BaseDataVariableState node2 = CreateHistoryReadVariable(h, "EqualTimesB");
            await h.Manager.AddNodeAsync(h.Context, default, node1).ConfigureAwait(false);
            await h.Manager.AddNodeAsync(h.Context, default, node2).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(node1.NodeId, provider);
            h.RegisterProvider(node2.NodeId, provider);
            provider.Register(node1.NodeId);
            provider.Register(node2.NodeId);

            // Part 11 v1.05.07 §6.5.4.2: startTime == endTime is a zero-width time domain that
            // "has no meaningful way to interpret", so the Server shall return Bad_InvalidArgument.
            // This reproduces CTT aggregate_002_01 (multi-node): each per-node operation result
            // must carry Bad_InvalidArgument while the HistoryRead service call itself succeeds.
            var details = new ReadProcessedDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime,
                ProcessingInterval = 1000,
                AggregateType = [ObjectIds.AggregateFunction_Average, ObjectIds.AggregateFunction_Average]
            };

            var nodesToRead = new List<HistoryReadValueId>
            {
                new() { NodeId = node1.NodeId },
                new() { NodeId = node2.NodeId }
            };
            var results = new List<HistoryReadResult> { null!, null! };
            var errors = new List<ServiceResult> { null!, null! };

            await h.Manager.HistoryReadAsync(
                h.OperationContext, details, TimestampsToReturn.Source,
                false, nodesToRead, results, errors).ConfigureAwait(false);

            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(errors[1].StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task HistoryUpdateInsertDataDispatchesToProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryWriteVariable(h, "InsertData");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            DateTime ts = BaseTime.AddSeconds(1);
            var updateDetails = new UpdateDataDetails
            {
                NodeId = variable.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues =
                [
                    new DataValue(new Variant(42.0), StatusCodes.Good, ts, ts)
                ]
            };

            var nodesToUpdate = new List<HistoryUpdateDetails> { updateDetails };
            var results = new List<HistoryUpdateResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryUpdateAsync(
                h.OperationContext, typeof(UpdateDataDetails),
                nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0], Is.Not.Null);
            Assert.That(results[0]!.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(results[0].OperationResults[0]), Is.True);

            // Verify data actually stored.
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                h.CreateHistorianOpContext(),
                new HistorianRawReadRequest
                {
                    NodeId = variable.NodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Value.GetValue<double>(0), Is.EqualTo(42.0));
        }

        [Test]
        public async Task HistoryUpdateDeleteRawDispatchesToProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryWriteVariable(h, "DeleteRaw");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(variable.NodeId, provider);
            provider.Register(variable.NodeId);

            // Seed 3 values.
            for (int i = 0; i < 3; i++)
            {
                DateTime t = BaseTime.AddSeconds(i);
                await provider.InsertAsync(
                    h.CreateHistorianOpContext(),
                    variable.NodeId,
                    [new DataValue(new Variant((double)i), StatusCodes.Good, t, t)],
                    CancellationToken.None).ConfigureAwait(false);
            }

            var deleteDetails = new DeleteRawModifiedDetails
            {
                NodeId = variable.NodeId,
                StartTime = BaseTime.AddSeconds(-1),
                EndTime = BaseTime.AddSeconds(10),
                IsDeleteModified = false
            };
            var nodesToUpdate = new List<HistoryUpdateDetails> { deleteDetails };
            var results = new List<HistoryUpdateResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryUpdateAsync(
                h.OperationContext, typeof(DeleteRawModifiedDetails),
                nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);

            // Verify all deleted.
            HistorianPage<HistoricalDataValue> page = await provider.ReadRawAsync(
                h.CreateHistorianOpContext(),
                new HistorianRawReadRequest
                {
                    NodeId = variable.NodeId,
                    StartTime = BaseTime.AddSeconds(-1),
                    EndTime = BaseTime.AddSeconds(10),
                    IsForward = true
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(0));
        }

        [Test]
        public async Task HistoryUpdateUpdateEventDispatchesToProviderAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);
            ushort nsIdx = h.Manager.NamespaceIndexes[0];

            var notifier = new BaseObjectState(null);
            notifier.CreateAsPredefinedNode(h.Context);
            notifier.NodeId = new NodeId("EventUpdNotifier", nsIdx);
            notifier.BrowseName = new QualifiedName("EventUpdNotifier", nsIdx);
            notifier.EventNotifier = EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;

            await h.Manager.AddNodeAsync(h.Context, default, notifier).ConfigureAwait(false);

            var provider = new InMemoryHistorianProvider();
            h.RegisterProvider(notifier.NodeId, provider);

            var eventId = new ByteString(Encoding.UTF8.GetBytes("upd-evt-1"));
            var filter = new EventFilter();
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventId, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Message, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.Time, Attributes.Value);
            filter.AddSelectClause(ObjectTypeIds.BaseEventType, BrowseNames.EventType, Attributes.Value);

            var eventFieldList = new HistoryEventFieldList
            {
                EventFields =
                [
                    new Variant(eventId),
                    new Variant(new LocalizedText("update event")),
                    new Variant((DateTimeUtc)BaseTime.AddSeconds(20)),
                    new Variant(ObjectTypeIds.BaseEventType)
                ]
            };

            var updateDetails = new UpdateEventDetails
            {
                NodeId = notifier.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                EventData = [eventFieldList],
                Filter = filter
            };

            var nodesToUpdate = new List<HistoryUpdateDetails> { updateDetails };
            var results = new List<HistoryUpdateResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryUpdateAsync(
                h.OperationContext, typeof(UpdateEventDetails),
                nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(errors[0]), Is.True);
            Assert.That(results[0], Is.Not.Null);
            Assert.That(results[0]!.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(results[0].OperationResults[0]), Is.True);

            // Verify stored via provider.
            HistorianPage<HistorianEventRecord> page = await provider.ReadEventsAsync(
                h.CreateHistorianOpContext(),
                new HistorianEventReadRequest
                {
                    NodeId = notifier.NodeId,
                    StartTime = BaseTime,
                    EndTime = BaseTime.AddMinutes(1),
                    IsForward = true,
                    Filter = filter
                },
                default,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].EventId, Is.EqualTo(eventId));
        }

        [Test]
        public async Task HistoryUpdateOnVariableWithoutProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            using Harness h = await CreateHarnessAsync().ConfigureAwait(false);

            BaseDataVariableState variable = CreateHistoryWriteVariable(h, "NoProvider");
            await h.Manager.AddNodeAsync(h.Context, default, variable).ConfigureAwait(false);

            // Intentionally do NOT register a provider.

            DateTime ts = BaseTime.AddSeconds(1);
            var updateDetails = new UpdateDataDetails
            {
                NodeId = variable.NodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues =
                [
                    new DataValue(new Variant(1.0), StatusCodes.Good, ts, ts)
                ]
            };

            var nodesToUpdate = new List<HistoryUpdateDetails> { updateDetails };
            var results = new List<HistoryUpdateResult> { null! };
            var errors = new List<ServiceResult> { null! };

            await h.Manager.HistoryUpdateAsync(
                h.OperationContext, typeof(UpdateDataDetails),
                nodesToUpdate, results, errors).ConfigureAwait(false);

            Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        private static BaseDataVariableState CreateHistoryReadVariable(Harness h, string name)
        {
            ushort nsIdx = h.Manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(h.Context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.HistoryRead;
            variable.Historizing = true;
            return variable;
        }

        private static BaseDataVariableState CreateHistoryWriteVariable(Harness h, string name)
        {
            ushort nsIdx = h.Manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(h.Context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DataType = DataTypeIds.Double;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.HistoryRead | AccessLevels.HistoryWrite;
            variable.Historizing = true;
            return variable;
        }

        private static async ValueTask<Harness> CreateHarnessAsync()
        {
            var mockServer = new Mock<IServerInternal>();
            var mockLogger = new Mock<ILogger>();
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            var mockSession = new Mock<ISession>();
            mockSession.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            mockSession.Setup(s => s.PreferredLocales).Returns([]);

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            var monitoredItemQueueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(monitoredItemQueueFactory);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };

            var manager = new HistoryRoutingTestNodeManager(
                mockServer.Object, configuration, mockLogger.Object, TestNamespaceUri);

            mockMasterNodeManager
                .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    NodeState? nodeState = manager.Find(nodeId);
                    if (nodeState == null)
                    {
                        return new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>((null, null));
                    }

                    var handle = new NodeHandle(nodeId, nodeState);
                    return new ValueTask<(object? handle, IAsyncNodeManager? nodeManager)>((handle, manager));
                });

            var opContext = new OperationContext(
                new RequestHeader(), null!, RequestType.HistoryRead, RequestLifetime.None, mockSession.Object);

            await Task.CompletedTask.ConfigureAwait(false);
            return new Harness(manager, serverSystemContext, opContext, mockServer.Object, monitoredItemQueueFactory);
        }

        private sealed class Harness : IDisposable
        {
            public HistoryRoutingTestNodeManager Manager { get; }
            public ServerSystemContext Context { get; }
            public OperationContext OperationContext { get; }

            private readonly IServerInternal m_server;
            private readonly MonitoredItemQueueFactory m_queueFactory;

            public Harness(
                HistoryRoutingTestNodeManager manager,
                ServerSystemContext context,
                OperationContext operationContext,
                IServerInternal server,
                MonitoredItemQueueFactory queueFactory)
            {
                Manager = manager;
                Context = context;
                OperationContext = operationContext;
                m_server = server;
                m_queueFactory = queueFactory;
            }

            public void RegisterProvider(NodeId nodeId, IHistorianProvider provider)
            {
                Manager.ProviderMap[nodeId] = provider;
            }

            public HistorianOperationContext CreateHistorianOpContext()
            {
                var systemContext = new ServerSystemContext(m_server, OperationContext);
                return new HistorianOperationContext(
                    systemContext, OperationContext, null, HistoryUpdateType.Insert);
            }

            public void Dispose()
            {
                m_queueFactory.Dispose();
                Manager.Dispose();
            }
        }

        private sealed class HistoryRoutingTestNodeManager : AsyncCustomNodeManager
        {
            public NodeIdDictionary<IHistorianProvider> ProviderMap { get; } = [];

            public HistoryRoutingTestNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            protected override IHistorianProvider? GetHistorianProvider(NodeState node)
            {
                if (ProviderMap.TryGetValue(node.NodeId, out IHistorianProvider? provider))
                {
                    return provider;
                }

                return null;
            }
        }
    }
}
