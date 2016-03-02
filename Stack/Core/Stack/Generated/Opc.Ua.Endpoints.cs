/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.Threading;
using System.Security.Principal;
using System.ServiceModel;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    #region SessionEndpoint Class
    /// <summary>
    /// A endpoint object used by clients to access a UA service.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.CodeGenerator", "1.0.0.0")]
    [ServiceMessageContextBehavior()]
    public partial class SessionEndpoint : EndpointBase, ISessionEndpoint, IDiscoveryEndpoint
    {
        #region Constructors
        /// <summary>
        /// Initializes the object when it is created by the WCF framework.
        /// </summary>
        public SessionEndpoint()
        {
            this.CreateKnownTypes();
        }

        /// <summary>
        /// Initializes the when it is created directly.
        /// </summary>
        public SessionEndpoint(IServiceHostBase host) : base(host)
        {
            this.CreateKnownTypes();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionEndpoint"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        public SessionEndpoint(ServerBase server) : base(server)
        {
            this.CreateKnownTypes();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The UA server instance that the endpoint is connected to.
        /// </summary>
        protected ISessionServer ServerInstance
        {
            get
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                return ServerForContext as ISessionServer;
             }
        }
        #endregion

        #region ISessionEndpoint Members
        #region FindServers Service
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public IServiceResponse FindServers(IServiceRequest incoming)
        {
            FindServersResponse response = null;

            FindServersRequest request = (FindServersRequest)incoming;

            ApplicationDescriptionCollection servers = null;

            response = new FindServersResponse();

            response.ResponseHeader = ServerInstance.FindServers(
               request.RequestHeader,
               request.EndpointUrl,
               request.LocaleIds,
               request.ServerUris,
               out servers);

            response.Servers = servers;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
        public virtual FindServersResponseMessage FindServers(FindServersMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                FindServersResponse response = (FindServersResponse)FindServers(request.FindServersRequest);
                return new FindServersResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.FindServersRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServers service.
        /// </summary>
        public virtual IAsyncResult BeginFindServers(FindServersMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.FindServersRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServers service to complete.
        /// </summary>
        public virtual FindServersResponseMessage EndFindServers(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new FindServersResponseMessage((FindServersResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region GetEndpoints Service
        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public IServiceResponse GetEndpoints(IServiceRequest incoming)
        {
            GetEndpointsResponse response = null;

            GetEndpointsRequest request = (GetEndpointsRequest)incoming;

            EndpointDescriptionCollection endpoints = null;

            response = new GetEndpointsResponse();

            response.ResponseHeader = ServerInstance.GetEndpoints(
               request.RequestHeader,
               request.EndpointUrl,
               request.LocaleIds,
               request.ProfileUris,
               out endpoints);

            response.Endpoints = endpoints;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
        public virtual GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                GetEndpointsResponse response = (GetEndpointsResponse)GetEndpoints(request.GetEndpointsRequest);
                return new GetEndpointsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.GetEndpointsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the GetEndpoints service.
        /// </summary>
        public virtual IAsyncResult BeginGetEndpoints(GetEndpointsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.GetEndpointsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.GetEndpointsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the GetEndpoints service to complete.
        /// </summary>
        public virtual GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new GetEndpointsResponseMessage((GetEndpointsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region CreateSession Service
        #if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        public IServiceResponse CreateSession(IServiceRequest incoming)
        {
            CreateSessionResponse response = null;

            CreateSessionRequest request = (CreateSessionRequest)incoming;

            NodeId sessionId = null;
            NodeId authenticationToken = null;
            double revisedSessionTimeout = 0;
            byte[] serverNonce = null;
            byte[] serverCertificate = null;
            EndpointDescriptionCollection serverEndpoints = null;
            SignedSoftwareCertificateCollection serverSoftwareCertificates = null;
            SignatureData serverSignature = null;
            uint maxRequestMessageSize = 0;

            response = new CreateSessionResponse();

            response.ResponseHeader = ServerInstance.CreateSession(
               request.RequestHeader,
               request.ClientDescription,
               request.ServerUri,
               request.EndpointUrl,
               request.SessionName,
               request.ClientNonce,
               request.ClientCertificate,
               request.RequestedSessionTimeout,
               request.MaxResponseMessageSize,
               out sessionId,
               out authenticationToken,
               out revisedSessionTimeout,
               out serverNonce,
               out serverCertificate,
               out serverEndpoints,
               out serverSoftwareCertificates,
               out serverSignature,
               out maxRequestMessageSize);

            response.SessionId                  = sessionId;
            response.AuthenticationToken        = authenticationToken;
            response.RevisedSessionTimeout      = revisedSessionTimeout;
            response.ServerNonce                = serverNonce;
            response.ServerCertificate          = serverCertificate;
            response.ServerEndpoints            = serverEndpoints;
            response.ServerSoftwareCertificates = serverSoftwareCertificates;
            response.ServerSignature            = serverSignature;
            response.MaxRequestMessageSize      = maxRequestMessageSize;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
        public virtual CreateSessionResponseMessage CreateSession(CreateSessionMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CreateSessionResponse response = (CreateSessionResponse)CreateSession(request.CreateSessionRequest);
                return new CreateSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CreateSessionRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateSession service.
        /// </summary>
        public virtual IAsyncResult BeginCreateSession(CreateSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateSessionRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CreateSessionRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateSession service to complete.
        /// </summary>
        public virtual CreateSessionResponseMessage EndCreateSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CreateSessionResponseMessage((CreateSessionResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region ActivateSession Service
        #if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        public IServiceResponse ActivateSession(IServiceRequest incoming)
        {
            ActivateSessionResponse response = null;

            ActivateSessionRequest request = (ActivateSessionRequest)incoming;

            byte[] serverNonce = null;
            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new ActivateSessionResponse();

            response.ResponseHeader = ServerInstance.ActivateSession(
               request.RequestHeader,
               request.ClientSignature,
               request.ClientSoftwareCertificates,
               request.LocaleIds,
               request.UserIdentityToken,
               request.UserTokenSignature,
               out serverNonce,
               out results,
               out diagnosticInfos);

            response.ServerNonce     = serverNonce;
            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
        public virtual ActivateSessionResponseMessage ActivateSession(ActivateSessionMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                ActivateSessionResponse response = (ActivateSessionResponse)ActivateSession(request.ActivateSessionRequest);
                return new ActivateSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.ActivateSessionRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ActivateSession service.
        /// </summary>
        public virtual IAsyncResult BeginActivateSession(ActivateSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ActivateSessionRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.ActivateSessionRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ActivateSession service to complete.
        /// </summary>
        public virtual ActivateSessionResponseMessage EndActivateSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new ActivateSessionResponseMessage((ActivateSessionResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region CloseSession Service
        #if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        public IServiceResponse CloseSession(IServiceRequest incoming)
        {
            CloseSessionResponse response = null;

            CloseSessionRequest request = (CloseSessionRequest)incoming;


            response = new CloseSessionResponse();

            response.ResponseHeader = ServerInstance.CloseSession(
               request.RequestHeader,
               request.DeleteSubscriptions);


            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
        public virtual CloseSessionResponseMessage CloseSession(CloseSessionMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CloseSessionResponse response = (CloseSessionResponse)CloseSession(request.CloseSessionRequest);
                return new CloseSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CloseSessionRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CloseSession service.
        /// </summary>
        public virtual IAsyncResult BeginCloseSession(CloseSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CloseSessionRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CloseSessionRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CloseSession service to complete.
        /// </summary>
        public virtual CloseSessionResponseMessage EndCloseSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CloseSessionResponseMessage((CloseSessionResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Cancel Service
        #if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        public IServiceResponse Cancel(IServiceRequest incoming)
        {
            CancelResponse response = null;

            CancelRequest request = (CancelRequest)incoming;

            uint cancelCount = 0;

            response = new CancelResponse();

            response.ResponseHeader = ServerInstance.Cancel(
               request.RequestHeader,
               request.RequestHandle,
               out cancelCount);

            response.CancelCount = cancelCount;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
        public virtual CancelResponseMessage Cancel(CancelMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CancelResponse response = (CancelResponse)Cancel(request.CancelRequest);
                return new CancelResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CancelRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Cancel service.
        /// </summary>
        public virtual IAsyncResult BeginCancel(CancelMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CancelRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CancelRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Cancel service to complete.
        /// </summary>
        public virtual CancelResponseMessage EndCancel(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CancelResponseMessage((CancelResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region AddNodes Service
        #if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        public IServiceResponse AddNodes(IServiceRequest incoming)
        {
            AddNodesResponse response = null;

            AddNodesRequest request = (AddNodesRequest)incoming;

            AddNodesResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new AddNodesResponse();

            response.ResponseHeader = ServerInstance.AddNodes(
               request.RequestHeader,
               request.NodesToAdd,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
        public virtual AddNodesResponseMessage AddNodes(AddNodesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                AddNodesResponse response = (AddNodesResponse)AddNodes(request.AddNodesRequest);
                return new AddNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.AddNodesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the AddNodes service.
        /// </summary>
        public virtual IAsyncResult BeginAddNodes(AddNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.AddNodesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.AddNodesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the AddNodes service to complete.
        /// </summary>
        public virtual AddNodesResponseMessage EndAddNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new AddNodesResponseMessage((AddNodesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region AddReferences Service
        #if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        public IServiceResponse AddReferences(IServiceRequest incoming)
        {
            AddReferencesResponse response = null;

            AddReferencesRequest request = (AddReferencesRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new AddReferencesResponse();

            response.ResponseHeader = ServerInstance.AddReferences(
               request.RequestHeader,
               request.ReferencesToAdd,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
        public virtual AddReferencesResponseMessage AddReferences(AddReferencesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                AddReferencesResponse response = (AddReferencesResponse)AddReferences(request.AddReferencesRequest);
                return new AddReferencesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.AddReferencesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the AddReferences service.
        /// </summary>
        public virtual IAsyncResult BeginAddReferences(AddReferencesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.AddReferencesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.AddReferencesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the AddReferences service to complete.
        /// </summary>
        public virtual AddReferencesResponseMessage EndAddReferences(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new AddReferencesResponseMessage((AddReferencesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region DeleteNodes Service
        #if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        public IServiceResponse DeleteNodes(IServiceRequest incoming)
        {
            DeleteNodesResponse response = null;

            DeleteNodesRequest request = (DeleteNodesRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new DeleteNodesResponse();

            response.ResponseHeader = ServerInstance.DeleteNodes(
               request.RequestHeader,
               request.NodesToDelete,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
        public virtual DeleteNodesResponseMessage DeleteNodes(DeleteNodesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                DeleteNodesResponse response = (DeleteNodesResponse)DeleteNodes(request.DeleteNodesRequest);
                return new DeleteNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.DeleteNodesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteNodes service.
        /// </summary>
        public virtual IAsyncResult BeginDeleteNodes(DeleteNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteNodesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.DeleteNodesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteNodes service to complete.
        /// </summary>
        public virtual DeleteNodesResponseMessage EndDeleteNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new DeleteNodesResponseMessage((DeleteNodesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region DeleteReferences Service
        #if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        public IServiceResponse DeleteReferences(IServiceRequest incoming)
        {
            DeleteReferencesResponse response = null;

            DeleteReferencesRequest request = (DeleteReferencesRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new DeleteReferencesResponse();

            response.ResponseHeader = ServerInstance.DeleteReferences(
               request.RequestHeader,
               request.ReferencesToDelete,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
        public virtual DeleteReferencesResponseMessage DeleteReferences(DeleteReferencesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                DeleteReferencesResponse response = (DeleteReferencesResponse)DeleteReferences(request.DeleteReferencesRequest);
                return new DeleteReferencesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.DeleteReferencesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteReferences service.
        /// </summary>
        public virtual IAsyncResult BeginDeleteReferences(DeleteReferencesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteReferencesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.DeleteReferencesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteReferences service to complete.
        /// </summary>
        public virtual DeleteReferencesResponseMessage EndDeleteReferences(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new DeleteReferencesResponseMessage((DeleteReferencesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Browse Service
        #if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        public IServiceResponse Browse(IServiceRequest incoming)
        {
            BrowseResponse response = null;

            BrowseRequest request = (BrowseRequest)incoming;

            BrowseResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new BrowseResponse();

            response.ResponseHeader = ServerInstance.Browse(
               request.RequestHeader,
               request.View,
               request.RequestedMaxReferencesPerNode,
               request.NodesToBrowse,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
        public virtual BrowseResponseMessage Browse(BrowseMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                BrowseResponse response = (BrowseResponse)Browse(request.BrowseRequest);
                return new BrowseResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.BrowseRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Browse service.
        /// </summary>
        public virtual IAsyncResult BeginBrowse(BrowseMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.BrowseRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.BrowseRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Browse service to complete.
        /// </summary>
        public virtual BrowseResponseMessage EndBrowse(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new BrowseResponseMessage((BrowseResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region BrowseNext Service
        #if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        public IServiceResponse BrowseNext(IServiceRequest incoming)
        {
            BrowseNextResponse response = null;

            BrowseNextRequest request = (BrowseNextRequest)incoming;

            BrowseResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new BrowseNextResponse();

            response.ResponseHeader = ServerInstance.BrowseNext(
               request.RequestHeader,
               request.ReleaseContinuationPoints,
               request.ContinuationPoints,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
        public virtual BrowseNextResponseMessage BrowseNext(BrowseNextMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                BrowseNextResponse response = (BrowseNextResponse)BrowseNext(request.BrowseNextRequest);
                return new BrowseNextResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.BrowseNextRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the BrowseNext service.
        /// </summary>
        public virtual IAsyncResult BeginBrowseNext(BrowseNextMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.BrowseNextRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.BrowseNextRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the BrowseNext service to complete.
        /// </summary>
        public virtual BrowseNextResponseMessage EndBrowseNext(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new BrowseNextResponseMessage((BrowseNextResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region TranslateBrowsePathsToNodeIds Service
        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public IServiceResponse TranslateBrowsePathsToNodeIds(IServiceRequest incoming)
        {
            TranslateBrowsePathsToNodeIdsResponse response = null;

            TranslateBrowsePathsToNodeIdsRequest request = (TranslateBrowsePathsToNodeIdsRequest)incoming;

            BrowsePathResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new TranslateBrowsePathsToNodeIdsResponse();

            response.ResponseHeader = ServerInstance.TranslateBrowsePathsToNodeIds(
               request.RequestHeader,
               request.BrowsePaths,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public virtual TranslateBrowsePathsToNodeIdsResponseMessage TranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                TranslateBrowsePathsToNodeIdsResponse response = (TranslateBrowsePathsToNodeIdsResponse)TranslateBrowsePathsToNodeIds(request.TranslateBrowsePathsToNodeIdsRequest);
                return new TranslateBrowsePathsToNodeIdsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.TranslateBrowsePathsToNodeIdsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public virtual IAsyncResult BeginTranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TranslateBrowsePathsToNodeIdsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.TranslateBrowsePathsToNodeIdsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TranslateBrowsePathsToNodeIds service to complete.
        /// </summary>
        public virtual TranslateBrowsePathsToNodeIdsResponseMessage EndTranslateBrowsePathsToNodeIds(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new TranslateBrowsePathsToNodeIdsResponseMessage((TranslateBrowsePathsToNodeIdsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region RegisterNodes Service
        #if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        public IServiceResponse RegisterNodes(IServiceRequest incoming)
        {
            RegisterNodesResponse response = null;

            RegisterNodesRequest request = (RegisterNodesRequest)incoming;

            NodeIdCollection registeredNodeIds = null;

            response = new RegisterNodesResponse();

            response.ResponseHeader = ServerInstance.RegisterNodes(
               request.RequestHeader,
               request.NodesToRegister,
               out registeredNodeIds);

            response.RegisteredNodeIds = registeredNodeIds;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
        public virtual RegisterNodesResponseMessage RegisterNodes(RegisterNodesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                RegisterNodesResponse response = (RegisterNodesResponse)RegisterNodes(request.RegisterNodesRequest);
                return new RegisterNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.RegisterNodesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the RegisterNodes service.
        /// </summary>
        public virtual IAsyncResult BeginRegisterNodes(RegisterNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RegisterNodesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.RegisterNodesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the RegisterNodes service to complete.
        /// </summary>
        public virtual RegisterNodesResponseMessage EndRegisterNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new RegisterNodesResponseMessage((RegisterNodesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region UnregisterNodes Service
        #if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        public IServiceResponse UnregisterNodes(IServiceRequest incoming)
        {
            UnregisterNodesResponse response = null;

            UnregisterNodesRequest request = (UnregisterNodesRequest)incoming;


            response = new UnregisterNodesResponse();

            response.ResponseHeader = ServerInstance.UnregisterNodes(
               request.RequestHeader,
               request.NodesToUnregister);


            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
        public virtual UnregisterNodesResponseMessage UnregisterNodes(UnregisterNodesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                UnregisterNodesResponse response = (UnregisterNodesResponse)UnregisterNodes(request.UnregisterNodesRequest);
                return new UnregisterNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.UnregisterNodesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the UnregisterNodes service.
        /// </summary>
        public virtual IAsyncResult BeginUnregisterNodes(UnregisterNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.UnregisterNodesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.UnregisterNodesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the UnregisterNodes service to complete.
        /// </summary>
        public virtual UnregisterNodesResponseMessage EndUnregisterNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new UnregisterNodesResponseMessage((UnregisterNodesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region QueryFirst Service
        #if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        public IServiceResponse QueryFirst(IServiceRequest incoming)
        {
            QueryFirstResponse response = null;

            QueryFirstRequest request = (QueryFirstRequest)incoming;

            QueryDataSetCollection queryDataSets = null;
            byte[] continuationPoint = null;
            ParsingResultCollection parsingResults = null;
            DiagnosticInfoCollection diagnosticInfos = null;
            ContentFilterResult filterResult = null;

            response = new QueryFirstResponse();

            response.ResponseHeader = ServerInstance.QueryFirst(
               request.RequestHeader,
               request.View,
               request.NodeTypes,
               request.Filter,
               request.MaxDataSetsToReturn,
               request.MaxReferencesToReturn,
               out queryDataSets,
               out continuationPoint,
               out parsingResults,
               out diagnosticInfos,
               out filterResult);

            response.QueryDataSets     = queryDataSets;
            response.ContinuationPoint = continuationPoint;
            response.ParsingResults    = parsingResults;
            response.DiagnosticInfos   = diagnosticInfos;
            response.FilterResult      = filterResult;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
        public virtual QueryFirstResponseMessage QueryFirst(QueryFirstMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                QueryFirstResponse response = (QueryFirstResponse)QueryFirst(request.QueryFirstRequest);
                return new QueryFirstResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.QueryFirstRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the QueryFirst service.
        /// </summary>
        public virtual IAsyncResult BeginQueryFirst(QueryFirstMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.QueryFirstRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.QueryFirstRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the QueryFirst service to complete.
        /// </summary>
        public virtual QueryFirstResponseMessage EndQueryFirst(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new QueryFirstResponseMessage((QueryFirstResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region QueryNext Service
        #if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        public IServiceResponse QueryNext(IServiceRequest incoming)
        {
            QueryNextResponse response = null;

            QueryNextRequest request = (QueryNextRequest)incoming;

            QueryDataSetCollection queryDataSets = null;
            byte[] revisedContinuationPoint = null;

            response = new QueryNextResponse();

            response.ResponseHeader = ServerInstance.QueryNext(
               request.RequestHeader,
               request.ReleaseContinuationPoint,
               request.ContinuationPoint,
               out queryDataSets,
               out revisedContinuationPoint);

            response.QueryDataSets            = queryDataSets;
            response.RevisedContinuationPoint = revisedContinuationPoint;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
        public virtual QueryNextResponseMessage QueryNext(QueryNextMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                QueryNextResponse response = (QueryNextResponse)QueryNext(request.QueryNextRequest);
                return new QueryNextResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.QueryNextRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the QueryNext service.
        /// </summary>
        public virtual IAsyncResult BeginQueryNext(QueryNextMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.QueryNextRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.QueryNextRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the QueryNext service to complete.
        /// </summary>
        public virtual QueryNextResponseMessage EndQueryNext(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new QueryNextResponseMessage((QueryNextResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Read Service
        #if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        public IServiceResponse Read(IServiceRequest incoming)
        {
            ReadResponse response = null;

            ReadRequest request = (ReadRequest)incoming;

            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new ReadResponse();

            response.ResponseHeader = ServerInstance.Read(
               request.RequestHeader,
               request.MaxAge,
               request.TimestampsToReturn,
               request.NodesToRead,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
        public virtual ReadResponseMessage Read(ReadMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                ReadResponse response = (ReadResponse)Read(request.ReadRequest);
                return new ReadResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.ReadRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Read service.
        /// </summary>
        public virtual IAsyncResult BeginRead(ReadMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ReadRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.ReadRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Read service to complete.
        /// </summary>
        public virtual ReadResponseMessage EndRead(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new ReadResponseMessage((ReadResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region HistoryRead Service
        #if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        public IServiceResponse HistoryRead(IServiceRequest incoming)
        {
            HistoryReadResponse response = null;

            HistoryReadRequest request = (HistoryReadRequest)incoming;

            HistoryReadResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new HistoryReadResponse();

            response.ResponseHeader = ServerInstance.HistoryRead(
               request.RequestHeader,
               request.HistoryReadDetails,
               request.TimestampsToReturn,
               request.ReleaseContinuationPoints,
               request.NodesToRead,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
        public virtual HistoryReadResponseMessage HistoryRead(HistoryReadMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                HistoryReadResponse response = (HistoryReadResponse)HistoryRead(request.HistoryReadRequest);
                return new HistoryReadResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.HistoryReadRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the HistoryRead service.
        /// </summary>
        public virtual IAsyncResult BeginHistoryRead(HistoryReadMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.HistoryReadRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.HistoryReadRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the HistoryRead service to complete.
        /// </summary>
        public virtual HistoryReadResponseMessage EndHistoryRead(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new HistoryReadResponseMessage((HistoryReadResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Write Service
        #if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        public IServiceResponse Write(IServiceRequest incoming)
        {
            WriteResponse response = null;

            WriteRequest request = (WriteRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new WriteResponse();

            response.ResponseHeader = ServerInstance.Write(
               request.RequestHeader,
               request.NodesToWrite,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
        public virtual WriteResponseMessage Write(WriteMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                WriteResponse response = (WriteResponse)Write(request.WriteRequest);
                return new WriteResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.WriteRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Write service.
        /// </summary>
        public virtual IAsyncResult BeginWrite(WriteMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.WriteRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.WriteRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Write service to complete.
        /// </summary>
        public virtual WriteResponseMessage EndWrite(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new WriteResponseMessage((WriteResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region HistoryUpdate Service
        #if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        public IServiceResponse HistoryUpdate(IServiceRequest incoming)
        {
            HistoryUpdateResponse response = null;

            HistoryUpdateRequest request = (HistoryUpdateRequest)incoming;

            HistoryUpdateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new HistoryUpdateResponse();

            response.ResponseHeader = ServerInstance.HistoryUpdate(
               request.RequestHeader,
               request.HistoryUpdateDetails,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
        public virtual HistoryUpdateResponseMessage HistoryUpdate(HistoryUpdateMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                HistoryUpdateResponse response = (HistoryUpdateResponse)HistoryUpdate(request.HistoryUpdateRequest);
                return new HistoryUpdateResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.HistoryUpdateRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the HistoryUpdate service.
        /// </summary>
        public virtual IAsyncResult BeginHistoryUpdate(HistoryUpdateMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.HistoryUpdateRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.HistoryUpdateRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the HistoryUpdate service to complete.
        /// </summary>
        public virtual HistoryUpdateResponseMessage EndHistoryUpdate(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new HistoryUpdateResponseMessage((HistoryUpdateResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Call Service
        #if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        public IServiceResponse Call(IServiceRequest incoming)
        {
            CallResponse response = null;

            CallRequest request = (CallRequest)incoming;

            CallMethodResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new CallResponse();

            response.ResponseHeader = ServerInstance.Call(
               request.RequestHeader,
               request.MethodsToCall,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
        public virtual CallResponseMessage Call(CallMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CallResponse response = (CallResponse)Call(request.CallRequest);
                return new CallResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CallRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Call service.
        /// </summary>
        public virtual IAsyncResult BeginCall(CallMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CallRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CallRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Call service to complete.
        /// </summary>
        public virtual CallResponseMessage EndCall(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CallResponseMessage((CallResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region CreateMonitoredItems Service
        #if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        public IServiceResponse CreateMonitoredItems(IServiceRequest incoming)
        {
            CreateMonitoredItemsResponse response = null;

            CreateMonitoredItemsRequest request = (CreateMonitoredItemsRequest)incoming;

            MonitoredItemCreateResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new CreateMonitoredItemsResponse();

            response.ResponseHeader = ServerInstance.CreateMonitoredItems(
               request.RequestHeader,
               request.SubscriptionId,
               request.TimestampsToReturn,
               request.ItemsToCreate,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
        public virtual CreateMonitoredItemsResponseMessage CreateMonitoredItems(CreateMonitoredItemsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CreateMonitoredItemsResponse response = (CreateMonitoredItemsResponse)CreateMonitoredItems(request.CreateMonitoredItemsRequest);
                return new CreateMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CreateMonitoredItemsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateMonitoredItems service.
        /// </summary>
        public virtual IAsyncResult BeginCreateMonitoredItems(CreateMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CreateMonitoredItemsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateMonitoredItems service to complete.
        /// </summary>
        public virtual CreateMonitoredItemsResponseMessage EndCreateMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CreateMonitoredItemsResponseMessage((CreateMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region ModifyMonitoredItems Service
        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        public IServiceResponse ModifyMonitoredItems(IServiceRequest incoming)
        {
            ModifyMonitoredItemsResponse response = null;

            ModifyMonitoredItemsRequest request = (ModifyMonitoredItemsRequest)incoming;

            MonitoredItemModifyResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new ModifyMonitoredItemsResponse();

            response.ResponseHeader = ServerInstance.ModifyMonitoredItems(
               request.RequestHeader,
               request.SubscriptionId,
               request.TimestampsToReturn,
               request.ItemsToModify,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
        public virtual ModifyMonitoredItemsResponseMessage ModifyMonitoredItems(ModifyMonitoredItemsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                ModifyMonitoredItemsResponse response = (ModifyMonitoredItemsResponse)ModifyMonitoredItems(request.ModifyMonitoredItemsRequest);
                return new ModifyMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.ModifyMonitoredItemsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ModifyMonitoredItems service.
        /// </summary>
        public virtual IAsyncResult BeginModifyMonitoredItems(ModifyMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ModifyMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.ModifyMonitoredItemsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ModifyMonitoredItems service to complete.
        /// </summary>
        public virtual ModifyMonitoredItemsResponseMessage EndModifyMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new ModifyMonitoredItemsResponseMessage((ModifyMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region SetMonitoringMode Service
        #if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        public IServiceResponse SetMonitoringMode(IServiceRequest incoming)
        {
            SetMonitoringModeResponse response = null;

            SetMonitoringModeRequest request = (SetMonitoringModeRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new SetMonitoringModeResponse();

            response.ResponseHeader = ServerInstance.SetMonitoringMode(
               request.RequestHeader,
               request.SubscriptionId,
               request.MonitoringMode,
               request.MonitoredItemIds,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
        public virtual SetMonitoringModeResponseMessage SetMonitoringMode(SetMonitoringModeMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                SetMonitoringModeResponse response = (SetMonitoringModeResponse)SetMonitoringMode(request.SetMonitoringModeRequest);
                return new SetMonitoringModeResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.SetMonitoringModeRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetMonitoringMode service.
        /// </summary>
        public virtual IAsyncResult BeginSetMonitoringMode(SetMonitoringModeMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetMonitoringModeRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.SetMonitoringModeRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetMonitoringMode service to complete.
        /// </summary>
        public virtual SetMonitoringModeResponseMessage EndSetMonitoringMode(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new SetMonitoringModeResponseMessage((SetMonitoringModeResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region SetTriggering Service
        #if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        public IServiceResponse SetTriggering(IServiceRequest incoming)
        {
            SetTriggeringResponse response = null;

            SetTriggeringRequest request = (SetTriggeringRequest)incoming;

            StatusCodeCollection addResults = null;
            DiagnosticInfoCollection addDiagnosticInfos = null;
            StatusCodeCollection removeResults = null;
            DiagnosticInfoCollection removeDiagnosticInfos = null;

            response = new SetTriggeringResponse();

            response.ResponseHeader = ServerInstance.SetTriggering(
               request.RequestHeader,
               request.SubscriptionId,
               request.TriggeringItemId,
               request.LinksToAdd,
               request.LinksToRemove,
               out addResults,
               out addDiagnosticInfos,
               out removeResults,
               out removeDiagnosticInfos);

            response.AddResults            = addResults;
            response.AddDiagnosticInfos    = addDiagnosticInfos;
            response.RemoveResults         = removeResults;
            response.RemoveDiagnosticInfos = removeDiagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
        public virtual SetTriggeringResponseMessage SetTriggering(SetTriggeringMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                SetTriggeringResponse response = (SetTriggeringResponse)SetTriggering(request.SetTriggeringRequest);
                return new SetTriggeringResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.SetTriggeringRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetTriggering service.
        /// </summary>
        public virtual IAsyncResult BeginSetTriggering(SetTriggeringMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetTriggeringRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.SetTriggeringRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetTriggering service to complete.
        /// </summary>
        public virtual SetTriggeringResponseMessage EndSetTriggering(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new SetTriggeringResponseMessage((SetTriggeringResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region DeleteMonitoredItems Service
        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        public IServiceResponse DeleteMonitoredItems(IServiceRequest incoming)
        {
            DeleteMonitoredItemsResponse response = null;

            DeleteMonitoredItemsRequest request = (DeleteMonitoredItemsRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new DeleteMonitoredItemsResponse();

            response.ResponseHeader = ServerInstance.DeleteMonitoredItems(
               request.RequestHeader,
               request.SubscriptionId,
               request.MonitoredItemIds,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
        public virtual DeleteMonitoredItemsResponseMessage DeleteMonitoredItems(DeleteMonitoredItemsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                DeleteMonitoredItemsResponse response = (DeleteMonitoredItemsResponse)DeleteMonitoredItems(request.DeleteMonitoredItemsRequest);
                return new DeleteMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.DeleteMonitoredItemsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteMonitoredItems service.
        /// </summary>
        public virtual IAsyncResult BeginDeleteMonitoredItems(DeleteMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.DeleteMonitoredItemsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteMonitoredItems service to complete.
        /// </summary>
        public virtual DeleteMonitoredItemsResponseMessage EndDeleteMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new DeleteMonitoredItemsResponseMessage((DeleteMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region CreateSubscription Service
        #if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        public IServiceResponse CreateSubscription(IServiceRequest incoming)
        {
            CreateSubscriptionResponse response = null;

            CreateSubscriptionRequest request = (CreateSubscriptionRequest)incoming;

            uint subscriptionId = 0;
            double revisedPublishingInterval = 0;
            uint revisedLifetimeCount = 0;
            uint revisedMaxKeepAliveCount = 0;

            response = new CreateSubscriptionResponse();

            response.ResponseHeader = ServerInstance.CreateSubscription(
               request.RequestHeader,
               request.RequestedPublishingInterval,
               request.RequestedLifetimeCount,
               request.RequestedMaxKeepAliveCount,
               request.MaxNotificationsPerPublish,
               request.PublishingEnabled,
               request.Priority,
               out subscriptionId,
               out revisedPublishingInterval,
               out revisedLifetimeCount,
               out revisedMaxKeepAliveCount);

            response.SubscriptionId            = subscriptionId;
            response.RevisedPublishingInterval = revisedPublishingInterval;
            response.RevisedLifetimeCount      = revisedLifetimeCount;
            response.RevisedMaxKeepAliveCount  = revisedMaxKeepAliveCount;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
        public virtual CreateSubscriptionResponseMessage CreateSubscription(CreateSubscriptionMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                CreateSubscriptionResponse response = (CreateSubscriptionResponse)CreateSubscription(request.CreateSubscriptionRequest);
                return new CreateSubscriptionResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.CreateSubscriptionRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateSubscription service.
        /// </summary>
        public virtual IAsyncResult BeginCreateSubscription(CreateSubscriptionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateSubscriptionRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.CreateSubscriptionRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateSubscription service to complete.
        /// </summary>
        public virtual CreateSubscriptionResponseMessage EndCreateSubscription(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new CreateSubscriptionResponseMessage((CreateSubscriptionResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region ModifySubscription Service
        #if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        public IServiceResponse ModifySubscription(IServiceRequest incoming)
        {
            ModifySubscriptionResponse response = null;

            ModifySubscriptionRequest request = (ModifySubscriptionRequest)incoming;

            double revisedPublishingInterval = 0;
            uint revisedLifetimeCount = 0;
            uint revisedMaxKeepAliveCount = 0;

            response = new ModifySubscriptionResponse();

            response.ResponseHeader = ServerInstance.ModifySubscription(
               request.RequestHeader,
               request.SubscriptionId,
               request.RequestedPublishingInterval,
               request.RequestedLifetimeCount,
               request.RequestedMaxKeepAliveCount,
               request.MaxNotificationsPerPublish,
               request.Priority,
               out revisedPublishingInterval,
               out revisedLifetimeCount,
               out revisedMaxKeepAliveCount);

            response.RevisedPublishingInterval = revisedPublishingInterval;
            response.RevisedLifetimeCount      = revisedLifetimeCount;
            response.RevisedMaxKeepAliveCount  = revisedMaxKeepAliveCount;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
        public virtual ModifySubscriptionResponseMessage ModifySubscription(ModifySubscriptionMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                ModifySubscriptionResponse response = (ModifySubscriptionResponse)ModifySubscription(request.ModifySubscriptionRequest);
                return new ModifySubscriptionResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.ModifySubscriptionRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ModifySubscription service.
        /// </summary>
        public virtual IAsyncResult BeginModifySubscription(ModifySubscriptionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ModifySubscriptionRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.ModifySubscriptionRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ModifySubscription service to complete.
        /// </summary>
        public virtual ModifySubscriptionResponseMessage EndModifySubscription(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new ModifySubscriptionResponseMessage((ModifySubscriptionResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region SetPublishingMode Service
        #if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        public IServiceResponse SetPublishingMode(IServiceRequest incoming)
        {
            SetPublishingModeResponse response = null;

            SetPublishingModeRequest request = (SetPublishingModeRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new SetPublishingModeResponse();

            response.ResponseHeader = ServerInstance.SetPublishingMode(
               request.RequestHeader,
               request.PublishingEnabled,
               request.SubscriptionIds,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
        public virtual SetPublishingModeResponseMessage SetPublishingMode(SetPublishingModeMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                SetPublishingModeResponse response = (SetPublishingModeResponse)SetPublishingMode(request.SetPublishingModeRequest);
                return new SetPublishingModeResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.SetPublishingModeRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetPublishingMode service.
        /// </summary>
        public virtual IAsyncResult BeginSetPublishingMode(SetPublishingModeMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetPublishingModeRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.SetPublishingModeRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetPublishingMode service to complete.
        /// </summary>
        public virtual SetPublishingModeResponseMessage EndSetPublishingMode(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new SetPublishingModeResponseMessage((SetPublishingModeResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Publish Service
        #if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        public IServiceResponse Publish(IServiceRequest incoming)
        {
            PublishResponse response = null;

            PublishRequest request = (PublishRequest)incoming;

            uint subscriptionId = 0;
            UInt32Collection availableSequenceNumbers = null;
            bool moreNotifications = false;
            NotificationMessage notificationMessage = null;
            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new PublishResponse();

            response.ResponseHeader = ServerInstance.Publish(
               request.RequestHeader,
               request.SubscriptionAcknowledgements,
               out subscriptionId,
               out availableSequenceNumbers,
               out moreNotifications,
               out notificationMessage,
               out results,
               out diagnosticInfos);

            response.SubscriptionId           = subscriptionId;
            response.AvailableSequenceNumbers = availableSequenceNumbers;
            response.MoreNotifications        = moreNotifications;
            response.NotificationMessage      = notificationMessage;
            response.Results                  = results;
            response.DiagnosticInfos          = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
        public virtual PublishResponseMessage Publish(PublishMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                PublishResponse response = (PublishResponse)Publish(request.PublishRequest);
                return new PublishResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.PublishRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Publish service.
        /// </summary>
        public virtual IAsyncResult BeginPublish(PublishMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.PublishRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.PublishRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Publish service to complete.
        /// </summary>
        public virtual PublishResponseMessage EndPublish(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new PublishResponseMessage((PublishResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region Republish Service
        #if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        public IServiceResponse Republish(IServiceRequest incoming)
        {
            RepublishResponse response = null;

            RepublishRequest request = (RepublishRequest)incoming;

            NotificationMessage notificationMessage = null;

            response = new RepublishResponse();

            response.ResponseHeader = ServerInstance.Republish(
               request.RequestHeader,
               request.SubscriptionId,
               request.RetransmitSequenceNumber,
               out notificationMessage);

            response.NotificationMessage = notificationMessage;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
        public virtual RepublishResponseMessage Republish(RepublishMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                RepublishResponse response = (RepublishResponse)Republish(request.RepublishRequest);
                return new RepublishResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.RepublishRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Republish service.
        /// </summary>
        public virtual IAsyncResult BeginRepublish(RepublishMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RepublishRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.RepublishRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Republish service to complete.
        /// </summary>
        public virtual RepublishResponseMessage EndRepublish(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new RepublishResponseMessage((RepublishResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region TransferSubscriptions Service
        #if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        public IServiceResponse TransferSubscriptions(IServiceRequest incoming)
        {
            TransferSubscriptionsResponse response = null;

            TransferSubscriptionsRequest request = (TransferSubscriptionsRequest)incoming;

            TransferResultCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new TransferSubscriptionsResponse();

            response.ResponseHeader = ServerInstance.TransferSubscriptions(
               request.RequestHeader,
               request.SubscriptionIds,
               request.SendInitialValues,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
        public virtual TransferSubscriptionsResponseMessage TransferSubscriptions(TransferSubscriptionsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                TransferSubscriptionsResponse response = (TransferSubscriptionsResponse)TransferSubscriptions(request.TransferSubscriptionsRequest);
                return new TransferSubscriptionsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.TransferSubscriptionsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TransferSubscriptions service.
        /// </summary>
        public virtual IAsyncResult BeginTransferSubscriptions(TransferSubscriptionsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TransferSubscriptionsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.TransferSubscriptionsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TransferSubscriptions service to complete.
        /// </summary>
        public virtual TransferSubscriptionsResponseMessage EndTransferSubscriptions(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new TransferSubscriptionsResponseMessage((TransferSubscriptionsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region DeleteSubscriptions Service
        #if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        public IServiceResponse DeleteSubscriptions(IServiceRequest incoming)
        {
            DeleteSubscriptionsResponse response = null;

            DeleteSubscriptionsRequest request = (DeleteSubscriptionsRequest)incoming;

            StatusCodeCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            response = new DeleteSubscriptionsResponse();

            response.ResponseHeader = ServerInstance.DeleteSubscriptions(
               request.RequestHeader,
               request.SubscriptionIds,
               out results,
               out diagnosticInfos);

            response.Results         = results;
            response.DiagnosticInfos = diagnosticInfos;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
        public virtual DeleteSubscriptionsResponseMessage DeleteSubscriptions(DeleteSubscriptionsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                DeleteSubscriptionsResponse response = (DeleteSubscriptionsResponse)DeleteSubscriptions(request.DeleteSubscriptionsRequest);
                return new DeleteSubscriptionsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.DeleteSubscriptionsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteSubscriptions service.
        /// </summary>
        public virtual IAsyncResult BeginDeleteSubscriptions(DeleteSubscriptionsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteSubscriptionsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.DeleteSubscriptionsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteSubscriptions service to complete.
        /// </summary>
        public virtual DeleteSubscriptionsResponseMessage EndDeleteSubscriptions(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new DeleteSubscriptionsResponseMessage((DeleteSubscriptionsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region TestStack Service
        #if (!OPCUA_EXCLUDE_TestStack)
        /// <summary>
        /// Invokes the TestStack service.
        /// </summary>
        public IServiceResponse TestStack(IServiceRequest incoming)
        {
            TestStackResponse response = null;

            TestStackRequest request = (TestStackRequest)incoming;

            Variant output = new Variant();

            response = new TestStackResponse();

            response.ResponseHeader = ServerInstance.TestStack(
               request.RequestHeader,
               request.TestId,
               request.Iteration,
               request.Input,
               out output);

            response.Output = output;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the TestStack service.
        /// </summary>
        public virtual TestStackResponseMessage TestStack(TestStackMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                TestStackResponse response = (TestStackResponse)TestStack(request.TestStackRequest);
                return new TestStackResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.TestStackRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TestStack service.
        /// </summary>
        public virtual IAsyncResult BeginTestStack(TestStackMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TestStackRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.TestStackRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TestStack service to complete.
        /// </summary>
        public virtual TestStackResponseMessage EndTestStack(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new TestStackResponseMessage((TestStackResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region TestStackEx Service
        #if (!OPCUA_EXCLUDE_TestStackEx)
        /// <summary>
        /// Invokes the TestStackEx service.
        /// </summary>
        public IServiceResponse TestStackEx(IServiceRequest incoming)
        {
            TestStackExResponse response = null;

            TestStackExRequest request = (TestStackExRequest)incoming;

            CompositeTestType output = null;

            response = new TestStackExResponse();

            response.ResponseHeader = ServerInstance.TestStackEx(
               request.RequestHeader,
               request.TestId,
               request.Iteration,
               request.Input,
               out output);

            response.Output = output;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the TestStackEx service.
        /// </summary>
        public virtual TestStackExResponseMessage TestStackEx(TestStackExMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                TestStackExResponse response = (TestStackExResponse)TestStackEx(request.TestStackExRequest);
                return new TestStackExResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.TestStackExRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TestStackEx service.
        /// </summary>
        public virtual IAsyncResult BeginTestStackEx(TestStackExMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TestStackExRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.TestStackExRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TestStackEx service to complete.
        /// </summary>
        public virtual TestStackExResponseMessage EndTestStackEx(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new TestStackExResponseMessage((TestStackExResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion
        #endregion

        #region Protected Members
        /// <summary>
        /// Populates the known types table.
        /// </summary>
        protected virtual void CreateKnownTypes()
        {
            #if (!OPCUA_EXCLUDE_FindServers)
            SupportedServices.Add(DataTypes.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers)));
            #endif
            #if (!OPCUA_EXCLUDE_GetEndpoints)
            SupportedServices.Add(DataTypes.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints)));
            #endif
            #if (!OPCUA_EXCLUDE_CreateSession)
            SupportedServices.Add(DataTypes.CreateSessionRequest, new ServiceDefinition(typeof(CreateSessionRequest), new InvokeServiceEventHandler(CreateSession)));
            #endif
            #if (!OPCUA_EXCLUDE_ActivateSession)
            SupportedServices.Add(DataTypes.ActivateSessionRequest, new ServiceDefinition(typeof(ActivateSessionRequest), new InvokeServiceEventHandler(ActivateSession)));
            #endif
            #if (!OPCUA_EXCLUDE_CloseSession)
            SupportedServices.Add(DataTypes.CloseSessionRequest, new ServiceDefinition(typeof(CloseSessionRequest), new InvokeServiceEventHandler(CloseSession)));
            #endif
            #if (!OPCUA_EXCLUDE_Cancel)
            SupportedServices.Add(DataTypes.CancelRequest, new ServiceDefinition(typeof(CancelRequest), new InvokeServiceEventHandler(Cancel)));
            #endif
            #if (!OPCUA_EXCLUDE_AddNodes)
            SupportedServices.Add(DataTypes.AddNodesRequest, new ServiceDefinition(typeof(AddNodesRequest), new InvokeServiceEventHandler(AddNodes)));
            #endif
            #if (!OPCUA_EXCLUDE_AddReferences)
            SupportedServices.Add(DataTypes.AddReferencesRequest, new ServiceDefinition(typeof(AddReferencesRequest), new InvokeServiceEventHandler(AddReferences)));
            #endif
            #if (!OPCUA_EXCLUDE_DeleteNodes)
            SupportedServices.Add(DataTypes.DeleteNodesRequest, new ServiceDefinition(typeof(DeleteNodesRequest), new InvokeServiceEventHandler(DeleteNodes)));
            #endif
            #if (!OPCUA_EXCLUDE_DeleteReferences)
            SupportedServices.Add(DataTypes.DeleteReferencesRequest, new ServiceDefinition(typeof(DeleteReferencesRequest), new InvokeServiceEventHandler(DeleteReferences)));
            #endif
            #if (!OPCUA_EXCLUDE_Browse)
            SupportedServices.Add(DataTypes.BrowseRequest, new ServiceDefinition(typeof(BrowseRequest), new InvokeServiceEventHandler(Browse)));
            #endif
            #if (!OPCUA_EXCLUDE_BrowseNext)
            SupportedServices.Add(DataTypes.BrowseNextRequest, new ServiceDefinition(typeof(BrowseNextRequest), new InvokeServiceEventHandler(BrowseNext)));
            #endif
            #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
            SupportedServices.Add(DataTypes.TranslateBrowsePathsToNodeIdsRequest, new ServiceDefinition(typeof(TranslateBrowsePathsToNodeIdsRequest), new InvokeServiceEventHandler(TranslateBrowsePathsToNodeIds)));
            #endif
            #if (!OPCUA_EXCLUDE_RegisterNodes)
            SupportedServices.Add(DataTypes.RegisterNodesRequest, new ServiceDefinition(typeof(RegisterNodesRequest), new InvokeServiceEventHandler(RegisterNodes)));
            #endif
            #if (!OPCUA_EXCLUDE_UnregisterNodes)
            SupportedServices.Add(DataTypes.UnregisterNodesRequest, new ServiceDefinition(typeof(UnregisterNodesRequest), new InvokeServiceEventHandler(UnregisterNodes)));
            #endif
            #if (!OPCUA_EXCLUDE_QueryFirst)
            SupportedServices.Add(DataTypes.QueryFirstRequest, new ServiceDefinition(typeof(QueryFirstRequest), new InvokeServiceEventHandler(QueryFirst)));
            #endif
            #if (!OPCUA_EXCLUDE_QueryNext)
            SupportedServices.Add(DataTypes.QueryNextRequest, new ServiceDefinition(typeof(QueryNextRequest), new InvokeServiceEventHandler(QueryNext)));
            #endif
            #if (!OPCUA_EXCLUDE_Read)
            SupportedServices.Add(DataTypes.ReadRequest, new ServiceDefinition(typeof(ReadRequest), new InvokeServiceEventHandler(Read)));
            #endif
            #if (!OPCUA_EXCLUDE_HistoryRead)
            SupportedServices.Add(DataTypes.HistoryReadRequest, new ServiceDefinition(typeof(HistoryReadRequest), new InvokeServiceEventHandler(HistoryRead)));
            #endif
            #if (!OPCUA_EXCLUDE_Write)
            SupportedServices.Add(DataTypes.WriteRequest, new ServiceDefinition(typeof(WriteRequest), new InvokeServiceEventHandler(Write)));
            #endif
            #if (!OPCUA_EXCLUDE_HistoryUpdate)
            SupportedServices.Add(DataTypes.HistoryUpdateRequest, new ServiceDefinition(typeof(HistoryUpdateRequest), new InvokeServiceEventHandler(HistoryUpdate)));
            #endif
            #if (!OPCUA_EXCLUDE_Call)
            SupportedServices.Add(DataTypes.CallRequest, new ServiceDefinition(typeof(CallRequest), new InvokeServiceEventHandler(Call)));
            #endif
            #if (!OPCUA_EXCLUDE_CreateMonitoredItems)
            SupportedServices.Add(DataTypes.CreateMonitoredItemsRequest, new ServiceDefinition(typeof(CreateMonitoredItemsRequest), new InvokeServiceEventHandler(CreateMonitoredItems)));
            #endif
            #if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
            SupportedServices.Add(DataTypes.ModifyMonitoredItemsRequest, new ServiceDefinition(typeof(ModifyMonitoredItemsRequest), new InvokeServiceEventHandler(ModifyMonitoredItems)));
            #endif
            #if (!OPCUA_EXCLUDE_SetMonitoringMode)
            SupportedServices.Add(DataTypes.SetMonitoringModeRequest, new ServiceDefinition(typeof(SetMonitoringModeRequest), new InvokeServiceEventHandler(SetMonitoringMode)));
            #endif
            #if (!OPCUA_EXCLUDE_SetTriggering)
            SupportedServices.Add(DataTypes.SetTriggeringRequest, new ServiceDefinition(typeof(SetTriggeringRequest), new InvokeServiceEventHandler(SetTriggering)));
            #endif
            #if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
            SupportedServices.Add(DataTypes.DeleteMonitoredItemsRequest, new ServiceDefinition(typeof(DeleteMonitoredItemsRequest), new InvokeServiceEventHandler(DeleteMonitoredItems)));
            #endif
            #if (!OPCUA_EXCLUDE_CreateSubscription)
            SupportedServices.Add(DataTypes.CreateSubscriptionRequest, new ServiceDefinition(typeof(CreateSubscriptionRequest), new InvokeServiceEventHandler(CreateSubscription)));
            #endif
            #if (!OPCUA_EXCLUDE_ModifySubscription)
            SupportedServices.Add(DataTypes.ModifySubscriptionRequest, new ServiceDefinition(typeof(ModifySubscriptionRequest), new InvokeServiceEventHandler(ModifySubscription)));
            #endif
            #if (!OPCUA_EXCLUDE_SetPublishingMode)
            SupportedServices.Add(DataTypes.SetPublishingModeRequest, new ServiceDefinition(typeof(SetPublishingModeRequest), new InvokeServiceEventHandler(SetPublishingMode)));
            #endif
            #if (!OPCUA_EXCLUDE_Publish)
            SupportedServices.Add(DataTypes.PublishRequest, new ServiceDefinition(typeof(PublishRequest), new InvokeServiceEventHandler(Publish)));
            #endif
            #if (!OPCUA_EXCLUDE_Republish)
            SupportedServices.Add(DataTypes.RepublishRequest, new ServiceDefinition(typeof(RepublishRequest), new InvokeServiceEventHandler(Republish)));
            #endif
            #if (!OPCUA_EXCLUDE_TransferSubscriptions)
            SupportedServices.Add(DataTypes.TransferSubscriptionsRequest, new ServiceDefinition(typeof(TransferSubscriptionsRequest), new InvokeServiceEventHandler(TransferSubscriptions)));
            #endif
            #if (!OPCUA_EXCLUDE_DeleteSubscriptions)
            SupportedServices.Add(DataTypes.DeleteSubscriptionsRequest, new ServiceDefinition(typeof(DeleteSubscriptionsRequest), new InvokeServiceEventHandler(DeleteSubscriptions)));
            #endif
            #if (!OPCUA_EXCLUDE_TestStack)
            SupportedServices.Add(DataTypes.TestStackRequest, new ServiceDefinition(typeof(TestStackRequest), new InvokeServiceEventHandler(TestStack)));
            #endif
            #if (!OPCUA_EXCLUDE_TestStackEx)
            SupportedServices.Add(DataTypes.TestStackExRequest, new ServiceDefinition(typeof(TestStackExRequest), new InvokeServiceEventHandler(TestStackEx)));
            #endif
        }
        #endregion
    }
    #endregion

    #region DiscoveryEndpoint Class
    /// <summary>
    /// A endpoint object used by clients to access a UA service.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.CodeGenerator", "1.0.0.0")]
    [ServiceMessageContextBehavior()]
    public partial class DiscoveryEndpoint : EndpointBase, IDiscoveryEndpoint, IRegistrationEndpoint
    {
        #region Constructors
        /// <summary>
        /// Initializes the object when it is created by the WCF framework.
        /// </summary>
        public DiscoveryEndpoint()
        {
            this.CreateKnownTypes();
        }

        /// <summary>
        /// Initializes the when it is created directly.
        /// </summary>
        public DiscoveryEndpoint(IServiceHostBase host) : base(host)
        {
            this.CreateKnownTypes();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscoveryEndpoint"/> class.
        /// </summary>
        /// <param name="server">The server.</param>
        public DiscoveryEndpoint(ServerBase server) : base(server)
        {
            this.CreateKnownTypes();
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The UA server instance that the endpoint is connected to.
        /// </summary>
        protected IDiscoveryServer ServerInstance
        {
            get
            {
                if (ServiceResult.IsBad(ServerError))
                {
                    throw new ServiceResultException(ServerError);
                }

                return ServerForContext as IDiscoveryServer;
             }
        }
        #endregion

        #region IDiscoveryEndpoint Members
        #region FindDnsServices Service
        #if (!OPCUA_EXCLUDE_FindDnsServices)
        /// <summary>
        /// Invokes the FindDnsServices service.
        /// </summary>
        public IServiceResponse FindDnsServices(IServiceRequest incoming)
        {
            FindDnsServicesResponse response = null;

            FindDnsServicesRequest request = (FindDnsServicesRequest)incoming;

            DnsServiceRecordCollection services = null;

            response = new FindDnsServicesResponse();

            response.ResponseHeader = ServerInstance.FindDnsServices(
               request.RequestHeader,
               request.EndpointUrl,
               request.ServiceNameFilters,
               request.ServiceTypeFilters,
               out services);

            response.Services = services;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the FindDnsServices service.
        /// </summary>
        public virtual FindDnsServicesResponseMessage FindDnsServices(FindDnsServicesMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                FindDnsServicesResponse response = (FindDnsServicesResponse)FindDnsServices(request.FindDnsServicesRequest);
                return new FindDnsServicesResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.FindDnsServicesRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindDnsServices service.
        /// </summary>
        public virtual IAsyncResult BeginFindDnsServices(FindDnsServicesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindDnsServicesRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.FindDnsServicesRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindDnsServices service to complete.
        /// </summary>
        public virtual FindDnsServicesResponseMessage EndFindDnsServices(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new FindDnsServicesResponseMessage((FindDnsServicesResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region FindServers Service
        #if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public IServiceResponse FindServers(IServiceRequest incoming)
        {
            FindServersResponse response = null;

            FindServersRequest request = (FindServersRequest)incoming;

            ApplicationDescriptionCollection servers = null;

            response = new FindServersResponse();

            response.ResponseHeader = ServerInstance.FindServers(
               request.RequestHeader,
               request.EndpointUrl,
               request.LocaleIds,
               request.ServerUris,
               out servers);

            response.Servers = servers;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
        public virtual FindServersResponseMessage FindServers(FindServersMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                FindServersResponse response = (FindServersResponse)FindServers(request.FindServersRequest);
                return new FindServersResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.FindServersRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServers service.
        /// </summary>
        public virtual IAsyncResult BeginFindServers(FindServersMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.FindServersRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServers service to complete.
        /// </summary>
        public virtual FindServersResponseMessage EndFindServers(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new FindServersResponseMessage((FindServersResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region GetEndpoints Service
        #if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public IServiceResponse GetEndpoints(IServiceRequest incoming)
        {
            GetEndpointsResponse response = null;

            GetEndpointsRequest request = (GetEndpointsRequest)incoming;

            EndpointDescriptionCollection endpoints = null;

            response = new GetEndpointsResponse();

            response.ResponseHeader = ServerInstance.GetEndpoints(
               request.RequestHeader,
               request.EndpointUrl,
               request.LocaleIds,
               request.ProfileUris,
               out endpoints);

            response.Endpoints = endpoints;

            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
        public virtual GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                GetEndpointsResponse response = (GetEndpointsResponse)GetEndpoints(request.GetEndpointsRequest);
                return new GetEndpointsResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.GetEndpointsRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the GetEndpoints service.
        /// </summary>
        public virtual IAsyncResult BeginGetEndpoints(GetEndpointsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.GetEndpointsRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.GetEndpointsRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the GetEndpoints service to complete.
        /// </summary>
        public virtual GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new GetEndpointsResponseMessage((GetEndpointsResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion

        #region RegisterServer Service
        #if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// Invokes the RegisterServer service.
        /// </summary>
        public IServiceResponse RegisterServer(IServiceRequest incoming)
        {
            RegisterServerResponse response = null;

            RegisterServerRequest request = (RegisterServerRequest)incoming;


            response = new RegisterServerResponse();

            response.ResponseHeader = ServerInstance.RegisterServer(
               request.RequestHeader,
               request.Server);


            return response;
        }

        #if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
        public virtual RegisterServerResponseMessage RegisterServer(RegisterServerMessage request)
        {
            try
            {
                SetRequestContext(RequestEncoding.Xml);
                RegisterServerResponse response = (RegisterServerResponse)RegisterServer(request.RegisterServerRequest);
                return new RegisterServerResponseMessage(response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(request.RegisterServerRequest, e);
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the RegisterServer service.
        /// </summary>
        public virtual IAsyncResult BeginRegisterServer(RegisterServerMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException("message");

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RegisterServerRequest);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(message.RegisterServerRequest, e);
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the RegisterServer service to complete.
        /// </summary>
        public virtual RegisterServerResponseMessage EndRegisterServer(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                return new RegisterServerResponseMessage((RegisterServerResponse)response);
            }
            catch (Exception e)
            {
                throw CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
            }
        }
        #endif
        #endif
        #endregion
        #endregion

        #region Protected Members
        /// <summary>
        /// Populates the known types table.
        /// </summary>
        protected virtual void CreateKnownTypes()
        {
            #if (!OPCUA_EXCLUDE_FindDnsServices)
            SupportedServices.Add(DataTypes.FindDnsServicesRequest, new ServiceDefinition(typeof(FindDnsServicesRequest), new InvokeServiceEventHandler(FindDnsServices)));
            #endif
            #if (!OPCUA_EXCLUDE_FindServers)
            SupportedServices.Add(DataTypes.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers)));
            #endif
            #if (!OPCUA_EXCLUDE_GetEndpoints)
            SupportedServices.Add(DataTypes.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints)));
            #endif
            #if (!OPCUA_EXCLUDE_RegisterServer)
            SupportedServices.Add(DataTypes.RegisterServerRequest, new ServiceDefinition(typeof(RegisterServerRequest), new InvokeServiceEventHandler(RegisterServer)));
            #endif
        }
        #endregion
    }
    #endregion
}