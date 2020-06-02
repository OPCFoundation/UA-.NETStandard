/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel;
using Opc.Ua.Bindings;

namespace Opc.Ua
{
    /// <summary>
    /// A base class for UA channel objects used to access UA interfaces
    /// </summary>
    public abstract class UaChannelBase : IChannelBase, ITransportChannel
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with the specified binding and endpoint address.
        /// </summary>
        public UaChannelBase()
        {
            m_messageContext = null;
            m_settings = null;
            m_uaBypassChannel = null;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to do.
        }
        #endregion

        #region IChannelBase Members
        /// <summary>
        /// Returns true if the channel uses the UA Binary encoding.
        /// </summary>
        public bool UseBinaryEncoding
        {
            get
            {
                if (m_settings != null && m_settings.Configuration != null)
                {
                    return m_settings.Configuration.UseBinaryEncoding;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the binary encoding support.
        /// </summary>
        public BinaryEncodingSupport BinaryEncodingSupport
        {
            get
            {
                if (m_settings != null && m_settings.Configuration != null)
                {
                    if (m_settings != null && m_settings.Configuration.UseBinaryEncoding)
                    {
                        return BinaryEncodingSupport.Required;
                    }

                    return BinaryEncodingSupport.None;
                }

                return BinaryEncodingSupport.Optional;
            }
        }

        /// <summary>
        /// Opens the channel with the server.
        /// </summary>
        public void OpenChannel()
        {
            ICommunicationObject channel = m_channel as ICommunicationObject;

            if (channel != null && channel.State == CommunicationState.Closed)
            {
                channel.Open();
            }
        }

        /// <summary>
        /// Closes the channel with the server.
        /// </summary>
        public void CloseChannel()
        {
            ICommunicationObject channel = m_channel as ICommunicationObject;

            if (channel != null && channel.State == CommunicationState.Opened)
            {
                channel.Abort();
            }
        }

        /// <summary>
        /// Schedules an outgoing request.
        /// </summary>
        /// <param name="request">The request.</param>
        public void ScheduleOutgoingRequest(IChannelOutgoingRequest request)
        {
#if MANAGE_CHANNEL_THREADS
            System.Threading.Thread thread = new System.Threading.Thread(OnSendRequest);
            thread.Start(request);
#else
            throw new NotImplementedException();
#endif
        }
        #endregion

        #region ITransportChannel Members
        /// <summary>
        /// A masking indicating which features are implemented.
        /// </summary>
        public TransportChannelFeatures SupportedFeatures
        {
            get
            {
                if (m_uaBypassChannel != null)
                {
                    return m_uaBypassChannel.SupportedFeatures;
                }

                return TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest | TransportChannelFeatures.BeginClose;
            }
        }

        /// <summary>
        /// Gets the description for the endpoint used by the channel.
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get
            {
                if (m_uaBypassChannel != null)
                {
                    return m_uaBypassChannel.EndpointDescription;
                }

                if (m_settings != null)
                {
                    return m_settings.Description;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the configuration for the channel.
        /// </summary>
        public EndpointConfiguration EndpointConfiguration
        {
            get
            {
                if (m_uaBypassChannel != null)
                {
                    return m_uaBypassChannel.EndpointConfiguration;
                }

                if (m_settings != null)
                {
                    return m_settings.Configuration;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the context used when serializing messages exchanged via the channel.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get
            {
                if (m_uaBypassChannel != null)
                {
                    return m_uaBypassChannel.MessageContext;
                }

                return m_messageContext;
            }
        }

        /// <summary>
        ///  Gets the the channel's current security token.
        /// </summary>
        public ChannelToken CurrentToken => null;

        /// <summary>
        /// Gets or sets the default timeout for requests send via the channel.
        /// </summary>
        public int OperationTimeout
        {
            get
            {
                if (m_uaBypassChannel != null)
                {
                    return m_uaBypassChannel.OperationTimeout;
                }

                return m_operationTimeout;
            }

            set
            {
                if (m_uaBypassChannel != null)
                {
                    m_uaBypassChannel.OperationTimeout = value;
                    return;
                }

                m_operationTimeout = value;
            }
        }

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="url">The URL for the endpoint.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Initialize(
            Uri url,
            TransportChannelSettings settings)
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.Initialize(url, settings);
                return;
            }

            throw new NotSupportedException("WCF channels must be configured when they are constructed.");
        }

        /// <summary>
        /// Initializes a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="connection">The connection to use.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Initialize(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings)
        {
            throw new NotSupportedException("WCF channels must be configured when they are constructed.");
        }

        /// <summary>
        /// Opens a secure channel with the endpoint identified by the URL.
        /// </summary>
        public void Open()
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.Open();
                return;
            }
        }

        /// <summary>
        /// Begins an asynchronous operation to open a secure channel with the endpoint identified by the URL.
        /// </summary>
        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.BeginOpen(callback, callbackData);
            }

            throw new NotSupportedException("WCF channels must be configured when they are constructed.");
        }

        /// <summary>
        /// Completes an asynchronous operation to open a communication object.
        /// </summary>
        public void EndOpen(IAsyncResult result)
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.EndOpen(result);
                return;
            }

            throw new NotSupportedException("WCF channels must be configured when they are constructed.");
        }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        public abstract void Reconnect();

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.BeginReconnect(callback, callbackData);
            }

            throw new NotSupportedException("WCF channels cannot be reconnected.");
        }

        /// <summary>
        /// Completes an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        public void EndReconnect(IAsyncResult result)
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.EndReconnect(result);
                return;
            }

            throw new NotSupportedException("WCF channels cannot be reconnected.");
        }

        /// <summary>
        /// Closes any existing secure channel.
        /// </summary>
        public void Close()
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.Close();
                return;
            }

            CloseChannel();
        }

        /// <summary>
        /// Begins an asynchronous operation to close the secure channel.
        /// </summary>
        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.BeginClose(callback, callbackData);
            }

            AsyncResultBase result = new AsyncResultBase(callback, callbackData, 0);
            result.OperationCompleted();
            return result;
        }

        /// <summary>
        /// Completes an asynchronous operation to close a communication object.
        /// </summary>
        public void EndClose(IAsyncResult result)
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.EndClose(result);
                return;
            }

            AsyncResultBase.WaitForComplete(result);
            CloseChannel();
        }

        /// <summary>
        /// Sends a request over the secure channel.
        /// </summary>
        public IServiceResponse SendRequest(IServiceRequest request)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.SendRequest(request);
            }

            byte[] requestMessage = BinaryEncoder.EncodeMessage(request, m_messageContext);
            InvokeServiceResponseMessage responseMessage = InvokeService(new InvokeServiceMessage(requestMessage));
            return (IServiceResponse)BinaryDecoder.DecodeMessage(responseMessage.InvokeServiceResponse, null, m_messageContext);
        }

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.BeginSendRequest(request, callback, callbackData);
            }

#if MANAGE_CHANNEL_THREADS
            SendRequestAsyncResult asyncResult = new SendRequestAsyncResult(this, callback, callbackData, 0);
            asyncResult.BeginSendRequest(SendRequest, request);
            return asyncResult;
#else
            byte[] requestMessage = BinaryEncoder.EncodeMessage(request, m_messageContext);
            return BeginInvokeService(new InvokeServiceMessage(requestMessage), callback, callbackData);
#endif
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            if (m_uaBypassChannel != null)
            {
                return m_uaBypassChannel.EndSendRequest(result);
            }

#if MANAGE_CHANNEL_THREADS
            return SendRequestAsyncResult.WaitForComplete(result);
#else
            InvokeServiceResponseMessage responseMessage = EndInvokeService(result);
            return (IServiceResponse)BinaryDecoder.DecodeMessage(responseMessage.InvokeServiceResponse, null, m_messageContext);
#endif
        }

        /// <summary>
        /// The client side implementation of the InvokeService service contract.
        /// </summary>
        public abstract InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request);

        /// <summary>
        /// The client side implementation of the BeginInvokeService service contract.
        /// </summary>
        public abstract IAsyncResult BeginInvokeService(InvokeServiceMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The client side implementation of the EndInvokeService service contract.
        /// </summary>
        public abstract InvokeServiceResponseMessage EndInvokeService(IAsyncResult result);
        #endregion

#if MANAGE_CHANNEL_THREADS
        #region SendRequestAsyncResult Class
        /// <summary>
        /// An AsyncResult object when handling an asynchronous request.
        /// </summary>
        protected class SendRequestAsyncResult : AsyncResultBase, IChannelOutgoingRequest
        {
        #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="SendRequestAsyncResult"/> class.
            /// </summary>
            /// <param name="channel">The channel being used.</param>
            /// <param name="callback">The callback to use when the operation completes.</param>
            /// <param name="callbackData">The callback data.</param>
            /// <param name="timeout">The timeout in milliseconds</param>
            public SendRequestAsyncResult(
                IChannelBase channel,
                AsyncCallback callback,
                object callbackData,
                int timeout)
            :
                base(callback, callbackData, timeout)
            {
                m_channel = channel;
            }
        #endregion

        #region IChannelOutgoingRequest Members
            /// <summary>
            /// Gets the request.
            /// </summary>
            /// <value>The request.</value>
            public IServiceRequest Request
            {
                get { return m_request; }
            }

            /// <summary>
            /// Gets the handler used to send the request.
            /// </summary>
            /// <value>The send request handler.</value>
            public ChannelSendRequestEventHandler Handler
            {
                get { return m_handler; }
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
                OnSendRequest(null);
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
                    m_response = null;
                }

                // operation completed.
                OperationCompleted();
            }
        #endregion

        #region Public Members
            /// <summary>
            /// Begins processing an incoming request.
            /// </summary>
            /// <param name="handler">The method which sends the request.</param>
            /// <param name="request">The request.</param>
            /// <returns>The result object that is used to call the EndSendRequest method.</returns>
            public IAsyncResult BeginSendRequest(
                ChannelSendRequestEventHandler handler,
                IServiceRequest request)
            {
                m_handler = handler;
                m_request = request;

                try
                {
                    // queue request.
                    m_channel.ScheduleOutgoingRequest(this);
                }
                catch (Exception e)
                {
                    m_error = e;
                    m_response = null;

                    // operation completed.
                    OperationCompleted();
                }

                return this;
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The response.</returns>
            public static new IServiceResponse WaitForComplete(IAsyncResult ar)
            {
                SendRequestAsyncResult result = ar as SendRequestAsyncResult;

                if (result == null)
                {
                    throw new ArgumentException("End called with an invalid IAsyncResult object.", "ar");
                }

                if (result.m_response == null && result.m_error == null)
                {
                    if (!result.WaitForComplete())
                    {
                        throw new TimeoutException();
                    }
                }

                if (result.m_error != null)
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
                SendRequestAsyncResult result = ar as SendRequestAsyncResult;

                if (result != null)
                {
                    return result.m_request;
                }

                return null;
            }
        #endregion

        #region Private Members
            /// <summary>
            /// Processes the request.
            /// </summary>
            private void OnSendRequest(object state)
            {
                try
                {
                    // call the service.
                    m_response = m_handler(m_request);
                }
                catch (Exception e)
                {
                    // save any error.
                    m_error = e;
                    m_response = null;
                }

                // report completion.
                OperationCompleted();
            }
        #endregion

        #region Private Fields
            private IChannelBase m_channel;
            private ChannelSendRequestEventHandler m_handler;
            private IServiceRequest m_request;
            private IServiceResponse m_response;
            private Exception m_error;
        #endregion
        }
        #endregion
        
        /// <summary>
        /// Processes the request.
        /// </summary>
        /// <param name="state">IChannelOutgoingRequest object passed to the ScheduleOutgoingRequest method.</param>
        protected virtual void OnSendRequest(object state)
        {
            try
            {
                IChannelOutgoingRequest request = (IChannelOutgoingRequest)state;
                request.CallSynchronously();
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error sending outgoing request.");
            }
        }
#endif

        #region Protected Methods
        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            ITransportWaitingConnection connection,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            ServiceMessageContext messageContext)
        {
            bool useUaTcp = description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp);
            bool useHttps = description.EndpointUrl.StartsWith(Utils.UriSchemeHttps);

            // initialize the channel which will be created with the server.
            ITransportChannel channel = null;

            if (useUaTcp)
            {
                channel = new TcpTransportChannel();
            }
#if !NO_HTTPS
            else if (useHttps)
            {
                channel = new HttpsTransportChannel();
            }
#endif

            if (channel == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile\r\n");
            }

            // create a UA channel.
            var settings = new TransportChannelSettings {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            if (description.ServerCertificate != null && description.ServerCertificate.Length > 0)
            {
                settings.ServerCertificate = Utils.ParseCertificateBlob(description.ServerCertificate);
            }

            if (configuration != null)
            {
                settings.CertificateValidator = configuration.CertificateValidator.GetChannelValidator();
            }

            settings.NamespaceUris = messageContext.NamespaceUris;
            settings.Factory = messageContext.Factory;

            channel.Initialize(connection, settings);
            channel.Open();

            return channel;
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            ServiceMessageContext messageContext)
        {
            return CreateUaBinaryChannel(configuration, description, endpointConfiguration, clientCertificate, null, messageContext);
        }

        /// <summary>
        /// Creates a new UA-binary transport channel if requested. Null otherwise.
        /// </summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="description">The description for the endpoint.</param>
        /// <param name="endpointConfiguration">The configuration to use with the endpoint.</param>
        /// <param name="clientCertificate">The client certificate.</param>
        /// <param name="clientCertificateChain">The client certificate chain.</param>
        /// <param name="messageContext">The message context to use when serializing the messages.</param>
        /// <returns></returns>
        public static ITransportChannel CreateUaBinaryChannel(
            ApplicationConfiguration configuration,
            EndpointDescription description,
            EndpointConfiguration endpointConfiguration,
            X509Certificate2 clientCertificate,
            X509Certificate2Collection clientCertificateChain,
            ServiceMessageContext messageContext)
        {
            bool useUaTcp = description.EndpointUrl.StartsWith(Utils.UriSchemeOpcTcp);
            bool useHttps = description.EndpointUrl.StartsWith(Utils.UriSchemeHttps);

            switch (description.TransportProfileUri)
            {
                case Profiles.UaTcpTransport:
                {
                    useUaTcp = true;
                    break;
                }

                case Profiles.HttpsBinaryTransport:
                {
                    useHttps = true;
                    break;
                }
            }

            // initialize the channel which will be created with the server.
            ITransportChannel channel = null;
            if (useUaTcp)
            {
                channel = new TcpTransportChannel();
            }
#if !NO_HTTPS
            else if (useHttps)
            {
                channel = new HttpsTransportChannel();
            }
#endif

            if (channel == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile\r\n");
            }

            // create a UA-TCP channel.
            TransportChannelSettings settings = new TransportChannelSettings {
                Description = description,
                Configuration = endpointConfiguration,
                ClientCertificate = clientCertificate,
                ClientCertificateChain = clientCertificateChain
            };

            if (description.ServerCertificate != null && description.ServerCertificate.Length > 0)
            {
                settings.ServerCertificate = Utils.ParseCertificateBlob(description.ServerCertificate);
            }

            if (configuration != null)
            {
                settings.CertificateValidator = configuration.CertificateValidator.GetChannelValidator();
            }

            settings.NamespaceUris = messageContext.NamespaceUris;
            settings.Factory = messageContext.Factory;

            channel.Initialize(new Uri(description.EndpointUrl), settings);
            channel.Open();

            return channel;
        }

        /// <summary>
        /// Handles the Opened event of the InnerChannel control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        internal void InnerChannel_Opened(object sender, EventArgs e)
        {
            Uri endpointUrl = this.m_channelFactory.Endpoint.Address.Uri;

            X509Certificate2 clientCertificate = null;
            X509Certificate2 serverCertificate = null;

            Security.Audit.SecureChannelCreated(
                    g_ImplementationString,
                    m_channelFactory.Endpoint.Address.Uri.ToString(),
                    null,
                    EndpointDescription,
                    clientCertificate,
                    serverCertificate,
                    BinaryEncodingSupport.Optional);
        }

        /// <summary>
        /// Converts a FaultException into a ServiceResultException.
        /// </summary>
        public ServiceResultException HandleSoapFault(System.ServiceModel.FaultException<ServiceFault> exception)
        {
            if (exception == null || exception.Detail == null || exception.Detail.ResponseHeader == null)
            {
                return ServiceResultException.Create(StatusCodes.BadUnexpectedError, exception, "SOAP fault did not contain any details.");
            }

            ResponseHeader header = exception.Detail.ResponseHeader;

            return new ServiceResultException(new ServiceResult(
                header.ServiceResult,
                header.ServiceDiagnostics,
                header.StringTable));
        }
        #endregion

        #region Private Fields
        internal TransportChannelSettings m_settings;
        internal ServiceMessageContext m_messageContext;
        internal ITransportChannel m_uaBypassChannel;
        internal int m_operationTimeout;
        internal ChannelFactory m_channelFactory;
        internal IChannelBase m_channel;
        internal string g_ImplementationString = "Opc.Ua.ChannelBase WCF Client " + Utils.GetAssemblySoftwareVersion();
        #endregion
    }

    /// <summary>
    /// A base class for UA channel objects used access UA interfaces
    /// </summary>
    public class WcfChannelBase<TChannel> : UaChannelBase where TChannel : class, IChannelBase
    {
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_channel);
                m_channel = null;

                Utils.SilentDispose(m_channelFactory);
                m_channelFactory = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region IChannelBase Members
        /// <summary>
        /// The client side implementation of the InvokeService service contract.
        /// </summary>
        public override InvokeServiceResponseMessage InvokeService(InvokeServiceMessage request)
        {
            IAsyncResult result = null;

            lock (this.Channel)
            {
                result = this.Channel.BeginInvokeService(request, null, null);
            }

            return this.Channel.EndInvokeService(result);
        }

        /// <summary>
        /// The client side implementation of the BeginInvokeService service contract.
        /// </summary>
        public override IAsyncResult BeginInvokeService(InvokeServiceMessage request, AsyncCallback callback, object asyncState)
        {
            WcfChannelAsyncResult asyncResult = new WcfChannelAsyncResult(m_channel, callback, asyncState);

            lock (asyncResult.Lock)
            {
                asyncResult.InnerResult = asyncResult.Channel.BeginInvokeService(request, asyncResult.OnOperationCompleted, null);
            }

            return asyncResult;
        }

        /// <summary>
        /// The client side implementation of the EndInvokeService service contract.
        /// </summary>
        public override InvokeServiceResponseMessage EndInvokeService(IAsyncResult result)
        {
            WcfChannelAsyncResult asyncResult = WcfChannelAsyncResult.WaitForComplete(result);
            return asyncResult.Channel.EndInvokeService(asyncResult.InnerResult);
        }
        #endregion

        #region ITransportChannel Members
        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        public override void Reconnect()
        {
            if (m_uaBypassChannel != null)
            {
                m_uaBypassChannel.Reconnect();
                return;
            }

            Utils.Trace("RECONNECT: Reconnecting to {0}.", m_settings.Description.EndpointUrl);

            // grap the existing channel.
            TChannel channel = m_channel;
            ChannelFactory<TChannel> channelFactory = m_channelFactory as ChannelFactory<TChannel>;

            // create the new channel.
            base.m_channel = m_channel = channelFactory.CreateChannel();

            ICommunicationObject communicationObject = null;

            if (channel != null)
            {
                try
                {
                    communicationObject = channel as ICommunicationObject;

                    if (communicationObject != null)
                    {
                        communicationObject.Close();
                    }
                }
                catch (Exception)
                {
                    // ignore errors.
                }
            }

            // register callback with new channel.
            communicationObject = m_channel as ICommunicationObject;

            if (communicationObject != null)
            {
                communicationObject.Opened += new EventHandler(InnerChannel_Opened);
            }
        }
        #endregion

        #region WcfChannelAsyncResult Class
        /// <summary>
        /// An async result object that wraps the WCF channel.
        /// </summary>
        protected class WcfChannelAsyncResult : AsyncResultBase
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="WcfChannelAsyncResult"/> class.
            /// </summary>
            /// <param name="channel">The channel.</param>
            /// <param name="callback">The callback.</param>
            /// <param name="callbackData">The callback data.</param>
            public WcfChannelAsyncResult(
                TChannel channel,
                AsyncCallback callback,
                object callbackData)
                :
                    base(callback, callbackData, 0)
            {
                m_channel = channel;
            }

            /// <summary>
            /// Gets the wrapped channel.
            /// </summary>
            /// <value>The wrapped channel.</value>
            public TChannel Channel => m_channel;

            /// <summary>
            /// Called when asynchronous operation completes.
            /// </summary>
            /// <param name="ar">The asynchronous result object.</param>
            public void OnOperationCompleted(IAsyncResult ar)
            {
                try
                {
                    // check if the begin operation has had a chance to complete.
                    lock (Lock)
                    {
                        if (InnerResult == null)
                        {
                            InnerResult = ar;
                        }
                    }

                    // signal that the operation is complete.
                    OperationCompleted();
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected exception invoking WcfChannelAsyncResult callback function.");
                }
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The oject that </returns>
            public static new WcfChannelAsyncResult WaitForComplete(IAsyncResult ar)
            {
                WcfChannelAsyncResult asyncResult = ar as WcfChannelAsyncResult;

                if (asyncResult == null)
                {
                    throw new ArgumentException("End called with an invalid IAsyncResult object.", nameof(ar));
                }

                if (!asyncResult.WaitForComplete())
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }

                return asyncResult;
            }

            private TChannel m_channel;
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Gets the inner channel.
        /// </summary>
        /// <value>The channel.</value>
        protected TChannel Channel => m_channel;
        #endregion

        #region Private Fields
        private new TChannel m_channel;
        #endregion
    }
}
