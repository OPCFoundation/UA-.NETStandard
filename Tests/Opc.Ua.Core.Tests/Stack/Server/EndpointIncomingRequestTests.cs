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
        private sealed class TestServer : ServerBase
        {
            public TestServer()
                : base(NUnitTelemetryContext.Create(true))
            {
                FieldInfo field = typeof(ServerBase).GetField(
                    "m_messageContext",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field.SetValue(this, ServiceMessageContext.Create(NUnitTelemetryContext.Create(true)));
            }

            public Action<IEndpointIncomingRequest> OnScheduleIncomingRequest { get; set; }

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

            public static ValueTask CallAsyncLocal(object incomingRequest)
            {
                return ((EndpointIncomingRequest)incomingRequest).CallAsync();
            }

            public static void OperationCompletedLocal(object incomingRequest, IServiceResponse response, ServiceResult error)
            {
                var req = (EndpointIncomingRequest)incomingRequest;
                req.OperationCompleted(response, error);
            }

            public void TestEquality(IServiceRequest req1, IServiceRequest req2, IServiceRequest req3)
            {
                var r1 = new EndpointIncomingRequest(this, null, req1);
                var r2 = new EndpointIncomingRequest(this, null, req2);
                var r3 = new EndpointIncomingRequest(this, null, req3);
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
    }
}
