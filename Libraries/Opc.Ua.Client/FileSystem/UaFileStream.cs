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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// A <see cref="Stream"/>-derived wrapper around an OPC UA
    /// <c>FileType</c> handle. Async members hit the wire via the
    /// supplied <see cref="FileTypeClient"/> proxy; sync members
    /// forward to their async counterparts via
    /// <c>GetAwaiter().GetResult()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="UaFileStream"/> serialises concurrent calls from
    /// multiple threads on the same instance via an internal
    /// <see cref="SemaphoreSlim"/>; the resulting behaviour matches
    /// <see cref="FileStream"/>'s "not thread-safe but doesn't corrupt
    /// state" contract.
    /// </para>
    /// <para>
    /// <see cref="Length"/> is tracked locally because OPC UA does not
    /// expose a "current length" accessor on the file handle. It is
    /// initialised at construction (typically from the file's
    /// <c>Size</c> property) and bumped whenever a write extends past
    /// the current length. Callers that mutate the underlying file
    /// through other handles should call
    /// <see cref="UaFileInfo.RefreshAsync"/> on the owning info object
    /// before relying on <see cref="Length"/>.
    /// </para>
    /// <para>
    /// <see cref="Position"/> is tracked locally and pushed to the
    /// server lazily — only when the requested cursor diverges from
    /// the last successfully transmitted position.
    /// </para>
    /// <para>
    /// Sync members (<c>Read</c>, <c>Write</c>, <c>Seek</c>,
    /// <c>Flush</c>, <c>Dispose</c>) forward to the async overrides
    /// via <c>GetAwaiter().GetResult()</c>. Calling them on a
    /// single-threaded synchronization context (e.g. WPF UI thread)
    /// can deadlock — prefer the async overrides.
    /// </para>
    /// </remarks>
    public sealed class UaFileStream : Stream
#if !(NETSTANDARD2_1_OR_GREATER || NET)
        , System.IAsyncDisposable
#endif
    {
        internal UaFileStream(
            FileTypeClient proxy,
            uint handle,
            UaFileMode mode,
            long initialLength,
            long initialPosition,
            int chunkSize)
        {
            m_proxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
            Handle = handle;
            Mode = mode;
            m_length = initialLength < 0 ? 0 : initialLength;
            m_position = initialPosition < 0 ? 0 : initialPosition;
            m_serverPosition = m_position;
            m_chunkSize = chunkSize <= 0 ? DefaultChunkSize : chunkSize;
        }

        /// <inheritdoc/>
        public override bool CanRead =>
            !m_disposed && (Mode & UaFileMode.Read) != 0;

        /// <inheritdoc/>
        public override bool CanWrite =>
            !m_disposed && (Mode & UaFileMode.Write) != 0;

        /// <inheritdoc/>
        public override bool CanSeek => !m_disposed;

        /// <inheritdoc/>
        public override bool CanTimeout => false;

        /// <inheritdoc/>
        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return Volatile.Read(ref m_length);
            }
        }

        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return Volatile.Read(ref m_position);
            }
            set => Seek(value, SeekOrigin.Begin);
        }

        /// <summary>
        /// The OPC UA <c>FileType</c> handle wrapped by this stream.
        /// </summary>
        public uint Handle { get; }

        /// <summary>
        /// The mode this stream was opened with.
        /// </summary>
        public UaFileMode Mode { get; }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBuffer(buffer, offset, count);
            return ReadCoreAsync(buffer, offset, count, cancellationToken);
        }

        /// <inheritdoc/>
        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ValidateBuffer(buffer, offset, count);
            return WriteCoreAsync(buffer, offset, count, cancellationToken);
        }

#if NETSTANDARD2_1_OR_GREATER || NET
        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                ThrowIfDisposed();
                return 0;
            }
            return await ReadIntoSpanAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                ThrowIfDisposed();
                return;
            }
            await WriteFromSpanAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            // base.DisposeAsync() calls Dispose(false) internally on the
            // standard Stream implementation; calling it keeps CA2215
            // happy while keeping our (already idempotent) Dispose(bool)
            // override in the chain.
            await base.DisposeAsync().ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }
#else
        /// <summary>
        /// Asynchronously closes the underlying server file handle.
        /// Implements the recommended <c>DisposeAsync</c> pattern from
        /// the .NET docs.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }
#endif

        /// <inheritdoc/>
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            // OPC UA file handles do not expose an explicit flush; writes
            // are applied immediately on the server.
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            return ReadCoreAsync(buffer, offset, count, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBuffer(buffer, offset, count);
            WriteCoreAsync(buffer, offset, count, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Volatile.Read(ref m_position) + offset,
                SeekOrigin.End => Volatile.Read(ref m_length) + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0)
            {
                throw new IOException("Cannot seek before the start of the stream.");
            }
            Volatile.Write(ref m_position, target);
            return target;
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            // OPC UA's FileType offers no truncation primitive; callers
            // that need truncation must reopen the file with
            // EraseExisting.
            throw new NotSupportedException(
                "OPC UA FileType does not support truncation; reopen the file with UaFileMode.EraseExisting.");
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            // No-op (see FlushAsync).
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (m_disposed)
            {
                base.Dispose(disposing);
                return;
            }
            if (disposing)
            {
                // Synchronous fallback for callers that use 'using'
                // instead of 'await using'. Mirrors the recommended
                // dispose pattern; the async path (DisposeAsync) is
                // preferred to avoid the GetAwaiter().GetResult()
                // dance.
                try
                {
                    DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
                }
                catch
                {
                    // Best-effort: async path surfaces exceptions.
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Performs the asynchronous cleanup of the server-side file
        /// handle. Idempotent.
        /// </summary>
        /// <remarks>
        /// Implements the recommended async dispose core pattern from
        /// <see href="https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-disposeasync"/>.
        /// The class is <c>sealed</c>, so this helper is <c>private</c>
        /// rather than the <c>protected virtual</c> shape recommended
        /// for unsealed types.
        /// </remarks>
        private async ValueTask DisposeAsyncCore()
        {
            if (m_disposed)
            {
                return;
            }
            await m_lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                try
                {
                    await m_proxy.CloseAsync(Handle, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Server may have already torn down the handle on
                    // session close — best-effort close.
                }
            }
            finally
            {
                m_lock.Release();
                m_lock.Dispose();
            }
        }

        /// <summary>
        /// Marks this stream disposed without sending a Close to the
        /// server. Used by <see cref="UaTemporaryWriteFile"/> when the
        /// owner has already closed (or committed) the handle through a
        /// different path and wants to prevent further writes through
        /// this stream wrapper.
        /// </summary>
        internal void MarkDisposedWithoutClosing()
        {
            if (m_disposed)
            {
                return;
            }
            m_lock.Wait();
            try
            {
                m_disposed = true;
            }
            finally
            {
                m_lock.Release();
            }
        }

        private async Task<int> ReadCoreAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            if (!CanRead)
            {
                throw new NotSupportedException("Stream not opened for reading.");
            }
            if (count == 0)
            {
                return 0;
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await SyncServerPositionAsync(ct).ConfigureAwait(false);

                int total = 0;
                while (total < count)
                {
                    int chunkLen = Math.Min(m_chunkSize, count - total);
                    ByteString data = await m_proxy.ReadAsync(
                        Handle,
                        chunkLen,
                        ct).ConfigureAwait(false);

                    byte[] payload = data.ToArray() ?? [];
                    if (payload.Length == 0)
                    {
                        break;
                    }

                    Buffer.BlockCopy(payload, 0, buffer, offset + total, payload.Length);
                    total += payload.Length;
                    m_position += payload.Length;
                    m_serverPosition = m_position;
                    if (m_position > m_length)
                    {
                        m_length = m_position;
                    }
                    if (payload.Length < chunkLen)
                    {
                        break;
                    }
                }
                return total;
            }
            finally
            {
                m_lock.Release();
            }
        }

        private async Task WriteCoreAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct)
        {
            ThrowIfDisposed();
            if (!CanWrite)
            {
                throw new NotSupportedException("Stream not opened for writing.");
            }
            if (count == 0)
            {
                return;
            }

            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await SyncServerPositionAsync(ct).ConfigureAwait(false);

                int written = 0;
                while (written < count)
                {
                    int chunkLen = Math.Min(m_chunkSize, count - written);
                    var slice = new byte[chunkLen];
                    Buffer.BlockCopy(buffer, offset + written, slice, 0, chunkLen);
                    await m_proxy.WriteAsync(
                        Handle,
                        slice.ToByteString(),
                        ct).ConfigureAwait(false);
                    written += chunkLen;
                    m_position += chunkLen;
                    m_serverPosition = m_position;
                    if (m_position > m_length)
                    {
                        m_length = m_position;
                    }
                }
            }
            finally
            {
                m_lock.Release();
            }
        }

#if NETSTANDARD2_1_OR_GREATER || NET
        private async ValueTask<int> ReadIntoSpanAsync(
            Memory<byte> buffer,
            CancellationToken ct)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int read = await ReadCoreAsync(rented, 0, buffer.Length, ct)
                    .ConfigureAwait(false);
                if (read > 0)
                {
                    rented.AsSpan(0, read).CopyTo(buffer.Span);
                }
                return read;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private async ValueTask WriteFromSpanAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken ct)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.Span.CopyTo(rented);
                await WriteCoreAsync(rented, 0, buffer.Length, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
#endif

        private async Task SyncServerPositionAsync(CancellationToken ct)
        {
            if (m_position == m_serverPosition)
            {
                return;
            }
            await m_proxy.SetPositionAsync(
                Handle,
                (ulong)m_position,
                ct).ConfigureAwait(false);
            m_serverPosition = m_position;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(UaFileStream));
            }
        }

        private static void ValidateBuffer(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (buffer.Length - offset < count)
            {
                throw new ArgumentException(
                    "Offset + count exceeds buffer length.",
                    nameof(count));
            }
        }

        private const int DefaultChunkSize = 64 * 1024;

        private readonly FileTypeClient m_proxy;
        private readonly int m_chunkSize;
        private readonly SemaphoreSlim m_lock = new(1, 1);

        private long m_length;
        private long m_position;
        private long m_serverPosition;
        private bool m_disposed;
    }
}
