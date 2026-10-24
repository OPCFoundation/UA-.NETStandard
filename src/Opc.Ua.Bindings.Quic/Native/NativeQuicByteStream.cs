// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Adapts one actual native stream to asynchronous byte I/O without adding wire framing.
    /// A bounded native segment is retained until consumed, including bytes preceding FIN or reset.
    /// </summary>
    internal sealed class NativeQuicByteStream : Stream, IDataChannelDirectStreamCompletion
    {
        /// <summary>
        /// Takes ownership of the supplied actual stream, independently of its connection's other streams.
        /// </summary>
        internal NativeQuicByteStream(NativeQuicStream stream)
        {
            m_stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <inheritdoc/>
        public override bool CanRead => Volatile.Read(ref m_disposed) == 0 &&
            (m_stream.IsBidirectional || !m_stream.IsLocal);

        /// <inheritdoc/>
        public override bool CanWrite => Volatile.Read(ref m_disposed) == 0 &&
            Volatile.Read(ref m_writesCompleted) == 0 &&
            (m_stream.IsBidirectional || m_stream.IsLocal);

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!CanRead)
            {
                throw new NotSupportedException("This native stream has no receive direction.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_stream.Closed, m_stream.Connection.Closed);
            await m_reader.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (buffer.IsEmpty)
                {
                    return 0;
                }
                while (m_pending.IsEmpty)
                {
                    if (m_terminal is { } terminal)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        if (terminal.ReadOutcome == 1 &&
                            (terminal.Status == NativeQuicStatus.Success ||
                            (terminal.Status == NativeQuicStatus.Cancelled &&
                            terminal.NativeStatus == NativeQuicStatus.Success)))
                        {
                            return 0;
                        }
                        throw new NativeQuicTransportException(terminal);
                    }
                    NativeQuicCompletion read = await m_stream.ReadAsync(linked.Token).ConfigureAwait(false);
                    m_pending = read.Payload;
                    if (read.Status == NativeQuicStatus.Cancelled && read.ReadOutcome == 0)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                    }
                    if (read.ReadOutcome != 0 ||
                        read.Status is not (NativeQuicStatus.Success or NativeQuicStatus.Timeout))
                    {
                        m_terminal = read;
                    }
                    linked.Token.ThrowIfCancellationRequested();
                }
                int length = Math.Min(buffer.Length, m_pending.Length);
                m_pending.Memory.Span[..length].CopyTo(buffer.Span);
                m_pending = m_pending.Slice(length);
                return length;
            }
            finally
            {
                m_reader.Release();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await WriteCoreAsync(buffer, false, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask CompleteWritesAsync(
            ByteString finalBytes, CancellationToken cancellationToken = default)
        {
            if (finalBytes.IsEmpty)
            {
                throw new ArgumentException("The final direct write must not be empty.", nameof(finalBytes));
            }
            await WriteCoreAsync(finalBytes.Memory, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes segments and places FIN only on the final segment of an orderly completion.
        /// </summary>
        private async ValueTask WriteCoreAsync(
            ReadOnlyMemory<byte> buffer, bool complete, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            if (!CanWrite)
            {
                throw new NotSupportedException("This native stream has no send direction.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_stream.Closed, m_stream.Connection.Closed);
            await m_writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (Volatile.Read(ref m_writesCompleted) != 0)
                {
                    throw new InvalidOperationException("The native stream's send direction is complete.");
                }
                while (!buffer.IsEmpty)
                {
                    int length = Math.Min(buffer.Length, 65536);
                    NativeQuicCompletion? sent = await m_stream.SendAsync(
                        new ByteString(buffer.Span[..length].ToArray()),
                        flags: complete && length == buffer.Length ? 1u : 0u, cancellationToken: linked.Token)
                        .ConfigureAwait(false);
                    if (sent == null || sent.Status != NativeQuicStatus.Success ||
                        sent.BytesTransferred != length)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        throw sent == null
                            ? new IOException("The native stream did not admit the byte write.")
                            : new NativeQuicTransportException(sent);
                    }
                    buffer = buffer[length..];
                }
                if (complete)
                {
                    Volatile.Write(ref m_writesCompleted, 1);
                }
            }
            finally
            {
                m_writer.Release();
            }
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        public override Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException("Use asynchronous stream I/O.");

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use asynchronous stream I/O.");

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Use asynchronous stream I/O.");

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            Task close;
            lock (m_gate)
            {
                close = m_close;
            }
            await close.ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (m_gate)
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        m_close = CloseAsync();
                    }
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Cancels and joins native I/O before releasing buffered bytes and adapter serialization gates.
        /// </summary>
        private async Task CloseAsync()
        {
            try
            {
                await m_stream.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await m_reader.WaitAsync().ConfigureAwait(false);
                await m_writer.WaitAsync().ConfigureAwait(false);
                m_pending = default;
                m_terminal = null;
                m_reader.Release();
                m_writer.Release();
                m_reader.Dispose();
                m_writer.Dispose();
            }
        }

        /// <summary>The actual native stream owner.</summary>
        private readonly NativeQuicStream m_stream;

        /// <summary>Whether the orderly final write and FIN completed.</summary>
        private int m_writesCompleted;

        /// <summary>Serializes retained receive bytes as well as native reads.</summary>
        private readonly SemaphoreSlim m_reader = new(1, 1);

        /// <summary>Prevents another write from interleaving a segmented byte write.</summary>
        private readonly SemaphoreSlim m_writer = new(1, 1);

        /// <summary>Protects asynchronous close publication without blocking on I/O.</summary>
        private readonly System.Threading.Lock m_gate = new();

        /// <summary>The one bounded native read segment not yet consumed.</summary>
        private ByteString m_pending;

        /// <summary>The exact terminal outcome deferred until preceding bytes have been read.</summary>
        private NativeQuicCompletion? m_terminal;

        /// <summary>The single published asynchronous cleanup completion.</summary>
        private Task m_close = Task.CompletedTask;

        /// <summary>The atomic byte-admission shutdown guard.</summary>
        private int m_disposed;
    }
}
