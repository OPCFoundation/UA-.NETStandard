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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Wraps a server-allocated temporary write file (returned by
    /// <see cref="TemporaryFileTransferClient.GenerateFileForWriteAsync"/>).
    /// Owns the close lifecycle: exactly one terminal call —
    /// <see cref="CommitAsync"/> (CloseAndCommit) or
    /// <see cref="DisposeAsync"/> (Close, server rollback) — is sent
    /// to the server.
    /// </summary>
    public sealed class UaTemporaryWriteFile : IAsyncDisposable, IDisposable
    {
        internal UaTemporaryWriteFile(
            TemporaryFileTransferTypeClient transferProxy,
            FileTypeClient fileProxy,
            NodeId fileNodeId,
            uint handle,
            int chunkSize)
        {
            m_transferProxy = transferProxy ?? throw new ArgumentNullException(nameof(transferProxy));
            m_fileProxy = fileProxy ?? throw new ArgumentNullException(nameof(fileProxy));
            FileNodeId = fileNodeId;
            m_handle = handle;
            m_inner = new UaFileStream(
                fileProxy,
                handle,
                UaFileMode.Write,
                initialLength: 0,
                initialPosition: 0,
                chunkSize);
            Stream = new NonClosingStreamWrapper(m_inner);
        }

        /// <summary>
        /// The NodeId of the temporary file allocated by the server.
        /// </summary>
        public NodeId FileNodeId { get; }

        /// <summary>
        /// A write-only <see cref="System.IO.Stream"/> wrapper around
        /// the underlying server handle. The wrapper's
        /// <see cref="System.IO.Stream.Dispose()"/> is suppressed —
        /// disposal is owned by <see cref="UaTemporaryWriteFile"/> and
        /// must go through <see cref="CommitAsync"/> or
        /// <see cref="DisposeAsync"/>.
        /// </summary>
        public Stream Stream { get; }

        /// <summary>
        /// Closes the temporary file and commits it on the server via
        /// the parent <c>TemporaryFileTransferType.CloseAndCommit</c>
        /// method. Returns the NodeId of the completion state machine
        /// the server uses to report progress (may be
        /// <see cref="NodeId.Null"/> when the server elects not to
        /// expose one).
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask<NodeId> CommitAsync(CancellationToken ct = default)
        {
            if (m_terminated)
            {
                return m_completionStateMachine;
            }
            await m_lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_terminated)
                {
                    return m_completionStateMachine;
                }
                m_terminated = true;
                m_completionStateMachine = await m_transferProxy
                    .CloseAndCommitAsync(m_handle, ct)
                    .ConfigureAwait(false);
                m_inner.MarkDisposedWithoutClosing();
                return m_completionStateMachine;
            }
            finally
            {
                m_lock.Release();
                m_lock.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Asynchronous core of the dispose pattern; sends a best-effort
        /// <c>Close</c> for the underlying server handle when the
        /// wrapper has not already been committed. Idempotent.
        /// </summary>
        /// <remarks>
        /// Implements the recommended pattern from
        /// <see href="https://learn.microsoft.com/dotnet/standard/garbage-collection/implementing-disposeasync"/>.
        /// The class is <c>sealed</c>, so this helper is <c>private</c>
        /// rather than the <c>protected virtual</c> shape recommended
        /// for unsealed types.
        /// </remarks>
        private async ValueTask DisposeAsyncCore()
        {
            if (m_terminated)
            {
                return;
            }
            await m_lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (m_terminated)
                {
                    return;
                }
                m_terminated = true;
                try
                {
                    await m_fileProxy.CloseAsync(m_handle, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException)
                {
                    // Best-effort: the server may roll back internally
                    // or the session may already be torn down.
                }
                m_inner.MarkDisposedWithoutClosing();
            }
            finally
            {
                m_lock.Release();
                m_lock.Dispose();
            }
        }

        /// <summary>
        /// Synchronous fallback for callers that use <c>using</c>
        /// instead of <c>await using</c>. Mirrors the
        /// <c>System.IDisposable</c> dispose pattern; prefer the async
        /// path to avoid a <c>GetAwaiter().GetResult()</c> hop.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (m_terminated || !disposing)
            {
                return;
            }
            try
            {
                DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort: async path surfaces exceptions.
            }
        }

        private readonly TemporaryFileTransferTypeClient m_transferProxy;
        private readonly FileTypeClient m_fileProxy;
        // CA2213: m_inner is intentionally NOT disposed via the standard
        // IDisposable pattern; ownership of the underlying server handle
        // belongs to UaTemporaryWriteFile.CommitAsync / DisposeAsync.
        // CA1816: see DisposeAsync below.
#pragma warning disable CA2213
        private readonly UaFileStream m_inner;
#pragma warning restore CA2213
        private readonly uint m_handle;
        private readonly SemaphoreSlim m_lock = new(1, 1);
        private NodeId m_completionStateMachine = NodeId.Null;
        private bool m_terminated;

        private sealed class NonClosingStreamWrapper : Stream
        {
            public NonClosingStreamWrapper(UaFileStream inner)
            {
                m_inner = inner;
            }

            public override bool CanRead => m_inner.CanRead;
            public override bool CanWrite => m_inner.CanWrite;
            public override bool CanSeek => m_inner.CanSeek;
            public override long Length => m_inner.Length;

            public override long Position
            {
                get => m_inner.Position;
                set => m_inner.Position = value;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return m_inner.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken ct)
            {
                return m_inner.ReadAsync(buffer, offset, count, ct);
            }

#if NETSTANDARD2_1_OR_GREATER || NET
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return m_inner.ReadAsync(buffer, cancellationToken);
            }
#endif

            public override void Write(byte[] buffer, int offset, int count)
            {
                m_inner.Write(buffer, offset, count);
            }

            public override Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken ct)
            {
                return m_inner.WriteAsync(buffer, offset, count, ct);
            }

#if NETSTANDARD2_1_OR_GREATER || NET
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return m_inner.WriteAsync(buffer, cancellationToken);
            }
#endif

            public override long Seek(long offset, SeekOrigin origin)
            {
                return m_inner.Seek(offset, origin);
            }

            public override void SetLength(long value)
            {
                m_inner.SetLength(value);
            }

            public override void Flush()
            {
                m_inner.Flush();
            }

            public override Task FlushAsync(CancellationToken ct)
            {
                return m_inner.FlushAsync(ct);
            }

            /// <summary>
            /// CA2215: Disposal of the inner stream (and the server file
            /// handle it owns) is intentionally suppressed here —
            /// UaTemporaryWriteFile.CommitAsync / DisposeAsync owns the
            /// close lifecycle. We still call base.Dispose(disposing) to
            /// satisfy the analyzer's contract.
            /// </summary>
            /// <param name="disposing"></param>
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
            }

            private readonly UaFileStream m_inner;
        }
    }
}
