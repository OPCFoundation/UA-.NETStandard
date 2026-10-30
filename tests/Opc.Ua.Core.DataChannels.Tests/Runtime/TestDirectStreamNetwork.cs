// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Connects real asynchronous loopback byte streams only after receiving-token registration.
    /// This fixture exercises the common runtime, not native QUIC or WebTransport conformance.
    /// </summary>
    internal sealed class TestDirectStreamNetwork
    {
        /// <summary>
        /// Gets complete writes for independent inspection of payload and barrier wire bytes.
        /// </summary>
        internal ConcurrentQueue<ByteString> Writes { get; } = new();

        /// <summary>
        /// Gets or sets the explicit provider result used to exercise failed receive admission.
        /// </summary>
        internal DataChannelResult ReceivingAdmissionResult { get; set; } = DataChannelResult.Good;

        /// <summary>
        /// Gets or sets whether END remains transport-owned until orderly completion.
        /// </summary>
        internal bool DeferEndUntilCompletion { get; set; }

        /// <summary>
        /// Gets the number of direct senders completed before disposal.
        /// </summary>
        internal int CompletedSenders => Volatile.Read(ref m_completedSenders);

        /// <summary>
        /// Registers a receiving token before its sending stream can connect.
        /// </summary>
        internal ValueTask<Stream> AcceptAsync(Guid token, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReceivingAdmissionResult != DataChannelResult.Good)
            {
                throw new DataChannelException(ReceivingAdmissionResult);
            }
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            if (!m_listeners.TryAdd(token, listener))
            {
                listener.Stop();
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            return new ValueTask<Stream>(AcceptCoreAsync(listener, cancellationToken));
        }

        /// <summary>
        /// Connects only to a registered, single-use token.
        /// </summary>
        internal async ValueTask<Stream> OpenAsync(Guid token, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_listeners.TryGetValue(token, out TcpListener? listener) || !m_opened.TryAdd(token, true))
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                return new RecordingStream(client, this);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Cancels the actual pending accept, rather than leaving a detached listener operation.
        /// </summary>
        private async Task<Stream> AcceptCoreAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(listener.Stop);
            try
            {
#if NET6_0_OR_GREATER
                TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
#else
                TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
#endif
                return new RecordingStream(client, this);
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>The receiving token history is never recycled.</summary>
        private readonly ConcurrentDictionary<Guid, TcpListener> m_listeners = new();

        /// <summary>The sending token history is independently single-use.</summary>
        private readonly ConcurrentDictionary<Guid, bool> m_opened = new();

        /// <summary>The number of retained orderly sending directions.</summary>
        private int m_completedSenders;

        /// <summary>
        /// Fragments reads at every header boundary and records exact complete writes.
        /// </summary>
        private sealed class RecordingStream(TcpClient client, TestDirectStreamNetwork network) :
            Stream, IDataChannelDirectStreamCompletion
        {
            /// <inheritdoc/>
            public override bool CanRead => m_stream.CanRead;

            /// <inheritdoc/>
            public override bool CanWrite => m_stream.CanWrite;

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
            public async ValueTask CompleteWritesAsync(
                ByteString finalBytes, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref m_completed) != 0)
                {
                    throw new InvalidOperationException("The send direction is already complete.");
                }
                network.Writes.Enqueue(finalBytes);
                await m_stream.WriteAsync(finalBytes.Memory, cancellationToken).ConfigureAwait(false);
                if (Interlocked.Exchange(ref m_completed, 1) == 0)
                {
                    Interlocked.Increment(ref network.m_completedSenders);
                }
            }

            /// <inheritdoc/>
            public override Task<int> ReadAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return m_stream.ReadAsync(buffer.AsMemory(offset, Math.Min(3, count)), cancellationToken).AsTask();
            }

            /// <inheritdoc/>
            public override async Task WriteAsync(
                byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ByteString payload = new(buffer.AsSpan(offset, count).ToArray());
                network.Writes.Enqueue(payload);
                if (HoldEnd(payload))
                {
                    return;
                }
                await m_stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
            /// <inheritdoc/>
            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return m_stream.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
            }

            /// <inheritdoc/>
            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                ByteString payload = new(buffer.ToArray());
                network.Writes.Enqueue(payload);
                if (HoldEnd(payload))
                {
                    return;
                }
                await m_stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
#endif
            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            /// <inheritdoc/>
            public override void Flush() => throw new NotSupportedException();

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            /// <inheritdoc/>
            public override void SetLength(long value) => throw new NotSupportedException();

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    m_stream.Dispose();
                    client.Dispose();
                }
                base.Dispose(disposing);
            }

            /// <summary>
            /// Models a completed local write that has not yet crossed the transport's retention barrier.
            /// </summary>
            private bool HoldEnd(ByteString payload)
            {
                return network.DeferEndUntilCompletion &&
                    DirectDeliveryFrameCodec.DecodeExact(payload, new()).Control is
                        FlowSignal { Operation: FlowOperation.End };
            }

            /// <summary>The real network stream whose lifetime belongs to this endpoint.</summary>
            private readonly NetworkStream m_stream = client.GetStream();

            /// <summary>The orderly-completion counter guard.</summary>
            private int m_completed;
        }
    }
}
