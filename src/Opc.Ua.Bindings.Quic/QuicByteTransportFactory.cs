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
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

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
        /// The endpoint selected by the OPC UA client. QUIC uses it to bind
        /// the TLS peer to the OPC UA peer after OpenSecureChannel.
        /// </summary>
        internal EndpointDescription? EndpointDescription { get; init; }

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
            QuicClientOptions options = m_options with
            {
                EndpointDescription = m_endpointDescription,
                ClientCertificate = m_options.ClientCertificate ?? m_clientCertificate
            };

#pragma warning disable CA2000 // QuicPeerBindingTransport owns and closes the inner transport.
            var transport = new QuicMultiplexedTransport(
                bufferManager,
                receiveBufferSize,
                telemetry ?? m_telemetry,
                options);
#pragma warning restore CA2000

            return new QuicPeerBindingTransport(
                transport,
                bufferManager,
                m_endpointDescription);
        }

        /// <summary>
        /// Supplies the selected endpoint before the byte transport connects.
        /// </summary>
        /// <param name="endpointDescription">The selected endpoint.</param>
        internal void SetEndpointDescription(EndpointDescription? endpointDescription)
        {
            m_endpointDescription = endpointDescription;
        }

        /// <summary>
        /// Supplies the OPC UA Application Instance Certificate to present
        /// as the TLS client certificate when the listener requests mutual
        /// authentication.
        /// </summary>
        /// <param name="clientCertificate">The selected client certificate.</param>
        internal void SetClientCertificate(Certificate? clientCertificate)
        {
            X509Certificate2? previous = m_clientCertificate;
            m_clientCertificate = clientCertificate?.AsX509Certificate2();
            previous?.Dispose();
        }

        /// <summary>
        /// Releases the cached TLS client certificate.
        /// </summary>
        internal void DisposeClientCertificate()
        {
            X509Certificate2? previous = m_clientCertificate;
            m_clientCertificate = null;
            previous?.Dispose();
        }

        private readonly ITelemetryContext m_telemetry;
        private readonly QuicClientOptions m_options;
        private EndpointDescription? m_endpointDescription;
        private X509Certificate2? m_clientCertificate;
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
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                    ValidateServerCertificate(
                        url,
                        options,
                        sender,
                        certificate,
                        chain,
                        sslPolicyErrors)
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
            catch (AuthenticationException e)
            {
                // A rejected ALPN or certificate surfaces from the TLS
                // handshake rather than from QUIC, and shall still reach the
                // caller as a StatusCode rather than a raw platform
                // exception.
                throw ServiceResultException.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    e,
                    "The QUIC handshake with {0} failed its security checks.",
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

        private static bool ValidateServerCertificate(
            Uri url,
            QuicClientOptions options,
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (certificate == null)
            {
                return false;
            }

            bool callbackAccepted = options.ServerCertificateValidation != null
                ? options.ServerCertificateValidation(sender, certificate, chain, sslPolicyErrors)
                : sslPolicyErrors == SslPolicyErrors.None;

            if (!callbackAccepted)
            {
                return false;
            }

            X509Certificate2? temporaryCertificate = null;
            X509Certificate2 certificate2 = certificate as X509Certificate2 ??
                (temporaryCertificate = new X509Certificate2(certificate));
            try
            {
                string host = GetEndpointHost(options.EndpointDescription) ?? url.DnsSafeHost;
                return SubjectAltNameCoversHost(certificate2, host);
            }
            finally
            {
                temporaryCertificate?.Dispose();
            }
        }

        private static string? GetEndpointHost(EndpointDescription? endpoint)
        {
            if (endpoint?.EndpointUrl == null ||
                !Uri.TryCreate(endpoint.EndpointUrl, UriKind.Absolute, out Uri? endpointUri))
            {
                return null;
            }

            return endpointUri.DnsSafeHost;
        }

        private static bool SubjectAltNameCoversHost(
            X509Certificate2 certificate,
            string host)
        {
            X509SubjectAltNameExtension? subjectAltName = FindSubjectAltName(certificate);
            if (subjectAltName == null)
            {
                return false;
            }

            if (IPAddress.TryParse(host, out IPAddress? address))
            {
                foreach (string ipAddress in subjectAltName.IPAddresses)
                {
                    if (IPAddress.TryParse(ipAddress, out IPAddress? candidate) &&
                        candidate.Equals(address))
                    {
                        return true;
                    }
                }

                return false;
            }

            string normalizedHost = NormalizeDnsName(host);
            foreach (string dnsName in subjectAltName.DomainNames)
            {
                if (DnsNameMatches(NormalizeDnsName(dnsName), normalizedHost))
                {
                    return true;
                }
            }

            return false;
        }

        private static X509SubjectAltNameExtension? FindSubjectAltName(
            X509Certificate2 certificate)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value is X509SubjectAltNameExtension.SubjectAltNameOid
                    or X509SubjectAltNameExtension.SubjectAltName2Oid)
                {
                    return new X509SubjectAltNameExtension(extension, extension.Critical);
                }
            }

            return null;
        }

        private static string NormalizeDnsName(string value)
        {
            return value.TrimEnd('.').ToLowerInvariant();
        }

        private static bool DnsNameMatches(string pattern, string host)
        {
            if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            const string wildcard = "*.";
            if (!pattern.StartsWith(wildcard, StringComparison.Ordinal) ||
                host.Length <= pattern.Length - wildcard.Length)
            {
                return false;
            }

            string suffix = pattern[wildcard.Length..];
            return host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase) &&
                host[..^(suffix.Length + 1)].IndexOf('.', StringComparison.Ordinal) < 0;
        }
    }

    internal sealed class QuicPeerBindingTransport :
        IUaSCByteTransport,
        IMultiplexedByteTransport,
        IUaSCSecureChannelBoundTransport
    {
        public QuicPeerBindingTransport(
            QuicMultiplexedTransport inner,
            BufferManager bufferManager,
            EndpointDescription? endpointDescription,
            bool bindToOpenSecureChannelOnly = false)
        {
            m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_endpointDescription = endpointDescription;
            m_bindToOpenSecureChannelOnly = bindToOpenSecureChannelOnly;
        }

        /// <summary>
        /// The connection this wrapper secures. Exposed so an application
        /// can reach the QUIC connection to bind a data channel to the
        /// stream the Server named in <c>revisedTransportChannelId</c>,
        /// without having to know that the peer binding wraps it.
        /// </summary>
        public QuicMultiplexedTransport Inner => m_inner;

        public EndPoint? LocalEndpoint => m_inner.LocalEndpoint;
        public EndPoint? RemoteEndpoint => m_inner.RemoteEndpoint;

        public TransportChannelFeatures Features => m_inner.Features;

        public string Implementation => m_inner.Implementation;

        public bool SupportsDatagrams => m_inner.SupportsDatagrams;

        public int MaxDatagramSize => m_inner.MaxDatagramSize;

        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            return m_inner.ConnectAsync(url, ct);
        }

        public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            return m_inner.SendChunkAsync(chunk, ct);
        }

        public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            return m_inner.SendChunkAsync(buffers, ct);
        }

        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            ArraySegment<byte> chunk = await m_inner.ReceiveChunkAsync(ct).ConfigureAwait(false);

            try
            {
                VerifyOpenSecureChannelBinding(chunk);
                return chunk;
            }
            catch
            {
                if (chunk.Array != null)
                {
                    m_bufferManager.ReturnBuffer(chunk.Array, nameof(ReceiveChunkAsync));
                }

                Close();
                throw;
            }
        }

        public ValueTask<ulong> OpenStreamAsync(bool bidirectional, CancellationToken ct)
        {
            return m_inner.OpenStreamAsync(bidirectional, ct);
        }

        public ValueTask<ulong> AcceptStreamAsync(CancellationToken ct)
        {
            return m_inner.AcceptStreamAsync(ct);
        }

        public ValueTask SendOnStreamAsync(
            ulong streamId,
            ReadOnlyMemory<byte> frame,
            CancellationToken ct)
        {
            return m_inner.SendOnStreamAsync(streamId, frame, ct);
        }

        public ValueTask SendDatagramAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            return m_inner.SendDatagramAsync(frame, ct);
        }

        public ValueTask<ArraySegment<byte>> ReceiveOnStreamAsync(
            ulong streamId,
            CancellationToken ct)
        {
            return m_inner.ReceiveOnStreamAsync(streamId, ct);
        }

        public ValueTask<ArraySegment<byte>> ReceiveDatagramAsync(CancellationToken ct)
        {
            return m_inner.ReceiveDatagramAsync(ct);
        }

        public void AbortStream(ulong streamId, uint errorCode)
        {
            m_inner.AbortStream(streamId, errorCode);
        }

        public void CloseStream(ulong streamId)
        {
            m_inner.CloseStream(streamId);
        }

        public void Close()
        {
            m_inner.Close();
        }

        /// <summary>
        /// Forwards the SecureChannel identifier to the wrapped transport.
        /// </summary>
        /// <remarks>
        /// The wrapper is what the channel holds, so every optional
        /// interface the inner transport relies on has to be forwarded
        /// through it. Omitting this one is not a small loss: the QUIC
        /// transport registers itself against the SecureChannel identifier
        /// here, and without that registration OpenDataChannel cannot find
        /// the connection and silently falls back to a Service-only
        /// transport.
        /// </remarks>
        /// <param name="secureChannelId">The SecureChannel identifier.</param>
        public void OnSecureChannelAttached(string secureChannelId)
        {
            m_inner.OnSecureChannelAttached(secureChannelId);
        }

        private void VerifyOpenSecureChannelBinding(ArraySegment<byte> chunk)
        {
            if (m_bindingVerified ||
                chunk.Array == null)
            {
                return;
            }

            if (!m_bindToOpenSecureChannelOnly &&
                (m_endpointDescription == null ||
                 m_endpointDescription.SecurityMode == MessageSecurityMode.None))
            {
                return;
            }

            ReadOnlySpan<byte> data = chunk.AsSpan();
            if (data.Length < 12 ||
                data[0] != (byte)'O' ||
                data[1] != (byte)'P' ||
                data[2] != (byte)'N')
            {
                return;
            }

            m_bindingVerified = true;
            byte[] secureChannelCertificate = ReadSenderCertificate(data);

            if (m_bindToOpenSecureChannelOnly &&
                m_inner.PeerCertificate == null &&
                secureChannelCertificate.Length == 0)
            {
                // Neither layer named a certificate, which is what a
                // SecurityPolicy None channel looks like: the Discovery
                // Services run on one, and it is reachable by design. There
                // is nothing to compare, and refusing here would make
                // GetEndpoints unreachable over opc.quic. Such a connection
                // still cannot carry data channels - §7.6.1 puts that
                // refusal on OpenDataChannel, where the SecureChannel is
                // known, rather than on the transport.
                return;
            }

            QuicPeerBindingResult result = m_bindToOpenSecureChannelOnly
                ? QuicPeerBinding.Verify(m_inner.PeerCertificate, secureChannelCertificate)
                : QuicPeerBinding.Verify(
                    m_inner.PeerCertificate,
                    m_endpointDescription!.ServerCertificate.ToArray(),
                    secureChannelCertificate);

            if (result != QuicPeerBindingResult.Bound)
            {
                throw ServiceResultException.Create(
                    QuicPeerBinding.ToStatusCode(result),
                    "The QUIC TLS peer is not bound to the OPC UA peer: {0}.",
                    result);
            }
        }

        private static byte[] ReadSenderCertificate(ReadOnlySpan<byte> chunk)
        {
            int offset = 12;
            SkipUaString(chunk, ref offset);
            return ReadUaByteString(chunk, ref offset);
        }

        private static void SkipUaString(ReadOnlySpan<byte> data, ref int offset)
        {
            int length = ReadUaLength(data, ref offset);
            if (length > 0)
            {
                EnsureAvailable(data, offset, length);
                offset += length;
            }
        }

        private static byte[] ReadUaByteString(ReadOnlySpan<byte> data, ref int offset)
        {
            int length = ReadUaLength(data, ref offset);
            if (length <= 0)
            {
                return [];
            }

            EnsureAvailable(data, offset, length);
            byte[] value = data.Slice(offset, length).ToArray();
            offset += length;
            return value;
        }

        private static int ReadUaLength(ReadOnlySpan<byte> data, ref int offset)
        {
            EnsureAvailable(data, offset, sizeof(int));
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, sizeof(int)));
            offset += sizeof(int);

            if (length < -1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "The OpenSecureChannel asymmetric header is malformed.");
            }

            return length;
        }

        private static void EnsureAvailable(ReadOnlySpan<byte> data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "The OpenSecureChannel asymmetric header is truncated.");
            }
        }

        private readonly QuicMultiplexedTransport m_inner;
        private readonly BufferManager m_bufferManager;
        private readonly EndpointDescription? m_endpointDescription;
        private readonly bool m_bindToOpenSecureChannelOnly;
        private bool m_bindingVerified;
    }
}
