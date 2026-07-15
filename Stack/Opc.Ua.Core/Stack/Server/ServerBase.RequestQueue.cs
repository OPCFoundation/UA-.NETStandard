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
            public RequestQueue(
                ServerBase server,
                int minThreadCount,
                int maxThreadCount,
                int maxRequestCount,
                bool decoupleHeldPublishRequests = true)
            {
                m_server = server;
                m_minThreadCount = minThreadCount;
                m_maxThreadCount = maxThreadCount;
                m_decoupleHeldPublishRequests = decoupleHeldPublishRequests;

                var options = new System.Threading.Channels.BoundedChannelOptions(maxRequestCount)
                {
                    SingleWriter = false,
                    SingleReader = false,
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait
                };

                m_queue = System.Threading.Channels.Channel.CreateBounded<IEndpointIncomingRequest>(options);

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
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_stopped = true;
                    m_cts.Cancel();

                    m_queue.Writer.Complete();

                    // drain any remaining requests from the queue
                    while (m_queue.Reader.TryRead(out IEndpointIncomingRequest? request))
                    {
                        request.OperationCompleted(null, StatusCodes.BadServerHalted);
                    }

                    // Wait for all worker threads to complete
                    Task[] workerTasks;
                    lock (m_workers)
                    {
                        workerTasks = [.. m_workers];
                    }

                    try
                    {
                        Task.WaitAll(workerTasks, TimeSpan.FromSeconds(5));
                    }
                    catch (AggregateException)
                    {
                        // Ignore exceptions during shutdown
                    }

                    m_cts.Dispose();
                }
            }

            /// <summary>
            /// Schedules an incoming request.
            /// </summary>
            /// <param name="request">The request.</param>
            public void ScheduleIncomingRequest(IEndpointIncomingRequest request)
            {
                // check if server is stopped
                if (m_stopped)
                {
                    request.OperationCompleted(null, StatusCodes.BadServerHalted);
                    return;
                }

                // Enqueue requests. Use TryWrite to fail immediately if limit is reached.
                if (!m_queue.Writer.TryWrite(request))
                {
                    request.OperationCompleted(null, StatusCodes.BadServerTooBusy);
                    // TODO: make a metric
                    m_server.m_logger.ServerBaseLogMessage9(m_activeThreadCount);
                    return;
                }

                // Optionally scale up workers if needed. Increment m_totalThreadCount
                // inside the lock and before Task.Run so that concurrent callers see the
                // updated count immediately and do not spawn duplicate workers.
                if (m_totalThreadCount < m_maxThreadCount &&
                    m_activeThreadCount >= m_totalThreadCount)
                {
                    lock (m_workers)
                    {
                        // Re-check inside the lock to prevent double-spawning.
                        if (m_totalThreadCount < m_maxThreadCount)
                        {
                            Interlocked.Increment(ref m_totalThreadCount);
                            m_workers.Add(Task.Run(() => WorkerLoopAsync(m_cts.Token)));
                        }
                    }
                }
            }

            /// <summary>
            /// Ran by the worker threads to process requests.
            /// </summary>
            /// <returns></returns>
            private async Task WorkerLoopAsync(CancellationToken ct)
            {
                try
                {
                    while (await m_queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        while (m_queue.Reader.TryRead(out IEndpointIncomingRequest? request))
                        {
                            // A request that parks (e.g. a held Publish waiting for
                            // notifications) releases the worker at the park point so a
                            // small worker pool can hold many outstanding requests. The
                            // response is still delivered out-of-band by the request
                            // itself, so the worker does not need to await completion.
                            // Requests that cannot park (ParkSink is null) use the legacy
                            // inline path with no additional per-request overhead.
                            if (m_decoupleHeldPublishRequests &&
                                request is IParkableIncomingRequest parkable &&
                                parkable.ParkSink is RequestParkSink parkSink)
                            {
                                await ProcessWithParkAsync(request, parkSink, ct)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                Interlocked.Increment(ref m_activeThreadCount);
                                try
                                {
                                    await ProcessRequestSafeAsync(request, ct).ConfigureAwait(false);
                                }
                                finally
                                {
                                    Interlocked.Decrement(ref m_activeThreadCount);
                                }
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
                IEndpointIncomingRequest request,
                RequestParkSink parkSink,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_activeThreadCount);

                // ProcessRequestSafeAsync never throws and always completes the request,
                // so the detached continuation for a parked request is fault-safe.
                Task processing = ProcessRequestSafeAsync(request, ct);

                try
                {
                    if (!processing.IsCompleted)
                    {
                        await Task.WhenAny(processing, parkSink.ParkedTask).ConfigureAwait(false);
                    }
                }
                finally
                {
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
                IEndpointIncomingRequest request,
                CancellationToken ct)
            {
                try
                {
                    await m_server.ProcessRequestAsync(request, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_server.m_logger.ServerBaseLogMessage10(ex);
                    try
                    {
                        request.OperationCompleted(null, StatusCodes.BadInternalError);
                    }
                    catch (Exception completeError)
                    {
                        m_server.m_logger.ServerBaseLogMessage11(completeError);
                    }
                }
            }

            private readonly ServerBase m_server;
            private readonly int m_minThreadCount;
            private readonly int m_maxThreadCount;
            private readonly bool m_decoupleHeldPublishRequests;
            private readonly System.Threading.Channels.Channel<IEndpointIncomingRequest> m_queue;
            private readonly List<Task> m_workers;
            private readonly CancellationTokenSource m_cts;
            private int m_activeThreadCount;
            private int m_totalThreadCount;
            private bool m_stopped;
        }
    }
}
