// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Exercises intentionally retained synchronous compatibility.
// TODO: Remove the legacy cases when the synchronous API is removed.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class ServerEventFanoutRegressionTests
    {
        /// <summary>
        /// An existing Server subscriber receives root events through the published owner's gate.
        /// </summary>
        /// <param name="forwarded">Whether the owner is a custom forwarding facade.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExistingServerSubscriberReceivesPublishedRootEventAsync(bool forwarded)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using var resources = new ResourceManager(new ApplicationConfiguration());
            server.SetupGet(value => value.ResourceManager).Returns(resources);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                IAsyncNodeManager registered = forwarded ? facade.Object : manager;
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [registered]);
                NodeManagerGeneration published = NodeManagerGenerationRegistry.GetOrCreate(registered);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                var item = (MonitoredItem)events.CreateMonitoredItem(
                    context, master.CoreNodeManager!, null!, 1, new MonitoredItemIdFactory(),
                    TimestampsToReturn.Both, 1000,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                    },
                    filter, false);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("forwarded-subscriber-root", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("forwarded-subscriber-root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, root).ConfigureAwait(false);
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                MonitoredNode2 monitored = manager.MonitoredNodes[root.NodeId];
                Assert.That(monitored.EventMonitoredItems[item.Id], Is.SameAs(item));
                NodeManagerGenerationReference generation = monitored.Generation
                    ?? throw new AssertionException("The monitored node has no generation.");
                Assert.That(generation.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                Assert.That(bound, Is.SameAs(published));
                Assert.That(bound!.NodeManager, Is.SameAs(registered));

                var occurrence = new BaseEventState(null);
                occurrence.Initialize(manager.SystemContext, root, EventSeverity.Medium,
                    new LocalizedText("forwarded-before"));
                ServiceResult permission = await manager.ValidateEventRolePermissionsAsync(item, occurrence)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(permission), Is.True, $"Event permission: {permission}");
                await root.ReportEventAsync(manager.SystemContext, occurrence).ConfigureAwait(false);
                await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
                var notifications = new Queue<QueuedEvent>();
                _ = item.Publish(context, notifications, 10);
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications.Dequeue().EventFields[0].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("forwarded-before"));

                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, manager, out var admission, monitored.Generation), Is.True);
                Task drain;
                using (MasterNodeManager.SourceEmissionOrigin? origin = admission?.TransferToOrigin())
                {
                    Assert.That(admission?.Generation, Is.SameAs(published));
                    Assert.That(origin, Is.Not.Null);
                    published.Gate.Retire();
                    drain = published.Gate.WaitForDrainAsync();
                    Assert.That(drain.IsCompleted, Is.False);
                    ISystemContext provenance = origin!.CreateContext(manager.SystemContext);
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, manager, out var child, monitored.Generation,
                        context: provenance), Is.True);
                    using (child)
                    {
                        Assert.That(child?.Generation, Is.SameAs(published));
                    }
                    var unrelated = new Mock<IAsyncNodeManager>();
                    unrelated.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                    bool unrelatedAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, unrelated.Object, out var other, monitored.Generation, context: provenance);
                    other?.Dispose();
                    Mock<IServerInternal> foreign = DeterministicServerMock.Create(
                        out MonitoredItemQueueFactory foreignQueues);
                    using (foreignQueues)
                    using (MasterNodeManager foreignMaster = DeterministicServerMock.CreateMasterNodeManager(
                        foreign, []))
                    {
                        bool foreignAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            foreign.Object, foreignMaster.CoreNodeManager, out var foreignAdmission,
                            isGlobalServerEventSink: true, context: provenance);
                        foreignAdmission?.Dispose();
                        Assert.Multiple(() =>
                        {
                            Assert.That(unrelatedAccepted, Is.False);
                            Assert.That(foreignAccepted, Is.False);
                        });
                    }
                    var rejected = new BaseEventState(null);
                    rejected.Initialize(manager.SystemContext, root, EventSeverity.Medium,
                        new LocalizedText("forwarded-after"));
                    await root.ReportEventAsync(manager.SystemContext, rejected).ConfigureAwait(false);
                    await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    _ = item.Publish(context, notifications, 10);
                    Assert.That(notifications, Is.Empty);
                }
                await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Root fan-out carries revocable source provenance and lets admitted child work finish after retirement.
        /// </summary>
        [Test]
        public async Task RootEventContextRetainsOnlyAdmittedFanoutWithoutAmbientSourceAsync(
            [Values] bool retire,
            [Values] bool fail,
            [Values] bool serverContext)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [manager]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("root-context", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                ISystemContext original = serverContext
                    ? manager.SystemContext
                    : new SystemContext(server.Object.Telemetry)
                    {
                        UserId = "root-event-user",
                        AuditEntryId = "root-event-audit",
                        NamespaceUris = manager.SystemContext.NamespaceUris,
                        ServerUris = manager.SystemContext.ServerUris,
                        TypeTable = manager.SystemContext.TypeTable,
                        EncodeableFactory = manager.SystemContext.EncodeableFactory,
                        NodeStateFactory = manager.SystemContext.NodeStateFactory,
                        PreferredLocales = ["en-US"]
                    };
                ISystemContext? escaped = null;
                bool ambient = false;
                bool admitted = false;
                MasterNodeManager.SourceEmissionAdmission? child = null;
                server.Setup(value => value.ReportEventAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns((ISystemContext context, IFilterTarget _, CancellationToken _) =>
                    {
                        escaped = context is ServerSystemContext propagated ? propagated.Copy() : context;
                        ambient = DeterministicServerMock.HasImplicitEventSource(server.Object, manager);
                        if (retire)
                        {
                            generation.Gate.Retire();
                        }
                        admitted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, master.CoreNodeManager!, out child,
                            isGlobalServerEventSink: true, context: escaped);
                        return fail
                            ? new ValueTask(Task.FromException(new InvalidOperationException("Controlled report failure.")))
                            : default;
                    });
                try
                {
                    if (fail)
                    {
                        await Assert.ThrowsAsync<InvalidOperationException>(
                            async () => await root.ReportEventAsync(original, new BaseEventState(null))
                                .ConfigureAwait(false)).ConfigureAwait(false);
                    }
                    else
                    {
                        await root.ReportEventAsync(original, new BaseEventState(null)).ConfigureAwait(false);
                    }
                    bool escapedAdmitted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, master.CoreNodeManager!, out var escapedAdmission,
                        isGlobalServerEventSink: true, context: escaped);
                    escapedAdmission?.Dispose();
                    Assert.Multiple(() =>
                    {
                        Assert.That(ambient, Is.False);
                        Assert.That(admitted, Is.True);
                        Assert.That(child?.Generation, Is.SameAs(generation));
                        Assert.That(generation.Gate.ActiveCount, Is.EqualTo(1));
                        Assert.That(escapedAdmitted, Is.False);
                        Assert.That(escaped, Is.Not.SameAs(original));
                        Assert.That(escaped!.NamespaceUris, Is.SameAs(original.NamespaceUris));
                        Assert.That(escaped.UserId, Is.EqualTo(original.UserId));
                        Assert.That(escaped.AuditEntryId, Is.EqualTo(original.AuditEntryId));
                        Assert.That(escaped.TypeTable, Is.SameAs(original.TypeTable));
                        Assert.That(escaped.EncodeableFactory, Is.SameAs(original.EncodeableFactory));
                        Assert.That(escaped.NodeStateFactory, Is.SameAs(original.NodeStateFactory));
                        Assert.That(escaped.PreferredLocales, Is.EqualTo(original.PreferredLocales));
                    });
                    generation.Gate.Retire();
                    Task drained = generation.Gate.WaitForDrainAsync();
                    Assert.That(drained.IsCompleted, Is.False);
                    child?.Dispose();
                    child = null;
                    await drained.ConfigureAwait(false);
                    Assert.That(generation.Gate.ActiveCount, Is.Zero);
                }
                finally
                {
                    child?.Dispose();
                }
            }
        }

        /// <summary>
        /// Delivers a root event from a resource registered through a forwarding facade only while the facade's
        /// generation is published and admitting work.
        /// </summary>
        [Test]
        public async Task ForwardedFacadeRootEventsUsePublishedFacadeGenerationAsync(
            [Values] bool forwarded)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                IAsyncNodeManager registered = forwarded ? facade.Object : manager;
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [registered]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(registered);
                var serverNode = new BaseObjectState(null) { NodeId = ObjectIds.Server };
                Mock.Get(master.CoreNodeManager!).Setup(value => value.ValidateEventRolePermissionsAsync(
                        It.IsAny<IEventMonitoredItem>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns((IEventMonitoredItem _, IFilterTarget _, CancellationToken _) =>
                        new ValueTask<ServiceResult>(ServiceResult.Good));
                await using var monitored = new MonitoredNode2(master.CoreNodeManager!, server.Object, serverNode);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.MonitoringAllEvents).Returns(true);
                monitored.Add(item.Object);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("forwarded-root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                NodeManagerGeneration? propagatedGeneration = null;
                server.Setup(value => value.ReportEventAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns((ISystemContext context, IFilterTarget target, CancellationToken cancellationToken) =>
                    {
                        bool admitted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, master.CoreNodeManager, out MasterNodeManager.SourceEmissionAdmission? child,
                            isGlobalServerEventSink: true, context: context);
                        propagatedGeneration = child?.Generation;
                        child?.Dispose();
                        Assert.That(admitted, Is.True);
                        return serverNode.ReportEventAsync(context, target, cancellationToken);
                    });
                IFilterTarget notification = Mock.Of<IFilterTarget>();

                await root.ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(generation.NodeManager, Is.SameAs(registered));
                    Assert.That(propagatedGeneration, Is.SameAs(generation));
                    Assert.That(generation.Gate.ActiveCount, Is.Zero);
                });
                item.Verify(value => value.QueueEvent(notification), Times.Once);

                generation.Gate.Retire();
                await root.ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                item.Verify(value => value.QueueEvent(notification), Times.Once);
            }
        }

        /// <summary>
        /// An existing Server subscriber receives root events through the exact published source generation.
        /// </summary>
        [Test]
        public async Task ExistingServerSubscriberUsesPublishedRootGenerationAsync([Values] bool forwarded)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using var logs = new RecordingLoggerProvider();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
            Mock.Get(server.Object.Telemetry).SetupGet(value => value.LoggerFactory).Returns(loggerFactory);
            using var resources = new ResourceManager(new ApplicationConfiguration());
            server.SetupGet(value => value.ResourceManager).Returns(resources);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                IAsyncNodeManager registered = forwarded ? facade.Object : manager;
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [registered]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(registered);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                var item = (MonitoredItem)events.CreateMonitoredItem(
                    context, master.CoreNodeManager!, null!, 1, new MonitoredItemIdFactory(),
                    TimestampsToReturn.Both, 1000,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                    },
                    filter, false);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("forwarded-subscriber-root", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("forwarded-subscriber-root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, root).ConfigureAwait(false);
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                MonitoredNode2 monitored = manager.MonitoredNodes[root.NodeId];
                Assert.That(monitored.EventMonitoredItems[item.Id], Is.SameAs(item));
                NodeManagerGenerationReference? reference = monitored.Generation;
                Assert.That(reference, Is.Not.Null);
                Assert.That(reference!.TryGetGeneration(out NodeManagerGeneration? bound), Is.True);
                Assert.That(bound, Is.SameAs(generation));

                var occurrence = new BaseEventState(null);
                occurrence.Initialize(manager.SystemContext, root, EventSeverity.Medium,
                    new LocalizedText("forwarded-before"));
                ServiceResult permission = await manager.ValidateEventRolePermissionsAsync(item, occurrence)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(permission), Is.True, $"Event permission: {permission}");
                await root.ReportEventAsync(manager.SystemContext, occurrence).ConfigureAwait(false);
                await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(
                    item.ItemsInQueue,
                    Is.EqualTo(1),
                    $"Event logs: {string.Join("; ", logs.Records.Select(record =>
                        record.Exception?.ToString() ?? record.Message))}");
                var notifications = new Queue<QueuedEvent>();
                _ = item.Publish(context, notifications, 10);
                Assert.That(notifications, Has.Count.EqualTo(1));
                Assert.That(notifications.Dequeue().EventFields[0].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(message.Text, Is.EqualTo("forwarded-before"));

                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, manager, out MasterNodeManager.SourceEmissionAdmission? admitted,
                    monitored.Generation), Is.True);
                Task drain;
                using (MasterNodeManager.SourceEmissionOrigin? origin = admitted?.TransferToOrigin())
                {
                    Assert.That(admitted?.Generation, Is.SameAs(generation));
                    Assert.That(origin, Is.Not.Null);
                    generation.Gate.Retire();
                    drain = generation.Gate.WaitForDrainAsync();
                    Assert.That(drain.IsCompleted, Is.False);
                    Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, manager, out MasterNodeManager.SourceEmissionAdmission? child,
                        monitored.Generation,
                        context: origin!.CreateContext(manager.SystemContext)), Is.True);
                    using (child)
                    {
                        Assert.That(child?.Generation, Is.SameAs(generation));
                    }
                    var rejected = new BaseEventState(null);
                    rejected.Initialize(manager.SystemContext, root, EventSeverity.Medium,
                        new LocalizedText("forwarded-after"));
                    await root.ReportEventAsync(manager.SystemContext, rejected).ConfigureAwait(false);
                    await monitored.WaitForPendingNotificationsAsync(CancellationToken.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    _ = item.Publish(context, notifications, 10);
                    Assert.That(notifications, Is.Empty);
                }
                await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// An unpublished facade cannot borrow the generation published for the same resource.
        /// </summary>
        [Test]
        public async Task UnpublishedFacadeCannotBorrowPublishedResourceGenerationAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                var publishedFacade = new Mock<IAsyncNodeManager>();
                publishedFacade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                publishedFacade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                var unpublishedFacade = new Mock<IAsyncNodeManager>();
                unpublishedFacade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                using MasterNodeManager master =
                    DeterministicServerMock.CreateMasterNodeManager(server, [publishedFacade.Object]);
                NodeManagerGeneration published = NodeManagerGenerationRegistry.GetOrCreate(publishedFacade.Object);
                NodeManagerGeneration unpublished = NodeManagerGenerationRegistry.GetOrCreate(unpublishedFacade.Object);

                bool rejected = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, unpublishedFacade.Object,
                    out MasterNodeManager.SourceEmissionAdmission? rejectedAdmission);
                bool admitted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, manager, out MasterNodeManager.SourceEmissionAdmission? sourceAdmission);
                bool rejectedBound = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, unpublishedFacade.Object,
                    out MasterNodeManager.SourceEmissionAdmission? rejectedBoundAdmission, published.Reference);
                bool rejectedItem = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, unpublishedFacade.Object, published.Reference,
                    out MasterNodeManager.SourceEmissionAdmission? rejectedItemAdmission);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(unpublished, Is.Not.SameAs(published));
                        Assert.That(rejected, Is.False);
                        Assert.That(rejectedAdmission.HasValue, Is.False);
                        Assert.That(rejectedBound, Is.False);
                        Assert.That(rejectedBoundAdmission.HasValue, Is.False);
                        Assert.That(rejectedItem, Is.False);
                        Assert.That(rejectedItemAdmission.HasValue, Is.False);
                        Assert.That(admitted, Is.True);
                        Assert.That(sourceAdmission?.Generation, Is.SameAs(published));
                    });
                }
                finally
                {
                    rejectedAdmission?.Dispose();
                    rejectedBoundAdmission?.Dispose();
                    rejectedItemAdmission?.Dispose();
                    sourceAdmission?.Dispose();
                }
            }
        }

        /// <summary>
        /// Preparing another facade does not publish that facade as an event or data source.
        /// </summary>
        [Test]
        public void PreparedUnpublishedFacadeCannotBorrowPublishedResourceGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var publishedFacade = new Mock<IAsyncNodeManager>();
                var preparedFacade = new Mock<IAsyncNodeManager>();
                publishedFacade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                preparedFacade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using MasterNodeManager master =
                    DeterministicServerMock.CreateMasterNodeManager(server, [publishedFacade.Object]);
                NodeManagerGeneration published = NodeManagerGenerationRegistry.GetOrCreate(publishedFacade.Object);
                NodeManagerGeneration prepared = NodeManagerGenerationRegistry.CreateAddition(preparedFacade.Object);
                Assert.That(prepared, Is.Not.SameAs(published));
                Assert.That(prepared.NodeManager, Is.SameAs(preparedFacade.Object));
                Assert.That(prepared.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                Assert.That(prepared.ResourceOwnership, Is.SameAs(published.ResourceOwnership));

                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, preparedFacade.Object,
                    out MasterNodeManager.SourceEmissionAdmission? eventAdmission);
                bool valueAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, preparedFacade.Object, null,
                    out MasterNodeManager.SourceEmissionAdmission? valueAdmission);
                bool boundEventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, preparedFacade.Object, out MasterNodeManager.SourceEmissionAdmission? boundEvent,
                    prepared.Reference);
                bool boundValueAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, preparedFacade.Object, prepared.Reference,
                    out MasterNodeManager.SourceEmissionAdmission? boundValue);
                eventAdmission?.Dispose();
                valueAdmission?.Dispose();
                boundEvent?.Dispose();
                boundValue?.Dispose();
                using MonitoredItem privateItem = CreateDataItem(server.Object, preparedFacade.Object, prepared);
                using MonitoredItem publishedItem = CreateDataItem(server.Object, publishedFacade.Object, published);
                privateItem.QueueValue(new DataValue(new Variant(41)), ServiceResult.Good);
                publishedItem.QueueValue(new DataValue(new Variant(42)), ServiceResult.Good);

                Assert.Multiple(() =>
                {
                    Assert.That(eventAccepted, Is.False);
                    Assert.That(valueAccepted, Is.False);
                    Assert.That(boundEventAccepted, Is.False);
                    Assert.That(boundValueAccepted, Is.False);
                    Assert.That(published.Gate.ActiveCount, Is.Zero);
                    Assert.That(prepared.Gate.ActiveCount, Is.Zero);
                    Assert.That(published.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    Assert.That(privateItem.Generation, Is.SameAs(prepared.Reference));
                    Assert.That(privateItem.ItemsInQueue, Is.Zero);
                    Assert.That(publishedItem.Generation, Is.SameAs(published.Reference));
                    Assert.That(publishedItem.ItemsInQueue, Is.EqualTo(1));
                });
            }
        }

        /// <summary>
        /// An unregistered-source lookup rechecks publication before creating a legacy generation.
        /// </summary>
        [Test]
        public void UnregisteredSourceResolutionRechecksPublishedRouting()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                NodeManagerRoutingTable.RoutingSnapshot earlier = routing.Revision;
                Assert.That(earlier.TryGetSourceGeneration(resource.Object, out _), Is.False);

                NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                routing.Add(facade.Object, [1]);
                NodeManagerGeneration resolved = routing.ResolveUnregisteredSource(resource.Object, null);

                Assert.Multiple(() =>
                {
                    Assert.That(resolved, Is.SameAs(generation));
                    Assert.That(NodeManagerGenerationRegistry.TryGet(resource.Object, out _), Is.False);
                    Assert.That(
                        MasterNodeManager.GetSourceGeneration(server.Object, resource.Object),
                        Is.SameAs(generation));
                });
            }
        }

        /// <summary>
        /// A published facade receives emissions and dispatch when its resource already has a generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void PublishedFacadeAfterPriorResourceGenerationReceivesBoundEmissionsAndDispatches(bool legacyFirst)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);

                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                var unpublishedFacade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                unpublishedFacade.SetupGet(value => value.SyncNodeManager).Returns(sync);

                NodeManagerGenerationReference? legacyReference = null;
                if (legacyFirst)
                {
                    NodeManagerGeneration legacy = routing.ResolveUnregisteredSource(resource.Object, null);
                    legacyReference = legacy.Reference;
                    Assert.That(legacy.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    bool legacyAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out MasterNodeManager.SourceEmissionAdmission? legacyAdmission,
                        legacyReference);
                    legacyAdmission?.Dispose();
                    Assert.That(legacyAccepted, Is.True);
                }
                else
                {
                    NodeManagerGeneration prepared = NodeManagerGenerationRegistry.CreateAddition(
                        unpublishedFacade.Object);
                    Assert.That(prepared.Gate.State, Is.EqualTo(NodeManagerGenerationState.Prepared));
                }

                NodeManagerGeneration registered = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                if (legacyFirst)
                {
                    NodeManagerGenerationRegistry.CreateAddition(unpublishedFacade.Object);
                }
                routing.Add(facade.Object, [1]);

                MasterNodeManager.SourceEmissionAdmission? eventAdmission = null;
                MasterNodeManager.SourceEmissionAdmission? itemAdmission = null;
                MasterNodeManager.SourceEmissionAdmission? unpublishedAdmission = null;
                MasterNodeManager.SourceEmissionAdmission? unpublishedItemAdmission = null;
                MasterNodeManager.SourceEmissionAdmission? resourceAdmission = null;
                MasterNodeManager.SourceEmissionAdmission? resourceItemAdmission = null;
                MasterNodeManager.NotificationDispatchLease[] dispatches = [];
                try
                {
                    bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, facade.Object, out eventAdmission, registered.Reference);
                    bool itemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, facade.Object, registered.Reference, out itemAdmission);
                    bool unpublishedAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, unpublishedFacade.Object, out unpublishedAdmission, registered.Reference);
                    bool unpublishedItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, unpublishedFacade.Object, registered.Reference, out unpublishedItemAdmission);
                    bool resourceAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out resourceAdmission, registered.Reference);
                    bool resourceItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object,
                        resource.Object,
                        registered.Reference,
                        out resourceItemAdmission);
                    if (legacyReference is not null)
                    {
                        bool staleAccepted = MasterNodeManager.TryCaptureSourceEmission(
                            server.Object, resource.Object,
                            out MasterNodeManager.SourceEmissionAdmission? staleAdmission,
                            legacyReference);
                        staleAdmission?.Dispose();
                        Assert.That(staleAccepted, Is.False,
                            "An explicit legacy token must not be redirected to the newly published facade.");
                    }
                    dispatches = master.GetAllEventNotificationDispatches([facade.Object]);
                    Assert.Multiple(() =>
                    {
                        Assert.That(eventAccepted, Is.True);
                        Assert.That(eventAdmission?.Generation, Is.SameAs(registered));
                        Assert.That(itemAccepted, Is.True);
                        Assert.That(itemAdmission?.Generation, Is.SameAs(registered));
                        Assert.That(unpublishedAccepted, Is.False);
                        Assert.That(unpublishedAdmission.HasValue, Is.False);
                        Assert.That(unpublishedItemAccepted, Is.False);
                        Assert.That(unpublishedItemAdmission.HasValue, Is.False);
                        Assert.That(resourceAccepted, Is.True);
                        Assert.That(resourceAdmission?.Generation, Is.SameAs(registered));
                        Assert.That(resourceItemAccepted, Is.True);
                        Assert.That(resourceItemAdmission?.Generation, Is.SameAs(registered));
                        Assert.That(dispatches.Select(dispatch => dispatch.NodeManager),
                            Is.EqualTo<IAsyncNodeManager[]>([facade.Object]));
                    });
                    MasterNodeManager.NotificationDispatchLease dispatch = dispatches.Single();
                    dispatch.Dispose();
                    Assert.That(dispatch.IsActive, Is.False);
                    Assert.That(() => dispatch.NodeManager, Throws.TypeOf<ObjectDisposedException>());
                }
                finally
                {
                    eventAdmission?.Dispose();
                    itemAdmission?.Dispose();
                    unpublishedAdmission?.Dispose();
                    unpublishedItemAdmission?.Dispose();
                    resourceAdmission?.Dispose();
                    resourceItemAdmission?.Dispose();
                    MasterNodeManager.DisposeNotificationDispatches(dispatches);
                }
            }
        }

        /// <summary>
        /// A condition-refresh callback goes to the routed facade rather than an earlier resource owner.
        /// </summary>
        [Test]
        public async Task ConditionRefreshUsesRoutedFacadeAfterPriorResourceGenerationAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                facade.Setup(value => value.ConditionRefreshAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<IList<IEventMonitoredItem>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                NodeManagerGeneration earlier = routing.ResolveUnregisteredSource(resource.Object, null);
                NodeManagerGeneration registered = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                Assert.That(earlier.NodeManager, Is.SameAs(resource.Object));
                routing.Add(facade.Object, [1]);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);

                await master.ConditionRefreshAsync(context, []).ConfigureAwait(false);

                facade.Verify(value => value.ConditionRefreshAsync(
                    context, It.IsAny<IList<IEventMonitoredItem>>(), It.IsAny<CancellationToken>()), Times.Once);
                resource.Verify(value => value.ConditionRefreshAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<IList<IEventMonitoredItem>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(registered.Gate.ActiveCount, Is.Zero);
            }
        }

        /// <summary>
        /// Publication controls source routing without rewriting an earlier logical owner's identity.
        /// </summary>
        [Test]
        public async Task UnregisteredSourceResolutionRacesFacadeReservationAsync()
        {
            for (int iteration = 0; iteration < 24; iteration++)
            {
                var routing = new NodeManagerRoutingTable();
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<NodeManagerGeneration?> lookup = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    try
                    {
                        return routing.ResolveUnregisteredSource(resource.Object, null);
                    }
                    catch (InvalidOperationException failure) when (
                        failure.Message == "The unregistered source cannot select another logical owner's generation.")
                    {
                        return null;
                    }
                });
                Task<NodeManagerGeneration> publication = Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    NodeManagerGeneration reserved = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                    routing.Add(facade.Object, [1]);
                    return reserved;
                });
                start.TrySetResult(true);
                await Task.WhenAll((Task)lookup, (Task)publication).WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                NodeManagerGeneration published = await publication.ConfigureAwait(false);
                NodeManagerGeneration? earlier = await lookup.ConfigureAwait(false);
                Assert.That(published.NodeManager, Is.SameAs(facade.Object));
                Assert.That(routing.ResolveUnregisteredSource(resource.Object, null), Is.SameAs(published));
                if (earlier is not null && !ReferenceEquals(earlier, published))
                {
                    Assert.That(earlier.NodeManager, Is.SameAs(resource.Object));
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(resource.Object), Is.SameAs(earlier));
                    Assert.That(earlier.Slot, Is.Not.SameAs(published.Slot));
                }
            }
        }

        /// <summary>
        /// A legacy owner outside the managed routes keeps its exact existing generation admission.
        /// </summary>
        [Test]
        public void LegacyExternalOwnerRetainsBoundEmissionAdmission()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, []);
                var external = new Mock<IAsyncNodeManager>();
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(external.Object);

                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, external.Object, out MasterNodeManager.SourceEmissionAdmission? eventAdmission,
                    generation.Reference);
                bool valueAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, external.Object, generation.Reference,
                    out MasterNodeManager.SourceEmissionAdmission? valueAdmission);
                eventAdmission?.Dispose();
                valueAdmission?.Dispose();

                Assert.Multiple(() =>
                {
                    Assert.That(eventAccepted, Is.True);
                    Assert.That(valueAccepted, Is.True);
                    Assert.That(generation.Gate.ActiveCount, Is.Zero);
                });
            }
        }

        /// <summary>
        /// Explicit stack adapters for one synchronous owner share its published emission generation.
        /// </summary>
        [Test]
        public void ExplicitAdapterAliasUsesPublishedEmissionGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var sync = new Mock<INodeManager>();
                sync.SetupGet(value => value.NamespaceUris).Returns([]);
                using var owner = new AsyncNodeManagerAdapter(sync.Object);
                using var alias = new AsyncNodeManagerAdapter(sync.Object);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, [owner]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(owner);

                bool eventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, alias, out MasterNodeManager.SourceEmissionAdmission? eventAdmission,
                    generation.Reference);
                bool valueAccepted = MasterNodeManager.TryCaptureItemEmission(
                    server.Object, alias, generation.Reference,
                    out MasterNodeManager.SourceEmissionAdmission? valueAdmission);
                eventAdmission?.Dispose();
                valueAdmission?.Dispose();

                Assert.Multiple(() =>
                {
                    Assert.That(eventAccepted, Is.True);
                    Assert.That(valueAccepted, Is.True);
                    Assert.That(generation.Gate.ActiveCount, Is.Zero);
                });
            }
        }

        /// <summary>
        /// A monitored node created before publication binds once the resource has one visible owner.
        /// </summary>
        [Test]
        public void MonitoredResourceBindsOnlyAfterItsFacadeIsVisible()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using var monitored = new MonitoredNode2(
                    resource.Object, server.Object, new BaseObjectState(null) { NodeId = new NodeId("pending", 1) });

                Assert.That(monitored.Generation, Is.Null);
                routing.Add(facade.Object, [1], visible: false);
                Assert.That(monitored.Generation, Is.Null);
                Assert.That(master.TryGetSourceGeneration(facade.Object, out _), Is.False);
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, resource.Object, out MasterNodeManager.SourceEmissionAdmission? before,
                    monitored.Generation), Is.False);
                Assert.That(before.HasValue, Is.False);

                routing.SetVisible(facade.Object, visible: true);
                NodeManagerGeneration published = routing.Revision.GetGeneration(facade.Object);
                NodeManagerGenerationReference? reference = monitored.Generation;
                Assert.That(reference, Is.SameAs(published.Reference));
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, resource.Object, out MasterNodeManager.SourceEmissionAdmission? admitted, reference),
                    Is.True);
                admitted?.Dispose();

                routing.Remove(facade.Object);
                Assert.That(monitored.Generation, Is.SameAs(reference));
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, resource.Object, out MasterNodeManager.SourceEmissionAdmission? after, reference),
                    Is.False);
                Assert.That(after.HasValue, Is.False);
            }
        }

        /// <summary>
        /// A monitored resource does not bind to either of two initial facades sharing that resource.
        /// </summary>
        [Test]
        public void MonitoredResourceDoesNotBindAnAmbiguousFacade()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var first = new Mock<IAsyncNodeManager>();
                var second = new Mock<IAsyncNodeManager>();
                first.SetupGet(value => value.SyncNodeManager).Returns(sync);
                second.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using MasterNodeManager master =
                    DeterministicServerMock.CreateMasterNodeManager(server, [first.Object, second.Object]);
                using var monitored = new MonitoredNode2(
                    resource.Object, server.Object, new BaseObjectState(null) { NodeId = new NodeId("ambiguous", 1) });

                Assert.That(monitored.Generation, Is.Null);
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, resource.Object, out MasterNodeManager.SourceEmissionAdmission? admitted,
                    monitored.Generation), Is.False);
                Assert.That(admitted.HasValue, Is.False);
                Assert.Throws<InvalidOperationException>(() => CreateDataItem(server.Object, resource.Object));
            }
        }

        /// <summary>
        /// An exact binding admits a shared resource through its own facade while unbound emissions are rejected.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task BoundSharedResourceEmissionsUseTheirExactFacadeAsync(bool registerResource)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var first = new Mock<IAsyncNodeManager>();
                var second = new Mock<IAsyncNodeManager>();
                first.SetupGet(value => value.SyncNodeManager).Returns(sync);
                second.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using var alias = new AsyncNodeManagerAdapter(sync);
                using var facadeAlias = new AsyncNodeManagerAdapter(new SyncNodeManagerAdapter(second.Object));
                IAsyncNodeManager[] owners = registerResource
                    ? [resource.Object, first.Object, second.Object]
                    : [first.Object, second.Object];
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, new ArrayOf<IAsyncNodeManager>(new ReadOnlyMemory<IAsyncNodeManager>(owners)));
                NodeManagerGeneration firstGeneration = NodeManagerGenerationRegistry.GetOrCreate(first.Object);
                NodeManagerGeneration secondGeneration = NodeManagerGenerationRegistry.GetOrCreate(second.Object);
                Assert.That(firstGeneration, Is.Not.SameAs(secondGeneration));

                MasterNodeManager.SourceEmissionAdmission? unboundEvent = null;
                MasterNodeManager.SourceEmissionAdmission? unboundItem = null;
                MasterNodeManager.SourceEmissionAdmission? unboundAlias = null;
                MasterNodeManager.SourceEmissionAdmission? firstEvent = null;
                MasterNodeManager.SourceEmissionAdmission? firstItem = null;
                MasterNodeManager.SourceEmissionAdmission? secondEvent = null;
                MasterNodeManager.SourceEmissionAdmission? secondItem = null;
                MasterNodeManager.SourceEmissionAdmission? boundAlias = null;
                MasterNodeManager.SourceEmissionAdmission? exactFacadeAlias = null;
                MasterNodeManager.SourceEmissionAdmission? wrongFacadeAlias = null;
                MasterNodeManager.SourceEmissionAdmission? retiredEvent = null;
                MasterNodeManager.SourceEmissionAdmission? remainingItem = null;
                try
                {
                    bool unboundEventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out unboundEvent);
                    bool unboundItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, resource.Object, null, out unboundItem);
                    bool unboundAliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, alias, out unboundAlias);
                    bool firstEventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out firstEvent, firstGeneration.Reference);
                    bool firstItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, resource.Object, firstGeneration.Reference, out firstItem);
                    bool secondEventAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out secondEvent, secondGeneration.Reference);
                    bool secondItemAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, resource.Object, secondGeneration.Reference, out secondItem);
                    bool boundAliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, alias, out boundAlias, secondGeneration.Reference);
                    bool exactFacadeAliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, facadeAlias, out exactFacadeAlias, secondGeneration.Reference);
                    bool wrongFacadeAliasAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, facadeAlias, out wrongFacadeAlias, firstGeneration.Reference);

                    Assert.Multiple(() =>
                    {
                        Assert.That(unboundEventAccepted, Is.False);
                        Assert.That(unboundItemAccepted, Is.False);
                        Assert.That(unboundAliasAccepted, Is.False);
                        Assert.That(firstEventAccepted, Is.True);
                        Assert.That(firstEvent?.Generation, Is.SameAs(firstGeneration));
                        Assert.That(firstItemAccepted, Is.True);
                        Assert.That(firstItem?.Generation, Is.SameAs(firstGeneration));
                        Assert.That(secondEventAccepted, Is.True);
                        Assert.That(secondEvent?.Generation, Is.SameAs(secondGeneration));
                        Assert.That(secondItemAccepted, Is.True);
                        Assert.That(secondItem?.Generation, Is.SameAs(secondGeneration));
                        Assert.That(boundAliasAccepted, Is.True);
                        Assert.That(boundAlias?.Generation, Is.SameAs(secondGeneration));
                        Assert.That(exactFacadeAliasAccepted, Is.True);
                        Assert.That(exactFacadeAlias?.Generation, Is.SameAs(secondGeneration));
                        Assert.That(wrongFacadeAliasAccepted, Is.False);
                        Assert.That(wrongFacadeAlias.HasValue, Is.False);
                    });

                    firstGeneration.Gate.Retire();
                    Task drain = firstGeneration.Gate.WaitForDrainAsync();
                    Assert.That(drain.IsCompleted, Is.False);
                    bool retiredAccepted = MasterNodeManager.TryCaptureSourceEmission(
                        server.Object, resource.Object, out retiredEvent, firstGeneration.Reference);
                    bool remainingAccepted = MasterNodeManager.TryCaptureItemEmission(
                        server.Object, resource.Object, secondGeneration.Reference, out remainingItem);
                    Assert.That(retiredAccepted, Is.False);
                    Assert.That(remainingAccepted, Is.True);
                    Assert.That(remainingItem?.Generation, Is.SameAs(secondGeneration));

                    firstEvent?.Dispose();
                    firstEvent = null;
                    firstItem?.Dispose();
                    firstItem = null;
                    await drain.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                finally
                {
                    unboundEvent?.Dispose();
                    unboundItem?.Dispose();
                    unboundAlias?.Dispose();
                    firstEvent?.Dispose();
                    firstItem?.Dispose();
                    secondEvent?.Dispose();
                    secondItem?.Dispose();
                    boundAlias?.Dispose();
                    exactFacadeAlias?.Dispose();
                    wrongFacadeAlias?.Dispose();
                    retiredEvent?.Dispose();
                    remainingItem?.Dispose();
                }
            }
        }

        /// <summary>
        /// An unbound adapter alias must not select the first of conflicting visible generations.
        /// </summary>
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void AdapterAliasRequiresOneVisibleGeneration(bool conflicting, bool hideSecond, bool expected)
        {
            INodeManager sync = Mock.Of<INodeManager>();
            using var first = new AsyncNodeManagerAdapter(sync);
            using var second = new AsyncNodeManagerAdapter(sync);
            using var source = new AsyncNodeManagerAdapter(sync);
            var view = new NodeManagerRoutingTable.RoutingSnapshot(
                [first, second],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                hideSecond ? [second] : []);
            NodeManagerGeneration original = view.Generations[0];
            Assert.That(view.Generations[1], Is.SameAs(original));
            if (conflicting)
            {
                view.Generations[1] = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, second, NodeManagerGenerationState.Live);
            }

            bool found = view.TryGetSourceGeneration(source, out NodeManagerGeneration? generation);
            Assert.That(found, Is.EqualTo(expected));
            Assert.That(generation, expected ? Is.SameAs(original) : Is.Null);
            Assert.That(view.TryGetSourceGeneration(first, out NodeManagerGeneration? exact), Is.True);
            Assert.That(exact, Is.SameAs(original));
            if (hideSecond)
            {
                Assert.That(view.TryGetSourceGeneration(source, out _, includeHidden: true), Is.False);
            }
        }

        /// <summary>
        /// A resource and its standard adapter aliases retain one unambiguous source identity.
        /// </summary>
        [Test]
        public void RegisteredResourceAndStandardAliasRemainUnambiguous()
        {
            var resource = new Mock<IAsyncNodeManager>();
            var sync = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
            using var alias = new AsyncNodeManagerAdapter(sync);
            var view = new NodeManagerRoutingTable.RoutingSnapshot(
                [resource.Object, alias],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                []);

            Assert.That(view.Generations[1], Is.SameAs(view.Generations[0]));
            Assert.That(view.TryGetSourceGeneration(resource.Object, out NodeManagerGeneration? generation), Is.True);
            Assert.That(generation, Is.SameAs(view.Generations[0]));
        }

        /// <summary>
        /// A hidden distinct facade does not make the visible resource source ambiguous until it is included.
        /// </summary>
        [Test]
        public void HiddenFacadeDoesNotMakeVisibleResourceAmbiguous()
        {
            var resource = new Mock<IAsyncNodeManager>();
            var sync = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
            var hidden = new Mock<IAsyncNodeManager>();
            hidden.SetupGet(value => value.SyncNodeManager).Returns(sync);
            var view = new NodeManagerRoutingTable.RoutingSnapshot(
                [resource.Object, hidden.Object],
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                [hidden.Object]);

            Assert.That(view.TryGetSourceGeneration(resource.Object, out NodeManagerGeneration? visible), Is.True);
            Assert.That(visible, Is.SameAs(view.Generations[0]));
            Assert.That(view.TryGetSourceGeneration(resource.Object, out _, includeHidden: true), Is.False);
        }

        /// <summary>
        /// An ambiguous source does not reserve a component, item, or monitored node during creation or restoration.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void AmbiguousDataItemCreationOrRestoreDoesNotReserveNativeState(bool restore, bool reuseExisting)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var first = new Mock<IAsyncNodeManager>();
                var second = new Mock<IAsyncNodeManager>();
                first.SetupGet(value => value.SyncNodeManager).Returns(sync);
                second.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using MasterNodeManager master =
                    DeterministicServerMock.CreateMasterNodeManager(server, [first.Object, second.Object]);
                using var manager = new MonitoredNodeMonitoredItemManager(resource.Object, server.Object);
                var node = new BaseDataVariableState(null) { NodeId = new NodeId("ambiguous-value", 1) };
                var handle = new NodeHandle(node.NodeId, node);
                MonitoredNode2? existing = null;
                if (reuseExisting)
                {
                    existing = new MonitoredNode2(resource.Object, server.Object, node);
                    manager.MonitoredNodes[node.NodeId] = existing;
                }
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                };
                bool componentCacheTouched = false;
                NodeState AddToCache(ISystemContext context, NodeHandle nodeHandle, NodeState cached)
                {
                    componentCacheTouched = true;
                    return cached;
                }

                if (restore)
                {
                    Assert.Throws<InvalidOperationException>(() => manager.RestoreMonitoredItem(
                        server.Object,
                        resource.Object,
                        server.Object.DefaultSystemContext,
                        handle,
                        new StoredMonitoredItem { Id = 1 },
                        Mock.Of<IUserIdentity>(),
                        AddToCache,
                        out _));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => manager.CreateMonitoredItem(
                        server.Object,
                        resource.Object,
                        server.Object.DefaultSystemContext,
                        handle,
                        subscriptionId: 1,
                        publishingInterval: 1000,
                        diagnosticsMasks: DiagnosticsMasks.None,
                        timestampsToReturn: TimestampsToReturn.Both,
                        itemToCreate: request,
                        euRange: null!,
                        filterToUse: null!,
                        samplingInterval: 1000,
                        revisedQueueSize: 10,
                        createDurable: false,
                        monitoredItemIdFactory: new MonitoredItemIdFactory(),
                        addNodeToComponentCache: AddToCache));
                }

                Assert.Multiple(() =>
                {
                    Assert.That(manager.MonitoredItems, Is.Empty);
                    Assert.That(componentCacheTouched, Is.False);
                    Assert.That(handle.MonitoredNode, Is.Null);
                    if (reuseExisting)
                    {
                        Assert.That(manager.MonitoredNodes[node.NodeId], Is.SameAs(existing));
                    }
                    else
                    {
                        Assert.That(manager.MonitoredNodes, Is.Empty);
                    }
                });
            }
        }

        /// <summary>
        /// A legacy monitored node can use its already established external owner.
        /// </summary>
        [Test]
        public void LegacyMonitoredNodeUsesExistingExternalGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(server, []);
                var external = new Mock<IAsyncNodeManager>();
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(external.Object);
                using var monitored = new MonitoredNode2(
                    external.Object, server.Object, new BaseObjectState(null) { NodeId = new NodeId("legacy", 1) });

                Assert.That(monitored.Generation, Is.SameAs(generation.Reference));
                Assert.That(MasterNodeManager.TryCaptureSourceEmission(
                    server.Object, external.Object, out MasterNodeManager.SourceEmissionAdmission? admission,
                    monitored.Generation), Is.True);
                admission?.Dispose();
            }
        }

        /// <summary>
        /// A data item keeps the staged owner's identity while emissions wait for publication.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void ForwardedDataItemBindsStagedOwnerBeforePublication(bool registerHidden)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object, new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.CreateAddition(facade.Object);
                if (registerHidden)
                {
                    routing.Add(facade.Object, [1], visible: false);
                }
                using MonitoredItem item = CreateDataItem(server.Object, resource.Object, generation);

                Assert.That(item.Generation, Is.SameAs(generation.Reference));
                item.QueueValue(new DataValue(new Variant(41)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.Zero);

                if (registerHidden)
                {
                    routing.SetVisible(facade.Object, visible: true);
                }
                else
                {
                    routing.Add(facade.Object, [1]);
                }
                item.QueueValue(new DataValue(new Variant(42)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));

                routing.Remove(facade.Object);
                Assert.That(item.Generation, Is.SameAs(generation.Reference));
                item.QueueValue(new DataValue(new Variant(43)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A data item created while the retired resource's request view is pinned retains its generation.
        /// </summary>
        [Test]
        public void ForwardedDataItemRetainsRetiredResourceGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var routing = new NodeManagerRoutingTable();
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(server.Object.NodeManager.ConfigurationNodeManager!);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(server.Object.CoreNodeManager);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                using var master = new MasterNodeManager(
                    server.Object, new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    routing);
                server.SetupGet(value => value.NodeManager).Returns(master);
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                routing.Add(facade.Object, [1]);
                NodeManagerGeneration generation = routing.Revision.GetGeneration(facade.Object);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                using OperationContext.RoutingScope pinned = context.CaptureRouting(routing);
                routing.Remove(facade.Object);

                Assert.That(context.RoutingSnapshot!.TryGetSourceGeneration(
                    resource.Object, out NodeManagerGeneration? pinnedGeneration), Is.True);
                Assert.That(pinnedGeneration, Is.SameAs(generation));
                using var manager = new MonitoredNodeMonitoredItemManager(resource.Object, server.Object);
                var node = new BaseDataVariableState(null) { NodeId = new NodeId("pinned-value", 1) };
                var handle = new NodeHandle(node.NodeId, node);
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                };
                var item = (MonitoredItem)manager.CreateMonitoredItem(
                    server.Object,
                    resource.Object,
                    new ServerSystemContext(server.Object, context),
                    handle,
                    subscriptionId: 1,
                    publishingInterval: 1000,
                    diagnosticsMasks: DiagnosticsMasks.None,
                    timestampsToReturn: TimestampsToReturn.Both,
                    itemToCreate: request,
                    euRange: null!,
                    filterToUse: null!,
                    samplingInterval: 1000,
                    revisedQueueSize: 10,
                    createDurable: false,
                    monitoredItemIdFactory: new MonitoredItemIdFactory(),
                    addNodeToComponentCache: static (_, _, cached) => cached);
                Assert.That(item.Generation, Is.SameAs(generation.Reference));
                item.QueueValue(new DataValue(new Variant(42)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.Zero);
            }
        }

        /// <summary>
        /// A data item created by a forwarded resource belongs to the published generation.
        /// </summary>
        [Test]
        public void ForwardedResourceDataItemUsesPublishedGeneration()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var resource = new Mock<IAsyncNodeManager>();
                var sync = new SyncNodeManagerAdapter(resource.Object);
                resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [facade.Object]);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(facade.Object);
                using MonitoredItem item = CreateDataItem(server.Object, resource.Object);
                Assert.That(item.Generation, Is.SameAs(generation.Reference));

                item.QueueValue(new DataValue(new Variant(42)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
                generation.Gate.Retire();
                item.QueueValue(new DataValue(new Variant(43)), ServiceResult.Good);
                Assert.That(item.ItemsInQueue, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Creates a data item bound to the specified source.
        /// </summary>
        private static MonitoredItem CreateDataItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            NodeManagerGeneration? sourceGeneration = null)
        {
            return new MonitoredItem(
                server,
                nodeManager,
                new NodeHandle(),
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId { NodeId = new NodeId("value", 1), AttributeId = Attributes.Value },
                diagnosticsMasks: DiagnosticsMasks.None,
                timestampsToReturn: TimestampsToReturn.Both,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval: 1000,
                queueSize: 10,
                discardOldest: true,
                sourceSamplingInterval: 1000,
                createDurable: false,
                timeProvider: null,
                sourceGeneration: sourceGeneration);
        }

        /// <summary>
        /// A resource source resolves only a single visible facade in the captured routing view.
        /// </summary>
        [TestCase(false, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void ResourceSourceRequiresOneVisibleFacade(bool hidden, bool ambiguous, bool expected)
        {
            var resource = new Mock<IAsyncNodeManager>();
            var sync = new SyncNodeManagerAdapter(resource.Object);
            resource.SetupGet(value => value.SyncNodeManager).Returns(sync);
            var facade = new Mock<IAsyncNodeManager>();
            facade.SetupGet(value => value.SyncNodeManager).Returns(sync);
            var otherFacade = new Mock<IAsyncNodeManager>();
            otherFacade.SetupGet(value => value.SyncNodeManager).Returns(sync);
            IAsyncNodeManager[] owners = ambiguous ? [facade.Object, otherFacade.Object] : [facade.Object];
            var view = new NodeManagerRoutingTable.RoutingSnapshot(
                owners,
                new Dictionary<int, IReadOnlyList<IAsyncNodeManager>>(),
                hidden ? [facade.Object] : []);

            bool found = view.TryGetSourceGeneration(resource.Object, out NodeManagerGeneration? generation);

            Assert.That(found, Is.EqualTo(expected));
            Assert.That(generation, expected ? Is.SameAs(view.Generations[0]) : Is.Null);
        }

        /// <summary>
        /// Rejects a root event from a resource whose forwarding facade has a generation outside the published view.
        /// </summary>
        [Test]
        public async Task UnpublishedForwardedResourceRootEventsAreRejectedAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(() => manager.NamespaceUris);
                facade.SetupGet(value => value.SyncNodeManager).Returns(manager.SyncNodeManager);
                NodeManagerGeneration unpublished = NodeManagerGenerationRegistry.GetOrCreate(facade.Object);
                var other = new Mock<IAsyncNodeManager>();
                using MasterNodeManager master = DeterministicServerMock.CreateMasterNodeManager(
                    server, [other.Object]);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("unpublished-root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                int delivered = 0;
                server.Setup(value => value.ReportEventAsync(
                        It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                    .Returns((ISystemContext _, IFilterTarget _, CancellationToken _) =>
                    {
                        delivered++;
                        return default;
                    });

                await root.ReportEventAsync(manager.SystemContext, new BaseEventState(null)).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(delivered, Is.Zero);
                    Assert.That(unpublished.Gate.ActiveCount, Is.Zero);
                });
            }
        }

        /// <summary>
        /// Explicit event provenance cannot authorize an unrelated notifier, generation or server.
        /// </summary>
        [Test]
        public void RootEventContextRejectsUnrelatedSourcesAndServers()
        {
            Mock<IServerInternal> first = DeterministicServerMock.Create(out MonitoredItemQueueFactory firstQueues);
            Mock<IServerInternal> second = DeterministicServerMock.Create(out MonitoredItemQueueFactory secondQueues);
            using (firstQueues)
            using (secondQueues)
            {
                var source = new Mock<IAsyncNodeManager>();
                var other = new Mock<IAsyncNodeManager>();
                var foreign = new Mock<IAsyncNodeManager>();
                using MasterNodeManager firstMaster =
                    DeterministicServerMock.CreateMasterNodeManager(first, [source.Object, other.Object]);
                using MasterNodeManager secondMaster =
                    DeterministicServerMock.CreateMasterNodeManager(second, [foreign.Object]);
                Assert.That(MasterNodeManager.TryCaptureItemEmission(
                    first.Object, source.Object, null, out var admission), Is.True);
                using MasterNodeManager.SourceEmissionOrigin? origin = admission?.TransferToOrigin();
                ISystemContext context = origin!.CreateContext(first.Object.DefaultSystemContext);

                bool own = MasterNodeManager.TryCaptureSourceEmission(
                    first.Object, source.Object, out var ownAdmission, context: context);
                bool unrelated = MasterNodeManager.TryCaptureSourceEmission(
                    first.Object, other.Object, out var unrelatedAdmission, context: context);
                bool rebound = MasterNodeManager.TryCaptureSourceEmission(
                    first.Object, source.Object, out var reboundAdmission,
                    NodeManagerGenerationRegistry.GetOrCreate(other.Object).Reference, context: context);
                bool crossServer = MasterNodeManager.TryCaptureSourceEmission(
                    second.Object, secondMaster.CoreNodeManager!, out var foreignAdmission,
                    isGlobalServerEventSink: true, context: context);
                ownAdmission?.Dispose();
                unrelatedAdmission?.Dispose();
                reboundAdmission?.Dispose();
                foreignAdmission?.Dispose();

                Assert.Multiple(() =>
                {
                    Assert.That(own, Is.True);
                    Assert.That(unrelated, Is.False);
                    Assert.That(rebound, Is.False);
                    Assert.That(crossServer, Is.False);
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(source.Object).Gate.ActiveCount, Is.EqualTo(1));
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(other.Object).Gate.ActiveCount, Is.Zero);
                    Assert.That(NodeManagerGenerationRegistry.GetOrCreate(foreign.Object).Gate.ActiveCount, Is.Zero);
                });
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EventModifyFailuresRemainPerItemAndDoNotSkipOtherOwnersAsync(bool throws)
        {
            using var harness = new FanoutHarness(false);

            ArrayOf<ServiceResult> results = await harness.ModifyEventPairWithFailureAsync(throws)
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            harness.VerifySubscriptions(unsubscribe: false, count: 2);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DataModifyFailurePreservesCompletedItemsAndContinuesOtherOwnersAsync(bool completeFirst)
        {
            using var harness = new FanoutHarness(false);

            ArrayOf<ServiceResult> results = await harness.ModifyDataBatchWithFailureAsync(completeFirst)
                .ConfigureAwait(false);

            Assert.That(results[0].StatusCode,
                Is.EqualTo(completeFirst ? StatusCodes.Good : StatusCodes.BadUnexpectedError));
            Assert.That(results[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        public async Task DeletedRootNotifiersReleaseAllEventLinksBeforeReRegistrationAsync(
            bool serviceDelete,
            bool childRoot,
            bool retainOtherRoot)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                await using var manager = new TestableAsyncCustomNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    false, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                IEventMonitoredItem item = events.CreateMonitoredItem(
                    context, manager, null!, 1, new MonitoredItemIdFactory(), TimestampsToReturn.Both, 1000,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = ObjectIds.Server,
                            AttributeId = Attributes.EventNotifier
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters { QueueSize = 10 }
                    },
                    new EventFilter(), false);
                var parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId("parent", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("parent", manager.NamespaceIndex)
                };
                var root = new BaseObjectState(childRoot ? parent : null)
                {
                    NodeId = new NodeId("root", manager.NamespaceIndex),
                    BrowseName = new QualifiedName("root", manager.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                if (childRoot)
                {
                    parent.AddChild(root);
                }
                await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, childRoot ? parent : root)
                    .ConfigureAwait(false);
                await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
                if (retainOtherRoot)
                {
                    await AddRootAsync(manager, "other", true).ConfigureAwait(false);
                }
                MonitoredNode2 previous = manager.MonitoredNodes[root.NodeId];
                NodeId deletedId = childRoot ? parent.NodeId : root.NodeId;
                if (serviceDelete)
                {
                    ServiceResult deleted = await manager.DeleteNodeAsync(
                        context, new DeleteNodesItem { NodeId = deletedId }).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(deleted), Is.True);
                }
                else
                {
                    Assert.That(await manager.DeleteNodeAsync(manager.SystemContext, deletedId)
                        .ConfigureAwait(false), Is.True);
                }

                Assert.That(manager.MonitoredNodes.ContainsKey(root.NodeId), Is.False);
                Assert.That(manager.MonitoredItems.ContainsKey(item.Id), Is.EqualTo(retainOtherRoot));
                Assert.That(root.OnReportEventAsync, Is.Null);
                Assert.That(events.GetMonitoredItems(), Has.Count.EqualTo(1));

                BaseObjectState replacement = await AddRootAsync(manager, "root", true).ConfigureAwait(false);
                Assert.That(manager.MonitoredNodes[replacement.NodeId], Is.Not.SameAs(previous));
                Assert.That(manager.MonitoredNodes[replacement.NodeId].Node, Is.SameAs(replacement));
                Assert.That(
                    manager.MonitoredNodes[replacement.NodeId].EventMonitoredItems.ContainsKey(item.Id), Is.True);
                Assert.That(replacement.AreEventsMonitored, Is.True);

                await manager.SubscribeToAllEventsAsync(context, 1, item, true).ConfigureAwait(false);
                events.DeleteMonitoredItem(item.Id);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UnsupportedManagerDoesNotPreventServerEventCreationOrDeletionAsync(bool unsupportedFirst)
        {
            using var harness = new FanoutHarness(unsupportedFirst);

            await harness.CreateAsync().ConfigureAwait(false);

            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Item, Is.Not.Null);
            Assert.That(harness.Events.GetMonitoredItems(), Has.Count.EqualTo(1));
            harness.VerifySubscriptions(unsubscribe: false);

            ServiceResult deleted = await harness.DeleteAsync().ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GenuineManagerFailureRollsBackEveryAttemptedOwnerAsync(bool throws)
        {
            using var harness = new FanoutHarness(true, StatusCodes.BadServerNotConnected, throws);

            await harness.CreateAsync().ConfigureAwait(false);

            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
            Assert.That(harness.Item, Is.Null);
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: false);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [Test]
        public async Task GenuineUnsubscribeFailureIsReportedAfterOtherOwnersAreCleanedAsync()
        {
            using var harness = new FanoutHarness(true, cleanupFailure: StatusCodes.BadTimeout);
            await harness.CreateAsync().ConfigureAwait(false);
            Assert.That(harness.Result.StatusCode, Is.EqualTo(StatusCodes.Good));

            ServiceResult deleted = await harness.DeleteAsync().ConfigureAwait(false);

            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(harness.Events.GetMonitoredItems(), Is.Empty);
            harness.VerifySubscriptions(unsubscribe: true);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task NullSuccessRootCallbacksPreserveSubscriptionAndCleanupResultsAsync(
            bool samplingGroups,
            bool failSecondSubscription)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            await using (var manager = new NullSuccessRootNodeManager(
                server.Object, samplingGroups, failSecondSubscription))
            {
                ArrayOf<BaseObjectState> roots =
                [
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("First", manager.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    },
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("Second", manager.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }
                ];
                for (int ii = 0; ii < roots.Count; ii++)
                {
                    BaseObjectState root = roots[ii];
                    root.CreateAsPredefinedNode(manager.SystemContext);
                    await manager.RegisterRootAsync(root).ConfigureAwait(false);
                }

                var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.NodeId).Returns(ObjectIds.Server);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                IFilterTarget notification = Mock.Of<IFilterTarget>();
                item.Setup(value => value.QueueEvent(notification)).Callback(() => delivered.TrySetResult(true));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                try
                {
                    ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, false)
                        .ConfigureAwait(false);

                    Assert.That(result.StatusCode,
                        Is.EqualTo(failSecondSubscription ? StatusCodes.BadServerNotConnected : StatusCodes.Good));
                    Assert.That(manager.SubscriptionCalls, Is.EqualTo(2));
                    if (!failSecondSubscription)
                    {
                        await roots[0].ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        item.Verify(value => value.QueueEvent(notification), Times.Once);
                    }
                }
                finally
                {
                    ServiceResult cleanup = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, true)
                        .ConfigureAwait(false);
                    Assert.That(cleanup.StatusCode, Is.EqualTo(StatusCodes.Good));
                }

                Assert.That(manager.UnsubscriptionCalls, Is.EqualTo(2));
                Assert.That(manager.MonitoredItemCount, Is.Zero);
                foreach (BaseObjectState root in roots)
                {
                    Assert.That(root.AreEventsMonitored, Is.False);
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task MixedRootsDeliverSupportedEventsAndUnsubscribeWithoutChangingCapabilitiesAsync(
            bool samplingGroups,
            bool cleanupFailure)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            await using (var manager = new TestableAsyncCustomNodeManager(
                server.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 100,
                        AvailableSamplingRates = []
                    }
                },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                BaseObjectState supported = await AddRootAsync(manager, "Supported", true).ConfigureAwait(false);
                BaseObjectState unsupported = await AddRootAsync(manager, "Unsupported", false).ConfigureAwait(false);
                BaseObjectState other = await AddRootAsync(manager, "Other", true).ConfigureAwait(false);
                var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.NodeId).Returns(ObjectIds.Server);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                IFilterTarget notification = Mock.Of<IFilterTarget>();
                item.Setup(value => value.QueueEvent(notification)).Callback(() => delivered.TrySetResult(true));
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);

                ServiceResult result = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, false)
                    .ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(unsupported.EventNotifier, Is.EqualTo(EventNotifiers.None));
                Assert.That(manager.MonitoredNodes.ContainsKey(unsupported.NodeId), Is.False);
                Assert.That(manager.MonitoredNodes[supported.NodeId].EventMonitoredItems.ContainsKey(42), Is.True);
                Assert.That(manager.MonitoredNodes[other.NodeId].EventMonitoredItems.ContainsKey(42), Is.True);
                await supported.ReportEventAsync(manager.SystemContext, notification).ConfigureAwait(false);
                await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                item.Verify(value => value.QueueEvent(notification), Times.Once);

                int unsubscribed = 0;
                manager.EventSubscriptionCallback = (unsubscribe, _) =>
                {
                    if (unsubscribe && Interlocked.Increment(ref unsubscribed) == 1 && cleanupFailure)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerNotConnected);
                    }
                    return default;
                };
                ServiceResult deleted = await manager.SubscribeToAllEventsAsync(context, 1, item.Object, true)
                    .ConfigureAwait(false);
                Assert.That(deleted.StatusCode,
                    Is.EqualTo(cleanupFailure ? StatusCodes.BadServerNotConnected : StatusCodes.Good));
                Assert.That(unsubscribed, Is.EqualTo(2));
                Assert.That(manager.MonitoredNodes, Has.Count.Zero);
                Assert.That(manager.MonitoredItems, Is.Empty);
                Assert.That(supported.AreEventsMonitored, Is.False);
                Assert.That(other.AreEventsMonitored, Is.False);

                object handle = (await manager.GetManagerHandleAsync(unsupported.NodeId).ConfigureAwait(false))!;
                ServiceResult direct = await manager.SubscribeToEventsAsync(
                    context, handle, 1, item.Object, false).ConfigureAwait(false);
                Assert.That(direct.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            }
        }

        /// <summary>
        /// Retries a failed provider release without repeating releases that already succeeded.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task FailedAllEventsProviderReleaseIsRetriedForOriginalRootAsync(
            bool samplingGroups,
            bool multipleRoots)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            await using (var manager = new TestableAsyncCustomNodeManager(
                server.Object,
                new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 100,
                        AvailableSamplingRates = []
                    }
                },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                BaseObjectState root = await AddRootAsync(manager, "RetryRoot", true).ConfigureAwait(false);
                BaseObjectState? secondRoot = multipleRoots
                    ? await AddRootAsync(manager, "SecondRoot", true).ConfigureAwait(false)
                    : null;
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.Id).Returns(42);
                item.SetupGet(value => value.NodeId).Returns(ObjectIds.Server);
                item.SetupGet(value => value.MonitoringAllEvents).Returns(true);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                ServiceResult subscribed = await manager.SubscribeToAllEventsAsync(
                    context, 1, item.Object, false).ConfigureAwait(false);
                Assert.That(subscribed.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(manager.MonitoredNodes[root.NodeId].EventMonitoredItems[item.Object.Id],
                    Is.SameAs(item.Object));

                int releaseAttempts = 0;
                manager.EventSubscriptionCallback = (unsubscribe, _) =>
                {
                    if (unsubscribe && ++releaseAttempts == 1)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerNotConnected);
                    }
                    return default;
                };
                ServiceResult firstRelease = await manager.SubscribeToAllEventsAsync(
                    context, 1, item.Object, true).ConfigureAwait(false);
                Assert.That(firstRelease.StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
                Assert.That(releaseAttempts, Is.EqualTo(multipleRoots ? 2 : 1));
                AssertSourceLinkReleased(root);
                if (secondRoot is not null)
                {
                    AssertSourceLinkReleased(secondRoot);
                }

                ServiceResult retry = await manager.SubscribeToAllEventsAsync(
                    context, 1, item.Object, true).ConfigureAwait(false);
                Assert.That(retry.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(releaseAttempts, Is.EqualTo(multipleRoots ? 3 : 2),
                    "Only the failed provider release is retried after its native link is removed.");

                ServiceResult repeated = await manager.SubscribeToAllEventsAsync(
                    context, 1, item.Object, true).ConfigureAwait(false);
                Assert.That(repeated.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(releaseAttempts, Is.EqualTo(multipleRoots ? 3 : 2));

                void AssertSourceLinkReleased(BaseObjectState source)
                {
                    if (manager.MonitoredNodes.TryGetValue(source.NodeId, out MonitoredNode2? monitored))
                    {
                        Assert.That(monitored.EventMonitoredItems.ContainsKey(item.Object.Id), Is.False);
                    }
                    Assert.That(source.AreEventsMonitored, Is.False);
                }
            }
        }

        private static async Task<BaseObjectState> AddRootAsync(
            TestableAsyncCustomNodeManager manager,
            string name,
            bool supported)
        {
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, manager.NamespaceIndex),
                BrowseName = new QualifiedName(name, manager.NamespaceIndex),
                EventNotifier = supported ? EventNotifiers.SubscribeToEvents : EventNotifiers.None
            };
            root.CreateAsPredefinedNode(manager.SystemContext);
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, root).ConfigureAwait(false);
            await manager.AddRootNotifierPublicAsync(root).ConfigureAwait(false);
            return root;
        }

        private sealed class NullSuccessRootNodeManager : AsyncCustomNodeManager
        {
            public NullSuccessRootNodeManager(IServerInternal server, bool samplingGroups, bool failSecondSubscription)
                : base(
                    server,
                    new ApplicationConfiguration
                    {
                        ServerConfiguration = new ServerConfiguration
                        {
                            MaxNotificationQueueSize = 100,
                            MaxDurableNotificationQueueSize = 100,
                            AvailableSamplingRates = []
                        }
                    },
                    samplingGroups,
                    NullLogger.Instance,
                    DeterministicServerMock.TestNamespaceUri)
            {
                m_failSecondSubscription = failSecondSubscription;
            }

            public int SubscriptionCalls { get; private set; }

            public int UnsubscriptionCalls { get; private set; }

            /// <summary>
            /// Gets native membership after the event subscription operation completes.
            /// </summary>
            public int MonitoredItemCount => MonitoredItems.Count;

            public async ValueTask RegisterRootAsync(BaseObjectState root)
            {
                await AddNodeAsync(SystemContext, NodeId.Null, root).ConfigureAwait(false);
                await AddRootNotifierAsync(root).ConfigureAwait(false);
            }

            protected override async ValueTask<ServiceResult> SubscribeToEventsAsync(
                ServerSystemContext context,
                NodeState source,
                IEventMonitoredItem monitoredItem,
                bool unsubscribe,
                CancellationToken cancellationToken = default)
            {
                ServiceResult result = await base.SubscribeToEventsAsync(
                    context, source, monitoredItem, unsubscribe, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
                if (unsubscribe)
                {
                    UnsubscriptionCalls++;
                }
                else if (++SubscriptionCalls == 2 && m_failSecondSubscription)
                {
                    return StatusCodes.BadServerNotConnected;
                }
                return null!;
            }

            private readonly bool m_failSecondSubscription;
        }

        private sealed class FanoutHarness : IDisposable
        {
            public FanoutHarness(
                bool unsupportedFirst,
                StatusCode failure = default,
                bool throws = false,
                StatusCode cleanupFailure = default)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                Events = new EventManager(server.Object, 100, 100);
                server.SetupGet(value => value.EventManager).Returns(Events);
                var factory = new Mock<IMainNodeManagerFactory>();
                factory.Setup(value => value.CreateConfigurationNodeManager())
                    .Returns(new Mock<IConfigurationNodeManager>().Object);
                factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                    .Returns(new Mock<ICoreNodeManager>().Object);
                server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
                m_supported = CreateOwner(server.Object.NamespaceUris.GetString(0)!, StatusCodes.Good, cleanupFailure);
                m_unsupported = CreateOwner("urn:tests:unsupported-events",
                    StatusCodes.BadNotSupported, StatusCodes.BadNotSupported);
                m_terminal = CreateOwner("urn:tests:terminal-events", failure, StatusCodes.Good);
                if (throws)
                {
                    m_terminal.Setup(value => value.SubscribeToAllEventsAsync(
                            It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), false,
                            It.IsAny<CancellationToken>()))
                        .ThrowsAsync(new ServiceResultException(failure));
                }

                var source = new BaseObjectState(null)
                {
                    NodeId = ObjectIds.Server,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                m_supported.Setup(value => value.GetManagerHandleAsync(
                        ObjectIds.Server, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<object?>(source));
                m_supported.Setup(value => value.GetNodeMetadataAsync(
                        It.IsAny<OperationContext>(), It.IsAny<object>(),
                        It.IsAny<BrowseResultMask>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeMetadata?>(new NodeMetadata(source, source.NodeId)
                    {
                        NodeClass = NodeClass.Object,
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }));
                IAsyncNodeManager[] owners = unsupportedFirst
                    ? [m_unsupported.Object, m_supported.Object, m_terminal.Object]
                    : [m_supported.Object, m_terminal.Object, m_unsupported.Object];
                m_master = new MasterNodeManager(
                    server.Object,
                    new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                    null,
                    owners);
            }

            public EventManager Events { get; }

            public IMonitoredItem Item { get; private set; } = null!;

            public ServiceResult Result { get; private set; } = null!;

            public async Task CreateAsync()
            {
                var request = new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId
                    {
                        NodeId = ObjectIds.Server,
                        AttributeId = Attributes.EventNotifier
                    },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        QueueSize = 10,
                        Filter = new ExtensionObject(new EventFilter
                        {
                            SelectClauses =
                            [
                                new SimpleAttributeOperand
                                {
                                    TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                    AttributeId = Attributes.Value,
                                    BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                                }
                            ]
                        })
                    }
                };
                var errors = new ServiceResult[1];
                var items = new IMonitoredItem[1];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None);
                await m_master.CreateMonitoredItemsAsync(
                    context, 1, 1000, TimestampsToReturn.Both, [request], errors,
                    new MonitoringFilterResult[1], items, false).ConfigureAwait(false);
                Item = items[0];
                Result = errors[0];
            }

            public async Task<ServiceResult> DeleteAsync()
            {
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.DeleteMonitoredItems, RequestLifetime.None);
                var errors = new ServiceResult[1];
                await m_master.DeleteMonitoredItemsAsync(context, 1, [Item], errors).ConfigureAwait(false);
                return errors[0];
            }

            public async Task<ArrayOf<ServiceResult>> ModifyEventPairWithFailureAsync(bool throws)
            {
                await CreateAsync().ConfigureAwait(false);
                IMonitoredItem first = Item;
                await CreateAsync().ConfigureAwait(false);
                IMonitoredItem second = Item;
                Moq.Language.Flow.ISetup<IAsyncNodeManager, ValueTask<ServiceResult>> failing = m_terminal
                    .Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), 1,
                    It.Is<IEventMonitoredItem>(item => item.Id == first.Id), false,
                    It.IsAny<CancellationToken>()));
                if (throws)
                {
                    failing.ThrowsAsync(new ServiceResultException(StatusCodes.BadServerNotConnected));
                }
                else
                {
                    failing.Returns(new ValueTask<ServiceResult>(StatusCodes.BadServerNotConnected));
                }
                foreach (Mock<IAsyncNodeManager> owner in new[] { m_supported, m_unsupported, m_terminal })
                {
                    owner.Invocations.Clear();
                }
                var filter = new EventFilter
                {
                    SelectClauses =
                    [
                        new SimpleAttributeOperand
                        {
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            AttributeId = Attributes.Value,
                            BrowsePath = [new QualifiedName(BrowseNames.EventId)]
                        }
                    ]
                };
                var errors = new ServiceResult[2];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.ModifyMonitoredItems, RequestLifetime.None);
                await m_master.ModifyMonitoredItemsAsync(
                    context, TimestampsToReturn.Both, [first, second],
                    [CreateModify(first.Id, filter), CreateModify(second.Id, filter)],
                    errors, new MonitoringFilterResult[2]).ConfigureAwait(false);
                return errors;
            }

            public async Task<ArrayOf<ServiceResult>> ModifyDataBatchWithFailureAsync(bool completeFirst)
            {
                var first = new Mock<IMonitoredItem>();
                var second = new Mock<IMonitoredItem>();
                var third = new Mock<IMonitoredItem>();
                first.SetupGet(value => value.NodeManager).Returns(m_supported.Object);
                second.SetupGet(value => value.NodeManager).Returns(m_supported.Object);
                third.SetupGet(value => value.NodeManager).Returns(m_terminal.Object);
                foreach (Mock<IMonitoredItem> item in new[] { first, second, third })
                {
                    item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                }
                m_supported.Setup(value => value.ModifyMonitoredItemsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<TimestampsToReturn>(),
                        It.IsAny<IList<IMonitoredItem>>(), It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, TimestampsToReturn _, IList<IMonitoredItem> _,
                        ArrayOf<MonitoredItemModifyRequest> requests, IList<ServiceResult> errors,
                        IList<MonitoringFilterResult> _, CancellationToken _) =>
                    {
                        Assert.That(requests[2].Processed, Is.True);
                        if (completeFirst)
                        {
                            requests[0].Processed = true;
                            errors[0] = ServiceResult.Good;
                        }
                        throw new InvalidOperationException("Simulated owner failure.");
                    });
                m_terminal.Setup(value => value.ModifyMonitoredItemsAsync(
                        It.IsAny<OperationContext>(), It.IsAny<TimestampsToReturn>(),
                        It.IsAny<IList<IMonitoredItem>>(), It.IsAny<ArrayOf<MonitoredItemModifyRequest>>(),
                        It.IsAny<IList<ServiceResult>>(), It.IsAny<IList<MonitoringFilterResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, TimestampsToReturn _, IList<IMonitoredItem> _,
                        ArrayOf<MonitoredItemModifyRequest> requests, IList<ServiceResult> errors,
                        IList<MonitoringFilterResult> _, CancellationToken _) =>
                    {
                        Assert.That(requests[2].Processed, Is.False);
                        requests[2].Processed = true;
                        errors[2] = ServiceResult.Good;
                        return default;
                    });
                var results = new ServiceResult[3];
                using var context = new OperationContext(
                    new RequestHeader(), null, RequestType.ModifyMonitoredItems, RequestLifetime.None);
                await m_master.ModifyMonitoredItemsAsync(
                    context, TimestampsToReturn.Both, [first.Object, second.Object, third.Object],
                    [CreateModify(1), CreateModify(2), CreateModify(3)], results, new MonitoringFilterResult[3])
                    .ConfigureAwait(false);
                return results;
            }

            public void VerifySubscriptions(bool unsubscribe, int count = 1)
            {
                foreach (Mock<IAsyncNodeManager> owner in new[] { m_supported, m_unsupported, m_terminal })
                {
                    owner.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), unsubscribe,
                        CancellationToken.None), Times.Exactly(count));
                }
            }

            public void Dispose()
            {
                m_master.Dispose();
                Events.Dispose();
                m_queues.Dispose();
            }

            private static Mock<IAsyncNodeManager> CreateOwner(string uri, StatusCode subscribe, StatusCode unsubscribe)
            {
                var owner = new Mock<IAsyncNodeManager>();
                owner.SetupGet(value => value.NamespaceUris).Returns([uri]);
                owner.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), false,
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(new ServiceResult(subscribe)));
                owner.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), 1, It.IsAny<IEventMonitoredItem>(), true,
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(new ServiceResult(unsubscribe)));
                return owner;
            }

            private static MonitoredItemModifyRequest CreateModify(uint id, EventFilter? filter = null)
            {
                return new MonitoredItemModifyRequest
                {
                    MonitoredItemId = id,
                    RequestedParameters = new MonitoringParameters
                    {
                        QueueSize = 10,
                        SamplingInterval = 500,
                        Filter = filter == null ? ExtensionObject.Null : new ExtensionObject(filter)
                    }
                };
            }

            private readonly MasterNodeManager m_master;
            private readonly MonitoredItemQueueFactory m_queues;
            private readonly Mock<IAsyncNodeManager> m_supported;
            private readonly Mock<IAsyncNodeManager> m_unsupported;
            private readonly Mock<IAsyncNodeManager> m_terminal;
        }
    }
}
