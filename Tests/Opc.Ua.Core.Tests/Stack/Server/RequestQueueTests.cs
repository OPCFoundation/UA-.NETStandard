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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RequestQueueTests
    {
        private sealed class TestEndpointIncomingRequest : IEndpointIncomingRequest
        {
            public TaskCompletionSource<bool> ProcessingStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ProcessingCompleted { get; } = new TaskCompletionSource<bool>();
            public StatusCode? CompletedStatusCode { get; private set; }

            public IServiceRequest Request => null;

            public SecureChannelContext SecureChannelContext => null;

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                ProcessingStarted.TrySetResult(true);
                return new ValueTask(ProcessingCompleted.Task);
            }

            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                CompletedStatusCode = error?.StatusCode;
                ProcessingCompleted.TrySetResult(true);
            }
        }

        private sealed class TestServer : ServerBase
        {
            public TestServer()
                : base(NUnitTelemetryContext.Create(true))
            {
            }

            public void ReplaceRequestQueue(int minThreads, int maxThreads, int maxQueue)
            {
                FieldInfo field = typeof(ServerBase).GetField(
                    "m_requestQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var oldQueue = field.GetValue(this) as IDisposable;
                oldQueue?.Dispose();

                var newQueue = new RequestQueue(this, minThreads, maxThreads, maxQueue);
                field.SetValue(this, newQueue);
            }

            protected override Task ProcessRequestAsync(IEndpointIncomingRequest request, CancellationToken cancellationToken = default)
            {
                return request.CallAsync(cancellationToken).AsTask();
            }
        }

        [Test]
        public async Task TestRequestProcessingAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(2, 4, 10);

            var request1 = new TestEndpointIncomingRequest();
            server.ScheduleIncomingRequest(request1);

            using var cts = new CancellationTokenSource(5000);
            Task<bool> t1 = request1.ProcessingStarted.Task;
            if (await Task.WhenAny(t1, Task.Delay(5000, cts.Token)).ConfigureAwait(false) != t1)
            {
                Assert.Fail("Timed out waiting for processing to start.");
            }

            request1.ProcessingCompleted.SetResult(true);
            Assert.That(request1.CompletedStatusCode, Is.Null, "Request should not have an error status.");
        }

        [Test]
        public async Task TestTooManyRequestsAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 1, 1);

            var req1 = new TestEndpointIncomingRequest();
            var req2 = new TestEndpointIncomingRequest();
            var req3 = new TestEndpointIncomingRequest();

            server.ScheduleIncomingRequest(req1);

            // Wait for req1 to start processing so it leaves the queue.
            Task<bool> t1 = req1.ProcessingStarted.Task;
            using (var cts1 = new CancellationTokenSource(5000))
            {
                if (await Task.WhenAny(t1, Task.Delay(5000, cts1.Token)).ConfigureAwait(false) != t1)
                {
                    Assert.Fail("Timed out waiting for processing to start.");
                }
            }

            // req1 is active (taking the 1 thread), queue is now empty. Capacity is 1.
            server.ScheduleIncomingRequest(req2); // Goes to queue.
            server.ScheduleIncomingRequest(req3); // Should fail to enter queue.

            Assert.That(req3.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));

            req1.ProcessingCompleted.TrySetResult(true);

            Task<bool> t2 = req2.ProcessingStarted.Task;
            using (var cts2 = new CancellationTokenSource(5000))
            {
                if (await Task.WhenAny(t2, Task.Delay(5000, cts2.Token)).ConfigureAwait(false) != t2)
                {
                    Assert.Fail("Timed out waiting for req2 processing to start.");
                }
            }

            req2.ProcessingCompleted.TrySetResult(true);
            req3.ProcessingCompleted.TrySetResult(true);
        }

        [Test]
        public async Task TestHighLoadAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(4, 10, 1000);

            const int count = 500;
            var requests = new TestEndpointIncomingRequest[count];

            for (int i = 0; i < count; i++)
            {
                requests[i] = new TestEndpointIncomingRequest();

                // Automatically complete request when it starts to parallelize processing
                TestEndpointIncomingRequest req = requests[i];
                _ = req.ProcessingStarted.Task.ContinueWith(_ => req.ProcessingCompleted.TrySetResult(true), TaskScheduler.Default);
            }

            var tcsStart = new TaskCompletionSource<bool>();

            var loadTask = Task.Run(async () =>
            {
                await tcsStart.Task.ConfigureAwait(false);
                for (int i = 0; i < count; i++)
                {
                    server.ScheduleIncomingRequest(requests[i]);
                }
            });

            tcsStart.SetResult(true);
            await loadTask.ConfigureAwait(false);

            // Wait for all to finish
            using var cts = new CancellationTokenSource(5000);
            Task<bool[]> allFinished = Task.WhenAll(Array.ConvertAll(requests, r => r.ProcessingCompleted.Task));
            if (await Task.WhenAny(allFinished, Task.Delay(5000, cts.Token)).ConfigureAwait(false) != allFinished)
            {
                Assert.Fail("Timed out waiting for all requests to finish.");
            }

            for (int i = 0; i < count; i++)
            {
                Assert.That(requests[i].CompletedStatusCode, Is.Not.EqualTo(StatusCodes.BadServerTooBusy));
                Assert.That(requests[i].CompletedStatusCode, Is.Not.EqualTo(StatusCodes.BadServerHalted));
            }
        }

        [Test]
        public async Task TestDisposeAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 1, 10);

            var req1 = new TestEndpointIncomingRequest();
            var req2 = new TestEndpointIncomingRequest();

            server.ScheduleIncomingRequest(req1);

            // Wait for req1 to start processing so it leaves the queue.
            Task<bool> t1 = req1.ProcessingStarted.Task;
            using (var cts1 = new CancellationTokenSource(5000))
            {
                if (await Task.WhenAny(t1, Task.Delay(5000, cts1.Token)).ConfigureAwait(false) != t1)
                {
                    Assert.Fail("Timed out waiting for processing to start.");
                }
            }

            // req1 is active (taking the 1 thread)
            // queue is now empty. Capacity is 10.
            server.ScheduleIncomingRequest(req2); // Goes to queue.

            // Request queue has req2 pending.
            // Dispose the server, which disposes the queue.
            var disposeTask = Task.Run(server.Dispose);

            // req2 should be failed with BadServerHalted.
            Task<bool> t2 = req2.ProcessingCompleted.Task;
            using (var cts2 = new CancellationTokenSource(5000))
            {
                if (await Task.WhenAny(t2, Task.Delay(5000, cts2.Token)).ConfigureAwait(false) != t2)
                {
                    Assert.Fail("Timed out waiting for req2 processing to be completed via Dispose.");
                }
            }
            Assert.That(req2.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

            // Scheduling a new request should immediately fail with BadServerHalted
            var req3 = new TestEndpointIncomingRequest();
            server.ScheduleIncomingRequest(req3);
            Assert.That(req3.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

            req1.ProcessingCompleted.TrySetResult(true);
            await disposeTask.ConfigureAwait(false);
        }
    }
}
