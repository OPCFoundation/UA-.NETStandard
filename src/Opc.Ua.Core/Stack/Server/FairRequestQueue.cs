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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Bounded, weighted owner FIFO. The provider owns admission and non-borrowable
    /// partitions; this queue owns ordering, cancellation and the lifetime of its leases.
    /// </summary>
    /// <remarks>
    /// The provider's RequestQueue capacity must not exceed the physical maxQueuedRequests.
    /// Every queued entry holds a provider lease, so matching capacity envelopes preserve
    /// protected slots even when the shared partition is full.
    /// </remarks>
    internal sealed class FairRequestQueue : IDisposable
    {
        /// <summary>
        /// Creates a bounded owner queue sharing admission accounting with its provider.
        /// </summary>
        public FairRequestQueue(
            IServerResourceIsolationProvider provider,
            int maxQueuedRequests,
            long requestCost,
            bool decoupleHeldPublishRequests,
            Action<IEndpointIncomingRequest, StatusCode> complete)
        {
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (maxQueuedRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxQueuedRequests));
            }
            if (requestCost <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestCost));
            }
            m_maxQueuedRequests = maxQueuedRequests;
            m_requestCost = requestCost;
            m_decoupleHeldPublishRequests = decoupleHeldPublishRequests;
            m_complete = complete ?? throw new ArgumentNullException(nameof(complete));
            m_provider.CapacityAvailable += OnCapacityAvailable;
        }

        /// <summary>
        /// Gets the number of requests still waiting for execution admission.
        /// </summary>
        internal int Count
        {
            get
            {
                lock (m_gate)
                {
                    return m_count;
                }
            }
        }

        /// <summary>
        /// Gets the number of owners with queued requests.
        /// </summary>
        internal int OwnerCount
        {
            get
            {
                lock (m_gate)
                {
                    return m_owners.Count;
                }
            }
        }

        /// <summary>
        /// Gets the number of reader wake signals issued, excluding coalesced duplicate signals.
        /// </summary>
        internal long WakeSignalCount => Interlocked.Read(ref m_wakeSignalCount);

        /// <summary>
        /// Admits a decoded request without waiting. Every request is charged one maximum
        /// encoded-message footprint, retained through execution/parking until completion.
        /// This is a conservative admission cost, not an estimate of managed heap bytes.
        /// </summary>
        public bool TryEnqueue(
            IEndpointIncomingRequest request,
            CancellationToken cancellationToken,
            out StatusCode error)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                error = StatusCodes.BadRequestCancelledByClient;
                return false;
            }
            lock (m_gate)
            {
                if (m_stopped)
                {
                    error = StatusCodes.BadServerHalted;
                    return false;
                }
                if (m_count >= m_maxQueuedRequests)
                {
                    error = StatusCodes.BadServerTooBusy;
                    return false;
                }
            }

            ResourceIsolationOwner owner = Classify(request);
            if (!m_provider.TryAcquire(
                ResourceIsolationStage.RequestQueue, owner, 1, out IDisposable? queueLease, out _))
            {
                error = StatusCodes.BadServerTooBusy;
                return false;
            }

            IDisposable? costLease = null;
            IDisposable? parkedLease = null;
            try
            {
                if (!m_provider.TryAcquire(
                    ResourceIsolationStage.RequestQueueBytes, owner, m_requestCost, out costLease, out _))
                {
                    error = StatusCodes.BadServerTooBusy;
                    return false;
                }
                if (m_decoupleHeldPublishRequests &&
                    request is IParkableIncomingRequest { ParkSink: not null } &&
                    !m_provider.TryAcquire(
                        ResourceIsolationStage.ParkedRequest, owner, 1, out parkedLease, out _))
                {
                    error = StatusCodes.BadServerTooBusy;
                    return false;
                }

                using var admission = new PendingAdmission(
                    this, request, owner, queueLease, costLease!, parkedLease, cancellationToken);
                queueLease = null;
                costLease = null;
                parkedLease = null;
                Entry entry = admission.Entry;
                entry.RegisterCancellation();
                lock (m_gate)
                {
                    if (m_stopped)
                    {
                        error = StatusCodes.BadServerHalted;
                        return false;
                    }
                    if (cancellationToken.IsCancellationRequested)
                    {
                        error = StatusCodes.BadRequestCancelledByClient;
                        return false;
                    }
                    if (m_count >= m_maxQueuedRequests)
                    {
                        error = StatusCodes.BadServerTooBusy;
                        return false;
                    }
                    if (!m_owners.TryGetValue(owner.Key, out OwnerQueue? ownerQueue))
                    {
                        ownerQueue = new OwnerQueue(owner.Weight);
                        ownerQueue.RoundNode = m_round.AddLast(ownerQueue);
                        m_owners.Add(owner.Key, ownerQueue);
                    }
                    entry.OwnerQueue = ownerQueue;
                    entry.Node = ownerQueue.Requests.AddLast(entry);
                    m_count++;
                    admission.TransferToQueue();
                    Changed();
                }
                Pulse();
                error = StatusCodes.Good;
                return true;
            }
            finally
            {
                parkedLease?.Dispose();
                costLease?.Dispose();
                queueLease?.Dispose();
            }
        }

        /// <summary>
        /// Makes at most one attempt per currently eligible owner per capacity version.
        /// Provider calls and completions are deliberately outside the queue gate.
        /// </summary>
        public bool TryDequeue([NotNullWhen(true)] out Entry? result)
        {
            int attempts = OwnerCount;
            while (attempts-- > 0 && TrySelect(out Entry? candidate, out long version))
            {
                IDisposable? executionLease = null;
                StatusCode error = StatusCodes.Good;
                bool acquired = false;
                bool removed = false;
                try
                {
                    if (candidate.CancellationToken.IsCancellationRequested)
                    {
                        error = StatusCodes.BadRequestCancelledByClient;
                    }
                    else
                    {
                        error = GetRevalidationStatus(candidate);
                        if (!StatusCode.IsBad(error))
                        {
                            acquired = m_provider.TryAcquire(
                                ResourceIsolationStage.RequestExecution,
                                candidate.Owner,
                                1,
                                out executionLease,
                                out _);
                        }
                    }
                }
                catch
                {
                    lock (m_gate)
                    {
                        m_selecting = false;
                        removed = Remove(candidate);
                    }
                    Pulse();
                    executionLease?.Dispose();
                    if (removed)
                    {
                        candidate.Dispose();
                        m_complete(candidate.Request, StatusCodes.BadInternalError);
                    }
                    throw;
                }

                lock (m_gate)
                {
                    m_selecting = false;
                    if (candidate.Node != null)
                    {
                        if (acquired || StatusCode.IsBad(error))
                        {
                            removed = Remove(candidate, acquired);
                            if (acquired)
                            {
                                candidate.SetExecutionLease(executionLease!);
                                executionLease = null;
                            }
                        }
                        else
                        {
                            candidate.OwnerQueue!.LastAttempt = version;
                            Rotate(candidate.OwnerQueue);
                        }
                    }
                }
                executionLease?.Dispose();
                if (Volatile.Read(ref m_version) != version)
                {
                    Pulse();
                }

                if (removed)
                {
                    candidate.ReleaseQueueLease();
                    if (StatusCode.IsBad(error))
                    {
                        candidate.Dispose();
                        m_complete(candidate.Request, error);
                    }
                    else
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
            result = null;
            return false;
        }

        /// <summary>
        /// Waits for a request, waking at most one blocked reader per coalesced signal.
        /// </summary>
        public async ValueTask<Entry> DequeueAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    if (m_stopped)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                }
                if (TryDequeue(out Entry? entry))
                {
                    return entry;
                }
                try
                {
                    await m_signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        /// <summary>
        /// Stops waiters and rejects retained requests without holding the queue gate during callbacks.
        /// </summary>
        public void Dispose()
        {
            List<Entry> pending = [];
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                foreach (OwnerQueue owner in m_round)
                {
                    foreach (Entry entry in owner.Requests)
                    {
                        entry.Node = null;
                        entry.OwnerQueue = null;
                        pending.Add(entry);
                    }
                    owner.Requests.Clear();
                }
                m_round.Clear();
                m_owners.Clear();
                m_count = 0;
                Changed();
            }
            m_signals.Writer.TryComplete();
            m_provider.CapacityAvailable -= OnCapacityAvailable;
            foreach (Entry entry in pending)
            {
                entry.Dispose();
                m_complete(entry.Request, StatusCodes.BadServerHalted);
            }
        }

        /// <summary>
        /// Resolves authoritative owner and service intent for admission.
        /// </summary>
        private ResourceIsolationOwner Classify(IEndpointIncomingRequest request)
        {
            return m_provider.Classify(
                request.SecureChannelContext,
                request.Request.RequestHeader.AuthenticationToken,
                IsSessionEstablishment(request.Request),
                IsControlRequest(request.Request));
        }

        /// <summary>
        /// Revalidates admission without silently promoting stale leases.
        /// </summary>
        private StatusCode GetRevalidationStatus(Entry entry)
        {
            if (m_provider is IResourceIsolationRevalidationProvider revalidation)
            {
                return revalidation.GetRevalidationStatus(
                    entry.Owner,
                    entry.Request.SecureChannelContext,
                    entry.Request.Request.RequestHeader.AuthenticationToken,
                    IsSessionEstablishment(entry.Request.Request),
                    IsControlRequest(entry.Request.Request));
            }
            return m_provider.IsCurrent(
                entry.Owner,
                entry.Request.SecureChannelContext,
                entry.Request.Request.RequestHeader.AuthenticationToken,
                IsSessionEstablishment(entry.Request.Request),
                IsControlRequest(entry.Request.Request)) ? StatusCodes.Good : StatusCodes.BadSessionIdInvalid;
        }

        /// <summary>
        /// Identifies services that establish session continuity.
        /// </summary>
        private static bool IsSessionEstablishment(IServiceRequest request)
        {
            return request is CreateSessionRequest or ActivateSessionRequest;
        }

        /// <summary>
        /// Identifies recovery and cancellation control services.
        /// </summary>
        private static bool IsControlRequest(IServiceRequest request)
        {
            return request is CancelRequest or CloseSessionRequest;
        }

        /// <summary>
        /// Selects one eligible owner head while excluding concurrent provider admission attempts.
        /// </summary>
        private bool TrySelect([NotNullWhen(true)] out Entry? entry, out long version)
        {
            lock (m_gate)
            {
                version = Volatile.Read(ref m_version);
                if (!m_stopped && !m_selecting)
                {
                    int remaining = m_round.Count;
                    while (remaining-- > 0 && m_round.First != null)
                    {
                        OwnerQueue owner = m_round.First.Value;
                        if (owner.LastAttempt != version)
                        {
                            entry = owner.Requests.First!.Value;
                            m_selecting = true;
                            return true;
                        }
                        Rotate(owner);
                    }
                }
                entry = null;
                return false;
            }
        }

        /// <summary>
        /// Removes a linked request while the caller holds the queue gate.
        /// </summary>
        private bool Remove(Entry entry, bool consumeTurn = false)
        {
            if (entry.Node == null)
            {
                return false;
            }
            OwnerQueue owner = entry.OwnerQueue!;
            owner.Requests.Remove(entry.Node);
            entry.Node = null;
            entry.OwnerQueue = null;
            m_count--;
            if (owner.Requests.Count == 0)
            {
                m_round.Remove(owner.RoundNode!);
                m_owners.Remove(entry.Owner.Key);
            }
            else if (consumeTurn && --owner.Remaining == 0)
            {
                Rotate(owner);
            }
            Changed();
            return true;
        }

        /// <summary>
        /// Advances an owner to the next weighted round under the queue gate.
        /// </summary>
        private void Rotate(OwnerQueue owner)
        {
            m_round.Remove(owner.RoundNode!);
            m_round.AddLast(owner.RoundNode!);
            owner.Remaining = owner.Weight;
        }

        /// <summary>
        /// Removes cancelled queued work before completing it outside the queue gate.
        /// </summary>
        private void Cancel(Entry entry)
        {
            bool removed;
            lock (m_gate)
            {
                removed = Remove(entry);
            }
            if (removed)
            {
                Pulse();
                entry.Dispose();
                m_complete(entry.Request, StatusCodes.BadRequestCancelledByClient);
            }
        }

        /// <summary>
        /// Coalesces execution releases without entering the queue gate on a provider callback.
        /// Other stages are fail-fast admission stages and never block a dequeue.
        /// </summary>
        private void OnCapacityAvailable(ResourceIsolationStage stage)
        {
            if (stage == ResourceIsolationStage.RequestExecution)
            {
                Changed();
                Pulse();
            }
        }

        /// <summary>
        /// Invalidates failed owner attempts, including releases concurrent with a selection.
        /// </summary>
        private void Changed()
        {
            Interlocked.Increment(ref m_version);
        }

        /// <summary>
        /// Retains at most one wake token and hands it to only one reader, never broadcasting.
        /// </summary>
        private void Pulse()
        {
            if (Volatile.Read(ref m_count) > 0 && !Volatile.Read(ref m_stopped) &&
                m_signals.Writer.TryWrite(true))
            {
                Interlocked.Increment(ref m_wakeSignalCount);
            }
        }

        /// <summary>
        /// Owns the admission leases for one request through queueing, execution and parking.
        /// </summary>
        internal sealed class Entry : IDisposable
        {
            /// <summary>
            /// Takes ownership of a request's acquired admission leases.
            /// </summary>
            internal Entry(
                FairRequestQueue queue,
                IEndpointIncomingRequest request,
                ResourceIsolationOwner owner,
                IDisposable queueLease,
                IDisposable costLease,
                IDisposable? parkedLease,
                CancellationToken cancellationToken)
            {
                m_queue = queue;
                Request = request;
                Owner = owner;
                CancellationToken = cancellationToken;
                m_queueLease = queueLease;
                m_costLease = costLease;
                m_parkedLease = parkedLease;
            }

            /// <summary>
            /// Gets the admitted request.
            /// </summary>
            public IEndpointIncomingRequest Request { get; }

            /// <summary>
            /// Gets the immutable classification that owns this entry's leases.
            /// </summary>
            public ResourceIsolationOwner Owner { get; }

            /// <summary>
            /// Gets the caller-controlled request lifetime.
            /// </summary>
            public CancellationToken CancellationToken { get; }

            /// <summary>
            /// Gets or sets the owning FIFO while linked under the queue gate.
            /// </summary>
            internal OwnerQueue? OwnerQueue { get; set; }

            /// <summary>
            /// Gets or sets the FIFO node; null means no longer queued.
            /// </summary>
            internal LinkedListNode<Entry>? Node { get; set; }

            /// <summary>
            /// Checks whether dispatch may use the existing classification.
            /// </summary>
            public bool IsCurrent()
            {
                return StatusCode.IsGood(GetRevalidationStatus());
            }

            /// <summary>
            /// Gets the dispatch validity result without renewing classification or leases.
            /// </summary>
            public StatusCode GetRevalidationStatus()
            {
                return m_queue.GetRevalidationStatus(this);
            }

            /// <summary>
            /// Returns execution capacity exactly once, including when the request parks.
            /// </summary>
            public void ReleaseExecution()
            {
                Interlocked.Exchange(ref m_executionLease, null)?.Dispose();
            }

            /// <summary>
            /// Unregisters cancellation and returns all remaining admission leases.
            /// </summary>
            public void Dispose()
            {
                CancellationTokenRegistration registration;
                lock (m_registrationGate)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    m_disposed = true;
                    registration = m_registration;
                }
                registration.Dispose();
                ReleaseQueueLease();
                ReleaseExecution();
                Interlocked.Exchange(ref m_parkedLease, null)?.Dispose();
                Interlocked.Exchange(ref m_costLease, null)?.Dispose();
            }

            /// <summary>
            /// Takes execution lease ownership before the entry leaves the queue gate.
            /// </summary>
            internal void SetExecutionLease(IDisposable executionLease)
            {
                m_executionLease = executionLease;
            }

            /// <summary>
            /// Returns the queue slot without releasing the retained-request footprint.
            /// </summary>
            internal void ReleaseQueueLease()
            {
                Interlocked.Exchange(ref m_queueLease, null)?.Dispose();
            }

            /// <summary>
            /// Registers removal safely even when cancellation runs before registration returns.
            /// </summary>
            internal void RegisterCancellation()
            {
                CancellationTokenRegistration registration = CancellationToken.Register(() => m_queue.Cancel(this));
                bool disposed;
                lock (m_registrationGate)
                {
                    disposed = m_disposed;
                    if (!disposed)
                    {
                        m_registration = registration;
                    }
                }
                if (disposed)
                {
                    registration.Dispose();
                }
            }

            /// <summary>
            /// Queue that controls ordering and classification validity.
            /// </summary>
            private readonly FairRequestQueue m_queue;

            /// <summary>
            /// Serializes registration transfer against entry disposal.
            /// </summary>
            private readonly Lock m_registrationGate = new();

            /// <summary>
            /// Retained queue-slot lease.
            /// </summary>
            private IDisposable? m_queueLease;

            /// <summary>
            /// Retained decoded-request cost lease.
            /// </summary>
            private IDisposable? m_costLease;

            /// <summary>
            /// Active execution lease, exchanged on park or completion.
            /// </summary>
            private IDisposable? m_executionLease;

            /// <summary>
            /// Reserved parked-request lease.
            /// </summary>
            private IDisposable? m_parkedLease;

            /// <summary>
            /// Registration owned until disposal starts.
            /// </summary>
            private CancellationTokenRegistration m_registration;

            /// <summary>
            /// Whether registration ownership has been released.
            /// </summary>
            private bool m_disposed;
        }

        /// <summary>
        /// Tracks one owner's FIFO and weighted turn under the queue gate.
        /// </summary>
        internal sealed class OwnerQueue(int weight)
        {
            /// <summary>
            /// Gets the owner's configured scheduling weight.
            /// </summary>
            public int Weight { get; } = weight;

            /// <summary>
            /// Gets or sets successful dispatches remaining in this turn.
            /// </summary>
            public int Remaining { get; set; } = weight;

            /// <summary>
            /// Gets or sets the capacity version of the last unsuccessful admission.
            /// </summary>
            public long LastAttempt { get; set; } = -1;

            /// <summary>
            /// Gets queued requests in admission order.
            /// </summary>
            public LinkedList<Entry> Requests { get; } = [];

            /// <summary>
            /// Gets or sets this owner's node in the scheduling round.
            /// </summary>
            public LinkedListNode<OwnerQueue>? RoundNode { get; set; }
        }

        /// <summary>
        /// Owns an entry until it is published. A rejected or interrupted admission
        /// disposes its entry; after publication the queue owns the same entry.
        /// </summary>
        private sealed class PendingAdmission : IDisposable
        {
            /// <summary>
            /// Creates the entry that this admission owns until publication.
            /// </summary>
            public PendingAdmission(
                FairRequestQueue queue,
                IEndpointIncomingRequest request,
                ResourceIsolationOwner owner,
                IDisposable queueLease,
                IDisposable costLease,
                IDisposable? parkedLease,
                CancellationToken cancellationToken)
            {
                m_entry = new Entry(
                    queue, request, owner, queueLease, costLease, parkedLease, cancellationToken);
            }

            /// <summary>
            /// Gets the entry before ownership transfers to the queue.
            /// </summary>
            public Entry Entry => m_entry ??
                throw new InvalidOperationException("Admission ownership has already transferred.");

            /// <summary>
            /// Relinquishes entry ownership after publication under the queue gate.
            /// </summary>
            public void TransferToQueue()
            {
                m_entry = null;
            }

            /// <summary>
            /// Returns leases if publication failed or was interrupted.
            /// </summary>
            public void Dispose()
            {
                m_entry?.Dispose();
                m_entry = null;
            }

            /// <summary>
            /// Entry owned by this unpublished admission.
            /// </summary>
            private Entry? m_entry;
        }

        /// <summary>
        /// Shared admission provider borrowed for the lifetime of this queue.
        /// </summary>
        private readonly IServerResourceIsolationProvider m_provider;

        /// <summary>
        /// Physical queue count envelope.
        /// </summary>
        private readonly int m_maxQueuedRequests;

        /// <summary>
        /// Conservative retained footprint charged for each admitted request.
        /// </summary>
        private readonly long m_requestCost;

        /// <summary>
        /// Whether parkable requests reserve an independent parked slot.
        /// </summary>
        private readonly bool m_decoupleHeldPublishRequests;

        /// <summary>
        /// Reports terminal outcomes outside queue and provider gates.
        /// </summary>
        private readonly Action<IEndpointIncomingRequest, StatusCode> m_complete;

        /// <summary>
        /// Protects queue membership and weighted selection state.
        /// </summary>
        private readonly Lock m_gate = new();

        /// <summary>
        /// Owner FIFOs retained only while they contain queued requests.
        /// </summary>
        private readonly Dictionary<string, OwnerQueue> m_owners = new(StringComparer.Ordinal);

        /// <summary>
        /// Current weighted scheduling round.
        /// </summary>
        private readonly LinkedList<OwnerQueue> m_round = [];

        /// <summary>
        /// Single coalesced wake token; a successful dispatch passes it to another waiter.
        /// </summary>
        private readonly Channel<bool> m_signals = Channel.CreateBounded<bool>(1);

        /// <summary>
        /// Number of linked requests.
        /// </summary>
        private int m_count;

        /// <summary>
        /// Atomic generation shared by queue mutations and execution-capacity releases.
        /// </summary>
        private long m_version;

        /// <summary>
        /// Diagnostic count of bounded reader wake signals successfully issued.
        /// </summary>
        private long m_wakeSignalCount;

        /// <summary>
        /// Whether an owner head is currently undergoing provider admission outside the gate.
        /// </summary>
        private bool m_selecting;

        /// <summary>
        /// Whether admissions and dispatch have stopped.
        /// </summary>
        private bool m_stopped;
    }
}
