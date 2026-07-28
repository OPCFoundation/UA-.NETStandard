/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// How a QUIC client establishes a connection: the certificate it
    /// presents, how it validates the peer's, and how long it will wait.
    /// </summary>
    public sealed record QuicClientOptions
    {
        /// <summary>
        /// The certificate this peer presents when the other end requires
        /// TLS client authentication. Under reverse connect the OPC UA
        /// Server is the QUIC client, so this is its Application Instance
        /// Certificate.
        /// </summary>
        public X509Certificate2? ClientCertificate { get; init; }

        /// <summary>
        /// Validates the certificate the TLS server presented. A client
        /// validates it under RFC 5280 against the trust list it uses for
        /// Application Instance Certificates, and verifies that a
        /// subjectAltName covers the host of the EndpointUrl.
        /// </summary>
        /// <remarks>
        /// This is necessary but not sufficient. The key equality check
        /// of <see cref="QuicPeerBinding"/> is what actually binds the
        /// TLS peer to the OPC UA peer, and it runs after
        /// OpenSecureChannel when both other certificates are known.
        /// </remarks>
        public RemoteCertificateValidationCallback? ServerCertificateValidation { get; init; }

        /// <summary>
        /// How long the QUIC handshake may take.
        /// </summary>
        public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The most inbound bidirectional streams the peer may open. Each
        /// data channel bound to a bidirectional stream consumes one.
        /// </summary>
        public int MaxInboundBidirectionalStreams { get; init; } = 128;

        /// <summary>
        /// The most inbound unidirectional streams the peer may open. A
        /// SourceToSink data channel consumes one.
        /// </summary>
        public int MaxInboundUnidirectionalStreams { get; init; } = 128;

        /// <summary>
        /// The application error code used when a stream is aborted
        /// without a more specific StatusCode.
        /// </summary>
        public long DefaultStreamErrorCode { get; init; } = 0x0A;

        /// <summary>
        /// The application error code used when the connection is closed.
        /// </summary>
        public long DefaultCloseErrorCode { get; init; } = 0x0B;
    }

    /// <summary>
    /// Creates unconnected QUIC transports for client side channels.
    /// </summary>
    public sealed class QuicByteTransportFactory : IUaSCByteTransportFactory
    {
        /// <summary>
        /// Creates a factory.
        /// </summary>
        /// <param name="telemetry">The default telemetry context.</param>
        /// <param name="options">How connections are established.</param>
        public QuicByteTransportFactory(
            ITelemetryContext telemetry,
            QuicClientOptions? options = null)
        {
            m_telemetry = telemetry;
            m_options = options ?? new QuicClientOptions();
        }

        /// <inheritdoc/>
        public string Implementation => QuicMultiplexedTransport.ImplementationName;

        /// <inheritdoc/>
        public IUaSCByteTransport Create(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            return new QuicMultiplexedTransport(
                bufferManager,
                receiveBufferSize,
                telemetry ?? m_telemetry,
                m_options);
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly QuicClientOptions m_options;
    }

    /// <summary>
    /// Establishes and configures QUIC connections.
    /// </summary>
    internal static class QuicConnectionBuilder
    {
        /// <summary>
        /// Connects to a peer, offering and then requiring the OPC UA
        /// ALPN identifier.
        /// </summary>
        /// <param name="url">The endpoint url.</param>
        /// <param name="options">How to connect.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async ValueTask<QuicConnection> ConnectAsync(
            Uri url,
            QuicClientOptions options,
            CancellationToken ct)
        {
            if (!QuicConnection.IsSupported)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "QUIC is unavailable on this platform.");
            }

            int port = url.Port > 0 ? url.Port : DataChannelConstants.QuicDefaultPort;

            var authentication = new SslClientAuthenticationOptions
            {
                ApplicationProtocols = [QuicTransport.ApplicationProtocol],
                TargetHost = url.DnsSafeHost,
                RemoteCertificateValidationCallback = options.ServerCertificateValidation
            };

            if (options.ClientCertificate != null)
            {
                authentication.ClientCertificates = [options.ClientCertificate];
            }

            var connectionOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = new DnsEndPoint(url.DnsSafeHost, port),
                ClientAuthenticationOptions = authentication,
                DefaultStreamErrorCode = options.DefaultStreamErrorCode,
                DefaultCloseErrorCode = options.DefaultCloseErrorCode,
                MaxInboundBidirectionalStreams = options.MaxInboundBidirectionalStreams,
                MaxInboundUnidirectionalStreams = options.MaxInboundUnidirectionalStreams
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.HandshakeTimeout);

            QuicConnection connection;

            try
            {
                connection = await QuicConnection
                    .ConnectAsync(connectionOptions, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (QuicException e)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotConnected,
                    e,
                    "The QUIC connection to {0} failed.",
                    url);
            }

            // A client shall abandon the connection if the server does not
            // select the OPC UA identifier, so a QUIC endpoint serving
            // another protocol on the same port is never mistaken for an
            // OPC UA Server (Part 6 errata 7.2).
            if (connection.NegotiatedApplicationProtocol != QuicTransport.ApplicationProtocol)
            {
                await connection.DisposeAsync().ConfigureAwait(false);

                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The peer selected ALPN '{0}' rather than '{1}'.",
                    connection.NegotiatedApplicationProtocol.ToString(),
                    DataChannelConstants.QuicAlpnProtocol);
            }

            return connection;
        }
    }
}
