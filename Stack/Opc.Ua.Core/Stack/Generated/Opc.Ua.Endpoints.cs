/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

#if (!NET_STANDARD)
using System.Collections.Generic;
using System.Xml;
using System.Threading;
using System.Security.Principal;
using System.ServiceModel;
using System.Runtime.Serialization;
#endif

#if (NET_STANDARD_ASYNC)
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Opc.Ua
{
    #region SessionEndpoint Class
    /// <summary>
    /// A endpoint object used by clients to access a UA service.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    #if (!NET_STANDARD)
    [ServiceMessageContextBehavior()]
    [ServiceBehavior(Namespace = Namespaces.OpcUaWsdl, InstanceContextMode=InstanceContextMode.PerSession, ConcurrencyMode=ConcurrencyMode.Multiple)]
    #endif
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
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServers_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
        public IServiceResponse FindServers(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            FindServersResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersRequest request = (FindServersRequest)incoming;

                ApplicationDescriptionCollection servers = null;

                response = new FindServersResponse();

                response.ResponseHeader = ServerInstance.FindServers(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ServerUris,
                   out servers);

                response.Servers = servers;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
        public virtual FindServersResponseMessage FindServers(FindServersMessage request)
        {
            FindServersResponse response = null;

            try
            {
                // OnRequestReceived(message.FindServersRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (FindServersResponse)FindServers(request.FindServersRequest);

                // OnResponseSent(response);
                return new FindServersResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.FindServersRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServers service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
            public virtual IAsyncResult BeginFindServers(FindServersMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.FindServersRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.FindServersRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServers service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
            public virtual FindServersResponseMessage EndFindServers(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new FindServersResponseMessage((FindServersResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public async Task<IServiceResponse> FindServersAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            FindServersResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersRequest request = (FindServersRequest)incoming;

                response = await ServerInstance.FindServersAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ServerUris,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region FindServersOnNetwork Service
        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
        public IServiceResponse FindServersOnNetwork(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersOnNetworkRequest request = (FindServersOnNetworkRequest)incoming;

                DateTime lastCounterResetTime = DateTime.MinValue;
                ServerOnNetworkCollection servers = null;

                response = new FindServersOnNetworkResponse();

                response.ResponseHeader = ServerInstance.FindServersOnNetwork(
                   secureChannelContext,
                   request.RequestHeader,
                   request.StartingRecordId,
                   request.MaxRecordsToReturn,
                   request.ServerCapabilityFilter,
                   out lastCounterResetTime,
                   out servers);

                response.LastCounterResetTime = lastCounterResetTime;
                response.Servers              = servers;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
        public virtual FindServersOnNetworkResponseMessage FindServersOnNetwork(FindServersOnNetworkMessage request)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                // OnRequestReceived(message.FindServersOnNetworkRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (FindServersOnNetworkResponse)FindServersOnNetwork(request.FindServersOnNetworkRequest);

                // OnResponseSent(response);
                return new FindServersOnNetworkResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.FindServersOnNetworkRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServersOnNetwork service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
            public virtual IAsyncResult BeginFindServersOnNetwork(FindServersOnNetworkMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.FindServersOnNetworkRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersOnNetworkRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.FindServersOnNetworkRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServersOnNetwork service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
            public virtual FindServersOnNetworkResponseMessage EndFindServersOnNetwork(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new FindServersOnNetworkResponseMessage((FindServersOnNetworkResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        public async Task<IServiceResponse> FindServersOnNetworkAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersOnNetworkRequest request = (FindServersOnNetworkRequest)incoming;

                response = await ServerInstance.FindServersOnNetworkAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.StartingRecordId,
                   request.MaxRecordsToReturn,
                   request.ServerCapabilityFilter,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region GetEndpoints Service
        #if (!OPCUA_EXCLUDE_GetEndpoints)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
        public IServiceResponse GetEndpoints(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            GetEndpointsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                GetEndpointsRequest request = (GetEndpointsRequest)incoming;

                EndpointDescriptionCollection endpoints = null;

                response = new GetEndpointsResponse();

                response.ResponseHeader = ServerInstance.GetEndpoints(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ProfileUris,
                   out endpoints);

                response.Endpoints = endpoints;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
        public virtual GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request)
        {
            GetEndpointsResponse response = null;

            try
            {
                // OnRequestReceived(message.GetEndpointsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (GetEndpointsResponse)GetEndpoints(request.GetEndpointsRequest);

                // OnResponseSent(response);
                return new GetEndpointsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.GetEndpointsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the GetEndpoints service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginGetEndpoints(GetEndpointsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.GetEndpointsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.GetEndpointsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.GetEndpointsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the GetEndpoints service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
            public virtual GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new GetEndpointsResponseMessage((GetEndpointsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public async Task<IServiceResponse> GetEndpointsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            GetEndpointsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                GetEndpointsRequest request = (GetEndpointsRequest)incoming;

                response = await ServerInstance.GetEndpointsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ProfileUris,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region CreateSession Service
        #if (!OPCUA_EXCLUDE_CreateSession)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateSession_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CreateSessionAsync instead.")]
        #endif
        public IServiceResponse CreateSession(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CreateSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

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
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
        public virtual CreateSessionResponseMessage CreateSession(CreateSessionMessage request)
        {
            CreateSessionResponse response = null;

            try
            {
                // OnRequestReceived(message.CreateSessionRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CreateSessionResponse)CreateSession(request.CreateSessionRequest);

                // OnResponseSent(response);
                return new CreateSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CreateSessionRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateSession service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateSessionAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCreateSession(CreateSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CreateSessionRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateSessionRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CreateSessionRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateSession service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateSessionAsync instead.")]
        #endif
            public virtual CreateSessionResponseMessage EndCreateSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CreateSessionResponseMessage((CreateSessionResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSession_ASYNC)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        public async Task<IServiceResponse> CreateSessionAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CreateSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CreateSessionRequest request = (CreateSessionRequest)incoming;

                response = await ServerInstance.CreateSessionAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ClientDescription,
                   request.ServerUri,
                   request.EndpointUrl,
                   request.SessionName,
                   request.ClientNonce,
                   request.ClientCertificate,
                   request.RequestedSessionTimeout,
                   request.MaxResponseMessageSize,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region ActivateSession Service
        #if (!OPCUA_EXCLUDE_ActivateSession)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ActivateSession_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use ActivateSessionAsync instead.")]
        #endif
        public IServiceResponse ActivateSession(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            ActivateSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ActivateSessionRequest request = (ActivateSessionRequest)incoming;

                byte[] serverNonce = null;
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new ActivateSessionResponse();

                response.ResponseHeader = ServerInstance.ActivateSession(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
        public virtual ActivateSessionResponseMessage ActivateSession(ActivateSessionMessage request)
        {
            ActivateSessionResponse response = null;

            try
            {
                // OnRequestReceived(message.ActivateSessionRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (ActivateSessionResponse)ActivateSession(request.ActivateSessionRequest);

                // OnResponseSent(response);
                return new ActivateSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.ActivateSessionRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ActivateSession service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ActivateSessionAsync instead.")]
        #endif
            public virtual IAsyncResult BeginActivateSession(ActivateSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.ActivateSessionRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ActivateSessionRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.ActivateSessionRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ActivateSession service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ActivateSessionAsync instead.")]
        #endif
            public virtual ActivateSessionResponseMessage EndActivateSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new ActivateSessionResponseMessage((ActivateSessionResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ActivateSession_ASYNC)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        public async Task<IServiceResponse> ActivateSessionAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            ActivateSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ActivateSessionRequest request = (ActivateSessionRequest)incoming;

                response = await ServerInstance.ActivateSessionAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ClientSignature,
                   request.ClientSoftwareCertificates,
                   request.LocaleIds,
                   request.UserIdentityToken,
                   request.UserTokenSignature,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region CloseSession Service
        #if (!OPCUA_EXCLUDE_CloseSession)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CloseSession_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CloseSessionAsync instead.")]
        #endif
        public IServiceResponse CloseSession(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CloseSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CloseSessionRequest request = (CloseSessionRequest)incoming;


                response = new CloseSessionResponse();

                response.ResponseHeader = ServerInstance.CloseSession(
                   secureChannelContext,
                   request.RequestHeader,
                   request.DeleteSubscriptions);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
        public virtual CloseSessionResponseMessage CloseSession(CloseSessionMessage request)
        {
            CloseSessionResponse response = null;

            try
            {
                // OnRequestReceived(message.CloseSessionRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CloseSessionResponse)CloseSession(request.CloseSessionRequest);

                // OnResponseSent(response);
                return new CloseSessionResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CloseSessionRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CloseSession service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CloseSessionAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCloseSession(CloseSessionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CloseSessionRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CloseSessionRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CloseSessionRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CloseSession service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CloseSessionAsync instead.")]
        #endif
            public virtual CloseSessionResponseMessage EndCloseSession(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CloseSessionResponseMessage((CloseSessionResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CloseSession_ASYNC)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        public async Task<IServiceResponse> CloseSessionAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CloseSessionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CloseSessionRequest request = (CloseSessionRequest)incoming;

                response = await ServerInstance.CloseSessionAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.DeleteSubscriptions,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Cancel Service
        #if (!OPCUA_EXCLUDE_Cancel)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Cancel_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CancelAsync instead.")]
        #endif
        public IServiceResponse Cancel(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CancelResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CancelRequest request = (CancelRequest)incoming;

                uint cancelCount = 0;

                response = new CancelResponse();

                response.ResponseHeader = ServerInstance.Cancel(
                   secureChannelContext,
                   request.RequestHeader,
                   request.RequestHandle,
                   out cancelCount);

                response.CancelCount = cancelCount;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
        public virtual CancelResponseMessage Cancel(CancelMessage request)
        {
            CancelResponse response = null;

            try
            {
                // OnRequestReceived(message.CancelRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CancelResponse)Cancel(request.CancelRequest);

                // OnResponseSent(response);
                return new CancelResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CancelRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Cancel service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CancelAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCancel(CancelMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CancelRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CancelRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CancelRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Cancel service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CancelAsync instead.")]
        #endif
            public virtual CancelResponseMessage EndCancel(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CancelResponseMessage((CancelResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Cancel_ASYNC)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        public async Task<IServiceResponse> CancelAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CancelResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CancelRequest request = (CancelRequest)incoming;

                response = await ServerInstance.CancelAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.RequestHandle,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region AddNodes Service
        #if (!OPCUA_EXCLUDE_AddNodes)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_AddNodes_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use AddNodesAsync instead.")]
        #endif
        public IServiceResponse AddNodes(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            AddNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                AddNodesRequest request = (AddNodesRequest)incoming;

                AddNodesResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new AddNodesResponse();

                response.ResponseHeader = ServerInstance.AddNodes(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToAdd,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
        public virtual AddNodesResponseMessage AddNodes(AddNodesMessage request)
        {
            AddNodesResponse response = null;

            try
            {
                // OnRequestReceived(message.AddNodesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (AddNodesResponse)AddNodes(request.AddNodesRequest);

                // OnResponseSent(response);
                return new AddNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.AddNodesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the AddNodes service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use AddNodesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginAddNodes(AddNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.AddNodesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.AddNodesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.AddNodesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the AddNodes service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use AddNodesAsync instead.")]
        #endif
            public virtual AddNodesResponseMessage EndAddNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new AddNodesResponseMessage((AddNodesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddNodes_ASYNC)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        public async Task<IServiceResponse> AddNodesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            AddNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                AddNodesRequest request = (AddNodesRequest)incoming;

                response = await ServerInstance.AddNodesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToAdd,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region AddReferences Service
        #if (!OPCUA_EXCLUDE_AddReferences)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_AddReferences_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use AddReferencesAsync instead.")]
        #endif
        public IServiceResponse AddReferences(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            AddReferencesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                AddReferencesRequest request = (AddReferencesRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new AddReferencesResponse();

                response.ResponseHeader = ServerInstance.AddReferences(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReferencesToAdd,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
        public virtual AddReferencesResponseMessage AddReferences(AddReferencesMessage request)
        {
            AddReferencesResponse response = null;

            try
            {
                // OnRequestReceived(message.AddReferencesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (AddReferencesResponse)AddReferences(request.AddReferencesRequest);

                // OnResponseSent(response);
                return new AddReferencesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.AddReferencesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the AddReferences service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use AddReferencesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginAddReferences(AddReferencesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.AddReferencesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.AddReferencesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.AddReferencesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the AddReferences service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use AddReferencesAsync instead.")]
        #endif
            public virtual AddReferencesResponseMessage EndAddReferences(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new AddReferencesResponseMessage((AddReferencesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_AddReferences_ASYNC)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        public async Task<IServiceResponse> AddReferencesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            AddReferencesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                AddReferencesRequest request = (AddReferencesRequest)incoming;

                response = await ServerInstance.AddReferencesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReferencesToAdd,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region DeleteNodes Service
        #if (!OPCUA_EXCLUDE_DeleteNodes)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteNodes_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use DeleteNodesAsync instead.")]
        #endif
        public IServiceResponse DeleteNodes(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            DeleteNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteNodesRequest request = (DeleteNodesRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new DeleteNodesResponse();

                response.ResponseHeader = ServerInstance.DeleteNodes(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToDelete,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
        public virtual DeleteNodesResponseMessage DeleteNodes(DeleteNodesMessage request)
        {
            DeleteNodesResponse response = null;

            try
            {
                // OnRequestReceived(message.DeleteNodesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (DeleteNodesResponse)DeleteNodes(request.DeleteNodesRequest);

                // OnResponseSent(response);
                return new DeleteNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.DeleteNodesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteNodes service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteNodesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginDeleteNodes(DeleteNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.DeleteNodesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteNodesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.DeleteNodesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteNodes service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteNodesAsync instead.")]
        #endif
            public virtual DeleteNodesResponseMessage EndDeleteNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new DeleteNodesResponseMessage((DeleteNodesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteNodes_ASYNC)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        public async Task<IServiceResponse> DeleteNodesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            DeleteNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteNodesRequest request = (DeleteNodesRequest)incoming;

                response = await ServerInstance.DeleteNodesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToDelete,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region DeleteReferences Service
        #if (!OPCUA_EXCLUDE_DeleteReferences)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteReferences_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use DeleteReferencesAsync instead.")]
        #endif
        public IServiceResponse DeleteReferences(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            DeleteReferencesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteReferencesRequest request = (DeleteReferencesRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new DeleteReferencesResponse();

                response.ResponseHeader = ServerInstance.DeleteReferences(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReferencesToDelete,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
        public virtual DeleteReferencesResponseMessage DeleteReferences(DeleteReferencesMessage request)
        {
            DeleteReferencesResponse response = null;

            try
            {
                // OnRequestReceived(message.DeleteReferencesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (DeleteReferencesResponse)DeleteReferences(request.DeleteReferencesRequest);

                // OnResponseSent(response);
                return new DeleteReferencesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.DeleteReferencesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteReferences service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteReferencesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginDeleteReferences(DeleteReferencesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.DeleteReferencesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteReferencesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.DeleteReferencesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteReferences service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteReferencesAsync instead.")]
        #endif
            public virtual DeleteReferencesResponseMessage EndDeleteReferences(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new DeleteReferencesResponseMessage((DeleteReferencesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteReferences_ASYNC)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        public async Task<IServiceResponse> DeleteReferencesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            DeleteReferencesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteReferencesRequest request = (DeleteReferencesRequest)incoming;

                response = await ServerInstance.DeleteReferencesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReferencesToDelete,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Browse Service
        #if (!OPCUA_EXCLUDE_Browse)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Browse_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use BrowseAsync instead.")]
        #endif
        public IServiceResponse Browse(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            BrowseResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                BrowseRequest request = (BrowseRequest)incoming;

                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new BrowseResponse();

                response.ResponseHeader = ServerInstance.Browse(
                   secureChannelContext,
                   request.RequestHeader,
                   request.View,
                   request.RequestedMaxReferencesPerNode,
                   request.NodesToBrowse,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
        public virtual BrowseResponseMessage Browse(BrowseMessage request)
        {
            BrowseResponse response = null;

            try
            {
                // OnRequestReceived(message.BrowseRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (BrowseResponse)Browse(request.BrowseRequest);

                // OnResponseSent(response);
                return new BrowseResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.BrowseRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Browse service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use BrowseAsync instead.")]
        #endif
            public virtual IAsyncResult BeginBrowse(BrowseMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.BrowseRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.BrowseRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.BrowseRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Browse service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use BrowseAsync instead.")]
        #endif
            public virtual BrowseResponseMessage EndBrowse(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new BrowseResponseMessage((BrowseResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Browse_ASYNC)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        public async Task<IServiceResponse> BrowseAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            BrowseResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                BrowseRequest request = (BrowseRequest)incoming;

                response = await ServerInstance.BrowseAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.View,
                   request.RequestedMaxReferencesPerNode,
                   request.NodesToBrowse,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region BrowseNext Service
        #if (!OPCUA_EXCLUDE_BrowseNext)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_BrowseNext_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use BrowseNextAsync instead.")]
        #endif
        public IServiceResponse BrowseNext(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            BrowseNextResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                BrowseNextRequest request = (BrowseNextRequest)incoming;

                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new BrowseNextResponse();

                response.ResponseHeader = ServerInstance.BrowseNext(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReleaseContinuationPoints,
                   request.ContinuationPoints,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
        public virtual BrowseNextResponseMessage BrowseNext(BrowseNextMessage request)
        {
            BrowseNextResponse response = null;

            try
            {
                // OnRequestReceived(message.BrowseNextRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (BrowseNextResponse)BrowseNext(request.BrowseNextRequest);

                // OnResponseSent(response);
                return new BrowseNextResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.BrowseNextRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the BrowseNext service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use BrowseNextAsync instead.")]
        #endif
            public virtual IAsyncResult BeginBrowseNext(BrowseNextMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.BrowseNextRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.BrowseNextRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.BrowseNextRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the BrowseNext service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use BrowseNextAsync instead.")]
        #endif
            public virtual BrowseNextResponseMessage EndBrowseNext(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new BrowseNextResponseMessage((BrowseNextResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_BrowseNext_ASYNC)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        public async Task<IServiceResponse> BrowseNextAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            BrowseNextResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                BrowseNextRequest request = (BrowseNextRequest)incoming;

                response = await ServerInstance.BrowseNextAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReleaseContinuationPoints,
                   request.ContinuationPoints,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region TranslateBrowsePathsToNodeIds Service
        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use TranslateBrowsePathsToNodeIdsAsync instead.")]
        #endif
        public IServiceResponse TranslateBrowsePathsToNodeIds(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            TranslateBrowsePathsToNodeIdsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                TranslateBrowsePathsToNodeIdsRequest request = (TranslateBrowsePathsToNodeIdsRequest)incoming;

                BrowsePathResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new TranslateBrowsePathsToNodeIdsResponse();

                response.ResponseHeader = ServerInstance.TranslateBrowsePathsToNodeIds(
                   secureChannelContext,
                   request.RequestHeader,
                   request.BrowsePaths,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public virtual TranslateBrowsePathsToNodeIdsResponseMessage TranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request)
        {
            TranslateBrowsePathsToNodeIdsResponse response = null;

            try
            {
                // OnRequestReceived(message.TranslateBrowsePathsToNodeIdsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (TranslateBrowsePathsToNodeIdsResponse)TranslateBrowsePathsToNodeIds(request.TranslateBrowsePathsToNodeIdsRequest);

                // OnResponseSent(response);
                return new TranslateBrowsePathsToNodeIdsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.TranslateBrowsePathsToNodeIdsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use TranslateBrowsePathsToNodeIdsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginTranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.TranslateBrowsePathsToNodeIdsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TranslateBrowsePathsToNodeIdsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.TranslateBrowsePathsToNodeIdsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TranslateBrowsePathsToNodeIds service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use TranslateBrowsePathsToNodeIdsAsync instead.")]
        #endif
            public virtual TranslateBrowsePathsToNodeIdsResponseMessage EndTranslateBrowsePathsToNodeIds(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new TranslateBrowsePathsToNodeIdsResponseMessage((TranslateBrowsePathsToNodeIdsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        public async Task<IServiceResponse> TranslateBrowsePathsToNodeIdsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            TranslateBrowsePathsToNodeIdsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                TranslateBrowsePathsToNodeIdsRequest request = (TranslateBrowsePathsToNodeIdsRequest)incoming;

                response = await ServerInstance.TranslateBrowsePathsToNodeIdsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.BrowsePaths,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region RegisterNodes Service
        #if (!OPCUA_EXCLUDE_RegisterNodes)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterNodes_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use RegisterNodesAsync instead.")]
        #endif
        public IServiceResponse RegisterNodes(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            RegisterNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterNodesRequest request = (RegisterNodesRequest)incoming;

                NodeIdCollection registeredNodeIds = null;

                response = new RegisterNodesResponse();

                response.ResponseHeader = ServerInstance.RegisterNodes(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToRegister,
                   out registeredNodeIds);

                response.RegisteredNodeIds = registeredNodeIds;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
        public virtual RegisterNodesResponseMessage RegisterNodes(RegisterNodesMessage request)
        {
            RegisterNodesResponse response = null;

            try
            {
                // OnRequestReceived(message.RegisterNodesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (RegisterNodesResponse)RegisterNodes(request.RegisterNodesRequest);

                // OnResponseSent(response);
                return new RegisterNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.RegisterNodesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the RegisterNodes service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterNodesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginRegisterNodes(RegisterNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.RegisterNodesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RegisterNodesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.RegisterNodesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the RegisterNodes service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterNodesAsync instead.")]
        #endif
            public virtual RegisterNodesResponseMessage EndRegisterNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new RegisterNodesResponseMessage((RegisterNodesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterNodes_ASYNC)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        public async Task<IServiceResponse> RegisterNodesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            RegisterNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterNodesRequest request = (RegisterNodesRequest)incoming;

                response = await ServerInstance.RegisterNodesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToRegister,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region UnregisterNodes Service
        #if (!OPCUA_EXCLUDE_UnregisterNodes)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use UnregisterNodesAsync instead.")]
        #endif
        public IServiceResponse UnregisterNodes(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            UnregisterNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                UnregisterNodesRequest request = (UnregisterNodesRequest)incoming;


                response = new UnregisterNodesResponse();

                response.ResponseHeader = ServerInstance.UnregisterNodes(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToUnregister);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
        public virtual UnregisterNodesResponseMessage UnregisterNodes(UnregisterNodesMessage request)
        {
            UnregisterNodesResponse response = null;

            try
            {
                // OnRequestReceived(message.UnregisterNodesRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (UnregisterNodesResponse)UnregisterNodes(request.UnregisterNodesRequest);

                // OnResponseSent(response);
                return new UnregisterNodesResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.UnregisterNodesRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the UnregisterNodes service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use UnregisterNodesAsync instead.")]
        #endif
            public virtual IAsyncResult BeginUnregisterNodes(UnregisterNodesMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.UnregisterNodesRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.UnregisterNodesRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.UnregisterNodesRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the UnregisterNodes service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use UnregisterNodesAsync instead.")]
        #endif
            public virtual UnregisterNodesResponseMessage EndUnregisterNodes(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new UnregisterNodesResponseMessage((UnregisterNodesResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        public async Task<IServiceResponse> UnregisterNodesAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            UnregisterNodesResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                UnregisterNodesRequest request = (UnregisterNodesRequest)incoming;

                response = await ServerInstance.UnregisterNodesAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToUnregister,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region QueryFirst Service
        #if (!OPCUA_EXCLUDE_QueryFirst)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_QueryFirst_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use QueryFirstAsync instead.")]
        #endif
        public IServiceResponse QueryFirst(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            QueryFirstResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                QueryFirstRequest request = (QueryFirstRequest)incoming;

                QueryDataSetCollection queryDataSets = null;
                byte[] continuationPoint = null;
                ParsingResultCollection parsingResults = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                ContentFilterResult filterResult = null;

                response = new QueryFirstResponse();

                response.ResponseHeader = ServerInstance.QueryFirst(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
        public virtual QueryFirstResponseMessage QueryFirst(QueryFirstMessage request)
        {
            QueryFirstResponse response = null;

            try
            {
                // OnRequestReceived(message.QueryFirstRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (QueryFirstResponse)QueryFirst(request.QueryFirstRequest);

                // OnResponseSent(response);
                return new QueryFirstResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.QueryFirstRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the QueryFirst service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use QueryFirstAsync instead.")]
        #endif
            public virtual IAsyncResult BeginQueryFirst(QueryFirstMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.QueryFirstRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.QueryFirstRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.QueryFirstRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the QueryFirst service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use QueryFirstAsync instead.")]
        #endif
            public virtual QueryFirstResponseMessage EndQueryFirst(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new QueryFirstResponseMessage((QueryFirstResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryFirst_ASYNC)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        public async Task<IServiceResponse> QueryFirstAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            QueryFirstResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                QueryFirstRequest request = (QueryFirstRequest)incoming;

                response = await ServerInstance.QueryFirstAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.View,
                   request.NodeTypes,
                   request.Filter,
                   request.MaxDataSetsToReturn,
                   request.MaxReferencesToReturn,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region QueryNext Service
        #if (!OPCUA_EXCLUDE_QueryNext)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_QueryNext_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use QueryNextAsync instead.")]
        #endif
        public IServiceResponse QueryNext(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            QueryNextResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                QueryNextRequest request = (QueryNextRequest)incoming;

                QueryDataSetCollection queryDataSets = null;
                byte[] revisedContinuationPoint = null;

                response = new QueryNextResponse();

                response.ResponseHeader = ServerInstance.QueryNext(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReleaseContinuationPoint,
                   request.ContinuationPoint,
                   out queryDataSets,
                   out revisedContinuationPoint);

                response.QueryDataSets            = queryDataSets;
                response.RevisedContinuationPoint = revisedContinuationPoint;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
        public virtual QueryNextResponseMessage QueryNext(QueryNextMessage request)
        {
            QueryNextResponse response = null;

            try
            {
                // OnRequestReceived(message.QueryNextRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (QueryNextResponse)QueryNext(request.QueryNextRequest);

                // OnResponseSent(response);
                return new QueryNextResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.QueryNextRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the QueryNext service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use QueryNextAsync instead.")]
        #endif
            public virtual IAsyncResult BeginQueryNext(QueryNextMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.QueryNextRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.QueryNextRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.QueryNextRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the QueryNext service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use QueryNextAsync instead.")]
        #endif
            public virtual QueryNextResponseMessage EndQueryNext(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new QueryNextResponseMessage((QueryNextResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_QueryNext_ASYNC)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        public async Task<IServiceResponse> QueryNextAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            QueryNextResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                QueryNextRequest request = (QueryNextRequest)incoming;

                response = await ServerInstance.QueryNextAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.ReleaseContinuationPoint,
                   request.ContinuationPoint,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Read Service
        #if (!OPCUA_EXCLUDE_Read)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Read_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use ReadAsync instead.")]
        #endif
        public IServiceResponse Read(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            ReadResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ReadRequest request = (ReadRequest)incoming;

                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new ReadResponse();

                response.ResponseHeader = ServerInstance.Read(
                   secureChannelContext,
                   request.RequestHeader,
                   request.MaxAge,
                   request.TimestampsToReturn,
                   request.NodesToRead,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
        public virtual ReadResponseMessage Read(ReadMessage request)
        {
            ReadResponse response = null;

            try
            {
                // OnRequestReceived(message.ReadRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (ReadResponse)Read(request.ReadRequest);

                // OnResponseSent(response);
                return new ReadResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.ReadRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Read service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ReadAsync instead.")]
        #endif
            public virtual IAsyncResult BeginRead(ReadMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.ReadRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ReadRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.ReadRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Read service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ReadAsync instead.")]
        #endif
            public virtual ReadResponseMessage EndRead(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new ReadResponseMessage((ReadResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Read_ASYNC)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        public async Task<IServiceResponse> ReadAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            ReadResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ReadRequest request = (ReadRequest)incoming;

                response = await ServerInstance.ReadAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.MaxAge,
                   request.TimestampsToReturn,
                   request.NodesToRead,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region HistoryRead Service
        #if (!OPCUA_EXCLUDE_HistoryRead)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_HistoryRead_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use HistoryReadAsync instead.")]
        #endif
        public IServiceResponse HistoryRead(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            HistoryReadResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                HistoryReadRequest request = (HistoryReadRequest)incoming;

                HistoryReadResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new HistoryReadResponse();

                response.ResponseHeader = ServerInstance.HistoryRead(
                   secureChannelContext,
                   request.RequestHeader,
                   request.HistoryReadDetails,
                   request.TimestampsToReturn,
                   request.ReleaseContinuationPoints,
                   request.NodesToRead,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
        public virtual HistoryReadResponseMessage HistoryRead(HistoryReadMessage request)
        {
            HistoryReadResponse response = null;

            try
            {
                // OnRequestReceived(message.HistoryReadRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (HistoryReadResponse)HistoryRead(request.HistoryReadRequest);

                // OnResponseSent(response);
                return new HistoryReadResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.HistoryReadRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the HistoryRead service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use HistoryReadAsync instead.")]
        #endif
            public virtual IAsyncResult BeginHistoryRead(HistoryReadMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.HistoryReadRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.HistoryReadRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.HistoryReadRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the HistoryRead service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use HistoryReadAsync instead.")]
        #endif
            public virtual HistoryReadResponseMessage EndHistoryRead(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new HistoryReadResponseMessage((HistoryReadResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryRead_ASYNC)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        public async Task<IServiceResponse> HistoryReadAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            HistoryReadResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                HistoryReadRequest request = (HistoryReadRequest)incoming;

                response = await ServerInstance.HistoryReadAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.HistoryReadDetails,
                   request.TimestampsToReturn,
                   request.ReleaseContinuationPoints,
                   request.NodesToRead,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Write Service
        #if (!OPCUA_EXCLUDE_Write)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Write_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use WriteAsync instead.")]
        #endif
        public IServiceResponse Write(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            WriteResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                WriteRequest request = (WriteRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new WriteResponse();

                response.ResponseHeader = ServerInstance.Write(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToWrite,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
        public virtual WriteResponseMessage Write(WriteMessage request)
        {
            WriteResponse response = null;

            try
            {
                // OnRequestReceived(message.WriteRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (WriteResponse)Write(request.WriteRequest);

                // OnResponseSent(response);
                return new WriteResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.WriteRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Write service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use WriteAsync instead.")]
        #endif
            public virtual IAsyncResult BeginWrite(WriteMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.WriteRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.WriteRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.WriteRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Write service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use WriteAsync instead.")]
        #endif
            public virtual WriteResponseMessage EndWrite(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new WriteResponseMessage((WriteResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Write_ASYNC)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        public async Task<IServiceResponse> WriteAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            WriteResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                WriteRequest request = (WriteRequest)incoming;

                response = await ServerInstance.WriteAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.NodesToWrite,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region HistoryUpdate Service
        #if (!OPCUA_EXCLUDE_HistoryUpdate)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use HistoryUpdateAsync instead.")]
        #endif
        public IServiceResponse HistoryUpdate(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            HistoryUpdateResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                HistoryUpdateRequest request = (HistoryUpdateRequest)incoming;

                HistoryUpdateResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new HistoryUpdateResponse();

                response.ResponseHeader = ServerInstance.HistoryUpdate(
                   secureChannelContext,
                   request.RequestHeader,
                   request.HistoryUpdateDetails,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
        public virtual HistoryUpdateResponseMessage HistoryUpdate(HistoryUpdateMessage request)
        {
            HistoryUpdateResponse response = null;

            try
            {
                // OnRequestReceived(message.HistoryUpdateRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (HistoryUpdateResponse)HistoryUpdate(request.HistoryUpdateRequest);

                // OnResponseSent(response);
                return new HistoryUpdateResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.HistoryUpdateRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the HistoryUpdate service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use HistoryUpdateAsync instead.")]
        #endif
            public virtual IAsyncResult BeginHistoryUpdate(HistoryUpdateMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.HistoryUpdateRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.HistoryUpdateRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.HistoryUpdateRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the HistoryUpdate service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use HistoryUpdateAsync instead.")]
        #endif
            public virtual HistoryUpdateResponseMessage EndHistoryUpdate(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new HistoryUpdateResponseMessage((HistoryUpdateResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        public async Task<IServiceResponse> HistoryUpdateAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            HistoryUpdateResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                HistoryUpdateRequest request = (HistoryUpdateRequest)incoming;

                response = await ServerInstance.HistoryUpdateAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.HistoryUpdateDetails,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Call Service
        #if (!OPCUA_EXCLUDE_Call)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Call_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CallAsync instead.")]
        #endif
        public IServiceResponse Call(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CallResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CallRequest request = (CallRequest)incoming;

                CallMethodResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new CallResponse();

                response.ResponseHeader = ServerInstance.Call(
                   secureChannelContext,
                   request.RequestHeader,
                   request.MethodsToCall,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
        public virtual CallResponseMessage Call(CallMessage request)
        {
            CallResponse response = null;

            try
            {
                // OnRequestReceived(message.CallRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CallResponse)Call(request.CallRequest);

                // OnResponseSent(response);
                return new CallResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CallRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Call service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CallAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCall(CallMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CallRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CallRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CallRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Call service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CallAsync instead.")]
        #endif
            public virtual CallResponseMessage EndCall(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CallResponseMessage((CallResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Call_ASYNC)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        public async Task<IServiceResponse> CallAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CallResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CallRequest request = (CallRequest)incoming;

                response = await ServerInstance.CallAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.MethodsToCall,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region CreateMonitoredItems Service
        #if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CreateMonitoredItemsAsync instead.")]
        #endif
        public IServiceResponse CreateMonitoredItems(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CreateMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CreateMonitoredItemsRequest request = (CreateMonitoredItemsRequest)incoming;

                MonitoredItemCreateResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new CreateMonitoredItemsResponse();

                response.ResponseHeader = ServerInstance.CreateMonitoredItems(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.TimestampsToReturn,
                   request.ItemsToCreate,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
        public virtual CreateMonitoredItemsResponseMessage CreateMonitoredItems(CreateMonitoredItemsMessage request)
        {
            CreateMonitoredItemsResponse response = null;

            try
            {
                // OnRequestReceived(message.CreateMonitoredItemsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CreateMonitoredItemsResponse)CreateMonitoredItems(request.CreateMonitoredItemsRequest);

                // OnResponseSent(response);
                return new CreateMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CreateMonitoredItemsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateMonitoredItems service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateMonitoredItemsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCreateMonitoredItems(CreateMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CreateMonitoredItemsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CreateMonitoredItemsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateMonitoredItems service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateMonitoredItemsAsync instead.")]
        #endif
            public virtual CreateMonitoredItemsResponseMessage EndCreateMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CreateMonitoredItemsResponseMessage((CreateMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        public async Task<IServiceResponse> CreateMonitoredItemsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CreateMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CreateMonitoredItemsRequest request = (CreateMonitoredItemsRequest)incoming;

                response = await ServerInstance.CreateMonitoredItemsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.TimestampsToReturn,
                   request.ItemsToCreate,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region ModifyMonitoredItems Service
        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use ModifyMonitoredItemsAsync instead.")]
        #endif
        public IServiceResponse ModifyMonitoredItems(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            ModifyMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ModifyMonitoredItemsRequest request = (ModifyMonitoredItemsRequest)incoming;

                MonitoredItemModifyResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new ModifyMonitoredItemsResponse();

                response.ResponseHeader = ServerInstance.ModifyMonitoredItems(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.TimestampsToReturn,
                   request.ItemsToModify,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
        public virtual ModifyMonitoredItemsResponseMessage ModifyMonitoredItems(ModifyMonitoredItemsMessage request)
        {
            ModifyMonitoredItemsResponse response = null;

            try
            {
                // OnRequestReceived(message.ModifyMonitoredItemsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (ModifyMonitoredItemsResponse)ModifyMonitoredItems(request.ModifyMonitoredItemsRequest);

                // OnResponseSent(response);
                return new ModifyMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.ModifyMonitoredItemsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ModifyMonitoredItems service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ModifyMonitoredItemsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginModifyMonitoredItems(ModifyMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.ModifyMonitoredItemsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ModifyMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.ModifyMonitoredItemsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ModifyMonitoredItems service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ModifyMonitoredItemsAsync instead.")]
        #endif
            public virtual ModifyMonitoredItemsResponseMessage EndModifyMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new ModifyMonitoredItemsResponseMessage((ModifyMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        public async Task<IServiceResponse> ModifyMonitoredItemsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            ModifyMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ModifyMonitoredItemsRequest request = (ModifyMonitoredItemsRequest)incoming;

                response = await ServerInstance.ModifyMonitoredItemsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.TimestampsToReturn,
                   request.ItemsToModify,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region SetMonitoringMode Service
        #if (!OPCUA_EXCLUDE_SetMonitoringMode)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use SetMonitoringModeAsync instead.")]
        #endif
        public IServiceResponse SetMonitoringMode(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            SetMonitoringModeResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetMonitoringModeRequest request = (SetMonitoringModeRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new SetMonitoringModeResponse();

                response.ResponseHeader = ServerInstance.SetMonitoringMode(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.MonitoringMode,
                   request.MonitoredItemIds,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
        public virtual SetMonitoringModeResponseMessage SetMonitoringMode(SetMonitoringModeMessage request)
        {
            SetMonitoringModeResponse response = null;

            try
            {
                // OnRequestReceived(message.SetMonitoringModeRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (SetMonitoringModeResponse)SetMonitoringMode(request.SetMonitoringModeRequest);

                // OnResponseSent(response);
                return new SetMonitoringModeResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.SetMonitoringModeRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetMonitoringMode service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetMonitoringModeAsync instead.")]
        #endif
            public virtual IAsyncResult BeginSetMonitoringMode(SetMonitoringModeMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.SetMonitoringModeRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetMonitoringModeRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.SetMonitoringModeRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetMonitoringMode service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetMonitoringModeAsync instead.")]
        #endif
            public virtual SetMonitoringModeResponseMessage EndSetMonitoringMode(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new SetMonitoringModeResponseMessage((SetMonitoringModeResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        public async Task<IServiceResponse> SetMonitoringModeAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            SetMonitoringModeResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetMonitoringModeRequest request = (SetMonitoringModeRequest)incoming;

                response = await ServerInstance.SetMonitoringModeAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.MonitoringMode,
                   request.MonitoredItemIds,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region SetTriggering Service
        #if (!OPCUA_EXCLUDE_SetTriggering)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetTriggering_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use SetTriggeringAsync instead.")]
        #endif
        public IServiceResponse SetTriggering(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            SetTriggeringResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetTriggeringRequest request = (SetTriggeringRequest)incoming;

                StatusCodeCollection addResults = null;
                DiagnosticInfoCollection addDiagnosticInfos = null;
                StatusCodeCollection removeResults = null;
                DiagnosticInfoCollection removeDiagnosticInfos = null;

                response = new SetTriggeringResponse();

                response.ResponseHeader = ServerInstance.SetTriggering(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
        public virtual SetTriggeringResponseMessage SetTriggering(SetTriggeringMessage request)
        {
            SetTriggeringResponse response = null;

            try
            {
                // OnRequestReceived(message.SetTriggeringRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (SetTriggeringResponse)SetTriggering(request.SetTriggeringRequest);

                // OnResponseSent(response);
                return new SetTriggeringResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.SetTriggeringRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetTriggering service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetTriggeringAsync instead.")]
        #endif
            public virtual IAsyncResult BeginSetTriggering(SetTriggeringMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.SetTriggeringRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetTriggeringRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.SetTriggeringRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetTriggering service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetTriggeringAsync instead.")]
        #endif
            public virtual SetTriggeringResponseMessage EndSetTriggering(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new SetTriggeringResponseMessage((SetTriggeringResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetTriggering_ASYNC)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        public async Task<IServiceResponse> SetTriggeringAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            SetTriggeringResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetTriggeringRequest request = (SetTriggeringRequest)incoming;

                response = await ServerInstance.SetTriggeringAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.TriggeringItemId,
                   request.LinksToAdd,
                   request.LinksToRemove,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region DeleteMonitoredItems Service
        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use DeleteMonitoredItemsAsync instead.")]
        #endif
        public IServiceResponse DeleteMonitoredItems(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            DeleteMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteMonitoredItemsRequest request = (DeleteMonitoredItemsRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new DeleteMonitoredItemsResponse();

                response.ResponseHeader = ServerInstance.DeleteMonitoredItems(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.MonitoredItemIds,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
        public virtual DeleteMonitoredItemsResponseMessage DeleteMonitoredItems(DeleteMonitoredItemsMessage request)
        {
            DeleteMonitoredItemsResponse response = null;

            try
            {
                // OnRequestReceived(message.DeleteMonitoredItemsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (DeleteMonitoredItemsResponse)DeleteMonitoredItems(request.DeleteMonitoredItemsRequest);

                // OnResponseSent(response);
                return new DeleteMonitoredItemsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.DeleteMonitoredItemsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteMonitoredItems service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteMonitoredItemsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginDeleteMonitoredItems(DeleteMonitoredItemsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.DeleteMonitoredItemsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteMonitoredItemsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.DeleteMonitoredItemsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteMonitoredItems service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteMonitoredItemsAsync instead.")]
        #endif
            public virtual DeleteMonitoredItemsResponseMessage EndDeleteMonitoredItems(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new DeleteMonitoredItemsResponseMessage((DeleteMonitoredItemsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        public async Task<IServiceResponse> DeleteMonitoredItemsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            DeleteMonitoredItemsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteMonitoredItemsRequest request = (DeleteMonitoredItemsRequest)incoming;

                response = await ServerInstance.DeleteMonitoredItemsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.MonitoredItemIds,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region CreateSubscription Service
        #if (!OPCUA_EXCLUDE_CreateSubscription)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateSubscription_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use CreateSubscriptionAsync instead.")]
        #endif
        public IServiceResponse CreateSubscription(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            CreateSubscriptionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CreateSubscriptionRequest request = (CreateSubscriptionRequest)incoming;

                uint subscriptionId = 0;
                double revisedPublishingInterval = 0;
                uint revisedLifetimeCount = 0;
                uint revisedMaxKeepAliveCount = 0;

                response = new CreateSubscriptionResponse();

                response.ResponseHeader = ServerInstance.CreateSubscription(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
        public virtual CreateSubscriptionResponseMessage CreateSubscription(CreateSubscriptionMessage request)
        {
            CreateSubscriptionResponse response = null;

            try
            {
                // OnRequestReceived(message.CreateSubscriptionRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (CreateSubscriptionResponse)CreateSubscription(request.CreateSubscriptionRequest);

                // OnResponseSent(response);
                return new CreateSubscriptionResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.CreateSubscriptionRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the CreateSubscription service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateSubscriptionAsync instead.")]
        #endif
            public virtual IAsyncResult BeginCreateSubscription(CreateSubscriptionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.CreateSubscriptionRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.CreateSubscriptionRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.CreateSubscriptionRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the CreateSubscription service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use CreateSubscriptionAsync instead.")]
        #endif
            public virtual CreateSubscriptionResponseMessage EndCreateSubscription(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new CreateSubscriptionResponseMessage((CreateSubscriptionResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_CreateSubscription_ASYNC)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        public async Task<IServiceResponse> CreateSubscriptionAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            CreateSubscriptionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                CreateSubscriptionRequest request = (CreateSubscriptionRequest)incoming;

                response = await ServerInstance.CreateSubscriptionAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.RequestedPublishingInterval,
                   request.RequestedLifetimeCount,
                   request.RequestedMaxKeepAliveCount,
                   request.MaxNotificationsPerPublish,
                   request.PublishingEnabled,
                   request.Priority,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region ModifySubscription Service
        #if (!OPCUA_EXCLUDE_ModifySubscription)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ModifySubscription_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use ModifySubscriptionAsync instead.")]
        #endif
        public IServiceResponse ModifySubscription(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            ModifySubscriptionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ModifySubscriptionRequest request = (ModifySubscriptionRequest)incoming;

                double revisedPublishingInterval = 0;
                uint revisedLifetimeCount = 0;
                uint revisedMaxKeepAliveCount = 0;

                response = new ModifySubscriptionResponse();

                response.ResponseHeader = ServerInstance.ModifySubscription(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
        public virtual ModifySubscriptionResponseMessage ModifySubscription(ModifySubscriptionMessage request)
        {
            ModifySubscriptionResponse response = null;

            try
            {
                // OnRequestReceived(message.ModifySubscriptionRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (ModifySubscriptionResponse)ModifySubscription(request.ModifySubscriptionRequest);

                // OnResponseSent(response);
                return new ModifySubscriptionResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.ModifySubscriptionRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the ModifySubscription service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ModifySubscriptionAsync instead.")]
        #endif
            public virtual IAsyncResult BeginModifySubscription(ModifySubscriptionMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.ModifySubscriptionRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.ModifySubscriptionRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.ModifySubscriptionRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the ModifySubscription service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use ModifySubscriptionAsync instead.")]
        #endif
            public virtual ModifySubscriptionResponseMessage EndModifySubscription(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new ModifySubscriptionResponseMessage((ModifySubscriptionResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_ModifySubscription_ASYNC)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        public async Task<IServiceResponse> ModifySubscriptionAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            ModifySubscriptionResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                ModifySubscriptionRequest request = (ModifySubscriptionRequest)incoming;

                response = await ServerInstance.ModifySubscriptionAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.RequestedPublishingInterval,
                   request.RequestedLifetimeCount,
                   request.RequestedMaxKeepAliveCount,
                   request.MaxNotificationsPerPublish,
                   request.Priority,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region SetPublishingMode Service
        #if (!OPCUA_EXCLUDE_SetPublishingMode)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use SetPublishingModeAsync instead.")]
        #endif
        public IServiceResponse SetPublishingMode(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            SetPublishingModeResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetPublishingModeRequest request = (SetPublishingModeRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new SetPublishingModeResponse();

                response.ResponseHeader = ServerInstance.SetPublishingMode(
                   secureChannelContext,
                   request.RequestHeader,
                   request.PublishingEnabled,
                   request.SubscriptionIds,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
        public virtual SetPublishingModeResponseMessage SetPublishingMode(SetPublishingModeMessage request)
        {
            SetPublishingModeResponse response = null;

            try
            {
                // OnRequestReceived(message.SetPublishingModeRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (SetPublishingModeResponse)SetPublishingMode(request.SetPublishingModeRequest);

                // OnResponseSent(response);
                return new SetPublishingModeResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.SetPublishingModeRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the SetPublishingMode service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetPublishingModeAsync instead.")]
        #endif
            public virtual IAsyncResult BeginSetPublishingMode(SetPublishingModeMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.SetPublishingModeRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.SetPublishingModeRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.SetPublishingModeRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the SetPublishingMode service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use SetPublishingModeAsync instead.")]
        #endif
            public virtual SetPublishingModeResponseMessage EndSetPublishingMode(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new SetPublishingModeResponseMessage((SetPublishingModeResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        public async Task<IServiceResponse> SetPublishingModeAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            SetPublishingModeResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                SetPublishingModeRequest request = (SetPublishingModeRequest)incoming;

                response = await ServerInstance.SetPublishingModeAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.PublishingEnabled,
                   request.SubscriptionIds,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Publish Service
        #if (!OPCUA_EXCLUDE_Publish)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Publish_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use PublishAsync instead.")]
        #endif
        public IServiceResponse Publish(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            PublishResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                PublishRequest request = (PublishRequest)incoming;

                uint subscriptionId = 0;
                UInt32Collection availableSequenceNumbers = null;
                bool moreNotifications = false;
                NotificationMessage notificationMessage = null;
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new PublishResponse();

                response.ResponseHeader = ServerInstance.Publish(
                   secureChannelContext,
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
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
        public virtual PublishResponseMessage Publish(PublishMessage request)
        {
            PublishResponse response = null;

            try
            {
                // OnRequestReceived(message.PublishRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (PublishResponse)Publish(request.PublishRequest);

                // OnResponseSent(response);
                return new PublishResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.PublishRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Publish service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use PublishAsync instead.")]
        #endif
            public virtual IAsyncResult BeginPublish(PublishMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.PublishRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.PublishRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.PublishRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Publish service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use PublishAsync instead.")]
        #endif
            public virtual PublishResponseMessage EndPublish(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new PublishResponseMessage((PublishResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Publish_ASYNC)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        public async Task<IServiceResponse> PublishAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            PublishResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                PublishRequest request = (PublishRequest)incoming;

                response = await ServerInstance.PublishAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionAcknowledgements,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region Republish Service
        #if (!OPCUA_EXCLUDE_Republish)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Republish_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use RepublishAsync instead.")]
        #endif
        public IServiceResponse Republish(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            RepublishResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RepublishRequest request = (RepublishRequest)incoming;

                NotificationMessage notificationMessage = null;

                response = new RepublishResponse();

                response.ResponseHeader = ServerInstance.Republish(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.RetransmitSequenceNumber,
                   out notificationMessage);

                response.NotificationMessage = notificationMessage;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
        public virtual RepublishResponseMessage Republish(RepublishMessage request)
        {
            RepublishResponse response = null;

            try
            {
                // OnRequestReceived(message.RepublishRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (RepublishResponse)Republish(request.RepublishRequest);

                // OnResponseSent(response);
                return new RepublishResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.RepublishRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the Republish service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RepublishAsync instead.")]
        #endif
            public virtual IAsyncResult BeginRepublish(RepublishMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.RepublishRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RepublishRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.RepublishRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the Republish service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RepublishAsync instead.")]
        #endif
            public virtual RepublishResponseMessage EndRepublish(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new RepublishResponseMessage((RepublishResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_Republish_ASYNC)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        public async Task<IServiceResponse> RepublishAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            RepublishResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RepublishRequest request = (RepublishRequest)incoming;

                response = await ServerInstance.RepublishAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionId,
                   request.RetransmitSequenceNumber,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region TransferSubscriptions Service
        #if (!OPCUA_EXCLUDE_TransferSubscriptions)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use TransferSubscriptionsAsync instead.")]
        #endif
        public IServiceResponse TransferSubscriptions(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            TransferSubscriptionsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                TransferSubscriptionsRequest request = (TransferSubscriptionsRequest)incoming;

                TransferResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new TransferSubscriptionsResponse();

                response.ResponseHeader = ServerInstance.TransferSubscriptions(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionIds,
                   request.SendInitialValues,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
        public virtual TransferSubscriptionsResponseMessage TransferSubscriptions(TransferSubscriptionsMessage request)
        {
            TransferSubscriptionsResponse response = null;

            try
            {
                // OnRequestReceived(message.TransferSubscriptionsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (TransferSubscriptionsResponse)TransferSubscriptions(request.TransferSubscriptionsRequest);

                // OnResponseSent(response);
                return new TransferSubscriptionsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.TransferSubscriptionsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the TransferSubscriptions service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use TransferSubscriptionsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginTransferSubscriptions(TransferSubscriptionsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.TransferSubscriptionsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.TransferSubscriptionsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.TransferSubscriptionsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the TransferSubscriptions service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use TransferSubscriptionsAsync instead.")]
        #endif
            public virtual TransferSubscriptionsResponseMessage EndTransferSubscriptions(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new TransferSubscriptionsResponseMessage((TransferSubscriptionsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        public async Task<IServiceResponse> TransferSubscriptionsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            TransferSubscriptionsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                TransferSubscriptionsRequest request = (TransferSubscriptionsRequest)incoming;

                response = await ServerInstance.TransferSubscriptionsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionIds,
                   request.SendInitialValues,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region DeleteSubscriptions Service
        #if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use DeleteSubscriptionsAsync instead.")]
        #endif
        public IServiceResponse DeleteSubscriptions(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            DeleteSubscriptionsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteSubscriptionsRequest request = (DeleteSubscriptionsRequest)incoming;

                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new DeleteSubscriptionsResponse();

                response.ResponseHeader = ServerInstance.DeleteSubscriptions(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionIds,
                   out results,
                   out diagnosticInfos);

                response.Results         = results;
                response.DiagnosticInfos = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
        public virtual DeleteSubscriptionsResponseMessage DeleteSubscriptions(DeleteSubscriptionsMessage request)
        {
            DeleteSubscriptionsResponse response = null;

            try
            {
                // OnRequestReceived(message.DeleteSubscriptionsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (DeleteSubscriptionsResponse)DeleteSubscriptions(request.DeleteSubscriptionsRequest);

                // OnResponseSent(response);
                return new DeleteSubscriptionsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.DeleteSubscriptionsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the DeleteSubscriptions service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteSubscriptionsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginDeleteSubscriptions(DeleteSubscriptionsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.DeleteSubscriptionsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.DeleteSubscriptionsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.DeleteSubscriptionsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the DeleteSubscriptions service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use DeleteSubscriptionsAsync instead.")]
        #endif
            public virtual DeleteSubscriptionsResponseMessage EndDeleteSubscriptions(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new DeleteSubscriptionsResponseMessage((DeleteSubscriptionsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        public async Task<IServiceResponse> DeleteSubscriptionsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            DeleteSubscriptionsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                DeleteSubscriptionsRequest request = (DeleteSubscriptionsRequest)incoming;

                response = await ServerInstance.DeleteSubscriptionsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.SubscriptionIds,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
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
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServers && !OPCUA_EXCLUDE_FindServers_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceAsyncEventHandler(FindServersAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_FindServers && !OPCUA_EXCLUDE_FindServers_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers), new InvokeServiceAsyncEventHandler(FindServersAsync)));
            #elif (!OPCUA_EXCLUDE_FindServers)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServersOnNetwork && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceAsyncEventHandler(FindServersOnNetworkAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_FindServersOnNetwork && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceEventHandler(FindServersOnNetwork), new InvokeServiceAsyncEventHandler(FindServersOnNetworkAsync)));
            #elif (!OPCUA_EXCLUDE_FindServersOnNetwork)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceEventHandler(FindServersOnNetwork)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_GetEndpoints && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceAsyncEventHandler(GetEndpointsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_GetEndpoints && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints), new InvokeServiceAsyncEventHandler(GetEndpointsAsync)));
            #elif (!OPCUA_EXCLUDE_GetEndpoints)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateSession && !OPCUA_EXCLUDE_CreateSession_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateSessionRequest, new ServiceDefinition(typeof(CreateSessionRequest), new InvokeServiceAsyncEventHandler(CreateSessionAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_CreateSession && !OPCUA_EXCLUDE_CreateSession_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateSessionRequest, new ServiceDefinition(typeof(CreateSessionRequest), new InvokeServiceEventHandler(CreateSession), new InvokeServiceAsyncEventHandler(CreateSessionAsync)));
            #elif (!OPCUA_EXCLUDE_CreateSession)
            SupportedServices.Add(DataTypeIds.CreateSessionRequest, new ServiceDefinition(typeof(CreateSessionRequest), new InvokeServiceEventHandler(CreateSession)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ActivateSession && !OPCUA_EXCLUDE_ActivateSession_ASYNC)
            SupportedServices.Add(DataTypeIds.ActivateSessionRequest, new ServiceDefinition(typeof(ActivateSessionRequest), new InvokeServiceAsyncEventHandler(ActivateSessionAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_ActivateSession && !OPCUA_EXCLUDE_ActivateSession_ASYNC)
            SupportedServices.Add(DataTypeIds.ActivateSessionRequest, new ServiceDefinition(typeof(ActivateSessionRequest), new InvokeServiceEventHandler(ActivateSession), new InvokeServiceAsyncEventHandler(ActivateSessionAsync)));
            #elif (!OPCUA_EXCLUDE_ActivateSession)
            SupportedServices.Add(DataTypeIds.ActivateSessionRequest, new ServiceDefinition(typeof(ActivateSessionRequest), new InvokeServiceEventHandler(ActivateSession)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CloseSession && !OPCUA_EXCLUDE_CloseSession_ASYNC)
            SupportedServices.Add(DataTypeIds.CloseSessionRequest, new ServiceDefinition(typeof(CloseSessionRequest), new InvokeServiceAsyncEventHandler(CloseSessionAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_CloseSession && !OPCUA_EXCLUDE_CloseSession_ASYNC)
            SupportedServices.Add(DataTypeIds.CloseSessionRequest, new ServiceDefinition(typeof(CloseSessionRequest), new InvokeServiceEventHandler(CloseSession), new InvokeServiceAsyncEventHandler(CloseSessionAsync)));
            #elif (!OPCUA_EXCLUDE_CloseSession)
            SupportedServices.Add(DataTypeIds.CloseSessionRequest, new ServiceDefinition(typeof(CloseSessionRequest), new InvokeServiceEventHandler(CloseSession)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Cancel && !OPCUA_EXCLUDE_Cancel_ASYNC)
            SupportedServices.Add(DataTypeIds.CancelRequest, new ServiceDefinition(typeof(CancelRequest), new InvokeServiceAsyncEventHandler(CancelAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Cancel && !OPCUA_EXCLUDE_Cancel_ASYNC)
            SupportedServices.Add(DataTypeIds.CancelRequest, new ServiceDefinition(typeof(CancelRequest), new InvokeServiceEventHandler(Cancel), new InvokeServiceAsyncEventHandler(CancelAsync)));
            #elif (!OPCUA_EXCLUDE_Cancel)
            SupportedServices.Add(DataTypeIds.CancelRequest, new ServiceDefinition(typeof(CancelRequest), new InvokeServiceEventHandler(Cancel)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_AddNodes && !OPCUA_EXCLUDE_AddNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.AddNodesRequest, new ServiceDefinition(typeof(AddNodesRequest), new InvokeServiceAsyncEventHandler(AddNodesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_AddNodes && !OPCUA_EXCLUDE_AddNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.AddNodesRequest, new ServiceDefinition(typeof(AddNodesRequest), new InvokeServiceEventHandler(AddNodes), new InvokeServiceAsyncEventHandler(AddNodesAsync)));
            #elif (!OPCUA_EXCLUDE_AddNodes)
            SupportedServices.Add(DataTypeIds.AddNodesRequest, new ServiceDefinition(typeof(AddNodesRequest), new InvokeServiceEventHandler(AddNodes)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_AddReferences && !OPCUA_EXCLUDE_AddReferences_ASYNC)
            SupportedServices.Add(DataTypeIds.AddReferencesRequest, new ServiceDefinition(typeof(AddReferencesRequest), new InvokeServiceAsyncEventHandler(AddReferencesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_AddReferences && !OPCUA_EXCLUDE_AddReferences_ASYNC)
            SupportedServices.Add(DataTypeIds.AddReferencesRequest, new ServiceDefinition(typeof(AddReferencesRequest), new InvokeServiceEventHandler(AddReferences), new InvokeServiceAsyncEventHandler(AddReferencesAsync)));
            #elif (!OPCUA_EXCLUDE_AddReferences)
            SupportedServices.Add(DataTypeIds.AddReferencesRequest, new ServiceDefinition(typeof(AddReferencesRequest), new InvokeServiceEventHandler(AddReferences)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteNodes && !OPCUA_EXCLUDE_DeleteNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteNodesRequest, new ServiceDefinition(typeof(DeleteNodesRequest), new InvokeServiceAsyncEventHandler(DeleteNodesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_DeleteNodes && !OPCUA_EXCLUDE_DeleteNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteNodesRequest, new ServiceDefinition(typeof(DeleteNodesRequest), new InvokeServiceEventHandler(DeleteNodes), new InvokeServiceAsyncEventHandler(DeleteNodesAsync)));
            #elif (!OPCUA_EXCLUDE_DeleteNodes)
            SupportedServices.Add(DataTypeIds.DeleteNodesRequest, new ServiceDefinition(typeof(DeleteNodesRequest), new InvokeServiceEventHandler(DeleteNodes)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteReferences && !OPCUA_EXCLUDE_DeleteReferences_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteReferencesRequest, new ServiceDefinition(typeof(DeleteReferencesRequest), new InvokeServiceAsyncEventHandler(DeleteReferencesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_DeleteReferences && !OPCUA_EXCLUDE_DeleteReferences_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteReferencesRequest, new ServiceDefinition(typeof(DeleteReferencesRequest), new InvokeServiceEventHandler(DeleteReferences), new InvokeServiceAsyncEventHandler(DeleteReferencesAsync)));
            #elif (!OPCUA_EXCLUDE_DeleteReferences)
            SupportedServices.Add(DataTypeIds.DeleteReferencesRequest, new ServiceDefinition(typeof(DeleteReferencesRequest), new InvokeServiceEventHandler(DeleteReferences)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Browse && !OPCUA_EXCLUDE_Browse_ASYNC)
            SupportedServices.Add(DataTypeIds.BrowseRequest, new ServiceDefinition(typeof(BrowseRequest), new InvokeServiceAsyncEventHandler(BrowseAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Browse && !OPCUA_EXCLUDE_Browse_ASYNC)
            SupportedServices.Add(DataTypeIds.BrowseRequest, new ServiceDefinition(typeof(BrowseRequest), new InvokeServiceEventHandler(Browse), new InvokeServiceAsyncEventHandler(BrowseAsync)));
            #elif (!OPCUA_EXCLUDE_Browse)
            SupportedServices.Add(DataTypeIds.BrowseRequest, new ServiceDefinition(typeof(BrowseRequest), new InvokeServiceEventHandler(Browse)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_BrowseNext && !OPCUA_EXCLUDE_BrowseNext_ASYNC)
            SupportedServices.Add(DataTypeIds.BrowseNextRequest, new ServiceDefinition(typeof(BrowseNextRequest), new InvokeServiceAsyncEventHandler(BrowseNextAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_BrowseNext && !OPCUA_EXCLUDE_BrowseNext_ASYNC)
            SupportedServices.Add(DataTypeIds.BrowseNextRequest, new ServiceDefinition(typeof(BrowseNextRequest), new InvokeServiceEventHandler(BrowseNext), new InvokeServiceAsyncEventHandler(BrowseNextAsync)));
            #elif (!OPCUA_EXCLUDE_BrowseNext)
            SupportedServices.Add(DataTypeIds.BrowseNextRequest, new ServiceDefinition(typeof(BrowseNextRequest), new InvokeServiceEventHandler(BrowseNext)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds && !OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
            SupportedServices.Add(DataTypeIds.TranslateBrowsePathsToNodeIdsRequest, new ServiceDefinition(typeof(TranslateBrowsePathsToNodeIdsRequest), new InvokeServiceAsyncEventHandler(TranslateBrowsePathsToNodeIdsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds && !OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds_ASYNC)
            SupportedServices.Add(DataTypeIds.TranslateBrowsePathsToNodeIdsRequest, new ServiceDefinition(typeof(TranslateBrowsePathsToNodeIdsRequest), new InvokeServiceEventHandler(TranslateBrowsePathsToNodeIds), new InvokeServiceAsyncEventHandler(TranslateBrowsePathsToNodeIdsAsync)));
            #elif (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
            SupportedServices.Add(DataTypeIds.TranslateBrowsePathsToNodeIdsRequest, new ServiceDefinition(typeof(TranslateBrowsePathsToNodeIdsRequest), new InvokeServiceEventHandler(TranslateBrowsePathsToNodeIds)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterNodes && !OPCUA_EXCLUDE_RegisterNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterNodesRequest, new ServiceDefinition(typeof(RegisterNodesRequest), new InvokeServiceAsyncEventHandler(RegisterNodesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_RegisterNodes && !OPCUA_EXCLUDE_RegisterNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterNodesRequest, new ServiceDefinition(typeof(RegisterNodesRequest), new InvokeServiceEventHandler(RegisterNodes), new InvokeServiceAsyncEventHandler(RegisterNodesAsync)));
            #elif (!OPCUA_EXCLUDE_RegisterNodes)
            SupportedServices.Add(DataTypeIds.RegisterNodesRequest, new ServiceDefinition(typeof(RegisterNodesRequest), new InvokeServiceEventHandler(RegisterNodes)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_UnregisterNodes && !OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.UnregisterNodesRequest, new ServiceDefinition(typeof(UnregisterNodesRequest), new InvokeServiceAsyncEventHandler(UnregisterNodesAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_UnregisterNodes && !OPCUA_EXCLUDE_UnregisterNodes_ASYNC)
            SupportedServices.Add(DataTypeIds.UnregisterNodesRequest, new ServiceDefinition(typeof(UnregisterNodesRequest), new InvokeServiceEventHandler(UnregisterNodes), new InvokeServiceAsyncEventHandler(UnregisterNodesAsync)));
            #elif (!OPCUA_EXCLUDE_UnregisterNodes)
            SupportedServices.Add(DataTypeIds.UnregisterNodesRequest, new ServiceDefinition(typeof(UnregisterNodesRequest), new InvokeServiceEventHandler(UnregisterNodes)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_QueryFirst && !OPCUA_EXCLUDE_QueryFirst_ASYNC)
            SupportedServices.Add(DataTypeIds.QueryFirstRequest, new ServiceDefinition(typeof(QueryFirstRequest), new InvokeServiceAsyncEventHandler(QueryFirstAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_QueryFirst && !OPCUA_EXCLUDE_QueryFirst_ASYNC)
            SupportedServices.Add(DataTypeIds.QueryFirstRequest, new ServiceDefinition(typeof(QueryFirstRequest), new InvokeServiceEventHandler(QueryFirst), new InvokeServiceAsyncEventHandler(QueryFirstAsync)));
            #elif (!OPCUA_EXCLUDE_QueryFirst)
            SupportedServices.Add(DataTypeIds.QueryFirstRequest, new ServiceDefinition(typeof(QueryFirstRequest), new InvokeServiceEventHandler(QueryFirst)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_QueryNext && !OPCUA_EXCLUDE_QueryNext_ASYNC)
            SupportedServices.Add(DataTypeIds.QueryNextRequest, new ServiceDefinition(typeof(QueryNextRequest), new InvokeServiceAsyncEventHandler(QueryNextAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_QueryNext && !OPCUA_EXCLUDE_QueryNext_ASYNC)
            SupportedServices.Add(DataTypeIds.QueryNextRequest, new ServiceDefinition(typeof(QueryNextRequest), new InvokeServiceEventHandler(QueryNext), new InvokeServiceAsyncEventHandler(QueryNextAsync)));
            #elif (!OPCUA_EXCLUDE_QueryNext)
            SupportedServices.Add(DataTypeIds.QueryNextRequest, new ServiceDefinition(typeof(QueryNextRequest), new InvokeServiceEventHandler(QueryNext)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Read && !OPCUA_EXCLUDE_Read_ASYNC)
            SupportedServices.Add(DataTypeIds.ReadRequest, new ServiceDefinition(typeof(ReadRequest), new InvokeServiceAsyncEventHandler(ReadAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Read && !OPCUA_EXCLUDE_Read_ASYNC)
            SupportedServices.Add(DataTypeIds.ReadRequest, new ServiceDefinition(typeof(ReadRequest), new InvokeServiceEventHandler(Read), new InvokeServiceAsyncEventHandler(ReadAsync)));
            #elif (!OPCUA_EXCLUDE_Read)
            SupportedServices.Add(DataTypeIds.ReadRequest, new ServiceDefinition(typeof(ReadRequest), new InvokeServiceEventHandler(Read)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_HistoryRead && !OPCUA_EXCLUDE_HistoryRead_ASYNC)
            SupportedServices.Add(DataTypeIds.HistoryReadRequest, new ServiceDefinition(typeof(HistoryReadRequest), new InvokeServiceAsyncEventHandler(HistoryReadAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_HistoryRead && !OPCUA_EXCLUDE_HistoryRead_ASYNC)
            SupportedServices.Add(DataTypeIds.HistoryReadRequest, new ServiceDefinition(typeof(HistoryReadRequest), new InvokeServiceEventHandler(HistoryRead), new InvokeServiceAsyncEventHandler(HistoryReadAsync)));
            #elif (!OPCUA_EXCLUDE_HistoryRead)
            SupportedServices.Add(DataTypeIds.HistoryReadRequest, new ServiceDefinition(typeof(HistoryReadRequest), new InvokeServiceEventHandler(HistoryRead)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Write && !OPCUA_EXCLUDE_Write_ASYNC)
            SupportedServices.Add(DataTypeIds.WriteRequest, new ServiceDefinition(typeof(WriteRequest), new InvokeServiceAsyncEventHandler(WriteAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Write && !OPCUA_EXCLUDE_Write_ASYNC)
            SupportedServices.Add(DataTypeIds.WriteRequest, new ServiceDefinition(typeof(WriteRequest), new InvokeServiceEventHandler(Write), new InvokeServiceAsyncEventHandler(WriteAsync)));
            #elif (!OPCUA_EXCLUDE_Write)
            SupportedServices.Add(DataTypeIds.WriteRequest, new ServiceDefinition(typeof(WriteRequest), new InvokeServiceEventHandler(Write)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_HistoryUpdate && !OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
            SupportedServices.Add(DataTypeIds.HistoryUpdateRequest, new ServiceDefinition(typeof(HistoryUpdateRequest), new InvokeServiceAsyncEventHandler(HistoryUpdateAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_HistoryUpdate && !OPCUA_EXCLUDE_HistoryUpdate_ASYNC)
            SupportedServices.Add(DataTypeIds.HistoryUpdateRequest, new ServiceDefinition(typeof(HistoryUpdateRequest), new InvokeServiceEventHandler(HistoryUpdate), new InvokeServiceAsyncEventHandler(HistoryUpdateAsync)));
            #elif (!OPCUA_EXCLUDE_HistoryUpdate)
            SupportedServices.Add(DataTypeIds.HistoryUpdateRequest, new ServiceDefinition(typeof(HistoryUpdateRequest), new InvokeServiceEventHandler(HistoryUpdate)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Call && !OPCUA_EXCLUDE_Call_ASYNC)
            SupportedServices.Add(DataTypeIds.CallRequest, new ServiceDefinition(typeof(CallRequest), new InvokeServiceAsyncEventHandler(CallAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Call && !OPCUA_EXCLUDE_Call_ASYNC)
            SupportedServices.Add(DataTypeIds.CallRequest, new ServiceDefinition(typeof(CallRequest), new InvokeServiceEventHandler(Call), new InvokeServiceAsyncEventHandler(CallAsync)));
            #elif (!OPCUA_EXCLUDE_Call)
            SupportedServices.Add(DataTypeIds.CallRequest, new ServiceDefinition(typeof(CallRequest), new InvokeServiceEventHandler(Call)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateMonitoredItems && !OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateMonitoredItemsRequest, new ServiceDefinition(typeof(CreateMonitoredItemsRequest), new InvokeServiceAsyncEventHandler(CreateMonitoredItemsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_CreateMonitoredItems && !OPCUA_EXCLUDE_CreateMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateMonitoredItemsRequest, new ServiceDefinition(typeof(CreateMonitoredItemsRequest), new InvokeServiceEventHandler(CreateMonitoredItems), new InvokeServiceAsyncEventHandler(CreateMonitoredItemsAsync)));
            #elif (!OPCUA_EXCLUDE_CreateMonitoredItems)
            SupportedServices.Add(DataTypeIds.CreateMonitoredItemsRequest, new ServiceDefinition(typeof(CreateMonitoredItemsRequest), new InvokeServiceEventHandler(CreateMonitoredItems)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ModifyMonitoredItems && !OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.ModifyMonitoredItemsRequest, new ServiceDefinition(typeof(ModifyMonitoredItemsRequest), new InvokeServiceAsyncEventHandler(ModifyMonitoredItemsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_ModifyMonitoredItems && !OPCUA_EXCLUDE_ModifyMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.ModifyMonitoredItemsRequest, new ServiceDefinition(typeof(ModifyMonitoredItemsRequest), new InvokeServiceEventHandler(ModifyMonitoredItems), new InvokeServiceAsyncEventHandler(ModifyMonitoredItemsAsync)));
            #elif (!OPCUA_EXCLUDE_ModifyMonitoredItems)
            SupportedServices.Add(DataTypeIds.ModifyMonitoredItemsRequest, new ServiceDefinition(typeof(ModifyMonitoredItemsRequest), new InvokeServiceEventHandler(ModifyMonitoredItems)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetMonitoringMode && !OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
            SupportedServices.Add(DataTypeIds.SetMonitoringModeRequest, new ServiceDefinition(typeof(SetMonitoringModeRequest), new InvokeServiceAsyncEventHandler(SetMonitoringModeAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_SetMonitoringMode && !OPCUA_EXCLUDE_SetMonitoringMode_ASYNC)
            SupportedServices.Add(DataTypeIds.SetMonitoringModeRequest, new ServiceDefinition(typeof(SetMonitoringModeRequest), new InvokeServiceEventHandler(SetMonitoringMode), new InvokeServiceAsyncEventHandler(SetMonitoringModeAsync)));
            #elif (!OPCUA_EXCLUDE_SetMonitoringMode)
            SupportedServices.Add(DataTypeIds.SetMonitoringModeRequest, new ServiceDefinition(typeof(SetMonitoringModeRequest), new InvokeServiceEventHandler(SetMonitoringMode)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetTriggering && !OPCUA_EXCLUDE_SetTriggering_ASYNC)
            SupportedServices.Add(DataTypeIds.SetTriggeringRequest, new ServiceDefinition(typeof(SetTriggeringRequest), new InvokeServiceAsyncEventHandler(SetTriggeringAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_SetTriggering && !OPCUA_EXCLUDE_SetTriggering_ASYNC)
            SupportedServices.Add(DataTypeIds.SetTriggeringRequest, new ServiceDefinition(typeof(SetTriggeringRequest), new InvokeServiceEventHandler(SetTriggering), new InvokeServiceAsyncEventHandler(SetTriggeringAsync)));
            #elif (!OPCUA_EXCLUDE_SetTriggering)
            SupportedServices.Add(DataTypeIds.SetTriggeringRequest, new ServiceDefinition(typeof(SetTriggeringRequest), new InvokeServiceEventHandler(SetTriggering)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteMonitoredItems && !OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteMonitoredItemsRequest, new ServiceDefinition(typeof(DeleteMonitoredItemsRequest), new InvokeServiceAsyncEventHandler(DeleteMonitoredItemsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_DeleteMonitoredItems && !OPCUA_EXCLUDE_DeleteMonitoredItems_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteMonitoredItemsRequest, new ServiceDefinition(typeof(DeleteMonitoredItemsRequest), new InvokeServiceEventHandler(DeleteMonitoredItems), new InvokeServiceAsyncEventHandler(DeleteMonitoredItemsAsync)));
            #elif (!OPCUA_EXCLUDE_DeleteMonitoredItems)
            SupportedServices.Add(DataTypeIds.DeleteMonitoredItemsRequest, new ServiceDefinition(typeof(DeleteMonitoredItemsRequest), new InvokeServiceEventHandler(DeleteMonitoredItems)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_CreateSubscription && !OPCUA_EXCLUDE_CreateSubscription_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateSubscriptionRequest, new ServiceDefinition(typeof(CreateSubscriptionRequest), new InvokeServiceAsyncEventHandler(CreateSubscriptionAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_CreateSubscription && !OPCUA_EXCLUDE_CreateSubscription_ASYNC)
            SupportedServices.Add(DataTypeIds.CreateSubscriptionRequest, new ServiceDefinition(typeof(CreateSubscriptionRequest), new InvokeServiceEventHandler(CreateSubscription), new InvokeServiceAsyncEventHandler(CreateSubscriptionAsync)));
            #elif (!OPCUA_EXCLUDE_CreateSubscription)
            SupportedServices.Add(DataTypeIds.CreateSubscriptionRequest, new ServiceDefinition(typeof(CreateSubscriptionRequest), new InvokeServiceEventHandler(CreateSubscription)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_ModifySubscription && !OPCUA_EXCLUDE_ModifySubscription_ASYNC)
            SupportedServices.Add(DataTypeIds.ModifySubscriptionRequest, new ServiceDefinition(typeof(ModifySubscriptionRequest), new InvokeServiceAsyncEventHandler(ModifySubscriptionAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_ModifySubscription && !OPCUA_EXCLUDE_ModifySubscription_ASYNC)
            SupportedServices.Add(DataTypeIds.ModifySubscriptionRequest, new ServiceDefinition(typeof(ModifySubscriptionRequest), new InvokeServiceEventHandler(ModifySubscription), new InvokeServiceAsyncEventHandler(ModifySubscriptionAsync)));
            #elif (!OPCUA_EXCLUDE_ModifySubscription)
            SupportedServices.Add(DataTypeIds.ModifySubscriptionRequest, new ServiceDefinition(typeof(ModifySubscriptionRequest), new InvokeServiceEventHandler(ModifySubscription)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_SetPublishingMode && !OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
            SupportedServices.Add(DataTypeIds.SetPublishingModeRequest, new ServiceDefinition(typeof(SetPublishingModeRequest), new InvokeServiceAsyncEventHandler(SetPublishingModeAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_SetPublishingMode && !OPCUA_EXCLUDE_SetPublishingMode_ASYNC)
            SupportedServices.Add(DataTypeIds.SetPublishingModeRequest, new ServiceDefinition(typeof(SetPublishingModeRequest), new InvokeServiceEventHandler(SetPublishingMode), new InvokeServiceAsyncEventHandler(SetPublishingModeAsync)));
            #elif (!OPCUA_EXCLUDE_SetPublishingMode)
            SupportedServices.Add(DataTypeIds.SetPublishingModeRequest, new ServiceDefinition(typeof(SetPublishingModeRequest), new InvokeServiceEventHandler(SetPublishingMode)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Publish && !OPCUA_EXCLUDE_Publish_ASYNC)
            SupportedServices.Add(DataTypeIds.PublishRequest, new ServiceDefinition(typeof(PublishRequest), new InvokeServiceAsyncEventHandler(PublishAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Publish && !OPCUA_EXCLUDE_Publish_ASYNC)
            SupportedServices.Add(DataTypeIds.PublishRequest, new ServiceDefinition(typeof(PublishRequest), new InvokeServiceEventHandler(Publish), new InvokeServiceAsyncEventHandler(PublishAsync)));
            #elif (!OPCUA_EXCLUDE_Publish)
            SupportedServices.Add(DataTypeIds.PublishRequest, new ServiceDefinition(typeof(PublishRequest), new InvokeServiceEventHandler(Publish)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_Republish && !OPCUA_EXCLUDE_Republish_ASYNC)
            SupportedServices.Add(DataTypeIds.RepublishRequest, new ServiceDefinition(typeof(RepublishRequest), new InvokeServiceAsyncEventHandler(RepublishAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_Republish && !OPCUA_EXCLUDE_Republish_ASYNC)
            SupportedServices.Add(DataTypeIds.RepublishRequest, new ServiceDefinition(typeof(RepublishRequest), new InvokeServiceEventHandler(Republish), new InvokeServiceAsyncEventHandler(RepublishAsync)));
            #elif (!OPCUA_EXCLUDE_Republish)
            SupportedServices.Add(DataTypeIds.RepublishRequest, new ServiceDefinition(typeof(RepublishRequest), new InvokeServiceEventHandler(Republish)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_TransferSubscriptions && !OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
            SupportedServices.Add(DataTypeIds.TransferSubscriptionsRequest, new ServiceDefinition(typeof(TransferSubscriptionsRequest), new InvokeServiceAsyncEventHandler(TransferSubscriptionsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_TransferSubscriptions && !OPCUA_EXCLUDE_TransferSubscriptions_ASYNC)
            SupportedServices.Add(DataTypeIds.TransferSubscriptionsRequest, new ServiceDefinition(typeof(TransferSubscriptionsRequest), new InvokeServiceEventHandler(TransferSubscriptions), new InvokeServiceAsyncEventHandler(TransferSubscriptionsAsync)));
            #elif (!OPCUA_EXCLUDE_TransferSubscriptions)
            SupportedServices.Add(DataTypeIds.TransferSubscriptionsRequest, new ServiceDefinition(typeof(TransferSubscriptionsRequest), new InvokeServiceEventHandler(TransferSubscriptions)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_DeleteSubscriptions && !OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteSubscriptionsRequest, new ServiceDefinition(typeof(DeleteSubscriptionsRequest), new InvokeServiceAsyncEventHandler(DeleteSubscriptionsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_DeleteSubscriptions && !OPCUA_EXCLUDE_DeleteSubscriptions_ASYNC)
            SupportedServices.Add(DataTypeIds.DeleteSubscriptionsRequest, new ServiceDefinition(typeof(DeleteSubscriptionsRequest), new InvokeServiceEventHandler(DeleteSubscriptions), new InvokeServiceAsyncEventHandler(DeleteSubscriptionsAsync)));
            #elif (!OPCUA_EXCLUDE_DeleteSubscriptions)
            SupportedServices.Add(DataTypeIds.DeleteSubscriptionsRequest, new ServiceDefinition(typeof(DeleteSubscriptionsRequest), new InvokeServiceEventHandler(DeleteSubscriptions)));
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
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute()]
    #if (!NET_STANDARD)
    [ServiceMessageContextBehavior()]
    [ServiceBehavior(Namespace = Namespaces.OpcUaWsdl, InstanceContextMode=InstanceContextMode.PerSession, ConcurrencyMode=ConcurrencyMode.Multiple)]
    #endif
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
        #region FindServers Service
        #if (!OPCUA_EXCLUDE_FindServers)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServers_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
        public IServiceResponse FindServers(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            FindServersResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersRequest request = (FindServersRequest)incoming;

                ApplicationDescriptionCollection servers = null;

                response = new FindServersResponse();

                response.ResponseHeader = ServerInstance.FindServers(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ServerUris,
                   out servers);

                response.Servers = servers;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
        public virtual FindServersResponseMessage FindServers(FindServersMessage request)
        {
            FindServersResponse response = null;

            try
            {
                // OnRequestReceived(message.FindServersRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (FindServersResponse)FindServers(request.FindServersRequest);

                // OnResponseSent(response);
                return new FindServersResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.FindServersRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServers service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
            public virtual IAsyncResult BeginFindServers(FindServersMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.FindServersRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.FindServersRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServers service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersAsync instead.")]
        #endif
            public virtual FindServersResponseMessage EndFindServers(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new FindServersResponseMessage((FindServersResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServers_ASYNC)
        /// <summary>
        /// Invokes the FindServers service.
        /// </summary>
        public async Task<IServiceResponse> FindServersAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            FindServersResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersRequest request = (FindServersRequest)incoming;

                response = await ServerInstance.FindServersAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ServerUris,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region FindServersOnNetwork Service
        #if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
        public IServiceResponse FindServersOnNetwork(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersOnNetworkRequest request = (FindServersOnNetworkRequest)incoming;

                DateTime lastCounterResetTime = DateTime.MinValue;
                ServerOnNetworkCollection servers = null;

                response = new FindServersOnNetworkResponse();

                response.ResponseHeader = ServerInstance.FindServersOnNetwork(
                   secureChannelContext,
                   request.RequestHeader,
                   request.StartingRecordId,
                   request.MaxRecordsToReturn,
                   request.ServerCapabilityFilter,
                   out lastCounterResetTime,
                   out servers);

                response.LastCounterResetTime = lastCounterResetTime;
                response.Servers              = servers;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
        public virtual FindServersOnNetworkResponseMessage FindServersOnNetwork(FindServersOnNetworkMessage request)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                // OnRequestReceived(message.FindServersOnNetworkRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (FindServersOnNetworkResponse)FindServersOnNetwork(request.FindServersOnNetworkRequest);

                // OnResponseSent(response);
                return new FindServersOnNetworkResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.FindServersOnNetworkRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the FindServersOnNetwork service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
            public virtual IAsyncResult BeginFindServersOnNetwork(FindServersOnNetworkMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.FindServersOnNetworkRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.FindServersOnNetworkRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.FindServersOnNetworkRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the FindServersOnNetwork service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use FindServersOnNetworkAsync instead.")]
        #endif
            public virtual FindServersOnNetworkResponseMessage EndFindServersOnNetwork(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new FindServersOnNetworkResponseMessage((FindServersOnNetworkResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
        /// <summary>
        /// Invokes the FindServersOnNetwork service.
        /// </summary>
        public async Task<IServiceResponse> FindServersOnNetworkAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            FindServersOnNetworkResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                FindServersOnNetworkRequest request = (FindServersOnNetworkRequest)incoming;

                response = await ServerInstance.FindServersOnNetworkAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.StartingRecordId,
                   request.MaxRecordsToReturn,
                   request.ServerCapabilityFilter,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region GetEndpoints Service
        #if (!OPCUA_EXCLUDE_GetEndpoints)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
        public IServiceResponse GetEndpoints(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            GetEndpointsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                GetEndpointsRequest request = (GetEndpointsRequest)incoming;

                EndpointDescriptionCollection endpoints = null;

                response = new GetEndpointsResponse();

                response.ResponseHeader = ServerInstance.GetEndpoints(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ProfileUris,
                   out endpoints);

                response.Endpoints = endpoints;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
        public virtual GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request)
        {
            GetEndpointsResponse response = null;

            try
            {
                // OnRequestReceived(message.GetEndpointsRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (GetEndpointsResponse)GetEndpoints(request.GetEndpointsRequest);

                // OnResponseSent(response);
                return new GetEndpointsResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.GetEndpointsRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the GetEndpoints service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
            public virtual IAsyncResult BeginGetEndpoints(GetEndpointsMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.GetEndpointsRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.GetEndpointsRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.GetEndpointsRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the GetEndpoints service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use GetEndpointsAsync instead.")]
        #endif
            public virtual GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new GetEndpointsResponseMessage((GetEndpointsResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_GetEndpoints_ASYNC)
        /// <summary>
        /// Invokes the GetEndpoints service.
        /// </summary>
        public async Task<IServiceResponse> GetEndpointsAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            GetEndpointsResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                GetEndpointsRequest request = (GetEndpointsRequest)incoming;

                response = await ServerInstance.GetEndpointsAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.EndpointUrl,
                   request.LocaleIds,
                   request.ProfileUris,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region RegisterServer Service
        #if (!OPCUA_EXCLUDE_RegisterServer)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the RegisterServer service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterServer_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use RegisterServerAsync instead.")]
        #endif
        public IServiceResponse RegisterServer(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            RegisterServerResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterServerRequest request = (RegisterServerRequest)incoming;


                response = new RegisterServerResponse();

                response.ResponseHeader = ServerInstance.RegisterServer(
                   secureChannelContext,
                   request.RequestHeader,
                   request.Server);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
        public virtual RegisterServerResponseMessage RegisterServer(RegisterServerMessage request)
        {
            RegisterServerResponse response = null;

            try
            {
                // OnRequestReceived(message.RegisterServerRequest);

                SetRequestContext(RequestEncoding.Xml);
                response = (RegisterServerResponse)RegisterServer(request.RegisterServerRequest);

                // OnResponseSent(response);
                return new RegisterServerResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.RegisterServerRequest, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the RegisterServer service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterServerAsync instead.")]
        #endif
            public virtual IAsyncResult BeginRegisterServer(RegisterServerMessage message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.RegisterServerRequest);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RegisterServerRequest);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.RegisterServerRequest, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the RegisterServer service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterServerAsync instead.")]
        #endif
            public virtual RegisterServerResponseMessage EndRegisterServer(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new RegisterServerResponseMessage((RegisterServerResponse)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer_ASYNC)
        /// <summary>
        /// Invokes the RegisterServer service.
        /// </summary>
        public async Task<IServiceResponse> RegisterServerAsync(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            RegisterServerResponse response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterServerRequest request = (RegisterServerRequest)incoming;

                response = await ServerInstance.RegisterServerAsync(
                   secureChannelContext,
                   request.RequestHeader,
                   request.Server,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }
        #endif
        #endif
        #endregion

        #region RegisterServer2 Service
        #if (!OPCUA_EXCLUDE_RegisterServer2)
        #if (!NET_STANDARD_NO_SYNC && !NET_STANDARD_NO_APM)
        /// <summary>
        /// Invokes the RegisterServer2 service.
        /// </summary>
        #if (NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterServer2_ASYNC)
        [Obsolete("Sync methods are deprecated in this version. Use RegisterServer2Async instead.")]
        #endif
        public IServiceResponse RegisterServer2(IServiceRequest incoming, SecureChannelContext secureChannelContext)
        {
            RegisterServer2Response response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterServer2Request request = (RegisterServer2Request)incoming;

                StatusCodeCollection configurationResults = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                response = new RegisterServer2Response();

                response.ResponseHeader = ServerInstance.RegisterServer2(
                   secureChannelContext,
                   request.RequestHeader,
                   request.Server,
                   request.DiscoveryConfiguration,
                   out configurationResults,
                   out diagnosticInfos);

                response.ConfigurationResults = configurationResults;
                response.DiagnosticInfos      = diagnosticInfos;
            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
        }

        #if (OPCUA_USE_SYNCHRONOUS_ENDPOINTS)
        /// <summary>
        /// The operation contract for the RegisterServer2 service.
        /// </summary>
        public virtual RegisterServer2ResponseMessage RegisterServer2(RegisterServer2Message request)
        {
            RegisterServer2Response response = null;

            try
            {
                // OnRequestReceived(message.RegisterServer2Request);

                SetRequestContext(RequestEncoding.Xml);
                response = (RegisterServer2Response)RegisterServer2(request.RegisterServer2Request);

                // OnResponseSent(response);
                return new RegisterServer2ResponseMessage(response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(request.RegisterServer2Request, e);
                // OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #else
        /// <summary>
        /// Asynchronously calls the RegisterServer2 service.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterServer2Async instead.")]
        #endif
            public virtual IAsyncResult BeginRegisterServer2(RegisterServer2Message message, AsyncCallback callback, object callbackData)
        {
            try
            {
                // check for bad data.
                if (message == null) throw new ArgumentNullException(nameof(message));

                OnRequestReceived(message.RegisterServer2Request);

                // set the request context.
                SetRequestContext(RequestEncoding.Xml);

                // create handler.
                ProcessRequestAsyncResult result = new ProcessRequestAsyncResult(this, callback, callbackData, 0);
                return result.BeginProcessRequest(SecureChannelContext.Current, message.RegisterServer2Request);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(message.RegisterServer2Request, e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }

        /// <summary>
        /// Waits for an asynchronous call to the RegisterServer2 service to complete.
        /// </summary>
        #if NET_STANDARD_OBSOLETE_APM
        [Obsolete("Begin/End methods are deprecated in this version. Use RegisterServer2Async instead.")]
        #endif
            public virtual RegisterServer2ResponseMessage EndRegisterServer2(IAsyncResult ar)
        {
            try
            {
                IServiceResponse response = ProcessRequestAsyncResult.WaitForComplete(ar, true);
                OnResponseSent(response);
                return new RegisterServer2ResponseMessage((RegisterServer2Response)response);
            }
            catch (Exception e)
            {
                Exception fault = CreateSoapFault(ProcessRequestAsyncResult.GetRequest(ar), e);
                OnResponseFaultSent(fault);
                throw fault;
            }
        }
        #endif
        #endif

        #if (!OPCUA_EXCLUDE_RegisterServer2_ASYNC)
        /// <summary>
        /// Invokes the RegisterServer2 service.
        /// </summary>
        public async Task<IServiceResponse> RegisterServer2Async(IServiceRequest incoming, SecureChannelContext secureChannelContext, CancellationToken cancellationToken = default)
        {
            RegisterServer2Response response = null;

            try
            {
                OnRequestReceived(incoming);

                RegisterServer2Request request = (RegisterServer2Request)incoming;

                response = await ServerInstance.RegisterServer2Async(
                   secureChannelContext,
                   request.RequestHeader,
                   request.Server,
                   request.DiscoveryConfiguration,cancellationToken).ConfigureAwait(false);

            }
            finally
            {
                OnResponseSent(response);
            }

            return response;
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
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServers && !OPCUA_EXCLUDE_FindServers_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceAsyncEventHandler(FindServersAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_FindServers && !OPCUA_EXCLUDE_FindServers_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers), new InvokeServiceAsyncEventHandler(FindServersAsync)));
            #elif (!OPCUA_EXCLUDE_FindServers)
            SupportedServices.Add(DataTypeIds.FindServersRequest, new ServiceDefinition(typeof(FindServersRequest), new InvokeServiceEventHandler(FindServers)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_FindServersOnNetwork && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceAsyncEventHandler(FindServersOnNetworkAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_FindServersOnNetwork && !OPCUA_EXCLUDE_FindServersOnNetwork_ASYNC)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceEventHandler(FindServersOnNetwork), new InvokeServiceAsyncEventHandler(FindServersOnNetworkAsync)));
            #elif (!OPCUA_EXCLUDE_FindServersOnNetwork)
            SupportedServices.Add(DataTypeIds.FindServersOnNetworkRequest, new ServiceDefinition(typeof(FindServersOnNetworkRequest), new InvokeServiceEventHandler(FindServersOnNetwork)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_GetEndpoints && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceAsyncEventHandler(GetEndpointsAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_GetEndpoints && !OPCUA_EXCLUDE_GetEndpoints_ASYNC)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints), new InvokeServiceAsyncEventHandler(GetEndpointsAsync)));
            #elif (!OPCUA_EXCLUDE_GetEndpoints)
            SupportedServices.Add(DataTypeIds.GetEndpointsRequest, new ServiceDefinition(typeof(GetEndpointsRequest), new InvokeServiceEventHandler(GetEndpoints)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterServer && !OPCUA_EXCLUDE_RegisterServer_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterServerRequest, new ServiceDefinition(typeof(RegisterServerRequest), new InvokeServiceAsyncEventHandler(RegisterServerAsync)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_RegisterServer && !OPCUA_EXCLUDE_RegisterServer_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterServerRequest, new ServiceDefinition(typeof(RegisterServerRequest), new InvokeServiceEventHandler(RegisterServer), new InvokeServiceAsyncEventHandler(RegisterServerAsync)));
            #elif (!OPCUA_EXCLUDE_RegisterServer)
            SupportedServices.Add(DataTypeIds.RegisterServerRequest, new ServiceDefinition(typeof(RegisterServerRequest), new InvokeServiceEventHandler(RegisterServer)));
            #endif
            #if (OPCUA_INCLUDE_ASYNC && NET_STANDARD_OBSOLETE_SYNC && !OPCUA_EXCLUDE_RegisterServer2 && !OPCUA_EXCLUDE_RegisterServer2_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterServer2Request, new ServiceDefinition(typeof(RegisterServer2Request), new InvokeServiceAsyncEventHandler(RegisterServer2Async)));
            #elif (OPCUA_INCLUDE_ASYNC && !OPCUA_EXCLUDE_RegisterServer2 && !OPCUA_EXCLUDE_RegisterServer2_ASYNC)
            SupportedServices.Add(DataTypeIds.RegisterServer2Request, new ServiceDefinition(typeof(RegisterServer2Request), new InvokeServiceEventHandler(RegisterServer2), new InvokeServiceAsyncEventHandler(RegisterServer2Async)));
            #elif (!OPCUA_EXCLUDE_RegisterServer2)
            SupportedServices.Add(DataTypeIds.RegisterServer2Request, new ServiceDefinition(typeof(RegisterServer2Request), new InvokeServiceEventHandler(RegisterServer2)));
            #endif
        }
        #endregion
    }
    #endregion
}