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

// CA2000: test code; disposables are ownership-transferred to test fixtures or are short-lived.
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
    /// Gap-coverage tests for <see cref="HistorianDispatcher"/> branches not
    /// exercised by <see cref="HistorianDispatcherTests"/> or
    /// <see cref="HistorianDispatcherBranchTests"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class HistorianDispatcherGapsTests
    {
        private static readonly DateTime BaseTime = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        // ─── IsAnnotationsProperty ──────────────────────────────────────────

        [Test]
        public void IsAnnotationsPropertyReturnsFalseForNull()
        {
            Assert.That(HistorianDispatcher.IsAnnotationsProperty(null), Is.False);
        }

        [Test]
        public void IsAnnotationsPropertyReturnsFalseForNonPropertyState()
        {
            var obj = new BaseObjectState(null) { NodeId = new NodeId("obj", 1) };
            Assert.That(HistorianDispatcher.IsAnnotationsProperty(obj), Is.False);
        }

        [Test]
        public void IsAnnotationsPropertyReturnsFalseForWrongBrowseName()
        {
            var parent = new BaseDataVariableState(null) { NodeId = new NodeId("p", 1) };
            var prop = new PropertyState(parent)
            {
                NodeId = new NodeId("wrongName", 1),
                BrowseName = new QualifiedName("NotAnnotations", 0)
            };
            parent.AddChild(prop);

            Assert.That(HistorianDispatcher.IsAnnotationsProperty(prop), Is.False);
        }

        [Test]
        public void IsAnnotationsPropertyReturnsFalseWhenBrowseNamespaceIsNonZero()
        {
            var parent = new BaseDataVariableState(null) { NodeId = new NodeId("p2", 1) };
            var prop = new PropertyState(parent)
            {
                NodeId = new NodeId("annot-ns", 1),
                BrowseName = new QualifiedName(BrowseNames.Annotations, 1)  // ns=1, not 0
            };
            parent.AddChild(prop);

            Assert.That(HistorianDispatcher.IsAnnotationsProperty(prop), Is.False);
        }

        [Test]
        public void IsAnnotationsPropertyReturnsFalseWhenParentIsNotBaseVariableState()
        {
            var objParent = new BaseObjectState(null) { NodeId = new NodeId("obj2", 1) };
            var prop = new PropertyState(objParent)
            {
                NodeId = new NodeId("annot-obj", 1),
                BrowseName = new QualifiedName(BrowseNames.Annotations, 0)
            };
            objParent.AddChild(prop);

            Assert.That(HistorianDispatcher.IsAnnotationsProperty(prop), Is.False);
        }

        [Test]
        public void IsAnnotationsPropertyReturnsTrueForWellFormedAnnotationsProperty()
        {
            var parent = new BaseDataVariableState(null) { NodeId = new NodeId("var-p", 1) };
            var prop = new PropertyState(parent)
            {
                NodeId = new NodeId("var-p.Annotations", 1),
                BrowseName = new QualifiedName(BrowseNames.Annotations, 0)
            };
            parent.AddChild(prop);

            Assert.That(HistorianDispatcher.IsAnnotationsProperty(prop), Is.True);
        }

        // ─── GetAnnotationsParent ────────────────────────────────────────────

        [Test]
        public void GetAnnotationsParentReturnsNullForNull()
        {
            Assert.That(HistorianDispatcher.GetAnnotationsParent(null), Is.Null);
        }

        [Test]
        public void GetAnnotationsParentReturnsVariableParentForAnnotationsProperty()
        {
            var parent = new BaseDataVariableState(null) { NodeId = new NodeId("annot-parent", 1) };
            var prop = new PropertyState(parent)
            {
                NodeId = new NodeId("annot-parent.Annotations", 1),
                BrowseName = new QualifiedName(BrowseNames.Annotations, 0)
            };
            parent.AddChild(prop);

            BaseVariableState? result = HistorianDispatcher.GetAnnotationsParent(prop);

            Assert.That(result, Is.SameAs(parent));
        }

        [Test]
        public void GetAnnotationsParentReturnsNullWhenParentIsNotVariable()
        {
            var objParent = new BaseObjectState(null) { NodeId = new NodeId("obj3", 1) };
            var prop = new PropertyState(objParent)
            {
                NodeId = new NodeId("obj3.p", 1),
                BrowseName = new QualifiedName(BrowseNames.Annotations, 0)
            };
            objParent.AddChild(prop);

            BaseVariableState? result = HistorianDispatcher.GetAnnotationsParent(prop);

            Assert.That(result, Is.Null);
        }

        // ─── DispatchUpdateDataAsync ─────────────────────────────────────────

        [Test]
        public async Task DispatchUpdateDataAsyncThrowsWhenSystemContextIsNullAsync()
        {
            var provider = new InMemoryHistorianProvider();
            BaseDataVariableState node = CreateVariable(new NodeId("n", 1));
            var details = new UpdateDataDetails { PerformInsertReplace = PerformUpdateType.Insert };
            var result = new HistoryUpdateResult();

            await AssertThrowsArgNullAsync(() =>
                HistorianDispatcher.DispatchUpdateDataAsync(
                    null!, provider, node, details, result, CancellationToken.None).AsTask()).ConfigureAwait(false);
        }

        [Test]
        public async Task DispatchUpdateDataAsyncWithNonDataProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId("upd-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = []
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, mockProvider.Object, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchUpdateDataAsyncInsertStoresValuesAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"upd-insert-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);
            BaseDataVariableState node = CreateVariable(nodeId);

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(1.0), StatusCodes.Good,
                        sourceTimestamp: BaseTime.AddSeconds(1),
                        serverTimestamp: BaseTime.AddSeconds(1))
                }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(result.OperationResults[0]), Is.True);
        }

        [Test]
        public async Task DispatchUpdateDataAsyncReplaceUpdatesExistingValueAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"upd-replace-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);
            BaseDataVariableState node = CreateVariable(nodeId);
            HistorianOperationContext ctx = CreateContext(h.SystemContext);
            DateTime ts = BaseTime.AddSeconds(5);
            await h.Provider.InsertAsync(ctx, nodeId,
                [new DataValue(new Variant(10.0), StatusCodes.Good, ts, ts)],
                CancellationToken.None).ConfigureAwait(false);

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Replace,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(99.0), StatusCodes.Good,
                        sourceTimestamp: ts, serverTimestamp: ts)
                }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
        }

        [Test]
        public async Task DispatchUpdateDataAsyncUpdateStoresValuesAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"upd-update-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);
            BaseDataVariableState node = CreateVariable(nodeId);

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Update,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(42.0), StatusCodes.Good,
                        sourceTimestamp: BaseTime.AddSeconds(7),
                        serverTimestamp: BaseTime.AddSeconds(7))
                }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
        }

        [Test]
        public async Task DispatchUpdateDataAsyncWithInvalidPerformInsertReturnsBadArgumentAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"upd-invalid-{Guid.NewGuid():N}", 1);
            h.Provider.Register(nodeId);
            BaseDataVariableState node = CreateVariable(nodeId);

            var details = new UpdateDataDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = (PerformUpdateType)999,
                UpdateValues = new DataValue[]
                {
                    new(new Variant(1.0), StatusCodes.Good, BaseTime, BaseTime)
                }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateDataAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True,
                "Dispatcher returns Good; per-entry status in OperationResults carries the error.");
            Assert.That(result.OperationResults, Has.Count.EqualTo(1));
            Assert.That(result.OperationResults[0], Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // ─── DispatchEventReadAsync ──────────────────────────────────────────

        [Test]
        public async Task DispatchEventReadAsyncWithNonEventProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId("evt-rd-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new ReadEventDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                Filter = new EventFilter()
            };
            var nodeToRead = new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = ByteString.Empty };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, mockProvider.Object, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchEventReadAsyncReturnsStoredEventsAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"evt-read-{Guid.NewGuid():N}", 1);
            HistorianOperationContext ctx = CreateContext(h.SystemContext);

            var evtId = (ByteString)new byte[] { 0x01, 0x02 };
            var events = new List<HistorianEventRecord>
            {
                new(evtId, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(1),
                    new Dictionary<string, Variant> { ["Message"] = new Variant("hello") })
            };
            await h.Provider.InsertEventsAsync(ctx, nodeId, events, CancellationToken.None).ConfigureAwait(false);

            BaseDataVariableState node = CreateVariable(nodeId);
            var details = new ReadEventDetails
            {
                StartTime = BaseTime,
                EndTime = BaseTime.AddMinutes(1),
                Filter = new EventFilter()
            };
            var nodeToRead = new HistoryReadValueId { NodeId = nodeId, ContinuationPoint = ByteString.Empty };
            var result = new HistoryReadResult();

            ServiceResult error = await HistorianDispatcher.DispatchEventReadAsync(
                h.SystemContext, h.Provider, node, nodeToRead, details,
                TimestampsToReturn.Source, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.HistoryData.TryGetValue(out HistoryEvent? he), Is.True);
            Assert.That(he!.Events, Has.Count.EqualTo(1));
        }

        // ─── DispatchUpdateEventAsync ────────────────────────────────────────

        [Test]
        public async Task DispatchUpdateEventAsyncWithNonEventProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId("evt-upd-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new UpdateEventDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = new EventFilter(),
                EventData = []
            };
            var result = new HistoryUpdateResult();

            ServiceResult error = await HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, mockProvider.Object, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchUpdateEventAsyncInsertAndDeleteRoundTripAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"evt-upd-{Guid.NewGuid():N}", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            HistorianOperationContext ctx = CreateContext(h.SystemContext);

            // Build an event filter with the standard select clauses for round-trip.
            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = new QualifiedName[] { new(BrowseNames.EventId) },
                        AttributeId = Attributes.Value
                    }
                }
            };

            var evtId = (ByteString)new byte[] { 0xAB, 0xCD };
            var incoming = new HistoryEventFieldList
            {
                EventFields = new Variant[] { new(evtId) }
            };
            var insertDetails = new UpdateEventDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = PerformUpdateType.Insert,
                Filter = filter,
                EventData = new HistoryEventFieldList[] { incoming }
            };

            var insertResult = new HistoryUpdateResult();
            ServiceResult insertError = await HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, h.Provider, node, insertDetails, insertResult, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(insertError), Is.True);
            Assert.That(insertResult.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(insertResult.OperationResults[0]), Is.True);
        }

        [Test]
        public async Task DispatchUpdateEventAsyncInvalidPerformUpdateReturnsBadArgumentPerEntryAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"evt-inv-{Guid.NewGuid():N}", 1);
            BaseDataVariableState node = CreateVariable(nodeId);

            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = new QualifiedName[] { new(BrowseNames.EventId) },
                        AttributeId = Attributes.Value
                    }
                }
            };

            var details = new UpdateEventDetails
            {
                NodeId = nodeId,
                PerformInsertReplace = (PerformUpdateType)999,
                Filter = filter,
                EventData = new HistoryEventFieldList[]
                {
                    new() { EventFields = new Variant[] { new((ByteString)new byte[]{0x01}) } }
                }
            };

            var result = new HistoryUpdateResult();
            ServiceResult error = await HistorianDispatcher.DispatchUpdateEventAsync(
                h.SystemContext, h.Provider, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.OperationResults, Has.Count.EqualTo(1));
            Assert.That(result.OperationResults[0], Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // ─── DispatchDeleteEventsAsync ───────────────────────────────────────

        [Test]
        public async Task DispatchDeleteEventsAsyncWithNonEventProviderReturnsBadHistoryOperationUnsupportedAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId("evt-del-nondp", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            var mockProvider = new Mock<IHistorianProvider>();

            var details = new DeleteEventDetails
            {
                NodeId = nodeId,
                EventIds = new ByteString[] { (ByteString)new byte[] { 0x01 } }
            };
            var result = new HistoryUpdateResult();

            ServiceResult error = await HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, mockProvider.Object, node, details, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadHistoryOperationUnsupported));
        }

        [Test]
        public async Task DispatchDeleteEventsAsyncRemovesInsertedEventAsync()
        {
            Fixture h = CreateFixture();
            var nodeId = new NodeId($"evt-del2-{Guid.NewGuid():N}", 1);
            BaseDataVariableState node = CreateVariable(nodeId);
            HistorianOperationContext ctx = CreateContext(h.SystemContext);

            var evtId = (ByteString)"ޭ"u8.ToArray();
            await h.Provider.InsertEventsAsync(ctx, nodeId,
            [
                new HistorianEventRecord(
                    evtId, ObjectTypeIds.BaseEventType,
                    (DateTimeUtc)BaseTime.AddSeconds(1),
                    new Dictionary<string, Variant>())
            ], CancellationToken.None).ConfigureAwait(false);

            var deleteDetails = new DeleteEventDetails
            {
                NodeId = nodeId,
                EventIds = new ByteString[] { evtId }
            };
            var result = new HistoryUpdateResult();

            ServiceResult error = await HistorianDispatcher.DispatchDeleteEventsAsync(
                h.SystemContext, h.Provider, node, deleteDetails, result, CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(error), Is.True);
            Assert.That(result.OperationResults, Has.Count.EqualTo(1));
            Assert.That(StatusCode.IsGood(result.OperationResults[0]), Is.True);
        }

        // ─── ProjectEventFields ──────────────────────────────────────────────

        [Test]
        public void ProjectEventFieldsThrowsWhenRecordIsNull()
        {
            var filter = new EventFilter();

            Assert.That(
                () => HistorianDispatcher.ProjectEventFields(null!, filter),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ProjectEventFieldsThrowsWhenFilterIsNull()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>());

            Assert.That(
                () => HistorianDispatcher.ProjectEventFields(record, null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ProjectEventFieldsReturnsSingleFieldForSingleSelectClause()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>
                {
                    ["Severity"] = new Variant((ushort)500)
                });

            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = new QualifiedName[] { new("Severity") },
                        AttributeId = Attributes.Value
                    }
                }
            };

            HistoryEventFieldList fields = HistorianDispatcher.ProjectEventFields(record, filter);

            Assert.That(fields.EventFields, Has.Count.EqualTo(1));
            Assert.That(fields.EventFields[0].TryGetValue(out ushort severity), Is.True);
            Assert.That(severity, Is.EqualTo(500));
        }

        [Test]
        public void ProjectEventFieldsResolvesNodeIdAttributeFromEmptyBrowsePath()
        {
            NodeId eventType = ObjectTypeIds.AuditEventType;
            var record = new HistorianEventRecord(
                ByteString.Empty, eventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>());

            // Select clause with empty BrowsePath + NodeId attribute asks for the EventType.
            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = [],
                        AttributeId = Attributes.NodeId
                    }
                }
            };

            HistoryEventFieldList fields = HistorianDispatcher.ProjectEventFields(record, filter);

            Assert.That(fields.EventFields, Has.Count.EqualTo(1));
            Assert.That(fields.EventFields[0].TryGetValue(out NodeId resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(eventType));
        }

        [Test]
        public void ProjectEventFieldsReturnsDefaultForUnknownBrowseName()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>());

            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = new QualifiedName[] { new("DoesNotExist") },
                        AttributeId = Attributes.Value
                    }
                }
            };

            HistoryEventFieldList fields = HistorianDispatcher.ProjectEventFields(record, filter);

            Assert.That(fields.EventFields, Has.Count.EqualTo(1));
            Assert.That(fields.EventFields[0], Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ProjectEventFieldsResolvesMultiSegmentBrowsePath()
        {
            var record = new HistorianEventRecord(
                ByteString.Empty, ObjectTypeIds.BaseEventType,
                (DateTimeUtc)BaseTime,
                new Dictionary<string, Variant>
                {
                    ["Parent/Child"] = new Variant("nested-value")
                });

            var filter = new EventFilter
            {
                SelectClauses = new SimpleAttributeOperand[]
                {
                    new()
                    {
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = new QualifiedName[] { new("Parent"), new("Child") },
                        AttributeId = Attributes.Value
                    }
                }
            };

            HistoryEventFieldList fields = HistorianDispatcher.ProjectEventFields(record, filter);

            Assert.That(fields.EventFields, Has.Count.EqualTo(1));
            Assert.That(fields.EventFields[0].TryGetValue(out string val), Is.True);
            Assert.That(val, Is.EqualTo("nested-value"));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static Fixture CreateFixture()
        {
            return new();
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

        private static HistorianOperationContext CreateContext(ServerSystemContext systemContext)
        {
            return new HistorianOperationContext(
                systemContext,
                systemContext.OperationContext!,
                null,
                HistoryUpdateType.Insert);
        }

        private static Task AssertThrowsArgNullAsync(Func<Task> action)
        {
            return Assert.ThatAsync(action, Throws.TypeOf<ArgumentNullException>());
        }

        private sealed class Fixture
        {
            public InMemoryHistorianProvider Provider { get; }
            public ServerSystemContext SystemContext { get; }

            public Fixture()
            {
                Provider = new InMemoryHistorianProvider();

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mockSession = new Mock<ISession>();

                var continuationStore = new Dictionary<Guid, object>();
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
                        return continuationStore.TryGetValue(id, out object? s) ? s : null;
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
        }
    }
}
