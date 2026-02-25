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
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for UA endpoints.
    /// </summary>
    public abstract class EndpointBase : IEndpointBase, ITransportListenerCallback
    {
        /// <summary>
        /// Initializes the object when it is created by the WCF framework.
        /// </summary>
        protected EndpointBase()
        {
            SupportedServices = [];

            try
            {
                m_host = GetHostForContext();
                m_server = GetServerForContext();
                m_logger = MessageContext.Telemetry.CreateLogger(this);

                EndpointDescription = GetEndpointDescription();
            }
            catch (Exception e)
            {
                ServerError = new ServiceResult(e);
                EndpointDescription = null;

                m_host = null;
                m_server = null;
            }
        }

        /// <summary>
        /// Initializes the object when it is created directly.
        /// </summary>
        /// <param name="host">The host.</param>
        protected EndpointBase(IServiceHostBase host)
        {
            m_host = host ?? throw new ArgumentNullException(nameof(host));
            m_server = host.Server;
            m_logger = MessageContext.Telemetry.CreateLogger(this);

            SupportedServices = [];
        }

        /// <summary>
        /// Initializes the endpoint with a server instead of a host.
        /// </summary>
        protected EndpointBase(ServerBase server)
        {
            m_host = null;
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = MessageContext.Telemetry.CreateLogger(this);

            SupportedServices = [];
        }

        /// <inheritdoc/>
        public ValueTask<IServiceResponse> ProcessRequestAsync(
            SecureChannelContext secureChannelContext,
            IServiceRequest request,
            CancellationToken cancellationToken = default)
        {
            if (secureChannelContext == null)
            {
                throw new ArgumentNullException(nameof(secureChannelContext));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var incomingRequest = new EndpointIncomingRequest(this, secureChannelContext, request);
            return incomingRequest.ProcessAsync(cancellationToken);
        }

        /// <summary>
        /// Trys to get the secure channel id for an AuthenticationToken.
        /// The ChannelId is known to the sessions of the Server.
        /// Each session has an AuthenticationToken which can be used to identify the session.
        /// </summary>
        /// <param name="authenticationToken">The AuthenticationToken from the RequestHeader</param>
        /// <param name="channelId">The Channel id</param>
        /// <returns>returns true if a channelId was found for the provided AuthenticationToken</returns>
        public bool TryGetSecureChannelIdForAuthenticationToken(
            NodeId authenticationToken,
            out uint channelId)
        {
            return m_server.TryGetSecureChannelIdForAuthenticationToken(
                authenticationToken,
                out channelId);
        }

        /// <inheritdoc/>
        public void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            // trigger the reporting of AuditOpenSecureChannelEventType
            ServerForContext?.ReportAuditOpenSecureChannelEvent(
                globalChannelId,
                endpointDescription,
                request,
                clientCertificate,
                exception);
        }

        /// <inheritdoc/>
        public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
        {
            // trigger the reporting of close AuditChannelEventType
            ServerForContext?.ReportAuditCloseSecureChannelEvent(globalChannelId, exception);
        }

        /// <inheritdoc/>
        public void ReportAuditCertificateEvent(
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            // trigger the reporting of OpenSecureChannelAuditEvent
            ServerForContext?.ReportAuditCertificateEvent(clientCertificate, exception);
        }

        /// <summary>
        /// Tries to extract the trace details from the AdditionalParametersType.
        /// </summary>
        public static bool TryExtractActivityContextFromParameters(
            AdditionalParametersType parameters,
            out ActivityContext activityContext)
        {
            if (parameters == null)
            {
                activityContext = default;
                return false;
            }

            foreach (KeyValuePair item in parameters.Parameters)
            {
                if (item.Key != "SpanContext")
                {
                    continue;
                }
                if (item.Value.TryGetStructure(out SpanContextDataType spanContext))
                {
#if NET8_0_OR_GREATER
                    Span<byte> spanIdBytes = stackalloc byte[8];
                    Span<byte> traceIdBytes = stackalloc byte[16];
                    ((Guid)spanContext.TraceId).TryWriteBytes(traceIdBytes);
                    BitConverter.TryWriteBytes(spanIdBytes, spanContext.SpanId);
#else
                    byte[] spanIdBytes = BitConverter.GetBytes(spanContext.SpanId);
                    byte[] traceIdBytes = ((Guid)spanContext.TraceId).ToByteArray();
#endif
                    var traceId = ActivityTraceId.CreateFromBytes(traceIdBytes);
                    var spanId = ActivitySpanId.CreateFromBytes(spanIdBytes);
                    // TODO: should also come from header
                    const ActivityTraceFlags traceFlags = ActivityTraceFlags.None;
                    activityContext = new ActivityContext(traceId, spanId, traceFlags);
                    return true;
                }
                break;
            }

            activityContext = default;
            return false;
        }

        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="incoming">Incoming request.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use ProcessRequestAsync instead.")]
        public virtual IServiceResponse ProcessRequest(
            IServiceRequest incoming,
            SecureChannelContext secureChannelContext)
        {
            try
            {
                SetRequestContext(RequestEncoding.Binary);

                // find service.
                if (!SupportedServices.TryGetValue(incoming.TypeId, out ServiceDefinition service))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServiceUnsupported,
                        Utils.Format(
                            "'{0}' is an unrecognized service identifier.",
                            incoming.TypeId));
                }

                // invoke service.
                return service.Invoke(incoming, secureChannelContext, m_logger);
            }
            catch (Exception e)
            {
                // create fault.
                return CreateFault(incoming, e);
            }
        }

        /// <summary>
        /// Asynchronously dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="incoming">Incoming request.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task<IServiceResponse> ProcessRequestAsync(
            IServiceRequest incoming,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetRequestContext(RequestEncoding.Binary);

                // find service.
                if (!SupportedServices.TryGetValue(incoming.TypeId, out ServiceDefinition service))
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported, Utils
                        .Format("'{0}' is an unrecognized service identifier.", incoming.TypeId));
                }

                // invoke service.
                return await service.InvokeAsync(incoming, secureChannelContext, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // create fault.
                return CreateFault(incoming, e);
            }
        }

        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task<InvokeServiceResponseMessage> InvokeServiceAsync(
            InvokeServiceMessage request,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken)
        {
            try
            {
                // check for bad data.
                if (request == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                // set the request context.
                SetRequestContext(RequestEncoding.Binary);

                // decoding incoming message.
                var serviceRequest =
                    BinaryDecoder.DecodeMessage(
                        request.InvokeServiceRequest,
                        null,
                        MessageContext) as IServiceRequest;

                // process the request.
                IServiceResponse response = await ProcessRequestAsync(
                    secureChannelContext,
                    serviceRequest,
                    cancellationToken).ConfigureAwait(false);

                // encode the response.
                return new InvokeServiceResponseMessage
                {
                    InvokeServiceResponse = BinaryEncoder.EncodeMessage(response, MessageContext)
                };
            }
            catch (Exception e)
            {
                // create fault.
                ServiceFault fault = CreateFault(null, e);

                // encode the fault as a response.
                return new InvokeServiceResponseMessage
                {
                    InvokeServiceResponse = BinaryEncoder.EncodeMessage(fault, MessageContext)
                };
            }
        }

        /// <summary>
        /// Returns the host associated with the current context.
        /// </summary>
        /// <value>The host associated with the current context.</value>
        protected IServiceHostBase HostForContext => m_host ??= GetHostForContext();

        /// <summary>
        /// Returns the host associated with the current context.
        /// </summary>
        /// <returns>The host associated with the current context.</returns>
        /// <exception cref="ServiceResultException"></exception>
        protected static IServiceHostBase GetHostForContext()
        {
            throw new ServiceResultException(
                StatusCodes.BadInternalError,
                "The endpoint is not associated with a host that supports IServerHostBase.");
        }

        /// <summary>
        /// Gets the server object from the operation context.
        /// </summary>
        /// <value>The server object from the operation context.</value>
        protected IServerBase ServerForContext => m_server ??= GetServerForContext();

        /// <summary>
        /// Gets the server object from the operation context.
        /// </summary>
        /// <returns>The server object from the operation context.</returns>
        /// <exception cref="ServiceResultException"></exception>
        protected IServerBase GetServerForContext()
        {
            // get the server associated with the host.
            IServerBase server =
                HostForContext.Server
                ?? throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The endpoint is not associated with a server instance.");

            // check the server status.
            if (ServiceResult.IsBad(server.ServerError))
            {
                throw new ServiceResultException(server.ServerError);
            }

            return server;
        }

        /// <summary>
        /// Find the endpoint description for the endpoint.
        /// </summary>
        protected EndpointDescription GetEndpointDescription()
        {
            return null;
        }

        /// <summary>
        /// Finds the service identified by the request type.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected ServiceDefinition FindService(ExpandedNodeId requestTypeId)
        {
            if (!SupportedServices.TryGetValue(requestTypeId, out ServiceDefinition service))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadServiceUnsupported,
                    "'{0}' is an unrecognized service identifier.",
                    requestTypeId);
            }

            return service;
        }

        /// <summary>
        /// Create a fault message
        /// </summary>
        /// <param name="request"></param>
        /// <param name="exception"></param>
        /// <returns></returns>
        protected ServiceFault CreateFault(IServiceRequest request, Exception exception)
        {
            return CreateFault(m_logger, request, exception);
        }

        /// <summary>
        /// Creates a fault message.
        /// </summary>
        /// <param name="logger">A contextual logger to log to</param>
        /// <param name="request">The request.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>A fault message.</returns>
        public static ServiceFault CreateFault(ILogger logger, IServiceRequest request, Exception exception)
        {
            DiagnosticsMasks diagnosticsMask = DiagnosticsMasks.ServiceNoInnerStatus;

            var fault = new ServiceFault();

            if (request != null)
            {
                fault.ResponseHeader.Timestamp = DateTime.UtcNow;
                fault.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;

                if (request.RequestHeader != null)
                {
                    diagnosticsMask = (DiagnosticsMasks)request.RequestHeader.ReturnDiagnostics;
                }
            }

            ServiceResult result;
            if (exception is ServiceResultException sre)
            {
                result = new ServiceResult(sre);
                if (sre.StatusCode == StatusCodes.BadNoSubscription ||
                    sre.StatusCode == StatusCodes.BadSessionClosed ||
                    sre.StatusCode == StatusCodes.BadSecurityChecksFailed ||
                    sre.StatusCode == StatusCodes.BadCertificateInvalid ||
                    sre.StatusCode == StatusCodes.BadServerHalted)
                {
                    // Log debug instead of warning for expected disconnection scenarios
                    logger.LogDebug(
                        "SERVER - Service Fault Occurred. Reason={StatusCode}",
                        result.StatusCode);
                }
                else if (sre.StatusCode == StatusCodes.BadUnexpectedError)
                {
                    logger.LogWarning(
                        Utils.TraceMasks.StackTrace,
                        sre,
                        "SERVER - Service Fault Occurred due to unexpected state");
                }
                else
                {
                    logger.LogWarning(
                        "SERVER - Service Fault Occurred. Reason={StatusCode}",
                        result.StatusCode);
                }
            }
            else
            {
                result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);
                logger.LogError(
                    exception,
                    "SERVER - Unexpected Service Fault: {Message}",
                    exception.Message);
            }

            fault.ResponseHeader.ServiceResult = result.Code;

            var stringTable = new StringTable();

            fault.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo(
                result,
                diagnosticsMask,
                true,
                stringTable,
                logger);

            fault.ResponseHeader.StringTable = stringTable.ToArray();

            return fault;
        }

        /// <summary>
        /// Returns the message context used by the server associated with the endpoint.
        /// </summary>
        /// <value>The message context.</value>
        protected IServiceMessageContext MessageContext => m_server.MessageContext;

        /// <summary>
        /// Returns the description for the endpoint
        /// </summary>
        /// <value>The endpoint description.</value>
        protected EndpointDescription EndpointDescription { get; set; }

        /// <summary>
        /// Returns the error of the server.
        /// </summary>
        /// <value>The server error.</value>
        protected ServiceResult ServerError { get; set; }

        /// <summary>
        /// The types of services known to the server.
        /// </summary>
        protected Dictionary<ExpandedNodeId, ServiceDefinition> SupportedServices { get; set; }

        /// <summary>
        /// Sets the request context for the thread.
        /// </summary>
        /// <param name="encoding">The encoding.</param>
        protected virtual void SetRequestContext(RequestEncoding encoding)
        {
        }

        /// <summary>
        /// Called when a new request is received by the endpoint.
        /// </summary>
        /// <param name="request">The request.</param>
        protected virtual void OnRequestReceived(IServiceRequest request)
        {
        }

        /// <summary>
        /// Called when a response sent via the endpoint.
        /// </summary>
        /// <param name="response">The response.</param>
        protected virtual void OnResponseSent(IServiceResponse response)
        {
        }

        /// <summary>
        /// Called when a response fault sent via the endpoint.
        /// </summary>
        /// <param name="fault">The fault.</param>
        protected virtual void OnResponseFaultSent(Exception fault)
        {
        }

        /// <summary>
        /// Stores the definition of a service supported by the server.
        /// </summary>
        protected class ServiceDefinition
        {
            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="invokeService">The async invoke method.</param>
            public ServiceDefinition(Type requestType, InvokeService invokeService)
            {
                RequestType = requestType;
                m_invokeServiceAsync = invokeService;
            }

            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="invokeMethod">The invoke method.</param>
            [Obsolete("Use constructor taking an InvokeService delegate.")]
            public ServiceDefinition(
                Type requestType,
                InvokeServiceEventHandler invokeMethod)
            {
                RequestType = requestType;
                m_invokeServiceAsync = (request, ctx, ct)
                    => new ValueTask<IServiceResponse>(invokeMethod(request, ctx));
            }

            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="asyncInvokeMethod">The async invoke method.</param>
            [Obsolete("Use constructor taking an InvokeService delegate.")]
            public ServiceDefinition(
                Type requestType,
                InvokeServiceAsyncEventHandler asyncInvokeMethod)
            {
                RequestType = requestType;
                m_invokeServiceAsync = async (request, ctx, ct)
                    => await asyncInvokeMethod(request, ctx, ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="invokeMethod">The invoke method.</param>
            /// <param name="asyncInvokeMethod">The async invoke method.</param>
            [Obsolete("Use constructor taking an InvokeService delegate.")]
            public ServiceDefinition(
                Type requestType,
                InvokeServiceEventHandler invokeMethod,
                InvokeServiceAsyncEventHandler asyncInvokeMethod)
            {
                RequestType = requestType;
                if (invokeMethod != null)
                {
                    m_invokeServiceAsync = (request, ctx, ct)
                        => new ValueTask<IServiceResponse>(invokeMethod(request, ctx));
                }
                else
                {
                    m_invokeServiceAsync = async (request, ctx, ct)
                        => await asyncInvokeMethod(request, ctx, ct).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the request.</value>
            public Type RequestType { get; }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the response.</value>
            public Type ResponseType => RequestType;

            /// <summary>
            /// Processes the request.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <param name="secureChannelContext">The secure channel context.</param>
            /// <param name="logger">A contextual logger to log to</param>
            [Obsolete("Use InvokeAsync.")]
            public IServiceResponse Invoke(
                IServiceRequest request,
                SecureChannelContext secureChannelContext,
                ILogger logger)
            {
                logger.LogWarning(
                    "Async Service invoked sychronously. Prefer using InvokeAsync for best performance.");
                return InvokeAsync(request, secureChannelContext).AsTask().GetAwaiter().GetResult();
            }

            /// <summary>
            /// Processes the request asynchronously.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <param name="secureChannelContext">The secure channel context.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns></returns>
            public ValueTask<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                SecureChannelContext secureChannelContext,
                CancellationToken cancellationToken = default)
            {
                return m_invokeServiceAsync(request, secureChannelContext, cancellationToken);
            }

            private readonly InvokeService m_invokeServiceAsync;
        }

        /// <summary>
        /// A delegate used to asynchronously dispatch incoming service requests.
        /// </summary>
        protected delegate ValueTask<IServiceResponse> InvokeService(
            IServiceRequest request,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// A delegate used to dispatch incoming service requests.
        /// </summary>
        [Obsolete("Use InvokeService delegate.")]
        protected delegate IServiceResponse InvokeServiceEventHandler(
            IServiceRequest request,
            SecureChannelContext secureChannelContext);

        /// <summary>
        /// A delegate used to asynchronously dispatch incoming service requests.
        /// </summary>
        [Obsolete("Use InvokeService delegate.")]
        protected delegate Task<IServiceResponse> InvokeServiceAsyncEventHandler(
            IServiceRequest request,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// An object that handles an incoming request for an endpoint.
        /// </summary>
        protected readonly struct EndpointIncomingRequest : IEndpointIncomingRequest, IEquatable<EndpointIncomingRequest>
        {
            /// <summary>
            /// Initialize the Object with a Request
            /// </summary>
            public EndpointIncomingRequest(
                EndpointBase endpoint,
                SecureChannelContext context,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                m_endpoint = endpoint;
                SecureChannelContext = context;
                Request = request;
                m_vts = ServiceResponsePooledValueTaskSource.Create();
                m_cancellationToken = cancellationToken;
            }

            /// <inheritdoc/>
            public SecureChannelContext SecureChannelContext { get; }

            /// <inheritdoc/>
            public IServiceRequest Request { get; }

            /// <summary>
            /// Process an incoming request
            /// </summary>
            /// <returns></returns>
            public ValueTask<IServiceResponse> ProcessAsync(CancellationToken cancellationToken = default)
            {
                try
                {
                    m_endpoint.ServerForContext.ScheduleIncomingRequest(this, cancellationToken);
                }
                catch (Exception e)
                {
                    m_vts.SetResult(m_endpoint.CreateFault(Request, e));
                }

                return m_vts.Task;
            }

            /// <inheritdoc/>
            public async ValueTask CallAsync(CancellationToken cancellationToken = default)
            {
                using CancellationTokenSource timeoutHintCts = (int)Request.RequestHeader.TimeoutHint > 0 ?
                    new CancellationTokenSource((int)Request.RequestHeader.TimeoutHint) : null;

                CancellationToken[] tokens = timeoutHintCts != null ?
                    [m_cancellationToken, cancellationToken, timeoutHintCts.Token] :
                    [m_cancellationToken, cancellationToken];

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(tokens);

                try
                {
                    Activity activity = null;
                    ActivitySource activitySource = m_endpoint.MessageContext.Telemetry
                        .GetActivitySource();
                    if (activitySource.HasListeners())
                    {
                        // extract trace information from the request header if available
                        if (Request.RequestHeader != null &&
                            Request.RequestHeader.AdditionalHeader
                                .TryGetEncodeable(out AdditionalParametersType parameters) &&
                            TryExtractActivityContextFromParameters(
                                parameters,
                                out ActivityContext activityContext))
                        {
                            activity = activitySource.StartActivity(
                                Request.GetType().Name,
                                ActivityKind.Server,
                                activityContext);
                        }
                    }

                    using (activity)
                    {
                        ServiceDefinition service = m_endpoint.FindService(Request.TypeId);
                        IServiceResponse response = await service.InvokeAsync(Request, SecureChannelContext, linkedCts.Token).ConfigureAwait(false);
                        m_vts.SetResult(response);
                    }
                }
                catch (Exception e)
                {
                    if (e is OperationCanceledException)
                    {
                        e = new ServiceResultException(StatusCodes.BadTimeout);
                    }
                    m_vts.SetResult(m_endpoint.CreateFault(Request, e));
                }
            }

            /// <inheritdoc/>
            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                if (ServiceResult.IsBad(error))
                {
                    m_vts.SetResult(m_endpoint.CreateFault(Request, new ServiceResultException(error)));
                }
                else
                {
                    m_vts.SetResult(response);
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (obj is EndpointIncomingRequest other)
                {
                    return Request.RequestHeader.Equals(other.Request.RequestHeader);
                }
                return false;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Request.RequestHeader.GetHashCode();
            }

            /// <inheritdoc/>
            public static bool operator ==(EndpointIncomingRequest left, EndpointIncomingRequest right)
            {
                return left.Equals(right);
            }

            /// <inheritdoc/>
            public static bool operator !=(EndpointIncomingRequest left, EndpointIncomingRequest right)
            {
                return !(left == right);
            }

            /// <inheritdoc/>
            public bool Equals(EndpointIncomingRequest other)
            {
                return Request.RequestHeader.Equals(other.Request.RequestHeader);
            }

            private readonly EndpointBase m_endpoint;
            private readonly ServiceResponsePooledValueTaskSource m_vts;
            private readonly CancellationToken m_cancellationToken;
        }

        /// <summary>
        /// Logger for this and the inherited classes
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected ILogger m_logger { get; } = LoggerUtils.Null.Logger;
#pragma warning restore IDE1006 // Naming Styles

        private IServiceHostBase m_host;
        private IServerBase m_server;
    }
}
