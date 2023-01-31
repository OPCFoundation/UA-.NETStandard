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
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace Opc.Ua.Bindings
{
    /// <remarks/>
    public sealed class WebSocketListener : IDisposable
    {
        private bool m_disposed;
        private X509Certificate2 m_certificate;
        private X509Certificate2 m_tlsCertificate;
        private TcpListener m_listener;
        private BufferManager m_bufferManager;
        private ICertificateValidator m_certificateValidator;
        private CancellationTokenSource m_cts = new CancellationTokenSource();

        internal ICertificateValidator CertificateValidator
        {
            get { return m_certificateValidator; }
            set { m_certificateValidator = value; }
        }

        /// <remarks/>
        public EventHandler<ReceiveMessageEventArgs> ReceiveMessage;
        /// <remarks/>
        public EventHandler<ConnectionStateEventArgs> ConnectionOpened;
        /// <remarks/>
        public EventHandler<ConnectionStateEventArgs> ConnectionClosed;

        /// <remarks/>
        public WebSocketListener(Uri endpointUrl, TransportListenerSettings settings)
        {
            m_disposed = false;
            m_certificate = settings.ServerCertificate;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                m_tlsCertificate = m_certificate;
            }
            else
            {
                m_tlsCertificate = FindBestTlsCertificate(this.GetType().Name, endpointUrl, m_certificate);
            }

            m_bufferManager = new BufferManager("WebSocketListener", (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);
            m_certificateValidator = settings.CertificateValidator;
        }

        /// <remarks/>
        public WebSocketListener(TransportChannelSettings settings)
        {
            m_disposed = false;
            m_certificate = settings.ClientCertificate;
            m_bufferManager = new BufferManager("WebSocketListener", (int)Int32.MaxValue, settings.Configuration.MaxBufferSize);
            m_certificateValidator = settings.CertificateValidator;
        }

        #region IDisposable Support
        void CloseAll()
        {
            if (m_listener != null)
            {
                m_listener.Stop();
                m_listener = null;
            }
        }

        void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                {
                    CloseAll();
                }

                m_disposed = true;
            }
        }

        /// <remarks/>
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

        /// <remarks/>
        public async Task ListenAsync(IPAddress address, int port)
        {
            m_listener = new TcpListener(IPAddress.IPv6Any, port);
            m_listener.Server.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, false);
            m_listener.Start();

            do
            {
                var client = await m_listener.AcceptTcpClientAsync().ConfigureAwait(false);

                try
                {
                    await AcceptConnection(client, m_cts.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "[WebSocketListener.ListenAsync] Could not accept new connection.");
                }
            }
            while (!m_cts.IsCancellationRequested);
        }

        private class SslStreamAuthenticator
        {
            public WebSocketListener Listener;

            public async Task<Stream> AuthenticateAsClient(TcpClient client, string targetHost, TlsProtocol protocol)
            {
                SslProtocols sslProtocol = SslProtocols.Tls12;

                if (protocol == TlsProtocol.Tls12)
                {
                    sslProtocol = SslProtocols.Tls12;
                }

                SslStream stream = new SslStream(
                    client.GetStream(),
                    false,
                    ValidateCertificate,
                    null);

                await stream.AuthenticateAsClientAsync(
                    targetHost,
                    new X509CertificateCollection(),
                    sslProtocol,
                    true).ConfigureAwait(false);

                return stream;
            }

            public Stream AuthenticateAsServer(TcpClient client, X509Certificate2 certificate)
            {
                SslStream stream = new SslStream(client.GetStream(), false);
                stream.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, true);
                return stream;
            }

            private bool ValidateCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
            {
                return Listener.ValidateCertificate(this, certificate, chain, sslPolicyErrors);
            }
        }

        private bool ValidateCertificate(SslStreamAuthenticator sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;

            /*if (m_cts.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                ((CertificateValidator)m_certificateValidator).Validate((X509Certificate2)certificate);
                return true;
            }
            catch (Exception e)
            {
                Utils.Trace(e, "[SslStreamAuthenticator.ValidateCertificate] SSL Certficate was not accepted. {0} {1}", sslPolicyErrors, certificate.Subject);
                return false;
            }*/
        }

        /// <remarks/>
        public async Task<WebSocketConnection> ConnectAsync(
            string targetHost,
            IPAddress address,
            int port,
            TlsProtocol protocol,
            CancellationToken cancellationToken)
        {
            var client = new TcpClient(AddressFamily.InterNetworkV6);
            client.Client.DualMode = true;
            client.NoDelay = true;
            await client.ConnectAsync(targetHost, port).ConfigureAwait(false);

            Stream stream = client.GetStream();

            if (protocol != TlsProtocol.None)
            {
                var authenticator = new SslStreamAuthenticator() { Listener = this };
                var tlsstream = await authenticator.AuthenticateAsClient(client, targetHost, protocol).ConfigureAwait(false);
                stream = tlsstream;
            }

            var connection = new WebSocketConnection(client, stream, m_bufferManager, false);
            await connection.ConnectAsync();

            var callback = ConnectionOpened;

            if (callback != null)
            {
                try
                {
                    callback(this, new ConnectionStateEventArgs(connection, ServiceResult.Good));
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "[WebSocketListener.ConnectAsync] Error raising ConnectionOpened event.");
                }
            }

            var task = Task.Run(() => MonitorConnection(connection));

            return connection;
        }

        /// <remarks/>
        public Task CloseAsync()
        {
            m_cts.Cancel();
            CloseAll();
            return Task.FromResult<object>(null);
        }

        private async Task MonitorConnection(WebSocketConnection connection)
        {
            do
            {
                ArraySegment<byte> message = new ArraySegment<byte>();

                try
                {
                    message = await connection.ReceiveMessage().ConfigureAwait(false);
                    
                    var callback = ReceiveMessage;

                    if (callback != null)
                    {
                        try
                        {
                            callback(this, new ReceiveMessageEventArgs(connection, message));
                        }
                        catch (Exception e)
                        {
                            Utils.Trace(e, "[WebSocketListener.MonitorConnection] Error raising ReceiveMessage event.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    m_bufferManager.ReturnBuffer(message.Array, "WebScoketListener.MonitorConnection");

                    var callback = ConnectionClosed;

                    if (callback != null)
                    {
                        uint statusCode = StatusCodes.BadUnexpectedError;

                        if (exception is ObjectDisposedException)
                        {
                            statusCode = StatusCodes.GoodNoData;
                        }

                        try
                        {
                            callback(this, new ConnectionStateEventArgs(connection, new ServiceResult(exception, statusCode)));
                        }
                        catch (Exception e)
                        {
                            Utils.Trace(e, "[WebSocketListener.MonitorConnection] Error raising ConnectionClosed event.");
                        }
                    }

                    connection.Dispose();
                    break;
                }
            }
            while (!m_cts.IsCancellationRequested);
        }

        private Task AcceptConnection(TcpClient client, CancellationToken cancellationToken)
        {
            client.NoDelay = true;

            var stream = client.GetStream();
            WebSocketConnection connection = new WebSocketConnection(client, stream, m_bufferManager, true);



            var authenticator = new SslStreamAuthenticator() { Listener = this };
            var tlsstream = authenticator.AuthenticateAsServer(connection.TcpClient, m_tlsCertificate);
            connection.Upgrade(tlsstream);

            var callback = ConnectionOpened;

            if (callback != null)
            {
                try
                {
                    callback(this, new ConnectionStateEventArgs(connection, ServiceResult.Good));
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "[WebSocketListener.MonitorConnection] Error raising ConnectionOpened event.");
                }
            }

            Task.Run(() => MonitorConnection(connection), cancellationToken);

            return Task.FromResult<object>(null);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="url"></param>
        /// <param name="tlsCertificate"></param>
        /// <returns></returns>
        public static X509Certificate2 FindBestTlsCertificate(string context, Uri url, X509Certificate2 tlsCertificate)
        {
            // use a certificate that web browser clients would accept if one is available.
            var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            foreach (var certificate in store.Certificates)
            {
                if (!certificate.HasPrivateKey)
                {
                    continue;
                }

                bool match = certificate.Subject.Contains(url.DnsSafeHost);

                if (!match)
                {
                    var domains = X509Utils.GetDomainsFromCertficate(certificate);

                    if (domains != null)
                    {
                        foreach (var domain in domains)
                        {
                            Utils.Trace(Utils.TraceMasks.Error, $"[{context}] Found SSL Domain ({domain}). {certificate.Subject}");

                            if (String.Compare(domain, url.DnsSafeHost, true) == 0)
                            {
                                match = true;
                                break;
                            }
                        }
                    }
                }

                Utils.Trace(Utils.TraceMasks.Error, $"[{context}] Found SSL Certficate ({match}). {certificate.Subject} {certificate.Thumbprint}");

                if (match)
                {
                    try
                    {
                        // verify private key is accessible.
                        if (certificate.PrivateKey.KeySize >= 1024)
                        {
                            tlsCertificate = certificate;
                            Utils.Trace(Utils.TraceMasks.Error, $"[{context}] SSL Certficate Selected ({url.DnsSafeHost})");
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // ignore bad certificates.
                    }
                }
            }

            store.Close();

            return tlsCertificate;
        }
    }

    /// <remarks/>
    public sealed class ReceiveMessageEventArgs : EventArgs
    {
        /// <remarks/>
        public ReceiveMessageEventArgs(WebSocketConnection connection, ArraySegment<byte> message)
        {
            Connection = connection;
            Message = message;
        }

        /// <remarks/>
        public WebSocketConnection Connection { get; private set; }

        /// <remarks/>
        public ArraySegment<byte> Message { get; private set; }
    }

    /// <remarks/>
    public sealed class ConnectionStateEventArgs : EventArgs
    {
        /// <remarks/>
        public ConnectionStateEventArgs(WebSocketConnection connection, ServiceResult error)
        {
            Connection = connection;
            Error = error;
        }

        /// <remarks/>
        public WebSocketConnection Connection { get; private set; }

        /// <remarks/>
        public ServiceResult Error { get; private set; }
    }

    /// <remarks/>
    public enum TlsProtocol
    {
        /// <remarks/>
        None,
        /// <remarks/>
        TlsBestAvailable,
        /// <remarks/>
        Tls12
    }
}
