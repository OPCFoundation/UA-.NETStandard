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
using System.Runtime.CompilerServices;
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
        /// Gets the currently published generation.
        /// </summary>
        internal NodeManagerGeneration? Current => Volatile.Read(ref m_current);

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

        /// <summary>Protects epoch registration, which occurs only while publishing views.</summary>
        private readonly Lock m_epochLock = new();

        /// <summary>The un-drained views that have routed this generation.</summary>
        private readonly List<AddressSpaceEpoch> m_viewEpochs = [];
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
            lock (s_lock)
            {
                if (s_generations.TryGetValue(nodeManager, out NodeManagerGeneration? generation))
                {
                    return generation;
                }
                var slot = new NodeManagerSlot();
                generation = new NodeManagerGeneration(
                    slot, 1, nodeManager, NodeManagerGenerationState.Live);
                s_generations.Add(nodeManager, generation);
                slot.Publish(generation);
                return generation;
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
            lock (s_lock)
            {
                if (s_generations.TryGetValue(replacement, out NodeManagerGeneration? existing))
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
                        existing.Gate.Restore();
                        return existing;
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
                    s_generations.Remove(replacement);
                }
                var generation = new NodeManagerGeneration(
                    current.Slot,
                    current.Number + 1,
                    replacement,
                    NodeManagerGenerationState.Prepared);
                s_generations.Add(replacement, generation);
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
            lock (s_lock)
            {
                if (s_generations.TryGetValue(nodeManager, out NodeManagerGeneration? existing))
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
                    s_generations.Remove(nodeManager);
                }
                var slot = new NodeManagerSlot();
                var generation = new NodeManagerGeneration(
                    slot, 1, nodeManager, NodeManagerGenerationState.Prepared);
                s_generations.Add(nodeManager, generation);
                return generation;
            }
        }

        /// <summary>Serializes generation identity changes on writer paths.</summary>
        private static readonly Lock s_lock = new();

        /// <summary>Associates each manager instance with its current generation identity.</summary>
        private static readonly ConditionalWeakTable<IAsyncNodeManager, NodeManagerGeneration> s_generations = new();
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
            }
        }

        /// <summary>
        /// Marks a retired generation disposed after its owners have drained.
        /// </summary>
        internal void DisposeGeneration()
        {
            if (ActiveCount != 0)
            {
                throw new InvalidOperationException("A generation with active operations cannot be disposed.");
            }
            Transition(NodeManagerGenerationState.Retiring, NodeManagerGenerationState.Disposed);
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
                if (HasNoActiveOperations)
                {
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
            Volatile.Write(ref m_closed, 1);
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
