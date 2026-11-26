// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The lifecycle state of one node-manager generation.
    /// </summary>
    internal enum NodeManagerGenerationState
    {
        Preparing,
        Prepared,
        Live,
        Retiring,
        Disposed,
        Aborted
    }

    /// <summary>
    /// The stable identity shared by every generation of one registered node manager.
    /// </summary>
    public sealed class NodeManagerSlot
    {
        /// <summary>
        /// Initializes a new slot.
        /// </summary>
        internal NodeManagerSlot()
        {
            m_id = Guid.NewGuid();
        }

        /// <summary>
        /// Gets the stable identifier of this slot.
        /// </summary>
        public Guid Id
        {
            get
            {
                lock (m_idLock)
                {
                    return m_id;
                }
            }
        }

        /// <summary>
        /// Gets one consistent snapshot of the currently published generation.
        /// </summary>
        /// <param name="snapshot">The current slot and generation state when present.</param>
        /// <returns><see langword="true"/> when the slot has a current generation.</returns>
        public bool TryGetCurrent(out NodeManagerSlotSnapshot snapshot)
        {
            NodeManagerGeneration? current = Current;
            if (current is null)
            {
                snapshot = default;
                return false;
            }
            snapshot = new NodeManagerSlotSnapshot(this, current.Number, current.NodeManager);
            return true;
        }

        /// <summary>
        /// Gets the subscription-owned items awaiting a compatible node in this slot.
        /// </summary>
        internal ConcurrentDictionary<IMonitoredItem, ISubscription> UnresolvedItems { get; } =
            new(MonitoredItemReferenceComparer.Instance);

        /// <summary>
        /// Gets the currently published generation.
        /// </summary>
        internal NodeManagerGeneration? Current => Volatile.Read(ref m_current);

        /// <summary>
        /// Gets or sets the committed migration policy revision, advanced by removal or invalidation.
        /// The lifecycle writer serializes this revision with target selection and publication.
        /// </summary>
        internal long LateMigrationRevision { get; set; }

        /// <summary>
        /// Associates the public registration identifier with the slot once.
        /// </summary>
        /// <param name="id">The identifier assigned to the registration.</param>
        internal void SetRegistrationId(Guid id)
        {
            lock (m_idLock)
            {
                if (!m_registrationIdSet)
                {
                    m_id = id;
                    m_registrationIdSet = true;
                }
            }
        }

        /// <summary>
        /// Publishes the active generation for this slot.
        /// </summary>
        /// <param name="generation">The generation becoming current.</param>
        internal void Publish(NodeManagerGeneration generation)
        {
            Volatile.Write(ref m_current, generation);
        }

        /// <summary>
        /// Clears the current generation only when it still matches the generation being retired.
        /// </summary>
        /// <param name="generation">The generation being removed from this slot.</param>
        internal void Unpublish(NodeManagerGeneration generation)
        {
            _ = Interlocked.CompareExchange(ref m_current, null, generation);
        }

        /// <summary>The current generation published for this slot.</summary>
        private NodeManagerGeneration? m_current;

        /// <summary>Protects the one-time public registration identifier assignment.</summary>
        private readonly Lock m_idLock = new();

        /// <summary>The stable slot identifier.</summary>
        private Guid m_id;

        /// <summary>Whether the public registration identifier has been assigned.</summary>
        private bool m_registrationIdSet;
    }

    /// <summary>
    /// A consistent snapshot of one stable NodeManager slot and its current generation.
    /// </summary>
    /// <remarks>
    /// A non-default snapshot retains its NodeManager even after the slot changes or is unpublished.
    /// </remarks>
    public readonly record struct NodeManagerSlotSnapshot
    {
        /// <summary>
        /// Creates a snapshot.
        /// </summary>
        /// <param name="slot">The stable slot.</param>
        /// <param name="generation">The generation number.</param>
        /// <param name="nodeManager">The NodeManager in that generation.</param>
        internal NodeManagerSlotSnapshot(
            NodeManagerSlot slot,
            long generation,
            IAsyncNodeManager nodeManager)
        {
            Slot = slot;
            Generation = generation;
            NodeManager = nodeManager;
        }

        /// <summary>
        /// Gets the stable slot.
        /// </summary>
        public NodeManagerSlot Slot { get; }

        /// <summary>
        /// Gets the generation number represented by this snapshot.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the NodeManager represented by this snapshot.
        /// </summary>
        public IAsyncNodeManager NodeManager { get; }
    }

    /// <summary>
    /// The immutable identity and lifetime gate for one node-manager instance.
    /// </summary>
    internal sealed class NodeManagerGeneration : INodeManagerCleanupParticipant
    {
        /// <summary>
        /// Initializes a generation.
        /// </summary>
        /// <param name="slot">The stable registration slot.</param>
        /// <param name="number">The generation number within the slot.</param>
        /// <param name="nodeManager">The wrapped node manager.</param>
        /// <param name="state">The initial lifecycle state.</param>
        /// <param name="resourceOwnership">The reserved physical arbiter, or null for an isolated generation.</param>
        internal NodeManagerGeneration(
            NodeManagerSlot slot,
            long number,
            IAsyncNodeManager nodeManager,
            NodeManagerGenerationState state,
            NodeManagerGenerationRegistry.ResourceLifetime? resourceOwnership = null)
            : this(slot, number, nodeManager, state, resourceOwnership, null)
        {
        }

        private NodeManagerGeneration(
            NodeManagerSlot slot,
            long number,
            IAsyncNodeManager nodeManager,
            NodeManagerGenerationState state,
            NodeManagerGenerationRegistry.ResourceLifetime? resourceOwnership,
            NodeManagerGeneration? facade)
        {
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            Number = number;
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            ResourceOwnership = resourceOwnership ?? new NodeManagerGenerationRegistry.ResourceLifetime();
            Gate = facade?.Gate ?? new GenerationGate(state, stateChanged: ResourceOwnership.NotifyStateChanged);
            CleanupRecoveryGate = facade?.CleanupRecoveryGate ?? new GenerationGate(NodeManagerGenerationState.Live);
            ForwardingFacade = facade?.Reference;
            Reference = new NodeManagerGenerationReference(this);
            SessionBindings = new ConcurrentDictionary<NodeId, GenerationSessionBindingRecord>();
            MonitoredItems = new ConcurrentDictionary<IMonitoredItem, ISubscription>(
                MonitoredItemReferenceComparer.Instance);
            EventBindings = new ConcurrentDictionary<IEventMonitoredItem, GenerationEventBindingRecord>(
                MonitoredItemReferenceComparer.Instance);
            if (facade is null)
            {
                ResourceOwnership.Add(this);
            }
        }

        /// <summary>
        /// Gets the stable slot shared by this generation and its replacements.
        /// </summary>
        internal NodeManagerSlot Slot { get; }

        /// <summary>
        /// Gets the generation number within the stable slot.
        /// </summary>
        internal long Number { get; }

        /// <summary>
        /// Gets the manager instance owned by this generation.
        /// </summary>
        internal IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// Gets the gate for background work that reaches the manager outside a pinned request.
        /// </summary>
        internal GenerationGate Gate { get; }

        /// <summary>
        /// Gets the physical ownership arbiter shared by otherwise independent logical generations.
        /// </summary>
        internal NodeManagerGenerationRegistry.ResourceLifetime ResourceOwnership { get; }

        /// <summary>
        /// Gets the exact routing owner for a non-owning forwarded source view.
        /// </summary>
        internal NodeManagerGenerationReference? ForwardingFacade { get; }

        /// <summary>
        /// Creates a raw source view without registering another routing or cleanup owner.
        /// </summary>
        internal NodeManagerGeneration CreateForwardedSourceView(IAsyncNodeManager source)
        {
            return new NodeManagerGeneration(Slot, Number, source, NodeManagerGenerationState.Prepared,
                ResourceOwnership, this);
        }

        /// <summary>
        /// Associates this generation with its server's NodeId reservations.
        /// </summary>
        internal void AttachNodeIdReservations(NodeIdReservations reservations)
        {
            NodeIdReservations? previous = Interlocked.CompareExchange(ref m_nodeIdReservations, reservations, null);
            if (previous is not null && !ReferenceEquals(previous, reservations))
            {
                throw new InvalidOperationException("A generation cannot reserve NodeIds on multiple servers.");
            }
        }

        /// <summary>
        /// Releases claims after the lifecycle verifies physical cleanup of this exact generation.
        /// </summary>
        internal void ReleaseNodeIdReservations()
        {
            Volatile.Read(ref m_nodeIdReservations)?.Release(this);
        }

        private NodeIdReservations? m_nodeIdReservations;

        /// <summary>
        /// Gets the opaque identity used to address cleanup failures on this exact generation.
        /// </summary>
        internal Guid CleanupRecoveryId { get; } = Guid.NewGuid();

        /// <summary>
        /// Admits short cleanup-recovery calls until a physical cleanup attempt begins.
        /// </summary>
        internal GenerationGate CleanupRecoveryGate { get; }

        /// <summary>
        /// Gets the weak generation token safe to retain in continuations.
        /// </summary>
        internal NodeManagerGenerationReference Reference { get; }

        /// <summary>
        /// Gets the per-session binding records owned by this generation.
        /// </summary>
        internal ConcurrentDictionary<NodeId, GenerationSessionBindingRecord> SessionBindings { get; }

        /// <summary>
        /// Gets the subscription-owned items bound to this exact generation.
        /// </summary>
        internal ConcurrentDictionary<IMonitoredItem, ISubscription> MonitoredItems { get; }

        /// <summary>
        /// Gets the per-item all-events binding records owned by this generation.
        /// </summary>
        internal ConcurrentDictionary<IEventMonitoredItem, GenerationEventBindingRecord> EventBindings { get; }

        /// <summary>
        /// Gets whether failed source releases still need provider cleanup.
        /// </summary>
        internal bool HasPendingSourceReleases
        {
            get
            {
                lock (m_sourceReleasesLock)
                {
                    return m_sourceReleases is { Count: > 0 };
                }
            }
        }

        /// <summary>
        /// Gets whether the all-events bindings are sealed because the generation retired. A sealed
        /// generation accepts no new bindings and keeps released records so a release happens once.
        /// </summary>
        internal bool EventBindingsSealed => Volatile.Read(ref m_eventBindingsSealed) != 0;

        /// <summary>
        /// Gets whether this logical owner has been routed independently of a forwarding facade.
        /// </summary>
        internal bool WasRouted => Volatile.Read(ref m_wasRouted);

        /// <summary>
        /// Records the routing owner's independent responsibility for its own binding cleanup.
        /// </summary>
        internal void MarkRouted()
        {
            Volatile.Write(ref m_wasRouted, true);
        }

        /// <summary>
        /// Gets or sets whether external consumers must wait for post-publication initialization.
        /// </summary>
        internal bool ReadinessPending
        {
            get => Volatile.Read(ref m_deferredEventState) == 1;
            set
            {
                lock (m_deferredEventsLock)
                {
                    if (value)
                    {
                        Volatile.Write(ref m_deferredEventState, 1);
                    }
                    else if (Volatile.Read(ref m_deferredEventState) == 1)
                    {
                        Volatile.Write(ref m_deferredEventState, 2);
                    }
                }
            }
        }

        /// <summary>
        /// Gets whether event deliveries must remain deferred until the readiness queue drains.
        /// </summary>
        internal bool IsDeferringEvents => Volatile.Read(ref m_deferredEventState) != 0;

        /// <summary>
        /// Gets the number of retained events dropped because the bounded retention was full.
        /// </summary>
        internal long DroppedDeferredEvents => Interlocked.Read(ref m_droppedDeferredEvents);

        /// <summary>
        /// Gets the number of item deliveries dropped with retained events.
        /// </summary>
        internal long DroppedDeferredDeliveries => Interlocked.Read(ref m_droppedDeferredDeliveries);

        /// <summary>
        /// Captures the readiness state for a source emission and retains its lifetime while events drain.
        /// </summary>
        /// <param name="eventId">The source emission identity.</param>
        /// <param name="readinessPending">Whether readiness was pending at capture time.</param>
        /// <returns>Whether the source emission must be tracked through the readiness drain.</returns>
        internal bool CaptureSourceEmission(long eventId, out bool readinessPending)
        {
            if (eventId == 0 || !IsDeferringEvents)
            {
                readinessPending = false;
                return false;
            }
            lock (m_deferredEventsLock)
            {
                int state = Volatile.Read(ref m_deferredEventState);
                readinessPending = state == 1;
                if (state == 0)
                {
                    return false;
                }
                TrackSourceEmissionLocked(eventId);
                return true;
            }
        }

        /// <summary>
        /// Tracks a child emission propagated from a source callback captured during readiness.
        /// </summary>
        /// <param name="eventId">The source emission identity.</param>
        internal void TrackSourceEmission(long eventId)
        {
            lock (m_deferredEventsLock)
            {
                TrackSourceEmissionLocked(eventId);
            }
        }

        /// <summary>
        /// Releases one source emission after its event deliveries have completed.
        /// </summary>
        /// <param name="eventId">The source emission identity.</param>
        internal void ReleaseSourceEmission(long eventId)
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_deferredEventsLock)
            {
                if (m_sourceEmissionCounts is null ||
                    !m_sourceEmissionCounts.TryGetValue(eventId, out int count))
                {
                    return;
                }
                if (count > 1)
                {
                    m_sourceEmissionCounts[eventId] = count - 1;
                }
                else
                {
                    m_sourceEmissionCounts.Remove(eventId);
                    m_droppedDeferredEventIds?.Remove(eventId);
                }
                if (--m_activeSourceEmissions == 0)
                {
                    drained = m_sourceEmissionsDrained;
                    m_sourceEmissionsDrained = null;
                }
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Retains one event's item deliveries until readiness completes.
        /// </summary>
        /// <param name="eventId">The identity shared by every delivery of this event.</param>
        /// <param name="delivery">The delivery work to run once readiness completes.</param>
        /// <param name="deliveryCount">The number of affected monitored items.</param>
        /// <param name="overflowed">Whether an older event was dropped to make room.</param>
        /// <returns><c>false</c> when event retention is not active.</returns>
        internal bool TryDeferEvent(
            long eventId,
            Func<Task> delivery,
            int deliveryCount,
            out bool overflowed)
        {
            overflowed = false;
            if (!IsDeferringEvents)
            {
                return false;
            }
            lock (m_deferredEventsLock)
            {
                if (Volatile.Read(ref m_deferredEventState) == 0)
                {
                    return false;
                }
                if (m_droppedDeferredEventIds?.Contains(eventId) == true)
                {
                    Interlocked.Add(ref m_droppedDeferredDeliveries, deliveryCount);
                    return true;
                }
                if (m_deferredEventsById is not null &&
                    m_deferredEventsById.TryGetValue(eventId, out DeferredEventWork? existing))
                {
                    existing.Deliveries.Add(new DeferredEventDelivery(delivery, deliveryCount));
                    existing.DeliveryCount += deliveryCount;
                    return true;
                }
                if (m_deferredEventOrder is { Count: >= MaxDeferredEvents })
                {
                    long droppedEventId = m_deferredEventOrder.First!.Value;
                    m_deferredEventOrder.RemoveFirst();
                    DeferredEventWork dropped = m_deferredEventsById![droppedEventId];
                    m_deferredEventsById.Remove(droppedEventId);
                    Interlocked.Increment(ref m_droppedDeferredEvents);
                    Interlocked.Add(ref m_droppedDeferredDeliveries, dropped.DeliveryCount);
                    if (m_sourceEmissionCounts?.ContainsKey(droppedEventId) == true)
                    {
                        (m_droppedDeferredEventIds ??= []).Add(droppedEventId);
                    }
                    overflowed = true;
                }
                var work = new DeferredEventWork();
                work.Deliveries.Add(new DeferredEventDelivery(delivery, deliveryCount));
                work.DeliveryCount = deliveryCount;
                (m_deferredEventsById ??= []).Add(eventId, work);
                (m_deferredEventOrder ??= new LinkedList<long>()).AddLast(eventId);
                return true;
            }
        }

        /// <summary>
        /// Delivers retained events in emission order after admitted readiness-time emissions have drained.
        /// </summary>
        /// <returns>The completion of the retained deliveries.</returns>
        /// <exception cref="AggregateException">
        /// One or more deliveries failed. Failed work remains queued ahead of later events for retry.
        /// </exception>
        internal async Task FlushDeferredEventsAsync()
        {
            if (ReadinessPending)
            {
                throw new InvalidOperationException("Deferred events cannot be flushed before readiness succeeds.");
            }
            while (true)
            {
                DeferredEventWork? work = null;
                long eventId = 0;
                Task? waitForEmissions = null;
                lock (m_deferredEventsLock)
                {
                    if (m_activeSourceEmissions != 0)
                    {
                        m_sourceEmissionsDrained ??= new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        waitForEmissions = m_sourceEmissionsDrained.Task;
                    }
                    else if (m_deferredEventOrder is { Count: > 0 })
                    {
                        eventId = m_deferredEventOrder.First!.Value;
                        m_deferredEventOrder.RemoveFirst();
                        work = m_deferredEventsById![eventId];
                        m_deferredEventsById.Remove(eventId);
                    }
                    else
                    {
                        Volatile.Write(ref m_deferredEventState, 0);
                        return;
                    }
                }

                if (waitForEmissions is not null)
                {
                    await waitForEmissions.ConfigureAwait(false);
                    continue;
                }

                List<Exception>? failures = null;
                List<DeferredEventDelivery>? failedDeliveries = null;
                foreach (DeferredEventDelivery pending in work!.Deliveries)
                {
                    try
                    {
                        await pending.Delivery().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        (failures ??= []).Add(ex);
                        (failedDeliveries ??= []).Add(pending);
                    }
                }
                if (failures is not null)
                {
                    lock (m_deferredEventsLock)
                    {
                        var retry = new DeferredEventWork();
                        retry.Deliveries.AddRange(failedDeliveries ?? []);
                        foreach (DeferredEventDelivery pending in retry.Deliveries)
                        {
                            retry.DeliveryCount += pending.DeliveryCount;
                        }
                        if (m_deferredEventsById is not null &&
                            m_deferredEventsById.TryGetValue(eventId, out DeferredEventWork? newer))
                        {
                            m_deferredEventsById.Remove(eventId);
                            retry.Deliveries.AddRange(newer.Deliveries);
                            retry.DeliveryCount += newer.DeliveryCount;
                        }
                        while (m_deferredEventOrder is { Count: >= MaxDeferredEvents })
                        {
                            long droppedEventId = m_deferredEventOrder.Last!.Value;
                            m_deferredEventOrder.RemoveLast();
                            DeferredEventWork dropped = m_deferredEventsById![droppedEventId];
                            m_deferredEventsById.Remove(droppedEventId);
                            Interlocked.Increment(ref m_droppedDeferredEvents);
                            Interlocked.Add(ref m_droppedDeferredDeliveries, dropped.DeliveryCount);
                            if (m_sourceEmissionCounts?.ContainsKey(droppedEventId) == true)
                            {
                                (m_droppedDeferredEventIds ??= []).Add(droppedEventId);
                            }
                        }
                        (m_deferredEventsById ??= []).Add(eventId, retry);
                        (m_deferredEventOrder ??= new LinkedList<long>()).AddFirst(eventId);
                    }
                    throw new AggregateException(failures);
                }
            }
        }

        /// <summary>
        /// Marks that the deferred-event overflow warning has been emitted for this generation.
        /// </summary>
        /// <returns>Whether this call should emit the warning.</returns>
        internal bool TryMarkDeferredEventOverflowLogged()
        {
            return Interlocked.Exchange(ref m_deferredEventOverflowLogged, 1) == 0;
        }

        private void TrackSourceEmissionLocked(long eventId)
        {
            m_sourceEmissionCounts ??= [];
            m_sourceEmissionCounts.TryGetValue(eventId, out int count);
            m_sourceEmissionCounts[eventId] = count + 1;
            m_activeSourceEmissions++;
        }

        private readonly record struct DeferredEventDelivery(Func<Task> Delivery, int DeliveryCount);

        private sealed class DeferredEventWork
        {
            internal List<DeferredEventDelivery> Deliveries { get; } = [];

            internal int DeliveryCount { get; set; }
        }

        /// <summary>
        /// Shares producer shutdown across preparation, retirement, and cleanup retries for this generation.
        /// </summary>
        /// <returns>The current attempt, retaining success and retrying only failed or canceled attempts.</returns>
        internal Task PrepareForShutdownAsync()
        {
            if (NodeManager is not INodeManagerShutdown shutdown)
            {
                return Task.CompletedTask;
            }
            TaskCompletionSource<bool> owner;
            lock (m_retirementResourcesLock)
            {
                if (m_shutdownPreparation is not null and not { IsFaulted: true } and not { IsCanceled: true })
                {
                    return m_shutdownPreparation;
                }
                owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_shutdownPreparation = owner.Task;
            }
            // Runs outside the state lock, and synchronously up to the provider's first await so
            // the provider stops admitting work before the caller continues.
            _ = RunProducerPreparationAsync(shutdown, owner);
            return owner.Task;
        }

        private static async Task RunProducerPreparationAsync(
            INodeManagerShutdown shutdown,
            TaskCompletionSource<bool> owner)
        {
            try
            {
                await shutdown.PrepareForShutdownAsync().ConfigureAwait(false);
                owner.TrySetResult(true);
            }
            catch (OperationCanceledException ex)
            {
                owner.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                owner.TrySetException(ex);
            }
        }

        /// <summary>
        /// Restores an unpublished retirement only before any producer-shutdown attempt has started.
        /// </summary>
        /// <returns>Whether the generation can still be restored without reviving stopped producers.</returns>
        internal bool TryRestoreBeforeShutdown()
        {
            lock (m_retirementResourcesLock)
            {
                if (m_shutdownPreparation is not null || !IsQuiescent)
                {
                    return false;
                }
                return ResourceOwnership.TryRestore(this);
            }
        }

        /// <summary>
        /// Seals the all-events bindings.
        /// </summary>
        /// <returns><c>true</c> when this call sealed the bindings.</returns>
        internal bool TrySealEventBindings()
        {
            lock (m_eventBindingsLock)
            {
                return Interlocked.Exchange(ref m_eventBindingsSealed, 1) == 0;
            }
        }

        /// <summary>
        /// Admits a binding record before retirement seals the generation, or resolves an existing release.
        /// </summary>
        /// <param name="monitoredItem">The exact item represented by the binding.</param>
        /// <param name="unsubscribe">Whether the operation releases an existing binding.</param>
        /// <returns>The admitted record, or null when retirement refuses the operation.</returns>
        internal GenerationEventBindingRecord? GetEventBinding(
            IEventMonitoredItem monitoredItem,
            bool unsubscribe)
        {
            lock (m_eventBindingsLock)
            {
                if (unsubscribe)
                {
                    return EventBindings.TryGetValue(monitoredItem, out GenerationEventBindingRecord? record)
                        ? record
                        : null;
                }
                if (EventBindingsSealed)
                {
                    return null;
                }
                return EventBindings.GetOrAdd(monitoredItem, static item => new GenerationEventBindingRecord(item));
            }
        }

        /// <summary>
        /// Gets whether all view pins and generation-gated operations have drained.
        /// </summary>
        internal bool IsQuiescent
        {
            get
            {
                if (Gate.ActiveCount != 0)
                {
                    return false;
                }
                lock (m_epochLock)
                {
                    foreach (AddressSpaceEpoch epoch in m_viewEpochs)
                    {
                        if (!epoch.IsDrained)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Gets whether an admitted generation operation or view pin still blocks reclamation.
        /// </summary>
        internal bool HasActiveQuiescenceBlockers
        {
            get
            {
                if (Gate.ActiveCount != 0)
                {
                    return true;
                }
                lock (m_epochLock)
                {
                    return m_viewEpochs.Any(static epoch => epoch.ActiveCount != 0);
                }
            }
        }

        /// <summary>
        /// Registers a view epoch whose dispatch routes include this generation.
        /// </summary>
        /// <param name="epoch">The view epoch.</param>
        internal void RegisterViewEpoch(AddressSpaceEpoch epoch)
        {
            lock (m_epochLock)
            {
                m_viewEpochs.RemoveAll(static current => current.IsDrained);
                m_viewEpochs.Add(epoch);
            }
        }

        /// <summary>
        /// Captures the epochs still associated with this generation.
        /// </summary>
        internal AddressSpaceEpoch[] GetViewEpochs()
        {
            lock (m_epochLock)
            {
                return [.. m_viewEpochs];
            }
        }

        /// <summary>
        /// Gets a cold-path description of the active admissions and pinned view epochs.
        /// </summary>
        /// <returns>The generation identity and current drain blockers.</returns>
        internal string GetQuiescenceWaitContext()
        {
            AddressSpaceEpoch[] epochs;
            lock (m_epochLock)
            {
                epochs = m_viewEpochs.Where(static epoch => epoch.ActiveCount != 0).ToArray();
            }
            string pinnedEpochs = epochs.Length == 0
                ? "none"
                : string.Join(", ", epochs.Select(static epoch => $"{epoch.Id}={epoch.ActiveCount}"));
            return $"slot={Slot.Id}; generation={Number}; manager={NodeManager.GetType().FullName}; " +
                $"activeAdmissions={Gate.ActiveCount}; pinnedEpochs=[{pinnedEpochs}]";
        }

        /// <summary>
        /// Waits for view pins and generation-gated background operations to drain.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the wait.</param>
        /// <returns>A task that completes when no operation can reach this generation.</returns>
        internal async ValueTask WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            AddressSpaceEpoch[] epochs;
            lock (m_epochLock)
            {
                epochs = [.. m_viewEpochs];
            }
            foreach (AddressSpaceEpoch epoch in epochs)
            {
                await epoch.WaitForDrainAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            await Gate.WaitForDrainAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Transfers a retired consumer's disposal to reclamation while admitted work can still use it.
        /// </summary>
        /// <param name="resource">The consumer that must outlive already-admitted work.</param>
        /// <returns>Whether reclamation now owns disposal.</returns>
        internal bool TryDeferDisposal(IDisposable resource)
        {
            lock (m_retirementResourcesLock)
            {
                if (m_retirementResources is not null)
                {
                    foreach (RetiredConsumer current in m_retirementResources)
                    {
                        if (current.Resource.TryGetDisposable(out IDisposable? retained) &&
                            ReferenceEquals(retained, resource))
                        {
                            // Only the synchronous callback selected by reclamation may bypass the transfer.
                            return current.DisposingThreadId != Environment.CurrentManagedThreadId;
                        }
                    }
                }
                if (m_retirementResourcesSealed ||
                    Gate.State is not (NodeManagerGenerationState.Retiring or NodeManagerGenerationState.Aborted) ||
                    IsQuiescent)
                {
                    return false;
                }
                m_retirementResources ??= [];
                m_retirementResources.Add(new RetiredConsumer(NodeManagerCleanupResource.FromDisposable(resource)));
                return true;
            }
        }

        /// <summary>
        /// Admits the reclaimer's selected synchronous disposal callback once for a transferred consumer.
        /// </summary>
        /// <param name="resource">The exact transferred consumer.</param>
        /// <returns>Whether this call owns the selected disposal callback.</returns>
        internal bool TryEnterRetiredConsumerDisposal(IDisposable resource)
        {
            lock (m_retirementResourcesLock)
            {
                if (m_retirementResources is not null)
                {
                    foreach (RetiredConsumer current in m_retirementResources)
                    {
                        if (current.Resource.TryGetDisposable(out IDisposable? retained) &&
                            ReferenceEquals(retained, resource) &&
                            current.DisposingThreadId == Environment.CurrentManagedThreadId &&
                            !current.CallbackEntered)
                        {
                            current.CallbackEntered = true;
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Retains a failed continuation cleanup and its session until verified recovery completes.
        /// </summary>
        /// <param name="resource">The exact continuation whose disposal failed.</param>
        /// <param name="failure">The original disposal failure.</param>
        /// <param name="onConfirmed">Releases the session's ownership after verified cleanup.</param>
        internal void RetainHistoryCleanupFailure(
            IDisposable resource,
            Exception failure,
            Func<Action?> onConfirmed)
        {
            lock (m_retirementResourcesLock)
            {
                m_retirementResources ??= [];
                m_retirementResources.Add(new RetiredConsumer(NodeManagerCleanupResource.FromDisposable(resource))
                {
                    Failure = failure,
                    ResourceKind = "HistoryContinuation",
                    OnConfirmed = onConfirmed
                });
            }
        }

        /// <summary>
        /// Retains the exact manager before starting its disposal.
        /// </summary>
        /// <param name="resource">The manager being disposed.</param>
        /// <param name="onCompleted">Marks the disposal stage complete after verified cleanup.</param>
        /// <returns>The registered disposal attempt.</returns>
        /// <exception cref="ArgumentException"><paramref name="resource"/> contains no disposal resource.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="onCompleted"/> is null.</exception>
        internal RetiredConsumer BeginManagerCleanup(
            NodeManagerCleanupResource resource,
            Action onCompleted)
        {
            if (resource.IsEmpty)
            {
                throw new ArgumentException("The manager resource is required.", nameof(resource));
            }
            if (onCompleted is null)
            {
                throw new ArgumentNullException(nameof(onCompleted));
            }
            var attempt = new RetiredConsumer(resource)
            {
                ResourceKind = "NodeManager",
                ManagerDisposalInProgress = true,
                OnConfirmed = () =>
                {
                    onCompleted();
                    return null;
                }
            };
            lock (m_retirementResourcesLock)
            {
                m_retirementResources ??= [];
                m_retirementResources.Add(attempt);
            }
            return attempt;
        }

        /// <summary>
        /// Owns a failed private abort until full cleanup of the exact manager has been verified.
        /// Managers without a disposal resource remain discoverable but cannot be confirmed through this seam.
        /// </summary>
        /// <param name="resource">The exact manager disposal target, if it has one.</param>
        /// <param name="failure">The original private abort failure.</param>
        /// <param name="onConfirmed">Marks abort and disposal complete, without replaying either callback.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="failure"/> or <paramref name="onConfirmed"/> is null.
        /// </exception>
        internal void RetainPrivateAbortFailure(
            NodeManagerCleanupResource resource,
            Exception failure,
            Action onConfirmed)
        {
            if (failure is null)
            {
                throw new ArgumentNullException(nameof(failure));
            }
            if (onConfirmed is null)
            {
                throw new ArgumentNullException(nameof(onConfirmed));
            }
            var retained = new RetiredConsumer(resource)
            {
                Failure = failure,
                ResourceKind = "NodeManager",
                ResourceName = "PrivateAbortAndManagerDisposal",
                OnConfirmed = () =>
                {
                    onConfirmed();
                    return null;
                }
            };
            lock (m_retirementResourcesLock)
            {
                m_retirementResources ??= [];
                m_retirementResources.Add(retained);
            }
        }

        /// <summary>
        /// Completes a manager disposal attempt or retains its original failure.
        /// </summary>
        /// <param name="attempt">The exact registered disposal attempt.</param>
        /// <param name="failure">The disposal failure, or null when disposal succeeded.</param>
        /// <exception cref="InvalidOperationException">
        /// The attempt is not retained by this generation or is no longer marked as a manager disposal in progress.
        /// </exception>
        internal void CompleteManagerCleanup(RetiredConsumer attempt, Exception? failure)
        {
            lock (m_retirementResourcesLock)
            {
                List<RetiredConsumer>? resources = m_retirementResources;
                if (resources is null || !resources.Contains(attempt) || !attempt.ManagerDisposalInProgress)
                {
                    throw new InvalidOperationException("The manager cleanup attempt is no longer owned.");
                }
                attempt.ManagerDisposalInProgress = false;
                if (failure is null)
                {
                    resources.Remove(attempt);
                }
                else
                {
                    attempt.Failure = failure;
                }
            }
        }

        /// <summary>
        /// Disposes retired consumers after the generation's existing epochs and gate have drained.
        /// A failed resource remains owned until its cleanup has been verified.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The generation is not quiescent, or a retained consumer is already being disposed
        /// or has no synchronous disposal resource.
        /// </exception>
        internal void DisposeRetiredResources()
        {
            RetiredConsumer[] resources;
            lock (m_retirementResourcesLock)
            {
                if (!IsQuiescent)
                {
                    throw new InvalidOperationException("Retired consumers cannot be disposed before quiescence.");
                }
                m_retirementResourcesSealed = true;
                resources = m_retirementResources?.ToArray() ?? [];
            }
            foreach (RetiredConsumer resource in resources)
            {
                lock (m_retirementResourcesLock)
                {
                    if (!m_retirementResources!.Contains(resource))
                    {
                        continue;
                    }
                    if (resource.Failure is { } failure)
                    {
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }
                    if (resource.DisposingThreadId != 0 || resource.ManagerDisposalInProgress)
                    {
                        throw new InvalidOperationException("The retired consumer is already being disposed.");
                    }
                    resource.DisposingThreadId = Environment.CurrentManagedThreadId;
                }
                try
                {
                    if (!resource.Resource.TryGetDisposable(out IDisposable? disposable))
                    {
                        throw new InvalidOperationException("The retired consumer has no synchronous disposal.");
                    }
                    disposable.Dispose();
                }
                catch (Exception failure)
                {
                    lock (m_retirementResourcesLock)
                    {
                        resource.DisposingThreadId = 0;
                        resource.Failure = failure;
                    }
                    throw;
                }
                lock (m_retirementResourcesLock)
                {
                    resource.DisposingThreadId = 0;
                    m_retirementResources!.Remove(resource);
                }
                NodeManagerCleanupRecovery.EnsureNoRetainedFailures(this);
            }
        }

        /// <inheritdoc/>
        ArrayOf<NodeManagerCleanupFailure> INodeManagerCleanupParticipant.GetCleanupFailures(
            NodeManagerCleanupAccess access)
        {
            if (access is null)
            {
                throw new ArgumentNullException(nameof(access));
            }
            lock (m_retirementResourcesLock)
            {
                if (!access.IsAuthorizedFor(this))
                {
                    throw new UnauthorizedAccessException("The cleanup discovery capability is invalid.");
                }
                var failures = new List<NodeManagerCleanupFailure>();
                if (m_retirementResources is not null)
                {
                    foreach (RetiredConsumer resource in m_retirementResources)
                    {
                        if (resource.Failure is not null)
                        {
                            failures.Add(new NodeManagerCleanupFailure(
                                resource.Id,
                                resource.ResourceKind,
                                resource.ResourceName,
                                StatusCodes.BadUnexpectedError));
                        }
                    }
                }
                return [.. failures];
            }
        }

        /// <inheritdoc/>
        NodeManagerCleanupResource INodeManagerCleanupParticipant.GetCleanupResource(
            NodeManagerCleanupVerification verification)
        {
            if (verification is null)
            {
                throw new ArgumentNullException(nameof(verification));
            }
            lock (m_retirementResourcesLock)
            {
                if (!verification.TryConsume(this, out Guid id))
                {
                    throw new InvalidOperationException("The cleanup verification capability is invalid.");
                }
                RetiredConsumer resource = m_retirementResources?.Find(
                    candidate => candidate.Id == id &&
                        candidate.Failure is not null &&
                        candidate.DisposingThreadId == 0 &&
                        !candidate.ConfirmationInProgress) ??
                    throw new InvalidOperationException("The retained consumer was not found.");
                return resource.Resource;
            }
        }

        /// <inheritdoc/>
        void INodeManagerCleanupParticipant.ConfirmCleanup(NodeManagerCleanupConfirmation confirmation)
        {
            if (confirmation is null)
            {
                throw new ArgumentNullException(nameof(confirmation));
            }
            RetiredConsumer resource;
            lock (m_retirementResourcesLock)
            {
                if (!confirmation.TryConsume(this, out Guid id))
                {
                    throw new InvalidOperationException("The cleanup confirmation capability is invalid.");
                }
                resource = m_retirementResources?.Find(
                    candidate => candidate.Id == id &&
                        candidate.Failure is not null &&
                        candidate.DisposingThreadId == 0 &&
                        !candidate.ConfirmationInProgress)
                    ?? throw new InvalidOperationException("The retained consumer was not found.");
                resource.ConfirmationInProgress = true;
            }
            try
            {
                Action? notification = resource.OnConfirmed?.Invoke();
                lock (m_retirementResourcesLock)
                {
                    if (m_retirementResources?.Remove(resource) != true)
                    {
                        throw new InvalidOperationException("The retained consumer was not found.");
                    }
                }
                notification?.Invoke();
            }
            finally
            {
                lock (m_retirementResourcesLock)
                {
                    resource.ConfirmationInProgress = false;
                }
            }
        }

        /// <summary>
        /// Retains one resource and the original failure of its single disposal attempt.
        /// </summary>
        internal sealed class RetiredConsumer(NodeManagerCleanupResource resource)
        {
            /// <summary>
            /// Gets the opaque identity of this retained consumer.
            /// </summary>
            internal Guid Id { get; } = Guid.NewGuid();

            /// <summary>
            /// Gets the exact consumer owned by this generation.
            /// </summary>
            internal NodeManagerCleanupResource Resource { get; } = resource;

            /// <summary>
            /// Gets or sets the thread running the synchronous disposal callback, or zero when it is not running.
            /// </summary>
            internal int DisposingThreadId { get; set; }

            /// <summary>
            /// Gets or sets whether the selected callback has entered the consumer's disposal path.
            /// </summary>
            internal bool CallbackEntered { get; set; }

            /// <summary>
            /// Gets or sets whether the manager's disposal attempt has not yet finished.
            /// </summary>
            internal bool ManagerDisposalInProgress { get; set; }

            /// <summary>
            /// Gets or sets the original failure when cleanup did not complete.
            /// </summary>
            internal Exception? Failure { get; set; }

            /// <summary>
            /// Gets or sets the bounded description used for discovery.
            /// </summary>
            internal string ResourceKind { get; set; } = "RetiredConsumer";

            /// <summary>
            /// Gets or sets the bounded dependent-stage description used for discovery.
            /// </summary>
            internal string? ResourceName { get; set; }

            /// <summary>
            /// Gets or sets the owner release and deferred notification for verified cleanup.
            /// </summary>
            internal Func<Action?>? OnConfirmed { get; set; }

            /// <summary>
            /// Gets or sets whether the confirmed owner release is in progress.
            /// </summary>
            internal bool ConfirmationInProgress { get; set; }
        }

        /// <summary>
        /// Retains failed source-provider cleanup independently of subscription membership.
        /// </summary>
        /// <param name="item">The item whose native source was released.</param>
        /// <param name="sourceRevision">The released source incarnation.</param>
        /// <param name="cleanup">The remaining provider and notification work.</param>
        internal void RetainSourceRelease(
            IMonitoredItem item,
            long sourceRevision,
            Func<CancellationToken, ValueTask> cleanup)
        {
            lock (m_sourceReleasesLock)
            {
                m_sourceReleases ??= new(MonitoredItemReferenceComparer.Instance);
                if (!m_sourceReleases.TryGetValue(item, out List<GenerationSourceRelease>? releases))
                {
                    m_sourceReleases.Add(item, releases = []);
                }
                releases.Add(new GenerationSourceRelease(item, sourceRevision, cleanup));
            }
        }

        /// <summary>
        /// Captures cleanup-owned items so a failed late handoff can resume after native release.
        /// </summary>
        /// <returns>The exact items with unfinished source work.</returns>
        internal IMonitoredItem[] CapturePendingSourceItems()
        {
            lock (m_sourceReleasesLock)
            {
                return m_sourceReleases is null ? [] : [.. m_sourceReleases.Keys];
            }
        }

        /// <summary>
        /// Completes retained cleanup before retrying an item or reclaiming its generation.
        /// </summary>
        /// <param name="item">The item to retry, or null to drain the entire generation.</param>
        /// <param name="ct">Cancels provider cleanup.</param>
        /// <param name="joinActiveCleanup">Whether independent reclamation may join work already running.</param>
        /// <param name="checkRetainedFailures">
        /// Whether to check for participant failures between releases. Recovery admission must be closed and drained.
        /// </param>
        internal async ValueTask CompleteSourceReleasesAsync(
            IMonitoredItem? item,
            CancellationToken ct,
            bool joinActiveCleanup = false,
            bool checkRetainedFailures = false)
        {
            GenerationSourceRelease[] releases;
            lock (m_sourceReleasesLock)
            {
                if (m_sourceReleases is null || m_sourceReleases.Count == 0)
                {
                    return;
                }
                if (item is not null)
                {
                    releases = m_sourceReleases.TryGetValue(item, out List<GenerationSourceRelease>? pending)
                        ? [.. pending]
                        : [];
                }
                else
                {
                    var pending = new List<GenerationSourceRelease>();
                    foreach (List<GenerationSourceRelease> values in m_sourceReleases.Values)
                    {
                        pending.AddRange(values);
                    }
                    releases = [.. pending];
                }
            }
            List<Exception>? failures = null;
            foreach (GenerationSourceRelease release in releases)
            {
                bool completed = false;
                try
                {
                    await release.CompleteAsync(joinActiveCleanup, ct).ConfigureAwait(false);
                    completed = true;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException &&
                    !(failure is OperationCanceledException && ct.IsCancellationRequested))
                {
                    (failures ??= []).Add(failure);
                }
                if (completed)
                {
                    lock (m_sourceReleasesLock)
                    {
                        if (m_sourceReleases.TryGetValue(release.Item, out List<GenerationSourceRelease>? pending))
                        {
                            pending.Remove(release);
                            if (pending.Count == 0)
                            {
                                m_sourceReleases.Remove(release.Item);
                            }
                        }
                    }
                }
                if (checkRetainedFailures)
                {
                    try
                    {
                        NodeManagerCleanupRecovery.EnsureNoRetainedFailures(this);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException && failures is not null)
                    {
                        failures.Add(failure);
                        break;
                    }
                }
            }
            if (failures is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures is { Count: > 1 })
            {
                throw new AggregateException("Monitored-item source-provider cleanup failed.", failures);
            }
        }

        /// <summary>
        /// Gets the opaque identity of one cleanup participant in this exact generation.
        /// </summary>
        /// <param name="participant">The participant whose failure registry is being addressed.</param>
        /// <returns>A stable identity for this participant within the generation.</returns>
        internal Guid GetCleanupParticipantId(INodeManagerCleanupParticipant participant)
        {
            return m_cleanupParticipants.GetValue(
                participant,
                static _ => new CleanupParticipantIdentity()).Id;
        }

        /// <summary>
        /// Admits verification before an exclusive confirmation or reconciliation begins.
        /// </summary>
        /// <returns>Whether the exact resource can be captured for verification.</returns>
        internal bool TryBeginCleanupVerification()
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_cleanupRecoveryInProgress || m_claimedCleanupConfirmation is not null)
                {
                    return false;
                }
                m_activeCleanupVerifications++;
                return true;
            }
        }

        /// <summary>
        /// Releases one verification after the verifier has stopped using the resource.
        /// </summary>
        /// <exception cref="InvalidOperationException">No cleanup verification remains active.</exception>
        internal void EndCleanupVerification()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_cleanupConfirmationLock)
            {
                if (m_activeCleanupVerifications == 0)
                {
                    throw new InvalidOperationException("No cleanup verification is active.");
                }
                if (--m_activeCleanupVerifications == 0)
                {
                    drained = m_cleanupVerificationsDrained;
                    m_cleanupVerificationsDrained = null;
                }
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Waits for verifiers admitted before the current exclusive recovery operation.
        /// </summary>
        /// <returns>A task that completes after those verifiers have exited.</returns>
        /// <exception cref="InvalidOperationException">Exclusive cleanup recovery has not been admitted.</exception>
        internal Task WaitForCleanupVerificationsAsync()
        {
            lock (m_cleanupConfirmationLock)
            {
                if (!m_cleanupRecoveryInProgress)
                {
                    throw new InvalidOperationException("Cleanup recovery has not been admitted.");
                }
                return m_activeCleanupVerifications == 0
                    ? Task.CompletedTask
                    : (m_cleanupVerificationsDrained ??= new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        /// <summary>
        /// Admits one confirmation or reconciliation at a time for this generation.
        /// </summary>
        internal bool TryBeginCleanupRecovery()
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_cleanupRecoveryInProgress ||
                    m_claimedCleanupConfirmation is not null)
                {
                    return false;
                }
                m_cleanupRecoveryInProgress = true;
                if (m_offeredCleanupConfirmation is not null)
                {
                    m_cleanupOfferSuspended = true;
                }
                return true;
            }
        }

        /// <summary>
        /// Gets whether a verified confirmation is still owned by recovery or a cleanup worker.
        /// </summary>
        internal bool HasPendingCleanupConfirmation => Volatile.Read(ref m_pendingCleanupConfirmationCount) != 0;

        /// <summary>
        /// Gets whether a cleanup worker may attempt to claim the pending confirmation.
        /// </summary>
        internal bool CanAttemptCleanup
        {
            get
            {
                lock (m_cleanupConfirmationLock)
                {
                    return m_pendingCleanupConfirmations.Count == 0 ||
                        (m_pendingCleanupConfirmations.Count == 1 &&
                            m_offeredCleanupConfirmation is not null &&
                            !m_cleanupOfferSuspended);
                }
            }
        }

        /// <summary>
        /// Ends the current confirmation or reconciliation admission.
        /// </summary>
        internal void EndCleanupRecovery()
        {
            lock (m_cleanupConfirmationLock)
            {
                m_cleanupRecoveryInProgress = false;
            }
        }

        /// <summary>
        /// Retains evidence that the trusted verifier succeeded before confirmation was consumed.
        /// </summary>
        /// <remarks>The caller must hold this generation's recovery admission.</remarks>
        /// <param name="handle">The exact participant and failure that were verified.</param>
        internal void RememberCleanupConfirmation(NodeManagerCleanupFailureHandle handle)
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_pendingCleanupConfirmations.Add(handle))
                {
                    Volatile.Write(ref m_pendingCleanupConfirmationCount, m_pendingCleanupConfirmations.Count);
                }
            }
        }

        /// <summary>
        /// Finds evidence retained for this exact generation, participant and failure.
        /// </summary>
        /// <remarks>The caller must hold this generation's recovery admission.</remarks>
        /// <param name="handle">The failure whose evidence is being checked.</param>
        /// <returns>Whether evidence remains available for reconciliation.</returns>
        internal bool HasCleanupConfirmation(NodeManagerCleanupFailureHandle handle)
        {
            lock (m_cleanupConfirmationLock)
            {
                return m_pendingCleanupConfirmations.Contains(handle);
            }
        }

        /// <summary>
        /// Captures the opaque confirmations still retained for this generation.
        /// </summary>
        /// <returns>A detached snapshot that does not contain resources or exceptions.</returns>
        internal NodeManagerCleanupFailureHandle[] SnapshotCleanupConfirmations()
        {
            lock (m_cleanupConfirmationLock)
            {
                return [.. m_pendingCleanupConfirmations];
            }
        }

        /// <summary>
        /// Removes one confirmation whose outcome has been resolved.
        /// </summary>
        /// <remarks>The caller must hold this generation's recovery admission.</remarks>
        /// <param name="handle">The failure whose evidence is no longer needed.</param>
        internal void ForgetCleanupConfirmation(NodeManagerCleanupFailureHandle handle)
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_pendingCleanupConfirmations.Remove(handle))
                {
                    Volatile.Write(ref m_pendingCleanupConfirmationCount, m_pendingCleanupConfirmations.Count);
                }
                m_auditedCleanupConfirmations.Remove(handle);
                if (m_offeredCleanupConfirmation == handle)
                {
                    m_offeredCleanupConfirmation = null;
                    m_cleanupOfferSuspended = false;
                }
            }
        }

        /// <summary>
        /// Resolves one audited confirmation or offers it to the cleanup worker that will own its outcome.
        /// </summary>
        /// <param name="handle">The verified and audited failure.</param>
        /// <param name="otherFailures">Whether another participant still reports a retained failure.</param>
        /// <returns>Whether cleanup can be resumed for this confirmation.</returns>
        /// <exception cref="InvalidOperationException">
        /// The confirmation is no longer pending, another unsuspended offer exists,
        /// or a cleanup worker already holds a confirmation claim.
        /// </exception>
        internal bool OfferCleanupConfirmation(NodeManagerCleanupFailureHandle handle, bool otherFailures)
        {
            lock (m_cleanupConfirmationLock)
            {
                if (!m_pendingCleanupConfirmations.Contains(handle) ||
                    (m_offeredCleanupConfirmation is not null && !m_cleanupOfferSuspended) ||
                    m_claimedCleanupConfirmation is not null)
                {
                    throw new InvalidOperationException("The cleanup confirmation is no longer available.");
                }
                m_offeredCleanupConfirmation = null;
                m_cleanupOfferSuspended = false;
                m_auditedCleanupConfirmations.Add(handle);
                if (otherFailures)
                {
                    return false;
                }
                m_auditedCleanupConfirmations.Remove(handle);
                foreach (NodeManagerCleanupFailureHandle resolved in m_auditedCleanupConfirmations)
                {
                    m_pendingCleanupConfirmations.Remove(resolved);
                }
                m_auditedCleanupConfirmations.Clear();
                m_auditedCleanupConfirmations.Add(handle);
                if (m_pendingCleanupConfirmations.Count != 1)
                {
                    m_pendingCleanupConfirmations.Remove(handle);
                    m_auditedCleanupConfirmations.Remove(handle);
                    Volatile.Write(ref m_pendingCleanupConfirmationCount, m_pendingCleanupConfirmations.Count);
                    return false;
                }
                Volatile.Write(ref m_pendingCleanupConfirmationCount, m_pendingCleanupConfirmations.Count);
                m_offeredCleanupConfirmation = handle;
                return true;
            }
        }

        /// <summary>
        /// Withdraws an offered confirmation if no cleanup worker has claimed it.
        /// </summary>
        /// <param name="handle">The confirmation whose resumption request failed.</param>
        internal void WithdrawCleanupConfirmationOffer(NodeManagerCleanupFailureHandle handle)
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_offeredCleanupConfirmation == handle)
                {
                    m_offeredCleanupConfirmation = null;
                    m_cleanupOfferSuspended = false;
                    m_auditedCleanupConfirmations.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Retains an unclaimed offer after a cleanup attempt fails, requiring reconciliation before reuse.
        /// </summary>
        internal void InvalidateOfferedCleanupConfirmation()
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_offeredCleanupConfirmation is { } offered)
                {
                    m_offeredCleanupConfirmation = null;
                    m_cleanupOfferSuspended = false;
                    m_auditedCleanupConfirmations.Remove(offered);
                }
            }
        }

        /// <summary>
        /// Claims an audited confirmation after recovery admission has closed and drained.
        /// </summary>
        /// <param name="handle">The claimed confirmation, or null when no confirmation was pending.</param>
        /// <returns>Whether this worker may continue physical cleanup.</returns>
        internal bool TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? handle)
        {
            lock (m_cleanupConfirmationLock)
            {
                handle = null;
                if (m_pendingCleanupConfirmations.Count == 0)
                {
                    return true;
                }
                if (m_pendingCleanupConfirmations.Count != 1 ||
                    m_offeredCleanupConfirmation is not { } offered ||
                    m_cleanupOfferSuspended ||
                    m_claimedCleanupConfirmation is not null ||
                    !m_pendingCleanupConfirmations.Contains(offered))
                {
                    return false;
                }
                m_offeredCleanupConfirmation = null;
                m_claimedCleanupConfirmation = offered;
                handle = offered;
                return true;
            }
        }

        /// <summary>
        /// Completes or returns a claimed confirmation before recovery admission reopens.
        /// </summary>
        /// <param name="handle">The exact confirmation claimed by the worker.</param>
        /// <param name="completed">Whether physical cleanup of this generation finished.</param>
        /// <param name="allowRetry">Whether a deferred worker may claim it again without reconciliation.</param>
        /// <exception cref="InvalidOperationException">
        /// The supplied handle is not the confirmation currently claimed by the cleanup worker.
        /// </exception>
        internal void FinishCleanupConfirmationClaim(
            NodeManagerCleanupFailureHandle handle,
            bool completed,
            bool allowRetry = false)
        {
            lock (m_cleanupConfirmationLock)
            {
                if (m_claimedCleanupConfirmation != handle)
                {
                    throw new InvalidOperationException("The cleanup confirmation claim is no longer owned.");
                }
                if (completed)
                {
                    m_pendingCleanupConfirmations.Remove(handle);
                    m_auditedCleanupConfirmations.Remove(handle);
                    Volatile.Write(ref m_pendingCleanupConfirmationCount, m_pendingCleanupConfirmations.Count);
                }
                else if (allowRetry)
                {
                    m_offeredCleanupConfirmation = handle;
                    m_cleanupOfferSuspended = false;
                }
                else
                {
                    m_auditedCleanupConfirmations.Remove(handle);
                }
                m_claimedCleanupConfirmation = null;
            }
        }

        /// <summary>
        /// Stores a participant identifier without retaining the participant independently of its owner.
        /// </summary>
        private sealed class CleanupParticipantIdentity
        {
            /// <summary>
            /// Gets the participant identifier.
            /// </summary>
            internal Guid Id { get; } = Guid.NewGuid();
        }

        /// <summary>Keeps stable participant identities without extending participant lifetimes.</summary>
#if NET8_0_OR_GREATER
        private readonly ConditionalWeakTable<INodeManagerCleanupParticipant, CleanupParticipantIdentity>
            m_cleanupParticipants = [];
#else
        private readonly ConditionalWeakTable<INodeManagerCleanupParticipant, CleanupParticipantIdentity>
            m_cleanupParticipants = new();
#endif

        /// <summary>Retains only opaque identities for unresolved verified confirmations.</summary>
        private readonly HashSet<NodeManagerCleanupFailureHandle> m_pendingCleanupConfirmations = [];

        /// <summary>Identifies confirmations whose effects and outcomes have already been audited.</summary>
        private readonly HashSet<NodeManagerCleanupFailureHandle> m_auditedCleanupConfirmations = [];

        /// <summary>Serializes confirmation evidence and cleanup-worker ownership.</summary>
        private readonly Lock m_cleanupConfirmationLock = new();

        /// <summary>Counts verifiers admitted before an exclusive recovery operation.</summary>
        private int m_activeCleanupVerifications;

        /// <summary>Completes when the admitted verifiers have exited.</summary>
        private TaskCompletionSource<bool>? m_cleanupVerificationsDrained;

        /// <summary>Serializes state transitions for this generation's recovery evidence.</summary>
        private bool m_cleanupRecoveryInProgress;

        /// <summary>Identifies an audited confirmation offered to a cleanup worker.</summary>
        private NodeManagerCleanupFailureHandle? m_offeredCleanupConfirmation;

        /// <summary>Blocks an existing offer while another recovery outcome is unresolved.</summary>
        private bool m_cleanupOfferSuspended;

        /// <summary>Identifies the confirmation owned by the active cleanup worker.</summary>
        private NodeManagerCleanupFailureHandle? m_claimedCleanupConfirmation;

        /// <summary>Publishes the pending-evidence state to cleanup workers.</summary>
        private int m_pendingCleanupConfirmationCount;

        /// <summary>Protects epoch registration, which occurs only while publishing views.</summary>
        private readonly Lock m_epochLock = new();

        /// <summary>The un-drained views that have routed this generation.</summary>
        private readonly List<AddressSpaceEpoch> m_viewEpochs = [];

        /// <summary>Serializes binding-record admission with retirement sealing.</summary>
        private readonly Lock m_eventBindingsLock = new();

        /// <summary>Non-zero once the all-events bindings were sealed at retirement.</summary>
        private int m_eventBindingsSealed;

        /// <summary>Whether this generation has belonged to a routing snapshot.</summary>
        private bool m_wasRouted;

        /// <summary>Zero when ready, one while the readiness callback is active, two while events are draining.</summary>
        private int m_deferredEventState;

        /// <summary>The maximum number of distinct source events retained during readiness.</summary>
        internal const int MaxDeferredEvents = 1024;
        private readonly Lock m_deferredEventsLock = new();
        private LinkedList<long>? m_deferredEventOrder;
        private Dictionary<long, DeferredEventWork>? m_deferredEventsById;
        private Dictionary<long, int>? m_sourceEmissionCounts;
        private HashSet<long>? m_droppedDeferredEventIds;
        private TaskCompletionSource<bool>? m_sourceEmissionsDrained;
        private int m_activeSourceEmissions;
        private long m_droppedDeferredEvents;
        private long m_droppedDeferredDeliveries;
        private int m_deferredEventOverflowLogged;

        /// <summary>Serializes cold-path disposal transfer with the reclamation handoff.</summary>
        private readonly Lock m_retirementResourcesLock = new();

        /// <summary>Consumers awaiting physical reclamation, not additional lifetime admissions.</summary>
        private List<RetiredConsumer>? m_retirementResources;

        /// <summary>Whether reclamation has assumed exclusive responsibility for pending disposals.</summary>
        private bool m_retirementResourcesSealed;

        /// <summary>The shared producer-shutdown attempt, retaining successful completion.</summary>
        private Task? m_shutdownPreparation;

        /// <summary>Protects exceptional source-cleanup ownership, never provider callbacks.</summary>
        private readonly Lock m_sourceReleasesLock = new();

        /// <summary>Failed source releases, allocated only when provider cleanup needs retry.</summary>
        private Dictionary<IMonitoredItem, List<GenerationSourceRelease>>? m_sourceReleases;
    }

    /// <summary>
    /// Owns retryable cleanup for one exact native source incarnation.
    /// </summary>
    internal sealed class GenerationSourceRelease
    {
        /// <summary>
        /// Captures cleanup that must survive removal of the item's subscription.
        /// </summary>
        internal GenerationSourceRelease(
            IMonitoredItem item,
            long sourceRevision,
            Func<CancellationToken, ValueTask> cleanup)
        {
            Item = item;
            SourceRevision = sourceRevision;
            m_cleanup = cleanup;
        }

        /// <summary>
        /// Gets the exact item whose provider must be released.
        /// </summary>
        internal IMonitoredItem Item { get; }

        /// <summary>
        /// Gets the released native source incarnation.
        /// </summary>
        internal long SourceRevision { get; }

        /// <summary>
        /// Retries without holding a generation lock; only independent reclamation joins active callbacks.
        /// </summary>
        internal async ValueTask CompleteAsync(bool joinActiveCleanup, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TaskCompletionSource<Exception?>? owner = null;
            Task<Exception?>? active = null;
            lock (m_sync)
            {
                if (m_completed)
                {
                    return;
                }
                if (m_completion is not null)
                {
                    if (!joinActiveCleanup)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The monitored-item source release is already being retried.");
                    }
                    active = m_completion.Task;
                }
                else
                {
                    m_completion = owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            if (active is not null)
            {
                Exception? failure = await active.WaitAsync(ct).ConfigureAwait(false);
                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
                return;
            }
            try
            {
                await m_cleanup(ct).ConfigureAwait(false);
                lock (m_sync)
                {
                    m_completed = true;
                }
                owner!.TrySetResult(null);
            }
            catch (Exception failure)
            {
                // Publish even a fatal escape so an independent join cannot be left waiting forever.
                owner!.TrySetResult(failure);
                throw;
            }
            finally
            {
                lock (m_sync)
                {
                    m_completion = null;
                }
            }
        }

        /// <summary>The remaining cold-path cleanup, including its progress.</summary>
        private readonly Func<CancellationToken, ValueTask> m_cleanup;

        /// <summary>Protects only cleanup claims and completion publication.</summary>
        private readonly Lock m_sync = new();

        /// <summary>Publishes a callback's result without creating unobserved faulted tasks.</summary>
        private TaskCompletionSource<Exception?>? m_completion;

        /// <summary>Whether every stage of this retained cleanup has completed.</summary>
        private bool m_completed;
    }

    /// <summary>
    /// Serializes callbacks for one session within a single NodeManager generation.
    /// </summary>
    internal sealed class GenerationSessionBindingRecord
    {
        /// <summary>
        /// Gets the semaphore that serializes this session's lifecycle callbacks.
        /// </summary>
        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>
        /// Gets or sets the binding currently installed for this session.
        /// </summary>
        internal GenerationReconciler.SessionBinding? Binding { get; set; }
    }

    /// <summary>
    /// Coordinates binding changes for one all-events monitored item.
    /// </summary>
    internal sealed class GenerationEventBindingRecord
    {
        /// <summary>
        /// Initializes a record for one exact monitored-item instance.
        /// </summary>
        /// <param name="item">The item represented by this record.</param>
        internal GenerationEventBindingRecord(IEventMonitoredItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        /// <summary>
        /// Gets the exact monitored-item instance represented by this record.
        /// </summary>
        internal IEventMonitoredItem Item { get; }

        /// <summary>
        /// Protects short state transitions; no manager callback runs while it is held.
        /// </summary>
        internal Lock Sync { get; } = new();

        /// <summary>
        /// Gets or sets the current binding transition.
        /// </summary>
        internal GenerationEventBindingState State { get; set; }

        /// <summary>
        /// Gets or sets whether cleanup retains the monitored item for an unsubscribe retry.
        /// </summary>
        internal bool RetainsDisposal { get; set; }

        /// <summary>
        /// Gets or sets whether an unsubscribe was requested during a bind callback.
        /// </summary>
        internal bool RemovalRequested { get; set; }

        /// <summary>
        /// Gets or sets completion for the active transition.
        /// </summary>
        internal TaskCompletionSource<bool>? Completion { get; set; }
    }

    /// <summary>
    /// Describes the transition state of one generation's all-events binding.
    /// </summary>
    internal enum GenerationEventBindingState
    {
        Unbound,
        Subscribing,
        Bound,
        Unsubscribing,
        Removed
    }

    /// <summary>
    /// Compares monitored items by reference identity.
    /// </summary>
    internal sealed class MonitoredItemReferenceComparer : IEqualityComparer<IMonitoredItem>
    {
        /// <summary>
        /// Gets the shared reference comparer.
        /// </summary>
        internal static MonitoredItemReferenceComparer Instance { get; } = new();

        /// <inheritdoc/>
        public bool Equals(IMonitoredItem? x, IMonitoredItem? y)
        {
            return ReferenceEquals(x, y);
        }

        /// <inheritdoc/>
        public int GetHashCode(IMonitoredItem obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    /// <summary>
    /// A non-owning token for an exact generation identity.
    /// </summary>
    internal sealed class NodeManagerGenerationReference
    {
        /// <summary>
        /// Initializes a weak token for a generation.
        /// </summary>
        /// <param name="generation">The generation identified by this token.</param>
        internal NodeManagerGenerationReference(NodeManagerGeneration generation)
        {
            m_slot = new WeakReference<NodeManagerSlot>(generation.Slot);
            Number = generation.Number;
            m_generation = new WeakReference<NodeManagerGeneration>(generation);
            RoutingOwner = generation.ForwardingFacade ?? this;
        }

        /// <summary>
        /// Gets the token used by items attached through the serving route.
        /// </summary>
        internal NodeManagerGenerationReference RoutingOwner { get; }

        /// <summary>
        /// Gets the stable slot identifier.
        /// </summary>
        internal Guid SlotId
        {
            get => m_slot.TryGetTarget(out NodeManagerSlot? slot) ? slot.Id : Guid.Empty;
        }

        /// <summary>
        /// Gets the generation number.
        /// </summary>
        internal long Number { get; }

        /// <summary>
        /// Attempts to resolve the generation without extending its lifetime.
        /// </summary>
        /// <param name="generation">The generation, when it remains alive.</param>
        /// <returns><c>true</c> when the generation is still available.</returns>
        internal bool TryGetGeneration(out NodeManagerGeneration? generation)
        {
            return m_generation.TryGetTarget(out generation);
        }

        /// <summary>
        /// Determines whether this token identifies the supplied generation.
        /// </summary>
        /// <param name="generation">The generation to compare.</param>
        /// <returns><c>true</c> when the generation created this token.</returns>
        internal bool Identifies(NodeManagerGeneration generation)
        {
            return ReferenceEquals(this, generation.Reference);
        }

        /// <summary>Weakly references the generation without keeping it or its manager alive.</summary>
        private readonly WeakReference<NodeManagerGeneration> m_generation;

        /// <summary>Weakly references the stable slot so its assigned identifier remains current.</summary>
        private readonly WeakReference<NodeManagerSlot> m_slot;
    }

    /// <summary>
    /// Tracks generation lookup keys and physical-resource associations.
    /// </summary>
    internal static class NodeManagerGenerationRegistry
    {
        /// <summary>
        /// Gets the server-scoped unresolved slot for restored items without a source manager.
        /// </summary>
        /// <param name="server">The server that owns the restored subscription.</param>
        /// <returns>The unresolved slot shared only by this server's ownerless items.</returns>
        internal static NodeManagerSlot GetOwnerlessSlot(IServerInternal server)
        {
            return s_ownerlessSlots.GetValue(server, static _ => new NodeManagerSlot());
        }

        /// <summary>
        /// Finds the server's ownerless slot without allocating one for ordinary recovery requests.
        /// </summary>
        /// <param name="server">The server whose ownerless items are being recovered.</param>
        /// <param name="slot">The existing unresolved slot, when present.</param>
        /// <returns>True when the server has restored an item without a source manager.</returns>
        internal static bool TryGetOwnerlessSlot(
            IServerInternal server,
            [NotNullWhen(true)] out NodeManagerSlot? slot)
        {
            return s_ownerlessSlots.TryGetValue(server, out slot);
        }

        /// <summary>
        /// Resolves an exact logical owner, creating a live legacy generation only for a new identity.
        /// </summary>
        /// <remarks>
        /// Transparent stack aliases share an association. A terminal association never creates a new generation.
        /// </remarks>
        /// <param name="nodeManager">The manager to resolve.</param>
        /// <returns>The entry for the normalized lookup key.</returns>
        internal static NodeManagerGeneration GetOrCreate(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            IAsyncNodeManager identity = GetGenerationIdentity(nodeManager);
            ResourceLifetime resource = GetResourceOwnership(nodeManager);
            lock (s_lock)
            {
                return GetOrCreateCore(nodeManager, identity, resource);
            }
        }

        /// <summary>
        /// Resolves an unregistered source without assigning it another facade's generation.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The source has no logical generation association, but its physical resource already has an owner.
        /// </exception>
        internal static NodeManagerGeneration GetOrCreateSource(IAsyncNodeManager nodeManager)
        {
            IAsyncNodeManager identity = GetGenerationIdentity(nodeManager);
            ResourceLifetime resource = GetResourceOwnership(nodeManager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
                {
                    return association.Get();
                }
                if (resource.HasOwners)
                {
                    throw new InvalidOperationException(
                        "The unregistered source cannot select another logical owner's generation.");
                }
                return GetOrCreateCore(nodeManager, identity, resource);
            }
        }

        /// <summary>
        /// Resolves an existing generation without creating or publishing an identity for an unregistered manager.
        /// </summary>
        /// <param name="nodeManager">The manager or an explicit wrapper of the same synchronous resource.</param>
        /// <param name="generation">The existing generation, when present.</param>
        /// <returns>Whether the manager already has a generation.</returns>
        internal static bool TryGet(IAsyncNodeManager nodeManager, out NodeManagerGeneration? generation)
        {
            IAsyncNodeManager identity = GetGenerationIdentity(nodeManager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
                {
                    return association.TryGet(out generation);
                }
                generation = null;
                return false;
            }
        }

        /// <summary>
        /// Creates the prepared generation that replaces the supplied current generation.
        /// </summary>
        /// <param name="current">The generation being replaced.</param>
        /// <param name="replacement">The replacement manager.</param>
        /// <returns>The prepared replacement generation.</returns>
        /// <exception cref="InvalidOperationException">
        /// The replacement identity already belongs to a live, retired or incompatible prepared generation,
        /// or its cleaned identity can no longer be reused.
        /// </exception>
        internal static NodeManagerGeneration CreateReplacement(
            NodeManagerGeneration current,
            IAsyncNodeManager replacement)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }
            IAsyncNodeManager identity = GetGenerationIdentity(replacement);
            ResourceLifetime resource = GetResourceOwnership(replacement);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
                {
                    NodeManagerGeneration existing = association.Get();
                    if (ReferenceEquals(existing.Slot, current.Slot) &&
                        existing.Number == current.Number + 1 &&
                        existing.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        return existing;
                    }
                    if (existing.Gate.State is NodeManagerGenerationState.Retiring or
                        NodeManagerGenerationState.Disposed)
                    {
                        throw new InvalidOperationException(
                            "A retired NodeManager instance cannot be reused as a replacement.");
                    }
                    if (existing.Gate.State == NodeManagerGenerationState.Live)
                    {
                        throw new InvalidOperationException(
                            "A live manager cannot be assigned to another generation slot.");
                    }
                    throw new InvalidOperationException(
                        "A NodeManager identity cannot be assigned to another generation.");
                }
                var generation = new NodeManagerGeneration(
                    current.Slot,
                    current.Number + 1,
                    replacement,
                    NodeManagerGenerationState.Prepared,
                    resource);
                AssociateGeneration(identity, generation);
                return generation;
            }
        }

        /// <summary>
        /// Creates a prepared generation for a newly added manager.
        /// </summary>
        /// <param name="nodeManager">The new manager.</param>
        /// <returns>The prepared generation.</returns>
        internal static NodeManagerGeneration CreateAddition(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            IAsyncNodeManager identity = GetGenerationIdentity(nodeManager);
            ResourceLifetime resource = GetResourceOwnership(nodeManager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
                {
                    NodeManagerGeneration existing = association.Get();
                    if (existing.Gate.State is NodeManagerGenerationState.Retiring or
                        NodeManagerGenerationState.Disposed or NodeManagerGenerationState.Aborted)
                    {
                        throw new InvalidOperationException(
                            "A retired NodeManager instance cannot be registered again.");
                    }
                    return existing;
                }
                var slot = new NodeManagerSlot();
                var generation = new NodeManagerGeneration(
                    slot, 1, nodeManager, NodeManagerGenerationState.Prepared, resource);
                AssociateGeneration(identity, generation);
                return generation;
            }
        }

        /// <summary>
        /// Creates a generation-lookup key, normalizing stack adapters but not other custom facades.
        /// </summary>
        /// <param name="nodeManager">The manager to normalize for generation lookup.</param>
        /// <returns>The normalized lookup key.</returns>
        internal static IAsyncNodeManager GetGenerationIdentity(IAsyncNodeManager nodeManager)
        {
            if (!IsStandardAdapter(nodeManager))
            {
                return nodeManager;
            }
            // TODO: Remove legacy adapter normalization when synchronous managers are removed.
#pragma warning disable CS0618, UA0031 // Normalize transparent legacy adapters without changing the generation key.
            var adapter = (AsyncNodeManagerAdapter)nodeManager;
            if (adapter.SyncNodeManager.GetType() == typeof(SyncNodeManagerAdapter))
            {
                var synchronous = (SyncNodeManagerAdapter)adapter.SyncNodeManager;
                return GetGenerationIdentity(synchronous.AsyncNodeManager);
            }
            return adapter.SyncNodeManager.ToAsyncNodeManager();
#pragma warning restore CS0618, UA0031
        }

        /// <summary>
        /// Identifies the stack's transparent async adapter without treating subclasses as aliases.
        /// </summary>
        internal static bool IsStandardAdapter(IAsyncNodeManager nodeManager)
        {
#pragma warning disable CS0618, UA0031 // Only the exact legacy stack adapter is transparent.
            return nodeManager.GetType() == typeof(AsyncNodeManagerAdapter);
#pragma warning restore CS0618, UA0031
        }

        /// <summary>
        /// Associates an exact logical owner and its transparent aliases with one generation.
        /// </summary>
        /// <param name="identity">The normalized generation-lookup key.</param>
        /// <param name="generation">The accepted generation.</param>
        private static void AssociateGeneration(
            IAsyncNodeManager identity,
            NodeManagerGeneration generation)
        {
            s_generations.Add(identity, new GenerationAssociation(generation));
        }

        /// <summary>
        /// Compares normalized generation-lookup keys rather than physical-resource identities.
        /// </summary>
        internal static bool HasSameGenerationIdentity(IAsyncNodeManager left, IAsyncNodeManager right)
        {
            return ReferenceEquals(GetGenerationIdentity(left), GetGenerationIdentity(right));
        }

        /// <summary>
        /// Uses one resource identity for standard and custom facades without replacing the generation owner.
        /// </summary>
        /// <param name="nodeManager">The facade supplied by the owner.</param>
        /// <returns>The canonical resource key.</returns>
#pragma warning disable CS0618, UA0031 // Unwraps legacy adapters to one canonical identity.
        internal static IAsyncNodeManager GetIdentity(IAsyncNodeManager nodeManager)
        {
            return s_resourceIdentities.GetValue(nodeManager, static manager =>
            {
                if (manager.SyncNodeManager is not { } resource)
                {
                    return manager;
                }
                if (resource is SyncNodeManagerAdapter synchronous)
                {
                    return ReferenceEquals(synchronous.AsyncNodeManager, manager)
                        ? manager
                        : GetIdentity(synchronous.AsyncNodeManager);
                }
                return resource.ToAsyncNodeManager();
            });
        }

        /// <summary>
        /// Returns the lookup-key entry or creates and publishes a live legacy generation under that key.
        /// </summary>
        private static NodeManagerGeneration GetOrCreateCore(
            IAsyncNodeManager nodeManager,
            IAsyncNodeManager identity,
            ResourceLifetime resource)
        {
            if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
            {
                return association.Get();
            }
            var slot = new NodeManagerSlot();
            var generation = new NodeManagerGeneration(
                slot, 1, nodeManager, NodeManagerGenerationState.Live, resource);
            AssociateGeneration(identity, generation);
            slot.Publish(generation);
            return generation;
        }

        /// <summary>
        /// Gets the lifetime arbiter for a physical resource, without conflating its logical owners.
        /// </summary>
        internal static ResourceLifetime GetResourceOwnership(IAsyncNodeManager manager)
        {
            return s_resources.GetValue(GetIdentity(manager), static _ => new ResourceLifetime());
        }

        /// <summary>
        /// Retains an accepted factory candidate before its registration slot and generation are assigned.
        /// Rejects reused terminal or live logical identities without guessing disposal ownership.
        /// </summary>
        /// <exception cref="NodeManagerAlreadyRegisteredException">
        /// The identity is already live or terminal, its exact physical candidate is already reserved,
        /// or the physical resource has begun cleanup or has only terminal owners and no candidate reservation.
        /// </exception>
        internal static void ReserveCandidate(IAsyncNodeManager manager)
        {
            IAsyncNodeManager identity = GetGenerationIdentity(manager);
            ResourceLifetime resource = GetResourceOwnership(manager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
                {
                    if (!association.TryGet(out NodeManagerGeneration? existing) ||
                        existing!.Gate.State != NodeManagerGenerationState.Prepared)
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    return;
                }
                resource.ReserveCandidate(manager);
            }
        }

        /// <summary>
        /// Rejects cleanup that would release another exact logical owner's association.
        /// </summary>
        internal static void ValidateCleanupAssociation(NodeManagerGeneration generation)
        {
            lock (s_lock)
            {
                GetCleanupAssociation(generation);
            }
        }

        /// <summary>
        /// Releases terminal associations only after exact, verified cleanup has succeeded.
        /// Tombstones retain identity and reject resurrection without retaining the manager or server.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The generation is not quiescent or is neither retiring nor aborted.
        /// </exception>
        internal static void CompleteCleanup(NodeManagerGeneration generation)
        {
            lock (s_lock)
            {
                GenerationAssociation? association = GetCleanupAssociation(generation);
                if (!generation.IsQuiescent ||
                    generation.Gate.State is not (NodeManagerGenerationState.Retiring or
                        NodeManagerGenerationState.Aborted))
                {
                    throw new InvalidOperationException("Only a quiescent terminal generation can release ownership.");
                }
                if (generation.Gate.State == NodeManagerGenerationState.Retiring)
                {
                    generation.Gate.DisposeGeneration();
                }
                generation.Slot.Unpublish(generation);
                association?.Release();
                generation.ResourceOwnership.Complete(generation);
            }
            ChangePreparer.Release(generation.NodeManager);
            generation.ReleaseNodeIdReservations();
        }

        /// <summary>
        /// Checks the current association before mutating a terminal gate or releasing resource ownership.
        /// </summary>
        private static GenerationAssociation? GetCleanupAssociation(NodeManagerGeneration generation)
        {
            IAsyncNodeManager identity = GetGenerationIdentity(generation.NodeManager);
            if (s_generations.TryGetValue(identity, out GenerationAssociation? association))
            {
                association.ValidateOwner(generation);
            }
            return association;
        }

        /// <summary>
        /// Retains an active generation, then only a non-owning terminal token and its tombstone.
        /// </summary>
        private sealed class GenerationAssociation(NodeManagerGeneration generation)
        {
            /// <summary>
            /// Resolves the retained generation or rejects a collected terminal identity.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// Cleanup released the retained owner and its terminal generation is no longer reachable.
            /// </exception>
            internal NodeManagerGeneration Get()
            {
                return TryGet(out NodeManagerGeneration? current)
                    ? current!
                    : throw new InvalidOperationException("A cleaned NodeManager identity cannot be reused.");
            }

            /// <summary>
            /// Resolves a generation without recreating a collected terminal identity.
            /// </summary>
            internal bool TryGet(out NodeManagerGeneration? current)
            {
                current = m_owner;
                return current is not null || m_reference.TryGetGeneration(out current);
            }

            /// <summary>
            /// Releases the validated lifetime owner while preserving the existing exact generation token.
            /// </summary>
            internal void Release()
            {
                m_owner = null;
            }

            /// <summary>
            /// Checks an exact cleanup owner without changing either its association or gate.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The supplied generation is not the association's retained cleanup owner.
            /// </exception>
            internal void ValidateOwner(NodeManagerGeneration owner)
            {
                if (!ReferenceEquals(m_owner, owner))
                {
                    throw new InvalidOperationException("The cleanup association has changed.");
                }
            }

            /// <summary>The authoritative lifetime owner before completed cleanup.</summary>
            private NodeManagerGeneration? m_owner = generation;

            /// <summary>The existing non-owning generation token; the association itself remains a tombstone.</summary>
            private readonly NodeManagerGenerationReference m_reference = generation.Reference;
        }

        /// <summary>
        /// Owns physical cleanup eligibility, shared admissions and verification dependencies.
        /// Opaque facades retain independent cleanup ownership until every owner has closed and drained.
        /// </summary>
        internal sealed class ResourceLifetime : IDisposable
        {
            /// <summary>
            /// Gets whether physical cleanup has sealed new registrations and restoration.
            /// </summary>
            internal bool CleanupStarted
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_cleanupStarted;
                    }
                }
            }

            /// <summary>
            /// Gets whether more than one logical owner still needs the physical resource.
            /// </summary>
            internal bool IsShared
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_owners.Count + m_candidates.Count > 1;
                    }
                }
            }

            /// <summary>
            /// Gets whether an unregistered source would compete with an existing logical owner.
            /// </summary>
            internal bool HasOwners
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_owners.Count != 0 || m_candidates.Count != 0 || m_cleanupStarted;
                    }
                }
            }

            /// <summary>
            /// Gets whether a quiescent caller can join cleanup without waiting for a still-live shared owner.
            /// </summary>
            internal bool IsCleanupReady
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_candidates.Count == 0 &&
                            m_owners.All(static current =>
                                current.Gate.State is NodeManagerGenerationState.Retiring or
                                    NodeManagerGenerationState.Aborted or NodeManagerGenerationState.Disposed);
                    }
                }
            }

            /// <summary>
            /// Gets whether offered shared evidence can resume a cleanup blocked by another logical owner.
            /// </summary>
            internal bool HasReadyCleanupConfirmation
            {
                get
                {
                    lock (m_sync)
                    {
                        return m_owners.Any(static current =>
                            current.HasPendingCleanupConfirmation && current.CanAttemptCleanup);
                    }
                }
            }

            /// <summary>
            /// Disposes synchronization only after terminal ownership and every cleanup admission have drained.
            /// An active lease or pending waiter owns the deferred disposal until its exact exit.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// Physical cleanup has not started, or logical owners or private candidate reservations still exist.
            /// </exception>
            public void Dispose()
            {
                lock (m_sync)
                {
                    if (!m_cleanupStarted || m_owners.Count != 0 || m_candidates.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "Physical synchronization cannot be disposed before its owners complete cleanup.");
                    }
                    if (m_cleanupDisposed || m_cleanupAdmissions != 0)
                    {
                        return;
                    }
                    m_cleanup.Dispose();
                    m_cleanupDisposed = true;
                }
            }

            /// <summary>
            /// Checks whether retiring a facade also owns cleanup of the unrouted physical source's bindings.
            /// </summary>
            internal bool OwnsUnroutedBindings(NodeManagerGeneration facade, NodeManagerGeneration source)
            {
                lock (m_sync)
                {
                    if (source.WasRouted || !m_owners.Contains(source))
                    {
                        return false;
                    }
                    return !m_owners.Any(owner => !ReferenceEquals(owner, facade) && owner.WasRouted &&
                        owner.Gate.State is NodeManagerGenerationState.Prepared or NodeManagerGenerationState.Live);
                }
            }

            /// <summary>
            /// Restores an exact tentative retirement atomically with the physical cleanup decision.
            /// </summary>
            internal bool TryRestore(NodeManagerGeneration owner)
            {
                lock (m_sync)
                {
                    if (m_cleanupStarted || !m_owners.Contains(owner))
                    {
                        return false;
                    }
                    owner.Gate.Restore();
                    return true;
                }
            }

            /// <summary>
            /// Reserves ownership before the logical generation can be published.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// Physical cleanup has started, or all remaining owners are terminal
            /// and no candidate reservation exists for this manager.
            /// </exception>
            internal void Add(NodeManagerGeneration owner)
            {
                lock (m_sync)
                {
                    int candidateIndex = m_candidates.FindIndex(
                        candidate => ReferenceEquals(candidate, owner.NodeManager));
                    if (m_cleanupStarted ||
                        (candidateIndex < 0 &&
                            m_owners.Count != 0 &&
                            m_owners.All(static current =>
                                current.Gate.State is NodeManagerGenerationState.Retiring or
                                    NodeManagerGenerationState.Aborted or NodeManagerGenerationState.Disposed)))
                    {
                        throw new InvalidOperationException(
                            "A retiring or cleaned physical resource cannot be reused.");
                    }
                    m_owners.Add(owner);
                    if (candidateIndex >= 0)
                    {
                        m_candidates.RemoveAt(candidateIndex);
                    }
                    NotifyStateChanged();
                }
            }

            /// <summary>
            /// Owns a private factory result until generation assignment consumes this physical reservation.
            /// </summary>
            /// <exception cref="NodeManagerAlreadyRegisteredException">
            /// Physical cleanup has started, this exact candidate is already reserved,
            /// or the resource has only terminal owners and no outstanding candidate reservation.
            /// </exception>
            internal void ReserveCandidate(IAsyncNodeManager candidate)
            {
                lock (m_sync)
                {
                    if (m_cleanupStarted ||
                        m_candidates.Any(current => ReferenceEquals(current, candidate)) ||
                        (m_candidates.Count == 0 &&
                            m_owners.Count != 0 &&
                            m_owners.All(static current =>
                                current.Gate.State is NodeManagerGenerationState.Retiring or
                                    NodeManagerGenerationState.Aborted or NodeManagerGenerationState.Disposed)))
                    {
                        throw new NodeManagerAlreadyRegisteredException();
                    }
                    m_candidates.Add(candidate);
                    NotifyStateChanged();
                }
            }

            /// <summary>
            /// Wakes physical cleanup when an owner closes admission.
            /// </summary>
            internal void NotifyStateChanged()
            {
                TaskCompletionSource<bool>? changed;
                lock (m_sync)
                {
                    changed = m_changed;
                    m_changed = null;
                }
                changed?.TrySetResult(true);
            }

            /// <summary>
            /// Serializes destructive callbacks only after all logical owners close admission.
            /// No provider callback runs while a synchronous state lock is held.
            /// The caller owns its closed recovery gate and releases this lease after handling its cleanup outcome.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// This lifetime no longer retains the exact logical owner requesting physical cleanup.
            /// </exception>
            internal async ValueTask<CleanupLease> EnterCleanupAsync(NodeManagerGeneration owner)
            {
                lock (m_sync)
                {
                    if (!m_owners.Contains(owner))
                    {
                        throw new InvalidOperationException(
                            "Physical cleanup ownership is no longer retained.");
                    }
                    m_cleanupAdmissions = checked(m_cleanupAdmissions + 1);
                }
                bool acquired = false;
                CleanupLease? lease = null;
                try
                {
                    while (true)
                    {
                        Task changed;
                        lock (m_sync)
                        {
                            if (!m_owners.Contains(owner))
                            {
                                throw new InvalidOperationException(
                                    "Physical cleanup ownership is no longer retained.");
                            }
                            if (m_candidates.Count == 0 &&
                                m_owners.All(static current =>
                                    current.Gate.State is NodeManagerGenerationState.Retiring or
                                        NodeManagerGenerationState.Aborted or NodeManagerGenerationState.Disposed))
                            {
                                m_cleanupStarted = true;
                                break;
                            }
                            changed = (m_changed ??= new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                        }
                        await changed.ConfigureAwait(false);
                    }
                    await m_cleanup.WaitAsync().ConfigureAwait(false);
                    acquired = true;
                    NodeManagerGeneration[] owners;
                    lock (m_sync)
                    {
                        if (!m_owners.Contains(owner))
                        {
                            throw new InvalidOperationException(
                                "Physical cleanup ownership is no longer retained.");
                        }
                        owners = [.. m_owners];
                    }
                    lease = new CleanupLease(this, owner, owners);
                    await lease.CloseRecoveryAsync().ConfigureAwait(false);
                    return lease;
                }
                catch
                {
                    if (lease is null)
                    {
                        ExitCleanup(acquired);
                    }
                    else
                    {
                        lease.Dispose();
                    }
                    throw;
                }
            }

            /// <summary>
            /// Drops only the exact completed owner, leaving an irreversible physical tombstone.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// Physical cleanup has not started, the owner is not quiescent,
            /// or this lifetime no longer retains the owner.
            /// </exception>
            internal void Complete(NodeManagerGeneration owner)
            {
                bool completed;
                lock (m_sync)
                {
                    if (!m_cleanupStarted || !owner.IsQuiescent || !m_owners.Remove(owner))
                    {
                        throw new InvalidOperationException("Physical cleanup has not completed for this owner.");
                    }
                    completed = m_owners.Count == 0 && m_candidates.Count == 0;
                }
                NotifyStateChanged();
                if (completed)
                {
                    Dispose();
                }
            }

            /// <summary>
            /// Releases synchronization before ending its admission, so pending waiters cannot race disposal.
            /// </summary>
            private void ExitCleanup(bool acquired)
            {
                if (acquired)
                {
                    m_cleanup.Release();
                }
                bool completed;
                lock (m_sync)
                {
                    m_cleanupAdmissions--;
                    completed = m_cleanupStarted &&
                        m_owners.Count == 0 &&
                        m_candidates.Count == 0 &&
                        m_cleanupAdmissions == 0;
                }
                if (completed)
                {
                    Dispose();
                }
            }

            /// <summary>
            /// Keeps sibling recovery admissions closed throughout an exclusive physical cleanup attempt.
            /// </summary>
            internal sealed class CleanupLease(
                ResourceLifetime resource,
                NodeManagerGeneration cleanupOwner,
                NodeManagerGeneration[] owners) : IDisposable
            {
                /// <summary>
                /// Gets whether a sibling confirmation prevented this physical cleanup attempt.
                /// </summary>
                internal bool ConfirmationPending { get; private set; }

                /// <summary>
                /// Excludes shared verifiers and transfers the fully drained owner gate to the caller.
                /// Entry failure leaves every closed gate owned by this lease for rollback.
                /// </summary>
                internal async ValueTask CloseRecoveryAsync()
                {
                    while (m_closedRecoveryCount < owners.Length)
                    {
                        NodeManagerGeneration current = owners[m_closedRecoveryCount];
                        current.CleanupRecoveryGate.Retire();
                        m_closedRecoveryCount++;
                        await current.CleanupRecoveryGate.WaitForDrainAsync().ConfigureAwait(false);
                    }
                    m_ownerRecoveryTransferred = true;
                }

                /// <summary>
                /// Claims sibling evidence and stops producers before joining the work they may release.
                /// The caller already owns its exact confirmation; siblings keep evidence for their own cleanup.
                /// </summary>
                /// <exception cref="InvalidOperationException">
                /// A shared logical owner's pending cleanup confirmation cannot be claimed for this attempt.
                /// </exception>
                internal async ValueTask PrepareForShutdownAsync(NodeManagerGeneration owner)
                {
                    var claims = new List<(NodeManagerGeneration Generation, NodeManagerCleanupFailureHandle Handle)>();
                    bool completed = false;
                    try
                    {
                        foreach (NodeManagerGeneration current in owners)
                        {
                            if (!ReferenceEquals(current, owner))
                            {
                                if (!current.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? claim))
                                {
                                    ConfirmationPending = true;
                                    throw new InvalidOperationException(
                                        "A shared owner's cleanup confirmation is not ready.");
                                }
                                if (claim is { } handle)
                                {
                                    claims.Add((current, handle));
                                }
                            }
                            NodeManagerCleanupRecovery.EnsureNoRetainedFailures(current);
                        }
                        foreach (NodeManagerGeneration current in owners)
                        {
                            await current.PrepareForShutdownAsync().ConfigureAwait(false);
                            NodeManagerCleanupRecovery.EnsureNoRetainedFailures(current);
                        }
                        completed = true;
                    }
                    finally
                    {
                        foreach ((NodeManagerGeneration generation, NodeManagerCleanupFailureHandle handle) in claims)
                        {
                            generation.FinishCleanupConfirmationClaim(
                                handle, completed: false, allowRetry: completed || ConfirmationPending);
                        }
                    }
                }

                /// <summary>
                /// Joins all shared physical admissions before deletion, abort or disposal.
                /// </summary>
                internal async ValueTask WaitForQuiescenceAsync()
                {
                    foreach (NodeManagerGeneration current in owners)
                    {
                        await current.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false);
                        NodeManagerCleanupRecovery.EnsureNoRetainedFailures(current);
                    }
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                    {
                        return;
                    }
                    try
                    {
                        for (int index = 0; index < m_closedRecoveryCount; index++)
                        {
                            NodeManagerGeneration current = owners[index];
                            if ((!m_ownerRecoveryTransferred || !ReferenceEquals(current, cleanupOwner)) &&
                                current.CleanupRecoveryGate.State == NodeManagerGenerationState.Retiring)
                            {
                                current.CleanupRecoveryGate.Restore();
                            }
                        }
                    }
                    finally
                    {
                        resource.ExitCleanup(acquired: true);
                    }
                }

                /// <summary>The prefix of the existing owner snapshot closed by this exact attempt.</summary>
                private int m_closedRecoveryCount;

                /// <summary>Whether the caller owns its drained recovery gate rather than this lease.</summary>
                private bool m_ownerRecoveryTransferred;

                /// <summary>Ensures this lease releases its physical cleanup admission exactly once.</summary>
                private int m_disposed;
            }

            /// <summary>Serializes ownership changes, never provider callbacks.</summary>
            private readonly Lock m_sync = new();

            /// <summary>Retains active and failed cleanup owners and their verification dependencies.</summary>
            private readonly List<NodeManagerGeneration> m_owners = [];

            /// <summary>Owns accepted private factory results until their exact generations are assigned.</summary>
            private readonly List<IAsyncNodeManager> m_candidates = [];

            /// <summary>Serializes physical cleanup attempts across independent logical generations.</summary>
            private readonly SemaphoreSlim m_cleanup = new(1, 1);

            /// <summary>Wakes workers when logical admission changes.</summary>
            private TaskCompletionSource<bool>? m_changed;

            /// <summary>Irreversibly seals the physical resource before any cleanup callback starts.</summary>
            private bool m_cleanupStarted;

            /// <summary>Counts active leases and pending cleanup calls, including logical and semaphore waiters.</summary>
            private int m_cleanupAdmissions;

            /// <summary>Records disposal without removing the physical resource's terminal tombstone.</summary>
            private bool m_cleanupDisposed;
        }
#pragma warning restore CS0618, UA0031

        /// <summary>Serializes generation identity changes on writer paths.</summary>
        private static readonly Lock s_lock = new();

        /// <summary>Fixes each facade's resource identity without extending its lifetime.</summary>
#if NET8_0_OR_GREATER
        private static readonly ConditionalWeakTable<IAsyncNodeManager, IAsyncNodeManager> s_resourceIdentities = [];
#else
        private static readonly ConditionalWeakTable<IAsyncNodeManager, IAsyncNodeManager> s_resourceIdentities = new();
#endif

        /// <summary>Maps a normalized lookup key to the associated generation.</summary>
#if NET8_0_OR_GREATER
        private static readonly ConditionalWeakTable<IAsyncNodeManager, GenerationAssociation> s_generations = [];
#else
        private static readonly ConditionalWeakTable<IAsyncNodeManager, GenerationAssociation> s_generations = new();
#endif

        /// <summary>Arbitrates physical lifetime without assigning one facade's identity to another.</summary>
#if NET8_0_OR_GREATER
        private static readonly ConditionalWeakTable<IAsyncNodeManager, ResourceLifetime> s_resources = [];
#else
        private static readonly ConditionalWeakTable<IAsyncNodeManager, ResourceLifetime> s_resources =
            new();
#endif

        /// <summary>Scopes restored ownerless items to the lifetime and identity of their server.</summary>
#if NET8_0_OR_GREATER
        private static readonly ConditionalWeakTable<IServerInternal, NodeManagerSlot> s_ownerlessSlots = [];
#else
        private static readonly ConditionalWeakTable<IServerInternal, NodeManagerSlot> s_ownerlessSlots = new();
#endif
    }

    /// <summary>
    /// A striped admission gate for non-request work that can outlive its initiating request.
    /// </summary>
    internal sealed class GenerationGate
    {
        /// <summary>
        /// Initializes the gate in the supplied state.
        /// </summary>
        /// <param name="state">The initial state.</param>
        /// <param name="active">The admission counter, or null to create one.</param>
        /// <param name="stateChanged">The private notification for physical cleanup eligibility.</param>
        internal GenerationGate(
            NodeManagerGenerationState state,
            StripedCounter? active = null,
            Action? stateChanged = null)
        {
            m_state = (int)state;
            m_active = active ?? new StripedCounter();
            m_stateChanged = stateChanged;
        }

        /// <summary>
        /// Gets the current lifecycle state.
        /// </summary>
        internal NodeManagerGenerationState State
            => (NodeManagerGenerationState)Volatile.Read(ref m_state);

        /// <summary>
        /// Attempts to enter while the generation is live.
        /// </summary>
        /// <param name="lease">The gate lease on success.</param>
        /// <returns><c>true</c> if this operation entered before retirement.</returns>
        internal bool TryEnter(out Lease? lease)
        {
            if (TryEnter(out int slot))
            {
                lease = new Lease(this, slot);
                return true;
            }
            lease = null;
            return false;
        }

        /// <summary>
        /// Attempts to enter the generation without allocating a lease object.
        /// </summary>
        /// <param name="slot">The counter slot to release on success.</param>
        /// <returns><c>true</c> if this operation entered before retirement.</returns>
        internal bool TryEnter(out int slot)
        {
            slot = m_active.Enter();
            if (State == NodeManagerGenerationState.Live)
            {
                return true;
            }
            Exit(slot);
            slot = -1;
            return false;
        }

        /// <summary>
        /// Moves a prepared generation into service.
        /// </summary>
        internal void Activate()
        {
            Transition(NodeManagerGenerationState.Prepared, NodeManagerGenerationState.Live);
        }

        /// <summary>
        /// Closes admission for a generation being retired.
        /// </summary>
        internal void Retire()
        {
            int previous = Interlocked.CompareExchange(
                ref m_state,
                (int)NodeManagerGenerationState.Retiring,
                (int)NodeManagerGenerationState.Live);
            if (previous == (int)NodeManagerGenerationState.Retiring ||
                previous == (int)NodeManagerGenerationState.Disposed ||
                previous == (int)NodeManagerGenerationState.Aborted)
            {
                return;
            }
            if (previous != (int)NodeManagerGenerationState.Live &&
                previous != (int)NodeManagerGenerationState.Prepared)
            {
                throw new InvalidOperationException("Only a prepared or live generation can retire.");
            }
            if (previous == (int)NodeManagerGenerationState.Prepared)
            {
                Volatile.Write(ref m_state, (int)NodeManagerGenerationState.Retiring);
            }
            CompleteDrainIfReady();
            m_stateChanged?.Invoke();
        }

        /// <summary>
        /// Reopens a retired generation when an unpublished replacement is rolled back.
        /// </summary>
        internal void Restore()
        {
            lock (m_waitLock)
            {
                int previous = Interlocked.CompareExchange(
                    ref m_state,
                    (int)NodeManagerGenerationState.Live,
                    (int)NodeManagerGenerationState.Retiring);
                if (previous != (int)NodeManagerGenerationState.Retiring)
                {
                    throw new InvalidOperationException(
                        "Only a retired generation can be restored after replacement rollback.");
                }
                m_drained = null;
                m_drainCompleted = false;
            }
            m_stateChanged?.Invoke();
        }

        /// <summary>
        /// Marks a retired generation disposed after its owners have drained.
        /// </summary>
        internal void DisposeGeneration()
        {
            lock (m_waitLock)
            {
                if (!m_drainCompleted && ActiveCount != 0)
                {
                    throw new InvalidOperationException("A generation with active operations cannot be disposed.");
                }
                Transition(NodeManagerGenerationState.Retiring, NodeManagerGenerationState.Disposed);
            }
        }

        /// <summary>
        /// Marks an unpublished generation aborted.
        /// </summary>
        internal void Abort()
        {
            int previous = Interlocked.CompareExchange(
                ref m_state,
                (int)NodeManagerGenerationState.Aborted,
                (int)NodeManagerGenerationState.Prepared);
            if (previous != (int)NodeManagerGenerationState.Prepared &&
                previous != (int)NodeManagerGenerationState.Aborted)
            {
                throw new InvalidOperationException("Only a prepared generation can be aborted.");
            }
            CompleteDrainIfReady();
            m_stateChanged?.Invoke();
        }

        /// <summary>
        /// Gets the number of operations that entered and have not exited.
        /// </summary>
        internal long ActiveCount => m_active.Count;

        /// <summary>
        /// Waits for all admitted operations to exit.
        /// </summary>
        /// <returns>A task that completes when the active count is zero.</returns>
        internal Task WaitForDrainAsync()
        {
            if (HasNoActiveOperations)
            {
                return Task.CompletedTask;
            }
            lock (m_waitLock)
            {
                if (HasNoActiveOperations)
                {
                    return Task.CompletedTask;
                }
                return (m_drained ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        private void Transition(NodeManagerGenerationState expected, NodeManagerGenerationState next)
        {
            int previous = Interlocked.CompareExchange(ref m_state, (int)next, (int)expected);
            if (previous != (int)expected && previous != (int)next)
            {
                throw new InvalidOperationException(
                    $"Cannot transition a generation from {(NodeManagerGenerationState)previous} to {next}.");
            }
            m_stateChanged?.Invoke();
        }

        internal void Exit(int slot)
        {
            m_active.Exit(slot);
            if (State != NodeManagerGenerationState.Live)
            {
                CompleteDrainIfReady();
            }
        }

        private void CompleteDrainIfReady()
        {
            if (!HasNoActiveOperations)
            {
                return;
            }
            lock (m_waitLock)
            {
                if (HasNoActiveOperations &&
                    State is NodeManagerGenerationState.Retiring or
                        NodeManagerGenerationState.Disposed or NodeManagerGenerationState.Aborted)
                {
                    // A later tentative entry must refuse itself; no accepted owner can reappear after this point.
                    m_drainCompleted = true;
                    m_drained?.TrySetResult(true);
                }
            }
        }

        private bool HasNoActiveOperations => m_active.Count == 0;

        /// <summary>
        /// Releases one successful gate admission.
        /// </summary>
        internal sealed class Lease : IDisposable
        {
            /// <summary>
            /// Initializes a lease for a striped counter slot.
            /// </summary>
            /// <param name="gate">The gate that owns the admission.</param>
            /// <param name="slot">The slot entered by the operation.</param>
            internal Lease(GenerationGate gate, int slot)
            {
                m_gate = gate;
                m_slot = slot;
            }

            /// <summary>
            /// Gets whether the lease is still active.
            /// </summary>
            internal bool IsActive => Volatile.Read(ref m_gate) is not null;

            /// <summary>
            /// Extends admitted work while its parent lease remains active, including after retirement.
            /// </summary>
            /// <param name="slot">The striped counter slot owned by the child on success.</param>
            /// <returns>Whether the parent was still active when the child entered.</returns>
            internal bool TryFork(out int slot)
            {
                GenerationGate? gate = Volatile.Read(ref m_gate);
                if (gate is null)
                {
                    slot = -1;
                    return false;
                }

                // Keep the accepted lineage in one stripe; a cross-stripe handoff can evade a drain scan.
                slot = m_slot;
                gate.m_active.Enter(slot);
                // The parent may have drained before this tentative entry. Only a still-live parent
                // can authorize resource access; a losing fork only unwinds its own counter slot.
                if (ReferenceEquals(Volatile.Read(ref m_gate), gate))
                {
                    return true;
                }
                gate.Exit(slot);
                slot = -1;
                return false;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_gate, null)?.Exit(m_slot);
            }

            /// <summary>The gate that owns the admission.</summary>
            private GenerationGate? m_gate;

            /// <summary>The striped counter slot entered by the operation.</summary>
            private readonly int m_slot;
        }

        /// <summary>The striped admission counter.</summary>
        private readonly StripedCounter m_active;

        /// <summary>Protects the optional asynchronous drain waiter.</summary>
        private readonly Lock m_waitLock = new();

        /// <summary>Completes when a closed gate has no remaining operations.</summary>
        private TaskCompletionSource<bool>? m_drained;

        /// <summary>Whether a closed gate reached zero accepted owners; protected by the drain lock.</summary>
        private bool m_drainCompleted;

        /// <summary>The current generation state.</summary>
        private int m_state;

        /// <summary>Notifies the private physical arbiter when lifecycle admission changes.</summary>
        private readonly Action? m_stateChanged;
    }

    /// <summary>
    /// A striped epoch that pins one immutable routing view.
    /// </summary>
    internal sealed class AddressSpaceEpoch
    {
        /// <summary>
        /// Associates this epoch with its routing view without extending the view's lifetime.
        /// </summary>
        internal void Attach(NodeManagerRoutingTable.RoutingSnapshot snapshot)
        {
            m_snapshot = new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(snapshot);
        }

        /// <summary>
        /// Attempts to recover the view while it remains reachable.
        /// </summary>
        internal bool TryGetSnapshot(out NodeManagerRoutingTable.RoutingSnapshot? snapshot)
        {
            snapshot = null;
            return m_snapshot?.TryGetTarget(out snapshot) == true;
        }

        /// <summary>
        /// Gets the stable diagnostic identity of this published view epoch.
        /// </summary>
        internal long Id { get; } = Interlocked.Increment(ref s_nextId);

        /// <summary>
        /// Attempts to pin this view while it is still published.
        /// </summary>
        /// <param name="slot">The counter slot on success.</param>
        /// <returns><c>true</c> when the view admitted the pin.</returns>
        internal bool TryPin(out int slot)
        {
            slot = m_active.Enter();
            if (Volatile.Read(ref m_closed) == 0)
            {
                return true;
            }
            Exit(slot);
            slot = -1;
            return false;
        }

        /// <summary>
        /// Prevents new pins from entering this view.
        /// </summary>
        internal void Close()
        {
            Interlocked.Exchange(ref m_closed, 1);
            CompleteDrainIfReady();
        }

        /// <summary>
        /// Gets whether this epoch is closed and has no active pins.
        /// </summary>
        internal bool IsDrained => Volatile.Read(ref m_closed) != 0 && m_active.Count == 0;

        /// <summary>
        /// Gets the number of requests currently pinning this view.
        /// </summary>
        internal long ActiveCount => m_active.Count;

        /// <summary>
        /// Waits for this view to close and for all admitted request pins to leave.
        /// </summary>
        /// <returns>A task that completes when the closed view has no active pins.</returns>
        internal Task WaitForDrainAsync()
        {
            if (IsDrained)
            {
                return Task.CompletedTask;
            }
            lock (m_waitLock)
            {
                if (IsDrained)
                {
                    return Task.CompletedTask;
                }
                return (m_drained ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        internal void Exit(int slot)
        {
            m_active.Exit(slot);
            CompleteDrainIfReady();
        }

        private void CompleteDrainIfReady()
        {
            if (Volatile.Read(ref m_closed) == 0 || !HasNoActivePins)
            {
                return;
            }
            lock (m_waitLock)
            {
                if (Volatile.Read(ref m_closed) != 0 && HasNoActivePins)
                {
                    m_drained?.TrySetResult(true);
                }
            }
        }

        private bool HasNoActivePins => m_active.Count == 0;

        /// <summary>The striped pin counter.</summary>
        private readonly StripedCounter m_active = new();

        /// <summary>The weak routing view that owns this epoch.</summary>
        private WeakReference<NodeManagerRoutingTable.RoutingSnapshot>? m_snapshot;

        /// <summary>Protects the optional asynchronous drain waiter.</summary>
        private readonly Lock m_waitLock = new();

        /// <summary>Completes when a closed epoch has no remaining pins.</summary>
        private TaskCompletionSource<bool>? m_drained;

        /// <summary>Whether this epoch has stopped admitting pins.</summary>
        private int m_closed;

        /// <summary>The next process-wide view epoch identity.</summary>
        private static long s_nextId;
    }
}
