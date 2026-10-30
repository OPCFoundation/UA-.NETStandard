// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Presents the client-initiated bidirectional WebTransport stream zero as a bounded byte stream.
    /// </summary>
    internal sealed class WebTransportCapsuleByteStream : Stream
    {
        private WebTransportCapsuleByteStream(
            Http3CapsuleSession session,
            int maximumCapsulePayloadBytes,
            int initialReceiveWindowBytes)
        {
            m_session = session;
            m_maximumCapsulePayloadBytes = maximumCapsulePayloadBytes;
            m_initialReceiveWindowBytes = (ulong)initialReceiveWindowBytes;
            m_incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });
        }

        /// <summary>
        /// Starts stream zero and advertises the receive window before accepting UACP bytes.
        /// </summary>
        internal static async ValueTask<WebTransportCapsuleByteStream> CreateAsync(
            Http3CapsuleSession session,
            int maximumCapsulePayloadBytes,
            int initialReceiveWindowBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapsulePayloadBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialReceiveWindowBytes);
            if (initialReceiveWindowBytes > MaximumReceiveWindowBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(initialReceiveWindowBytes));
            }
            var stream = new WebTransportCapsuleByteStream(
                session, maximumCapsulePayloadBytes, initialReceiveWindowBytes);
            try
            {
                await stream.WriteCreditCapsulesAsync(stream.m_initialReceiveWindowBytes, cancellationToken)
                    .ConfigureAwait(false);
                stream.m_advertisedReceiveMaximum = stream.m_initialReceiveWindowBytes;
                stream.m_receiveTask = stream.ReceiveCapsulesAsync(stream.m_lifetime.Token);
                return stream;
            }
            catch
            {
                stream.m_readGate.Dispose();
                stream.m_writeGate.Dispose();
                stream.m_lifetime.Dispose();
                throw;
            }
        }

        public override bool CanRead => Volatile.Read(ref m_disposed) == 0;

        public override bool CanSeek => false;

        public override bool CanWrite => Volatile.Read(ref m_disposed) == 0;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>
        /// Gets the number of payload arrays retained by the receive queue and current read.
        /// </summary>
        internal int RetainedPayloadCount => m_incoming.Reader.Count + (m_currentPayload.Length == 0 ? 0 : 1);

        public override void Flush()
        {
            throw new NotSupportedException("WebTransport capsule writes are asynchronous.");
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("WebTransport capsule reads are asynchronous.");
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ReadCoreAsync(buffer, cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("WebTransport capsule writes are asynchronous.");
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return WriteCoreAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref m_disposed, 1);
            if (Interlocked.Exchange(ref m_asyncDisposeStarted, 1) != 0)
            {
                return;
            }

            m_lifetime.Cancel();
            try
            {
                await m_writeGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await WriteStreamCapsuleAsync(
                        StreamFinCapsuleType, new byte[] { 0 }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    // Closing the owned HTTP/3 session can race with the peer's session shutdown.
                }
                catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
                {
                    // UASC shutdown can cancel the underlying QUIC stream before its final capsule.
                }
                finally
                {
                    m_writeGate.Release();
                }
            }
            finally
            {
                m_incoming.Writer.TryComplete();
                if (m_receiveTask != null)
                {
                    try
                    {
                        await m_receiveTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
                    {
                    }
                }
                await m_readGate.WaitAsync().ConfigureAwait(false);
                await m_writeGate.WaitAsync().ConfigureAwait(false);
                while (m_incoming.Reader.TryRead(out _))
                {
                }
                m_currentPayload = [];
                m_currentOffset = 0;
                m_readGate.Dispose();
                m_writeGate.Dispose();
                m_lifetime.Dispose();
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Volatile.Read(ref m_asyncDisposeStarted) == 0)
            {
                Interlocked.Exchange(ref m_disposed, 1);
                m_lifetime.Cancel();
                m_incoming.Writer.TryComplete();
            }
            base.Dispose(disposing);
        }

        private async ValueTask<int> ReadCoreAsync(Memory<byte> destination, CancellationToken cancellationToken)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token);
            await m_readGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                while (m_currentOffset == m_currentPayload.Length)
                {
                    if (m_remoteFin)
                    {
                        return 0;
                    }
                    m_currentPayload = await m_incoming.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
                    m_currentOffset = 0;
                    if (m_currentPayload.Length == 0)
                    {
                        m_remoteFin = true;
                        return 0;
                    }
                }

                int count = Math.Min(destination.Length, m_currentPayload.Length - m_currentOffset);
                m_currentPayload.AsMemory(m_currentOffset, count).CopyTo(destination);
                m_currentOffset += count;
                await ReplenishReceiveCreditAsync((ulong)count, linked.Token).ConfigureAwait(false);
                return count;
            }
            finally
            {
                m_readGate.Release();
            }
        }

        private async ValueTask WriteCoreAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_lifetime.Token);
            await m_writeGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref m_disposed) != 0, this);
                int maximumDataPerCapsule = m_maximumCapsulePayloadBytes - StreamIdPrefixLength;
                if (maximumDataPerCapsule <= 0 && !source.IsEmpty)
                {
                    throw new InvalidOperationException("The capsule payload limit cannot carry a stream identifier.");
                }

                for (int offset = 0; offset < source.Length;)
                {
                    int count = Math.Min(maximumDataPerCapsule, source.Length - offset);
                    await ReserveSendCreditAsync((ulong)count, linked.Token).ConfigureAwait(false);
                    byte[] capsulePayload = new byte[StreamIdPrefixLength + count];
                    WriteStreamId(capsulePayload);
                    source.Span.Slice(offset, count).CopyTo(capsulePayload.AsSpan(StreamIdPrefixLength));
                    await WriteStreamCapsuleAsync(
                        StreamDataCapsuleType, capsulePayload, linked.Token).ConfigureAwait(false);
                    offset += count;
                }
            }
            finally
            {
                m_writeGate.Release();
            }
        }

        private async Task ReceiveCapsulesAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    WebTransportCapsule? capsule = await m_session.ReadCapsuleAsync(cancellationToken)
                        .ConfigureAwait(false) ?? throw new EndOfStreamException("The WebTransport CONNECT stream ended without stream FIN.");

                    ReadOnlySequence<byte> payload = capsule.Value.Payload;
                    SequenceReader<byte> reader = new(payload);
                    if (capsule.Value.Type is StreamDataCapsuleType or StreamFinCapsuleType)
                    {
                        if (!Http3VarInt.TryRead(ref reader, out ulong streamId) || streamId != StreamId)
                        {
                            throw new InvalidDataException("The capsule does not identify WebTransport stream zero.");
                        }
                        ulong dataLength = (ulong)reader.Remaining;
                        lock (m_receiveGate)
                        {
                            if (m_receivedBytes > m_advertisedReceiveMaximum ||
                                dataLength > m_advertisedReceiveMaximum - m_receivedBytes)
                            {
                                throw new InvalidDataException(
                                    "The peer exceeded the WebTransport stream receive credit.");
                            }
                            m_receivedBytes += dataLength;
                        }
                        if (capsule.Value.Type == StreamFinCapsuleType && dataLength != 0)
                        {
                            throw new InvalidDataException("The WebTransport stream FIN capsule has trailing data.");
                        }

                        if (dataLength > 0)
                        {
                            byte[] data = payload.Slice(reader.Position).ToArray();
                            await m_incoming.Writer.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                        }
                        if (capsule.Value.Type == StreamFinCapsuleType)
                        {
                            await m_incoming.Writer.WriteAsync([], cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                    else if (capsule.Value.Type == MaxDataCapsuleType)
                    {
                        ulong value = ReadSingleVarInt(payload);
                        lock (m_creditGate)
                        {
                            if (value < m_peerMaximumData)
                            {
                                throw new InvalidDataException("The peer reduced its WebTransport session credit.");
                            }
                            m_peerMaximumData = value;
                            PulseCreditWaiters();
                        }
                    }
                    else if (capsule.Value.Type == MaxStreamDataCapsuleType)
                    {
                        if (!Http3VarInt.TryRead(ref reader, out ulong streamId) || streamId != StreamId ||
                            !Http3VarInt.TryRead(ref reader, out ulong value) || reader.Remaining != 0)
                        {
                            throw new InvalidDataException("The peer sent an invalid stream-credit capsule.");
                        }
                        lock (m_creditGate)
                        {
                            if (value < m_peerMaximumStreamData)
                            {
                                throw new InvalidDataException("The peer reduced its WebTransport stream credit.");
                            }
                            m_peerMaximumStreamData = value;
                            PulseCreditWaiters();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_incoming.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                m_incoming.Writer.TryComplete(exception);
                lock (m_creditGate)
                {
                    m_creditFailure = exception;
                    PulseCreditWaiters();
                }
            }
        }

        private async ValueTask ReplenishReceiveCreditAsync(ulong bytes, CancellationToken cancellationToken)
        {
            ulong maximum;
            lock (m_receiveGate)
            {
                m_consumedBytes = checked(m_consumedBytes + bytes);
                maximum = checked(m_initialReceiveWindowBytes + m_consumedBytes);
            }

            await WriteCreditCapsulesAsync(maximum, cancellationToken).ConfigureAwait(false);
            lock (m_receiveGate)
            {
                m_advertisedReceiveMaximum = maximum;
            }
        }

        private async ValueTask WriteCreditCapsulesAsync(ulong maximum, CancellationToken cancellationToken)
        {
            byte[] maxData = EncodeVarInt(maximum);
            await WriteStreamCapsuleAsync(MaxDataCapsuleType, maxData, cancellationToken).ConfigureAwait(false);

            byte[] maxStreamData = new byte[8 + maxData.Length];
            if (!Http3VarInt.TryWrite(maxStreamData, StreamId, out int streamIdLength))
            {
                throw new InvalidOperationException("The WebTransport stream identifier could not be encoded.");
            }
            maxData.CopyTo(maxStreamData.AsSpan(streamIdLength));
            Array.Resize(ref maxStreamData, streamIdLength + maxData.Length);
            await WriteStreamCapsuleAsync(
                MaxStreamDataCapsuleType, maxStreamData, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ReserveSendCreditAsync(ulong bytes, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task waitTask;
                lock (m_creditGate)
                {
                    if (m_creditFailure != null)
                    {
                        throw new IOException("The WebTransport session stopped while waiting for stream credit.",
                            m_creditFailure);
                    }
                    if (bytes <= m_peerMaximumData - m_sentBytes &&
                        bytes <= m_peerMaximumStreamData - m_sentStreamBytes)
                    {
                        m_sentBytes = checked(m_sentBytes + bytes);
                        m_sentStreamBytes = checked(m_sentStreamBytes + bytes);
                        return;
                    }
                    waitTask = m_creditChanged.Task;
                }
                await waitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private void PulseCreditWaiters()
        {
            TaskCompletionSource<bool> previous = m_creditChanged;
            m_creditChanged = NewSignal();
            previous.TrySetResult(true);
        }

        private async ValueTask WriteStreamCapsuleAsync(
            ulong type,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            await m_session.WriteCapsuleAsync(type, payload, cancellationToken).ConfigureAwait(false);
        }

        private static ulong ReadSingleVarInt(ReadOnlySequence<byte> payload)
        {
            SequenceReader<byte> reader = new(payload);
            if (!Http3VarInt.TryRead(ref reader, out ulong value) || reader.Remaining != 0)
            {
                throw new InvalidDataException("The WebTransport credit capsule is malformed.");
            }
            return value;
        }

        private static byte[] EncodeVarInt(ulong value)
        {
            byte[] encoded = new byte[8];
            if (!Http3VarInt.TryWrite(encoded, value, out int bytesWritten))
            {
                throw new InvalidDataException("The WebTransport credit exceeds the QUIC variable integer range.");
            }
            Array.Resize(ref encoded, bytesWritten);
            return encoded;
        }

        private static void WriteStreamId(Span<byte> destination)
        {
            if (!Http3VarInt.TryWrite(destination, StreamId, out int bytesWritten) ||
                bytesWritten != StreamIdPrefixLength)
            {
                throw new InvalidOperationException("The initial WebTransport stream ID encoding changed.");
            }
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private const ulong StreamId = 0;
        private const int StreamIdPrefixLength = 1;
        private const ulong StreamDataCapsuleType = 0x190B4D3C;
        private const ulong StreamFinCapsuleType = 0x190B4D3B;
        private const ulong MaxDataCapsuleType = 0x190B4D3D;
        private const ulong MaxStreamDataCapsuleType = 0x190B4D3E;
        private const int MaximumReceiveWindowBytes = 16 * 1024 * 1024;

        private readonly Http3CapsuleSession m_session;
        private readonly int m_maximumCapsulePayloadBytes;
        private readonly ulong m_initialReceiveWindowBytes;
        private readonly Channel<byte[]> m_incoming;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly SemaphoreSlim m_readGate = new(1, 1);
        private readonly SemaphoreSlim m_writeGate = new(1, 1);
        private readonly Lock m_creditGate = new();
        private readonly Lock m_receiveGate = new();
        private TaskCompletionSource<bool> m_creditChanged = NewSignal();
        private Task? m_receiveTask;
        private Exception? m_creditFailure;
        private byte[] m_currentPayload = [];
        private int m_currentOffset;
        private ulong m_peerMaximumData;
        private ulong m_peerMaximumStreamData;
        private ulong m_sentBytes;
        private ulong m_sentStreamBytes;
        private ulong m_receivedBytes;
        private ulong m_consumedBytes;
        private ulong m_advertisedReceiveMaximum;
        private bool m_remoteFin;
        private int m_disposed;
        private int m_asyncDisposeStarted;
    }
}
