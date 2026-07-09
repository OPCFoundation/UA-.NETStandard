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
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Common base for the client + server WebSocket implementations of
    /// <see cref="IUaSCByteTransport"/>. Each UASC <c>MessageChunk</c>
    /// (Part 6 §6.7.2) is mapped to exactly one WebSocket binary frame
    /// (<c>EndOfMessage = true</c>) per Part 6 §7.5.2 (opcua+uacp).
    /// </summary>
    internal abstract class WebSocketByteTransportBase : IUaSCByteTransport, IDisposable
    {
        protected WebSocketByteTransportBase(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
        {
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            m_receiveBufferSize = receiveBufferSize;
            m_logger = telemetry.CreateLogger<WebSocketByteTransportBase>();
            m_sendLock = new SemaphoreSlim(1, 1);
        }

        /// <inheritdoc/>
        public string Implementation => "UA-WSS";

        /// <inheritdoc/>
        public TransportChannelFeatures Features => TransportChannelFeatures.Reconnect;

        /// <inheritdoc/>
        public abstract EndPoint? LocalEndpoint { get; }

        /// <inheritdoc/>
        public abstract EndPoint? RemoteEndpoint { get; }

        /// <inheritdoc/>
        public abstract ValueTask ConnectAsync(Uri url, CancellationToken ct);

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            WebSocket socket = RequireOpenSocket();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                await socket
                    .SendAsync(chunk, WebSocketMessageType.Binary, endOfMessage: true, ct)
                    .ConfigureAwait(false);
#else
                ArraySegment<byte> segment;
                if (MemoryMarshal.TryGetArray(chunk, out ArraySegment<byte> seg) && seg.Array != null)
                {
                    segment = seg;
                }
                else
                {
                    byte[] tmp = chunk.ToArray();
                    segment = new ArraySegment<byte>(tmp, 0, tmp.Length);
                }
                await socket
                    .SendAsync(segment, WebSocketMessageType.Binary, endOfMessage: true, ct)
                    .ConfigureAwait(false);
#endif
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            WebSocket socket = RequireOpenSocket();
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // WebSocket does not support a vectored send out of the box; concat the
                // gathered segments into a single contiguous chunk for the frame.
                int totalSize = buffers.TotalSize;
                byte[] frame = m_bufferManager.TakeBuffer(totalSize, nameof(SendChunkAsync));
                try
                {
                    int offset = 0;
                    foreach (ArraySegment<byte> segment in buffers)
                    {
                        if (segment.Array == null)
                        {
                            continue;
                        }
                        Buffer.BlockCopy(segment.Array, segment.Offset, frame, offset, segment.Count);
                        offset += segment.Count;
                    }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await socket
                        .SendAsync(
                            new ReadOnlyMemory<byte>(frame, 0, totalSize),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            ct)
                        .ConfigureAwait(false);
#else
                    await socket
                        .SendAsync(
                            new ArraySegment<byte>(frame, 0, totalSize),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            ct)
                        .ConfigureAwait(false);
#endif
                }
                finally
                {
                    m_bufferManager.ReturnBuffer(frame, nameof(SendChunkAsync));
                }
            }
            finally
            {
                m_sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            WebSocket socket = RequireOpenSocket();
            byte[] buffer = m_bufferManager.TakeBuffer(m_receiveBufferSize, nameof(ReceiveChunkAsync));
            int totalRead = 0;
            try
            {
                while (true)
                {
                    ValueWebSocketReceiveResult result;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    System.Net.WebSockets.ValueWebSocketReceiveResult r = await socket
                        .ReceiveAsync(
                            new Memory<byte>(buffer, totalRead, buffer.Length - totalRead),
                            ct)
                        .ConfigureAwait(false);
                    result = new ValueWebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
#else
                    WebSocketReceiveResult r = await socket
                        .ReceiveAsync(
                            new ArraySegment<byte>(buffer, totalRead, buffer.Length - totalRead),
                            ct)
                        .ConfigureAwait(false);
                    result = new ValueWebSocketReceiveResult(r.Count, r.MessageType, r.EndOfMessage);
#endif

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            "WebSocket peer closed the connection.");
                    }
                    if (result.MessageType != WebSocketMessageType.Binary)
                    {
                        // opcua+uacp requires binary frames (Part 6 §7.5.2 Table 81).
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTypeInvalid,
                            "Expected binary WebSocket frame for opcua+uacp; got {0}.",
                            result.MessageType);
                    }

                    totalRead += result.Count;
                    if (totalRead > m_receiveBufferSize)
                    {
                        // Map to OPC UA error and tear down per Part 6 §7.5.2 (1009 too-big).
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "WebSocket frame exceeds the negotiated max message size ({0} bytes).",
                            m_receiveBufferSize);
                    }
                    if (result.EndOfMessage)
                    {
                        var segment = new ArraySegment<byte>(buffer, 0, totalRead);
                        buffer = null!; // ownership transferred to the caller
                        return segment;
                    }

                    // Not end-of-message: guard against a peer that streams
                    // zero-length or buffer-filling continuation frames without
                    // ever terminating the UASC chunk. Without this the next
                    // ReceiveAsync would get a zero-length destination and spin
                    // the loop (CPU DoS), because the size check above uses '>'.
                    if (result.Count == 0 || totalRead >= m_receiveBufferSize)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadTcpMessageTooLarge,
                            "WebSocket continuation frame made no progress or exceeds the " +
                            "negotiated max message size ({0} bytes).",
                            m_receiveBufferSize);
                    }
                }
            }
            catch
            {
                if (buffer != null)
                {
                    m_bufferManager.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                }
                throw;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closed, 1) != 0)
            {
                return;
            }
            WebSocket? socket = Interlocked.Exchange(ref m_socket, null);
            if (socket != null)
            {
                try
                {
                    socket.Abort();
                }
                catch
                {
                    // Best-effort.
                }
                socket.Dispose();
            }
            m_sendLock.Dispose();
        }

        /// <summary>
        /// Releases the underlying WebSocket and synchronization primitives.
        /// Equivalent to <see cref="Close"/>.
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        /// <summary>
        /// Assigns the live <see cref="WebSocket"/> to the transport. Called
        /// by derived implementations from their connect / accept paths.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="socket"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        protected void AttachSocket(WebSocket socket)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            if (Interlocked.CompareExchange(ref m_socket, socket, null) != null)
            {
                throw new InvalidOperationException("WebSocket transport is already attached.");
            }
        }

        private WebSocket RequireOpenSocket()
        {
            WebSocket? socket = m_socket;
            if (socket == null || m_closed != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The WebSocket transport is not connected.");
            }
            if (socket.State != WebSocketState.Open)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The WebSocket is in state {0}.",
                    socket.State);
            }
            return socket;
        }

        /// <summary>
        /// m_logger is used by derived implementations for handshake diagnostics.
        /// </summary>
        protected readonly ILogger m_logger;
        private WebSocket? m_socket;
        private int m_closed;
        private readonly BufferManager m_bufferManager;
        private readonly int m_receiveBufferSize;
        private readonly SemaphoreSlim m_sendLock;
    }

    /// <summary>
    /// Local copy of <c>System.Net.WebSockets.ValueWebSocketReceiveResult</c> for
    /// platforms (net472 / net48) where the type is not available.
    /// </summary>
    internal readonly struct ValueWebSocketReceiveResult
    {
        public ValueWebSocketReceiveResult(int count, WebSocketMessageType messageType, bool endOfMessage)
        {
            Count = count;
            MessageType = messageType;
            EndOfMessage = endOfMessage;
        }

        public int Count { get; }
        public WebSocketMessageType MessageType { get; }
        public bool EndOfMessage { get; }
    }

    /// <summary>
    /// Client-side <see cref="IUaSCByteTransport"/> backed by
    /// <see cref="ClientWebSocket"/>. Negotiates the
    /// <c>opcua+uacp</c> sub-protocol on the WebSocket upgrade per
    /// OPC UA Part 6 §7.5.2.
    /// </summary>
    internal sealed class WebSocketClientByteTransport : WebSocketByteTransportBase
    {
        public WebSocketClientByteTransport(
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
            : base(bufferManager, receiveBufferSize, telemetry)
        {
        }

        /// <summary>
        /// Optional OPC UA certificate validator invoked by the
        /// <c>RemoteCertificateValidationCallback</c> hook installed on
        /// <see cref="ClientWebSocket"/>. When <c>null</c> the .NET default
        /// TLS validation (system root trust) is used.
        /// </summary>
        public ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// Optional client TLS certificate added to
        /// <c>ClientWebSocketOptions.ClientCertificates</c> for servers
        /// that require mutual TLS authentication.
        /// </summary>
        public Certificate? ClientTlsCertificate { get; set; }

        /// <inheritdoc/>
        public override EndPoint? LocalEndpoint => null;

        /// <inheritdoc/>
        public override EndPoint? RemoteEndpoint => m_remoteEndpoint;

        /// <inheritdoc/>
        public override async ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            // ClientWebSocket expects wss://host:port/path; opc.wss:// is the UA
            // scheme alias and must be normalized before handing off.
            Uri wsUrl = NormalizeUrl(url);

            var ws = new ClientWebSocket();
            try
            {
                ws.Options.AddSubProtocol(Profiles.OpcUaWsSubProtocolUacp);

#if NET5_0_OR_GREATER
                if (CertificateValidator != null)
                {
                    ICertificateValidatorEx validator = CertificateValidator;
                    ws.Options.RemoteCertificateValidationCallback =
                        (sender, cert, chain, errors) => ValidateRemoteCertificate(
                            validator,
                            cert as X509Certificate2,
                            chain);
                }
                if (ClientTlsCertificate != null)
                {
                    using X509Certificate2 clientCert = ClientTlsCertificate.AsX509Certificate2();
                    ws.Options.ClientCertificates ??= [];
                    ws.Options.ClientCertificates.Add(clientCert);
                }
#else
                // net472 / net48 / netstandard2.1: ClientWebSocketOptions does not
                // expose RemoteCertificateValidationCallback or per-connection client
                // certificates, so the configured OPC UA certificate validator cannot
                // be attached at the TLS layer here. TLS server validation falls back
                // to the OS default (chain + hostname), which fails closed for the
                // self-signed / private-CA certificates typical of OPC UA. For binary
                // opcua+uacp the UASC OpenSecureChannel exchange still authenticates
                // the peer; for the TLS-only opcua+uajson sub-protocol prefer net8.0+
                // where the UA validator is honored.
#endif

                await ws.ConnectAsync(wsUrl, ct).ConfigureAwait(false);

                if (!string.Equals(
                        ws.SubProtocol,
                        Profiles.OpcUaWsSubProtocolUacp,
                        StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNotConnected,
                        "Server did not select the opcua+uacp WebSocket sub-protocol (got '{0}').",
                        ws.SubProtocol ?? "<none>");
                }

                AttachSocket(ws);
                m_remoteEndpoint = new DnsEndPoint(
                    wsUrl.IdnHost,
                    wsUrl.Port > 0 ? wsUrl.Port : Utils.UaWebSocketsDefaultPort);
                ws = null; // ownership transferred
            }
            catch (Exception ex)
            {
                m_logger.LogDebug(ex, "WebSocket connect to {Url} failed.", wsUrl);
                throw;
            }
            finally
            {
                ws?.Dispose();
            }
        }

        private bool ValidateRemoteCertificate(
            ICertificateValidatorEx validator,
            X509Certificate2? cert,
            X509Chain? chain)
        {
            if (cert == null)
            {
                return false;
            }
            try
            {
                var collection = new X509Certificate2Collection();
                if (chain != null && chain.ChainElements != null)
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        collection.Add(element.Certificate);
                    }
                }
                else
                {
                    collection.Add(cert);
                }
                using var validation = CertificateCollection.From(collection);
                // Run the async validator from the sync TLS callback; the
                // BCL TLS handshake plumbing is itself synchronous here so
                // there is no easier way to bridge it.
#pragma warning disable CA2025
                CertificateValidationResult result = validator
                    .ValidateAsync(validation, ct: default)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore CA2025
                return result.IsValid;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "WebSocketClientByteTransport: failed to validate server TLS certificate.");
                return false;
            }
        }

        private static Uri NormalizeUrl(Uri url)
        {
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcWss, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeWss };
                if (url.IsDefaultPort)
                {
                    builder.Port = Utils.UaWebSocketsDefaultPort;
                }
                return builder.Uri;
            }
            return url;
        }

        private EndPoint? m_remoteEndpoint;
    }

    /// <summary>
    /// Server-side <see cref="IUaSCByteTransport"/> built from an already-
    /// accepted <see cref="WebSocket"/> (returned by Kestrel's
    /// <c>HttpContext.WebSockets.AcceptWebSocketAsync(subProtocol)</c>).
    /// </summary>
    internal sealed class WebSocketServerByteTransport : WebSocketByteTransportBase
    {
        public WebSocketServerByteTransport(
            WebSocket socket,
            EndPoint? localEndpoint,
            EndPoint? remoteEndpoint,
            BufferManager bufferManager,
            int receiveBufferSize,
            ITelemetryContext telemetry)
            : base(bufferManager, receiveBufferSize, telemetry)
        {
            if (socket == null)
            {
                throw new ArgumentNullException(nameof(socket));
            }
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
            AttachSocket(socket);
        }

        /// <inheritdoc/>
        public override EndPoint? LocalEndpoint { get; }

        /// <inheritdoc/>
        public override EndPoint? RemoteEndpoint { get; }

        /// <inheritdoc/>
        public override ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            throw new NotSupportedException(
                "WebSocketServerByteTransport is constructed from an accepted WebSocket and cannot connect outbound.");
        }
    }
}
