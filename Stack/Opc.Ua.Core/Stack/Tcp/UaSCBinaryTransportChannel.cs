/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a transport channel for the ITransportChannel interface.
    /// Implements the UA-SC security and UA Binary encoding.
    /// The socket layer requires a IMessageSocketFactory implementation.
    /// </summary>
    public class UaSCUaBinaryTransportChannel : ITransportChannel, IMessageSocketChannel
    {
        #region Constructors
        public UaSCUaBinaryTransportChannel(IMessageSocketFactory messageSocketFactory)
        {
            m_messageSocketFactory = messageSocketFactory;
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
            if (disposing)
            {
                Utils.SilentDispose(m_channel);
                m_channel = null;
            }
        }
        #endregion

        #region IBinaryTransportChannel Members
        /// <summary>
        /// Returns the channel's underlying message socket if connected / available.
        /// </summary>
        public IMessageSocket Socket
        {
            get { lock (m_lock) { return m_channel?.Socket; } }
        }
        #endregion

        #region ITransportChannel Members
        /// <summary>
        /// A masking indicating which features are implemented.
        /// </summary>
        public TransportChannelFeatures SupportedFeatures
        {
            get { return TransportChannelFeatures.Open | TransportChannelFeatures.BeginOpen | TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest; }
        }

        /// <summary>
        /// Gets the description for the endpoint used by the channel.
        /// </summary>
        public EndpointDescription EndpointDescription
        {
            get { return m_settings.Description; }
        }

        /// <summary>
        /// Gets the configuration for the channel.
        /// </summary>
        public EndpointConfiguration EndpointConfiguration
        {
            get { return m_settings.Configuration; }
        }

        /// <summary>
        /// Gets the context used when serializing messages exchanged via the channel.
        /// </summary>
        public ServiceMessageContext MessageContext
        {
            get { return m_quotas.MessageContext; }
        }

        /// <summary>
        /// Gets or sets the default timeout for requests send via the channel.
        /// </summary>
        public int OperationTimeout
        {
            get { return m_operationTimeout;  }
            set { m_operationTimeout = value; }
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
            SaveSettings(url, settings);
        }

        /// <summary>
        /// Opens a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open()
        {
            // opens when the first request is called to preserve previous behavoir.
        }

        /// <summary>
        /// Begins an asynchronous operation to open a secure channel with the endpoint identified by the URL.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndOpen method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            lock (m_lock)
            {
                // create the channel.
                m_channel = new UaSCUaBinaryClientChannel(
                    Guid.NewGuid().ToString(),
                    m_bufferManager,
                    m_messageSocketFactory,
                    m_quotas,
                    m_settings.ClientCertificate,
                    m_settings.ServerCertificate,
                    m_settings.Description);

                // begin connect operation.
                return m_channel.BeginConnect(this.m_url, m_operationTimeout, callback, callbackData);
            }
        }

        /// <summary>
        /// Completes an asynchronous operation to open a secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginOpen call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        public void EndOpen(IAsyncResult result)
        {
            m_channel.EndConnect(result);
        }

        /// <summary>
        /// Closes any existing secure channel and opens a new one.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <remarks>
        /// Calling this method will cause outstanding requests over the current secure channel to fail.
        /// </remarks>
        public void Reconnect()
        {
            Utils.Trace("TransportChannel RECONNECT: Reconnecting to {0}.", m_url);

            lock (m_lock)
            {
                // the new channel must be created first because WinSock will reuse sockets and this
                // can result in messages sent over the old socket arriving as messages on the new socket.
                // if this happens the new channel is shutdown because of a security violation.
                UaSCUaBinaryClientChannel channel = m_channel;
                m_channel = null;
                
                // reconnect.
                OpenOnDemand();

                // begin connect operation.
                IAsyncResult result = m_channel.BeginConnect(m_url, m_operationTimeout, null, null);
                m_channel.EndConnect(result);

                // close existing channel.
                if (channel != null)
                {
                    try
                    {
                        channel.Close(1000);
                    }
                    catch (Exception)
                    {
                        // do nothing.
                    }
                    finally
                    {
                        channel.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Begins an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndReconnect method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect"/>
        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Completes an asynchronous operation to close the existing secure channel and open a new one.
        /// </summary>
        /// <param name="result">The result returned from the BeginReconnect call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Reconnect"/>
        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Closes the secure channel.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Close()
        {
            if (m_channel != null)
            {
                lock (m_lock)
                {
                    if (m_channel != null)
                    {
                        m_channel.Close(1000);
                        m_channel = null;
                    }
                }
            }
        }

        /// <summary>
        /// Begins an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndClose method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close"/>
        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Completes an asynchronous operation to close the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginClose call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Close"/>
        public void EndClose(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Sends a request over the secure channel.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>The response returned by the server.</returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public IServiceResponse SendRequest(IServiceRequest request)
        {
            IAsyncResult result = BeginSendRequest(request, null, null);
            return EndSendRequest(result);
        }

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="callback">The callback to call when the operation completes.</param>
        /// <param name="callbackData">The callback data to return with the callback.</param>
        /// <returns>
        /// The result which must be passed to the EndSendRequest method.
        /// </returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="SendRequest"/>
        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            UaSCUaBinaryClientChannel channel = m_channel;

            if (channel == null)
            {
                lock (m_lock)
                {
                    if (m_channel == null)
                    {
                        OpenOnDemand();
                    }

                    channel = m_channel;
                }
            }

            return channel.BeginSendRequest(request, m_operationTimeout, callback, callbackData);
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginSendRequest call.</param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="SendRequest"/>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            UaSCUaBinaryClientChannel channel = m_channel;

            if (channel == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecureChannelClosed, "Channel has been closed.");
            }

            return channel.EndSendRequest(result);
        }

        /// <summary>
        /// Saves the settings so the channel can be opened later.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="settings">The settings.</param>
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            // save the settings.
            m_url = url;
            m_settings = settings;
            m_operationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas();

            m_quotas.MaxBufferSize = m_settings.Configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = m_settings.Configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = m_settings.Configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = m_settings.Configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext();

            m_quotas.MessageContext.MaxArrayLength = m_settings.Configuration.MaxArrayLength;
            m_quotas.MessageContext.MaxByteStringLength = m_settings.Configuration.MaxByteStringLength;
            m_quotas.MessageContext.MaxMessageSize = m_settings.Configuration.MaxMessageSize;
            m_quotas.MessageContext.MaxStringLength = m_settings.Configuration.MaxStringLength;
            m_quotas.MessageContext.NamespaceUris = m_settings.NamespaceUris;
            m_quotas.MessageContext.ServerUris = new StringTable();
            m_quotas.MessageContext.Factory = m_settings.Factory;

            m_quotas.CertificateValidator = settings.CertificateValidator;

            // create the buffer manager.
            m_bufferManager = new BufferManager("Client", (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);
        }

        /// <summary>
        /// Opens the channel before sending the request.
        /// </summary>
        private void OpenOnDemand()
        {
            // create the channel.
            m_channel = new UaSCUaBinaryClientChannel(
                Guid.NewGuid().ToString(),
                m_bufferManager,
                m_messageSocketFactory,
                m_quotas,
                m_settings.ClientCertificate,
                m_settings.ServerCertificate,
                m_settings.Description);
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private ChannelQuotas m_quotas;
        private BufferManager m_bufferManager;
        private UaSCUaBinaryClientChannel m_channel;
        private IMessageSocketFactory m_messageSocketFactory;
        #endregion
    }
}
