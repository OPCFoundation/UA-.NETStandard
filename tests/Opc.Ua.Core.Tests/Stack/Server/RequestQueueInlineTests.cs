/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code; requests and servers are disposed by the fixtures that own them.
#pragma warning disable CA2000
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// A request that arrives while nothing is queued, and that the isolation
    /// provider grants execution capacity at once, runs on the thread that
    /// received it instead of being handed to a worker.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class RequestQueueInlineTests
    {
        [Test]
        [CancelAfter(30000)]
        public async Task ARequestRunsOnTheReceivingThreadWhenNothingIsQueuedAsync()
        {
            using var server = new TestServer(new TestProvider());
            var request = new TestRequest(new ReadRequest());
            request.CompleteSynchronously = true;
            Schedule(server, request);

            Assert.That(request.Completed.Task.IsCompleted, Is.True, "ran to completion before scheduling returned");
            Assert.That(await request.Started.Task.ConfigureAwait(false), Is.True, "ran on the scheduling thread");
            Assert.That(request.CompletedStatusCode, Is.Null);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task TheServerCanDeclineInlineExecutionAsync()
        {
            using var server = new TestServer(new TestProvider()) { RunInline = false };
            var request = new TestRequest(new ReadRequest());
            request.CompleteSynchronously = true;

            Schedule(server, request);

            Assert.That(
                await request.Started.Task.ConfigureAwait(false),
                Is.False,
                "a declined request runs on a worker");
            await request.Completed.Task.ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(30000)]
        public async Task DisabledInlineExecutionQueuesEveryRequestAsync()
        {
            using var server = new TestServer(new TestProvider(), InlineRequestExecutionMode.Disabled);
            var request = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };

            Schedule(server, request);

            Assert.That(await request.Started.Task.ConfigureAwait(false), Is.False);
            await request.Completed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Two requests in a row that hold the receive loop of their channel longer
        /// than the budget pause inline execution on that channel; the requests of
        /// the channel are queued until the pause ends.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task RequestsThatOverrunTheBudgetPauseInlineExecutionOnTheirChannelAsync()
        {
            var time = new FakeTimeProvider();
            var channel = new InlineExecutionValve(time);
            var other = new InlineExecutionValve(time);
            using var server = new TestServer(new TestProvider(), inlineRequestTimeBudget: 5);
            Action overrun = () => time.Advance(TimeSpan.FromMilliseconds(20));

            var first = new TestRequest(new ReadRequest()) { CompleteSynchronously = true, OnCall = overrun };
            var second = new TestRequest(new ReadRequest()) { CompleteSynchronously = true, OnCall = overrun };
            Schedule(server, first, channel);
            Schedule(server, second, channel);
            Assert.That(await first.Started.Task.ConfigureAwait(false), Is.True);
            Assert.That(await second.Started.Task.ConfigureAwait(false), Is.True);

            var paused = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };
            Schedule(server, paused, channel);
            Assert.That(await paused.Started.Task.ConfigureAwait(false), Is.False, "queued while paused");
            await paused.Completed.Task.ConfigureAwait(false);

            var elsewhere = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };
            Schedule(server, elsewhere, other);
            Assert.That(await elsewhere.Started.Task.ConfigureAwait(false), Is.True, "other channels run inline");

            time.Advance(TimeSpan.FromSeconds(1));
            var resumed = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };
            Schedule(server, resumed, channel);
            Assert.That(await resumed.Started.Task.ConfigureAwait(false), Is.True, "inline again after the pause");
        }

        [Test]
        [CancelAfter(30000)]
        public async Task AZeroBudgetDoesNotPauseInlineExecutionAsync()
        {
            var time = new FakeTimeProvider();
            var channel = new InlineExecutionValve(time);
            using var server = new TestServer(new TestProvider(), inlineRequestTimeBudget: 0);

            for (int ii = 0; ii < 3; ii++)
            {
                var request = new TestRequest(new ReadRequest())
                {
                    CompleteSynchronously = true,
                    OnCall = () => time.Advance(TimeSpan.FromSeconds(1))
                };
                Schedule(server, request, channel);
                Assert.That(await request.Started.Task.ConfigureAwait(false), Is.True);
            }
            Assert.That(channel.IsPaused, Is.False);
        }

        /// <summary>
        /// Requests that establish or end sessions, cancel, or can park always go
        /// through the queue, whose admission gives them their reserved capacity.
        /// </summary>
        [TestCase(typeof(CreateSessionRequest))]
        [TestCase(typeof(ActivateSessionRequest))]
        [TestCase(typeof(CloseSessionRequest))]
        [TestCase(typeof(CancelRequest))]
        [TestCase(typeof(PublishRequest))]
        [CancelAfter(30000)]
        public async Task SessionControlAndParkableRequestsAreQueuedAsync(Type requestType)
        {
            using var server = new TestServer(new TestProvider());
            var request = new TestRequest((IServiceRequest)Activator.CreateInstance(requestType)!)
            {
                Parkable = requestType == typeof(PublishRequest),
                CompleteSynchronously = true
            };

            Schedule(server, request);

            Assert.That(await request.Started.Task.ConfigureAwait(false), Is.False);
            await request.Completed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Without execution capacity the request is queued, and runs on a worker
        /// once capacity returns, like any queued request.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ARequestIsQueuedWhileExecutionCapacityIsExhaustedAsync()
        {
            var provider = new TestProvider { ExecutionCapacity = 1 };
            using var server = new TestServer(provider);
            var first = new TestRequest(new ReadRequest());
            var second = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };

            // the first runs inline up to its first wait and keeps its execution lease.
            Schedule(server, first);
            Assert.That(first.Started.Task.IsCompleted, Is.True);

            Schedule(server, second);
            await Task.Delay(100).ConfigureAwait(false);
            Assert.That(second.Started.Task.IsCompleted, Is.False, "waits for execution capacity");

            first.Complete();
            Assert.That(await second.Started.Task.ConfigureAwait(false), Is.False);
            await second.Completed.Task.ConfigureAwait(false);
            Assert.That(provider.ExecutionUsed, Is.Zero);
        }

        /// <summary>
        /// A request that arrives while others are queued does not overtake them:
        /// it is queued behind them.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ARequestDoesNotOvertakeQueuedRequestsAsync()
        {
            var provider = new TestProvider { ExecutionCapacity = 1 };
            using var server = new TestServer(provider);
            var running = new TestRequest(new ReadRequest());
            var queued = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };
            var later = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };

            Schedule(server, running);
            Schedule(server, queued);

            // capacity for the later request, but the queue is not empty.
            provider.ExecutionCapacity = 2;
            Schedule(server, later);

            Assert.That(await later.Started.Task.ConfigureAwait(false), Is.False);
            Assert.That(queued.Started.Task.IsCompleted, Is.True, "the queued request was selected first");

            running.Complete();
            await Task.WhenAll(queued.Completed.Task, later.Completed.Task).ConfigureAwait(false);
        }

        /// <summary>
        /// A request that arrives while a queued request has left its FIFO but waits for
        /// its execution admission does not run inline ahead of it: the queue counts it
        /// as queued no longer, but as being selected.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ARequestDoesNotOvertakeASelectionInFlightAsync()
        {
            var provider = new TestProvider { ExecutionCapacity = 1 };
            using var server = new TestServer(provider);
            var running = new TestRequest(new ReadRequest());
            var queued = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };
            var later = new TestRequest(new ReadRequest()) { CompleteSynchronously = true };

            // running holds the only execution lease; queued is queued, and the worker
            // tried it once and put it back.
            Schedule(server, running);
            Schedule(server, queued);
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (provider.ExecutionAttempts < 3 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5).ConfigureAwait(false);
            }
            Assert.That(provider.ExecutionAttempts, Is.GreaterThanOrEqualTo(3));
            Assert.That(queued.Started.Task.IsCompleted, Is.False);

            // later arrives while the worker asks for the lease running returns.
            provider.BeforeExecutionGrant = () => Schedule(server, later);
            running.Complete();

            await Task.WhenAll(queued.Completed.Task, later.Completed.Task).ConfigureAwait(false);
            Assert.That(await later.Started.Task.ConfigureAwait(false), Is.False, "later ran inline");
            Assert.That(queued.StartOrder, Is.LessThan(later.StartOrder), "later overtook the queued request");
        }

        /// <summary>
        /// A request that went asynchronous after starting inline is cancelled by a
        /// server stop and reported as halted, like a request on a worker.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task StoppingTheServerCancelsARequestStartedInlineAsync()
        {
            var server = new TestServer(new TestProvider());
            var request = new TestRequest(new ReadRequest()) { WaitForCancellation = true };

            Schedule(server, request);
            Assert.That(await request.Started.Task.ConfigureAwait(false), Is.True);
            Assert.That(request.Completed.Task.IsCompleted, Is.False);

            await server.StopQueueAsync().ConfigureAwait(false);

            await request.Completed.Task.ConfigureAwait(false);
            Assert.That(request.CompletedStatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            server.Dispose();
        }

        /// <summary>
        /// A handler that blocks the receiving thread still times out: the timeout
        /// hint cancels its token from a timer, not from the blocked thread.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task ABlockingInlineHandlerStillTimesOutAsync()
        {
            using var server = new TestServer(new TestProvider());
            var endpoint = new TestEndpoint(server);
            bool handlerInline = false;
            endpoint.AddService(
                new ReadRequest().TypeId,
                typeof(ReadRequest),
                (request, context, lifetime) =>
                {
                    handlerInline = t_scheduling;
                    // blocks the receiving thread until the request lifetime ends.
                    lifetime.CancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                    lifetime.CancellationToken.ThrowIfCancellationRequested();
                    return new ValueTask<IServiceResponse>(new ReadResponse());
                });
            var read = new ReadRequest { RequestHeader = new RequestHeader { TimeoutHint = 200 } };
            var watch = Stopwatch.StartNew();

            ValueTask<IServiceResponse> pending = endpoint.Process(read, NewContext());

            Assert.That(pending.IsCompleted, Is.True, "the handler ran on this thread");
            Assert.That(handlerInline, Is.True);
            IServiceResponse response = await pending.ConfigureAwait(false);
            Assert.That(watch.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150)));
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
        }

        /// <summary>
        /// The transport's cancellation of a request reaches a handler that started
        /// inline and then waits, as it reaches one on a worker.
        /// </summary>
        [Test]
        [CancelAfter(30000)]
        public async Task TransportCancellationReachesARequestStartedInlineAsync()
        {
            using var server = new TestServer(new TestProvider());
            var endpoint = new TestEndpoint(server);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.AddService(
                new ReadRequest().TypeId,
                typeof(ReadRequest),
                async (request, context, lifetime) =>
                {
                    started.TrySetResult(t_scheduling);
                    await Task.Delay(Timeout.Infinite, lifetime.CancellationToken).ConfigureAwait(false);
                    return new ReadResponse();
                });
            using var transport = new CancellationTokenSource();

            ValueTask<IServiceResponse> pending = endpoint.Process(
                new ReadRequest { RequestHeader = new RequestHeader() }, NewContext(), transport.Token);

            Assert.That(await started.Task.ConfigureAwait(false), Is.True);
            Assert.That(pending.IsCompleted, Is.False);
            transport.Cancel();

            IServiceResponse response = await pending.ConfigureAwait(false);
            Assert.That(response, Is.InstanceOf<ServiceFault>());
        }

        /// <summary>
        /// Schedules a request with <see cref="t_scheduling"/> set on this thread, so that a
        /// handler can tell whether it runs inside the call.
        /// </summary>
        private static void Schedule(
            ServerBase server,
            IEndpointIncomingRequest request,
            InlineExecutionValve? channel = null)
        {
            t_scheduling = true;
            InlineExecutionValve? previous = InlineExecutionValve.Enter(channel);
            try
            {
                server.ScheduleIncomingRequest(request);
            }
            finally
            {
                InlineExecutionValve.Exit(previous);
                t_scheduling = false;
            }
        }

        /// <summary>
        /// Set while the test thread is inside a scheduling call. A worker that later
        /// runs on the same thread sees it cleared.
        /// </summary>
        [ThreadStatic]
        private static bool t_scheduling;

        private static SecureChannelContext NewContext()
        {
            return new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
        }

        /// <summary>
        /// Hosts a fair request queue with one worker and the inline policy under test.
        /// </summary>
        private sealed class TestServer : ServerBase
        {
            public TestServer(
                IServerResourceIsolationProvider provider,
                InlineRequestExecutionMode inlineRequestExecution = InlineRequestExecutionMode.Auto,
                int inlineRequestTimeBudget = 5)
                : base(NUnitTelemetryContext.Create(true))
            {
                FieldInfo context = typeof(ServerBase).GetField(
                    "m_messageContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
                context.SetValue(this, ServiceMessageContext.Create(NUnitTelemetryContext.Create(true)));
                FieldInfo queueField = typeof(ServerBase).GetField(
                    "m_requestQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
                ((IDisposable)queueField.GetValue(this)!).Dispose();
                m_queue = new RequestQueue(
                    this, 1, 1, 10, true, provider, 10, inlineRequestExecution, inlineRequestTimeBudget);
                queueField.SetValue(this, m_queue);
            }

            public bool RunInline { get; set; } = true;

            public ValueTask StopQueueAsync()
            {
                return m_queue.StopAsync();
            }

            protected override bool CanRunRequestInline(IEndpointIncomingRequest request)
            {
                return RunInline;
            }

            protected override Task ProcessRequestAsync(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                return request.CallAsync(cancellationToken).AsTask();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_queue.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly RequestQueue m_queue;
        }

        /// <summary>
        /// Creates endpoint requests with the production request lifetime.
        /// </summary>
        private sealed class TestEndpoint : EndpointBase
        {
            public TestEndpoint(ServerBase server)
                : base(server)
            {
            }

            public void AddService(
                ExpandedNodeId typeId,
                Type requestType,
                Func<IServiceRequest, SecureChannelContext, RequestLifetime, ValueTask<IServiceResponse>> invoke)
            {
                SupportedServices[typeId] = new ServiceDefinition(requestType, (r, c, l) => invoke(r, c, l));
            }

            /// <summary>
            /// Processes a request with <see cref="t_scheduling"/> set for the part that
            /// runs synchronously; the flag is cleared before the response completes.
            /// </summary>
#pragma warning disable RCS1229 // the finally is meant to run before the returned task completes
            public ValueTask<IServiceResponse> Process(
                IServiceRequest request,
                SecureChannelContext context,
                CancellationToken cancellationToken = default)
            {
                t_scheduling = true;
                try
                {
                    return new EndpointIncomingRequest(this, context, request).ProcessAsync(cancellationToken);
                }
                finally
                {
                    t_scheduling = false;
                }
            }
#pragma warning restore RCS1229
        }

        /// <summary>
        /// Records the thread a request starts on and completes when told to.
        /// </summary>
        private sealed class TestRequest : IParkableIncomingRequest
        {
            public TestRequest(IServiceRequest request)
            {
                request.RequestHeader ??= new RequestHeader();
                Request = request;
            }

            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Completed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public StatusCode? CompletedStatusCode { get; private set; }

            public bool CompleteSynchronously { get; set; }

            public bool WaitForCancellation { get; set; }

            public bool Parkable { get; set; }

            /// <summary>
            /// Runs when the request starts, on the thread it starts on.
            /// </summary>
            public Action? OnCall { get; set; }

            public IServiceRequest Request { get; }

            public SecureChannelContext SecureChannelContext { get; } = NewContext();

            RequestParkSink? IParkableIncomingRequest.ParkSink => Parkable ? m_parkSink : null;

            public void Complete()
            {
                m_release.TrySetResult(true);
            }

            /// <summary>
            /// The position in which the request started among all test requests.
            /// </summary>
            public int StartOrder { get; private set; }

            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                StartOrder = Interlocked.Increment(ref s_startOrder);
                OnCall?.Invoke();
                Started.TrySetResult(t_scheduling);
                if (WaitForCancellation)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                else if (!CompleteSynchronously)
                {
                    await m_release.Task.ConfigureAwait(false);
                }
                Completed.TrySetResult(true);
            }

            public void OperationCompleted(IServiceResponse? response, ServiceResult error)
            {
                CompletedStatusCode = error?.StatusCode;
                Completed.TrySetResult(true);
            }

            private static int s_startOrder;

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly RequestParkSink m_parkSink = new();
        }

        /// <summary>
        /// Grants every stage up to a settable execution capacity.
        /// </summary>
        private sealed class TestProvider : IServerResourceIsolationProvider
        {
            public bool UseFairScheduling => true;

            public long ExecutionCapacity { get; set; } = 100;

            public long ExecutionUsed => Interlocked.Read(ref m_executionUsed);

            /// <summary>
            /// Execution admissions attempted, granted or not.
            /// </summary>
            public int ExecutionAttempts => Volatile.Read(ref m_executionAttempts);

            /// <summary>
            /// Runs once, at the next execution admission, before it is decided.
            /// </summary>
            public Action? BeforeExecutionGrant { get; set; }

            public event Action<ResourceIsolationStage>? CapacityAvailable;

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
            {
                return m_owner;
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return m_owner;
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return true;
            }

            public bool TryAcquire(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                [NotNullWhen(true)] out IDisposable? lease,
                out ResourceIsolationFailure failure)
            {
                failure = default;
                if (stage != ResourceIsolationStage.RequestExecution)
                {
                    lease = new Lease(null);
                    return true;
                }
                Interlocked.Increment(ref m_executionAttempts);
                Action? before = BeforeExecutionGrant;
                if (before != null)
                {
                    BeforeExecutionGrant = null;
                    before();
                }
                if (Interlocked.Increment(ref m_executionUsed) > ExecutionCapacity)
                {
                    Interlocked.Decrement(ref m_executionUsed);
                    lease = null;
                    failure = new ResourceIsolationFailure(ResourceIsolationFailureReason.Capacity, TimeSpan.Zero);
                    return false;
                }
                lease = new Lease(this);
                return true;
            }

            private void Release()
            {
                Interlocked.Decrement(ref m_executionUsed);
                CapacityAvailable?.Invoke(ResourceIsolationStage.RequestExecution);
            }

            private sealed class Lease(TestProvider? provider) : IDisposable
            {
                public void Dispose()
                {
                    Interlocked.Exchange(ref m_provider, null)?.Release();
                }

                private TestProvider? m_provider = provider;
            }

            private readonly ResourceIsolationOwner m_owner = new(
                "owner", ResourceIsolationClass.Established, 1, new long[] { 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000 });

            private long m_executionUsed;

            private int m_executionAttempts;
        }
    }
}
