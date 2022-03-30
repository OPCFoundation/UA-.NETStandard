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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
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
            throw new NotImplementedException("UaBaseChannel does not implement OpenChannel()");
        }

        /// <summary>
        /// Closes the channel with the server.
        /// </summary>
        public void CloseChannel()
        {
            throw new NotImplementedException("UaBaseChannel does not implement CloseChannel()");
        }

        /// <summary>
        /// Schedules an outgoing request.
        /// </summary>
        /// <param name="request">The request.</param>
        public void ScheduleOutgoingRequest(IChannelOutgoingRequest request)
        {
            throw new NotImplementedException("UaBaseChannel does not implement ScheduleOutgoingRequest()");
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

                return TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest |
                    TransportChannelFeatures.BeginClose | TransportChannelFeatures.SendRequestAsync;
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
        public IServiceMessageContext MessageContext
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
        /// Closes any existing secure channel and opens a new one using an existing channel.
        /// </summary>
        /// <param name="connection">The reverse transport connection for the Reconnect.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        public abstract void Reconnect(ITransportWaitingConnection connection);

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
        /// Sends a request over the secure channel.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The response.</returns>
        public Task<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct)
        {
            return Task.Factory.FromAsync<IServiceRequest, IServiceResponse>(BeginSendRequest, EndSendRequest, request, null);
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
                Utils.LogError(e, "Unexpected error sending outgoing request.");
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
            IServiceMessageContext messageContext)
        {
            // initialize the channel which will be created with the server.
            string uriScheme = new Uri(description.EndpointUrl).Scheme;
            ITransportChannel channel = TransportBindings.Channels.GetChannel(uriScheme);
            if (channel == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.", uriScheme);
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
            IServiceMessageContext messageContext)
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
            IServiceMessageContext messageContext)
        {
            string uriScheme = new Uri(description.EndpointUrl).Scheme;

            switch (description.TransportProfileUri)
            {
                case Profiles.UaTcpTransport:
                {
                    uriScheme = Utils.UriSchemeOpcTcp;
                    break;
                }

                case Profiles.HttpsBinaryTransport:
                {
                    uriScheme = Utils.UriSchemeHttps;
                    break;
                }

                case Profiles.UaWssTransport:
                {
                    uriScheme = Utils.UriSchemeOpcWss;
                    break;
                }
            }

            // initialize the channel which will be created with the server.
            ITransportChannel channel = TransportBindings.Channels.GetChannel(uriScheme);
            if (channel == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadProtocolVersionUnsupported,
                    "Unsupported transport profile for scheme {0}.", uriScheme);
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
        #endregion

        #region Private Fields
        internal TransportChannelSettings m_settings;
        internal IServiceMessageContext m_messageContext;
        internal ITransportChannel m_uaBypassChannel;
        internal int m_operationTimeout;
        internal IChannelBase m_channel;
        internal string g_ImplementationString = "Opc.Ua.ChannelBase UA Client " + Utils.GetAssemblySoftwareVersion();
        #endregion
    }

    /// <summary>
    /// A base class for UA channel objects used access UA interfaces
    /// </summary>
    public class UaChannelBase<TChannel> : UaChannelBase where TChannel : class, IChannelBase
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
            UaChannelAsyncResult asyncResult = new UaChannelAsyncResult(m_channel, callback, asyncState);

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
            UaChannelAsyncResult asyncResult = UaChannelAsyncResult.WaitForComplete(result);
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

            Utils.LogInfo("RECONNECT: Reconnecting to {0}.", m_settings.Description.EndpointUrl);
        }

        /// <inheritdoc/>
        public override void Reconnect(ITransportWaitingConnection connection)
        {
            throw new NotImplementedException("Reconnect for waiting connections is not supported for this channel");
        }
        #endregion

        #region UaChannelAsyncResult Class
        /// <summary>
        /// An async result object that wraps the UA channel.
        /// </summary>
        protected class UaChannelAsyncResult : AsyncResultBase
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="UaChannelAsyncResult"/> class.
            /// </summary>
            /// <param name="channel">The channel.</param>
            /// <param name="callback">The callback.</param>
            /// <param name="callbackData">The callback data.</param>
            public UaChannelAsyncResult(
                TChannel channel,
                AsyncCallback callback,
                object callbackData)
                : base(callback, callbackData, 0)
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
                    Utils.LogError(e, "Unexpected exception invoking UaChannelAsyncResult callback function.");
                }
            }

            /// <summary>
            /// Checks for a valid IAsyncResult object and waits for the operation to complete.
            /// </summary>
            /// <param name="ar">The IAsyncResult object for the operation.</param>
            /// <returns>The oject that </returns>
            public static new UaChannelAsyncResult WaitForComplete(IAsyncResult ar)
            {
                UaChannelAsyncResult asyncResult = ar as UaChannelAsyncResult;

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
