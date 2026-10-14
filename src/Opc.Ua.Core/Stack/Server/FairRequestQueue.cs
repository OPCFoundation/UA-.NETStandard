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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Bounded admission with weighted owner FIFOs or one global FIFO. The provider owns
    /// admission and non-borrowable partitions; this queue owns ordering and lease lifetimes.
    /// </summary>
    /// <remarks>
    /// The provider's RequestQueue capacity must not exceed the physical bound. A provider that
    /// reports its capacity raises the bound from maxQueuedRequests to that capacity, because the
    /// default plan adds protected floors on top of the configured queue size. Every queued entry
    /// holds a provider lease, so matching capacity envelopes preserve protected slots even when
    /// the shared partition is full.
    /// Admission and dispatch take no lock. Each owner has a lock-free FIFO, and the weighted
    /// round robin runs over a lock-free ready queue of turn tokens: an owner holds one token per
    /// queued request, up to its weight per turn, and queues for its next turn behind the other
    /// owners when the last token of a turn is taken. The weighting holds over time, not for every
    /// window: a worker preempted between taking an owner's last token and publishing its next
    /// turn leaves that owner without a queued token for the length of the stall, the
    /// work-conserving dispatch serves the other owners meanwhile, and the lost share is not made
    /// up afterwards. A request is claimed exactly once, by
    /// selection, cancellation or stop. The count bound is reserved atomically. Only a head that
    /// failed execution admission and the retirement of an empty owner FIFO use the owner's gate.
    /// </remarks>
    internal sealed class FairRequestQueue : IDisposable
    {
        /// <summary>
        /// Creates a bounded queue sharing admission accounting with its provider.
        /// The provider's scheduling flag selects weighted owner order or strict global FIFO.
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
            m_maxQueuedRequests = provider is IResourceIsolationCapacitySource capacitySource
                ? (int)Math.Min(int.MaxValue, Math.Max(
                    maxQueuedRequests, capacitySource.GetStageCapacity(ResourceIsolationStage.RequestQueue)))
                : maxQueuedRequests;
            m_requestCost = requestCost;
            m_useFairScheduling = provider.UseFairScheduling;
            m_decoupleHeldPublishRequests = decoupleHeldPublishRequests;
            m_complete = complete ?? throw new ArgumentNullException(nameof(complete));
            m_provider.CapacityAvailable += OnCapacityAvailable;
        }

        /// <summary>
        /// Gets the number of requests still waiting for execution admission.
        /// </summary>
        /// <remarks>
        /// The count is changed atomically and callers use it as a snapshot (worker growth
        /// heuristics, diagnostics).
        /// </remarks>
        internal int Count => Volatile.Read(ref m_count);

        /// <summary>
        /// Gets the number of scheduling FIFOs with queued requests: one per owner in weighted
        /// mode, or one shared FIFO regardless of owner in global FIFO mode.
        /// </summary>
        internal int OwnerCount
        {
            get
            {
                // Diagnostic snapshot: an owner FIFO is retired as soon as it empties.
                return m_owners.Count;
            }
        }

        /// <summary>
        /// Gets the number of turn tokens in the ready queue, including stale tokens of retired
        /// owner FIFOs that the next dequeue pass discards.
        /// </summary>
        internal int ReadyTokenCount => Volatile.Read(ref m_readyCount);

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
            // Fail fast without the gate; both conditions are re-checked under the
            // gate before the entry is published.
            if (Volatile.Read(ref m_stopped) != 0)
            {
                error = StatusCodes.BadServerHalted;
                return false;
            }
            if (Volatile.Read(ref m_count) >= m_maxQueuedRequests)
            {
                error = StatusCodes.BadServerTooBusy;
                return false;
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

                if (IsStopped())
                {
                    error = StatusCodes.BadServerHalted;
                    return false;
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    error = StatusCodes.BadRequestCancelledByClient;
                    return false;
                }
                // Reserve the queue slot before linking: the count never exceeds the bound,
                // also while admissions of different owners run concurrently.
                if (Interlocked.Increment(ref m_count) > m_maxQueuedRequests)
                {
                    Interlocked.Decrement(ref m_count);
                    error = StatusCodes.BadServerTooBusy;
                    return false;
                }
                error = Link(entry, version: null);
                if (StatusCode.IsBad(error))
                {
                    Interlocked.Decrement(ref m_count);
                    return false;
                }
                if (IsStopped())
                {
                    // Stopped after linking: either this admission takes the entry back,
                    // or the stop already completed it and the queue owned it.
                    if (TryUnlink(entry))
                    {
                        error = StatusCodes.BadServerHalted;
                        return false;
                    }
                }
                admission.TransferToQueue();
                Changed();
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
        /// Makes at most one attempt per currently eligible scheduling FIFO per capacity version.
        /// A blocked global FIFO head cannot be overtaken by another owner.
        /// Provider calls and completions are deliberately outside the owner gates.
        /// </summary>
        /// <remarks>
        /// The selected head is unlinked under its owner's gate, so a dispatch that is admitted
        /// needs no second gate entry, and in weighted mode several workers select at once. A head
        /// whose execution admission fails returns to the front of its owner's FIFO. In global
        /// FIFO mode one worker selects at a time, so the blocked head keeps its place.
        /// </remarks>
        public bool TryDequeue([NotNullWhen(true)] out Entry? result)
        {
            int attempts = -1;
            while (TrySelect(ref attempts, out Entry? candidate, out long version))
            {
                IDisposable? executionLease = null;
                StatusCode error = StatusCodes.Good;
                bool acquired = false;
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
                    EndSelection();
                    executionLease?.Dispose();
                    Pulse();
                    candidate.Dispose();
                    m_complete(candidate.Request, StatusCodes.BadInternalError);
                    throw;
                }

                if (acquired)
                {
                    candidate.SetExecutionLease(executionLease!);
                    executionLease = null;
                    EndSelection();
                    Pulse();
                    candidate.ReleaseQueueLease();
                    if (Volatile.Read(ref m_stopped) != 0)
                    {
                        // stopped while selecting: the request was no longer linked for Dispose
                        candidate.Dispose();
                        m_complete(candidate.Request, StatusCodes.BadServerHalted);
                        break;
                    }
                    if (candidate.CancellationToken.IsCancellationRequested)
                    {
                        // cancelled while selecting: the callback found the request unlinked
                        candidate.Dispose();
                        m_complete(candidate.Request, StatusCodes.BadRequestCancelledByClient);
                        continue;
                    }
                    result = candidate;
                    return true;
                }
                executionLease?.Dispose();

                if (StatusCode.IsBad(error))
                {
                    EndSelection();
                    Pulse();
                    candidate.Dispose();
                    m_complete(candidate.Request, error);
                    continue;
                }

                // Execution capacity is exhausted: the head returns to the front of its FIFO and
                // the owner is not retried before capacity or membership changes.
                StatusCode terminal = Relink(candidate, version);
                // global FIFO: the next selection starts only once the head is back in place
                EndFifoSelection();
                if (StatusCode.IsBad(terminal))
                {
                    candidate.Dispose();
                    m_complete(candidate.Request, terminal);
                }
                if (Volatile.Read(ref m_version) != version)
                {
                    Pulse();
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
                if (Volatile.Read(ref m_stopped) != 0)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (TryDequeue(out Entry? entry))
                {
                    return entry;
                }
                try
                {
                    await m_signals.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    // The token is consumed; let the next Pulse publish a new one. This
                    // reader dequeues after the reset, so a Pulse skipped before it is
                    // still observed.
                    Volatile.Write(ref m_signalPending, 0);
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
            if (Interlocked.Exchange(ref m_stopped, 1) != 0)
            {
                return;
            }
            // An admission or relink that links after this point sees the stop and takes its
            // entry back; every entry linked before it is drained here.
            List<Entry> pending = [];
            foreach (KeyValuePair<string, OwnerQueue> item in m_owners)
            {
                OwnerQueue owner = item.Value;
                while (TryTake(owner, out Entry? entry))
                {
                    pending.Add(entry);
                }
            }
            while (m_ready.TryDequeue(out _))
            {
                Interlocked.Decrement(ref m_readyCount);
            }
            Changed();
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
        /// Selects and claims one eligible owner head. In global FIFO mode concurrent selection is
        /// excluded, so the head cannot be overtaken while its admission is attempted.
        /// The first call of a dequeue pass (<paramref name="attempts"/> negative) snapshots the
        /// number of turn tokens in the ready queue; each token taken consumes one attempt, so a
        /// pass visits every owner that was ready when it started at most once per token.
        /// </summary>
        /// <remarks>
        /// No lock is taken: the ready queue and the owner FIFOs are lock-free. A token of an
        /// owner that already failed admission at the current capacity version goes back to the
        /// end of the ready queue untried.
        /// </remarks>
        private bool TrySelect(ref int attempts, [NotNullWhen(true)] out Entry? entry, out long version)
        {
            version = Volatile.Read(ref m_version);
            entry = null;
            if (attempts < 0)
            {
                attempts = Volatile.Read(ref m_readyCount);
            }
            if (attempts <= 0 || IsStopped())
            {
                return false;
            }
            if (!m_useFairScheduling && Interlocked.CompareExchange(ref m_selecting, 1, 0) != 0)
            {
                return false;
            }
            while (attempts > 0 && m_ready.TryDequeue(out OwnerQueue? owner))
            {
                attempts--;
                Interlocked.Decrement(ref m_readyCount);
                // A token of a retired FIFO is stale: its requests were selected,
                // cancelled or moved to the owner's next FIFO.
                if (Volatile.Read(ref owner.Retired) != 0)
                {
                    continue;
                }
                if (Volatile.Read(ref owner.LastAttempt) == version)
                {
                    PushTurn(owner);
                    continue;
                }
                // The turn is accounted before the head is claimed, so the owner's next turn
                // is queued in token order.
                TakeTurnToken(owner);
                if (TryTake(owner, out entry))
                {
                    return true;
                }
                // A request being linked is counted before it is visible; keep a turn for it.
                if (Volatile.Read(ref owner.Retired) == 0 && Volatile.Read(ref owner.Queued) > 0)
                {
                    GrowTurn(owner);
                }
            }
            EndFifoSelection();
            return false;
        }

        /// <summary>
        /// Links an admitted or relinked request into its owner's FIFO in admission order and
        /// grants the owner turn tokens for it. <paramref name="version"/> is the capacity version
        /// a relinked head failed admission at, or null for a new admission.
        /// </summary>
        /// <returns>Good, or the status that ends a request that cannot be linked.</returns>
        private StatusCode Link(Entry entry, long? version)
        {
            string schedulingKey = m_useFairScheduling ? entry.Owner.Key : string.Empty;
            var spinner = new SpinWait();
            while (true)
            {
                if (!m_owners.TryGetValue(schedulingKey, out OwnerQueue? owner))
                {
                    owner = m_owners.GetOrAdd(
                        schedulingKey,
                        new OwnerQueue(schedulingKey, m_useFairScheduling ? entry.Owner.Weight : 1));
                }
                // Counting the request first pins the FIFO: a retired FIFO cannot be counted,
                // so this admission moves on to the owner's next FIFO.
                if (!TryCount(owner))
                {
                    spinner.SpinOnce();
                    continue;
                }
                StatusCode status = IsStopped()
                    ? StatusCodes.BadServerHalted
                    : entry.CancellationToken.IsCancellationRequested
                        ? StatusCodes.BadRequestCancelledByClient
                        : StatusCodes.Good;
                if (StatusCode.IsBad(status))
                {
                    // the cancellation callback found the request unlinked
                    ReleaseCount(owner);
                    return status;
                }
                entry.OwnerQueue = owner;
                Volatile.Write(ref entry.State, Entry.Linked);
                if (version == null)
                {
                    entry.Sequence = Interlocked.Increment(ref m_sequence);
                    owner.Enqueue(entry);
                }
                else
                {
                    owner.Return(entry);
                    Volatile.Write(ref owner.LastAttempt, version.Value);
                }
                GrowTurn(owner);
                return StatusCodes.Good;
            }
        }

        /// <summary>
        /// Returns a head whose execution admission failed to the front of its owner's FIFO, and
        /// marks the owner as attempted at <paramref name="version"/>.
        /// </summary>
        /// <returns>Good, or the status that ends the request instead.</returns>
        private StatusCode Relink(Entry entry, long version)
        {
            Interlocked.Increment(ref m_count);
            StatusCode status = Link(entry, version);
            if (StatusCode.IsBad(status))
            {
                Interlocked.Decrement(ref m_count);
                return status;
            }
            if (IsStopped() && TryUnlink(entry))
            {
                return StatusCodes.BadServerHalted;
            }
            return StatusCodes.Good;
        }

        /// <summary>
        /// Grants a token for a request just linked: it starts a turn, or grows the current
        /// turn with the backlog up to the owner's weight.
        /// </summary>
        private void GrowTurn(OwnerQueue owner)
        {
            while (true)
            {
                long turn = Volatile.Read(ref owner.Turn);
                int tokens = TurnTokens(turn);
                int granted = TurnGranted(turn);
                long next;
                if (tokens == 0)
                {
                    next = MakeTurn(1, 1);
                }
                else if (granted < owner.Weight && tokens < Volatile.Read(ref owner.Queued))
                {
                    next = MakeTurn(tokens + 1, granted + 1);
                }
                else
                {
                    return;
                }
                if (Interlocked.CompareExchange(ref owner.Turn, next, turn) == turn)
                {
                    PushTurn(owner);
                    return;
                }
            }
        }

        /// <summary>
        /// Accounts a token taken from the ready queue. The last token of a turn grants the
        /// next turn at once, one token per request left behind it, up to the owner's weight.
        /// </summary>
        private void TakeTurnToken(OwnerQueue owner)
        {
            while (true)
            {
                long turn = Volatile.Read(ref owner.Turn);
                int tokens = TurnTokens(turn);
                if (tokens <= 0)
                {
                    return;
                }
                int grant = 0;
                long next;
                if (tokens > 1)
                {
                    next = MakeTurn(tokens - 1, TurnGranted(turn));
                }
                else
                {
                    // a request linked concurrently either counts here or grows the turn itself
                    grant = Math.Min(owner.Weight, Math.Max(Volatile.Read(ref owner.Queued) - 1, 0));
                    next = MakeTurn(grant, grant);
                }
                if (Interlocked.CompareExchange(ref owner.Turn, next, turn) == turn)
                {
                    for (int ii = 0; ii < grant; ii++)
                    {
                        PushTurn(owner);
                    }
                    return;
                }
            }
        }

        private static long MakeTurn(int tokens, int granted)
        {
            return ((long)tokens << 32) | (uint)granted;
        }

        private static int TurnTokens(long turn)
        {
            return (int)(turn >> 32);
        }

        private static int TurnGranted(long turn)
        {
            return (int)turn;
        }

        /// <summary>
        /// Appends one turn token of an owner to the ready queue.
        /// </summary>
        private void PushTurn(OwnerQueue owner)
        {
            BeforePublishTurn?.Invoke();
            EnqueueTurn(owner);
            // The turn may have been granted just before the owner was retired or the queue
            // stopped, and their cleanup may already have run. The enqueue is a full fence, so
            // either that cleanup saw this token or this check sees the retirement or the stop.
            if (Volatile.Read(ref owner.Retired) != 0 || Volatile.Read(ref m_stopped) != 0)
            {
                DiscardStaleTurns();
            }
        }

        /// <summary>
        /// Appends one turn token to the ready queue without checking for a stale owner.
        /// </summary>
        private void EnqueueTurn(OwnerQueue owner)
        {
            Interlocked.Increment(ref m_readyCount);
            m_ready.Enqueue(owner);
        }

        /// <summary>
        /// Test hook run before a turn token is published.
        /// </summary>
        internal Action? BeforePublishTurn { get; set; }

        /// <summary>
        /// Claims the oldest linked request of an owner: returned heads first, then the FIFO.
        /// Entries claimed elsewhere (cancelled or taken back) are dropped on the way.
        /// </summary>
        private bool TryTake(OwnerQueue owner, [NotNullWhen(true)] out Entry? entry)
        {
            if (owner.TryTakeReturned(out entry) || owner.TryTakeQueued(out entry))
            {
                Unlinked(owner);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Claims a request if it is still linked in an owner FIFO; it is removed lazily.
        /// </summary>
        /// <returns>True when this call claimed it.</returns>
        private bool TryUnlink(Entry entry)
        {
            if (!entry.TryClaim())
            {
                return false;
            }
            Unlinked(entry.OwnerQueue!);
            return true;
        }

        /// <summary>
        /// Accounts a claimed request.
        /// </summary>
        private void Unlinked(OwnerQueue owner)
        {
            Interlocked.Decrement(ref m_count);
            ReleaseCount(owner);
        }

        /// <summary>
        /// Drops one request count of an owner, retiring the FIFO when none is left.
        /// </summary>
        private void ReleaseCount(OwnerQueue owner)
        {
            if (Interlocked.Decrement(ref owner.Queued) == 0)
            {
                TryRetire(owner);
            }
        }

        /// <summary>
        /// Counts a request being linked into an owner FIFO, unless the FIFO is retired.
        /// </summary>
        private static bool TryCount(OwnerQueue owner)
        {
            while (true)
            {
                int queued = Volatile.Read(ref owner.Queued);
                if (queued < 0)
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref owner.Queued, queued + 1, queued) == queued)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Removes an owner FIFO that no request counts any more, so idle owners keep no queue
        /// state. Retirement is one atomic step from a zero count, so a FIFO is never revived:
        /// its remaining tokens are stale and a later request of the owner starts a new FIFO.
        /// A request counted concurrently keeps the FIFO alive.
        /// </summary>
        private void TryRetire(OwnerQueue owner)
        {
            if (Interlocked.CompareExchange(ref owner.Queued, OwnerQueue.RetiredCount, 0) != 0)
            {
                return;
            }
            Interlocked.Exchange(ref owner.Retired, 1);
            ((ICollection<KeyValuePair<string, OwnerQueue>>)m_owners).Remove(
                new KeyValuePair<string, OwnerQueue>(owner.Key, owner));
            if (Volatile.Read(ref m_count) == 0)
            {
                // nothing is queued: no dispatch pass will come to drop the stale tokens
                DiscardStaleTurns();
            }
        }

        /// <summary>
        /// Ends a selection that removed its head for good.
        /// </summary>
        private void EndSelection()
        {
            Changed();
            EndFifoSelection();
        }

        /// <summary>
        /// Lets the next global FIFO selection start. Weighted mode never sets the flag, and
        /// skipping the write keeps the line out of every worker's dispatch.
        /// </summary>
        private void EndFifoSelection()
        {
            if (!m_useFairScheduling)
            {
                Volatile.Write(ref m_selecting, 0);
            }
        }

        /// <summary>
        /// Whether the queue has stopped. A full fence orders it after a preceding link, so a
        /// request linked concurrently with the stop is either drained or taken back.
        /// </summary>
        private bool IsStopped()
        {
            return Interlocked.CompareExchange(ref m_stopped, 0, 0) != 0;
        }

        /// <summary>
        /// Removes cancelled queued work before completing it outside the owner's gate.
        /// </summary>
        private void Cancel(Entry entry)
        {
            if (!TryUnlink(entry))
            {
                return;
            }
            Changed();
            Pulse();
            entry.Dispose();
            m_complete(entry.Request, StatusCodes.BadRequestCancelledByClient);
        }

        /// <summary>
        /// Drops the tokens of retired owners from the ready queue once no request is queued, so
        /// cancelled requests leave no scheduling state behind when no worker passes.
        /// </summary>
        private void DiscardStaleTurns()
        {
            bool stopped = Volatile.Read(ref m_stopped) != 0;
            int tokens = Volatile.Read(ref m_readyCount);
            while (tokens-- > 0 && m_ready.TryDequeue(out OwnerQueue? owner))
            {
                Interlocked.Decrement(ref m_readyCount);
                if (!stopped && Volatile.Read(ref owner.Retired) == 0)
                {
                    // a concurrent admission's token
                    EnqueueTurn(owner);
                }
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
        /// <remarks>
        /// Pulse runs on every admission, dispatch and capacity release. While a token is
        /// already pending the call returns without entering the signal channel's lock,
        /// which otherwise serialises every request thread on it.
        /// </remarks>
        private void Pulse()
        {
            if (Volatile.Read(ref m_count) > 0 && Volatile.Read(ref m_stopped) == 0 &&
                Volatile.Read(ref m_signalPending) == 0 &&
                Interlocked.Exchange(ref m_signalPending, 1) == 0 &&
                m_signals.Writer.TryWrite(true))
            {
                Interlocked.Increment(ref m_wakeSignalCount);
            }
        }

        /// <summary>
        /// Owns the admission leases for one request through queueing, execution and parking.
        /// </summary>
        internal sealed class Entry : QueueNode, IDisposable
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
            /// Gets or sets the owning FIFO while linked; written under that FIFO's gate.
            /// </summary>
            internal OwnerQueue? OwnerQueue
            {
                get => Volatile.Read(ref m_ownerQueue);
                set => Volatile.Write(ref m_ownerQueue, value);
            }

            /// <summary>
            /// Gets or sets the admission order, assigned when first linked.
            /// </summary>
            internal long Sequence { get; set; }

            /// <summary>
            /// <see cref="Linked"/> while queued; claimed exactly once by selection,
            /// cancellation or stop with <see cref="TryClaim"/>.
            /// </summary>
            internal int State;

            /// <summary>
            /// The value of <see cref="State"/> while the entry is linked in an owner FIFO.
            /// </summary>
            internal const int Linked = 1;

            /// <summary>
            /// Claims a linked entry; only one claimant succeeds per link.
            /// </summary>
            internal bool TryClaim()
            {
                return Interlocked.CompareExchange(ref State, 0, Linked) == Linked;
            }

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
                var prior = (RegistrationState)Interlocked.Exchange(
                    ref m_registrationState, (int)RegistrationState.Disposed);
                if (prior == RegistrationState.Disposed)
                {
                    return;
                }
                if (prior == RegistrationState.Registered)
                {
                    m_registration.Dispose();
                }
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
                m_registration = registration;

                // published unless the entry was disposed meanwhile, possibly by the
                // callback running inside Register; the registration is ours to end then.
                if (Interlocked.CompareExchange(
                    ref m_registrationState,
                    (int)RegistrationState.Registered,
                    (int)RegistrationState.None) != (int)RegistrationState.None)
                {
                    registration.Dispose();
                }
            }

            /// <summary>
            /// Queue that controls ordering and classification validity.
            /// </summary>
            private readonly FairRequestQueue m_queue;

            /// <summary>
            /// The owning FIFO while linked.
            /// </summary>
            private OwnerQueue? m_ownerQueue;

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
            /// Registration owned until disposal starts; published by
            /// <see cref="m_registrationState"/>.
            /// </summary>
            private CancellationTokenRegistration m_registration;

            /// <summary>
            /// A <see cref="RegistrationState"/>, stored as an <see cref="int"/> so that
            /// <see cref="Interlocked"/> can change it; an entry needs no lock object.
            /// </summary>
            private int m_registrationState;

            /// <summary>
            /// Who owns the cancellation registration of an entry.
            /// </summary>
            private enum RegistrationState
            {
                /// <summary>
                /// Not registered yet.
                /// </summary>
                None,

                /// <summary>
                /// Registered; disposal ends the registration.
                /// </summary>
                Registered,

                /// <summary>
                /// Disposal started; a registration made afterwards is ended by its maker.
                /// </summary>
                Disposed
            }
        }

        /// <summary>
        /// A node of an owner's intrusive lock-free FIFO; queued entries are the nodes.
        /// </summary>
        internal class QueueNode
        {
            /// <summary>
            /// The next node, published with a compare-and-swap.
            /// </summary>
            internal QueueNode? Next;
        }

        /// <summary>
        /// A scheduling FIFO and its weighted turn. Global FIFO mode shares one instance across
        /// all resource-accounting owners.
        /// </summary>
        /// <remarks>
        /// Admissions append to a lock-free FIFO (Michael and Scott, with the entries themselves
        /// as nodes, so queueing allocates nothing). A head whose execution admission failed is
        /// older than every request still in the FIFO; it goes to a small sorted list under
        /// <see cref="Gate"/> that is served first. Entries claimed elsewhere (cancelled or
        /// taken back on stop) stay in place and are dropped when they reach the head.
        /// A turn is a set of tokens in the queue's ready queue: one per queued request, up to
        /// the owner's weight. Each token taken dispatches one request, and the owner queues for
        /// its next turn when the last token of the turn is taken.
        /// </remarks>
        internal sealed class OwnerQueue
        {
            /// <summary>
            /// Creates an empty FIFO for a scheduling key.
            /// </summary>
            public OwnerQueue(string key, int weight)
            {
                Key = key;
                Weight = weight;
                m_head = m_tail = new QueueNode();
            }

            /// <summary>
            /// The scheduling key the FIFO is registered under.
            /// </summary>
            public string Key { get; }

            /// <summary>
            /// The owner's configured scheduling weight.
            /// </summary>
            public int Weight { get; }

            /// <summary>
            /// Protects the returned heads.
            /// </summary>
            public Lock Gate { get; } = new();

            /// <summary>
            /// The current turn, changed atomically: the tokens still in the ready queue (high
            /// 32 bits) and the tokens granted in the turn, at most the weight (low 32 bits).
            /// </summary>
            public long Turn;

            /// <summary>
            /// Linked requests plus admissions about to link, changed atomically. While it is
            /// not zero the FIFO is not retired; <see cref="RetiredCount"/> once retired.
            /// </summary>
            public int Queued;

            /// <summary>
            /// The value of <see cref="Queued"/> of a retired FIFO, which no admission can count.
            /// </summary>
            public const int RetiredCount = int.MinValue / 2;

            /// <summary>
            /// The capacity version of the last unsuccessful admission.
            /// </summary>
            public long LastAttempt = -1;

            /// <summary>
            /// 1 once the FIFO is retired for good; its tokens are stale.
            /// </summary>
            public int Retired;

            /// <summary>
            /// Appends a newly admitted entry.
            /// </summary>
            public void Enqueue(Entry entry)
            {
                entry.Next = null;
                while (true)
                {
                    QueueNode tail = Volatile.Read(ref m_tail);
                    QueueNode? next = Volatile.Read(ref tail.Next);
                    if (!ReferenceEquals(tail, Volatile.Read(ref m_tail)))
                    {
                        continue;
                    }
                    if (next != null)
                    {
                        // help a lagging tail along
                        Interlocked.CompareExchange(ref m_tail, next, tail);
                        continue;
                    }
                    if (Interlocked.CompareExchange(ref tail.Next, entry, null) == null)
                    {
                        Interlocked.CompareExchange(ref m_tail, entry, tail);
                        return;
                    }
                }
            }

            /// <summary>
            /// Puts a head whose admission failed back in front of the FIFO, in admission order.
            /// </summary>
            public void Return(Entry entry)
            {
                lock (Gate)
                {
                    m_returned ??= [];
                    int index = m_returned.Count;
                    while (index > 0 && m_returned[index - 1].Sequence > entry.Sequence)
                    {
                        index--;
                    }
                    m_returned.Insert(index, entry);
                    Volatile.Write(ref m_returnedCount, m_returned.Count);
                }
            }

            /// <summary>
            /// Claims the oldest linked returned head, if any.
            /// </summary>
            public bool TryTakeReturned([NotNullWhen(true)] out Entry? entry)
            {
                entry = null;
                if (Volatile.Read(ref m_returnedCount) == 0)
                {
                    return false;
                }
                lock (Gate)
                {
                    while (m_returned!.Count > 0)
                    {
                        Entry candidate = m_returned[0];
                        m_returned.RemoveAt(0);
                        if (candidate.TryClaim())
                        {
                            entry = candidate;
                            break;
                        }
                    }
                    Volatile.Write(ref m_returnedCount, m_returned.Count);
                }
                return entry != null;
            }

            /// <summary>
            /// Claims the oldest linked entry of the FIFO, if any.
            /// </summary>
            public bool TryTakeQueued([NotNullWhen(true)] out Entry? entry)
            {
                while (TryDequeue(out entry))
                {
                    if (entry.TryClaim())
                    {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// Removes the head node; the removed entry becomes the new sentinel.
            /// </summary>
            private bool TryDequeue([NotNullWhen(true)] out Entry? entry)
            {
                while (true)
                {
                    QueueNode head = Volatile.Read(ref m_head);
                    QueueNode tail = Volatile.Read(ref m_tail);
                    QueueNode? next = Volatile.Read(ref head.Next);
                    if (!ReferenceEquals(head, Volatile.Read(ref m_head)))
                    {
                        continue;
                    }
                    if (next == null)
                    {
                        entry = null;
                        return false;
                    }
                    if (ReferenceEquals(head, tail))
                    {
                        // help a lagging tail along
                        Interlocked.CompareExchange(ref m_tail, next, tail);
                        continue;
                    }
                    if (ReferenceEquals(Interlocked.CompareExchange(ref m_head, next, head), head))
                    {
                        entry = (Entry)next;
                        return true;
                    }
                }
            }

            private QueueNode m_head;
            private QueueNode m_tail;
            private List<Entry>? m_returned;
            private int m_returnedCount;
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
        /// Whether requests are grouped into weighted owner FIFOs rather than one global FIFO.
        /// </summary>
        private readonly bool m_useFairScheduling;

        /// <summary>
        /// Whether parkable requests reserve an independent parked slot.
        /// </summary>
        private readonly bool m_decoupleHeldPublishRequests;

        /// <summary>
        /// Reports terminal outcomes outside queue and provider gates.
        /// </summary>
        private readonly Action<IEndpointIncomingRequest, StatusCode> m_complete;

        /// <summary>
        /// Scheduling FIFOs retained while nonempty, keyed by owner in weighted mode
        /// or by the empty string for global FIFO mode.
        /// </summary>
        private readonly ConcurrentDictionary<string, OwnerQueue> m_owners = new(StringComparer.Ordinal);

        /// <summary>
        /// Turn tokens in round-robin order; a token of a retired FIFO is skipped.
        /// </summary>
        private readonly ConcurrentQueue<OwnerQueue> m_ready = new();

        /// <summary>
        /// Number of tokens in <see cref="m_ready"/>, at least the number dequeuable.
        /// </summary>
        private int m_readyCount;

        /// <summary>
        /// Single coalesced wake token; a successful dispatch passes it to another waiter.
        /// </summary>
        private readonly Channel<bool> m_signals = Channel.CreateBounded<bool>(1);

        /// <summary>
        /// 1 while a wake token is in <see cref="m_signals"/> and not yet consumed.
        /// </summary>
        private int m_signalPending;

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
        /// Admission order of the last linked request.
        /// </summary>
        private long m_sequence;

        /// <summary>
        /// 1 while a selected head is undergoing provider admission; only global FIFO mode
        /// waits for it, weighted mode selects concurrently.
        /// </summary>
        private int m_selecting;

        /// <summary>
        /// 1 once admissions and dispatch have stopped.
        /// </summary>
        private int m_stopped;
    }
}
