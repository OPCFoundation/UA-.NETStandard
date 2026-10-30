// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    public sealed partial class WebTransportDataChannelConnection
    {
        /// <summary>
        /// Delivers H3 input in stream order to the established ordinary UACP byte stream.
        /// </summary>
        private async ValueTask DeliverBaselineAsync(
            StreamState state,
            Task previous,
            Http3Event metadata,
            ByteString payload)
        {
            await previous.ConfigureAwait(false);
            await m_baselineReady.Task.WaitAsync(m_lifetime).ConfigureAwait(false);
            WebTransportByteStream stream = m_baselineStream ??
                throw new WebTransportException("The baseline byte stream was not attached.");
            await stream.DeliverAsync(payload, metadata.Kind == 16, m_lifetime).ConfigureAwait(false);
        }

        /// <summary>
        /// Submits baseline or direct bytes to H3 until the actual QUIC send lease is released.
        /// </summary>
        private async ValueTask SendBaselineAsync(
            StreamState state,
            ByteString payload,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime);
            await state.Writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                int offset = 0;
                while (offset < payload.Length)
                {
                    int length = Math.Min(65536, payload.Length - offset);
                    ByteString segment = payload.Slice(offset, length);
                    while (true)
                    {
                        Task? changed = null;
                        PendingSend? pending = null;
                        lock (m_gate)
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            if (m_closing || state.Closing || state.Retired)
                            {
                                throw new IOException("The WebTransport byte stream is closed.");
                            }
                            pending = new PendingSend();
                            state.PendingSend = pending;
                            if (!TryCommandLocked(13, state.Stream.StreamId, 0, 0, segment, out Http3Error error))
                            {
                                state.PendingSend = null;
                                pending = null;
                                if (!error.Retryable)
                                {
                                    throw new WebTransportException(error);
                                }
                                changed = m_changed.Task;
                            }
                        }
                        if (pending != null)
                        {
                            DataChannelResult result = await pending.Completion.Task.ConfigureAwait(false);
                            if (result != DataChannelResult.Good)
                            {
                                throw new IOException("The WebTransport byte stream write failed.");
                            }
                            offset += length;
                            break;
                        }
                        await changed!.WaitAsync(linked.Token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                state.Writer.Release();
            }
        }

        /// <summary>
        /// Returns receive credit only for bytes consumed by the owning baseline or direct reader.
        /// </summary>
        private async ValueTask ConsumeBaselineAsync(WebTransportByteStream stream, int count)
        {
            if (count == 0)
            {
                return;
            }
            lock (m_gate)
            {
                if (stream.IsDisposed || count > stream.QueuedBytes || count > m_queuedStreamBytes)
                {
                    throw new IOException("The WebTransport receive owner is closed or its accounting is invalid.");
                }
                stream.QueuedBytes -= count;
                m_queuedStreamBytes -= count;
            }
            await RetryCommandAsync(18, 0, (ulong)count, default, m_lifetime).ConfigureAwait(false);
        }

        /// <summary>
        /// Retains bounded input until its baseline or direct reader consumes it.
        /// </summary>
        private async ValueTask QueueBaselinePayloadAsync(
            WebTransportByteStream stream,
            ByteString payload,
            bool fin,
            CancellationToken cancellationToken)
        {
            bool discarded = false;
            if (payload.Length != 0)
            {
                lock (m_gate)
                {
                    if (stream.IsDisposed)
                    {
                        discarded = true;
                    }
                    else
                    {
                        if (payload.Length > m_options.MaxHostBufferedBytes - m_queuedStreamBytes)
                        {
                            throw new WebTransportException("The retained WebTransport input limit was reached.");
                        }
                        m_queuedStreamBytes += payload.Length;
                        stream.QueuedBytes += payload.Length;
                    }
                }
            }
            if (discarded)
            {
                await RetryCommandAsync(18, 0, (ulong)payload.Length, default, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            try
            {
                await stream.WriteInputAsync(payload, fin, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                stream.IsDisposed && exception is IOException or ChannelClosedException)
            {
                // Disposal returned the reservation and credit for this already-accounted input.
            }
            catch
            {
                if (payload.Length != 0)
                {
                    lock (m_gate)
                    {
                        if (!stream.IsDisposed)
                        {
                            m_queuedStreamBytes -= payload.Length;
                            stream.QueuedBytes -= payload.Length;
                        }
                    }
                }
                throw;
            }
        }

        /// <summary>
        /// Exposes bounded H3 payload bytes without leaking numeric stream identity.
        /// </summary>
        private sealed class WebTransportByteStream(
            WebTransportDataChannelConnection owner,
            StreamState state,
            CancellationToken lifetime,
            bool direct = false) : Stream, IDataChannelDirectStreamCompletion
        {
            /// <summary>
            /// Gets whether this byte owner has retired.
            /// </summary>
            internal bool IsDisposed => Volatile.Read(ref m_disposed) != 0;

            /// <summary>
            /// Gets or sets retained receive bytes under the owning H3 gate.
            /// </summary>
            internal int QueuedBytes { get; set; }

            /// <summary>
            /// Wakes a blocked reader when its actual transport stream terminates.
            /// </summary>
            internal void CompleteInput(DataChannelResult result)
            {
                m_input.Writer.TryComplete(new DataChannelException(result));
            }

            /// <summary>
            /// Enqueues an ordered H3 payload and completes the byte stream on FIN.
            /// </summary>
            internal async ValueTask DeliverAsync(ByteString payload, bool fin, CancellationToken cancellationToken)
            {
                await owner.QueueBaselinePayloadAsync(this, payload, fin, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Queues already-accounted input and publishes the actual stream's FIN.
            /// </summary>
            internal async ValueTask WriteInputAsync(
                ByteString payload,
                bool fin,
                CancellationToken cancellationToken)
            {
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new IOException("The WebTransport UASC byte stream is closed.");
                }
                if (payload.Length != 0)
                {
                    await m_input.Writer.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                }
                if (fin)
                {
                    m_input.Writer.TryComplete();
                }
            }

            public override bool CanRead => !IsDisposed && (!direct || !state.Stream.IsLocal);

            public override bool CanSeek => false;

            public override bool CanWrite =>
                !IsDisposed && Volatile.Read(ref m_writesCompleted) == 0 && (!direct || state.Stream.IsLocal);

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public async ValueTask CompleteWritesAsync(
                ByteString finalBytes, CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (!direct || !state.Stream.IsLocal)
                {
                    throw new NotSupportedException("Only a direct sending stream can complete its writes.");
                }
                if (Interlocked.Exchange(ref m_writing, 1) != 0)
                {
                    throw new InvalidOperationException("Only one direct send may run on the stream.");
                }
                try
                {
                    if (Volatile.Read(ref m_writesCompleted) != 0)
                    {
                        throw new InvalidOperationException("The direct stream's send direction is complete.");
                    }
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                    linked.Token.ThrowIfCancellationRequested();
                    if (finalBytes.IsEmpty)
                    {
                        throw new ArgumentException("The final direct write must not be empty.", nameof(finalBytes));
                    }
                    lock (owner.m_gate)
                    {
                        state.RetainWrites = true;
                    }
                    while (!finalBytes.IsEmpty)
                    {
                        int count = Math.Min(65536, finalBytes.Length);
                        await owner.SendBaselineAsync(
                            state, finalBytes.Slice(0, count), linked.Token)
                            .ConfigureAwait(false);
                        finalBytes = finalBytes.Slice(count);
                    }
                    Volatile.Write(ref m_writesCompleted, 1);
                }
                finally
                {
                    lock (owner.m_gate)
                    {
                        state.RetainWrites = false;
                    }
                    Volatile.Write(ref m_writing, 0);
                }
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ObjectDisposedException.ThrowIf(IsDisposed, this);
                if (direct && state.Stream.IsLocal)
                {
                    throw new NotSupportedException("This direct stream only supports sending.");
                }
                if (buffer.IsEmpty)
                {
                    return 0;
                }
                if (Interlocked.Exchange(ref m_reading, 1) != 0)
                {
                    throw new InvalidOperationException("Only one UACP receive may run on the baseline stream.");
                }
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime);
                    while (m_current.IsEmpty)
                    {
                        if (!await m_input.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(false))
                        {
                            return 0;
                        }
                        if (!m_input.Reader.TryRead(out ByteString input))
                        {
                            continue;
                        }
                        m_current = input;
                    }
                    int count = Math.Min(buffer.Length, m_current.Length);
                    m_current.Span[..count].CopyTo(buffer.Span);
                    m_current = m_current.Slice(count);
                    await owner.ConsumeBaselineAsync(this, count).ConfigureAwait(false);
                    return count;
                }
                finally
                {
                    Volatile.Write(ref m_reading, 0);
                }
            }

            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                if (direct && !state.Stream.IsLocal)
                {
                    throw new NotSupportedException("This direct stream only supports receiving.");
                }
                if (Volatile.Read(ref m_disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(WebTransportByteStream));
                }
                if (Interlocked.Exchange(ref m_writing, 1) != 0)
                {
                    throw new InvalidOperationException("Only one UACP send may run on the baseline stream.");
                }
                try
                {
                    if (Volatile.Read(ref m_disposed) != 0)
                    {
                        throw new ObjectDisposedException(nameof(WebTransportByteStream));
                    }
                    if (Volatile.Read(ref m_writesCompleted) != 0)
                    {
                        throw new InvalidOperationException("The direct stream's send direction is complete.");
                    }
                    while (!buffer.IsEmpty)
                    {
                        int count = Math.Min(65536, buffer.Length);
                        ByteString segment = new(buffer[..count].ToArray());
                        await owner.SendBaselineAsync(state, segment, cancellationToken).ConfigureAwait(false);
                        buffer = buffer[count..];
                    }
                }
                finally
                {
                    Volatile.Write(ref m_writing, 0);
                }
            }

            public override void Flush()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Flush();
                return Task.CompletedTask;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("Use asynchronous UACP writes.");

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("Use asynchronous UACP reads.");

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync().ConfigureAwait(false);
                Task? disposal;
                lock (owner.m_gate)
                {
                    disposal = m_disposal;
                }
                if (disposal != null)
                {
                    await disposal.ConfigureAwait(false);
                }
                GC.SuppressFinalize(this);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (owner.m_gate)
                    {
                        if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                        {
                            m_input.Writer.TryComplete(new IOException("The WebTransport byte stream was closed."));
                            int queued = QueuedBytes;
                            QueuedBytes = 0;
                            owner.m_queuedStreamBytes -= queued;
                            m_current = default;
                            while (m_input.Reader.TryRead(out _))
                            {
                            }
                            if (!owner.m_closing)
                            {
                                m_disposal = owner.StartJobLocked(async () =>
                                {
                                    if (queued != 0)
                                    {
                                        await owner.RetryCommandAsync(
                                            18, 0, (ulong)queued, default, lifetime).ConfigureAwait(false);
                                    }
                                    if (!ReferenceEquals(state, owner.m_baselineStreamState))
                                    {
                                        await owner.CloseFlowAsync(
                                            state.Stream.StreamId, DataChannelResult.Closed).ConfigureAwait(false);
                                    }
                                });
                            }
                        }
                    }
                }
                base.Dispose(disposing);
            }

            private readonly Channel<ByteString> m_input = Channel.CreateBounded<ByteString>(
                new BoundedChannelOptions(16)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });
            private ByteString m_current;

            /// <summary>Whether the final reliable direct write completed.</summary>
            private int m_writesCompleted;
            private Task? m_disposal;
            private int m_disposed;
            private int m_reading;
            private int m_writing;
        }
    }
}
