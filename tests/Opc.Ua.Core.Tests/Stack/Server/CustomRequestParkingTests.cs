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

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class CustomRequestParkingTests
    {
        [Test]
        public async Task CustomHandlerReleasesOnlyExecutionAndLetsPeerUseTheSingleWorkerAsync()
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider)
            {
                RequestParkingPolicy = new DelegateRequestParkingPolicy(static request => request is CallRequest)
            };
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest(), "held");
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(sink, Is.Not.Null, "An opted-in handler needs the queue's real parking sink.");
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                sink.NotifyParked();
                sink.NotifyParked();
                await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.Zero);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.Zero);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));

                Task<IServiceResponse> peerResponse = endpoint.SendAsync(new ReadRequest(), "peer");
                await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(response.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                peer.Release();
                held.Release();
                Assert.That(await response.WaitAsync(deadline.Token).ConfigureAwait(false), Is.TypeOf<CallResponse>());
                Assert.That(await peerResponse.WaitAsync(deadline.Token).ConfigureAwait(false),
                    Is.TypeOf<ReadResponse>());
                sink.NotifyParked();
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        [Test]
        public async Task SelectedRequestReservesParkedCapacityBeforeDispatchEvenWithoutSignalingAsync()
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> blocker = endpoint.SendAsync(new ReadRequest(), "peer");
            try
            {
                await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Task<IServiceResponse> selected = endpoint.SendAsync(new CallRequest());
                Assert.That(held.Started.Task.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                IServiceResponse rejected = await endpoint.SendAsync(new CallRequest())
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
                peer.Release();
                await blocker.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false), Is.Not.Null);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                rejected = await endpoint.SendAsync(new CallRequest()).WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
                held.Release();
                Assert.That(await selected.WaitAsync(deadline.Token).ConfigureAwait(false), Is.TypeOf<CallResponse>());
                await provider.WaitForReleaseAsync(ResourceIsolationStage.ParkedRequest, 1, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(await endpoint.SendAsync(new CallRequest()).WaitAsync(deadline.Token).ConfigureAwait(false),
                    Is.TypeOf<CallResponse>(), "Completion must return the reserved slot.");
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        [Test]
        public async Task ParkingDoesNotReturnRetainedCostToAdmitAnotherRequestAsync()
        {
            var provider = new TrackingProvider(costCapacity: 10);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                sink.NotifyParked();
                await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                    .ConfigureAwait(false);
                IServiceResponse rejected = await endpoint.SendAsync(new ReadRequest(), "peer")
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(rejected.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerTooBusy));
                Assert.That(peer.Started.Task.IsCompleted, Is.False);
                Assert.That(response.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                held.Release();
                await response.WaitAsync(deadline.Token).ConfigureAwait(false);
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
            }
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(false, true)]
        public async Task CancellationBeforeAndAfterParkingReturnsAllLeasesOnceAsync(bool queued, bool park)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> blocker = null;
            try
            {
                if (queued)
                {
                    blocker = endpoint.SendAsync(new ReadRequest(), "peer");
                    await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                Task<IServiceResponse> response = endpoint.SendAsync(
                    new CallRequest(), cancellationToken: cancellation.Token);
                IRequestParkSink sink = null;
                if (!queued)
                {
                    sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                    if (park)
                    {
                        sink.NotifyParked();
                        await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                            .ConfigureAwait(false);
                    }
                }
                await cancellation.CancelAsync().ConfigureAwait(false);
                IServiceResponse fault = await response.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(fault, Is.TypeOf<ServiceFault>());
                Assert.That(fault.ResponseHeader.ServiceResult,
                    Is.EqualTo(queued ? StatusCodes.BadRequestCancelledByClient : StatusCodes.BadTimeout));
                Assert.That(held.Started.Task.IsCompleted, Is.EqualTo(!queued));
                sink?.NotifyParked();
                peer.Release();
                if (blocker != null)
                {
                    await blocker.WaitAsync(deadline.Token).ConfigureAwait(false);
                }
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        [Test]
        public async Task PreCancelledRequestNeverInvokesHandlerOrAcquiresCapacityAsync()
        {
            var provider = new TrackingProvider();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var endpoint = new TestEndpoint(server, held, new Handler());
            IServiceResponse response = await endpoint.SendAsync(
                new CallRequest(), cancellationToken: cancellation.Token).ConfigureAwait(false);
            Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(held.Started.Task.IsCompleted, Is.False);
            await server.StopAsync().ConfigureAwait(false);
            provider.AssertAllReleasedOnce();
            Assert.That(provider.TotalGrants, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAndStopRetainParkedCapacityUntilActualCompletionAsync(bool stop)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler { ObserveCancellation = false };
            var endpoint = new TestEndpoint(server, held, new Handler());
            Task<IServiceResponse> response = endpoint.SendAsync(
                new CallRequest(), cancellationToken: cancellation.Token);
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                sink.NotifyParked();
                await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                    .ConfigureAwait(false);
                Task stopping = null;
                if (stop)
                {
                    stopping = server.StopAsync(deadline.Token).AsTask();
                }
                else
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                }
                await held.Cancelled.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                sink.NotifyParked();
                Assert.That(response.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.Zero);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                if (stopping != null)
                {
                    Assert.That(stopping.IsCompleted, Is.False);
                }
                held.Release();
                Assert.That(await response.WaitAsync(deadline.Token).ConfigureAwait(false), Is.TypeOf<CallResponse>());
                if (stopping != null)
                {
                    await stopping.ConfigureAwait(false);
                }
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HandlerExceptionBeforeOrAfterParkingFaultsOnceAndReturnsCapacityAsync(bool park)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler { ThrowOnCompletion = true };
            var endpoint = new TestEndpoint(server, held, new Handler());
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (park)
                {
                    sink.NotifyParked();
                    await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                        .ConfigureAwait(false);
                }
                held.Release();
                IServiceResponse fault = await response.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(fault, Is.TypeOf<ServiceFault>());
                Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
                sink.NotifyParked();
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
            }
        }

        [Test]
        public async Task StopCancelsRunningParkedAndQueuedEndpointRequestsAsync()
        {
            var provider = new TrackingProvider(parkedCapacity: 2);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> parked = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                sink.NotifyParked();
                Task<IServiceResponse> running = endpoint.SendAsync(new ReadRequest(), "peer");
                await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Task<IServiceResponse> queued = endpoint.SendAsync(new CallRequest());
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                Assert.That((await parked.WaitAsync(deadline.Token).ConfigureAwait(false))
                    .ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That((await running.WaitAsync(deadline.Token).ConfigureAwait(false))
                    .ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadTimeout));
                Assert.That((await queued.WaitAsync(deadline.Token).ConfigureAwait(false))
                    .ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerHalted));
                Assert.That((await endpoint.SendAsync(new CallRequest()).ConfigureAwait(false))
                    .ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadServerHalted));
                sink.NotifyParked();
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public async Task NonparticipantsAndGlobalOptOutKeepTheExecutionWorkerAsync(bool installPolicy, bool decouple)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IRequestParkingPolicy policy = installPolicy
                ? new DelegateRequestParkingPolicy(_ => !decouple)
                : null;
            await using var server = new QueueServer(provider, decouple, policy);
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(sink is null, Is.EqualTo(decouple));
                sink?.NotifyParked();
                Task<IServiceResponse> peerResponse = endpoint.SendAsync(new ReadRequest(), "peer");
                Assert.That(peer.Started.Task.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.Zero);
                held.Release();
                await response.WaitAsync(deadline.Token).ConfigureAwait(false);
                await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                peer.Release();
                await peerResponse.WaitAsync(deadline.Token).ConfigureAwait(false);
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PublishRemainsIntrinsicAndBypassesCustomPolicyAsync(bool installPolicy)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IRequestParkingPolicy policy = installPolicy
                ? new DelegateRequestParkingPolicy(static _ => throw new InvalidOperationException("Not for Publish."))
                : null;
            await using var server = new QueueServer(provider, policy: policy);
            var held = new Handler();
            var endpoint = new TestEndpoint(server, held, new Handler());
            Task<IServiceResponse> response = endpoint.SendAsync(new PublishRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(sink, Is.Not.Null);
                sink.NotifyParked();
                await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(response.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                held.Release();
                Assert.That(await response.WaitAsync(deadline.Token).ConfigureAwait(false),
                    Is.TypeOf<PublishResponse>());
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
            }
        }

        [Test]
        public async Task PolicyFailureReturnsFaultBeforeAdmissionAndDoesNotStopTheWorkerAsync()
        {
            var provider = new TrackingProvider();
            await using var server = new QueueServer(provider, policy:
                new DelegateRequestParkingPolicy(static _ => throw new InvalidOperationException("Policy failure.")));
            var held = new Handler();
            var peer = new Handler();
            peer.Release();
            var endpoint = new TestEndpoint(server, held, peer);
            IServiceResponse fault = await endpoint.SendAsync(new CallRequest()).ConfigureAwait(false);
            Assert.That(fault, Is.TypeOf<ServiceFault>());
            Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(held.Started.Task.IsCompleted, Is.False);
            Assert.That(provider.TotalGrants, Is.Zero);
            server.RequestParkingPolicy = null;
            Assert.That(await endpoint.SendAsync(new ReadRequest()).ConfigureAwait(false), Is.TypeOf<ReadResponse>());
            await server.StopAsync().ConfigureAwait(false);
            provider.AssertAllReleasedOnce();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExistingServerInterfaceCanOptionallySupplyParkingPolicyAsync(bool supplyPolicy)
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var queueServer = new QueueServer(provider);
            var server = new Mock<IServerBase>();
            server.SetupGet(value => value.MessageContext).Returns(
                ServiceMessageContext.Create(NUnitTelemetryContext.Create()));
            var policy = CustomPolicy();
            if (supplyPolicy)
            {
                server.As<IRequestParkingPolicySource>().SetupGet(value => value.RequestParkingPolicy).Returns(policy);
            }
            server.Setup(value => value.ScheduleIncomingRequest(
                It.IsAny<IEndpointIncomingRequest>(), It.IsAny<CancellationToken>()))
                .Callback<IEndpointIncomingRequest, CancellationToken>(queueServer.ScheduleIncomingRequest);
            var host = new Mock<IServiceHostBase>();
            host.SetupGet(value => value.Server).Returns(server.Object);
            var held = new Handler();
            var endpoint = new TestEndpoint(host.Object, held, new Handler());
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(sink is not null, Is.EqualTo(supplyPolicy));
                Assert.That(endpoint.RequestParkingPolicy, Is.SameAs(supplyPolicy ? policy : null));
                sink?.NotifyParked();
                held.Release();
                Assert.That(await response.WaitAsync(deadline.Token).ConfigureAwait(false), Is.TypeOf<CallResponse>());
                await queueServer.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
            }
        }

        [Test]
        public void DelegatePolicyRejectsNullAndSelectsByRequestContents()
        {
            Assert.That(() => new DelegateRequestParkingPolicy(null), Throws.ArgumentNullException);
            var policy = new DelegateRequestParkingPolicy(
                request => request.TypeId == DataTypeIds.CallRequest && request.RequestHeader.RequestHandle == 42);
            Assert.That(() => policy.CanPark(null), Throws.ArgumentNullException);
            Assert.That(policy.CanPark(new ReadRequest { RequestHeader = new RequestHeader { RequestHandle = 42 } }),
                Is.False);
            Assert.That(policy.CanPark(new CallRequest { RequestHeader = new RequestHeader { RequestHandle = 1 } }),
                Is.False);
            Assert.That(policy.CanPark(new CallRequest { RequestHeader = new RequestHeader { RequestHandle = 42 } }),
                Is.True);
        }

        [Test]
        public async Task PublicIncomingRequestUsesTheSameParkingAndCompletionLifecycleAsync()
        {
            var provider = new TrackingProvider();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider);
            var direct = new DirectRequest();
            server.ScheduleIncomingRequest(direct);
            try
            {
                await direct.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                direct.ParkSink.NotifyParked();
                direct.ParkSink.NotifyParked();
                await provider.WaitForReleaseAsync(ResourceIsolationStage.RequestExecution, 1, deadline.Token)
                    .ConfigureAwait(false);
                Assert.That(direct.Completed.Task.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(10));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                direct.Release();
                Assert.That(await direct.Completed.Task.WaitAsync(deadline.Token).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                direct.ParkSink.NotifyParked();
                Assert.That(direct.CompletionCount, Is.EqualTo(1));
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                direct.Release();
            }
        }

        [Test]
        public async Task CustomParkingReleasesFifoWorkerWithoutBypassingProviderAccountingAsync()
        {
            var provider = new TrackingProvider { UseFairScheduling = false };
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var server = new QueueServer(provider, policy: CustomPolicy());
            var held = new Handler();
            var peer = new Handler();
            var endpoint = new TestEndpoint(server, held, peer);
            Task<IServiceResponse> response = endpoint.SendAsync(new CallRequest());
            try
            {
                IRequestParkSink sink = await held.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                sink.NotifyParked();
                Task<IServiceResponse> peerResponse = endpoint.SendAsync(new ReadRequest(), "peer");
                await peer.Started.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.That(response.IsCompleted, Is.False);
                Assert.That(provider.Used(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueue), Is.Zero);
                Assert.That(provider.Used(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(20));
                Assert.That(provider.Used(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
                held.Release();
                peer.Release();
                Assert.That(await response.WaitAsync(deadline.Token).ConfigureAwait(false), Is.TypeOf<CallResponse>());
                Assert.That(await peerResponse.WaitAsync(deadline.Token).ConfigureAwait(false),
                    Is.TypeOf<ReadResponse>());
                await server.StopAsync(deadline.Token).ConfigureAwait(false);
                provider.AssertAllReleasedOnce();
            }
            finally
            {
                held.Release();
                peer.Release();
            }
        }

        private static DelegateRequestParkingPolicy CustomPolicy()
        {
            return new DelegateRequestParkingPolicy(static request => request is CallRequest);
        }

        private sealed class TestEndpoint : EndpointBase
        {
            public TestEndpoint(QueueServer server, Handler held, Handler peer)
                : base(server)
            {
                RegisterServices(held, peer);
            }

            public TestEndpoint(IServiceHostBase host, Handler held, Handler peer)
                : base(host)
            {
                RegisterServices(held, peer);
            }

            public Task<IServiceResponse> SendAsync(
                IServiceRequest request,
                string channel = "held",
                CancellationToken cancellationToken = default)
            {
                request.RequestHeader = new RequestHeader();
                return ProcessRequestAsync(
                    new SecureChannelContext(channel, new EndpointDescription(), RequestEncoding.Binary),
                    request,
                    cancellationToken).AsTask();
            }

            private void RegisterServices(Handler held, Handler peer)
            {
                SupportedServices[DataTypeIds.CallRequest] = new ServiceDefinition(
                    typeof(CallRequest), held.InvokeAsync);
                SupportedServices[DataTypeIds.ReadRequest] = new ServiceDefinition(
                    typeof(ReadRequest), peer.InvokeAsync);
                SupportedServices[DataTypeIds.PublishRequest] = new ServiceDefinition(
                    typeof(PublishRequest), held.InvokeAsync);
            }
        }

        private sealed class Handler
        {
            public bool ObserveCancellation { get; init; } = true;

            public bool ThrowOnCompletion { get; init; }

            public TaskCompletionSource<IRequestParkSink> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Cancelled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                SecureChannelContext context,
                RequestLifetime lifetime)
            {
                using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(
                    () => Cancelled.TrySetResult(true));
                Started.TrySetResult(lifetime.ParkSink);
                await m_release.Task.WaitAsync(
                    ObserveCancellation ? lifetime.CancellationToken : CancellationToken.None)
                    .ConfigureAwait(false);
                if (ThrowOnCompletion)
                {
                    throw new InvalidOperationException("Handler failure.");
                }
                return request switch
                {
                    CallRequest => new CallResponse(),
                    PublishRequest => new PublishResponse(),
                    _ => new ReadResponse()
                };
            }

            public void Release()
            {
                m_release.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class QueueServer : ServerBase, IServerBase, IAsyncDisposable
        {
            public QueueServer(
                TrackingProvider provider,
                bool decouple = true,
                IRequestParkingPolicy policy = null)
                : base(NUnitTelemetryContext.Create(), null, policy)
            {
                m_context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                m_queue = new RequestQueue(this, 1, 1, 10, decouple, provider, 10);
            }

            IServiceMessageContext IServerBase.MessageContext => m_context;

            public override void ScheduleIncomingRequest(
                IEndpointIncomingRequest request,
                CancellationToken cancellationToken = default)
            {
                m_queue.ScheduleIncomingRequest(request, cancellationToken);
            }

            public override ValueTask StopAsync(CancellationToken cancellationToken = default)
            {
                return m_queue.StopAsync(cancellationToken);
            }

            public async ValueTask DisposeAsync()
            {
                await m_queue.StopAsync().ConfigureAwait(false);
                Dispose();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_queue.Dispose();
                }
                base.Dispose(disposing);
            }

            private readonly IServiceMessageContext m_context;
            private readonly RequestQueue m_queue;
        }

        private sealed class DirectRequest : IParkableIncomingRequest
        {
            public SecureChannelContext SecureChannelContext { get; } =
                new("held", new EndpointDescription(), RequestEncoding.Binary);

            public IServiceRequest Request { get; } = new CallRequest { RequestHeader = new RequestHeader() };

            public RequestParkSink ParkSink { get; } = new();

            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<StatusCode> Completed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int CompletionCount => Volatile.Read(ref m_completionCount);

            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                Started.TrySetResult(true);
                await m_release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                OperationCompleted(new CallResponse(), ServiceResult.Good);
            }

            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                Interlocked.Increment(ref m_completionCount);
                Completed.TrySetResult(error.StatusCode);
            }

            public void Release()
            {
                m_release.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_completionCount;
        }

        private sealed class TrackingProvider : IServerResourceIsolationProvider
        {
            public TrackingProvider(long parkedCapacity = 1, long costCapacity = 100)
            {
                int stages = (int)ResourceIsolationStage.ParkedRequest + 1;
                m_capacity = new long[stages];
                m_used = new long[stages];
                m_grants = new int[stages];
                m_releases = new int[stages];
                for (int ii = 0; ii < stages; ii++)
                {
                    m_capacity[ii] = 100;
                }
                m_capacity[(int)ResourceIsolationStage.RequestExecution] = 1;
                m_capacity[(int)ResourceIsolationStage.ParkedRequest] = parkedCapacity;
                m_capacity[(int)ResourceIsolationStage.RequestQueueBytes] = costCapacity;
                m_held = new ResourceIsolationOwner("held", ResourceIsolationClass.Established, 1, m_capacity);
                m_peer = new ResourceIsolationOwner("peer", ResourceIsolationClass.Established, 1, m_capacity);
            }

            public bool UseFairScheduling { get; init; } = true;

            public int TotalGrants
            {
                get
                {
                    lock (m_gate)
                    {
                        int total = 0;
                        foreach (int grants in m_grants)
                        {
                            total += grants;
                        }
                        return total;
                    }
                }
            }

            public event Action<ResourceIsolationStage> CapacityAvailable;

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint remoteEndpoint)
            {
                return m_held;
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return channelContext.SecureChannelId == "peer" ? m_peer : m_held;
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                return ReferenceEquals(owner, Classify(channelContext));
            }

            public bool TryAcquire(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                [NotNullWhen(true)] out IDisposable lease,
                out ResourceIsolationFailure failure)
            {
                lock (m_gate)
                {
                    if (amount > m_capacity[(int)stage] - m_used[(int)stage])
                    {
                        lease = null;
                        failure = new ResourceIsolationFailure(ResourceIsolationFailureReason.Capacity, TimeSpan.Zero);
                        return false;
                    }
                    m_used[(int)stage] += amount;
                    m_grants[(int)stage]++;
                    lease = new Lease(this, stage, amount);
                    failure = default;
                    return true;
                }
            }

            public long Used(ResourceIsolationStage stage)
            {
                lock (m_gate)
                {
                    return m_used[(int)stage];
                }
            }

            public async Task WaitForReleaseAsync(
                ResourceIsolationStage stage,
                int count,
                CancellationToken cancellationToken)
            {
                var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CapacityAvailable += OnReleased;
                try
                {
                    OnReleased(stage);
                    await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CapacityAvailable -= OnReleased;
                }

                void OnReleased(ResourceIsolationStage releasedStage)
                {
                    lock (m_gate)
                    {
                        if (releasedStage == stage && m_releases[(int)stage] >= count)
                        {
                            released.TrySetResult(true);
                        }
                    }
                }
            }

            public void AssertAllReleasedOnce()
            {
                lock (m_gate)
                {
                    Assert.That(m_used, Is.All.Zero);
                    Assert.That(m_releases, Is.EqualTo(m_grants));
                    Assert.That(m_duplicateDisposals, Is.Zero);
                }
            }

            private void Release(ResourceIsolationStage stage, long amount)
            {
                lock (m_gate)
                {
                    m_used[(int)stage] -= amount;
                    m_releases[(int)stage]++;
                }
                CapacityAvailable?.Invoke(stage);
            }

            private sealed class Lease(
                TrackingProvider provider, ResourceIsolationStage stage, long amount) : IDisposable
            {
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        provider.Release(stage, amount);
                    }
                    else
                    {
                        Interlocked.Increment(ref provider.m_duplicateDisposals);
                    }
                }

                private int m_disposed;
            }

            private readonly Lock m_gate = new();
            private readonly long[] m_capacity;
            private readonly long[] m_used;
            private readonly int[] m_grants;
            private readonly int[] m_releases;
            private readonly ResourceIsolationOwner m_held;
            private readonly ResourceIsolationOwner m_peer;
            private int m_duplicateDisposals;
        }
    }
}
