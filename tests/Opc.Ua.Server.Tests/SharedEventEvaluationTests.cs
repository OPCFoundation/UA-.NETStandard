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
        /// Event consumers carry admission into virtual queues without installing an ambient source.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task EventDeliveryKeepsItsAdmissionWithoutAmbientSourceAsync(bool serverNode, bool rebind)
        {
            MonitoredNode2 monitored = CreateMonitoredNode(serverNode);
            var item = new CountingMonitoredItem(
                this, 1, CreateFilter(SelectA(), null), monitored.NodeManager, monitored.Node.NodeId);
            m_disposables.Add(item);
            using MasterNodeManager master = CreateMaster([monitored.NodeManager]);
            bool ambientDuringQueue = false;
            item.BeforeQueue = _ =>
            {
                ambientDuringQueue = NodeManager.DeterministicServerMock.HasImplicitEventSource(
                    m_server, monitored.NodeManager);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(monitored.NodeManager);
                generation.Gate.Retire();
                if (rebind)
                {
                    NodeManagerGeneration replacement = NodeManagerGenerationRegistry.CreateReplacement(
                        generation, new Mock<IAsyncNodeManager>().Object);
                    ((IDetachableMonitoredItem)item).Rebind(replacement.NodeManager, new object());
                }
            };
            monitored.Add((IEventMonitoredItem)item);

            await monitored.OnReportEventAsync(m_systemContext, monitored.Node, CreateEvent(EventSeverity.High))
                .ConfigureAwait(false);
            await monitored.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ambientDuringQueue, Is.False);
                Assert.That(item.Queued, Is.EqualTo(1));
                Assert.That(Publish(item), Has.Count.EqualTo(rebind ? 0 : 1));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(monitored.NodeManager).Gate.ActiveCount, Is.Zero);
            });
        }

        /// <summary>
        /// A root report admitted before retirement reaches the actual Server notifier consumer afterward.
        /// </summary>
        [Test]
        public async Task RootEventAdmittedBeforeRetirementReachesServerConsumerAsync()
        {
            await using var source = new TestableAsyncCustomNodeManager(
                m_server,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                false,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                "urn:tests:root-event-source");
            MonitoredNode2 monitored = CreateMonitoredNode(true);
            using MasterNodeManager master = CreateMaster([source, monitored.NodeManager]);
            var item = new CountingMonitoredItem(this, 1, CreateFilter(SelectA(), null), monitored.NodeManager);
            m_disposables.Add(item);
            monitored.Add((IEventMonitoredItem)item);
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("root-event-source", source.NamespaceIndex),
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            await source.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source);
            var authorizing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAuthorization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock.Get(monitored.NodeManager).Setup(manager => manager.ValidateEventRolePermissionsAsync(
                    It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (IEventMonitoredItem _, IFilterTarget _, CancellationToken cancellationToken) =>
                {
                    authorizing.TrySetResult(true);
                    await releaseAuthorization.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return ServiceResult.Good;
                });
            bool ambient = false;
            Mock.Get(m_server).Setup(server => server.ReportEventAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns(async (ISystemContext context, IFilterTarget target, CancellationToken cancellationToken) =>
                {
                    ambient = NodeManager.DeterministicServerMock.HasImplicitEventSource(m_server, source);
                    generation.Gate.Retire();
                    await monitored.OnReportEventAsync(context, monitored.Node, target, cancellationToken)
                        .ConfigureAwait(false);
                });

            try
            {
                await root.ReportEventAsync(source.SystemContext, CreateEvent(EventSeverity.High)).ConfigureAwait(false);
                await authorizing.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1),
                    "After the root report returns, its queued Server delivery must still own the original source.");
                Assert.That(generation.Gate.WaitForDrainAsync().IsCompleted, Is.False);
            }
            finally
            {
                releaseAuthorization.TrySetResult(true);
                await monitored.DisposeAsync().ConfigureAwait(false);
            }

            Assert.Multiple(() =>
            {
                Assert.That(ambient, Is.False);
                Assert.That(item.Queued, Is.EqualTo(1));
                Assert.That(Publish(item), Has.Count.EqualTo(1));
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
            });
        }

        /// <summary>
        /// Refresh callbacks can finish admitted events after retirement but cannot reuse an expired target.
        /// </summary>
        [Test]
        public async Task RefreshTargetsCarryRevocableSourceAdmissionsWithoutAmbientStateAsync(
            [Values] bool retire,
            [Values(0, 1, 2)] int completion)
        {
            var source = new Mock<IAsyncNodeManager>();
            MonitoredNode2 monitored = CreateMonitoredNode(true);
            var item = new CountingMonitoredItem(
                this, 1, CreateFilter(SelectA(), 500), monitored.NodeManager);
            m_disposables.Add(item);
            using MasterNodeManager master = CreateMaster([source.Object, monitored.NodeManager]);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source.Object);
            IEventMonitoredItem? escaped = null;
            bool ambient = false;
            source.Setup(manager => manager.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(), It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (OperationContext _, IList<IEventMonitoredItem> items, CancellationToken _) =>
                {
                    escaped = items.Single(target => target.Id == item.Id);
                    ambient = NodeManager.DeterministicServerMock.HasImplicitEventSource(m_server, source.Object);
                    if (retire)
                    {
                        generation.Gate.Retire();
                    }
                    escaped.QueueEvent(CreateEvent(EventSeverity.High));
                    await Task.Yield();
                    escaped.QueueEvent(CreateEvent(EventSeverity.Low), bypassFilter: false);
                    escaped.QueueEvent(CreateEvent(EventSeverity.Low), bypassFilter: true);
                    if (completion == 1)
                    {
                        throw new InvalidOperationException("refresh callback failed");
                    }
                    if (completion == 2)
                    {
                        throw new OperationCanceledException("refresh callback canceled");
                    }
                    return ServiceResult.Good;
                });
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            await master.ConditionRefreshAsync(context, [item]).ConfigureAwait(false);
            escaped!.QueueEvent(CreateEvent(EventSeverity.High));

            Assert.Multiple(() =>
            {
                Assert.That(ambient, Is.False);
                Assert.That(Publish(item), Has.Count.EqualTo(2));
                Assert.That(item.OneArgumentCalls, Is.EqualTo(1));
                Assert.That(item.Queued, Is.EqualTo(3));
                Assert.That(escaped.Id, Is.EqualTo(item.Id));
                Assert.That(escaped.ManagerHandle, Is.SameAs(item.ManagerHandle));
                Assert.That(escaped.NodeManager, Is.SameAs(item.NodeManager));
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
            });
        }

        /// <summary>
        /// A completed refresh cannot borrow the admission retained for a later manager's callback.
        /// </summary>
        [Test]
        public async Task RefreshTargetExpiresBeforeNextManagerCallbackAsync()
        {
            var source = new Mock<IAsyncNodeManager>();
            MonitoredNode2 monitored = CreateMonitoredNode(true);
            var item = new CountingMonitoredItem(this, 1, CreateFilter(SelectA(), null), monitored.NodeManager);
            m_disposables.Add(item);
            using MasterNodeManager master = CreateMaster([source.Object, monitored.NodeManager]);
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(source.Object);
            IEventMonitoredItem? escaped = null;
            source.Setup(manager => manager.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(), It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, IList<IEventMonitoredItem> items, CancellationToken _) =>
                {
                    escaped = items.Single(target => target.Id == item.Id);
                    escaped.QueueEvent(CreateEvent(EventSeverity.High));
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            bool nextCallbackRan = false;
            Mock.Get(monitored.NodeManager).Setup(manager => manager.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(), It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, IList<IEventMonitoredItem> items, CancellationToken _) =>
                {
                    nextCallbackRan = true;
                    Assert.That(generation.Gate.ActiveCount, Is.Zero);
                    escaped!.QueueEvent(CreateEvent(EventSeverity.High));
                    items.Single(target => target.Id == item.Id).QueueEvent(CreateEvent(EventSeverity.High));
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            await master.ConditionRefreshAsync(context, [item]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(nextCallbackRan, Is.True);
                Assert.That(Publish(item), Has.Count.EqualTo(2));
                Assert.That(item.Queued, Is.EqualTo(2));
                Assert.That(generation.Gate.ActiveCount, Is.Zero);
            });
        }

        /// <summary>
        /// Ordinary targets accept only their own source, including interface-only provider items.
        /// </summary>
        [Test]
        public async Task RefreshTargetRejectsForeignSourcesAsync([Values] bool provider, [Values] bool retire)
        {
            var owner = new Mock<IAsyncNodeManager>();
            var foreign = new Mock<IAsyncNodeManager>();
            using MasterNodeManager master = CreateMaster([owner.Object, foreign.Object]);
            IEventMonitoredItem item;
            CountingMonitoredItem? concrete = null;
            Mock<IEventMonitoredItem>? external = null;
            if (provider)
            {
                external = new Mock<IEventMonitoredItem>();
                external.SetupGet(target => target.NodeManager).Returns(owner.Object);
                external.SetupGet(target => target.Id).Returns(1);
                item = external.Object;
            }
            else
            {
                concrete = new CountingMonitoredItem(
                    this, 1, CreateFilter(SelectA(), null), owner.Object, new NodeId("ordinary", 1));
                m_disposables.Add(concrete);
                item = concrete;
            }
            int callbacks = 0;
            foreach (Mock<IAsyncNodeManager> manager in new[] { owner, foreign })
            {
                manager.Setup(value => value.ConditionRefreshAsync(
                        It.IsAny<OperationContext>(), It.IsAny<IList<IEventMonitoredItem>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, IList<IEventMonitoredItem> items, CancellationToken _) =>
                    {
                        callbacks++;
                        if (retire)
                        {
                            NodeManagerGenerationRegistry.GetOrCreate(manager.Object).Gate.Retire();
                        }
                        items.Single(target => target.Id == item.Id).QueueEvent(CreateEvent(EventSeverity.High));
                        items.Single(target => target.Id == item.Id).QueueEvent(
                            CreateEvent(EventSeverity.Low), bypassFilter: true);
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });
            }
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Call, RequestLifetime.None);

            await master.ConditionRefreshAsync(context, [item]).ConfigureAwait(false);

            Assert.That(callbacks, Is.EqualTo(2));
            if (external is not null)
            {
                external.Verify(target => target.QueueEvent(It.IsAny<IFilterTarget>()), Times.Once);
                external.Verify(target => target.QueueEvent(It.IsAny<IFilterTarget>(), true), Times.Once);
            }
            else
            {
                Assert.That(concrete!.Queued, Is.EqualTo(2));
                Assert.That(Publish(concrete), Has.Count.EqualTo(2));
            }
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(owner.Object).Gate.ActiveCount, Is.Zero);
            Assert.That(NodeManagerGenerationRegistry.GetOrCreate(foreign.Object).Gate.ActiveCount, Is.Zero);
        }

        /// <summary>
        /// Refresh targets reject events after their captured authorization or owning session changes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RefreshTargetRejectsChangedAuthorizationAsync(bool transfer)
        {
            MonitoredNode2 monitored = CreateMonitoredNode(true);
            var item = new CountingMonitoredItem(this, 1, CreateFilter(SelectA(), null), monitored.NodeManager);
            m_disposables.Add(item);
            var session = new Mock<ISession>();
            bool stale = false;
            session.SetupGet(value => value.IsIdentityStale).Returns(() => stale);
            var subscription = new Mock<ISubscription>();
            ISession currentSession = session.Object;
            subscription.SetupGet(value => value.Session).Returns(() => currentSession);
            item.SubscriptionCallback = subscription.Object;
            using MasterNodeManager master = CreateMaster([monitored.NodeManager]);
            bool attempted = false;
            Mock.Get(monitored.NodeManager).Setup(manager => manager.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(), It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, IList<IEventMonitoredItem> items, CancellationToken _) =>
                {
                    IEventMonitoredItem target = items.Single();
                    target.QueueEvent(CreateEvent(EventSeverity.High));
                    if (transfer)
                    {
                        currentSession = new Mock<ISession>().Object;
                    }
                    else
                    {
                        stale = true;
                    }
                    target.QueueEvent(CreateEvent(EventSeverity.High));
                    attempted = true;
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            using var context = new OperationContext(session.Object, DiagnosticsMasks.All);

            await master.ConditionRefreshAsync(context, [item]).ConfigureAwait(false);

            stale = false;
            currentSession = session.Object;
            Assert.Multiple(() =>
            {
                Assert.That(attempted, Is.True);
                Assert.That(item.Queued, Is.EqualTo(1));
                Assert.That(Publish(item), Has.Count.EqualTo(1));
                Assert.That(NodeManagerGenerationRegistry.GetOrCreate(monitored.NodeManager).Gate.ActiveCount, Is.Zero);
            });
        }

        /// <summary>
        /// An overridable delivery cannot lend its source admission to another item's nested queue call.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AdmittedVirtualEventCallCannotLendItsSourceToAnotherItem(bool preselectedFields)
        {
            InstanceStateSnapshot snapshot = CreateEvent(EventSeverity.High);
            EventFilter filter = CreateFilter(SelectA(), null);
            MonitoredItem reference = CreateItem(3, filter, null);
            reference.QueueEvent(snapshot);
            EventFieldList fields = Publish(reference)[0];
            var source = new Mock<IAsyncNodeManager>();
            var unrelatedSource = new Mock<IAsyncNodeManager>();
            var parent = new CountingMonitoredItem(this, 1, filter, source.Object);
            var nested = new CountingMonitoredItem(this, 2, filter, unrelatedSource.Object);
            m_disposables.Add(parent);
            m_disposables.Add(nested);
            parent.BeforeQueue = instance =>
            {
                if (preselectedFields)
                {
                    nested.QueueEvent(fields);
                }
                else
                {
                    nested.QueueEvent(instance);
                }
            };
            using MasterNodeManager master = CreateMaster([source.Object, unrelatedSource.Object]);
            Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                m_server, source.Object, out MasterNodeManager.SourceEmissionAdmission? admission), Is.True);
            using (admission)
            {
                NodeManagerGenerationRegistry.GetOrCreate(source.Object).Gate.Retire();
                NodeManagerGenerationRegistry.GetOrCreate(unrelatedSource.Object).Gate.Retire();

                parent.QueueSharedEvent(snapshot, parent.GetSharedEventFilterKey()!,
                    new MonitoredItem.SharedEventEvaluation(), admission?.Generation);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Publish(parent), Has.Count.EqualTo(1));
                Assert.That(Publish(nested), Is.Empty);
            });
        }

        /// <summary>
        /// Independent work with a captured execution context cannot borrow an item's in-progress delivery.
        /// </summary>
        [Test]
        public async Task EscapedQueueCallCannotBorrowAnotherThreadsItemAdmissionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var release = new ManualResetEventSlim();
            InstanceStateSnapshot snapshot = CreateEvent(EventSeverity.High);
            EventFilter filter = CreateFilter(SelectA(), null);
            MonitoredItem reference = CreateItem(2, filter, null);
            reference.QueueEvent(snapshot);
            EventFieldList fields = Publish(reference)[0];
            var source = new Mock<IAsyncNodeManager>();
            var item = new CountingMonitoredItem(this, 1, filter, source.Object);
            m_disposables.Add(item);
            using MasterNodeManager master = CreateMaster([source.Object]);
            var entered = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            item.BeforeQueue = _ =>
            {
                entered.TrySetResult(Task.Run(() => item.QueueEvent(fields)));
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("The admitted virtual delivery was not released.");
                }
            };
            Task delivery = Task.Run(() =>
            {
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    m_server, source.Object, out MasterNodeManager.SourceEmissionAdmission? admission), Is.True);
                using (admission)
                {
                    NodeManagerGenerationRegistry.GetOrCreate(source.Object).Gate.Retire();
                    item.QueueSharedEvent(snapshot, item.GetSharedEventFilterKey()!,
                        new MonitoredItem.SharedEventEvaluation(), admission?.Generation);
                }
            });
            Task? escaped = null;
            try
            {
                escaped = await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                await escaped.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(delivery.IsCompleted, Is.False,
                    "The unrelated queue call must return without waiting for the admitted item's lock.");
            }
            finally
            {
                release.Set();
                await Task.WhenAll(delivery, escaped ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            Assert.That(Publish(item), Has.Count.EqualTo(1));
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

        private static ReadValueId EventItemToMonitor(NodeId nodeId = default)
        {
            return new ReadValueId
            {
                NodeId = nodeId.IsNull ? ObjectIds.Server : nodeId,
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

        /// <summary>
        /// Creates a routing owner for socket-free generation-admission tests.
        /// </summary>
        private MasterNodeManager CreateMaster(ArrayOf<IAsyncNodeManager> managers)
        {
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(new Mock<IConfigurationNodeManager>().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(new Mock<ICoreNodeManager>().Object);
            Mock<IServerInternal> server = Mock.Get(m_server);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            var master = new MasterNodeManager(
                m_server,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                null,
                managers.ToList(),
                null);
            server.SetupGet(value => value.NodeManager).Returns(master);
            return master;
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

            public CountingMonitoredItem(
                SharedEventEvaluationTests fixture,
                uint id,
                EventFilter filter,
                IAsyncNodeManager? nodeManager = null,
                NodeId nodeId = default)
                : base(
                    fixture.m_server,
                    nodeManager ?? new Mock<IAsyncNodeManager>().Object,
                    null!,
                    1,
                    id,
                    EventItemToMonitor(nodeId),
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

            /// <summary>
            /// Gets the number of calls through the one-argument virtual queue entry point.
            /// </summary>
            public int OneArgumentCalls => Volatile.Read(ref m_oneArgumentCalls);

            /// <summary>
            /// Gets or sets the nested call exercised before forwarding to the base queue implementation.
            /// </summary>
            public Action<IFilterTarget>? BeforeQueue { get; set; }

            /// <inheritdoc/>
            public override void QueueEvent(IFilterTarget instance)
            {
                Interlocked.Increment(ref m_oneArgumentCalls);
                base.QueueEvent(instance);
            }

            public override void QueueEvent(IFilterTarget instance, bool bypassFilter)
            {
                Interlocked.Increment(ref m_queued);
                BeforeQueue?.Invoke(instance);
                base.QueueEvent(instance, bypassFilter);
            }

            /// <summary>The one-argument virtual entry count.</summary>
            private int m_oneArgumentCalls;
        }
    }
}
