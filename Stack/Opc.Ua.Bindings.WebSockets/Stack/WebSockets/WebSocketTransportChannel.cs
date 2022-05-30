/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{

    /// <summary>
    /// Creates a new WebSocketTransportChannel with ITransportChannel interface.
    /// </summary>
    public class WebSocketTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcWss;

        /// <summary>
        /// The method creates a new instance of a Https transport channel
        /// </summary>
        /// <returns>The transport channel</returns>
        public ITransportChannel Create()
        {
            return new WebSocketTransportChannel();
        }
    }

    /// <summary>
    /// Wraps the WebSocketTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class WebSocketTransportChannel : ITransportChannel
    {


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
        private Dictionary<uint, SendRequestAsyncResult> m_requests = new Dictionary<uint, SendRequestAsyncResult>();
        private SendRequestAsyncResult m_connectInProgress;
        private string g_ImplementationString = "WebSocketTransportChannel UA-{0} " + Utils.GetAssemblySoftwareVersion();

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcWss;

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures =>
            TransportChannelFeatures.Open |
            TransportChannelFeatures.Reconnect |
            TransportChannelFeatures.BeginSendRequest |
            TransportChannelFeatures.SendRequestAsync;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription => m_settings.Description;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => m_settings.Configuration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_quotas.MessageContext;

        /// <inheritdoc/>
        public ChannelToken CurrentToken => null;

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => m_operationTimeout;
            set => m_operationTimeout = value;
        }

        /// <inheritdoc/>
        public void Initialize(
            Uri url,
            TransportChannelSettings settings)
        {
            SaveSettings(url, settings);
        }

        /// <summary>
        /// Initializes a secure channel with a waiting reverse connection.
        /// </summary>
        /// <param name="connection">The connection to use.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Initialize(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings)
        {
            SaveSettings(connection.EndpointUrl, settings);
        }

        /// <inheritdoc/>
        public void Open()
        {
        }

        /// <inheritdoc/>
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
                    (m_url.Scheme == Utils.UriSchemeOpcWss) ? TlsProtocol.Tls12 : TlsProtocol.TlsBestAvailable,
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

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndOpen(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void Reconnect()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        void ITransportChannel.Reconnect(ITransportWaitingConnection connection)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndClose(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public IServiceResponse SendRequest(IServiceRequest request)
        {
            IAsyncResult result = BeginSendRequest(request, null, null);
            return EndSendRequest(result);
        }

        /// <inheritdoc/>
        public Task<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct)
        {
            IAsyncResult result = BeginSendRequest(request, null, null);
            return new Task<IServiceResponse>(()=> { return EndSendRequest(result); });
        }

        /// <summary>
        /// Save the settings for a connection.
        /// </summary>
        /// <param name="url">The server url.</param>
        /// <param name="settings">The settings for the transport channel.</param>
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            m_url = new Uri(url.ToString());

            m_settings = settings;
            m_operationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas {
                MaxBufferSize = m_settings.Configuration.MaxBufferSize,
                MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                ChannelLifetime = m_settings.Configuration.ChannelLifetime,
                SecurityTokenLifetime = m_settings.Configuration.SecurityTokenLifetime,

                MessageContext = new ServiceMessageContext {
                    MaxArrayLength = m_settings.Configuration.MaxArrayLength,
                    MaxByteStringLength = m_settings.Configuration.MaxByteStringLength,
                    MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                    MaxStringLength = m_settings.Configuration.MaxStringLength,
                    NamespaceUris = m_settings.NamespaceUris,
                    ServerUris = new StringTable(),
                    Factory = m_settings.Factory
                },

                CertificateValidator = settings.CertificateValidator
            };
            m_listener = new WebSocketListener(m_settings);
            m_listener.ReceiveMessage += Listener_ReceiveMessage;
            m_listener.ConnectionClosed += Listener_ConnectionClosed;
            m_bufferManager = new BufferManager(url.PathAndQuery, (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);
        }

        private class SendRequestAsyncResult : AsyncResultBase
        {
            public uint RequestId;
            public WebSocketConnection Connection;
            public IServiceRequest Request;
            public IServiceResponse Response;
            public Task WorkItem;
            public new CancellationToken CancellationToken;

            public SendRequestAsyncResult(AsyncCallback callback, object callbackData, int timeout)
            :
                base(callback, callbackData, timeout)
            {
                CancellationToken = new CancellationToken();
            }
        }
    }
}

