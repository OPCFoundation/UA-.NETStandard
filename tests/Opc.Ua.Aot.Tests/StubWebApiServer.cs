/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

        public IServiceRequest LastRequest => m_lastRequest ??
            throw new InvalidOperationException("The WebApi fixture has not received a request.");

        public WebApiInvocationContext LastInvocation => m_lastInvocation ??
            throw new InvalidOperationException("The WebApi fixture has not received an invocation.");

        public ValueTask<IServiceResponse> InvokeAsync(
            IServiceRequest request,
            WebApiInvocationContext context,
            CancellationToken ct)
        {
            m_lastRequest = request;
            m_lastInvocation = context;

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

        /// <summary>The request recorded by the most recent invocation.</summary>
        private IServiceRequest? m_lastRequest;

        /// <summary>The context recorded by the most recent invocation.</summary>
        private WebApiInvocationContext? m_lastInvocation;
    }
}
