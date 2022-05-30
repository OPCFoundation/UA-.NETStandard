/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;


namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new <see cref="WebSocketTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public class WebSocketTransportListenerFactory : WebSocketServiceHost
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public override string UriScheme => Utils.UriSchemeOpcWss;

        /// <summary>
        /// The method creates a new instance of a <see cref="WebSocketTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public override ITransportListener Create()
        {
            return new WebSocketTransportListener();
        }
    }
    /// <summary>
    /// Manages the connections for a UA WebSocket server.
    /// </summary>
    public class WebSocketTransportListener : ITransportListener
    {
        #region Private Fields
        private object m_lock = new object();
        private string m_listenerId;
        private Uri m_url;
        private EndpointDescriptionCollection m_descriptions;
        private EndpointConfiguration m_configuration;
        private ChannelQuotas m_quotas;
        private ITransportListenerCallback m_callback;
        private WebSocketListener m_listener;
        private BufferManager m_bufferManager;
        private X509Certificate2 m_serverCertificate;
        private Timer m_cleanupTimer;
        private long m_maxSetupTime;
        private long m_maxIdleTime;
        private string g_ImplementationString = "WebSocketTransportListener UA-{0} " + Utils.GetAssemblySoftwareVersion();

        private long m_nextChannelId = 1000;
        private Dictionary<uint, WebSocketServerChannel> m_channels = new Dictionary<uint, WebSocketServerChannel>();
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="WebSocketTransportListener"/> class.
        /// </summary>
        public WebSocketTransportListener()
        {
            m_nextChannelId = 0;
            m_maxIdleTime = 60000 * 60;
            m_maxSetupTime = 60000 * 2;
            m_cleanupTimer = new Timer(OnCleanupTimerExpired, null, m_maxSetupTime, m_maxSetupTime / 2);
            m_descriptions = new EndpointDescriptionCollection();
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_simulator")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ConnectionStatusChanged = null;
                ConnectionWaiting = null;
            }
        }
        #endregion

        #region ITransportListener Members
        /// <summary>
        /// The URI scheme handled by the listener.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcWss;

        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback)
        {
            // assign a unique guid to the listener.
            m_listenerId = Guid.NewGuid().ToString();
            m_serverCertificate = settings.ServerCertificate;

            // m_tlsCertificate = settings.TlsCertificate;

            m_url = baseAddress;
            m_descriptions = settings.Descriptions;
            m_configuration = settings.Configuration;

            m_listener = new WebSocketListener(m_url, settings);
            m_listener.ConnectionOpened += Listener_ConnectionOpened;
            m_listener.ConnectionClosed += Listener_ConnectionClosed;
            m_listener.ReceiveMessage += Listener_ReceiveMessage;

            // initialize the quotas.
            m_quotas = new ChannelQuotas();

            m_quotas.MaxBufferSize = m_configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = m_configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = m_configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = m_configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext() {
                MaxArrayLength = m_configuration.MaxArrayLength,
                MaxByteStringLength = m_configuration.MaxByteStringLength,
                MaxMessageSize = m_configuration.MaxMessageSize,
                MaxStringLength = m_configuration.MaxStringLength,
                NamespaceUris = settings.NamespaceUris,
                ServerUris = new StringTable(),
                Factory = settings.Factory
        };

            m_quotas.CertificateValidator = settings.CertificateValidator;

            // save the callback to the server.
            m_callback = callback;

            m_bufferManager = new BufferManager(baseAddress.PathAndQuery, (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);

            // start the listener.
            Start();
        }

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Close()
        {
            Stop();
        }

        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        public event ConnectionWaitingHandlerAsync ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <inheritdoc/>
        /// <remarks>
        /// Reverse connect for the websocket transport listener is not implemeted.
        /// </remarks>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            // suppress warnings
            ConnectionWaiting = null;
            ConnectionWaiting?.Invoke(null, null);
            ConnectionStatusChanged = null;
            ConnectionStatusChanged?.Invoke(null, null);
            throw new NotImplementedException();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl => m_url;

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
            IPAddress address = IPAddress.IPv6Any;
            int port = (m_url.Port <= 0) ? 4843 : m_url.Port;

            if (!IPAddress.TryParse(m_url.DnsSafeHost, out address))
            {
                address = IPAddress.IPv6Any;
            }


            Task.Run(() => m_listener.ListenAsync(address, port));
        }

        /// <summary>
        /// Called when a UpdateCertificate event occured.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidator validator,
            X509Certificate2 serverCertificate,
            X509Certificate2Collection serverCertificateChain)
        {
            Stop();

            m_quotas.CertificateValidator = validator;
            m_serverCertificate = serverCertificate;
            foreach (var description in m_descriptions)
            {
                if (description.ServerCertificate != null)
                {
                    description.ServerCertificate = serverCertificate.RawData;
                }
            }

            Start();
        }
        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            lock (m_lock)
            {
                if (m_listener != null)
                {
                    m_listener.ConnectionOpened -= Listener_ConnectionOpened;
                    m_listener.ConnectionClosed -= Listener_ConnectionClosed;
                    m_listener.ReceiveMessage -= Listener_ReceiveMessage;
                    m_listener.CloseAsync().Wait();
                }
            }
            Dispose();
        }
        private void CleanupChannel(WebSocketServerChannel channel)
            {
                if (!channel.IsDisposed)
                {
                    lock (m_channels)
                    {
                        m_channels.Remove(channel.ChannelId);
                    }

                    channel.IsDisposed = true;

                    if (channel.Serializer != null)
                    {
                        channel.Serializer.Dispose();
                    }

                    channel.Connection.Dispose();
                }
            }

        private void OnCleanupTimerExpired(object state)
        {
            try
            {
                Queue<WebSocketServerChannel> channelsToCleanup = new Queue<WebSocketServerChannel>();

                lock (m_channels)
                {
                    foreach (var channel in m_channels)
                    {
                        long quietTime = DateTime.UtcNow.Ticks - channel.Value.LastMessageTime;
                        quietTime /= TimeSpan.TicksPerMillisecond;

                        if (quietTime > m_maxIdleTime || (!channel.Value.WasOpened && quietTime > m_maxSetupTime))
                        {
                            channelsToCleanup.Enqueue(channel.Value);
                        }
                    }
                }

                while (channelsToCleanup.Count > 0)
                {
                    CleanupChannel(channelsToCleanup.Dequeue());
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.Write("Unexpected error deleting connections: " + e.Message);
            }
        }
            

        private class WebSocketServerChannel
        {
            public uint ChannelId;
            public uint RequestId;
            public bool UseJsonEncoding;
            public UaTcpChannelSerializer Serializer;
            public WebSocketConnection Connection;
            public long LastMessageTime;
            public bool IsDisposed;
            public bool WasOpened;
        }

        private void Listener_ConnectionOpened(object sender, ConnectionStateEventArgs e)
        {
            var channel = new WebSocketServerChannel() {
                ChannelId = (uint)Utils.IncrementIdentifier(ref m_nextChannelId),
                Connection = e.Connection,
                LastMessageTime = DateTime.UtcNow.Ticks
            };

            // assume we are using UA TCP until we get the HTTP upgrade request.
            channel.Serializer = new UaTcpChannelSerializer(m_bufferManager, m_quotas, m_serverCertificate, null, m_descriptions);
            channel.UseJsonEncoding = false;
            channel.Serializer.ChannelId = channel.ChannelId;

            lock (m_channels)
            {
                m_channels[channel.ChannelId] = channel;
            }

            e.Connection.Handle = channel;
        }

        private void Listener_ConnectionClosed(object sender, ConnectionStateEventArgs e)
        {
            var channel = e.Connection.Handle as WebSocketServerChannel;

            if (channel != null)
            {
                CleanupChannel(channel);
            }

            if (ServiceResult.IsBad(e.Error))
            {
                Utils.Trace("WebSocketServerChannel Error: {0}", e.Error);
            }
        }

        private void Listener_ReceiveMessage(object sender, ReceiveMessageEventArgs e)
        {
            var channel = e.Connection.Handle as WebSocketServerChannel;

            if (channel.UseJsonEncoding || channel.Connection.MessageEncoding == "opcua+uajson")
            {
                // convert channel to a JSON channel if this is the first request.
                if (!channel.UseJsonEncoding)
                {
                    channel.UseJsonEncoding = true;
                    channel.RequestId = 0;
                    channel.Serializer.Dispose();
                    channel.Serializer = null;
                }

                // no hello/ack or open secure channel when using JSON.
                ProcessRequest(e.Connection, e.Message);
                return;
            }

            var message = e.Message;
            var messageType = BitConverter.ToUInt32(message.Array, message.Offset);

            switch (messageType)
            {
                case TcpMessageType.Hello:
                {
                    ProcessHello(e.Connection, message);
                    break;
                }

                case TcpMessageType.Open | TcpMessageType.Final:
                {
                    ProcessOpen(e.Connection, message);
                    break;
                }

                case TcpMessageType.Close | TcpMessageType.Final:
                {
                    ProcessClose(e.Connection, message);
                    break;
                }

                case TcpMessageType.Message | TcpMessageType.Intermediate:
                case TcpMessageType.Message | TcpMessageType.Final:
                {
                    ProcessRequest(e.Connection, message);
                    break;
                }

                default:
                {
                    Utils.Trace("Invalid MessageType: 0x{0:X8}", messageType);
                    m_bufferManager.ReturnBuffer(message.Array, "WebSocketTransportListener.Listener_ReceiveMessage");
                    e.Connection.Dispose();
                    break;
                }
            }
        }

        private void ProcessHello(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ServiceResult result = null;
            ArraySegment<byte> message;

            var channel = connection.Handle as WebSocketServerChannel;

            if (channel == null)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessHello");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(StatusCodes.BadSecureChannelIdInvalid));
                connection.SendMessage(message);
                return;
            }

            channel.LastMessageTime = DateTime.UtcNow.Ticks;

            try
            {
                result = channel.Serializer.ProcessHelloMessage(chunk);

                if (ServiceResult.IsGood(result))
                {
                    message = channel.Serializer.ConstructAcknowledgeMessage();
                    connection.SendMessage(message);
                    return;
                }
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessHello");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(e));
                connection.SendMessage(message);
            }
        }

        private void ProcessOpen(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ArraySegment<byte> message;

            var channel = connection.Handle as WebSocketServerChannel;

            if (channel == null)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessOpen");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(StatusCodes.BadSecureChannelIdInvalid));
                connection.SendMessage(message);
                return;
            }

            channel.LastMessageTime = DateTime.UtcNow.Ticks;

            try
            {
                uint requestId = channel.Serializer.ProcessOpenSecureChannelRequest(chunk);

                foreach (var endpoint in m_descriptions)
                {
                    if (endpoint.SecurityMode == channel.Serializer.SecurityMode)
                    {
                        if (endpoint.SecurityPolicyUri == channel.Serializer.SecurityPolicyUri)
                        {
                            channel.Serializer.EndpointDescription = endpoint;
                            break;
                        }
                    }
                }

                Opc.Ua.Security.Audit.SecureChannelCreated(
                    String.Format(g_ImplementationString, (connection.Stream is System.Net.Security.SslStream) ? "WebSockets" : "TCP"),
                    m_url.ToString(),
                    channel.ChannelId.ToString(),
                    channel.Serializer.EndpointDescription,
                    channel.Serializer.ClientCertificate,
                    channel.Serializer.ServerCertificate,
                    BinaryEncodingSupport.Required);

                channel.WasOpened = true;
                message = channel.Serializer.ConstructOpenSecureChannelResponse(requestId);
                connection.SendMessage(message);
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessHello");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(e));
                connection.SendMessage(message);
            }
        }

        private void ProcessClose(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            ArraySegment<byte> message;

            var channel = connection.Handle as WebSocketServerChannel;

            if (channel == null)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessClose");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(StatusCodes.BadSecureChannelIdInvalid));
                connection.SendMessage(message);
                return;
            }

            channel.LastMessageTime = DateTime.UtcNow.Ticks;

            try
            {
                channel.Serializer.ProcessCloseSecureChannelRequest(chunk);
                CleanupChannel(channel);
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessClose");
                message = channel.Serializer.ConstructErrorMessage(new ServiceResult(e));
                connection.SendMessage(message);
            }
        }

        private class WebSocketProcessRequestAsyncState
        {
            public uint RequestId;
            public WebSocketServerChannel Channel;
        }

        private void ProcessRequest(WebSocketConnection connection, ArraySegment<byte> chunk)
        {
            var channel = connection.Handle as WebSocketServerChannel;

            IServiceRequest request = null;
            channel.LastMessageTime = DateTime.UtcNow.Ticks;

            try
            {
                uint requestId = 0;

                if (channel.UseJsonEncoding)
                {
                    request = JsonDecoder.DecodeMessage(chunk, null, m_quotas.MessageContext) as IServiceRequest;

                    if (request == null)
                    {
                        throw new ServiceResultException(ServiceResult.Create(StatusCodes.BadStructureMissing, "Could not parse request body."));
                    }

                    m_bufferManager.ReturnBuffer(chunk.Array, "WebSocketTransportListener.ProcessRequest");
                    requestId = ++channel.RequestId;
                }
                else
                {
                    request = channel.Serializer.ProcessRequest(chunk, out requestId);
                }

                if (request != null)
                {
                    var ad = new WebSocketProcessRequestAsyncState() {
                        Channel = channel,
                        RequestId = requestId
                    };

                    var endpoint = (channel.Serializer != null) ? channel.Serializer.EndpointDescription : m_descriptions[0];

                    m_callback.BeginProcessRequest(
                        m_listenerId,
                        endpoint,
                        request,
                        OnRequestProcessed,
                        ad);
                }
            }
            catch (Exception exception)
            {
                // check if the application is forcing the channel to close.
                var sre = exception as ServiceResultException;

                if (sre != null)
                {
                    if (sre.StatusCode == StatusCodes.BadSecureChannelClosed)
                    {
                        CleanupChannel(channel);
                    }
                }

                if (!channel.IsDisposed)
                {
                    // send a UATCP error message if not using JSON encoding.
                    if (!channel.UseJsonEncoding)
                    {
                        var message = channel.Serializer.ConstructErrorMessage(ServiceResult.Create(exception, StatusCodes.BadTcpInternalError, "Could not process request."));
                        connection.SendMessage(message);
                    }

                    // send a JSON encoded response.
                    else
                    {
                        ServiceFault fault = EndpointBase.CreateServiceFault(request, exception);
                        SendJsonResponse(channel, fault);
                    }
                }

                return;
            }
        }

        private void SendJsonResponse(WebSocketServerChannel channel, IServiceResponse response)
        {
            var buffer = m_bufferManager.TakeBuffer(m_quotas.MaxBufferSize, "WebSocketTransportListener.SendJsonResponse");

            try
            {
                var message = JsonEncoder.EncodeMessage(response, buffer, m_quotas.MessageContext);
                channel.Connection.SendMessage(message);
            }
            catch (Exception e)
            {
                m_bufferManager.ReturnBuffer(buffer, "WebSocketTransportListener.SendJsonResponse");
                Utils.Trace(e, "WEBSOCKET LISTENER - Unexpected error sending JSON response. [{0}] {1}");
            }
        }

        private void OnRequestProcessed(IAsyncResult result)
        {
            WebSocketProcessRequestAsyncState ad = (WebSocketProcessRequestAsyncState)result.AsyncState;

            try
            {
                if (!ad.Channel.IsDisposed)
                {
                    var response = m_callback.EndProcessRequest(result);

                    if (!ad.Channel.UseJsonEncoding)
                    {
                        var message = ad.Channel.Serializer.ConstructResponse(ad.RequestId, response);
                        ad.Channel.Connection.SendMessage(message);
                    }
                    else
                    {
                        SendJsonResponse(ad.Channel, response);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "WEBSOCKET LISTENER - Unexpected error sending response.");
            }
        }
        #endregion
    }
}
