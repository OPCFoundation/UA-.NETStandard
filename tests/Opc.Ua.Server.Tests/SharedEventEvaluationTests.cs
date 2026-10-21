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

// CA2000: the monitored items are disposed by the fixture.
#pragma warning disable CA2000

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The event consumer of a notifier evaluates an event once per group of event monitored
    /// items with an equal filter. Every item must still get exactly what evaluating the
    /// event for the item alone gives.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class SharedEventEvaluationTests
    {
        private const string kMessageKey = "SharedEventEvaluationTests.Message";
        private const string kMessageEnglish = "Pump started";
        private const string kMessageGerman = "Pumpe gestartet";

        private static int s_eventCount;
        private ITelemetryContext m_telemetry = null!;
        private SystemContext m_systemContext = null!;
        private ResourceManager m_resourceManager = null!;
        private MonitoredItemQueueFactory m_queueFactory = null!;
        private IServerInternal m_server = null!;
        private List<IDisposable> m_disposables = null!;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_systemContext = new SystemContext(m_telemetry) { NamespaceUris = new NamespaceTable() };
            m_systemContext.NamespaceUris.Append(Ua.Namespaces.OpcUa);
            var typeTable = new TypeTable(m_systemContext.NamespaceUris);
            typeTable.AddSubtype(ObjectTypeIds.BaseObjectType, default);
            typeTable.AddSubtype(ObjectTypeIds.BaseEventType, ObjectTypeIds.BaseObjectType);
            m_systemContext.TypeTable = typeTable;

            m_resourceManager = new ResourceManager(new ApplicationConfiguration());
            m_resourceManager.Add(kMessageKey, "en-US", kMessageEnglish);
            m_resourceManager.Add(kMessageKey, "de-DE", kMessageGerman);
            m_queueFactory = new MonitoredItemQueueFactory(m_telemetry);
            m_disposables = [];

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(m_telemetry);
            server.Setup(s => s.NamespaceUris).Returns(m_systemContext.NamespaceUris);
            server.Setup(s => s.TypeTree).Returns(typeTable);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            server.Setup(s => s.ResourceManager).Returns(m_resourceManager);
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);
            server.Setup(s => s.Auditing).Returns(false);
            m_server = server.Object;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable disposable in m_disposables)
            {
                disposable.Dispose();
            }
            m_queueFactory.Dispose();
            m_resourceManager.Dispose();
        }

        /// <summary>
        /// Items with different select clauses, different where clauses and different
        /// session locales on one notifier each get the fields an item evaluating the
        /// event alone gets.
        /// </summary>
        [Test]
        public async Task MixedFiltersOnOneNotifierGetWhatPerItemEvaluationGivesAsync(
            [Values(true, false)] bool serverNode,
            [Values(false, true)] bool splitEvents)
        {
            (Func<EventFilter> Filter, string[]? Locales)[] specs =
            [
                (() => CreateFilter(SelectA(), null), null),
                (() => CreateFilter(SelectA(), null), null),
                (() => CreateFilter(SelectB(), null), null),
                (() => CreateFilter(SelectA(), 500), null),
                (() => CreateFilter(SelectA(), 500), null),
                (() => CreateFilter(SelectA(), 950), null),
                (() => CreateFilter(SelectB(), 500), null),
                (() => CreateFilter(SelectA(), null), ["de-DE"]),
                (() => CreateFilter(SelectA(), null), ["de-DE"]),
                (() => CreateFilter(SelectA(), null), ["en-US"]),
                (() => CreateFilter(SelectB(), 500), ["de-DE"])
            ];

            MonitoredNode2 monitoredNode = CreateMonitoredNode(serverNode, splitEvents);
            var shared = new List<MonitoredItem>();
            var alone = new List<MonitoredItem>();
            for (int ii = 0; ii < specs.Length; ii++)
            {
                MonitoredItem item = CreateItem((uint)(ii + 1), specs[ii].Filter(), specs[ii].Locales);
                monitoredNode.Add((IEventMonitoredItem)item);
                shared.Add(item);
                alone.Add(CreateItem((uint)(ii + 1), specs[ii].Filter(), specs[ii].Locales));
            }

            foreach (EventSeverity severity in new[] { EventSeverity.High, EventSeverity.Low, EventSeverity.Max })
            {
                InstanceStateSnapshot snapshot = CreateEvent(severity);
                await monitoredNode.OnReportEventAsync(m_systemContext, monitoredNode.Node, snapshot)
                    .ConfigureAwait(false);
                foreach (MonitoredItem item in alone)
                {
                    item.QueueEvent(snapshot);
                }
            }
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            var published = new List<List<EventFieldList>>();
            for (int ii = 0; ii < specs.Length; ii++)
            {
                List<EventFieldList> expected = Publish(alone[ii]);
                List<EventFieldList> actual = Publish(shared[ii]);
                published.Add(actual);
                Assert.That(actual, Has.Count.EqualTo(expected.Count), $"item {ii}");
                for (int jj = 0; jj < expected.Count; jj++)
                {
                    Assert.That(actual[jj].ClientHandle, Is.EqualTo(shared[ii].ClientHandle), $"item {ii}");
                    Assert.That(actual[jj].Handle, Is.SameAs(expected[jj].Handle), $"item {ii} event {jj}");
                    Assert.That(
                        actual[jj].EventFields.ToArray(),
                        Is.EqualTo(expected[jj].EventFields.ToArray()),
                        $"item {ii} event {jj}");
                }
            }

            // the where clauses and the locales took effect: severities 900, 100, 1000.
            Assert.That(published[0], Has.Count.EqualTo(3));
            Assert.That(published[3], Has.Count.EqualTo(2));
            Assert.That(published[5], Has.Count.EqualTo(1));
            Assert.That(published[6], Has.Count.EqualTo(2));
            Assert.That(Message(published[0][0]), Is.EqualTo(kMessageEnglish));
            Assert.That(Message(published[7][0]), Is.EqualTo(kMessageGerman));
            Assert.That(Message(published[9][0]), Is.EqualTo(kMessageEnglish));
        }

        /// <summary>
        /// Items with equal filters read the select clauses and the operands of the where
        /// clause of an event once, however many items there are.
        /// </summary>
        [Test]
        public async Task ItemsWithEqualFiltersEvaluateTheEventOnceAsync(
            [Values(true, false)] bool serverNode,
            [Values(false, true)] bool splitEvents)
        {
            int oneItem = await CountReadsAsync(serverNode, splitEvents, 1).ConfigureAwait(false);
            int manyItems = await CountReadsAsync(serverNode, splitEvents, 20).ConfigureAwait(false);

            Assert.That(oneItem, Is.GreaterThan(SelectA().Length));
            Assert.That(manyItems, Is.EqualTo(oneItem));
        }

        /// <summary>
        /// An item type that overrides how events are queued sees every event and still
        /// gets the fields of per item evaluation.
        /// </summary>
        [Test]
        public async Task DerivedItemThatOverridesQueueEventSeesEveryEventAsync()
        {
            MonitoredNode2 monitoredNode = CreateMonitoredNode(true);
            var derived = new List<CountingMonitoredItem>();
            for (uint ii = 1; ii <= 3; ii++)
            {
                var item = new CountingMonitoredItem(this, ii, CreateFilter(SelectA(), 500));
                m_disposables.Add(item);
                monitoredNode.Add((IEventMonitoredItem)item);
                derived.Add(item);
            }
            MonitoredItem plain = CreateItem(4, CreateFilter(SelectA(), 500), null);
            monitoredNode.Add((IEventMonitoredItem)plain);
            MonitoredItem alone = CreateItem(5, CreateFilter(SelectA(), 500), null);

            for (int ii = 0; ii < 2; ii++)
            {
                InstanceStateSnapshot snapshot = CreateEvent(EventSeverity.High);
                await monitoredNode.OnReportEventAsync(m_systemContext, monitoredNode.Node, snapshot)
                    .ConfigureAwait(false);
                alone.QueueEvent(snapshot);
            }
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            List<EventFieldList> expected = Publish(alone);
            Assert.That(expected, Has.Count.EqualTo(2));
            foreach (MonitoredItem item in derived.Cast<MonitoredItem>().Append(plain))
            {
                List<EventFieldList> actual = Publish(item);
                Assert.That(actual, Has.Count.EqualTo(2));
                for (int jj = 0; jj < expected.Count; jj++)
                {
                    Assert.That(
                        actual[jj].EventFields.ToArray(),
                        Is.EqualTo(expected[jj].EventFields.ToArray()));
                }
            }
            Assert.That(derived.Select(item => item.Queued), Is.All.EqualTo(2));
        }

        /// <summary>
        /// An item whose session changes its locales gets the new translation from the next
        /// event on, although the items it shared the evaluation with keep theirs.
        /// </summary>
        [Test]
        public async Task SessionLocaleChangeTakesEffectForTheNextEventAsync()
        {
            MonitoredNode2 monitoredNode = CreateMonitoredNode(true);
            string[] locales = ["de-DE"];
            MonitoredItem changing = CreateItemWithLocales(1, CreateFilter(SelectA(), null), () => locales);
            MonitoredItem staying = CreateItem(2, CreateFilter(SelectA(), null), ["de-DE"]);
            monitoredNode.Add((IEventMonitoredItem)changing);
            monitoredNode.Add((IEventMonitoredItem)staying);

            await monitoredNode.OnReportEventAsync(
                m_systemContext, monitoredNode.Node, CreateEvent(EventSeverity.High)).ConfigureAwait(false);
            await monitoredNode.WaitForPendingNotificationsAsync(CancellationToken.None).ConfigureAwait(false);
            locales = ["en-US"];
            await monitoredNode.OnReportEventAsync(
                m_systemContext, monitoredNode.Node, CreateEvent(EventSeverity.High)).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(
                Publish(changing).Select(fields => Message(fields)),
                Is.EqualTo(new[] { kMessageGerman, kMessageEnglish }));
            Assert.That(
                Publish(staying).Select(fields => Message(fields)),
                Is.EqualTo(new[] { kMessageGerman, kMessageGerman }));
        }

        private async Task<int> CountReadsAsync(bool serverNode, bool splitEvents, int itemCount)
        {
            MonitoredNode2 monitoredNode = CreateMonitoredNode(serverNode, splitEvents);
            var items = new List<MonitoredItem>();
            for (int ii = 0; ii < itemCount; ii++)
            {
                MonitoredItem item = CreateItem((uint)(ii + 1), CreateFilter(SelectA(), 500), null);
                monitoredNode.Add((IEventMonitoredItem)item);
                items.Add(item);
            }

            var target = new CountingFilterTarget(CreateEvent(EventSeverity.High));
            await monitoredNode.OnReportEventAsync(m_systemContext, monitoredNode.Node, target)
                .ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            // the node forgets its items when it is disposed, so check the items created here.
            foreach (MonitoredItem item in items)
            {
                Assert.That(Publish(item), Has.Count.EqualTo(1), $"item {item.Id}");
            }
            return target.Reads;
        }

        /// <summary>
        /// The keys of items with equal filters settle on one representative during the first
        /// event, so later events group them by reference instead of comparing the filters.
        /// </summary>
        [Test]
        public async Task EqualFilterKeysCompareByReferenceAfterTheFirstEventAsync()
        {
            MonitoredNode2 monitoredNode = CreateMonitoredNode(true);
            var items = new List<MonitoredItem>();
            for (uint ii = 1; ii <= 4; ii++)
            {
                MonitoredItem item = CreateItem(ii, CreateFilter(SelectA(), 500), null);
                monitoredNode.Add((IEventMonitoredItem)item);
                items.Add(item);
            }

            var representatives = new List<MonitoredItem.SharedEventFilterKey>();
            for (int ii = 0; ii < 3; ii++)
            {
                await monitoredNode.OnReportEventAsync(
                    m_systemContext, monitoredNode.Node, CreateEvent(EventSeverity.High)).ConfigureAwait(false);
                await monitoredNode.WaitForPendingNotificationsAsync(CancellationToken.None).ConfigureAwait(false);

                MonitoredItem.SharedEventFilterKey[] keys =
                    [.. items.Select(item => item.GetSharedEventFilterKey()!)];
                for (int jj = 1; jj < keys.Length; jj++)
                {
                    Assert.That(keys[jj].Filter, Is.Not.SameAs(keys[0].Filter), "equal, not shared filters");
                }
                Assert.That(
                    keys.Select(key => key.Representative).Distinct().Count(),
                    Is.EqualTo(1),
                    $"event {ii}");
                representatives.Add(keys[0].Representative);
            }
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            Assert.That(representatives.Distinct().Count(), Is.EqualTo(1), "stable across events");
            foreach (MonitoredItem item in items)
            {
                Assert.That(Publish(item), Has.Count.EqualTo(3), $"item {item.Id}");
            }
        }

        /// <summary>
        /// Keys are equal only for equal filters and equal locales in the same order.
        /// </summary>
        [Test]
        public void KeysCompareFiltersAndLocalesByValue()
        {
            var key = new MonitoredItem.SharedEventFilterKey(CreateFilter(SelectA(), 500), ["de-DE", "en-US"]);

            var equal = new MonitoredItem.SharedEventFilterKey(CreateFilter(SelectA(), 500), ["de-DE", "en-US"]);
            Assert.That(key, Is.EqualTo(equal));
            Assert.That(key.GetHashCode(), Is.EqualTo(equal.GetHashCode()));

            MonitoredItem.SharedEventFilterKey[] different =
            [
                new(CreateFilter(SelectA(), 500), null),
                new(CreateFilter(SelectA(), 500), []),
                new(CreateFilter(SelectA(), 500), ["de-DE"]),
                new(CreateFilter(SelectA(), 500), ["en-US", "de-DE"]),
                new(CreateFilter(SelectA(), 600), ["de-DE", "en-US"]),
                new(CreateFilter(SelectB(), 500), ["de-DE", "en-US"])
            ];
            foreach (MonitoredItem.SharedEventFilterKey other in different)
            {
                Assert.That(key, Is.Not.EqualTo(other));
                Assert.That(other, Is.Not.EqualTo(key));
            }

            var noLocales = new MonitoredItem.SharedEventFilterKey(CreateFilter(SelectA(), 500), null);
            Assert.That(noLocales, Is.EqualTo(different[0]));
        }

        /// <summary>
        /// An item that no longer matches the key the event was grouped by (its filter or its
        /// session's locales changed) ignores the shared evaluation and evaluates the event
        /// itself.
        /// </summary>
        [Test]
        public void ItemThatNoLongerMatchesTheGroupEvaluatesTheEventItself()
        {
            MonitoredItem other = CreateItem(1, CreateFilter(SelectB(), null), null);
            MonitoredItem item = CreateItem(2, CreateFilter(SelectA(), null), null);
            MonitoredItem alone = CreateItem(3, CreateFilter(SelectA(), null), null);
            MonitoredItem.SharedEventFilterKey otherKey = other.GetSharedEventFilterKey()!;
            var evaluation = new MonitoredItem.SharedEventEvaluation();
            InstanceStateSnapshot snapshot = CreateEvent(EventSeverity.High);

            // the evaluation now holds the fields of the other item's select clauses.
            other.QueueSharedEvent(snapshot, otherKey, evaluation);
            item.QueueSharedEvent(snapshot, otherKey, evaluation);
            alone.QueueEvent(snapshot);

            List<EventFieldList> expected = Publish(alone);
            List<EventFieldList> actual = Publish(item);
            Assert.That(actual, Has.Count.EqualTo(1));
            Assert.That(actual[0].EventFields.ToArray(), Is.EqualTo(expected[0].EventFields.ToArray()));
            Assert.That(actual[0].EventFields.Count, Is.EqualTo(SelectA().Length));
            Assert.That(Publish(other)[0].EventFields.Count, Is.EqualTo(SelectB().Length));
        }

        /// <summary>
        /// Looking up keys in a dictionary in any order makes the looked-up keys adopt the
        /// representative of the key the dictionary holds.
        /// </summary>
        [Test]
        public void LookedUpKeysAdoptTheRepresentativeOfTheHeldKey()
        {
            MonitoredItem.SharedEventFilterKey[] keys =
            [
                new(CreateFilter(SelectA(), 500), null),
                new(CreateFilter(SelectA(), 500), null),
                new(CreateFilter(SelectA(), 500), null)
            ];

            foreach (int[] order in new[] { new[] { 0, 1, 2 }, new[] { 2, 0, 1 }, new[] { 1, 2, 0 } })
            {
                var groups = new Dictionary<MonitoredItem.SharedEventFilterKey, int>();
                foreach (int index in order)
                {
                    if (!groups.TryGetValue(keys[index], out int count))
                    {
                        count = 0;
                    }
                    groups[keys[index]] = count + 1;
                }

                Assert.That(groups, Has.Count.EqualTo(1));
                Assert.That(groups.Values.Single(), Is.EqualTo(3));
                Assert.That(keys.Select(key => key.Representative).Distinct().Count(), Is.EqualTo(1));
            }
            Assert.That(keys[0].Representative, Is.SameAs(keys[0]), "the first held key stays its own");
        }

        private static string? Message(EventFieldList fields)
        {
            // Message is the third select clause of SelectA and SelectB.
            return fields.EventFields[2].TryGetValue(out LocalizedText text) ? text.Text : null;
        }

        private static List<EventFieldList> Publish(MonitoredItem item)
        {
            var notifications = new Queue<EventFieldList>();
            _ = item.Publish(new OperationContext(item), notifications, 100);
            return [.. notifications];
        }

        private static List<EventFieldList> Publish(MonitoredItem item, DiagnosticsMasks diagnosticsMask)
        {
            var notifications = new Queue<EventFieldList>();
            using var context = new OperationContext(
                new RequestHeader { ReturnDiagnostics = (uint)diagnosticsMask },
                null,
                RequestType.Publish,
                RequestLifetime.None);
            _ = item.Publish(context, notifications, 100);
            return [.. notifications];
        }

        /// <summary>
        /// Publishing rewrites a StatusResult field with the diagnostics mask and string table
        /// of the publishing session, so items of a group must not share those fields: the
        /// notification of one item must not change when another item publishes.
        /// </summary>
        [Test]
        public async Task StatusResultFieldsStayPerItemWhenItemsShareTheEvaluationAsync()
        {
            SimpleAttributeOperand[] selectClauses = [.. SelectA(), Field("Result")];
            MonitoredNode2 monitoredNode = CreateMonitoredNode(true);
            MonitoredItem first = CreateItem(1, CreateFilter(selectClauses, null), null);
            MonitoredItem second = CreateItem(2, CreateFilter([.. SelectA(), Field("Result")], null), null);
            monitoredNode.Add((IEventMonitoredItem)first);
            monitoredNode.Add((IEventMonitoredItem)second);

            BaseEventState e = CreateEventState(EventSeverity.High);
            var result = new PropertyState(e)
            {
                NodeId = new NodeId("Result", 1),
                BrowseName = QualifiedName.From("Result"),
                DisplayName = new LocalizedText("Result"),
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = DataTypeIds.StatusResult,
                ValueRank = ValueRanks.Scalar,
                WrappedValue = Variant.FromStructure(new StatusResult(new ServiceResult(
                    StatusCodes.BadUserAccessDenied,
                    new LocalizedText("en-US", "The pump refused the command."))))
            };
            e.AddChild(result);

            // a NodeState is cloned by the notifier and read through the event node.
            await monitoredNode.OnReportEventAsync(m_systemContext, monitoredNode.Node, e).ConfigureAwait(false);
            await monitoredNode.DisposeAsync().ConfigureAwait(false);

            StatusResult firstResult = ResultOf(Publish(first, DiagnosticsMasks.All).Single(), selectClauses.Length - 1);
            DiagnosticInfo? firstDiagnostics = firstResult.DiagnosticInfo;
            Assert.That(firstDiagnostics, Is.Not.Null);

            StatusResult secondResult = ResultOf(Publish(second, DiagnosticsMasks.None).Single(), selectClauses.Length - 1);
            Assert.That(secondResult, Is.Not.SameAs(firstResult));
            Assert.That(firstResult.DiagnosticInfo, Is.SameAs(firstDiagnostics), "the first notification is unchanged");
        }

        private static StatusResult ResultOf(EventFieldList fields, int index)
        {
            StatusResult result;
            Assert.That(fields.EventFields[index].TryGetStructure(out result!), Is.True);
            return result;
        }

        private MonitoredNode2 CreateMonitoredNode(bool serverNode, bool splitEvents = false)
        {
            var nodeManager = new Mock<IAsyncNodeManager>();
            nodeManager
                .Setup(m => m.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<IFilterTarget>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            var notifier = new BaseObjectState(null)
            {
                NodeId = serverNode ? ObjectIds.Server : new NodeId("Notifier", 1),
                BrowseName = new QualifiedName("Notifier", 1),
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            var monitoredNode = new MonitoredNode2(nodeManager.Object, m_server, notifier);
            if (splitEvents)
            {
                // queue the items of every event in ranges of one item on several threads.
                monitoredNode.ParallelEventDeliveryBudget = -1;
                monitoredNode.MinItemsPerEventDeliveryRange = 1;
            }
            m_disposables.Add(monitoredNode);
            return monitoredNode;
        }

        private MonitoredItem CreateItem(uint id, EventFilter filter, string[]? locales)
        {
            return CreateItemWithLocales(id, filter, locales == null ? null : () => locales);
        }

        private MonitoredItem CreateItemWithLocales(uint id, EventFilter filter, Func<string[]>? locales)
        {
            var item = new MonitoredItem(
                m_server,
                new Mock<IAsyncNodeManager>().Object,
                null!,
                1,
                id,
                EventItemToMonitor(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                id + 100,
                filter,
                filter,
                null,
                1000.0,
                100,
                true,
                1000);
            m_disposables.Add(item);
            AttachSession(item, locales);
            return item;
        }

        private static void AttachSession(MonitoredItem item, Func<string[]>? locales)
        {
            if (locales == null)
            {
                return;
            }

            var session = new Mock<ISession>();
            session.Setup(s => s.PreferredLocales).Returns(locales);
            var subscription = new Mock<ISubscription>();
            subscription.Setup(s => s.Session).Returns(session.Object);
            item.SubscriptionCallback = subscription.Object;
        }

        private static ReadValueId EventItemToMonitor()
        {
            return new ReadValueId
            {
                NodeId = ObjectIds.Server,
                AttributeId = Attributes.EventNotifier
            };
        }

        private InstanceStateSnapshot CreateEvent(EventSeverity severity)
        {
            var snapshot = new InstanceStateSnapshot();
            snapshot.Initialize(m_systemContext, CreateEventState(severity));
            return snapshot;
        }

        private BaseEventState CreateEventState(EventSeverity severity)
        {
            var e = new BaseEventState(null);
            e.Create(
                m_systemContext,
                new NodeId((uint)Interlocked.Increment(ref s_eventCount), 1),
                new QualifiedName("Event", 1),
                new LocalizedText(string.Empty, "Event"),
                true);
            e.EventId!.Value = Uuid.NewUuid().ToByteString();
            e.EventType!.Value = ObjectTypeIds.BaseEventType;
            e.Time!.Value = DateTimeUtc.Now;
            e.Severity!.Value = (ushort)severity;
            e.SourceName!.Value = "Pump";
            e.Message!.Value = new LocalizedText(new TranslationInfo(kMessageKey, "en-US", kMessageEnglish));
            return e;
        }

        private EventFilter CreateFilter(SimpleAttributeOperand[] selectClauses, ushort? minimumSeverity)
        {
            var whereClause = new ContentFilter();
            if (minimumSeverity != null)
            {
                whereClause.Push(
                    FilterOperator.GreaterThanOrEqual,
                    Variant.FromStructure(Field(BrowseNames.Severity)),
                    Variant.FromStructure(new LiteralOperand { Value = new Variant(minimumSeverity.Value) }));
            }

            var filter = new EventFilter
            {
                SelectClauses = selectClauses,
                WhereClause = whereClause
            };
            _ = filter.Validate(new FilterContext(
                m_systemContext.NamespaceUris,
                m_systemContext.TypeTable,
                m_telemetry));
            return filter;
        }

        private static SimpleAttributeOperand[] SelectA()
        {
            return
            [
                Field(BrowseNames.EventId),
                Field(BrowseNames.EventType),
                Field(BrowseNames.Message),
                Field(BrowseNames.Severity),
                Field(BrowseNames.SourceName)
            ];
        }

        private static SimpleAttributeOperand[] SelectB()
        {
            return
            [
                Field(BrowseNames.Time),
                Field(BrowseNames.Severity),
                Field(BrowseNames.Message)
            ];
        }

        private static SimpleAttributeOperand Field(string browseName)
        {
            return new SimpleAttributeOperand
            {
                AttributeId = Attributes.Value,
                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                BrowsePath = [QualifiedName.From(browseName)]
            };
        }

        /// <summary>
        /// Counts the attribute reads of an event.
        /// </summary>
        private sealed class CountingFilterTarget : IFilterTarget
        {
            private readonly IFilterTarget m_target;
            private int m_reads;

            public CountingFilterTarget(IFilterTarget target)
            {
                m_target = target;
            }

            public int Reads => Volatile.Read(ref m_reads);

            public bool IsTypeOf(IFilterContext context, NodeId typeDefinitionId)
            {
                return m_target.IsTypeOf(context, typeDefinitionId);
            }

            public Variant GetAttributeValue(
                IFilterContext context,
                NodeId typeDefinitionId,
                ArrayOf<QualifiedName> relativePath,
                uint attributeId,
                NumericRange indexRange)
            {
                Interlocked.Increment(ref m_reads);
                return m_target.GetAttributeValue(context, typeDefinitionId, relativePath, attributeId, indexRange);
            }
        }

        /// <summary>
        /// An item that overrides how events are queued and hands them on to the base class.
        /// </summary>
        private sealed class CountingMonitoredItem : MonitoredItem
        {
            private int m_queued;

            public CountingMonitoredItem(SharedEventEvaluationTests fixture, uint id, EventFilter filter)
                : base(
                    fixture.m_server,
                    new Mock<IAsyncNodeManager>().Object,
                    null!,
                    1,
                    id,
                    EventItemToMonitor(),
                    DiagnosticsMasks.All,
                    TimestampsToReturn.Server,
                    MonitoringMode.Reporting,
                    id + 100,
                    filter,
                    filter,
                    null,
                    1000.0,
                    100,
                    true,
                    1000)
            {
            }

            public int Queued => Volatile.Read(ref m_queued);

            public override void QueueEvent(IFilterTarget instance, bool bypassFilter)
            {
                Interlocked.Increment(ref m_queued);
                base.QueueEvent(instance, bypassFilter);
            }
        }
    }
}
