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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua.Client.Subscriptions.MonitoredItems;

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
            ReturnDiagnostics = returnDiagnostics;
            m_publishController = PublishControllerAsync(m_cts.Token);
            m_acks = Channel.CreateUnboundedPrioritized(
                new UnboundedPrioritizedChannelOptions<SubscriptionAcknowledgement>
                {
                    Comparer = Comparer<SubscriptionAcknowledgement>
                        .Create((x, y) => x.SequenceNumber.CompareTo(y.SequenceNumber))
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
                    return m_subscriptions.ToList();
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
                    return m_subscriptions.Count;
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
            try
            {
                await m_cts.CancelAsync().ConfigureAwait(false);
                m_publishControl.Set();
                await m_publishController.ConfigureAwait(false);

                List<IManagedSubscription>? subscriptions = null;
                lock (m_subscriptionLock)
                {
                    subscriptions = [.. m_subscriptions];
                    m_subscriptions.Clear();
                }
                foreach (IManagedSubscription subscription in subscriptions)
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                subscriptions.Clear();
                m_subscriptionHistory.Clear();
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Exception during dispose");
            }
            finally
            {
                m_cts.Dispose();
                (m_acks as IDisposable)?.Dispose();
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

        /// <inheritdoc/>
        public ValueTask QueueAsync(
            SubscriptionAcknowledgement ack, CancellationToken ct)
        {
            return m_acks.Writer.WriteAsync(ack, ct);
        }

        /// <inheritdoc/>
        public int DropPendingForSubscription(uint subscriptionId)
        {
            // Drain the prioritised channel into a local buffer,
            // keeping only the acks that do not target the dead
            // subscription id, then re-publish the kept items.
            // Concurrent workers may interleave reads or writes
            // during this loop; that is intentional — a worker
            // racing in is acceptable because (a) any ack it reads
            // would have been sent to the server anyway, (b) any
            // ack it writes is queued back into the channel. The
            // contract is "no targeted acks remain when this
            // method returns" — callers must invoke it BEFORE
            // recreate assigns a fresh subscription id so a new
            // generation cannot enter the queue.
            var keep = new List<SubscriptionAcknowledgement>();
            int dropped = 0;
            while (m_acks.Reader.TryRead(out SubscriptionAcknowledgement? ack))
            {
                if (ack.SubscriptionId == subscriptionId)
                {
                    dropped++;
                }
                else
                {
                    keep.Add(ack);
                }
            }
            foreach (SubscriptionAcknowledgement ack in keep)
            {
                m_acks.Writer.TryWrite(ack);
            }
            return dropped;
        }

        /// <inheritdoc/>
        public ValueTask CompleteAsync(uint subscriptionId, CancellationToken ct)
        {
            IManagedSubscription? subscription;
            lock (m_subscriptionLock)
            {
                // find the subscription.
                subscription = m_subscriptions
                    .FirstOrDefault(s => s.Id == subscriptionId);
                if (subscription == null ||
                    !m_subscriptions.Remove(subscription))
                {
                    return default;
                }
                m_subscriptionHistory.Enqueue(subscriptionId);
            }
            while (m_subscriptionHistory.Count > kMaxSubscriptionHistory)
            {
                m_subscriptionHistory.TryDequeue(out _);
            }
            m_logger.LogInformation("{Subscription} REMOVED.", subscription);
            m_publishControl.Set();
            return default;
        }

        /// <inheritdoc/>
        public ISubscription Add(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options)
        {
            IManagedSubscription subscription = m_session.CreateSubscription(handler, options, this);
            lock (m_subscriptionLock)
            {
                if (!m_subscriptions.Add(subscription))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to add subscription.");
                }
                m_logger.LogInformation("{Subscription} ADDED.", subscription);
            }
            m_publishControl.Set();
            return subscription;
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
                itemLoadStates.Add(new MonitoredItemLoadState(
                    item.Name,
                    new OptionsMonitor<MonitoredItems.MonitoredItemOptions>(item.ToOptions()),
                    item.ClientHandle,
                    item.ServerId,
                    item.TriggeringItemClientHandle));
            }
            var loadState = new SubscriptionLoadState(
                state.ServerId, itemLoadStates);

            SubscriptionOptions options = state.ToOptions();
            IManagedSubscription subscription = m_session.CreateSubscription(
                handler,
                new OptionsMonitor<SubscriptionOptions>(options),
                this,
                loadState);
            lock (m_subscriptionLock)
            {
                if (!m_subscriptions.Add(subscription))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to add restored subscription.");
                }
                m_logger.LogInformation(
                    "{Subscription} ADDED (transfer-pending, ServerId={ServerId}).",
                    subscription,
                    state.ServerId);
            }

            // Issue TransferSubscriptions for the saved server id.
            // sendInitialValues honors SubscriptionOptions.SendInitialValuesOnTransfer
            // (default false) — the snapshot captured the last server-
            // emitted values, so requesting initial values is only
            // useful when the caller wants the server to re-emit them
            // to a fresh notification handler.
            var ids = new uint[] { state.ServerId };
            TransferSubscriptionsResponse response = await m_session
                .TransferSubscriptionsAsync(
                    null,
                    ids.ToArrayOf(),
                    sendInitialValues: options.SendInitialValuesOnTransfer,
                    ct)
                .ConfigureAwait(false);

            bool transferred = false;
            ResponseHeader responseHeader = response.ResponseHeader;
            if (StatusCode.IsGood(responseHeader.ServiceResult))
            {
                ArrayOf<TransferResult> results = response.Results;
                ClientBase.ValidateResponse(results, ids.ToArrayOf());
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
                    m_logger.LogWarning(
                        "{Subscription}: TransferSubscriptions per-item " +
                        "result Bad ({Status}); falling back to recreate.",
                        subscription,
                        results[0].StatusCode);
                }
            }
            else if (responseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
            {
                m_logger.LogWarning(
                    "{Subscription}: server does not support " +
                    "TransferSubscriptions; falling back to recreate.",
                    subscription);
            }
            else
            {
                m_logger.LogWarning(
                    "{Subscription}: TransferSubscriptions service-level " +
                    "result Bad ({Status}); falling back to recreate.",
                    subscription,
                    responseHeader.ServiceResult);
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
                    m_logger.LogWarning(
                        "{Subscription}: re-creation did not complete " +
                        "within 15s after failed transfer; returning anyway.",
                        subscription);
                }
            }

            m_publishControl.Set();
            return subscription;
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
            lock (m_subscriptionLock)
            {
                foreach (IManagedSubscription item in m_subscriptions)
                {
                    item.NotifySubscriptionManagerPaused(false);
                }
            }
            m_running.Set();
        }

        /// <summary>
        /// Pause subscriptions
        /// </summary>
        internal void Pause()
        {
            lock (m_subscriptionLock)
            {
                foreach (IManagedSubscription item in m_subscriptions)
                {
                    item.NotifySubscriptionManagerPaused(true);
                }
            }
            m_running.Reset();
        }

        /// <summary>
        /// Wait for all in-flight publish requests to complete or be
        /// cancelled. Used together with <see cref="Pause"/> by callers
        /// that need a hard quiesce (e.g. a session re-create) before
        /// rebinding session state. <see cref="Pause"/> alone is a
        /// soft signal — workers complete their current cycle before
        /// observing the pause.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        internal Task DrainAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref m_activePublishRequests) == 0)
            {
                return Task.CompletedTask;
            }
            return m_drainSignal.WaitAsync(ct);
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

            IReadOnlyList<IManagedSubscription> subscriptions = [.. m_subscriptions];
            if (TransferSubscriptionsOnRecreate && previousSessionId != null)
            {
                subscriptions = await TransferSubscriptionsAsync(subscriptions,
                    false, ct).ConfigureAwait(false);
            }
            // Force creation of the subscriptions which were not transferred.
            foreach (IManagedSubscription subscription in subscriptions)
            {
                bool force = previousSessionId != null && subscription.Created;
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
                TransferSubscriptionsResponse response = await m_session.TransferSubscriptionsAsync(
                    null,
                    subscriptionIds,
                    sendInitialValues,
                    ct).ConfigureAwait(false);

                ResponseHeader responseHeader = response.ResponseHeader;
                if (!StatusCode.IsGood(responseHeader.ServiceResult))
                {
                    if (responseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnRecreate = false;
                        m_logger.LogWarning("Transfer subscription unsupported, " +
                            "TransferSubscriptionsOnReconnect set to false.");
                    }
                    else
                    {
                        m_logger.LogError(
                            "Transfer subscriptions failed with error {Error}.",
                            responseHeader.ServiceResult);
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
                        m_logger.LogDebug(
                            "Subscription {Id} is already member of the session.",
                            subscriptionIds[index]);
                        // Done
                    }
                    else if (!StatusCode.IsGood(transferResults[index].StatusCode))
                    {
                        m_logger.LogError(
                            "Subscription {Id} failed to transfer, StatusCode={Status}",
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
                // find the subscription.
                foreach (IManagedSubscription subscription in m_subscriptions)
                {
                    if (subscription.Id == subscriptionId)
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
            try
            {
                while (!ct.IsCancellationRequested)
                {
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
                            m_logger.LogInformation("Removing publish worker {Index}",
                                worker.Index);
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
                        // Not enough workers increase
                        publishWorkers.AddRange(Enumerable
                            .Range(publishWorkers.Count,
                                desiredWorkerCount - publishWorkers.Count)
                            .Select(index => new PublishWorker(this, index)));
                    }
                    PublishWorkerCount = publishWorkers.Count;

                    Task[] waiting = [.. publishWorkers
                        .Select(w => w.Task)
                        .Prepend(m_publishControl.WaitAsync(ct))];
                    await Task.WhenAny(waiting).ConfigureAwait(false);
                    PublishControlCycles++;
                    int index = 0;
                    foreach (Task? item in waiting.Skip(1)) // Skip wait handle
                    {
                        if (item.IsCompleted)
                        {
                            PublishWorker worker = publishWorkers[index];
                            m_logger.LogInformation(
                                "Publish worker {Index} exited",
                                worker.Index);
                            await worker.DisposeAsync().ConfigureAwait(false);
                            publishWorkers.RemoveAt(index);
                            continue;
                        }
                        index++;
                    }

                    // Now lower the max publish request if we got any too
                    // many requests errors
                    if (publishWorkers.Any(w => w.TooManyPublishRequests))
                    {
                        if (MaxPublishWorkerCount > 1)
                        {
                            MaxPublishWorkerCount--;
                        }
                    }
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
                int publishCount = CreatedCount;
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
            public bool TooManyPublishRequests { get; private set; }

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
                Task = PublishWorkerAsync(m_cts.Token);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await m_cts.CancelAsync().ConfigureAwait(false);
                    if (!Task.IsCompleted)
                    {
                        try
                        {
                            await Task.ConfigureAwait(false);
                        }
                        catch
                        {
                        } // Ignore
                    }
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
                m_logger.LogInformation("PUBLISH Worker #{Handle} - STARTED.", Index);
                while (!ct.IsCancellationRequested)
                {
                    if (!m_outer.m_running.IsSet)
                    {
                        m_logger.LogInformation("PUBLISH Worker #{Handle} - PAUSED.", Index);
                        try
                        {
                            await m_outer.m_running.WaitAsync(ct).ConfigureAwait(false);
                            m_logger.LogInformation(
                                "PUBLISH Worker #{Handle} - RESUMED.",
                                Index);
                        }
                        catch (OperationCanceledException)
                        {
                            continue;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(
                                ex,
                                "PUBLISH Worker #{Handle} - Unexpected exception while waiting to connect.",
                                Index);
                            break;
                        }
                    }
                    long currentLatencyMs = publishLatencyRunning
                        ? (long)m_outer.m_timeProvider.GetElapsedTime(publishLatencyStart).TotalMilliseconds
                        : (long)publishLatency.TotalMilliseconds;
                    int ackWaitTimeout = CalculateTimeouts(
                        currentLatencyMs, ref timeoutHint);
                    ArrayOf<SubscriptionAcknowledgement> acks = GetAcksReadyToSend();
                    uint handle = Utils.IncrementIdentifier(ref m_outer.m_publishRequestCounter);
                    try
                    {
                        if (acks.Count == 0 && !moreNotifications && ackWaitTimeout != 0)
                        {
                            // Throttle publishing as we wait for acks to arrive
                            acks = await WaitForAcksAsync(ackWaitTimeout, ct).ConfigureAwait(false);
                        }
                        publishLatencyStart = m_outer.m_timeProvider.GetTimestamp();
                        publishLatencyRunning = true;
                        if (Interlocked.Increment(ref m_outer.m_activePublishRequests) == 1)
                        {
                            m_outer.m_drainSignal.Reset();
                        }
                        try
                        {
                            PublishResponse response = await m_outer.m_session.PublishAsync(new RequestHeader
                            {
                                TimeoutHint = timeoutHint,
                                ReturnDiagnostics = (uint)(int)m_outer.ReturnDiagnostics,
                                RequestHandle = handle
                            }, acks, ct).ConfigureAwait(false);

                            moreNotifications = response.MoreNotifications;
                            uint subscriptionId = response.SubscriptionId;
                            NotificationMessage notificationMessage = response.NotificationMessage;
                            ArrayOf<uint> availableSequenceNumbers = response.AvailableSequenceNumbers;

                            ArrayOf<StatusCode> acknowledgeResults = response.Results;
                            ArrayOf<DiagnosticInfo> acknowledgeDiagnosticInfos = response.DiagnosticInfos;
                            ClientBase.ValidateResponse(acknowledgeResults, acks);
                            ClientBase.ValidateDiagnosticInfos(acknowledgeDiagnosticInfos, acks);
                            TooManyPublishRequests = false;

                            // Get the subscription with the provided identifier
                            IManagedSubscription? subscription = m_outer.GetById(subscriptionId);
                            publishLatency = m_outer.m_timeProvider.GetElapsedTime(publishLatencyStart);
                            publishLatencyRunning = false;
                            if (subscription != null)
                            {
                                // deliver to subscription
                                await subscription.OnPublishReceivedAsync(
                                    notificationMessage,
                                    availableSequenceNumbers.ToList(),
                                    response.ResponseHeader.StringTable.ToList()).ConfigureAwait(false);
                                Interlocked.Increment(ref m_outer.m_goodPublishRequestCount);
                            }
                            else if (!ct.IsCancellationRequested &&
                                !m_outer.m_subscriptionHistory.Contains(subscriptionId))
                            {
                                // ignore messages with a subscription that was deleted
                                // Do not delete publish requests of stale subscriptions
                                m_logger.LogInformation(
                                    "PUBLISH Worker #{Handle}-{Id} - Received Publish Response " +
                                    "for Unknown SubscriptionId={SubscriptionId}. Deleting...",
                                    Index, handle, subscriptionId);
                                Interlocked.Increment(ref m_outer.m_badPublishRequestCount);
                                await m_outer.m_session.DeleteSubscriptionsAsync(
                                    null,
                                    [subscriptionId],
                                    ct).ConfigureAwait(false);
                                moreNotifications = true;
                            }
                        }
                        finally
                        {
                            int active = Interlocked.Decrement(ref m_outer.m_activePublishRequests);
                            if (active == 0)
                            {
                                m_outer.m_drainSignal.Set();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
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
                            break;
                        }

                        Interlocked.Increment(ref m_outer.m_badPublishRequestCount);
                        // Rollback acks we collected
                        acks.ForEach(ack => m_outer.m_acks.Writer.TryWrite(ack));

                        // ignore errors if paused.
                        if (!m_outer.m_running.IsSet)
                        {
                            m_logger.LogWarning("PUBLISH Worker #{Handle}-{Id} - Publish " +
                                "abandoned after error due to reconnect: {Message}",
                                Index, handle, e.Message);
                            continue;
                        }

                        // don't send another publish for these errors,
                        // or throttle to avoid server overload.
                        StatusCode statusCode = error.StatusCode;
                        if (statusCode == StatusCodes.BadTooManyPublishRequests)
                        {
                            TooManyPublishRequests = true;
                        }
                        else if (statusCode == StatusCodes.BadNoSubscription ||
                            statusCode == StatusCodes.BadSessionClosed ||
                            statusCode == StatusCodes.BadSecurityChecksFailed ||
                            statusCode == StatusCodes.BadCertificateInvalid ||
                            statusCode == StatusCodes.BadServerHalted)
                        {
                            // ignore
                        }
                        // may require a reconnect or activate to recover
                        else if (statusCode == StatusCodes.BadSessionIdInvalid ||
                            statusCode == StatusCodes.BadSecureChannelIdInvalid ||
                            statusCode == StatusCodes.BadSecureChannelClosed)
                        {
                            // TODO
                            // OnKeepAliveError(error);
                        }
                        // Servers may return this error when overloaded
                        else if (statusCode == StatusCodes.BadTooManyOperations ||
                            statusCode == StatusCodes.BadTcpServerTooBusy ||
                            statusCode == StatusCodes.BadServerTooBusy)
                        {
                            // throttle the next publish to reduce server load
                            m_logger.LogDebug("PUBLISH Worker #{Handle}-{Id} - " +
                                "Server busy, throttling worker.",
                                Index, handle);
                            moreNotifications = false; // throttle
                        }
                        else if (statusCode == StatusCodes.BadTimeout ||
                            statusCode == StatusCodes.BadRequestTimeout)
                        {
                            // Timed out - retry with larger timeout
                            timeoutHint += 1000; // Increase by seconds
                            m_logger.LogDebug("PUBLISH Worker #{Handle}-{Id} - " +
                                "Timed out, increasing timeout to {Timeout}.",
                                Index, handle, timeoutHint);
                            moreNotifications = true;
                        }
                        else
                        {
                            m_logger.LogError(e, "PUBLISH Worker #{Handle}-{Id} - " +
                                "Unhandled error {Status} during Publish.",
                                Index, handle, error.StatusCode);
                        }
                    }
                }
                m_logger.LogInformation("PUBLISH Worker #{Handle} - STOPPED.", Index);
            }

            /// <summary>
            /// Wait until acks arrive and return them
            /// </summary>
            /// <param name="maxWaitTime"></param>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task<ArrayOf<SubscriptionAcknowledgement>> WaitForAcksAsync(
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
                    m_logger.LogDebug(
                        "PUBLISH Worker #{Handle} - Waiting max {Time}ms for acks to arrive.",
                        Index, maxWaitTime);
                    timeoutCts = m_outer.m_timeProvider
                        .CreateCancellationTokenSource(TimeSpan.FromMilliseconds(maxWaitTime));
                    linkedCts = CancellationTokenSource
                        .CreateLinkedTokenSource(ct, timeoutCts.Token);
                    waitToken = linkedCts.Token;
                }
                else
                {
                    m_logger.LogDebug(
                        "PUBLISH Worker #{Handle} - Waiting for acks to arrive.",
                        Index);
                    waitToken = ct;
                }
                try
                {
                    SubscriptionAcknowledgement firstAck = await m_outer.m_acks.Reader.ReadAsync(
                        waitToken).ConfigureAwait(false);
                    ArrayOf<SubscriptionAcknowledgement> restAcks = GetAcksReadyToSend();
                    var ackList = restAcks.ToList();
                    ackList.Insert(0, firstAck);
                    ArrayOf<SubscriptionAcknowledgement> acks = ackList;
                    m_logger.LogDebug(
                        "PUBLISH Worker #{Handle} - Publish {Count} acks after pausing {Duration}.",
                        Index,
                        acks.Count,
                        m_outer.m_timeProvider.GetElapsedTime(swStart));
                    return acks;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    m_logger.LogInformation(
                        "PUBLISH Worker #{Handle} - Publish with no acks after waiting {Duration}.",
                        Index,
                        m_outer.m_timeProvider.GetElapsedTime(swStart));
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
            private ArrayOf<SubscriptionAcknowledgement> GetAcksReadyToSend()
            {
                var acks = new List<SubscriptionAcknowledgement>();

                // TODO: Is this something that we can get from ops limit?
                int available = m_outer.m_acks.Reader.Count;
                int maxAcks = available / Math.Max(m_outer.PublishWorkerCount, 1);
                for (int i = 0; i < maxAcks &&
                    m_outer.m_acks.Reader.TryRead(out SubscriptionAcknowledgement? ack); i++)
                {
                    acks.Add(ack);
                }
                if (acks.Count != 0)
                {
                    m_logger.LogDebug(
                        "PUBLISH Worker #{Handle} - Acknoledging {Count} of {Total} messages.",
                        Index, acks.Count, available);
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
                    TimeSpan keepAlive = publishingInterval.Multiply(s.CurrentKeepAliveCount);
                    if (timeout < keepAlive)
                    {
                        timeout = keepAlive;
                    }

                    int pi = (int)publishingInterval.TotalMilliseconds;
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
                timeout = timeout.Multiply(2);
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
        }

        private static readonly TimeSpan s_maxOperationTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan s_minOperationTimeout = TimeSpan.FromSeconds(1);
        private const int kMaxSubscriptionHistory = 10;
        private uint m_publishRequestCounter;
#pragma warning disable IDE0032 // Use auto property
        private int m_badPublishRequestCount;
        private int m_goodPublishRequestCount;
#pragma warning restore IDE0032 // Use auto property
        private readonly Channel<SubscriptionAcknowledgement> m_acks;
        private readonly AsyncManualResetEvent m_running = new();
        private readonly AsyncAutoResetEvent m_publishControl = new();
        private readonly AsyncManualResetEvent m_drainSignal = new(true);
        private int m_activePublishRequests;
        private int m_disposed;
        private readonly ConcurrentQueue<uint> m_subscriptionHistory = new();
        private readonly Task m_publishController;
        private readonly Lock m_subscriptionLock = new();
        private readonly HashSet<IManagedSubscription> m_subscriptions = [];
        private readonly CancellationTokenSource m_cts = new();
        private readonly ISubscriptionManagerContext m_session;
        private readonly ILoggerFactory m_loggerFactory;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
    }
}
