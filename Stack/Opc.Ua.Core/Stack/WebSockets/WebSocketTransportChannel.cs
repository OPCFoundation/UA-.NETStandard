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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Wraps the AmqpTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class WebSocketTransportChannel : ITransportChannel
    {
        #region Private Fields
        private object m_lock = new object();
        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private ChannelQuotas m_quotas;
        private long m_requestId;
        private UaTcpChannelSerializer m_serializer;
        private WebSocketConnection m_connection;
        private WebSocketListener m_listener;
        private BufferManager m_bufferManager;
        private Dictionary<uint, SendRequestAsyncResult> m_requests;
        private SendRequestAsyncResult m_connectInProgress;
        private string g_ImplementationString = "WebSocketTransportChannel UA-{0} " + Utils.GetAssemblySoftwareVersion();
        #endregion

        /// <remarks/>
        public WebSocketTransportChannel(ApplicationConfiguration configuration)
        {
            m_requests = new Dictionary<uint, SendRequestAsyncResult>();
        }

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
                var connection = m_connection;
                m_connection = null;

                if (connection != null)
                {
                    connection.Dispose();
                }

                var listener = m_listener;
                m_listener = null;

                if (listener != null)
                {
                    listener.ReceiveMessage -= Listener_ReceiveMessage;
                    listener.Dispose();
                }
            }
        }
        #endregion

        #region ITransportChannel Members
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
            get { return m_operationTimeout; }
            set { m_operationTimeout = value; }
        }

        /// <summary>
        /// SupportedFeatures
        /// </summary>
        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.Open | TransportChannelFeatures.BeginOpen | TransportChannelFeatures.Reconnect | TransportChannelFeatures.BeginSendRequest;

        /// <summary>
        /// CurrentToken
        /// </summary>
        public ChannelToken CurrentToken
        {
            get { return null; }
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
            return new AsyncResultBase(callback, callbackData, m_operationTimeout);
        }

        /// <summary>
        /// Completes an asynchronous operation to open a secure channel.
        /// </summary>
        /// <param name="result">The result returned from the BeginOpen call.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        /// <seealso cref="Open"/>
        public void EndOpen(IAsyncResult result)
        {
        }

        /// <summary>
        /// Closes the secure channel.
        /// </summary>
        public void Close()
        {
            var serializer = m_serializer;
            m_serializer = null;

            var connection = m_connection;
            m_connection = null;

            if (connection != null)
            {
                var message = serializer.ConstructCloseSecureChannelRequest(Utils.IncrementIdentifier(ref m_requestId));
                connection.SendMessage(message);
                serializer.Dispose();
            }

            var listener = m_listener;
            m_listener = null;

            if (listener != null)
            {
                listener.ConnectionClosed -= Listener_ConnectionClosed;
                listener.ReceiveMessage -= Listener_ReceiveMessage;
                listener.CloseAsync();
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

        private class SendRequestAsyncResult : AsyncResultBase
        {
            public uint RequestId;
            public WebSocketConnection Connection;
            public IServiceRequest Request;
            public IServiceResponse Response;
            public Task WorkItem;
            public CancellationToken CancellationToken;

            public SendRequestAsyncResult(AsyncCallback callback, object callbackData, int timeout)
            :
                base(callback, callbackData, timeout)
            {
                CancellationToken = new CancellationToken();
            }
        }
        
        private void Listener_ConnectionClosed(object sender, ConnectionStateEventArgs e)
        {
            if (ServiceResult.IsBad(e.Error))
            {
                Utils.Trace("ConnectionClosed: {0}", e.Error);

                lock (m_requests)
                {
                    var list = new List<SendRequestAsyncResult>(m_requests.Values);

                    foreach (var request in list)
                    {
                        request.Exception = new ServiceResultException(e.Error);
                        request.OperationCompleted();
                    }
                }
            }
        }

        private void Listener_ReceiveMessage(object sender, ReceiveMessageEventArgs e)
        {
            var message = e.Message;
            var messageType = BitConverter.ToUInt32(message.Array, message.Offset);

            switch (messageType)
            {
                case TcpMessageType.Acknowledge:
                {
                    ProcessAcknowledge(e.Connection, message);
                    break;
                }

                case TcpMessageType.Open | TcpMessageType.Final:
                {
                    ProcessOpen(e.Connection, message);
                    break;
                }

                case TcpMessageType.Intermediate | TcpMessageType.Message:
                case TcpMessageType.Final | TcpMessageType.Message:
                {
                    ProcessResponse(e.Connection, message);
                    break;
                }

                case TcpMessageType.Error:
                {
                    ProcessError(e.Connection, message);
                    break;
                }

                default:
                {
                    Utils.Trace("Invalid MessageType: 0x{0:X8}", messageType);
                    m_bufferManager.ReturnBuffer(message.Array, "Listener_ReceiveMessage");
                    break;
                }
            }
        }

        private void ProcessError(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ServiceResult result = null;

            try
            {
                result = m_serializer.ProcessError(chunk);
                connection.Dispose();
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessAcknowledge");
                Utils.Trace(e, "ERROR [WebSocketTransportChannel.ProcessOpen] Could not process OpenSecureChannel response.");
                result = new ServiceResult(e);
            }

            var ar = m_connectInProgress;

            if (ar != null)
            {
                m_connectInProgress = null;
                ar.Exception = new ServiceResultException(result);
                ar.OperationCompleted();
            }

            lock (m_requests)
            {
                foreach (var request in m_requests)
                {
                    request.Value.Exception = new ServiceResultException(result);
                    request.Value.OperationCompleted();
                }
            }
        }

        private void ProcessAcknowledge(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ServiceResult result = null;
            ArraySegment<byte> message;

            try
            {
                m_serializer.ProcessAcknowledgeMessage(chunk);
                message = m_serializer.ConstructOpenSecureChannelRequest(false);
                connection.SendMessage(message);
                return;
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessAcknowledge");
                Utils.Trace(e, "ERROR [WebSocketTransportChannel.ProcessOpen] Could not process OpenSecureChannel response.");
                result = new ServiceResult(e);
            }

            var ar = m_connectInProgress;

            if (ar != null)
            {
                m_connectInProgress = null;
                ar.Exception = new ServiceResultException(result);
                ar.OperationCompleted();
                return;
            }
        }

        private void ProcessOpen(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ServiceResult result = null;
            SendRequestAsyncResult ar = null;

            try
            {
                m_serializer.ProcessOpenSecureChannelResponse(chunk);
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessResponse");
                Utils.Trace(e, "ERROR [WebSocketTransportChannel.ProcessOpen] Could not process OpenSecureChannel response.");
                result = new ServiceResult(e);
            }

            ar = m_connectInProgress;

            if (ServiceResult.IsGood(result))
            {
                Opc.Ua.Security.Audit.SecureChannelCreated(
                    String.Format(g_ImplementationString, (connection.Stream is System.Net.Security.SslStream) ? "WebSocket" : "TCP"),
                    m_url.ToString(),
                    m_serializer.ChannelId.ToString(),
                    m_serializer.EndpointDescription,
                    m_serializer.ClientCertificate,
                    m_serializer.ServerCertificate,
                    BinaryEncodingSupport.Required);

                if (ar != null)
                {
                    if (Interlocked.CompareExchange(ref m_connection, ar.Connection, null) != null)
                    {
                        ar.Connection.Dispose();
                        throw new ServiceResultException(StatusCodes.BadSecureChannelClosed, "ProcessOpen: channel closed due to simulataneous connects.");
                    }

                    m_connectInProgress = null;
                    var message2 = m_serializer.ConstructRequest(ar.RequestId, ar.Request);
                    ar.Connection.SendMessage(message2);
                }
            }
            else
            {
                if (ar != null)
                {
                    m_connectInProgress = null;
                    ar.Exception = new ServiceResultException(result);
                    ar.OperationCompleted();
                    return;
                }
            }
        }

        private void ProcessResponse(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            // parse and process response.
            uint requestId = 0;
            IServiceResponse response = null;

            try
            {
                response = m_serializer.ProcessResponse(chunk, out requestId);

                // nothing more to do with partial message.
                if (response == null)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessResponse");
                Utils.Trace(exception, "ERROR [WebSocketTransportChannel.ProcessResponse] Could not process response.");
                return;
            }

            // notify application if not already timed out.
            SendRequestAsyncResult ar;

            lock (m_requests)
            {
                if (!m_requests.TryGetValue(requestId, out ar))
                {
                    Utils.Trace(Utils.TraceMasks.Information, "WARNING [WebSocketTransportChannel.ProcessResponse] Response received for unknown request.");
                    return;
                }
            }

            ar.Response = response;
            ar.OperationCompleted();
        }

        private async void ConnectAsync(SendRequestAsyncResult ar)
        {
            try
            {
                m_serializer = new UaTcpChannelSerializer(
                    m_bufferManager,
                    m_quotas,
                    m_settings.ServerCertificate,
                    m_settings.ClientCertificate,
                    new EndpointDescriptionCollection(new EndpointDescription[] { m_settings.Description }));

                ar.Connection = await m_listener.ConnectAsync(
                    m_url.DnsSafeHost, 
                    IPAddress.Any, 
                    (m_url.Port <= 0) ? Utils.UaWebSocketsDefaultPort : m_url.Port,
                    (m_url.Scheme == Utils.UriSchemeOpcTcp) ? TlsProtocol.None : TlsProtocol.TlsBestAvailable,
                    ar.CancellationToken).ConfigureAwait(false);

                m_connectInProgress = ar;
                var message = m_serializer.ConstructHelloMessage();
                ar.Connection.SendMessage(message);
            }
            catch (Exception e)
            {
                ar.Exception = e;
                ar.OperationCompleted();
            }
        }

        /// <summary>
        /// Begins an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            SendRequestAsyncResult ar = new SendRequestAsyncResult(callback, callbackData, (int)request.RequestHeader.TimeoutHint);

            try
            {
                ar.RequestId = Utils.IncrementIdentifier(ref m_requestId);
                ar.Request = request;

                lock (m_requests)
                {
                    m_requests[ar.RequestId] = ar;
                }

                ar.Connection = m_connection;

                if (ar.Connection == null)
                {
                    ar.WorkItem = Task.Run(() => ConnectAsync(ar), ar.CancellationToken);
                    return ar;
                }

                var message = m_serializer.ConstructRequest(ar.RequestId, request);
                m_connection.SendMessage(message);
                return ar;
            }
            catch (Exception e)
            {
                ar.Exception = e;
                ar.OperationCompleted();
            }

            return ar;
        }

        /// <summary>
        /// Completes an asynchronous operation to send a request over the secure channel.
        /// </summary>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            SendRequestAsyncResult ar = result as SendRequestAsyncResult;

            if (ar == null)
            {
                throw new ArgumentException("Invalid result object passed.", "result");
            }

            try
            {
                if (!ar.WaitForComplete())
                {
                    throw new TimeoutException();
                }

                return ar.Response;
            }
            finally
            {
                lock (m_requests)
                {
                    m_requests.Remove(ar.RequestId);
                }
            }
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

            m_listener = new WebSocketListener(m_settings);
            m_listener.ReceiveMessage += Listener_ReceiveMessage;
            m_listener.ConnectionClosed += Listener_ConnectionClosed;
            m_bufferManager = new BufferManager(url.PathAndQuery, (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);
        }

        public void Reconnect()
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
