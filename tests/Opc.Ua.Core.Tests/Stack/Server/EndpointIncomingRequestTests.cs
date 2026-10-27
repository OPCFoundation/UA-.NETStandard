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
    public class EndpointIncomingRequestTests
    {
        /// <summary>
        /// Hosts endpoint requests in a single-worker queue so queued and executing states are deterministic.
        /// </summary>
        private sealed class TestServer : ServerBase
        {
            /// <summary>
            /// Initializes message context and installs the single-worker queue owned by this server.
            /// </summary>
            public TestServer()
                : base(NUnitTelemetryContext.Create(true))
            {
                FieldInfo field = typeof(ServerBase).GetField(
                    "m_messageContext",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                field!.SetValue(this, ServiceMessageContext.Create(NUnitTelemetryContext.Create(true)));
                FieldInfo queueField = typeof(ServerBase).GetField(
                    "m_requestQueue", BindingFlags.NonPublic | BindingFlags.Instance)!;
                ((IDisposable)queueField!.GetValue(this)!).Dispose();
                m_queue = new RequestQueue(this, 1, 1, 10);
                queueField.SetValue(this, m_queue);
            }

            public Action<IEndpointIncomingRequest> OnScheduleIncomingRequest { get; set; } = null!;

            public override void ScheduleIncomingRequest(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                if (OnScheduleIncomingRequest != null)
                {
                    OnScheduleIncomingRequest(request);
                }
                else
                {
                    base.ScheduleIncomingRequest(request, cancellationToken);
                }
            }

            /// <summary>
            /// Releases the test queue before disposing the server's remaining state.
            /// </summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_queue.Dispose();
                }
                base.Dispose(disposing);
            }

            /// <summary>
            /// Single-worker queue shared with the base server's dispatch path.
            /// </summary>
            private readonly RequestQueue m_queue;
        }

        private sealed class TestEndpointBase : EndpointBase
        {
            public TestEndpointBase(ServerBase server)
                : base(server)
            {
            }

            public object CreateIncomingRequest(IServiceRequest request, SecureChannelContext context)
            {
                return new EndpointIncomingRequest(this, context, request);
            }

            public void AddServiceLocal(
                ExpandedNodeId typeId,
                Type requestType,
                Func<IServiceRequest, SecureChannelContext, RequestLifetime, ValueTask<IServiceResponse>> invokeService)
            {
                SupportedServices[typeId] = new ServiceDefinition(
                    requestType,
                    (req, ctx, lifetime) => invokeService(req, ctx, lifetime));
            }

            public static ValueTask<IServiceResponse> ProcessAsyncLocal(object incomingRequest)
            {
                return ((EndpointIncomingRequest)incomingRequest).ProcessAsync();
            }

            public static ValueTask<IServiceResponse> ProcessAsyncLocal(
                object incomingRequest,
                CancellationToken cancellationToken)
            {
                return ((EndpointIncomingRequest)incomingRequest).ProcessAsync(cancellationToken);
            }

            public static ValueTask CallAsyncLocal(object incomingRequest)
            {
                return ((EndpointIncomingRequest)incomingRequest).CallAsync();
            }

            public static void OperationCompletedLocal(object incomingRequest, IServiceResponse response, ServiceResult error)
            {
                var req = (EndpointIncomingRequest)incomingRequest;
                req.OperationCompleted(response, error);
            }

            /// <summary>
            /// Moves the receipt of a request into the past, as if it had waited that long.
            /// </summary>
            public static void SetAge(object incomingRequest, TimeSpan age)
            {
                FieldInfo field = typeof(EndpointIncomingRequest).GetField(
                    "m_receivedTimestamp", BindingFlags.NonPublic | BindingFlags.Instance)!;
                field.SetValue(
                    incomingRequest,
                    System.Diagnostics.Stopwatch.GetTimestamp() -
                        (long)(age.TotalSeconds * System.Diagnostics.Stopwatch.Frequency));
            }

            public void TestEquality(IServiceRequest req1, IServiceRequest req2, IServiceRequest req3)
            {
                var r1 = new EndpointIncomingRequest(this, null!, req1);
                var r2 = new EndpointIncomingRequest(this, null!, req2);
                var r3 = new EndpointIncomingRequest(this, null!, req3);
                EndpointIncomingRequest rr1 = r1;

                Assert.That(r1, Is.EqualTo(rr1));
                Assert.That(r1, Is.Not.EqualTo(r2));
                Assert.That(r1, Is.EqualTo(r3));

                Assert.That(r1, Is.EqualTo((object)rr1));
                Assert.That(r1, Is.Not.EqualTo(new object()));

                Assert.That(r1, Is.EqualTo(rr1));
                Assert.That(r1, Is.Not.EqualTo(r2));

                Assert.That(r1.GetHashCode(), Is.EqualTo(r3.GetHashCode()));
            }
        }

        [Test]
        public void Constructor_SetsProperties()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest();
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            var incoming = (IEndpointIncomingRequest)endpoint.CreateIncomingRequest(req, ctx);
            Assert.That(incoming.Request, Is.SameAs(req));
            Assert.That(incoming.SecureChannelContext, Is.SameAs(ctx));
        }

        [Test]
        public void EqualityMethods_WorkCorrectly()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);

            var req1 = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } };
            var req2 = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 2 } };
            var req3 = new ReadRequest { RequestHeader = req1.RequestHeader };

            endpoint.TestEquality(req1, req2, req3);
        }

        [Test]
        public async Task ProcessAsync_SchedulesRequest_AndReturnsResultAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            object incoming = endpoint.CreateIncomingRequest(req, ctx);

            server.OnScheduleIncomingRequest = (r) =>
            {
                var response = new ReadResponse();
                TestEndpointBase.OperationCompletedLocal(r, response, new ServiceResult(StatusCodes.Good));
            };

            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ReadResponse>());
        }

        [Test]
        public async Task ProcessAsync_CatchesScheduleException_ReturnsFaultAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => throw new InvalidOperationException("Simulated exception");

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// A scheduler that completes the request and then throws must not complete it twice (#4729).
        /// </summary>
        [Test]
        public async Task ProcessAsync_ScheduleCompletesThenThrows_KeepsFirstCompletionAsync()
        {
            using var server = new TestServer();
            var expected = new ReadResponse();
            server.OnScheduleIncomingRequest = (r) =>
            {
                r.OperationCompleted(expected, ServiceResult.Good);
                throw new ObjectDisposedException("queue");
            };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            IServiceResponse response = await TestEndpointBase.ProcessAsyncLocal(incoming).ConfigureAwait(false);

            Assert.That(response, Is.SameAs(expected));
            Assert.DoesNotThrow(() => TestEndpointBase.OperationCompletedLocal(
                incoming, null!, new ServiceResult(StatusCodes.BadServerHalted)));
        }

        [Test]
        public async Task CallAsync_FindsServiceAndInvokesItAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            var expectedResponse = new ReadResponse();
            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                (request, context, lifetime) => new ValueTask<IServiceResponse>(expectedResponse));

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            await TestEndpointBase.CallAsyncLocal(incoming).ConfigureAwait(false);

            IServiceResponse response = await responseTask.ConfigureAwait(false);
            Assert.That(response, Is.SameAs(expectedResponse));
        }

        [Test]
        public async Task CallAsync_WhenServiceThrowsOperationCanceledException_GoodStatusCode_ReturnsTimeoutFaultAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                (request, context, lifetime) => throw new OperationCanceledException());

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            await TestEndpointBase.CallAsyncLocal(incoming).ConfigureAwait(false);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public async Task CallAsync_WhenServiceThrowsOperationCanceledException_BadStatusCode_ReturnsTimeoutFaultAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest), (request, context, lifetime) =>
            {
                lifetime.TryCancel(StatusCodes.BadCertificateInvalid);
                throw new OperationCanceledException();
            });

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            await TestEndpointBase.CallAsyncLocal(incoming).ConfigureAwait(false);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public async Task CallAsync_WhenServiceThrowsOtherException_ReturnsFaultAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                (request, context, lifetime) => throw new InvalidOperationException("Error"));

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            await TestEndpointBase.CallAsyncLocal(incoming).ConfigureAwait(false);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
        }

        /// <summary>
        /// Cancellation before dispatch rejects the queued request without invoking its service.
        /// </summary>
        [Test]
        public async Task ProcessAsyncCancellationAbortsQueuedServiceAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 2 } };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int queuedServiceCalls = 0;

            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                async (request, _, lifetime) =>
                {
                    if (request.RequestHeader.RequestHandle == 1)
                    {
                        started.TrySetResult(true);
                        await release.Task.WaitAsync(lifetime.CancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        Interlocked.Increment(ref queuedServiceCalls);
                    }
                    return new ReadResponse();
                });

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            object blocking = endpoint.CreateIncomingRequest(
                new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } }, ctx);
            Task<IServiceResponse> blockingResponse = TestEndpointBase.ProcessAsyncLocal(blocking).AsTask();
            await started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            using var cancellation = new CancellationTokenSource();
            Task<IServiceResponse> responseTask =
                TestEndpointBase.ProcessAsyncLocal(incoming, cancellation.Token).AsTask();
            cancellation.Cancel();
            Assert.That(responseTask.IsCompleted, Is.False, "The only worker still owns the first request.");
            release.TrySetResult(true);
            IServiceResponse first = await blockingResponse.WaitAsync(deadline.Token).ConfigureAwait(false);
            IServiceResponse response = await responseTask.WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(first, Is.InstanceOf<ReadResponse>());
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(queuedServiceCalls, Is.Zero);
        }

        /// <summary>
        /// Cancellation after service entry retains the endpoint's request-lifetime timeout mapping.
        /// </summary>
        [Test]
        public async Task ProcessAsyncCancellationAbortsExecutingServiceWithTimeoutAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                async (_, _, lifetime) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.CancellationToken).ConfigureAwait(false);
                    return new ReadResponse();
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var cancellation = new CancellationTokenSource();
            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            Task<IServiceResponse> responseTask =
                TestEndpointBase.ProcessAsyncLocal(incoming, cancellation.Token).AsTask();
            await started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            cancellation.Cancel();
            IServiceResponse response = await responseTask.WaitAsync(deadline.Token).ConfigureAwait(false);
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [Test]
        public async Task OperationCompleted_WithBadServiceResult_ReturnsFaultAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            TestEndpointBase.OperationCompletedLocal(incoming, new ReadResponse(), new ServiceResult(StatusCodes.BadUserAccessDenied));
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task OperationCompleted_WithGoodServiceResult_ReturnsResponseAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader() };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);

            var expectedResponse = new ReadResponse();
            TestEndpointBase.OperationCompletedLocal(incoming, expectedResponse, new ServiceResult(StatusCodes.Good));
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.SameAs(expectedResponse));
        }

        /// <summary>
        /// A request whose timeout hint ran out while it was queued is faulted with
        /// BadTimeout at dequeue and its service never runs.
        /// </summary>
        [Test]
        public async Task ExpiredQueuedRequestTimesOutWithoutInvokingServiceAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader { TimeoutHint = 1000 } };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            int serviceCalls = 0;
            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest), (_, _, _) =>
            {
                Interlocked.Increment(ref serviceCalls);
                return new ValueTask<IServiceResponse>(new ReadResponse());
            });

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            TestEndpointBase.SetAge(incoming, TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            IServiceResponse response = await TestEndpointBase.ProcessAsyncLocal(incoming).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(serviceCalls, Is.Zero);
        }

        /// <summary>
        /// A request whose budget runs out after dequeue but before its call starts is faulted
        /// with BadTimeout without invoking its service.
        /// </summary>
        [Test]
        public async Task CallAsync_WhenBudgetSpent_ReturnsTimeoutWithoutInvokingServiceAsync()
        {
            using var server = new TestServer();
            server.OnScheduleIncomingRequest = (r) => { };

            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader { TimeoutHint = 1000 } };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            int serviceCalls = 0;
            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest), (_, _, _) =>
            {
                Interlocked.Increment(ref serviceCalls);
                return new ValueTask<IServiceResponse>(new ReadResponse());
            });

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            ValueTask<IServiceResponse> responseTask = TestEndpointBase.ProcessAsyncLocal(incoming);
            TestEndpointBase.SetAge(incoming, TimeSpan.FromSeconds(5));

            await TestEndpointBase.CallAsyncLocal(incoming).ConfigureAwait(false);
            IServiceResponse response = await responseTask.ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(serviceCalls, Is.Zero);
        }

        /// <summary>
        /// A request that is still within its timeout hint at dequeue only gets what is left
        /// of the hint, not the full hint again.
        /// </summary>
        [Test]
        public async Task QueuedRequestRunsWithRemainingTimeoutBudgetAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new ReadRequest { RequestHeader = new RequestHeader { TimeoutHint = 60_000 } };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.AddServiceLocal(req.TypeId, typeof(ReadRequest),
                async (_, _, lifetime) =>
                {
                    started.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.CancellationToken).ConfigureAwait(false);
                    return new ReadResponse();
                });

            // 59.5 of the 60 s are spent: the full hint would outlast the test deadline
            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            TestEndpointBase.SetAge(incoming, TimeSpan.FromMilliseconds(59_500));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IServiceResponse response = await TestEndpointBase.ProcessAsyncLocal(incoming).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(started.Task.IsCompleted, Is.True, "The service runs with the remaining budget.");
            Assert.That(response, Is.InstanceOf<ServiceFault>());
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
        }

        /// <summary>
        /// A held Publish keeps its full timeout hint: the subscription layer applies it from
        /// when the request is handled, so queue time is not charged at dequeue.
        /// </summary>
        [Test]
        public async Task QueuedPublishIsNotExpiredAtDequeueAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var req = new PublishRequest { RequestHeader = new RequestHeader { TimeoutHint = 1000 } };
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            endpoint.AddServiceLocal(req.TypeId, typeof(PublishRequest),
                (_, _, _) => new ValueTask<IServiceResponse>(new PublishResponse()));

            object incoming = endpoint.CreateIncomingRequest(req, ctx);
            TestEndpointBase.SetAge(incoming, TimeSpan.FromSeconds(5));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            IServiceResponse response = await TestEndpointBase.ProcessAsyncLocal(incoming).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(response, Is.InstanceOf<PublishResponse>());
        }

        /// <summary>
        /// A request rejected because the queue is full is faulted with a retry-after hint
        /// in the response header, which the client surfaces on the exception it throws.
        /// </summary>
        [Test]
        public async Task QueueFullFaultCarriesRetryAfterAsync()
        {
            using var server = new TestServer();
            var endpoint = new TestEndpointBase(server);
            var ctx = new SecureChannelContext("1", new EndpointDescription(), RequestEncoding.Binary);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            endpoint.AddServiceLocal(new ReadRequest().TypeId, typeof(ReadRequest),
                async (_, _, lifetime) =>
                {
                    started.TrySetResult(true);
                    await release.Task.WaitAsync(lifetime.CancellationToken).ConfigureAwait(false);
                    return new ReadResponse();
                });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // one request occupies the single worker, ten fill the queue
            var accepted = new Task<IServiceResponse>[11];
            accepted[0] = TestEndpointBase.ProcessAsyncLocal(endpoint.CreateIncomingRequest(
                new ReadRequest { RequestHeader = new RequestHeader() }, ctx)).AsTask();
            await started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            for (int ii = 1; ii < accepted.Length; ii++)
            {
                accepted[ii] = TestEndpointBase.ProcessAsyncLocal(endpoint.CreateIncomingRequest(
                    new ReadRequest { RequestHeader = new RequestHeader() }, ctx)).AsTask();
            }
            IServiceResponse rejected = await TestEndpointBase.ProcessAsyncLocal(endpoint.CreateIncomingRequest(
                new ReadRequest { RequestHeader = new RequestHeader() }, ctx)).AsTask()
                .WaitAsync(deadline.Token).ConfigureAwait(false);
            release.TrySetResult(true);
            await Task.WhenAll(accepted).WaitAsync(deadline.Token).ConfigureAwait(false);

            Assert.That(rejected, Is.InstanceOf<ServiceFault>());
            Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
            TimeSpan? retryAfter = RetryAfterHeader.Read(rejected.ResponseHeader);
            Assert.That(retryAfter, Is.Not.Null);
            Assert.That(retryAfter!.Value, Is.InRange(
                RequestQueueBudget.MinQueueFullRetryAfter, RequestQueueBudget.MaxQueueFullRetryAfter));

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => ResponseValidator.Validate(rejected.ResponseHeader))!;
            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            Assert.That(
                ex.AdditionalInfo,
                Does.Contain(Utils.Format("RetryAfterMs={0}", (long)retryAfter.Value.TotalMilliseconds)));
        }

        /// <summary>
        /// Exposes the client's response validation, which turns a retry-after header into
        /// the token its reconnect policy reads.
        /// </summary>
        private abstract class ResponseValidator : ClientBase
        {
            protected ResponseValidator(ITransportChannel channel, ITelemetryContext telemetry)
                : base(channel, telemetry)
            {
            }

            public static void Validate(ResponseHeader header)
            {
                ValidateResponse(header);
            }
        }
    }
}
