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
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
	/// A base class for UA endpoints.
	/// </summary>
    public abstract class EndpointBase : IEndpointBase, ITransportListenerCallback
    {
        #region Constructors
        /// <summary>
        /// Initializes the object when it is created by the WCF framework.
        /// </summary>
        protected EndpointBase()
        {
            SupportedServices = new Dictionary<ExpandedNodeId, ServiceDefinition>();

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
        /// Initializes the when it is created directly.
        /// </summary>
        /// <param name="host">The host.</param>
        protected EndpointBase(IServiceHostBase host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            m_host = host;
            m_server = host.Server;

            SupportedServices = new Dictionary<ExpandedNodeId, ServiceDefinition>();
        }

        /// <summary>
        /// Initializes the endpoint with a server instead of a host.
        /// </summary>
        protected EndpointBase(ServerBase server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            m_host = null;
            m_server = server;

            SupportedServices = new Dictionary<ExpandedNodeId, ServiceDefinition>();
        }
        #endregion

        #region ITransportListenerCallback Members
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
        public IAsyncResult BeginProcessRequest(
            string channeId,
            EndpointDescription endpointDescription,
            IServiceRequest request,
            AsyncCallback callback,
            object callbackData)
        {
            if (channeId == null) throw new ArgumentNullException(nameof(channeId));
            if (request == null) throw new ArgumentNullException(nameof(request));

            // create operation.
            ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);

            SecureChannelContext context = new SecureChannelContext(
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
        #endregion

        #region IAuditEventCallback Members
        /// <inheritdoc/>
        public void ReportAuditOpenSecureChannelEvent(
            string globalChannelId,
            EndpointDescription endpointDescription,
            OpenSecureChannelRequest request,
            X509Certificate2 clientCertificate,
            Exception exception)
        {
            // trigger the reporting of AuditOpenSecureChannelEventType
            ServerForContext?.ReportAuditOpenSecureChannelEvent(globalChannelId, endpointDescription, request, clientCertificate, exception);
        }

        /// <inheritdoc/>
        public void ReportAuditCloseSecureChannelEvent(
            string globalChannelId,
            Exception exception)
        {
            // trigger the reporting of close AuditChannelEventType
            ServerForContext?.ReportAuditCloseSecureChannelEvent(globalChannelId, exception);
        }

        /// <inheritdoc/>
        public void ReportAuditCertificateEvent(X509Certificate2 clientCertificate, Exception exception)
        {
            // trigger the reporting of OpenSecureChannelAuditEvent
            ServerForContext?.ReportAuditCertificateEvent(clientCertificate, exception);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="incoming">Incoming request.</param>
        public virtual IServiceResponse ProcessRequest(IServiceRequest incoming)
        {
            try
            {
                SetRequestContext(RequestEncoding.Binary);

                ServiceDefinition service = null;

                // find service.
                if (!SupportedServices.TryGetValue(incoming.TypeId, out service))
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported, Utils.Format("'{0}' is an unrecognized service identifier.", incoming.TypeId));
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
        #endregion

        #region IEndpointBase Members
#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Invoke service response message.</returns>
        public virtual InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request)
        {          
            IServiceRequest decodedRequest = null;
            IServiceResponse  response = null;          
            
            // create context for request and reply.
            ServiceMessageContext context = MessageContext;
            
            try
            {
                // check for null.
                if (request == null || request.InvokeServiceRequest == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, Utils.Format("Null message cannot be processed."));
                }
                
                // decoding incoming message.
                decodedRequest = BinaryDecoder.DecodeMessage(request.InvokeServiceRequest, null, context) as IServiceRequest;

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
        public virtual IAsyncResult BeginInvokeService(InvokeServiceMessage message, AsyncCallback callack, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }

                // set the request context.
                SetRequestContext(RequestEncoding.Binary);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callack, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.InvokeServiceRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(null, e);
            }
        }

        /// <summary>
        /// Dispatches an incoming binary encoded request.
        /// </summary>
        /// <param name="ar">The async result.</param>
        public virtual InvokeServiceResponseMessage EndInvokeService(IAsyncResult ar)
        {
            try
            {
                // wait for the response.
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, false);

                // encode the response.
                InvokeServiceResponseMessage outgoing = new InvokeServiceResponseMessage();
                outgoing.InvokeServiceResponse = BinaryEncoder.EncodeMessage(response, MessageContext);
                return outgoing;
            }
            catch (Exception e)
            {
                // create fault.
                ServiceFault fault = CreateFault(ProcessRequestAsyncResult.GetRequest(ar), e);

                // encode the fault as a response.
                InvokeServiceResponseMessage outgoing = new InvokeServiceResponseMessage();
                outgoing.InvokeServiceResponse = BinaryEncoder.EncodeMessage(fault, MessageContext);
                return outgoing;
            }
        }
#endif

        /// <summary>
        /// Returns the host associated with the current context.
        /// </summary>
        /// <value>The host associated with the current context.</value>
        protected IServiceHostBase HostForContext
        {
            get
            {
                if (m_host == null)
                {
                    m_host = GetHostForContext();
                }

                return m_host;
            }
        }

        /// <summary>
        /// Returns the host associated with the current context.
        /// </summary>
        /// <returns>The host associated with the current context.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        protected static IServiceHostBase GetHostForContext()
        {
            throw new ServiceResultException(StatusCodes.BadInternalError, "The endpoint is not associated with a host that supports IServerHostBase.");
        }

        /// <summary>
        /// Gets the server object from the operation context.
        /// </summary>
        /// <value>The server object from the operation context.</value>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        protected IServerBase ServerForContext
        {
            get
            {
                if (m_server == null)
                {
                    m_server = GetServerForContext();
                }

                return m_server;
            }
        }

        /// <summary>
        /// Gets the server object from the operation context.
        /// </summary>
        /// <returns>The server object from the operation context.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        protected IServerBase GetServerForContext()
        {
            // get the server associated with the host.
            IServerBase server = HostForContext.Server;

            if (server == null)
            {
                throw new ServiceResultException(StatusCodes.BadInternalError, "The endpoint is not associated with a server instance.");
            }

            // check the server status.
            if (ServiceResult.IsBad(server.ServerError))
            {
                throw new ServiceResultException(server.ServerError);
            }

            return server;
        }
        #endregion

        #region Protected Methods
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
        protected ServiceDefinition FindService(ExpandedNodeId requestTypeId)
        {
            ServiceDefinition service = null;

            if (!SupportedServices.TryGetValue(requestTypeId, out service))
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

            ServiceFault fault = new ServiceFault();

            if (request != null)
            {
                fault.ResponseHeader.Timestamp = DateTime.UtcNow;
                fault.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;

                if (request.RequestHeader != null)
                {
                    diagnosticsMask = (DiagnosticsMasks)request.RequestHeader.ReturnDiagnostics;
                }
            }

            ServiceResult result = null;

            ServiceResultException sre = exception as ServiceResultException;

            if (sre != null)
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
                Utils.LogError(exception, "SERVER - Unexpected Service Fault: {0}", exception.Message);
            }

            fault.ResponseHeader.ServiceResult = result.Code;

            StringTable stringTable = new StringTable();

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
            ServiceResult error = fault.ResponseHeader.ServiceResult;

            if (error == null)
            {
                error = ServiceResult.Create(StatusCodes.BadUnexpectedError, "An unknown error occurred.");
            }

            // construct the fault code and fault reason.
            string codeName = StatusCodes.GetBrowseName(error.Code);

            return new ServiceResultException((uint)error.StatusCode, codeName, exception);
        }

        /// <summary>
        /// Returns the message context used by the server associated with the endpoint.
        /// </summary>
        /// <value>The message context.</value>
        protected IServiceMessageContext MessageContext
        {
            get { return m_messageContext; }
            set { m_messageContext = value; }
        }

        /// <summary>
        /// Returns the description for the endpoint
        /// </summary>
        /// <value>The endpoint description.</value>
        protected EndpointDescription EndpointDescription
        {
            get { return m_endpointDescription; }
            set { m_endpointDescription = value; }
        }

        /// <summary>
        /// Returns the error of the server.
        /// </summary>
        /// <value>The server error.</value>
        protected ServiceResult ServerError
        {
            get { return m_serverError; }
            set { m_serverError = value; }
        }

        /// <summary>
        /// The types of services known to the server.
        /// </summary>
        protected Dictionary<ExpandedNodeId, ServiceDefinition> SupportedServices
        {
            get { return m_supportedServices; }
            set { m_supportedServices = value; }
        }

        /// <summary>
        /// Sets the request context for the thread.
        /// </summary>
        /// <param name="encoding">The encoding.</param>
        protected void SetRequestContext(RequestEncoding encoding)
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
        #endregion

        #region ServiceDefinition Class
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
            public ServiceDefinition(
                Type requestType,
                InvokeServiceEventHandler invokeMethod)
            {
                m_requestType = requestType;
                m_InvokeService = invokeMethod;
            }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the request.</value>
            public Type RequestType
            {
                get { return m_requestType; }
            }

            /// <summary>
            /// The system type of the request object.
            /// </summary>
            /// <value>The type of the response.</value>
            public Type ResponseType
            {
                get { return m_requestType; }
            }

            /// <summary>
            /// Processes the request.
            /// </summary>
            /// <param name="request">The request.</param>
            /// <returns></returns>
            public IServiceResponse Invoke(IServiceRequest request)
            {
                if (m_InvokeService != null)
                {
                    return m_InvokeService(request);
                }

                return null;
            }

            #region Private Fields
            private Type m_requestType;
            private InvokeServiceEventHandler m_InvokeService;
            #endregion
        }

        /// <summary>
        /// A delegate used to dispatch incoming service requests.
        /// </summary>
        protected delegate IServiceResponse InvokeServiceEventHandler(IServiceRequest request);
        #endregion

        #region ProcessRequestAsyncResult Class
        /// <summary>
        /// An AsyncResult object when handling an asynchronous request.
        /// </summary>
        protected class ProcessRequestAsyncResult : AsyncResultBase, IEndpointIncomingRequest
        {
            #region Constructors
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
            :
                base(callback, callbackData, timeout)
            {
                m_endpoint = endpoint;
            }
            #endregion

            #region IEndpointIncomingRequest Members
            /// <summary>
            /// Gets the request.
            /// </summary>
            /// <value>The request.</value>
            public IServiceRequest Request
            {
                get { return m_request; }
            }

            /// <summary>
            /// Gets the secure channel context associated with the request.
            /// </summary>
            /// <value>The secure channel context.</value>
            public SecureChannelContext SecureChannelContext
            {
                get { return m_context; }
            }

            /// <summary>
            /// Gets or sets the call data associated with the request.
            /// </summary>
            /// <value>The call data.</value>
            public object Calldata
            {
                get { return m_calldata; }
                set { m_calldata = value; }
            }

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
            /// Used to indicate that the asynchronous operation has completed.
            /// </summary>
            /// <param name="response">The response. May be null if an error is provided.</param>
            /// <param name="error"></param>
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
            #endregion

            #region Public Members
            /// <summary>
            /// Begins processing an incoming request.
            /// </summary>
            /// <param name="context">The security context for the request</param>
            /// <param name="requestData">The request data.</param>
            /// <returns>
            /// The result object that is used to call the EndProcessRequest method.
            /// </returns>
            public IAsyncResult BeginProcessRequest(
                SecureChannelContext context,
                byte[] requestData)
            {
                m_context = context;

                try
                {
                    // decoding incoming message.
                    m_request = BinaryDecoder.DecodeMessage(requestData, null, m_endpoint.MessageContext) as IServiceRequest;

                    // find service.
                    m_service = m_endpoint.FindService(m_request.TypeId);

                    if (m_service == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadServiceUnsupported, "'{0}' is an unrecognized service type.", m_request.TypeId);
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
            public IAsyncResult BeginProcessRequest(
                SecureChannelContext context,
                IServiceRequest request)
            {
                m_context = context;
                m_request = request;

                try
                {
                    // find service.
                    m_service = m_endpoint.FindService(m_request.TypeId);

                    if (m_service == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadServiceUnsupported, "'{0}' is an unrecognized service type.", m_request.TypeId);
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
            public static IServiceResponse WaitForComplete(IAsyncResult ar, bool throwOnError)
            {
                ProcessRequestAsyncResult result = ar as ProcessRequestAsyncResult;

                if (result == null)
                {
                    throw new ArgumentException("End called with an invalid IAsyncResult object.", nameof(ar));
                }

                if (result.m_response == null)
                {
                    if (!result.WaitForComplete())
                    {
                        throw new TimeoutException();
                    }
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
                ProcessRequestAsyncResult result = ar as ProcessRequestAsyncResult;

                if (result != null)
                {
                    return result.m_request;
                }

                return null;
            }
            #endregion

            #region Private Members
            /// <summary>
            /// Saves an exception as response.
            /// </summary>
            /// <param name="e">The exception.</param>
            private IServiceResponse SaveExceptionAsResponse(Exception e)
            {
                try
                {
                    return EndpointBase.CreateFault(m_request, e);
                }
                catch (Exception e2)
                {
                    return EndpointBase.CreateFault(null, e2);
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
                    SecureChannelContext.Current = m_context;

                    // call the service.
                    m_response = m_service.Invoke(m_request);
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
            #endregion     

            #region Private Fields
            private EndpointBase m_endpoint;
            private SecureChannelContext m_context;
            private IServiceRequest m_request;
            private IServiceResponse m_response;
            private ServiceDefinition m_service;
            private Exception m_error;
            private object m_calldata;
            #endregion
        }
        #endregion

        #region Private Fields
        private ServiceResult m_serverError;
        private IServiceMessageContext m_messageContext;
        private EndpointDescription m_endpointDescription;
        private Dictionary<ExpandedNodeId, ServiceDefinition> m_supportedServices;
        private IServiceHostBase m_host;
        private IServerBase m_server;
        private string g_ImplementationString = "Opc.Ua.EndpointBase UA Service " + Utils.GetAssemblySoftwareVersion();
        #endregion
    }
}
