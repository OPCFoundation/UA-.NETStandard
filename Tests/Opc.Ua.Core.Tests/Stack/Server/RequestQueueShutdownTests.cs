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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests that stopping the request queue completes every request exactly once (#4729).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public class RequestQueueShutdownTests
    {
        [Test]
        public async Task DisposeCompletesQueuedRequestsOnceAsync()
        {
            using var server = new QueueTestServer();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            server.Processing = async request =>
            {
                started.TrySetResult(true);
                await gate.Task.ConfigureAwait(false);
                request.OperationCompleted(new ReadResponse(), ServiceResult.Good);
            };

            CountingRequest[] requests = [.. Enumerable.Range(0, 10).Select(_ => new CountingRequest())];
            using (QueueTestServer.RequestQueueHandle queue = server.CreateQueue(1, 1, 100))
            {
                Schedule(queue, requests[0]);
                await started.Task.ConfigureAwait(false);
                foreach (CountingRequest request in requests.Skip(1))
                {
                    Schedule(queue, request);
                }
            }

            // Requests still queued are halted; one picked up by an extra worker stays blocked on the gate.
            Assert.That(
                requests.Skip(1).Count(r => r.Completions == 1 && r.LastError == StatusCodes.BadServerHalted),
                Is.GreaterThan(0));
            gate.SetResult(true);
            await WaitForCompletionAsync(requests).ConfigureAwait(false);

            Assert.That(requests.Select(r => r.Completions), Is.All.EqualTo(1));
        }

        [Test]
        public void ScheduleAfterDisposeCompletesOnce()
        {
            using var server = new QueueTestServer();
            QueueTestServer.RequestQueueHandle queue = server.CreateQueue(1, 1, 100);
            queue.Dispose();

            var request = new CountingRequest();
            Schedule(queue, request);

            Assert.That(request.Completions, Is.EqualTo(1));
            Assert.That(request.LastError, Is.EqualTo((StatusCode)StatusCodes.BadServerHalted));
        }

        [Test]
        public async Task ConcurrentScheduleAndDisposeCompletesEachRequestOnceAsync()
        {
            using var server = new QueueTestServer();
            server.Processing = async request =>
            {
                await Task.Yield();
                request.OperationCompleted(new ReadResponse(), ServiceResult.Good);
            };

            for (int iteration = 0; iteration < 50; iteration++)
            {
                var requests = new List<CountingRequest>();
                QueueTestServer.RequestQueueHandle queue = server.CreateQueue(2, 4, 10_000);
                using var start = new ManualResetEventSlim();
                Task[] producers = [.. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < 50; i++)
                    {
                        var request = new CountingRequest();
                        lock (requests)
                        {
                            requests.Add(request);
                        }
                        Schedule(queue, request);
                    }
                }))];
                start.Set();
                await Task.Delay(1).ConfigureAwait(false);
                queue.Dispose();
                await Task.WhenAll(producers).ConfigureAwait(false);

                await WaitForCompletionAsync(requests).ConfigureAwait(false);
                // give any late duplicate completion time to show up
                await Task.Delay(20).ConfigureAwait(false);
                Assert.That(requests.Where(r => r.Completions != 1), Is.Empty, $"Iteration {iteration}");
            }
        }

        /// <summary>
        /// Mirrors EndpointIncomingRequest.ProcessAsync, which completes the request
        /// with a fault when scheduling throws.
        /// </summary>
        private static void Schedule(QueueTestServer.RequestQueueHandle queue, CountingRequest request)
        {
            try
            {
                queue.ScheduleIncomingRequest(request);
            }
            catch (Exception)
            {
                request.OperationCompleted(null, StatusCodes.BadUnexpectedError);
            }
        }

        private static async Task WaitForCompletionAsync(IReadOnlyCollection<CountingRequest> requests)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (requests.Any(r => r.Completions == 0))
            {
                await Task.Delay(5, cts.Token).ConfigureAwait(false);
            }
        }

        private sealed class CountingRequest : IEndpointIncomingRequest
        {
            private int m_completions;

            public int Completions => Volatile.Read(ref m_completions);

            public StatusCode LastError { get; private set; }

            public IServiceRequest Request { get; } = new ReadRequest { RequestHeader = new RequestHeader() };

            public SecureChannelContext SecureChannelContext => null;

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                LastError = error?.StatusCode ?? StatusCodes.Good;
                Interlocked.Increment(ref m_completions);
            }
        }

        private sealed class QueueTestServer : ServerBase
        {
            public Func<IEndpointIncomingRequest, Task> Processing { get; set; }

            public RequestQueueHandle CreateQueue(int minThreads, int maxThreads, int maxRequests)
            {
                var queue = new RequestQueue(this, minThreads, maxThreads, maxRequests);
                return new RequestQueueHandle(queue.ScheduleIncomingRequest, queue);
            }

            protected override Task ProcessRequestAsync(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                return Processing(request);
            }

            /// <summary>
            /// Exposes the protected queue type to the test.
            /// </summary>
            public sealed class RequestQueueHandle : IDisposable
            {
                private readonly Action<IEndpointIncomingRequest> m_schedule;
                private readonly IDisposable m_queue;

                public RequestQueueHandle(Action<IEndpointIncomingRequest> schedule, IDisposable queue)
                {
                    m_schedule = schedule;
                    m_queue = queue;
                }

                public void ScheduleIncomingRequest(IEndpointIncomingRequest request)
                {
                    m_schedule(request);
                }

                public void Dispose()
                {
                    m_queue.Dispose();
                }
            }
        }
    }
}
