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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Redaction;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Manages all subscriptions of a session on the server side. This
    /// includes managing the publish requests and acknowledgements.
    /// The publish requests are managed by a controller that adjusts
    /// the number of workers based on the number of subscriptions.
    /// The workers are responsible for sending the publish requests
    /// to the server and dispatching the notifications to the
    /// subscriptions. The subscriptions queue acknowledgements for
    /// the completed notifications as soon as they are dispatched.
    /// The queued acknowledgements are then sent by the workers the
    /// next publish cycle.
    /// </summary>
    internal sealed class SubscriptionManager : ISubscriptionManager,
        IMessageAckQueue, IAsyncDisposable
    {
        /// <summary>
        /// Create subscription manager
        /// </summary>
        /// <param name="session"></param>
        /// <param name="loggerFactory"></param>
        /// <param name="returnDiagnostics"></param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// used for elapsed-time and timer calculations. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        public SubscriptionManager(ISubscriptionManagerContext session,
            ILoggerFactory loggerFactory, DiagnosticsMasks returnDiagnostics,
            TimeProvider? timeProvider = null)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_session = session;
            m_loggerFactory = loggerFactory;
            m_logger = loggerFactory.CreateLogger<SubscriptionManager>();
            m_outcomeDispatcher = new AcknowledgementOutcomeDispatcher(m_logger);
            ShutdownToken = m_cts.Token;
            ReturnDiagnostics = returnDiagnostics;
            m_disposeToken = m_cts.Token;
            m_publishController = PublishControllerAsync(m_disposeToken);
            m_acks = Channel.CreateUnboundedPrioritized(
                new UnboundedPrioritizedChannelOptions<PendingAcknowledgement>
                {
                    Comparer = SequenceNumberComparer.Instance
                });
        }

        /// <summary>
        /// If the subscriptions are transferred when a session is
        /// recreated.
        /// </summary>
        /// <remarks>
        /// Default <c>false</c>, set to <c>true</c> if subscriptions
        /// should be transferred after recreating the session.
        /// Service must be supported by server.
        /// </remarks>
        public bool TransferSubscriptionsOnRecreate { get; set; }

        /// <inheritdoc/>
        public bool PoolNotifications { get; set; }

        /// <inheritdoc/>
        public CancellationToken ShutdownToken { get; }

        /// <inheritdoc/>
        public DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <inheritdoc/>
        /// <remarks>
        /// Setting this property signals the publish controller so the worker
        /// pool resizes promptly — without the signal, the controller stays
        /// parked on <see cref="m_publishControl"/> and the change does not
        /// take effect until some other event (subscription add/remove,
        /// pause/resume, worker exit) wakes it.
        /// </remarks>
        public int MinPublishWorkerCount
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }
                field = value;
                m_publishControl.Set();
            }
        } = 2;

        /// <inheritdoc/>
        /// <remarks>
        /// Setting this property signals the publish controller so the worker
        /// pool resizes promptly. See <see cref="MinPublishWorkerCount"/>.
        /// </remarks>
        public int MaxPublishWorkerCount
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }
                field = value;
                m_publishControl.Set();
            }
        } = 15;

        /// <inheritdoc/>
        public IEnumerable<ISubscription> Items
        {
            get
            {
                lock (m_subscriptionLock)
                {
                    // Expose logical wrappers — callers see one
                    // ISubscription per logical subscription regardless
                    // of how many partitions back it.
                    return [.. m_logicals];
                }
            }
        }

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (m_subscriptionLock)
                {
                    return m_logicals.Count;
                }
            }
        }

        /// <inheritdoc/>
        public int GoodPublishRequestCount => m_goodPublishRequestCount;

        /// <inheritdoc/>
        public int BadPublishRequestCount => m_badPublishRequestCount;

        /// <inheritdoc/>
        public long MissingMessageCount
        {
            get
            {
                long total = 0;
                lock (m_subscriptionLock)
                {
                    foreach (IManagedSubscription s in m_subscriptions)
                    {
                        total += s.MissingMessageCount;
                    }
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public long RepublishMessageCount
        {
            get
            {
                long total = 0;
                lock (m_subscriptionLock)
                {
                    foreach (IManagedSubscription s in m_subscriptions)
                    {
                        total += s.RepublishMessageCount;
                    }
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public int PublishWorkerCount { get; private set; }

        /// <inheritdoc/>
        internal int PublishControlCycles { get; set; }

        /// <summary>
        /// Created items
        /// </summary>
        internal List<IManagedSubscription> Created
        {
            get
            {
                lock (m_subscriptionLock)
                {
                    return [.. m_subscriptions.Where(s => s.Created)];
                }
            }
        }

        /// <summary>
        /// Number of created items
        /// </summary>
        public int CreatedCount
        {
            get
            {
                lock (m_subscriptionLock)
                {
                    return m_subscriptions.Count(s => s.Created);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            bool quiescenceAcquired = false;
            try
            {
                Task cancellation = m_cts.CancelAsync();
                // Cancellation callbacks can wait for work owned by the publish controller.
                // Signal it before awaiting callback completion so shutdown can make progress.
                m_publishControl.Set();
                await cancellation.ConfigureAwait(false);
                await m_publishController.ConfigureAwait(false);
                await m_publishQuiescenceGate.WaitAsync().ConfigureAwait(false);
                quiescenceAcquired = true;

                List<LogicalSubscription>? logicals;
                List<IManagedSubscription>? orphans;
                List<IManagedSubscription> registered;
                lock (m_subscriptionLock)
                {
                    logicals = [.. m_logicals];
                    m_logicals.Clear();
                    registered = [.. m_subscriptions];
                    m_subscriptions.Clear();
                    m_establishedSubscriptions.Clear();
                }

                // Reading LogicalSubscription.Partitions acquires the
                // composite's partition lock, which must never be taken while
                // the registry lock is held: the partition factory runs the
                // other way round (partition lock -> registry lock) and the
                // pair would deadlock.
                //
                // Drop the dispatch registry entries owned by the wrappers we
                // are about to dispose so any partition not currently bound to
                // a wrapper (transient pre-registration window) can still be
                // cleaned up below.
                var ownedByLogicals = new HashSet<IManagedSubscription>();
                foreach (LogicalSubscription wrapper in logicals)
                {
                    foreach (IManagedSubscription partition in wrapper.Partitions)
                    {
                        ownedByLogicals.Add(partition);
                    }
                }
                orphans = [];
                foreach (IManagedSubscription partition in registered)
                {
                    if (!ownedByLogicals.Contains(partition))
                    {
                        orphans.Add(partition);
                    }
                }
                foreach (LogicalSubscription wrapper in logicals)
                {
                    await wrapper.DisposeAsync().ConfigureAwait(false);
                }
                foreach (IManagedSubscription partition in orphans)
                {
                    await partition.DisposeAsync().ConfigureAwait(false);
                }
                m_subscriptionHistory.Clear();
            }
            catch (Exception ex)
            {
                m_logger.ExceptionDuringDispose(ex);
            }
            finally
            {
                if (!quiescenceAcquired)
                {
                    await m_publishQuiescenceGate.WaitAsync().ConfigureAwait(false);
                }
                m_publishQuiescenceGate.Release();
                m_publishQuiescenceGate.Dispose();
                m_cts.Dispose();
                List<AcknowledgementOutcome>? abandoned = null;
                while (m_acks.Reader.TryRead(out PendingAcknowledgement queued))
                {
                    AddOutcome(ref abandoned, queued, AcknowledgementDisposition.Abandoned,
                        StatusCodes.BadShutdown);
                }
                (m_acks as IDisposable)?.Dispose();
                lock (m_acknowledgementPolicyLock)
                {
                    foreach (PendingAcknowledgement deferred in m_deferredAcknowledgements)
                    {
                        AddOutcome(ref abandoned, deferred, AcknowledgementDisposition.Abandoned,
                            StatusCodes.BadShutdown);
                    }
                    m_deferredAcknowledgements.Clear();
                }
                foreach (AcknowledgementHandle handle in m_outstandingAcknowledgements.Keys)
                {
                    (abandoned ??= []).Add(new AcknowledgementOutcome(
                        handle, AcknowledgementDisposition.Abandoned, StatusCodes.BadShutdown));
                }
                ReportOutcomes(abandoned);
                await CompleteOutcomeDeliveryAsync().ConfigureAwait(false);
                lock (m_subscriptionLock)
                {
                    m_claims.Clear();
                    m_superseded.Clear();
                    m_windows.Clear();
                }
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Trigger publish controller
        /// </summary>
        public void Update()
        {
            m_publishControl.Set();
        }

        /// <summary>
        /// Number of acknowledgements waiting in the queue, excluding deferred and in-flight ones.
        /// </summary>
        internal int QueuedAcknowledgementCount => m_acks.Reader.Count;

        /// <summary>
        /// Whether terminal state is required by confirmations or an application observer.
        /// </summary>
        private bool RequiresAcknowledgementTracking =>
            m_session.AcknowledgementObserver != null || m_session.AcknowledgementProvider is IAcknowledgementTracker;

        /// <inheritdoc/>
        public ValueTask QueueAsync(IMessageProcessor owner, long generation,
            SubscriptionAcknowledgement ack, CancellationToken ct)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            var pending = new PendingAcknowledgement(ack, owner.GetAcknowledgementScope(generation));
            if (!RequiresAcknowledgementTracking)
            {
                return m_acks.Writer.WriteAsync(pending, ct);
            }
            if (!m_outstandingAcknowledgements.TryUpdate(pending.Handle, 1, 0))
            {
                // Only receipt admits a lifetime. A duplicate or a retry after its terminal
                // outcome must not create another lifetime with the same handle.
                return default;
            }
            ValueTask write = m_acks.Writer.WriteAsync(pending, ct);
            return write.IsCompletedSuccessfully ? default : AwaitQueuedAsync(write, pending);
        }

        /// <inheritdoc/>
        public void TrackAcknowledgement(IMessageProcessor owner, long generation, uint sequenceNumber)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (RequiresAcknowledgementTracking)
            {
                var handle = new AcknowledgementHandle(owner.GetAcknowledgementScope(generation), sequenceNumber);
                lock (m_subscriptionLock)
                {
                    if (m_outstandingAcknowledgements.TryAdd(handle, 0))
                    {
                        (m_session.AcknowledgementProvider as IAcknowledgementTracker)?.Track(handle);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void OnSubscriptionCreated(IMessageProcessor owner, uint subscriptionId,
            DateTimeUtc createdAt = default)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            if (subscriptionId == 0)
            {
                return;
            }
            lock (m_subscriptionLock)
            {
                if (m_claims.TryGetValue(subscriptionId, out SubscriptionClaim previous) &&
                    !ReferenceEquals(previous.Owner, owner) &&
                    IsLiveOwnerLocked(previous.Owner, subscriptionId))
                {
                    m_superseded.Add(previous.Owner);
                }
                foreach (IManagedSubscription registered in m_subscriptions)
                {
                    // An owner registered without a claim (for example, transferred) is superseded too.
                    if (registered.Id == subscriptionId && !ReferenceEquals(registered, owner))
                    {
                        m_superseded.Add(registered);
                    }
                }
                m_superseded.Remove(owner);
                m_claims[subscriptionId] = new SubscriptionClaim(
                    owner, Interlocked.Increment(ref m_claimSequence), createdAt);
                m_windows.Remove(owner);
            }
        }

        /// <inheritdoc/>
        public async ValueTask RunWithPublishingQuiescedAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SubscriptionManager));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                ct, m_disposeToken);
            CancellationToken linkedToken = linked.Token;
            await m_publishQuiescenceGate.WaitAsync(linkedToken)
                .ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(SubscriptionManager));
                }
                SetPublishingQuiesced(true);
                try
                {
                    await DrainAsync(linkedToken).ConfigureAwait(false);
                    await operation(linkedToken).ConfigureAwait(false);
                }
                finally
                {
                    SetPublishingQuiesced(false);
                }
            }
            finally
            {
                m_publishQuiescenceGate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask RunWithSessionAvailableAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken ct)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            while (true)
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct, m_disposeToken);
                CancellationToken token = attempt.Token;
                while (true)
                {
                    await m_sessionAvailable.WaitAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    lock (m_publishStateLock)
                    {
                        if (m_sessionRecoveryPaused)
                        {
                            continue;
                        }
                        m_activeSubscriptionUpdates.Add(attempt);
                        break;
                    }
                }
                try
                {
                    token.ThrowIfCancellationRequested();
                    await operation(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return;
                }
                catch (OperationCanceledException) when (attempt.IsCancellationRequested &&
                    !ct.IsCancellationRequested &&
                    !m_disposeToken.IsCancellationRequested)
                {
                    // The recovery owner restores the subscription before this pass retries.
                }
                finally
                {
                    lock (m_publishStateLock)
                    {
                        m_activeSubscriptionUpdates.Remove(attempt);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool OwnsSubscriptionId(
            IMessageProcessor subscription,
            uint subscriptionId)
        {
            if (subscriptionId == 0)
            {
                return false;
            }
            lock (m_subscriptionLock)
            {
                // The asker itself is still registered under its retired id,
                // so a match on it proves nothing; only a match on a sibling
                // does. Hence the full walk rather than a first-hit lookup.
                foreach (IManagedSubscription registered in m_subscriptions)
                {
                    if (registered.Id == subscriptionId &&
                        !ReferenceEquals(registered, subscription))
                    {
                        return false;
                    }
                }
            }
            // No sibling claims the id, so the caller is still the last owner.
            return true;
        }

        /// <inheritdoc/>
        public int DropPendingForSubscription(IMessageProcessor owner, uint subscriptionId)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }
            List<AcknowledgementOutcome>? retired = null;
            lock (m_subscriptionLock)
            {
                ForgetOwnerLocked(owner, subscriptionId);
                if (RequiresAcknowledgementTracking && !m_outstandingAcknowledgements.IsEmpty)
                {
                    // Queued and in-flight acknowledgements end now as well: with no
                    // subscription left, no publish worker may run to discard them.
                    foreach (AcknowledgementHandle handle in m_outstandingAcknowledgements.Keys)
                    {
                        if (handle.Scope is { } scope &&
                            ReferenceEquals(scope.Owner, owner) &&
                            IsRetiredScopeLocked(scope))
                        {
                            (retired ??= []).Add(new AcknowledgementOutcome(handle,
                                AcknowledgementDisposition.OwnerRetired, StatusCodes.BadSubscriptionIdInvalid));
                        }
                    }
                }
            }
            int dropped;
            lock (m_acknowledgementPolicyLock)
            {
                if (RequiresAcknowledgementTracking)
                {
                    foreach (PendingAcknowledgement pending in m_deferredAcknowledgements)
                    {
                        if (ReferenceEquals(pending.Owner, owner))
                        {
                            AddOutcome(ref retired, pending, AcknowledgementDisposition.OwnerRetired,
                                StatusCodes.BadSubscriptionIdInvalid);
                        }
                    }
                }
                dropped = m_deferredAcknowledgements.RemoveAll(
                    pending => ReferenceEquals(pending.Owner, owner));
                UpdateDeferredWarning();
            }
            ReportOutcomes(retired);
            return dropped;
        }

        /// <inheritdoc/>
        public ValueTask CompleteAsync(IMessageProcessor completing,
            uint subscriptionId, CancellationToken ct)
        {
            if (completing is not IManagedSubscription partition)
            {
                return default;
            }

            LogicalSubscription? logical = null;
            LogicalSubscription[] wrappers;
            bool registered;
            lock (m_subscriptionLock)
            {
                // Drop the partition from the dispatch registry by
                // reference — never by identifier, because every
                // subscription that is not created (yet) shares id 0.
                registered = m_subscriptions.Remove(partition);
                if (registered)
                {
                    m_establishedSubscriptions.Remove(partition);

                    // Record the retired identifier while the registry lock
                    // is still held. A publish worker resolves incoming
                    // responses under the same lock, so it can never observe
                    // the partition as removed before the identifier is
                    // known to be retired and mistake a late response for an
                    // orphaned server side subscription.
                    RetireSubscriptionId(subscriptionId);
                }

                wrappers = [.. m_logicals];
            }
            if (!registered)
            {
                // A rollback path removed the partition already; still retire what it owned.
                RetireOwner(completing, subscriptionId);
                return default;
            }

            // If the removed partition was the primary of any logical
            // wrapper, the wrapper has no usable partitions left — drop it
            // from the public registry so ISubscriptionManager.Items / Count
            // reflect the deletion. Secondary partitions (added on demand by
            // the composite collection) do not remove the wrapper; the
            // wrapper keeps living as long as the primary is registered.
            //
            // Reading Partitions takes the composite's partition lock, so it
            // has to happen outside the registry lock: the partition factory
            // acquires the two in the opposite order and the pair would
            // otherwise deadlock the whole publish pipeline.
            foreach (LogicalSubscription wrapper in wrappers)
            {
                IReadOnlyList<IManagedSubscription> parts = wrapper.Partitions;
                if (parts.Count > 0 && ReferenceEquals(parts[0], partition))
                {
                    logical = wrapper;
                    break;
                }
            }
            if (logical != null)
            {
                lock (m_subscriptionLock)
                {
                    m_logicals.Remove(logical);
                }
            }
            m_logger.SubscriptionRemoved(subscriptionId);
            m_publishControl.Set();

            // No publishing quiescence: acknowledgements of the removed partition that a
            // worker holds in flight fail send-time validation if they are requeued.
            RetireOwner(completing, subscriptionId);
            return default;
        }

        /// <summary>
        /// Retires the acknowledgement state a removed subscription owned.
        /// </summary>
        private void RetireOwner(IMessageProcessor owner, uint subscriptionId)
        {
            if (subscriptionId != 0 && Volatile.Read(ref m_disposed) == 0)
            {
                DropPendingForSubscription(owner, subscriptionId);
                return;
            }
            lock (m_subscriptionLock)
            {
                ForgetOwnerLocked(owner, subscriptionId);
            }
        }

        /// <inheritdoc/>
        public ISubscription Add(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            SubscriptionOptions snapshot = options.CurrentValue ?? new SubscriptionOptions();

            // In multi-partition mode every partition fires its
            // notification callbacks on the same logical wrapper —
            // wrap the user handler in a forwarding shim that (a)
            // serialises calls across partitions and (b) re-targets
            // the subscription parameter to the wrapper instead of
            // the partition.
            ISubscriptionNotificationHandler effectiveHandler = handler;
            PartitionForwardingHandler? forwardingHandler = null;
            if (!snapshot.DisableUnboundedItemMode)
            {
#pragma warning disable CA2000 // ownership transfers to wrapper.AttachForwardingHandler
                forwardingHandler = new PartitionForwardingHandler(handler, m_logger);
#pragma warning restore CA2000
                effectiveHandler = forwardingHandler;
            }

            IManagedSubscription primary = m_session.CreateSubscription(
                effectiveHandler, options, this);

            // Build the placement policy + on-demand partition factory
            // unless the caller opted out via DisableUnboundedItemMode.
            // The factory is invoked by the composite collection inside
            // its lock when no existing partition has capacity; the
            // factory mints a sibling partition with the same handler
            // and options, registers it in the dispatch registry, and
            // wakes the publish controller so a worker can serve it.
            PartitionPlacementPolicy? policy = null;
            Func<IManagedSubscription>? partitionFactory = null;
            LogicalSubscription wrapper;
            if (snapshot.DisableUnboundedItemMode)
            {
                wrapper = new LogicalSubscription(primary, null, null);
            }
            else
            {
                policy = new PartitionPlacementPolicy(
                    snapshot.MaxMonitoredItemsPerPartition ?? 0,
                    snapshot.MaxPartitionCount);
                // Capture the wrapper into the factory closure so newly
                // minted secondary partitions can route their reactive-
                // fallback callbacks back to the same logical.
                LogicalSubscription? created = null;
                partitionFactory = () => MintPartition(effectiveHandler, options,
                    part => InitializePartition(part, created));
                wrapper = new LogicalSubscription(primary, policy, partitionFactory,
                    m_timeProvider,
                    NormalizeIdleTimeout(snapshot.SecondaryPartitionIdleTimeout),
                    RemoveSecondaryPartitionAsync);
                created = wrapper;
                AttachReactiveFallback(primary, wrapper);
                forwardingHandler!.BindLogical(wrapper);
                wrapper.AttachForwardingHandler(forwardingHandler);
            }

            lock (m_subscriptionLock)
            {
                if (!m_subscriptions.Add(primary))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to register primary partition.");
                }
                if (!m_logicals.Add(wrapper))
                {
                    // Roll the primary back out so we do not leak
                    // a partition with no logical wrapper.
                    m_subscriptions.Remove(primary);
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to register logical subscription.");
                }
                m_logger.SubscriptionAdded(primary.Id);
            }
            m_publishControl.Set();
            return wrapper;
        }

        /// <summary>
        /// Wire a partition's
        /// <see cref="Subscription.OnPartitionCapReached"/> hook
        /// back to the owning logical wrapper so a server
        /// <c>Bad_TooManyMonitoredItems</c> response marks the
        /// partition no-grow in the wrapper's placement policy. No-op
        /// when the partition is not a <see cref="Subscription"/>
        /// (e.g. a test fake) or when the wrapper has not yet been
        /// captured by the factory closure.
        /// </summary>
        private static void AttachReactiveFallback(
            IManagedSubscription partition,
            LogicalSubscription? wrapper)
        {
            if (wrapper == null)
            {
                return;
            }
            if (partition is Subscription concrete)
            {
                concrete.OnPartitionCapReached = _ => wrapper.OnPartitionCapReached(partition);
            }
        }

        /// <summary>
        /// Wire a freshly constructed secondary partition to its owning
        /// logical wrapper. Runs before the partition starts its first
        /// create pass, so a durable logical subscription sends
        /// <c>SetSubscriptionDurable</c> on the partition even when the
        /// server answers <c>CreateSubscription</c> before the partition
        /// factory returns.
        /// </summary>
        private static void InitializePartition(
            IManagedSubscription partition,
            LogicalSubscription? wrapper)
        {
            AttachReactiveFallback(partition, wrapper);
            wrapper?.TryApplyDurableToNewPartition(partition);
        }

        /// <summary>
        /// Normalize a caller-supplied
        /// <see cref="SubscriptionOptions.SecondaryPartitionIdleTimeout"/>
        /// for the wrapper. Per the option docs,
        /// <see cref="TimeSpan.Zero"/> means immediate idle-delete
        /// and <see cref="Timeout.InfiniteTimeSpan"/>
        /// (the only valid negative value) disables idle-delete. Any
        /// other negative value is treated as "disabled" defensively
        /// because <see cref="ITimer"/> rejects them.
        /// </summary>
        private static TimeSpan NormalizeIdleTimeout(TimeSpan idleTimeout)
        {
            return idleTimeout < TimeSpan.Zero
                ? Timeout.InfiniteTimeSpan
                : idleTimeout;
        }

        /// <summary>
        /// Secondary-partition disposer handed to the wrapper's
        /// idle-delete timer. Drops the partition from this
        /// manager's dispatch registry, drains any pending
        /// acknowledgements addressed at it, and disposes the
        /// partition (which sends <c>DeleteSubscriptions</c> to the
        /// server). Logical wrapper book-keeping is performed by the
        /// composite collection under its own lock before this
        /// callback runs, so we only need to clean up manager-side
        /// state here.
        /// </summary>
        private async ValueTask RemoveSecondaryPartitionAsync(IManagedSubscription partition)
        {
            uint partitionId = partition.Id;
            lock (m_subscriptionLock)
            {
                m_subscriptions.Remove(partition);
                // Retire the identifier under the registry lock so a
                // publish worker cannot observe the partition as removed
                // before the identifier is known to be retired.
                RetireSubscriptionId(partitionId);
            }
            RetireOwner(partition, partitionId);
            try
            {
                await partition.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.IdleDeleteSecondaryPartitionThrew(ex, partition.Id);
            }
            m_publishControl.Set();
        }

        /// <summary>
        /// Remember a server side subscription identifier that this
        /// manager has just retired. Late publish responses carrying a
        /// retired identifier are dropped instead of being mistaken
        /// for an orphaned server side subscription.
        /// </summary>
        /// <remarks>
        /// Callers record the identifier in the same critical section
        /// that drops the subscription from the dispatch registry, so
        /// the removal and the retirement are observed atomically by a
        /// publish worker resolving an incoming response.
        /// </remarks>
        /// <param name="subscriptionId">The retired identifier. Zero
        /// is ignored because it never identifies a subscription on
        /// the server.</param>
        private void RetireSubscriptionId(uint subscriptionId)
        {
            if (subscriptionId == 0)
            {
                return;
            }
            m_subscriptionHistory.Enqueue(subscriptionId);
            while (m_subscriptionHistory.Count > kMaxSubscriptionHistory)
            {
                m_subscriptionHistory.TryDequeue(out _);
            }
        }

        /// <summary>
        /// Whether any registered subscription has not yet been
        /// assigned a server side identifier. While that is the case a
        /// publish response can carry an identifier that belongs to a
        /// subscription this manager owns but has not observed yet, so
        /// unresolved identifiers must not be deleted.
        /// </summary>
        private bool HasSubscriptionsPendingCreation()
        {
            lock (m_subscriptionLock)
            {
                foreach (IManagedSubscription subscription in m_subscriptions)
                {
                    if (subscription.IsCreationInProgress)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Synchronous partition factory handed to the composite
        /// collection. Constructs a sibling partition with the same
        /// handler + options as the logical subscription's primary,
        /// registers it in the dispatch registry, and wakes the
        /// publish controller so a worker is scheduled for it.
        /// </summary>
        private IManagedSubscription MintPartition(
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options,
            Action<IManagedSubscription> initialize)
        {
            return MintPartitionCore(handler, options, loadState: null, initialize);
        }

        /// <summary>
        /// Variant of <see cref="MintPartition"/> that accepts a
        /// pre-existing <see cref="SubscriptionLoadState"/> so the
        /// minted partition is construction-time bound to a
        /// previously-saved server-side identifier and items list.
        /// Used by <see cref="RestoreGroupAsync"/> to attach
        /// secondary partitions during multi-partition snapshot
        /// restore.
        /// </summary>
        private IManagedSubscription MintPreloadedPartition(
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options,
            SubscriptionLoadState loadState)
        {
            return MintPartitionCore(handler, options, loadState, initialize: null);
        }

        private IManagedSubscription MintPartitionCore(
            ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options,
            SubscriptionLoadState? loadState,
            Action<IManagedSubscription>? initialize)
        {
            IManagedSubscription partition = m_session.CreateSubscription(
                handler, options, this, loadState, initialize);
            lock (m_subscriptionLock)
            {
                if (!m_subscriptions.Add(partition))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to register partition.");
                }
                if (loadState == null)
                {
                    m_logger.SubscriptionAddedSecondaryPartition(partition.Id);
                }
                else
                {
                    m_logger.SubscriptionAddedPreloadedSecondaryPartition(partition.Id);
                }
            }
            m_publishControl.Set();
            return partition;
        }

        /// <summary>
        /// Restore a single subscription from a snapshot previously
        /// produced by <see cref="Subscription.Snapshot"/>. The
        /// returned subscription is registered with the manager via the
        /// same path as <see cref="Add"/>.
        /// </summary>
        /// <param name="handler">Notification handler for the restored
        /// subscription.</param>
        /// <param name="state">Snapshot captured earlier on the source
        /// session.</param>
        /// <param name="transferSubscriptions">
        /// When <c>true</c> the saved server-side subscription id and
        /// per-item server ids are preserved and an OPC UA
        /// TransferSubscriptions service call is issued so the new
        /// session takes over the existing server-side state. If
        /// transfer is unavailable (e.g. the server returns
        /// <c>BadSubscriptionIdInvalid</c>), the restore falls back to
        /// recreate.
        /// When <c>false</c> the V2 state machine mints fresh
        /// server-side ids — equivalent to a fresh
        /// <see cref="Add"/> with the saved configuration.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <remarks>
        /// Not on <see cref="ISubscriptionManager"/>: callers that want
        /// stream-based restore should use <see cref="LoadAsync"/>;
        /// fluent helpers and the serializer cast to the concrete
        /// <see cref="SubscriptionManager"/> to reach this method.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        internal ValueTask<ISubscription> RestoreAsync(
            ISubscriptionNotificationHandler handler,
            SubscriptionStateSnapshot state,
            bool transferSubscriptions = false,
            CancellationToken ct = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (transferSubscriptions && state.ServerId != 0)
            {
                return RestoreTransferAsync(handler, state, ct);
            }
            return RestoreRecreateAsync(handler, state, ct);
        }

        private ValueTask<ISubscription> RestoreRecreateAsync(
            ISubscriptionNotificationHandler handler,
            SubscriptionStateSnapshot state,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ISubscription subscription = Add(
                handler,
                new OptionsMonitor<SubscriptionOptions>(state.ToOptions()));
            foreach (MonitoredItemStateSnapshot item in state.MonitoredItems)
            {
                subscription.MonitoredItems.TryAdd(
                    item.Name,
                    new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(item.ToOptions()),
                    out _);
            }
            return new ValueTask<ISubscription>(subscription);
        }

        private async ValueTask<ISubscription> RestoreTransferAsync(
            ISubscriptionNotificationHandler handler,
            SubscriptionStateSnapshot state,
            CancellationToken ct)
        {
            // Build the internal load-state record (decoupled from the
            // public DTO so the engine has no DTO coupling). The reverse
            // "items I trigger" set is reconstructed on demand from the
            // triggering relationships per item.
            var itemLoadStates = new List<MonitoredItemLoadState>(
                state.MonitoredItems.Count);
            foreach (MonitoredItemStateSnapshot item in state.MonitoredItems)
            {
                IReadOnlyList<string> triggeredBy = item.TriggeredByNames.IsNull
                    ? []
                    : item.TriggeredByNames.ToArray() ?? [];
                itemLoadStates.Add(new MonitoredItemLoadState(
                    item.Name,
                    new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(item.ToOptions()),
                    item.ClientHandle,
                    item.ServerId,
                    triggeredBy));
            }
            var loadState = new SubscriptionLoadState(
                state.ServerId, itemLoadStates);

            SubscriptionOptions options = state.ToOptions();
            var optionsMonitor = new OptionsMonitor<SubscriptionOptions>(options);

            // Match the Add() path: wrap the user handler in a
            // forwarding shim when running in multi-partition mode so
            // post-restore growth keeps the serialised-dispatch +
            // logical-subscription-parameter contract.
            ISubscriptionNotificationHandler effectiveHandler = handler;
            PartitionForwardingHandler? forwardingHandler = null;
            if (!options.DisableUnboundedItemMode)
            {
#pragma warning disable CA2000 // ownership transfers to wrapper.AttachForwardingHandler
                forwardingHandler = new PartitionForwardingHandler(handler, m_logger);
#pragma warning restore CA2000
                effectiveHandler = forwardingHandler;
            }

            IManagedSubscription subscription = m_session.CreateSubscription(
                effectiveHandler,
                optionsMonitor,
                this,
                loadState);

            // Wrap in a logical subscription so callers receive the
            // same surface as a freshly added subscription. Pick the
            // same placement policy + factory the standard Add path
            // would use so post-restore growth obeys the configured
            // limits.
            PartitionPlacementPolicy? policy = null;
            Func<IManagedSubscription>? partitionFactory = null;
            LogicalSubscription wrapper;
            if (options.DisableUnboundedItemMode)
            {
                wrapper = new LogicalSubscription(subscription, null, null);
            }
            else
            {
                policy = new PartitionPlacementPolicy(
                    options.MaxMonitoredItemsPerPartition ?? 0,
                    options.MaxPartitionCount);
                LogicalSubscription? created = null;
                partitionFactory = () => MintPartition(effectiveHandler, optionsMonitor,
                    part => InitializePartition(part, created));
                wrapper = new LogicalSubscription(subscription, policy, partitionFactory,
                    m_timeProvider,
                    NormalizeIdleTimeout(options.SecondaryPartitionIdleTimeout),
                    RemoveSecondaryPartitionAsync);
                created = wrapper;
                AttachReactiveFallback(subscription, wrapper);
                forwardingHandler!.BindLogical(wrapper);
                wrapper.AttachForwardingHandler(forwardingHandler);
            }

            lock (m_subscriptionLock)
            {
                if (!m_subscriptions.Add(subscription))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to add restored subscription.");
                }
                if (!m_logicals.Add(wrapper))
                {
                    m_subscriptions.Remove(subscription);
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to register restored logical subscription.");
                }
                m_logger.SubscriptionAddedTransferPending(subscription.Id, state.ServerId);
            }

            try
            {
                // Issue TransferSubscriptions for the saved server id.
                // sendInitialValues honors SubscriptionOptions.SendInitialValuesOnTransfer
                // (default false) — the snapshot captured the last server-
                // emitted values, so requesting initial values is only
                // useful when the caller wants the server to re-emit them
                // to a fresh notification handler.
                ArrayOf<uint> subscriptionIds = [state.ServerId];
                bool transferred = false;
                try
                {
                    TransferSubscriptionsResponse response = await m_session.TransferSubscriptionsAsync(
                        null,
                        subscriptionIds,
                        sendInitialValues: options.SendInitialValuesOnTransfer,
                        ct)
                        .ConfigureAwait(false);

                    ResponseHeader responseHeader = response.ResponseHeader;
                    if (StatusCode.IsGood(responseHeader.ServiceResult))
                    {
                        ArrayOf<TransferResult> results = response.Results;
                        ClientBase.ValidateResponse(results, subscriptionIds);
                        if (results.Count > 0 && StatusCode.IsGood(results[0].StatusCode))
                        {
                            transferred = await subscription.TryCompleteTransferAsync(
                                results[0].AvailableSequenceNumbers.IsNull
                                    ? []
                                    : [.. results[0].AvailableSequenceNumbers],
                                ct).ConfigureAwait(false);
                        }
                        else if (results.Count > 0)
                        {
                            m_logger.TransferPerItemResultBad(subscription.Id, results[0].StatusCode);
                        }
                    }
                    else if (responseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
                    {
                        m_logger.ServerDoesNotSupportTransfer(subscription.Id);
                    }
                    else
                    {
                        m_logger.TransferServiceLevelResultBad(
                            subscription.Id,
                            responseHeader.ServiceResult);
                    }
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    m_logger.ServerDoesNotSupportTransfer(subscription.Id);
                }
                catch (ServiceResultException ex)
                {
                    m_logger.TransferServiceLevelResultBad(
                        subscription.Id,
                        ex.StatusCode);
                }

                if (!transferred && subscription is Subscription loaded)
                {
                    await loaded.ResetToRecreateAsync(ct).ConfigureAwait(false);
                    // Await re-creation under a bounded timeout so LoadAsync
                    // only returns after the server has assigned a fresh
                    // SubscriptionId. Items still need ApplyChangesAsync to
                    // round-trip but that runs in the same state-manager
                    // iteration as CreateAsync.
                    using CancellationTokenSource timeoutCts = m_timeProvider
                        .CreateCancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var linkedCts = CancellationTokenSource
                        .CreateLinkedTokenSource(ct, timeoutCts.Token);
                    try
                    {
                        await loaded.WaitForCreatedAsync(linkedCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        m_logger.ReCreationDidNotComplete(subscription.Id);
                    }
                }
            }
            catch
            {
                // The subscription and its wrapper are already registered.
                // Leaving them behind on a failed transfer would register a
                // zombie that the publish loop keeps dispatching to and that
                // no caller ever gets a handle to.
                lock (m_subscriptionLock)
                {
                    m_logicals.Remove(wrapper);
                    m_subscriptions.Remove(subscription);
                }
                try
                {
                    await wrapper.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeException)
                    when (disposeException is not OutOfMemoryException)
                {
                    m_logger.ReCreationDidNotComplete(subscription.Id);
                }
                throw;
            }

            m_publishControl.Set();
            return wrapper;
        }

        /// <summary>
        /// Restore a logical subscription whose partitions span more
        /// than one snapshot record (multi-partition snapshot
        /// produced by <see cref="LogicalSubscription.SnapshotAllPartitions"/>).
        /// The primary snapshot (<c>PartitionIndex == 0</c>) is
        /// restored via the existing single-snapshot path; each
        /// secondary snapshot is then attached to the same wrapper
        /// as an additional partition with its server-side
        /// identifier preserved (transfer) or recreated (recreate).
        /// </summary>
        /// <remarks>
        /// The caller is expected to pre-sort
        /// <paramref name="snapshots"/> by <c>PartitionIndex</c> and
        /// to have validated the grouping (contiguous indexes,
        /// exactly one primary, shared <c>LogicalGroupId</c>) — see
        /// <see cref="SubscriptionManagerSerializer.LoadAsync"/>
        /// which performs the validation before dispatching here.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal async ValueTask<ISubscription> RestoreGroupAsync(
            ISubscriptionNotificationHandler handler,
            IReadOnlyList<SubscriptionStateSnapshot> snapshots,
            bool transferSubscriptions,
            CancellationToken ct)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            if (snapshots == null || snapshots.Count == 0)
            {
                throw new ArgumentException(
                    "Group must contain at least the primary snapshot.",
                    nameof(snapshots));
            }

            // Primary first — single-partition restore returns the
            // wrapper that owns all subsequent secondaries.
            ISubscription primaryWrapper = await RestoreAsync(
                handler, snapshots[0], transferSubscriptions, ct)
                .ConfigureAwait(false);
            if (snapshots.Count == 1)
            {
                return primaryWrapper;
            }

            if (primaryWrapper is not LogicalSubscription wrapper)
            {
                throw new InvalidOperationException(
                    "Restore returned a subscription that does not " +
                    "support multi-partition grouping.");
            }

            // Reconstruct the options the secondaries share with
            // the primary. The primary's wrapper was built from
            // snapshots[0] so any field accessible via ToOptions()
            // is valid for every partition in the group.
            SubscriptionOptions options = snapshots[0].ToOptions();
            var optionsMonitor = new OptionsMonitor<SubscriptionOptions>(options);

            try
            {
                for (int i = 1; i < snapshots.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    await AttachSecondaryPartitionAsync(
                        wrapper, handler, optionsMonitor,
                        snapshots[i], transferSubscriptions, ct)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // The primary and any secondaries attached so far are
                // registered, but the caller never receives the wrapper.
                // Roll back like RestoreAsync does for a failed transfer so
                // no zombie keeps a server subscription alive and keeps
                // calling the handler with nobody able to dispose it.
                IReadOnlyList<IManagedSubscription> partitions = wrapper.Partitions;
                lock (m_subscriptionLock)
                {
                    m_logicals.Remove(wrapper);
                    foreach (IManagedSubscription partition in partitions)
                    {
                        m_subscriptions.Remove(partition);
                    }
                }
                try
                {
                    await wrapper.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception disposeException)
                    when (disposeException is not OutOfMemoryException)
                {
                    m_logger.ReCreationDidNotComplete(
                        partitions.Count > 0 ? partitions[0].Id : 0);
                }
                throw;
            }
            return wrapper;
        }

        private async ValueTask AttachSecondaryPartitionAsync(
            LogicalSubscription wrapper,
            ISubscriptionNotificationHandler handler,
            OptionsMonitor<SubscriptionOptions> optionsMonitor,
            SubscriptionStateSnapshot snap,
            bool transferSubscriptions,
            CancellationToken ct)
        {
            // Build the per-partition load state from the snapshot.
            var itemLoadStates = new List<MonitoredItemLoadState>(
                snap.MonitoredItems.Count);
            foreach (MonitoredItemStateSnapshot item in snap.MonitoredItems)
            {
                itemLoadStates.Add(new MonitoredItemLoadState(
                    item.Name,
                    new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(item.ToOptions()),
                    item.ClientHandle,
                    item.ServerId,
                    item.TriggeredByNames.IsNull
                        ? []
                        : item.TriggeredByNames.ToArray() ?? []));
            }
            var loadState = new SubscriptionLoadState(snap.ServerId, itemLoadStates);

            // Mint the partition via the preloaded-variant factory so
            // it is wired into the dispatch registry from the moment
            // it is constructed. Route the partition's notification
            // callbacks through the wrapper's forwarding handler so
            // restored secondaries observe the same serialization and
            // logical-subscription identity contract that fresh-Add
            // secondaries get; without this the raw user handler is
            // invoked concurrently from multiple partitions on the
            // restore path, breaking the single-threaded dispatch
            // guarantee callers rely on.
            ISubscriptionNotificationHandler effective =
                wrapper.ForwardingHandler ?? handler;
            IManagedSubscription partition = MintPreloadedPartition(
                effective, optionsMonitor, loadState);
            try
            {
                AttachReactiveFallback(partition, wrapper);

                // Append to the wrapper's partition list so the composite
                // collection's policy + index account for it.
                wrapper.AppendPreloadedPartition(partition);
            }
            catch
            {
                // Not yet owned by the wrapper, so the group rollback would
                // not find it.
                lock (m_subscriptionLock)
                {
                    m_subscriptions.Remove(partition);
                }
                await partition.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            // Best-effort transfer: when the saved ServerId is
            // non-zero AND the caller asked for transfer, issue
            // TransferSubscriptions; on failure - and when no transfer was
            // requested at all - fall back to recreate via the partition's own
            // state machine.
            if (partition is not Subscription concrete)
            {
                return;
            }

            if (!transferSubscriptions || snap.ServerId == 0)
            {
                // Without a transfer the saved server-side subscription does
                // not belong to this session: the partition would keep the
                // stale identifier, never be created, and silently deliver
                // nothing. Drive it through the recreate path instead.
                await concrete.ResetToRecreateAsync(ct).ConfigureAwait(false);
                return;
            }

            bool transferred = false;
            try
            {
                TransferSubscriptionsResponse response =
                    await m_session.TransferSubscriptionsAsync(
                        null,
                        new uint[] { snap.ServerId }.ToArrayOf(),
                        sendInitialValues: optionsMonitor.CurrentValue.SendInitialValuesOnTransfer,
                        ct).ConfigureAwait(false);
                if (StatusCode.IsGood(response.ResponseHeader.ServiceResult) &&
                    response.Results.Count > 0 &&
                    StatusCode.IsGood(response.Results[0].StatusCode))
                {
                    transferred = await concrete.TryCompleteTransferAsync(
                        response.Results[0].AvailableSequenceNumbers.IsNull
                            ? []
                            : [.. response.Results[0].AvailableSequenceNumbers],
                        ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.TransferThrewSecondaryPartitionRestore(ex, partition.Id);
            }
            if (!transferred)
            {
                await concrete.ResetToRecreateAsync(ct).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask SaveAsync(
            System.IO.Stream stream,
            IServiceMessageContext messageContext,
            IEnumerable<ISubscription>? subscriptions = null,
            CancellationToken ct = default)
        {
            return SubscriptionManagerSerializer.SaveAsync(
                this,
                stream,
                messageContext,
                subscriptions,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<ISubscription>> LoadAsync(
            System.IO.Stream stream,
            IServiceMessageContext messageContext,
            Func<string, ISubscriptionNotificationHandler> handlerFactory,
            bool transferSubscriptions = false,
            CancellationToken ct = default)
        {
            return SubscriptionManagerSerializer.LoadAsync(
                this,
                stream,
                messageContext,
                handlerFactory,
                transferSubscriptions,
                ct);
        }

        /// <summary>
        /// Resume subscriptions
        /// </summary>
        internal void Resume()
        {
            SetPublishingRequested(true);
        }

        /// <summary>
        /// Pause subscriptions
        /// </summary>
        internal void Pause()
        {
            SetPublishingRequested(false);
        }

        private void SetPublishingRequested(bool requested)
        {
            bool? paused;
            lock (m_publishStateLock)
            {
                m_publishingRequested = requested;
                paused = UpdatePublishingState();
            }
            if (paused.HasValue)
            {
                NotifySubscriptionsPaused(paused.Value);
            }
            // Wake the controller so the pool is sized for the new state.
            // Resuming is the first point at which a manager that was
            // constructed paused has a reason to hold workers at all.
            m_publishControl.Set();
        }

        internal void SetSessionRecoveryPaused(bool paused)
        {
            SetPublishingQuiesced(paused, sessionRecovery: true);
        }

        private void SetPublishingQuiesced(bool quiesced, bool sessionRecovery = false)
        {
            bool? paused;
            CancellationTokenSource[] attempts = [];
            lock (m_publishStateLock)
            {
                if (sessionRecovery)
                {
                    m_sessionRecoveryPaused = quiesced;
                    if (quiesced)
                    {
                        m_sessionAvailable.Reset();
                        attempts = [.. m_activeSubscriptionUpdates];
                    }
                    else
                    {
                        m_sessionAvailable.Set();
                    }
                }
                else
                {
                    m_publishingQuiesced = quiesced;
                }
                paused = UpdatePublishingState();
            }
            CancelAttempts(attempts);
            if (paused.HasValue)
            {
                NotifySubscriptionsPaused(paused.Value);
            }
            // See SetPublishingRequested - leaving quiescence resumes
            // publishing just like Resume does.
            m_publishControl.Set();
        }

        /// <summary>
        /// Updates publishing availability and retires error notifications from the previous lifecycle epoch.
        /// </summary>
        private bool? UpdatePublishingState()
        {
            bool shouldRun = m_publishingRequested &&
                !m_publishingQuiesced &&
                !m_sessionRecoveryPaused &&
                Volatile.Read(ref m_disposed) == 0;
            if (m_running.IsSet == shouldRun)
            {
                return null;
            }
            m_publishEpoch++;
            m_publishRecoveryReported = false;
            if (shouldRun)
            {
                m_publishingPaused.Reset();
                m_running.Set();
            }
            else
            {
                m_running.Reset();
                m_publishingPaused.Set();
            }
            return !shouldRun;
        }

        private void NotifySubscriptionsPaused(bool paused)
        {
            lock (m_subscriptionLock)
            {
                foreach (IManagedSubscription item in m_subscriptions)
                {
                    item.NotifySubscriptionManagerPaused(paused);
                }
            }
        }

        /// <summary>
        /// Wait for all in-flight publish requests to complete or be
        /// cancelled. Used together with <see cref="Pause"/> by callers
        /// that need a hard quiesce (e.g. a session re-create) before
        /// rebinding session state. <see cref="Pause"/> alone is a
        /// soft signal — workers complete their current cycle before
        /// observing the pause.
        /// </summary>
        /// <remarks>
        /// A worker that began its request before the quiesce can be parked
        /// inside the service call waiting for the channel to become ready.
        /// That wait cannot finish while the caller draining here is the very
        /// operation that makes the channel ready, so the attempts are aborted
        /// first. Aborting only cancels the client-side attempt: the worker
        /// rolls its acknowledgements back and returns to the paused park, so
        /// publish ingress stays quiesced for the caller's operation.
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        internal Task DrainAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_activePublishRequests) == 0)
            {
                return Task.CompletedTask;
            }
            AbortActivePublishRequests();
            return m_drainSignal.WaitAsync(ct);
        }

        /// <summary>
        /// Cancels the publish attempts that are currently in flight without
        /// stopping their workers.
        /// </summary>
        private void AbortActivePublishRequests()
        {
            CancellationTokenSource[] attempts;
            lock (m_publishStateLock)
            {
                if (m_activePublishAttempts.Count == 0)
                {
                    return;
                }
                attempts = [.. m_activePublishAttempts];
            }
            CancelAttempts(attempts);
        }

        private static void CancelAttempts(CancellationTokenSource[] attempts)
        {
            foreach (CancellationTokenSource attempt in attempts)
            {
                try
                {
                    attempt.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The worker completed its attempt and released the source.
                }
            }
        }

        /// <summary>
        /// Registers a publish attempt so a quiesce can abort it.
        /// </summary>
        private void RegisterPublishAttempt(CancellationTokenSource attempt)
        {
            lock (m_publishStateLock)
            {
                m_activePublishAttempts.Add(attempt);
            }
        }

        /// <summary>
        /// Removes a completed publish attempt from the abortable set.
        /// </summary>
        private void UnregisterPublishAttempt(CancellationTokenSource attempt)
        {
            lock (m_publishStateLock)
            {
                m_activePublishAttempts.Remove(attempt);
            }
        }

        /// <summary>
        /// Reserves an active request before acknowledgement preparation so recovery can drain it.
        /// </summary>
        private bool TryBeginPublishRequest()
        {
            lock (m_publishStateLock)
            {
                if (!m_running.IsSet || Volatile.Read(ref m_disposed) != 0)
                {
                    return false;
                }
                if (++m_activePublishRequests == 1)
                {
                    m_drainSignal.Reset();
                }
                return true;
            }
        }

        /// <summary>
        /// Captures recovery ordering after preparation, immediately before sending Publish.
        /// </summary>
        private bool TryCapturePublishDispatch(out long epoch, out long requestSequence)
        {
            lock (m_publishStateLock)
            {
                epoch = m_publishEpoch;
                requestSequence = 0;
                if (!m_running.IsSet || Volatile.Read(ref m_disposed) != 0)
                {
                    return false;
                }
                requestSequence = ++m_publishRequestSequence;
                return true;
            }
        }

        /// <summary>
        /// Retires Publish recovery signals from the transport preceding successful session reactivation.
        /// </summary>
        internal void OnSessionReactivated()
        {
            lock (m_publishStateLock)
            {
                m_publishEpoch++;
                m_publishRecoveryWatermark = m_publishRequestSequence;
                m_publishRecoveryReported = false;
            }
        }

        /// <summary>
        /// Rearms failure reporting only for success from an attempt begun after the reported outage.
        /// An older in-flight response cannot clear a newer failure's notification state.
        /// </summary>
        private void RecordPublishSuccess(long epoch, long requestSequence)
        {
            lock (m_publishStateLock)
            {
                if (epoch == m_publishEpoch && requestSequence > m_publishRecoveryWatermark)
                {
                    m_publishRecoveryWatermark = requestSequence;
                    m_publishRecoveryReported = false;
                }
            }
        }

        /// <summary>
        /// Coalesces current Publish failures before notifying the session outside the state lock.
        /// A failure from an attempt begun before the last reported outage belongs to that outage
        /// and is ignored, even after a newer success has rearmed reporting in the same epoch.
        /// </summary>
        private async ValueTask ReportPublishRecoveryErrorAsync(
            long epoch, long requestSequence, NodeId sessionId, ServiceResult error, CancellationToken ct)
        {
            long recoveryWatermark;
            long previousWatermark;
            lock (m_publishStateLock)
            {
                if (epoch != m_publishEpoch ||
                    !m_running.IsSet ||
                    Volatile.Read(ref m_disposed) != 0 ||
                    m_publishRecoveryReported ||
                    requestSequence <= m_publishRecoveryWatermark)
                {
                    return;
                }
                m_publishRecoveryReported = true;
                previousWatermark = m_publishRecoveryWatermark;
                m_publishRecoveryWatermark = m_publishRequestSequence;
                recoveryWatermark = m_publishRecoveryWatermark;
            }

            bool accepted = false;
            try
            {
                accepted = await m_session.OnPublishRecoveryErrorAsync(sessionId, error, IsCurrent, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (!accepted)
                {
                    lock (m_publishStateLock)
                    {
                        if (epoch == m_publishEpoch &&
                            m_publishRecoveryReported &&
                            m_publishRecoveryWatermark == recoveryWatermark)
                        {
                            m_publishRecoveryReported = false;
                            m_publishRecoveryWatermark = previousWatermark;
                        }
                    }
                }
            }

            bool IsCurrent()
            {
                lock (m_publishStateLock)
                {
                    return epoch == m_publishEpoch &&
                        m_running.IsSet &&
                        Volatile.Read(ref m_disposed) == 0 &&
                        m_publishRecoveryReported &&
                        m_publishRecoveryWatermark == recoveryWatermark;
                }
            }
        }

        private void EndPublishRequest()
        {
            lock (m_publishStateLock)
            {
                if (--m_activePublishRequests == 0)
                {
                    m_drainSignal.Set();
                }
            }
        }

        /// <summary>
        /// Recreate subscriptions
        /// </summary>
        /// <param name="previousSessionId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async Task RecreateSubscriptionsAsync(
            NodeId? previousSessionId,
            CancellationToken ct)
        {
            if (Count == 0)
            {
                // Nothing to do
                return;
            }

            IReadOnlyList<IManagedSubscription> subscriptions;
            lock (m_subscriptionLock)
            {
                subscriptions = [.. m_subscriptions];
            }
            if (TransferSubscriptionsOnRecreate && previousSessionId != null)
            {
                subscriptions = await TransferSubscriptionsAsync(subscriptions,
                    false, ct).ConfigureAwait(false);
            }
            // Force creation of the subscriptions which were not transferred.
            foreach (IManagedSubscription subscription in subscriptions)
            {
                await subscription.RecreateAsync(ct).ConfigureAwait(false);
            }
            m_publishControl.Set();

            // Helper to try and transfer the subscriptions
            async Task<IReadOnlyList<IManagedSubscription>> TransferSubscriptionsAsync(
                IReadOnlyList<IManagedSubscription> subscriptions,
                bool sendInitialValues,
                CancellationToken ct)
            {
                var remaining = subscriptions.Where(s => !s.Created).ToList();
                subscriptions = [.. subscriptions.Where(s => s.Created)];
                if (subscriptions.Count == 0)
                {
                    return remaining;
                }
                var subscriptionIds = subscriptions.Select(s => s.Id).ToArrayOf();
                TransferSubscriptionsResponse response;
                try
                {
                    response = await m_session.TransferSubscriptionsAsync(
                        null,
                        subscriptionIds,
                        sendInitialValues,
                        ct).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    // The generated client validates the response header and
                    // throws, so a service fault never reaches the header check
                    // below. Treat every subscription as remaining and let the
                    // recreate loop rebuild them instead of aborting the whole
                    // session recreate (which would also leak the server side
                    // session on every reconnect).
                    if (sre.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnRecreate = false;
                        m_logger.TransferSubscriptionUnsupported();
                    }
                    else
                    {
                        m_logger.TransferSubscriptionsFailed(sre.StatusCode);
                    }
                    remaining.AddRange(subscriptions);
                    return remaining;
                }

                ResponseHeader responseHeader = response.ResponseHeader;
                if (!StatusCode.IsGood(responseHeader.ServiceResult))
                {
                    if (responseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnRecreate = false;
                        m_logger.TransferSubscriptionUnsupported();
                    }
                    else
                    {
                        m_logger.TransferSubscriptionsFailed(responseHeader.ServiceResult);
                    }
                    remaining.AddRange(subscriptions);
                    return remaining;
                }

                ArrayOf<TransferResult> transferResults = response.Results;
                ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
                ClientBase.ValidateResponse(transferResults, subscriptionIds);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);
                for (int index = 0; index < subscriptions.Count; index++)
                {
                    if (transferResults[index].StatusCode == StatusCodes.BadNothingToDo)
                    {
                        m_logger.SubscriptionAlreadyMember(subscriptionIds[index]);
                        // Done
                    }
                    else if (!StatusCode.IsGood(transferResults[index].StatusCode))
                    {
                        m_logger.SubscriptionFailedToTransfer(
                            subscriptionIds[index],
                            transferResults[index].StatusCode);
                        remaining.Add(subscriptions[index]);
                    }
                    else
                    {
                        bool success = await subscriptions[index].TryCompleteTransferAsync(
                            transferResults[index].AvailableSequenceNumbers.ToList(),
                            ct).ConfigureAwait(false);
                        if (success)
                        {
                            continue;
                        }
                        //
                        // Recreate as we cannot sync the subscription. This happens
                        // when the GetMonitoredItems call fails and we cannot synchronize
                        // the subscription state
                        //
                        remaining.Add(subscriptions[index]);
                    }
                }
                return remaining;
            }
        }

        /// <summary>
        /// Get subscription with the specified id
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        private IManagedSubscription? GetById(uint subscriptionId)
        {
            lock (m_subscriptionLock)
            {
                // The most recently created owner wins when a server reused the id
                // before the client noticed that the previous owner expired.
                if (m_claims.TryGetValue(subscriptionId, out SubscriptionClaim claim) &&
                    IsLiveOwnerLocked(claim.Owner, subscriptionId))
                {
                    return (IManagedSubscription)claim.Owner;
                }

                // find the subscription.
                foreach (IManagedSubscription subscription in m_subscriptions)
                {
                    if (subscription.Id == subscriptionId && !m_superseded.Contains(subscription))
                    {
                        return subscription;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Controls the publish workers.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task PublishControllerAsync(CancellationToken ct)
        {
            var publishWorkers = new List<PublishWorker>();
            //
            // The control wait is created once and only re-armed after it
            // has completed. Creating a fresh wait on every iteration would
            // abandon the previous waiter inside the auto reset event, and a
            // later Set() would then be handed to the abandoned waiter and
            // lost — leaving the worker pool permanently unresized.
            //
            Task controlWait = m_publishControl.WaitAsync(ct);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    //
                    // Wait before touching the pool. The controller runs
                    // synchronously up to this first await, i.e. still inside
                    // the constructor, where MinPublishWorkerCount and
                    // MaxPublishWorkerCount are whatever the defaults are —
                    // the owner has not had a chance to configure them yet.
                    // Sizing the pool there spins up workers off the default
                    // minimum only to tear them down again on the first
                    // resize, and publishes that overshoot through
                    // PublishWorkerCount. Every path that changes the desired
                    // count (add/remove, pause/resume, the Min/Max setters,
                    // Update) signals m_publishControl, so the pool is sized
                    // exclusively in response to a signal or a worker exit.
                    //
                    Task[] waiting = [.. publishWorkers
                        .Select(w => w.Task)
                        .Prepend(controlWait)];
                    await Task.WhenAny(waiting).ConfigureAwait(false);
                    if (controlWait.IsCompleted)
                    {
                        controlWait = m_publishControl.WaitAsync(ct);
                    }
                    PublishControlCycles++;

                    // Reap the workers that exited on their own before
                    // sizing the pool, so a worker that is already gone is
                    // never counted towards the pool nor kept alongside its
                    // replacement.
                    int index = 0;
                    foreach (Task? item in waiting.Skip(1)) // Skip wait handle
                    {
                        if (item.IsCompleted)
                        {
                            PublishWorker worker = publishWorkers[index];
                            m_logger.PublishWorkerExited(worker.Index);
                            await worker.DisposeAsync().ConfigureAwait(false);
                            publishWorkers.RemoveAt(index);
                            continue;
                        }
                        index++;
                    }

                    // Now lower the max publish request if we got any too
                    // many requests errors. Consume the flags while doing so:
                    // the MaxPublishWorkerCount setter signals this controller
                    // again, and a flag that is only cleared by the worker's
                    // next successful publish would make every one of those
                    // wake-ups decrement the cap until it collapses to 1.
                    bool tooManyPublishRequests = false;
                    foreach (PublishWorker worker in publishWorkers)
                    {
                        if (worker.TooManyPublishRequests)
                        {
                            tooManyPublishRequests = true;
                            worker.TooManyPublishRequests = false;
                        }
                    }
                    if (tooManyPublishRequests && MaxPublishWorkerCount > 1)
                    {
                        MaxPublishWorkerCount--;
                    }

                    int desiredWorkerCount = GetDesiredPublishWorkerCount();
                    if (publishWorkers.Count > desiredWorkerCount)
                    {
                        // Too many workers, reduce
#if NET8_0_OR_GREATER
                        foreach (PublishWorker worker in publishWorkers[desiredWorkerCount..])
#else
                        foreach (PublishWorker worker in publishWorkers.Skip(desiredWorkerCount))
#endif
                        {
                            m_logger.RemovingPublishWorker(worker.Index);
                            await worker.DisposeAsync().ConfigureAwait(false);
                        }
#if NET8_0_OR_GREATER
                        publishWorkers = publishWorkers[..desiredWorkerCount];
#else
                        publishWorkers = [.. publishWorkers.Take(desiredWorkerCount)];
#endif
                    }
                    else if (desiredWorkerCount > publishWorkers.Count)
                    {
                        // Not enough workers increase. Publish the new count
                        // before the workers start: they run on the thread
                        // pool and can issue their first publish before this
                        // loop continues, and a caller that observes a publish
                        // in flight must not still see the old count.
                        int count = publishWorkers.Count;
                        PublishWorkerCount = desiredWorkerCount;
                        publishWorkers.AddRange(Enumerable
                            .Range(count, desiredWorkerCount - count)
                            .Select(index => new PublishWorker(this, index)));
                    }

                    // The pool is settled for this cycle.
                    PublishWorkerCount = publishWorkers.Count;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                // Controller exits, clean up all workers
                foreach (PublishWorker worker in publishWorkers)
                {
                    await worker.DisposeAsync().ConfigureAwait(false);
                    PublishWorkerCount--;
                }
            }

            int GetDesiredPublishWorkerCount()
            {
                int publishCount;
                lock (m_subscriptionLock)
                {
                    // Only subscriptions that established their own demand retain it across recreation.
                    // Pending subscriptions cannot inherit workers from classic or removed subscriptions.
                    m_establishedSubscriptions.IntersectWith(m_subscriptions);
                    foreach (IManagedSubscription subscription in m_subscriptions)
                    {
                        if (subscription.IsIntentionallyDeleted)
                        {
                            m_establishedSubscriptions.Remove(subscription);
                        }
                        else if (subscription.Created)
                        {
                            m_establishedSubscriptions.Add(subscription);
                        }
                    }
                    publishCount = m_establishedSubscriptions.Count;
                }
                publishCount += m_session.SessionSubscriptionCount;
                if (publishCount != 0)
                {
                    //
                    // Limit resulting to a number between min and max
                    // request count. If max is below min, we honor the
                    // min publish request count.
                    //
                    if (publishCount > MaxPublishWorkerCount)
                    {
                        publishCount = MaxPublishWorkerCount;
                    }
                    if (publishCount < MinPublishWorkerCount)
                    {
                        publishCount = MinPublishWorkerCount;
                    }
                    if (publishCount <= 0)
                    {
                        publishCount = 1;
                    }
                }
                return publishCount;
            }
        }

        /// <summary>
        /// Removes stale entries and applies the session's deferral policy to the
        /// acknowledgements selected for the next Publish request together with the
        /// previously deferred ones. The policy callback is serialized, matching the
        /// classic engine, so handlers never observe concurrent invocations.
        /// </summary>
        /// <param name="acknowledgements">The worker's batch; it may be reused for the result.</param>
        private List<PendingAcknowledgement> PrepareAcknowledgementsToSend(
            List<PendingAcknowledgement> acknowledgements)
        {
            lock (m_acknowledgementPolicyLock)
            {
                int deferredCount = m_deferredAcknowledgements.Count;
                if (acknowledgements.Count == 0 && deferredCount == 0)
                {
                    return acknowledgements;
                }
                if (deferredCount == 0 && !m_session.HasAcknowledgementPolicy)
                {
                    // Without a deferral policy the batch is only validated: nothing is copied
                    // or presented, and no callback can change ownership before it is sent.
                    RemoveStaleAcknowledgements(acknowledgements);
                    return acknowledgements;
                }

                List<PendingAcknowledgement> candidates = acknowledgements;
                List<PendingAcknowledgement>? previouslyDeferred = null;
                if (deferredCount != 0)
                {
                    candidates = new List<PendingAcknowledgement>(acknowledgements.Count + deferredCount);
                    candidates.AddRange(acknowledgements);
                    candidates.AddRange(m_deferredAcknowledgements);
                    previouslyDeferred = [.. m_deferredAcknowledgements];
                    m_deferredAcknowledgements.Clear();
                }
                RemoveStaleAcknowledgements(candidates);
                if (candidates.Count == 0)
                {
                    UpdateDeferredWarning();
                    return candidates;
                }

                // Reused under the policy lock; the callback must not keep the list.
                List<SubscriptionAcknowledgement> presented = m_presentedAcknowledgements;
                presented.Clear();
                foreach (PendingAcknowledgement candidate in candidates)
                {
                    presented.Add(candidate.Acknowledgement);
                }
                Dictionary<SubscriptionAcknowledgement, PendingAcknowledgement>? byIdentity = null;

                List<SubscriptionAcknowledgement> toSend;
                List<SubscriptionAcknowledgement> retained;
                try
                {
                    (toSend, retained) = m_session.PrepareAcknowledgementsToSend(presented);
                }
                catch
                {
                    // The worker requeues its own batch; restore only the older deferred entries.
                    if (previouslyDeferred != null)
                    {
                        m_deferredAcknowledgements.AddRange(previouslyDeferred);
                    }
                    throw;
                }

                // Without a deferral policy, or when it changed nothing, the candidates are sent
                // as is. They are validated again, since a subscription can retire during the callback.
                if (retained.Count == 0 && IsUnchanged(toSend, candidates))
                {
                    UpdateDeferredWarning();
                    RemoveStaleAcknowledgements(candidates);
                    return candidates;
                }
                int unresolved = 0;
                int deferredStart = m_deferredAcknowledgements.Count;
                foreach (SubscriptionAcknowledgement acknowledgement in retained)
                {
                    if (TryResolve(acknowledgement, out PendingAcknowledgement pending))
                    {
                        m_deferredAcknowledgements.Add(pending);
                    }
                    else
                    {
                        unresolved++;
                    }
                }
                UpdateDeferredWarning();
                var result = new List<PendingAcknowledgement>(toSend.Count);
                foreach (SubscriptionAcknowledgement acknowledgement in toSend)
                {
                    if (TryResolve(acknowledgement, out PendingAcknowledgement pending))
                    {
                        result.Add(pending);
                    }
                    else
                    {
                        unresolved++;
                    }
                }
                if (unresolved != 0)
                {
                    m_logger.StaleAcknowledgementsDiscarded(unresolved);
                }
                if (RequiresAcknowledgementTracking)
                {
                    ReportDroppedAcknowledgements(candidates, result, deferredStart);
                }
                // Entries the handler created are validated like queued ones before sending.
                RemoveStaleAcknowledgements(result);
                return result;

                // A handler may add acknowledgements it created itself. They are attributed to
                // the current owner of their id, so they are validated like any other entry.
                bool TryResolve(SubscriptionAcknowledgement acknowledgement, out PendingAcknowledgement pending)
                {
                    if (TryLookup(acknowledgement, out pending))
                    {
                        return true;
                    }
                    IManagedSubscription? owner = GetById(acknowledgement.SubscriptionId);
                    if (owner == null)
                    {
                        return false;
                    }
                    long generation = owner.MessageGeneration;
                    pending = new PendingAcknowledgement(
                        acknowledgement, owner.GetAcknowledgementScope(generation));
                    return true;
                }

                // The identity map is only built when a handler changed the lists.
                bool TryLookup(SubscriptionAcknowledgement acknowledgement, out PendingAcknowledgement pending)
                {
                    if (byIdentity == null)
                    {
                        byIdentity = new Dictionary<SubscriptionAcknowledgement, PendingAcknowledgement>(
                            candidates.Count, IdentityComparer<SubscriptionAcknowledgement>.Instance);
                        foreach (PendingAcknowledgement candidate in candidates)
                        {
                            byIdentity[candidate.Acknowledgement] = candidate;
                        }
                    }
                    return byIdentity.TryGetValue(acknowledgement, out pending);
                }
            }
        }

        /// <summary>
        /// Prepares the acknowledgements of the next Publish request. With an
        /// <see cref="ISubscriptionAcknowledgementProvider"/>, the provider decides which
        /// acknowledgements are sent and which are deferred, without holding any lock; the
        /// legacy deferral event is then not raised. Otherwise the legacy policy applies.
        /// </summary>
        /// <param name="acknowledgements">The worker's batch; it may be reused for the result.</param>
        /// <param name="ct">The Publish attempt's cancellation token.</param>
        private async ValueTask<List<PendingAcknowledgement>> PrepareAcknowledgementsToSendAsync(
            List<PendingAcknowledgement> acknowledgements,
            CancellationToken ct)
        {
            ISubscriptionAcknowledgementProvider? provider = m_session.AcknowledgementProvider;
            if (provider == null)
            {
                return PrepareAcknowledgementsToSend(acknowledgements);
            }

            List<PendingAcknowledgement> candidates = acknowledgements;
            lock (m_acknowledgementPolicyLock)
            {
                if (m_deferredAcknowledgements.Count != 0)
                {
                    // Merged into the worker's batch, so an abandoned attempt requeues them as well.
                    candidates.AddRange(m_deferredAcknowledgements);
                    m_deferredAcknowledgements.Clear();
                }
            }
            RemoveStaleAcknowledgements(candidates);
            if (candidates.Count == 0)
            {
                lock (m_acknowledgementPolicyLock)
                {
                    UpdateDeferredWarning();
                }
                return candidates;
            }

            var handles = new AcknowledgementHandle[candidates.Count];
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = candidates[i].Handle;
            }
            AcknowledgementDecision decision;
            try
            {
                decision = await provider.DecideAsync(handles, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The worker requeues the batch of an abandoned attempt.
                throw;
            }
            catch (Exception ex)
            {
                // Fail closed: nothing is acknowledged that the provider did not approve.
                m_logger.AcknowledgementProviderFailed(ex, candidates.Count);
                DeferAcknowledgements(candidates);
                return [];
            }

            ArrayOf<int> deferredIndices = decision.Deferred;
            if (deferredIndices.Count == 0)
            {
                // Validated again: a subscription can retire while the provider decides.
                RemoveStaleAcknowledgements(candidates);
                return candidates;
            }
            bool[] deferred = new bool[candidates.Count];
            for (int i = 0; i < deferredIndices.Count; i++)
            {
                int index = deferredIndices[i];
                if ((uint)index < (uint)deferred.Length)
                {
                    deferred[index] = true;
                }
            }
            var toSend = new List<PendingAcknowledgement>(candidates.Count);
            var toDefer = new List<PendingAcknowledgement>(deferredIndices.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                (deferred[i] ? toDefer : toSend).Add(candidates[i]);
            }
            DeferAcknowledgements(toDefer);
            RemoveStaleAcknowledgements(toSend);
            return toSend;
        }

        /// <summary>
        /// Keeps acknowledgements for a later Publish request.
        /// </summary>
        private void DeferAcknowledgements(List<PendingAcknowledgement> acknowledgements)
        {
            lock (m_acknowledgementPolicyLock)
            {
                m_deferredAcknowledgements.AddRange(acknowledgements);
                UpdateDeferredWarning();
            }
        }

        /// <summary>
        /// Ends the presented acknowledgements the legacy deferral handler neither sent nor
        /// retained. Called under the policy lock.
        /// </summary>
        /// <param name="candidates">The presented acknowledgements.</param>
        /// <param name="sent">The acknowledgements the handler sends.</param>
        /// <param name="deferredStart">The first deferred entry the handler retained.</param>
        private void ReportDroppedAcknowledgements(
            List<PendingAcknowledgement> candidates,
            List<PendingAcknowledgement> sent,
            int deferredStart)
        {
            var kept = new HashSet<AcknowledgementHandle>();
            foreach (PendingAcknowledgement pending in sent)
            {
                kept.Add(pending.Handle);
            }
            for (int i = deferredStart; i < m_deferredAcknowledgements.Count; i++)
            {
                kept.Add(m_deferredAcknowledgements[i].Handle);
            }
            List<AcknowledgementOutcome>? dropped = null;
            foreach (PendingAcknowledgement candidate in candidates)
            {
                if (!kept.Contains(candidate.Handle))
                {
                    AddOutcome(ref dropped, candidate, AcknowledgementDisposition.Abandoned,
                        StatusCodes.BadRequestCancelledByClient);
                }
            }
            ReportOutcomes(dropped);
        }

        /// <summary>
        /// Reports how the acknowledgements of a successful Publish request ended.
        /// </summary>
        /// <param name="sent">The acknowledgements of the request, in request order.</param>
        /// <param name="results">The server's result per acknowledgement.</param>
        private void ReportAcknowledgementResults(
            List<PendingAcknowledgement> sent,
            ArrayOf<StatusCode> results)
        {
            if (sent.Count == 0 || !RequiresAcknowledgementTracking)
            {
                return;
            }
            var outcomes = new List<AcknowledgementOutcome>(sent.Count);
            for (int i = 0; i < sent.Count; i++)
            {
                StatusCode status = i < results.Count ? results[i] : StatusCodes.BadUnexpectedError;
                outcomes.Add(new AcknowledgementOutcome(
                    sent[i].Handle,
                    StatusCode.IsGood(status)
                        ? AcknowledgementDisposition.Acknowledged
                        : AcknowledgementDisposition.NotRetained,
                    status));
            }
            ReportOutcomes(outcomes);
        }

        /// <summary>
        /// Adds an outcome to a lazily created list.
        /// </summary>
        private static void AddOutcome(
            ref List<AcknowledgementOutcome>? outcomes,
            in PendingAcknowledgement pending,
            AcknowledgementDisposition disposition,
            StatusCode status)
        {
            (outcomes ??= []).Add(new AcknowledgementOutcome(pending.Handle, disposition, status));
        }

        /// <summary>
        /// Hands outcomes to the observer, outside any lock and without blocking a worker.
        /// Only the first outcome of an outstanding acknowledgement is reported.
        /// </summary>
        private void ReportOutcomes(List<AcknowledgementOutcome>? outcomes)
        {
            if (outcomes == null || outcomes.Count == 0)
            {
                return;
            }
            lock (m_subscriptionLock)
            {
                outcomes.RemoveAll(outcome => !m_outstandingAcknowledgements.TryRemove(outcome.Handle, out _));
                if (m_session.AcknowledgementProvider is IAcknowledgementTracker tracker)
                {
                    foreach (AcknowledgementOutcome outcome in outcomes)
                    {
                        tracker.Complete(outcome.Handle);
                    }
                }
            }
            m_outcomeDispatcher.Report(m_session.AcknowledgementObserver, outcomes);
        }

        /// <summary>
        /// Completes a queue write. An acknowledgement that could not be queued ends as
        /// abandoned, exactly once.
        /// </summary>
        private async ValueTask AwaitQueuedAsync(ValueTask write, PendingAcknowledgement pending)
        {
            try
            {
                await write.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && m_acks.Writer.TryWrite(pending))
                {
                    // Cancellation of a queue attempt does not end a live message. Keep
                    // the original lifetime queued so transfer/retry cannot revive it later.
                    throw;
                }
                List<AcknowledgementOutcome>? abandoned = null;
                AddOutcome(ref abandoned, pending, AcknowledgementDisposition.Abandoned,
                    ex is OperationCanceledException
                        ? StatusCodes.BadRequestCancelledByClient
                        : StatusCodes.BadShutdown);
                ReportOutcomes(abandoned);
                throw;
            }
        }

        /// <summary>
        /// Delivers the outcomes that are still pending and stops the delivery loop.
        /// </summary>
        private ValueTask CompleteOutcomeDeliveryAsync()
        {
            return m_outcomeDispatcher.CompleteAsync();
        }

        /// <summary>
        /// Whether the policy returned exactly the presented acknowledgements in order.
        /// </summary>
        private static bool IsUnchanged(
            List<SubscriptionAcknowledgement> toSend,
            List<PendingAcknowledgement> candidates)
        {
            if (toSend.Count != candidates.Count)
            {
                return false;
            }
            for (int i = 0; i < toSend.Count; i++)
            {
                if (!ReferenceEquals(toSend[i], candidates[i].Acknowledgement))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Puts acknowledgements of an abandoned or failed Publish attempt back into
        /// the queue, except those that must not be sent anymore.
        /// </summary>
        private void RequeueAcknowledgements(List<PendingAcknowledgement> acknowledgements)
        {
            if (acknowledgements.Count == 0)
            {
                return;
            }
            RemoveStaleAcknowledgements(acknowledgements);
            List<AcknowledgementOutcome>? abandoned = null;
            foreach (PendingAcknowledgement pending in acknowledgements)
            {
                if (!m_acks.Writer.TryWrite(pending))
                {
                    AddOutcome(ref abandoned, pending, AcknowledgementDisposition.Abandoned,
                        StatusCodes.BadShutdown);
                }
            }
            ReportOutcomes(abandoned);
        }

        /// <summary>
        /// Removes acknowledgements that must not be sent anymore. Each entry is
        /// validated in constant time under a single registry lock acquisition.
        /// </summary>
        private void RemoveStaleAcknowledgements(List<PendingAcknowledgement> acknowledgements)
        {
            int kept = 0;
            bool observed = RequiresAcknowledgementTracking;
            List<AcknowledgementOutcome>? outcomes = null;
            lock (m_subscriptionLock)
            {
                for (int i = 0; i < acknowledgements.Count; i++)
                {
                    PendingAcknowledgement pending = acknowledgements[i];
                    AcknowledgementState state = ValidateLocked(pending);
                    if (state == AcknowledgementState.Valid)
                    {
                        acknowledgements[kept++] = pending;
                    }
                    else if (observed)
                    {
                        if (state == AcknowledgementState.NotHeld)
                        {
                            AddOutcome(ref outcomes, pending, AcknowledgementDisposition.NotRetained,
                                StatusCodes.BadSequenceNumberUnknown);
                        }
                        else
                        {
                            AddOutcome(ref outcomes, pending, AcknowledgementDisposition.OwnerRetired,
                                StatusCodes.BadSubscriptionIdInvalid);
                        }
                    }
                }
            }
            int removed = acknowledgements.Count - kept;
            if (removed != 0)
            {
                acknowledgements.RemoveRange(kept, removed);
                m_logger.StaleAcknowledgementsDiscarded(removed);
            }
            ReportOutcomes(outcomes);
        }

        /// <summary>
        /// Classifies an owned acknowledgement. It is <see cref="AcknowledgementState.Stale"/>
        /// when its owner was removed, recreated or deleted, changed its server id, or a more
        /// recently created subscription holds the id (the server reused it): sending it could
        /// confirm a message of another subscription the client never processed. It is
        /// <see cref="AcknowledgementState.NotHeld"/> when the server reported that it no longer
        /// holds the message, or that it has no retransmission queue (Part 4, 5.14.5.2: the
        /// client should then not acknowledge).
        /// </summary>
        private AcknowledgementState ValidateLocked(in PendingAcknowledgement pending)
        {
            IMessageProcessor owner = pending.Owner;
            SubscriptionAcknowledgement acknowledgement = pending.Acknowledgement;
            uint subscriptionId = acknowledgement.SubscriptionId;
            if (owner.Id != subscriptionId ||
                owner.MessageGeneration != pending.Generation ||
                !IsRegisteredLocked(owner) ||
                m_superseded.Contains(owner))
            {
                return AcknowledgementState.Stale;
            }
            if (m_claims.TryGetValue(subscriptionId, out SubscriptionClaim claim) &&
                !ReferenceEquals(claim.Owner, owner) &&
                IsLiveOwnerLocked(claim.Owner, subscriptionId))
            {
                return AcknowledgementState.Stale;
            }
            if (m_windows.TryGetValue(owner, out AvailabilityWindow? window) &&
                window.MessageGeneration == pending.Generation)
            {
                if (window.RetransmissionQueueUnsupported)
                {
                    return AcknowledgementState.NotHeld;
                }
                uint sequenceNumber = acknowledgement.SequenceNumber;
                if (IsOlder(sequenceNumber, window.Horizon) && !window.Contains(sequenceNumber))
                {
                    return AcknowledgementState.NotHeld;
                }
            }
            return AcknowledgementState.Valid;
        }

        /// <summary>
        /// Whether a subscription lifetime no longer owns its acknowledgements: its owner was
        /// removed, recreated or deleted, changed its server id, or was superseded by a more
        /// recently created owner of the id.
        /// </summary>
        private bool IsRetiredScopeLocked(AcknowledgementScope scope)
        {
            if (scope.Owner is not IMessageProcessor owner)
            {
                return true;
            }
            uint subscriptionId = scope.SubscriptionId;
            if (owner.Id != subscriptionId ||
                owner.MessageGeneration != scope.Generation ||
                !IsRegisteredLocked(owner) ||
                m_superseded.Contains(owner))
            {
                return true;
            }
            return m_claims.TryGetValue(subscriptionId, out SubscriptionClaim claim) &&
                !ReferenceEquals(claim.Owner, owner) &&
                IsLiveOwnerLocked(claim.Owner, subscriptionId);
        }

        /// <summary>
        /// Whether a Publish response was produced for the subscription that held the
        /// owner's id before the server reused it. Only a response to a request sent before
        /// the id was claimed can be one, and its message was published before the owner was
        /// created; both times come from the server's clock. A message that is wrongly
        /// discarded stays in the server's retransmission queue and is republished once the
        /// owner receives its next message.
        /// </summary>
        /// <param name="owner">The subscription the response resolves to.</param>
        /// <param name="message">The response's notification message.</param>
        /// <param name="claimMark">The claim sequence when the request was sent.</param>
        private bool IsMessageOfPreviousSubscription(
            IManagedSubscription owner,
            NotificationMessage message,
            long claimMark)
        {
            if (message.PublishTime.IsNull)
            {
                return false;
            }
            lock (m_subscriptionLock)
            {
                return m_claims.TryGetValue(owner.Id, out SubscriptionClaim claim) &&
                    ReferenceEquals(claim.Owner, owner) &&
                    claim.Sequence > claimMark &&
                    !claim.CreatedAt.IsNull &&
                    message.PublishTime < claim.CreatedAt;
            }
        }

        /// <summary>
        /// Records the retransmission queue a Publish response reported for its subscription.
        /// The list is prepared after the request's acknowledgements were processed, and a
        /// message older than the response that is not listed is no longer held by the server.
        /// </summary>
        /// <param name="owner">The subscription the response was dispatched to.</param>
        /// <param name="message">The response's notification message.</param>
        /// <param name="availableSequenceNumbers">The response's available sequence numbers.</param>
        /// <param name="claimMark">The claim sequence when the request was sent. A response
        /// whose id was claimed afterwards may come from the id's previous server
        /// subscription and must not describe the current owner's queue.</param>
        private void RecordAvailability(
            IManagedSubscription owner,
            NotificationMessage message,
            ArrayOf<uint> availableSequenceNumbers,
            long claimMark)
        {
            // A keep-alive carries the sequence number of the next message, a data message its own.
            uint horizon = message.SequenceNumber;
            if (horizon == 0)
            {
                return;
            }
            bool data = message.NotificationData.Count != 0;
            long generation = owner.MessageGeneration;
            lock (m_subscriptionLock)
            {
                if (!IsRegisteredLocked(owner) ||
                    (m_claims.TryGetValue(owner.Id, out SubscriptionClaim claim) && claim.Sequence > claimMark))
                {
                    return;
                }
                // A data response must list its own message unless the server has no queue.
                bool unsupported = data && availableSequenceNumbers.Count == 0;
                if (m_windows.TryGetValue(owner, out AvailabilityWindow? current) &&
                    current.MessageGeneration == generation)
                {
                    if (IsOlder(horizon, current.Horizon))
                    {
                        // Responses can be processed out of order; keep the newer view.
                        return;
                    }
                    if (!data)
                    {
                        unsupported = current.RetransmissionQueueUnsupported;
                    }
                }
                if (current == null)
                {
                    current = new AvailabilityWindow();
                    m_windows[owner] = current;
                }
                current.Update(generation, horizon, availableSequenceNumbers, unsupported);
            }
        }

        /// <summary>
        /// Forgets the retransmission view of a retiring owner, and its claim on
        /// <paramref name="subscriptionId"/> unless a live subscription created later holds it.
        /// Requires the registry lock.
        /// </summary>
        private void ForgetOwnerLocked(IMessageProcessor owner, uint subscriptionId)
        {
            m_windows.Remove(owner);
            m_superseded.Remove(owner);
            if (subscriptionId != 0 &&
                m_claims.TryGetValue(subscriptionId, out SubscriptionClaim claim) &&
                (ReferenceEquals(claim.Owner, owner) || !IsLiveOwnerLocked(claim.Owner, subscriptionId)))
            {
                m_claims.Remove(subscriptionId);
            }
        }

        /// <summary>
        /// Whether the processor is still registered. Requires the registry lock.
        /// </summary>
        private bool IsRegisteredLocked(IMessageProcessor owner)
        {
            return owner is IManagedSubscription subscription && m_subscriptions.Contains(subscription);
        }

        /// <summary>
        /// Whether the processor is registered and still uses the id. Requires the registry lock.
        /// </summary>
        private bool IsLiveOwnerLocked(IMessageProcessor owner, uint subscriptionId)
        {
            return owner.Id == subscriptionId && IsRegisteredLocked(owner);
        }

        /// <summary>
        /// Whether a sequence number precedes another, allowing for wrap-around.
        /// </summary>
        private static bool IsOlder(uint sequenceNumber, uint reference)
        {
            return unchecked((int)(reference - sequenceNumber)) > 0;
        }

        /// <summary>
        /// Warns once when deferred acknowledgements accumulate, and re-arms when the
        /// backlog has drained. Must be called with the policy lock held.
        /// </summary>
        private void UpdateDeferredWarning()
        {
            int count = m_deferredAcknowledgements.Count;
            if (!m_deferredWarningRaised && count >= kDeferredAcknowledgementWarningThreshold)
            {
                m_deferredWarningRaised = true;
                m_logger.DeferredAcknowledgementsAccumulating(count);
            }
            else if (m_deferredWarningRaised && count < kDeferredAcknowledgementWarningThreshold / 2)
            {
                m_deferredWarningRaised = false;
            }
        }

        /// <summary>
        /// Logs acknowledgements the server rejected. <c>Bad_SequenceNumberUnknown</c> is
        /// expected after a retried request; an invalid subscription id means the
        /// subscription no longer exists on the server.
        /// </summary>
        private void LogAcknowledgementResults(ArrayOf<StatusCode> results)
        {
            int unknown = 0;
            int invalid = 0;
            int other = 0;
            for (int i = 0; i < results.Count; i++)
            {
                StatusCode code = results[i];
                if (StatusCode.IsGood(code))
                {
                    continue;
                }
                if (code == StatusCodes.BadSequenceNumberUnknown)
                {
                    unknown++;
                }
                else if (code == StatusCodes.BadSubscriptionIdInvalid)
                {
                    invalid++;
                }
                else
                {
                    other++;
                }
            }
            if (unknown != 0 || invalid != 0 || other != 0)
            {
                // Unknown sequence numbers alone are expected after a retried request.
                m_logger.AcknowledgementsRejected(
                    invalid != 0 || other != 0 ? LogLevel.Information : LogLevel.Debug,
                    results.Count, unknown, invalid, other);
            }
        }

        /// <summary>
        /// A queued acknowledgement stamped with the processor that produced it and
        /// that processor's message generation at the time.
        /// </summary>
        /// <param name="Acknowledgement">The acknowledgement to send.</param>
        /// <param name="Scope">The owner's acknowledgement scope of that generation.</param>
        private readonly record struct PendingAcknowledgement(
            SubscriptionAcknowledgement Acknowledgement,
            AcknowledgementScope Scope)
        {
            /// <summary>
            /// Gets the producing processor captured by the scope.
            /// </summary>
            public IMessageProcessor Owner => (IMessageProcessor)Scope.Owner;

            /// <summary>
            /// Gets the message generation captured by the scope.
            /// </summary>
            public long Generation => Scope.Generation;

            /// <summary>
            /// Gets the handle applications use for this acknowledgement.
            /// </summary>
            public AcknowledgementHandle Handle => new(Scope, Acknowledgement.SequenceNumber);
        }

        /// <summary>
        /// Orders queued acknowledgements by sequence number, so that older messages are
        /// acknowledged first. <see cref="PendingAcknowledgement"/> does not implement
        /// <see cref="IComparable{T}"/>: this order ignores the subscription and the owner, so
        /// it is not consistent with the record's equality.
        /// </summary>
        private sealed class SequenceNumberComparer : IComparer<PendingAcknowledgement>
        {
            /// <summary>
            /// The shared instance.
            /// </summary>
            public static SequenceNumberComparer Instance { get; } = new();

            /// <inheritdoc/>
            public int Compare(PendingAcknowledgement x, PendingAcknowledgement y)
            {
                return x.Acknowledgement.SequenceNumber.CompareTo(y.Acknowledgement.SequenceNumber);
            }
        }

        /// <summary>
        /// Whether an owned acknowledgement may be sent.
        /// </summary>
        private enum AcknowledgementState
        {
            /// <summary>It may be sent.</summary>
            Valid,

            /// <summary>Its owner retired it, or another subscription now owns its id.</summary>
            Stale,

            /// <summary>The server no longer holds the message, but the owner is alive.</summary>
            NotHeld
        }

        /// <summary>
        /// The most recently created owner of a server id.
        /// </summary>
        /// <param name="Owner">The owner.</param>
        /// <param name="Sequence">Orders claims, so a response to a request sent before the
        /// claim can be told apart from one the current owner's server subscription sent.</param>
        /// <param name="CreatedAt">The server time when the owner was created, from the
        /// CreateSubscription response header, or null if unknown.</param>
        private readonly record struct SubscriptionClaim(
            IMessageProcessor Owner,
            long Sequence,
            DateTimeUtc CreatedAt);

        /// <summary>
        /// The retransmission queue the server last reported for an owner. Reused across
        /// responses and guarded by the manager's registry lock, so recording a response does
        /// not allocate.
        /// </summary>
        private sealed class AvailabilityWindow
        {
            /// <summary>
            /// The owner's message generation when recorded.
            /// </summary>
            public long MessageGeneration { get; private set; }

            /// <summary>
            /// Messages older than this are only held if listed.
            /// </summary>
            public uint Horizon { get; private set; }

            /// <summary>
            /// Whether a data response listed nothing, i.e. the server has no retransmission queue.
            /// </summary>
            public bool RetransmissionQueueUnsupported { get; private set; }

            /// <summary>
            /// Replaces the recorded view.
            /// </summary>
            public void Update(long messageGeneration, uint horizon, ArrayOf<uint> available, bool unsupported)
            {
                MessageGeneration = messageGeneration;
                Horizon = horizon;
                RetransmissionQueueUnsupported = unsupported;
                m_available = available;
                if (available.Count > kLinearSearchLimit)
                {
                    m_lookup ??= [];
                    m_lookup.Clear();
                    foreach (uint sequenceNumber in available)
                    {
                        m_lookup.Add(sequenceNumber);
                    }
                }
            }

            /// <summary>
            /// Whether the server listed the sequence number as available.
            /// </summary>
            public bool Contains(uint sequenceNumber)
            {
                if (m_available.Count > kLinearSearchLimit)
                {
                    return m_lookup!.Contains(sequenceNumber);
                }
                ReadOnlySpan<uint> available = m_available.Span;
                for (int i = 0; i < available.Length; i++)
                {
                    if (available[i] == sequenceNumber)
                    {
                        return true;
                    }
                }
                return false;
            }

            private const int kLinearSearchLimit = 16;
            private ArrayOf<uint> m_available;
            private HashSet<uint>? m_lookup;
        }

        /// <summary>
        /// Compares instances by object identity.
        /// </summary>
        /// <typeparam name="T">The compared reference type.</typeparam>
        private sealed class IdentityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            /// <summary>
            /// The shared instance.
            /// </summary>
            public static IdentityComparer<T> Instance { get; } = new();

            /// <inheritdoc/>
            public bool Equals(T? x, T? y)
            {
                return ReferenceEquals(x, y);
            }

            /// <inheritdoc/>
            public int GetHashCode(T obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        /// <summary>
        /// Worker object that manages the publish worker tasks inside
        /// the controller.
        /// </summary>
        private sealed class PublishWorker : IAsyncDisposable
        {
            /// <summary>
            /// Worker id
            /// </summary>
            public int Index { get; }

            /// <summary>
            /// Task to wait until worker exits
            /// </summary>
            public Task Task { get; }

            /// <summary>
            /// Signal too many publish requests running
            /// </summary>
            public bool TooManyPublishRequests { get; set; }

            /// <summary>
            /// Create worker
            /// </summary>
            /// <param name="outer"></param>
            /// <param name="index"></param>
            public PublishWorker(SubscriptionManager outer, int index)
            {
                Index = index;
                m_outer = outer;
                m_cts = CancellationTokenSource.CreateLinkedTokenSource(outer.m_cts.Token);
                m_logger = m_outer.m_loggerFactory.CreateLogger<PublishWorker>();
                //
                // Start on the thread pool. Workers are constructed inline by
                // the publish controller, and the worker loop only yields
                // where it awaits something that is not already completed. A
                // transport whose publish completes synchronously would
                // therefore run the entire worker loop on the controller's
                // stack, starving the control loop - it could neither resize
                // the pool nor publish PublishWorkerCount.
                //
                CancellationToken token = m_cts.Token;
                Task = Task.Run(() => PublishWorkerAsync(token), token);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await m_cts.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        //
                        // Await unconditionally. The controller reaps a
                        // worker whose task has already completed, and that
                        // completion is usually a fault or a cancellation -
                        // skipping the await for completed tasks would leave
                        // those exceptions unobserved.
                        //
                        await Task.ConfigureAwait(false);
                    }
                    catch
                    {
                    } // Ignore
                }
                finally
                {
                    m_cts.Dispose();
                    GC.SuppressFinalize(this);
                }
            }

            /// <summary>
            /// Represents a continously running publish forwarder that forwards
            /// publish responses to the subscriptions contained in this session.
            /// The publish worker tasks have a controller that reduces or
            /// increases the number of workers as new subscriptions are added
            /// or subscriptions are removed. Once the message has been delivered
            /// to the subscription, the subscription will queue acknowledges
            /// to the worker which it will send.
            /// </summary>
            /// <param name="ct"></param>
            private async Task PublishWorkerAsync(CancellationToken ct)
            {
                long publishLatencyStart = m_outer.m_timeProvider.GetTimestamp();
                TimeSpan publishLatency = TimeSpan.Zero;
                bool publishLatencyRunning = false;
                uint timeoutHint = 0u;
                bool moreNotifications = true; // Dont wait first time we enter the loop.
                m_logger.PublishWorkerStarted(Index);
                while (!ct.IsCancellationRequested)
                {
                    if (!m_outer.m_running.IsSet)
                    {
                        m_logger.PublishWorkerPaused(Index);
                        try
                        {
                            await m_outer.m_running.WaitAsync(ct).ConfigureAwait(false);
                            m_logger.PublishWorkerResumed(Index);
                        }
                        catch (OperationCanceledException)
                        {
                            continue;
                        }
                        catch (Exception ex)
                        {
                            m_logger.PublishWorkerUnexpectedExceptionConnecting(ex, Index);
                            break;
                        }
                    }
                    long currentLatencyMs = publishLatencyRunning
                        ? (long)m_outer.m_timeProvider.GetElapsedTime(publishLatencyStart).TotalMilliseconds
                        : (long)publishLatency.TotalMilliseconds;
                    int ackWaitTimeout = CalculateTimeouts(
                        currentLatencyMs, ref timeoutHint);
                    if (!m_outer.TryBeginPublishRequest())
                    {
                        continue;
                    }
                    List<PendingAcknowledgement> batch = [];
                    ArrayOf<SubscriptionAcknowledgement> acks = [];
                    uint handle = 0;
                    bool publishActive = true;
                    bool publishPending = false;
                    long epoch = 0;
                    long requestSequence = 0;
                    NodeId publishSessionId = NodeId.Null;
                    // A quiesce can abort this attempt without stopping the worker.
                    var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    CancellationToken attemptToken = attempt.Token;
                    m_outer.RegisterPublishAttempt(attempt);
                    try
                    {
                        batch = GetAcksReadyToSend();
                        handle = ClientBase.NewSharedRequestHandle();
                        if (batch.Count == 0 && !moreNotifications && ackWaitTimeout != 0)
                        {
                            // Throttle publishing as we wait for acks to arrive
                            batch = await WaitForAcksAsync(ackWaitTimeout, attemptToken).ConfigureAwait(false);
                        }
                        if (!m_outer.m_running.IsSet)
                        {
                            m_outer.RequeueAcknowledgements(batch);
                            continue;
                        }
                        batch = await m_outer.PrepareAcknowledgementsToSendAsync(batch, attemptToken)
                            .ConfigureAwait(false);
                        if (batch.Count != 0)
                        {
                            var wireAcknowledgements = new SubscriptionAcknowledgement[batch.Count];
                            for (int i = 0; i < wireAcknowledgements.Length; i++)
                            {
                                wireAcknowledgements[i] = batch[i].Acknowledgement;
                            }
                            acks = wireAcknowledgements;
                        }
                        long claimMark = Interlocked.Read(ref m_outer.m_claimSequence);
                        publishLatencyStart = m_outer.m_timeProvider.GetTimestamp();
                        publishLatencyRunning = true;
                        publishSessionId = m_outer.m_session.SessionId;
                        if (!m_outer.TryCapturePublishDispatch(out epoch, out requestSequence))
                        {
                            m_outer.RequeueAcknowledgements(batch);
                            continue;
                        }
                        publishPending = true;
                        PublishResponse response = await m_outer.m_session.PublishAsync(
                            new RequestHeader
                            {
                                TimeoutHint = timeoutHint,
                                ReturnDiagnostics = (uint)(int)m_outer.ReturnDiagnostics,
                                RequestHandle = handle
                            }, acks, attemptToken).ConfigureAwait(false);
                        publishPending = false;

                        moreNotifications = response.MoreNotifications;
                        uint subscriptionId = response.SubscriptionId;
                        NotificationMessage notificationMessage = response.NotificationMessage;
                        ArrayOf<uint> availableSequenceNumbers =
                            response.AvailableSequenceNumbers;

                        ArrayOf<StatusCode> acknowledgeResults = response.Results;
                        ArrayOf<DiagnosticInfo> acknowledgeDiagnosticInfos =
                            response.DiagnosticInfos;
                        ClientBase.ValidateResponse(acknowledgeResults, acks);
                        ClientBase.ValidateDiagnosticInfos(acknowledgeDiagnosticInfos, acks);
                        m_outer.LogAcknowledgementResults(acknowledgeResults);
                        m_outer.ReportAcknowledgementResults(batch, acknowledgeResults);
                        m_outer.RecordPublishSuccess(epoch, requestSequence);

                        // A publish completed, so the channel is healthy:
                        // clear the consecutive-error backoff state.
                        m_consecutivePublishErrors = 0;
                        m_lastLoggedErrorStatus = default;

                        //
                        // Snapshot the pending-creation state BEFORE the id
                        // lookup. A creation assigns its id before it clears
                        // its in-progress flag, so a creation that is no
                        // longer pending at the snapshot has its id visible
                        // to the lookup, and one that completes after the
                        // snapshot is still covered by the snapshot. The
                        // orphan delete below is decided on this snapshot
                        // only; a later, separate check could observe the
                        // creation completed after a lookup that missed it.
                        //
                        bool creationPending = m_outer.HasSubscriptionsPendingCreation();

                        // Get the subscription with the provided identifier
                        IManagedSubscription? subscription = m_outer.GetById(subscriptionId);
                        publishLatency = m_outer.m_timeProvider.GetElapsedTime(
                            publishLatencyStart);
                        publishLatencyRunning = false;
                        if (subscription != null &&
                            m_outer.IsMessageOfPreviousSubscription(subscription, notificationMessage, claimMark))
                        {
                            //
                            // The server reused the id and produced this message for the
                            // subscription that held it before. Delivering it would move the
                            // new owner's sequence cursor past its own first messages. It is
                            // not acknowledged either: that subscription no longer exists.
                            // The id still belongs to the new owner, so it is not deleted.
                            //
                            m_outer.EndPublishRequest();
                            publishActive = false;
                            m_logger.PublishWorkerDiscardedMessageOfPreviousSubscription(
                                Index, handle, subscriptionId, notificationMessage.SequenceNumber);
                        }
                        else if (subscription != null)
                        {
                            m_outer.RecordAvailability(
                                subscription, notificationMessage, availableSequenceNumbers, claimMark);

                            // Capture the delivery generation before draining can retire it.
                            ValueTask delivery = subscription.OnPublishReceivedAsync(
                                notificationMessage,
                                availableSequenceNumbers.ToList(),
                                response.ResponseHeader.StringTable.ToList());
                            m_outer.EndPublishRequest();
                            publishActive = false;
                            // A callback may await the service reader while recreation owns
                            // the writer. Ingress backpressure is not an in-flight request.
                            await delivery.ConfigureAwait(false);
                            Interlocked.Increment(ref m_outer.m_goodPublishRequestCount);
                            m_lastUnknownSubscriptionId = 0;
                            m_consecutiveUnresolvedResponses = 0;
                        }
                        else if (!ct.IsCancellationRequested)
                        {
                            //
                            // The Publish itself succeeded, so the server has
                            // processed the acknowledgements it carried. End
                            // the in-flight request now: a failure below must
                            // not re-queue those acknowledgements (the server
                            // would answer Bad_SequenceNumberUnknown), and a
                            // drain must not wait for the throttle or the
                            // orphan delete below.
                            //
                            m_outer.EndPublishRequest();
                            publishActive = false;

                            //
                            // The identifier does not resolve to any subscription
                            // this manager owns. Delete the orphan on the server,
                            // but only when no subscription is currently awaiting
                            // its identifier: a publish response can overtake the
                            // CreateSubscription continuation that assigns the id,
                            // and deleting in that window would silently kill a
                            // healthy subscription. Leaving a genuine orphan alive
                            // until its lifetime expires is the safe trade-off.
                            //
                            // None of the branches below made progress, so the
                            // server can answer the next publish request with the
                            // very same undeliverable response. Publishing again
                            // without waiting would busy-spin against the server
                            // and flood the log, and a transport that completes
                            // synchronously would never release the worker's
                            // thread pool thread. Therefore always throttle here.
                            //
                            if (m_outer.m_subscriptionHistory.Contains(subscriptionId))
                            {
                                // ignore messages with a subscription that was deleted
                                // Do not delete publish requests of stale subscriptions
                                moreNotifications = false;
                                await DelayUnresolvedSubscriptionAsync(ct)
                                    .ConfigureAwait(false);
                            }
                            else if (creationPending)
                            {
                                // Log only the first response of a run for the
                                // same identifier so a long lived creation window
                                // cannot flood the log.
                                if (m_lastUnknownSubscriptionId != subscriptionId)
                                {
                                    m_lastUnknownSubscriptionId = subscriptionId;
                                    m_logger.PublishWorkerDeferredUnknownSubscription(
                                        Index, handle, subscriptionId);
                                }
                                moreNotifications = false;
                                await DelayUnresolvedSubscriptionAsync(ct)
                                    .ConfigureAwait(false);
                            }
                            else if (m_outer.m_session.TryDispatchToSessionSubscription(
                                subscriptionId,
                                notificationMessage,
                                availableSequenceNumbers,
                                response.ResponseHeader.StringTable,
                                moreNotifications))
                            {
                                // The session holds this subscription through the classic
                                // API. It is live and owned by the application, so the
                                // notification belongs to it: deliver rather than drop it,
                                // and never delete it as abandoned.
                                Interlocked.Increment(ref m_outer.m_goodPublishRequestCount);
                                m_lastUnknownSubscriptionId = 0;
                                m_consecutiveUnresolvedResponses = 0;
                            }
                            else
                            {
                                m_logger.PublishWorkerReceivedUnknownSubscription(
                                    Index, handle, subscriptionId);
                                Interlocked.Increment(ref m_outer.m_badPublishRequestCount);
                                await m_outer.m_session.DeleteSubscriptionsAsync(
                                    null,
                                    [subscriptionId],
                                    attemptToken).ConfigureAwait(false);
                                //
                                // Retire the identifier so responses that were
                                // already in flight, or that the server emits
                                // before the delete takes effect, are ignored
                                // instead of triggering another delete. Without
                                // this the worker issues DeleteSubscriptions for
                                // the same orphan on every publish response.
                                //
                                m_outer.RetireSubscriptionId(subscriptionId);
                                m_lastUnknownSubscriptionId = 0;
                                m_consecutiveUnresolvedResponses = 0;
                                moreNotifications = true;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Put the collected acknowledgements back: another
                        // worker (or this one after a reconnect) still has to
                        // send them, otherwise the server retransmits those
                        // messages until its retransmission queue overflows.
                        if (publishActive)
                        {
                            m_outer.RequeueAcknowledgements(batch);
                        }
                        if (!ct.IsCancellationRequested)
                        {
                            // A quiesce aborted this attempt, not the worker. Release
                            // the request so the drain completes, then return to the
                            // paused park and resume once the caller finishes.
                            continue;
                        }
                        break;
                    }
                    catch (Exception e)
                    {
                        // raise an error event.
                        publishLatency = m_outer.m_timeProvider.GetElapsedTime(publishLatencyStart);
                        publishLatencyRunning = false;
                        var error = new ServiceResult(e);

                        if (error.Code == StatusCodes.BadRequestInterrupted &&
                            ct.IsCancellationRequested)
                        {
                            if (publishActive)
                            {
                                m_outer.RequeueAcknowledgements(batch);
                            }
                            break;
                        }

                        Interlocked.Increment(ref m_outer.m_badPublishRequestCount);
                        bool requestFailed = publishActive && publishPending;
                        if (publishActive)
                        {
                            // Only a failed service request needs acknowledgement rollback.
                            m_outer.RequeueAcknowledgements(batch);
                            m_outer.EndPublishRequest();
                            publishActive = false;
                        }

                        // ignore errors if paused.
                        if (!m_outer.m_running.IsSet)
                        {
                            m_logger.PublishAbandonedAfterError(Index, handle, e.Message);
                            continue;
                        }

                        // don't send another publish for these errors,
                        // or throttle to avoid server overload.
                        StatusCode statusCode = error.StatusCode;
                        if (statusCode == StatusCodes.BadTooManyPublishRequests)
                        {
                            TooManyPublishRequests = true;
                            m_outer.m_publishControl.Set();
                        }
                        else if (statusCode == StatusCodes.BadNoSubscription ||
                            statusCode == StatusCodes.BadSessionClosed ||
                            statusCode == StatusCodes.BadSecurityChecksFailed ||
                            statusCode == StatusCodes.BadCertificateInvalid ||
                            statusCode == StatusCodes.BadServerHalted)
                        {
                            // ignore
                        }
                        // Transport is down or the session/channel is no longer
                        // valid. Notify the session-level reconnect/failover owner
                        // and throttle so the worker does not spin issuing
                        // Publish requests that fail instantly (for example
                        // BadNotConnected while the channel is being rebuilt).
                        else if (statusCode == StatusCodes.BadSessionIdInvalid ||
                            statusCode == StatusCodes.BadSecureChannelIdInvalid ||
                            statusCode == StatusCodes.BadSecureChannelClosed ||
                            statusCode == StatusCodes.BadNotConnected ||
                            statusCode == StatusCodes.BadConnectionClosed)
                        {
                            moreNotifications = false; // throttle
                            if (requestFailed && !attemptToken.IsCancellationRequested)
                            {
                                await m_outer.ReportPublishRecoveryErrorAsync(
                                    epoch, requestSequence, publishSessionId, error, attemptToken)
                                    .ConfigureAwait(false);
                            }
                            // Log the transport-down transition once per status
                            // rather than a full stack trace every iteration.
                            if (m_lastLoggedErrorStatus != statusCode)
                            {
                                m_lastLoggedErrorStatus = statusCode;
                                m_logger.TransportUnavailable(Index, handle, statusCode);
                            }
                        }
                        // Servers may return this error when overloaded
                        else if (statusCode == StatusCodes.BadTooManyOperations ||
                            statusCode == StatusCodes.BadTcpServerTooBusy ||
                            statusCode == StatusCodes.BadServerTooBusy)
                        {
                            // throttle the next publish to reduce server load
                            m_logger.ServerBusyThrottling(Index, handle);
                            moreNotifications = false; // throttle
                        }
                        else if (statusCode == StatusCodes.BadTimeout ||
                            statusCode == StatusCodes.BadRequestTimeout)
                        {
                            // Timed out - retry with larger timeout
                            timeoutHint += 1000; // Increase by seconds
                            m_logger.TimedOutIncreasingTimeout(Index, handle, timeoutHint);
                            moreNotifications = true;
                        }
                        else if (m_lastLoggedErrorStatus != statusCode)
                        {
                            // Log unexpected errors, but only emit the full
                            // exception (stack trace) once per distinct status so
                            // a recurring error cannot flood the logs.
                            m_lastLoggedErrorStatus = statusCode;
                            m_logger.UnhandledErrorDuringPublish(e, Index, handle, error.StatusCode);
                        }
                        else
                        {
                            m_logger.UnhandledErrorDuringPublishRepeated(Index, handle, error.StatusCode);
                        }

                        // Always apply a bounded, exponential backoff after a failed
                        // publish so that a persistently failing channel (for
                        // example one stuck in BadNotConnected) can never busy-spin
                        // or flood the logs while reconnect runs in the background.
                        // The shift is capped before the multiply to avoid overflow;
                        // s_maxPublishBackoff bounds the final delay.
                        int errorCount = ++m_consecutivePublishErrors;
                        int backoffMs = Math.Min(
                            kBasePublishBackoffMs << Math.Min(errorCount - 1, 5),
                            (int)s_maxPublishBackoff.TotalMilliseconds);
                        try
                        {
                            await m_outer.m_timeProvider
                                .Delay(TimeSpan.FromMilliseconds(backoffMs), ct)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        // Ordering matters: the acknowledgement rollback above has
                        // already run, and EndPublishRequest is what releases the
                        // drain. Releasing the count first would let a quiesced
                        // caller observe a zero count while acknowledgements from
                        // this attempt were still unwinding, breaking the guarantee
                        // DropPendingForSubscription relies on.
                        if (publishActive)
                        {
                            m_outer.EndPublishRequest();
                        }
                        m_outer.UnregisterPublishAttempt(attempt);
                        attempt.Dispose();
                    }
                }
                m_logger.PublishWorkerStopped(Index);
            }

            /// <summary>
            /// Back off after a publish response that carried a subscription
            /// identifier this manager could not dispatch to. The server keeps
            /// answering with the same undeliverable response until the
            /// identifier resolves or the subscription is gone, so without an
            /// explicit delay the worker would issue publish requests in a
            /// tight loop - and a transport that completes synchronously would
            /// additionally never release its thread pool thread. The delay
            /// escalates and is bounded so a healthy subscription sharing this
            /// worker is not starved.
            /// </summary>
            /// <param name="ct"></param>
            private Task DelayUnresolvedSubscriptionAsync(CancellationToken ct)
            {
                int count = ++m_consecutiveUnresolvedResponses;
                int backoffMs = Math.Min(
                    kBaseUnresolvedBackoffMs << Math.Min(count - 1, 4),
                    (int)s_maxUnresolvedBackoff.TotalMilliseconds);
                return m_outer.m_timeProvider
                    .Delay(TimeSpan.FromMilliseconds(backoffMs), ct);
            }

            /// <summary>
            /// Wait until acks arrive and return them
            /// </summary>
            /// <param name="maxWaitTime"></param>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task<List<PendingAcknowledgement>> WaitForAcksAsync(
                int maxWaitTime,
                CancellationToken ct)
            {
                Debug.Assert(maxWaitTime != 0, "Checked before entering");
                long swStart = m_outer.m_timeProvider.GetTimestamp();
                CancellationTokenSource? timeoutCts = null;
                CancellationTokenSource? linkedCts = null;
                CancellationToken waitToken;
                if (maxWaitTime != Timeout.Infinite)
                {
#if FALSE
                    var workers = m_outer.PublishWorkerCount;
                    if (workers == 0)
                    {
                        Debug.Fail("Must have at least this worker here.");
                        workers = 1;
                    }
                    maxWaitTime /= workers;
#endif
                    m_logger.PublishWorkerWaitingMaxForAcks(Index, maxWaitTime);
                    timeoutCts = m_outer.m_timeProvider
                        .CreateCancellationTokenSource(TimeSpan.FromMilliseconds(maxWaitTime));
                    linkedCts = CancellationTokenSource
                        .CreateLinkedTokenSource(ct, timeoutCts.Token);
                    waitToken = linkedCts.Token;
                }
                else
                {
                    m_logger.PublishWorkerWaitingForAcks(Index);
                    waitToken = ct;
                }
                try
                {
                    using var pauseCts =
                        CancellationTokenSource.CreateLinkedTokenSource(waitToken);
                    Task<PendingAcknowledgement> readTask = m_outer.m_acks.Reader
                        .ReadAsync(pauseCts.Token).AsTask();
                    Task pausedTask = m_outer.m_publishingPaused
                        .WaitAsync(pauseCts.Token);
                    bool readConsumed = false;
                    PendingAcknowledgement firstAck;
                    try
                    {
                        Task completed = await Task.WhenAny(readTask, pausedTask)
                            .ConfigureAwait(false);
                        if (ReferenceEquals(completed, pausedTask))
                        {
                            // Throws when the wait timed out or was cancelled;
                            // the finally below still rescues a racing ack.
                            await pausedTask.ConfigureAwait(false);
                            return [];
                        }
                        firstAck = await readTask.ConfigureAwait(false);
                        readConsumed = true;
                    }
                    finally
                    {
                        if (!readConsumed)
                        {
                            // A writer can hand an acknowledgement to the
                            // pending read in the same instant the wait ends.
                            // Put it back on every exit path, otherwise it is
                            // never sent and the server keeps the message in
                            // its retransmission queue.
                            await pauseCts.CancelAsync().ConfigureAwait(false);
                            try
                            {
                                PendingAcknowledgement acknowledgement =
                                    await readTask.ConfigureAwait(false);
                                m_outer.m_acks.Writer.TryWrite(acknowledgement);
                            }
                            catch (Exception) when (readTask.IsCanceled || readTask.IsFaulted)
                            {
                            }
                        }
                    }
                    await pauseCts.CancelAsync().ConfigureAwait(false);
                    try
                    {
                        await pausedTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    List<PendingAcknowledgement> acks = GetAcksReadyToSend();
                    acks.Insert(0, firstAck);
                    if (m_logger.IsEnabled(LogLevel.Debug))
                    {
                        m_logger.PublishWorkerPublishAcksAfterPausing(
                            Index,
                            acks.Count,
                            m_outer.m_timeProvider.GetElapsedTime(swStart));
                    }
                    return acks;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (m_logger.IsEnabled(LogLevel.Information))
                    {
                        m_logger.PublishWorkerNoAcksAfterWaiting(
                            Index,
                            m_outer.m_timeProvider.GetElapsedTime(swStart));
                    }
                    return [];
                }
                finally
                {
                    linkedCts?.Dispose();
                    timeoutCts?.Dispose();
                }
            }

            /// <summary>
            /// Get acks that are ready to send
            /// </summary>
            /// <returns></returns>
            private List<PendingAcknowledgement> GetAcksReadyToSend()
            {
                var acks = new List<PendingAcknowledgement>();

                // TODO: Is this something that we can get from ops limit?
                int available = m_outer.m_acks.Reader.Count;
                int maxAcks = available == 0
                    ? 0
                    : Math.Max(1, available / Math.Max(m_outer.PublishWorkerCount, 1));
                for (int i = 0; i < maxAcks &&
                    m_outer.m_acks.Reader.TryRead(out PendingAcknowledgement ack); i++)
                {
                    acks.Add(ack);
                }
                if (acks.Count != 0)
                {
                    m_logger.PublishWorkerAcknowledging(Index, acks.Count, available);
                }
                return acks;
            }

            /// <summary>
            /// Calculate the publish timeout to use
            /// </summary>
            /// <param name="latency"></param>
            /// <param name="currentTimeout"></param>
            /// <returns>Max time to throttle the publish</returns>
            private int CalculateTimeouts(long latency, ref uint currentTimeout)
            {
                List<IManagedSubscription> created = m_outer.Created;
                if (created.Count == 0)
                {
                    return 0;
                }

                TimeSpan timeout = TimeSpan.Zero;
                int minPublishInterval = Timeout.Infinite;
                foreach (IManagedSubscription s in created)
                {
                    TimeSpan publishingInterval = s.CurrentPublishingInterval;
                    // Saturate: the server may revise both factors up to the
                    // limits of their wire types (Part 4 §5.14.2.2), and a
                    // throwing multiply here would fault every publish worker.
                    TimeSpan keepAlive = SaturatingTimeSpan.Multiply(
                        publishingInterval, s.CurrentKeepAliveCount);
                    if (timeout < keepAlive)
                    {
                        timeout = keepAlive;
                    }

                    int pi = (int)Math.Min(
                        publishingInterval.TotalMilliseconds, int.MaxValue);
                    if (pi <= 0)
                    {
                        continue;
                    }
                    if (minPublishInterval > pi ||
                        minPublishInterval == Timeout.Infinite)
                    {
                        minPublishInterval = pi;
                    }
                }
                //
                // The timeout while publishing should be twice the
                // value for PublishingInterval * KeepAliveCount
                // TODO: Validate this against spec
                //
                timeout = SaturatingTimeSpan.Multiply(timeout, 2);
                if (timeout < s_minOperationTimeout)
                {
                    timeout = s_minOperationTimeout;
                }
                if (timeout > s_maxOperationTimeout)
                {
                    timeout = s_maxOperationTimeout;
                }
                uint newTimeout = (uint)timeout.TotalMilliseconds;
                if (newTimeout > currentTimeout)
                {
                    currentTimeout = newTimeout;
                }
                if (minPublishInterval < latency)
                {
                    return 0;
                }
                return minPublishInterval - (int)latency;
            }

            private readonly ILogger m_logger;
            private readonly SubscriptionManager m_outer;
            private readonly CancellationTokenSource m_cts;
            private int m_consecutivePublishErrors;
            private StatusCode m_lastLoggedErrorStatus;
            private uint m_lastUnknownSubscriptionId;
            private int m_consecutiveUnresolvedResponses;
            private const int kBasePublishBackoffMs = 200;
            private const int kBaseUnresolvedBackoffMs = 20;
            private static readonly TimeSpan s_maxPublishBackoff = TimeSpan.FromSeconds(5);
            private static readonly TimeSpan s_maxUnresolvedBackoff = TimeSpan.FromMilliseconds(250);
        }

        private static readonly TimeSpan s_maxOperationTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan s_minOperationTimeout = TimeSpan.FromSeconds(1);
        private const int kMaxSubscriptionHistory = 256;
        private const int kDeferredAcknowledgementWarningThreshold = 1000;
#pragma warning disable IDE0032 // Use auto property
        private int m_badPublishRequestCount;
        private int m_goodPublishRequestCount;
#pragma warning restore IDE0032 // Use auto property
        private readonly Channel<PendingAcknowledgement> m_acks;
        /// <summary>
        /// Serializes the session's acknowledgement-deferral callback and guards
        /// <see cref="m_deferredAcknowledgements"/> and <see cref="m_deferredWarningRaised"/>.
        /// </summary>
        private readonly Lock m_acknowledgementPolicyLock = new();
        /// <summary>
        /// Application-deferred acknowledgements, reconsidered on subsequent Publish attempts.
        /// </summary>
        private readonly List<PendingAcknowledgement> m_deferredAcknowledgements = [];
        /// <summary>
        /// Acknowledgements presented to the deferral callback. Guarded by
        /// <see cref="m_acknowledgementPolicyLock"/>.
        /// </summary>
        private readonly List<SubscriptionAcknowledgement> m_presentedAcknowledgements = [];
        private bool m_deferredWarningRaised;
        private readonly AcknowledgementOutcomeDispatcher m_outcomeDispatcher;
        /// <summary>
        /// Acknowledgements that are queued, deferred or in flight and have not ended yet.
        /// Only maintained while an observer receives outcomes.
        /// </summary>
        private readonly ConcurrentDictionary<AcknowledgementHandle, byte> m_outstandingAcknowledgements = new();
        /// <summary>
        /// Most recently created owner of each server id. Guarded by <see cref="m_subscriptionLock"/>.
        /// </summary>
        private readonly Dictionary<uint, SubscriptionClaim> m_claims = [];
        private long m_claimSequence;
        /// <summary>
        /// Registered subscriptions whose server id a more recently created subscription claimed
        /// (the server reused it). They stay superseded until they are recreated or retire, even
        /// when the newer owner completes first. Guarded by <see cref="m_subscriptionLock"/>.
        /// </summary>
        private readonly HashSet<IMessageProcessor> m_superseded =
            new(IdentityComparer<IMessageProcessor>.Instance);
        /// <summary>
        /// Latest retransmission-queue view the server reported per owner.
        /// Guarded by <see cref="m_subscriptionLock"/>.
        /// </summary>
        private readonly Dictionary<IMessageProcessor, AvailabilityWindow> m_windows =
            new(IdentityComparer<IMessageProcessor>.Instance);
        private readonly AsyncManualResetEvent m_running = new();
        private readonly AsyncManualResetEvent m_publishingPaused = new(true);
        private readonly AsyncAutoResetEvent m_publishControl = new();
        private readonly AsyncManualResetEvent m_drainSignal = new(true);
        private readonly AsyncManualResetEvent m_sessionAvailable = new(true);
        private readonly SemaphoreSlim m_publishQuiescenceGate = new(1, 1);
        private readonly Lock m_publishStateLock = new();

        /// <summary>Publishing lifecycle epoch, guarded by <see cref="m_publishStateLock"/>.</summary>
        private long m_publishEpoch;

        /// <summary>Attempt sequence, guarded by <see cref="m_publishStateLock"/>.</summary>
        private long m_publishRequestSequence;

        /// <summary>Recovery or success ordering boundary, guarded by <see cref="m_publishStateLock"/>.</summary>
        private long m_publishRecoveryWatermark;

        /// <summary>Whether this outage was reported, guarded by <see cref="m_publishStateLock"/>.</summary>
        private bool m_publishRecoveryReported;
        private readonly HashSet<CancellationTokenSource> m_activePublishAttempts = [];
        private readonly HashSet<CancellationTokenSource> m_activeSubscriptionUpdates = [];
        private readonly CancellationToken m_disposeToken;
        private int m_activePublishRequests;
        private int m_disposed;
        private bool m_publishingRequested;
        private bool m_publishingQuiesced;
        private bool m_sessionRecoveryPaused;
        private readonly ConcurrentQueue<uint> m_subscriptionHistory = new();
        private readonly Task m_publishController;
        private readonly Lock m_subscriptionLock = new();
        private readonly HashSet<IManagedSubscription> m_establishedSubscriptions = [];
        /// <summary>
        /// Dispatch registry: every partition subscription this manager
        /// owns, including the primaries of logical wrappers. Publish
        /// dispatch (GetById), acknowledgement routing, recreate, and
        /// transfer iterate this set so they remain partition-aware.
        /// </summary>
        private readonly HashSet<IManagedSubscription> m_subscriptions = [];
        /// <summary>
        /// Public registry: logical wrappers returned to callers from
        /// ISubscriptionManager.Items / Add. One wrapper per logical
        /// subscription regardless of how many partitions back it.
        /// </summary>
        private readonly HashSet<LogicalSubscription> m_logicals = [];
        private readonly CancellationTokenSource m_cts = new();
        private readonly ISubscriptionManagerContext m_session;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="SubscriptionManager"/>.
    /// </summary>
    internal static partial class SubscriptionManagerLog
    {
        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 0, Level = LogLevel.Error,
            Message = "Exception during dispose")]
        public static partial void ExceptionDuringDispose(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 1, Level = LogLevel.Information,
            Message = "{SubscriptionId} REMOVED.")]
        public static partial void SubscriptionRemoved(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 2, Level = LogLevel.Information,
            Message = "{SubscriptionId} ADDED.")]
        public static partial void SubscriptionAdded(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 3, Level = LogLevel.Information,
            Message = "Idle-delete of secondary partition {PartitionId} threw on dispose.")]
        public static partial void IdleDeleteSecondaryPartitionThrew(
            this ILogger logger,
            Exception exception,
            uint partitionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 4, Level = LogLevel.Information,
            Message = "{SubscriptionId} ADDED (secondary partition).")]
        public static partial void SubscriptionAddedSecondaryPartition(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 5, Level = LogLevel.Information,
            Message = "{SubscriptionId} ADDED (preloaded secondary partition).")]
        public static partial void SubscriptionAddedPreloadedSecondaryPartition(
            this ILogger logger,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 6, Level = LogLevel.Information,
            Message = "{SubscriptionId} ADDED (transfer-pending, ServerId={ServerId}).")]
        public static partial void SubscriptionAddedTransferPending(
            this ILogger logger,
            uint subscriptionId,
            uint serverId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 7, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: TransferSubscriptions per-item result Bad ({Status}); " +
                "falling back to recreate.")]
        public static partial void TransferPerItemResultBad(
            this ILogger logger,
            uint subscriptionId,
            StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 8, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: server does not support TransferSubscriptions; " +
                "falling back to recreate.")]
        public static partial void ServerDoesNotSupportTransfer(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 9, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: TransferSubscriptions service-level result Bad ({Status}); " +
                "falling back to recreate.")]
        public static partial void TransferServiceLevelResultBad(
            this ILogger logger,
            uint subscriptionId,
            StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 10, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: re-creation did not complete within 15s after failed transfer; " +
                "returning anyway.")]
        public static partial void ReCreationDidNotComplete(this ILogger logger, uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 11, Level = LogLevel.Warning,
            Message = "{SubscriptionId}: TransferSubscriptions threw on secondary partition restore; " +
                "falling back to recreate.")]
        public static partial void TransferThrewSecondaryPartitionRestore(
            this ILogger logger,
            Exception exception,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 12, Level = LogLevel.Warning,
            Message = "Transfer subscription unsupported, TransferSubscriptionsOnReconnect set to false.")]
        public static partial void TransferSubscriptionUnsupported(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 13, Level = LogLevel.Error,
            Message = "Transfer subscriptions failed with error {Error}.")]
        public static partial void TransferSubscriptionsFailed(this ILogger logger, StatusCode error);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 14, Level = LogLevel.Debug,
            Message = "Subscription {Id} is already member of the session.")]
        public static partial void SubscriptionAlreadyMember(this ILogger logger, uint id);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 15, Level = LogLevel.Error,
            Message = "Subscription {Id} failed to transfer, StatusCode={Status}")]
        public static partial void SubscriptionFailedToTransfer(this ILogger logger, uint id, StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 16, Level = LogLevel.Information,
            Message = "Removing publish worker {Index}")]
        public static partial void RemovingPublishWorker(this ILogger logger, int index);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 17, Level = LogLevel.Information,
            Message = "Publish worker {Index} exited")]
        public static partial void PublishWorkerExited(this ILogger logger, int index);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 18, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle} - STARTED.")]
        public static partial void PublishWorkerStarted(this ILogger logger, int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 19, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle} - PAUSED.")]
        public static partial void PublishWorkerPaused(this ILogger logger, int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 20, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle} - RESUMED.")]
        public static partial void PublishWorkerResumed(this ILogger logger, int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 21, Level = LogLevel.Error,
            Message = "PUBLISH Worker #{Handle} - Unexpected exception while waiting to connect.")]
        public static partial void PublishWorkerUnexpectedExceptionConnecting(
            this ILogger logger,
            Exception exception,
            int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 22, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle}-{Id} - Received Publish Response for Unknown " +
                "SubscriptionId={SubscriptionId}. Deleting...")]
        public static partial void PublishWorkerReceivedUnknownSubscription(
            this ILogger logger,
            int handle,
            uint id,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 23, Level = LogLevel.Warning,
            Message = "PUBLISH Worker #{Handle}-{Id} - Publish abandoned after error due to reconnect: " +
                "{Message}")]
        public static partial void PublishAbandonedAfterError(
            this ILogger logger,
            int handle,
            uint id,
            [SensitiveText] string message);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 24, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle}-{Id} - Transport unavailable ({Status}); " +
                "waiting for reconnect.")]
        public static partial void TransportUnavailable(
            this ILogger logger,
            int handle,
            uint id,
            StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 25, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle}-{Id} - Server busy, throttling worker.")]
        public static partial void ServerBusyThrottling(this ILogger logger, int handle, uint id);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 26, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle}-{Id} - Timed out, increasing timeout to {Timeout}.")]
        public static partial void TimedOutIncreasingTimeout(
            this ILogger logger,
            int handle,
            uint id,
            uint timeout);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 27, Level = LogLevel.Error,
            Message = "PUBLISH Worker #{Handle}-{Id} - Unhandled error {Status} during Publish.")]
        public static partial void UnhandledErrorDuringPublish(
            this ILogger logger,
            Exception exception,
            int handle,
            uint id,
            StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 28, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle}-{Id} - Unhandled error {Status} during Publish (repeated).")]
        public static partial void UnhandledErrorDuringPublishRepeated(
            this ILogger logger,
            int handle,
            uint id,
            StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 29, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle} - STOPPED.")]
        public static partial void PublishWorkerStopped(this ILogger logger, int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 30, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle} - Waiting max {Time}ms for acks to arrive.")]
        public static partial void PublishWorkerWaitingMaxForAcks(this ILogger logger, int handle, int time);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 31, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle} - Waiting for acks to arrive.")]
        public static partial void PublishWorkerWaitingForAcks(this ILogger logger, int handle);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 32, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle} - Publish {Count} acks after pausing {Duration}.")]
        public static partial void PublishWorkerPublishAcksAfterPausing(
            this ILogger logger,
            int handle,
            int count,
            TimeSpan duration);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 33, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle} - Publish with no acks after waiting {Duration}.")]
        public static partial void PublishWorkerNoAcksAfterWaiting(
            this ILogger logger,
            int handle,
            TimeSpan duration);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 34, Level = LogLevel.Debug,
            Message = "PUBLISH Worker #{Handle} - Acknoledging {Count} of {Total} messages.")]
        public static partial void PublishWorkerAcknowledging(
            this ILogger logger,
            int handle,
            int count,
            int total);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManagerContinued + 1, Level = LogLevel.Error,
            Message = "The acknowledgement provider failed; {Count} acknowledgements are deferred.")]
        public static partial void AcknowledgementProviderFailed(
            this ILogger logger,
            Exception exception,
            int count);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManagerContinued + 0, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle}-{Id} - Discarded message #{SequenceNumber} of " +
                "SubscriptionId={SubscriptionId}: it was published before the subscription that " +
                "now holds the reused id was created.")]
        public static partial void PublishWorkerDiscardedMessageOfPreviousSubscription(
            this ILogger logger,
            int handle,
            uint id,
            uint subscriptionId,
            uint sequenceNumber);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 35, Level = LogLevel.Information,
            Message = "PUBLISH Worker #{Handle}-{Id} - Received Publish Response for Unknown " +
                "SubscriptionId={SubscriptionId} while subscriptions are still being created. " +
                "Dropping the message instead of deleting the subscription.")]
        public static partial void PublishWorkerDeferredUnknownSubscription(
            this ILogger logger,
            int handle,
            uint id,
            uint subscriptionId);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 37, Level = LogLevel.Debug,
            Message = "Discarded {Count} acknowledgements of retired or reused subscription ids.")]
        public static partial void StaleAcknowledgementsDiscarded(
            this ILogger logger,
            int count);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 38, Level = LogLevel.Warning,
            Message = "{Count} acknowledgements are deferred by the acknowledgement policy. " +
                "The server keeps the corresponding messages until they are acknowledged.")]
        public static partial void DeferredAcknowledgementsAccumulating(
            this ILogger logger,
            int count);

        [LoggerMessage(EventId = ClientEventIds.SubscriptionManager + 39,
            Message = "The server rejected acknowledgements ({Count} sent): {Unknown} unknown sequence " +
                "numbers, {Invalid} invalid subscription ids, {Other} other errors.")]
        public static partial void AcknowledgementsRejected(
            this ILogger logger,
            LogLevel level,
            int count,
            int unknown,
            int invalid,
            int other);
    }
}
