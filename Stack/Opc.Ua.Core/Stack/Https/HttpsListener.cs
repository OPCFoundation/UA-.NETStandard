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
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Sockets;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public partial class UaHttpsChannelListener : IDisposable, ITransportListener
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="UaTcpChannelListener"/> class.
        /// </summary>
        public UaHttpsChannelListener()
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
            // nothing to do
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

        public class DefaultHttpHandler : DelegatingHandler
        {
            public UaHttpsChannelListener Listener { get; set; }

            protected async override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (request.Method != HttpMethod.Post)
                {
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                }

                return await Listener.SendAsync(request, cancellationToken);
            }
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
        #endregion

        #region Socket Event Handler
        /// <summary>
        /// Handles a new connection.
        /// </summary>
        private void OnAccept(object sender, SocketAsyncEventArgs e)
        {
            HttpsTransportChannel channel = null;

            lock (m_lock)
            {
                Socket listeningSocket = e.UserToken as Socket;

                if (listeningSocket == null)
                {
                    Utils.Trace("OnAccept: Listensocket was null.");
                    e.Dispose();
                    return;
                }

                // check if the accept socket has been created.
                if (e.AcceptSocket != null && e.SocketError == SocketError.Success)
                {
                    try
                    {
                        // create the channel to manage incoming messages.
                        channel = new HttpsTransportChannel();
                        //    m_listenerId,
                        //    this,
                        //    m_bufferManager,
                        //    m_quotas,
                        //    m_serverCertificate,
                        //    m_descriptions);

                        //// start accepting messages on the channel.
                        //channel.Attach(++m_lastChannelId, e.AcceptSocket);

                        //// save the channel for shutdown and reconnects.
                        //m_channels.Add(m_lastChannelId, channel);

                        //if (m_callback != null)
                        //{
                        //    channel.SetRequestReceivedCallback(new TcpChannelRequestEventHandler(OnRequestReceived));
                        //}
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace(ex, "Unexpected error accepting a new connection.");
                    }
                }

                e.Dispose();

                // go back and wait for the next connection.
                try
                {
                    e = new SocketAsyncEventArgs();
                    e.Completed += OnAccept;
                    e.UserToken = listeningSocket;
                    listeningSocket.AcceptAsync(e);
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
        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IAsyncResult result = null;

            try
            {
                if (m_callback == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotImplemented);
                }

                var istrm = await request.Content.ReadAsByteArrayAsync();
                
                IServiceRequest input = (IServiceRequest)BinaryDecoder.DecodeMessage(istrm, null, m_quotas.MessageContext);
               
                // extract the JWT token from the HTTP headers.
                if (input.RequestHeader == null) input.RequestHeader = new RequestHeader();

                if (NodeId.IsNull(input.RequestHeader.AuthenticationToken) && input.TypeId != DataTypeIds.CreateSessionRequest)
                {
                    if (request.Headers.Authorization != null && request.Headers.Authorization.Scheme == "Bearer")
                    {
                        input.RequestHeader.AuthenticationToken = new NodeId(request.Headers.Authorization.Parameter);
                    }
                }

                EndpointDescription endpoint = null;

                foreach (var ep in m_descriptions)
                {
                    if (ep.EndpointUrl.StartsWith(Utils.UriSchemeHttps))
                    {
                        endpoint = ep;
                        break;
                    }
                }

                result = m_callback.BeginProcessRequest(
                    m_listenerId,
                    endpoint,
                    input as IServiceRequest,
                    null,
                    null);

                var output = m_callback.EndProcessRequest(result);

                MemoryStream ostrm = new MemoryStream();

                BinaryEncoder.EncodeMessage(output, ostrm, m_quotas.MessageContext);
                
                ostrm.Position = 0;

                HttpResponseMessage response = new HttpResponseMessage();

                response.Content = new StreamContent(ostrm);
                response.Content.Headers.ContentType = request.Content.Headers.ContentType;

                return response;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "HTTPSLISTENER - Unexpected error processing request.");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { ReasonPhrase = e.Message };
            }
        }

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
        /// Handles requests arriving from a channel.
        /// </summary>
        private IAsyncResult BeginProcessRequest(Stream istrm, string action, string securityPolicyUri, object callbackData)
        {
            IAsyncResult result = null;

            try
            {
                if (m_callback != null)
                {
                    //string contentType = WebOperationContext.Current.IncomingRequest.ContentType;
                    //Uri uri = WebOperationContext.Current.IncomingRequest.UriTemplateMatch.RequestUri;

                    string scheme = /*uri.Scheme +*/ ":";

                    EndpointDescription endpoint = null;

                    for (int ii = 0; ii < m_descriptions.Count; ii++)
                    {
                        if (m_descriptions[ii].EndpointUrl.StartsWith(scheme))
                        {
                            if (endpoint == null)
                            {
                                endpoint = m_descriptions[ii];
                            }

                            if (m_descriptions[ii].SecurityPolicyUri == securityPolicyUri)
                            {
                                endpoint = m_descriptions[ii];
                                break;
                            }
                        }
                    }

                    IEncodeable request = BinaryDecoder.DecodeMessage(istrm, null, this.m_quotas.MessageContext);
                                       
                    result = m_callback.BeginProcessRequest(
                        m_listenerId,
                        endpoint,
                        request as IServiceRequest,
                        null,
                        callbackData);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "HTTPSLISTENER - Unexpected error processing request.");
            }

            return result;
        }

        private Stream EndProcessRequest(IAsyncResult result)
        {
            MemoryStream ostrm = new MemoryStream();

            try
            {
                if (m_callback != null)
                {
                    IServiceResponse response = m_callback.EndProcessRequest(result);

                    //if (WebOperationContext.Current.IncomingRequest.ContentType == "application/octet-stream")
                    {
                        BinaryEncoder encoder = new BinaryEncoder(ostrm, this.m_quotas.MessageContext);
                        encoder.EncodeMessage(response);
                    }

                    ostrm.Position = 0;
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "TCPLISTENER - Unexpected error sending result.");
            }

            return ostrm;
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
        private TcpChannelQuotas m_quotas;
        private ITransportListenerCallback m_callback;
        //private Task m_host;
        //private X509Certificate2 m_serverCertificate;
        //private uint m_lastChannelId;
        private Socket m_listeningSocket;
        private Socket m_listeningSocketIPv6;
        //private Dictionary<uint, TcpServerChannel> m_channels;
        #endregion
    }
}

