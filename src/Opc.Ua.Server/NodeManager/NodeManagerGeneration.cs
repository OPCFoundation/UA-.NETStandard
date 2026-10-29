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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    internal sealed class NodeManagerGeneration
    {
        /// <summary>
        /// Initializes a generation.
        /// </summary>
        /// <param name="slot">The stable registration slot.</param>
        /// <param name="number">The generation number within the slot.</param>
        /// <param name="nodeManager">The wrapped node manager.</param>
        /// <param name="state">The initial lifecycle state.</param>
        internal NodeManagerGeneration(
            NodeManagerSlot slot,
            long number,
            IAsyncNodeManager nodeManager,
            NodeManagerGenerationState state)
        {
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            Number = number;
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            Gate = new GenerationGate(state);
            Reference = new NodeManagerGenerationReference(this);
            SessionBindings = new ConcurrentDictionary<NodeId, GenerationSessionBindingRecord>();
            MonitoredItems = new ConcurrentDictionary<IMonitoredItem, ISubscription>(
                MonitoredItemReferenceComparer.Instance);
            EventBindings = new ConcurrentDictionary<IEventMonitoredItem, GenerationEventBindingRecord>(
                MonitoredItemReferenceComparer.Instance);
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
        /// Gets or sets whether external consumers must wait for post-publication initialization.
        /// </summary>
        internal bool ReadinessPending
        {
            get => Volatile.Read(ref m_readinessPending) != 0;
            set => Volatile.Write(ref m_readinessPending, value ? 1 : 0);
        }

        /// <summary>
        /// Gets the number of retained events dropped because the bounded retention was full.
        /// </summary>
        internal long DroppedDeferredEvents => Interlocked.Read(ref m_droppedDeferredEvents);

        /// <summary>
        /// Retains an event for items not bound to this generation until its readiness completes.
        /// </summary>
        /// <param name="delivery">The delivery to run once readiness completed.</param>
        /// <returns><c>false</c> when readiness already completed and the caller must deliver now.</returns>
        internal bool TryDeferEvent(Func<Task> delivery)
        {
            lock (m_deferredEventsLock)
            {
                if (!ReadinessPending)
                {
                    return false;
                }
                m_deferredEvents ??= new Queue<Func<Task>>();
                if (m_deferredEvents.Count >= MaxDeferredEvents)
                {
                    m_deferredEvents.Dequeue();
                    Interlocked.Increment(ref m_droppedDeferredEvents);
                }
                m_deferredEvents.Enqueue(delivery);
                return true;
            }
        }

        /// <summary>
        /// Delivers the events retained while the readiness was pending, in emission order.
        /// </summary>
        /// <returns>The completion of the retained deliveries.</returns>
        /// <exception cref="AggregateException">
        /// One or more deliveries failed. The remaining retained events were still delivered.
        /// </exception>
        internal async Task FlushDeferredEventsAsync()
        {
            Func<Task>[] pending;
            lock (m_deferredEventsLock)
            {
                if (m_deferredEvents is not { Count: > 0 })
                {
                    return;
                }
                pending = [.. m_deferredEvents];
                m_deferredEvents.Clear();
            }
            List<Exception>? failures = null;
            foreach (Func<Task> delivery in pending)
            {
                try
                {
                    await delivery().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
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
                if (m_shutdownPreparation is not null)
                {
                    return false;
                }
                Gate.Restore();
                return true;
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
                if (EventBindingsSealed)
                {
                    return unsubscribe &&
                        EventBindings.TryGetValue(monitoredItem, out GenerationEventBindingRecord? record)
                        ? record
                        : null;
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
                if (m_retirementResourcesSealed ||
                    Gate.State is not (NodeManagerGenerationState.Retiring or NodeManagerGenerationState.Aborted) ||
                    Gate.WaitForDrainAsync().IsCompleted)
                {
                    return false;
                }
                m_retirementResources ??= [];
                foreach (IDisposable current in m_retirementResources)
                {
                    if (ReferenceEquals(current, resource))
                    {
                        return true;
                    }
                }
                m_retirementResources.Add(resource);
                return true;
            }
        }

        /// <summary>
        /// Disposes retired consumers after the generation's existing epochs and gate have drained.
        /// Failed resources remain owned for the next reclamation attempt.
        /// </summary>
        internal void DisposeRetiredResources()
        {
            IDisposable[] resources;
            lock (m_retirementResourcesLock)
            {
                if (!IsQuiescent)
                {
                    throw new InvalidOperationException("Retired consumers cannot be disposed before quiescence.");
                }
                m_retirementResourcesSealed = true;
                resources = m_retirementResources?.ToArray() ?? [];
            }
            foreach (IDisposable resource in resources)
            {
                resource.Dispose();
                lock (m_retirementResourcesLock)
                {
                    m_retirementResources!.RemoveAt(0);
                }
            }
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
        internal async ValueTask CompleteSourceReleasesAsync(
            IMonitoredItem? item,
            CancellationToken ct,
            bool joinActiveCleanup = false)
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
                try
                {
                    await release.CompleteAsync(joinActiveCleanup, ct).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException &&
                    !(failure is OperationCanceledException && ct.IsCancellationRequested))
                {
                    (failures ??= []).Add(failure);
                    continue;
                }
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
            if (failures is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures is { Count: > 1 })
            {
                throw new AggregateException("Monitored-item source-provider cleanup failed.", failures);
            }
        }

        /// <summary>Protects epoch registration, which occurs only while publishing views.</summary>
        private readonly Lock m_epochLock = new();

        /// <summary>The un-drained views that have routed this generation.</summary>
        private readonly List<AddressSpaceEpoch> m_viewEpochs = [];

        /// <summary>Serializes binding-record admission with retirement sealing.</summary>
        private readonly Lock m_eventBindingsLock = new();

        /// <summary>Non-zero once the all-events bindings were sealed at retirement.</summary>
        private int m_eventBindingsSealed;

        /// <summary>Nonzero until the committed generation's readiness callback has finished.</summary>
        private int m_readinessPending;
        private const int MaxDeferredEvents = 1024;
        private readonly Lock m_deferredEventsLock = new();
        private Queue<Func<Task>>? m_deferredEvents;
        private long m_droppedDeferredEvents;

        /// <summary>Serializes cold-path disposal transfer with the reclamation handoff.</summary>
        private readonly Lock m_retirementResourcesLock = new();

        /// <summary>Consumers awaiting physical reclamation, not additional lifetime admissions.</summary>
        private List<IDisposable>? m_retirementResources;

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
        }

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
        /// <returns><c>true</c> when slot and generation numbers match.</returns>
        internal bool Identifies(NodeManagerGeneration generation)
        {
            return SlotId == generation.Slot.Id && Number == generation.Number;
        }

        /// <summary>Weakly references the generation without keeping it or its manager alive.</summary>
        private readonly WeakReference<NodeManagerGeneration> m_generation;

        /// <summary>Weakly references the stable slot so its assigned identifier remains current.</summary>
        private readonly WeakReference<NodeManagerSlot> m_slot;
    }

    /// <summary>
    /// Associates manager instances with their exact generation identity.
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
        /// Gets the live generation associated with a manager, creating its initial slot if needed.
        /// </summary>
        /// <param name="nodeManager">The manager to resolve.</param>
        /// <returns>The manager's generation.</returns>
        internal static NodeManagerGeneration GetOrCreate(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }
            IAsyncNodeManager identity = GetIdentity(nodeManager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out NodeManagerGeneration? generation))
                {
                    return generation;
                }
                var slot = new NodeManagerSlot();
                generation = new NodeManagerGeneration(
                    slot, 1, nodeManager, NodeManagerGenerationState.Live);
                s_generations.Add(identity, generation);
                slot.Publish(generation);
                return generation;
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
            IAsyncNodeManager identity = GetIdentity(nodeManager);
            lock (s_lock)
            {
                return s_generations.TryGetValue(identity, out generation);
            }
        }

        /// <summary>
        /// Creates the prepared generation that replaces the supplied current generation.
        /// </summary>
        /// <param name="current">The generation being replaced.</param>
        /// <param name="replacement">The replacement manager.</param>
        /// <returns>The prepared replacement generation.</returns>
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
            IAsyncNodeManager identity = GetIdentity(replacement);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out NodeManagerGeneration? existing))
                {
                    if (ReferenceEquals(existing.Slot, current.Slot) &&
                        existing.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        return existing;
                    }
                    if (existing.Gate.State == NodeManagerGenerationState.Live)
                    {
                        throw new InvalidOperationException(
                            "A live manager cannot be assigned to another generation slot.");
                    }
                    if (existing.Gate.State == NodeManagerGenerationState.Retiring &&
                        ReferenceEquals(existing.Slot, current.Slot))
                    {
                        if (existing.TryRestoreBeforeShutdown())
                        {
                            return existing;
                        }
                        throw new InvalidOperationException(
                            "A retired NodeManager instance cannot be reused as a replacement.");
                    }
                    if (existing.Gate.State == NodeManagerGenerationState.Disposed ||
                        (existing.Gate.State == NodeManagerGenerationState.Retiring &&
                            !ReferenceEquals(existing.Slot, current.Slot)))
                    {
                        throw new InvalidOperationException(
                            "A retired NodeManager instance cannot be reused as a replacement.");
                    }
                    if (existing.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        existing.Gate.Abort();
                    }
                    s_generations.Remove(identity);
                }
                var generation = new NodeManagerGeneration(
                    current.Slot,
                    current.Number + 1,
                    replacement,
                    NodeManagerGenerationState.Prepared);
                s_generations.Add(identity, generation);
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
            IAsyncNodeManager identity = GetIdentity(nodeManager);
            lock (s_lock)
            {
                if (s_generations.TryGetValue(identity, out NodeManagerGeneration? existing))
                {
                    if (existing.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        return existing;
                    }
                    if (existing.Gate.State is NodeManagerGenerationState.Retiring or
                        NodeManagerGenerationState.Disposed)
                    {
                        throw new InvalidOperationException(
                            "A retired NodeManager instance cannot be registered again.");
                    }
                    if (existing.Gate.State != NodeManagerGenerationState.Aborted)
                    {
                        return existing;
                    }
                    s_generations.Remove(identity);
                }
                var slot = new NodeManagerSlot();
                var generation = new NodeManagerGeneration(
                    slot, 1, nodeManager, NodeManagerGenerationState.Prepared);
                s_generations.Add(identity, generation);
                return generation;
            }
        }

        /// <summary>
        /// Uses one resource identity for public wrappers without replacing the generation's owning facade.
        /// </summary>
        /// <param name="nodeManager">The facade supplied by the owner.</param>
        /// <returns>The canonical resource key.</returns>
        internal static IAsyncNodeManager GetIdentity(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is not AsyncNodeManagerAdapter adapter)
            {
                return nodeManager;
            }
            if (adapter.SyncNodeManager is SyncNodeManagerAdapter synchronous)
            {
                return GetIdentity(synchronous.AsyncNodeManager);
            }
            return adapter.SyncNodeManager.ToAsyncNodeManager();
        }

        /// <summary>Serializes generation identity changes on writer paths.</summary>
        private static readonly Lock s_lock = new();

        /// <summary>Associates each manager instance with its current generation identity.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, NodeManagerGeneration> s_generations = new();

        /// <summary>Scopes restored ownerless items to the lifetime and identity of their server.</summary>
        private static readonly ConditionalWeakTable<IServerInternal, NodeManagerSlot> s_ownerlessSlots = new();
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
        internal GenerationGate(NodeManagerGenerationState state)
        {
            m_state = (int)state;
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
        private readonly StripedCounter m_active = new();

        /// <summary>Protects the optional asynchronous drain waiter.</summary>
        private readonly Lock m_waitLock = new();

        /// <summary>Completes when a closed gate has no remaining operations.</summary>
        private TaskCompletionSource<bool>? m_drained;

        /// <summary>Whether a closed gate reached zero accepted owners; protected by the drain lock.</summary>
        private bool m_drainCompleted;

        /// <summary>The current generation state.</summary>
        private int m_state;
    }

    /// <summary>
    /// A striped epoch that pins one immutable routing view.
    /// </summary>
    internal sealed class AddressSpaceEpoch
    {
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
        /// Waits for all request pins to leave this view.
        /// </summary>
        /// <returns>A task that completes when the active pin count is zero.</returns>
        internal Task WaitForDrainAsync()
        {
            if (HasNoActivePins)
            {
                return Task.CompletedTask;
            }
            lock (m_waitLock)
            {
                if (HasNoActivePins)
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

        /// <summary>Protects the optional asynchronous drain waiter.</summary>
        private readonly Lock m_waitLock = new();

        /// <summary>Completes when a closed epoch has no remaining pins.</summary>
        private TaskCompletionSource<bool>? m_drained;

        /// <summary>Whether this epoch has stopped admitting pins.</summary>
        private int m_closed;
    }
}
