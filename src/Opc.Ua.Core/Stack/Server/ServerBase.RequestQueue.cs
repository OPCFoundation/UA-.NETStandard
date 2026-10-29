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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    public partial class ServerBase
    {
        /// <summary>
        /// Limits how many requests execute at once and queues the requests that wait.
        /// </summary>
        /// <remarks>
        /// Without an isolation provider a request takes one of
        /// <c>maxThreadCount</c> permits and runs on the thread pool; the queue holds only
        /// the requests waiting for a permit, and a finished request hands its permit to
        /// the oldest of them. With an isolation provider, workers dequeue from the
        /// <see cref="FairRequestQueue"/>.
        /// </remarks>
        protected class RequestQueue : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestQueue"/> class.
            /// </summary>
            /// <param name="server">The server.</param>
            /// <param name="minThreadCount">
            /// The number of fair-queue workers started up front. Without an isolation
            /// provider no workers are started, and it only has to be positive.
            /// </param>
            /// <param name="maxThreadCount">
            /// The maximum number of requests executing at once.
            /// </param>
            /// <param name="maxRequestCount">The maximum number of requests that will placed in the queue.</param>
            /// <param name="decoupleHeldPublishRequests">
            /// When <c>true</c> (the default) a request that parks (for example a held
            /// <c>Publish</c> waiting for notifications) releases its permit or worker at the
            /// park point instead of occupying it for the whole wait, so the concurrency
            /// limit need not scale with the number of concurrently-held requests.
            /// </param>
            /// <param name="resourceIsolationProvider">
            /// The server-scoped isolation provider, or null for the SharedOnly compatibility path.
            /// </param>
            /// <param name="maxRequestCost">
            /// Positive maximum encoded request footprint charged to every retained decoded request.
            /// Required when isolation is enabled; this cost is not managed-heap accounting.
            /// </param>
            public RequestQueue(
                ServerBase server,
                int minThreadCount,
                int maxThreadCount,
                int maxRequestCount,
                bool decoupleHeldPublishRequests = true,
                IServerResourceIsolationProvider? resourceIsolationProvider = null,
                long maxRequestCost = 0)
            {
                m_server = server ?? throw new ArgumentNullException(nameof(server));
                if (minThreadCount <= 0 || maxThreadCount < minThreadCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(minThreadCount));
                }
                m_minThreadCount = minThreadCount;
                m_maxThreadCount = maxThreadCount;
                m_decoupleHeldPublishRequests = decoupleHeldPublishRequests;
                m_maxRequestCount = maxRequestCount;
                m_waiting = new Queue<QueuedRequest>();
                if (resourceIsolationProvider != null)
                {
                    m_fairQueue = new FairRequestQueue(
                        resourceIsolationProvider,
                        maxRequestCount,
                        maxRequestCost,
                        decoupleHeldPublishRequests,
                        RejectRequest,
                        GetEnqueueTimestamp,
                        request => (m_server.RequestParkingPolicy as IRequestControlPolicy)?
                            .IsControlRequest(request) == true);
                }

                m_meter = server.m_telemetry.CreateMeter();
                m_rejections = m_meter.CreateCounter<long>(
                    "opcua.server.request_queue.rejections",
                    description: "Requests the request queue completed without running them, by status code.");
                m_timeInQueue = m_meter.CreateHistogram<double>(
                    "opcua.server.request_queue.time_in_queue",
                    unit: "s",
                    description: "Time a request waited in the request queue before it was dequeued.");
                m_meter.CreateObservableGauge(
                    "opcua.server.request_queue.queued",
                    () => m_fairQueue?.Count ?? Volatile.Read(ref m_waitingCount),
                    description: "Requests waiting in the request queue.");
                m_meter.CreateObservableGauge(
                    "opcua.server.request_queue.executing",
                    () => Volatile.Read(ref m_activeThreadCount),
                    description: "Requests executing and holding a permit or worker.");
                m_meter.CreateObservableGauge(
                    "opcua.server.request_queue.parked",
                    () => Volatile.Read(ref m_parkedCount),
                    description: "Requests parked (for example held Publish requests) after releasing their permit or worker.");

                m_workers = [];
                m_cts = new CancellationTokenSource();
                m_stopToken = m_cts.Token;
                m_activeThreadCount = 0;
                m_totalThreadCount = 0;
                m_stopped = false;

                // The process-wide ThreadPool limits are left to the host: workers are
                // asynchronous and do not occupy a pool thread while they wait.
                if (m_fairQueue == null)
                {
                    return;
                }

                // Start worker tasks. Increment m_totalThreadCount before Task.Run so that
                // ScheduleIncomingRequest sees the correct worker count immediately and does
                // not spawn extra workers before the initial workers have started executing.
                CancellationToken token = m_cts.Token;
                for (int i = 0; i < m_minThreadCount; i++)
                {
                    Interlocked.Increment(ref m_totalThreadCount);
                    m_workers.Add(Task.Run(() => WorkerLoopAsync(token)));
                }
            }

            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Stops admissions, cancels queued and active work, and asynchronously waits
            /// for workers and parked handlers. Cancellation limits the caller's wait;
            /// uncooperative handlers keep their accounting until they actually finish.
            /// </summary>
            public async ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                Dispose();
                await m_stopTask!.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Schedules an incoming request.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <param name="cancellationToken">The caller's request lifetime.</param>
            /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
            public void ScheduleIncomingRequest(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }
                bool serverStopped;
                bool queueFull = false;

                if (m_fairQueue != null)
                {
                    // The fair queue rejects admissions itself once stopped, so the
                    // worker gate is not needed on this path.
                    serverStopped = Volatile.Read(ref m_stopped);
                }
                else
                {
                    var queued = new QueuedRequest(request, GetEnqueueTimestamp(), cancellationToken);
                    bool dispatch = false;
                    lock (m_workerGate)
                    {
                        serverStopped = m_stopped;
                        if (!serverStopped)
                        {
                            // A free permit goes to a waiting request first, so a request
                            // runs at once only when nothing is waiting.
                            if (m_waitingCount == 0 && m_activeThreadCount < m_maxThreadCount)
                            {
                                m_activeThreadCount++;
                                dispatch = true;
                            }
                            else if (m_waitingCount < m_maxRequestCount)
                            {
                                m_waiting.Enqueue(queued);
                                m_waitingCount++;
                            }
                            else
                            {
                                queueFull = true;
                            }
                        }
                    }
                    if (dispatch)
                    {
                        Dispatch(queued);
                    }
                }

                if (serverStopped)
                {
                    RejectRequest(request, StatusCodes.BadServerHalted);
                    return;
                }

                if (m_fairQueue != null)
                {
                    try
                    {
                        if (!m_fairQueue.TryEnqueue(request, cancellationToken, out StatusCode error))
                        {
                            RejectRequest(request, error);
                        }
                        else
                        {
                            GrowFairWorkers();
                        }
                    }
                    catch (Exception ex)
                    {
                        m_server.m_logger.RequestQueueProcessingFailed(ex);
                        CompleteRequest(request, StatusCodes.BadInternalError);
                    }
                    return;
                }
                if (queueFull)
                {
                    RejectRequest(request, StatusCodes.BadServerTooBusy);
                    m_server.m_logger.RequestQueueFull(m_activeThreadCount);
                }
            }

            /// <summary>
            /// Takes the enqueue timestamp for the time-in-queue histogram, or 0 when
            /// nothing listens to it.
            /// </summary>
            private long GetEnqueueTimestamp()
            {
                return m_timeInQueue.Enabled ? Stopwatch.GetTimestamp() : 0;
            }

            /// <summary>
            /// Records how long a dequeued request waited, if its enqueue time was taken.
            /// </summary>
            private void RecordTimeInQueue(long enqueueTimestamp)
            {
                if (enqueueTimestamp != 0 && m_timeInQueue.Enabled)
                {
                    m_timeInQueue.Record(
                        (double)(Stopwatch.GetTimestamp() - enqueueTimestamp) / Stopwatch.Frequency);
                }
            }

            /// <summary>
            /// Runs a request that holds a permit on the thread pool, so that the caller,
            /// usually a transport receive loop, does not run the handler.
            /// </summary>
            private void Dispatch(QueuedRequest queued)
            {
                var item = new DispatchItem(this, queued);
#if NETCOREAPP3_0_OR_GREATER
                ThreadPool.UnsafeQueueUserWorkItem(item, preferLocal: false);
#else
                ThreadPool.UnsafeQueueUserWorkItem(static state => ((DispatchItem)state!).Execute(), item);
#endif
            }

            /// <summary>
            /// Runs requests while this permit is handed from one request to the next
            /// waiting one, then returns the permit.
            /// </summary>
            /// <remarks>
            /// Never throws: <see cref="ProcessRequestSafeAsync"/> completes every request.
            /// </remarks>
            private async Task RunWithPermitAsync(QueuedRequest queued)
            {
                while (true)
                {
                    RecordTimeInQueue(queued.EnqueueTimestamp);
                    CancellationToken ct = m_stopToken;
                    if (ct.IsCancellationRequested || Volatile.Read(ref m_stopped) ||
                        queued.CancellationToken.IsCancellationRequested)
                    {
                        RejectRequest(
                            queued.Request,
                            ct.IsCancellationRequested || Volatile.Read(ref m_stopped) ?
                                StatusCodes.BadServerHalted : StatusCodes.BadRequestCancelledByClient);
                    }
                    else if (RequestQueueBudget.HasExpired(queued.Request))
                    {
                        // the client gave up while the request was queued
                        CompleteRequest(queued.Request, StatusCodes.BadTimeout);
                    }
                    else if (m_decoupleHeldPublishRequests &&
                        queued.Request is IParkableIncomingRequest { ParkSink: RequestParkSink parkSink })
                    {
                        // A request that parks keeps running detached; the permit is
                        // returned at the park point.
                        Task processing = ProcessRequestSafeAsync(queued, null, ct);
                        if (!processing.IsCompleted)
                        {
                            await Task.WhenAny(processing, parkSink.ParkedTask).ConfigureAwait(false);
                            CountWhileParked(processing);
                        }
                    }
                    else
                    {
                        await ProcessRequestSafeAsync(queued, null, ct).ConfigureAwait(false);
                    }

                    if (!TryHandOffPermit(out queued))
                    {
                        return;
                    }
                }
            }

            /// <summary>
            /// Hands the permit of a finished or parked request to the oldest waiting
            /// request, or returns it when none is waiting or the queue has stopped.
            /// </summary>
            private bool TryHandOffPermit(out QueuedRequest next)
            {
                lock (m_workerGate)
                {
                    if (!m_stopped && m_waitingCount > 0)
                    {
                        next = m_waiting.Dequeue();
                        m_waitingCount--;
                        return true;
                    }
                    m_activeThreadCount--;
                    if (m_stopped && m_activeThreadCount == 0 && Volatile.Read(ref m_processingCount) == 0)
                    {
                        m_processingDrained?.TrySetResult(true);
                    }
                }
                next = default;
                return false;
            }

            /// <summary>
            /// Starts shared asynchronous disposal without blocking the caller.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (m_workerGate)
                    {
                        if (m_disposed)
                        {
                            return;
                        }
                        m_disposed = true;
                        m_stopped = true;
                        Task[] workers = [.. m_workers];
                        m_stopTask = Task.Run(() => StopCoreAsync(workers));
                    }
                }
            }

            /// <summary>
            /// Adds one worker when queued work outlives all available workers. Dispatch
            /// also checks demand so a burst admitted before workers start cannot be stranded.
            /// </summary>
            private void GrowFairWorkers()
            {
                // The counters are re-checked under the gate; the unlocked pre-check keeps the
                // gate off the per-request path while a worker is idle or the pool is full.
                if (m_fairQueue?.Count > 0 &&
                    Volatile.Read(ref m_totalThreadCount) < m_maxThreadCount &&
                    Volatile.Read(ref m_activeThreadCount) >= Volatile.Read(ref m_totalThreadCount))
                {
                    lock (m_workerGate)
                    {
                        if (!m_stopped &&
                            m_totalThreadCount < m_maxThreadCount &&
                            m_activeThreadCount >= m_totalThreadCount)
                        {
                            Interlocked.Increment(ref m_totalThreadCount);
                            m_workers.Add(Task.Run(() => WorkerLoopAsync(m_cts.Token), CancellationToken.None));
                        }
                    }
                }
            }

            /// <summary>
            /// Cancels admission and handlers, drains all work, then disposes queue-owned resources.
            /// </summary>
            private async Task StopCoreAsync(Task[] workers)
            {
                Task cancellation = m_cts.CancelAsync();
                m_fairQueue?.Dispose();
                QueuedRequest[] waiting;
                lock (m_workerGate)
                {
                    // Admission and hand-off check m_stopped under the gate, so nothing
                    // enters the waiting queue after this.
                    waiting = [.. m_waiting];
                    m_waiting.Clear();
                    m_waitingCount = 0;
                }
                foreach (QueuedRequest request in waiting)
                {
                    RejectRequest(request.Request, StatusCodes.BadServerHalted);
                }
                try
                {
                    await cancellation.ConfigureAwait(false);
                }
                catch (AggregateException ex)
                {
                    m_server.m_logger.RequestQueueProcessingFailed(ex);
                }
                await Task.WhenAll(workers).ConfigureAwait(false);
                Task processing;
                lock (m_workerGate)
                {
                    // Parked handlers may still finish after the workers, and requests that
                    // hold a permit may not have started yet; the last handler or permit to
                    // finish completes the drain, unless none is left already.
                    m_processingDrained =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (Volatile.Read(ref m_processingCount) == 0 && m_activeThreadCount == 0)
                    {
                        m_processingDrained.TrySetResult(true);
                    }
                    processing = m_processingDrained.Task;
                }
                await processing.ConfigureAwait(false);
                m_cts.Dispose();
                m_meter.Dispose();
            }

            /// <summary>
            /// Ran by the fair-queue workers to process requests.
            /// </summary>
            /// <returns></returns>
            private async Task WorkerLoopAsync(CancellationToken ct)
            {
                // Stopping disposes the fair queue, which ends every parked wait, so a parked
                // worker does not register on the shared stop token every time it parks.
                FairRequestQueue fairQueue = m_fairQueue!;
                WorkerWakeSignal.Waiter waiter = fairQueue.CreateWaiter();
                try
                {
                    while (!ct.IsCancellationRequested && !Volatile.Read(ref m_stopped))
                    {
                        FairRequestQueue.Entry entry;
                        try
                        {
                            entry = await fairQueue.DequeueAsync(waiter, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            m_server.m_logger.RequestQueueProcessingFailed(ex);
                            continue;
                        }
                        var queued = new QueuedRequest(
                            entry.Request, entry.EnqueueTimestamp, entry.CancellationToken);
                        RecordTimeInQueue(queued.EnqueueTimestamp);
                        if (ct.IsCancellationRequested || Volatile.Read(ref m_stopped) ||
                            queued.CancellationToken.IsCancellationRequested)
                        {
                            entry.Dispose();
                            RejectRequest(
                                queued.Request,
                                ct.IsCancellationRequested || Volatile.Read(ref m_stopped) ?
                                    StatusCodes.BadServerHalted : StatusCodes.BadRequestCancelledByClient);
                            continue;
                        }
                        if (m_decoupleHeldPublishRequests &&
                            queued.Request is IParkableIncomingRequest { ParkSink: RequestParkSink parkSink })
                        {
                            await ProcessWithParkAsync(queued, parkSink, entry, ct).ConfigureAwait(false);
                        }
                        else
                        {
                            Interlocked.Increment(ref m_activeThreadCount);
                            GrowFairWorkers();
                            try
                            {
                                await ProcessRequestSafeAsync(queued, entry, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                Interlocked.Decrement(ref m_activeThreadCount);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                }
                finally
                {
                    Interlocked.Decrement(ref m_totalThreadCount);
                }
            }

            /// <summary>
            /// Processes a request, releasing the worker as soon as the request either
            /// completes or parks (whichever comes first). The active-worker counter is
            /// held only until the park point, so parked requests do not consume a
            /// worker slot for the duration of their wait.
            /// </summary>
            private async Task ProcessWithParkAsync(
                QueuedRequest request,
                RequestParkSink parkSink,
                FairRequestQueue.Entry? entry,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_activeThreadCount);
                GrowFairWorkers();

                // ProcessRequestSafeAsync never throws and always completes the request,
                // so the detached continuation for a parked request is fault-safe.
                Task processing = ProcessRequestSafeAsync(request, entry, ct);

                try
                {
                    if (!processing.IsCompleted)
                    {
                        await Task.WhenAny(processing, parkSink.ParkedTask).ConfigureAwait(false);
                        CountWhileParked(processing);
                    }
                }
                finally
                {
                    entry?.ReleaseExecution();
                    Interlocked.Decrement(ref m_activeThreadCount);
                }

                // If the request parked it completes independently; the response is
                // delivered via the request's value-task source. Nothing further is
                // required here because ProcessRequestSafeAsync observes all faults.
            }

            /// <summary>
            /// Counts a request that released its permit or worker at the park point as
            /// parked until its handler finishes.
            /// </summary>
            private void CountWhileParked(Task processing)
            {
                if (processing.IsCompleted)
                {
                    return;
                }
                Interlocked.Increment(ref m_parkedCount);
                // ProcessRequestSafeAsync never throws, so the continuation only counts.
                _ = processing.ContinueWith(
                    static (_, state) => Interlocked.Decrement(ref ((RequestQueue)state!).m_parkedCount),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            /// <summary>
            /// Invokes the server request handler and traps any unexpected error the same
            /// way the legacy inline path did, guaranteeing the request is always faulted
            /// to the client and that the returned task never throws.
            /// </summary>
            private async Task ProcessRequestSafeAsync(
                QueuedRequest queued,
                FairRequestQueue.Entry? entry,
                CancellationToken ct)
            {
                IEndpointIncomingRequest request = queued.Request;
                Interlocked.Increment(ref m_processingCount);
                try
                {
                    if (entry != null)
                    {
                        StatusCode status = entry.GetRevalidationStatus();
                        if (StatusCode.IsBad(status))
                        {
                            RejectRequest(request, status);
                            return;
                        }
                        if (request is IResourceRetainingRequest retaining)
                        {
                            retaining.SetResponseResourceTransfer(entry.TransferResponseCost);
                        }
                    }
                    // An endpoint request links the token it was scheduled with into its own
                    // request lifetime; linking it here as well would cost a second token source.
                    using CancellationTokenSource? linked = queued.CancellationToken.CanBeCanceled &&
                        !(request is ILinksScheduledCancellation scheduled &&
                            scheduled.ScheduledCancellationToken == queued.CancellationToken) ?
                        CancellationTokenSource.CreateLinkedTokenSource(ct, queued.CancellationToken) : null;
                    await m_server.ProcessRequestAsync(request, linked?.Token ?? ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested ||
                    queued.CancellationToken.IsCancellationRequested)
                {
                    CompleteRequest(
                        request,
                        ct.IsCancellationRequested ?
                            StatusCodes.BadServerHalted : StatusCodes.BadRequestCancelledByClient);
                }
                catch (Exception ex)
                {
                    m_server.m_logger.RequestQueueProcessingFailed(ex);
                    CompleteRequest(request, StatusCodes.BadInternalError);
                }
                finally
                {
                    entry?.Dispose();
                    // Only shutdown waits for the drain, and it starts once every worker
                    // has exited, so the gate is needed only after the stop. A permit that
                    // is still held completes the drain when it is returned.
                    if (Interlocked.Decrement(ref m_processingCount) == 0 && Volatile.Read(ref m_stopped))
                    {
                        lock (m_workerGate)
                        {
                            if (m_activeThreadCount == 0)
                            {
                                m_processingDrained?.TrySetResult(true);
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// Reports a terminal queue outcome without allowing a completion callback to stop a worker.
            /// </summary>
            private void CompleteRequest(IEndpointIncomingRequest request, StatusCode statusCode)
            {
                try
                {
                    // a queue-full rejection tells the client when to retry
                    if (statusCode == StatusCodes.BadServerTooBusy &&
                        request is IQueueAwareIncomingRequest queueAware)
                    {
                        queueAware.OperationFailed(RequestQueueBudget.CreateQueueFullException());
                    }
                    else
                    {
                        request.OperationCompleted(null, statusCode);
                    }
                }
                catch (Exception ex)
                {
                    m_server.m_logger.RequestFaultDeliveryFailed(ex);
                }
            }

            /// <summary>
            /// Counts a request the queue completes without running it, then completes it.
            /// </summary>
            private void RejectRequest(IEndpointIncomingRequest request, StatusCode statusCode)
            {
                if (m_rejections.Enabled)
                {
                    // Symbolic ids are interned, so the tag allocates nothing per rejection.
                    string? status = statusCode.SymbolicId ??
                        (StatusCode.TryGetInternedStatusCode(statusCode.Code, out StatusCode interned) ?
                            interned.SymbolicId : null);
                    m_rejections.Add(1, new KeyValuePair<string, object?>("status", status ?? "Bad"));
                }
                CompleteRequest(request, statusCode);
            }

            /// <summary>
            /// Couples a retained request with its caller-controlled lifetime and the
            /// <see cref="Stopwatch"/> time it was queued (0 when not measured).
            /// </summary>
            private readonly record struct QueuedRequest(
                IEndpointIncomingRequest Request,
                long EnqueueTimestamp,
                CancellationToken CancellationToken);

            /// <summary>
            /// Thread pool work item that runs a request holding a permit.
            /// </summary>
            private sealed class DispatchItem(RequestQueue queue, QueuedRequest queued)
#if NETCOREAPP3_0_OR_GREATER
                : IThreadPoolWorkItem
#endif
            {
                /// <summary>
                /// Runs the request; the returned task never faults.
                /// </summary>
                public void Execute()
                {
                    _ = queue.RunWithPermitAsync(queued);
                }
            }

            /// <summary>
            /// Server that dispatches admitted requests.
            /// </summary>
            private readonly ServerBase m_server;

            /// <summary>
            /// Number of fair-queue workers started before any requests arrive.
            /// </summary>
            private readonly int m_minThreadCount;

            /// <summary>
            /// Maximum number of requests executing at once: the permits of the shared
            /// queue, or the retained worker tasks of the fair queue.
            /// </summary>
            private readonly int m_maxThreadCount;

            /// <summary>
            /// Maximum number of requests waiting for a permit.
            /// </summary>
            private readonly int m_maxRequestCount;

            /// <summary>
            /// Whether parked requests release their permit or worker.
            /// </summary>
            private readonly bool m_decoupleHeldPublishRequests;

            /// <summary>
            /// Requests waiting for a permit when no isolation provider is installed,
            /// guarded by <see cref="m_workerGate"/>.
            /// </summary>
            private readonly Queue<QueuedRequest> m_waiting;

            /// <summary>
            /// Number of requests in <see cref="m_waiting"/>, changed under
            /// <see cref="m_workerGate"/> and read without it by the queued gauge.
            /// </summary>
            private int m_waitingCount;

            /// <summary>
            /// Token of <see cref="m_cts"/>, read without touching the source, which
            /// shutdown disposes once every permit and handler has finished.
            /// </summary>
            private readonly CancellationToken m_stopToken;
            // Ownership transfers to StopCoreAsync, which disposes these after asynchronous shutdown.
            // TODO: Remove the pragma when CA2213 recognizes deferred asynchronous ownership.
#pragma warning disable CA2213
            /// <summary>
            /// Provider-accounted queue owned by the asynchronous shutdown task after disposal begins.
            /// </summary>
            private readonly FairRequestQueue? m_fairQueue;

            /// <summary>
            /// Worker cancellation source retained until shutdown has drained all handlers.
            /// </summary>
            private readonly CancellationTokenSource m_cts;

            /// <summary>
            /// Request queue instruments, disposed once shutdown has drained all handlers.
            /// </summary>
            private readonly Meter m_meter;
#pragma warning restore CA2213

            /// <summary>
            /// Requests completed by the queue without running, tagged by status code.
            /// </summary>
            private readonly Counter<long> m_rejections;

            /// <summary>
            /// Time from enqueue to dequeue, in seconds.
            /// </summary>
            private readonly Histogram<double> m_timeInQueue;

            /// <summary>
            /// Worker tasks joined by asynchronous shutdown.
            /// </summary>
            private readonly List<Task> m_workers;

            /// <summary>
            /// Protects worker growth and shutdown bookkeeping.
            /// </summary>
            private readonly Lock m_workerGate = new();

            /// <summary>
            /// Completion of all active and detached parked handlers.
            /// </summary>
            private TaskCompletionSource<bool>? m_processingDrained;

            /// <summary>
            /// Shared asynchronous shutdown operation.
            /// </summary>
            private Task? m_stopTask;

            /// <summary>
            /// Number of handlers that still retain request accounting, changed with
            /// <see cref="Interlocked"/>.
            /// </summary>
            private int m_processingCount;

            /// <summary>
            /// Number of handlers that parked and released their permit or worker,
            /// changed with <see cref="Interlocked"/>.
            /// </summary>
            private int m_parkedCount;

            /// <summary>
            /// Number of requests executing rather than parked or waiting: the permits in
            /// use, changed under <see cref="m_workerGate"/>, or the busy fair-queue
            /// workers, changed with <see cref="Interlocked"/>.
            /// </summary>
            private int m_activeThreadCount;

            /// <summary>
            /// Number of workers started but not yet exited.
            /// </summary>
            private int m_totalThreadCount;

            /// <summary>
            /// Whether admissions and worker growth have stopped.
            /// </summary>
            private bool m_stopped;

            /// <summary>
            /// Whether disposal has already scheduled the shared shutdown operation.
            /// </summary>
            private bool m_disposed;
        }
    }
}
