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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Verifies worker sizing, request completion and parked-request dispatch.
    /// </summary>
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

            public IServiceRequest Request => null!;

            public SecureChannelContext SecureChannelContext => null!;

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                ProcessingStarted.TrySetResult(true);
                return new ValueTask(ProcessingCompleted.Task);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                CompletedStatusCode = error?.StatusCode;
                ProcessingCompleted.TrySetResult(true);
            }
        }

        /// <summary>
        /// Records the token the queue passes to <see cref="CallAsync"/>, optionally declaring that
        /// it links its scheduled token itself, as endpoint requests do.
        /// </summary>
        private class TokenRecordingRequest : IEndpointIncomingRequest
        {
            public TaskCompletionSource<CancellationToken> Called { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } = new();
            public IServiceRequest Request => null!;
            public SecureChannelContext SecureChannelContext => null!;

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                Called.TrySetResult(cancellationToken);
                return new ValueTask(Release.Task);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                Release.TrySetResult(true);
            }
        }

        private sealed class SelfLinkingRequest(CancellationToken scheduled) :
            TokenRecordingRequest, ILinksScheduledCancellation
        {
            public CancellationToken ScheduledCancellationToken { get; } = scheduled;
        }

        /// <summary>
        /// The queue links the scheduled token into the handler's token unless the request links
        /// it itself, so endpoint requests do not pay for a second linked token source.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ScheduledTokenIsLinkedOnlyWhenTheRequestDoesNotLinkItAsync(bool selfLinking)
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 2, 10);
            using var transport = new CancellationTokenSource();
            TokenRecordingRequest request = selfLinking
                ? new SelfLinkingRequest(transport.Token)
                : new TokenRecordingRequest();

            server.ScheduleIncomingRequest(request, transport.Token);
            CancellationToken passed = await request.Called.Task
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            transport.Cancel();

            Assert.That(passed.IsCancellationRequested, Is.EqualTo(!selfLinking));
            Assert.That(passed.CanBeCanceled, Is.True, "the server's stop token is always passed");
            request.Release.TrySetResult(true);
        }

        private sealed class ParkableTestRequest : IParkableIncomingRequest
        {
            public TaskCompletionSource<bool> ProcessingStarted { get; } = new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> ProcessingCompleted { get; } = new TaskCompletionSource<bool>();
            public StatusCode? CompletedStatusCode { get; private set; }

            public IServiceRequest Request => null!;

            public SecureChannelContext SecureChannelContext => null!;

            RequestParkSink IParkableIncomingRequest.ParkSink => m_parkSink;

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                ProcessingStarted.TrySetResult(true);

                // Simulate reaching the park point (queued waiting for a notification):
                // the worker should be released even though processing has not completed.
                m_parkSink.NotifyParked();
                return new ValueTask(ProcessingCompleted.Task);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                CompletedStatusCode = error?.StatusCode;
                ProcessingCompleted.TrySetResult(true);
            }

            private readonly RequestParkSink m_parkSink = new();
        }

        /// <summary>
        /// Hosts configurable production request queues and controllable test handlers.
        /// </summary>
        private sealed class TestServer : ServerBase
        {
            public TestServer()
                : base(NUnitTelemetryContext.Create(true))
            {
            }

            /// <summary>
            /// Gets the actual number of retained queue workers, including idle workers.
            /// </summary>
            public int WorkerCount
            {
                get
                {
                    FieldInfo queueField = typeof(ServerBase).GetField(
                        "m_requestQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var queue = (RequestQueue)queueField!.GetValue(this)!;
                    FieldInfo countField = typeof(RequestQueue).GetField(
                        "m_totalThreadCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    return (int)countField!.GetValue(queue)!;
                }
            }

            /// <summary>
            /// Gets the meter of the installed request queue.
            /// </summary>
            public Meter QueueMeter
            {
                get
                {
                    FieldInfo queueField = typeof(ServerBase).GetField(
                        "m_requestQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var queue = (RequestQueue)queueField!.GetValue(this)!;
                    FieldInfo meterField = typeof(RequestQueue).GetField(
                        "m_meter", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    return (Meter)meterField!.GetValue(queue)!;
                }
            }

            /// <summary>
            /// Replaces the default queue with the requested worker and admission envelope.
            /// </summary>
            public void ReplaceRequestQueue(
                int minThreads,
                int maxThreads,
                int maxQueue,
                bool decoupleHeldPublishRequests = true,
                IServerResourceIsolationProvider? provider = null)
            {
                FieldInfo field = typeof(ServerBase).GetField(
                    "m_requestQueue",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;

                var oldQueue = field!.GetValue(this) as IDisposable;
                oldQueue?.Dispose();

                var newQueue = new RequestQueue(
                    this, minThreads, maxThreads, maxQueue, decoupleHeldPublishRequests, provider, 10);
                field.SetValue(this, newQueue);
            }

            /// <summary>
            /// Fair scheduling does not reserve the maximum worker envelope before demand exists.
            /// </summary>
            [Test]
            public void FairQueueStartsOnlyItsMinimumWorkers()
            {
                var provider = new Mock<IServerResourceIsolationProvider>();
                provider.SetupGet(value => value.UseFairScheduling).Returns(true);
                using var server = new TestServer();
                server.ReplaceRequestQueue(2, 16, 100, provider: provider.Object);
                Assert.That(server.WorkerCount, Is.EqualTo(2));
            }

            protected override Task ProcessRequestAsync(IEndpointIncomingRequest request, CancellationToken cancellationToken = default)
            {
                return request.CallAsync(cancellationToken).AsTask();
            }
        }

        /// <summary>
        /// A server that has not started has no request queue, so a request is halted
        /// instead of starting workers for a queue that is replaced at start.
        /// </summary>
        [Test]
        public void RequestBeforeStartIsHalted()
        {
            using var server = new TestServer();
            var request = new TestEndpointIncomingRequest();

            server.ScheduleIncomingRequest(request);

            Assert.That(request.ProcessingStarted.Task.IsCompleted, Is.False);
            Assert.That(request.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
        }

        /// <summary>
        /// The queue's worker counts are not applied to the process-wide ThreadPool.
        /// </summary>
        [Test]
        public void QueueLeavesThreadPoolLimitsUnchanged()
        {
            ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
            ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
            using var server = new TestServer();

            server.ReplaceRequestQueue(minWorkers + 1, minWorkers + 2, 10);

            ThreadPool.GetMinThreads(out int minWorkersAfter, out int minIoAfter);
            ThreadPool.GetMaxThreads(out int maxWorkersAfter, out int maxIoAfter);
            Assert.That(minWorkersAfter, Is.EqualTo(minWorkers));
            Assert.That(minIoAfter, Is.EqualTo(minIo));
            Assert.That(maxWorkersAfter, Is.EqualTo(maxWorkers));
            Assert.That(maxIoAfter, Is.EqualTo(maxIo));
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

            // req3 should be rejected synchronously or near-synchronously; wait briefly.
            using (var cts3 = new CancellationTokenSource(5000))
            {
                await Task.WhenAny(req3.ProcessingCompleted.Task, Task.Delay(5000, cts3.Token))
                    .ConfigureAwait(false);
            }

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

            // req3 should be rejected synchronously or near-synchronously; wait briefly.
            using (var cts3 = new CancellationTokenSource(5000))
            {
                await Task.WhenAny(req3.ProcessingCompleted.Task, Task.Delay(5000, cts3.Token))
                    .ConfigureAwait(false);
            }

            Assert.That(req3.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerHalted));

            req1.ProcessingCompleted.TrySetResult(true);
            await disposeTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ParkedRequestReleasesWorkerForNextRequestAsync()
        {
            using var server = new TestServer();

            // A single worker: only the park decoupling can let a second request be
            // serviced while the first is still parked waiting for a notification.
            server.ReplaceRequestQueue(1, 1, 10, decoupleHeldPublishRequests: true);

            var reqA = new ParkableTestRequest();
            var reqB = new ParkableTestRequest();

            server.ScheduleIncomingRequest(reqA);
            await AwaitOrFailAsync(reqA.ProcessingStarted.Task, "request A to start").ConfigureAwait(false);

            server.ScheduleIncomingRequest(reqB);
            await AwaitOrFailAsync(reqB.ProcessingStarted.Task, "request B to start while A is parked")
                .ConfigureAwait(false);

            Assert.That(
                reqA.ProcessingCompleted.Task.IsCompleted,
                Is.False,
                "Request A should still be parked while B is serviced.");

            reqA.ProcessingCompleted.TrySetResult(true);
            reqB.ProcessingCompleted.TrySetResult(true);
        }

        [Test]
        public async Task DisabledDecouplingKeepsWorkerBlockedUntilCompletionAsync()
        {
            using var server = new TestServer();

            // Decoupling disabled: the single worker stays bound to the parked request,
            // so a second request cannot start until the first completes.
            server.ReplaceRequestQueue(1, 1, 10, decoupleHeldPublishRequests: false);

            var reqA = new ParkableTestRequest();
            var reqB = new ParkableTestRequest();

            server.ScheduleIncomingRequest(reqA);
            await AwaitOrFailAsync(reqA.ProcessingStarted.Task, "request A to start").ConfigureAwait(false);

            server.ScheduleIncomingRequest(reqB);

            bool bStarted = await CompletedWithinAsync(reqB.ProcessingStarted.Task, 500).ConfigureAwait(false);
            Assert.That(
                bStarted,
                Is.False,
                "Request B must not start while the only worker is blocked on parked A.");

            // Completing A frees the worker to service B.
            reqA.ProcessingCompleted.TrySetResult(true);
            await AwaitOrFailAsync(reqB.ProcessingStarted.Task, "request B to start after A completes")
                .ConfigureAwait(false);
            reqB.ProcessingCompleted.TrySetResult(true);
        }

        [Test]
        public async Task ManyParkedRequestsAreServedBySmallWorkerPoolAsync()
        {
            using var server = new TestServer();

            // Two workers must be able to service far more concurrently-parked requests
            // than the worker count, because a parked request does not hold a worker.
            server.ReplaceRequestQueue(2, 2, 1000, decoupleHeldPublishRequests: true);

            const int count = 200;
            var requests = new ParkableTestRequest[count];
            for (int i = 0; i < count; i++)
            {
                requests[i] = new ParkableTestRequest();
                server.ScheduleIncomingRequest(requests[i]);
            }

            Task allStarted = Task.WhenAll(Array.ConvertAll(requests, r => r.ProcessingStarted.Task));
            await AwaitOrFailAsync(allStarted, "all parked requests to start", 15000).ConfigureAwait(false);

            // None of them have completed - they are all still parked - yet all were
            // admitted and started by only two workers.
            foreach (ParkableTestRequest request in requests)
            {
                Assert.That(
                    request.CompletedStatusCode,
                    Is.Null,
                    "A parked request should not have been faulted.");
                request.ProcessingCompleted.TrySetResult(true);
            }
        }

        /// <summary>
        /// The shared queue reports queued and executing requests, rejections by status code
        /// and the time a request waited for a worker.
        /// </summary>
        [Test]
        public async Task SharedQueueReportsQueueMetricsAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 1, 1);
            using var metrics = new QueueMetricsListener(server.QueueMeter);
            var running = new TestEndpointIncomingRequest();
            var queued = new TestEndpointIncomingRequest();
            var rejected = new TestEndpointIncomingRequest();

            server.ScheduleIncomingRequest(running);
            await AwaitOrFailAsync(running.ProcessingStarted.Task, "the first request to start").ConfigureAwait(false);
            server.ScheduleIncomingRequest(queued);
            server.ScheduleIncomingRequest(rejected);

            Assert.That(rejected.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(metrics.Observe("opcua.server.request_queue.queued"), Is.EqualTo(1));
            Assert.That(metrics.Observe("opcua.server.request_queue.executing"), Is.EqualTo(1));
            Assert.That(metrics.Observe("opcua.server.request_queue.parked"), Is.Zero);
            Assert.That(metrics.Rejections("BadServerTooBusy"), Is.EqualTo(1));

            await Task.Delay(20).ConfigureAwait(false);
            running.ProcessingCompleted.TrySetResult(true);
            await AwaitOrFailAsync(queued.ProcessingStarted.Task, "the queued request to start").ConfigureAwait(false);

            double[] waits = metrics.TimesInQueue;
            Assert.That(waits, Has.Length.EqualTo(2), "both executed requests were measured");
            Assert.That(waits[1], Is.GreaterThanOrEqualTo(0.015));
            Assert.That(metrics.Observe("opcua.server.request_queue.queued"), Is.Zero);
            queued.ProcessingCompleted.TrySetResult(true);
        }

        /// <summary>
        /// A parked request leaves the executing gauge and is reported as parked.
        /// </summary>
        [Test]
        public async Task ParkedRequestIsReportedAsParkedAsync()
        {
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 1, 10, decoupleHeldPublishRequests: true);
            using var metrics = new QueueMetricsListener(server.QueueMeter);
            var parked = new ParkableTestRequest();

            server.ScheduleIncomingRequest(parked);
            await AwaitOrFailAsync(parked.ProcessingStarted.Task, "the request to park").ConfigureAwait(false);

            await AwaitOrFailAsync(
                WaitUntilAsync(() => metrics.Observe("opcua.server.request_queue.parked") == 1),
                "the parked gauge").ConfigureAwait(false);
            Assert.That(metrics.Observe("opcua.server.request_queue.executing"), Is.Zero);

            parked.ProcessingCompleted.TrySetResult(true);
            await AwaitOrFailAsync(
                WaitUntilAsync(() => metrics.Observe("opcua.server.request_queue.parked") == 0),
                "the parked request to finish").ConfigureAwait(false);
        }

        /// <summary>
        /// The isolation path reports the same instruments, including rejections from fair admission.
        /// </summary>
        [Test]
        public async Task FairQueueReportsQueueMetricsAsync()
        {
            var provider = new FairRequestQueueTests.TestProvider(executionCapacity: 1);
            using var server = new TestServer();
            server.ReplaceRequestQueue(1, 1, 10, provider: provider);
            using var metrics = new QueueMetricsListener(server.QueueMeter);
            var running = new FairTestRequest();
            var queued = new FairTestRequest();
            var cancelled = new FairTestRequest();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            server.ScheduleIncomingRequest(running);
            await AwaitOrFailAsync(running.ProcessingStarted.Task, "the first request to start").ConfigureAwait(false);
            server.ScheduleIncomingRequest(queued);
            server.ScheduleIncomingRequest(cancelled, cancellation.Token);

            Assert.That(cancelled.CompletedStatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(metrics.Rejections("BadRequestCancelledByClient"), Is.EqualTo(1));
            Assert.That(metrics.Observe("opcua.server.request_queue.queued"), Is.EqualTo(1));
            Assert.That(metrics.Observe("opcua.server.request_queue.executing"), Is.EqualTo(1));

            running.ProcessingCompleted.TrySetResult(true);
            await AwaitOrFailAsync(queued.ProcessingStarted.Task, "the queued request to start").ConfigureAwait(false);
            Assert.That(metrics.TimesInQueue, Has.Length.EqualTo(2));
            queued.ProcessingCompleted.TrySetResult(true);
        }

        /// <summary>
        /// A request the isolation provider can classify.
        /// </summary>
        private sealed class FairTestRequest : IEndpointIncomingRequest
        {
            public TaskCompletionSource<bool> ProcessingStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ProcessingCompleted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public StatusCode? CompletedStatusCode { get; private set; }

            public IServiceRequest Request { get; } = new ReadRequest { RequestHeader = new RequestHeader() };

            public SecureChannelContext SecureChannelContext { get; } =
                new("channel", null, RequestEncoding.Binary);

            public ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                ProcessingStarted.TrySetResult(true);
                return new ValueTask(ProcessingCompleted.Task);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                CompletedStatusCode = error?.StatusCode;
                ProcessingCompleted.TrySetResult(true);
            }
        }

        /// <summary>
        /// Listens to the instruments of one request queue meter.
        /// </summary>
        private sealed class QueueMetricsListener : IDisposable
        {
            public QueueMetricsListener(Meter meter)
            {
                m_listener = new MeterListener
                {
                    InstrumentPublished = (instrument, listener) =>
                    {
                        if (ReferenceEquals(instrument.Meter, meter))
                        {
                            listener.EnableMeasurementEvents(instrument);
                        }
                    }
                };
                m_listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
                    m_observed[instrument.Name] = value);
                m_listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                {
                    string status = string.Empty;
                    foreach (KeyValuePair<string, object?> tag in tags)
                    {
                        if (tag.Key == "status")
                        {
                            status = (string)tag.Value!;
                        }
                    }
                    m_rejections.AddOrUpdate(status, value, (_, total) => total + value);
                });
                m_listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
                    m_timesInQueue.Enqueue(value));
                m_listener.Start();
            }

            public double[] TimesInQueue => [.. m_timesInQueue];

            public long Rejections(string status)
            {
                return m_rejections.TryGetValue(status, out long count) ? count : 0;
            }

            public int Observe(string name)
            {
                m_listener.RecordObservableInstruments();
                return m_observed.TryGetValue(name, out int value) ? value : -1;
            }

            public void Dispose()
            {
                m_listener.Dispose();
            }

            private readonly MeterListener m_listener;
            private readonly ConcurrentDictionary<string, int> m_observed = new();
            private readonly ConcurrentDictionary<string, long> m_rejections = new();
            private readonly ConcurrentQueue<double> m_timesInQueue = new();
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            while (!condition())
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private static async Task AwaitOrFailAsync(Task task, string what, int timeoutMs = 5000)
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(timeoutMs, cts.Token);
            if (await Task.WhenAny(task, delay).ConfigureAwait(false) != task)
            {
                Assert.Fail($"Timed out waiting for {what}.");
            }
            cts.Cancel();
            await task.ConfigureAwait(false);
        }

        private static async Task<bool> CompletedWithinAsync(Task task, int timeoutMs)
        {
            using var cts = new CancellationTokenSource();
            var delay = Task.Delay(timeoutMs, cts.Token);
            Task completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
            cts.Cancel();
            return completed == task;
        }
    }
}
