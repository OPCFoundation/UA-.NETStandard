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

using Opc.Ua.Bindings;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Minimal <see cref="IWebApiServer"/> used by
    /// <see cref="WebApiAotFixture"/> to round-trip OPC UA REST
    /// requests without standing up a real OPC UA server. Records the
    /// last invocation so tests can assert on identity / payload
    /// propagation.
    /// </summary>
    public sealed class StubWebApiServer : IWebApiServer
    {
        public StubWebApiServer(IServiceMessageContext context)
        {
            MessageContext = context;
        }

        public IServiceMessageContext MessageContext { get; }
        public bool IsReady => true;
        public IServiceRequest LastRequest { get; private set; }
        public WebApiInvocationContext LastInvocation { get; private set; }

        public ValueTask<IServiceResponse> InvokeAsync(
            IServiceRequest request,
            WebApiInvocationContext context,
            CancellationToken ct)
        {
            LastRequest = request;
            LastInvocation = context;

            IServiceResponse response = BuildTypedResponse(request);
            return new ValueTask<IServiceResponse>(response);
        }

        private static IServiceResponse BuildTypedResponse(IServiceRequest request)
        {
            var header = new ResponseHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = request.RequestHeader?.RequestHandle ?? 0,
                ServiceResult = StatusCodes.Good,
                StringTable = new ArrayOf<string>(),
                AdditionalHeader = new ExtensionObject()
            };
            return request switch
            {
                ReadRequest => new ReadResponse { ResponseHeader = header },
                WriteRequest => new WriteResponse { ResponseHeader = header },
                BrowseRequest => new BrowseResponse { ResponseHeader = header },
                CreateSessionRequest => new CreateSessionResponse
                {
                    ResponseHeader = header,
                    SessionId = new NodeId(Guid.NewGuid()),
                    AuthenticationToken = new NodeId(Guid.NewGuid()),
                    RevisedSessionTimeout = 120000.0,
                    ServerNonce = ByteString.From(0x01, 0x02, 0x03, 0x04),
                    ServerCertificate = ByteString.Empty,
                    ServerEndpoints = new ArrayOf<EndpointDescription>(),
                    ServerSoftwareCertificates =
                        new ArrayOf<SignedSoftwareCertificate>(),
                    MaxRequestMessageSize = 0,
                    ServerSignature = new SignatureData()
                },
                CreateSubscriptionRequest req => new CreateSubscriptionResponse
                {
                    ResponseHeader = header,
                    SubscriptionId = 1u,
                    RevisedPublishingInterval = req.RequestedPublishingInterval,
                    RevisedLifetimeCount = req.RequestedLifetimeCount,
                    RevisedMaxKeepAliveCount = req.RequestedMaxKeepAliveCount
                },
                PublishRequest => new PublishResponse
                {
                    ResponseHeader = header,
                    SubscriptionId = 1u,
                    AvailableSequenceNumbers = new ArrayOf<uint>(),
                    MoreNotifications = false,
                    NotificationMessage = new NotificationMessage
                    {
                        SequenceNumber = 1u,
                        PublishTime = DateTime.UtcNow,
                        NotificationData = new ArrayOf<ExtensionObject>()
                    },
                    Results = new ArrayOf<StatusCode>(),
                    DiagnosticInfos = new ArrayOf<DiagnosticInfo>()
                },
                _ => new ServiceFault { ResponseHeader = header }
            };
        }
    }
}
