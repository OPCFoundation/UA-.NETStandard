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
                    else if (!IsCurrent(candidate))
                    {
                        error = StatusCodes.BadSessionIdInvalid;
                    }
                    else
                    {
                        acquired = m_provider.TryAcquire(
                            ResourceIsolationStage.RequestExecution,
                            candidate.Owner,
                            1,
                            out executionLease,
                            out _);
                    }
                }
                catch
                {
                    lock (m_gate)
                    {
                        m_selecting = false;
                        removed = Remove(candidate);
                        Pulse();
                    }
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
                    Pulse();
                }
                executionLease?.Dispose();

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

        public async ValueTask<Entry> DequeueAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (m_gate)
                {
                    if (m_stopped)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
                    changed = m_changed.Task;
                }
                if (TryDequeue(out Entry? entry))
                {
                    return entry;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

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
            m_provider.CapacityAvailable -= OnCapacityAvailable;
            foreach (Entry entry in pending)
            {
                entry.Dispose();
                m_complete(entry.Request, StatusCodes.BadServerHalted);
            }
        }

        private ResourceIsolationOwner Classify(IEndpointIncomingRequest request)
        {
            return m_provider.Classify(
                request.SecureChannelContext,
                request.Request.RequestHeader.AuthenticationToken,
                IsSessionEstablishment(request.Request),
                IsControlRequest(request.Request));
        }

        private bool IsCurrent(Entry entry)
        {
            return m_provider.IsCurrent(
                entry.Owner,
                entry.Request.SecureChannelContext,
                entry.Request.Request.RequestHeader.AuthenticationToken,
                IsSessionEstablishment(entry.Request.Request),
                IsControlRequest(entry.Request.Request));
        }

        private static bool IsSessionEstablishment(IServiceRequest request)
        {
            return request is CreateSessionRequest or ActivateSessionRequest;
        }

        private static bool IsControlRequest(IServiceRequest request)
        {
            return request is CancelRequest or CloseSessionRequest;
        }

        private bool TrySelect([NotNullWhen(true)] out Entry? entry, out long version)
        {
            lock (m_gate)
            {
                version = m_version;
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

        private void Rotate(OwnerQueue owner)
        {
            m_round.Remove(owner.RoundNode!);
            m_round.AddLast(owner.RoundNode!);
            owner.Remaining = owner.Weight;
        }

        private void Cancel(Entry entry)
        {
            bool removed;
            lock (m_gate)
            {
                removed = Remove(entry);
            }
            if (removed)
            {
                entry.Dispose();
                m_complete(entry.Request, StatusCodes.BadRequestCancelledByClient);
            }
        }

        private void OnCapacityAvailable()
        {
            lock (m_gate)
            {
                if (!m_stopped)
                {
                    Changed();
                }
            }
        }

        private void Changed()
        {
            m_version++;
            Pulse();
        }

        private void Pulse()
        {
            TaskCompletionSource<bool> changed = m_changed;
            m_changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult(true);
        }

        internal sealed class Entry : IDisposable
        {
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

            public IEndpointIncomingRequest Request { get; }
            public ResourceIsolationOwner Owner { get; }
            public CancellationToken CancellationToken { get; }
            internal OwnerQueue? OwnerQueue { get; set; }
            internal LinkedListNode<Entry>? Node { get; set; }

            public bool IsCurrent()
            {
                return m_queue.IsCurrent(this);
            }

            public void ReleaseExecution()
            {
                Interlocked.Exchange(ref m_executionLease, null)?.Dispose();
            }

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

            internal void SetExecutionLease(IDisposable executionLease)
            {
                m_executionLease = executionLease;
            }

            internal void ReleaseQueueLease()
            {
                Interlocked.Exchange(ref m_queueLease, null)?.Dispose();
            }

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

            private readonly FairRequestQueue m_queue;
            private readonly Lock m_registrationGate = new();
            private IDisposable? m_queueLease;
            private IDisposable? m_costLease;
            private IDisposable? m_executionLease;
            private IDisposable? m_parkedLease;
            private CancellationTokenRegistration m_registration;
            private bool m_disposed;
        }

        internal sealed class OwnerQueue(int weight)
        {
            public int Weight { get; } = weight;
            public int Remaining { get; set; } = weight;
            public long LastAttempt { get; set; } = -1;
            public LinkedList<Entry> Requests { get; } = [];
            public LinkedListNode<OwnerQueue>? RoundNode { get; set; }
        }

        /// <summary>
        /// Owns an entry until it is published. A rejected or interrupted admission
        /// disposes its entry; after publication the queue owns the same entry.
        /// </summary>
        private sealed class PendingAdmission : IDisposable
        {
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

            public Entry Entry => m_entry ??
                throw new InvalidOperationException("Admission ownership has already transferred.");

            public void TransferToQueue()
            {
                m_entry = null;
            }

            public void Dispose()
            {
                m_entry?.Dispose();
                m_entry = null;
            }

            private Entry? m_entry;
        }

        private readonly IServerResourceIsolationProvider m_provider;
        private readonly int m_maxQueuedRequests;
        private readonly long m_requestCost;
        private readonly bool m_decoupleHeldPublishRequests;
        private readonly Action<IEndpointIncomingRequest, StatusCode> m_complete;
        private readonly Lock m_gate = new();
        private readonly Dictionary<string, OwnerQueue> m_owners = new(StringComparer.Ordinal);
        private readonly LinkedList<OwnerQueue> m_round = [];
        private TaskCompletionSource<bool> m_changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int m_count;
        private long m_version;
        private bool m_selecting;
        private bool m_stopped;
    }
}
