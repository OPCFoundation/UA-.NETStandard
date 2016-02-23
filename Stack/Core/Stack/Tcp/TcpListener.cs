/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;
using System.Net;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the connections for a UA TCP server.
    /// </summary>
    public partial class UaTcpChannelListener : IDisposable, ITransportListener
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="UaTcpChannelListener"/> class.
        /// </summary>
        public UaTcpChannelListener()
        {
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
                lock (m_lock)
                {
                    if (m_listeningSocket != null)
                    {
                        try
                        {
                            m_listeningSocket.Dispose();
                        }
                        catch
                        {
                            // ignore errors.
                        }
                        
                        m_listeningSocket = null;
                    }

                    if (m_listeningSocketIPv6 != null)
                    {
                        try
                        {
                            m_listeningSocketIPv6.Dispose();
                        }
                        catch
                        {
                            // ignore errors.
                        }

                        m_listeningSocketIPv6 = null;
                    }

                    if (m_simulator != null)
                    {
                        Utils.SilentDispose(m_simulator);
                        m_simulator = null; 
                    }

                    foreach (TcpServerChannel channel in m_channels.Values)
                    {
                        Utils.SilentDispose(channel);
                    }
                }
            }
        }
        #endregion
        
        #region ITransportListener Members
        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open(Uri baseAddress, TransportListenerSettings settings, ITransportListenerCallback callback)
        {
            // assign a unique guid to the listener.
            m_listenerId = Guid.NewGuid().ToString();

            m_uri = baseAddress;
            m_descriptions = settings.Descriptions;
            m_configuration = settings.Configuration;

            // initialize the quotas.
            m_quotas = new TcpChannelQuotas();

            m_quotas.MaxBufferSize = m_configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = m_configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = m_configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = m_configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext();

            m_quotas.MessageContext.MaxArrayLength = m_configuration.MaxArrayLength;
            m_quotas.MessageContext.MaxByteStringLength = m_configuration.MaxByteStringLength;
            m_quotas.MessageContext.MaxMessageSize = m_configuration.MaxMessageSize;
            m_quotas.MessageContext.MaxStringLength = m_configuration.MaxStringLength;
            m_quotas.MessageContext.NamespaceUris = settings.NamespaceUris;
            m_quotas.MessageContext.ServerUris = new StringTable();
            m_quotas.MessageContext.Factory = settings.Factory;

            m_quotas.CertificateValidator = settings.CertificateValidator;

            // save the server certificate.
            m_serverCertificate = settings.ServerCertificate;
            //m_serverCertificateChain = settings.ServerCertificateChain;

            m_bufferManager = new BufferManager("Server", (int)Int32.MaxValue, m_quotas.MaxBufferSize);
            m_channels = new Dictionary<uint, TcpServerChannel>();

            // save the callback to the server.
            m_callback = callback;

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
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl
        {
            get { return m_uri; }
        }

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
            lock (m_lock)
            {
                // ensure a valid port.
                int port = m_uri.Port;

                if (port <= 0 || port > UInt16.MaxValue)
                {
                    port = Utils.UaTcpDefaultPort;
                }

                // create IPv4 socket.
                try                    
                {
                    IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);
                    m_listeningSocket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                    args.Completed += OnAccept;
                    args.UserToken = m_listeningSocket;
                    m_listeningSocket.Bind(endpoint);
                    m_listeningSocket.Listen(Int32.MaxValue);
                    m_listeningSocket.AcceptAsync(args);
                }
                catch
                {
                    // no IPv4 support.
                    m_listeningSocket = null;
                    Utils.Trace("failed to create IPv4 listening socket");
                }

                // create IPv6 socket
                try                    
                {
                    IPEndPoint endpointIPv6 = new IPEndPoint(IPAddress.IPv6Any, port);
                    m_listeningSocketIPv6 = new Socket(endpointIPv6.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                    args.Completed += OnAccept;
                    args.UserToken = m_listeningSocketIPv6;
                    m_listeningSocketIPv6.Bind(endpointIPv6);
                    m_listeningSocketIPv6.Listen(Int32.MaxValue);
                    m_listeningSocketIPv6.AcceptAsync(args);
                }
                catch
                {
                    // no IPv6 support
                    m_listeningSocketIPv6 = null;
                    Utils.Trace("failed to create IPv6 listening socket");
                }

                if (m_listeningSocketIPv6 == null && m_listeningSocket == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNoCommunication,
                        "Failed to establish tcp listener sockets for Ipv4 and IPv6.\r\n");
                }
            }
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            lock (m_lock)
            {
                if (m_listeningSocket != null)
                {
                    m_listeningSocket.Dispose();
                    m_listeningSocket = null;
                }

                if (m_listeningSocketIPv6 != null)
                {
                    m_listeningSocketIPv6.Dispose();
                    m_listeningSocketIPv6 = null;
                }
            }
        }

        /// <summary>
        /// Binds a new socket to an existing channel.
        /// </summary>
        internal bool ReconnectToExistingChannel(
            TcpMessageSocket         socket, 
            uint                     requestId,
            uint                     sequenceNumber,
            uint                     channelId,
            X509Certificate2         clientCertificate, 
            TcpChannelToken          token,
            OpenSecureChannelRequest request)
        {            
            TcpServerChannel channel = null;

            lock (m_lock)
            {
                if (!m_channels.TryGetValue(channelId, out channel))
                {
                    throw ServiceResultException.Create(StatusCodes.BadTcpSecureChannelUnknown, "Could not find secure channel referenced in the OpenSecureChannel request.");
                }
            }
                       
            channel.Reconnect(socket, requestId, sequenceNumber, clientCertificate, token, request);
            return true;
        }

        /// <summary>
        /// Called when a channel closes.
        /// </summary>
        internal void ChannelClosed(uint channelId)
        {
            lock (m_lock)
            {
                m_channels.Remove(channelId);
            }

            Utils.Trace("Channel {0} closed", channelId);
        }
        #endregion
        
        #region Socket Event Handler
        /// <summary>
        /// Handles a new connection.
        /// </summary>
        private void OnAccept(object sender, SocketAsyncEventArgs e)
        {
            TcpServerChannel channel = null;

            lock (m_lock)
            {
                // check if the accept socket has been created.
                if (e.AcceptSocket == null || e.SocketError != SocketError.Success)
                {
                    Utils.Trace("OnAccept: No accept socket or error." + e.SocketError.ToString());
                    return;
                }

                try
                {
                    // create the channel to manage incoming messages.
                    channel = new TcpServerChannel(
                        m_listenerId,
                        this,
                        m_bufferManager,
                        m_quotas,
                        m_serverCertificate,
                        m_descriptions);

                    // start accepting messages on the channel.
                    channel.Attach(++m_lastChannelId, e.AcceptSocket);

                    // save the channel for shutdown and reconnects.
                    m_channels.Add(m_lastChannelId, channel);

                    if (m_callback != null)
                    {
                        channel.SetRequestReceivedCallback(new TcpChannelRequestEventHandler(OnRequestReceived));
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Unexpected error accepting a new connection.");
                }

                // go back and wait for the next connection.
                try
                {
                    Socket listeningSocket = e.UserToken as Socket;
                    SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                    args.Completed += OnAccept;
                    args.UserToken = listeningSocket;
                    listeningSocket.AcceptAsync(args);
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Unexpected error listening for a new connection.");
                }

            }
        }
#endregion
                
#region Private Methods
        /// <summary>
        /// Handles requests arriving from a channel.
        /// </summary>
        private void OnRequestReceived(TcpServerChannel channel, uint requestId, IServiceRequest request)
        {
            try
            {
                if (m_callback != null)
                {
                    IAsyncResult result = m_callback.BeginProcessRequest(
                        channel.GlobalChannelId,
                        channel.EndpointDescription,
                        request,
                        OnProcessRequestComplete,
                        new object[] { channel, requestId, request });
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "TCPLISTENER - Unexpected error processing request.");
            }
        }

        private void OnProcessRequestComplete(IAsyncResult result)
        {
            try
            {
                object[] args = (object[])result.AsyncState;

                if (m_callback != null)
                {
                    TcpServerChannel channel = (TcpServerChannel)args[0];
                    IServiceResponse response = m_callback.EndProcessRequest(result);
                    channel.SendResponse((uint)args[1], response);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "TCPLISTENER - Unexpected error sending result.");
            }
        }

        /// <summary>
        /// Sets the URI for the listener.
        /// </summary>
        private void SetUri(Uri baseAddress, string relativeAddress)
        {
            if (baseAddress == null) throw new ArgumentNullException("baseAddress");

            // validate uri.
            if (!baseAddress.IsAbsoluteUri)
            {
                throw new ArgumentException(Utils.Format("Base address must be an absolute URI."), "baseAddress");
            }

            if (String.Compare(baseAddress.Scheme, Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new ArgumentException(Utils.Format("Invalid URI scheme: {0}.", baseAddress.Scheme), "baseAddress");
            }

            m_uri = baseAddress;

            // append the relative path to the base address.
            if (!String.IsNullOrEmpty(relativeAddress))
            {
                if (!baseAddress.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                {
                    UriBuilder uriBuilder = new UriBuilder(baseAddress);
                    uriBuilder.Path = uriBuilder.Path + "/";
                    baseAddress = uriBuilder.Uri;
                }

                m_uri = new Uri(baseAddress, relativeAddress);
            }
        }
#endregion

#region Private Fields
        private object m_lock = new object();

        private string m_listenerId;
        private Uri m_uri;
        private EndpointDescriptionCollection m_descriptions;
        private EndpointConfiguration m_configuration;

        private BufferManager m_bufferManager;
        private TcpChannelQuotas m_quotas;
        private X509Certificate2 m_serverCertificate;

        private uint m_lastChannelId;

        private Socket m_listeningSocket;
        private Socket m_listeningSocketIPv6;
        private Dictionary<uint,TcpServerChannel> m_channels;

        private Timer m_simulator;
        private ITransportListenerCallback m_callback;
#endregion
    }    
}
