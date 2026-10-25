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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies binding-record admission and retry through the generation reconciler.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class GenerationReconcilerTests
    {
        /// <summary>
        /// A failed committed bind preserves earlier successful records so recovery never replays their callbacks.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCommittedBindingPreservesSuccessfulRecordsForRetryAsync(bool cancel)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                var ids = new MonitoredItemIdFactory();
                CreateAllEventsItem(events, manager.Object, context, ids);
                CreateAllEventsItem(events, manager.Object, context, ids);
                IEventMonitoredItem? successfulItem = null;
                int subscriptions = 0;
                bool reject = true;
                manager.Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .Returns((OperationContext _, uint _, IEventMonitoredItem item, bool unsubscribe,
                        CancellationToken _) =>
                    {
                        if (!unsubscribe)
                        {
                            subscriptions++;
                            if (subscriptions == 1)
                            {
                                successfulItem = item;
                            }
                            else if (reject)
                            {
                                if (cancel)
                                {
                                    throw new OperationCanceledException();
                                }
                                throw new InvalidOperationException("The later binding failed.");
                            }
                        }
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    });

                await Assert.ThatAsync(
                    async () => await reconciler.BindAsync(generation, CancellationToken.None).ConfigureAwait(false),
                    cancel
                        ? Throws.TypeOf<OperationCanceledException>()
                        : Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

                Assert.That(successfulItem, Is.Not.Null);
                Assert.That(generation.EventBindings.ContainsKey(successfulItem!), Is.True);
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), successfulItem!, true,
                    It.IsAny<CancellationToken>()), Times.Never);
                reject = false;
                GenerationReconciler.Bindings recovered = await reconciler.BindAsync(
                    generation, CancellationToken.None).ConfigureAwait(false);

                Assert.That(recovered.EventMonitoredItems, Has.Count.EqualTo(2));
                Assert.That(subscriptions, Is.EqualTo(3), "Only the failed binding may repeat.");
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), successfulItem!, false,
                    It.IsAny<CancellationToken>()), Times.Once);
                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);
                Assert.That(generation.EventBindings, Has.Count.EqualTo(2));
                foreach (GenerationEventBindingRecord record in generation.EventBindings.Values)
                {
                    Assert.That(record.State, Is.EqualTo(GenerationEventBindingState.Removed));
                }
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), successfulItem!, true,
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Retirement rejects new bindings while keeping previously admitted records available for release.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SealingRefusesNewBindingsAndPreservesAdmittedReleaseAsync(bool admitBeforeSeal)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                var item = new Mock<IEventMonitoredItem>();
                GenerationEventBindingRecord? admitted = admitBeforeSeal
                    ? generation.GetEventBinding(item.Object, unsubscribe: false)
                    : null;
                Assert.That(generation.TrySealEventBindings(), Is.True);
                Assert.That(generation.TrySealEventBindings(), Is.False);
                Assert.That(generation.GetEventBinding(item.Object, unsubscribe: false), Is.Null);
                Assert.That(generation.GetEventBinding(item.Object, unsubscribe: true), Is.SameAs(admitted));

                int callbacks = 0;
                ServiceResult result = await reconciler.ChangeAllEventsSubscriptionAsync(
                    generation,
                    item.Object,
                    (_, _) =>
                    {
                        callbacks++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    },
                    unsubscribe: false,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(callbacks, Is.Zero);
                Assert.That(generation.EventBindings, Has.Count.EqualTo(admitBeforeSeal ? 1 : 0));
            }
        }

        /// <summary>
        /// A failed post-bind release cannot turn a deleted item's record back into a subscribable binding.
        /// Retirement retries the exact release once and never replays a successful release.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedDeletedItemReleaseCannotRebindAndRetirementRetriesAsync(bool throwOnRelease)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), It.IsAny<IEventMonitoredItem>(),
                    true, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                var item = new Mock<IEventMonitoredItem>();
                item.SetupGet(value => value.MonitoringAllEvents).Returns(true);
                int subscriptions = 0;
                int releases = 0;
                ValueTask<ServiceResult> DispatchAsync(bool unsubscribe, CancellationToken cancellationToken)
                {
                    if (!unsubscribe)
                    {
                        subscriptions++;
                        return new ValueTask<ServiceResult>(ServiceResult.Good);
                    }
                    releases++;
                    if (throwOnRelease)
                    {
                        throw new InvalidOperationException("Release failed.");
                    }
                    return new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadCommunicationError));
                }

                await Assert.ThatAsync(
                    async () => await reconciler.ChangeAllEventsSubscriptionAsync(
                        generation, item.Object, DispatchAsync, false, CancellationToken.None).ConfigureAwait(false),
                    throwOnRelease
                        ? Throws.TypeOf<InvalidOperationException>()
                        : Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                Assert.That(subscriptions, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));
                GenerationEventBindingRecord record = generation.EventBindings[item.Object];
                Assert.That(record.RemovalRequested, Is.True);
                Assert.That(record.State, Is.EqualTo(GenerationEventBindingState.Bound));

                ServiceResult result = await reconciler.ChangeAllEventsSubscriptionAsync(
                    generation, item.Object, DispatchAsync, false, CancellationToken.None).ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(subscriptions, Is.EqualTo(1));
                Assert.That(releases, Is.EqualTo(1));

                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);
                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);
                Assert.That(record.State, Is.EqualTo(GenerationEventBindingState.Removed));
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), item.Object, true,
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Reconciliation joins an initial bind once, while modification dispatches a new callback.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConcurrentReconciliationBindsOnceButModificationRefreshesAsync(bool bindInFlight)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var reconciler = new GenerationReconciler(server.Object);
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, Mock.Of<IAsyncNodeManager>(), NodeManagerGenerationState.Live);
                IEventMonitoredItem item = CreateAllEventsItem(events, generation.NodeManager, context);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int subscriptions = 0;
                int unsubscriptions = 0;
                async ValueTask<ServiceResult> DispatchAsync(bool unsubscribe, CancellationToken cancellationToken)
                {
                    if (unsubscribe)
                    {
                        unsubscriptions++;
                    }
                    else
                    {
                        subscriptions++;
                        entered.TrySetResult(true);
                        if (bindInFlight)
                        {
                            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    return ServiceResult.Good;
                }

                Task<ServiceResult> first = reconciler.ChangeAllEventsSubscriptionAsync(
                    generation, item, DispatchAsync, false, timeout.Token, refreshExisting: false).AsTask();
                try
                {
                    await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                    Task<ServiceResult> second = reconciler.ChangeAllEventsSubscriptionAsync(
                        generation, item, DispatchAsync, false, timeout.Token, refreshExisting: false).AsTask();
                    if (bindInFlight)
                    {
                        Assert.That(first.IsCompleted, Is.False);
                        Assert.That(second.IsCompleted, Is.False);
                    }
                    release.TrySetResult(true);
                    Assert.That((await first.ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That((await second.ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(subscriptions, Is.EqualTo(1));
                    Assert.That(unsubscriptions, Is.Zero);

                    ServiceResult modified = await reconciler.ChangeAllEventsSubscriptionAsync(
                        generation, item, DispatchAsync, false, timeout.Token).ConfigureAwait(false);
                    Assert.That(modified.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(subscriptions, Is.EqualTo(2));
                    ServiceResult removed = await reconciler.ChangeAllEventsSubscriptionAsync(
                        generation, item, DispatchAsync, true, timeout.Token).ConfigureAwait(false);
                    Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(unsubscriptions, Is.EqualTo(1));
                    Assert.That(generation.EventBindings, Is.Empty);
                }
                finally
                {
                    release.TrySetResult(true);
                    await first.ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// An unsupported all-events owner does not abort generation preparation or duplicate its callback.
        /// </summary>
        [Test]
        public async Task UnsupportedAllEventsOwnerDoesNotAbortPreparationAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), It.IsAny<IEventMonitoredItem>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(new ServiceResult(StatusCodes.BadNotSupported)));
                IEventMonitoredItem item = CreateAllEventsItem(events, manager.Object, context);
                NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(manager.Object);

                GenerationReconciler.Bindings bindings = await reconciler.BindAsync(
                    generation, CancellationToken.None).ConfigureAwait(false);
                Assert.That(bindings.EventMonitoredItems, Is.Empty);
                Assert.That(generation.EventBindings.ContainsKey(item), Is.True);
                await reconciler.BindAsync(generation, CancellationToken.None).ConfigureAwait(false);
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), item, false,
                    It.IsAny<CancellationToken>()), Times.Once);
                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);
                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), item, true,
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Partial-binding rollback uses the exact record and accepts an unsupported release without retrying it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PartialBindingRollbackRemovesItsRecordExactlyOnceAsync(bool unsupportedRelease)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var events = new EventManager(server.Object, 100, 100))
            using (var context = new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None))
            {
                server.SetupGet(value => value.EventManager).Returns(events);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                manager.Setup(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), It.IsAny<IEventMonitoredItem>(),
                    true, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(unsupportedRelease
                        ? new ServiceResult(StatusCodes.BadNotSupported)
                        : ServiceResult.Good));
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Live);
                IEventMonitoredItem item = CreateAllEventsItem(events, manager.Object, context);
                await reconciler.ChangeAllEventsSubscriptionAsync(
                    generation, item,
                    (_, _) => new ValueTask<ServiceResult>(ServiceResult.Good),
                    false, CancellationToken.None).ConfigureAwait(false);
                GenerationEventBindingRecord record = generation.EventBindings[item];
                var bindings = new GenerationReconciler.Bindings();
                bindings.EventMonitoredItems.Add(item.Id, item);

                await reconciler.UnbindAsync(generation, bindings, CancellationToken.None).ConfigureAwait(false);
                await reconciler.ReleaseRetiredEventsAsync(generation, CancellationToken.None).ConfigureAwait(false);

                Assert.That(record.State, Is.EqualTo(GenerationEventBindingState.Removed));
                Assert.That(generation.EventBindings, Is.Empty);
                manager.Verify(value => value.SubscribeToAllEventsAsync(
                    It.IsAny<OperationContext>(), It.IsAny<uint>(), item, true,
                    It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Retirement releases its exact session records even after sessions leave the server registry.
        /// </summary>
        [Test]
        public async Task RetirementClosesOnlyRecordedSessionsIncludingRemovedSessionsAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var sessions = new Mock<ISessionManager>();
                sessions.Setup(value => value.GetSessions()).Returns([]);
                server.SetupGet(value => value.SessionManager).Returns(sessions.Object);
                var reconciler = new GenerationReconciler(server.Object);
                var manager = new Mock<IAsyncNodeManager>();
                var generation = new NodeManagerGeneration(
                    new NodeManagerSlot(), 1, manager.Object, NodeManagerGenerationState.Retiring);
                var session = new Mock<ISession>();
                var sessionId = new NodeId("RemovedSession", 2);
                var identity = new UserIdentity();
                session.SetupGet(value => value.Id).Returns(sessionId);
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
                generation.SessionBindings[sessionId] = new GenerationSessionBindingRecord
                {
                    Binding = new GenerationReconciler.SessionBinding(session.Object, identity, generation.Reference)
                };

                await reconciler.UnbindRetiredAsync(
                    generation, false, CancellationToken.None).ConfigureAwait(false);
                await reconciler.UnbindRetiredAsync(
                    generation, false, CancellationToken.None).ConfigureAwait(false);

                Assert.That(generation.SessionBindings, Is.Empty);
                manager.Verify(value => value.SessionClosingAsync(
                    It.Is<OperationContext>(context => ReferenceEquals(context.Session, session.Object)),
                    sessionId, false, It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Creates a live all-events item in the deterministic server's event manager.
        /// </summary>
        private static IEventMonitoredItem CreateAllEventsItem(
            EventManager events,
            IAsyncNodeManager manager,
            OperationContext context,
            MonitoredItemIdFactory? ids = null)
        {
            return events.CreateMonitoredItem(
                context, manager, null!, 1, ids ?? new MonitoredItemIdFactory(), TimestampsToReturn.Both, 1000,
                new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = ObjectIds.Server, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 1,
                        SamplingInterval = 0,
                        QueueSize = 10,
                        DiscardOldest = true
                    }
                },
                new EventFilter(), createDurable: false);
        }
    }
}
