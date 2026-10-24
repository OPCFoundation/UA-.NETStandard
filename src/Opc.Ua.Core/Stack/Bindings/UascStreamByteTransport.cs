/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adapts an established reliable bidirectional stream to ordinary UACP chunks without an outer envelope.
    /// The existing SecureChannel remains responsible for HEL/ACK, application authentication and UASC security.
    /// </summary>
    /// <remarks>
    /// Successful construction transfers stream ownership. The caller owns received buffers and returns
    /// them to the supplied buffer manager. This adapter does not authenticate a transport, select a
    /// profile, or authorize additional streams or direct delivery.
    /// </remarks>
    public sealed class UascStreamByteTransport :
        IUaSCByteTransport, IUaSCByteTransportLimits, IDataChannelDirectBindingProvider,
        IDataChannelDirectStreamProvider, IDataChannelDirectDatagramProvider, IDisposable
    {
        /// <summary>
        /// Takes ownership of one established readable and writable stream with a finite chunk limit.
        /// </summary>
        public UascStreamByteTransport(
            Stream stream,
            BufferManager bufferManager,
            int receiveBufferSize,
            string implementation,
            EndPoint? localEndpoint = null,
            EndPoint? remoteEndpoint = null,
            IDataChannelDirectBindingProvider? directBindingProvider = null)
            : this(bufferManager, receiveBufferSize, implementation, localEndpoint, remoteEndpoint,
                directBindingProvider)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (!stream.CanRead || !stream.CanWrite)
            {
                throw new ArgumentException("A readable and writable stream is required.", nameof(stream));
            }
            m_stream = stream;
        }

        /// <summary>
        /// Defers stream establishment until the ordinary UASC client begins connecting.
        /// </summary>
        internal UascStreamByteTransport(
            Func<Uri, CancellationToken, ValueTask<Stream>> connector,
            BufferManager bufferManager,
            int receiveBufferSize,
            string implementation,
            IDataChannelDirectBindingProvider? directBindingProvider = null)
            : this(bufferManager, receiveBufferSize, implementation, null, null, directBindingProvider)
        {
            m_connector = connector ?? throw new ArgumentNullException(nameof(connector));
        }

        /// <summary>
        /// Initializes bounded framing and lifetime resources without consuming a stream.
        /// </summary>
        private UascStreamByteTransport(
            BufferManager bufferManager,
            int receiveBufferSize,
            string implementation,
            EndPoint? localEndpoint,
            EndPoint? remoteEndpoint,
            IDataChannelDirectBindingProvider? directBindingProvider)
        {
            m_bufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            ValidateReceiveBufferSize(receiveBufferSize);
            if (string.IsNullOrWhiteSpace(implementation))
            {
                throw new ArgumentException("A transport implementation name is required.", nameof(implementation));
            }
            m_closedToken = m_closed.Token;
            m_receiveBufferSize = receiveBufferSize;
            Implementation = implementation;
            LocalEndpoint = localEndpoint;
            RemoteEndpoint = remoteEndpoint;
            m_directBindingProvider = directBindingProvider;
        }

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint { get; }

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint { get; }

        /// <inheritdoc/>
        public TransportChannelFeatures Features => TransportChannelFeatures.None;

        /// <inheritdoc/>
        public string Implementation { get; }

        /// <inheritdoc/>
        public bool IsAvailable =>
            Volatile.Read(ref m_closeStarted) == 0 && m_directBindingProvider?.IsAvailable == true;

        /// <inheritdoc/>
        public string TransportProfileUri => m_directBindingProvider?.TransportProfileUri ?? string.Empty;

        /// <inheritdoc/>
        public string Capability => m_directBindingProvider?.Capability ?? string.Empty;

        /// <inheritdoc/>
        public string Authority => m_directBindingProvider?.Authority ?? string.Empty;

        /// <inheritdoc/>
        public string Path => m_directBindingProvider?.Path ?? string.Empty;

        /// <inheritdoc/>
        public bool AreDirectStreamsAvailable =>
            Volatile.Read(ref m_closeStarted) == 0 &&
            m_directBindingProvider is IDataChannelDirectStreamProvider { AreDirectStreamsAvailable: true };

        /// <inheritdoc/>
        public int MaxDirectDatagramSize => Volatile.Read(ref m_closeStarted) == 0 &&
            m_directBindingProvider is IDataChannelDirectDatagramProvider provider
                ? provider.MaxDirectDatagramSize : 0;

        /// <inheritdoc/>
        public async ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closedToken);
            linked.Token.ThrowIfCancellationRequested();
            return m_directBindingProvider is IDataChannelDirectDatagramProvider provider
                ? await provider.SendDirectDatagramAsync(write, linked.Token).ConfigureAwait(false)
                : throw new DataChannelException(DataChannelResult.Unsupported);
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default)
        {
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closedToken);
            linked.Token.ThrowIfCancellationRequested();
            ByteString datagram = m_directBindingProvider is IDataChannelDirectDatagramProvider provider
                ? await provider.ReceiveDirectDatagramAsync(linked.Token).ConfigureAwait(false)
                : throw new DataChannelException(DataChannelResult.Unsupported);
            linked.Token.ThrowIfCancellationRequested();
            return datagram;
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            return GetDirectStreamAsync(associationToken, true, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> AcceptDirectStreamAsync(
            Guid associationToken,
            CancellationToken cancellationToken = default)
        {
            return GetDirectStreamAsync(associationToken, false, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default)
        {
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closedToken);
            linked.Token.ThrowIfCancellationRequested();
            IDataChannelDirectBindingProvider provider = m_directBindingProvider ??
                throw new DataChannelException(DataChannelResult.Unsupported);
            ByteString material = await provider.ExportKeyingMaterialAsync(transcriptDigest, linked.Token)
                .ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return material;
        }

        /// <inheritdoc/>
        public async ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            using Operation operation = EnterOperation();
            if (m_connector == null)
            {
                throw new NotSupportedException("The reliable stream is already established.");
            }
            if (Interlocked.Exchange(ref m_connectStarted, 1) != 0)
            {
                throw new InvalidOperationException("A stream transport cannot connect more than once.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
            linked.Token.ThrowIfCancellationRequested();
            Stream? stream = await m_connector(url, linked.Token).ConfigureAwait(false);
            try
            {
                if (stream == null || !stream.CanRead || !stream.CanWrite)
                {
                    throw new InvalidOperationException("The connector must return a readable and writable stream.");
                }
                lock (m_lifetimeGate)
                {
                    ThrowIfClosed();
                    linked.Token.ThrowIfCancellationRequested();
                    m_stream = stream;
                    stream = null;
                }
            }
            finally
            {
                stream?.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            using Operation operation = EnterOperation();
            ValidateChunk(chunk.Span, chunk.Length);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
            await m_writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfClosed();
                linked.Token.ThrowIfCancellationRequested();
                Stream stream = GetStream();
                try
                {
                    await stream.WriteAsync(chunk, linked.Token).ConfigureAwait(false);
                }
                catch
                {
                    // A failed stream write can have committed a prefix of the chunk.
                    Close();
                    throw;
                }
            }
            finally
            {
                m_writer.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            using Operation operation = EnterOperation();
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            ValidateGatheredChunks(buffers);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
            await m_writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfClosed();
                linked.Token.ThrowIfCancellationRequested();
                Stream stream = GetStream();
                try
                {
                    foreach (ArraySegment<byte> segment in buffers)
                    {
                        await stream.WriteAsync(segment.AsMemory(), linked.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    Close();
                    throw;
                }
            }
            finally
            {
                m_writer.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            using Operation operation = EnterOperation();
            if (Interlocked.Exchange(ref m_receiving, 1) != 0)
            {
                throw new InvalidOperationException("Only one chunk receive may run on a reliable stream.");
            }
            try
            {
                if (m_receiveBroken)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTcpInternalError, "A partially consumed chunk cannot be resumed.");
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_closedToken);
                while (m_headerLength < m_header.Length)
                {
                    int count = await ReadAsync(m_header.AsMemory(m_headerLength), linked.Token).ConfigureAwait(false);
                    m_headerLength += count;
                }
                int length;
                try
                {
                    length = ValidateChunk(m_header, null, Volatile.Read(ref m_receiveBufferSize));
                }
                catch
                {
                    m_receiveBroken = true;
                    throw;
                }
                byte[] buffer = m_bufferManager.TakeBuffer(length, nameof(ReceiveChunkAsync), linked.Token);
                try
                {
                    m_header.CopyTo(buffer, 0);
                    int received = m_header.Length;
                    try
                    {
                        while (received < length)
                        {
                            received += await ReadAsync(
                                buffer.AsMemory(received, length - received), linked.Token).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        m_receiveBroken = true;
                        throw;
                    }
                    m_headerLength = 0;
                    return new ArraySegment<byte>(buffer, 0, length);
                }
                catch
                {
                    m_bufferManager.ReturnBuffer(buffer, nameof(ReceiveChunkAsync));
                    throw;
                }
            }
            finally
            {
                Volatile.Write(ref m_receiving, 0);
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closeStarted, 1) == 0)
            {
                try
                {
                    m_closed.Cancel();
                }
                finally
                {
                    try
                    {
                        Stream? stream;
                        lock (m_lifetimeGate)
                        {
                            stream = m_stream;
                            m_stream = null;
                        }
                        stream?.Dispose();
                    }
                    finally
                    {
                        lock (m_lifetimeGate)
                        {
                            m_closeCompleted = true;
                            DisposeResourcesLocked();
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
        }

        /// <inheritdoc/>
        void IUaSCByteTransportLimits.SetReceiveBufferSize(int receiveBufferSize)
        {
            ValidateReceiveBufferSize(receiveBufferSize);
            Volatile.Write(ref m_receiveBufferSize, receiveBufferSize);
        }

        /// <summary>
        /// Checks framing before renting a receive buffer or committing any outgoing byte.
        /// </summary>
        private static int ValidateChunk(ReadOnlySpan<byte> header, long? actualLength, int maximum = int.MaxValue)
        {
            if (header.Length < TcpMessageLimits.MessageTypeAndSize)
            {
                throw new ServiceResultException(StatusCodes.BadTcpMessageTooLarge, "The UACP header is incomplete.");
            }
            if (!TcpMessageType.IsValid(BinaryPrimitives.ReadUInt32LittleEndian(header)))
            {
                throw new ServiceResultException(StatusCodes.BadTcpMessageTypeInvalid, "The UACP type is invalid.");
            }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (length <= TcpMessageLimits.MessageTypeAndSize || length > maximum ||
                (actualLength.HasValue && actualLength.Value != length))
            {
                throw new ServiceResultException(StatusCodes.BadTcpMessageTooLarge, "The UACP size is invalid.");
            }
            return checked((int)length);
        }

        /// <summary>
        /// Validates all chunks before writing, including headers split between gathered segments.
        /// </summary>
        private static void ValidateGatheredChunks(BufferCollection buffers)
        {
            Span<byte> header = stackalloc byte[TcpMessageLimits.MessageTypeAndSize];
            int headerLength = 0;
            int remaining = 0;
            bool hasChunk = false;
            foreach (ArraySegment<byte> segment in buffers)
            {
                ReadOnlySpan<byte> bytes = segment.AsSpan();
                while (!bytes.IsEmpty)
                {
                    if (remaining != 0)
                    {
                        int consumed = Math.Min(remaining, bytes.Length);
                        bytes = bytes[consumed..];
                        remaining -= consumed;
                    }
                    else
                    {
                        int copied = Math.Min(header.Length - headerLength, bytes.Length);
                        bytes[..copied].CopyTo(header[headerLength..]);
                        bytes = bytes[copied..];
                        headerLength += copied;
                        if (headerLength == header.Length)
                        {
                            remaining = ValidateChunk(header, null) - header.Length;
                            headerLength = 0;
                            hasChunk = true;
                        }
                    }
                }
            }
            if (!hasChunk || headerLength != 0 || remaining != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTcpMessageTooLarge, "The gathered UACP chunks are incomplete.");
            }
        }

        /// <summary>
        /// Rejects limits that cannot hold the fixed UACP header and a body.
        /// </summary>
        private static void ValidateReceiveBufferSize(int receiveBufferSize)
        {
            if (receiveBufferSize <= TcpMessageLimits.MessageTypeAndSize)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize));
            }
        }

        /// <summary>
        /// Reads only the current header or body, leaving coalesced following chunks in the stream.
        /// </summary>
        private async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
        {
            int count = await GetStream().ReadAsync(destination, ct).ConfigureAwait(false);
            if (count == 0)
            {
                throw new ServiceResultException(StatusCodes.BadConnectionClosed, "The reliable stream ended.");
            }
            return count;
        }

        /// <summary>
        /// Refuses further work after local closure.
        /// </summary>
        private void ThrowIfClosed()
        {
            if (Volatile.Read(ref m_closeStarted) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadConnectionClosed);
            }
        }

        /// <summary>
        /// Keeps direct admission within the baseline lifetime and retires a stream returned during closure.
        /// </summary>
        private async ValueTask<Stream> GetDirectStreamAsync(
            Guid associationToken,
            bool open,
            CancellationToken cancellationToken)
        {
            using Operation operation = EnterOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_closedToken);
            linked.Token.ThrowIfCancellationRequested();
            if (m_directBindingProvider is not IDataChannelDirectStreamProvider provider)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            Stream stream = open
                ? await provider.OpenDirectStreamAsync(associationToken, linked.Token).ConfigureAwait(false)
                : await provider.AcceptDirectStreamAsync(associationToken, linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                return stream;
            }
            catch
            {
                if (stream is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    stream.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// Returns the established stream without interpreting transport security or profile state.
        /// </summary>
        private Stream GetStream()
        {
            lock (m_lifetimeGate)
            {
                ThrowIfClosed();
                return m_stream ?? throw new ServiceResultException(StatusCodes.BadNotConnected);
            }
        }

        /// <summary>
        /// Keeps I/O cancellation and serialization resources alive until every admitted operation exits.
        /// </summary>
        private Operation EnterOperation()
        {
            lock (m_lifetimeGate)
            {
                ThrowIfClosed();
                m_operations++;
                return new Operation(this);
            }
        }

        /// <summary>
        /// Releases an I/O lifetime after its writer/reader and linked cancellation have finished.
        /// </summary>
        private void ExitOperation()
        {
            lock (m_lifetimeGate)
            {
                m_operations--;
                DisposeResourcesLocked();
            }
        }

        /// <summary>
        /// Disposes resources only after close signaling and all users have completed.
        /// </summary>
        private void DisposeResourcesLocked()
        {
            if (m_closeCompleted && m_operations == 0 && !m_resourcesDisposed)
            {
                m_resourcesDisposed = true;
                m_writer.Dispose();
                m_closed.Dispose();
            }
        }

        /// <summary>The deferred client connector, absent for accepted streams.</summary>
        private readonly Func<Uri, CancellationToken, ValueTask<Stream>>? m_connector;

        /// <summary>The caller's receive-buffer owner.</summary>
        private readonly BufferManager m_bufferManager;

        /// <summary>Serializes complete outgoing chunks, including gathered segments.</summary>
        private readonly SemaphoreSlim m_writer = new(1, 1);

        /// <summary>Cancels pending I/O when this transport closes.</summary>
        private readonly CancellationTokenSource m_closed = new();

        /// <summary>The stable lifetime token, also readable after its source is disposed.</summary>
        private readonly CancellationToken m_closedToken;

        /// <summary>The optional borrowed provider for this baseline's authenticated transport association.</summary>
        private readonly IDataChannelDirectBindingProvider? m_directBindingProvider;

        /// <summary>Protects resource disposal against admitted I/O operations.</summary>
        private readonly System.Threading.Lock m_lifetimeGate = new();

        /// <summary>Retains at most one fixed header while waiting for a peer.</summary>
        private readonly byte[] m_header = new byte[TcpMessageLimits.MessageTypeAndSize];

        /// <summary>The stream owned by this adapter after successful establishment.</summary>
        private Stream? m_stream;

        /// <summary>The negotiated receive chunk limit.</summary>
        private int m_receiveBufferSize;

        /// <summary>The number of retained header bytes.</summary>
        private int m_headerLength;

        /// <summary>The single receive-operation guard.</summary>
        private int m_receiving;

        /// <summary>The idempotent close guard.</summary>
        private int m_closeStarted;

        /// <summary>The single client-connect guard.</summary>
        private int m_connectStarted;

        /// <summary>The number of admitted I/O operations.</summary>
        private int m_operations;

        /// <summary>Indicates that close signaling and stream disposal have completed.</summary>
        private bool m_closeCompleted;

        /// <summary>Indicates that operation resources have been disposed exactly once.</summary>
        private bool m_resourcesDisposed;

        /// <summary>Indicates that body bytes were consumed without completing a chunk.</summary>
        private bool m_receiveBroken;

        /// <summary>
        /// Holds one operation lifetime; only used by private, single-owner using declarations.
        /// </summary>
        private readonly struct Operation(UascStreamByteTransport owner) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                owner.ExitOperation();
            }
        }
    }
}
