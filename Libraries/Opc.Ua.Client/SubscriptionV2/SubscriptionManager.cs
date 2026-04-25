#if OPCUA_CLIENT_V2
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

namespace Opc.Ua.Client.Subscriptions
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

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
        public DiagnosticsMasks ReturnDiagnostics { get; set; }

        /// <inheritdoc/>
        public int MinPublishWorkerCount { get; set; } = 2;

        /// <inheritdoc/>
        public int MaxPublishWorkerCount { get; set; } = 15;

        /// <inheritdoc/>
        public IEnumerable<ISubscription> Items
        {
            get
            {
                lock (_subscriptionLock)
                {
                    return _subscriptions.ToList();
                }
            }
        }

        /// <inheritdoc/>
        public int Count
        {
            get
            {
                lock (_subscriptionLock)
                {
                    return _subscriptions.Count;
                }
            }
        }

        /// <inheritdoc/>
        public int GoodPublishRequestCount => _goodPublishRequestCount;

        /// <inheritdoc/>
        public int BadPublishRequestCount => _badPublishRequestCount;

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
                lock (_subscriptionLock)
                {
                    return [.. _subscriptions.Where(s => s.Created)];
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
                lock (_subscriptionLock)
                {
                    return _subscriptions.Count(s => s.Created);
                }
            }
        }

        /// <summary>
        /// Create subscription manager
        /// </summary>
        /// <param name="session"></param>
        /// <param name="loggerFactory"></param>
        /// <param name="returnDiagnostics"></param>
        public SubscriptionManager(ISubscriptionManagerContext session,
            ILoggerFactory loggerFactory, DiagnosticsMasks returnDiagnostics)
        {
            _session = session;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SubscriptionManager>();
            ReturnDiagnostics = returnDiagnostics;
            _publishController = PublishControllerAsync(_cts.Token);
            _acks = Channel.CreateUnboundedPrioritized<SubscriptionAcknowledgement>(
                new UnboundedPrioritizedChannelOptions<SubscriptionAcknowledgement>
                {
                    Comparer = Comparer<SubscriptionAcknowledgement>
                        .Create((x, y) => x.SequenceNumber.CompareTo(y.SequenceNumber))
                });
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                _cts.Cancel();
                _publishControl.Set();
                await _publishController.ConfigureAwait(false);

                List<IManagedSubscription>? subscriptions = null;
                lock (_subscriptionLock)
                {
                    subscriptions = [.. _subscriptions];
                    _subscriptions.Clear();
                }
                foreach (var subscription in subscriptions)
                {
                    await subscription.DisposeAsync().ConfigureAwait(false);
                }
                subscriptions.Clear();
                _subscriptionHistory.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during dispose");
            }
            finally
            {
                _cts.Dispose();
            }
        }

        /// <summary>
        /// Trigger publish controller
        /// </summary>
        public void Update()
        {
            _publishControl.Set();
        }

        /// <inheritdoc/>
        public ValueTask QueueAsync(
            SubscriptionAcknowledgement ack, CancellationToken ct)
        {
            return _acks.Writer.WriteAsync(ack, ct);
        }

        /// <inheritdoc/>
        public ValueTask CompleteAsync(uint subscriptionId, CancellationToken ct)
        {
            IManagedSubscription? subscription;
            lock (_subscriptionLock)
            {
                // find the subscription.
                subscription = _subscriptions
                    .FirstOrDefault(s => s.Id == subscriptionId);
                if (subscription == null ||
                    !_subscriptions.Remove(subscription))
                {
                    return ValueTask.CompletedTask;
                }
                _subscriptionHistory.Enqueue(subscriptionId);
            }
            while (_subscriptionHistory.Count > kMaxSubscriptionHistory)
            {
                _subscriptionHistory.TryDequeue(out _);
            }
            _logger.LogInformation("{Subscription} REMOVED.", subscription);
            _publishControl.Set();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public ISubscription Add(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<SubscriptionOptions> options)
        {
            var subscription = _session.CreateSubscription(handler, options, this);
            lock (_subscriptionLock)
            {
                if (!_subscriptions.Add(subscription))
                {
                    throw ServiceResultException.Create(StatusCodes.BadAlreadyExists,
                        "Failed to add subscription.");
                }
                _logger.LogInformation("{Subscription} ADDED.", subscription);
            }
            _publishControl.Set();
            return subscription;
        }

        /// <summary>
        /// Resume subscriptions
        /// </summary>
        internal void Resume()
        {
            lock (_subscriptionLock)
            {
                foreach (var item in _subscriptions)
                {
                    item.NotifySubscriptionManagerPaused(false);
                }
            }
            _running.Set();
        }

        /// <summary>
        /// Pause subscriptions
        /// </summary>
        internal void Pause()
        {
            lock (_subscriptionLock)
            {
                foreach (var item in _subscriptions)
                {
                    item.NotifySubscriptionManagerPaused(true);
                }
            }
            _running.Reset();
        }

        /// <summary>
        /// Recreate subscriptions
        /// </summary>
        /// <param name="previousSessionId"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async Task RecreateSubscriptionsAsync(NodeId? previousSessionId,
            CancellationToken ct)
        {
            if (Count == 0)
            {
                // Nothing to do
                return;
            }

            IReadOnlyList<IManagedSubscription> subscriptions = [.. _subscriptions];
            if (TransferSubscriptionsOnRecreate && previousSessionId != null)
            {
                subscriptions = await TransferSubscriptionsAsync(subscriptions,
                    false, ct).ConfigureAwait(false);
            }
            // Force creation of the subscriptions which were not transferred.
            foreach (var subscription in subscriptions)
            {
                var force = previousSessionId != null && subscription.Created;
                await subscription.RecreateAsync(ct).ConfigureAwait(false);
            }
            _publishControl.Set();

            // Helper to try and transfer the subscriptions
            async Task<IReadOnlyList<IManagedSubscription>> TransferSubscriptionsAsync(
                IReadOnlyList<IManagedSubscription> subscriptions, bool sendInitialValues,
                CancellationToken ct)
            {
                var remaining = subscriptions.Where(s => !s.Created).ToList();
                subscriptions = subscriptions.Where(s => s.Created).ToList();
                if (subscriptions.Count == 0)
                {
                    return remaining;
                }
                var subscriptionIds = new ArrayOf<uint>(subscriptions
                    .Select(s => s.Id).ToArray());
                var response = await _session.TransferSubscriptionsAsync(null,
                    subscriptionIds, sendInitialValues, ct).ConfigureAwait(false);

                var responseHeader = response.ResponseHeader;
                if (!StatusCode.IsGood(responseHeader.ServiceResult))
                {
                    if (responseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
                    {
                        TransferSubscriptionsOnRecreate = false;
                        _logger.LogWarning("Transfer subscription unsupported, " +
                            "TransferSubscriptionsOnReconnect set to false.");
                    }
                    else
                    {
                        _logger.LogError(
                            "Transfer subscriptions failed with error {Error}.",
                            responseHeader.ServiceResult);
                    }
                    remaining.AddRange(subscriptions);
                    return remaining;
                }

                var transferResults = response.Results;
                var diagnosticInfos = response.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(transferResults, subscriptionIds);
                Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, subscriptionIds);
                for (var index = 0; index < subscriptions.Count; index++)
                {
                    if (transferResults[index].StatusCode == StatusCodes.BadNothingToDo)
                    {
                        _logger.LogDebug(
                            "Subscription {Id} is already member of the session.",
                            subscriptionIds[index]);
                        // Done
                    }
                    else if (!StatusCode.IsGood(transferResults[index].StatusCode))
                    {
                        _logger.LogError(
                            "Subscription {Id} failed to transfer, StatusCode={Status}",
                            subscriptionIds[index], transferResults[index].StatusCode);
                        remaining.Add(subscriptions[index]);
                    }
                    else
                    {
                        var success = await subscriptions[index].TryCompleteTransferAsync(
                            transferResults[index].AvailableSequenceNumbers.ToList(), ct).ConfigureAwait(false);
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
            lock (_subscriptionLock)
            {
                // find the subscription.
                foreach (var subscription in _subscriptions)
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
                    var desiredWorkerCount = GetDesiredPublishWorkerCount();
                    if (publishWorkers.Count > desiredWorkerCount)
                    {
                        // Too many workers, reduce
                        foreach (var worker in publishWorkers[desiredWorkerCount..])
                        {
                            _logger.LogInformation("Removing publish worker {Index}",
                                worker.Index);
                            await worker.DisposeAsync().ConfigureAwait(false);
                        }
                        publishWorkers = publishWorkers[..desiredWorkerCount];
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
                    var waiting = publishWorkers
                        .Select(w => w.Task)
                        .Prepend(_publishControl.WaitAsync(ct))
                        .ToArray();
                    await Task.WhenAny(waiting).ConfigureAwait(false);
                    PublishControlCycles++;
                    var index = 0;
                    foreach (var item in waiting.Skip(1)) // Skip wait handle
                    {
                        if (item.IsCompleted)
                        {
                            var worker = publishWorkers[index];
                            _logger.LogInformation("Publish worker {Index} exited",
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
            catch (OperationCanceledException) { }
            finally
            {
                // Controller exits, clean up all workers
                foreach (var worker in publishWorkers)
                {
                    await worker.DisposeAsync().ConfigureAwait(false);
                    PublishWorkerCount--;
                }
            }

            int GetDesiredPublishWorkerCount()
            {
                var publishCount = CreatedCount;
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
                _outer = outer;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(outer._cts.Token);
                _logger = _outer._loggerFactory.CreateLogger<PublishWorker>();
                Task = PublishWorkerAsync(_cts.Token);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _cts.CancelAsync().ConfigureAwait(false);
                    if (!Task.IsCompleted)
                    {
                        try
                        {
                            await Task.ConfigureAwait(false);
                        }
                        catch { } // Ignore
                    }
                }
                finally
                {
                    _cts.Dispose();
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
                var publishLatency = new Stopwatch();
                var timeoutHint = 0u;
                var moreNotifications = true; // Dont wait first time we enter the loop.
                _logger.LogInformation("PUBLISH Worker #{Handle} - STARTED.", Index);
                while (!ct.IsCancellationRequested)
                {
                    if (!_outer._running.IsSet)
                    {
                        _logger.LogInformation("PUBLISH Worker #{Handle} - PAUSED.", Index);
                        try
                        {
                            await _outer._running.WaitAsync(ct).ConfigureAwait(false);
                            _logger.LogInformation("PUBLISH Worker #{Handle} - RESUMED.",
                                Index);
                        }
                        catch (OperationCanceledException)
                        {
                            continue;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "PUBLISH Worker #{Handle} - " +
                                "Unexpected exception while waiting to connect.", Index);
                            break;
                        }
                    }
                    var ackWaitTimeout = CalculateTimeouts(
                        publishLatency.ElapsedMilliseconds, ref timeoutHint);
                    var acks = GetAcksReadyToSend();
                    var handle = Utils.IncrementIdentifier(ref _outer._publishRequestCounter);
                    try
                    {
                        if (acks.Count == 0 && !moreNotifications && ackWaitTimeout != 0)
                        {
                            // Throttle publishing as we wait for acks to arrive
                            acks = await WaitForAcksAsync(ackWaitTimeout, ct).ConfigureAwait(false);
                        }
                        publishLatency.Restart();
                        var response = await _outer._session.PublishAsync(new RequestHeader
                        {
                            TimeoutHint = timeoutHint,
                            ReturnDiagnostics = (uint)(int)_outer.ReturnDiagnostics,
                            RequestHandle = handle
                        }, acks, ct).ConfigureAwait(false);

                        moreNotifications = response.MoreNotifications;
                        var subscriptionId = response.SubscriptionId;
                        var notificationMessage = response.NotificationMessage;
                        var availableSequenceNumbers = response.AvailableSequenceNumbers;

                        var acknowledgeResults = response.Results;
                        var acknowledgeDiagnosticInfos = response.DiagnosticInfos;
                        Ua.ClientBase.ValidateResponse(acknowledgeResults, acks);
                        Ua.ClientBase.ValidateDiagnosticInfos(acknowledgeDiagnosticInfos, acks);
                        TooManyPublishRequests = false;

                        // Get the subscription with the provided identifier
                        var subscription = _outer.GetById(subscriptionId);
                        publishLatency.Stop();
                        if (subscription != null)
                        {
                            // deliver to subscription
                            await subscription.OnPublishReceivedAsync(notificationMessage,
                                availableSequenceNumbers.ToList(),
                                response.ResponseHeader.StringTable.ToList()).ConfigureAwait(false);
                            Interlocked.Increment(ref _outer._goodPublishRequestCount);
                        }
                        else if (!ct.IsCancellationRequested &&
                            !_outer._subscriptionHistory.Contains(subscriptionId))
                        {
                            // ignore messages with a subscription that was deleted
                            // Do not delete publish requests of stale subscriptions
                            _logger.LogInformation(
                                "PUBLISH Worker #{Handle}-{Id} - Received Publish Response " +
                                "for Unknown SubscriptionId={SubscriptionId}. Deleting...",
                                Index, handle, subscriptionId);
                            Interlocked.Increment(ref _outer._badPublishRequestCount);
                            await _outer._session.DeleteSubscriptionsAsync(null,
                                [subscriptionId], ct).ConfigureAwait(false);
                            moreNotifications = true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        // raise an error event.
                        publishLatency.Stop();
                        var error = new ServiceResult(e);

                        if (error.Code == StatusCodes.BadRequestInterrupted &&
                            ct.IsCancellationRequested)
                        {
                            break;
                        }

                        Interlocked.Increment(ref _outer._badPublishRequestCount);
                        // Rollback acks we collected
                        acks.ForEach(ack => _outer._acks.Writer.TryWrite(ack));

                        // ignore errors if paused.
                        if (!_outer._running.IsSet)
                        {
                            _logger.LogWarning("PUBLISH Worker #{Handle}-{Id} - Publish " +
                                "abandoned after error due to reconnect: {Message}",
                                Index, handle, e.Message);
                            continue;
                        }

                        // don't send another publish for these errors,
                        // or throttle to avoid server overload.
                        var statusCode = error.StatusCode;
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
                            _logger.LogDebug("PUBLISH Worker #{Handle}-{Id} - " +
                                "Server busy, throttling worker.",
                                Index, handle);
                            moreNotifications = false; // throttle
                        }
                        else if (statusCode == StatusCodes.BadTimeout ||
                            statusCode == StatusCodes.BadRequestTimeout)
                        {
                            // Timed out - retry with larger timeout
                            timeoutHint += 1000; // Increase by seconds
                            _logger.LogDebug("PUBLISH Worker #{Handle}-{Id} - " +
                                "Timed out, increasing timeout to {Timeout}.",
                                Index, handle, timeoutHint);
                            moreNotifications = true;
                        }
                        else
                        {
                            _logger.LogError(e, "PUBLISH Worker #{Handle}-{Id} - " +
                                "Unhandled error {Status} during Publish.",
                                Index, handle, error.StatusCode);
                        }
                    }
                }
                _logger.LogInformation("PUBLISH Worker #{Handle} - STOPPED.", Index);
            }

            /// <summary>
            /// Wait until acks arrive and return them
            /// </summary>
            /// <param name="maxWaitTime"></param>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task<ArrayOf<SubscriptionAcknowledgement>> WaitForAcksAsync(
                int maxWaitTime, CancellationToken ct)
            {
                Debug.Assert(maxWaitTime != 0, "Checked before entering");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var sw = Stopwatch.StartNew();
                if (maxWaitTime != Timeout.Infinite)
                {
#if FALSE
                    var workers = _outer.PublishWorkerCount;
                    if (workers == 0)
                    {
                        Debug.Fail("Must have at least this worker here.");
                        workers = 1;
                    }
                    maxWaitTime /= workers;
#endif
                    _logger.LogInformation(
                        "PUBLISH Worker #{Handle} - Waiting max {Time}ms for acks to arrive.",
                        Index, maxWaitTime);
                    cts.CancelAfter(maxWaitTime);
                }
                else
                {
                    _logger.LogDebug("PUBLISH Worker #{Handle} - Waiting for acks to arrive.",
                        Index);
                }
                try
                {
                    var firstAck = await _outer._acks.Reader.ReadAsync(
                        cts.Token).ConfigureAwait(false);
                    var restAcks = GetAcksReadyToSend();
                    var ackList = restAcks.ToList();
                    ackList.Insert(0, firstAck);
                    ArrayOf<SubscriptionAcknowledgement> acks = ackList;
                    _logger.LogInformation(
                        "PUBLISH Worker #{Handle} - Publish {Count} acks after pausing {Duration}.",
                        Index, acks.Count, sw.Elapsed);
                    return acks;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "PUBLISH Worker #{Handle} - Publish with no acks after waiting {Duration}.",
                        Index, sw.Elapsed);
                    return [];
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
                var available = _outer._acks.Reader.Count;
                var maxAcks = available / Math.Max(_outer.PublishWorkerCount, 1);
                for (var i = 0; i < maxAcks
                    && _outer._acks.Reader.TryRead(out var ack); i++)
                {
                    acks.Add(ack);
                }
                if (acks.Count != 0)
                {
                    _logger.LogDebug(
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
                var created = _outer.Created;
                if (created.Count == 0)
                {
                    return 0;
                }

                var timeout = TimeSpan.Zero;
                var minPublishInterval = Timeout.Infinite;
                foreach (var s in created)
                {
                    var publishingInterval = s.CurrentPublishingInterval;
                    var keepAlive = publishingInterval * s.CurrentKeepAliveCount;
                    if (timeout < keepAlive)
                    {
                        timeout = keepAlive;
                    }

                    var pi = (int)publishingInterval.TotalMilliseconds;
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
                timeout *= 2;
                if (timeout < kMinOperationTimeout)
                {
                    timeout = kMinOperationTimeout;
                }
                if (timeout > kMaxOperationTimeout)
                {
                    timeout = kMaxOperationTimeout;
                }
                var newTimeout = (uint)timeout.TotalMilliseconds;
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

            private readonly ILogger _logger;
            private readonly SubscriptionManager _outer;
            private readonly CancellationTokenSource _cts;
        }

        private static readonly TimeSpan kMaxOperationTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan kMinOperationTimeout = TimeSpan.FromSeconds(1);
        private const int kMaxSubscriptionHistory = 10;
        private uint _publishRequestCounter;
#pragma warning disable IDE0032 // Use auto property
        private int _badPublishRequestCount;
        private int _goodPublishRequestCount;
#pragma warning restore IDE0032 // Use auto property
        private readonly Channel<SubscriptionAcknowledgement> _acks;
        private readonly Nito.AsyncEx.AsyncManualResetEvent _running = new();
        private readonly Nito.AsyncEx.AsyncAutoResetEvent _publishControl = new();
        private readonly ConcurrentQueue<uint> _subscriptionHistory = new();
        private readonly Task _publishController;
        private readonly Lock _subscriptionLock = new();
        private readonly HashSet<IManagedSubscription> _subscriptions = [];
        private readonly CancellationTokenSource _cts = new();
        private readonly ISubscriptionManagerContext _session;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
    }
}
#endif
