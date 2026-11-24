// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class GenerationReconcilerTests
    {
        /// <summary>
        /// Releasing either owner preserves the other owner's physical link to the same Server event item.
        /// </summary>
        /// <remarks>
        /// The mocked master isolates physical subscriptions and does not exercise source admission.
        /// </remarks>
        /// <param name="samplingGroups">Whether the raw manager uses sampling groups.</param>
        /// <param name="facadeFirst">Whether the facade releases its binding before the raw owner.</param>
        /// <param name="failFirstRelease">Whether the first release fails after reaching the raw manager.</param>
        /// <param name="dynamicRoot">Whether a root notifier is added, removed, and added again after binding.</param>
        /// <param name="replaceOperationContext">Whether the facade creates a new context for the raw manager.</param>
        [TestCase(false, false, false, false, false)]
        [TestCase(false, false, true, false, false)]
        [TestCase(false, true, false, false, false)]
        [TestCase(false, true, true, false, false)]
        [TestCase(true, false, false, false, false)]
        [TestCase(true, false, true, false, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(true, true, true, false, false)]
        [TestCase(false, false, false, true, false)]
        [TestCase(false, false, true, true, false)]
        [TestCase(false, true, false, true, false)]
        [TestCase(false, true, true, true, false)]
        [TestCase(true, false, false, true, false)]
        [TestCase(true, false, true, true, false)]
        [TestCase(true, true, false, true, false)]
        [TestCase(true, true, true, true, false)]
        [TestCase(false, false, false, false, true)]
        [TestCase(false, true, false, false, true)]
        [TestCase(true, false, false, false, true)]
        [TestCase(true, true, false, false, true)]
        [TestCase(false, false, true, true, true)]
        [TestCase(false, true, true, true, true)]
        [TestCase(true, false, true, true, true)]
        [TestCase(true, true, true, true, true)]
        public async Task RetiringOwnerPreservesSharedPhysicalEventBindingAsync(
            bool samplingGroups,
            bool facadeFirst,
            bool failFirstRelease,
            bool dynamicRoot,
            bool replaceOperationContext)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var resources = new ResourceManager(new ApplicationConfiguration()))
            using (var events = new EventManager(server.Object, 100, 100))
            await using (var raw = new TestableAsyncCustomNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                server.SetupGet(value => value.ResourceManager).Returns(resources);
                server.SetupGet(value => value.EventManager).Returns(events);
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId("physical-binding-session", 2));
                session.SetupGet(value => value.Activated).Returns(true);
                session.SetupGet(value => value.EndpointDescription)
                    .Returns(new EndpointDescription { SecurityMode = MessageSecurityMode.None });
                var identity = new UserIdentity();
                session.SetupGet(value => value.Identity).Returns(identity);
                session.SetupGet(value => value.IdentityToken).Returns(identity.TokenHandler);
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
                Assert.That(NotificationAuthorization.Capture(session.Object), Is.Not.Null);
                using var context = new OperationContext(session.Object, DiagnosticsMasks.None);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("physical-binding-root", raw.NamespaceIndex),
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                raw.AddRootNotifierSynchronously(root);
                var filter = new EventFilter();
                filter.AddSelectClause(ObjectTypeIds.BaseEventType, QualifiedName.From(BrowseNames.Message));
                IEventMonitoredItem item = events.CreateMonitoredItem(
                    context,
                    raw,
                    null!,
                    1,
                    new MonitoredItemIdFactory(),
                    TimestampsToReturn.Both,
                    1000,
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
                    filter,
                    createDurable: false);
                var subscription = new Mock<ISubscription>();
                subscription.SetupGet(value => value.Session).Returns(session.Object);
                var native = (MonitoredItem)item;
                native.SubscriptionCallback = subscription.Object;
                var binding = (IMonitoredItemGenerationBinding)native;
                binding.BindSubscription(subscription.Object);
                var facade = new Mock<IAsyncNodeManager>();
                bool failFacade = facadeFirst && failFirstRelease;
                int replacedContexts = 0;
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager).Returns(raw.SyncNodeManager);
                facade.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<uint>(),
                        It.IsAny<IEventMonitoredItem>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (OperationContext operation, uint subscriptionId, IEventMonitoredItem target,
                        bool unsubscribe, CancellationToken token) =>
                    {
                        using var forwardedContext = replaceOperationContext
                            ? new OperationContext(session.Object, operation.DiagnosticsMask)
                            : null;
                        if (forwardedContext is not null)
                        {
                            Assert.That(forwardedContext, Is.Not.SameAs(operation));
                            Assert.That(forwardedContext.Session, Is.SameAs(operation.Session));
                            Assert.That(forwardedContext.UserIdentity, Is.SameAs(operation.UserIdentity));
                            replacedContexts++;
                        }
                        ServiceResult result = await raw.SubscribeToAllEventsAsync(
                            forwardedContext ?? operation, subscriptionId, target, unsubscribe, token)
                            .ConfigureAwait(false);
                        if (unsubscribe && failFacade)
                        {
                            failFacade = false;
                            return new ServiceResult(StatusCodes.BadUnexpectedError);
                        }
                        return result;
                    });
                var routing = new NodeManagerRoutingTable();
                routing.Add(raw, [raw.NamespaceIndex]);
                NodeManagerGeneration original = routing.Revision.GetGeneration(raw);
                var reconciler = new GenerationReconciler(server.Object);
                int releaseAttempts = 0;
                BaseObjectState? addedRoot = null;

                try
                {
                    Assert.That(native.Generation!.TryGetGeneration(out var issuer), Is.True);
                    Assert.That(issuer, Is.SameAs(original));
                    await reconciler.BindAsync(original, CancellationToken.None).ConfigureAwait(false);
                    routing.Add(facade.Object, [raw.NamespaceIndex]);
                    NodeManagerGeneration forwarded = routing.Revision.GetGeneration(facade.Object);
                    Assert.That(forwarded, Is.Not.SameAs(original));
                    Assert.That(forwarded.ResourceOwnership, Is.SameAs(original.ResourceOwnership));
                    await reconciler.BindAsync(forwarded, CancellationToken.None).ConfigureAwait(false);
                    NodeManagerGeneration retiring = facadeFirst ? forwarded : original;
                    NodeManagerGeneration remaining = facadeFirst ? original : forwarded;
                    if (dynamicRoot)
                    {
                        addedRoot = new BaseObjectState(null)
                        {
                            NodeId = new NodeId("added-physical-binding-root", raw.NamespaceIndex),
                            BrowseName = new QualifiedName("added-physical-binding-root", raw.NamespaceIndex),
                            EventNotifier = EventNotifiers.SubscribeToEvents
                        };
                        await raw.AddNodeAsync(raw.SystemContext, NodeId.Null, addedRoot).ConfigureAwait(false);
                        await raw.AddRootNotifierPublicAsync(addedRoot).ConfigureAwait(false);
                        AssertPhysicalLink(addedRoot);
                        await raw.RemoveRootNotifierPublicAsync(addedRoot).ConfigureAwait(false);
                        Assert.That(raw.MonitoredNodes.ContainsKey(addedRoot.NodeId), Is.False);
                        Assert.That(addedRoot.AreEventsMonitored, Is.False);
                        AssertPhysicalLink(root);
                        await raw.AddRootNotifierPublicAsync(addedRoot).ConfigureAwait(false);
                    }

                    raw.EventSubscriptionCallback = (unsubscribe, _) =>
                    {
                        if (unsubscribe && ++releaseAttempts == 1 && failFirstRelease && !facadeFirst)
                        {
                            throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                        }
                        return default;
                    };
                    AssertPhysicalLinks();
                    await AssertPhysicallyDeliveredAsync("before release", root).ConfigureAwait(false);
                    if (addedRoot is not null)
                    {
                        await AssertPhysicallyDeliveredAsync("added root before release", addedRoot)
                            .ConfigureAwait(false);
                    }

                    routing.Remove(facadeFirst ? facade.Object : raw);
                    Assert.That(retiring.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                    Assert.That(remaining.Gate.State, Is.EqualTo(NodeManagerGenerationState.Live));
                    if (failFirstRelease)
                    {
                        await Assert.ThatAsync(
                            async () => await reconciler.UnbindRetiredAsync(
                                retiring, true, CancellationToken.None).ConfigureAwait(false),
                            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                        AssertPhysicalLinks();
                        await AssertPhysicallyDeliveredAsync("after failed release", root).ConfigureAwait(false);
                        if (addedRoot is not null)
                        {
                            await AssertPhysicallyDeliveredAsync("added root after failed release", addedRoot)
                                .ConfigureAwait(false);
                        }
                    }

                    await reconciler.UnbindRetiredAsync(
                        retiring, true, CancellationToken.None).ConfigureAwait(false);
                    AssertPhysicalLinks();
                    await AssertPhysicallyDeliveredAsync("after first release", root).ConfigureAwait(false);
                    if (addedRoot is not null)
                    {
                        await AssertPhysicallyDeliveredAsync("added root after first release", addedRoot)
                            .ConfigureAwait(false);
                    }
                    facade.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), item.SubscriptionId, item, true,
                        It.IsAny<CancellationToken>()),
                        Times.Exactly(facadeFirst ? (failFirstRelease ? 2 : 1) : 0));

                    routing.Remove(facadeFirst ? raw : facade.Object);
                    Assert.That(remaining.Gate.State, Is.EqualTo(NodeManagerGenerationState.Retiring));
                    await reconciler.UnbindRetiredAsync(
                        remaining, true, CancellationToken.None).ConfigureAwait(false);
                    Assert.That(raw.MonitoredNodes.ContainsKey(root.NodeId), Is.False);
                    Assert.That(root.AreEventsMonitored, Is.False);
                    if (addedRoot is not null)
                    {
                        Assert.That(raw.MonitoredNodes.ContainsKey(addedRoot.NodeId), Is.False);
                        Assert.That(addedRoot.AreEventsMonitored, Is.False);
                    }
                    facade.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), item.SubscriptionId, item, false,
                        It.IsAny<CancellationToken>()), Times.Once);
                    facade.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), item.SubscriptionId, item, true,
                        It.IsAny<CancellationToken>()),
                        Times.Exactly(facadeFirst && failFirstRelease ? 2 : 1));
                    Assert.That(releaseAttempts, Is.EqualTo((dynamicRoot ? 2 : 1) * (failFirstRelease ? 3 : 2)));
                    Assert.That(replacedContexts, Is.EqualTo(
                        replaceOperationContext ? (facadeFirst && failFirstRelease ? 3 : 2) : 0));
                }
                finally
                {
                    raw.EventSubscriptionCallback = (_, _) => default;
                    binding.UnbindSubscription(subscription.Object);
                    events.DeleteMonitoredItem(item.Id);
                }

                void AssertPhysicalLinks()
                {
                    AssertPhysicalLink(root);
                    if (addedRoot is not null)
                    {
                        AssertPhysicalLink(addedRoot);
                    }
                }

                void AssertPhysicalLink(BaseObjectState source)
                {
                    Assert.That(raw.MonitoredNodes.TryGetValue(source.NodeId, out MonitoredNode2? monitored), Is.True);
                    Assert.That(monitored!.EventMonitoredItems.TryGetValue(
                        item.Id, out IEventMonitoredItem? actual), Is.True);
                    Assert.That(actual, Is.SameAs(item));
                    Assert.That(source.AreEventsMonitored, Is.True);
                }

                async Task AssertPhysicallyDeliveredAsync(string message, BaseObjectState source)
                {
                    var occurrence = new BaseEventState(null);
                    occurrence.Initialize(
                        raw.SystemContext, source, EventSeverity.Medium, new LocalizedText(message));
                    ServiceResult permission = await raw.ValidateEventRolePermissionsAsync(item, occurrence)
                        .ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(permission), Is.True);
                    await source.ReportEventAsync(raw.SystemContext, occurrence).ConfigureAwait(false);
                    await raw.MonitoredNodes[source.NodeId].WaitForPendingNotificationsAsync(CancellationToken.None)
                        .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    var notifications = new Queue<QueuedEvent>();
                    _ = native.Publish(context, notifications, 10);
                    Assert.That(notifications, Has.Count.EqualTo(1));
                    Assert.That(notifications.Dequeue().EventFields[0].TryGetValue(out LocalizedText actual), Is.True);
                    Assert.That(actual.Text, Is.EqualTo(message));
                }
            }
        }

        /// <summary>
        /// A failed facade bind cleans up its raw subscriptions.
        /// An unsupported binding releases its subscriptions at retirement.
        /// </summary>
        /// <param name="samplingGroups">Whether the raw manager uses sampling groups.</param>
        /// <param name="unsupported">Whether the facade reports an unsupported binding.</param>
        /// <param name="failOnSecondRoot">Whether the second subscription attempt fails.</param>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        public async Task AttemptedFacadeBindingReleasesPhysicalRootsAsync(
            bool samplingGroups,
            bool unsupported,
            bool failOnSecondRoot)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var resources = new ResourceManager(new ApplicationConfiguration()))
            using (var events = new EventManager(server.Object, 100, 100))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None))
            await using (var raw = new TestableAsyncCustomNodeManager(
                server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() },
                samplingGroups,
                NullLogger.Instance,
                DeterministicServerMock.TestNamespaceUri))
            {
                server.SetupGet(value => value.ResourceManager).Returns(resources);
                server.SetupGet(value => value.EventManager).Returns(events);
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                BaseObjectState[] roots =
                [
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("attempted-first-root", raw.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    },
                    new BaseObjectState(null)
                    {
                        NodeId = new NodeId("attempted-second-root", raw.NamespaceIndex),
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    }
                ];
                foreach (BaseObjectState root in roots)
                {
                    raw.AddRootNotifierSynchronously(root);
                }

                IEventMonitoredItem item = CreateAllEventsItem(events, raw, context);
                int subscriptions = 0;
                int releases = 0;
                int observedRoots = 0;
                raw.EventSubscriptionCallback = (unsubscribe, _) =>
                {
                    if (unsubscribe)
                    {
                        releases++;
                    }
                    else if (++subscriptions == 2 && failOnSecondRoot)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerNotConnected);
                    }
                    return default;
                };
                var facade = new Mock<IAsyncNodeManager>();
                facade.SetupGet(value => value.NamespaceUris).Returns(Array.Empty<string>());
                facade.SetupGet(value => value.SyncNodeManager).Returns(raw.SyncNodeManager);
                facade.Setup(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<uint>(),
                        It.IsAny<IEventMonitoredItem>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (OperationContext operation, uint subscriptionId, IEventMonitoredItem target,
                        bool unsubscribe, CancellationToken token) =>
                    {
                        try
                        {
                            ServiceResult result = await raw.SubscribeToAllEventsAsync(
                                operation, subscriptionId, target, unsubscribe, token).ConfigureAwait(false);
                            if (!unsubscribe && ServiceResult.IsGood(result))
                            {
                                observedRoots = CountLinkedRoots();
                                return new ServiceResult(unsupported
                                    ? StatusCodes.BadNotSupported
                                    : StatusCodes.BadServerNotConnected);
                            }
                            return result;
                        }
                        catch (ServiceResultException) when (!unsubscribe && failOnSecondRoot)
                        {
                            observedRoots = CountLinkedRoots();
                            throw;
                        }
                    });
                NodeManagerGeneration original = NodeManagerGenerationRegistry.GetOrCreate(raw);
                var routing = new NodeManagerRoutingTable();
                routing.Add(facade.Object, [1]);
                NodeManagerGeneration generation = routing.Revision.GetGeneration(facade.Object);
                Assert.That(generation, Is.Not.SameAs(original));
                Assert.That(generation.ResourceOwnership, Is.SameAs(original.ResourceOwnership));
                var reconciler = new GenerationReconciler(server.Object);
                try
                {
                    if (unsupported)
                    {
                        GenerationReconciler.Bindings bindings = await reconciler.BindAsync(
                            generation, CancellationToken.None).ConfigureAwait(false);
                        Assert.That(bindings.EventMonitoredItems, Is.Empty);
                        Assert.That(generation.EventBindings.TryGetValue(
                            item, out GenerationEventBindingRecord? retained), Is.True);
                        Assert.That(retained!.State, Is.EqualTo(GenerationEventBindingState.Bound));
                        Assert.That(CountLinkedRoots(), Is.EqualTo(2));
                        facade.Verify(value => value.SubscribeToAllEventsAsync(
                            It.IsAny<OperationContext>(), item.SubscriptionId, item, true,
                            It.IsAny<CancellationToken>()), Times.Never);
                    }
                    else
                    {
                        await Assert.ThatAsync(
                            async () => await reconciler.BindAsync(generation, CancellationToken.None)
                                .ConfigureAwait(false),
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode))
                                .EqualTo(StatusCodes.BadServerNotConnected)).ConfigureAwait(false);
                        facade.Verify(value => value.SubscribeToAllEventsAsync(
                            It.IsAny<OperationContext>(), item.SubscriptionId, item, true,
                            It.IsAny<CancellationToken>()), Times.Once);
                        Assert.That(CountLinkedRoots(), Is.Zero);
                        Assert.That(raw.MonitoredItems.ContainsKey(item.Id), Is.False);
                    }

                    Assert.That(subscriptions, Is.EqualTo(2));
                    Assert.That(observedRoots, Is.EqualTo(failOnSecondRoot ? 1 : 2));
                    routing.Remove(facade.Object);
                    await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None)
                        .ConfigureAwait(false);
                    await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None)
                        .ConfigureAwait(false);
                    facade.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), item.SubscriptionId, item, false,
                        It.IsAny<CancellationToken>()), Times.Once);
                    facade.Verify(value => value.SubscribeToAllEventsAsync(
                        It.IsAny<OperationContext>(), item.SubscriptionId, item, true,
                        It.IsAny<CancellationToken>()), Times.Once);
                    Assert.That(releases, Is.EqualTo(2));
                    Assert.That(CountLinkedRoots(), Is.Zero);
                    Assert.That(raw.MonitoredItems.ContainsKey(item.Id), Is.False);
                    foreach (BaseObjectState root in roots)
                    {
                        Assert.That(root.AreEventsMonitored, Is.False);
                    }
                }
                finally
                {
                    raw.EventSubscriptionCallback = static (_, _) => default;
                    await raw.SubscribeToAllEventsAsync(
                        context, item.SubscriptionId, item, true, CancellationToken.None).ConfigureAwait(false);
                    events.DeleteMonitoredItem(item.Id);
                }

                int CountLinkedRoots()
                {
                    int count = 0;
                    foreach (BaseObjectState root in roots)
                    {
                        if (raw.MonitoredNodes.TryGetValue(root.NodeId, out MonitoredNode2? monitored) &&
                            monitored.EventMonitoredItems.TryGetValue(item.Id, out IEventMonitoredItem? actual) &&
                            ReferenceEquals(actual, item))
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }
    }
}
