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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    public partial class ServerBase
    {
        /// <summary>
        /// Asynchronously manages a queue of requests.
        /// </summary>
        protected class RequestQueue : IDisposable
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RequestQueue"/> class.
            /// </summary>
            /// <param name="server">The server.</param>
            /// <param name="minThreadCount">The minimum number of threads in the pool.</param>
            /// <param name="maxThreadCount">The maximum number of threads  in the pool.</param>
            /// <param name="maxRequestCount">The maximum number of requests that will placed in the queue.</param>
            /// <param name="decoupleHeldPublishRequests">
            /// When <c>true</c> (the default) a request that parks (for example a held
            /// <c>Publish</c> waiting for notifications) releases its worker at the park
            /// point instead of occupying it for the whole wait, so the worker/thread
            /// budget need not scale with the number of concurrently-held requests.
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

                var options = new System.Threading.Channels.BoundedChannelOptions(maxRequestCount)
                {
                    SingleWriter = false,
                    SingleReader = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                };

                m_queue = System.Threading.Channels.Channel.CreateBounded<QueuedRequest>(options);
                if (resourceIsolationProvider?.UseFairScheduling == true)
                {
                    m_fairQueue = new FairRequestQueue(
                        resourceIsolationProvider,
                        maxRequestCount,
                        maxRequestCost,
                        decoupleHeldPublishRequests,
                        CompleteRequest);
                }

                m_workers = [];
                m_cts = new CancellationTokenSource();
                m_activeThreadCount = 0;
                m_totalThreadCount = 0;
                m_stopped = false;

                ThreadPool.GetMinThreads(out minThreadCount, out int minCompletionPortThreads);

                ThreadPool.SetMinThreads(
                    Math.Max(minThreadCount, m_minThreadCount),
                    Math.Max(minCompletionPortThreads, m_minThreadCount)
                );

                ThreadPool.GetMaxThreads(out maxThreadCount, out int maxCompletionPortThreads);

                ThreadPool.SetMaxThreads(
                    Math.Max(maxThreadCount, m_maxThreadCount),
                    Math.Max(maxCompletionPortThreads, m_maxThreadCount)
                );

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

                lock (m_workerGate)
                {
                    serverStopped = m_stopped;
                    if (!serverStopped && m_fairQueue == null)
                    {
                        queueFull = !m_queue.Writer.TryWrite(new QueuedRequest(request, cancellationToken));
                        if (!queueFull &&
                            m_totalThreadCount < m_maxThreadCount &&
                            m_activeThreadCount >= m_totalThreadCount)
                        {
                            Interlocked.Increment(ref m_totalThreadCount);
                            m_workers.Add(Task.Run(() => WorkerLoopAsync(m_cts.Token), CancellationToken.None));
                        }
                    }
                }

                if (serverStopped)
                {
                    CompleteRequest(request, StatusCodes.BadServerHalted);
                    return;
                }

                if (m_fairQueue != null)
                {
                    try
                    {
                        if (!m_fairQueue.TryEnqueue(request, cancellationToken, out StatusCode error))
                        {
                            CompleteRequest(request, error);
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
                    CompleteRequest(request, StatusCodes.BadServerTooBusy);
                    // TODO: make a metric
                    m_server.m_logger.RequestQueueFull(m_activeThreadCount);
                }
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
                if (m_fairQueue?.Count > 0)
                {
                    lock (m_workerGate)
                    {
                        if (!m_stopped && m_totalThreadCount < m_maxThreadCount &&
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
                m_queue.Writer.TryComplete();
                m_fairQueue?.Dispose();
                while (m_queue.Reader.TryRead(out QueuedRequest request))
                {
                    CompleteRequest(request.Request, StatusCodes.BadServerHalted);
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
                Task? processing;
                lock (m_workerGate)
                {
                    processing = m_processingDrained?.Task;
                }
                if (processing != null)
                {
                    await processing.ConfigureAwait(false);
                }
                m_cts.Dispose();
            }

            /// <summary>
            /// Ran by the worker threads to process requests.
            /// </summary>
            /// <returns></returns>
            private async Task WorkerLoopAsync(CancellationToken ct)
            {
                try
                {
                    while (!ct.IsCancellationRequested && !Volatile.Read(ref m_stopped))
                    {
                        FairRequestQueue.Entry? entry = null;
                        QueuedRequest queued;
                        if (m_fairQueue != null)
                        {
                            try
                            {
                                entry = await m_fairQueue.DequeueAsync(ct).ConfigureAwait(false);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                m_server.m_logger.RequestQueueProcessingFailed(ex);
                                continue;
                            }
                            queued = new QueuedRequest(entry.Request, entry.CancellationToken);
                        }
                        else
                        {
                            queued = await m_queue.Reader.ReadAsync(ct).ConfigureAwait(false);
                        }
                        if (ct.IsCancellationRequested || Volatile.Read(ref m_stopped) ||
                            queued.CancellationToken.IsCancellationRequested)
                        {
                            entry?.Dispose();
                            CompleteRequest(
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
                catch (System.Threading.Channels.ChannelClosedException)
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
                lock (m_workerGate)
                {
                    if (m_processingCount++ == 0)
                    {
                        m_processingDrained =
                            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }
                try
                {
                    if (entry != null)
                    {
                        StatusCode status = entry.GetRevalidationStatus();
                        if (StatusCode.IsBad(status))
                        {
                            CompleteRequest(request, status);
                            return;
                        }
                    }
                    using CancellationTokenSource? linked = queued.CancellationToken.CanBeCanceled ?
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
                    lock (m_workerGate)
                    {
                        if (--m_processingCount == 0)
                        {
                            m_processingDrained!.TrySetResult(true);
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
                    request.OperationCompleted(null, statusCode);
                }
                catch (Exception ex)
                {
                    m_server.m_logger.RequestFaultDeliveryFailed(ex);
                }
            }

            /// <summary>
            /// Couples a retained request with its caller-controlled lifetime.
            /// </summary>
            private readonly record struct QueuedRequest(
                IEndpointIncomingRequest Request,
                CancellationToken CancellationToken);

            /// <summary>
            /// Server that dispatches admitted requests.
            /// </summary>
            private readonly ServerBase m_server;

            /// <summary>
            /// Number of workers started before any requests arrive.
            /// </summary>
            private readonly int m_minThreadCount;

            /// <summary>
            /// Maximum number of simultaneously retained worker tasks.
            /// </summary>
            private readonly int m_maxThreadCount;

            /// <summary>
            /// Whether parked requests release their execution workers.
            /// </summary>
            private readonly bool m_decoupleHeldPublishRequests;

            /// <summary>
            /// Compatibility FIFO used when fair scheduling is disabled.
            /// </summary>
            private readonly System.Threading.Channels.Channel<QueuedRequest> m_queue;
            // Ownership transfers to StopCoreAsync, which disposes these after asynchronous shutdown.
            // TODO: Remove the pragma when CA2213 recognizes deferred asynchronous ownership.
#pragma warning disable CA2213
            /// <summary>
            /// Fair admission queue owned by the asynchronous shutdown task after disposal begins.
            /// </summary>
            private readonly FairRequestQueue? m_fairQueue;

            /// <summary>
            /// Worker cancellation source retained until shutdown has drained all handlers.
            /// </summary>
            private readonly CancellationTokenSource m_cts;
#pragma warning restore CA2213
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
            /// Number of handlers that still retain request accounting.
            /// </summary>
            private int m_processingCount;

            /// <summary>
            /// Number of workers currently executing rather than parked or idle.
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
