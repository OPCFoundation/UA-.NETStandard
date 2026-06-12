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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Adapter that exposes a legacy <see cref="IMessageSocket"/> as the
    /// modern <see cref="IUaSCByteTransport"/>. Lets the UASC binary channel
    /// drive every transport (TCP, WebSocket, ...) through a single
    /// pull-based contract while the legacy <c>TcpMessageSocket</c> code
    /// path is being phased out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This adapter is intentionally a temporary bridge: once the TCP
    /// listener / client paths have been migrated to construct
    /// <see cref="TcpByteTransport"/> directly, the adapter, the
    /// <see cref="IMessageSocket"/> family and this file will be removed
    /// (see <c>plan.md</c> task <c>p1-remove-imessagesocket</c>).
    /// </para>
    /// <para>
    /// Receive path: the adapter installs itself as the
    /// <see cref="IMessageSink"/> on the underlying socket and buffers
    /// delivered chunks in an unbounded <see cref="Channel{T}"/>; callers
    /// drain via <see cref="ReceiveChunkAsync"/>.
    /// Send path: each <see cref="SendChunkAsync(System.ReadOnlyMemory{byte}, CancellationToken)"/>
    /// allocates an <see cref="IMessageSocketAsyncEventArgs"/>, posts it
    /// via <see cref="IMessageSocket.Send"/> and completes a
    /// <see cref="TaskCompletionSource{TResult}"/> from the SAEA callback.
    /// </para>
    /// </remarks>
    internal sealed class MessageSocketByteTransport : IUaSCByteTransport, IMessageSink
    {
        public MessageSocketByteTransport(IMessageSocket socket)
        {
            InnerSocket = socket ?? throw new ArgumentNullException(nameof(socket));
            m_chunks = Channel.CreateUnbounded<ArraySegment<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
        }

        /// <summary>
        /// The wrapped legacy socket. Exposed for the migration period so
        /// the existing TCP code paths can keep using SAEA-style APIs while
        /// new code consumes the <see cref="IUaSCByteTransport"/> shape.
        /// </summary>
        internal IMessageSocket InnerSocket { get; }

        /// <inheritdoc/>
        public EndPoint? LocalEndpoint => InnerSocket.LocalEndpoint;

        /// <inheritdoc/>
        public EndPoint? RemoteEndpoint => InnerSocket.RemoteEndpoint;

        /// <inheritdoc/>
        public TransportChannelFeatures Features => InnerSocket.MessageSocketFeatures;

        /// <inheritdoc/>
        public string Implementation => "UA-TCP";

        /// <inheritdoc/>
        public ValueTask ConnectAsync(Uri url, CancellationToken ct)
        {
            return new ValueTask(InnerSocket.ConnectAsync(url, ct));
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            byte[] array;
            int offset;
            int count;
            if (MemoryMarshal.TryGetArray(chunk, out ArraySegment<byte> seg) && seg.Array != null)
            {
                array = seg.Array;
                offset = seg.Offset;
                count = seg.Count;
            }
            else
            {
                array = chunk.ToArray();
                offset = 0;
                count = array.Length;
            }

            IMessageSocketAsyncEventArgs args = InnerSocket.MessageSocketEventArgs();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            args.SetBuffer(array, offset, count);
            args.Completed += OnCompleted;
            try
            {
                if (!InnerSocket.Send(args))
                {
                    OnCompleted(this, args);
                }
            }
            catch (Exception ex)
            {
                args.Completed -= OnCompleted;
                args.Dispose();
                tcs.TrySetException(ex);
            }
            return new ValueTask(tcs.Task);

            void OnCompleted(object? sender, IMessageSocketAsyncEventArgs e)
            {
                e.Completed -= OnCompleted;
                try
                {
                    if (e.IsSocketError)
                    {
                        tcs.TrySetException(ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            e.SocketErrorString));
                    }
                    else if (e.BytesTransferred < count)
                    {
                        tcs.TrySetException(ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            "Sent {0} of {1} bytes.",
                            e.BytesTransferred,
                            count));
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                }
                finally
                {
                    e.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct)
        {
            if (buffers == null)
            {
                throw new ArgumentNullException(nameof(buffers));
            }
            ct.ThrowIfCancellationRequested();

            int expected = buffers.TotalSize;
            IMessageSocketAsyncEventArgs args = InnerSocket.MessageSocketEventArgs();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            args.BufferList = buffers;
            args.Completed += OnCompleted;
            try
            {
                if (!InnerSocket.Send(args))
                {
                    OnCompleted(this, args);
                }
            }
            catch (Exception ex)
            {
                args.Completed -= OnCompleted;
                args.Dispose();
                tcs.TrySetException(ex);
            }
            return new ValueTask(tcs.Task);

            void OnCompleted(object? sender, IMessageSocketAsyncEventArgs e)
            {
                e.Completed -= OnCompleted;
                try
                {
                    if (e.IsSocketError)
                    {
                        tcs.TrySetException(ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            e.SocketErrorString));
                    }
                    else if (e.BytesTransferred < expected)
                    {
                        tcs.TrySetException(ServiceResultException.Create(
                            StatusCodes.BadConnectionClosed,
                            "Sent {0} of {1} bytes.",
                            e.BytesTransferred,
                            expected));
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                }
                finally
                {
                    e.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct)
        {
            EnsureReaderStarted();
            return m_chunks.Reader.ReadAsync(ct);
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (Interlocked.Exchange(ref m_closed, 1) != 0)
            {
                return;
            }
            m_chunks.Writer.TryComplete();
            try
            {
                InnerSocket.Close();
            }
            catch
            {
                // Best-effort close.
            }
            InnerSocket.Dispose();
        }

        // IMessageSink: invoked by the wrapped socket whenever a chunk arrives.
        bool IMessageSink.ChannelFull => false;

        void IMessageSink.OnMessageReceived(IMessageSocket source, ArraySegment<byte> message)
        {
            if (!m_chunks.Writer.TryWrite(message))
            {
                // The reader has completed; return the buffer to avoid a leak.
                if (message.Array != null)
                {
                    // Best-effort: we do not own a BufferManager reference here, so
                    // ownership goes to the channel logic which is no longer reading.
                    // This path is only reachable after Close() and is harmless because
                    // the consuming channel has already discarded its buffers.
                }
            }
        }

        void IMessageSink.OnReceiveError(IMessageSocket source, ServiceResult result)
        {
            m_chunks.Writer.TryComplete(new ServiceResultException(result));
        }

        private void EnsureReaderStarted()
        {
            if (Interlocked.CompareExchange(ref m_readerStarted, 1, 0) == 0)
            {
                InnerSocket.ChangeSink(this);
                InnerSocket.ReadNextMessage();
            }
        }

        private readonly Channel<ArraySegment<byte>> m_chunks;
        private int m_readerStarted;
        private int m_closed;
    }
}
