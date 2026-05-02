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
            public RequestQueue(
                ServerBase server,
                int minThreadCount,
                int maxThreadCount,
                int maxRequestCount)
            {
                m_server = server;
                m_minThreadCount = minThreadCount;
                m_maxThreadCount = maxThreadCount;

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
                    m_server.m_logger.LogDebug("Too many operations. Active threads: {Count}", m_activeThreadCount);
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
                            try
                            {
                                Interlocked.Increment(ref m_activeThreadCount);
                                await m_server.ProcessRequestAsync(request, ct)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                m_server.m_logger.LogError(ex, "Unexpected error processing incoming request.");
                                request.OperationCompleted(null, StatusCodes.BadInternalError);
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

            private readonly ServerBase m_server;
            private readonly int m_minThreadCount;
            private readonly int m_maxThreadCount;
            private readonly System.Threading.Channels.Channel<IEndpointIncomingRequest> m_queue;
            private readonly List<Task> m_workers;
            private readonly CancellationTokenSource m_cts;
            private int m_activeThreadCount;
            private int m_totalThreadCount;
            private bool m_stopped;
        }
    }
}
