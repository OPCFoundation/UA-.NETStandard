/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

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

                MessageContext = m_server.MessageContext;

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

            SupportedServices = [];
        }

        /// <summary>
        /// Initializes the endpoint with a server instead of a host.
        /// </summary>
        protected EndpointBase(ServerBase server)
        {
            m_host = null;
            m_server = server ?? throw new ArgumentNullException(nameof(server));

            SupportedServices = [];
        }

        /// <summary>
        /// Begins processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="channeId">A unique identifier for the secure channel which is the source of the request.</param>
        /// <param name="endpointDescription">The description of the endpoint which the secure channel is using.</param>
        /// <param name="request">The incoming request.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="callbackData">The callback data.</param>
        /// <returns>
        /// The result which must be passed to the EndProcessRequest method.
        /// </returns>
        /// <seealso cref="EndProcessRequest"/>
        /// <seealso cref="ITransportListener"/>
        /// <exception cref="ArgumentNullException"><paramref name="channeId"/> is <c>null</c>.</exception>
        public IAsyncResult BeginProcessRequest(
            string channeId,
            EndpointDescription endpointDescription,
            IServiceRequest request,
            AsyncCallback callback,
            object callbackData)
        {
            if (channeId == null)
            {
                throw new ArgumentNullException(nameof(channeId));
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // create operation.
            var result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);

            var context = new SecureChannelContext(
                channeId,
                endpointDescription,
                RequestEncoding.Binary);

            // begin invoke service.
            return result.BeginProcessRequest(context, request);
        }

        /// <summary>
        /// Ends processing a request received via a binary encoded channel.
        /// </summary>
        /// <param name="result">The result returned by the BeginProcessRequest method.</param>
        /// <returns>
        /// The response to return over the secure channel.
        /// </returns>
        /// <seealso cref="BeginProcessRequest"/>
        public IServiceResponse EndProcessRequest(IAsyncResult result)
        {
            return ProcessRequestAsyncResult.WaitForComplete(result, false);
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
        /// Activity Source Name.
        /// </summary>
        public static readonly string ActivitySourceName = "Opc.Ua.Server-ActivitySource";

        /// <summary>
        /// Activity Source static instance.
        /// </summary>
        public static ActivitySource ActivitySource => s_activitySource.Value;

        private static readonly Lazy<ActivitySource> s_activitySource = new(() =>
            new ActivitySource(ActivitySourceName, "1.0.0"));

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
                if (item.Key == "traceparent")
                {
                    string traceparent = item.Value.ToString();
                    int firstDash = traceparent.IndexOf('-', StringComparison.Ordinal);
                    int secondDash = traceparent.IndexOf('-', firstDash + 1);
                    int thirdDash = traceparent.IndexOf('-', secondDash + 1);

                    if (firstDash != -1 && secondDash != -1)
                    {
                        ReadOnlySpan<char> traceIdSpan = traceparent.AsSpan(
                            firstDash + 1,
                            secondDash - firstDash - 1);
                        ReadOnlySpan<char> spanIdSpan = traceparent.AsSpan(
                            secondDash + 1,
                            thirdDash - secondDash - 1);
                        ReadOnlySpan<char> traceFlagsSpan = traceparent.AsSpan(thirdDash + 1);

                        var traceId = ActivityTraceId.CreateFromString(traceIdSpan);
                        var spanId = ActivitySpanId.CreateFromString(spanIdSpan);
                        ActivityTraceFlags traceFlags = traceFlagsSpan.SequenceEqual("01".AsSpan())
                            ? ActivityTraceFlags.Recorded
                            : ActivityTraceFlags.None;
                        activityContext = new ActivityContext(traceId, spanId, traceFlags);
                        return true;
                    }
                    activityContext = default;
                    return false;
                }
            }

            // no traceparent header found
            activityContext = default;
            return false;
        }

        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="incoming">Incoming request.</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual IServiceResponse ProcessRequest(IServiceRequest incoming)
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
                return service.Invoke(incoming);
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
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual async Task<IServiceResponse> ProcessRequestAsync(
            IServiceRequest incoming,
            CancellationToken cancellationToken = default)
        {
            try
            {
                SetRequestContext(RequestEncoding.Binary);

                ServiceDefinition service = null;

                // find service.
                if (!SupportedServices.TryGetValue(incoming.TypeId, out service))
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported, Utils
                        .Format("'{0}' is an unrecognized service identifier.", incoming.TypeId));
                }

                // invoke service.
                return await service.InvokeAsync(incoming, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // create fault.
                return CreateFault(incoming, e);
            }
        }

#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Invoke service response message.</returns>
        public virtual InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request)
        {
            IServiceRequest decodedRequest = null;
            IServiceResponse response = null;

            // create context for request and reply.
            ServiceMessageContext context = MessageContext;

            try
            {
                // check for null.
                if (request == null || request.InvokeServiceRequest == null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        Utils.Format("Null message cannot be processed."));
                }

                // decoding incoming message.
                decodedRequest =
                    BinaryDecoder.DecodeMessage(request.InvokeServiceRequest, null, context) as IServiceRequest;

                // invoke service.
                response = ProcessRequest(decodedRequest);

                // encode response.
                InvokeServiceResponseMessage outgoing = new InvokeServiceResponseMessage();
                outgoing.InvokeServiceResponse = BinaryEncoder.EncodeMessage(response, context);
                return outgoing;
            }
            catch (Exception e)
            {
                // create fault.
                ServiceFault fault = CreateFault(decodedRequest, e);

                // encode fault response.
                if (context == null)
                {
                    context = new ServiceMessageContext();
                }

                InvokeServiceResponseMessage outgoing = new InvokeServiceResponseMessage();
                outgoing.InvokeServiceResponse = BinaryEncoder.EncodeMessage(fault, context);
                return outgoing;
            }
        }
#else
        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual IAsyncResult BeginInvokeService(
            InvokeServiceMessage request,
            AsyncCallback callback,
            object asyncState)
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

                // create handler.
                var result = new ProcessRequestAsyncResult(this, callback, asyncState, 0);
                return result.BeginProcessRequest(
                    SecureChannelContext.Current,
                    request.InvokeServiceRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(null, e);
            }
        }

        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="result">The async result.</param>
        public virtual InvokeServiceResponseMessage EndInvokeService(IAsyncResult result)
        {
            try
            {
                // wait for the response.
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(
                    result,
                    false);

                // encode the response.
                return new InvokeServiceResponseMessage
                {
                    InvokeServiceResponse = BinaryEncoder.EncodeMessage(response, MessageContext)
                };
            }
            catch (Exception e)
            {
                // create fault.
                ServiceFault fault = CreateFault(ProcessRequestAsyncResult.GetRequest(result), e);

                // encode the fault as a response.
                return new InvokeServiceResponseMessage
                {
                    InvokeServiceResponse = BinaryEncoder.EncodeMessage(fault, MessageContext)
                };
            }
        }
#endif

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
        /// Creates a fault message.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>A fault message.</returns>
        public static ServiceFault CreateFault(IServiceRequest request, Exception exception)
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
                Utils.LogWarning("SERVER - Service Fault Occurred. Reason={0}", result.StatusCode);
                if (sre.StatusCode == StatusCodes.BadUnexpectedError)
                {
                    Utils.LogWarning(Utils.TraceMasks.StackTrace, sre, sre.ToString());
                }
            }
            else
            {
                result = new ServiceResult(exception, StatusCodes.BadUnexpectedError);
                Utils.LogError(
                    exception,
                    "SERVER - Unexpected Service Fault: {0}",
                    exception.Message);
            }

            fault.ResponseHeader.ServiceResult = result.Code;

            var stringTable = new StringTable();

            fault.ResponseHeader.ServiceDiagnostics = new DiagnosticInfo(
                result,
                diagnosticsMask,
                true,
                stringTable);

            fault.ResponseHeader.StringTable = stringTable.ToArray();

            return fault;
        }

        /// <summary>
        /// Creates a fault message.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="exception">The exception.</param>
        /// <returns>A fault message.</returns>
        public static Exception CreateSoapFault(IServiceRequest request, Exception exception)
        {
            ServiceFault fault = CreateFault(request, exception);

            // get the error from the header.
            StatusCode error = fault.ResponseHeader.ServiceResult;

            // construct the fault code and fault reason.
            string codeName = StatusCodes.GetBrowseName(error.Code);

            return new ServiceResultException(error.Code, codeName, exception);
        }

        /// <summary>
        /// Returns the message context used by the server associated with the endpoint.
        /// </summary>
        /// <value>The message context.</value>
        protected IServiceMessageContext MessageContext { get; set; }

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
            /// <param name="invokeMethod">The invoke method.</param>
            public ServiceDefinition(Type requestType, InvokeServiceEventHandler invokeMethod)
            {
                RequestType = requestType;
                m_invokeService = invokeMethod;
            }

            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="asyncInvokeMethod">The async invoke method.</param>
            public ServiceDefinition(
                Type requestType,
                InvokeServiceAsyncEventHandler asyncInvokeMethod)
            {
                RequestType = requestType;
                m_invokeServiceAsync = asyncInvokeMethod;
            }

            /// <summary>
            /// Initializes the object with its request type and implementation.
            /// </summary>
            /// <param name="requestType">Type of the request.</param>
            /// <param name="invokeMethod">The invoke method.</param>
            /// <param name="asyncInvokeMethod">The async invoke method.</param>
            public ServiceDefinition(
                Type requestType,
                InvokeServiceEventHandler invokeMethod,
                InvokeServiceAsyncEventHandler asyncInvokeMethod)
            {
                RequestType = requestType;
                m_invokeService = invokeMethod;
                m_invokeServiceAsync = asyncInvokeMethod;
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
            public IServiceResponse Invoke(IServiceRequest request)
            {
                if (m_invokeService == null && m_invokeServiceAsync != null)
                {
                    Utils.LogWarning("Async Service invoced sychronously. Prefer using InvokeAsync for best performance.");
                    return InvokeAsync(request).GetAwaiter().GetResult();
                }
                return m_invokeService?.Invoke(request);
            }

            /// <summary>
            /// Processes the request asynchronously.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns></returns>
            public async Task<IServiceResponse> InvokeAsync(
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                InvokeServiceAsyncEventHandler asyncHandler = m_invokeServiceAsync;

                if (asyncHandler != null)
                {
                    return await asyncHandler(request, cancellationToken).ConfigureAwait(false);
                }
                var syncHandler = m_invokeService;

                return syncHandler?.Invoke(request);
            }

            private readonly InvokeServiceEventHandler m_invokeService;
            private readonly InvokeServiceAsyncEventHandler m_invokeServiceAsync;
        }

        /// <summary>
        /// A delegate used to dispatch incoming service requests.
        /// </summary>
        protected delegate IServiceResponse InvokeServiceEventHandler(IServiceRequest request);

        /// <summary>
        /// A delegate used to asynchronously dispatch incoming service requests.
        /// </summary>
        protected delegate Task<IServiceResponse> InvokeServiceAsyncEventHandler(
            IServiceRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// An AsyncResult object when handling an asynchronous request.
        /// </summary>
        protected class ProcessRequestAsyncResult : AsyncResultBase, IEndpointIncomingRequest
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="ProcessRequestAsyncResult"/> class.
            /// </summary>
            /// <param name="endpoint">The endpoint being called.</param>
            /// <param name="callback">The callback to use when the operation completes.</param>
            /// <param name="callbackData">The callback data.</param>
            /// <param name="timeout">The timeout in milliseconds</param>
            public ProcessRequestAsyncResult(
                EndpointBase endpoint,
                AsyncCallback callback,
                object callbackData,
                int timeout)
                : base(callback, callbackData, timeout)
            {
                m_endpoint = endpoint;
            }

            /// <summary>
            /// Gets the request.
            /// </summary>
            /// <value>The request.</value>
            public IServiceRequest Request { get; private set; }

            /// <summary>
            /// Gets the secure channel context associated with the request.
            /// </summary>
            /// <value>The secure channel context.</value>
            public SecureChannelContext SecureChannelContext { get; private set; }

            /// <summary>
            /// Gets or sets the call data associated with the request.
            /// </summary>
            /// <value>The call data.</value>
            public object Calldata { get; set; }

            /// <summary>
            /// Used to call the default synchronous handler.
            /// </summary>
            /// <remarks>
            /// This method may block the current thread so the caller must not call in the
            /// thread that calls IServerBase.ScheduleIncomingRequest().
            /// This method always traps any exceptions and reports them to the client as a fault.
            /// </remarks>
            public void CallSynchronously()
            {
                OnProcessRequest(null);
            }

            /// <summary>
            /// Used to call the default asynchronous handler.
            /// </summary>
            /// <remarks>
            /// This method may block the current thread so the caller must not call in the
            /// thread that calls IServerBase.ScheduleIncomingRequest().
            /// This method always traps any exceptions and reports them to the client as a fault.
            /// </remarks>
            public async Task CallAsync(CancellationToken cancellationToken = default)
            {
                await OnProcessRequestAsync(null, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Used to indicate that the asynchronous operation has completed.
            /// </summary>
            /// <param name="response">The response. May be null if an error is provided.</param>
            /// <param name="error">Error result</param>
            public void OperationCompleted(IServiceResponse response, ServiceResult error)
            {
                // save response and/or error.
                m_error = null;
                m_response = response;

                if (ServiceResult.IsBad(error))
                {
                    m_error = new ServiceResultException(error);
                    m_response = SaveExceptionAsResponse(m_error);
                }

                // operation completed.
                OperationCompleted();
            }

            /// <summary>
            /// Begins processing an incoming request.
            /// </summary>
            /// <param name="context">The security context for the request</param>
            /// <param name="requestData">The request data.</param>
            /// <returns>
            /// The result object that is used to call the EndProcessRequest method.
            /// </returns>
            /// <exception cref="ServiceResultException"></exception>
            public IAsyncResult BeginProcessRequest(
                SecureChannelContext context,
                byte[] requestData)
            {
                SecureChannelContext = context;

                try
                {
                    // decoding incoming message.
                    Request =
                        BinaryDecoder.DecodeMessage(
                            requestData,
                            null,
                            m_endpoint.MessageContext) as IServiceRequest;

                    // find service.
                    m_service = m_endpoint.FindService(Request.TypeId);

                    if (m_service == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadServiceUnsupported,
                            "'{0}' is an unrecognized service type.",
                            Request.TypeId);
                    }

                    // queue request.
                    m_endpoint.ServerForContext.ScheduleIncomingRequest(this);
                }
                catch (Exception e)
                {
                    m_error = e;
                    m_response = SaveExceptionAsResponse(e);

                    // operation completed.
                    OperationCompleted();
                }

                return this;
            }

            /// <summary>
            /// Begins processing an incoming request.
            /// </summary>
            /// <param name="context">The security context for the request</param>
            /// <param name="request">The request.</param>
            /// <returns>The result object that is used to call the EndProcessRequest method.</returns>
            /// <exception cref="ServiceResultException"></exception>
            public IAsyncResult BeginProcessRequest(
                SecureChannelContext context,
                IServiceRequest request)
            {
                SecureChannelContext = context;
                Request = request;

                try
                {
                    // find service.
                    m_service = m_endpoint.FindService(Request.TypeId);

                    if (m_service == null)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadServiceUnsupported,
                            "'{0}' is an unrecognized service type.",
                            Request.TypeId);
                    }

                    // queue request.
                    m_endpoint.ServerForContext.ScheduleIncomingRequest(this);
                }
                catch (Exception e)
                {
                    m_error = e;
                    m_response = SaveExceptionAsResponse(e);

                    // operation completed.
                    OperationCompleted();
                }

                return this;
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <param name="throwOnError">if set to <c>true</c> an exception is thrown if an error occurred.</param>
            /// <returns>The response.</returns>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="TimeoutException"></exception>
            /// <exception cref="ServiceResultException"></exception>
            public static IServiceResponse WaitForComplete(IAsyncResult ar, bool throwOnError)
            {
                if (ar is not ProcessRequestAsyncResult result)
                {
                    throw new ArgumentException(
                        "End called with an invalid IAsyncResult object.",
                        nameof(ar));
                }

                if (result.m_response == null && !result.WaitForComplete())
                {
                    throw new TimeoutException();
                }

                if (throwOnError && result.m_error != null)
                {
                    throw new ServiceResultException(result.m_error, StatusCodes.BadInternalError);
                }

                return result.m_response;
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and returns the original request object.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The request object if available; otherwise null.</returns>
            public static IServiceRequest GetRequest(IAsyncResult ar)
            {
                if (ar is ProcessRequestAsyncResult result)
                {
                    return result.Request;
                }

                return null;
            }

            /// <summary>
            /// Saves an exception as response.
            /// </summary>
            /// <param name="e">The exception.</param>
            private ServiceFault SaveExceptionAsResponse(Exception e)
            {
                try
                {
                    return CreateFault(Request, e);
                }
                catch (Exception e2)
                {
                    return CreateFault(null, e2);
                }
            }

            /// <summary>
            /// Processes the request.
            /// </summary>
            private void OnProcessRequest(object state)
            {
                try
                {
                    // set the context.
                    SecureChannelContext.Current = SecureChannelContext;

                    if (ActivitySource.HasListeners())
                    {
                        // extract trace information from the request header if available
                        if (Request.RequestHeader?.AdditionalHeader?
                                .Body is AdditionalParametersType parameters &&
                            TryExtractActivityContextFromParameters(
                                parameters,
                                out ActivityContext activityContext))
                        {
                            using Activity activity = ActivitySource.StartActivity(
                                Request.GetType().Name,
                                ActivityKind.Server,
                                activityContext);
                            // call the service.
                            m_response = m_service.Invoke(Request);
                        }
                        else
                        {
                            // call the service even when there is no trace information
                            m_response = m_service.Invoke(Request);
                        }
                    }
                    else
                    {
                        // no listener, directly call the service.
                        m_response = m_service.Invoke(Request);
                    }
                }
                catch (Exception e)
                {
                    // save any error.
                    m_error = e;
                    m_response = SaveExceptionAsResponse(e);
                }

                // report completion.
                OperationCompleted();
            }

            /// <summary>
            /// Processes the request asynchronously.
            /// </summary>
            private async Task OnProcessRequestAsync(
                object state,
                CancellationToken cancellationToken = default)
            {
                try
                {
                    // set the context.
                    SecureChannelContext.Current = SecureChannelContext;

                    if (ActivitySource.HasListeners())
                    {
                        // extract trace information from the request header if available
                        if (Request.RequestHeader?.AdditionalHeader?
                            .Body is AdditionalParametersType parameters &&
                            TryExtractActivityContextFromParameters(
                                parameters,
                                out var activityContext))
                        {
                            using (var activity = ActivitySource.StartActivity(
                                Request.GetType().Name,
                                ActivityKind.Server,
                                activityContext))
                            {
                                // call the service.
                                m_response = await m_service.InvokeAsync(Request, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // call the service even when there is no trace information
                            m_response = await m_service.InvokeAsync(Request, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        // no listener, directly call the service.
                        m_response = await m_service.InvokeAsync(Request, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    // save any error.
                    m_error = e;
                    m_response = SaveExceptionAsResponse(e);
                }

                // report completion.
                OperationCompleted();
            }

            private readonly EndpointBase m_endpoint;
            private IServiceResponse m_response;
            private ServiceDefinition m_service;
            private Exception m_error;
        }

        private IServiceHostBase m_host;
        private IServerBase m_server;
    }
}
